using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VidShrink.App;
using VidShrink.App.Localization;
using VidShrink.App.Playback;
using VidShrink.Ffmpeg;
using Xunit;

namespace VidShrink.Tests;

internal static class GirdiKanit
{
    internal static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VidShrink.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? AppContext.BaseDirectory;
        }
    }

    internal static string Folder
    {
        get
        {
            var path = Path.Combine(Root, ".calisma", "T176");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static void Write(string name, string body)
        => File.WriteAllText(Path.Combine(Folder, name), body, new UTF8Encoding(false));

    /// <summary>
    /// Son asertten sonra çağrılır: yeşil koşum kendi bıraktığını siler, kırmızı koşum
    /// kanıtını korur çünkü düşen asert buraya hiç gelmez. Klasör boşalınca o da gider.
    /// </summary>
    internal static void Kapat(params string[] adlar) => KanitKapanisi.Kapat(Path.Combine(Root, ".calisma", "T176"), adlar);
}

internal static class GirdiSurucu
{
    private static readonly Pointer Fare = new(1, PointerType.Mouse, true);

    internal static void Wheel(PlayerView view, double notches, KeyModifiers modifiers)
    {
        var args = new PointerWheelEventArgs(
            view,
            Fare,
            view,
            new Point(1, 1),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            modifiers,
            new Vector(0, notches))
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent
        };

        view.RaiseEvent(args);
    }

    internal static void Press(PlayerView view, PointerUpdateKind kind, RawInputModifiers buttons, int clicks = 1)
    {
        var args = new PointerPressedEventArgs(
            view,
            Fare,
            view,
            new Point(1, 1),
            0,
            new PointerPointProperties(buttons, kind),
            KeyModifiers.None,
            clicks)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        };

        view.RaiseEvent(args);
    }

    internal static void Key(PlayerView view, Key key, KeyModifiers modifiers = KeyModifiers.None, string? symbol = null)
        => view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            KeySymbol = symbol
        });

    internal static PlayerView Kur(out Window window)
    {
        var view = new PlayerView();
        var host = new Window { Width = 640, Height = 480, Content = view };
        window = host;
        return view;
    }
}

public sealed class OynaticiGirdiTests
{
    [Fact]
    public void DokuzGirdininDokuzuIcinOncesiSonrasiOlculur()
    {
        var satirlar = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            view.Seek.Duration = 100000;
            var rows = new List<string>();

            void Tekerlek(string ad, KeyModifiers mods)
            {
                var before = view.PositionSeconds;
                GirdiSurucu.Wheel(view, 1, mods);
                rows.Add(FormattableString.Invariant($"{ad}\tkonum {before:0.###} -> {view.PositionSeconds:0.###} sn"));
            }

            var sesOnce = view.VolumeLevel;
            var konumOnce = view.PositionSeconds;
            GirdiSurucu.Wheel(view, 1, KeyModifiers.None);
            rows.Add($"tekerlek\tses {sesOnce:0.###} -> {view.VolumeLevel:0.###} (konum {konumOnce:0.###} -> {view.PositionSeconds:0.###} sn)");

            Tekerlek("ctrl+tekerlek", KeyModifiers.Control);
            Tekerlek("shift+tekerlek", KeyModifiers.Shift);
            Tekerlek("ctrl+shift+tekerlek", KeyModifiers.Control | KeyModifiers.Shift);

            var zoomBefore = view.ZoomScale;
            var posBeforeZoom = view.PositionSeconds;
            GirdiSurucu.Wheel(view, 1, KeyModifiers.Alt);
            rows.Add(FormattableString.Invariant($"alt+tekerlek\tyakinlastirma {zoomBefore:0.###} -> {view.ZoomScale:0.###} (konum {posBeforeZoom:0.###} -> {view.PositionSeconds:0.###} sn)"));

            var playBefore = view.IsPlaying;
            GirdiSurucu.Press(view, PointerUpdateKind.RightButtonPressed, RawInputModifiers.RightMouseButton);
            rows.Add($"sag tik\tiz {view.Trace[^1]} (oynatma {playBefore} -> {view.IsPlaying})");

            var playBefore2 = view.IsPlaying;
            GirdiSurucu.Key(view, Avalonia.Input.Key.Space);
            rows.Add($"bosluk\toynatma {playBefore2} -> {view.IsPlaying}");

            var fsBefore = view.Fullscreen.IsFullscreen;
            GirdiSurucu.Press(view, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
            rows.Add($"orta tik\ttam ekran {fsBefore} -> {view.Fullscreen.IsFullscreen}");

            var snapshot = FormattableString.Invariant($"{view.PositionSeconds:0.###}|{view.ZoomScale:0.###}|{view.IsPlaying}|{view.Fullscreen.IsFullscreen}");
            var clicksBefore = view.LeftClicks;
            GirdiSurucu.Press(view, PointerUpdateKind.LeftButtonPressed, RawInputModifiers.LeftMouseButton);
            var after = FormattableString.Invariant($"{view.PositionSeconds:0.###}|{view.ZoomScale:0.###}|{view.IsPlaying}|{view.Fullscreen.IsFullscreen}");
            rows.Add($"sol tik\tdurum {snapshot} -> {after} (tiklama sayaci {clicksBefore} -> {view.LeftClicks})");

            Assert.Equal(snapshot, after);
            window.Close();
            return rows;
        });

        GirdiKanit.Write("k1-izgara.txt", string.Join(Environment.NewLine, satirlar) + Environment.NewLine);

