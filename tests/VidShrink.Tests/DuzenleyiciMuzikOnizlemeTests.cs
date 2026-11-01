using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using VidShrink.App.Editing;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Ffmpeg;
using VidShrink.Player;
using Xunit;

namespace VidShrink.Tests;

public sealed class DuzenleyiciMuzikOnizlemeTests
{
    private static long S(double saniye) => EditTime.FromSeconds(saniye);

    private static string Klasor()
    {
        var klasor = Path.Combine(GirdiKanit.Root, ".calisma", "muzik-onizleme", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(klasor);
        return klasor;
    }

    private static void Sil(string klasor)
    {
        if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        var ust = Path.GetDirectoryName(klasor)!;
        if (Directory.Exists(ust) && !Directory.EnumerateFileSystemEntries(ust).Any()) Directory.Delete(ust);
    }

    [Fact]
    public async Task MuzikCizelgeKonumunaKilitliCalarBaslangictanOnceVeSonraSusar()
    {
        var motorlar = new List<MuzikMotoru>();
        using var muzik = new MusicPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; });
        var parca = new EditMusic("muzik.mp3", S(10), S(2), -12);

        muzik.Sync(parca, S(0.5), true, 1, 100, false);
        Assert.Empty(motorlar);

        muzik.Sync(parca, S(3), false, 1, 100, false);
        Assert.Empty(motorlar);

        muzik.Sync(parca, S(3), true, 1, 100, false);
        await muzik.Opening;
        muzik.Sync(parca, S(3), true, 1.5, 80, true);
        var motor = Assert.Single(motorlar);
        Assert.False(motor.IsPaused);
        Assert.Equal(1.0, motor.PositionSeconds, 6);
        Assert.Equal(1.5, motor.Speed, 6);
        Assert.Equal("volume=-12.0dB", motor.Ses);
        Assert.Equal(80, motor.Volume, 6);
        Assert.True(motor.Muted);

        motor.PositionSeconds = 1.1;
        muzik.Sync(parca, S(3.2), true, 1.5, 80, true);
        Assert.Equal(1, muzik.Seeks);

        muzik.Sync(parca, S(7), true, 1.5, 80, true);
        Assert.Equal(5.0, motor.PositionSeconds, 6);
        Assert.Equal(2, muzik.Seeks);

        muzik.Sync(parca, S(12.5), true, 1.5, 80, true);
        Assert.True(motor.IsPaused);

        muzik.Sync(parca.Placed(parca.Start, -6), S(4), true, 1, 80, true);
        Assert.False(motor.IsPaused);
        Assert.Equal(2.0, motor.PositionSeconds, 6);
        Assert.Equal("volume=-6.0dB", motor.Ses);

