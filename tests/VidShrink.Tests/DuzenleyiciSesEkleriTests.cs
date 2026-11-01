using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VidShrink.App.Editing;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Ffmpeg;
using Xunit;

namespace VidShrink.Tests;

/// <summary>
/// Later 139: A3 ses izi (ayrilan ses DZB-10, seslendirme DZB-47) ve iz denetimleri (DZB-06: sessiz, yalniz bu,
/// kilit, gizle). Ayrilan ses parcanin kaynak araligini tasir, video parcasini susturur ve tek adimda geri alinir.
/// Teslim A3 parcalarini <c>[abed]</c> uzerine <c>amix</c> eder; sessiz ya da yalniz-bu disinda kalan iz teslime gitmez.
/// Proje dosyasi A3'u ve iz durumunu tasir. Canli kol 3 sn'lik 440 Hz kaynagin sesini ayirip 1. saniyeye 3000 Hz
/// seslendirme ekler; A3 susunca iki ses de kaybolur. Kanit <c>.calisma/worktree-agent-later-139/ses-ekleri/</c>, test siler.
/// </summary>
public sealed class DuzenleyiciSesEkleriTests
{
    private const long Gb = 1_000_000_000;

    private static readonly double[] Kareler = { 0.0, 2, 4, 6, 8 };

    private static long S(double saniye) => EditTime.FromSeconds(saniye);

    private static string Kanit
    {
        get
        {
            var yol = Path.Combine(GirdiKanit.Root, ".calisma", "worktree-agent-later-139", "ses-ekleri");
            Directory.CreateDirectory(yol);
            return yol;
        }
    }

    private static MediaInfo Bilgi() => new()
    {
        FilePath = "kaynak.mp4",
        FileSizeBytes = 1,
        DurationSeconds = 60,
        Width = 1920,
        Height = 1080,
        Fps = 30,
        VideoCodec = "h264",
        TotalBitrateBps = 1,
        AudioCodec = "aac",
        AudioChannels = 2,
        PixelFormat = "yuv420p",
        BitDepth = 8,
        Streams = new[]
        {
            new SourceStream(0, StreamKind.Video, "h264"),
            new SourceStream(1, StreamKind.Audio, "aac", "tur", IsDefault: true, Channels: 2)
        }
    };

    private static EditTimeline IkiKlip() => new(new[] { new EditClip(S(1), S(3)), new EditClip(S(4), S(6)) });

    private static EditAudioClip Anlatim(double baslangic = 1) => new("anlatim.m4a", S(2), 0, S(2), S(baslangic), -3, EditAudioKind.Narration);

    private static ExportPlan Plan(ExportMode kip, EditTimeline cizelge)
        => EditExport.Build(cizelge, Bilgi(), Kareler, 0, kip, "c.mp4", "is", 8 * Gb,
            DuzenleyiciAkilliKodekTests.Sinirlar(Kareler), _ => true);

    private static IReadOnlyList<string> Args(ExportPlan plan) => Assert.Single(plan.Steps).Args;

    private static string Grafik(ExportPlan plan) => Args(plan)[Args(plan).ToList().IndexOf("-filter_complex") + 1];

    private static List<string> Eslenen(IReadOnlyList<string> args)
        => args.Select((a, i) => (a, i)).Where(x => x.i > 0 && args[x.i - 1] == "-map").Select(x => x.a).ToList();

    [Fact]
    public void SesAyirmaParcayiSusturupA3eTasirVeTekAdimdaGeriAlinir()
    {
        var cizelge = IkiKlip();
        Assert.True(cizelge.SetEffects(new[] { 1 }, e => e with { VolumeDb = -4.5 }));

        Assert.True(cizelge.DetachAudio(1, "kaynak.mp4"));

        var parca = Assert.Single(cizelge.AudioClips);
        Assert.True(cizelge.Clips[1].Effects.Muted);
        Assert.False(cizelge.Clips[0].Effects.Muted);
        Assert.Equal(S(4), parca.In);
        Assert.Equal(S(6), parca.Out);
        Assert.Equal(S(2), parca.Start);
        Assert.Equal(-4.5, parca.LevelDb);
        Assert.Equal(EditAudioKind.Detached, parca.Kind);
        Assert.False(cizelge.DetachAudio(1, "kaynak.mp4"));

        Assert.True(cizelge.Undo());
        Assert.Empty(cizelge.AudioClips);
        Assert.False(cizelge.Clips[1].Effects.Muted);
        Assert.True(cizelge.Redo());
        Assert.Single(cizelge.AudioClips);
        Assert.True(cizelge.Clips[1].Effects.Muted);
    }