        Assert.Equal(9, satirlar.Count);
        Assert.Contains("ses 100 -> 100 (konum 0 -> 1 sn)", satirlar[0]);
        Assert.Contains("konum 1 -> 1.1 sn", satirlar[1]);
        Assert.Contains("konum 1.1 -> 11.1 sn", satirlar[2]);
        Assert.Contains("konum 11.1 -> 111.1 sn", satirlar[3]);
        Assert.Contains("yakinlastirma 1 -> 1.24", satirlar[4]);
        Assert.Contains("konum 111.1 -> 111.1 sn", satirlar[4]);
        Assert.Contains("iz menu (oynatma False -> False)", satirlar[5]);
        Assert.Contains("oynatma False -> True", satirlar[6]);
        Assert.Contains("tam ekran False -> True", satirlar[7]);
        Assert.Contains("tiklama sayaci 0 -> 1", satirlar[8]);
        GirdiKanit.Kapat("k1-izgara.txt");
    }

    [Fact]
    public void TekerlekAdimlariHaritadakiDortSayidir()
    {
        Assert.Equal(new PlayerCommand(PlayerCommandKind.Seek, 1), Keymap.ForWheel(1, KeyModifiers.None));
        Assert.Equal(new PlayerCommand(PlayerCommandKind.Seek, -1), Keymap.ForWheel(-1, KeyModifiers.None));
        Assert.Equal(new PlayerCommand(PlayerCommandKind.Seek, 0.1), Keymap.ForWheel(1, KeyModifiers.Control));
        Assert.Equal(new PlayerCommand(PlayerCommandKind.Seek, 10), Keymap.ForWheel(1, KeyModifiers.Shift));
        Assert.Equal(new PlayerCommand(PlayerCommandKind.Seek, -100), Keymap.ForWheel(-1, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(PlayerCommandKind.Zoom, Keymap.ForWheel(1, KeyModifiers.Alt).Kind);
        Assert.Equal(PlayerCommandKind.TogglePlay, Keymap.ForPress(PlayerButton.Left).Kind);
        Assert.Equal(PlayerCommandKind.ContextMenu, Keymap.ForPress(PlayerButton.Right).Kind);
        Assert.Equal(PlayerCommandKind.CompactOrFullscreen, Keymap.ForPress(PlayerButton.Middle).Kind);
        Assert.Equal(PlayerCommandKind.ToggleFullscreen, Keymap.ForKey(Key.F, KeyModifiers.None, "f").Kind);
        Assert.Equal(PlayerCommandKind.LeaveFullscreen, Keymap.ForKey(Key.Escape, KeyModifiers.None, null).Kind);
        Assert.Equal(PlayerCommandKind.TogglePlay, Keymap.ForKey(Key.Space, KeyModifiers.None, " ").Kind);
        Assert.Equal(PlayerCommandKind.Volume, Keymap.ForKey(Key.Up, KeyModifiers.None, null).Kind);
    }

    [Fact]
    public async Task OnHizliTikTekHedefteBirikirVeOnAramaAcmaz()
    {
        var calls = new List<double>();
        var gate = new SemaphoreSlim(0);
        var coalescer = new SeekCoalescer(async at =>
        {
            lock (calls) calls.Add(at);
            await gate.WaitAsync();
        })
        { Duration = 1000 };

        for (var i = 0; i < 10; i++) coalescer.Nudge(Keymap.SeekFine);

        Assert.Equal(10, coalescer.Target);
        lock (calls) Assert.Single(calls);

        gate.Release();
        await Task.Delay(50);
        gate.Release();
        await coalescer.Idle;

        var body = new StringBuilder();
        body.AppendLine("10 hizli tik, tik basi 1 sn");
        body.AppendLine($"hedef konum: {coalescer.Target} sn");
        body.AppendLine($"tetiklenen arama sayisi: {coalescer.SeekCalls}");
        body.AppendLine("arama hedefleri: " + string.Join(", ", coalescer.IssuedTargets));
        GirdiKanit.Write("k3-birikme.txt", body.ToString());

        Assert.Equal(10, coalescer.Target);
        Assert.True(coalescer.SeekCalls < 10, $"birikme yok: {coalescer.SeekCalls} arama");
        Assert.Equal(10, coalescer.IssuedTargets[^1]);
        GirdiKanit.Kapat("k3-birikme.txt");
    }

    [Fact]
    public void OrtaTikTamEkranaGecerIkincisiOncekiHaleDoner()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            view.CurrentTabIndex = () => 2;
            view.PlayerTabIndex = () => 5;
            var selected = new List<int>();
            view.SelectTab = index => selected.Add(index);

            var before = new WindowSnapshot(0, 40, 60, 900, 700, 2);
            var toFull = view.Fullscreen.Toggle(before, (int)WindowState.FullScreen, 5);
            var back = view.Fullscreen.Toggle(toFull, (int)WindowState.FullScreen, 5);

            var body = new StringBuilder();
            body.AppendLine($"once      : durum {before.State} dikdortgen {before.X},{before.Y} {before.Width}x{before.Height} sekme {before.TabIndex}");
            body.AppendLine($"tam ekran : durum {toFull.State} dikdortgen {toFull.X},{toFull.Y} {toFull.Width}x{toFull.Height} sekme {toFull.TabIndex}");
            body.AppendLine($"geri donus: durum {back.State} dikdortgen {back.X},{back.Y} {back.Width}x{back.Height} sekme {back.TabIndex}");

            Assert.Equal((int)WindowState.FullScreen, toFull.State);
            Assert.Equal(5, toFull.TabIndex);
            Assert.Equal(before, back);

            var window = new Window { Width = 640, Height = 480, Content = view };
            view.CurrentTabIndex = () => 2;
            GirdiSurucu.Press(view, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
            var fsPos = view.PositionSeconds;
            GirdiSurucu.Wheel(view, 1, KeyModifiers.Control);
            GirdiSurucu.Key(view, Avalonia.Input.Key.Space);
            body.AppendLine($"tam ekranda ctrl+tekerlek: konum {fsPos:0.###} -> {view.PositionSeconds:0.###} sn, bosluk: oynatma {view.IsPlaying}");
            Assert.True(view.Fullscreen.IsFullscreen);
            Assert.Equal(fsPos + Keymap.WheelSeekStep / Keymap.WheelFactor, view.PositionSeconds, 9);
            Assert.True(view.IsPlaying);

            GirdiSurucu.Press(view, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
            body.AppendLine($"ikinci orta tik: tam ekran {view.Fullscreen.IsFullscreen}, secilen sekmeler {string.Join(",", selected)}");
            Assert.False(view.Fullscreen.IsFullscreen);

            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k4-tam-ekran.txt", rapor);
        Assert.Contains("geri donus", rapor);
        GirdiKanit.Kapat("k4-tam-ekran.txt");
    }

    [Fact]
    public void MenuDugmesiBaglamMenusunuAcarVeKeymapSatirlariniTasir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 640, Height = 480, Content = view };

            var body = new StringBuilder();
            foreach (var dil in new[] { "en", "tr" })
            {
                Strings.Use(dil);
                var menu = view.BuildMenu();
                var eylemSatirlari = OynaticiGirdiTestsMenuSatirlari.EylemSatirlari(menu).ToList();
                var basliklar = eylemSatirlari.Select(item => item.Header?.ToString() ?? "").ToList();
                var ekler = menu.Items.OfType<MenuItem>().Where(item => item.Tag is not PlayerAction).Select(item => item.Header?.ToString() ?? "").ToList();
                body.AppendLine($"{dil}: {string.Join(" | ", basliklar)} || {string.Join(" | ", ekler)}");
                Assert.Equal(Keymap.MenuActions, eylemSatirlari.Select(item => (PlayerAction)item.Tag!));
                Assert.All(basliklar, baslik => Assert.False(string.IsNullOrWhiteSpace(baslik)));
                Assert.Equal(OynaticiGirdiTestsMenuSatirlari.EkBasliklar(), ekler);
            }

            Strings.Use("en");
            view.Apply(Keymap.OpenMenu.ToCommand());
            body.AppendLine($"menu izi: {view.Trace[^1]}");
            Assert.Equal("menu", view.Trace[^1]);

            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k5-menu.txt", rapor);
        GirdiKanit.Kapat("k5-menu.txt");
    }

    [Fact]
    public void KabukYolununActigiSekmeOynaticidir()
    {
        var rapor = AppHost.Run(() =>
        {
            var klip = Path.Combine(GirdiKanit.Folder, "kabuk-ornek.mp4");
            if (!File.Exists(klip)) File.WriteAllBytes(klip, new byte[] { 0, 1, 2, 3 });

            var argv = new[] { klip };
            var cozulen = VidShrink.Core.ShellIntegration.ResolveStartupPath(argv);
            Assert.Equal(klip, cozulen);
            Assert.Null(Program.StartupFor(argv));

            var window = new MainWindow(cozulen);
            var oynaticiIndex = window.PlayerTabIndex;
            var basta = window.Tabs.SelectedIndex;
            var yukleme = window.LoadStartupFileAsync();
            var sonra = window.Tabs.SelectedIndex;
            var baslik = MainWindow.TabHeaderText((TabItem)window.Tabs.Items[oynaticiIndex]!);
            _ = yukleme.ContinueWith(_ => { }, TaskScheduler.Default);
            Assert.Equal(oynaticiIndex, basta);
            Assert.Equal(oynaticiIndex, sonra);
            window.Close();

            return $"argv: {klip}{Environment.NewLine}"
                 + $"ResolveStartupPath: {cozulen}{Environment.NewLine}"
                 + $"oynatici sekme sirasi: {oynaticiIndex}{Environment.NewLine}"
                 + $"acilistaki sekme: {basta} -> {sonra}{Environment.NewLine}"
                 + $"sekme basligi: {baslik}{Environment.NewLine}";
        });

        GirdiKanit.Write("k6-kabuk.txt", rapor);
        Assert.Contains("sekme basligi:", rapor);
        GirdiKanit.Kapat("k6-kabuk.txt", "kabuk-ornek.mp4");
    }
}

