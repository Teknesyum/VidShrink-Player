using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App;
using VidShrink.App.Playback;

namespace VidShrink.Tests;

/// <summary>
/// T44: üç kademe ve köşe yarıçapı gerçek pencerede ölçülüyor. Pencere gösterilmiyor —
/// <see cref="AppHost"/> Avalonia'yı kendi iş parçacığında kuruyor, ölçüm yerleşimi elle
/// koşturuyor. Ekranda hiçbir şey açılmıyor.
///
/// T82: panelin boyu artık fareye değil iki tuşa bağlı; gecikmeli iniş ve fareyle büyüme
/// ölçüleriyle birlikte kalktı. Sahte saat (<see cref="FakeClock"/>) yalnız denetim
/// şeridinin sayacında kaldı.
///
/// Zamanlayıcılar burada tik atmaz: bu iş parçacığında ileti döngüsü yok, dolayısıyla
/// <see cref="DispatcherTimer"/> ateşlenmez; süreyi ölçümün kendisi ilerletiyor. Hiçbir
/// ölçümde <c>Thread.Sleep</c> yok (T70/K7).
/// </summary>
public sealed class ComparisonPanelTests
{
    private static readonly Size WindowSize = new(1560, 1060);

    /// <summary>
    /// T66/K4: yerleşimi verilen görüş alanında koşturur — konağın penceresinden bağımsız.
    /// Pencerenin kendisine <see cref="Layoutable.Arrange"/> çağırmak boyutu taşımıyor;
    /// <c>Window.ArrangeSetBounds</c> verilen boyutu değil <c>ClientSize</c>'ı döndürür, yani
    /// sınırlar platform penceresinden gelir. Bu yüzden istenen boyut pencerenin kök görsel
    /// çocuğuna veriliyor — <see cref="WindowLayoutTests"/> ile aynı yol (T59). O kök aynı
    /// zamanda panelin terfi ettiği katmanın taşıyıcısıdır, dolayısıyla kök kademelerin
    /// ölçüldüğü alan da buradan gelir.
    /// </summary>
    private static void LayOutAt(MainWindow window, Size size)
    {
        window.Width = double.NaN;
        window.Height = double.NaN;

        window.Measure(size);
        window.Arrange(new Rect(size));
        window.UpdateLayout();

        var root = (Layoutable)window.GetVisualChildren().Single();
        root.Measure(size);
        root.Arrange(new Rect(size));
    }

    private static T Read<T>(Func<MainWindow, ComparisonPanel, T> read) => Read(WindowSize, read);

    private static T Read<T>(Size size, Func<MainWindow, ComparisonPanel, T> read) =>
        AppHost.Run(() =>
        {
            var window = new MainWindow();
            LayOutAt(window, size);

            var panel = window.GetVisualDescendants().OfType<ComparisonPanel>().Single();
            return read(window, panel);
        });

    /// <summary>Kademeyi tekerlekle sürer; her dokunuştan sonra bekleyen işler koşturulur.</summary>
    private static void WheelTo(MainWindow window, ComparisonPanel panel, ShelterStage stage)
    {
        for (var i = 0; i < 100 && panel.Shelter != stage; i++)
        {
            panel.Zoom(1, new Point(0, 0));
            Settle(window);
        }

        Assert.Equal(stage, panel.Shelter);
    }