    [Fact]
    public void HizliYaDaTersParcaninSesiAyrilmaz()
    {
        var cizelge = IkiKlip();
        Assert.True(cizelge.SetSpeed(0, 2m));
        Assert.False(cizelge.DetachAudio(0, "kaynak.mp4"));
        Assert.Empty(cizelge.AudioClips);
        Assert.False(cizelge.Clips[0].Effects.Muted);
    }

    [Fact]
    public void A3ParcasiTasinirKirpilirVeDuzeyiSinirlanir()
    {
        var cizelge = IkiKlip();
        Assert.Equal(0, cizelge.AddAudio(Anlatim()));

        Assert.True(cizelge.MoveAudio(0, S(2)));
        Assert.Equal(S(2), cizelge.AudioClips[0].Start);
        Assert.True(cizelge.TrimAudio(0, false, S(3)));
        Assert.Equal(S(1), cizelge.AudioClips[0].Length);
        Assert.False(cizelge.TrimAudio(0, false, S(3)));
        Assert.True(cizelge.TrimAudio(0, true, S(2.5)));
        Assert.Equal(S(0.5), cizelge.AudioClips[0].In);
        Assert.Equal(S(2.5), cizelge.AudioClips[0].Start);
        Assert.True(cizelge.TrimAudio(0, true, 0));
        Assert.Equal(0, cizelge.AudioClips[0].In);
        Assert.Equal(S(2), cizelge.AudioClips[0].Start);
        Assert.True(cizelge.SetAudioLevel(0, 99));
        Assert.Equal(ClipEffects.MaxVolumeDb, cizelge.AudioClips[0].LevelDb);

        cizelge.DeleteAudio(0);
        Assert.False(cizelge.HasAudioClips);
        Assert.True(cizelge.Undo());
        Assert.True(cizelge.HasAudioClips);
    }