public sealed class PlayerTabTests
{
    [Fact]
    public void OynaticiSekmesiSekmeSeridindeVeIkiDildeBasligiVar()
    {
        var rapor = AppHost.Run(() =>
        {
            var window = new MainWindow();
            var index = window.PlayerTabIndex;
            var tab = (TabItem)window.Tabs.Items[index]!;
            var body = new StringBuilder();
            body.AppendLine($"sekme sayisi: {window.Tabs.ItemCount}");
            body.AppendLine($"oynatici sirasi: {index}");

            foreach (var dil in new[] { "en", "tr" })
            {
                Strings.Use(dil);
                var beklenen = Strings.Get("main.tab.player");
                var goruldu = MainWindow.TabHeaderText(tab);
                body.AppendLine($"{dil}: beklenen '{beklenen}' goruldu '{goruldu}'");
                Assert.Equal(beklenen, goruldu);
            }

            Strings.Use("en");
            var digerleri = window.Tabs.Items.OfType<TabItem>().Select(item => item.Theme?.ToString() ?? "yok").Distinct().ToList();
            body.AppendLine("sekme temalari: " + string.Join(" | ", digerleri));
            Assert.Single(digerleri);
            Assert.IsType<PlayerView>(tab.Content);

            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k1-sekme.txt", rapor);
        Assert.Contains("oynatici sirasi:", rapor);
        GirdiKanit.Kapat("k1-sekme.txt");
    }
}

public sealed class GirdiKlipFixture : IAsyncLifetime
{
    public const int UretimTavaniMs = 60_000;

    public string? ClipPath { get; private set; }

    public async Task InitializeAsync()
    {
        if (!ToolLocator.IsAvailable(out _)) return;

        var path = Path.Combine(GirdiKanit.Folder, "girdi-20sn.mkv");
        ClipPath = path;
        if (File.Exists(path)) return;

        await UretAsync(path, UretimTavaniMs);
    }

    public static async Task<int> UretAsync(string path, int timeoutMs)
    {
        var psi = new ProcessStartInfo(ToolLocator.Ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[]
        {
            "-y", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=30:duration=20",
            "-force_key_frames", "expr:gte(t,n_forced*1)",
            "-pix_fmt", "yuv420p", "-c:v", "libx264", "-preset", "ultrafast",
            "-an", path
        }) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException(
                $"klip uretimi {timeoutMs} ms icinde bitmedi: {Path.GetFileName(path)}");
        }

        await stdout;
        return (await stderr).Length;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class OynaticiGirdiTestsKlipUretimi
{
    [FfmpegAvailableFact]
    public async Task Klip_uretimi_stderr_borusu_dolsa_da_zamaninda_biter()
    {
        var yol = Path.Combine(GirdiKanit.Folder, "kalip-kanit-20sn.mkv");
        if (File.Exists(yol)) File.Delete(yol);

        var saat = Stopwatch.StartNew();
        var stderrBayt = await GirdiKlipFixture.UretAsync(yol, 30_000);
        saat.Stop();

        var body = new StringBuilder();
        body.AppendLine($"klip: {Path.GetFileName(yol)}");
        body.AppendLine($"uretim suresi: {saat.Elapsed.TotalMilliseconds:0.#} ms");
        body.AppendLine($"stderr bayt sayisi: {stderrBayt}");
        body.AppendLine("windows anonim boru tamponu: 4096 bayt");
        GirdiKanit.Write("k4-klip-uretimi.txt", body.ToString());

        Assert.True(File.Exists(yol), "klip uretilmedi");
        Assert.True(
            stderrBayt > 4096,
            $"stderr yalnizca {stderrBayt} bayt; boru tamponu dolmadan bu test kusuru yakalayamaz");
        Assert.True(
            saat.Elapsed.TotalSeconds < 30,
            $"klip uretimi {saat.Elapsed.TotalSeconds:0.#} sn surdu");
        GirdiKanit.Kapat("k4-klip-uretimi.txt", "kalip-kanit-20sn.mkv");
    }
}

public sealed class FfmpegAvailableFactAttribute : FactAttribute
{
    public FfmpegAvailableFactAttribute()
    {
        if (!ToolLocator.IsAvailable(out var missing))
            Skip = $"{missing} bulunamadi, ffmpeg isteyen test atlandi.";
    }
}

public sealed class OynaticiGirdiTestsMenuSatirlari
{
    internal static IEnumerable<MenuItem> Hepsi(IEnumerable<object?> ogeler)
        => ogeler.OfType<MenuItem>().SelectMany(item => (ReferenceEquals(item.Tag, Keymap.Settings) ? Enumerable.Empty<MenuItem>() : Hepsi(item.Items)).Prepend(item));

    internal static IEnumerable<MenuItem> EylemSatirlari(MenuFlyout menu)
        => Hepsi(menu.Items).Where(item => item.Tag is PlayerAction eylem && Keymap.MenuActions.Contains(eylem));

    internal static string[] EkBasliklar() => new[]
    {
        Strings.Get("player.menu.reveal"),
        Strings.Get(PlayerView.NewWindowKey),
        Strings.Get("player.tracks.audio"),
        Strings.Get("player.subtitle.menu"),
        Strings.Get("player.menu.view"),
        Strings.Get("player.menu.playback"),
        Strings.Get("player.menu.loop"),
        Strings.Get("main.player.menu.chapters"),
        Strings.Get("player.tools.menu"),
        Strings.Get("player.menu.open-folder"),
        Strings.Get("player.list.recent"),
        Strings.Get("player.favorites.title"),
        Strings.Get("player.playlists.menu")
    };

    internal static string[] UstSatirlar() => new[]
    {
        Strings.Get(Keymap.PlayPause.LabelKey),
        Strings.Get(Keymap.Fullscreen.LabelKey),
        Strings.Get("player.menu.reveal"),
        Strings.Get(PlayerView.NewWindowKey),
        "-",
        Strings.Get("player.tracks.audio"),
        Strings.Get("player.subtitle.menu"),
        Strings.Get("player.menu.view"),
        Strings.Get("player.menu.playback"),
        Strings.Get("player.menu.loop"),
        Strings.Get("main.player.menu.chapters"),
        "-",
        Strings.Get(Keymap.Screenshot.LabelKey),
        Strings.Get(Keymap.CopyFrame.LabelKey),
        Strings.Get("player.tools.menu"),
        Strings.Get("player.menu.open-folder"),
        Strings.Get("player.list.recent"),
        Strings.Get("player.favorites.title"),
        Strings.Get("player.playlists.menu"),
        Strings.Get(Keymap.Settings.LabelKey)
    };

    private static MenuItem Satir(PlayerView view, PlayerAction eylem)
        => Hepsi(view.BuildMenu().Items).First(item => ReferenceEquals(item.Tag, eylem));

    [Fact]
    public void UstMenuOnSekizSatirVeIkiAyiriciTasir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 640, Height = 480, Content = view };
            var body = new StringBuilder();

            foreach (var dil in new[] { "en", "tr" })
            {
                Strings.Use(dil);
                var menu = view.BuildMenu();
                var ust = menu.Items.Select(item => item is Separator ? "-" : (item as MenuItem)?.Header?.ToString() ?? "").ToList();
                body.AppendLine($"[{dil}] {string.Join(" | ", ust)}");
                Assert.Equal(UstSatirlar(), ust);
                Assert.Equal(18, menu.Items.OfType<MenuItem>().Count());
                Assert.Equal(2, menu.Items.OfType<Separator>().Count());
                var tumu = Hepsi(menu.Items).ToList();
                Assert.DoesNotContain(tumu, item => ReferenceEquals(item.Tag, Keymap.Stop));
                Assert.DoesNotContain(tumu, item => ReferenceEquals(item.Tag, Keymap.GoToStart));
                Assert.DoesNotContain(tumu, item => ReferenceEquals(item.Tag, Keymap.Mute));
                Assert.Single(tumu, item => ReferenceEquals(item.Tag, Keymap.NormalSpeed));
            }

            Strings.Use("en");
            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k9-menu-ust.txt", rapor);
        GirdiKanit.Kapat("k9-menu-ust.txt");
    }

    [Fact]
    public void DosyaKonumunuAcWindowsdaDosyayiSeciliAcar()
    {
        var yol = Path.Combine(Path.GetTempPath(), "vidshrink konum.mp4");
        var windows = PlayerView.RevealStart(yol, windows: true, mac: false);
        Assert.Equal("explorer.exe", windows.FileName);
        Assert.False(windows.UseShellExecute);
        Assert.Equal(new[] { "/select,", yol }, windows.ArgumentList);

        var mac = PlayerView.RevealStart(yol, windows: false, mac: true);
        Assert.Equal("open", mac.FileName);
        Assert.Equal(new[] { Path.GetDirectoryName(yol) }, mac.ArgumentList);

        var linux = PlayerView.RevealStart(yol, windows: false, mac: false);
        Assert.Equal("xdg-open", linux.FileName);
        Assert.Equal(new[] { Path.GetDirectoryName(yol) }, linux.ArgumentList);
    }

    [Fact]
    public void DosyaKonumunuAcDosyaYokkenKapaliVarkenCalisir()
    {
        var klasor = Path.Combine(GirdiKanit.Root, ".calisma", "menu-konum");
        Directory.CreateDirectory(klasor);
        var dosya = Path.Combine(klasor, "konum.mp4");
        File.WriteAllBytes(dosya, new byte[] { 0 });
        try
        {
            var sonuc = AppHost.Run(() =>
            {
                var view = new PlayerView();
                var window = new Window { Width = 640, Height = 480, Content = view };
                var cagrilar = new List<ProcessStartInfo>();
                view.RevealLauncher = start => cagrilar.Add(start);
                MenuItem Konum() => view.BuildMenu().Items.OfType<MenuItem>().Single(item => (string?)item.Header == Strings.Get("player.menu.reveal"));
                var alan = typeof(PlayerView).GetField("_path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

                var bos = Konum().IsEnabled;
                var bosCagri = view.RevealFile();
                alan.SetValue(view, Path.Combine(klasor, "yok.mp4"));
                var yokKapali = Konum().IsEnabled;
                alan.SetValue(view, dosya);
                var satir = Konum();
                var acik = satir.IsEnabled;
                satir.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent) { Source = satir });

                window.Close();
                return (bos, bosCagri, yokKapali, acik, cagrilar);
            });

            Assert.False(sonuc.bos);
            Assert.False(sonuc.bosCagri);
            Assert.False(sonuc.yokKapali);
            Assert.True(sonuc.acik);
            var cagri = Assert.Single(sonuc.cagrilar);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("explorer.exe", cagri.FileName);
                Assert.Equal(new[] { "/select,", Path.GetFullPath(dosya) }, cagri.ArgumentList);
            }
            else Assert.Equal(new[] { Path.GetFullPath(klasor) }, cagri.ArgumentList);
        }
        finally
        {
            Directory.Delete(klasor, true);
        }
    }

    [Fact]
    public void YeniMenuAnahtarlariHerDildeCevrilmis()
    {
        var anahtarlar = new[] { "player.menu.reveal", "player.menu.view", "player.menu.playback", "player.menu.loop" };
        var kok = Path.Combine(GirdiKanit.Root, "src", "VidShrink.App", "Locales");
        var diller = Directory.GetDirectories(kok).Where(d => File.Exists(Path.Combine(d, "playback.json"))).ToList();
        var ingilizce = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(kok, "en", "playback.json"))).RootElement;
        Assert.True(diller.Count > 40, $"dil sayisi {diller.Count}");
        foreach (var klasor in diller)
        {
            var dil = Path.GetFileName(klasor);
            using var belge = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(klasor, "playback.json")));
            foreach (var anahtar in anahtarlar)
            {
                Assert.True(belge.RootElement.TryGetProperty(anahtar, out var deger), $"{dil}: {anahtar} yok");
                Assert.False(string.IsNullOrWhiteSpace(deger.GetString()), $"{dil}: {anahtar} bos");
                if (dil != "en") Assert.NotEqual(ingilizce.GetProperty(anahtar).GetString(), deger.GetString());
            }
        }
    }

    private static void Tikla(MenuItem item)
        => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent) { Source = item });

    [Fact]
    public void MenuSatiriOynatDuraklatOynatmayiCevirir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 640, Height = 480, Content = view };

            var once = view.IsPlaying;
            var item = Satir(view, Keymap.PlayPause);
            Tikla(item);
            var sonra = view.IsPlaying;
            Tikla(Satir(view, Keymap.PlayPause));
            var geri = view.IsPlaying;

            var body = $"satir 1 basligi: {item.Header}{Environment.NewLine}"
                     + $"oynatma {once} -> {sonra} -> {geri}{Environment.NewLine}"
                     + $"iz: {string.Join(" | ", view.Trace)}{Environment.NewLine}";

            Assert.False(once);
            Assert.True(sonra);
            Assert.False(geri);
            Assert.Equal(new[] { "play -> True", "play -> False" }, view.Trace);

            window.Close();
            return body;
        });

        GirdiKanit.Write("k9-menu-oynat.txt", rapor);
        GirdiKanit.Kapat("k9-menu-oynat.txt");
    }

    [Fact]
    public void MenuSatiriTamEkranIkiYondeCalisir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            view.CurrentTabIndex = () => 2;
            view.PlayerTabIndex = () => 5;
            var secilen = new List<int>();
            view.SelectTab = index => secilen.Add(index);
            var window = new Window { Width = 640, Height = 480, Content = view };

            var once = view.Fullscreen.IsFullscreen;
            var item = Satir(view, Keymap.Fullscreen);
            Tikla(item);
            var acik = view.Fullscreen.IsFullscreen;
            Tikla(Satir(view, Keymap.Fullscreen));
            var kapali = view.Fullscreen.IsFullscreen;

            var body = $"satir 2 basligi: {item.Header}{Environment.NewLine}"
                     + $"tam ekran {once} -> {acik} -> {kapali}{Environment.NewLine}"
                     + $"secilen sekmeler: {string.Join(",", secilen)}{Environment.NewLine}"
                     + $"iz: {string.Join(" | ", view.Trace)}{Environment.NewLine}";

            Assert.False(once);
            Assert.True(acik);
            Assert.False(kapali);
            Assert.Empty(secilen);

            window.Close();
            return body;
        });

        GirdiKanit.Write("k9-menu-tam-ekran.txt", rapor);
        GirdiKanit.Kapat("k9-menu-tam-ekran.txt");
    }

    [Fact]
    public void MenuSatiriYakinlastirmayiSifirlar()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 640, Height = 480, Content = view };

            GirdiSurucu.Wheel(view, 3, KeyModifiers.Alt);
            var buyutulmus = view.ZoomScale;
            var item = Satir(view, Keymap.ResetZoom);
            Tikla(item);
            var sifirlanmis = view.ZoomScale;

            var body = FormattableString.Invariant(
                $"satir 3 basligi: {item.Header}{Environment.NewLine}yakinlastirma 1 -> {buyutulmus:0.###} -> {sifirlanmis:0.###}{Environment.NewLine}iz: {string.Join(" | ", view.Trace)}{Environment.NewLine}");

            Assert.True(buyutulmus > 1, $"alt+tekerlek buyutmedi: {buyutulmus}");
            Assert.Equal(1, sifirlanmis);
            Assert.Equal("zoomreset -> 1", view.Trace[^1]);

            window.Close();
            return body;
        });

        GirdiKanit.Write("k9-menu-sifirla.txt", rapor);
        GirdiKanit.Kapat("k9-menu-sifirla.txt");
    }

    [Fact]
    public void HerMenuSatiriAyriBirEtkiUretir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 640, Height = 480, Content = view };
            var menu = view.BuildMenu();
            var ogeler = menu.Items.OfType<MenuItem>().ToList();
            var satirlar = EylemSatirlari(menu).ToList();
            var ekler = ogeler.Where(item => item.Tag is not PlayerAction).Select(item => item.Header?.ToString() ?? "").ToList();
            Assert.Equal(EkBasliklar(), ekler);
            var parcaSatirlari = ogeler.Where(item => item.Tag is null)
                .SelectMany(altMenu => altMenu.Items.OfType<MenuItem>())
                .Where(item => item.Tag is PlayerAction eylem && !Keymap.MenuActions.Contains(eylem))
                .ToList();
            Assert.Equal(
                Keymap.Rows.Count(row => row.Action.Command
                    is PlayerCommandKind.AudioCycle or PlayerCommandKind.SubtitleCycle
                    or PlayerCommandKind.SubtitleDelay or PlayerCommandKind.AudioDelay
                    or PlayerCommandKind.ClipExport or PlayerCommandKind.GifExport
                    or PlayerCommandKind.MiniMode or PlayerCommandKind.OpenUrl or PlayerCommandKind.PasteUrl)
                + KeymapTests.YalnizAraclarMenusunde.Length,
                parcaSatirlari.Count);
            var body = new StringBuilder();

            foreach (var (item, sira) in satirlar.Concat(parcaSatirlari).Select((item, sira) => (item, sira)))
            {
                var oncekiIz = view.Trace.Count;
                if (ReferenceEquals(item.Tag, Keymap.Settings))
                {
                    var altlar = item.Items.OfType<MenuItem>().Select(child => child.Header?.ToString()).ToList();
                    body.AppendLine($"satir {sira} '{item.Header}' -> alt menu: {string.Join(" | ", altlar)}");
                    Assert.Contains(Strings.Get("settings.player-shortcuts.title"), altlar);
                    continue;
                }

                Tikla(item.Items.OfType<MenuItem>().FirstOrDefault(child => ReferenceEquals(child.Tag, item.Tag)) ?? item);
                var uretilen = view.Trace.Skip(oncekiIz).ToList();
                body.AppendLine($"satir {sira} '{item.Header}' -> {(uretilen.Count == 0 ? "ETKI YOK" : string.Join(" | ", uretilen))}");
                Assert.NotEmpty(uretilen);
                Assert.DoesNotContain("none", uretilen);
            }

            body.AppendLine($"satir sayisi: {satirlar.Count}");
            Assert.Equal(Keymap.MenuActions.Count, satirlar.Count);

            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k9-menu-satirlari.txt", rapor);
        Assert.Contains($"satir sayisi: {Keymap.MenuActions.Count}", rapor);
        GirdiKanit.Kapat("k9-menu-satirlari.txt");
    }

    /// <summary>
    /// Olcu eskiden <c>Echo</c>'nun <c>[Conditional("DEBUG")]</c> ile urun ikilisinden
    /// dustugunu pimliyordu. T176 turu bitti ve iskele tamamen kaldirildi (kod borcu 16);
    /// iddia artik daha guclu: yontem hic yok. Yansimayla okunur, cunku geri gelmesi
    /// derlemeyi kirmaz — sessizce eski iskeleyi geri getirirdi.
    /// </summary>
    [Fact]
    public void OlcumKancasiUrunIkilisindeDerlenmez()
    {
        const System.Reflection.BindingFlags hepsi =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        var yontem = typeof(PlayerView).GetMethod("Echo", hepsi);
        var alanlar = typeof(PlayerView).GetProperties(hepsi).Select(p => p.Name).ToList();

        GirdiKanit.Write(
            "k13-olcum-kancasi.txt",
            $"PlayerView.Echo duruyor mu: {yontem is not null}{Environment.NewLine}"
            + $"OpenedMenu ozelligi duruyor mu: {alanlar.Contains("OpenedMenu")}{Environment.NewLine}");

        Assert.Null(yontem);
        Assert.NotNull(typeof(PlayerView).GetProperty("Trace", hepsi));
        Assert.DoesNotContain("OpenedMenu", alanlar);
        GirdiKanit.Kapat("k13-olcum-kancasi.txt");
    }
}