        muzik.Sync((EditMusic?)null, S(4), true, 1, 80, true);
        Assert.False(motor.IsOpen);
        Assert.Null(muzik.Engine);
    }

    [Fact]
    public void DuzenleyiciOnizlemesiOynarkenMuzikMotoruCalarDuraklayincaSusar()
    {
        var klasor = Klasor();
        var kaynak = Path.Combine(klasor, "kaynak.mp4");
        var sarki = Path.Combine(klasor, "sarki.mp3");
        File.WriteAllBytes(kaynak, new byte[16]);
        File.WriteAllBytes(sarki, new byte[16]);
        try
        {
            var sonuc = AppHost.Run(() =>
            {
                var motorlar = new List<MuzikMotoru>();
                var view = new EditorView
                {
                    KnownInfo = yol => new MediaInfo { FilePath = yol, FileSizeBytes = 16, DurationSeconds = 600, Width = 320, Height = 240, Fps = 30, VideoCodec = "h264", TotalBitrateBps = 1 },
                    AutosaveDelay = TimeSpan.FromHours(1),
                    MusicPlayer = new MusicPreview(_ => { var m = new MuzikMotoru(); motorlar.Add(m); return m; })
                };
                view.Player.EngineFactory = () => new IzMotoru(1);
                view.OverlayRoot = Path.Combine(klasor, "katman");
                var pencere = new Window { Width = 900, Height = 600, Content = view };
                pencere.Show();
                var acilis = view.OpenSourceAsync(kaynak);
                DenetimSurucu.Pump(view.Player, () => acilis.IsCompleted, 10);
                acilis.GetAwaiter().GetResult();

                view.TimelineView.Playhead = S(2);
                var konan = view.PlaceMusic(sarki, new MediaInfo
                {
                    FilePath = sarki, FileSizeBytes = 16, DurationSeconds = 30, Width = 0, Height = 0, Fps = 0, VideoCodec = "none",
                    AudioCodec = "mp3", TotalBitrateBps = 1, IsAudioOnly = true
                });
                Bekle(view, () => view.Driver is not null);
                var tohum = view.Driver!.SeekAsync(S(5));
                Bekle(view, () => tohum.IsCompleted);
                if (view.Player.IsPlaying) view.Player.TogglePlay();
                view.Follow();
                var durgunMotor = motorlar.Count(m => !m.IsPaused) + (view.MusicPlayer.Playing ? 1 : 0);

                view.Player.TogglePlay();
                view.Follow();
                Bekle(view, () => view.MusicPlayer.Opening.IsCompleted);
                view.Follow();
                var motor = motorlar.Single();
                var calarken = (!motor.IsPaused, motor.PositionSeconds, motor.Ses);

                view.Player.TogglePlay();
                view.Follow();
                var durunca = motor.IsPaused;

                view.RemoveMusic();
                view.Follow();
                var kalkinca = (motor.IsOpen, view.MusicPlayer.Engine is null);
                pencere.Close();
                return (konan, durgunMotor, calarken, durunca, kalkinca);
            });

            Assert.True(sonuc.konan);
            Assert.Equal(0, sonuc.durgunMotor);
            Assert.True(sonuc.calarken.Item1, "önizleme oynarken müzik motoru çalmadı");
            Assert.Equal(3.0, sonuc.calarken.PositionSeconds, 3);
            Assert.Equal("volume=-12.0dB", sonuc.calarken.Ses);
            Assert.True(sonuc.durunca, "önizleme durunca müzik susmadı");
            Assert.Equal((false, true), sonuc.kalkinca);
        }
        finally
        {
            Sil(klasor);
        }
    }

    [FfmpegFact]
    public async Task CanliMotorAoNullIleMuzikSesiniDuzeyleVeKonumlaVerir()
    {
        var klasor = Klasor();
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
            });
            var parca = new EditMusic(sarki, S(8), S(1), -12);
            var saat = Stopwatch.StartNew();
            double Cizelge() => 2 + saat.Elapsed.TotalSeconds;

            muzik.Sync(parca, S(Cizelge()), true, 1, 100, false);
            await muzik.Opening;
            var canli = Assert.IsType<MpvEngine>(muzik.Engine);
            Assert.True(canli.MeterAudio(true));
            for (var i = 0; i < 30; i++)
            {
                muzik.Sync(parca, S(Cizelge()), true, 1, 100, false);
                await Task.Delay(50);
            }

            var kayma = Math.Abs(canli.PositionSeconds - (Cizelge() - 1));
            var tepe = canli.AudioPeakDb;
            var af = canli.GetProperty("af") ?? "";
            var sesParam = canli.GetProperty("audio-params/samplerate");
            var kanit = $"kayma={kayma.ToString("0.000", CultureInfo.InvariantCulture)} tepe={tepe.ToString("0.0", CultureInfo.InvariantCulture)} af={af} params={sesParam} seek={muzik.Seeks}";

            muzik.Sync(parca, S(Cizelge()), false, 1, 100, false);
            var durdu = false;
            for (var i = 0; i < 30 && !durdu; i++)
            {
                durdu = canli.IsPaused;
                await Task.Delay(50);
            }

            Assert.Equal("48000", sesParam);
            Assert.Contains("volume=-12.0dB", af, StringComparison.Ordinal);
            Assert.True(kayma < MusicPreview.DriftSeconds + 0.1, kanit);
            Assert.InRange(tepe, -33.5, -26.5);
            Assert.True(durdu, kanit);
        }
        finally
        {
            Sil(klasor);
        }
    }

    private static void Bekle(Control view, Func<bool> kosul)
    {
        var saat = Stopwatch.StartNew();
        while (!kosul() && saat.Elapsed.TotalSeconds < 15)
        {
            using var dilim = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
            Dispatcher.UIThread.MainLoop(dilim.Token);
        }

        Assert.True(kosul(), "bekleme süresi doldu");
    }
}

internal sealed class MuzikMotoru : IPlaybackEngine
{
    public string Name => "muzik-motoru";

    public bool IsOpen { get; private set; }

    public double DurationSeconds => 30;

    public bool HasAudio => true;

    public bool HasVideo => false;

    public bool IsPaused { get; private set; } = true;

    public bool EndReached => false;

    public double PositionSeconds { get; set; }

    public double AudioVideoOffsetSeconds => double.NaN;

    public long FramesRendered => 0;

    public double Speed { get; private set; } = 1;

    public double Volume { get; private set; } = 100;

    public bool Muted { get; private set; }

    public string? Ses { get; private set; }

    public int SesKurulumu { get; private set; }

    public bool Yonlu { get; init; }

    public bool Komutlu { get; init; }

    public bool Geri { get; private set; }

    public double? KomutDb { get; private set; }

    public bool Olculu { get; private set; }

    public double AudioPeakDb { get; set; } = double.NaN;

    public event EventHandler<PlaybackFault>? Faulted { add { } remove { } }

    public Task OpenAsync(string path, CancellationToken ct = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public void Play() => IsPaused = false;

    public void Pause() => IsPaused = true;

    public void SetSpeed(double speed) => Speed = speed;

    public void SetVolume(double volume) => Volume = volume;

    public void SetMuted(bool muted) => Muted = muted;

    public void SetEditTimed(string? video, string? audio)
    {
        Ses = audio;
        SesKurulumu++;
    }

    public bool SetEditAudioVolume(double db)
    {
        if (!Komutlu) return false;
        KomutDb = db;
        return true;
    }

    public bool SetPlayDirection(bool backward)
    {
        if (!Yonlu) return false;
        Geri = backward;
        return true;
    }

    public bool MeterAudio(bool on)
    {
        Olculu = on;
        return true;
    }

    public Task<SeekResult> SeekAsync(double seconds, SeekPrecision precision, CancellationToken ct = default)
    {
        PositionSeconds = seconds;
        return Task.FromResult(new SeekResult(SeekOutcome.Shown, 1));
    }

    public bool TryCopyLatest(ref long seen, FrameCopy copy) => false;

    public void Dispose() => IsOpen = false;
}
