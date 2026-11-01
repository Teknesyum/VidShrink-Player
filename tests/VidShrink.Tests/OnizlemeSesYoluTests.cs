using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using VidShrink.App.Editing;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Ffmpeg;
using VidShrink.Player;
using Xunit;
using Xunit.Abstractions;

namespace VidShrink.Tests;

/// <summary>
/// Önizlemenin ayrı ses motoru yolu: A2 kısması (<see cref="PreviewDuck"/>), A3 yuvaları (<see cref="SoundClipsPreview"/>), geri
/// oynatmada müzik ve gizli V1. Model ve düzey hesabı saf, motor bağlantısı sahte motorla, libmpv yalnız iki kısa canlı ölçümde.
/// </summary>
public sealed class OnizlemeSesYoluTests
{
    private readonly ITestOutputHelper _cikti;

    public OnizlemeSesYoluTests(ITestOutputHelper cikti) => _cikti = cikti;

    private static long S(double saniye) => EditTime.FromSeconds(saniye);

    private static EditMusic Kisan(double duzey = -12) => new("muzik.mp3", S(30), 0, duzey) { Duck = true, DuckDb = 12, DuckReleaseMs = 400 };

    [Fact]
    public void KismaEsigiTeslimdekiYanZincirOranininDesibelidir()
    {
        Assert.Equal(20 * Math.Log10(0.01 / 4), PreviewDuck.KeyThresholdDb, 9);
        Assert.Equal(-52.04, PreviewDuck.KeyThresholdDb, 2);
        Assert.Equal(20, PreviewDuck.AttackMs);
        Assert.True(PreviewDuck.Keyed(-50));
        Assert.False(PreviewDuck.Keyed(-53));
        Assert.False(PreviewDuck.Keyed(double.NaN));
        Assert.False(PreviewDuck.Keyed(double.NegativeInfinity));
    }

    [Fact]
    public void KismaSaldiridaHizliBirakmadaYavasYaklasirVeMiktardaDurur()
    {
        var kisma = new PreviewDuck();
        var muzik = Kisan();

        Assert.Equal(-12 * (1 - Math.Exp(-1)), kisma.Step(muzik, -20, 0.020), 6);
        Assert.Equal(-12, kisma.Step(muzik, -20, 5), 6);
        Assert.Equal(-12, kisma.Step(muzik, -20, 5), 6);

        Assert.Equal(-12 * Math.Exp(-1), kisma.Step(muzik, double.NaN, 0.4), 6);
        var once = kisma.GainDb;
        Assert.Equal(once, kisma.Step(muzik, -70, -1), 9);
        Assert.Equal(once * Math.Exp(-0.05 / 0.4), kisma.Step(muzik, -70, 0.05), 6);

        kisma.Reset();
        Assert.Equal(0, kisma.GainDb);
    }

    [Fact]
    public void KismaKapaliysaKazancSifiraDoner()
    {
        var kisma = new PreviewDuck();
        kisma.Step(Kisan(), -10, 1);
        Assert.True(kisma.GainDb < -11);

        Assert.Equal(0, kisma.Step(new EditMusic("muzik.mp3", S(30)), -10, 1));
        kisma.Step(Kisan(), -10, 1);
        Assert.Equal(0, kisma.Step(null, -10, 1));
    }

