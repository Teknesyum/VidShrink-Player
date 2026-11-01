using Avalonia;
using Avalonia.Controls;
using VidShrink.App.Playback;
using VidShrink.Core.Playback;
using VidShrink.Ffmpeg;

namespace VidShrink.Tests;

/// <summary>
/// T184 - onizleme dortlusu. Dort kabul kriterinin olcusu bu dosyada:
/// K1 ses yolu, K2 tekerlek zoom ve kararma, K3 rozetler, K4 duraklat/devam konumu.
/// </summary>
public sealed class PlaybackPanelTests : IClassFixture<PlaybackPanelTests.SesliKlipler>
{
    private readonly SesliKlipler _clips;

    public PlaybackPanelTests(SesliKlipler clips) => _clips = clips;

    /// <summary>
    /// Iki klip: biri sesli, biri sessiz. Ortak olcum parcalarindan ayri uretiliyor cunku
    /// paylasilan parcalarin hicbirinde ses akisi yok.
    /// </summary>
    public sealed class SesliKlipler : IDisposable
    {
        public string Directory { get; }
        public bool Ready { get; }

        /// <summary>320x180, 4 sn, 30 fps, 48 kHz stereo sinus.</summary>
        public string Sesli => System.IO.Path.Combine(Directory, "sesli.mp4");

        /// <summary>Ayni olcude, ses akisi hic yok.</summary>
        public string Sessiz => System.IO.Path.Combine(Directory, "sessiz.mp4");

        public SesliKlipler()
        {
            Directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "vidshrink-t184-" + Guid.NewGuid().ToString("N")[..8]);
            System.IO.Directory.CreateDirectory(Directory);
            if (!ToolLocator.IsAvailable(out _)) return;