public sealed class OynaticiGirdiTestsPencereYazma
{
    [Fact]
    public void TamEkranGercekPencereDikdortgeniniGeriYazar()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var window = new Window { Width = 900, Height = 700, Content = view };
            window.Show();
            window.Position = new PixelPoint(140, 90);
            window.Width = 900;
            window.Height = 700;

            string Oku(string ad) => FormattableString.Invariant(
                $"{ad}: durum {(int)window.WindowState} dikdortgen {window.Position.X},{window.Position.Y} {window.Width:0.###}x{window.Height:0.###}");

            var body = new StringBuilder();
            body.AppendLine(Oku("once     "));
            var once = ((int)window.WindowState, window.Position.X, window.Position.Y, window.Width, window.Height);

            GirdiSurucu.Key(view, Avalonia.Input.Key.F, KeyModifiers.None, "f");
            body.AppendLine(Oku("tam ekran"));
            var tamEkranDurumu = (int)window.WindowState;

            window.WindowState = WindowState.Normal;
            window.Position = new PixelPoint(500, 480);
            window.Width = 400;
            window.Height = 300;
            body.AppendLine(Oku("kaydirildi"));

            GirdiSurucu.Key(view, Avalonia.Input.Key.F, KeyModifiers.None, "f");
            body.AppendLine(Oku("geri     "));
            var geri = ((int)window.WindowState, window.Position.X, window.Position.Y, window.Width, window.Height);
            body.AppendLine($"arka uc: {AppHost.Backend}");

