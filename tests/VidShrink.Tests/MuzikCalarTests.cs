using System.Runtime.InteropServices;
using Avalonia.Controls;
using VidShrink.App;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Ffmpeg;
using VidShrink.Player;
using Xunit;

namespace VidShrink.Tests;

public sealed class MuzikCalarTests
{
    private static string SesDosyasi(string ad)
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-calar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var yol = Path.Combine(klasor, ad);
        File.WriteAllBytes(yol, new byte[16]);
        return yol;
    }

    private static PlayerView Ac(IPlaybackEngine motor, string yol, out Window pencere)
    {
        var view = new PlayerView { EngineFactory = () => motor };
        pencere = new Window { Width = 640, Height = 360, Content = view };
        pencere.Show();
        var acilis = view.OpenAsync(yol);
        DenetimSurucu.Pump(view, () => acilis.IsCompleted, 10);
        acilis.GetAwaiter().GetResult();
        DenetimSurucu.Wait(view, 0.2);
        return view;
    }

    [Fact]
    public void Ses_dosyasinda_kart_baslik_ve_sanatciyi_gosterir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var motor = new SesMotoru(new MediaTags("Parça", "Sanatçı", "Albüm"));
            var view = Ac(motor, SesDosyasi("sarki.mp3"), out var pencere);
            var cikti = (view.AudioOnly, view.AudioTitle, view.AudioSubtitle);
            view.Close();
            pencere.Close();
            return cikti;
        });

        Assert.True(sonuc.AudioOnly);
        Assert.Equal("Parça", sonuc.AudioTitle);
        Assert.Equal("Sanatçı - Albüm", sonuc.AudioSubtitle);
    }

    [Fact]
    public void Etiketsiz_ses_dosyasinda_dosya_adi_baslik_olur()
    {
        var sonuc = AppHost.Run(() =>
        {
            var view = Ac(new SesMotoru(MediaTags.Empty), SesDosyasi("kayit 01.flac"), out var pencere);
            var cikti = (view.AudioTitle, view.AudioSubtitle);
            view.Close();
            pencere.Close();
            return cikti;
        });

        Assert.Equal("kayit 01", sonuc.AudioTitle);
        Assert.Null(sonuc.AudioSubtitle);
    }

    [Fact]
    public void Ses_dosyasinda_sure_cubugu_motorun_konumunu_izler()
    {
        var konum = AppHost.Run(() =>
        {
            var motor = new SesMotoru(MediaTags.Empty);
            var view = Ac(motor, SesDosyasi("sarki.m4a"), out var pencere);
            motor.PositionSeconds = 42.5;
            DenetimSurucu.Pump(view, () => Math.Abs(view.PositionSeconds - 42.5) < 0.01, 3);
            var cikti = view.PositionSeconds;
            view.Close();
            pencere.Close();
            return cikti;
        });

        Assert.Equal(42.5, konum, 2);
    }

    [Fact]
    public void Ses_dosyasinda_takilma_uyarisi_cikmaz()
    {
        var takildi = AppHost.Run(() =>
        {
            var view = Ac(new SesMotoru(MediaTags.Empty), SesDosyasi("sarki.wav"), out var pencere);
            view.PollStall(0);
            view.PollStall(10);
            var cikti = view.Stall.Stalled;
            view.Close();
            pencere.Close();
            return cikti;
        });

        Assert.False(takildi);
    }

    [Fact]
    public void Video_dosyasinda_ses_karti_gorunmez()
    {
        var sonuc = AppHost.Run(() =>
        {
            var view = Ac(new YolMotoru(), SesDosyasi("film.mp4"), out var pencere);
            var cikti = (view.AudioOnly, view.AudioTitle);
            view.Close();
            pencere.Close();
            return cikti;
        });

        Assert.False(sonuc.AudioOnly);
        Assert.Null(sonuc.AudioTitle);
    }

    [Fact]
    public void Motor_gomulu_kapagi_gosterir()
    {
        Assert.Contains(("audio-display", "embedded-first"), MpvEngine.OptionsFor(new PlaybackOptions()));
    }

    [Fact]
    public void Klasorde_ses_sesle_video_videoyla_gezilir()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-klasor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        foreach (var ad in new[] { "01 a.mp3", "02 b.flac", "03 c.mp4", "04 d.opus", "05 e.mkv", "kapak.jpg", "liste.m3u" })
            File.WriteAllBytes(Path.Combine(klasor, ad), new byte[1]);

        var ses = FolderNavigator.Siblings(Path.Combine(klasor, "02 b.flac")).Select(Path.GetFileName).ToArray();
        var video = FolderNavigator.Siblings(Path.Combine(klasor, "03 c.mp4")).Select(Path.GetFileName).ToArray();
        var sonraki = FolderNavigator.Step(Path.Combine(klasor, "02 b.flac"), true, RepeatMode.Off, false, 0);
        Directory.Delete(klasor, true);

        Assert.Equal(new[] { "01 a.mp3", "02 b.flac", "04 d.opus" }, ses);
        Assert.Equal(new[] { "03 c.mp4", "05 e.mkv" }, video);
        Assert.Equal("04 d.opus", Path.GetFileName(sonraki));
    }

    [Fact]
    public void Ses_uzantilari_videoyla_cakismaz()
    {
        Assert.Empty(ShellIntegration.AudioExtensions.Intersect(ShellIntegration.MediaExtensions));
        Assert.True(ShellIntegration.IsAudio(@"C:\m\Sarki.MP3"));
        Assert.False(ShellIntegration.IsAudio("film.mp4"));
        var wmpDisi = new[] { "m4b", "ogg", "oga", "opus", "ac3", "ape", "wv", "weba" };
        Assert.All(ShellIntegration.AudioExtensions.Except(wmpDisi), e => Assert.Contains(e, ShellIntegration.BulkDefaultExtensions));
    }

    [Fact]
    public void Oynaticida_acilan_ses_kucultme_sekmesine_gitmez()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-odak", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var ses = Path.Combine(klasor, "sarki.mp3");
        var film = Path.Combine(klasor, "film.mp4");
        File.WriteAllBytes(ses, new byte[1]);
        File.WriteAllBytes(film, new byte[1]);

        var yuklenen = AppHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                var liste = new List<string>();
                window.FollowShrinkLoader = p => { liste.Add(Path.GetFileName(p)); return Task.CompletedTask; };
                window.ChkFollowRecording.IsChecked = true;
                window.PlayerOpenedForTest(ses);
                window.PlayerOpenedForTest(film);
                return liste;
            }
            finally { window.Close(); }
        });
        Directory.Delete(klasor, true);

        Assert.Equal(new[] { "film.mp4" }, yuklenen);
    }

    [Fact]
    public void Acilista_verilen_ses_oynaticida_acilir_kucultmeye_yuklenmez()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-acilis", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var ses = Path.Combine(klasor, "sarki.mp3");
        var film = Path.Combine(klasor, "bozuk.mp4");
        File.WriteAllBytes(ses, new byte[16]);
        File.WriteAllBytes(film, new byte[16]);

        (bool Hata, string? Yuklu, string? Calan) Ac(string yol) => AppHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                window.PlayerTab.EngineFactory = () => new SesMotoru(MediaTags.Empty);
                window.Show();
                var is1 = window.LoadStartupFileAsync(yol);
                DenetimSurucu.Pump(window.PlayerTab, () => is1.IsCompleted, 30);
                return (window.SourceStatusVisible, window.ShrinkLoadedPath, window.PlayerTab.LoadedPath);
            }
            finally { window.Close(); }
        });

        var sesSonuc = Ac(ses);
        var filmSonuc = Ac(film);
        Directory.Delete(klasor, true);

        Assert.False(sesSonuc.Hata);
        Assert.Null(sesSonuc.Yuklu);
        Assert.Equal(ses, sesSonuc.Calan);
        Assert.True(filmSonuc.Hata, "negatif kontrol: bozuk video kucultmeye gidip hata vermeliydi");
    }

    private static string Uret(string klasor, string ad, params string[] arguman)
    {
        Assert.True(ToolLocator.IsAvailable(out var eksik), $"ornek uretimi icin {eksik} gerekli");
        var yol = Path.Combine(klasor, ad);
        var (kod, _, hata) = GorunumKanit.Kos(ToolLocator.Ffmpeg, arguman.Prepend("-hide_banner").Prepend("-y").Append(yol).ToArray());
        Assert.True(kod == 0, $"ffmpeg {ad} uretemedi: {hata[Math.Max(0, hata.Length - 400)..]}");
        return yol;
    }

    [Fact]
    public async Task Libmpv_kapaksiz_mp3te_arama_zaman_asimina_dusmeden_biter()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-arama", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var mp3 = Uret(klasor, "kapaksiz.mp3",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=20",
            "-c:a", "libmp3lame");

        using var motor = new MpvEngine();
        motor.SetProperty("ao", "null");
        await motor.OpenAsync(mp3);
        var sonuc = await motor.SeekAsync(12, SeekPrecision.Exact);
        var konum = motor.PositionSeconds;
        motor.Dispose();
        Directory.Delete(klasor, true);

        Assert.False(motor.HasVideo);
        Assert.Equal(SeekOutcome.Shown, sonuc.Outcome);
        Assert.InRange(konum, 11.5, 12.5);
    }

    [Fact]
    public async Task Libmpv_her_ses_uzantisini_acar_ve_istenen_yere_sarar()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-tum-uzantilar", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var kaynak = new[] { "-f", "lavfi", "-i", "sine=frequency=440:duration=20" };
        var tarifler = new Dictionary<string, string[]>
        {
            ["mp3"] = new[] { "-c:a", "libmp3lame" },
            ["m4a"] = new[] { "-c:a", "aac" },
            ["m4b"] = new[] { "-c:a", "aac", "-f", "ipod" },
            ["aac"] = new[] { "-c:a", "aac", "-f", "adts" },
            ["adts"] = new[] { "-c:a", "aac", "-f", "adts" },
            ["adt"] = new[] { "-c:a", "aac", "-f", "adts" },
            ["flac"] = new[] { "-c:a", "flac" },
            ["wav"] = new[] { "-c:a", "pcm_s16le" },
            ["wma"] = new[] { "-c:a", "wmav2", "-f", "asf" },
            ["ogg"] = new[] { "-c:a", "libvorbis", "-f", "ogg" },
            ["oga"] = new[] { "-c:a", "libvorbis", "-f", "ogg" },
            ["opus"] = new[] { "-c:a", "libopus", "-f", "opus" },
            ["mka"] = new[] { "-c:a", "libvorbis", "-f", "matroska" },
            ["aif"] = new[] { "-c:a", "pcm_s16be", "-f", "aiff" },
            ["aifc"] = new[] { "-c:a", "pcm_s16be", "-f", "aiff" },
            ["aiff"] = new[] { "-c:a", "pcm_s16be", "-f", "aiff" },
            ["au"] = new[] { "-c:a", "pcm_s16be", "-f", "au" },
            ["snd"] = new[] { "-c:a", "pcm_s16be", "-f", "au" },
            ["mp2"] = new[] { "-c:a", "mp2", "-f", "mp2" },
            ["mpa"] = new[] { "-c:a", "mp2", "-f", "mp2" },
            ["ac3"] = new[] { "-c:a", "ac3", "-f", "ac3" },
            ["weba"] = new[] { "-c:a", "libopus", "-f", "webm" }
        };

        var kusurlar = new List<string>();
        foreach (var (uzanti, tarif) in tarifler)
        {
            string yol;
            try { yol = Uret(klasor, "ornek." + uzanti, kaynak.Concat(tarif).ToArray()); }
            catch (Xunit.Sdk.XunitException) { continue; }

            using var motor = new MpvEngine();
            motor.SetProperty("ao", "null");
            try
            {
                await motor.OpenAsync(yol);
                var sonuc = await motor.SeekAsync(12, SeekPrecision.Exact);
                var konum = motor.PositionSeconds;
                if (motor.HasVideo || sonuc.Outcome != SeekOutcome.Shown || konum < 11.4 || konum > 12.6)
                    kusurlar.Add($"{uzanti}: {sonuc.Outcome} konum={konum:F2} video={motor.HasVideo}");
            }
            catch (Exception hata) { kusurlar.Add($"{uzanti}: {hata.GetType().Name} {hata.Message}"); }
        }
        Directory.Delete(klasor, true);

        Assert.True(kusurlar.Count == 0, string.Join("; ", kusurlar));
    }

    [Fact]
    public async Task Libmpv_kapakli_mp3te_etiketi_okur_kapagi_cizer_goruntu_saymaz()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-canli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var mp3 = Uret(klasor, "kapakli.mp3",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-f", "lavfi", "-i", "color=c=red:s=64x64:r=1:d=1",
            "-map", "0:a", "-map", "1:v", "-c:a", "libmp3lame", "-c:v", "mjpeg",
            "-disposition:v", "attached_pic", "-id3v2_version", "3",
            "-metadata", "title=Parca", "-metadata", "artist=Sanatci", "-metadata", "album=Album");

        using var motor = new MpvEngine();
        motor.SetProperty("ao", "null");
        await motor.OpenAsync(mp3);
        long gorulen = 0;
        (int Genislik, byte B, byte G, byte R)? kare = null;
        var bitis = DateTime.UtcNow.AddSeconds(5);
        while (kare is null && DateTime.UtcNow < bitis)
        {
            motor.TryCopyLatest(ref gorulen, (piksel, genislik, yukseklik, adim) =>
            {
                var orta = new byte[4];
                Marshal.Copy(piksel + yukseklik / 2 * adim + genislik / 2 * 4, orta, 0, 4);
                kare = (genislik, orta[0], orta[1], orta[2]);
            });
            if (kare is null) await Task.Delay(20);
        }
        var kayit = $"vid={motor.GetProperty("vid")} dwidth={motor.GetProperty("dwidth")} log: " + string.Join(" | ", motor.RecentLog);

        Assert.False(motor.HasVideo, kayit);
        Assert.True(motor.HasAudio, kayit);
        Assert.Equal(new MediaTags("Parca", "Sanatci", "Album"), motor.Tags);
        Assert.True(kare is not null, "kapak cizilmedi; " + kayit);
        Assert.Equal(64, kare!.Value.Genislik);
        Assert.True(kare.Value.R > 200 && kare.Value.G < 60 && kare.Value.B < 60, $"kapak kirmizi degil: {kare}");
        Assert.Null(motor.Details?.VideoCodec);
        motor.Dispose();
        Directory.Delete(klasor, true);
    }

    [Fact]
    public async Task Libmpv_kapaksiz_seste_ve_videoda_goruntuyu_dogru_sayar()
    {
        var klasor = Path.Combine(TestPaths.OutputRoot, "muzik-canli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var wav = Uret(klasor, "yalin.wav", "-f", "lavfi", "-i", "sine=frequency=440:duration=1");
        var mp4 = Uret(klasor, "film.mp4", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25", "-t", "1", "-pix_fmt", "yuv420p", "-c:v", "libx264");

        using (var ses = new MpvEngine())
        {
            ses.SetProperty("ao", "null");
            await ses.OpenAsync(wav);
            Assert.False(ses.HasVideo);
            Assert.Equal(MediaTags.Empty, ses.Tags);
        }

        using (var film = new MpvEngine())
        {
            film.SetProperty("ao", "null");
            await film.OpenAsync(mp4);
            Assert.True(film.HasVideo);
        }

        Directory.Delete(klasor, true);
    }
}

internal sealed class SesMotoru : IPlaybackEngine
{
    private readonly MediaTags _tags;

    internal SesMotoru(MediaTags tags) => _tags = tags;

    public string Name => "ses-motoru";

    public bool IsOpen { get; private set; }

    public double DurationSeconds => 240;

    public bool HasAudio => true;

    public bool HasVideo => false;

    public MediaTags Tags => _tags;

    public bool IsPaused { get; private set; } = true;

    public bool EndReached => false;

    public double PositionSeconds { get; set; }

    public double AudioVideoOffsetSeconds => double.NaN;

    public long FramesRendered => 0;

    public event EventHandler<PlaybackFault>? Faulted { add { } remove { } }

    public Task OpenAsync(string path, CancellationToken ct = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public void Play() => IsPaused = false;

    public void Pause() => IsPaused = true;

    public Task<SeekResult> SeekAsync(double seconds, SeekPrecision precision, CancellationToken ct = default)
    {
        PositionSeconds = seconds;
        return Task.FromResult(new SeekResult(SeekOutcome.Shown, 1));
    }

    public bool TryCopyLatest(ref long seen, FrameCopy copy) => false;

    public void Dispose() => IsOpen = false;
}