    [Fact]
    public async Task MuzikBaslamadanBirSaniyeIcindeDuraklatilmisAcilirBaslayincaYerindeCalar()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(10), S(2), -6);

        muzik.Sync(parca, S(0.5), true, 1, 100, false);
        Assert.Empty(motorlar);

        muzik.Sync(parca, S(1.2), true, 1, 100, false, backward: true);
        Assert.Empty(motorlar);

        muzik.Sync(parca, S(1.2), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(1.4), true, 1, 100, false);
        var motor = Assert.Single(motorlar);
        Assert.True(motor.IsOpen);
        Assert.True(motor.IsPaused);
        Assert.False(muzik.Playing);

        muzik.Sync(parca, S(2.25), true, 1, 100, false);
        Assert.Single(motorlar);
        Assert.False(motor.IsPaused);
        Assert.Equal(0.25, motor.PositionSeconds, 6);
    }

    [Fact]
    public async Task YonDestekleyenMotordaMuzikGeriOynatmadaGeriyeCalar()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru { Yonlu = true }; motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(20));

        muzik.Sync(parca, S(5), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(5), true, 1, 100, false);
        var motor = Assert.Single(motorlar);
        Assert.False(motor.IsPaused);
        var seek = muzik.Seeks;

        muzik.Sync(parca, S(4.9), true, 1, 100, false, backward: true);
        Assert.True(motor.Geri);
        Assert.True(muzik.Backward);
        Assert.False(motor.IsPaused);
        Assert.Equal(4.9, motor.PositionSeconds, 6);
        Assert.Equal(seek + 1, muzik.Seeks);

        motor.PositionSeconds = 4.75;
        muzik.Sync(parca, S(4.8), true, 1, 100, false, backward: true);
        Assert.Equal(seek + 1, muzik.Seeks);

        muzik.Sync(parca, S(4.8), true, 1, 100, false);
        Assert.False(motor.Geri);
        Assert.False(motor.IsPaused);
        Assert.Equal(4.8, motor.PositionSeconds, 6);
    }

    [Fact]
    public async Task YonsuzMotordaMuzikGeriOynatmadaDururIleriDonunceYerindeDevamEder()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(20));

        muzik.Sync(parca, S(5), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(5), true, 1, 100, false);
        var motor = Assert.Single(motorlar);
        Assert.False(motor.IsPaused);

        muzik.Sync(parca, S(4.5), true, 1, 100, false, backward: true);
        Assert.True(motor.IsPaused);
        Assert.False(muzik.Playing);
        muzik.Sync(parca, S(4), true, 1, 100, false, backward: true);
        Assert.True(motor.IsPaused);

        muzik.Sync(parca, S(4), true, 1, 100, false);
        Assert.False(motor.IsPaused);
        Assert.Equal(4.0, motor.PositionSeconds, 6);
        Assert.Single(motorlar);
    }

    [Fact]
    public async Task KismaKazanciKomutlaZinciriYenidenKurmadanUygulanir()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru { Komutlu = true }; motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(20), 0, -12);

        muzik.Sync(parca, S(1), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(1), true, 1, 100, false);
        var motor = Assert.Single(motorlar);
        Assert.Equal("volume=-12.0dB", motor.Ses);
        Assert.Equal(1, motor.SesKurulumu);
        Assert.Null(motor.KomutDb);

        muzik.Sync(parca, S(1.05), true, 1, 100, false, gainDb: -6);
        Assert.Equal(-18, motor.KomutDb!.Value, 9);
        Assert.Equal(-18, muzik.AppliedDb, 9);

        muzik.Sync(parca, S(1.1), true, 1, 100, false, gainDb: -6.03);
        Assert.Equal(-18, motor.KomutDb!.Value, 9);

        muzik.Sync(parca, S(1.15), true, 1, 100, false, gainDb: 0);
        Assert.Equal(-12, motor.KomutDb!.Value, 9);
        Assert.Equal(1, motor.SesKurulumu);
    }

    [Fact]
    public async Task KomutYoksaKazancFiltreyiBirDesibellikAdimlaYenidenKurar()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(20), 0, -12);

        muzik.Sync(parca, S(1), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(1), true, 1, 100, false);
        var motor = Assert.Single(motorlar);
        Assert.Equal(1, motor.SesKurulumu);

        muzik.Sync(parca, S(1.05), true, 1, 100, false, gainDb: -0.5);
        Assert.Equal(1, motor.SesKurulumu);
        Assert.Equal("volume=-12.0dB", motor.Ses);

        muzik.Sync(parca, S(1.1), true, 1, 100, false, gainDb: -3);
        Assert.Equal(2, motor.SesKurulumu);
        Assert.Equal("volume=-15.0dB", motor.Ses);

        muzik.Sync(parca, S(1.15), true, 1, 100, false, gainDb: -2.5);
        Assert.Equal(2, motor.SesKurulumu);

        muzik.Sync(parca, S(1.2), true, 1, 100, false, gainDb: -0.02);
        Assert.Equal(3, motor.SesKurulumu);
        Assert.Equal("volume=-12.0dB", motor.Ses);
    }

    [Fact]
    public void A3SiraBuyukParcalariBaslangicaGoreSecerYaklasaniIleriOynatmadaEkler()
    {
        var a = new EditAudioClip("a.wav", S(10), 0, S(4), 0);
        var b = new EditAudioClip("b.wav", S(10), S(1), S(5), S(2));
        var c = new EditAudioClip("c.wav", S(10), 0, S(2), S(5.5));
        var d = new EditAudioClip("d.wav", S(10), 0, S(2), S(10));
        var parcalar = new[] { d, c, b, a };

        Assert.Equal(new[] { "a.wav", "b.wav" }, SoundClipsPreview.Wanted(parcalar, S(3)).Select(q => q.Path));
        Assert.Equal(new[] { "b.wav", "c.wav" }, SoundClipsPreview.Wanted(parcalar, S(4.6)).Select(q => q.Path));
        Assert.Equal(new[] { "b.wav" }, SoundClipsPreview.Wanted(parcalar, S(4.6), backward: true).Select(q => q.Path));
        Assert.Empty(SoundClipsPreview.Wanted(parcalar, S(8)));

        var kalabalik = Enumerable.Range(0, 6).Select(i => new EditAudioClip(i + ".wav", S(10), 0, S(5), S(i * 0.1))).ToArray();
        Assert.Equal(SoundClipsPreview.MaxSlots, SoundClipsPreview.Wanted(kalabalik, S(1)).Count);
        Assert.Equal("3.wav", SoundClipsPreview.Wanted(kalabalik, S(1))[^1].Path);
    }

    [Fact]
    public async Task A3UstUsteBinenParcalariAyriOlculuYuvalardaKonumundaCalar()
    {
        var motorlar = new List<MuzikMotoru>();
        using var sesler = new SoundClipsPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; });
        var a = new EditAudioClip("a.wav", S(10), 0, S(4), 0, -6);
        var b = new EditAudioClip("b.wav", S(10), S(1), S(5), S(2));
        var c = new EditAudioClip("c.wav", S(10), 0, S(2), S(5.5));
        var parcalar = new[] { a, b, c };

        sesler.Sync(parcalar, S(3), true, 1, 90, false);
        await Task.WhenAll(sesler.Slots.Select(y => y.Opening));
        sesler.Sync(parcalar, S(3), true, 1, 90, false);
        Assert.Equal(2, motorlar.Count);
        Assert.All(motorlar, m => Assert.True(m.Olculu));
        Assert.All(motorlar, m => Assert.False(m.IsPaused));
        Assert.Equal(3.0, motorlar[0].PositionSeconds, 6);
        Assert.Equal("volume=-6.0dB", motorlar[0].Ses);
        Assert.Equal(2.0, motorlar[1].PositionSeconds, 6);
        Assert.Equal(90, motorlar[1].Volume, 6);

        motorlar[1].AudioPeakDb = -20;
        motorlar[0].AudioPeakDb = -30;
        Assert.Equal(-20, sesler.PeakDb, 9);

        sesler.Sync(parcalar, S(4.6), true, 1, 90, false);
        Assert.Equal(2, sesler.Slots.Count);
        Assert.Equal("c.wav", sesler.Slots[0].Path);
        Assert.Equal("b.wav", sesler.Slots[1].Path);
        Assert.False(motorlar[0].IsOpen);

        sesler.Sync(parcalar, S(4.6), false, 1, 90, false);
        Assert.All(motorlar.Where(m => m.IsOpen), m => Assert.True(m.IsPaused));
        Assert.True(double.IsNaN(sesler.PeakDb));
    }

    [Fact]
    public void KismaAnahtariSesliOlanKaynaklarinYuksegidir()
    {
        Assert.Equal(-10, EditorView.Louder(-10, -30));
        Assert.Equal(-30, EditorView.Louder(double.NaN, -30));
        Assert.Equal(-10, EditorView.Louder(-10, double.NaN));
        Assert.True(double.IsNaN(EditorView.Louder(double.NaN, double.NaN)));
    }

    [Fact]
    public void GizliV1TeslimdeVeOnizlemedeKararirSuresiVeSesiKalir()
    {
        var parca = new EditClip(0, S(4)) { Effects = new ClipEffects { Saturation = 1.4, Lut = "x.cube", AutoColor = true, CurveShadows = 0.3, MatchGainR = 1.2, FitNum = 1, FitDen = 1, PadColor = "ff0000", VolumeDb = 3 } };
        var model = new EditTimeline(new[] { parca, new EditClip(S(4), S(6)) });
        Assert.True(EditTracks.CanHide(EditTrack.Video));
        Assert.Same(model, model.Shown());

        Assert.True(model.SetTrack(EditTrack.Video, new EditTrackState(Hidden: true)));
        Assert.True(model.Track(EditTrack.Video).Hidden);

        foreach (var kopya in new[] { model.ForExport(), model.Shown() })
        {
            Assert.Equal(model.Duration, kopya.Duration);
            var etki = kopya.Clips[0].Effects;
            Assert.Equal("eq=brightness=-1:contrast=0:saturation=0", ClipFilters.Color(etki));
            Assert.Equal(3, etki.VolumeDb);
            Assert.True(etki.HasFit);
            Assert.Equal(ClipEffects.DefaultPadColor, etki.PadColor);
            Assert.False(etki.Muted);
            Assert.Equal("eq=brightness=-1:contrast=0:saturation=0", ClipFilters.Color(kopya.Clips[1].Effects));
        }

        Assert.Equal(1.4, model.Clips[0].Effects.Saturation);
        model.SetTrack(EditTrack.Video, new EditTrackState(Hidden: true, Muted: true));
        Assert.True(model.ForExport().Clips[0].Effects.Muted);
        Assert.False(model.Shown().Clips[0].Effects.Muted);
    }

    [FfmpegFact]
    public async Task CanliMotorKazancKomutuylaTepeyiDusururGeriYondeKonumGeriyeAkar()
    {
        var klasor = Path.Combine(GirdiKanit.Root, ".calisma", "onizleme-ses-yolu", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(klasor);
        try
        {
            var sarki = Path.Combine(klasor, "sarki.m4a");
            var (kod, _, hata) = GorunumKanit.Kos(ToolLocator.Ffmpeg, "-y", "-hide_banner", "-threads", "2", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                "-t", "8", "-c:a", "aac", "-b:a", "128k", sarki);
            Assert.True(kod == 0, hata[Math.Max(0, hata.Length - 400)..]);

            using var muzik = new MusicPreview(secenek =>
            {
                var motor = new MpvEngine(secenek);
                motor.SetProperty("ao", "null");
                return motor;
            }) { Metered = true };
            var parca = new EditMusic(sarki, S(8));
            muzik.Sync(parca, S(2), true, 1, 100, false);
            await muzik.Opening;
            var canli = Assert.IsType<MpvEngine>(muzik.Engine);
            muzik.Sync(parca, S(2), true, 1, 100, false);
            await Task.Delay(400);
            var acik = canli.AudioPeakDb;

            Assert.True(canli.SetEditAudioVolume(-12));
            await Task.Delay(400);
            var kisik = canli.AudioPeakDb;
            var af = canli.GetProperty("af") ?? "";

            muzik.Sync(parca, S(6), true, 1, 100, false);
            await Task.Delay(150);
            muzik.Sync(parca, S(6), true, 1, 100, false, backward: true);
            await Task.Delay(100);
            var bas = canli.PositionSeconds;
            await Task.Delay(800);
            var son = canli.PositionSeconds;
            var geriTepe = canli.AudioPeakDb;
            var kanit = string.Create(CultureInfo.InvariantCulture, $"acik={acik:0.0} kisik={kisik:0.0} af={af} bas={bas:0.000} son={son:0.000} geriTepe={geriTepe:0.0} geri={muzik.Backward}");
            File.WriteAllText(Path.Combine(klasor, "kanit.txt"), kanit);
            _cikti.WriteLine(kanit);

            Assert.InRange(acik, -21.5, -14.5);
            Assert.InRange(kisik - acik, -14, -10);
            Assert.True(muzik.Backward, kanit);
            Assert.True(son < bas - 0.3, kanit);
            Assert.True(geriTepe > -30, kanit);
        }
        finally
        {
            if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
            var ust = Path.GetDirectoryName(klasor)!;
            if (Directory.Exists(ust) && !Directory.EnumerateFileSystemEntries(ust).Any()) Directory.Delete(ust);
        }
    }
}