            Assert.Equal((int)WindowState.FullScreen, tamEkranDurumu);
            Assert.Equal(once, geri);

            window.Close();
            return body.ToString();
        });

        GirdiKanit.Write("k16-pencere-geri-yazma.txt", rapor);
        Assert.Contains("geri     ", rapor);
        GirdiKanit.Kapat("k16-pencere-geri-yazma.txt");
    }
}

public sealed class OynaticiGirdiTestsKabukHatasi
{
    [Fact]
    public void OynaticiAcilisHatasiSessizceYutulmaz()
    {
        var rapor = AppHost.Run(() =>
        {
            var bozuk = Path.Combine(GirdiKanit.Folder, "bozuk-ornek.mp4");
            File.WriteAllBytes(bozuk, new byte[] { 0, 1, 2, 3 });

            var window = new MainWindow(bozuk);
            var oncekiMetin = window.SourceStatusText;
            var oncekiHata = window.PlayerOpenFailure;

            var is_ = window.LoadStartupFileAsync();
            var saat = Stopwatch.StartNew();
            while (!is_.IsCompleted && saat.Elapsed < TimeSpan.FromSeconds(60))
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            var body = $"dosya: {Path.GetFileName(bozuk)} ({new FileInfo(bozuk).Length} bayt){Environment.NewLine}"
                     + $"LoadStartupFileAsync bitti mi: {is_.IsCompleted} ({saat.ElapsedMilliseconds} ms){Environment.NewLine}"
                     + $"once  : istisna {oncekiHata?.GetType().Name ?? "yok"} metin '{oncekiMetin}'{Environment.NewLine}"
                     + $"sonra : istisna {window.PlayerOpenFailure?.GetType().Name ?? "yok"}: {window.PlayerOpenFailure?.Message}{Environment.NewLine}"
                     + $"kaynak durumu gorunur {window.SourceStatusVisible}, metin '{window.SourceStatusText}'{Environment.NewLine}";

            Assert.True(is_.IsCompleted, "LoadStartupFileAsync 60 sn icinde bitmedi");
            Assert.Null(oncekiHata);
            Assert.NotNull(window.PlayerOpenFailure);
            Assert.True(window.SourceStatusVisible);
            Assert.NotEqual("", window.SourceStatusText);

            window.Close();
            return body;
        });

        GirdiKanit.Write("k12-acilis-hatasi.txt", rapor);
        GirdiKanit.Kapat("k12-acilis-hatasi.txt", "bozuk-ornek.mp4");
    }