    /// <summary>
    /// Terfi sonrası boy <see cref="DispatcherPriority.Render"/> önceliğinde gönderiliyor
    /// (geçiş bandından başlasın diye). Ölçüm o işi elle koşturur.
    /// </summary>
    private static void Settle(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect OverlayBounds(ComparisonPanel panel)
    {
        var overlay = panel.GetVisualAncestors().OfType<VisualLayerManager>().First();
        return new Rect(overlay.Bounds.Size);
    }

    // ---- K1: üç kademe --------------------------------------------------------------

    /// <summary>
    /// Ölçülen pencere boyutları. Tasarım boyutu, pencerenin kendi tabanı ve tabanın
    /// altında kalan bir başsız konak boyutu — sonuncusu CI'ın koştuğu haldir (T66).
    /// </summary>
    public static TheoryData<double, double> WindowSizes() => new()
    {
        { 1560, 1060 },
        { 1040, 720 },
        { 480, 326 }
    };

    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void Orta_kademe_bandindan_buyuk_pencereden_kucuktur(double width, double height)
    {
        var (band, mid, window) = Read(new Size(width, height), (host, panel) =>
        {
            var bandWidth = panel.Bounds.Width;
            WheelTo(host, panel, ShelterStage.Mid);
            return (bandWidth, panel.StageTarget, OverlayBounds(panel));
        });

        Assert.True(mid.Width > band, $"orta {mid.Width:0.#} <= band {band:0.#}");
        Assert.True(mid.Width < window.Width, $"orta {mid.Width:0.#} >= pencere {window.Width:0.#}");
        Assert.True(mid.Height < window.Height, $"orta {mid.Height:0.#} >= pencere {window.Height:0.#}");
        Assert.True(mid.X > 0 && mid.Y > 0, $"orta kademe kenara yapıştı: {mid.X:0.#},{mid.Y:0.#}");
    }

    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void Tam_kademe_pencerenin_tamamini_kaplar(double width, double height)
    {
        var (full, window) = Read(new Size(width, height), (host, panel) =>
        {
            WheelTo(host, panel, ShelterStage.Full);
            return (panel.StageTarget, OverlayBounds(panel));
        });

        Assert.Equal(0, full.X, 6);
        Assert.Equal(0, full.Y, 6);
        Assert.Equal(window.Width, full.Width, 6);
        Assert.Equal(window.Height, full.Height, 6);
    }

    /// <summary>
    /// T66/K3: kullanıcı tekerleği çevirdiğinde üç kademe görür, pencere ne kadar küçük
    /// olursa olsun. Orta kademe tam kademeye eşitlenirse üç kademe ikiye düşer — bu bir
    /// ürün kusurudur, ölçü kusuru değil. Ölçülen şey her iki eksende de ölçülebilir bir
    /// pay: orta kademe hem bandından büyük hem tam kademeden küçüktür.
    /// </summary>
    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void Uc_kademe_her_pencere_boyutunda_uc_ayri_boydur(double width, double height)
    {
        var (band, mid, full) = Read(new Size(width, height), (host, panel) =>
        {
            var bandSize = panel.Bounds.Size;
            WheelTo(host, panel, ShelterStage.Mid);
            var midTarget = panel.StageTarget;
            WheelTo(host, panel, ShelterStage.Full);
            return (bandSize, midTarget, panel.StageTarget);
        });

        Assert.True(mid.Width > band.Width, $"orta {mid.Width:0.#} <= band {band.Width:0.#}");
        Assert.True(mid.Width < full.Width, $"orta {mid.Width:0.#} >= tam {full.Width:0.#}");
        Assert.True(mid.Height < full.Height, $"orta {mid.Height:0.#} >= tam {full.Height:0.#}");
    }

    [Fact]
    public void Orta_kademe_panel_kok_katmandadir()
    {
        var promoted = Read((host, panel) =>
        {
            WheelTo(host, panel, ShelterStage.Mid);
            return panel.IsPromoted;
        });

        Assert.True(promoted);
    }

    [Fact]
    public void Yer_tutucu_kademe_degisince_bandini_korur()
    {
        var (atMid, atFull) = Read((host, panel) =>
        {
            WheelTo(host, panel, ShelterStage.Mid);
            var mid = panel.Bounds.Height;
            WheelTo(host, panel, ShelterStage.Full);
            return (mid, panel.Bounds.Height);
        });

        Assert.Equal(atMid, atFull, 6);
    }

    // ---- T82/K1: fare kademesi kalktı -----------------------------------------------

    /// <summary>
    /// T70/K7: bekleme süresini duvar saati olmadan süren sahte saat. Gerçek zamanlayıcı bu
    /// iş parçacığında zaten tik atmaz (ileti döngüsü yok); ölçüm süreyi kendi ilerletir,
    /// yani hiçbir ölçüm <c>Thread.Sleep</c> beklemiyor.
    /// </summary>
    private sealed class FakeClock : IHoverClock
    {
        private Action? _fire;

        /// <summary>Sayaca sorulan süre. Belirtecin koda ulaştığını bu gösterir.</summary>
        public TimeSpan Delay { get; private set; }

        public TimeSpan Remaining { get; private set; }

        /// <summary>Bekleyen bir tik var mı. Yoksa karar beklemeden verilmiştir.</summary>
        public bool Pending => _fire is not null;

        public void Start(TimeSpan delay, Action fire)
        {
            Delay = delay;
            Remaining = delay;
            _fire = fire;
        }

        public void Stop() => _fire = null;

        public void Advance(TimeSpan step)
        {
            if (_fire is null) return;
            Remaining -= step;
            if (Remaining > TimeSpan.Zero) return;
            var fire = _fire;
            _fire = null;
            fire();
        }
    }

    private static double PanelScale(ComparisonPanel panel) => Math.Round(panel.Gesture.PanelScale, 9);

    /// <summary>
    /// K1: fare panele girip çıkınca boy değişmiyor. Ölçü kabuğun gerçekten yerleştiği boyu
    /// okuyor: giriş kademesi kaldırılmasaydı panel bu turda iki katına çıkardı.
    /// </summary>
    [Fact]
    public void Fare_panele_girip_cikinca_boy_degismez()
    {
        var (start, entered, exited, stage, scale) = Read((host, panel) =>
        {
            var band = panel.Shell.Bounds.Size;

            panel.Shell.RaiseEvent(PointerCrossing(panel.Shell, InputElement.PointerEnteredEvent));
            Settle(host);
            var inside = panel.Shell.Bounds.Size;

            panel.Shell.RaiseEvent(PointerCrossing(panel.Shell, InputElement.PointerExitedEvent));
            Settle(host);
            return (band, inside, panel.Shell.Bounds.Size, panel.Shelter, PanelScale(panel));
        });

        Assert.True(start.Width > 0 && start.Height > 0, $"band ölçülmedi: {start}");
        Assert.Equal(start, entered);
        Assert.Equal(start, exited);
        Assert.Equal(ShelterStage.Band, stage);
        Assert.Equal(1.0, scale);
    }

    /// <summary>
    /// K1'in ikinci yönü: fare pencereyi terk edince büyümüş panel küçülmüyor. Eski kademe
    /// bu olayla iniyordu; artık boyun tek sahibi tuşlardır.
    /// </summary>
    [Fact]
    public void Fare_pencereden_cikinca_buyuk_panel_kuculmez()
    {
        var (grown, afterExit, stage) = Read((host, panel) =>
        {
            Maximize(host, panel);
            var big = panel.StageTarget;

            panel.Shell.RaiseEvent(PointerCrossing(panel.Shell, InputElement.PointerExitedEvent));
            host.RaiseEvent(new PointerEventArgs(
                InputElement.PointerExitedEvent,
                host,
                new Pointer(0, PointerType.Mouse, true),
                host,
                new Point(-1, -1),
                0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
                KeyModifiers.None));
            Settle(host);

            return (big, panel.StageTarget, panel.Shelter);
        });

        Assert.Equal(grown, afterExit);
        Assert.Equal(ShelterStage.Mid, stage);
    }

    /// <summary>
    /// T70/K5: gösterme beklemez — sayaç kurulmadan şerit açılır. İnce şerit isteği: gizleme
    /// de beklemez, fare çıkınca şerit anında kapanır; sayaç hiç kurulmaz.
    /// </summary>
    [Fact]
    public void Serit_gostermede_ve_gizlemede_beklemez()
    {
        var (shown, armedOnShow, armedOnHide, hidden) = Read((host, panel) =>
        {
            var zone = panel.Controls.Zone;
            zone.Reset(false);

            var clock = new FakeClock();
            zone.Clock = clock;

            zone.PointerWithin(true);
            var open = zone.IsVisible;
            var pendingOnShow = clock.Pending;

            zone.PointerWithin(false);
            return (open, pendingOnShow, clock.Pending, zone.IsVisible);
        });

        Assert.True(shown, "şerit gösterme için bekledi");
        Assert.False(armedOnShow, "gösterme sayaç kurdu");
        Assert.False(armedOnHide, "gizleme sayaç kurdu");
        Assert.False(hidden, "şerit fare çıkınca anında gizlenmedi");
    }

    // ---- T82/K3: maksimize ----------------------------------------------------------

    /// <summary>Tuşa basar ve bekleyen yerleşim işlerini koşturur.</summary>
    private static void Press(MainWindow window, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    /// <summary>Maksimize tuşuna basar. Panel orta kademeye çıkar.</summary>
    private static void Maximize(MainWindow window, ComparisonPanel panel)
    {
        Press(window, ZoomButton(window, "BtnPanelMaximize"));
        Assert.Equal(ShelterStage.Mid, panel.Shelter);
    }

    public static TheoryData<double, double> RiseWindows() => new()
    {
        { 1560, 1060 },
        { 2560, 1440 }
    };

    /// <summary>
    /// K3: maksimize edilmiş panelin boyu pencereden hesaplanıyor. Beklenen sayı ölçümde
    /// uydurulmuyor, kök katmanın kendi boyu ile <c>PlaybackMidShare</c> belirtecinden
    /// çıkıyor — sabit çarpan kalsaydı uzun pencerede panel bu boyun altında kalırdı.
    /// Ölçü iki şeyi birden tutuyor: panel paya sığıyor ve <b>en az bir eksende</b> ona
    /// değiyor, yani sığan en büyük boy alınmış oluyor.
    /// </summary>
    [Theory]
    [MemberData(nameof(RiseWindows))]
    public void Maksimize_panel_pencereye_sigan_en_buyuk_boyu_alir(double width, double height)
    {
        var (band, grown, area, share) = Read(new Size(width, height), (host, panel) =>
        {
            var start = panel.Shell.Bounds.Size;
            Maximize(host, panel);
            host.TryFindResource("PlaybackMidShare", out var value);
            return (start, panel.StageTarget, OverlayBounds(panel), (double)value!);
        });

        var fit = new Size(area.Width * share, area.Height * share);

        Assert.True(grown.Width <= fit.Width + 0.001, $"panel {grown.Width:0.#} > pay {fit.Width:0.#}");
        Assert.True(grown.Height <= fit.Height + 0.001, $"panel {grown.Height:0.#} > pay {fit.Height:0.#}");
        Assert.True(Math.Abs(grown.Width - fit.Width) < 0.001 || Math.Abs(grown.Height - fit.Height) < 0.001,
            $"panel paya hiçbir eksende değmiyor: {grown.Width:0.#}x{grown.Height:0.#}, pay {fit.Width:0.#}x{fit.Height:0.#}");

        Assert.True(grown.Height > band.Height, $"maksimize {grown.Height:0.#} <= band {band.Height:0.#}");
        Assert.True(grown.Height < area.Height, $"panel {grown.Height:0.#} >= pencere {area.Height:0.#}");
        Assert.True(grown.X > 0 && grown.Y > 0, $"panel kenara yapıştı: {grown.X:0.#},{grown.Y:0.#}");
    }

    /// <summary>
    /// K3'ün ikinci yarısı: tuşa ikinci basış eski boya döndürür ve dönüş <b>birebir</b>.
    /// Ölçü kabuğun gerçekten yerleştiği boyu okuyor — hedef kademe değil — çünkü kusur
    /// tam orada görünürdü: panel bandına yaklaşık dönseydi altındaki düzen kayardı.
    /// Başlangıç boyu da oturmuş yerleşimden okunur: Küçült'ün orta sütunu görüş alanını
    /// ilk geçişte bilmiyor, tek geçişte bant 619, oturunca 632 px okunuyordu.
    /// </summary>
    [Fact]
    public void Maksimize_tusu_ikinci_basista_eski_boya_doner()
    {
        var (band, grown, back, stage, promoted) = Read((host, panel) =>
        {
            panel.MotionReduced = true;
            ReLayOut(host, WindowSize);
            var start = panel.Shell.Bounds.Size;

            var button = ZoomButton(host, "BtnPanelMaximize");
            Press(host, button);
            ReLayOut(host, WindowSize);
            var big = panel.Shell.Bounds.Size;

            Press(host, button);
            ReLayOut(host, WindowSize);
            return (start, big, panel.Shell.Bounds.Size, panel.Shelter, panel.IsPromoted);
        });

        Assert.True(grown.Height > band.Height + 1, $"maksimize büyütmedi: {band.Height:0.#} -> {grown.Height:0.#}");
        Assert.Equal(band.Width, back.Width, 6);
        Assert.Equal(band.Height, back.Height, 6);
        Assert.Equal(ShelterStage.Band, stage);
        Assert.False(promoted, "panel kök katmanda kaldı");
    }

    // ---- T82/K2: iki tuş sağ üstte ---------------------------------------------------

    /// <summary>
    /// K2: iki tuş da panelin sağ üst çeyreğinde ve birbirini örtmüyor. Metin taşımadıkları
    /// da ölçülüyor: içerik bir dizge olsaydı bu sözleşme çevrilecek metin üretirdi.
    /// </summary>
    [Fact]
    public void Maksimize_ve_tam_ekran_tuslari_sag_ust_ceyrekte()
    {
        var (maximize, fullScreen, stage, words) = Read((host, panel) =>
        {
            FakeFrame(panel);
            Relayout(host);

            var one = ZoomButton(host, "BtnPanelMaximize");
            var two = ZoomButton(host, "BtnPanelFullScreen");

            Rect Where(Button button) =>
                new(button.TranslatePoint(new Point(0, 0), panel.Stage) ?? default, button.Bounds.Size);

            return (Where(one), Where(two), panel.Stage.Bounds.Size,
                new[] { one.Content as string, two.Content as string });
        });

        foreach (var (name, box) in new[] { ("maksimize", maximize), ("tam ekran", fullScreen) })
        {
            Assert.True(box.Width > 0 && box.Height > 0, $"{name} tuşu ölçülmedi: {box}");
            Assert.True(box.X > stage.Width / 2, $"{name} tuşu sol yarıda: {box.X:0.#} / {stage.Width:0.#}");
            Assert.True(box.Bottom < stage.Height / 2, $"{name} tuşu alt yarıda: {box.Bottom:0.#} / {stage.Height:0.#}");
        }

        Assert.False(maximize.Intersects(fullScreen), $"tuşlar örtüşüyor: {maximize} / {fullScreen}");
        Assert.All(words, word => Assert.Null(word));
    }

    // ---- T82/K4: tam ekran -----------------------------------------------------------

    private static KeyEventArgs Escape(MainWindow window) =>
        new() { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window };

    /// <summary>
    /// K4: tam ekran tuşu paneli kök katmanın tamamına yayar — kenar boşluğu yok ve öteki
    /// paneller görünmez, çünkü kabuk katmanın her pikselini kaplıyor.
    /// </summary>
    [Fact]
    public void Tam_ekran_tusu_pencerenin_tamamini_kaplar()
    {
        var (full, area, stage) = Read((host, panel) =>
        {
            Press(host, ZoomButton(host, "BtnPanelFullScreen"));
            return (panel.StageTarget, OverlayBounds(panel), panel.Shelter);
        });

        Assert.Equal(ShelterStage.Full, stage);
        Assert.Equal(0, full.X, 6);
        Assert.Equal(0, full.Y, 6);
        Assert.Equal(area.Width, full.Width, 6);
        Assert.Equal(area.Height, full.Height, 6);
    }

    /// <summary>
    /// K4'ün ikinci yarısı: üç çıkış yolu da ölçülüyor. Tuşa ikinci basış ve Esc paneli
    /// bandına indiriyor, maksimize tuşu ise tam ekranı bırakıp maksimize boya oturtuyor.
    /// </summary>
    [Fact]
    public void Tam_ekranin_uc_cikis_yolu()
    {
        var (button, escape, viaMaximize, maximized) = Read((host, panel) =>
        {
            var full = ZoomButton(host, "BtnPanelFullScreen");
            var max = ZoomButton(host, "BtnPanelMaximize");

            Press(host, full);
            Press(host, full);
            var afterButton = panel.Shelter;

            panel.Descend();
            Settle(host);
            Press(host, full);
            host.RaiseEvent(Escape(host));
            Settle(host);
            var afterEscape = panel.Shelter;

            panel.Descend();
            Settle(host);
            Press(host, full);
            var covered = panel.StageTarget;
            Press(host, max);
            return (afterButton, afterEscape, panel.Shelter, (covered, after: panel.StageTarget));
        });

        Assert.Equal(ShelterStage.Band, button);
        Assert.Equal(ShelterStage.Band, escape);
        Assert.Equal(ShelterStage.Mid, viaMaximize);
        Assert.True(maximized.after.Height < maximized.covered.Height - 1,
            $"maksimize tuşu tam ekranı bırakmadı: {maximized.covered.Height:0.#} -> {maximized.after.Height:0.#}");
    }

    private static readonly Pointer PanelFare = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    private static void PanelBas(Control stage, PointerUpdateKind kind, RawInputModifiers buttons, int clicks = 1) =>
        stage.RaiseEvent(new PointerPressedEventArgs(stage, PanelFare, stage, new Point(20, 20), 0,
            new PointerPointProperties(buttons, kind), KeyModifiers.None, clicks)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        });

    /// <summary>
    /// Karşılaştırma paneli oynatıcıyla aynı girdiyi izler: tekerlek zamanı 10 sn kaydırır
    /// (Alt ile yakınlaştırır), tek tık bırakışta oynat/duraklatı hemen çevirir, çift tık
    /// oynatıcıdaki gibi tam ekrana geçip ilk tıkın duraklatmasını geri alır; orta tuş ve F de tam ekran.
    /// </summary>
    [Fact]
    public void Panel_girdisi_oynaticiyla_ayni()
    {
        var (seek, zoom, clicks, full) = Read((host, panel) =>
        {
            var strip = panel.Controls;
            strip.Duration = TimeSpan.FromSeconds(60);
            strip.Position = TimeSpan.FromSeconds(20);
            var requested = new List<double>();
            strip.SeekRequested += (_, at) => requested.Add(at.TotalSeconds);
            var toggles = 0;
            strip.PlayPauseRequested += (_, _) => toggles++;

            var stageBefore = panel.Shelter;
            var tStart = panel.Gesture.T;
            var down = panel.Wheel(1, KeyModifiers.None, new Point(0, 0));
            var afterDown = strip.Position.TotalSeconds;
            panel.Wheel(-1, KeyModifiers.None, new Point(0, 0));
            var afterUp = strip.Position.TotalSeconds;
            var seekResult = (down.Kind, afterDown, afterUp, Requested: string.Join(",", requested), Same: panel.Shelter == stageBefore && panel.Gesture.T == tStart);

            var tBefore = panel.Gesture.T;
            var zoomCommand = panel.Wheel(1, KeyModifiers.Alt, new Point(0, 0));
            Settle(host);
            var zoomResult = (zoomCommand.Kind, Position: strip.Position.TotalSeconds, Grew: panel.Gesture.T > tBefore);
            panel.Descend();
            Settle(host);

            var clock = 100.0;
            panel.FareSaati = () => clock;
            var playing = strip.IsPlaying;
            panel.StagePress(new Point(20, 20));
            var heldToggles = toggles;
            clock += 0.05;
            var first = panel.StageRelease();
            var afterFirst = strip.IsPlaying;
            clock += 0.1;
            panel.StagePress(new Point(20, 20));
            clock += 0.05;
            var second = panel.StageRelease();
            Settle(host);
            var afterDouble = strip.IsPlaying;
            var full = panel.Shelter == ShelterStage.Full;
            panel.ToggleFullScreen();
            Settle(host);
            clock += 5;
            panel.StagePress(new Point(20, 20));
            panel.StageMove(new Point(60, 20));
            var dragged = panel.StageRelease();
            Settle(host);
            var clickResult = (heldToggles, first, second, Flipped: afterFirst != playing, Back: afterDouble == playing, Toggles: toggles, dragged, Full: full);

            var stage = panel.FindControl<Control>("Stage")!;
            PanelBas(stage, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
            Settle(host);
            var middleOn = panel.Shelter;
            PanelBas(stage, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
            Settle(host);
            var middleOff = panel.Shelter;
            var shell = panel.FindControl<Control>("Shell")!;
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeySymbol = "f", Source = shell });
            Settle(host);
            var keyOn = panel.Shelter;
            return (seekResult, zoomResult, clickResult, (middleOn, middleOff, keyOn));
        });

        Assert.Equal(PlayerCommandKind.Seek, seek.Kind);
        Assert.Equal(21, seek.afterDown, 3);
        Assert.Equal(20, seek.afterUp, 3);
        Assert.Equal("21,20", seek.Requested);
        Assert.True(seek.Same, "tekerlek paneli büyüttü");

        Assert.Equal(PlayerCommandKind.Zoom, zoom.Kind);
        Assert.Equal(20, zoom.Position, 3);
        Assert.True(zoom.Grew, "Alt+tekerlek paneli büyütmedi");

        Assert.Equal(0, clicks.heldToggles);
        Assert.Equal(SolTikSonucu.Tik, clicks.first);
        Assert.Equal(SolTikSonucu.CiftTik, clicks.second);
        Assert.True(clicks.Flipped, "tek tık bırakışta oynatmayı çevirmedi");
        Assert.True(clicks.Back, "çift tık ilk tıkın duraklatmasını geri almadı");
        Assert.Equal(2, clicks.Toggles);
        Assert.Equal(SolTikSonucu.Surukleme, clicks.dragged);
        Assert.True(clicks.Full, "çift tık paneli tam ekrana geçirmedi");

        Assert.Equal(ShelterStage.Full, full.middleOn);
        Assert.NotEqual(ShelterStage.Full, full.middleOff);
        Assert.Equal(ShelterStage.Full, full.keyOn);
    }

    /// <summary>Yerleşimi verilen boyda yeniden koşturur; ölçüm arada bir ölçü değiştirdiyse.</summary>
    private static void ReLayOut(MainWindow window, Size size)
    {
        window.InvalidateMeasure();
        foreach (var part in window.GetVisualDescendants().OfType<Layoutable>()) part.InvalidateMeasure();

        var root = (Layoutable)window.GetVisualChildren().Single();
        root.Measure(size);
        root.Arrange(new Rect(size));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// K3'ün asıl ölçüsü: taban boy küçükken de panel pencereyi dolduruyor.
    ///
    /// Bu ayrımı ölçmek için band bilerek küçültülüyor. Sebebi şu: bu düzende panel bandı
    /// pencereyle birlikte büyüyor, dolayısıyla eski sabit çarpan (2x) her erişilebilir
    /// pencere boyunda tavanı zaten aşıyor ve fark görünmüyor. Band küçüldüğü anda —
    /// önizleme sütununa başka bir şey girdiğinde olacağı gibi — sabit çarpan pencerenin
    /// altında kalır: 300'ün iki katı 600, pencerenin payı ise 954. Ölçek pencereden
    /// hesaplandığında panel o 954'ü alıyor.
    ///
    /// Tavan ölçüldükten sonra kaldırılıyor: küçültme bandı kurmak içindi, büyümüş paneli
    /// kısıtlamak için değil. Ölçüm hem hedefi hem kabuğun gerçekten yerleştiği boyu okuyor.
    /// </summary>
    /// <summary>
    /// Ölçümün küçülttüğü band. Pencerenin payının (1060 x 0,9 = 954) yarısından küçük
    /// olmak zorunda: büyüğü sabit çarpanla da tavanı aşar ve iki hesap ayırt edilemez.
    /// Kare olması gerekiyor: ölçek iki eksenin küçüğünü aldığı için yalnız boyu kısaltmak
    /// genişliği bağlayıcı eksen yapar ve sabit çarpanla arasındaki fark kapanır.
    /// </summary>
    private const double ShortBand = 300;

    [Fact]
    public void Kucuk_bandli_panel_de_pencereyi_dolduruyor()
    {
        var (band, target, settled, area, share, scale, fixedFactor) = Read((host, panel) =>
        {
            // Kabuğun gerçekten yerleştiği boy okunacak: geçiş açıkken boy bir animasyon
            // değeridir ve bu koşuda saat tik atmaz, kabuk terfi anındaki boyunda kalır.
            // Hareket azaltma yolu boyu doğrudan uygular, ölçülen sayı da gerçek olur.
            panel.MotionReduced = true;
            panel.Descend();
            panel.Shell.MinHeight = ShortBand;
            panel.Shell.MaxHeight = ShortBand;
            panel.Shell.MaxWidth = ShortBand;
            ReLayOut(host, WindowSize);
            var shortBand = panel.Shell.Bounds.Height;

            Maximize(host, panel);
            var grown = panel.StageTarget;
            var reach = panel.Gesture.PanelScale;

            panel.Shell.ClearValue(Layoutable.MaxHeightProperty);
            panel.Shell.ClearValue(Layoutable.MinHeightProperty);
            panel.Shell.ClearValue(Layoutable.MaxWidthProperty);
            ReLayOut(host, WindowSize);

            host.TryFindResource("PlaybackMidShare", out var midShare);
            host.TryFindResource("PlaybackHoverZoom", out var hoverZoom);
            return (shortBand, grown, panel.Shell.Bounds.Height, OverlayBounds(panel),
                (double)midShare!, reach, (double)hoverZoom!);
        });

        Assert.Equal(ShortBand, band, 3);
        Assert.Equal(area.Height * share, target.Height, 3);
        Assert.Equal(area.Height * share, settled, 3);
        Assert.True(scale > fixedFactor,
            $"ölçek {scale:0.###} sabit çarpanda ({fixedFactor:0.###}) kaldı, band {band:0.#}");
    }

    /// <summary>
    /// K3'ün ikinci yarısı: iki farklı pencere boyu iki farklı boy veriyor. Sabit çarpan tek
    /// sayı üretirdi; ölçülen şey tam olarak bu farktır.
    /// </summary>
    [Fact]
    public void Iki_pencere_boyu_iki_ayri_buyume_boyu_verir()
    {
        double Grown(double width, double height) =>
            Read(new Size(width, height), (host, panel) =>
            {
                Maximize(host, panel);
                return panel.StageTarget.Height;
            });

        var shortWindow = Grown(1560, 1060);
        var tallWindow = Grown(1560, 1600);

        Assert.True(tallWindow > shortWindow + 1,
            $"uzun pencere {tallWindow:0.#}, kısa pencere {shortWindow:0.#}");
    }

    /// <summary>
    /// İkinci kapı: maksimize etmek paneli tam kademeye çıkarmıyor. T66'da orta kademe
    /// pencereye eşitlenerek üç kademeyi ikiye düşürmüştü; aynı sonuç bu kez ölçeği
    /// tavana dayamakla doğardı — iki tuş ayırt edilemez olurdu. Uzun pencerede bile kademe
    /// orta kalıyor ve tam kademe bundan ölçülebilir biçimde büyük.
    /// </summary>
    [Theory]
    [MemberData(nameof(RiseWindows))]
    public void Maksimize_tam_kademeye_esitlenmez(double width, double height)
    {
        var (stage, grown, full) = Read(new Size(width, height), (host, panel) =>
        {
            Maximize(host, panel);
            var mid = (panel.Shelter, panel.StageTarget);

            WheelTo(host, panel, ShelterStage.Full);
            return (mid.Shelter, mid.StageTarget, panel.StageTarget);
        });

        Assert.Equal(ShelterStage.Mid, stage);
        Assert.True(grown.Height < full.Height, $"fareyle büyüyen {grown.Height:0.#} >= tam {full.Height:0.#}");
        Assert.True(grown.Width < full.Width, $"fareyle büyüyen {grown.Width:0.#} >= tam {full.Width:0.#}");
    }


    [Fact]
    public void Zaman_asimi_parametreyi_de_tabana_indirir()
    {
        var (t, stage, afterOneNotch) = Read((host, panel) =>
        {
            WheelTo(host, panel, ShelterStage.Full);

            // İniş sayacının zaman aşımı bu yolu çağırır.
            panel.Descend();
            Settle(host);

            var floor = panel.Gesture.T;
            var landed = panel.Shelter;

            panel.Zoom(1, new Point(0, 0));
            Settle(host);
            return (floor, landed, panel.Shelter);
        });

        Assert.Equal(0, t, 9);
        Assert.Equal(ShelterStage.Band, stage);
        Assert.Equal(ShelterStage.Band, afterOneNotch);
    }

    [Fact]
    public void Inis_baslamisken_tekerlek_geri_yukari_cevrilebilir()
    {
        var (stage, promoted) = Read((host, panel) =>
        {
            WheelTo(host, panel, ShelterStage.Mid);
            panel.Descend();
            Settle(host);

            WheelTo(host, panel, ShelterStage.Mid);
            return (panel.Shelter, panel.IsPromoted);
        });

        Assert.Equal(ShelterStage.Mid, stage);
        Assert.True(promoted);
    }

    private static TimeSpan? Span(MainWindow window, string key)
        => window.TryFindResource(key, out var value) && value is TimeSpan span ? span : null;

    /// <summary>
    /// T79/K1, K2: panelin iki bekleme belirteci de temadan kalktı — sıfırlanmadı, silindi.
    /// Geri sayımın üç ölçüsü de yok. Şeridin kendi iki süresi duruyor: onlar başka bir
    /// kararın ölçüsü ve bu sözleşme onlara dokunmuyor.
    /// </summary>
    [Fact]
    public void Kalkan_belirtecler_temada_yok()
    {
        var (gone, stripShow, stripHide) = AppHost.Run(() =>
        {
            var window = new MainWindow();
            var dead = new[]
            {
                "PlaybackPanelRiseDelay", "PlaybackPanelFallDelay", "PlaybackDescendDelay",
                "PlaybackCountdownSize", "PlaybackCountdownRadius", "PlaybackCountdownStroke",
                "PlaybackCountdown", "PlaybackIdleShell"
            };
            return (dead.Where(key => window.TryFindResource(key, out _)).ToArray(),
                    Span(window, "PlaybackStripShowDelay"),
                    Span(window, "PlaybackStripHideDelay"));
        });

        Assert.Empty(gone);
        Assert.Equal(TimeSpan.Zero, stripShow);
        Assert.Equal(TimeSpan.Zero, stripHide);
    }

    // ---- K3: köşe yarıçapı ----------------------------------------------------------

    [Fact]
    public void Pano_kabugun_kose_yaricapini_tasir()
    {
        var (radius, expected, size) = Read((host, panel) =>
        {
            var clip = Assert.IsType<RectangleGeometry>(panel.Stage.Clip);
            var token = panel.TryFindResource("RadiusPanelScalar", out var value) && value is double number ? number : -1;
            return (clip.RadiusX, token, clip.Rect.Size);
        });

        Assert.Equal(expected, radius, 6);
        Assert.True(size.Width > 0 && size.Height > 0, $"pano ölçüsü {size}");
    }

    [Fact]
    public void Kose_pikseli_pano_zemininin_disinda_kalir()
    {
        var (corner, centre) = Read((host, panel) =>
        {
            var clip = Assert.IsType<RectangleGeometry>(panel.Stage.Clip);
            return (clip.FillContains(new Point(0.5, 0.5)), clip.FillContains(clip.Rect.Center));
        });

        Assert.False(corner);
        Assert.True(centre);
    }

    private static Button ZoomButton(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static PointerEventArgs PointerCrossing(Control source, RoutedEvent<PointerEventArgs> which) =>
        new(which, source, new Pointer(0, PointerType.Mouse, true), source, default,
            0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None);

    /// <summary>
    /// T46/K3: dugmeler tek jest parametresini centik centik surer. Ayri bir yakinlastirma
    /// durumu yok - tekerlekle dugme ayni sayiya yaziyor.
    /// </summary>
    [Fact]
    public void TiklamaDugmeleriTekParametreyiSurer()
    {
        var (afterIn, afterTwo, afterOut, afterWheelThenOut) = Read((window, panel) =>
        {
            var zoomIn = ZoomButton(window, "BtnZoomIn");
            var zoomOut = ZoomButton(window, "BtnZoomOut");

            panel.Descend();
            zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var one = panel.Gesture.T;
            zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var two = panel.Gesture.T;
            zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var back = panel.Gesture.T;
            panel.Zoom(1, new Point(0, 0));
            zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return (one, two, back, panel.Gesture.T);
        });

        Assert.Equal(ZoomGesture.NotchStep, afterIn, 9);
        Assert.Equal(2 * ZoomGesture.NotchStep, afterTwo, 9);
        Assert.Equal(ZoomGesture.NotchStep, afterOut, 9);
        Assert.Equal(ZoomGesture.NotchStep, afterWheelThenOut, 9);
    }

    /// <summary>T46/K3: uclarda dugmeler devre disi - tavanda arti, tabanda eksi.</summary>
    [Fact]
    public void UclardaDugmelerDevreDisi()
    {
        var (floorIn, floorOut, ceilingIn, ceilingOut) = Read((window, panel) =>
        {
            var zoomIn = ZoomButton(window, "BtnZoomIn");
            var zoomOut = ZoomButton(window, "BtnZoomOut");

            panel.Descend();
            var atFloorIn = zoomIn.IsEnabled;
            var atFloorOut = zoomOut.IsEnabled;
            WheelTo(window, panel, ShelterStage.Full);
            return (atFloorIn, atFloorOut, zoomIn.IsEnabled, zoomOut.IsEnabled);
        });

        Assert.True(floorIn);
        Assert.False(floorOut);
        Assert.False(ceilingIn);
        Assert.True(ceilingOut);
    }

    // ---- T79/K3 (T82/K7): yakinlastirma dugmeleri paneli gercekten buyutur -----------

    /// <summary>
    /// K3, asıl şikâyet. Panel maksimize edilmiş, yani orta kademenin payına dayanmış
    /// durumda. Eskiden artı tuşu bu noktada panelin boyunu değil yalnız yüzde okumasını
    /// oynatıyordu. T82: büyüten şey fare değil maksimize tuşu; güvence aynı kaldı.
    ///
    /// Ölçü kademenin sınırını da pinliyor: yükseklik payda (<c>PlaybackMidShare</c>) asılı
    /// duruyor — panel o eksende zaten tavanda — ama panel yine de büyüyor. Eksi aynı yoldan
    /// geri getiriyor.
    /// </summary>
    [Fact]
    public void Yakinlastirma_dugmesi_buyumus_paneli_de_buyutur()
    {
        var (risen, afterIn, afterOut, area, share) = Read((window, panel) =>
        {
            Maximize(window, panel);
            var grown = panel.StageTarget;

            var zoomIn = ZoomButton(window, "BtnZoomIn");
            zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            var bigger = panel.StageTarget;

            var zoomOut = ZoomButton(window, "BtnZoomOut");
            zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);

            window.TryFindResource("PlaybackMidShare", out var value);
            return (grown, bigger, panel.StageTarget, OverlayBounds(panel), (double)value!);
        });

        Assert.True(afterIn.Width > risen.Width + 1,
            $"artı yutuldu: {risen.Width:0.#} -> {afterIn.Width:0.#}");
        Assert.Equal(area.Height * share, risen.Height, 3);
        Assert.Equal(risen.Height, afterIn.Height, 3);

        Assert.True(afterOut.Width < afterIn.Width - 1,
            $"eksi yutuldu: {afterIn.Width:0.#} -> {afterOut.Width:0.#}");
        Assert.Equal(risen.Width, afterOut.Width, 3);
    }

    /// <summary>
    /// K3'ün sınır yarısı: hiçbir dokunuş sessizce yutulmuyor. Panel maksimize edilmiş
    /// hâlden başlayıp artı tuşuna tam kademeye varana kadar basılıyor ve <b>her</b> dokunuşun
    /// boyu değiştirmesi şart koşuluyor.
    ///
    /// Düzeltmeden önce bu dizinin ortası dümdüzdü: iki eksen de paya dayandıktan sonra beş
    /// dokunuş üst üste hiçbir şey yapmıyor, panel ancak onuncu dokunuşta tam kademeye
    /// çıkıyordu. Dizinin son halkası pencerenin kendisi: kademe sınırı tuşu yutmuyor, geçiriyor.
    /// </summary>
    [Fact]
    public void Hicbir_yakinlastirma_dokunusu_yutulmaz()
    {
        var (steps, stage, area) = Read((window, panel) =>
        {
            Maximize(window, panel);

            var zoomIn = ZoomButton(window, "BtnZoomIn");
            var seen = new List<Rect> { panel.StageTarget };

            for (var i = 0; i < 12 && panel.Shelter != ShelterStage.Full; i++)
            {
                zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settle(window);
                seen.Add(panel.StageTarget);
            }

            return (seen.ToArray(), panel.Shelter, OverlayBounds(panel));
        });

        for (var i = 1; i < steps.Length; i++)
            Assert.True(steps[i].Width > steps[i - 1].Width + 1 || steps[i].Height > steps[i - 1].Height + 1,
                $"{i}. dokunuş yutuldu: {steps[i - 1].Width:0.#}x{steps[i - 1].Height:0.#} -> " +
                $"{steps[i].Width:0.#}x{steps[i].Height:0.#}");

        Assert.Equal(ShelterStage.Full, stage);
        Assert.Equal(area.Width, steps[^1].Width, 3);
        Assert.Equal(area.Height, steps[^1].Height, 3);
    }

    /// <summary>
    /// K3'ün üçüncü yarısı: düzeltme "her artı tam kademeye fırlatır" demiyor. Tam kademeden
    /// bir eksi panel bandına değil orta kademeye iner ve panel pencereyi kaplamayı bırakır —
    /// eskiden ilk eksi histerezis bandına düşüp hiçbir şey yapmıyordu.
    /// </summary>
    [Fact]
    public void Tam_kademeden_bir_eksi_paneli_kucultur()
    {
        var (full, after, stage) = Read((window, panel) =>
        {
            WheelTo(window, panel, ShelterStage.Full);
            var covered = panel.StageTarget;

            var zoomOut = ZoomButton(window, "BtnZoomOut");
            zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);

            return (covered, panel.StageTarget, panel.Shelter);
        });

        Assert.Equal(ShelterStage.Mid, stage);
        Assert.True(after.Height < full.Height - 1,
            $"eksi yutuldu: {full.Height:0.#} -> {after.Height:0.#}");
    }

    // ---- T82/K5, K6: bandda esit saydamlik, buyukken opak siyah ----------------------

    /// <summary>Fırçanın çizime giden örtücülüğü: kendi opaklığı çarpı renginin alfası.</summary>
    private static double Ink(IBrush? brush)
        => brush is ISolidColorBrush solid ? solid.Opacity * solid.Color.A / 255.0 : -1;

    /// <summary>
    /// T79/K4'ten kalan yarı: kaynak yüklü değilken panonun zemini arkayı gösterir.
    /// Saydamlık kodda yazılmış bir sayı değil: perdenin opaklığı
    /// <c>PlaybackIdleVeilOpacity</c> belirtecinin kendisi ve o belirteç de ankanın
    /// çizildiği opaklıkla aynı kaynaktan geliyor. Kare geldiğinde zemin yine örtücüdür;
    /// iki fırça aynı renk, tek fark opaklık.
    ///
    /// T82: kabuğun kendi yüzeyi bu ölçünün dışına çıktı — orada ölçü artık saydamlık değil
    /// öteki panellerle eşitlik. <see cref="Bandda_panel_saydamligi_oteki_panellerle_esit"/>.
    ///
    /// Fırçaların kendisi dışarı taşınmıyor: Avalonia nesnelerinin özellikleri yalnız kendi iş
    /// parçacığında okunabiliyor, bu yüzden karşılaştırma konakta yapılıp sayı dönüyor.
    /// </summary>
    [Fact]
    public void Bos_onizlemenin_zemini_arkayi_gosterir()
    {
        var (idle, filled, veil, phoenix, sameColour) = Read((window, panel) =>
        {
            var empty = (ISolidColorBrush)panel.Stage.Background!;
            var emptyOpacity = empty.Opacity;

            FakeFrame(panel);
            var loaded = (ISolidColorBrush)panel.Stage.Background!;

            window.TryFindResource("PlaybackIdleVeilOpacity", out var token);
            window.TryFindResource("PhoenixOpacity", out var anka);
            return (emptyOpacity, loaded.Opacity, (double)token!, (double)anka!, empty.Color == loaded.Color);
        });

        Assert.Equal(veil, idle, 6);
        Assert.True(veil < 1, $"boş önizlemenin perdesi örtücü: {veil:0.###}");
        Assert.Equal(phoenix, veil, 6);
        Assert.Equal(1.0, filled, 6);
        Assert.True(sameColour, "iki zemin aynı renk değil");
    }

    /// <summary>
    /// K6: panel büyümemişken kabuğunun saydamlığı öteki panellerinkiyle eşit. Ölçü iki
    /// yönden bakıyor: iki panelin fırçası <b>aynı nesne</b> — yani kabuk kendi yerel
    /// değerini taşımıyor, öteki panellerin gösterdiği <c>StaticResource</c>'u gösteriyor —
    /// ve çizime giden örtücülük iki panelde de aynı. Karşılaştırılan panel kaynak paneli.
    ///
    /// Belirtecin adı ayrıca sayıdan doğrulanıyor: <c>PanelSurfaceOpacity</c> temada tek
    /// yerde duruyor ve kabuğun çizdiği örtücülük odur, kopyalanmış bir sayı değil. Fırçanın
    /// kendisiyle kimlik karşılaştırması yapılamıyor, çünkü <c>Theme.axaml</c> hem uygulama
    /// kaynaklarına hem de <c>Controls.axaml</c>'a ayrı ayrı katılıyor: aynı tanımın iki
    /// örneği var, iki panel bunlardan aynısını gösteriyor.
    /// </summary>
    [Fact]
    public void Bandda_panel_saydamligi_oteki_panellerle_esit()
    {
        var (sameBrush, shell, other, token) = Read((window, panel) =>
        {
            var source = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SourcePanel");
            window.TryFindResource("PanelSurfaceOpacity", out var value);

            return (ReferenceEquals(panel.Shell.Background, source.Background),
                Ink(panel.Shell.Background), Ink(source.Background), (double)value!);
        });

        Assert.True(sameBrush, "kabuk öteki panelin fırçasını göstermiyor");
        Assert.Equal(other, shell, 6);
        Assert.Equal(token, shell, 6);
    }

    /// <summary>
    /// K5: panel taban boyunun üstündeki her kademede zemin opak — arkadaki anka görünmez.
    /// Ölçü sabit okumuyor: iki katmanın da (kabuk ve pano) çizime giden fırçasını okuyup
    /// örtücülüğünü hesaplıyor. Bandda perde hâlâ saydam; ölçü büyümenin kendisini ayırıyor.
    /// </summary>
    [Fact]
    public void Buyumus_panelin_zemini_opak()
    {
        var (bandShell, bandStage, midShell, midStage, fullShell, fullStage) = Read((window, panel) =>
        {
            var atBand = (shell: Ink(panel.Shell.Background), stage: Ink(panel.Stage.Background));

            Maximize(window, panel);
            var atMid = (shell: Ink(panel.Shell.Background), stage: Ink(panel.Stage.Background));

            Press(window, ZoomButton(window, "BtnPanelFullScreen"));
            Assert.Equal(ShelterStage.Full, panel.Shelter);

            return (atBand.shell, atBand.stage, atMid.shell, atMid.stage,
                Ink(panel.Shell.Background), Ink(panel.Stage.Background));
        });

        Assert.Equal(1.0, midShell, 6);
        Assert.Equal(1.0, midStage, 6);
        Assert.Equal(1.0, fullShell, 6);
        Assert.Equal(1.0, fullStage, 6);

        Assert.True(bandStage < 1, $"bandda pano örtücü: {bandStage:0.###}");
        Assert.True(bandShell < 1, $"bandda kabuk örtücü: {bandShell:0.###}");
    }

    // ---- T79/K5: paravan sekilleniyor ------------------------------------------------

    /// <summary>
    /// K5: paravan artık düz bir örtü değil. Seçilen biçim kenar sönümü: örtü altından
    /// yukarı doğru son çeyreğinde aynı rengin sıfır örtücülüklü haline geçiyor. Ölçü
    /// biçimin sayısını pinliyor: sönen çeyreğin başladığı durak <c>1 - PlaybackScrimEdge</c>,
    /// ve o oran panelin tek band payı oranıyla (<c>PlaybackHoverZoneShare</c>) aynı saydır —
    /// yeni bir oran uydurulmadı. Biçimin yüzeye gerçekten geçtiği de ölçülüyor: şeridin
    /// örtüsü bu fırçanın kendisidir.
    /// </summary>
    [Fact]
    public void Paravanin_kenari_soner()
    {
        var (onStrip, start, end, offsets, colours, edge, zone, solid) = Read((window, panel) =>
        {
            var brush = (LinearGradientBrush)window.FindResource("PlaybackScrimVeil")!;
            window.TryFindResource("PlaybackScrimEdge", out var share);
            window.TryFindResource("PlaybackHoverZoneShare", out var band);
            window.TryFindResource("PlaybackScrimColor", out var opaque);

            return (ReferenceEquals(brush, panel.Controls.Bar.Background),
                brush.StartPoint,
                brush.EndPoint,
                brush.GradientStops.Select(stop => stop.Offset).ToArray(),
                brush.GradientStops.Select(stop => stop.Color).ToArray(),
                (double)share!,
                (double)band!,
                (Color)opaque!);
        });

        Assert.True(onStrip, "şeridin örtüsü biçimli paravan değil");

        // Aşağıdan yukarı: örtücü taban şeridin metninin altında, sönen uç üst kenarda.
        Assert.Equal(new RelativePoint(0.5, 1, RelativeUnit.Relative), start);
        Assert.Equal(new RelativePoint(0.5, 0, RelativeUnit.Relative), end);

        Assert.Equal(new[] { 0, 1 - edge, 1 }, offsets);
        Assert.Equal(edge, zone, 6);

        Assert.Equal(solid, colours[0]);
        Assert.Equal(solid, colours[1]);

        // Sönen uç aynı renk: değişen tek şey örtücülük, kenarda ikinci bir renk yok.
        Assert.Equal(0, colours[2].A);
        Assert.Equal((solid.R, solid.G, solid.B), (colours[2].R, colours[2].G, colours[2].B));
    }

    // ---- T49: yaklasik onizleme rozeti ----------------------------------------------

    /// <summary>
    /// Ikinci bir yerlesim turu. Pencere gosterilmedigi icin <c>UpdateLayout</c> tek
    /// basina yeni tur acmiyor: ilk turdan sonra degisen gorunurluk ve metin ancak olcum
    /// elle yeniden kosturuldugunda desired/bounds degerlerine yansiyor.
    /// </summary>
    private static void Relayout(MainWindow window)
    {
        window.InvalidateMeasure();
        foreach (var part in window.GetVisualDescendants().OfType<Layoutable>()) part.InvalidateMeasure();

        var root = (Layoutable)window.GetVisualChildren().Single();
        root.Measure(WindowSize);
        root.Arrange(new Rect(WindowSize));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Panelin bos durumu kare varligina bakiyor; kare yolu gercek cizim turunda aciliyor
    /// ve olcumde cizim turu yok. Bu yuzden kapi dogrudan aciliyor: yuzeyin kendisi
    /// olcumun sahibi degil, burada yalniz "elimde kare var" hali kuruluyor.
    /// </summary>
    private static void FakeFrame(ComparisonPanel panel)
    {
        panel.Frames.Configure(new PixelSize(2560, 720));
        typeof(ComparisonSurface)
            .GetField("_hasFrame", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(panel.Frames, true);
        panel.RefreshEmptyState();
    }

    private const string BadgeText = "Approximate preview \u00b7 CRF 21";

    [Fact]
    public void Rozet_yalniz_yaklasik_parcada_gorunur()
    {
        var (clip, full, curtain) = Read((window, panel) =>
        {
            FakeFrame(panel);

            panel.SetRightBadge(BadgeText);
            Relayout(window);
            var onClip = panel.ApproxBadge.IsVisible;

            panel.SetRightBadge(null);
            var onFull = panel.ApproxBadge.IsVisible;

            panel.SetRightBadge(BadgeText);
            panel.SetRightNotice("This part will be processed");
            var onCurtain = panel.ApproxBadge.IsVisible;

            return (onClip, onFull, onCurtain);
        });

        Assert.True(clip, "parca gosterilirken rozet yok");
        Assert.False(full, "tam ciktida rozet duruyor");
        Assert.False(curtain, "perde inmisken rozet duruyor");
    }

    [Fact]
    public void Rozet_panelin_sag_yarisindadir()
    {
        var (badgeLeft, half, stageWidth) = Read((window, panel) =>
        {
            FakeFrame(panel);
            panel.SetRightBadge(BadgeText);
            Relayout(window);

            var origin = panel.ApproxBadge.TranslatePoint(new Point(0, 0), panel.Stage) ?? default;
            return (origin.X, panel.Stage.Bounds.Width / 2, panel.ApproxBadge.Bounds.Width);
        });

        Assert.True(stageWidth > 0, $"rozet olculmedi: {stageWidth:0.#}");
        Assert.True(badgeLeft > half, $"rozetin sol kenari {badgeLeft:0.#}, panonun ortasi {half:0.#}");
    }

    /// <summary>
    /// T49/K2: metin panelde uretilmiyor. Barindiran taraf ne verirse ekranda o duruyor;
    /// panel sayiyi yeniden bicimlendirmiyor, ceviriyi ikinci kez uygulamiyor.
    /// </summary>
    [Fact]
    public void Rozet_metni_verildigi_gibi_gorunur()
    {
        var (english, turkish) = Read((window, panel) =>
        {
            FakeFrame(panel);
            panel.SetRightBadge(BadgeText);
            var en = panel.ApproxBadgeText.Text;

            panel.SetLanguage("tr");
            panel.SetRightBadge("Yakla\u015f\u0131k \u00d6nizleme \u00b7 CRF 21");
            return (en, panel.ApproxBadgeText.Text);
        });

        Assert.Equal(BadgeText, english);
        Assert.Equal("Yakla\u015f\u0131k \u00d6nizleme \u00b7 CRF 21", turkish);
    }

    [Fact]
    public void Rozetin_iki_anahtari_sozlukte()
    {
        var turkish = Locales.Values("tr");

        Assert.Equal("\u0130\u015flenmi\u015f", turkish["playback.badge.processed"]);
        Assert.Equal("\u00d6nizleme \u00f6rne\u011fi kodlanamad\u0131", turkish["playback.sample-failed"]);
    }

    /// <summary>
    /// T49/K3 tuzagi: rozet fare olaylarini yutarsa panonun kendi isleyicileri — denetim
    /// seridini aciyor olan da dahil — rozetin altinda kor kalir. Iki olcum: rozet ve metni
    /// isabet testine kapali, ve rozetten kalkan bir fare olayi panonun isleyicisine
    /// ulasiyor.
    ///
    /// Gercek isabet testi burada olculemez: <c>InputHitTest</c> penceresiz kosuda
    /// panonun ortasinda bile <c>null</c> donuyor, yani cizim yuzeyi olmadan calismiyor.
    /// Olculen sey isabet testini surukleyen ozelligin kendisi ve olay yolu.
    /// </summary>
    [Fact]
    public void Rozet_fare_olaylarina_saydamdir()
    {
        var (badgeOpen, textOpen, reached) = Read((window, panel) =>
        {
            FakeFrame(panel);
            panel.SetRightBadge(BadgeText);
            Relayout(window);

            var seen = 0;
            void Spy(object? sender, PointerEventArgs e) => seen++;
            panel.Stage.PointerMoved += Spy;
            panel.ApproxBadge.RaiseEvent(PointerCrossing(panel.ApproxBadge, InputElement.PointerMovedEvent));
            panel.Stage.PointerMoved -= Spy;

            return (panel.ApproxBadge.IsHitTestVisible, panel.ApproxBadgeText.IsHitTestVisible, seen);
        });

        Assert.False(badgeOpen, "rozet isabet testine acik");
        Assert.False(textOpen, "rozet metni isabet testine acik");
        Assert.Equal(1, reached);
    }

    /// <summary>
    /// T49/K4: secilen davranis kirpma. Rozet sarmiyor, paneli genisletmiyor; sigmayan
    /// metin uc noktayla kesiliyor. Tavan sag yarinin kendi genisliginden geliyor.
    /// </summary>
    [Fact]
    public void Uzun_rozet_metni_paneli_genisletmez()
    {
        var (shortPanel, longPanel, shortDesired, longDesired, badgeWidth, cap, half) =
            Read((window, panel) =>
            {
                FakeFrame(panel);

                panel.SetRightBadge(BadgeText);
                panel.InvalidateMeasure();
                Relayout(window);
                var narrowPanel = panel.Bounds.Width;
                var narrowDesired = panel.DesiredSize.Width;

                panel.SetRightBadge(new string('W', 400));
                panel.InvalidateMeasure();
                Relayout(window);

                return (narrowPanel, panel.Bounds.Width, narrowDesired, panel.DesiredSize.Width,
                    panel.ApproxBadge.Bounds.Width, panel.ApproxBadge.MaxWidth, panel.Stage.Bounds.Width / 2);
            });

        Assert.Equal(shortPanel, longPanel, 3);
        Assert.Equal(shortDesired, longDesired, 3);
        Assert.True(cap > 0 && cap < half, $"tavan {cap:0.#}, sag yari {half:0.#}");
        Assert.True(badgeWidth <= cap + 0.5, $"rozet {badgeWidth:0.#} > tavan {cap:0.#}");
    }

    /// <summary>
    /// Kullanici 09-07: ORIJINAL ve ISLENMIS etiketleri panonun ustunde, solda ve sagda.
    /// CRF rozeti geldiginde ISLENMIS etiketi sonmez, ikisi birlikte gorunur.
    /// </summary>
    [Fact]
    public void Taraf_etiketleri_panonun_ustunde_solda_ve_sagda_durur()
    {
        var (sameParent, rowBottom, stageTop, leftX, rightX, half, rightWithCrf, crfShown) = Read((window, panel) =>
        {
            FakeFrame(panel);
            panel.Split = 0.5;
            panel.SetRightBadge("CRF 21");
            Relayout(window);

            return (ReferenceEquals(panel.LabelRow.Parent, panel.Stage.Parent),
                panel.LabelRow.Bounds.Bottom, panel.Stage.Bounds.Top,
                panel.LeftBadge.Bounds.X, panel.RightBadge.Bounds.X, panel.LabelRow.Bounds.Width / 2,
                panel.RightBadge.IsVisible, panel.ApproxBadge.IsVisible);
        });

        Assert.True(sameParent, "etiket satiri panoyla ayni izgarada degil");
        Assert.True(rightWithCrf, "CRF rozeti gelince ISLENMIS etiketi sondu");
        Assert.True(crfShown, "CRF rozeti gorunmuyor");
        Assert.True(rowBottom > 0, "etiket satiri olculmedi");
        Assert.True(rowBottom <= stageTop + 0.5, $"etiket satirinin alti {rowBottom:0.#}, panonun ustu {stageTop:0.#}");
        Assert.True(leftX < half, $"sol etiket {leftX:0.#}, orta {half:0.#}");
        Assert.True(rightX > half, $"sag etiket {rightX:0.#}, orta {half:0.#}");
    }
}