    [Fact]
    public void YalnizBuVeSessizDuyulanIzleriBelirler()
    {
        var cizelge = IkiKlip();
        Assert.True(cizelge.Audible(EditTrack.Video));
        Assert.True(cizelge.Audible(EditTrack.Music));
        Assert.False(cizelge.Audible(EditTrack.Text));

        Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Solo: true)));
        Assert.True(cizelge.Audible(EditTrack.Audio));
        Assert.False(cizelge.Audible(EditTrack.Video));
        Assert.False(cizelge.Audible(EditTrack.Music));

        Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Muted: true, Solo: true)));
        Assert.False(cizelge.Audible(EditTrack.Audio));

        Assert.False(cizelge.SetTrack(EditTrack.Text, new EditTrackState(Muted: true, Solo: true)));
        Assert.True(cizelge.SetTrack(EditTrack.Music, new EditTrackState(Hidden: true, Locked: true)));
        Assert.Equal(new EditTrackState(Locked: true), cizelge.Track(EditTrack.Music));
        Assert.True(cizelge.SetTrack(EditTrack.Video, new EditTrackState(Hidden: true, Locked: true)));
        Assert.Equal(new EditTrackState(Hidden: true, Locked: true), cizelge.Track(EditTrack.Video));
        Assert.False(cizelge.Visible(EditTrack.Video));
        Assert.True(cizelge.IsLocked(EditTrack.Video));
        Assert.True(cizelge.SetTrack(EditTrack.Video, default));
        Assert.False(cizelge.TrackStates.ContainsKey(EditTrack.Video));
    }

    [Fact]
    public void TeslimKopyasiSessizVeGizliIzleriDusurur()
    {
        var cizelge = IkiKlip();
        cizelge.AddText(new TextLayer("a", 0, S(1)));
        Assert.True(cizelge.SetMusic(new EditMusic("muzik.m4a", S(30), 0, -6)));
        cizelge.AddAudio(Anlatim());

        var acik = cizelge.ForExport();
        Assert.True(acik.HasText);
        Assert.True(acik.HasMusic);
        Assert.True(acik.HasAudioClips);
        Assert.All(acik.Clips, c => Assert.False(c.Effects.Muted));

        Assert.True(cizelge.SetTrack(EditTrack.Text, new EditTrackState(Hidden: true)));
        Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Solo: true)));
        var yalniz = cizelge.ForExport();
        Assert.False(yalniz.HasText);
        Assert.False(yalniz.HasMusic);
        Assert.True(yalniz.HasAudioClips);
        Assert.All(yalniz.Clips, c => Assert.True(c.Effects.Muted));
        Assert.All(cizelge.Clips, c => Assert.False(c.Effects.Muted));

        Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Muted: true)));
        var sessiz = cizelge.ForExport();
        Assert.False(sessiz.HasAudioClips);
        Assert.True(sessiz.HasMusic);
        Assert.All(sessiz.Clips, c => Assert.False(c.Effects.Muted));
    }

    [Theory]
    [InlineData(ExportMode.Fast)]
    [InlineData(ExportMode.Smart)]
    [InlineData(ExportMode.Full)]
    public void TeslimA3uKaynakSesininUstuneKatar(ExportMode kip)
    {
        var cizelge = IkiKlip();
        cizelge.AddAudio(Anlatim(1));
        cizelge.AddAudio(new EditAudioClip("ayrik.mp4", S(60), S(4), S(6), S(2), 0));

        var plan = Plan(kip, cizelge);
        var args = Args(plan);
        var grafik = Grafik(plan);

        Assert.Equal(ExportMode.Full, plan.Effective);
        Assert.Equal(3, args.Count(a => a == "-i"));
        Assert.Equal("anlatim.m4a", args[args.ToList().IndexOf("-i", args.ToList().IndexOf("-i") + 1) + 1]);
        Assert.Contains("[abed]", grafik);
        Assert.Contains("[1:a:0]atrim=start=0:end=2,asetpts=PTS-STARTPTS,volume=-3.0dB,", grafik);
        Assert.Contains("adelay=1000:all=1,apad,atrim=duration=4,", grafik);
        Assert.Contains("[2:a:0]atrim=start=4:end=6,", grafik);
        Assert.Contains("amix=inputs=3:duration=first:dropout_transition=0:normalize=0[aout]", grafik);
        Assert.Single(Regex.Matches(grafik, @"\[aout\]"));
        Assert.Contains("[aout]", Eslenen(args));
    }

    [Fact]
    public void A3VeMuzikBirlikteMuzikSonaKatilir()
    {
        var cizelge = IkiKlip();
        cizelge.AddAudio(Anlatim());
        Assert.True(cizelge.SetMusic(new EditMusic("muzik.m4a", S(30), 0, -6)));

        var args = Args(Plan(ExportMode.Full, cizelge));
        var girdiler = args.Select((a, i) => (a, i)).Where(x => x.i > 0 && args[x.i - 1] == "-i").Select(x => x.a).ToArray();

        Assert.Equal(new[] { "kaynak.mp4", "anlatim.m4a", "muzik.m4a" }, girdiler);
        Assert.Contains("[amix]", Eslenen(args));
    }

    [Fact]
    public void ZincirSureyiVeGecikmeyiCizelgedenAlir()
    {
        var zincir = EditExport.AudioClipChain(new EditAudioClip("a.m4a", S(10), S(1.5), S(4), S(0.25), 2.5), 7, "9.5");

        Assert.StartsWith("[7:a:0]atrim=start=1.5:end=4,asetpts=PTS-STARTPTS,volume=2.5dB,", zincir);
        Assert.Contains(",adelay=250:all=1,apad,atrim=duration=9.5,asetpts=PTS-STARTPTS", zincir);
    }

    [Fact]
    public void ProjeDosyasiA3uVeIzDurumunuTasir()
    {
        var damga = new SourceStamp("kaynak.mp4", 10, 20);
        var cizelge = IkiKlip();
        cizelge.AddAudio(Anlatim(1.5));
        cizelge.AddAudio(new EditAudioClip(@"C:\kaynak\ayrik.mp4", S(60), S(4), S(6), S(2), -4.5));
        Assert.True(cizelge.SetTrack(EditTrack.Music, new EditTrackState(Muted: true, Locked: true)));
        Assert.True(cizelge.SetTrack(EditTrack.Text, new EditTrackState(Hidden: true)));

        var json = EditProject.From(damga, cizelge).ToJson();
        var geri = EditProject.Parse(json);

        Assert.NotNull(geri);
        Assert.Equal(cizelge.AudioClips, geri!.AudioClips);
        Assert.Contains("\"A2\"", json);
        var yeni = IkiKlip();
        Assert.True(yeni.Restore(yeni.Clips.ToArray(), Array.Empty<TextLayer>(), null, default, geri.AudioClips));
        geri.ApplyTracks(yeni);
        Assert.Equal(cizelge.AudioClips, yeni.AudioClips);
        Assert.Equal(new EditTrackState(Muted: true, Locked: true), yeni.Track(EditTrack.Music));
        Assert.Equal(new EditTrackState(Hidden: true), yeni.Track(EditTrack.Text));
        Assert.True(yeni.Undo());
        Assert.Empty(yeni.AudioClips);

        var eski = EditProject.From(damga, IkiKlip()).ToJson();
        Assert.DoesNotContain("audioClips", eski);
        Assert.DoesNotContain("tracks", eski);
        Assert.Empty(EditProject.Parse(eski)!.AudioClips);

        var bozuk = Regex.Replace(json, @"""level"":\s*-4\.5", "\"level\": 99");
        Assert.NotEqual(json, bozuk);
        Assert.Null(EditProject.Parse(bozuk));
    }

    [Fact]
    public void KaydetmeAnahtariA3VeIzDurumuylaDegisir()
    {
        var cizelge = IkiKlip();
        var bos = EditorView.Fingerprint(cizelge);
        cizelge.AddAudio(Anlatim());
        var sesli = EditorView.Fingerprint(cizelge);
        Assert.NotEqual(bos, sesli);
        Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Muted: true)));
        Assert.NotEqual(sesli, EditorView.Fingerprint(cizelge));
    }

    [Fact]
    public void SeslendirmeTekMikrofonuAacOlarakYazar()
    {
        var mikrofon = new AudioCaptureDevice("Mikrofon (USB)", CaptureBackend.DirectShow, AudioSourceRole.Microphone);
        var diger = new AudioCaptureDevice("Kulaklik", CaptureBackend.DirectShow, AudioSourceRole.Microphone);
        var sistem = new AudioCaptureDevice("Hoparlor", CaptureBackend.DirectShow, AudioSourceRole.SystemAudio);
        var liste = new[] { sistem, diger, mikrofon };

        Assert.Same(mikrofon, NarrationArguments.PickMicrophone(liste, "Mikrofon (USB)"));
        Assert.Same(diger, NarrationArguments.PickMicrophone(liste, "yok"));
        Assert.Null(NarrationArguments.PickMicrophone(new[] { sistem }, null));

        var plan = NarrationArguments.Plan(mikrofon, liste, AudioFilterOptions.None with { PushToTalk = true });
        var args = NarrationArguments.Build(plan, "cikti.m4a");

        Assert.Equal(1, args.Count(a => a == "-i"));
        Assert.Contains(args, a => a.Contains("Mikrofon (USB)", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.Contains("Kulaklik", StringComparison.Ordinal) || a.Contains("Hoparlor", StringComparison.Ordinal));
        Assert.Equal(new[] { "-vn", "-c:a", "aac", "-b:a", NarrationArguments.Bitrate, "cikti.m4a" }, args.TakeLast(6));
        Assert.Equal("anlatim-20261021-140501.m4a", NarrationArguments.FileName(new DateTime(2026, 10, 21, 14, 5, 1)));
    }

    [FfmpegFact]
    public async Task CanliTeslimAyrikSesiVeSeslendirmeyiKatar()
    {
        string[] dosyalar = { "kaynak.mp4", "anlatim.m4a", "acik.mp4", "a3sessiz.mp4", "olcu.txt" };
        KanitKapanisi.Onceki(Kanit, dosyalar);
        var kaynak = Path.Combine(Kanit, "kaynak.mp4");
        var anlatim = Path.Combine(Kanit, "anlatim.m4a");
        Ffmpeg("-hide_banner", "-y",
            "-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=3",
            "-f", "lavfi", "-i", "sine=f=440:d=3",
            "-c:v", "libx264", "-preset", "ultrafast", "-g", "15", "-threads", "2", "-c:a", "aac", "-shortest", kaynak);
        Ffmpeg("-hide_banner", "-y", "-f", "lavfi", "-i", "sine=f=3000:d=1.5", "-c:a", "aac", "-threads", "2", anlatim);
        var satirlar = new List<string>();
        var olcu = new Dictionary<string, (double Alt, double Ust, double Bas)>();

        foreach (var (sessiz, ad) in new[] { (false, "acik.mp4"), (true, "a3sessiz.mp4") })
        {
            var cizelge = new EditTimeline(new[] { new EditClip(S(0), S(3)) });
            Assert.True(cizelge.DetachAudio(0, kaynak));
            cizelge.AddAudio(new EditAudioClip(anlatim, S(1.5), 0, S(1.5), S(1), 0, EditAudioKind.Narration));
            if (sessiz) Assert.True(cizelge.SetTrack(EditTrack.Audio, new EditTrackState(Muted: true)));
            var cikti = Path.Combine(Kanit, ad);
            var plan = await EditExportRunner.PrepareAsync(new[] { kaynak }, cizelge.ForExport(), ExportMode.Full, cikti, 8 * Gb);
            await EditExportRunner.RunAsync(plan, new Bos());
            const string ust = "highpass=f=2000,highpass=f=2000,highpass=f=2000";
            var alt = OrtalamaDuzey(cikti, "lowpass=f=1000,lowpass=f=1000,lowpass=f=1000");
            var yuksek = OrtalamaDuzey(cikti, ust);
            var bas = OrtalamaDuzey(cikti, "atrim=0:0.9," + ust);
            olcu[ad] = (alt, yuksek, bas);
            satirlar.Add($"{ad} alt1k={alt:0.0}dB ust2k={yuksek:0.0}dB ilk0.9sn_ust2k={bas:0.0}dB");
        }

        File.WriteAllLines(Path.Combine(Kanit, "olcu.txt"), satirlar);
        var ozet = string.Join(" | ", satirlar);
        Assert.True(olcu["acik.mp4"].Alt > -40, ozet);
        Assert.True(olcu["acik.mp4"].Ust > -40, ozet);
        Assert.True(olcu["acik.mp4"].Bas < -60, ozet);
        Assert.True(olcu["a3sessiz.mp4"].Alt < -60, ozet);
        Assert.True(olcu["a3sessiz.mp4"].Ust < -60, ozet);
        KanitKapanisi.Kapat(Kanit, dosyalar);
    }

    private static double OrtalamaDuzey(string dosya, string suzgec)
    {
        var hata = Ffmpeg("-hide_banner", "-nostdin", "-threads", "2", "-i", dosya, "-vn", "-af", suzgec + ",volumedetect", "-f", "null", "-");
        var eslesme = Regex.Match(hata, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        Assert.True(eslesme.Success, hata);
        return eslesme.Groups[1].Value == "-inf" ? double.NegativeInfinity : double.Parse(eslesme.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string Ffmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var surec = Process.Start(psi)!;
        var hata = surec.StandardError.ReadToEndAsync();
        if (!surec.WaitForExit(30000))
        {
            surec.Kill(true);
            Assert.Fail("ffmpeg 30 sn icinde bitmedi.");
        }

        var metin = hata.GetAwaiter().GetResult();
        Assert.True(surec.ExitCode == 0, metin);
        return metin;
    }

    private sealed class Bos : IProgress<EncodeProgress>
    {
        public void Report(EncodeProgress value)
        {
        }
    }
}