    private static int DilAboneSayisi()
    {
        var alan = typeof(Strings).GetField(
            nameof(Strings.Changed),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (alan?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    /// <summary>
    /// T181: <see cref="Strings.Use"/> surec genelinde ve arayuz is parcaciginin disindan
    /// cagrilabiliyor. Iki sey olculuyor: gorsel agactan ayrilan oynatici aboneligini
    /// gercekten birakiyor mu (abone sayisi kurulus seviyesine doner), ve hala ekliyken
    /// olay baska bir is parcacigindan gelirse <c>RefreshState</c> istisna atiyor mu.
    /// </summary>
    [Fact]
    public void AyrilanOynaticiDilAboneliginiBirakir()
    {
        var onceki = Strings.Language;
        Window? pencere = null;

        try
        {
            var (kurulus, ekli) = AppHost.Run(() =>
            {
                var view = new PlayerView();
                pencere = new Window { Width = 640, Height = 480, Content = view };
                var a = DilAboneSayisi();
                pencere.Show();
                return (a, DilAboneSayisi());
            });

            Strings.Use("tr");

            var ayrik = AppHost.Run(() =>
            {
                pencere!.Close();
                return DilAboneSayisi();
            });

            Strings.Use("tr");

            Assert.Equal(kurulus + 1, ekli);
            Assert.Equal(kurulus, ayrik);
        }
        finally
        {
            AppHost.Run(() => pencere?.Close());
            Strings.Use(onceki);
        }
    }
}

internal static class FareKanit
{
    internal static string Folder
    {
        get
        {
            var path = Path.Combine(GirdiKanit.Root, ".calisma", "dalga7a");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static void Write(string name, string body)
        => File.WriteAllText(Path.Combine(Folder, name), body, new UTF8Encoding(false));

    /// <summary>
    /// Satırı eklediği anda diske yazan defter. Gövdeyi sonda yazmak kırmızı koşumda kanıtı
    /// hiç üretmiyordu: düşen asert <c>Write</c>'a gelmiyor, klasör boş kalıyordu.
    /// </summary>
    internal sealed class Defter(string ad)
    {
        private readonly StringBuilder govde = new();

        internal void Satir(string metin)
        {
            govde.AppendLine(metin);
            Write(ad, govde.ToString());
        }

        internal string Metin => govde.ToString();
    }

    /// <summary>
    /// Son asertten sonra cagrilir: yesil kosum kendi biraktigini siler, kirmizi kosum
    /// kanitini korur cunku dusen asert buraya hic gelmez. Klasor bosalinca o da gider.
    /// </summary>
    internal static void Kapat(params string[] adlar) => KanitKapanisi.Kapat(Folder, adlar);
}

public sealed class OynaticiFareTests
{
    [Fact]
    public void TekTikDuraklatirCiftTikTamEkranVeOynatmayiKorur()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var body = new FareKanit.Defter("f1-tik-ayrimi.txt");
            var saat = 100.0;
            view.FareSaati = () => saat;
            var cift = view.Sol.CiftSn;

            var once = view.IsPlaying;
            view.FarePress(10, 10);
            var basiliken = view.IsPlaying;
            saat += 0.05;
            var tek = view.FareRelease();
            var birakista = view.IsPlaying;
            body.Satir($"tek tik   : oynatma {once} -> basili {basiliken} -> birakis {birakista} ({tek})");

            saat += cift + 0.05;
            var tamOnce = view.Fullscreen.IsFullscreen;
            var oynatmaOnce = view.IsPlaying;
            var izOnce = view.Trace.Count;
            view.FarePress(10, 10);
            saat += 0.05;
            var ilk = view.FareRelease();
            var ilkBirakis = view.IsPlaying;
            saat += 0.05;
            view.FarePress(10, 10);
            saat += 0.05;
            var ikinci = view.FareRelease();
            var uretilen = view.Trace.Skip(izOnce).ToList();
            var tamSonra = view.Fullscreen.IsFullscreen;
            var oynatmaSonra = view.IsPlaying;
            body.Satir($"cift tik  : {ilk} + {ikinci}, tam ekran {tamOnce} -> {tamSonra}, oynatma {oynatmaOnce} -> {ilkBirakis} -> {oynatmaSonra} (sure {cift:0.###} sn)");
            body.Satir($"cift tikin urettigi iz: {string.Join(" | ", uretilen)}");

            saat += cift + 0.05;
            view.FarePress(10, 10);
            saat += 0.05;
            view.FareRelease();
            saat += 0.05;
            view.FarePress(10, 10);
            saat += 0.05;
            view.FareRelease();
            body.Satir($"ikinci cift tik: tam ekran {tamSonra} -> {view.Fullscreen.IsFullscreen}, oynatma {view.IsPlaying}");

            saat += cift + 0.05;
            var yavasOnce = view.IsPlaying;
            view.FarePress(10, 10);
            saat += 0.05;
            view.FareRelease();
            saat += cift + 0.01;
            view.FarePress(10, 10);
            saat += 0.05;
            var yavas = view.FareRelease();
            body.Satir($"yavas iki tik: {yavas}, tam ekran {view.Fullscreen.IsFullscreen}, oynatma {yavasOnce} -> {view.IsPlaying}");

            Assert.False(once);
            Assert.False(basiliken);
            Assert.Equal(SolTikSonucu.Tik, tek);
            Assert.True(birakista);
            Assert.False(tamOnce);
            Assert.Equal(SolTikSonucu.Tik, ilk);
            Assert.Equal(SolTikSonucu.CiftTik, ikinci);
            Assert.NotEqual(oynatmaOnce, ilkBirakis);
            Assert.True(tamSonra);
            Assert.Equal(oynatmaOnce, oynatmaSonra);
            Assert.Contains(uretilen, satir => satir.StartsWith("fullscreen", StringComparison.Ordinal));
            Assert.Equal(SolTikSonucu.Tik, yavas);
            Assert.False(view.Fullscreen.IsFullscreen);
            Assert.Equal(yavasOnce, view.IsPlaying);

            window.Close();
            return body.Metin;
        });

        Assert.Contains("tam ekran False -> True", rapor);
        FareKanit.Kapat("f1-tik-ayrimi.txt");
    }

    [Fact]
    public void BasiliTutmaHiziIkiKatinaCikarirBirakisEskiHizaDonerTikSayilmaz()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var body = new FareKanit.Defter("f7-basili-tutma.txt");
            var saat = 50.0;
            view.FareSaati = () => saat;
            var belirtec = Application.Current!.TryFindResource("PlayerHoldFastDelay", out var deger) ? deger : null;
            var esik = view.Sol.TutmaSn;

            var oynatmaOnce = view.IsPlaying;
            var hizOnce = view.SpeedFactor;
            view.FarePress(20, 20);
            saat += esik - 0.01;
            var erken = view.FareZaman();
            var erkenHiz = view.OynayanHiz;
            saat += 0.01;
            var gecti = view.FareZaman();
            var tutarkenHiz = view.OynayanHiz;
            var tutarkenAyar = view.SpeedFactor;
            var kip = view.FareMove(20 + 3 * ClickArbiter.DragThresholdDip, 20);
            saat += 1;
            var birakis = view.FareRelease();
            var oynatmaBirakista = view.IsPlaying;
            body.Satir($"belirtec {belirtec} esik {esik:0.###} sn: erken {erken} hiz {erkenHiz}, esikte {gecti} hiz {tutarkenHiz} (ayar {tutarkenAyar}), surukleme [{kip}]");
            body.Satir($"birakis {birakis}: hiz {view.OynayanHiz} ayar {view.SpeedFactor}, oynatma {oynatmaOnce} -> {view.IsPlaying}, tutuluyor {view.HizliTutuluyor}");
            body.Satir("iz: " + string.Join(" | ", view.Trace.Where(s => s.StartsWith("hold", StringComparison.Ordinal))));

            saat += 0.05;
            view.FarePress(20, 20);
            saat += 0.05;
            var sonrakiTik = view.FareRelease();
            body.Satir($"tutmadan sonra hemen tik: {sonrakiTik}, oynatma {view.IsPlaying}, tam ekran {view.Fullscreen.IsFullscreen}");

            Assert.Equal(TimeSpan.FromSeconds(esik), belirtec);
            Assert.False(erken);
            Assert.Equal(hizOnce, erkenHiz);
            Assert.True(gecti);
            Assert.Equal(Keymap.HoldSpeed, tutarkenHiz);
            Assert.Equal(hizOnce, tutarkenAyar);
            Assert.Equal("", kip);
            Assert.Equal(SolTikSonucu.TutmaBitti, birakis);
            Assert.Equal(hizOnce, view.OynayanHiz);
            Assert.False(view.HizliTutuluyor);
            Assert.Equal(oynatmaOnce, oynatmaBirakista);
            Assert.Equal(SolTikSonucu.Tik, sonrakiTik);
            Assert.NotEqual(oynatmaOnce, view.IsPlaying);
            Assert.False(view.Fullscreen.IsFullscreen);

            window.Close();
            return body.Metin;
        });

