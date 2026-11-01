using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App.Localization;

namespace VidShrink.App.Playback;

/// <summary>
/// Karşılaştırma paneli: sunum yüzeyi, ayırıcı, yakınlaştırma ve terfi.
///
/// Denetim şeridi T40'ta eklendi ve <see cref="ControlStrip"/> içinde yaşıyor; panel
/// ona yalnız yer ve fare konumu verir. Kare kaynağı burada değil: panel
/// kaynağı bilmez, <see cref="ComparisonSurface"/> üstünden "elimde bir kare var"
/// varsayımıyla çalışır.
/// </summary>
internal partial class ComparisonPanel : UserControl
{
    /// <summary>Klavyeyle ayırıcının küçük adımı: pano genişliğinin yüzde biri.</summary>
    private const double SplitKeyStep = 0.01;

    /// <summary>Ölçek karşılaştırmasının toleransı. Jest sınıfındakiyle aynı büyüklük.</summary>
    private const double ScaleEpsilon = 1e-9;

    /// <summary>
    /// Sentetik kare ölçüsü. İki 720p taraf yan yana. T37 2x1080p'yi ölçtü ve sunum yolu
    /// orada da duvar değil; burada daha küçük olmasının sebebi sunum değil, üreteç —
    /// deseni yönetilen kodda pişirmek 2x1080p'de açılışı saniyelerce bekletirdi.
    /// </summary>
    private static readonly PixelSize DemoFrame = new(2560, 720);

    private readonly ZoomGesture _gesture;

    // Ayırıcı Margin ile değil, dönüşümle taşınıyor: Margin ölçüme girer ve uçlarda
    // ızgaranın istediği genişliği şişirip pencereye yatay kaydırma çubuğu getirir.
    private readonly TranslateTransform _separatorAt = new();

    private SyntheticFrameSource? _demo;
    private OverlayLayer? _overlay;
    private DispatcherTimer? _landing;

    private ShelterStage _stage = ShelterStage.Band;
    private Rect _target;

    // T52: terfi anındaki band ölçüsü. Orta kademenin boyu bunun ölçekli katı olduğu için
    // saklanıyor: yer tutucunun sınırı terfi turunda henüz yerleşmemiş olabilir, _target ise
    // ilk kademe uygulandığı anda üstüne yazılır.
    private Size _band;

    private string _language = Strings.FallbackLanguage;
    private string? _noticeArg;
    private string? _notice;
    private string? _rightNotice;
    private string? _rightBadge;
    private bool _motionReduced;
    private bool _promoted;
    private bool _draggingSeparator;
    private bool _panning;
    private bool _enlarged;
    private double _restore;
    private Point _panFrom;
    private readonly ClickArbiter _click = new();
    private double _split = 0.5;

    public ComparisonPanel()
    {
        InitializeComponent();

        // T52: jestin aralığı da bandından çıkış eşiği de temadan gelir; sınıf hiçbir
        // sayı uydurmaz, buradaki yedekler belirteçlerle birebir aynı değerlerdir.
        _gesture = new ZoomGesture(Scalar("PlaybackMaxPanelScale", 4), Scalar("PlaybackHoverZoom", 2));

        // K5: azaltılmış hareket ayarını okuyan tek yer HoverZone. İkinci kopya yok.
        _motionReduced = HoverZone.MotionReduced;

        Surface.Gesture = _gesture;
        SeparatorGrip.RenderTransform = _separatorAt;

        Shell.PointerWheelChanged += OnWheel;
        Shell.KeyDown += OnShellKey;

        BtnZoomIn.Click += (_, _) => Zoom(1, StageCentre());
        BtnZoomOut.Click += (_, _) => Zoom(-1, StageCentre());
        BtnPanelMaximize.Click += (_, _) => ToggleMaximize();
        BtnPanelFullScreen.Click += (_, _) => ToggleFullScreen();

        Stage.PointerPressed += OnStagePressed;
        Stage.PointerMoved += OnStageMoved;
        Stage.PointerReleased += OnStageReleased;
        Stage.PointerCaptureLost += (_, _) => { if (_hizliTutma) TutmaKoptu(); };

        SeparatorGrip.PointerPressed += OnSeparatorPressed;
        SeparatorGrip.PointerMoved += OnSeparatorMoved;
        SeparatorGrip.PointerReleased += OnSeparatorReleased;
        SeparatorGrip.PointerEntered += (_, _) => LightSeparator(true);
        SeparatorGrip.PointerExited += (_, _) => LightSeparator(_draggingSeparator || SeparatorGrip.IsFocused);
        SeparatorGrip.GotFocus += (_, _) => LightSeparator(true);
        SeparatorGrip.LostFocus += (_, _) => LightSeparator(_draggingSeparator);
        SeparatorGrip.KeyDown += OnSeparatorKey;

        Stage.SizeChanged += (_, _) =>
        {
            ApplySplit();
            ClipStage();
        };
        Stage.PointerExited += (_, _) => Strip.PointerGone();

        SetLanguage(Strings.FallbackLanguage);
        ApplySplit();
        ClipStage();
        RefreshReadout();
        RefreshEmptyState();

        if (Design.IsDesignMode) StartDemo();
    }

    /// <summary>Terfi hâli. Panel kök katmandaysa doğrudur.</summary>
    internal bool IsPromoted => _promoted;