            Ready =
                SegmentClips.Ffmpeg(new[]
                {
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=4",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=4",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-ac", "2", "-shortest", Sesli
                }) == 0
                && SegmentClips.Ffmpeg(new[]
                {
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=4",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-pix_fmt", "yuv420p",
                    "-an", Sessiz
                }) == 0;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); } catch { }
        }
    }


    /// <summary>
    /// K1: sesi olan klipte kuyu boruya takiliyor ve atlama gercekten kosuyor. Ses
    /// <b>cihazi</b> olcumun disinda: PreviewAudio.HasAudio kaynagin kendi ozelligini
    /// okuyor, sessiz makinede de ayni cevabi veriyor.
    /// </summary>
    [FfmpegFact]
    public async Task Sesli_klipte_kuyu_boruya_takiliyor()
    {
        Assert.True(_clips.Ready, "olcum klipleri uretilemedi");

        using var audio = new PreviewAudio();
        await audio.AttachAsync(_clips.Sesli);

        Assert.True(audio.Attached, "ses motoru takilmadi");
        Assert.True(audio.HasAudio, "sesli klipte ses akisi gorulmedi");

        audio.SeekTo(1.0);
        Assert.Equal(1, audio.Seeks);
    }

    /// <summary>K1'in ikinci yarisi: sessiz klipte ses cikmiyor ve hata da vermiyor.</summary>
    [FfmpegFact]
    public async Task Sessiz_klipte_ses_yolu_sessizce_geri_donuyor()
    {
        Assert.True(_clips.Ready, "olcum klipleri uretilemedi");

        using var audio = new PreviewAudio();
        await audio.AttachAsync(_clips.Sessiz);

        Assert.True(audio.Attached, "kuyu sessiz klipte de takilmali");
        Assert.False(audio.HasAudio, "sessiz klipte ses akisi bulundu");

        audio.SeekTo(1.0);
        audio.Play();
        audio.Pause();
        Assert.Equal(0, audio.Seeks);
    }


    private static readonly Size WindowSize = new(1560, 1060);

    private static T Read<T>(Func<Window, ComparisonPanel, T> read) =>
        AppHost.Run(() =>
        {
            var panel = new ComparisonPanel();
            var window = new Window { Content = panel };
            window.Measure(WindowSize);
            window.Arrange(new Rect(WindowSize));
            return read(window, panel);
        });

    private static void Relayout(Window window)
    {
        window.InvalidateMeasure();
        window.Measure(WindowSize);
        window.Arrange(new Rect(WindowSize));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void MarkFrame(ComparisonPanel panel)
    {
        typeof(ComparisonSurface)
            .GetField("_hasFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(panel.Frames, true);
    }

    /// <summary>
    /// K2: tekerlek olayi zoom degerini gercekten oynatiyor ve yuzde yuz ile yuzde iki yuz
    /// arasindaki her centik ekrandaki goruntunun boyunu degistiriyor. Eskiden bu araligin
    /// tamami oluydu: panelin bandindaki yuva yildiz olculu oldugu icin kabuk buyuyemiyor,
    /// goruntunun olcegi de sigdirma olceginde sabit duruyordu - oynayan tek sey yuzde
    /// okumasiydi. Olcu tam bunu okuyor: centik basina ContentWidth artiyor mu.
    /// </summary>
    [Fact]
    public void Tekerlek_yuzde_yuz_ile_iki_yuz_arasinda_boyu_degistiriyor()
    {
        var (start, steps, backScale, backWidth, floorStuck) = Read((window, panel) =>
        {
            panel.Frames.Configure(new PixelSize(1280, 360));
            panel.Gesture.SetViewport(640, 360);
            var first = panel.Gesture.ContentWidth;
            var seen = new List<(double Scale, double Width)>();

            for (var i = 0; i < 8 && panel.Shelter == ShelterStage.Band && panel.Gesture.PanelScale < 2.0; i++)
            {
                if (!panel.Zoom(1, new Point(0, 0))) break;
                if (panel.Shelter != ShelterStage.Band) break;
                seen.Add((panel.Gesture.PanelScale, panel.Gesture.ContentWidth));
            }

            for (var i = 0; i < 16 && panel.Gesture.PanelScale > 1.0; i++) panel.Zoom(-1, new Point(0, 0));

            var stuck = panel.Zoom(-1, new Point(0, 0));
            return (first, seen, panel.Gesture.PanelScale, panel.Gesture.ContentWidth, stuck);
        });

        Assert.Equal(640, start, 3);
        Assert.NotEmpty(steps);

        var previousScale = 1.0;
        var previousWidth = start;
        foreach (var (scale, width) in steps)
        {
            Assert.True(scale > previousScale, $"olcek {previousScale:0.###} -> {scale:0.###} takildi");
            Assert.True(width > previousWidth + 0.5,
                $"goruntu {previousWidth:0.#} -> {width:0.#} kipirdamadi ({scale:0.###}x)");
            Assert.Equal(start * scale, width, 3);
            previousScale = scale;
            previousWidth = width;
        }

        Assert.True(steps.Count >= 4, $"band araliginda yalniz {steps.Count} centik olculdu");
        Assert.True(previousScale > 1.9, $"tekerlek band tavanina varmadan durdu: {previousScale:0.###}");
        Assert.Equal(1.0, backScale, 6);
        Assert.Equal(start, backWidth, 3);
        Assert.False(floorStuck, "tabanda tekerlek hala bir sey yaptigini soyluyor");
    }

    /// <summary>
    /// K2'nin ikinci yarisi: yakinlastirma panelin olcusunu degistirince boru yeni olcuyle
    /// yeniden kuruluyor ve pano Configure ediliyordu; eldeki kare atiliyor, ekran ilk yeni
    /// kareye kadar kararan bir kutu oluyordu. Simdi eldeki kare yeni kare gelene kadar durur.
    /// </summary>
    [Fact]
    public void Yeniden_yapilandirma_eldeki_kareyi_karartmiyor()
    {
        var (before, sameSize, biggerSize) = Read((window, panel) =>
        {
            panel.Frames.Configure(new PixelSize(640, 180));
            MarkFrame(panel);
            var had = panel.Frames.HasFrame;

            panel.Frames.Configure(new PixelSize(640, 180));
            var afterSame = panel.Frames.HasFrame;

            panel.Frames.Configure(new PixelSize(800, 220));
            return (had, afterSame, panel.Frames.HasFrame);
        });

        Assert.True(before, "olcum karesi kurulamadi");
        Assert.True(sameSize, "ayni olcuye yapilandirma kareyi atti");
        Assert.True(biggerSize, "olcu degisiminde kare atildi, ekran kararir");
    }


    /// <summary>K3: eski ozur dizgesi sozlukten kalkti, iki dilde de.</summary>
    [Fact]
    public void Yaklasik_onizleme_dizgesi_kalkti()
    {
        Assert.False(Locales.Values("tr").ContainsKey("playback.approximate-preview"));
        Assert.False(Locales.Values("en").ContainsKey("playback.approximate-preview"));
    }

    /// <summary>K3: iki rozet metni de dil dosyasindan geliyor, koda gomulu degil.</summary>
    [Fact]
    public void Rozet_metinleri_iki_dilde_de_sozlukten()
    {
        Assert.Equal("Original", Locales.Values("en")["playback.badge.original"]);
        Assert.Equal("Processed", Locales.Values("en")["playback.badge.processed"]);
        Assert.Equal("Orijinal", Locales.Values("tr")["playback.badge.original"]);
        Assert.Equal("\u0130\u015flenmi\u015f", Locales.Values("tr")["playback.badge.processed"]);
    }

    /// <summary>
    /// K3: rozetler perde hareket ederken sabit durur - konumlari ayirici oynadikca
    /// degismez - ve ortulen tarafin etiketi soner.
    /// </summary>
    [Fact]
    public void Rozetler_perde_hareketinde_sabit_durur_ve_ortulen_taraf_soner()
    {
        var (leftAt, rightAt, leftAtEnd, rightAtEnd, leftHidden, rightHidden) = Read((window, panel) =>
        {
            panel.Frames.Configure(new PixelSize(640, 180));
            MarkFrame(panel);
            panel.RefreshEmptyState();
            panel.Split = 0.5;
            Relayout(window);

            var l0 = panel.LeftBadge.TranslatePoint(new Point(0, 0), panel.Stage) ?? default;
            var r0 = panel.RightBadge.TranslatePoint(new Point(0, 0), panel.Stage) ?? default;

            panel.Split = 0.8;
            Relayout(window);
            var l1 = panel.LeftBadge.TranslatePoint(new Point(0, 0), panel.Stage) ?? default;
            var r1 = panel.RightBadge.TranslatePoint(new Point(0, 0), panel.Stage) ?? default;

            panel.Split = 1.0;
            Relayout(window);
            var rightGone = !panel.RightBadge.IsVisible;

            panel.Split = 0.0;
            Relayout(window);
            var leftGone = !panel.LeftBadge.IsVisible;

            return (l0, r0, l1, r1, leftGone, rightGone);
        });

        Assert.Equal(leftAt.X, leftAtEnd.X, 3);
        Assert.Equal(leftAt.Y, leftAtEnd.Y, 3);
        Assert.Equal(rightAt.X, rightAtEnd.X, 3);
        Assert.Equal(rightAt.Y, rightAtEnd.Y, 3);
        Assert.True(rightHidden, "sag yari tamamen ortuluyken sag rozet duruyor");
        Assert.True(leftHidden, "sol yari tamamen ortuluyken sol rozet duruyor");
    }


    /// <summary>
    /// Sayili kare veren kaynak. T161'in SessizKaynak'i hic kare vermedigi icin serit
    /// konumu (ControlStrip.Position) o turda olculememisti; burada kare geliyor ve konum
    /// gercekten okunuyor.
    /// </summary>
    private sealed class SayanKaynak : IComparisonFrameSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _fps;
        private int _sequence;
        private bool _playing = true;

        internal int StartCount;
        internal int PlayCount;
        internal int PauseCount;
        internal int SeekCount;

        internal SayanKaynak(int width, int height, int fps)
        {
            _width = width;
            _height = height;
            _fps = fps;
        }

        public event EventHandler<ComparisonSourceStatus>? StatusChanged;

        public ComparisonSourceStatus Status => new(ComparisonSourceState.Oynuyor, _sequence, 0, _fps, 0, 0);

        public Task StartAsync(ComparisonFrameRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref StartCount);
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public bool TryTake(out PlaybackFrame frame)
        {
            if (!_playing)
            {
                frame = null!;
                return false;
            }

            var made = new PlaybackFrame(new byte[_width * 4 * _height]);
            made.Describe(_width, _height, _width / 2, TimeSpan.FromSeconds(_sequence / (double)_fps), _sequence);
            _sequence++;
            frame = made;
            return true;
        }

        internal readonly List<double> Speeds = new();

        public void SetSpeed(double speed) => Speeds.Add(speed);

        public void Return(PlaybackFrame frame) { }
        public void Play() { PlayCount++; _playing = true; }
        public void Pause() { PauseCount++; _playing = false; }

        public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
        {
            SeekCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// K4: durdur/baslat sonrasi serit konumu geriye dusmuyor. T161 boruyu ve kare damgasini
    /// olcmustu; olcemedigi tek sey ekrandaki konumdu ve bu olcu tam onu okuyor. Ayni turda
    /// borunun yeniden kurulmadigi da sayiliyor - bastan isleme girmenin konak karsiligi.
    /// </summary>
    [Fact]
    public void Duraklat_devam_serit_konumunu_koruyor()
    {
        var (afterPlay, whilePaused, afterResume, starts, plays, pauses, seeks, made) =
            AppHost.Run(() =>
            {
                var panel = new ComparisonPanel();
                var window = new Window { Content = panel };
                window.Measure(WindowSize);
                window.Arrange(new Rect(WindowSize));

                var built = 0;
                using var host = new PanelHost(panel, () =>
                {
                    built++;
                    return new SayanKaynak(640, 180, 30);
                });

                panel.Frames.Configure(new PixelSize(640, 180));

                var live = new SayanKaynak(640, 180, 30);
                typeof(PanelHost)
                    .GetField("_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(host, live);

                panel.Controls.Duration = TimeSpan.FromSeconds(4);
                panel.Controls.IsPlaying = true;

                for (var i = 0; i < 6; i++) host.DrainOnce();
                var played = panel.Controls.Position;

                panel.Controls.TogglePlay();
                for (var i = 0; i < 4; i++) host.DrainOnce();
                var paused = panel.Controls.Position;

                panel.Controls.TogglePlay();
                for (var i = 0; i < 3; i++) host.DrainOnce();
                var resumed = panel.Controls.Position;

                return (played, paused, resumed,
                    live.StartCount, live.PlayCount, live.PauseCount, live.SeekCount, built);
            });

        Assert.True(afterPlay > TimeSpan.Zero, $"kare akmadi, konum {afterPlay}");
        Assert.Equal(afterPlay, whilePaused);
        Assert.True(afterResume > whilePaused, $"devam sonrasi konum {afterResume}, duraklamada {whilePaused}");
        Assert.Equal(0, starts);
        Assert.Equal(0, seeks);
        Assert.Equal(1, plays);
        Assert.Equal(1, pauses);
        Assert.Equal(0, made);
    }

    /// <summary>
    /// Panel yüzeyinde basılı tutma oynatıcıdaki gibi eşikte 2x'e çıkar, hız kaynağa iner,
    /// bırakış 1x'e döndürür ve tık sayılmaz; eşikten önce oynayan basış hızlanmaz.
    /// </summary>
    [Fact]
    public void Panel_basili_tutma_kaynagi_iki_kata_cikarir_birakis_dondurur()
    {
        var sonuc = AppHost.Run(() =>
        {
            var panel = new ComparisonPanel();
            var window = new Window { Content = panel };
            window.Measure(WindowSize);
            window.Arrange(new Rect(WindowSize));
            using var host = new PanelHost(panel, () => new SayanKaynak(640, 180, 30));
            var live = new SayanKaynak(640, 180, 30);
            typeof(PanelHost)
                .GetField("_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(host, live);
            panel.Controls.IsPlaying = true;

            var saat = 10.0;
            panel.FareSaati = () => saat;
            var esik = panel.Sol.TutmaSn;
            panel.StagePress(new Point(20, 20));
            saat += esik - 0.01;
            var erken = panel.FareZaman();
            saat += 0.01;
            var gecti = panel.FareZaman();
            var tutarken = (host.Hiz, panel.HizliTutuluyor, Hizlar: string.Join(",", live.Speeds));
            saat += 2;
            var birakis = panel.StageRelease();
            var sonra = (host.Hiz, panel.HizliTutuluyor, Hizlar: string.Join(",", live.Speeds), panel.Controls.IsPlaying, live.PlayCount, live.PauseCount);

            saat += 5;
            panel.StagePress(new Point(20, 20));
            panel.StageMove(new Point(60, 20));
            saat += esik + 1;
            var suruklemede = panel.FareZaman();
            var suruklemeBirakis = panel.StageRelease();
            return (erken, gecti, tutarken, birakis, sonra, suruklemede, suruklemeBirakis, Son: string.Join(",", live.Speeds));
        });

        Assert.False(sonuc.erken);
        Assert.True(sonuc.gecti);
        Assert.Equal(Keymap.HoldSpeed, sonuc.tutarken.Hiz);
        Assert.True(sonuc.tutarken.HizliTutuluyor);
        Assert.Equal("2", sonuc.tutarken.Hizlar);
        Assert.Equal(SolTikSonucu.TutmaBitti, sonuc.birakis);
        Assert.Equal(1, sonuc.sonra.Hiz);
        Assert.False(sonuc.sonra.HizliTutuluyor);
        Assert.Equal("2,1", sonuc.sonra.Hizlar);
        Assert.True(sonuc.sonra.IsPlaying, "tutma bırakışı tık sayıldı");
        Assert.Equal(0, sonuc.sonra.PlayCount);
        Assert.Equal(0, sonuc.sonra.PauseCount);
        Assert.False(sonuc.suruklemede);
        Assert.Equal(SolTikSonucu.Surukleme, sonuc.suruklemeBirakis);
        Assert.Equal("2,1", sonuc.Son);
    }
}