        Assert.Contains("birakis TutmaBitti", rapor);
        FareKanit.Kapat("f7-basili-tutma.txt");
    }

    [Fact]
    public void BasiliTutmaRozetteIkiKatiSolmadanGosterirBirakistaEskiHiziSoyler()
    {
        AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var saat = 80.0;
            view.FareSaati = () => saat;
            var hizOnce = view.SpeedFactor;
            var tutmaMetni = Strings.Get("main.player.osd.speed", VidShrink.Core.Bicim.Kat(Keymap.HoldSpeed, Strings.Culture));
            var eskiMetin = Strings.Get("main.player.osd.speed", VidShrink.Core.Bicim.Kat(hizOnce, Strings.Culture));

            view.FarePress(20, 20);
            saat += view.Sol.TutmaSn - 0.01;
            view.FareZaman();
            var erken = view.OsdText;
            saat += 0.01;
            Assert.True(view.FareZaman());
            var tutarken = view.OsdText;
            var tutarkenSoluyor = view.OsdSoluyor;
            saat += 5;
            Assert.Equal(SolTikSonucu.TutmaBitti, view.FareRelease());

            Assert.NotEqual(tutmaMetni, erken);
            Assert.Equal(tutmaMetni, tutarken);
            Assert.False(tutarkenSoluyor);
            Assert.Equal(eskiMetin, view.OsdText);
            Assert.True(view.OsdSoluyor);

            window.Close();
            return 0;
        });
    }

    [Fact]
    public void SuruklemeEsigiTiklamayiYutarEsikAltiBasisDuraklatir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var body = new FareKanit.Defter("f2-surukleme-esigi.txt");

            view.FarePress(100, 100);
            var kisaKip = view.FareMove(100 + ClickArbiter.DragThresholdDip - 1, 100);
            var kisaBirakis = view.FareRelease();
            var kisaSonrasi = view.IsPlaying;
            body.Satir($"esik alti ({ClickArbiter.DragThresholdDip - 1} dip): kip [{kisaKip}] birakis {kisaBirakis} oynatma {kisaSonrasi}");

            var oynatmaOnce = view.IsPlaying;
            view.FarePress(100, 100);
            var uzunKip = view.FareMove(100 + ClickArbiter.DragThresholdDip, 100);
            var uzunBirakis = view.FareRelease();
            body.Satir($"esik ustu ({ClickArbiter.DragThresholdDip} dip): kip [{uzunKip}] birakis {uzunBirakis} oynatma {oynatmaOnce} -> {view.IsPlaying}");

            Assert.Equal("", kisaKip);
            Assert.Equal(SolTikSonucu.Tik, kisaBirakis);
            Assert.True(kisaSonrasi);

            Assert.Equal("window", uzunKip);
            Assert.Equal(SolTikSonucu.Surukleme, uzunBirakis);
            Assert.Equal(oynatmaOnce, view.IsPlaying);

            window.Close();
            return body.Metin;
        });

        Assert.Contains("esik ustu", rapor);
        FareKanit.Kapat("f2-surukleme-esigi.txt");
    }

    [Fact]
    public void NormalKiptePencereTasinirTamEkrandaGoruntuKayar()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var body = new FareKanit.Defter("f3-tasima-kipi.txt");

            view.FarePress(50, 50);
            var normal = view.FareMove(80, 50);
            view.FareRelease();
            body.Satir($"normal pencere : tam ekran {view.Fullscreen.IsFullscreen} -> surukleme kipi [{normal}]");

            view.Apply(new PlayerCommand(PlayerCommandKind.ToggleFullscreen, 0));
            view.FarePress(50, 50);
            var tam = view.FareMove(80, 50);
            view.FareRelease();
            body.Satir($"tam ekran      : tam ekran {view.Fullscreen.IsFullscreen} -> surukleme kipi [{tam}]");
            body.Satir($"miknatis esigi : {view.Pan.SnapDip} dip (PlaybackBadgeMargin)");

            Assert.Equal("window", normal);
            Assert.Equal("pan", tam);

            window.Close();
            return body.Metin;
        });

        Assert.Contains("[pan]", rapor);
        FareKanit.Kapat("f3-tasima-kipi.txt");
    }

    [Fact]
    public void MerkezMiknatisiEsikIcindeOrtalarDisindaOrtalamaz()
    {
        var pan = new SurfacePan(12);
        pan.SetBounds(1000, 800, 600, 400);

        var body = new FareKanit.Defter("f4-miknatis.txt");
        body.Satir($"sinir: {pan.LimitX} x {pan.LimitY} dip, miknatis {pan.SnapDip} dip");

        pan.Drag(pan.SnapDip - 2, 0);
        body.Satir($"esik icinde surukleme ({pan.SnapDip - 2} dip): X {pan.X} Y {pan.Y} ortada {pan.Centered}");
        Assert.True(pan.Centered);

        pan.Drag(pan.SnapDip * 2, 0);
        body.Satir($"esik disinda surukleme ({pan.SnapDip * 2} dip): X {pan.X} Y {pan.Y} ortada {pan.Centered}");
        Assert.False(pan.Centered);
        Assert.Equal(pan.SnapDip * 2, pan.X);

        pan.Drag(-pan.SnapDip, 0);
        body.Satir($"merkeze donus: X {pan.X} Y {pan.Y} ortada {pan.Centered}");
        Assert.True(pan.Centered);

        pan.Drag(10000, 10000);
        body.Satir($"sinir disina surukleme: X {pan.X} Y {pan.Y} (sinir {pan.LimitX},{pan.LimitY})");
        Assert.Equal(pan.LimitX, pan.X);
        Assert.Equal(pan.LimitY, pan.Y);

        FareKanit.Kapat("f4-miknatis.txt");
    }

    /// <summary>
    /// Kesit B: başlıktaki üç nokta düğmesi kalktı, menünün ikinci yolu artık klavye.
    /// Sağ tık menüyü farenin altında açıyor, klavye ise panonun kendisine yaslıyor;
    /// iki yol da aynı <c>BuildMenu</c>'den geçiyor, yani içerik tek yerden geliyor.
    /// </summary>
    [Fact]
    public void SagTikMenuyuFareKonumundaAcarKlavyeYoluPanoyaYaslanir()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var body = new FareKanit.Defter("f5-menu-konumu.txt");

            GirdiSurucu.Press(view, PointerUpdateKind.RightButtonPressed, RawInputModifiers.RightMouseButton);
            var sagTik = view.MenuAnchor;
            body.Satir($"sag tik      : yaslanma [{sagTik}], iz {view.Trace[^1]}");

            view.MenuAtPointer = false;
            view.Apply(Keymap.OpenMenu.ToCommand());
            var dugme = view.MenuAnchor;
            body.Satir($"klavye       : yaslanma [{dugme}], iz {view.Trace[^1]}");

            var basliklar = view.BuildMenu().Items.OfType<MenuItem>()
                .Where(item => item.Tag is PlayerAction)
                .Select(item => item.Header?.ToString() ?? "")
                .ToList();
            body.Satir($"ilk satir    : {basliklar[0]}");

            Assert.Equal("pointer", sagTik);
            Assert.Equal("surface", dugme);
            Assert.Equal(Strings.Get("main.player.menu.playpause"), basliklar[0]);
            Assert.Equal(Strings.Get("main.player.menu.settings"), basliklar[^1]);

            window.Close();
            return body.Metin;
        });

        Assert.Contains("pointer", rapor);
        FareKanit.Kapat("f5-menu-konumu.txt");
    }

    [Fact]
    public void AyarlarAltMenusuSekmeyeGoturmezKisayolSekmeyiAcar()
    {
        var rapor = AppHost.Run(() =>
        {
            var view = GirdiSurucu.Kur(out var window);
            var acilan = 0;
            view.OpenSettings = () => acilan++;

            var ayarlar = view.BuildMenu().Items.OfType<MenuItem>().Last();
            Assert.Same(Keymap.Settings, ayarlar.Tag);
            IEnumerable<MenuItem> Hepsi(MenuItem m) => m.Items.OfType<MenuItem>().SelectMany(c => Hepsi(c).Prepend(c));
            var sekmeyeGiden = Hepsi(ayarlar).Count(child => ReferenceEquals(child.Tag, Keymap.Settings));
            view.Apply(Keymap.Settings.ToCommand());

            var body = $"satir 0 basligi: {ayarlar.Header}{Environment.NewLine}"
                     + $"sekmeye giden satir {sekmeyeGiden}{Environment.NewLine}"
                     + $"ayarlar cagrisi: {acilan}{Environment.NewLine}"
                     + $"iz: {view.Trace[^1]}{Environment.NewLine}";
            FareKanit.Write("f6-ayarlar-satiri.txt", body);

            Assert.Equal(0, sekmeyeGiden);
            Assert.Equal(1, acilan);
            Assert.Equal("settings -> open", view.Trace[^1]);

            window.Close();
            return body;
        });

        Assert.Contains("ayarlar cagrisi: 1", rapor);
        FareKanit.Kapat("f6-ayarlar-satiri.txt");
    }
}