    /// <summary>Panelin uygulanmış boy kademesi. Ad çakışmasın diye pano <c>Stage</c> kaldı.</summary>
    internal ShelterStage Shelter => _stage;

    /// <summary>
    /// Hedef kademenin kök katmandaki sınırı. Ölçüm buna bakar, panelin animasyon
    /// sırasındaki anlık sınırına değil (T44/K2, ikinci tuzak).
    /// </summary>
    internal Rect StageTarget => _target;

    /// <summary>Düğmeyle büyütülmüş mü — geri dönülecek bir boy saklı mı.</summary>
    internal bool IsEnlarged => _enlarged;

    /// <summary>
    /// Hareket azaltma ayarı. Değeri <see cref="HoverZone.MotionReduced"/> veriyor; ölçüm
    /// işletim sistemi ayarını değiştiremediği için açık kapı (K4).
    /// </summary>
    internal bool MotionReduced
    {
        get => _motionReduced;
        set => _motionReduced = value;
    }

    /// <summary>Ayırıcının konumu, sıfır ile bir arası. Uçlar geçerlidir.</summary>
    internal double Split
    {
        get => _split;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(next - _split) < 1e-9) return;
            _split = next;
            ApplySplit();
        }
    }

    /// <summary>Jest parametresi — dışarıdan okunur, tanılama ve test içindir.</summary>
    internal ZoomGesture Gesture => _gesture;

    internal ComparisonSurface Frames => Surface;

    /// <summary>Denetim şeridi. Kareyi süren taraf oynat/duraklat/atla isteklerini buradan alır.</summary>
    internal ControlStrip Controls => Strip;

    /// <summary>
    /// Kapalı panel bandını daraltır. İki ölçü de temadan gelir: açıkken sahne en az
    /// <c>PlaybackStageMinHeight</c>, önizleme yokken <c>PlaybackIdleMinHeight</c>
    /// (T46/K2 — boş panel artık bırakma alanı ölçüsünde değil, panel tabanında).
    /// </summary>
    internal void SetCompact(bool compact)
        => Shell.MinHeight = Scalar(compact ? "PlaybackIdleMinHeight" : "PlaybackStageMinHeight", 256);

    /// <summary>
    /// Boş durumun ikinci satırı. Kare kaynağı bir sebep bildirdiğinde (motor yok, akış
    /// açılamadı) o sebep buraya yazılır; boş bırakılınca panel kendi varsayılanına döner.
    /// Metin İngilizce verilir, ekrana çalışan dilde çıkar.
    /// </summary>
    internal void SetNotice(string? english, string? arg = null)
    {
        _notice = string.IsNullOrWhiteSpace(english) ? null : english;
        _noticeArg = arg;
        RefreshTexts();
    }

    /// <summary>
    /// Sağ tarafın perdesi. İşlenmiş dosya yokken görüntü değil sebep gösterilir —
    /// planın §6 dürüstlük kuralı. Boş bırakılınca perde kalkar.
    /// </summary>
    internal void SetRightNotice(string? english)
    {
        _rightNotice = string.IsNullOrWhiteSpace(english) ? null : english;
        RefreshTexts();
        RefreshEmptyState();
        ApplySplit();
    }

    /// <summary>
    /// T49/K1: sağ yarının yaklaşıklık rozeti. Barındıran taraf metni çalışan dilde verir
    /// ve panel onu olduğu gibi gösterir — sayı burada üretilmez, çeviri ikinci kez
    /// uygulanmaz (metin "CRF 21" gibi sayı taşıdığı için sözlükte
    /// karşılığı yoktur). Boş bırakılınca rozet kalkar.
    ///
    /// Dil değiştiğinde metnin de değişmesi barındıran tarafın işidir: panel neyi
    /// gösterdiğini bilir, neyin çevirisi olduğunu bilmez.
    /// </summary>
    internal void SetRightBadge(string? text)
    {
        _rightBadge = string.IsNullOrWhiteSpace(text) ? null : text;
        ApproxBadgeText.Text = _rightBadge ?? string.Empty;
        RefreshEmptyState();
        ApplySplit();
    }

    /// <summary>
    /// K7: kare kaynağı bağlanmadan panel denenebilsin diye sentetik üreteci başlatır.
    /// Renkler temadan çözülüyor; üreteç kendi rengini uydurmuyor.
    /// </summary>
    internal void StartDemo()
    {
        if (_demo is not null) return;
        Surface.Configure(DemoFrame);
        _demo = new SyntheticFrameSource(Surface, DemoFrame, Palette(), BlockSize())
        {
            TargetFps = 60
        };
        _demo.Start();
        Strip.StartDemoClock();
        RefreshEmptyState();
        DispatcherTimer.RunOnce(RefreshEmptyState, Motion("MotionSlow", 360));
    }

    internal void StopDemo()
    {
        _demo?.Dispose();
        _demo = null;
        Strip.StopDemoClock();
    }

    /// <summary>
    /// Panelin kendi metinleri. Ana pencerenin ağaç yürüyüşü bu dizgeleri sözlüğünde
    /// bulamaz — sözlük bu sözleşmenin alanında değil — bu yüzden dil geçidi burada.
    /// Barındıran taraf dil değişiminde bunu çağırır.
    /// </summary>
    internal void SetLanguage(string language)
    {
        _language = language;
        RefreshTexts();
        Strip.SetLanguage(language);
        RefreshReadout();
    }

    /// <summary>
    /// Panelin bütün metinleri tek geçitten. Kaynak dizge İngilizce, Türkçesi sözlükte;
    /// panel ikinci bir kopya taşımaz (K5).
    /// </summary>
    private void RefreshTexts()
    {
        EmptyTitle.Text = Text("playback.panel.title");
        EmptyHint.Text = _notice is null
            ? Text("playback.panel.hint")
            : Text(_notice, _noticeArg);
        ToolTip.SetTip(EmptyHint, _notice is null || string.IsNullOrEmpty(_noticeArg) ? null : _noticeArg);
        PlaceholderText.Text = Text("playback.panel.moved");
        LeftBadgeText.Text = Text("playback.badge.original");
        RightBadgeText.Text = Text("playback.badge.processed");
        RightCurtainText.Text = _rightNotice is null ? string.Empty : Text(_rightNotice);
    }

    private string Text(string key, params object?[] args)
        => key.StartsWith("playback.", StringComparison.Ordinal) || key.StartsWith("main.", StringComparison.Ordinal)
            ? LanguageCatalog.Title(Strings.GetIn(_language, key, args), _language)
            : key;

    // ---- ayırıcı (K2) -------------------------------------------------------------

    /// <summary>
    /// T49/K4: yaklaşık rozetinin tavanı panonun sağ yarısı eksi kenar boşluğu; sığmayan metin
    /// üç noktayla kırpılır ve panelin istediği genişliği büyütmez. Tavan perdeye bağlı değil,
    /// rozetin konumu gibi panonun köşesine bağlı (T184/K3): perdenin genişliğinden türediğinde
    /// ayırıcı sağa kaydıkça tavan 0'a iniyor ve metin tümden kayboluyordu.
    /// </summary>
    private void ApplySplit()
    {
        Surface.Split = _split;
        var width = Stage.Bounds.Width;
        if (width > 0)
        {
            _separatorAt.X = _split * width - SeparatorGrip.Width / 2;
            // Perde ayırıcıdan sağa uzanır. Genişlik panonun kendi ölçüsünden türüyor,
            // bu yüzden istenen genişlik hiçbir zaman panodan büyük olmaz.
            var right = Math.Max(0, width - Math.Clamp(_split, 0, 1) * width);
            RightCurtain.Width = right;

            ApproxBadge.MaxWidth = Math.Max(0, width / 2 - Inset("PlaybackBadgeMargin", 24));
        }

        RefreshBadgeFade();
    }

    /// <summary>
    /// T184/K3: iki rozet de perde hareket ederken yerinden kıpırdamaz — konumları panonun
    /// köşelerine bağlı, ayırıcıya değil. Perdeyle değişen tek şey görünürlük: bir yarı
    /// tümüyle örtüldüğünde o yarının etiketi söner, çünkü işaret ettiği görüntü ekranda
    /// kalmamıştır.
    ///
    /// Kullanıcı 09-07: taraf etiketleri panonun üstündeki satırda, CRF rozeti panonun sağ
    /// üstünde. Rozet geldi diye İŞLENMİŞ etiketi sönmez; ikisi aynı anda görünür.
    /// </summary>
    private void RefreshBadgeFade()
    {
        var live = Surface.HasFrame;
        var leftShown = _split > SplitKeyStep;
        var rightShown = _split < 1 - SplitKeyStep;

        LeftBadge.IsVisible = live && leftShown;
        RightBadge.IsVisible = live && rightShown && _rightNotice is null;
        ApproxBadge.IsVisible = live && rightShown && _rightNotice is null && _rightBadge is not null;
    }

    /// <summary>
    /// K3 (T44): kabuk yuvarlak köşeli bir Border, ama içindeki kare arka plan ve kare
    /// çizilen yüzey dört köşeyi örtüyordu — kenarlık çiziliyor, köşede görünmüyordu.
    /// Pano aynı yarıçapla kesilince köşeler açılıyor; kesme panonun bütün çocuklarını
    /// kapsadığı için sağ perde de aynı köşeyi taşıyor. Yarıçap uydurulmuyor,
    /// <c>RadiusPanelScalar</c> belirtecinden geliyor.
    /// </summary>
    private void ClipStage()
    {
        var size = Stage.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            Stage.Clip = null;
            return;
        }

        var radius = Scalar("RadiusPanelScalar", 16);
        Stage.Clip = new RectangleGeometry(new Rect(size)) { RadiusX = radius, RadiusY = radius };
    }

    private void LightSeparator(bool lit)
    {
        SeparatorLine.Classes.Set("lit", lit);
        SeparatorHandle.Classes.Set("lit", lit);
    }

    private void OnSeparatorPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed) return;
        _draggingSeparator = true;
        LightSeparator(true);
        e.Pointer.Capture(SeparatorGrip);
        SeparatorGrip.Focus();
        e.Handled = true;
    }

    private void OnSeparatorMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingSeparator) return;
        var width = Stage.Bounds.Width;
        if (width <= 0) return;
        Split = e.GetPosition(Stage).X / width;
        e.Handled = true;
    }

    private void OnSeparatorReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_draggingSeparator) return;
        _draggingSeparator = false;
        e.Pointer.Capture(null);
        LightSeparator(SeparatorGrip.IsFocused || SeparatorGrip.IsPointerOver);
        e.Handled = true;
    }

    private void OnSeparatorKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Split -= SplitKeyStep; break;
            case Key.Right: Split += SplitKeyStep; break;
            case Key.Home: Split = 0; break;
            case Key.End: Split = 1; break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- yakınlaştırma ve pano (K3) ----------------------------------------------

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var notches = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (notches == 0) return;
        Wheel(notches, e.KeyModifiers, e.GetPosition(Surface));
        e.Handled = true;
    }

    internal PlayerCommand Wheel(double notches, KeyModifiers modifiers, Point anchor)
    {
        var command = Keymap.ForWheel(notches, modifiers);
        switch (command.Kind)
        {
            case PlayerCommandKind.Seek:
                Strip.SeekBy(command.Amount);
                break;
            case PlayerCommandKind.Zoom:
                Zoom(notches, anchor);
                break;
        }

        return command;
    }

    /// <summary>
    /// Tek jest girişi. Tekerlek de, dışarıdan gelen çağrı da buradan geçer; tavanda
    /// yanlış döner, yani tekerlek durur ve panel daha büyümez.
    ///
    /// T79: yakınlaştırma düğmesinin paneli büyütmemesinin sebebi burasıydı. Kademenin boyu
    /// paya (<c>PlaybackMidShare</c>) kırpılıyor, tam kademede ise ölçekten hiç türemiyor;
    /// ölçek o tavana dayandıktan sonra her çentik yalnız yüzde okumasını oynatıyor, panelin
    /// boyunu oynatmıyordu. Büyütülmüş panel tam o doyum noktasında duruyor
    /// (<see cref="MaximizeScale"/> payın kendisini hedefler) — yani kullanıcının bastığı her
    /// artı yutulan çentikti. Aynısı eksi yönünde tam kademede oluyordu: ilk çentik histerezis
    /// bandına düşüyor, panel pencereyi kaplamaya devam ediyordu.
    ///
    /// Kural artık tek: terfi etmiş panelde bir çentik boyu değiştirmek zorundadır. Çentik
    /// yutuluyorsa jest aynı yönde ilerler ve ilk görünür boyda durur; taban ya da tavana
    /// varılırsa <see cref="ZoomGesture.Wheel"/> yanlış döner ve döngü biter.
    /// </summary>
    internal bool Zoom(double notches, Point anchor)
    {
        if (notches == 0) return false;

        var before = StageSize(_gesture.Shelter);
        var step = notches;
        var moved = false;

        while (_gesture.Wheel(step, anchor.X, anchor.Y))
        {
            moved = true;

            // Bandda boy jestin değil düzenin: orada çentik terfi eşiğine tırmanır ve
            // yalnız okuma oynar (T46/K3). Kural terfi eden kademelere ait.
            if (_gesture.Shelter == ShelterStage.Band) break;
            if (!Same(StageSize(_gesture.Shelter), before)) break;

            step = Math.Sign(notches);
        }

        if (!moved) return false;

        AfterZoom();
        return true;
    }

    /// <summary>
    /// T82: maksimize tuşu. Panel uygulama penceresine sığan en büyük boya çıkar; tekrar
    /// basıldığında saklanan jest parametresine, yani basılmadan önceki boya döner.
    /// Tam ekrandayken bu tuş çıkışın üçüncü yoludur: panel tam ekranı bırakıp maksimize
    /// boya oturur.
    /// </summary>
    internal void ToggleMaximize()
    {
        if (_enlarged && _gesture.Shelter == ShelterStage.Full)
        {
            GoTo(MaximizeScale());
            return;
        }

        if (_enlarged)
        {
            Restore();
            return;
        }

        _restore = _gesture.T;
        _enlarged = true;
        GoTo(MaximizeScale());
    }

    /// <summary>
    /// T82: tam ekran tuşu. Panel kök katmanın tamamını kaplar — kenar boşluğu yok. İkinci
    /// basış saklanan boya döndürür; <see cref="Descend"/> (Esc) ve maksimize tuşu öteki iki
    /// çıkış yolu.
    /// </summary>
    internal void ToggleFullScreen()
    {
        if (_enlarged && _gesture.Shelter == ShelterStage.Full)
        {
            Restore();
            return;
        }

        if (!_enlarged) _restore = _gesture.T;
        _enlarged = true;
        _gesture.Promote();
        AfterZoom();
    }

    /// <summary>Saklanan jest parametresine döner. Parametre tabandaysa panel bandına iner.</summary>
    private void Restore()
    {
        _enlarged = false;
        GoTo(_gesture.ScaleAt(_restore));
    }

    private void GoTo(double panelScale)
    {
        var centre = StageCentre();
        if (_gesture.ScaleTo(panelScale, centre.X, centre.Y)) AfterZoom();
    }

    private static bool Same(Size a, Size b)
        => Math.Abs(a.Width - b.Width) < ScaleEpsilon && Math.Abs(a.Height - b.Height) < ScaleEpsilon;

    /// <summary>Panonun ortası. Düğmeyle yakınlaştırmanın çıpası — fare imleci yok.</summary>
    private Point StageCentre() => new(Surface.Bounds.Width / 2, Surface.Bounds.Height / 2);

    private void AfterZoom()
    {
        SyncShelter();
        Surface.InvalidateVisual();
        RefreshReadout();
    }

    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (ReferenceEquals(source, Strip) || Strip.IsVisualAncestorOf(source))) return;
        var point = e.GetCurrentPoint(Stage);
        if (point.Properties.IsMiddleButtonPressed)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        StagePress(point.Position);
        _stagePress = EtkilesimliDenetim.Icinde(e.Source as Visual, Stage) ? null : e;
        if (!_gesture.CanPan) return;
        _panning = true;
        e.Pointer.Capture(Stage);
        Stage.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    internal void StagePress(Point at)
    {
        SolBas(at);
        _panFrom = at;
    }

    internal void StageMove(Point at) => Sol.Oyna(at.X, at.Y);

    internal SolTikSonucu StageRelease() => SolBirak();

    private void OnStageMoved(object? sender, PointerEventArgs e)
    {
        // T40: şerit panelin alt bölgesinde belirir; bölge oranı Stage yüksekliğinden.
        var here = e.GetPosition(Stage);
        Strip.PointerAt(here.Y, Stage.Bounds.Height);
        StageMove(here);
        if (!_panning && _click.Dragging && _stagePress is { } press) StageMoveWindow(press, e);

        if (!_panning) return;
        var now = e.GetPosition(Stage);
        if (_gesture.Drag(now.X - _panFrom.X, now.Y - _panFrom.Y)) Surface.InvalidateVisual();
        _panFrom = now;
    }

    private PointerPressedEventArgs? _stagePress;

    internal int StageWindowMoves { get; private set; }

    internal Action<PointerPressedEventArgs>? StageMoveStarter { get; set; }

    private void StageMoveWindow(PointerPressedEventArgs press, PointerEventArgs e)
    {
        _stagePress = null;
        if (!e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed) return;
        if (TopLevel.GetTopLevel(this) is not Window { WindowState: WindowState.Normal } window) return;
        TutmaKoptu();
        StageWindowMoves++;
        if (StageMoveStarter is { } start) start(press);
        else window.BeginMoveDrag(press);
    }

    private void OnStageReleased(object? sender, PointerReleasedEventArgs e)
    {
        _stagePress = null;
        if (e.InitialPressMouseButton == MouseButton.Left) StageRelease();
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
        Stage.Cursor = Cursor.Default;
    }

    private void RefreshReadout()
    {
        // T52: okunan yüzde panelin boy ölçeği. %100 taban boy, %200 iki katı.
        var percent = _gesture.PanelScale * 100.0;
        ZoomText.Text = Text("playback.zoom-readout", percent.ToString("0", Strings.CultureOf(_language)));
        BtnZoomIn.IsEnabled = !_gesture.AtCeiling;
        BtnZoomOut.IsEnabled = !_gesture.AtFloor;
    }

    internal void RefreshEmptyState()
    {
        var empty = !Surface.HasFrame;
        EmptyState.IsVisible = empty;
        Surface.Opacity = empty ? 0 : 1;
        // Perde varken sağ tarafta "işlenmiş" diye bir şey yok; yonga da sebebi söyleyen
        // perdenin üstünde durmaz.
        RightCurtain.IsVisible = !empty && _rightNotice is not null;
        // Rozet yalnız gerçekten yaklaşık bir parça gösterilirken var: tam çıktıda
        // metin gelmez, perde inmişken sağ tarafta işaret edilecek bir görüntü yoktur.
        RefreshBadgeFade();
        ZoomRow.IsVisible = !empty;
        SeparatorGrip.IsVisible = !empty;
        Strip.IsVisible = !empty;

        RefreshBackdrop();
    }

    /// <summary>
    /// Panelin iki zemini. Pano kare varken örtücüdür — yoksa görüntünün altından arka plan
    /// sızardı — ve kaynak yokken T79'un perdesine geçer; saydamlık kodda yazılmıyor,
    /// <c>PlaybackIdleVeilOpacity</c> belirtecinden geliyor.
    ///
    /// T82: taban boyun üstündeki her kademede iki zemin de örtücüdür. Kabuk orada
    /// programın en koyu zeminini (<c>PlaybackPromotedShell</c>) çiziyor, pano da perdesini
    /// bırakıyor: büyümüş panelin arkasında anka hiç görünmüyor.
    ///
    /// Taban boyda kabuk kendi yerel değerini hiç taşımaz — zemini <c>Panel</c> temasından,
    /// yani öteki panellerin gösterdiği <c>PanelSurface</c> belirtecinden alır. Saydamlığın
    /// ölçüsü eşitliktir; buraya ikinci bir sayı yazılmıyor.
    /// </summary>
    private void RefreshBackdrop()
    {
        var empty = !Surface.HasFrame;
        var enlarged = _stage != ShelterStage.Band;

        if (Fill(empty && !enlarged ? "PlaybackIdleVeil" : "PlaybackStageFill") is { } backdrop)
            Stage.Background = backdrop;

        if (enlarged && Fill("PlaybackPromotedShell") is { } solid) Shell.Background = solid;
        else Shell.ClearValue(Border.BackgroundProperty);
    }

    // ---- terfi ve yer tutucu (K4, K5) ---------------------------------------------

    /// <summary>
    /// Jest hangi kademeyi söylüyorsa panel oraya gider. Üç kademe de tek parametreden
    /// türüyor; panel kendi başına kademe kararı vermiyor (T44/K1).
    /// </summary>
    private void SyncShelter()
    {
        var stage = _gesture.Shelter;
        if (stage == _stage)
        {
            // T52: kademe aynı kalsa da ölçek değişmiş olabilir; orta kademenin boyu
            // sabit bir oran değil, jestin söylediği ölçektir.
            if (_promoted) ApplyStage();
            return;
        }

        if (stage == ShelterStage.Band)
        {
            Land();
            return;
        }

        if (!_promoted)
        {
            Promote(stage);
            return;
        }

        // İniş başlamış ama daha oturmamışken tekerlek geri yukarı çevrilebilir. Bekleyen
        // iniş iptal edilmezse panel az sonra kendiliğinden bandına düşerdi.
        _landing?.Stop();
        _landing = null;

        _stage = stage;
        ApplyStage();
    }

    private void Promote(ShelterStage stage)
    {
        var overlay = OverlayLayer.GetOverlayLayer(this);
        if (overlay is null) return;

        var origin = Shell.TranslatePoint(new Point(0, 0), overlay) ?? new Point(0, 0);
        var band = new Rect(origin, Shell.Bounds.Size);

        // Yer tutucu terfi anında bandını aynı boyutta tutar: altındaki düzen zıplamaz.
        // Kademe değişimlerinde bu boy bir daha yazılmaz — yer tutucu hep bandındadır.
        Placeholder.Height = band.Height;
        Placeholder.IsVisible = true;

        Root.Children.Remove(Shell);
        Shell.Width = band.Width;
        Shell.Height = band.Height;
        Canvas.SetLeft(Shell, band.X);
        Canvas.SetTop(Shell, band.Y);
        overlay.Children.Add(Shell);

        _overlay = overlay;
        _promoted = true;
        _stage = stage;
        _target = band;
        _band = band.Size;
        overlay.SizeChanged += OnOverlayResized;

        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.AddHandler(KeyDownEvent, OnTopLevelKey, RoutingStrategies.Tunnel);
            top.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel);
        }

        // K5: geçiş kabuğa uygulanıyor, görüntüye değil. Yüzey kendi turunda çizmeye
        // devam eder; geçişin kare hızıyla ilgisi yoktur.
        Shell.Transitions = _motionReduced ? null : ShellTransitions();
        if (_motionReduced) ApplyStage();
        else Dispatcher.UIThread.Post(ApplyStage, DispatcherPriority.Render);

        Shell.Focus();
    }

    /// <summary>
    /// Kademenin kök katmandaki sınırı. T52: orta kademenin boyu artık sabit bir oran değil,
    /// jestin söylediği ölçek — bandın boyu çarpı <c>PanelScale</c>. Tavanı
    /// <c>PlaybackMidShare</c>: panel pencereyi kaplamaz, kenarı bırakır. Tam kademe kök
    /// katmanın tamamıdır.
    ///
    /// T66: tavan istisnasızdır. Eskiden band pencerenin payından genişse tavan bandın
    /// kendisi oluyordu; kısa pencerede band pencereden uzun olduğu için orta kademe
    /// pencereye eşitleniyor ve tam kademeden ayırt edilemez hale geliyordu — üç kademe
    /// ikiye düşüyordu. Pay her boyutta uygulanınca orta kademenin çevresinde ölçülebilir
    /// bir kenar hep kalır. Bandın pencereye sığmadığı durumda orta kademe bandından kısa
    /// olabilir; terfi etmiş panel kök katmandan taşamaz, band ise kaydırılan sayfada durur.
    ///
    /// T73: maksimize edilen panelin ölçeği bu paya oturacak şekilde seçiliyor
    /// (<see cref="MaximizeScale"/>), yani buradaki <c>Math.Min</c> o yolda tam olarak
    /// payın kendisini veriyor. Pay hâlâ tek belirteç; tavanı iki yerde ayrı ayrı yazmak
    /// orta kademeyi yine tam kademeye doğru kaydırırdı.
    /// </summary>
    private Rect StageBounds(ShelterStage stage)
    {
        if (_overlay is null) return _target;

        var area = OverlayArea();
        var size = StageSize(stage);
        return new Rect((area.Width - size.Width) / 2, (area.Height - size.Height) / 2, size.Width, size.Height);
    }

    /// <summary>
    /// T79: kademenin boyu. <see cref="StageBounds"/> bunu ortalar, <see cref="Zoom"/> ise
    /// "bu çentik bir şey değiştiriyor mu" diye sorar — iki soru tek hesaptan cevaplanıyor.
    /// Bandda boy jestin değil düzenin olduğu için ölçek hiç geçmiyor.
    /// </summary>
    private Size StageSize(ShelterStage stage)
    {
        if (stage == ShelterStage.Band) return _promoted ? _band : Shell.Bounds.Size;

        var area = OverlayArea();
        if (stage == ShelterStage.Full) return area;

        var share = Scalar("PlaybackMidShare", 0.9);
        var scale = _gesture.PanelScale;
        return new Size(Math.Min(_band.Width * scale, area.Width * share),
                        Math.Min(_band.Height * scale, area.Height * share));
    }

    /// <summary>
    /// Kök katmanın kapladığı alan. Katmanın kendisi bir <see cref="Canvas"/> ve ölçüsünü
    /// çocuklarından almaz; yerleşim turu ona boy vermediğinde ölçü katman yöneticisinden
    /// okunur. Yöneticinin sınırı pencerenin içerik alanıdır ve katman onun başlangıcında
    /// durur, bu yüzden iki koordinat çakışır.
    /// </summary>
    private Size OverlayArea()
    {
        // T73: terfiden önce de sorulabiliyor — büyüme ölçeği pencereden hesaplandığı için
        // panel daha kök katmana çıkmadan alanın bilinmesi gerekiyor. Katman görsel ağaçta
        // panelden bağımsız durur, terfi onu yaratmaz.
        var overlay = _overlay ?? OverlayLayer.GetOverlayLayer(this);
        if (overlay is null) return default;

        var area = overlay.Bounds.Size;
        if (area.Width > 0 && area.Height > 0) return area;
        return overlay.GetVisualAncestors().OfType<VisualLayerManager>().FirstOrDefault()?.Bounds.Size ?? area;
    }

    /// <summary>Panelin taban boyu. Terfi ettikten sonra kabuk büyümüş olur; band saklanandır.</summary>
    private Size BandSize() => _promoted ? _band : Shell.Bounds.Size;

    /// <summary>
    /// T82: maksimize tuşunun çıktığı boy ölçeği. Sabit bir çarpan değil: pencerenin payına
    /// sığan en büyük boy taban boya bölünüyor, yani panel pencere kısaldığında taşmıyor,
    /// uzadığında boşa yer bırakmıyor. Pay <see cref="StageBounds"/> ile aynı belirteçten
    /// (<c>PlaybackMidShare</c>) geliyor; iki hesap aynı payı kullanmasa panel kendi
    /// tavanına çarpardı. İki eksenin küçüğü seçiliyor — panel tek bir ölçekle büyüyor ve
    /// büyük olan eksen paydan taşmıyor.
    ///
    /// İki uç bilerek kapalı ve ikisi de T66 kusurunun aynısını üretmemek için:
    ///   Taban <c>PlaybackHoverZoom</c> — orta kademenin eşiği odur, altına inen bir ölçek
    ///   paneli hiç terfi ettirmez ve tuş görünür bir şey yapmaz.
    ///   Tavan tam kademe eşiğinin bir tekerlek çentiği altı; karşılığı jestin kendi ölçek
    ///   hesabından okunuyor (T79/K7, formülün ikinci kopyası kalktı). Ölçek tavana değseydi
    ///   maksimize tuşu paneli doğrudan tam kademeye çıkarır, iki tuş ayırt edilemez olurdu.
    /// </summary>
    private double MaximizeScale()
    {
        var floor = _gesture.PromoteScale;
        var band = BandSize();
        var area = OverlayArea();
        if (band.Width <= 0 || band.Height <= 0 || area.Width <= 0 || area.Height <= 0) return floor;

        var share = Scalar("PlaybackMidShare", 0.9);
        var top = _gesture.ScaleAt(ZoomGesture.FullDropAt);
        var fit = Math.Min(area.Width * share / band.Width, area.Height * share / band.Height);
        return Math.Clamp(fit, floor, Math.Max(floor, top));
    }

    private void ApplyStage()
    {
        if (_overlay is null) return;

        _target = StageBounds(_stage);
        Canvas.SetLeft(Shell, _target.X);
        Canvas.SetTop(Shell, _target.Y);
        Shell.Width = _target.Width;
        Shell.Height = _target.Height;
        RefreshBackdrop();
    }

    /// <summary>
    /// Pencere boyu değişti. T82: panel maksimize edilmişse ölçek yeni pencereye göre baştan
    /// hesaplanır — tavan pencereden geldiği için pencere uzayınca panel de uzar. Kullanıcı
    /// tekerleği çevirdiyse seçtiği ölçek durur; taşma zaten <see cref="StageBounds"/>'un
    /// payıyla engelleniyor.
    /// </summary>
    private void OnOverlayResized(object? sender, SizeChangedEventArgs e)
    {
        if (!_promoted) return;

        if (_enlarged && _gesture.Shelter != ShelterStage.Full)
        {
            var centre = StageCentre();
            if (_gesture.ScaleTo(MaximizeScale(), centre.X, centre.Y))
            {
                AfterZoom();
                return;
            }
        }

        ApplyStage();
    }

    private void Land()
    {
        if (!_promoted || _overlay is null) return;
        var overlay = _overlay;

        var origin = Placeholder.TranslatePoint(new Point(0, 0), overlay) ?? new Point(0, 0);
        var band = new Rect(origin, Placeholder.Bounds.Size);

        _stage = ShelterStage.Band;
        _target = band;
        RefreshBackdrop();

        if (_motionReduced)
        {
            Settle();
            return;
        }

        Canvas.SetLeft(Shell, band.X);
        Canvas.SetTop(Shell, band.Y);
        Shell.Width = band.Width;
        Shell.Height = band.Height;

        _landing?.Stop();
        _landing = new DispatcherTimer { Interval = Motion("MotionBase", 240) };
        _landing.Tick += (_, _) =>
        {
            _landing?.Stop();
            _landing = null;
            Settle();
        };
        _landing.Start();
    }

    private void Settle()
    {
        if (_overlay is null) return;
        _overlay.SizeChanged -= OnOverlayResized;
        _overlay.Children.Remove(Shell);
        _overlay = null;
        _promoted = false;
        _stage = ShelterStage.Band;

        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.RemoveHandler(KeyDownEvent, OnTopLevelKey);
            top.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        }

        Shell.Transitions = null;
        Shell.ClearValue(WidthProperty);
        Shell.ClearValue(HeightProperty);
        Shell.ClearValue(Canvas.LeftProperty);
        Shell.ClearValue(Canvas.TopProperty);

        if (!Root.Children.Contains(Shell)) Root.Children.Add(Shell);

        Placeholder.IsVisible = false;
        Placeholder.ClearValue(HeightProperty);
        ApplySplit();
    }

    private Transitions ShellTransitions()
    {
        var duration = Motion("MotionBase", 240);
        var easing = new CubicEaseOut();
        return new Transitions
        {
            new DoubleTransition { Property = Canvas.LeftProperty, Duration = duration, Easing = easing },
            new DoubleTransition { Property = Canvas.TopProperty, Duration = duration, Easing = easing },
            new DoubleTransition { Property = WidthProperty, Duration = duration, Easing = easing },
            new DoubleTransition { Property = HeightProperty, Duration = duration, Easing = easing }
        };
    }

    private void OnShellKey(object? sender, KeyEventArgs e)
    {
        // K4: boşluk tuşu oynat/duraklat, panel odaktayken.
        if (e.Key == Key.Space)
        {
            Strip.TogglePlay();
            e.Handled = true;
            return;
        }

        if (Keymap.ForKey(e.Key, e.KeyModifiers, e.KeySymbol).Kind == PlayerCommandKind.ToggleFullscreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || !_promoted) return;
        Leave();
        e.Handled = true;
    }

    private void OnTopLevelKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_promoted) return;
        Leave();
        e.Handled = true;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Visual root) return;
        if (TryDismissOnOutsideClick(e.GetPosition(root))) e.Handled = true;
    }

    internal bool TryDismissOnOutsideClick(Point pointOnTopLevel)
    {
        if (!_promoted) return false;
        if (TopLevel.GetTopLevel(this) is not { } top) return false;

        var origin = Shell.TranslatePoint(new Point(0, 0), top) ?? new Point(0, 0);
        var rect = new Rect(origin, Shell.Bounds.Size);
        if (rect.Contains(pointOnTopLevel)) return false;

        Descend();
        top.FocusManager?.Focus(null);
        return true;
    }

    /// <summary>
    /// Esc'in yolu. Panel düğmeyle büyütüldüyse saklanan boya döner — tam ekranın üç çıkış
    /// yolundan biri budur; tekerlekle büyütüldüyse tabana iner.
    /// </summary>
    private void Leave()
    {
        if (_enlarged) Restore();
        else Descend();
    }

    /// <summary>Jest parametresi tabana iner, panel bandına döner.</summary>
    internal void Descend()
    {
        _enlarged = false;
        _gesture.Demote();
        Land();
        Surface.InvalidateVisual();
        RefreshReadout();
    }

    // ---- tema ve ortam ------------------------------------------------------------

    private double Scalar(string key, double fallback)
        => this.TryFindResource(key, out var value) && value is double number ? number : fallback;

    private double Inset(string key, double fallback)
        => this.TryFindResource(key, out var value) && value is Thickness edge ? edge.Left + edge.Right : fallback;

    /// <summary>Temadan zemin fırçası. Renk burada üretilmiyor, belirteçten okunuyor.</summary>
    private IBrush? Fill(string key)
        => this.TryFindResource(key, out var value) ? value as IBrush : null;

    private TimeSpan Motion(string key, double fallbackMs)
        => this.TryFindResource(key, out var value) && value is TimeSpan span
            ? span
            : TimeSpan.FromMilliseconds(fallbackMs);

    private uint[] Palette()
    {
        var keys = new[] { "Renk1Color", "Renk3Color", "Renk2Color", "SuccessColor" };
        var set = new List<uint>();
        foreach (var key in keys)
            if (this.TryFindResource(key, out var value) && value is Color colour)
                set.Add(colour.ToUInt32());
        return set.Count > 0 ? set.ToArray() : new[] { 0xFF00F3FFu };
    }

    /// <summary>
    /// Sağ tarafın blok boyu. Uydurulmuş bir sayı değil: yarıçap ölçeğinin en büyük
    /// basamağı (RadiusPanel, 16). Sentetik "işlenmiş" taraf gözle ayırt edilsin diye var.
    /// </summary>
    private int BlockSize()
        => this.TryFindResource("RadiusPanelScalar", out var value) && value is double scalar
            ? (int)Math.Max(2, scalar)
            : 16;

    /// <summary>
    /// T79/K4: boş durumun zeminleri temadan çözülüyor ve tema ancak panel ağaca
    /// bağlandığında görünür oluyor — yapıcıda sorulan belirteç hiç bulunamıyordu. Boş
    /// durum bu yüzden bağlanma turunda bir kez daha uygulanıyor.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshEmptyState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopDemo();
        base.OnDetachedFromVisualTree(e);
    }
}
