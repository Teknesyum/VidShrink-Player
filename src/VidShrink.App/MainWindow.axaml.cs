using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using VidShrink.App.Localization;
using VidShrink.Core.Subtitles;
using VidShrink.App.Subtitles;
using VidShrink.App.Share;
using VidShrink.App.Themes;
using VidShrink.App.Performance;
using VidShrink.App.Playback;
using VidShrink.Core;
using CoreShare = VidShrink.Core.Share;
using VidShrink.Ffmpeg;

namespace VidShrink.App;

public enum DropVisual { Idle, Accept, Reject }

public partial class MainWindow : Window
{
    private const double WhatsAppTargetMb = 16;
    private const double MacTrafficLightInset = 80;

    // Ayar kapalıyken açılışta bir kez sorulur. Ağ yoksa bekleyen tek şey bu arka plan işi;
    // pencere zaten açılmış olur.
    private static readonly TimeSpan UpdateProbeTimeout = TimeSpan.FromSeconds(8);

    // Şeridin kapatıldığı sürüm. Ayar dosyası değil, yanına konan bir
    // işaret dosyası; UpdateSettings'e ait olduğu için oraya yazılmaz.
    internal const string DismissedNoticeFileName = "dismissed-update.txt";
    internal const string PlayerHistoryFileName = "player-history.json";
    internal const string LayoutFileName = "layout.json";

    private static readonly ConversionPlan ConversionDefaults = new();

    private static readonly MediaInfo HardwareProbeSource = new()
    {
        FilePath = "hardware-probe.mp4",
        FileSizeBytes = 200_000_000L,
        DurationSeconds = 120,
        Width = 1920,
        Height = 1080,
        Fps = 30,
        VideoCodec = "h264",
        TotalBitrateBps = 14_000_000
    };

    private MediaInfo? _info;

    /// <summary>
    /// Yoklamanin daraltilmamis hali. Baslik secici bunun uzerinden calisiyor: secim
    /// degisince daraltma her seferinde tam envanterden yeniden kuruluyor, bir onceki
    /// secimin daralttigi listeden degil.
    /// </summary>
    private MediaInfo? _probed;

    private bool _titleSyncing;
    private string? _sourceName;
    private EncodePlan? _autoPlan;
    private EncodePlan? _aiPlan;
    private CancellationTokenSource? _cts;
    private ComplexityProfile? _profile;
    private SceneMapAttempt? _sceneMap;
    private CancellationTokenSource? _probeCts;
    private SizeEstimate? _estimate;
    private IEncoderAvailability? _encoders;
    private DeferredEncoderAvailability? _planEncoders;
    private string? _probeStatusText;
    private double _predictedQuality;
    private StrategyAdvice? _advice;
    private string? _lastOutput;
    private string? _ffmpegVersion;
    private bool _syncing;
    private IReadOnlyList<string> _languageOrder = Array.Empty<string>();
    private IReadOnlyList<string> _themeOrder = Array.Empty<string>();
    private string _theme = PaletteCatalog.Default;

    // T61/K1: iki denetim birbirini sürüyor. Bayrak "bu değeri kullanıcı değil program
    // yazıyor" demektir; yazılan tarafın işleyicisi o turda hiçbir şey türetmez, böylece
    // döngü tek turda kapanır. İki bayrak var çünkü iki yön ayrı ayrı bastırılıyor.
    private bool _targetIsDerived;
    private bool _qualityIsDerived;
    private double _savedTargetMb = new UpdateSettings().TargetMb;
    private double _savedQualityTarget = new UpdateSettings().QualityTarget;
    private TaskCompletionSource<OvershootChoice>? _retryDecision;
    private RetryPrompt? _activeRetryPrompt;
    private bool _hardwareProbed;
    private bool _hardwareEncoderAvailable;
    private HardwareVerdict _hardwareVerdict = HardwareVerdict.NotProbed;
    private bool _motionReduced;

    private bool _aligningTitle;
    private ContentPresenter? _noticeLayer;
    private TransitioningContentControl? _contentHost;
    private Transitions? _noticeSlide;

    private const string ChromeHidden = "chrome-hidden";

    private bool _chromeShown = true;
    private HoverZone? _chromeZone;
    private DropVisual _dropVisual = DropVisual.Idle;
    private IReadOnlyList<string>? _dropBatch;
    private DispatcherTimer? _recalculateTimer;
    private DateTime _lastEstimatePulse = DateTime.MinValue;
    private bool _controlsReady;
    private bool _updateUiSyncing;
    private bool _settingsSyncing;
    private bool _kapandi;
    private string? _noticeVersion;
    private readonly DeveloperUnlock _developerUnlock = new();
    private AppliedUpdateNotice? _appliedNotice;
    private UpdateBadgeState _updateBadgeState = UpdateBadgeState.Checking;
    private CoreShare.ShareTargetTable _shareTargets = CoreShare.ShareTargetTable.Fallback;
    private CoreShare.IHttpTransport? _shareTransport;
    private ShareFlow? _shareFlow;
    private PanelHost? _preview;
    private Intent _intent = Intent.Sharing;
    private bool _chipSizeCapped = true;
    private bool _platformChip;
    private OutputContainer? _presetContainer;
    internal static readonly string[] AdvancedPresetCandidates =
    {
        "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow",
        "p1", "p2", "p3", "p4", "p5", "p6", "p7",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13"
    };
    internal static readonly string[] AdvancedTuneCandidates =
    {
        "film", "animation", "grain",
        "0", "1", "2"
    };
    internal static readonly int[] AdvancedCrfCandidates = { 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 40, 44 };
    internal static readonly int[] AdvancedAudioKbpsCandidates = { 64, 96, 128, 160, 192, 256, 320 };
    internal static readonly int[] AdvancedMinResolutionCandidates = { 480, 540, 720, 900, 1080, 1440, 2160 };
    internal static readonly double[] AdvancedMinFpsCandidates = { 24, 25, 30, 48, 50, 60 };

    private readonly string? _startupFile;

    private EncodePlan? ActivePlan => _aiPlan ?? _autoPlan;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupFile)
    {
        _startupFile = startupFile;
        AcilisIzi.Yaz("pencere-yapici");
        InitializeComponent();
        AcilisIzi.Yaz("xaml");
        _controlsReady = true;
        TxtLastCheck.Text = Say("main.about.check.never");
        TxtResult.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBlock.TextProperty || TxtResult.Text is not { Length: > 0 } yazi) return;
            var bagli = Bicim.Satir.Bagla(yazi);
            if (bagli != yazi) TxtResult.SetCurrentValue(TextBlock.TextProperty, bagli);
        };

        // T43: panel ana pencereye burada bağlanıyor. Kaynağı üreten çağrı tek yerde durur;
        // panel hangi motorun kare ürettiğini bilmez.
        _preview = new PanelHost(Preview, () => new EngineComparisonFrameSource());
        _preview.SetLanguage(Strings.Language);

        Player.PlayerTabIndex = () => PlayerTabIndex;
        Player.CurrentTabIndex = () => Tabs.SelectedIndex;
        Player.SelectTab = index => Tabs.SelectedIndex = index;
        Player.OpenSettings = () => Tabs.SelectedIndex = SettingsTabIndex;
        Player.EditRequested = OpenInEditorAsync;
        Player.AppSettingsItems = PlayerSettingsItems;
        PlayerAdvancedPanel.Player = Player;

        Player.OpenInNewWindow = target => NewWindowStarter(target);
        Player.HistoryPath = () => Path.Combine(
            Path.GetDirectoryName(SettingsPathOverride ?? UpdateSettings.DefaultPath) ?? AppContext.BaseDirectory,
            PlayerHistoryFileName);

        BuildLanguageSwitch();
        BuildThemeList();
        Strings.Changed += OnLanguageChanged;
        ShowSourceName();
        YongalariAdlandir();
        RefreshOutputNamePreview();
        RefreshPlatforms();

        ShowScrollOnlyOnHover(TxtCommand, TxtAiJson, TxtConvertCommand);

        ApplyMotion();
        ApplyStartupSize();
        PencereKonumunuIzle();
        PreparePanelEntrance();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        TitleBar.PointerPressed += OnTitleBarPointerPressed;
        TrackBackgroundDrag();
        TrackShellShortcuts();
        SekmeSeridiOdagi();
        TrackWindowMode();
        TepsiyiIzle();
        TitleBrand.SizeChanged += (_, _) => AlignTabsToTitle();
        SizeChanged += (_, _) => AlignTabsToTitle();
        TitleBarRight.SizeChanged += (_, _) => AlignTabsToTitle();
        Tabs.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Tabs.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault() is { } strip)
                strip.SizeChanged += (_, _) => AlignTabsToTitle();
        });
        LoadTitleBarLogo();
        SurumDugmesiniKur();
        AlignTabsToTitle();
        TrackChrome();
        TrackNoticeLayer();
        ShrinkColumns.Olcut = InfoGrid;
        SetupShellMenu();
        Tabs.SelectionChanged += (_, _) => ApplyWindowFrame();
        Tabs.SelectionChanged += (_, _) => KaydediciSekmesiSecildi();
        Tabs.SelectionChanged += (_, _) => DuzenleyiciSekmesiSecildi();
        Tabs.SelectionChanged += (_, _) => OynaticiSekmesiSecildi();


        if (OperatingSystem.IsMacOS())
        {
            WindowDecorations = WindowDecorations.Full;
            WindowButtons.IsVisible = false;
            TitleBarContent.Margin = new Thickness(MacTrafficLightInset, 0, TitleBarContent.Margin.Right, 0);
        }

        Watch(SliderTarget, RangeBase.ValueProperty, OnTargetSliderChanged);
        Watch(TxtTarget, TextBox.TextProperty, OnTargetTextChanged);
        Watch(SliderQualityTarget, RangeBase.ValueProperty, OnQualityTargetSliderChanged);
        Watch(TxtQualityTarget, TextBox.TextProperty, OnQualityTargetTextChanged);
        foreach (var toggle in ShrinkChoiceToggles())
            Watch(toggle, ToggleButton.IsCheckedProperty, OnOptionChanged);
        foreach (var check in new ToggleButton[] { ChkResolution, ChkFps, ChkFastGpu, ChkAdvKeepTracks, ChkAdvDropMetadata })
            Watch(check, ToggleButton.IsCheckedProperty, OnOptionChanged);
        Watch(ChkFastGpu, ToggleButton.IsCheckedProperty, OnFastGpuChanged);
        foreach (var toggle in new ToggleButton[] { ChkResolution, ChkWhatsAppCompatible })
            Watch(toggle, ToggleButton.IsCheckedProperty, RefreshFrameAndCodecLocks);
        for (var i = 0; i < FixedResolutionCandidates.Count; i++)
            FixedResolutionRadios()[i].Content = FixedResolutionCandidates[i].ToString(CultureInfo.InvariantCulture) + "p";

        Watch(PlanPanelRow, RowDefinition.HeightProperty, OnSplitterMoved);
        WatchPlanCeiling();

        InitializeAdvancedUi();
        foreach (var box in AdvBoxes())
            Watch(box, SelectingItemsControl.SelectedIndexProperty, OnOptionChanged);
        foreach (var box in AdvBoxes())
            Watch(box, SelectingItemsControl.SelectedIndexProperty, SaveSettings);
        foreach (var toggle in AdvStripToggles())
            Watch(toggle, ToggleButton.IsCheckedProperty, OnOptionChanged);
        foreach (var toggle in AdvStripToggles())
            Watch(toggle, ToggleButton.IsCheckedProperty, SaveSettings);
        foreach (var box in FilterBoxes())
            Watch(box, SelectingItemsControl.SelectedIndexProperty, OnFilterControlChanged);
        foreach (var check in FilterChecks().Concat<ToggleButton>(FilterRadios()))
            Watch(check, ToggleButton.IsCheckedProperty, OnFilterControlChanged);
        Watch(TxtAdvFilters, TextBox.TextProperty, OnFilterTextChanged);
        Watch(ChkFltAutoCrop, ToggleButton.IsCheckedProperty, OnAutoCropChanged);
        Watch(ChkAudioLoudnorm, ToggleButton.IsCheckedProperty, OnOptionChanged);
        Watch(ChkAudioDenoise, ToggleButton.IsCheckedProperty, OnOptionChanged);
        Watch(ChkAudioReplace, ToggleButton.IsCheckedProperty, OnOptionChanged);
        Watch(CmbAudioGain, SelectingItemsControl.SelectedIndexProperty, OnOptionChanged);
        Watch(CmbBurnSubtitle, SelectingItemsControl.SelectedIndexProperty, OnBurnChoiceChanged);
        Watch(CmbBurnFont, SelectingItemsControl.SelectedIndexProperty, OnOptionChanged);
        Watch(CmbBurnSize, SelectingItemsControl.SelectedIndexProperty, OnOptionChanged);
        Watch(ChkBurnBox, ToggleButton.IsCheckedProperty, OnOptionChanged);

        Watch(SliderQuality, RangeBase.ValueProperty, OnQualitySliderChanged);
        Watch(TxtQuality, TextBox.TextProperty, OnQualityTextChanged);
        foreach (var box in new[] { CmbQualityMode, CmbConvertCodec })
            Watch(box, SelectingItemsControl.SelectedIndexProperty, OnQualityRangeChanged);
        foreach (var box in new[] { CmbContainer, CmbResolution, CmbConvertFps, CmbConvertAudio, CmbConvertChannels, CmbConvertSampleRate, CmbConvertGifLoop, CmbConvertGifColors, CmbConvertGifDither })
            Watch(box, SelectingItemsControl.SelectedIndexProperty, OnConvertChanged);
        Watch(ChkConvertLoudnorm, ToggleButton.IsCheckedProperty, OnConvertChanged);
        Watch(ChkConvertDropMetadata, ToggleButton.IsCheckedProperty, OnConvertChanged);
        Watch(ChkConvertSingleFrame, ToggleButton.IsCheckedProperty, OnConvertChanged);
        Watch(CmbConvertAudioVisual, SelectingItemsControl.SelectedIndexProperty, OnSesGoruntusuDegisti);
        Watch(ChkConvertAudioReplace, ToggleButton.IsCheckedProperty, OnConvertChanged);
        Watch(ChkConvertAudioShortest, ToggleButton.IsCheckedProperty, OnConvertChanged);
        Watch(CmbConvertBurn, SelectingItemsControl.SelectedIndexProperty, OnConvertBurnChanged);
        Watch(CmbConvertBurnFont, SelectingItemsControl.SelectedIndexProperty, OnConvertChanged);
        Watch(CmbConvertBurnSize, SelectingItemsControl.SelectedIndexProperty, OnConvertChanged);
        Watch(CmbConvertBurnColor, SelectingItemsControl.SelectedIndexProperty, OnConvertChanged);
        Watch(ChkConvertBurnBox, ToggleButton.IsCheckedProperty, OnConvertChanged);
        foreach (var field in new[] { TxtCustomResolution, TxtCustomFps, TxtAudioBitrate, TxtTrimStart, TxtTrimEnd, TxtConvertExtraArgs })
            Watch(field, TextBox.TextProperty, OnConvertChanged);
        InitializeConvertFilterUi();
        InitializeConvertEncoderUi();
        InitializeManualCropUi();

        Watch(ChkAutoUpdate, ToggleButton.IsCheckedProperty, OnAutoUpdateChanged);
        foreach (var radio in new[] { RbTrimEnd, RbTrimStart, RbTrimBoth })
            Watch(radio, ToggleButton.IsCheckedProperty, RefreshTrimRange);
        Watch(TxtDefaultTargetMb, TextBox.TextProperty, OnDefaultTargetMbChanged);
        Watch(CmbLanguage, SelectingItemsControl.SelectedIndexProperty, OnLanguageChosen);
        Watch(CmbTheme, SelectingItemsControl.SelectedIndexProperty, OnThemeChosen);
        WatchSystemThemeAndMotion();
        Watch(RbOutputFixed, ToggleButton.IsCheckedProperty, OnOutputFolderModeChanged);
        Watch(TxtOutputFolder, TextBox.TextProperty, SaveAppSettings);
        Watch(TxtOutputName, TextBox.TextProperty, OnOutputNameChanged);
        Watch(ChkAdvancedDefaultOpen, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkFollowRecording, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkNotifyWhenDone, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkFinishSound, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkPreventSleep, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkMinimizeToTray, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkKeepDates, ToggleButton.IsCheckedProperty, SaveAppSettings);
        CmbParallelJobs.ItemsSource = ParallelJobChoices(Environment.ProcessorCount);
        CmbParallelJobs.SelectedIndex = 0;
        Watch(CmbParallelJobs, SelectingItemsControl.SelectedIndexProperty, SaveAppSettings);
        InitializeEncodeLoadUi();
        RefreshFinishSoundFile();
        Player.Opened += OnPlayerOpened;
        Watch(ChkAdvKeepTracks, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkAdvDropMetadata, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkAdvMeasureQuality, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkAdvSearchByMeasure, ToggleButton.IsCheckedProperty, SaveAppSettings);
        Watch(ChkConvertDropMetadata, ToggleButton.IsCheckedProperty, SaveAppSettings);
        foreach (var box in new[] { CmbConvertGifLoop, CmbConvertGifColors, CmbConvertGifDither })
            Watch(box, SelectingItemsControl.SelectedIndexProperty, SaveAppSettings);
        Watch(RbFfmpegManual, ToggleButton.IsCheckedProperty, OnFfmpegPathModeChanged);
        Watch(TxtFfmpegPath, TextBox.TextProperty, OnFfmpegPathTextChanged);
        Watch(TxtOpenSubtitlesKey, TextBox.TextProperty, SaveAppSettings);
        Watch(TxtOpenSubtitlesUser, TextBox.TextProperty, SaveAppSettings);
        InitUserPresets();
        InitConvertPresets();
        InitConvertTags();
        BuildShareTargetStrip();
        Watch(CmbShareRetention, SelectingItemsControl.SelectedIndexProperty, SaveSettings);
        foreach (var control in new SelectingItemsControl[]
                 {
                     CmbQualityMode,
                     CmbContainer, CmbConvertCodec, CmbResolution, CmbConvertFps, CmbConvertAudio
                 })
            Watch(control, SelectingItemsControl.SelectedIndexProperty, SaveSettings);
        foreach (var control in ShrinkChoiceToggles())
            Watch(control, ToggleButton.IsCheckedProperty, SaveSettings);
        foreach (var control in new ToggleButton[] { ChkResolution, ChkFps })
            Watch(control, ToggleButton.IsCheckedProperty, SaveSettings);
        foreach (var control in new TextBox[]
                 {
                     TxtTarget, TxtQualityTarget, TxtQuality, TxtCustomResolution, TxtCustomFps,
                     TxtAudioBitrate, TxtTrimStart, TxtTrimEnd
                 })
            Watch(control, TextBox.TextProperty, SaveSettings);

        RefreshQualityTargetAvailability();
        OzelDonusturAlanlari();
        RefreshChipDerivation();
        RefreshSectionSummaries();
        // Sınır cümlesi ölçüm koşmadan da ekranda durur; sonda burada çağrılmıyor.
        ShowPerformanceResult(PerformanceCheckResult.NotMeasured);
        if (_startupFile is not null) Tabs.SelectedIndex = PlayerTabIndex;
        Opened += OnWindowLoaded;
        Opened += (_, _) => OynaticiSekmesiSecildi();
        IlkBoyayiBekle();
        AcilisGoruntusunuBildir();
        AcilisIzi.Yaz("yapici-bitti");
    }

    /// <summary>
    /// "VidShrink varsayilan degil" onerisini bildirim yiginina ekler. Serit yalniz
    /// <see cref="Integration.DefaultAppSuggestionBar.Wanted"/> dogru derse eklenir: makine
    /// Windows olacak, uzantilari baska bir program aciyor olacak ve oneri daha once
    /// reddedilmemis olacak. "Bir daha sorma" dendiginde ret <c>settings.json</c>'a yazilir,
    /// dolayisiyla bir sonraki acilista bu kosul tutmaz ve serit hic uretilmez.
    ///
    /// <para>Cagri <c>OnWindowLoaded</c> icinde durur, yapicida degil: yerlesimi pimleyen
    /// bassiz olcum pencereyi hic gostermedigi icin o olay orada ates almaz ve pimlenen
    /// yukseklik bu seritten etkilenmez. Serit gercek kullanicinin gordugu acilista
    /// eklenir.</para>
    /// </summary>
    private void ShowDefaultAppSuggestion()
    {
        if (!OperatingSystem.IsWindows() || Tasinabilir) return;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return;
        if (!Integration.DefaultAppSuggestionBar.Wanted(executable, ShellIntegration.DefaultPlayerExtensions, SettingsPathOverride)) return;
        if (AppliedNotice.Parent is not Panel host) return;
        if (host.Children.OfType<Integration.DefaultAppSuggestionBar>().Any()) return;

        host.Children.Add(new Integration.DefaultAppSuggestionBar(SettingsPathOverride));
    }

    private TimeSpan Motion(string key, double fallbackMs)
        => this.TryFindResource(key, out var value) && value is TimeSpan span ? span : TimeSpan.FromMilliseconds(fallbackMs);

    private double Scalar(string key, double fallback)
        => this.TryFindResource(key, out var value) && value is double number ? number : fallback;

    private IBrush? Paint(string key)
        => this.TryFindResource(key, out var value) ? value as IBrush : null;

    private ControlTheme? Look(string key)
        => this.TryFindResource(key, out var value) ? value as ControlTheme : null;

    private Geometry? Draw(string key)
        => this.TryFindResource(key, out var value) ? value as Geometry : null;

    private void ApplyStartupSize()
    {
        try
        {
            var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (screen is null) return;

            var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
            var fit = StartupFit(
                new Size(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling),
                Scalar("WindowWorkAreaShare", 0.9),
                new Size(Scalar("WindowPreferredWidth", Width), Scalar("WindowPreferredHeight", Height)),
                new Size(MinWidth, MinHeight));

            MinWidth = fit.Min.Width;
            MinHeight = fit.Min.Height;
            Width = fit.Size.Width;
            Height = fit.Size.Height;
        }
        catch (Exception)
        {
            // the declared Width/Height in markup stay in force
        }
    }

    /// <summary>
    /// Açılış boyutu: tercih boyutu çalışma alanının payına sığdırılır, ama taban boyutun altına
    /// inmez. Taban da çalışma alanını aşamaz: 1920x1080 %150'de mantıksal alan 1280x688,
    /// 720'lik taban pencerenin altını görev çubuğunun arkasına itiyordu.
    /// </summary>
    internal static (Size Min, Size Size) StartupFit(Size workArea, double share, Size preferred, Size minimum)
    {
        var min = new Size(Math.Min(minimum.Width, workArea.Width), Math.Min(minimum.Height, workArea.Height));
        var size = new Size(
            Math.Max(min.Width, Math.Min(preferred.Width, workArea.Width * share)),
            Math.Max(min.Height, Math.Min(preferred.Height, workArea.Height * share)));
        return (min, size);
    }

    private Control[] EntrancePanels() => new Control[] { SourcePanel, TargetPanel, PlanPanel, OutputPanel, OutcomePanel, AiPanel };

    private void PreparePanelEntrance()
    {
        var entering = _motionReduced ? "enter-flat" : "enter";
        foreach (var panel in EntrancePanels()) panel.Classes.Add(entering);
    }

    private void PlayPanelEntrance()
    {
        var entering = _motionReduced ? "enter-flat" : "enter";
        var step = this.TryFindResource("Stagger", out var stagger) && stagger is TimeSpan span ? span : TimeSpan.FromMilliseconds(40);
        var panels = EntrancePanels();
        for (var index = 0; index < panels.Length; index++)
        {
            var panel = panels[index];
            if (index == 0) panel.Classes.Remove(entering);
            else DispatcherTimer.RunOnce(() => panel.Classes.Remove(entering), step * index);
        }
    }

    private readonly Dictionary<Control, int> _fadeGeneration = new();
    private readonly HashSet<Control> _fadingOut = new();

    /// <summary>
    /// Uygulama açılışta Türkçe koşuyor (<c>OnWindowLoaded</c>). Ölçüm başsız koştuğu için
    /// o olay ateşlenmiyor ve pencere İngilizce ölçülüyordu; Türkçe karşılıkların
    /// kutulara sığıp sığmadığı bu kapıdan geçmeden ölçülemez.
    /// </summary>
    internal void UseTurkish() => UseLanguage("tr");

    internal void SettleFades()
    {
        foreach (var control in _fadingOut) control.IsVisible = false;
        _fadingOut.Clear();
    }

    private void ShowScrollOnlyOnHover(params TextBox[] boxes)
    {
        foreach (var box in boxes)
        {
            ApplyScrollAffordance(box, false);
            box.PointerEntered += (_, _) => ApplyScrollAffordance(box, true);
            box.PointerExited += (_, _) => ApplyScrollAffordance(box, false);
        }
    }

    private static void ApplyScrollAffordance(TextBox box, bool hovered)
    {
        var shown = hovered ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        ScrollViewer.SetVerticalScrollBarVisibility(box, shown);
        ScrollViewer.SetHorizontalScrollBarVisibility(
            box,
            box.TextWrapping == TextWrapping.Wrap ? ScrollBarVisibility.Disabled : shown);
    }


    /// <summary>
    /// Bildirim yigini icerikle ayni gozde, uste yaslanmis bir katman: oynaticida
    /// goruntunun ustunde cikiyor ki uyari her acildiginda oynatici kisalmasin. Diger
    /// sekmelerde ayni katman panel basliklarini ortuyordu; "varsayilan program degil"
    /// serit Kaynak ve Cikti basliklarini kalici olarak kapatiyordu. Oynatici disinda
    /// icerik yiginin boyu kadar asagi iniyor, inis hareket azaltilmamissa kayarak.
    /// </summary>
    private void TrackNoticeLayer()
    {
        Tabs.TemplateApplied += (_, e) =>
        {
            _noticeLayer = e.NameScope.Find<ContentPresenter>("NoticeLayer");
            _contentHost = e.NameScope.Find<TransitioningContentControl>("SelectedContentHost");
            if (_noticeLayer is not null) _noticeLayer.SizeChanged += (_, _) => PushContentBelowNotices();
            _noticeSlide = new Transitions
            {
                new ThicknessTransition { Property = PaddingProperty, Duration = Motion("MotionBase", 240), Easing = new CubicEaseOut() }
            };
            PushContentBelowNotices();
        };
        Tabs.SelectionChanged += (_, _) => PushContentBelowNotices();
    }

    internal static double NoticePush(bool playerTab, double noticeHeight) => playerTab ? 0 : Math.Max(0, noticeHeight);

    private void PushContentBelowNotices()
    {
        if (_noticeLayer is null || _contentHost is null) return;
        var top = NoticePush(Tabs.SelectedIndex == PlayerTabIndex, _noticeLayer.Bounds.Height);
        if (Math.Abs(_contentHost.Padding.Top - top) < 0.5) return;
        _contentHost.Transitions = Classes.Contains("reduced-motion") ? null : _noticeSlide;
        _contentHost.Padding = new Thickness(0, top, 0, 0);
    }

    private void EnsureFade(Control control)
    {
        if (control.Transitions is not null) return;
        control.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = Motion("MotionBase", 240), Easing = new CubicEaseOut() }
        };
    }

    /// <summary>
    /// Yüzeyi saydamlıkla gösterir ya da gizler. Hareket azaltma açık pencerede geçiş kurulmaz,
    /// yüzey hemen görünür ya da gizlenir.
    /// </summary>
    internal void Fade(Control control, bool visible)
    {
        if (Classes.Contains("reduced-motion"))
        {
            _fadeGeneration[control] = NextFadeGeneration(control);
            _fadingOut.Remove(control);
            control.Transitions = null;
            control.Opacity = 1;
            control.IsVisible = visible;
            return;
        }
        EnsureFade(control);
        if (visible)
        {
            _fadeGeneration[control] = NextFadeGeneration(control);
            _fadingOut.Remove(control);
            if (control.IsVisible && control.Opacity > 0.99) return;
            control.Opacity = 0;
            control.IsVisible = true;
            Dispatcher.UIThread.Post(() => control.Opacity = 1, DispatcherPriority.Loaded);
            return;
        }

        if (!control.IsVisible) return;
        var generation = _fadeGeneration[control] = NextFadeGeneration(control);
        _fadingOut.Add(control);
        control.Opacity = 0;
        DispatcherTimer.RunOnce(() =>
        {
            if (_fadeGeneration.TryGetValue(control, out var current) && current != generation) return;
            _fadingOut.Remove(control);
            control.IsVisible = false;
        }, Motion("MotionBase", 240));
    }

    private int NextFadeGeneration(Control control)
        => _fadeGeneration.TryGetValue(control, out var current) ? current + 1 : 1;

    private void Pulse(Control control, bool throttled)
    {
        if (_motionReduced) return;
        var now = DateTime.UtcNow;
        if (throttled && now - _lastEstimatePulse < Motion("MotionSlow", 360) + Motion("MotionBase", 240)) return;
        _lastEstimatePulse = now;
        control.Opacity = 0.35;
        DispatcherTimer.RunOnce(() => control.Opacity = 1, Motion("MotionFast", 160));
    }

    private void SetStage(TextBlock target, string text)
    {
        if (target.Text == text) return;
        target.Text = text;
        Pulse(target, false);
    }

    private void ScheduleRecalculate()
    {
        _recalculateTimer ??= new DispatcherTimer { Interval = Motion("MotionFast", 160) };
        _recalculateTimer.Stop();
        _recalculateTimer.Tick -= OnRecalculateTick;
        _recalculateTimer.Tick += OnRecalculateTick;
        _recalculateTimer.Start();
    }

    private void OnRecalculateTick(object? sender, EventArgs e)
    {
        _recalculateTimer?.Stop();
        Recalculate();
    }

    private void LoadTitleBarLogo()
    {
        if (!this.TryFindResource("AppIconUri", out var uri) || uri is not string source)
        {
            AppLogo.IsVisible = false;
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri(source));
                var logo = new Bitmap(stream);
                Dispatcher.UIThread.Post(() =>
                {
                    AppLogo.Source = logo;
                    UpdateNoticeIcon.Source = logo;
                    AppLogo.IsVisible = true;
                });
            }
            catch (Exception)
            {
                Dispatcher.UIThread.Post(() => AppLogo.IsVisible = false);
            }
        });
    }

    private static void Watch(AvaloniaObject target, AvaloniaProperty property, Action handler)
        => target.PropertyChanged += (_, args) => { if (args.Property == property) handler(); };

    internal int AcilisSayisi { get; private set; }

    private async void OnWindowLoaded(object? sender, EventArgs e)
    {
        Opened -= OnWindowLoaded;
        AcilisSayisi++;
        try
        {
            AcilisIzi.Yaz("pencere-yuklendi");
            UpdateMaximizeGlyph();
            ApplyWindowFrame();
            WindowShell.Margin = OffScreenMargin;
            var recovery = CheckSettingsRecovery();
            var settings = UpdateSettings.Load(SettingsPathOverride);
            _settingsSyncing = true;
            UseLanguage(ResolveLanguage(settings.Language, CultureInfo.CurrentUICulture.Name));
            RestoreSplitterSettings();
            InitializeShareUi();
            RestoreSettings(settings);
            var appSettings = AppSettings.Load(SettingsPathOverride);
            RestoreAppSettings(appSettings);
            ShowSettingsRecovery(recovery);
            InitializeUpdateUi(settings);
            AcilisIzi.Yaz("ayarlar");
            PlayPanelEntrance();
            AcilisIzi.Yaz("giris-canlandirmasi");
            await LoadStartupFileAsync();

            // Varsayilan uygulama onerisi ile surum sorusu ilk karenin onunden alindi:
            // ikisi de kullanicinin acmak istedigi dosyayla ilgisiz, ikisi de acilis
            // yolunda arayuz is parcaciginda durur (oneri uzanti basina bir
            // AssocQueryString, surum sorusu bir HttpClient kurulumu). Sirayi degistirmek
            // gorunurlerini degistirmez: serit de bildirim de ayni acilista belirir.
            if (!NewWindow.Active)
            {
                ShowDefaultAppSuggestion();
                AcilisIzi.Yaz("varsayilan-oneri");
                if (UpdateCheck.AutoUpdateEnabled(settings)) _ = OtomatikGuncellemeyiBaslatAsync();
                else _ = CheckForUpdateAsync();
                _ = KisayolSimgesiniTazeleAsync();
            }
            await LoadFfmpegVersionAsync();
            await ProbeHardwareEncodersAsync();
        }
        catch (Exception ex)
        {
            ReportSourceError($"{Say("main.error.startup")}: {ex.Message}");
        }
        finally
        {
            Playback.AcilisMotoru.Birak();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && OperatingSystem.IsWindows())
        {
            if (WindowState == WindowState.FullScreen) WindowDecorationsTheme = SessizPencereBasligi.Tema;
            else ClearValue(WindowDecorationsThemeProperty);
        }
        if (change.Property == ClientSizeProperty) NormalKonumuAl();
        if (!_controlsReady) return;
        if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Maximized) Kip.KucukBitir();
            UpdateMaximizeGlyph();
            ApplyWindowFrame();
        }
        else if (change.Property == OffScreenMarginProperty) WindowShell.Margin = OffScreenMargin;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Strings.Changed -= OnLanguageChanged;
        TepsiyiKaldir();
        _probeCts?.Cancel();
        _cts?.Cancel();
        // Kaynak burada kapanır: pencere kapanırken öksüz ffmpeg kalmaz.
        _preview?.Dispose();
        PencereKonumunuKaydet();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _kapandi = true;
        HoldSleep(false);
        ReleaseSystemThemeAndMotion();
        base.OnClosed(e);
    }

    /// <summary>
    /// Ust seridin uc blogu ayni 30 pikseli paylasiyor: marka, sekme seridi ve sag grup.
    /// Sekme seridi kendi sablonunda oldugu icin izgara onlari ayiramiyor; dar pencerede
    /// sag grup seridin uzerine biniyor ve en sonra bildirildigi icin tiklari yutuyordu.
    ///
    /// <para>Yer yetmeyince parcalar sirayla cekiliyor: once destek ve GitHub dugmeleri
    /// (ikisi de Hakkinda'da), sonra guncelleme rozetinin yazisi (nokta ve ipucu kalir),
    /// en son dil dugmeleri (dil Ayarlar'da). Her turda sag grup her sey acikken yeniden
    /// olculuyor; rozet acilistan sonra belirdigi ve dil seridi degistirdigi icin onbellege
    /// alinmis bir genislik bayatliyordu. Sag grubun ve seridin boyu degisince yeniden
    /// kosuyor.</para>
    /// <para>Dil dugmeleri de cekildigi halde yer yetmezse sekmeler simgesiz dizilir
    /// (<c>compact</c> sinifi); yalniz sag grubu daraltmak el 1136'da Gelismis acikken
    /// son sekmeyi pencere dugmelerinin 9 px altina sokuyordu. Serit genisligi kararda
    /// hep simgeli haliyle hesaplanir, yoksa kademe iki durum arasinda gidip gelir.
    /// Simgeli genislik sinifa gore degil son yerlesime gore bulunur
    /// (<see cref="SimgesizDizildi"/>): sinif degistikten sonra yerlesim kosmadan gelen bir
    /// <c>SizeChanged</c> cagrisi sinifi yeni, genisligi eski haliyle okuyordu; simgesiz
    /// genislik simgeli sanilinca kademe 3'e dusuyor, sonraki cagri 4'e geri aliyordu.
    /// 1024 ekranli CI kosucusunda el 1152'de son sekme sag gruba 7 px kaliyordu.</para>
    /// <para>Simgesiz sekmeler de sigmazsa (kucuk kip, 1920 alanin ucte biri %150'de 738 px)
    /// marka yazisi cekilir ve sekmeler sol kenara yaslanir (kademe 5). Marka genisligi her
    /// turda gizliyken de olculur, yoksa gizlenen marka sol payi sifirlayip kademeyi geri aliyordu.</para>
    /// <para>Son sekme ile sag grup arasinda en az <c>SpaceLg</c> kalir; hesap yalniz
    /// ortusmeyi onluyordu ve el 1136'da son sekme pencere dugmelerine yaklasik 10 px
    /// yaklasiyordu.</para>
    /// </summary>
    private void AlignTabsToTitle()
    {
        if (_aligningTitle) return;
        _aligningTitle = true;
        try
        {
            var gap = TitleBarContent.ColumnSpacing;
            TitleBrand.IsVisible = true;
            TitleBrand.Measure(Size.Infinity);
            var marka = TitleBrand.DesiredSize.Width + gap;
            var sol = TitleBarContent.Margin.Left + marka;

            var serit = Tabs.GetVisualDescendants().OfType<ItemsPresenter>().FirstOrDefault()?.Bounds.Width ?? 0;
            if (serit <= 0 || TitleBarLayer.Bounds.Width <= 0)
            {
                Tabs.Padding = new Thickness(sol, 0, 0, 0);
                return;
            }

            var enAz = this.TryFindResource("SpaceLg", out var pay) && pay is double d ? d : 0;
            var ikon = this.TryFindResource("IconSizeSm", out var boy) && boy is double b ? b : 0;
            var ara = this.TryFindResource("SpaceSm", out var bosluk) && bosluk is double a ? a : 0;
            var sekmeler = Tabs.Items.OfType<TabItem>().Where(t => t.IsVisible).ToList();
            var tamSerit = serit + sekmeler.Count(t => SimgesizDizildi(t, ikon)) * (ikon + ara);
            var bos = TitleBarLayer.Bounds.Width - sol - tamSerit - TitleBarContent.Margin.Right - enAz;
            var kademe = TitleBarStage(bos, out var sagEnAz);
            if (kademe > 3 && sagEnAz > bos + sekmeler.Count * (ikon + ara)) kademe = 5;
            BtnSponsor.IsVisible = kademe < 1;
            BtnGitHub.IsVisible = kademe < 1;
            TxtUpdateBadge.IsVisible = kademe < 2;
            LangSwitch.IsVisible = kademe < 3;
            Tabs.Classes.Set("compact", kademe > 3);
            TitleBrand.IsVisible = kademe < 5;
            Tabs.Padding = new Thickness(kademe < 5 ? sol : TitleBarContent.Margin.Left, 0, 0, 0);
            InvalidateTitleBarRight();
        }
        finally
        {
            _aligningTitle = false;
        }
    }

    /// <summary>
    /// Sekmenin son yerlesimde simgesiz dizilip dizilmedigi: baslik yiginiyla yazinin
    /// genislik farki simgeye yetmiyorsa simge o yerlesimde yer tutmadi. Sinif degil
    /// yerlesim okunur; seridin genisligi de ayni yerlesimden geliyor.
    /// </summary>
    private static bool SimgesizDizildi(TabItem sekme, double ikon)
    {
        var yazi = sekme.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_ContentPresenter");
        return yazi?.GetVisualParent() is StackPanel yigin && yigin.Bounds.Width - yazi.Bounds.Width < ikon;
    }

    /// <summary>
    /// Sag grubun kacinci kademede sigdigi: 0 hepsi, 1 baglar cekilmis, 2 rozet yazisi da,
    /// 3 dil dugmeleri de, 4 o da yetmiyor ve sekmeler simgesiz. Parcalar olcum icin gecici olarak aciliyor; ayni cagride geri
    /// kapandigi icin ekrana hic cizilmiyorlar. <paramref name="enAz"/> her sey cekilmisken sag grubun genisligi.
    /// </summary>
    private int TitleBarStage(double bos, out double enAz)
    {
        BtnSponsor.IsVisible = true;
        BtnGitHub.IsVisible = true;
        TxtUpdateBadge.IsVisible = true;
        LangSwitch.IsVisible = true;
        InvalidateTitleBarRight();
        TitleBarRight.Measure(Size.Infinity);

        var tam = TitleBarRight.DesiredSize.Width;
        var baglar = BtnSponsor.DesiredSize.Width + BtnGitHub.DesiredSize.Width;
        var rozet = BtnUpdateBadge.IsVisible
            ? TxtUpdateBadge.DesiredSize.Width + ((TxtUpdateBadge.Parent as StackPanel)?.Spacing ?? 0)
            : 0;
        var dil = LangSwitch.DesiredSize.Width;
        enAz = tam - baglar - rozet - dil;

        if (tam <= bos) return 0;
        if (tam - baglar <= bos) return 1;
        if (tam - baglar - rozet <= bos) return 2;
        if (tam - baglar - rozet - dil <= bos) return 3;
        return 4;
    }

    private void InvalidateTitleBarRight()
    {
        foreach (var parca in TitleBarRight.GetVisualDescendants().OfType<Avalonia.Layout.Layoutable>()) parca.InvalidateMeasure();
        TitleBarRight.InvalidateMeasure();
    }


    /// <summary>
    /// Üst şerit yalnız <b>oynatıcı sekmesinde</b> kendiliğinden gizlenir: işaretçi
    /// pencerenin ilk <c>PlaybackTopRevealZone</c> pikseline (sabit 48, pencere boyuyla
    /// büyümez) girdiğinde belirir, şeridi terk edince kaybolur. Kural alt şeritle aynı <see cref="HoverZone"/> ve aynı iki belirteç
    /// (<c>PlaybackStripShowDelay</c> / <c>PlaybackStripHideDelay</c>): kaybolma gecikmeli,
    /// duraklatılmışken şerit açık kalır. Şerit içeriğin üstünde bir katman olduğu için
    /// görünüp kaybolurken hiçbir şey yer değiştirmiyor.
    ///
    /// <para>Diğer sekmelerde şerit sabit durur. Gizlenme oynatıcının kendi gereği —
    /// görüntünün üstünü kapatmasın diye; küçültme, dönüştürme, kaydedici ve ayarlar
    /// sayfalarında ise okurken kaybolan bir sekme şeridi oluyordu.</para>
    /// </summary>
    private void TrackChrome()
    {
        AddHandler(PointerMovedEvent, OnChromePointerMoved, RoutingStrategies.Tunnel);
        PointerExited += (_, _) => ChromeZone.PointerGone();
        Tabs.SelectionChanged += (_, _) => ApplyChromeMode();
        Player.PlayingChanged += (_, _) => ApplyChromeMode();
        Player.MediaTitleMenuChanged += ApplyChromeMode;
        ApplyChromeMode();
    }

    internal bool ChromeHidesItself => Tabs.SelectedIndex == PlayerTabIndex && Player.LoadedPath is not null;

    internal bool ChromeShown => _chromeShown;

    internal HoverZone ChromeZone
    {
        get
        {
            if (_chromeZone is not null) return _chromeZone;
            _chromeZone = new HoverZone(0, () => ChromeDelay("PlaybackStripShowDelay"), () => ChromeDelay("PlaybackStripHideDelay"), ShowChrome);
            _chromeZone.Reset(_chromeShown);
            return _chromeZone;
        }
    }

    private TimeSpan ChromeDelay(string key) => this.FindResource(key) is TimeSpan span ? span : TimeSpan.Zero;

    internal double TopRevealZone => this.TryFindResource("PlaybackTopRevealZone", out var bant) && bant is double d ? d : 0;

    private void ApplyChromeMode()
    {
        ChromeZone.Hold(!ChromeHidesItself || Player.MediaTitleMenuOpen);
    }

    private void OnChromePointerMoved(object? sender, PointerEventArgs e)
    {
        ChromeZone.PointerWithin(e.GetPosition(this).Y <= Math.Max(TitleBar.Height, TopRevealZone) || Player.OverMediaTitle(e));
        ApplyChromeMode();
    }

    private void ShowChrome(bool show)
    {
        if (show == _chromeShown) return;
        _chromeShown = show;
        if (show) Classes.Remove(ChromeHidden);
        else Classes.Add(ChromeHidden);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount % 2 == 0)
        {
            PencereDongusu();
            e.Handled = true;
            return;
        }
        if (EtkilesimliDenetim.Icinde(e.Source as Visual, TitleBar)) return;
        ZeminBas(e);
    }

    /// <summary>
    /// Tam ekranda, küçük kipte ve oynatıcı sekmesinde kabuk kenarlığını kaldırır; kural her
    /// sekmede aynı. Windows kendi pencerelerinde de böyle yapar ve sebebi süs değil: 1 piksellik kenarlık kalırsa ekranın sağ üst pikseli
    /// kapatma düğmesinin değil kenarlığın üstüne düşer, köşenin kolay hedef olma kazancı gider.
    /// Normal boyutta kenarlık ve yuvarlama belirteçlerden geri gelir.
    /// </summary>
    private void ApplyWindowFrame()
    {
        var ekraniDolduruyor = WindowState is WindowState.Maximized or WindowState.FullScreen;
        WindowShell.BorderThickness = ekraniDolduruyor || Kip.Kucuk || Tabs.SelectedIndex == PlayerTabIndex
            ? new Thickness(0)
            : this.TryFindResource("BorderThin", out var border) && border is Thickness thickness
                ? thickness
                : new Thickness(1);
        WindowShell.CornerRadius = ekraniDolduruyor
            ? new CornerRadius(0)
            : this.TryFindResource("RadiusControl", out var radius) && radius is CornerRadius corner
                ? corner
                : default;
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestore(object? sender, RoutedEventArgs e) => ToggleMaximizeRestore();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
    private void ToggleMaximizeRestore() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void UpdateMaximizeGlyph()
        => MaximizeGlyph.Data = Draw(WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize");

    private void OnOpenGitHub(object? sender, RoutedEventArgs e) => OpenExternal(this.TryFindResource("LinkGitHub", out var url) ? url as string : null);
    private void OnOpenSponsor(object? sender, RoutedEventArgs e) => OpenExternal(this.TryFindResource("LinkSponsor", out var url) ? url as string : null);

    private void OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { TxtSystemStatus.Text = $"{Say("main.error.link")}: {ex.Message}"; }
    }

    /// <summary>
    /// Koddan yazılan her metnin geçtiği kapı: karşılık sözlükten anahtarla okunur, sonra
    /// yürürlükteki dilin büyük harf kuralından geçer. Biçimlemedeki <c>{loc:Text}</c> bağı
    /// da tam olarak aynı iki adımı uyguluyor, böylece iki yol aynı metni veriyor.
    /// </summary>
    private static string Say(string key) => LanguageCatalog.Display(Strings.Get(key));

    private static string Say(string key, params object?[] args)
        => LanguageCatalog.Display(Strings.Get(key, args));

    internal static string SimdiSatiri(string deger)
        => string.Format(Strings.Culture, Say("main.advanced.now"), deger);

    /// <summary>
    /// Aynı geçit, ama dili çağıran seçiyor. Ölçüler iki dilin satırını yan yana koyabilsin
    /// diye var; arayüz her zaman yürürlükteki dili geçiriyor.
    /// </summary>
    /// <summary>
    /// Sayı biçimlemesi tek yerden geçiyor. Kültür yürürlükteki dilden gelir:
    /// İngilizce arayüzde <c>15.6 MB</c>, Türkçe arayüzde <c>15,6 MB</c>. Sabit
    /// <see cref="CultureInfo.InvariantCulture"/> ikisini de noktaya çeviriyordu; C#
    /// ara değer dizgesi (<c>$"{x:0.0}"</c>) ise makinenin kültürünü kullandığı için
    /// İngilizce arayüzde bile virgül yazıyordu. İki yanlış da bu geçitle kapanıyor.
    /// </summary>
    internal static string Num(double value, string format) => value.ToString(format, Strings.Culture);

    /// <summary>
    /// Yüzde işaretinin yeri de kültürden gelir: İngilizcede sayının sağında
    /// (<c>3.7%</c>), Türkçede solunda (<c>%3,7</c>). Oran verilir, yüzle çarpmayı
    /// biçim yapar. Gövde <see cref="Bicim.Yuzde"/>'de; burası yalnız kültürü veriyor.
    /// </summary>
    internal static string Percent(double ratio) => Bicim.Yuzde.Isaretli(ratio, Strings.Culture);

    private static string Speak(string language, string key, params object?[] args)
        => LanguageCatalog.Title(Strings.GetIn(language, key, args), language);

    /// <summary>
    /// Dil düğmeleri <c>Locales</c> altındaki klasörlerden kuruluyor; kodda hiçbir dil adı
    /// yazılı değil. Üçüncü bir dil klasörü kopyalandığında düğmesi kendiliğinden belirir
    /// ve her dil kendi adını kendi dosyasında taşır.
    /// </summary>
    private void BuildLanguageSwitch()
    {
        LangSwitch.Children.Clear();

        foreach (var language in Strings.ShortcutLanguages)
        {
            var button = new Button
            {
                Content = Strings.PeekIn(language, "main.language.name"),
                Theme = Look("LanguageButton"),
                Tag = language
            };

            AutomationProperties.SetName(button, Strings.PeekIn(language, "main.language.name"));
            button.Click += (_, _) => UseLanguage(language);
            LangSwitch.Children.Add(button);
        }

        BuildLanguageList();
        MarkChosenLanguage();
        ApplyFlowDirection();
    }

    private void ApplyFlowDirection()
    {
        FlowDirection = Strings.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        Classes.Set("untracked", Strings.IsUntrackedLanguage(Strings.Language));
    }

    /// <summary>
    /// Ayarlardaki tam liste. Üst şerit yalnız kısayolu taşır; kurulumdaki her dil buradan
    /// seçilir ve her satır dilin kendi adını kendi dosyasından yazar. Liste dosya
    /// klasörlerinden kuruluyor, kodda hiçbir dil adı yazılı değil.
    /// </summary>
    /// <summary>
    /// Ayarlardaki tema listesi. Adlar palet dosyalarının kendi adları; çeviriye girmezler,
    /// her dilde aynı yazılırlar — marka adı gibi. Seçim <see cref="PaletteCatalog"/>
    /// üzerinden yürürlüğe girer ve ayar dosyasında saklanır.
    /// </summary>
    private void BuildThemeList()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            _themeOrder = PaletteCatalog.Names;
            var labels = _themeOrder.Select(PaletteCatalog.Label).ToArray();
            CmbTheme.ItemsSource = labels;
            MarkChosenTheme();
            FillThemePairLists(labels);
            FillTextSizeList();
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private void MarkChosenTheme()
    {
        var index = -1;
        for (var at = 0; at < _themeOrder.Count; at++)
            if (string.Equals(_themeOrder[at], _theme, StringComparison.OrdinalIgnoreCase))
            {
                index = at;
                break;
            }

        CmbTheme.SelectedIndex = index;
    }

    private void OnThemeChosen()
    {
        if (_syncing) return;
        var chosen = CmbTheme.SelectedIndex;
        if (chosen < 0 || chosen >= _themeOrder.Count) return;

        _theme = PaletteCatalog.Pick(_themeOrder[chosen], false, null, null, null);
        _themeChosen = true;
        ApplyTheme();
        SaveAppSettings();
    }

    private void BuildLanguageList()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            _languageOrder = Strings.Languages;
            CmbLanguage.ItemsSource = _languageOrder
                .Select(Strings.LanguageLabel)
                .ToArray();
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    /// <summary>
    /// Ayarlar sekmesini seçip dil seçicisine iner. Oynatıcının sağ klik menüsündeki
    /// "Ayarlar" satırı da buraya bağlanır; sekme şeritte durduğu için ayrı bir düğme yok.
    /// </summary>
    internal void OpenLanguageSettings()
    {
        Tabs.SelectedIndex = SettingsTabIndex;
        CmbLanguage.BringIntoView();
        CmbLanguage.Focus();
    }

    private void OnLanguageChosen()
    {
        if (_syncing) return;
        var chosen = CmbLanguage.SelectedIndex;
        if (chosen < 0 || chosen >= _languageOrder.Count) return;
        UseLanguage(_languageOrder[chosen]);
    }

    /// <summary>
    /// Açılır listede <b>seçili</b> satırın metni bir kopyadır: Avalonia onu seçim anında
    /// alıp saklıyor, öğenin bağı sonradan tazelenince kopya eskide kalıyor. Seçim aynı
    /// yere geri konarak kopya yenileniyor; olay işleyicileri bu sırada susturuluyor.
    /// </summary>
    private void RefreshChoiceLabels()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            foreach (var box in this.GetVisualDescendants().OfType<ComboBox>())
            {
                var chosen = box.SelectedIndex;
                if (chosen < 0) continue;
                box.SelectedIndex = -1;
                box.SelectedIndex = chosen;
            }
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private void MarkChosenLanguage()
    {
        foreach (var button in LangSwitch.Children.OfType<Button>())
            button.Classes.Set(
                "selected",
                string.Equals(button.Tag as string, Strings.Language, StringComparison.OrdinalIgnoreCase));

        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            var index = -1;
            for (var at = 0; at < _languageOrder.Count; at++)
                if (string.Equals(_languageOrder[at], Strings.Language, StringComparison.OrdinalIgnoreCase))
                {
                    index = at;
                    break;
                }

            CmbLanguage.SelectedIndex = index;
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    private void UseLanguage(string language) => Strings.Use(language);

    internal List<Control> PlayerSettingsItems()
    {
        return new List<Control>
        {
            ChoiceMenu(Strings.Get("settings-tab.language.label"), CmbLanguage),
            ChoiceMenu(Strings.Get("settings-tab.theme.label"), CmbTheme)
        };

        static MenuItem ChoiceMenu(string header, ComboBox box)
        {
            var menu = new MenuItem { Header = header };
            var at = 0;
            foreach (var label in box.Items)
            {
                var index = at++;
                var item = new MenuItem
                {
                    Header = label?.ToString() ?? "",
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = index == box.SelectedIndex
                };
                item.Click += (_, _) => box.SelectedIndex = index;
                menu.Items.Add(item);
            }

            return menu;
        }
    }

    /// <summary>
    /// Dil değişti. Biçimlemeden gelen metni bağlar kendisi tazeliyor; burada yalnız koddan
    /// yazılan metinler yeniden kuruluyor.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Dil, arayüz iş parçacığının dışından da değiştirilebiliyor (ölçümler böyle
        // yapıyor). Görsel ağaca oradan dokunmak yasak; iş kuyruğa alınır.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnLanguageChanged(sender, e));
            return;
        }

        MarkChosenLanguage();
        ShowSourceName();
        ApplyFlowDirection();
        RefreshChoiceLabels();
        YongalariAdlandir();
        ApplyFastGpuTip();
        _preview?.SetLanguage(Strings.Language);
        if (_activeRetryPrompt is { } pendingPrompt) ShowRetryAsk(pendingPrompt);
        RefreshUpdateTexts();
        RefreshSettingsTexts();
       RelabelShellMenu();
        RefreshShareTarget();
        RefreshAdvancedTexts();
        RefreshEncodeLoadTexts();
        RefreshFinishSoundFile();
        UpdateToolStatus();
        RefreshPlatforms();
        SonDenetimiYaz();
        if (!_performanceRunning) ShowPerformanceResult(_performanceShown);
        if (_info is not null) { ShowInfo(_info); Recalculate(); RefreshConversion(); }
        else RefreshQualityPanels();
        RefreshQualityTargetAvailability();
        SaveSettings();
    }

    private void RefreshSettingsTexts()
    {
        BtnResetSettings.Content = Say("settings.reset-all");
        TxtResetSettingsConfirm.Text = Say("settings.reset-confirm");
        BtnConfirmResetSettings.Content = Say("settings.reset-confirm-button");
        BtnCancelResetSettings.Content = Say("settings.reset-cancel");
        RefreshOutputNamePreview();
    }

    /// <summary>Çıktı klasörü kipi: 0 kaynağın yanı, 1 sabit klasör. Kayıtlı ayarla aynı sayı.</summary>
    internal int OutputFolderModeIndex
    {
        get => RbOutputFixed.IsChecked == true ? 1 : 0;
        set
        {
            RbOutputBesideSource.IsChecked = value != 1;
            RbOutputFixed.IsChecked = value == 1;
        }
    }

    /// <summary>ffmpeg yolu kipi: 0 otomatik, 1 elle. Kayıtlı ayarla aynı sayı.</summary>
    internal int FfmpegPathModeIndex
    {
        get => RbFfmpegManual.IsChecked == true ? 1 : 0;
        set
        {
            RbFfmpegAuto.IsChecked = value != 1;
            RbFfmpegManual.IsChecked = value == 1;
        }
    }

    internal static string ResolveLanguage(string? saved, string? operatingSystem)
    {
        var known = Strings.Languages;
        var stored = known.FirstOrDefault(language => string.Equals(language, saved, StringComparison.OrdinalIgnoreCase));
        if (stored is not null) return stored;
        var system = known.FirstOrDefault(language => operatingSystem?.StartsWith(language, StringComparison.OrdinalIgnoreCase) == true);
        return system ?? Strings.FallbackLanguage;
    }

    /// <summary>
    /// K6: madde işaretini gövde metninden ayırır. Yuvarlak işaret neon mavisi bir koşu olur,
    /// cümle gövde renginde kalır; göz maddenin nerede başladığını sarma satırından ayırt eder.
    /// Düz metin <see cref="StyledElement.Tag"/> içinde saklanır, çünkü koşulardan kurulmuş bir
    /// <see cref="TextBlock"/> artık <c>Text</c> üzerinden okunup yazılamaz ve dil geçidi ile
    /// büyük harf geçidi kaynak metni oradan alır.
    /// </summary>
    public static void PaintBullets(TextBlock block, string plain)
    {
        block.Tag = plain;
        if (block.Inlines is not { } inlines) { block.Text = plain; return; }

        inlines.Clear();
        var bullet = block.TryFindResource("NeonBlue", out var value) ? value as IBrush : null;
        var lines = plain.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) inlines.Add(new LineBreak());
            var line = lines[index];
            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                inlines.Add(new Run("• ") { Foreground = bullet });
                line = line[2..];
            }
            inlines.Add(new Run(line));
        }
    }


    private void ApplyFastGpuTip()
    {
        // Text yazmak koşuları silerdi: ipucu gövdesi aynı boyayıcıdan geçmeli.
        var body = Say(_hardwareProbed && !_hardwareEncoderAvailable ? "main.fast-gpu.tip-missing" : "main.fast-gpu.tip");
        var verdict = FastGpuVerdictLine(_hardwareVerdict, ChkFastGpu.IsChecked == true, Strings.Language);
        PaintBullets(TipFastGpu, verdict is null ? body : $"{body}\n{verdict}");
    }

    /// <summary>
    /// Kutunun neden açıldığı ya da neden kapalı kaldığı. Ayrı bir pencere açılmaz; ölçüm
    /// ipucunun son satırı olarak durur.
    ///
    /// Kutunun gerçek durumu <paramref name="fastGpuOn"/> ile geliyor: ölçüm kapalı
    /// önerdiği hâlde kullanıcı kutuyu elle açmış olabilir ve satır o zaman "kapalı kaldı"
    /// diyemez.
    /// </summary>
    internal static string? FastGpuVerdictLine(HardwareVerdict verdict, bool fastGpuOn, string language)
    {
        string Line(string key, params object?[] args) => Speak(language, key, args);

        var v = verdict;

        // Gövdenin kendisi zaten donanım bulunamadığını yazıyor.
        if (v.Reason is HardwareVerdictReason.NotProbed or HardwareVerdictReason.NoHardwareEncoder)
            return null;

        if (v.Reason == HardwareVerdictReason.Usable)
            return fastGpuOn
                ? Line("main.fast-gpu.on-usable", v.Codec, v.ElapsedMs, Strings.BitHizi(v.RequestedBitrateK), Strings.BitHizi(v.UsableBitrateK))
                : Line("main.fast-gpu.off-usable", v.Codec);

        var measurement = v.Reason switch
        {
            HardwareVerdictReason.ProbeFailed => Line("main.fast-gpu.probe-failed", v.Codec),
            HardwareVerdictReason.ProbeSlow => Line("main.fast-gpu.probe-slow", v.Codec, v.ElapsedMs, HardwareVerdict.ProbeBudgetMs),
            HardwareVerdictReason.BitrateFloorTooHigh => Line("main.fast-gpu.bitrate-floor", v.Codec, Strings.BitHizi(v.UsableBitrateK), Strings.BitHizi(v.RequestedBitrateK)),
            _ => null
        };

        if (measurement is null) return null;

        return fastGpuOn
            ? Line("main.fast-gpu.on-against-advice", measurement)
            : Line("main.fast-gpu.off-as-advised", measurement);
    }

    private async Task LoadFfmpegVersionAsync()
    {
        if (_ffmpegVersion is not null) return;
        try { _ffmpegVersion = await Task.Run(ToolLocator.GetFfmpegVersion); }
        catch (Exception ex) { _ffmpegVersion = $"{Say("main.about.unavailable")} ({ex.Message})"; }
        UpdateToolStatus();
    }

    private void UpdateToolStatus()
    {
        if (!ToolLocator.IsAvailable(out var missing))
        {
            TxtSystemStatus.Text = Say("main.about.tool-missing", missing);
            return;
        }

        var fallback = FfmpegPathModeIndex == 1 && ToolLocator.Manual is null;
        TxtSystemStatus.Text = string.Join("\n",
            fallback ? $"FFmpeg: {YolSarar(ToolLocator.Ffmpeg)}\n{Say("settings-tab.ffmpeg-path.error")}" : $"FFmpeg: {YolSarar(ToolLocator.Ffmpeg)}",
            $"{Say("main.about.version")}: {_ffmpegVersion ?? Say("main.about.reading")}",
            $".NET: {Environment.Version}",
            $"VidShrink: {AppVersion()}");
    }

    /// <summary>
    /// Yol, ayraçlarından sonra satır kırılabilir hale gelir (sıfır genişlikli boşluk). WinGet'in
    /// kurduğu ffmpeg'in yolu Hakkında kartına sığmıyor, Avalonia boşluksuz yolu harf ortasından
    /// bölüyordu ("WinG|et", "Pac|kages"). Nokta ve alt çizgi de ayraç: paket klasörü
    /// "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe" 401 px kartta "8wekyb3d8bb|we" bölünüyordu.
    /// </summary>
    internal static string YolSarar(string? yol)
        => System.Text.RegularExpressions.Regex.Replace(yol ?? string.Empty, @"[\\/._]", "$0​");

    /// <summary>
    /// Uygulamanın kendi sürümü. Hakkında kutusu da bildirim şeridi de burayı okur;
    /// AssemblyInformationalVersion'a giden ikinci bir yol açılmaz.
    /// </summary>
    private static string AppVersion() => DisplayVersion(UpdateCheck.CurrentVersion(Assembly.GetExecutingAssembly()));

    internal static string DisplayVersion(string informationalVersion)
    {
        var build = informationalVersion.IndexOf('+');
        return build < 0 ? informationalVersion : informationalVersion[..build];
    }

    private void RestoreSettings(UpdateSettings settings)
    {
        _settingsSyncing = _syncing = _updateUiSyncing = true;
        try
        {
            TxtTarget.Text = FormatBoxNumber(settings.TargetMb);
            TxtQualityTarget.Text = FormatBoxNumber(settings.QualityTarget);
            _savedTargetMb = settings.TargetMb;
            _savedQualityTarget = settings.QualityTarget;
            _intent = (Intent)Math.Clamp(settings.Intent, 0, 2);
            _chipSizeCapped = settings.ChipSizeCapped;
            SetCodecIndex(settings.Codec);
            ChkResolution.IsChecked = settings.MayLowerResolution;
            FixedResolutionIndex = settings.FixedResolution;
            ChkWhatsAppCompatible.IsChecked = settings.WhatsAppCompatible;
            ChkFps.IsChecked = settings.MayLowerFps;
            ChkFastGpu.IsChecked = settings.FastGpu ?? false;
            SetFillIndex(settings.FillPolicy);
            SetHdrIndex(settings.HdrPolicy);
            CmbQualityMode.SelectedIndex = settings.QualityMode;
            TxtQuality.Text = settings.QualityValue.ToString(CultureInfo.InvariantCulture);
            CmbContainer.SelectedIndex = settings.Container;
            KapSecenekleriniYokla();
            KodekSec(ConversionCodecChoice.FromSaved(settings.ConvertCodecName, settings.ConvertCodec));
            DonanimKodlayicilariniYokla();
            CmbResolution.SelectedIndex = settings.Resolution;
            TxtCustomResolution.Text = settings.CustomResolution;
            CmbConvertFps.SelectedIndex = settings.ConvertFps;
            TxtCustomFps.Text = settings.CustomFps;
            CmbConvertAudio.SelectedIndex = settings.ConvertAudio;
            TxtAudioBitrate.Text = settings.AudioBitrate;
            TxtTrimStart.Text = settings.TrimStart;
            TxtTrimEnd.Text = settings.TrimEnd;
            ShareTargetIndex = settings.ShareTarget;
            RefreshShareTarget();
            if (CmbShareRetention.ItemCount > 0)
                CmbShareRetention.SelectedIndex = Math.Clamp(settings.ShareRetention, 0, CmbShareRetention.ItemCount - 1);
            ChkAutoUpdate.IsChecked = settings.AutoUpdate;
        }
        finally
        {
            _updateUiSyncing = _syncing = _settingsSyncing = false;
        }

        RefreshFrameAndCodecLocks();
        RefreshChipDerivation();
        RefreshSectionSummaries();
    }

    private UpdateSettings CaptureSettings() => new()
    {
        Language = Strings.Language,
        AutoUpdate = ChkAutoUpdate.IsChecked == true,
        FastGpu = ChkFastGpu.IsChecked == true,
        TargetMb = _savedTargetMb,
        QualityTarget = _savedQualityTarget,
        Intent = SelectedIntentIndex,
        ChipSizeCapped = _chipSizeCapped,
        Codec = CodecIndex,
        MayLowerResolution = ChkResolution.IsChecked == true,
        FixedResolution = FixedResolutionIndex,
        WhatsAppCompatible = ChkWhatsAppCompatible.IsChecked == true,
        MayLowerFps = ChkFps.IsChecked == true,
        FillPolicy = FillPolicyIndex,
        HdrPolicy = HdrPolicyIndex,
        QualityMode = CmbQualityMode.SelectedIndex,
        QualityValue = int.TryParse(TxtQuality.Text, out var quality) ? quality : 23,
        Container = CmbContainer.SelectedIndex,
        ConvertCodec = ConversionCodecChoice.LegacyIndex(SelectedTag(CmbConvertCodec)),
        ConvertCodecName = SelectedTag(CmbConvertCodec),
        Resolution = CmbResolution.SelectedIndex,
        CustomResolution = TxtCustomResolution.Text ?? "",
        ConvertFps = CmbConvertFps.SelectedIndex,
        CustomFps = TxtCustomFps.Text ?? "",
        ConvertAudio = CmbConvertAudio.SelectedIndex,
        AudioBitrate = TxtAudioBitrate.Text ?? "",
        TrimStart = TxtTrimStart.Text ?? "",
        TrimEnd = TxtTrimEnd.Text ?? "",
        ShareTarget = ShareTargetIndex,
        ShareRetention = CmbShareRetention.SelectedIndex
    };

    /// <summary>
    /// Kapanan pencere ayar yazmaz. Kapalı pencerenin seçim düğmeleri Avalonia'nın grup
    /// yöneticisinde kalıyor; aynı süreçte kurulan ikinci pencerenin XAML'i kendi düğmesini
    /// işaretleyince kapalı pencereninki düşüyor ve <see cref="Watch"/> kapalı pencerenin
    /// dosyasını yeniden yazıyordu (<c>docs/olcumler/kapanan-pencere-ayar-yaziyor.md</c>).
    /// </summary>
    private void SaveSettings()
    {
        if (_settingsSyncing || _syncing || _kapandi) return;
        try
        {
            CaptureSettings().Save(SettingsPathOverride);
            CaptureAppSettings().Save(SettingsPathOverride);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TxtSystemStatus.Text = $"{Say("settings.error.save")}: {ex.Message}";
        }
    }

    private void SaveAppSettings()
    {
        if (_settingsSyncing || _syncing || _kapandi) return;
        try { CaptureAppSettings().Save(SettingsPathOverride); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TxtSystemStatus.Text = $"{Say("settings.error.save")}: {ex.Message}";
        }
    }

    private void OnAboutToggle(object? sender, RoutedEventArgs e)
    {
        AboutBody.IsVisible = !AboutBody.IsVisible;
        AboutGlyph.Classes.Set("open", AboutBody.IsVisible);
    }

    /// <summary>
    /// Günlüğü uygulama içindeki tanılama penceresinde açar (<see cref="ShowDiagnostics"/>);
    /// dosya yöneticisinde gösterme oradaki düğmede.
    /// </summary>
    private void OnOpenLog(object? sender, RoutedEventArgs e) => ShowDiagnostics();

    private void OnResetSettings(object? sender, RoutedEventArgs e)
        => ResetSettingsConfirm.IsVisible = true;

    private void OnCancelResetSettings(object? sender, RoutedEventArgs e)
        => ResetSettingsConfirm.IsVisible = false;

    private void OnConfirmResetSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsFile = SettingsPathOverride ?? UpdateSettings.DefaultPath;
            _recorderPane?.RestoreDesktopIcons();
            AppDataReset.Run(Path.GetDirectoryName(settingsFile), settingsFile);
            Playback.ShortcutBindings.Load();
            if (File.Exists(DismissedNoticePath)) File.Delete(DismissedNoticePath);
            var defaults = new UpdateSettings();
            _settingsSyncing = true;
            UseLanguage(ResolveLanguage(null, CultureInfo.CurrentUICulture.Name));
            RestoreSettings(defaults);
            RestoreAppSettings(new AppSettings());
            ResetSettingsConfirm.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TxtSystemStatus.Text = $"{Say("settings.error.reset")}: {ex.Message}";
        }
    }

    internal void RestoreSettingsForTest(UpdateSettings settings) => RestoreSettings(settings);

    internal UpdateSettings CaptureSettingsForTest() => CaptureSettings();
    internal void ConfirmResetSettingsForTest() => OnConfirmResetSettings(null, new RoutedEventArgs());
    internal void RestoreAppSettingsForTest(AppSettings settings) => RestoreAppSettings(settings);
    internal AppSettings CaptureAppSettingsForTest() => CaptureAppSettings();
    internal List<string> ReasonLinesForTest(EncodePlan plan) => ReasonLines(plan);

    private SelectingItemsControl[] AdvBoxes() => new SelectingItemsControl[]
    {
        CmbAdvCrf, CmbAdvPreset, CmbAdvTune, CmbAdvModulus, CmbAdvAudioKbps, CmbAdvAudioChannels, CmbAdvAudioCodec,
        CmbAdvMinResolution, CmbAdvMinFps, CmbAdvCodecLock
    };

    internal ToggleButton[] AdvStripToggles() => new ToggleButton[]
    {
        RbAdvModeAuto, RbAdvModeCrf, RbAdvModeTwoPass,
        RbAdvPathAuto, RbAdvPathSoftware, RbAdvPathHardware
    };

    internal int AdvModeIndex
    {
        get => RbAdvModeCrf.IsChecked == true ? 1 : RbAdvModeTwoPass.IsChecked == true ? 2 : 0;
        set
        {
            RbAdvModeCrf.IsChecked = value == 1;
            RbAdvModeTwoPass.IsChecked = value == 2;
            RbAdvModeAuto.IsChecked = value is not (1 or 2);
        }
    }

    internal int AdvEncoderPathIndex
    {
        get => RbAdvPathSoftware.IsChecked == true ? 1 : RbAdvPathHardware.IsChecked == true ? 2 : 0;
        set
        {
            RbAdvPathSoftware.IsChecked = value == 1;
            RbAdvPathHardware.IsChecked = value == 2;
            RbAdvPathAuto.IsChecked = value is not (1 or 2);
        }
    }

    private AppSettings CaptureAppSettings()
    {
        var boxes = AdvBoxes();
        return new AppSettings
        {
            AdvMode = AdvModeIndex,
            AdvCrf = CmbAdvCrf.SelectedIndex,
            AdvPreset = CmbAdvPreset.SelectedIndex,
            AdvTune = CmbAdvTune.SelectedIndex,
            AdvModulus = CmbAdvModulus.SelectedIndex,
            AdvAudioKbps = CmbAdvAudioKbps.SelectedIndex,
            AdvAudioChannels = CmbAdvAudioChannels.SelectedIndex,
            AdvAudioCodec = CmbAdvAudioCodec.SelectedIndex,
            AdvMinResolution = CmbAdvMinResolution.SelectedIndex,
            AdvMinFps = CmbAdvMinFps.SelectedIndex,
            AdvEncoderPath = AdvEncoderPathIndex,
            AdvCodecLock = CmbAdvCodecLock.SelectedIndex,
            AdvKeepTracks = ChkAdvKeepTracks.IsChecked == true,
            AdvDropMetadata = ChkAdvDropMetadata.IsChecked == true,
            AdvMeasureQuality = ChkAdvMeasureQuality.IsChecked == true,
            AdvSearchByMeasure = ChkAdvSearchByMeasure.IsChecked == true,
            ConvertDropMetadata = ChkConvertDropMetadata.IsChecked == true,
            ConvertGifLoop = SelectedTag(CmbConvertGifLoop) ?? "",
            ConvertGifColors = SelectedTag(CmbConvertGifColors) ?? "",
            ConvertGifDither = SelectedTag(CmbConvertGifDither) ?? "",
            EditorDropMetadata = _editorPane?.DropMetadata ?? _editorDropMetadata,
            EditorClipChapters = _editorPane?.ClipChapters ?? _editorClipChapters,
            EditorPartNamePattern = _editorPartNamePattern,
            OutputFolderMode = OutputFolderModeIndex,
            OutputFolder = TxtOutputFolder.Text ?? "",
            OutputNamePattern = TxtOutputName.Text ?? "",
            AdvancedDefaultOpen = ChkAdvancedDefaultOpen.IsChecked == true,
            FollowRecording = ChkFollowRecording.IsChecked == true,
            NotifyWhenDone = ChkNotifyWhenDone.IsChecked == true,
            FinishSound = ChkFinishSound.IsChecked == true,
            FinishSoundFile = _finishSoundFile,
            PreventSleepWhileEncoding = ChkPreventSleep.IsChecked == true,
            MinimizeToTray = ChkMinimizeToTray.IsChecked == true,
            KeepSourceDates = ChkKeepDates.IsChecked == true,
            ParallelJobs = Math.Max(0, CmbParallelJobs.SelectedIndex) + 1,
            EncodePriority = SelectedEncodePriority,
            EncoderThreads = SelectedEncoderThreads,
            FfmpegPathMode = FfmpegPathModeIndex,
            FfmpegPath = TxtFfmpegPath.Text ?? "",
            OpenSubtitlesApiKey = (TxtOpenSubtitlesKey.Text ?? "").Trim(),
            OpenSubtitlesUser = (TxtOpenSubtitlesUser.Text ?? "").Trim(),
            Theme = _theme,
            ThemeChosen = _themeChosen,
            FontScale = _fontScale,
            FollowSystemTheme = ChkFollowSystemTheme.IsChecked == true,
            LightTheme = _lightTheme,
            DarkTheme = _darkTheme,
            ReduceMotion = ChkReduceMotion.IsChecked == true
        };
    }

    private void RestoreAppSettings(AppSettings settings)
    {
        var wasSyncing = _syncing;
        _settingsSyncing = _syncing = true;
        try
        {
            AdvModeIndex = settings.AdvMode;
            AdvEncoderPathIndex = settings.AdvEncoderPath;
            var restore = new (SelectingItemsControl Box, int Index)[]
            {
                (CmbAdvCrf, settings.AdvCrf),
                (CmbAdvPreset, settings.AdvPreset),
                (CmbAdvTune, settings.AdvTune),
                (CmbAdvModulus, settings.AdvModulus),
                (CmbAdvAudioKbps, settings.AdvAudioKbps),
                (CmbAdvAudioChannels, settings.AdvAudioChannels),
                (CmbAdvAudioCodec, settings.AdvAudioCodec),
                (CmbAdvMinResolution, settings.AdvMinResolution),
                (CmbAdvMinFps, settings.AdvMinFps),
                (CmbAdvCodecLock, settings.AdvCodecLock)
            };
            foreach (var (box, index) in restore)
                if (index >= 0 && index < box.ItemCount) box.SelectedIndex = index;
            ChkAdvKeepTracks.IsChecked = settings.AdvKeepTracks;
            ChkAdvDropMetadata.IsChecked = settings.AdvDropMetadata;
            ChkAdvMeasureQuality.IsChecked = settings.AdvMeasureQuality;
            ChkAdvSearchByMeasure.IsChecked = settings.AdvSearchByMeasure;
            ChkConvertDropMetadata.IsChecked = settings.ConvertDropMetadata;
            SelectByTag(CmbConvertGifLoop, settings.ConvertGifLoop);
            SelectByTag(CmbConvertGifColors, settings.ConvertGifColors);
            SelectByTag(CmbConvertGifDither, settings.ConvertGifDither);
            _editorDropMetadata = settings.EditorDropMetadata;
            if (_editorPane is not null) _editorPane.DropMetadata = settings.EditorDropMetadata;
            _editorClipChapters = settings.EditorClipChapters;
            if (_editorPane is not null) _editorPane.ClipChapters = settings.EditorClipChapters;
            _editorPartNamePattern = settings.EditorPartNamePattern;
            if (_editorPane is not null) _editorPane.PartNamePattern = settings.EditorPartNamePattern;

            OutputFolderModeIndex = Math.Clamp(settings.OutputFolderMode, 0, 1);
            TxtOutputFolder.Text = settings.OutputFolder;
            TxtOutputName.Text = string.IsNullOrWhiteSpace(settings.OutputNamePattern)
                ? AdlandirmaDeseni.Varsayilan
                : settings.OutputNamePattern;
            OutputFolderPickerRow.IsVisible = settings.OutputFolderMode == 1;
            RefreshOutputNamePreview();

            RestoreSystemThemeAndMotion(settings);

            ChkAdvancedDefaultOpen.IsChecked = settings.AdvancedDefaultOpen;
            if (settings.AdvancedDefaultOpen) ExpandAdvanced();
            ChkFollowRecording.IsChecked = settings.FollowRecording;
            ChkNotifyWhenDone.IsChecked = settings.NotifyWhenDone;
            ChkFinishSound.IsChecked = settings.FinishSound;
            SetFinishSoundFile(settings.FinishSoundFile);
            ChkPreventSleep.IsChecked = settings.PreventSleepWhileEncoding;
            ChkMinimizeToTray.IsChecked = settings.MinimizeToTray;
            ChkKeepDates.IsChecked = settings.KeepSourceDates;
            CmbParallelJobs.SelectedIndex = ParallelJobs.Clamp(settings.ParallelJobs, Environment.ProcessorCount) - 1;
            RestoreEncodeLoad(settings);

            FfmpegPathModeIndex = Math.Clamp(settings.FfmpegPathMode, 0, 1);
            TxtFfmpegPath.Text = settings.FfmpegPath;
            TxtOpenSubtitlesKey.Text = settings.OpenSubtitlesApiKey;
            TxtOpenSubtitlesUser.Text = settings.OpenSubtitlesUser;
            ShowSubtitleSession();
            FfmpegPathPickerRow.IsVisible = settings.FfmpegPathMode == 1;
            ValidateFfmpegPath();

            TxtDefaultTargetMb.Text = TxtTarget.Text;
        }
        finally
        {
            _settingsSyncing = false;
            _syncing = wasSyncing;
        }
        Recalculate();
    }

    private void OnDefaultTargetMbChanged()
    {
        if (_settingsSyncing || _syncing) return;
        TxtTarget.Text = TxtDefaultTargetMb.Text;
    }

    private void OnOutputFolderModeChanged()
    {
        OutputFolderPickerRow.IsVisible = OutputFolderModeIndex == 1;
        SaveAppSettings();
    }

    /// <summary>Kullanıcının yazdığı desen: geçerliyse örnek, değilse gerekçe yazılır.</summary>
    private void OnOutputNameChanged()
    {
        RefreshOutputNamePreview();
        SaveAppSettings();
    }

    private void OnResetOutputName(object? sender, RoutedEventArgs e)
        => TxtOutputName.Text = AdlandirmaDeseni.Varsayilan;

    private void RefreshOutputNamePreview()
    {
        var desen = TxtOutputName.Text;
        if (AdlandirmaDeseni.Hata(desen) is { } hata)
        {
            LblOutputNamePreview.Text = Strings.Get(hata);
            LblOutputNamePreview.Classes.Set("error", true);
            return;
        }

        LblOutputNamePreview.Classes.Set("error", false);
        LblOutputNamePreview.Text = Strings.Get(
            "settings-tab.output-name.preview",
            AdlandirmaDeseni.Uygula(desen, OrnekAdBilgisi()) + ".mp4");
    }

    /// <summary>Örnek satırın verileri; ekranda dosya yokken de desen okunabilmeli.</summary>
    private AdBilgisi OrnekAdBilgisi()
        => _info is null
            ? new AdBilgisi("video") { HedefMb = ParseTargetMb(), Crf = 26, VideoBitrateK = 1200, Yukseklik = 720, Kodek = "libx264", KareHizi = 30, KaynakKlasoru = "Videolar", Sayac = 1 }
            : AdBilgisiFor(_info.FilePath, ActivePlan);

    private AdBilgisi AdBilgisiFor(string inputPath, EncodePlan? plan)
        => new(ShrinkEngine.KaynakAdi(inputPath))
        {
            HedefMb = _chipSizeCapped ? ParseTargetMb() : null,
            Crf = plan?.Crf,
            VideoBitrateK = plan?.VideoBitrateK,
            Yukseklik = plan?.Height,
            Kodek = plan?.Codec,
            KareHizi = plan?.Fps > 0 ? plan.Fps : null,
            KaynakKlasoru = ShrinkEngine.KaynakKlasoru(inputPath),
            Sayac = 1
        };

    private void OnFfmpegPathModeChanged()
    {
        FfmpegPathPickerRow.IsVisible = FfmpegPathModeIndex == 1;
        if (_settingsSyncing) return;
        ValidateFfmpegPath();
        SaveAppSettings();
    }

    private void OnFfmpegPathTextChanged()
    {
        if (_settingsSyncing) return;
        ValidateFfmpegPath();
        SaveAppSettings();
    }

    /// <summary>
    /// Seçimi <see cref="ToolLocator"/>'a uygular: elle yol geçerliyse kodlama, yoklama ve
    /// kaydedici onu kullanır; geçersizse otomatik ffmpeg'e düşülür ve bu hata satırında
    /// söylenir. Yürürlükteki ffmpeg değişince sürüm satırı ve donanım yoklaması tazelenir.
    /// </summary>
    private void ValidateFfmpegPath()
    {
        var before = ToolLocator.Manual;
        var manual = FfmpegPathModeIndex == 1;
        var valid = new AppSettings { FfmpegPathMode = FfmpegPathModeIndex, FfmpegPath = TxtFfmpegPath.Text ?? "" }.ApplyFfmpegPath();
        TxtFfmpegPathError.IsVisible = manual && !valid;
        TxtFfmpegPathError.Text = manual && !valid ? Say("settings-tab.ffmpeg-path.error") : "";
        if (before == ToolLocator.Manual) return;
        _ffmpegVersion = null;
        UpdateToolStatus();
        _ = LoadFfmpegVersionAsync();
        _ = ProbeHardwareEncodersAsync();
    }

    private async void OnBrowseOutputFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (path is not null)
            {
                TxtOutputFolder.Text = path;
                SaveAppSettings();
            }
        }
        catch (Exception ex)
        {
            ReportSourceError($"{Say("main.error.pick")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Anahtar bizde değil kullanıcıda: sağlayıcının anahtar sayfasını açar, kullanıcı
    /// kendi anahtarını alıp yandaki kutuya yapıştırır.
    /// </summary>
    private void OnOpenSubtitlesKeyPage(object? sender, RoutedEventArgs e)
        => OpenExternal(OpenSubtitlesProvider.KeyPageUrl);

    /// <summary>
    /// Kullanici adi ve parolayla OpenSubtitles oturumu acar.
    /// </summary>
    /// <remarks>
    /// Parola kutudan alinir, istegin govdesinde gider ve cagri biter bitmez kutu bosaltilir;
    /// ayara, gunluge ya da durum satirina girmez. Saklanan tek sey donen belirtectir.
    /// </remarks>
    private async void OnOpenSubtitlesSignIn(object? sender, RoutedEventArgs e)
    {
        var key = (TxtOpenSubtitlesKey.Text ?? "").Trim();
        var user = (TxtOpenSubtitlesUser.Text ?? "").Trim();
        var password = TxtOpenSubtitlesPassword.Text ?? "";

        if (key.Length == 0 || user.Length == 0 || password.Length == 0)
        {
            LblOpenSubtitlesStatus.Text = Strings.Get("settings-tab.opensubtitles.signinfailed");
            return;
        }

        LblOpenSubtitlesStatus.Text = Strings.Get("settings-tab.opensubtitles.signingin");
        BtnOpenSubtitlesSignIn.IsEnabled = false;
        try
        {
            using var transport = new CoreShare.HttpClientTransport();
            var provider = new OpenSubtitlesProvider(transport, key, sessions: new SessionStore());
            var result = await provider.LoginAsync(user, password, CancellationToken.None);
            LblOpenSubtitlesStatus.Text = result.Session is not null
                ? Strings.Get("settings-tab.opensubtitles.signedin", user)
                : Strings.Get("settings-tab.opensubtitles.signinfailed");
        }
        finally
        {
            TxtOpenSubtitlesPassword.Text = "";
            BtnOpenSubtitlesSignIn.IsEnabled = true;
        }
    }

    private void OnOpenSubtitlesSignOut(object? sender, RoutedEventArgs e)
    {
        new SessionStore().Clear();
        TxtOpenSubtitlesPassword.Text = "";
        ShowSubtitleSession();
    }

    /// <summary>Oturum durumunu yazar; belirtec varsa kimin adina, yoksa gerekli oldugunu.</summary>
    private void ShowSubtitleSession()
    {
        var user = (TxtOpenSubtitlesUser.Text ?? "").Trim();
        var live = new SessionStore().Read();
        LblOpenSubtitlesStatus.Text = live is not null && live.Valid(DateTimeOffset.UtcNow) && user.Length > 0
            ? Strings.Get("settings-tab.opensubtitles.signedin", user)
            : Strings.Get("settings-tab.opensubtitles.signedout");
    }

    private async void OnBrowseFfmpegPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (path is not null)
            {
                TxtFfmpegPath.Text = path;
                ValidateFfmpegPath();
                SaveAppSettings();
            }
        }
        catch (Exception ex)
        {
            ReportSourceError($"{Say("main.error.pick")}: {ex.Message}");
        }
    }

    private void InitializeUpdateUi(UpdateSettings? settings = null)
    {
        AutoUpdateRow.IsVisible = UpdateCheck.CanSelfUpdate && !Tasinabilir;

        _updateUiSyncing = true;
        try { ChkAutoUpdate.IsChecked = (settings ?? UpdateSettings.Load(SettingsPathOverride)).AutoUpdate; }
        finally { _updateUiSyncing = false; }

        RefreshUpdateTexts();
        RefreshPlatforms();
        SonDenetimiYaz();
        ReportAppliedUpdate();
    }

    /// <summary>Hakkında'nın platform satırları: yayının kurduğu her hedef, bu kurulumun hedefi işaretli.</summary>
    internal void RefreshPlatforms()
    {
        var current = UpdateCheck.Rid;
        TxtPlatforms.Text = string.Join("\n", UpdateCheck.ReleasedRids.Select(rid =>
            string.Equals(rid, current, StringComparison.OrdinalIgnoreCase)
                ? $"{PlatformName(rid)}  ← {Say("main.about.platforms.current")}"
                : PlatformName(rid)));
    }

    internal static string PlatformName(string rid) => rid switch
    {
        "win-x64" => "Windows x64",
        "win-arm64" => "Windows ARM64",
        "osx-arm64" => "macOS Apple Silicon (arm64)",
        "osx-x64" => "macOS Intel (x64)",
        "linux-x64" => "Linux x64",
        "linux-arm64" => "Linux ARM64",
        _ => rid
    };

    /// <summary>
    /// Kendini güncelleyen uygulama yeniden başlar, bu yüzden "geçildi" bilgisi bellekte
    /// tutulamaz: başlatıcının bıraktığı işaret dosyası okunur. İşaret şerit kapanınca
    /// silinir: kullanıcı kapatınca ya da şerit <see cref="AppliedNoticeSeconds"/> saniye
    /// görünüp kendiliğinden gidince. Görüldüğü anda silinseydi, kullanıcı okumadan
    /// pencereyi kapattığında bilgi bir daha hiç görünmezdi.
    /// </summary>
    private void ReportAppliedUpdate()
    {
        _appliedNotice = new AppliedUpdateNotice(AppContext.BaseDirectory);
        if (!_appliedNotice.Load()) return;

        TxtAppliedVersion.Text = _appliedNotice.Version!;
        ShowAppliedNotice();
    }

    /// <summary>"Yeni sürüme geçildi" şeridinin kendiliğinden kapanma süresi.</summary>
    internal const int AppliedNoticeSeconds = 5;

    private PausableCountdown? _appliedCountdown;
    private DispatcherTimer? _appliedTimer;
    private bool _appliedPointerOver;
    private bool _appliedNoticeWatched;

    /// <summary>Şeridin sayacının saati; ölçü sahte saat verir.</summary>
    internal Func<TimeSpan> AppliedNoticeClock { get; set; } = () => AppliedNoticeWatch.Elapsed;

    private static readonly Stopwatch AppliedNoticeWatch = Stopwatch.StartNew();

    internal PausableCountdown? AppliedNoticeCountdown => _appliedCountdown;

    internal DispatcherTimer? AppliedNoticeTimer => _appliedTimer;

    /// <summary>
    /// Şeridi gösterir ve <see cref="AppliedNoticeSeconds"/> saniyelik sayacı kurar. Fare
    /// üstündeyken ya da klavye odağı şeritteyken sayaç durur, ayrılınca kalandan sürer.
    /// </summary>
    internal void ShowAppliedNotice()
    {
        AppliedNotice.IsVisible = true;
        if (!_appliedNoticeWatched)
        {
            _appliedNoticeWatched = true;
            AppliedNotice.PointerEntered += (_, _) => AppliedNoticePointer(true);
            AppliedNotice.PointerExited += (_, _) => AppliedNoticePointer(false);
            AppliedNotice.GotFocus += (_, _) => RefreshAppliedCountdown();
            AppliedNotice.LostFocus += (_, _) => RefreshAppliedCountdown();
        }
        _appliedPointerOver = false;
        _appliedCountdown = new PausableCountdown(TimeSpan.FromSeconds(AppliedNoticeSeconds), AppliedNoticeClock);
        RefreshAppliedCountdown();
    }

    internal void AppliedNoticePointer(bool over)
    {
        _appliedPointerOver = over;
        RefreshAppliedCountdown();
    }

    private void RefreshAppliedCountdown()
    {
        var countdown = _appliedCountdown;
        if (countdown is null) return;

        if (_appliedPointerOver || AppliedNotice.IsKeyboardFocusWithin)
        {
            countdown.Pause();
            _appliedTimer?.Stop();
            return;
        }

        countdown.Resume();
        if (_appliedTimer is null)
        {
            _appliedTimer = new DispatcherTimer();
            _appliedTimer.Tick += (_, _) => AppliedNoticeTick();
        }
        _appliedTimer.Stop();
        _appliedTimer.Interval = countdown.Remaining > TimeSpan.Zero ? countdown.Remaining : TimeSpan.FromMilliseconds(1);
        _appliedTimer.Start();
    }

    internal void AppliedNoticeTick()
    {
        var countdown = _appliedCountdown;
        if (countdown is null) return;
        if (countdown.Elapsed) CloseAppliedNotice();
        else RefreshAppliedCountdown();
    }

    private void CloseAppliedNotice()
    {
        _appliedTimer?.Stop();
        _appliedCountdown = null;
        _appliedNotice?.Shown();
        AppliedNotice.IsVisible = false;
    }

    private void OnDismissAppliedNotice(object? sender, RoutedEventArgs e) => CloseAppliedNotice();

    private void RefreshUpdateTexts()
    {
        TxtAutoUpdateEffect.Text = Say(Tasinabilir ? "settings.update.portable-effect"
            : UpdateCheck.CanSelfUpdate ? "settings.update.auto-effect" : "settings.update.no-self-effect");
        RefreshUpdateNoticeButton();
    }

    /// <summary>
    /// T163/K4: dokuz gelişmiş kalemin liste içerikleri. Kodek kilidi listesi
    /// <see cref="FfmpegArguments.KnownCodecs"/>'ten geliyor — burada ikinci bir kodek
    /// adı kümesi elle yazılmıyor. Ön ayar listesi ffmpeg'in kendi terimleri
    /// (<see cref="AdvancedPresetCandidates"/>); geçerliliği kodeğe göre değişir ve o
    /// kontrol <see cref="FfmpegArguments.IsValidPreset"/> ile plan kurulurken yapılır,
    /// burada önceden filtrelenmiyor.
    /// </summary>
    private void InitializeAdvancedUi()
    {
        var automatic = Say("main.plan.automatic");

        CmbAdvCrf.ItemsSource = new[] { automatic }.Concat(AdvancedCrfCandidates.Select(c => c.ToString(CultureInfo.InvariantCulture))).ToList();
        CmbAdvCrf.SelectedIndex = 0;

        CmbAdvPreset.ItemsSource = new[] { automatic }.Concat(AdvancedPresetCandidates).ToList();
        CmbAdvPreset.SelectedIndex = 0;

        CmbAdvTune.ItemsSource = new[] { automatic }.Concat(AdvancedTuneCandidates).ToList();
        CmbAdvTune.SelectedIndex = 0;

        CmbAdvModulus.ItemsSource = new[] { automatic }
            .Concat(Olcek.Moduller.Select(m => m.ToString(CultureInfo.InvariantCulture))).ToList();
        CmbAdvModulus.SelectedIndex = 0;

        CmbAdvAudioKbps.ItemsSource = new[] { automatic }.Concat(AdvancedAudioKbpsCandidates.Select(c => c.ToString(CultureInfo.InvariantCulture))).ToList();
        CmbAdvAudioKbps.SelectedIndex = 0;

        CmbAdvAudioChannels.ItemsSource = new[]
        {
            automatic, Say("main.advanced.audio-channels.stereo"), Say("main.advanced.audio-channels.mono"), Say("main.advanced.audio-channels.none"),
            Say("main.advanced.audio-channels.source")
        };
        CmbAdvAudioChannels.SelectedIndex = 0;

        CmbAdvAudioCodec.ItemsSource = new[] { automatic, "AAC", "Dolby Digital (AC-3)", "Dolby Digital Plus (E-AC-3)" };
        CmbAdvAudioCodec.SelectedIndex = 0;

        CmbAdvMinResolution.ItemsSource = new[] { automatic }.Concat(AdvancedMinResolutionCandidates.Select(c => c.ToString(CultureInfo.InvariantCulture))).ToList();
        CmbAdvMinResolution.SelectedIndex = 0;

        CmbAdvMinFps.ItemsSource = new[] { automatic }.Concat(AdvancedMinFpsCandidates.Select(c => c.ToString("0.##", CultureInfo.InvariantCulture))).ToList();
        CmbAdvMinFps.SelectedIndex = 0;

        CmbAdvCodecLock.ItemsSource = new[] { automatic }.Concat(FfmpegArguments.OfferedCodecs(CodecModel.CurrentPlatform).OrderBy(c => c, StringComparer.OrdinalIgnoreCase)).ToList();
        CmbAdvCodecLock.SelectedIndex = 0;

        InitializeFilterUi(automatic);
        InitializeTrackUi();
    }

    private bool _suzgecEsitleniyor;

    internal ComboBox[] FilterBoxes() => new[] { CmbFltSharpen, CmbFltRotate };

    internal RadioButton[] FltDeinterlace => new[] { RbFltDeintAuto, RbFltDeintOff, RbFltDeintOn };
    internal RadioButton[] FltDenoise => new[] { RbFltDenoiseOff, RbFltDenoiseNlm, RbFltDenoiseHq };
    internal RadioButton[] FltStrength => new[] { RbFltStrLight, RbFltStrMedium, RbFltStrStrong };
    internal RadioButton[] FltColor => new[] { RbFltColorKeep, RbFltColor709, RbFltColor601 };

    internal RadioButton[] FilterRadios() => FltDeinterlace.Concat(FltDenoise).Concat(FltStrength).Concat(FltColor).ToArray();

    /// <summary>Şeridin seçili sırası; seçim yoksa 0. Sıra enum sırasıyla aynı.</summary>
    internal static int Secili(RadioButton[] serit) => Math.Max(0, Array.FindIndex(serit, r => r.IsChecked == true));

    private static void Sec(RadioButton[] serit, int sira) => serit[Math.Clamp(sira, 0, serit.Length - 1)].IsChecked = true;

    private static void Yaz(RadioButton[] serit, params object[] etiketler)
    {
        for (var i = 0; i < serit.Length; i++) serit[i].Content = etiketler[i];
    }

    internal CheckBox[] FilterChecks() => new[] { ChkFltDetelecine, ChkFltDeblock, ChkFltDeband, ChkFltGray };

    /// <summary>
    /// C1-5: süzgeç panelinin listeleri. Sıraları <see cref="VideoFilterOptions"/>'ın enum
    /// sırasıyla aynı; seçim sıra numarasıyla enuma çevrilir. Liste kurulunca denetimler
    /// metin kutusundan yeniden okunur, dil değişimi seçimi kaybettirmez.
    /// </summary>
    private void InitializeFilterUi(string automatic)
    {
        var was = _suzgecEsitleniyor;
        _suzgecEsitleniyor = true;
        var off = Say("main.advanced.filters.off");
        var strengths = new[] { Say("main.advanced.filters.light"), Say("main.advanced.filters.medium"), Say("main.advanced.filters.strong") };
        Yaz(FltDeinterlace, automatic, off, Say("main.advanced.filters.on"));
        Yaz(FltDenoise, off, "NLMeans", "hqdn3d");
        Yaz(FltStrength, strengths);
        CmbFltSharpen.ItemsSource = new[] { off }.Concat(strengths).ToArray();
        CmbFltRotate.ItemsSource = new[]
        {
            off, Say("main.advanced.filters.rotate.clock"), Say("main.advanced.filters.rotate.cclock"), Say("main.advanced.filters.rotate.180"),
            Say("main.advanced.filters.rotate.hflip"), Say("main.advanced.filters.rotate.vflip")
        };
        Yaz(FltColor, Say("main.advanced.filters.color.keep"), "BT.709", "BT.601");
        SuzgecDenetimleriniYaz(TxtAdvFilters.Text is { } metin && !string.IsNullOrWhiteSpace(metin)
            ? SuzgecOku(out _)
            : VideoFilterOptions.Default);
        _suzgecEsitleniyor = was;
    }

    private void SuzgecDenetimleriniYaz(VideoFilterOptions o)
    {
        Sec(FltDeinterlace, (int)o.Deinterlace);
        Sec(FltDenoise, (int)o.Denoise);
        Sec(FltStrength, (int)o.DenoiseStrength);
        FltStrengthGroup.IsEnabled = o.Denoise != DenoiseFilter.Off;
        CmbFltSharpen.SelectedIndex = (int)o.Sharpen;
        CmbFltRotate.SelectedIndex = (int)o.Transpose;
        Sec(FltColor, (int)o.ColorMatrix);
        ChkFltDetelecine.IsChecked = o.Detelecine;
        ChkFltDeblock.IsChecked = o.Deblock;
        ChkFltDeband.IsChecked = o.Deband;
        ChkFltGray.IsChecked = o.Grayscale;
    }

    /// <summary>
    /// Denetim değişti: metin kutusu tek kaynaktır, denetim onun üstüne yazılır. Kırpma ve
    /// kenar gibi panelde karşılığı olmayan belirteçler metinden okunup korunur.
    /// </summary>
    private void OnFilterControlChanged()
    {
        if (_suzgecEsitleniyor) return;
        static int Index(ComboBox box) => Math.Max(0, box.SelectedIndex);
        var o = SuzgecOku(out _) with
        {
            Deinterlace = (DeinterlaceMode)Secili(FltDeinterlace),
            Denoise = (DenoiseFilter)Secili(FltDenoise),
            DenoiseStrength = (FilterStrength)Secili(FltStrength),
            Sharpen = (SharpenMode)Index(CmbFltSharpen),
            Transpose = (TransposeMode)Index(CmbFltRotate),
            ColorMatrix = (ColorMatrixTarget)Secili(FltColor),
            Detelecine = ChkFltDetelecine.IsChecked == true,
            Deblock = ChkFltDeblock.IsChecked == true,
            Deband = ChkFltDeband.IsChecked == true,
            Grayscale = ChkFltGray.IsChecked == true
        };
        _suzgecEsitleniyor = true;
        FltStrengthGroup.IsEnabled = o.Denoise != DenoiseFilter.Off;
        TxtAdvFilters.Text = VideoFilterChain.Format(o);
        _suzgecEsitleniyor = false;
    }

    /// <summary>Metin değişti: geçerliyse denetimler eşitlenir, bozuksa olduğu gibi kalır; plan yeniden hesaplanır.</summary>
    private void OnFilterTextChanged()
    {
        if (!_suzgecEsitleniyor)
        {
            var o = SuzgecOku(out var hata);
            if (hata is null)
            {
                _suzgecEsitleniyor = true;
                SuzgecDenetimleriniYaz(o);
                _suzgecEsitleniyor = false;
            }
        }
        RefreshAdvancedHints();
        OnOptionChanged();
    }

    /// <summary>Dil değişince "Otomatik" ve enum etiketleri yeniden kurulur, seçim korunur.</summary>
    private void RefreshAdvancedTexts()
    {
        var boxes = AdvBoxes();
        var indices = boxes.Select(box => box.SelectedIndex).ToArray();
        var wasSyncing = _syncing;
        _syncing = true;
        InitializeAdvancedUi();
        for (var i = 0; i < boxes.Length; i++)
            if (indices[i] >= 0 && indices[i] < boxes[i].ItemCount) boxes[i].SelectedIndex = indices[i];
        _syncing = wasSyncing;
    }

    private void OnToggleAdvanced(object? sender, RoutedEventArgs e) => SetSection(AdvancedBody, GlyphAdvanced, !AdvancedBody.IsVisible);

    private void OnToggleQuality(object? sender, RoutedEventArgs e) => SetSection(QualitySectionBody, GlyphQuality, !QualitySectionBody.IsVisible);

    private void OnToggleAudio(object? sender, RoutedEventArgs e) => SetSection(AudioBody, GlyphAudio, !AudioBody.IsVisible);

    private void OnToggleFrame(object? sender, RoutedEventArgs e) => SetSection(FrameBody, GlyphFrame, !FrameBody.IsVisible);

    private void SetSection(Control body, Avalonia.Controls.Shapes.Path glyph, bool open)
    {
        body.IsVisible = open;
        Chevron(glyph, open);
        RefreshSectionSummaries();
    }

    /// <summary>
    /// Ok geometrisi sabit (<c>IconChevronDown</c>); açık hâl <c>open</c> sınıfıyla 180° döner.
    /// Dönüşün geçişi yalnız canlandırmalı pencerede kurulu (<c>MainWindow.axaml</c>, <c>Path.chevron</c>).
    /// </summary>
    private static void Chevron(Avalonia.Controls.Shapes.Path glyph, bool open) => glyph.Classes.Set("open", open);

    internal void ExpandAdvanced() => SetSection(AdvancedBody, GlyphAdvanced, true);

    internal IReadOnlyList<ToggleButton> ShrinkChoiceToggles() => new ToggleButton[]
    {
        RbCodecAuto, RbCodecCompatible, RbCodecSmallest, RbHdrPreserve, RbHdrSdr, RbFillTarget, RbFillCeiling,
        ChkWhatsAppCompatible, RbFixedSource, RbFixed1080, RbFixed720, RbFixed480
    };

    /// <summary>Sabit çözünürlük seçenekleri, kısa kenar. Sıra radyoların sırasıyla aynı; 0 kaynak boyu.</summary>
    internal static readonly IReadOnlyList<int> FixedResolutionCandidates = new[] { 1080, 720, 480 };

    private RadioButton[] FixedResolutionRadios() => new[] { RbFixed1080, RbFixed720, RbFixed480 };

    /// <summary>0 kaynak boyu, 1.. <see cref="FixedResolutionCandidates"/> sırası.</summary>
    internal int FixedResolutionIndex
    {
        get
        {
            var radios = FixedResolutionRadios();
            for (var i = 0; i < radios.Length; i++)
                if (radios[i].IsChecked == true) return i + 1;
            return 0;
        }
        set
        {
            var radios = FixedResolutionRadios();
            var index = value >= 1 && value <= radios.Length ? value : 0;
            for (var i = 0; i < radios.Length; i++) radios[i].IsChecked = index == i + 1;
            RbFixedSource.IsChecked = index == 0;
        }
    }

    /// <summary>
    /// Plana giden sabit boy. Dinamik çözünürlük kutusu işaretliyken ya da "Kaynak" seçiliyken
    /// yoktur; motor kendi karar verir ya da kaynak boyunu korur.
    /// </summary>
    internal int? FixedResolutionShortSide
        => ChkResolution.IsChecked == true || FixedResolutionIndex == 0 ? null : FixedResolutionCandidates[FixedResolutionIndex - 1];

    private void RefreshFrameAndCodecLocks()
    {
        FixedResolutionRow.IsVisible = ChkResolution.IsChecked != true;
        CodecChoiceRow.IsEnabled = ChkWhatsAppCompatible.IsChecked != true;
    }

    /// <summary>WhatsApp uyumu işaretliyse kodek seçimi ne olursa olsun uyumlu kol (H.264).</summary>
    internal int EffectiveCodecIndex => ChkWhatsAppCompatible.IsChecked == true ? 1 : CodecIndex;

    internal int SelectedIntentIndex => (int)_intent;

    internal int CodecIndex => RbCodecCompatible.IsChecked == true ? 1 : RbCodecSmallest.IsChecked == true ? 2 : 0;

    internal int FillPolicyIndex => RbFillCeiling.IsChecked == true ? 1 : 0;

    internal int HdrPolicyIndex => RbHdrSdr.IsChecked == true ? 1 : 0;

    private void SetCodecIndex(int index)
    {
        RbCodecCompatible.IsChecked = index == 1;
        RbCodecSmallest.IsChecked = index == 2;
        RbCodecAuto.IsChecked = index is not (1 or 2);
    }

    private void SetFillIndex(int index)
    {
        RbFillCeiling.IsChecked = index == 1;
        RbFillTarget.IsChecked = index != 1;
    }

    private void SetHdrIndex(int index)
    {
        RbHdrSdr.IsChecked = index == 1;
        RbHdrPreserve.IsChecked = index != 1;
    }

    /// <summary>
    /// Yonga seridinin tamami: her yonga bir hedefi ve o hedefin plan karsiligini tasir.
    /// <c>SizeCapped</c> false olan tek yonga <c>ChipArchive</c>'dir; onun kisiti boyut
    /// degil kalitedir.
    /// </summary>
    internal readonly record struct ChipPlan(
        string Chip, double? TargetMb, bool SizeCapped, Intent Intent, CodecPreference Codec, FillPolicy Fill);

    internal static IReadOnlyList<ChipPlan> ChipPlans() => PresetLibrary.BuiltIn.Chips()
        .Select(preset => new ChipPlan(preset.Chip!, preset.TargetMb, preset.SizeCapped, preset.Intent, preset.Codec, preset.Fill))
        .ToList();

    internal void ApplyChipPlan(string chip)
    {
        var plan = ChipPlans().Single(candidate => candidate.Chip == chip);
        _presetContainer = null;
        _intent = plan.Intent;
        _chipSizeCapped = plan.SizeCapped;
        _platformChip = plan.TargetMb is not null;
        SetCodecIndex(plan.Codec switch
        {
            CodecPreference.Compatible => 1,
            CodecPreference.MaxCompression => 2,
            _ => 0
        });
        SetFillIndex(plan.Fill == FillPolicy.FillTarget ? 0 : 1);

        if (plan.TargetMb is { } fixedMb)
        {
            if (fixedMb > SliderTarget.Maximum) SliderTarget.Maximum = fixedMb;
            TxtTarget.Text = FormatBoxNumber(fixedMb);
        }
        else if (plan.SizeCapped && _info is not null)
        {
            TxtTarget.Text = FormatBoxNumber(Math.Round(_info.FileSizeMb / 2, 1));
        }

        RefreshChipDerivation();
        RefreshSectionSummaries();
    }

    private string CodecLabel() => ChkWhatsAppCompatible.IsChecked == true ? Say("main.whatsapp-compatible") : CodecIndex switch
    {
        1 => Say("main.codec.compatible"),
        2 => Say("main.codec.smallest"),
        _ => Say("main.codec.automatic")
    };

    private string FillLabel() => FillPolicyIndex == 1 ? Say("main.fill.ceiling") : Say("main.fill.target");

    private void RefreshChipDerivation()
    {
        var size = _chipSizeCapped
            ? Say("main.derivation.size", TxtTarget.Text ?? "")
            : Say("main.derivation.no-cap");
        TxtChipDerivation.Text = string.Join(" · ", size, CodecLabel(), FillLabel());
    }

    /// <summary>
    /// Katlanmis her bolum basligi kendi degerlerini yazar; katlamak durumu gizlemez.
    /// </summary>
    private void RefreshSectionSummaries()
    {
        var quality = new List<string> { CodecLabel(), FillLabel() };
        if (HdrPolicyPanel.IsVisible) quality.Add(HdrPolicyIndex == 1 ? Say("main.hdr.sdr") : Say("main.hdr.preserve"));
        TxtQualitySummary.Text = QualitySectionBody.IsVisible ? "" : string.Join(" · ", quality);

        TxtAudioSummary.Text = AudioBody.IsVisible
            ? ""
            : string.Join(" · ", CmbAdvAudioKbps.SelectedItem as string ?? "", CmbAdvAudioChannels.SelectedItem as string ?? "",
                CmbAdvAudioCodec.SelectedItem as string ?? "");

        var frame = new List<string>
        {
            ChkResolution.IsChecked == true ? Say("main.allow.resolution") : FixedResolutionIndex > 0 ? FixedResolutionRadios()[FixedResolutionIndex - 1].Content as string ?? "" : Say("main.section.frame.resolution-locked"),
            ChkFps.IsChecked == true ? Say("main.allow.fps") : Say("main.section.frame.fps-locked")
        };
        TxtFrameSummary.Text = FrameBody.IsVisible ? "" : string.Join(" · ", frame);

        var advanced = new SelectingItemsControl[] { CmbAdvCrf, CmbAdvPreset, CmbAdvTune, CmbAdvCodecLock }
            .Count(box => box.SelectedIndex > 0)
            + (AdvModeIndex > 0 ? 1 : 0)
            + (AdvEncoderPathIndex > 0 ? 1 : 0);
        TxtAdvancedSummary.Text = AdvancedBody.IsVisible
            ? ""
            : advanced == 0 ? Say("main.section.advanced.none") : Say("main.section.advanced.overrides", advanced);
    }

    private static string? AdvancedText(ComboBox box) => box.SelectedIndex > 0 ? box.SelectedItem as string : null;

    /// <summary>
    /// Dokuz gelişmiş kalemi <see cref="PlanOptions"/>'a taşır. Kutu 0. sırada durduğu
    /// sürece "Otomatik" demektir ve alan <c>null</c>/varsayılan kalır.
    /// </summary>
    private void ApplyAdvancedOptions(PlanOptions options)
    {
        if (AdvModeIndex > 0)
            options.LockedMode = AdvModeIndex == 1 ? EncodeMode.Crf : EncodeMode.TwoPass;

        if (AdvancedText(CmbAdvCrf) is { } crfText
            && double.TryParse(crfText, NumberStyles.Float, CultureInfo.InvariantCulture, out var crf))
            options.LockedCrf = crf;

        if (AdvancedText(CmbAdvPreset) is { } presetText)
            options.LockedPreset = presetText;

        if (AdvancedText(CmbAdvTune) is { } tuneText)
            options.LockedTune = tuneText;

        if (AdvancedText(CmbAdvModulus) is { } modulusText
            && int.TryParse(modulusText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modulus))
            options.ScaleModulus = modulus;

        if (AdvancedText(CmbAdvAudioKbps) is { } audioKbpsText
            && int.TryParse(audioKbpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var audioKbps))
            options.LockedAudioKbps = audioKbps;

        options.AudioChannels = CmbAdvAudioChannels.SelectedIndex switch
        {
            1 => AudioChannelOverride.Stereo,
            2 => AudioChannelOverride.Mono,
            3 => AudioChannelOverride.None,
            4 => AudioChannelOverride.Source,
            _ => AudioChannelOverride.Auto
        };

        options.AudioCodec = CmbAdvAudioCodec.SelectedIndex switch
        {
            1 => AudioCodecChoice.Aac,
            2 => AudioCodecChoice.Ac3,
            3 => AudioCodecChoice.Eac3,
            _ => AudioCodecChoice.Auto
        };

        options.KeepAllTracks = ChkAdvKeepTracks.IsChecked == true;
        options.DropMetadata = ChkAdvDropMetadata.IsChecked == true;
        options.PlatformDelivery = _platformChip;
        options.PreferredLanguage = Strings.Language;

        if (AdvancedText(CmbAdvMinResolution) is { } minResText
            && int.TryParse(minResText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minRes))
            options.MinResolutionHeight = minRes;

        if (AdvancedText(CmbAdvMinFps) is { } minFpsText
            && double.TryParse(minFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var minFps))
            options.MinFps = minFps;

        options.EncoderPath = AdvEncoderPathIndex switch
        {
            1 => EncoderPathOverride.Software,
            2 => EncoderPathOverride.Hardware,
            _ => EncoderPathOverride.Auto
        };

        options.LockedCodec = AdvancedText(CmbAdvCodecLock);

        options.Filters = KirpmalariUygula(SuzgecOku(out _));
        ApplyTrackOptions(options);
    }

    /// <summary>
    /// Gelişmiş paneldeki süzgeç kutusunu okur. Boş kutu bugünkü davranıştır; bozuk belirtim
    /// varsayılana düşer ve <paramref name="hata"/> kutunun yanındaki satırı besler — plan
    /// hesabı sessizce yanlış süzgeçle koşmasın.
    /// </summary>
    private VideoFilterOptions SuzgecOku(out string? hata)
    {
        hata = null;
        var metin = TxtAdvFilters.Text;
        if (string.IsNullOrWhiteSpace(metin)) return VideoFilterOptions.Default;
        try
        {
            return VideoFilterChain.Parse(metin);
        }
        catch (ArgumentException ex)
        {
            hata = ex.Message;
            return VideoFilterOptions.Default;
        }
    }

    /// <summary>
    /// Her kalemin yanındaki "şu an" satırı — motorun az önce hesapladığı plandan okunur,
    /// kalem "Otomatik" dursa da doluysa da aynı yerden gelir (K4 CHECK).
    /// </summary>
    private void RefreshAdvancedHints()
    {
        var plan = ActivePlan;
        var has = plan is not null;
        TxtAdvModeNow.Text = has ? SimdiSatiri(plan!.ModeEnum switch
        {
            EncodeMode.Crf => Say("main.advanced.mode.crf"),
            EncodeMode.PassThrough => Say("main.plan.mode.copy"),
            _ => Say("main.advanced.mode.two-pass")
        }) : "";
        TxtAdvCrfNow.Text = has ? SimdiSatiri(plan!.Crf is { } crf ? crf.ToString(CultureInfo.InvariantCulture) : "-") : "";
        TxtAdvPresetNow.Text = has ? SimdiSatiri(plan!.Preset) : "";
        TxtAdvTuneNow.Text = has ? SimdiSatiri(plan!.Tune ?? "-") : "";
        TxtAdvAudioKbpsNow.Text = has ? SimdiSatiri(plan!.AudioBitrateK.ToString(CultureInfo.InvariantCulture)) : "";
        TxtAdvAudioChannelsNow.Text = has
            ? SimdiSatiri(plan!.AudioChannels?.ToString(CultureInfo.InvariantCulture) ?? Say("main.advanced.audio-channels.none"))
            : "";
        TxtAdvAudioCodecNow.Text = has ? SimdiSatiri(plan!.AudioCodec ?? Say("main.advanced.audio-channels.none")) : "";
        TxtAdvMinResolutionNow.Text = has ? SimdiSatiri(Bicim.Cozunurluk(plan!.Width, plan.Height)) : "";
        TxtAdvMinFpsNow.Text = has ? SimdiSatiri(Bicim.Kare(plan!.Fps, Strings.Culture)) : "";
        TxtAdvEncoderPathNow.Text = has ? SimdiSatiri(CodecModel.IsHardware(plan!.Codec)
            ? Say("main.advanced.encoder-path.hardware")
            : Say("main.advanced.encoder-path.software")) : "";
        TxtAdvCodecLockNow.Text = has ? SimdiSatiri(plan!.Codec) : "";
        SuzgecOku(out var suzgecHata);
        TxtAdvFiltersNow.Text = suzgecHata is not null
            ? Say("main.advanced.filters.bad")
            : has && _info is { } kaynak
                ? SimdiSatiri(string.Join(", ", VideoFilterChain.Steps(kaynak, plan!)) is { Length: > 0 } zincir ? zincir : "-")
                : "";

        TxtTargetCrfLockedNotice.IsVisible = has && plan!.ReasonCodes.Any(note => note.Code == ReasonCode.ManualCrfOverride);
    }

    /// <summary>
    /// Hedef listesi <c>paylasim-hedefleri.json</c>'dan gelir. Dosya yoksa şema
    /// varsayılanları kullanılır; ne liste ne de tavanlar XAML'de sabit durur, bu yüzden
    /// JSON'a eklenen üçüncü bir hedef burada kendiliğinden belirir.
    /// </summary>
    private void InitializeShareUi()
    {
        _shareTargets = CoreShare.ShareTargetTable.LoadOrFallback();

        BuildShareTargetStrip();
        RefreshShareTarget();
    }

    private readonly List<RadioButton> _shareTargetRadios = [];

    private void BuildShareTargetStrip()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        ShareTargetStrip.Children.Clear();
        _shareTargetRadios.Clear();
        foreach (var target in _shareTargets.Targets)
        {
            var radio = new RadioButton { GroupName = "ShareTarget", Content = target.DisplayName };
            var index = _shareTargetRadios.Count;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked != true) return;
                ShareTargetIndex = index;
                OnShareTargetChanged();
            };
            _shareTargetRadios.Add(radio);
            ShareTargetStrip.Children.Add(radio);
        }
        ShareTargetIndex = Math.Max(0, IndexOfTarget(_shareTargets.DefaultTarget));
        _syncing = wasSyncing;
    }

    internal int ShareTargetIndex
    {
        get => _shareTargetRadios.FindIndex(radio => radio.IsChecked == true);
        set
        {
            for (var index = 0; index < _shareTargetRadios.Count; index++)
                _shareTargetRadios[index].IsChecked = index == value;
        }
    }

    internal IReadOnlyList<RadioButton> ShareTargetRadios => _shareTargetRadios;

    private int IndexOfTarget(CoreShare.ShareTarget? target)
    {
        if (target is null) return -1;
        for (var index = 0; index < _shareTargets.Targets.Count; index++)
            if (string.Equals(_shareTargets.Targets[index].Id, target.Id, StringComparison.OrdinalIgnoreCase))
                return index;
        return -1;
    }

    private void OnShareTargetChanged()
    {
        if (_syncing) return;
        RefreshShareTarget();
        SaveSettings();
    }

    private CoreShare.ShareTarget SelectedShareTarget()
    {
        var index = ShareTargetIndex;
        return index >= 0 && index < _shareTargets.Targets.Count
            ? _shareTargets.Targets[index]
            : _shareTargets.DefaultTarget ?? CoreShare.ShareTargetTable.Fallback.Targets[0];
    }

    private void RefreshShareTarget()
    {
        var target = SelectedShareTarget();
        TxtShareCeiling.Text = DescribeBytes(target.MaxBytes);

        var wasSyncing = _syncing;
        _syncing = true;
        CmbShareRetention.IsVisible = target.RetentionDays.Count > 0;
        TxtShareRetentionFixed.IsVisible = target.RetentionDays.Count == 0;
        if (target.RetentionDays.Count > 0)
        {
            CmbShareRetention.ItemsSource = target.RetentionDays
                .Select(day => day == 1 ? Say("settings.share.day-one") : Say("settings.share.days", day))
                .ToList();
            var chosen = target.RetentionDays.ToList().IndexOf(target.DefaultRetentionDays ?? -1);
            CmbShareRetention.SelectedIndex = chosen >= 0 ? chosen : 0;
        }
        else
        {
            TxtShareRetentionFixed.Text = target.FixedRetentionHours is { } hours
                ? hours == 1 ? Say("settings.share.hour-one-fixed") : Say("settings.share.hours-fixed", hours)
                : Say("settings.share.not-stated");
        }
        _syncing = wasSyncing;

        // Silme yeteneği hedefe göre değişir ve gizlenmez: uguu.se gönderene silme jetonu
        // vermiyor, kullanıcı bunu seçim anında bilmek zorunda.
        BtnShareDelete.IsVisible = target.CanDelete;
        TxtShareDeleteNote.Text = target.CanDelete
            ? Say("settings.share.can-delete", target.DisplayName)
            : target.FixedRetentionHours is { } window
                ? Say("settings.share.no-delete-window", target.DisplayName, window)
                : Say("settings.share.no-delete", target.DisplayName);
    }

    /// <summary>
    /// Yüklemenin konuştuğu hedef satırı. Ayarlardaki liste yalnız görüneni okuyor, uç
    /// noktalar motorun tablosunda duruyor; ikisi aynı dosyadan gelir ve kimlikle eşlenir.
    /// Tablo bulunamazsa paylaşım yapılamaz, çünkü adres koda gömülü değildir.
    /// </summary>
    private CoreShare.ShareTarget? SelectedShareEndpoint()
    {
        var target = _shareTargets.Find(SelectedShareTarget().Id);
        return CoreShare.ShareProviderFactory.CanCreate(target) ? target : null;
    }

    private ShareFlow Share() => _shareFlow ??= new ShareFlow(target =>
        CoreShare.ShareProviderFactory.Create(
            target,
            _shareTransport ??= new CoreShare.HttpClientTransport(),
            _shareTargets));

    /// <summary>Seçili ömür, gün. Hedef ömür seçtirmiyorsa boş.</summary>
    private int? SelectedRetentionDays()
    {
        var target = SelectedShareTarget();
        if (target.RetentionDays.Count == 0) return null;
        var index = CmbShareRetention.SelectedIndex;
        return index >= 0 && index < target.RetentionDays.Count ? target.RetentionDays[index] : target.DefaultRetentionDays;
    }

    /// <summary>
    /// Paylaş düğmesi yalnız teslim edilmiş bir dosya varken görünür. Yeni bir kodlama
    /// başladığında önceki bağlantı da düşer: gösterilen adres artık yeni dosyayı göstermez.
    /// </summary>
    private void ResetShare(bool fileReady)
    {
        BtnShare.IsVisible = fileReady;
        BtnShare.IsEnabled = fileReady;
        BtnShareCancel.IsVisible = false;
        ShareProgress.IsVisible = false;
        ShareProgress.Value = 0;
        ShareLinkRow.IsVisible = false;
        TxtShareLink.Text = "";
        TxtShareStatus.Text = "";
        BtnShareDelete.IsEnabled = false;
    }

    internal void ResetShareForTest(bool fileReady) => ResetShare(fileReady);

    private void SetSharing(bool sharing)
    {
        BtnShare.IsEnabled = !sharing;
        BtnShareCancel.IsVisible = sharing;
        ShareProgress.IsVisible = sharing;
        if (sharing) ShareProgress.Value = 0;
    }

    private ShareRetryBinder? _shareRetry;

    private ShareRetryBinder Retry() => _shareRetry ??= new ShareRetryBinder(
        BtnShareRetry, () => _shareTargets, id => ShareOnce(id));

    /// <summary>Yeniden deneme düğmesinin o anki hali; ölçü piksele değil buna bakar.</summary>
    internal ShareRetryPrompt ShareRetryPromptForTest => Retry().Current;

    private void OnShare(object? sender, RoutedEventArgs e) => ShareOnce(null);

    private async void ShareOnce(string? targetIdOverride)
    {
        if (_lastOutput is null || !File.Exists(_lastOutput))
        {
            TxtShareStatus.Text = Say("settings.share.nothing");
            return;
        }

        var target = targetIdOverride is null
            ? SelectedShareEndpoint()
            : _shareTargets?.Find(targetIdOverride) ?? SelectedShareEndpoint();

        if (target is null)
        {
            TxtShareStatus.Text = Say("settings.share.targets-missing", CoreShare.ShareTargetTable.FileName);
            return;
        }

        var flow = Share();
        if (flow.Running) return;

        SetSharing(true);
        Retry().Hide();
        TxtShareStatus.Text = Say("settings.share.uploading");
        ShareLinkRow.IsVisible = false;
        TxtShareLink.Text = "";

        var progress = new Progress<CoreShare.UploadProgress>(step => ShareProgress.Value = step.Fraction);
        var result = await flow.ShareAsync(target, _lastOutput, SelectedRetentionDays(), progress);

        SetSharing(false);
        ShowShareResult(result, flow);
    }

    private void ShowShareResult(CoreShare.ShareResult result, ShareFlow flow)
    {
        if (result.Ok && result.Link is { } link)
        {
            TxtShareLink.Text = link.Url;
            Fade(ShareLinkRow, true);
            Retry().Hide();
            BtnShareDelete.IsEnabled = flow.CanDelete;
            TxtShareStatus.Text = link.ExpiresAt is { } expires
                ? Say("settings.share.shared-until", Bicim.Damga(expires, Strings.Culture))
                : Say("settings.share.shared");
            return;
        }

        ShareLinkRow.IsVisible = false;
        TxtShareLink.Text = "";
        Retry().Show(result);
        BtnShareDelete.IsEnabled = flow.CanDelete;
        TxtShareStatus.Text = result.Failure == CoreShare.ShareFailure.Cancelled
            ? Say("settings.share.cancelled")
            : $"{Say("settings.share.failed")}: {ShareMessage.Of(result)}";
    }

    private void OnShareCancel(object? sender, RoutedEventArgs e) => _shareFlow?.Cancel();

    private async void OnCopyShareLink(object? sender, RoutedEventArgs e)
    {
        var clipboard = Clipboard;
        TxtShareStatus.Text = await CopyShareLinkAsync(
            clipboard is null ? null : text => clipboard.SetTextAsync(text), TxtShareLink.Text ?? "", Say);
    }

    /// <summary>
    /// Paylaşım bağlantısını panoya yazar ve kullanıcıya gösterilecek durumu döndürür: yazıldıysa
    /// <c>settings.share.link-copied</c>, pano yoksa ya da yazma düşerse <c>main.ai.clipboard-failed</c>
    /// ve nedeni. İki pencere aynı sonucu aynı sözle söyler; adres her durumda seçilebilir kalır.
    /// </summary>
    internal static async Task<string> CopyShareLinkAsync(Func<string, Task>? setText, string link, Func<string, string> say)
    {
        try
        {
            if (setText is null) throw new InvalidOperationException(say("main.ai.no-clipboard"));
            await setText(link);
            return say("settings.share.link-copied");
        }
        catch (Exception ex)
        {
            return $"{say("main.ai.clipboard-failed")}: {ex.Message}";
        }
    }

    private async void OnShareDelete(object? sender, RoutedEventArgs e)
    {
        var flow = Share();
        if (flow.Link is null) return;
        if (SelectedShareEndpoint() is not { } target) return;

        BtnShareDelete.IsEnabled = false;
        var result = await flow.DeleteAsync(target);
        if (result.Ok)
        {
            ShareLinkRow.IsVisible = false;
            TxtShareLink.Text = "";
            TxtShareStatus.Text = Say("settings.share.closed");
            return;
        }

        BtnShareDelete.IsEnabled = flow.CanDelete;
        TxtShareStatus.Text = $"{Say("settings.share.close-failed")}: {ShareMessage.Of(result)}";
    }

    /// <summary>
    /// Tavan JSON'da bayt olarak duruyor ve tam ikilik katlar: 128 MiB ve 25 GiB. Ondalık
    /// birime yuvarlamak sayıyı değiştirirdi, bu yüzden ikilik ad yazılır.
    /// </summary>
    internal static string DescribeBytes(long bytes) =>
        bytes <= 0 ? "-" : Bicim.Boyut.Bayt(bytes, Strings.Culture);

    private void OnAutoUpdateChanged()
    {
        if (_updateUiSyncing) return;

        var settings = UpdateSettings.Load(SettingsPathOverride);
        settings.AutoUpdate = ChkAutoUpdate.IsChecked == true;
        try
        {
            settings.Save(SettingsPathOverride);
        }
        catch (Exception ex)
        {
            TxtSystemStatus.Text = $"{Say("main.error.setting")}: {ex.Message}";
            return;
        }

        if (UpdateCheck.AutoUpdateEnabled(settings))
        {
            if (UpdateNoticeLocked) return;
            UpdateNotice.IsVisible = false;
            if (_updateBadgeState is not UpdateBadgeState.Ready) _ = OtomatikGuncellemeyiBaslatAsync();
            else if (OperatingSystem.IsWindows())
            {
                _kapanistaKurulacak = true;
                SetUpdateBadge(UpdateBadgeState.Ready);
            }
            return;
        }

        _kapanistaKurulacak = false;
        if (_sessizIndirme is not null) _updateCancel?.Cancel();
        _ = CheckForUpdateAsync();
    }

    /// <summary>
    /// Yeni sürümü arka planda sorar. Açılışı geciktirmez ve hiçbir hatada kullanıcıya
    /// bir şey göstermez: haber verilecek bir şey yoksa şerit hiç belirmez.
    /// </summary>
    /// <summary>
    /// Rozeti durumuna göre yazar: nokta rengi, metin ve ipucu tek yerden gelir. Metnin
    /// saatli biçimi <see cref="UpdateBadge.Compose"/>'da, renk eşlemesi burada; ikisi de
    /// özel rafın güncelleme paneli ölçütünden. Rozet denetim başlayana kadar görünmez.
    /// </summary>
    internal void SetUpdateBadge(UpdateBadgeState state)
    {
        _updateBadgeState = state;
        var govde = Say(state switch
        {
            UpdateBadgeState.Checking => "main.update.checking",
            UpdateBadgeState.UpToDate => "main.update.current",
            UpdateBadgeState.NewVersion => "main.update.badge",
            UpdateBadgeState.Downloading => "main.update.badge",
            UpdateBadgeState.Ready => "main.update.badge",
            UpdateBadgeState.Installing => "main.update.starting",
            _ => "main.update.offline"
        });
        var etiket = state switch
        {
            UpdateBadgeState.NewVersion or UpdateBadgeState.Downloading or UpdateBadgeState.Ready => "update.label",
            UpdateBadgeState.Offline => "sync.offline",
            _ => null
        };
        if (etiket is not null && Etiketler.Kapsar(Strings.Language)) govde = Etiketler.Get(etiket);

        var firca = state switch
        {
            UpdateBadgeState.UpToDate => "NeonSuccess",
            UpdateBadgeState.NewVersion => "AccentGradient",
            UpdateBadgeState.Downloading => "AccentGradient",
            UpdateBadgeState.Ready => "NeonSuccess",
            UpdateBadgeState.Installing => "NeonBlue",
            UpdateBadgeState.Offline => "NeonEmber",
            _ => "TextDisabled"
        };

        TxtUpdateBadge.Text = UpdateBadge.Compose(state, govde, DateTimeOffset.Now);
        if (this.TryFindResource(firca, out var kaynak) && kaynak is IBrush brush)
        {
            UpdateBadgeDot.Fill = brush;
            if (state is UpdateBadgeState.Ready)

                TxtUpdateBadge.Foreground = brush;
            else TxtUpdateBadge.ClearValue(TextBlock.ForegroundProperty);
        }
        ToolTip.SetTip(BtnUpdateBadge, state switch
        {
            UpdateBadgeState.Ready => HazirIpucu(),
            UpdateBadgeState.NewVersion when Etiketler.Kapsar(Strings.Language) => Etiketler.Get("update.download"),
            _ => TxtUpdateBadge.Text
        });
        AutomationProperties.SetName(BtnUpdateBadge, TxtUpdateBadge.Text);
        BtnUpdateBadge.IsVisible = true;
        RefreshUpdateNoticeButton();
    }

    /// <summary>
    /// Rozete tıklamak denetimi hemen tekrarlar ve rozet anında <c>Denetleniyor…</c>'ya
    /// düşer. Denetim sürerken ikinci tık işlemez.
    ///
    /// <para>Yeni sürüm varken tık iki adımlı akışın ilk adımıdır: panel açılır ve indirme
    /// arka planda başlar. İnerken ve indikten sonra tık yalnız paneli açar; kurulum
    /// panelin "Yükle" düğmesini bekler.</para>
    /// </summary>
    private void OnUpdateBadgeClicked(object? sender, RoutedEventArgs e)
    {
        if (_updateBadgeState == UpdateBadgeState.Checking) return;
        if (_updateBadgeState is UpdateBadgeState.Downloading or UpdateBadgeState.Ready or UpdateBadgeState.Installing)
        {
            UpdateNotice.IsVisible = true;
            return;
        }
        if (_updateBadgeState == UpdateBadgeState.NewVersion && _noticeVersion is not null)
        {
            UpdateNotice.IsVisible = true;
            StartUpdateDownload();
            return;
        }

        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync(bool yedek = false)
    {
        if (!yedek && UpdateCheck.AutoUpdateEnabled())
        {
            await Dispatcher.UIThread.InvokeAsync(() => BtnUpdateBadge.IsVisible = false);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => SetUpdateBadge(UpdateBadgeState.Checking));

        string version;
        try
        {
            using var http = new HttpClient { Timeout = UpdateProbeTimeout };
            var asset = UpdateCheck.ManifestAssetName(UpdateCheck.Rid);
            var json = await http.GetStringAsync(UpdateCheck.LatestAssetUrl(asset));
            version = UpdateCheck.ParseManifest(json).Version;
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() => SetUpdateBadge(UpdateBadgeState.Offline));
            return;
        }

        var yeniMi = UpdateCheck.IsNewer(version, AppVersion());
        var susturulmus = string.Equals(ReadDismissedVersion(), version, StringComparison.OrdinalIgnoreCase);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_updateBadgeState is UpdateBadgeState.Downloading or UpdateBadgeState.Ready) return;
            SetUpdateBadge(yeniMi ? UpdateBadgeState.NewVersion : UpdateBadgeState.UpToDate);
            if (!yeniMi)
            {
                UpdateHealth.RecordSuccess(SettingsPathOverride, DateTimeOffset.UtcNow);
                return;
            }

            _noticeVersion = version;
            TxtNoticeVersion.Text = version;
            if (susturulmus) return;

            UpdateNotice.IsVisible = true;
        });
    }

    /// <summary>
    /// Panelin indirme başladıktan sonraki tek düğmesi: inerken "İptal" olarak indirmeyi keser
    /// (<see cref="CancelUpdateDownload"/>); indikten sonra "Yükle" olur ve aşağıdaki kurulum
    /// akışı yalnız o zaman koşar. Önce hızlı yol denenir (<see cref="YerindeGuncelleme"/>):
    /// değişen dosyalar koşan süreç altında yeniden adlandırılıp yerine konur, yeni sürüm
    /// açılır, bu süreç kapanır. Hızlı yol kilit bulamaz ya da bir dosya taşınamazsa hiçbir
    /// şey değişmemiştir; o zaman başlatıcı elle yükleme kipinde açılır, bu süreç kapanır,
    /// başlatıcı çıkışı bekleyip güncellemeyi uygular ve uygulamayı yeni sürümle açar.
    /// Kendiliğinden güncelleme ayarına bakılmaz ve yazılmaz; elle bir yükleme tercihi
    /// değiştirmez. Kapanmadan önce rozete ara metin yazılmaz: pencere o karede gidiyor,
    /// bakım 400 ms'yi aşarsa ekrana yalnız başlatıcının paneli gelir.
    ///
    /// <para>Başlatıcısı olmayan kurulumda (Linux, düz macOS kopyası) yükleyecek bir şey
    /// yok; düğme o zaman yayın sayfasını açar. Panel kabuk komutu yazmıyor.</para>
    /// </summary>
    private void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        var launcher = LauncherUpdate.LocateLauncher(AppContext.BaseDirectory);
        if (launcher is null)
        {
            OpenExternal(UpdateCheck.ReleasesPageUrl);
            return;
        }

        if (_updateBadgeState == UpdateBadgeState.Downloading)
        {
            CancelUpdateDownload();
            return;
        }

        if (_updateBadgeState != UpdateBadgeState.Ready)
        {
            StartUpdateDownload();
            return;
        }

        if (HizliYukle()) return;

        try
        {
            var start = new ProcessStartInfo { FileName = launcher, UseShellExecute = false };
            start.ArgumentList.Add(LauncherUpdate.UpdateNowArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Process.Start(start);
        }
        catch (Exception exception)
        {
            TxtSystemStatus.Text = $"{Say("main.error.setting")}: {exception.Message}";
            return;
        }

        _stagedUpdate = null;
        Close();
    }

    /// <summary>
    /// Yükle'nin hızlı yolu. Sahne bu oturumda indiyse yerinde uygular, yeni sürümü açar ve
    /// pencereyi kapatır. Yanlış dönerse dosyalar olduğu gibidir ve çağıran eski yolu sürer.
    /// </summary>
    private bool HizliYukle()
    {
        var staged = _stagedUpdate;
        if (staged is null) return false;
        var appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var baseDirectory = Path.GetDirectoryName(appDirectory);
        if (string.IsNullOrEmpty(baseDirectory)) return false;
        if (!YerindeGuncelleme.Uygula(baseDirectory, appDirectory, staged)) return false;
        _stagedUpdate = null;

        try { using var yeni = YerindeGuncelleme.YeniSurumuAc(baseDirectory, appDirectory); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        Close();
        return true;
    }

    /// <summary>
    /// Gelismis sekmesi gorunurde degil; Hakkinda sekmesindeki sistem durumu satirina
    /// arka arkaya basmak aciyor. Sayac <see cref="DeveloperUnlock"/>'ta, esik ve
    /// pencere oradan okunuyor. Acilan sekme secilir; icindeki dugme onu geri kapatir.
    /// </summary>
    private void OnSystemStatusTapped(object? sender, PointerPressedEventArgs e)
    {
        if (TabAdvanced.IsVisible) return;
        if (!_developerUnlock.Tap(DateTimeOffset.UtcNow)) return;
        TabAdvanced.IsVisible = true;
        Tabs.SelectedItem = TabAdvanced;
    }

    private void OnHideAdvanced(object? sender, RoutedEventArgs e)
    {
        TabAdvanced.IsVisible = false;
        _developerUnlock.Reset();
        if (ReferenceEquals(Tabs.SelectedItem, TabAdvanced)) Tabs.SelectedIndex = ShrinkTabIndex;
    }

    private void OnDismissUpdateNotice(object? sender, RoutedEventArgs e)
    {
        if (UpdateNoticeLocked) return;
        UpdateNotice.IsVisible = false;
        if (_noticeVersion is not null) WriteDismissedVersion(_noticeVersion);
    }

    private static string DismissedNoticePath
    {
        get
        {
            var folder = Path.GetDirectoryName(UpdateSettings.DefaultPath);
            return string.IsNullOrEmpty(folder) ? DismissedNoticeFileName : Path.Combine(folder, DismissedNoticeFileName);
        }
    }

    private static string? ReadDismissedVersion()
    {
        try
        {
            return File.Exists(DismissedNoticePath) ? File.ReadAllText(DismissedNoticePath).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteDismissedVersion(string version)
    {
        try
        {
            var folder = Path.GetDirectoryName(DismissedNoticePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(DismissedNoticePath, version);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Yazılamazsa şerit bir sonraki açılışta yeniden belirir; başka bir sonucu yok.
        }
    }

    /// <summary>
    /// Hızlı mod kararının yazıldığı ayar dosyası. Boşken <see cref="UpdateSettings.DefaultPath"/>
    /// kullanılır; ölçüm süreç genelindeki ortam değişkenine dokunmadan kendi dosyasını verir.
    /// </summary>
    internal string? SettingsPathOverride { get; set; }

    /// <summary>
    /// Avalonia'nın XAML derleyicisi <c>RowDefinition</c>'a bir NameScope girişi vermiyor
    /// (yalnız <see cref="Control"/> alt sınıfları isimle bulunabiliyor), bu yüzden satır
    /// çevreleyen <c>PreviewPlanGrid</c> üzerinden 3. sıra olarak bulunuyor.
    /// </summary>
    private RowDefinition PlanPanelRow => PreviewPlanGrid.RowDefinitions[2];

    /// <summary>
    /// T163/K3: ayırıcının konumu kullanıcı verisidir, ölçü belirteci değil — bu yüzden
    /// <c>Theme.axaml</c>'e değil buraya, <see cref="UpdateSettings"/>'in yanına gider.
    /// <see cref="UpdateSettings"/>'in kendisi <c>VidShrink.Core</c>'da (bu sözleşmenin
    /// alanı dışında) durduğu için ayrı, küçük bir dosyaya yazılıyor; kayıt yeri yine
    /// aynı ayar klasörü.
    /// </summary>
    private string SplitterSettingsPath => Path.Combine(
        Path.GetDirectoryName(SettingsPathOverride ?? UpdateSettings.DefaultPath) ?? AppContext.BaseDirectory,
        LayoutFileName);

    private void SaveSplitterSettings()
    {
        if (_settingsSyncing || _syncing) return;
        if (PlanPanelRow.Height.GridUnitType != GridUnitType.Pixel) return;
        var height = PlanPanelRow.Height.Value;
        LayoutYaz(kok => kok["planPanelHeight"] = height);
    }

    private void RestoreSplitterSettings()
    {
        try
        {
            if (!File.Exists(SplitterSettingsPath)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(SplitterSettingsPath));
            if (!document.RootElement.TryGetProperty("planPanelHeight", out var element) || !element.TryGetDouble(out var height))
                return;

            var floor = PlanPanelRow.MinHeight;
            var ceiling = double.IsFinite(PlanPanelRow.MaxHeight) ? PlanPanelRow.MaxHeight : height;
            _settingsSyncing = true;
            PlanPanelRow.Height = new GridLength(Math.Clamp(height, floor, ceiling), GridUnitType.Pixel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TxtSystemStatus.Text = $"{Say("settings.error.save")}: {ex.Message}";
        }
        finally { _settingsSyncing = false; }
    }

    private void OnSplitterMoved() => SaveSplitterSettings();

    internal void RecalculateForTest() => Recalculate();
    internal EncodePlan? ActivePlanForTest => ActivePlan;
    internal string? AdvancedErrorForTest => TxtAdvancedError.IsVisible ? TxtAdvancedError.Text : null;
    internal void RestoreSplitterSettingsForTest() => RestoreSplitterSettings();
    internal void SetSplitterHeightForTest(double pixels) => PlanPanelRow.Height = new GridLength(pixels, GridUnitType.Pixel);
    internal double SplitterHeightForTest => PlanPanelRow.Height.Value;
    internal bool SplitterIsPixelForTest => PlanPanelRow.Height.GridUnitType == GridUnitType.Pixel;
    internal double SplitterFloorForTest => PlanPanelRow.MinHeight;
    internal double SplitterCeilingForTest => PlanPanelRow.MaxHeight;
    internal double SplitterRowActualHeightForTest => PlanPanelRow.ActualHeight;

    private void OnFastGpuChanged()
    {
        if (_syncing) return;
        try
        {
            var settings = UpdateSettings.Load(SettingsPathOverride);
            var enabled = ChkFastGpu.IsChecked == true;
            if (settings.FastGpu == enabled) return;
            settings.FastGpu = enabled;
            settings.Save(SettingsPathOverride);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TxtSystemStatus.Text = $"{Say("main.error.setting")}: {ex.Message}";
        }
        ApplyFastGpuTip();
    }

    private PlanOptions CurrentOptions()
    {
        var options = new PlanOptions
        {
            TargetMb = PlanTargetMb(),
            Intent = _intent,
            Codec = CodecFromIndex(EffectiveCodecIndex),
            FixedResolution = FixedResolutionShortSide,
            AllowResolutionDrop = ChkResolution.IsChecked == true,
            AllowFpsDrop = ChkFps.IsChecked == true,
            HdrPolicy = HdrPolicyIndex == 1 ? HdrPolicy.TonemapToSdr : HdrPolicy.Preserve,
            FillPolicy = FillPolicyIndex == 1 ? FillPolicy.QualityCeiling : FillPolicy.FillTarget,
            SpeedMode = ChkFastGpu.IsChecked == true ? SpeedMode.Fast : SpeedMode.Quality,
            DeliveredContainer = PresetLibrary.DeliveredContainer(_presetContainer)
        };
        ApplyAdvancedOptions(options);
        ApplyEncodeLoad(options);
        if (ChkWhatsAppCompatible.IsChecked == true) options.LockedCodec = null;
        return options;
    }

    private double ParseTargetMb()
        => TryReadBoxNumber(TxtTarget.Text, out var mb) && mb > 0 ? mb : WhatsAppTargetMb;

    /// <summary>
    /// Boyut tavani olmayan yonga secildiginde hedef kutusundaki sayi plani kurmaz:
    /// motor kalite tavanina kadar bit harcar. Tavansizligin sayisal karsiligi
    /// <see cref="PlanCalculator.QualityCeilingTargetMb"/>; yeni bir olcu uydurulmadi.
    /// </summary>
    internal double PlanTargetMb()
        => _chipSizeCapped || _info is null ? ParseTargetMb() : PlanCalculator.QualityCeilingTargetMb(_info);

    internal PlanOptions PlanOptionsForTest() => CurrentOptions();

    private static string? SelectedTag(SelectingItemsControl box)
        => (box.SelectedItem as Control)?.Tag as string;

    private async void OnBrowse(object? sender, RoutedEventArgs e) => await BrowseAsync(true);

    private async void OnBrowseConvert(object? sender, RoutedEventArgs e) => await BrowseAsync(true);

    /// <summary>
    /// Seçici dikişi; argüman çoklu seçimin açık olup olmadığı. Ölçüm sahte yol listesini buraya
    /// takar; üretimde sistemin dosya seçicisi.
    /// </summary>
    internal Func<bool, Task<IReadOnlyList<string>>>? SourcePicker { get; set; }

    /// <summary>
    /// Kuyruk dikişi. Ölçüm kuyruğa giden yolları buradan okur, pencere açılmaz ve kodlama başlamaz;
    /// üretimde <see cref="OpenBatch"/> penceresi gösterilir.
    /// </summary>
    internal Action<IReadOnlyList<string>>? BatchOpener { get; set; }

    /// <summary>
    /// DON-P6 kuyruk dikişi: Dönüştür sekmesi açıkken birden çok dosya buraya gider. Ölçüm yolları
    /// okur; üretimde <see cref="OpenConvertBatch"/> penceresi gösterilir.
    /// </summary>
    internal Action<IReadOnlyList<string>>? ConvertBatchOpener { get; set; }

    /// <summary>
    /// "Gözat": seçici iki sekmede de çoklu seçime açık; Dönüştür sekmesinin çoklu seçimi kendi
    /// kuyruğuna (<see cref="OpenConvertBatch"/>) gider. Tek dosya eskisi gibi süzülmeden bu pencereye yüklenir; birden çok dosya bırakmanın
    /// süzgecinden (<see cref="DroppedMedia.Collect(IEnumerable{string})"/>) geçip bırakmayla aynı
    /// kola (<see cref="OpenSourcesAsync"/>) gider. İş koşarken seçici açılmaz.
    /// </summary>
    internal async Task BrowseAsync(bool multiple)
    {
        if (_cts is not null) return;
        try
        {
            var paths = await (SourcePicker ?? PickSourcesAsync)(multiple);
            await OpenSourcesAsync(paths.Count == 1 ? paths[0] : null, DroppedMedia.Collect(paths, TabConvert.IsSelected));
        }
        catch (Exception ex)
        {
            ReportSourceError($"{Say("main.error.pick")}: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<string>> PickSourcesAsync(bool multiple)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = multiple,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media") { Patterns = ShellIntegration.MediaExtensions.Select(extension => "*." + extension).ToArray() },
                FilePickerFileTypes.All
            }
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    /// <summary>
    /// Bırakmanın ve Gözat'ın ortak kolu: tek dosya bu pencereye yüklenir, birden çok video kuyruk
    /// penceresine gider (Dönüştür sekmesindeyse Dönüştür kuyruğuna). Süzgeçten bir ya da sıfır video çıkan çoklu seçim hiçbir şey açmaz.
    /// </summary>
    internal async Task OpenSourcesAsync(string? file, IReadOnlyList<string> batch)
    {
        if (file is not null) await LoadAsync(file);
        else if (batch.Count > 1 && TabConvert.IsSelected) (ConvertBatchOpener ?? (paths => OpenConvertBatch(paths).Show()))(batch);
        else if (batch.Count > 1) (BatchOpener ?? (paths => OpenBatch(paths).Show()))(batch);
    }

    /// <summary>
    /// DON-P6: Dönüştür'ün toplu kuyruğu. Ortak ayar panelin o anki seçimleri, dosya başına seçilecekler
    /// kayıtlı Dönüştür ön ayarları; ikisi de <see cref="ConversionPreset"/>.
    /// </summary>
    internal ConvertQueueWindow OpenConvertBatch(IReadOnlyList<string> paths)
        => new(paths, CurrentConvertPreset(""), _convertPresets.ToList(),
            HdrPolicyIndex == 1 ? HdrPolicy.TonemapToSdr : HdrPolicy.Preserve,
            UsableFixedFolder(OutputFolderModeIndex, TxtOutputFolder.Text));

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        _dropBatch ??= DroppedBatch(e);
        var accepted = _cts is null && (TryGetDroppedFile(e) is not null || _dropBatch.Count > 1
            || _info is not null && DroppedSubtitles(e).Count > 0 || DroppedSequence(e, _dropBatch) is not null);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropVisual(accepted ? DropVisual.Accept : DropVisual.Reject);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _dropBatch = null;
        SetDropVisual(DropVisual.Idle);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = TryGetDroppedFile(e);
        var batch = _dropBatch ?? DroppedBatch(e);
        _dropBatch = null;
        e.Handled = true;
        SetDropVisual(DropVisual.Idle);
        if (_cts is not null) return;
        if (_info is not null && DroppedSubtitles(e) is { Count: > 0 } subtitles) AddSubtitleFiles(subtitles);
        else if (file is null && DroppedSequence(e, batch) is { } sequence) await LoadSequenceAsync(sequence.Files);
        else await OpenSourcesAsync(file, batch);
    }

    /// <summary>ARC-P4: Dönüştür'e video içermeyen numaralı resimler ya da klasör bırakılınca tek dizi kaynağı olur.</summary>
    private ImageSequence? DroppedSequence(DragEventArgs e, IReadOnlyList<string> batch)
    {
        if (!TabConvert.IsSelected || batch.Count > 0) return null;
        var paths = e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
        return paths is { Count: > 0 } ? DroppedMedia.Sequence(paths) : null;
    }

    /// <summary>
    /// C1-3: birden çok video ya da klasör kuyruk penceresine gider, pencerenin o anki seçenekleriyle.
    /// Tek video bugünkü gibi bu pencereye yüklenir.
    /// </summary>
    internal ShrinkJobWindow OpenBatch(IReadOnlyList<string> paths)
    {
        var options = CurrentOptions();
        options.ExternalSubtitles = Array.Empty<ExternalSubtitle>();
        options.ExternalAudio = null;
        options.Filters = options.Filters with { BurnSubtitle = null, BurnFont = null, BurnFontSize = null, BurnBox = false, Crop = SuzgecOku(out _).Crop };
        return new(paths, options, !_chipSizeCapped && _info is not null, PresetLibrary.DeliveredExtension(_presetContainer), OtomatikKirpmaAcik)
        {
            IstenenKalite = _kaliteSurer && _aiPlan is null ? ParseQualityTarget() : null,
            OlcerekAra = ChkAdvSearchByMeasure.IsChecked == true
        };
    }

    /// <summary>C1-6: kaynak açıkken bırakılan altyazı dosyaları o videoya iz olarak eklenir, video sanılıp açılmaz.</summary>
    private static IReadOnlyList<string> DroppedSubtitles(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>().Where(IsSubtitleFile).ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>();

    private IReadOnlyList<string> DroppedBatch(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return Array.Empty<string>();
        return DroppedMedia.Collect(items.Select(item => item.TryGetLocalPath()).OfType<string>(), TabConvert.IsSelected);
    }

    private void SetDropVisual(DropVisual state)
    {
        if (_dropVisual == state) return;
        _dropVisual = state;

        var (outline, fill, icon, scale) = state switch
        {
            DropVisual.Accept => ("NeonBlue", "NeonBlueActive", "NeonBlue", "scale(1.08)"),
            DropVisual.Reject => ("NeonPink", "NeonPinkFill", "NeonPink", "scale(0.94)"),
            _ => ("NeonBlueBorderStrong", "NeonBlueFill", "NeonBlue", "none")
        };

        DropOutline.Stroke = Paint(outline);
        DropOutline.Fill = Paint(fill);
        DropIcon.Stroke = Paint(icon);
        DropIcon.RenderTransform = TransformOperations.Parse(_motionReduced ? "none" : scale);

        ApplyDropText();
    }

    private void ApplyDropText()
    {
        var (title, hint) = _dropVisual switch
        {
            DropVisual.Accept when _dropBatch is { Count: > 1 } => ("main.drop.batch", "main.drop.batch-hint"),
            DropVisual.Accept => ("main.drop.release", "main.drop.hint"),
            DropVisual.Reject => ("main.drop.none", "main.drop.hint"),
            _ => ("main.drop.title", "main.drop.hint")
        };

        TxtDropTitle.Text = Bicim.Satir.SonuBagla(title == "main.drop.batch" ? Say(title, _dropBatch!.Count) : Say(title));
        TxtDropHint.Text = Say(hint);
    }

    private static string? TryGetDroppedFile(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList();
        if (items is null || items.Count != 1) return null;
        if (items[0] is IStorageFolder) return null;
        var path = items[0].TryGetLocalPath();
        if (path is null || Directory.Exists(path) || !File.Exists(path)) return null;
        return path;
    }

    /// <summary>
    /// C1-1: kalite doyduğu için hedefin altında teslim edilen dosya bir cümle alır; hedefi aşan ya da
    /// kırpılan teslim kendi cümlesini zaten taşıdığı için almaz.
    /// </summary>
    internal static bool ShowsSaturated(EncodeResult result) =>
        result.Success && result.Saturated && !result.OverTarget && result.Trim is null;

    internal static string SaturatedSuffix(EncodeResult result, double targetMb) =>
        ShowsSaturated(result) ? " " + Say("main.run.saturated", Num(result.OutputMb, "0.0"), Num(targetMb, "0.##")) : "";

    /// <summary>
    /// Büyük hâliyle kabul edilen teslimin cümlesi. Fark, motorun "hedefin üzerinde" dediği
    /// etkin hedefe göre (<see cref="EncodeRunner.EffectiveTargetMb"/>); kullanıcının hedefi
    /// kaynağa göre kırpıldığında o hedefle hesaplanan fark eksiye düşüyordu.
    /// </summary>
    internal static string AcceptedLargerText(double outputMb, double targetMb, EncodePlan plan)
    {
        var effectiveTargetMb = EncodeRunner.EffectiveTargetMb(targetMb, plan);
        return Say("main.run.accepted-larger", Num(outputMb - effectiveTargetMb, "0.00"), Num(effectiveTargetMb, "0.##"));
    }

    private void ReportSourceError(string message)
    {
        TxtSourceStatus.Text = message;
        TxtSourceStatus.IsVisible = true;
        TxtSystemStatus.Text = message;
    }

    private void ClearSourceError()
    {
        TxtSourceStatus.Text = "";
        TxtSourceStatus.IsVisible = false;
    }

    private void ShowSourceName()
    {
        TxtFileName.Text = _sourceName ?? Say("main.source.placeholder");
        TxtConvertSource.Text = _sequence is { } sequence ? SequenceSourceText(sequence)
            : _info is null && _sesKaynagi is null ? Say("main.convert.source-empty") : _sourceName;
        ApplyDropText();
    }

    private async Task LoadAsync(string path)
    {
        _sequence = null;
        _sesKaynagi = null;
        _sourceName = Path.GetFileName(path);
        ShowSourceName();
        Fade(SourceCard, true);
        ClearSourceError();

        MediaInfo probed;
        try { probed = await YoklaAsync(path); }
        catch (Exception ex)
        {
            _info = null;
            ResetTracksForSource();
            ShowSourceName();
            BtnStart.IsEnabled = BtnConvert.IsEnabled = false;
            Fade(InfoGrid, false);
            Fade(DropZone, true);
            ResetPlanView();
            RefreshQualityPanels();
            RefreshQualityTargetAvailability();
            ReportSourceError($"{Say("main.error.unusable")}: {DescribeFailure(ex)}");
            return;
        }

        if (probed.IsAudioOnly)
        {
            SesKaynaginiYukle(probed);
            return;
        }

        Media.Publish(path, probed);
        ApplyLoaded(path, probed);
        await MeasureComplexityAsync(probed);
    }

    /// <summary>
    /// Komut satırından gelen yolu, sürükle-bırakın kullandığı yükleyiciden geçirir.
    /// Yol yoksa hiçbir şey yapmaz; kötü dosyanın hatasını o yükleyici bildirir.
    /// </summary>
    internal Task LoadStartupFileAsync()
        => _startupFile is not { } path ? Task.CompletedTask : StartShellOpen(path, () => LoadStartupFileAsync(path));

    internal int PlayerTabIndex => Tabs.Items.IndexOf(TabPlayer);

    internal int ShrinkTabIndex => Tabs.Items.IndexOf(TabShrink);

    /// <summary>
    /// Biten bir kaydı küçültme sekmesine taşır. Yükleyici sürükle-bırakınkiyle aynı;
    /// kaydedici kendi çözümleyicisini kurmuyor.
    /// </summary>
    internal async Task OpenInShrinkAsync(string path)
    {
        Tabs.SelectedIndex = ShrinkTabIndex;
        await LoadAsync(path);
    }

    /// <summary>Biten bir kaydı oynatıcı sekmesinde açar.</summary>
    internal async Task OpenInPlayerAsync(string path)
    {
        Tabs.SelectedIndex = PlayerTabIndex;
        try { await Player.OpenAsync(path); }
        catch (Exception ex) { ReportPlayerOpenFailure(ex); }
    }

    private int SettingsTabIndex => Tabs.Items.IndexOf(TabSettings);

    internal int RecorderTabIndex => Tabs.Items.IndexOf(TabRecorder);

    internal PlayerView PlayerTab => Player;

    internal async Task LoadStartupFileAsync(string path)
    {
        Tabs.SelectedIndex = PlayerTabIndex;
        AcilisIzi.Yaz("sekme");
        IlkKareyiBekle();
        _ = CizimiOlcAsync(false);
        try { await Player.OpenAsync(path); }
        catch (Exception ex) { ReportPlayerOpenFailure(ex); }
        AcilisIzi.Yaz("motor-acildi");
        if (!ShellIntegration.IsPlayerOnly(path) && !MediaAddress.IsAddress(path)) await LoadAsync(path);
        AcilisIzi.Yaz("kucultme-yuklendi");
        _ = CizimiOlcAsync(true);
    }

    internal static string TabHeaderText(TabItem tab) => tab.Header switch
    {
        string text => text,
        StackPanel panel => panel.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "",
        _ => tab.Header?.ToString() ?? "",
    };

    internal Exception? PlayerOpenFailure { get; private set; }

    internal string SourceStatusText => TxtSourceStatus.Text ?? "";

    internal bool SourceStatusVisible => TxtSourceStatus.IsVisible;

    internal void ReportPlayerOpenFailure(Exception ex)
    {
        PlayerOpenFailure = ex;
        if (ex is global::VidShrink.Player.PlaybackEngineUnavailableException { MessageKey: { } key })
        {
            ReportSourceError($"{Say(key)}\n{ex.Message}");
            return;
        }
        ReportSourceError($"{Say("main.error.unusable")}: {DescribeFailure(ex)}");
    }

    /// <summary>
    /// Açılış ölçümü için yoklamayı elle başlatır. <c>OnWindowLoaded</c> yalnız gerçek
    /// pencere gösterildiğinde ateşlendiği için başsız ölçüm aynı yolu buradan çağırır.
    /// </summary>
    internal Task ProbeForMeasurement() => ProbeHardwareEncodersAsync();

    internal void LoadWithoutProbing(string path, MediaInfo info)
    {
        _sourceName = Path.GetFileName(path);
        ShowSourceName();
        Fade(SourceCard, true);
        ClearSourceError();
        Media.Publish(path, info);
        ApplyLoaded(path, info);
    }

    /// <summary>
    /// Baslik secici yalnizca birden cok baslik varsa gorunur; tek baslikli dosyada
    /// arayuz bugunku gibi kaliyor. Satirlar numarayla, sureyle ve varsa kaynagin kendi
    /// etiketiyle yaziliyor.
    /// </summary>
    private void BaslikSeciciyiKur(MediaInfo info)
    {
        _titleSyncing = true;
        try
        {
            CmbTitle.ItemsSource = info.Titles
                .Select(b => string.IsNullOrWhiteSpace(b.Label)
                    ? b.Number + "  " + BaslikSuresi(b)
                    : b.Number + "  " + BaslikSuresi(b) + "  " + b.Label)
                .ToArray();
            CmbTitle.SelectedIndex = info.Titles.Count > 0 ? 0 : -1;
            TitleRow.IsVisible = info.HasMultipleTitles;
        }
        finally
        {
            _titleSyncing = false;
        }
    }

    private static string BaslikSuresi(SourceTitle baslik)
        => Saat.Ekran(TimeSpan.FromSeconds(baslik.DurationSeconds), TimeSpan.FromHours(1));

    private MediaInfo SecilenBaslikleUygula(MediaInfo info)
    {
        var index = CmbTitle.SelectedIndex;
        if (index < 0 || index >= info.Titles.Count) return info;
        return SourceTitles.Uygula(info, info.Titles[index]);
    }

    private void OnTitleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_titleSyncing || _probed is null) return;
        _info = SecilenBaslikleUygula(_probed);
        RefreshBurnChoices();
        RefreshConvertBurnChoices();
        ShowInfo(_info);
        Recalculate();
        RefreshConversion();
    }

    private void ApplyLoaded(string path, MediaInfo info)
    {
        _probed = info;
        BaslikSeciciyiKur(info);
        info = SecilenBaslikleUygula(info);
        _info = info;
        ResetTracksForSource();

        Fade(DropZone, false);

        _aiPlan = null;
        _profile = null;
        _sceneMap = null;
        BtnRevert.IsVisible = false;
        TxtAiStatus.Text = "";
        SetAiDetails(false);
        ShowSourceName();
        ShowInfo(info);
        RefreshPreviewSource();

        _syncing = true;
        var suggested = info.FileSizeMb > WhatsAppTargetMb
            ? WhatsAppTargetMb
            : Math.Max(1, Math.Round(info.FileSizeMb / 2));
        SliderTarget.Maximum = Math.Max(SliderTarget.Maximum, Math.Ceiling(suggested));
        SliderTarget.Value = suggested;
        TxtTarget.Text = FormatBoxNumber(suggested);
        _syncing = false;
        RefreshChipDerivation();

        UpdateToolStatus();
        KirpmaYoklamasiniBaslat();
        Recalculate();
        RefreshQualityTargetAvailability();
        DeriveQualityFromTarget();
        KapSecenekleriniYokla();
        ResetConvertChapters(info);
        RefreshConversion();
    }

    public static Task<ComplexityProfile> ProbeWithMeasuredQualityAsync(
        MediaInfo info, SpeedMode speed, IQualityMeasurement? meter, CancellationToken ct)
        => ShrinkEngine.ProbeWithMeasuredQualityAsync(info, speed, meter, ct);

    public static SceneMap? CalibrationScenes(SceneMapAttempt? attempt) => ShrinkEngine.CalibrationScenes(attempt);

    public static SceneMap? QualityScenes(SceneMapAttempt? attempt) => ShrinkEngine.QualityScenes(attempt);

    public static IQualityMeasurement ProbeMeter(SceneMap? scenes) => ShrinkEngine.ProbeMeter(scenes);

    private async Task MeasureComplexityAsync(MediaInfo info)
    {
        _probeCts?.Cancel();
        _probeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _probeCts = cts;

        TxtEstimateNote.Text = Say("main.estimate.measuring");
        try
        {
            var speed = CurrentOptions().SpeedMode;
            _sceneMap = await EncodeRunner.TryBuildSceneMapAsync(info, ct: cts.Token);
            AcilisIzi.Yaz("sahne-haritasi");
            if (cts.IsCancellationRequested || !ReferenceEquals(_info, info)) return;
            Recalculate();

            await ShrinkEngine.CalibrateAsync(info, _sceneMap, speed, CurrentOptions, _planEncoders, (stage, profile) =>
            {
                if (cts.IsCancellationRequested || !ReferenceEquals(_info, info)) return false;
                _profile = profile;
                if (_preview is not null) { _preview.AraOlcum = stage == ShrinkMeasureStage.Probed; _preview.OlcumPlani = true; }
                try { Recalculate(); }
                finally { if (_preview is not null) { _preview.AraOlcum = false; _preview.OlcumPlani = false; } }
                AcilisIzi.Yaz("olcum-" + stage);
                if (stage == ShrinkMeasureStage.Probed) TxtEstimateNote.Text = Say("main.estimate.calibrating");
                return true;
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_info, info)) TxtEstimateNote.Text = $"{Say("main.estimate.failed")}: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_probeCts, cts)) _probeCts = null;
            cts.Dispose();
            if (ReferenceEquals(_info, info)) _preview?.ErtelenenPlaniUygula();
        }
    }

    private void ShowInfo(MediaInfo info)
    {
        Fade(InfoGrid, true);
        TxtDuration.Text = Saat.Ekran(TimeSpan.FromSeconds(info.DurationSeconds), TimeSpan.FromHours(1));
        TxtSize.Text = Say("main.unit.mb-value", Num(info.FileSizeMb, "0.0"));
        TxtResolution.Text = Bicim.Cozunurluk(info.Width, info.Height);
        TxtFps.Text = Bicim.Kare(info.Fps, Strings.Culture);
        TxtVideoCodec.Text = info.VideoCodec;
        TxtAudio.Text = info.HasAudio ? $"{info.AudioCodec} {Strings.BitHizi(Bicim.BitHizi.BpsToKbps(info.AudioBitrateBps))}" : Say("main.info.none");
        TxtBitrate.Text = Strings.BitHizi(Bicim.BitHizi.BpsToKbps(info.TotalBitrateBps));
        TxtHdr.Text = info.IsHdr ? Say("main.info.yes") : Say("main.info.no");
        Fade(HdrPolicyPanel, info.IsHdr);
        SecAudio.IsVisible = info.HasAudio;
        RefreshSectionSummaries();
    }

    /// <summary>
    /// Panelin süreceği dosyaları tazeler. Sağ taraf yalnız gerçekten üretilmiş bir dosya
    /// varken doludur; yoksa panel o tarafta sahte çıktı sunmaz (PLAN §6).
    /// </summary>
    private void RefreshPreviewSource()
    {
        if (_preview is null || _info is null) return;
        var aspect = _info.Height > 0 ? _info.Width / (double)_info.Height : 16.0 / 9.0;
        _preview.SetFiles(
            _info.FilePath,
            _lastOutput,
            aspect,
            TimeSpan.FromSeconds(_info.DurationSeconds),
            _info.Fps);
        if (!_preview.IsOpen) _preview.Open();
    }

    internal static CodecPreference CodecFromIndex(int index) => index switch
    {
        1 => CodecPreference.Compatible,
        2 => CodecPreference.MaxCompression,
        _ => CodecPreference.Auto
    };

    private void Recalculate()
    {
        if (_info is null) return;

        PlanResult detailed;
        try
        {
            detailed = PlanCalculator.BuildDetailed(_info, CurrentOptions(), _profile, _planEncoders);
        }
        catch (ArgumentException ex)
        {
            // T163/K4: gelismis ayarlardan gecen gecersiz bir deger (orn. kodegin kabul
            // etmedigi bir on ayar). Cikti hicbir zaman crash olmuyor; onceki gecerli plan
            // ekranda kalir ve baslatma kilitlenir.
            TxtAdvancedError.Text = Say("main.advanced.error", ex.Message);
            TxtAdvancedError.IsVisible = true;
            BtnStart.IsEnabled = false;
            return;
        }

        TxtAdvancedError.IsVisible = false;
        _autoPlan = detailed.Plan;
        PlanHardwareNotMeasured = detailed.HardwareNotMeasured;
        _predictedQuality = detailed.PredictedQuality;
        _advice = detailed.Advice;
        _profile ??= detailed.Profile;

        if (_aiPlan is not null)
        {
            var validation = PlanParser.Parse(TxtAiJson.Text ?? "", _info, CurrentOptions());
            if (validation.Ok) _aiPlan = validation.Plan;
            else
            {
                _aiPlan = null;
                BtnRevert.IsVisible = false;
                TxtAiStatus.Text = Say("main.ai.stale");
            }
        }

        RefreshPlanView();
        RefreshQualityPanels();
        BtnStart.IsEnabled = _cts is null && ToolLocator.IsAvailable(out _);
        ReportUnsettledProbe();

        // T48/K1: plan tazelendi. Panel gecikmesini kendi kurar; burası yalnız haber verir.
        if (_preview is not null) _preview.Scenes = _sceneMap?.Map;
        _preview?.SetPlan(_info, ActivePlan, _profile);
    }

    /// <summary>Ölçü için: arka planda koşan yoklama var mı.</summary>
    internal bool PlanProbePending => _planEncoders?.Pending ?? false;

    /// <summary>Ölçü için: geçidin bugüne kadar başlattığı yoklama sayısı.</summary>
    internal int PlanProbeCount => _planEncoders?.Probes ?? 0;

    /// <summary>Ölçü için: son hesabın donanım cevabını ölçülmemiş sayıp saymadığı.</summary>
    internal bool PlanHardwareNotMeasured { get; private set; }

    /// <summary>Ölçü için: geçitte denemesi bitmiş ama yerleşmemiş bir yoklama var mı.</summary>
    internal bool PlanProbeUnsettled => _planEncoders?.Unsettled ?? false;

    /// <summary>Ölçü için: geçitte istisnayla düşmüş bir yoklamanın metni.</summary>
    internal string? PlanProbeFailure => _planEncoders?.FirstFailure;

    /// <summary>Ölçü için: durum satırı raporlamasını tek başına koşturur.</summary>
    internal void ReportProbeStatusForMeasurement() => ReportUnsettledProbe();

    /// <summary>
    /// Yerleşmeyen yoklamayı kullanıcıya söyler. Yoklama bir cevap üretemediğinde bu bir
    /// sonuç değil bilinmeyendir; sessizce varsayılana düşmek — HDR kaynakta tonemap'e —
    /// aynı dosyanın iki koşumda iki farklı çıktı vermesi demekti ve kullanıcı nedenini
    /// hiçbir yerde göremiyordu.
    ///
    /// İki ayrı cümle, çünkü iki ayrı durum: yoklama koşup sonuca varamadıysa
    /// <c>main.status.probe-unsettled</c>, hiç koşamayıp istisna fırlattıysa
    /// <c>main.status.probe-failed</c> — istisnanın metniyle birlikte. İkisi de
    /// başarısızlık değil bilinmezlik bildiriyor; <c>main.error.probe</c> gerçek
    /// başarısızlık için açılışta duruyor (<see cref="ProbeHardwareEncodersAsync"/>).
    ///
    /// Temizlik yalnız <b>yoklamanın kendi yazdığı metni</b> siler. Önceki hâli "yoklama
    /// bir kez yazdı mı" tutan bir bayraktı ve "şu anki metin yoklamanın mı" tutmuyordu:
    /// durum satırını <see cref="UpdateToolStatus"/>, bağlantı, ayar ve araç hataları da
    /// yazıyor; yoklama yerleştiğinde o metinler de siliniyordu.
    /// </summary>
    private void ReportUnsettledProbe()
    {
        if (_planEncoders is null) return;

        string? text = null;
        if (_planEncoders.FirstFailure is { } failure) text = Say("main.status.probe-failed", failure);
        else if (_planEncoders.Unsettled) text = Say("main.status.probe-unsettled");

        if (text is not null)
        {
            if (TxtSystemStatus.Text != text) TxtSystemStatus.Text = text;
            _probeStatusText = text;
        }
        else if (_probeStatusText is not null)
        {
            if (TxtSystemStatus.Text == _probeStatusText) TxtSystemStatus.Text = string.Empty;
            _probeStatusText = null;
        }
    }

    /// <summary>
    /// Yongalar ve her birinin hedefi. <c>Half</c>'ın hedefi kaynağa bağlı olduğu için
    /// burada boş durur ve hesap anında kaynaktan türetilir.
    /// </summary>
    private IReadOnlyList<(Button Chip, double? TargetMb)> QualityChips() => new (Button, double?)[]
    {
        (ChipWhatsApp, 16), (Chip8, 8), (Chip25, 25), (Chip100, 100),
        (Chip134, 134), (Chip180, 180), (ChipHalf, null)
    };

    private double? ChipTargetMb(double? declared)
    {
        if (declared is { } fixedMb) return fixedMb;
        return _info is null ? null : Math.Max(1, Math.Round(_info.FileSizeMb / 2, 1));
    }

    /// <summary>
    /// Her yonganın balonuna tahmini kalite paneli koyar.
    ///
    /// Hesap <see cref="PlanCalculator.BuildDetailed"/>'dır. Karmaşıklık tarafı saf aritmetik:
    /// ölçülmüş profil varsa o kullanılır, yoksa <c>BuildDetailed</c> kendi içinde
    /// <see cref="ComplexityProfile.FromSourceBitrate"/>'a düşer.
    ///
    /// Kodlayıcı tarafı saf değildi ve T130'a kadar bu cümle yanlıştı: <c>BuildDetailed</c>
    /// donanım adaylarını ve HDR piksel biçimini yetenek nesnesine soruyor, o da ffmpeg
    /// süreci doğuruyordu. Artık arayüz yolu gerçek yetenek nesnesini değil
    /// <see cref="DeferredEncoderAvailability"/> geçidini görüyor; geçit yalnız ısıtılmış
    /// cevabı okur, süreç doğurmaz. Bu yüzden panel fare üstüne gelince beklemeden çıkar.
    /// Profilin ölçülmüş olup olmadığını panel ayrıca yazar; tahmin ölçüm gibi sunulmaz.
    /// </summary>
    private void RefreshQualityPanels()
    {
        foreach (var (chip, declared) in QualityChips())
        {
            if (ToolTip.GetTip(chip) is not StackPanel panel || panel.Children.Count == 0) continue;

            while (panel.Children.Count > 1) panel.Children.RemoveAt(panel.Children.Count - 1);
            panel.Children.Add(new Border { Theme = Look("PanelRule") });
            panel.Children.Add(QualityBody(ChipTargetMb(declared)));
        }
    }

    private Control QualityBody(double? targetMb)
    {
        var hint = QualityHint.For(_info, CurrentOptions(), targetMb, _profile, _planEncoders);

        // Video yüklü değilken skor hesaplanamaz. Sıfır ya da uydurma bir sayı yerine alan
        // boş kalır ve yonganın ne işe yaradığı yukarıdaki maddelerde durmaya devam eder.
        if (hint.Score is not { } score || hint.TargetMb is not { } target)
            return new SarilanMetin
            {
                Text = Say("main.quality.tip.empty"),
                Theme = Look("Hint"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Scalar("TooltipMaxWidth", 460)
            };

        // Ölçülen ile tahmin edilen ayrı kelimelerle söylenir; tahmini ölçüm gibi sunmak
        // kullanıcının güvenerek yanlış hedef seçmesine yol açar.
        var basis = hint.Basis switch
        {
            QualityBasis.Measured => Say("main.quality.basis.measured"),
            QualityBasis.Estimated => Say("main.quality.basis.estimated"),
            _ => Say("main.quality.basis.under-target")
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = Scalar("SpaceMd", 12),
            RowSpacing = Scalar("SpaceSm", 8),
            MaxWidth = Scalar("TooltipMaxWidth", 460)
        };

        AddQualityRow(grid, Say("main.quality.target"), Say("main.unit.mb-value", Num(target, "0.##")));
        AddQualityRow(grid, Say("main.quality.predicted"), Say("main.unit.score-value", Num(score, "0.#")));
        AddQualityRow(grid, Say("main.quality.loss"), Say("main.quality.loss-points", Num(hint.LossPoints, "0.#")));
        AddQualityRow(grid, Say("main.quality.basis"), basis);
        return grid;
    }

    private void AddQualityRow(Grid grid, string label, string value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var key = new SarilanMetin { Text = LanguageCatalog.Display(label), Theme = Look("PlanFactLabel") };
        Grid.SetRow(key, row);
        Grid.SetColumn(key, 0);

        var read = new SarilanMetin { Text = LanguageCatalog.Display(value), Theme = Look("PlanFactValue") };
        Grid.SetRow(read, row);
        Grid.SetColumn(read, 1);

        grid.Children.Add(key);
        grid.Children.Add(read);
    }

    private void ResetPlanView()
    {
        PlanFacts.Children.Clear();
        PlanReasons.Children.Clear();
        PlanRule.IsVisible = false;
        TxtPlanEmpty.IsVisible = true;
        TxtCommand.Text = "";
    }

    private void AddPlanFact(string label, string value)
    {
        var key = new SarilanMetin { Text = LanguageCatalog.Display(label), Theme = Look("PlanFactLabel") };
        var read = new SarilanMetin { Text = value, Theme = Look("PlanFactValue") };

        PlanFacts.Children.Add(key);
        PlanFacts.Children.Add(read);
    }

    private void AddPlanReason(string text)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new SarilanMetin { Text = "•", Theme = Look("PlanBullet") });

        var body = new SarilanMetin { Text = LanguageCatalog.Display(text), Theme = Look("PlanReasonText") };
        Grid.SetColumn(body, 1);
        row.Children.Add(body);

        PlanReasons.Children.Add(row);
    }

    private void RefreshPlanView()
    {
        if (ActivePlan is not { } plan || _info is null) return;
        _estimate = PlanCalculator.Estimate(plan, _info, _profile);

        PlanFacts.Children.Clear();
        PlanReasons.Children.Clear();
        TxtPlanEmpty.IsVisible = false;

        var channels = plan.AudioChannels == 1 ? $" {Say("main.plan.audio.mono")}" : "";
        AddPlanFact(Say("main.plan.fact.plan"), _aiPlan is null ? Say("main.plan.automatic") : Say("main.plan.ai"));
        AddPlanFact(Say("main.plan.fact.encoder"), plan.Codec);
        AddPlanFact(Say("main.plan.fact.mode"), plan.ModeEnum switch
        {
            EncodeMode.Crf => Say("main.plan.mode.crf-value", plan.Crf),
            EncodeMode.PassThrough => Say("main.plan.mode.copy"),
            _ => $"{Strings.BitHizi(plan.VideoBitrateK)} · {Say(RunsSinglePass(plan) ? "main.plan.mode.single-pass" : "main.plan.mode.two-pass")}"
        });
        AddPlanFact(Say("main.plan.fact.resolution"), Bicim.Cozunurluk(plan.Width, plan.Height));
        AddPlanFact(Say("main.plan.fact.frame-rate"), Say("main.unit.fps-value", Bicim.Kare(plan.Fps, Strings.Culture)));
        AddPlanFact(Say("main.plan.fact.audio"), plan.AudioCodec is null ? Say("main.info.none") : $"{plan.AudioCodec} {Strings.BitHizi(plan.AudioBitrateK)}{channels}");
        AddPlanFact(Say("main.plan.fact.preset"), plan.Preset);
        AddPlanFact(Say("main.plan.fact.estimated-size"), _estimate is { } size ? Say("main.unit.mb-value", Num(size.ExpectedMb, "0.0")) : "-");

        foreach (var line in StrategyLines()) AddPlanReason(line);
        foreach (var line in ReasonLines(plan)) AddPlanReason(line);

        var reasons = PlanReasons.Children.Count;
        PlanRule.IsVisible = reasons > 0;
        PlanReasonsHead.IsVisible = reasons > 0;
        TxtPlanReasonsHead.Text = $"{Say("main.plan.reasons")} · {reasons}";
        SetPlanReasonsExpanded(_reasonsExpanded);

        RefreshEstimateView();
        RefreshDurationView();
        RefreshAdvancedHints();
        TxtCommand.Text = FfmpegArguments.ToCommandLine(DisplayedEncodeArguments(_info, plan,
            BuildUniqueOutputPath(_info.FilePath, "shrunk", ShrinkExtension(plan), plan), _encoders, _sceneMap?.Map));
    }

    /// <summary>
    /// Ekranda gosterilen komut. <paramref name="scenes"/> kodlamaya giden haritanin ta kendisidir;
    /// ayri gecilmezse gosterilen komut kosan komuttan anahtar kare araliginda ayrisirdi.
    /// </summary>
    public static IReadOnlyList<string> DisplayedEncodeArguments(MediaInfo info, EncodePlan plan,
        string outputPath, IEncoderAvailability? availability, SceneMap? scenes = null)
        => ShrinkEngine.DisplayedArguments(info, plan, outputPath, availability, scenes);

    private void RefreshEstimateView()
    {
        if (_estimate is not { } estimate || _info is null)
        {
            TxtEstimateValue.Text = "-";
            TxtEstimateRange.Text = "";
            TxtEstimateNote.Text = "";
            return;
        }

        var reading = Say("main.unit.mb-value", Num(estimate.ExpectedMb, "0.0"));
        if (TxtEstimateValue.Text != reading) Pulse(TxtEstimateValue, true);
        TxtEstimateValue.Text = reading;
        TxtEstimateRange.Text =
            $"{Say("main.unit.mb-range", Num(estimate.LowMb, "0.0"), Num(estimate.HighMb, "0.0"))} · {Say("main.estimate.of-source")} "
            + Percent(estimate.ExpectedMb / Math.Max(_info.FileSizeMb, 0.01));

        var basis = estimate.Measured
            ? Say("main.estimate.basis.measured")
            : Say("main.estimate.basis.estimated");
        var mode = Say(EstimateModeKey(ActivePlan, estimate));
        TxtEstimateNote.Text = $"{basis} · {mode} · {Say("main.estimate.predicted-quality")} {Say("main.unit.score-value", Num(_predictedQuality, "0.#"))}";
    }

    private void RefreshDurationView()
    {
        if (ActivePlan is not { } plan || _info is null)
        {
            TxtDurationValue.Text = "-";
            TxtDurationRange.Text = "";
            return;
        }

        var profile = _profile ?? ComplexityProfile.FromSourceBitrate(_info);
        if (profile.EstimateTime(plan, _info.DurationSeconds) is not { } duration)
        {
            TxtDurationValue.Text = "-";
            TxtDurationRange.Text = profile.Speed is null
                ? Say("main.duration.not-measured")
                : Say("main.duration.other-settings");
            return;
        }

        if (duration.StreamCopy)
        {
            TxtDurationValue.Text = Say("main.duration.copied");
            TxtDurationRange.Text = Say("main.duration.copied-note");
            return;
        }

        var reading = $"~{HumanDuration(duration.ExpectedSeconds)}";
        if (TxtDurationValue.Text != reading) Pulse(TxtDurationValue, true);
        TxtDurationValue.Text = reading;

        var rate = profile.Speed!.FramesPerSecond;
        TxtDurationRange.Text = $"{HumanDuration(duration.LowSeconds)} - {HumanDuration(duration.HighSeconds)} · {Say("main.duration.measured")} {Num(rate, "0")} {Say("main.duration.frames-per-second")}";
    }

    private string HumanDuration(double seconds)
    {
        if (seconds < 60) return $"{Num(Math.Max(5, Math.Round(seconds / 5.0) * 5.0), "0")} {Say("main.duration.seconds")}";

        if (seconds < 3600)
        {
            var minutes = seconds / 60.0;
            if (minutes < 10) return $"{Num(Math.Max(1.0, Math.Round(minutes * 2.0) / 2.0), "0.#")} {Say("main.duration.minutes")}";
            return $"{Num(Math.Round(minutes), "0")} {Say("main.duration.minutes")}";
        }

        var hours = (int)(seconds / 3600);
        var rest = (int)Math.Round((seconds - hours * 3600.0) / 60.0);
        if (rest >= 60)
        {
            hours++;
            rest = 0;
        }
        return rest == 0 ? $"{hours} {Say("main.duration.hours")}" : $"{hours} {Say("main.duration.hours")} {rest} {Say("main.duration.minutes")}";
    }

    /// <summary>
    /// Ayarlardaki sabit klasör gerçekten kullanılabiliyorsa onu, değilse boş döner.
    /// Var olmak yetmez: klasöre yazılamıyorsa çıktı oraya gitmez, o yüzden sıfır
    /// baytlık bir sonda yazılıp silinir. Boş dönen her durumda çağıran kullanıcıya
    /// söyler — sessizce kaynağın yanına yazmak ayarı yalan yapar.
    /// </summary>
    internal static string? UsableFixedFolder(int mode, string? folder)
    {
        if (mode != 1 || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        var probe = Path.Combine(folder, ".vidshrink-yazma-sondasi-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(probe); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Sabit klasör seçili ama kullanılamıyor: çıktı kaynağın yanına düşecek.</summary>
    private bool FixedFolderUnusable =>
        OutputFolderModeIndex == 1 && UsableFixedFolder(OutputFolderModeIndex, TxtOutputFolder.Text) is null;

    /// <summary>
    /// Küçültme çıktısının uzantısı: uygulanan kullanıcı ön ayarı bir kap taşıyorsa o, değilse
    /// planın kabı. Gömülü yonga ya da boyut tavanına dönüş ön ayarın kabını bırakır.
    /// </summary>
    internal string ShrinkExtension(EncodePlan plan) =>
        PresetLibrary.DeliveredExtension(_presetContainer) ?? plan.Streams?.Extension ?? "mp4";

    private string BuildUniqueOutputPath(string inputPath, string suffix, string extension, EncodePlan? plan = null)
        => ShrinkEngine.UniqueOutputPath(inputPath, suffix, extension,
            UsableFixedFolder(OutputFolderModeIndex, TxtOutputFolder.Text),
            suffix == "shrunk" ? AdlandirmaDeseni.Uygula(TxtOutputName.Text, AdBilgisiFor(inputPath, plan)) : null);

    private void OnTargetSliderChanged()
    {
        if (_syncing) return;
        _syncing = true;
        TxtTarget.Text = FormatBoxNumber(Math.Round(SliderTarget.Value, 1));
        _syncing = false;
        if (!_targetIsDerived) _savedTargetMb = ParseTargetMb();
        RestoreSizeCap();
        if (!_targetIsDerived) { _kaliteSurer = false; DeriveQualityFromTarget(); }
        ScheduleRecalculate();
    }

    private void OnTargetTextChanged()
    {
        if (_syncing) return;
        _syncing = true;
        var mb = ParseTargetMb();
        if (mb > SliderTarget.Maximum) SliderTarget.Maximum = Math.Ceiling(mb);
        SliderTarget.Value = mb;
        _syncing = false;
        if (!_targetIsDerived) _savedTargetMb = mb;
        RestoreSizeCap();
        if (!_targetIsDerived) { _kaliteSurer = false; DeriveQualityFromTarget(); }
        ScheduleRecalculate();
    }

    /// <summary>
    /// Hedefe elle dokunmak boyut tavanini geri getirir: kutuya yazilan sayi motora
    /// ulasmiyorsa turetme satiri da yalan soyluyor demektir.
    ///
    /// <para>Turetme satiri <c>TxtTarget.Text</c>'i okuyor, dolayisiyla tavan zaten
    /// aciksa da yenilenmesi gerekiyor. Eski surumde <c>_chipSizeCapped</c> true iken
    /// bastan donuluyordu; kutuda 24 yazarken satir "Hedef 16 MB" diye kaliyordu.</para>
    /// </summary>
    private void RestoreSizeCap()
    {
        _chipSizeCapped = true;
        _platformChip = false;
        _presetContainer = null;
        RefreshChipDerivation();
        RefreshSectionSummaries();
    }

    private void OnQualityTargetSliderChanged()
    {
        if (_syncing || _qualityIsDerived) return;
        _qualityIsDerived = true;
        TxtQualityTarget.Text = FormatBoxNumber(Math.Round(SliderQualityTarget.Value));
        _qualityIsDerived = false;
        _savedQualityTarget = ParseQualityTarget();
        DeriveTargetFromQuality();
    }

    private void OnQualityTargetTextChanged()
    {
        if (_syncing || _qualityIsDerived) return;

        // T61/K2: kutuya yazılan sayı geri yazılmaz. Kaydırıcı yazılan değere gider ama
        // kutu olduğu gibi kalır; gidiş dönüş tam olarak aynı sayıya dönmediği için
        // kullanıcının 60'ı 59,2 diye düzeltilirdi.
        _qualityIsDerived = true;
        SliderQualityTarget.Value = ParseQualityTarget();
        _qualityIsDerived = false;
        _savedQualityTarget = ParseQualityTarget();
        DeriveTargetFromQuality();
    }

    /// <summary>Kutudaki kalite skoru. Okunamayan metin kaydırıcının o anki değeridir.</summary>
    /// <summary>
    /// Hedef ve kalite kutusuna yazılan sayı arayüz dilinin ondalık ayırıcısıyla yazılır
    /// (Türkçede 12,5). Okurken önce o dil, sonra nokta denenir: dil değişmeden önce yazılmış
    /// metin de, klavyeden nokta basan kullanıcı da sessizce varsayılana düşmez.
    /// </summary>
    internal static string FormatBoxNumber(double value) => value.ToString("0.##", Strings.Culture);

    internal static bool TryReadBoxNumber(string? text, out double value)
        => double.TryParse(text, NumberStyles.Float, Strings.Culture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private double ParseQualityTarget()
    {
        var value = TryReadBoxNumber(TxtQualityTarget.Text, out var score)
            ? score
            : SliderQualityTarget.Value;
        return Math.Clamp(value, SliderQualityTarget.Minimum, SliderQualityTarget.Maximum);
    }

    /// <summary>
    /// Kaliteden hedef MB türetir. Kullanıcının ellediği taraf kalite olduğu için kalite
    /// kutusuna dokunulmaz; yalnız hedef yazılır ve sınır durumu ekrana basılır.
    /// </summary>
    private void DeriveTargetFromQuality()
    {
        if (_info is null) { RefreshQualityTargetAvailability(); return; }

        QualityDerivedTargets++;
        _kaliteSurer = true;
        var result = PlanCalculator.TargetMbForQuality(_info, CurrentOptions(), ParseQualityTarget(), _profile, _planEncoders);
        var mb = Math.Round(result.TargetMb, 1);

        _targetIsDerived = true;
        if (mb > SliderTarget.Maximum) SliderTarget.Maximum = Math.Ceiling(mb);
        SliderTarget.Value = mb;
        TxtTarget.Text = FormatBoxNumber(mb);
        _targetIsDerived = false;

        ShowQualityTargetBound(result, mb);
        ScheduleRecalculate();
    }

    /// <summary>
    /// Hedef MB'dan kalite türetir. Bu yön kullanıcının MB'ı ellediği yön olduğu için MB
    /// kutusuna dokunulmaz.
    /// </summary>
    private void DeriveQualityFromTarget()
    {
        if (_info is null) { RefreshQualityTargetAvailability(); return; }

        var hint = QualityHint.For(_info, CurrentOptions(), ParseTargetMb(), _profile, _planEncoders);
        if (hint.Score is not { } score) return;

        TargetDerivedQualities++;
        _qualityIsDerived = true;
        SliderQualityTarget.Value = Math.Clamp(score, SliderQualityTarget.Minimum, SliderQualityTarget.Maximum);
        TxtQualityTarget.Text = FormatBoxNumber(Math.Round(score, 1));
        _qualityIsDerived = false;

        SetQualityTargetNotice("");
    }

    /// <summary>
    /// T61/K3: iki sınır sessizce kırpılmaz. Kullanıcı kaydırıcıyı sürüklerken ne olduğunu
    /// ve nedenini denetimin hemen altında okur; balonda saklanmaz.
    /// </summary>
    private void ShowQualityTargetBound(QualityTargetResult result, double mb)
    {
        var target = Num(mb, "0.##");
        var reached = Num(result.PredictedQuality, "0.#");

        SetQualityTargetNotice(result.Bound switch
        {
            QualityTargetBound.BelowFloor => Say("main.quality.below-floor", target, reached),
            QualityTargetBound.AboveSourceCeiling => Say("main.quality.above-ceiling", target, reached),
            _ => ""
        });
    }

    private void SetQualityTargetNotice(string text)
    {
        TxtQualityTargetNotice.Text = text;
        TxtQualityTargetNotice.IsVisible = text.Length > 0;
    }

    /// <summary>
    /// T61/K4: kaynak yokken kaliteden MB türetilemez. Denetim kapanır ve neden kapalı
    /// olduğu tek satırla yazılır; sayı uydurulmaz.
    /// </summary>
    private void RefreshQualityTargetAvailability()
    {
        var ready = _info is not null;
        SliderQualityTarget.IsEnabled = TxtQualityTarget.IsEnabled = ready;
        if (ready) SetQualityTargetNotice("");
        else SetQualityTargetNotice(Say("main.quality.no-source"));
    }

    /// <summary>Ölçüm için tur sayacı: her yönde kaç türetme yapıldı.</summary>
    internal int QualityDerivedTargets { get; private set; }

    internal int TargetDerivedQualities { get; private set; }

    private void OnPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Name: { } name }) ApplyChipPlan(name);
    }

    private void OnPresetHalf(object? sender, RoutedEventArgs e) => ApplyChipPlan("ChipHalf");

    private void OnPresetArchive(object? sender, RoutedEventArgs e) => ApplyChipPlan("ChipArchive");

    private void OnOptionChanged()
    {
        RefreshChipDerivation();
        RefreshSectionSummaries();
        if (_syncing) return;
        ScheduleRecalculate();
    }

    private bool _reasonsExpanded;

    private void OnTogglePlanReasons(object? sender, RoutedEventArgs e) => SetPlanReasonsExpanded(!_reasonsExpanded);

    /// <summary>
    /// Gerekçelerin açık olduğu hâli ölçüme açar. Plan panelinin tavanı yalnız bu hâlde
    /// bağlayıcı oluyor ve düğmeye basmadan o hâl kurulamıyor.
    /// </summary>
    internal void ExpandPlanReasons() => SetPlanReasonsExpanded(true);

    /// <summary>
    /// K6: gerekçeler katlanır. Kapalıyken plan paneli olguların net özetidir ve kaymaz;
    /// açıkken listenin tamamı görünür ve taşma <c>PlanScroll</c>'a düşer. Metin hiçbir
    /// durumda kısaltılmıyor.
    /// </summary>
    private void SetPlanReasonsExpanded(bool expanded)
    {
        _reasonsExpanded = expanded;
        PlanReasons.IsVisible = expanded && PlanReasons.Children.Count > 0;
        Chevron(GlyphPlanReasons, expanded);
    }

    private void OnTogglePerformance(object? sender, RoutedEventArgs e) => SetPerformanceDetails(!PerformanceDetails.IsVisible);

    private void SetPerformanceDetails(bool visible)
    {
        PerformanceDetails.IsVisible = visible;
        Chevron(GlyphPerformance, visible);
    }

    /// <summary>
    /// Açık hâli ölçüme açar. Panelin yerleşimi yalnız bu hâlde bağlayıcı ve düğmeye
    /// basmadan o hâl kurulamıyor.
    /// </summary>
    internal void ExpandPerformanceCheck() => SetPerformanceDetails(true);

    /// <summary>
    /// Ölçümü başlatan çağrı. Alan olarak duruyor ki ölçüm sondayı sahtesiyle
    /// değiştirebilsin; gerçek sonda yalnız buradan çağrılır ve kendiliğinden hiç
    /// koşmaz — ne açılışta, ne sekme açılırken, ne dosya yüklenince.
    /// </summary>
    internal Func<CancellationToken, Task<PerformanceCheckResult>> PerformanceProbeRunner { get; set; }
        = token => PerformanceProbe.RunAsync(ct: token);

    private bool _performanceRunning;

    /// <summary>
    /// Ekranda duran son ölçüm sonucu. Dil değişince panel bundan yeniden kuruluyor;
    /// aksi hâlde açılışta yazılan satırlar ilk dilde donup kalıyordu.
    /// </summary>
    private PerformanceCheckResult _performanceShown = PerformanceCheckResult.NotMeasured;

    private async void OnRunPerformanceCheck(object? sender, RoutedEventArgs e) => await RunPerformanceCheckAsync();

    internal async Task RunPerformanceCheckAsync()
    {
        if (_performanceRunning) return;

        _performanceRunning = true;
        BtnPerformanceRun.IsEnabled = false;
        TxtPerformanceStatus.Text = Say("performance.running");

        try
        {
            ShowPerformanceResult(await PerformanceProbeRunner(CancellationToken.None));
        }
        catch (Exception ex)
        {
            TxtPerformanceStatus.Text = $"{Say("performance.failed")}: {ex.Message}";
        }
        finally
        {
            _performanceRunning = false;
            BtnPerformanceRun.IsEnabled = true;
        }
    }

    /// <summary>
    /// Sonucu ekrana yazar. Manşet <see cref="PerformanceCheckResult.Impact"/> alanından
    /// değil bulgulardan kurulur; gerekçesi <see cref="PerformanceReportText"/> içinde.
    /// </summary>
    internal void ShowPerformanceResult(PerformanceCheckResult result)
    {
        _performanceShown = result;
        TxtPerformanceStatus.Text = string.Empty;

        PerformanceFacts.Children.Clear();
        foreach (var fact in PerformanceReportText.Facts(result))
        {
            var row = new SarilanMetin
            {
                Theme = Look("MonoValue"),
                FontWeight = FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap
            };

            // Koşulardan kurulu bir TextBlock büyük harf geçidine uğramıyor. Kodlayıcı adı
            // ve birim ölçüldüğü yazımla kalsın diye satır burada kuruluyor: "libx264"
            // sözcük kuralından geçseydi "Libx264", "ms" ise "Ms" olurdu.
            if (row.Inlines is { } inlines)
            {
                inlines.Add(new Run(LanguageCatalog.Display(fact.Label) + ": "));
                inlines.Add(new Run(fact.Value));
            }
            else row.Text = $"{fact.Label}: {fact.Value}";

            PerformanceFacts.Children.Add(row);
        }

        PerformanceLines.Children.Clear();
        foreach (var line in PerformanceReportText.Describe(result))
            PerformanceLines.Children.Add(new SarilanMetin
            {
                Text = LanguageCatalog.Display(line),
                Theme = Look("Hint"),
                TextWrapping = TextWrapping.Wrap
            });
    }

    private void OnToggleAiDetails(object? sender, RoutedEventArgs e) => SetAiDetails(!AiDetails.IsVisible);

    private void SetAiDetails(bool visible)
    {
        AiDetails.IsVisible = visible;
        TxtAiHint.IsVisible = !visible;
        Chevron(GlyphAiDetails, visible);
    }

    private async void OnCopyPrompt(object? sender, RoutedEventArgs e)
    {
        SetAiDetails(true);
        if (_info is null || _autoPlan is null)
        {
            TxtAiStatus.Text = Say("main.ai.load-first");
            return;
        }

        try
        {
            if (Clipboard is null) throw new InvalidOperationException(Say("main.ai.no-clipboard"));
            await Clipboard.SetTextAsync(PromptBuilder.Build(_info, CurrentOptions(), _autoPlan));
            TxtAiStatus.Text = Say("main.ai.prompt-copied");
        }
        catch (Exception ex)
        {
            TxtAiStatus.Text = $"{Say("main.ai.clipboard-failed")}: {ex.Message}";
        }
    }

    private void OnApplyJson(object? sender, RoutedEventArgs e)
    {
        if (_info is null || _autoPlan is null)
        {
            TxtAiStatus.Text = Say("main.ai.load-first");
            return;
        }

        var result = PlanParser.Parse(TxtAiJson.Text ?? "", _info, CurrentOptions());
        if (!result.Ok)
        {
            _aiPlan = null;
            TxtAiStatus.Text = Say("main.ai.rejected") + string.Join("\n• ", result.Errors);
            RefreshPlanView();
            return;
        }

        _aiPlan = result.Plan;
        BtnRevert.IsVisible = true;
        var differences = _autoPlan.DescribeDifferences(_aiPlan!).ToList();
        TxtAiStatus.Text = differences.Count == 0
            ? Say("main.ai.same-decision")
            : Say("main.ai.changes") + string.Join("\n• ", differences);
        if (result.Warnings.Count > 0)
            TxtAiStatus.Text += Say("main.ai.warnings") + string.Join("\n• ", result.Warnings);
        RefreshPlanView();
    }

    private void OnRevertAuto(object? sender, RoutedEventArgs e)
    {
        _aiPlan = null;
        BtnRevert.IsVisible = false;
        TxtAiStatus.Text = Say("main.ai.reverted");
        RefreshPlanView();
    }

    private bool WhatsAppTargeted(double targetMb) =>
        ChkWhatsAppCompatible.IsChecked == true || Math.Abs(targetMb - WhatsAppTargetMb) < 0.005;

    private string WhatsAppDocumentHint(double targetMb) =>
        WhatsAppTargeted(targetMb) ? " " + Say("main.run.whatsapp-document") : "";

    internal string WhatsAppDocumentHintForTest(double targetMb) => WhatsAppDocumentHint(targetMb);

    private async void OnStart(object? sender, RoutedEventArgs e)
    {
        if (_info is null || ActivePlan is null || _cts is not null) return;
        await KirpmaHazirAsync();
        if (_info is null || ActivePlan is null || _cts is not null) return;

        var output = BuildUniqueOutputPath(_info.FilePath, "shrunk", ShrinkExtension(ActivePlan), ActivePlan);
        QueueWatch.MarkOwn(output);
        if (FixedFolderUnusable)
            TxtResult.Text = Say("settings-tab.output-folder.unusable", TxtOutputFolder.Text ?? "");
        var targetMb = ParseTargetMb();
        if (DiskSpaceGuard.TryGetFreeBytes(output, out var freeBytes) && !DiskSpaceGuard.HasEnoughSpace(freeBytes, targetMb))
        {
            var neededMb = Megabayt.Oku(DiskSpaceGuard.RequiredBytes(targetMb));
            TxtResult.Text = Say("main.run.no-space", Num(neededMb, "0"));
            return;
        }

        _probeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var biten = new BitenIs(_info.FilePath, IsSonucu.Hatali);
        try
        {
            SetRunning(true, true);
            TxtResult.Text = "";
            ShowResultWarning(false);
            BtnReveal.IsVisible = false;
            ResetShare(false);
            HideRetryAsk();
            var progress = new Progress<EncodeProgress>(ShowEncodeProgress);

            var (searchedMb, plan) = await BaslangicPlaniAsync(targetMb, ActivePlan!, cts.Token);
            targetMb = searchedMb;
            var source = _info.FilePath;
            var result = await ShrinkEngine.EncodeAsync(_info, plan, output, targetMb, progress, cts.Token, CurrentOptions().FillPolicy, _profile, AskBeforeRetryAsync, _sceneMap?.Map, RunPause, ChkKeepDates.IsChecked == true);
            _lastOutput = result.OutputPath;
            RefreshPreviewSource();

            if (result.Success)
            {
                TxtOutSize.Text = Say("main.unit.mb-value", Num(result.OutputMb, "0.0"));
                var saved = 100 - result.OutputMb / _info.FileSizeMb * 100;
                TxtResult.Text = Say("main.run.done",
                    result.DeliveredAttemptNumber, Num(_info.FileSizeMb, "0.0"), Num(result.OutputMb, "0.0"), Num(saved, "0.#"));
                if (result.OverTarget)
                    TxtResult.Text += " " + AcceptedLargerText(result.OutputMb, targetMb, plan);
                if (result.Trim is { } trim)
                    TxtResult.Text += " " + Say("main.run.trimmed", Num(trim.RemovedSeconds, "0.#"), Clock(trim.DurationSeconds), Clock(trim.KeptSeconds));
                TxtResult.Text += SaturatedSuffix(result, targetMb);
                TxtResult.Text += WhatsAppDocumentHint(targetMb);
                TxtResult.Text += AramaOzeti();
                if (Hdr10PlusMissed(result) is { } hdr10Plus)
                {
                    TxtResult.Text += " " + Say("main.run.hdr10plus-short", hdr10Plus.OutputFrames, hdr10Plus.SourceFrames);
                    ShowResultWarning(true);
                }
            }
            else if (result.CeilingExceeded)
            {
                TxtOutSize.Text = "-";
                TxtResult.Text = Say("main.run.over-ceiling",
                    Num(EncodeRunner.EffectiveTargetMb(targetMb, plan), "0.##"), result.Attempts, Num(result.OutputMb, "0.0"));
            }
            else if (result.Kusur is { } kusur)
            {
                TxtOutSize.Text = "-";
                TxtResult.Text = kusur.Cozulemedi
                    ? Say("main.run.output-unreadable")
                    : Say("main.run.output-short", Num(kusur.OkunanSaniye!.Value, "0.#"), Num(kusur.BeklenenSaniye, "0.#"));
            }
            else
            {
                TxtOutSize.Text = "-";
                TxtResult.Text = Say("main.run.ended");
            }

            if (result.Success && ChkAdvMeasureQuality.IsChecked == true
                && await KaliteSatiriAsync(source, result.OutputPath, cts.Token) is { Length: > 0 } kalite)
                TxtResult.Text += " " + kalite;

            BtnReveal.IsVisible = result.Success;
            ResetShare(result.Success);
            if (result.Success) biten = biten with { Sonuc = IsSonucu.Basarili };
        }
        catch (OperationCanceledException)
        {
            biten = biten with { Sonuc = IsSonucu.Iptal };
            TxtResult.Text = Say("main.run.cancelled");
        }
        catch (Exception ex)
        {
            TxtResult.Text = DescribeFailure(ex);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            HideRetryAsk();
            SetRunning(false);
            RefreshConversion();
            FlushPendingMacFile();
            IsBittiHaberi(biten);
        }
    }

    // The engine stops after an attempt that lands over the target and hands the decision here.
    // Nothing blocks: the panel is shown, the awaited task completes when a button is pressed.
    private async Task<OvershootChoice> AskBeforeRetryAsync(RetryPrompt prompt, CancellationToken ct)
    {
        var decision = new TaskCompletionSource<OvershootChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _retryDecision = decision;

        await Dispatcher.UIThread.InvokeAsync(() => ShowRetryAsk(prompt));
        using var cancellation = ct.Register(() => decision.TrySetCanceled(ct));

        try
        {
            return await decision.Task;
        }
        finally
        {
            _retryDecision = null;
            await Dispatcher.UIThread.InvokeAsync(HideRetryAsk);
        }
    }

    private void ShowRetryAsk(RetryPrompt prompt)
    {
        _activeRetryPrompt = prompt;
        TxtOutSize.Text = Say("main.unit.mb-value", Num(prompt.ActualMb, "0.0"));

        TxtRetryOutcome.Text = Bicim.Satir.Bagla(Say("main.retry.outcome",
            prompt.Attempt,
            prompt.MaxAttempts,
            Num(prompt.ActualMb, "0.0"),
            Num(prompt.TargetMb, "0.##"),
            Num(prompt.OverMb, "0.0"),
            Bicim.Yuzde.Hazir(prompt.OverPercent, Strings.Culture),
            Saat.Ekran(prompt.AttemptDuration)));

        TxtRetryMeaning.Text = Bicim.Satir.Bagla(prompt.HasUnderBandFallback
            ? Say("main.retry.meaning-with-fallback", Num(prompt.FallbackMb, "0.0"))
            : Say("main.retry.meaning-without-fallback"));
        BtnRetryAgain.IsVisible = prompt.CanRetry;
        var kabulYazisi = Bicim.Satir.Bagla(Say("main.retry.accept", Num(prompt.ActualMb, "0.0")));
        BtnRetryAccept.Content = kabulYazisi;
        AutomationProperties.SetName(BtnRetryAccept, kabulYazisi);
        var canTrim = prompt.Trims is { Count: > 0 };
        BtnRetryTrim.IsVisible = canTrim;
        if (canTrim)
        {
            var kirpmaYazisi = Bicim.Satir.Bagla(Say("main.retry.trim", Num(prompt.Trims!.Min(plan => plan.RemovedSeconds), "0.#")));
            BtnRetryTrim.Content = kirpmaYazisi;
            AutomationProperties.SetName(BtnRetryTrim, kirpmaYazisi);
        }
        else
            AutomationProperties.SetName(BtnRetryTrim, Say("main.retry.trim.name"));
        RbTrimEnd.IsEnabled = prompt.TrimFor(TrimSide.End) is not null;
        RbTrimStart.IsEnabled = prompt.TrimFor(TrimSide.Start) is not null;
        RbTrimBoth.IsEnabled = prompt.TrimFor(TrimSide.Both) is not null;
        if (!RetryTrimPanel.IsVisible)
        {
            var first = new[] { RbTrimEnd, RbTrimStart, RbTrimBoth }.FirstOrDefault(radio => radio.IsEnabled);
            if (first is not null) first.IsChecked = true;
        }
        RetryTrimPanel.IsVisible = canTrim && RetryTrimPanel.IsVisible;
        RefreshTrimRange();
        Fade(RetryAskPanel, true);
        SetStage(TxtStage, Say("main.output.waiting"));
    }

    private void HideRetryAsk()
    {
        _activeRetryPrompt = null;
        RetryAskPanel.IsVisible = false;
        RetryTrimPanel.IsVisible = false;
    }

    private TrimSide SelectedTrimSide =>
        RbTrimStart.IsChecked == true ? TrimSide.Start
        : RbTrimBoth.IsChecked == true ? TrimSide.Both
        : TrimSide.End;

    private void RefreshTrimRange()
    {
        if (_activeRetryPrompt?.TrimFor(SelectedTrimSide) is not { } plan)
        {
            TxtTrimRange.Text = "";
            BtnTrimConfirm.IsEnabled = false;
            return;
        }

        var removed = new List<string>();
        if (plan.RemovedFromStart > 0) removed.Add($"{Clock(0)}–{Clock(plan.StartSeconds)}");
        if (plan.RemovedFromEnd > 0) removed.Add($"{Clock(plan.EndSeconds)}–{Clock(plan.DurationSeconds)}");
        TxtTrimRange.Text = Bicim.Satir.Bagla(Say("main.retry.trim.range",
            Num(plan.RemovedSeconds, "0.#"),
            Clock(plan.DurationSeconds),
            Clock(plan.KeptSeconds),
            string.Join(" · ", removed),
            Num(Megabayt.Oku(plan.KeptBytes), "0.00")));
        BtnTrimConfirm.IsEnabled = true;
    }

    private static string Clock(double seconds)
    {
        return Saat.Kesit(TimeSpan.FromSeconds(Math.Max(0, seconds)), Strings.Culture);
    }

    internal Task<OvershootChoice> ShowRetryAskForTest(RetryPrompt prompt)
    {
        var decision = new TaskCompletionSource<OvershootChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _retryDecision = decision;
        ShowRetryAsk(prompt);
        return decision.Task;
    }

    private void OnRetryAgain(object? sender, RoutedEventArgs e) => _retryDecision?.TrySetResult(OvershootChoice.Retry);

    private void OnRetryStop(object? sender, RoutedEventArgs e) => _retryDecision?.TrySetResult(OvershootChoice.Leave);

    private void OnRetryAccept(object? sender, RoutedEventArgs e) => _retryDecision?.TrySetResult(OvershootChoice.AcceptLarger);

    private void OnRetryTrim(object? sender, RoutedEventArgs e)
    {
        if (RetryTrimPanel.IsVisible) RetryTrimPanel.IsVisible = false;
        else Fade(RetryTrimPanel, true);
        RefreshTrimRange();
    }

    private void OnTrimConfirm(object? sender, RoutedEventArgs e) => _retryDecision?.TrySetResult(SelectedTrimSide switch
    {
        TrimSide.Start => OvershootChoice.TrimStart,
        TrimSide.Both => OvershootChoice.TrimBoth,
        _ => OvershootChoice.TrimEnd
    });

    /// <summary>Panelin planı; toplu kuyruğun ön ayardan kurduğu planla eşdeğerliği bununla ölçülür.</summary>
    internal ConversionPlan CurrentConversionPlan() => ReadConversionPlan();

    private ConversionPlan ReadConversionPlan()
    {
        var container = SelectedTag(CmbContainer) ?? ConversionDefaults.Container;
        var codecTag = SelectedTag(CmbConvertCodec) ?? ConversionDefaults.VideoCodec;
        var intermediate = IntermediateCodecs.FromId(codecTag);
        var codec = intermediate?.Encoder ?? codecTag;
        int.TryParse(TxtQuality.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality);
        int.TryParse(TxtAudioBitrate.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var audioK);

        int? width = null, height = null;
        var resolutionTag = SelectedTag(CmbResolution);
        if (resolutionTag == "custom")
        {
            var parts = (TxtCustomResolution.Text ?? "").ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)) (width, height) = (w, h);
        }
        else if (resolutionTag is not null && int.TryParse(resolutionTag, out var fixedHeight))
        {
            height = fixedHeight;
        }

        double? fps = null;
        var fpsTag = SelectedTag(CmbConvertFps);
        if (fpsTag == "custom")
        {
            if (double.TryParse(TxtCustomFps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var custom)) fps = custom;
        }
        else if (fpsTag is not null && double.TryParse(fpsTag, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedFps))
        {
            fps = fixedFps;
        }

        var audio = SelectedTag(CmbConvertAudio);
        if (new ConversionPlan { Container = container }.AudioOnly) audio = ConversionPresetPlan.AudioCodecForAudioContainer(container);

        return DonusturKodlayiciPlani(WithConvertTags(new ConversionPlan
        {
            Container = container,
            VideoCodec = codec,
            VideoProfile = intermediate?.Profile,
            QualityMode = CmbQualityMode.SelectedIndex == 1 ? ConversionQualityMode.Bitrate : ConversionQualityMode.Crf,
            Crf = quality > 0 ? quality : ConversionDefaults.Crf,
            VideoBitrateK = quality > 0 ? quality : ConversionDefaults.VideoBitrateK,
            Width = width,
            Height = height,
            Fps = fps,
            AudioCodec = audio,
            AudioBitrateK = audioK > 0 ? audioK : ConversionDefaults.AudioBitrateK,
            AudioLoudnorm = ChkConvertLoudnorm.IsChecked == true,
            AudioChannels = ConvertAudioNumber(CmbConvertChannels),
            AudioSampleRate = ConvertAudioNumber(CmbConvertSampleRate),
            Start = ParseTime(TxtTrimStart.Text),
            End = ParseTime(TxtTrimEnd.Text),
            DropMetadata = ChkConvertDropMetadata.IsChecked == true,
            SubtitleTracks = ConvertSubtitleChoice(),
            AudioTracks = ConvertAudioChoice(),
            ExternalAudio = ConvertExternalAudio,
            EndAtShortest = ChkConvertAudioShortest.IsChecked == true,
            ExternalSubtitles = _convertSubtitles.ToList(),
            SingleFrame = ChkConvertSingleFrame.IsChecked == true,
            BurnSubtitle = ConvertBurnChoice,
            BurnFont = ConvertBurnsText ? ConvertBurnFont : null,
            BurnFontSize = ConvertBurnsText ? ConvertBurnFontSize : null,
            BurnColor = ConvertBurnsText ? ConvertBurnColor : null,
            BurnBox = ConvertBurnsText && ChkConvertBurnBox.IsChecked == true,
            Filters = DonusturSuzgecleri(),
            Letterbox = resolutionTag == "custom" && ChkConvertLetterbox.IsChecked == true,
            FrameRate = DonusturKareHiziKipi(),
            Interpolate = ChkConvertInterpolate.IsVisible && ChkConvertInterpolate.IsChecked == true,
            TargetBytes = _sequence is null ? GifPalette.TargetFor(_info ?? _sesKaynagi, container, ParseTargetMb()) : null,
            GifLoop = container == "gif" ? ConvertAudioNumber(CmbConvertGifLoop) : null,
            GifColors = container == "gif" ? ConvertAudioNumber(CmbConvertGifColors) : null,
            GifDither = container == "gif" && SelectedTag(CmbConvertGifDither) is { Length: > 0 } dither ? dither : null,
            ExtraArgs = CustomArguments.Parse(TxtConvertExtraArgs.Text).Arguments,
            AudioVisual = SesGoruntusu()
        }));
    }

    private static string? ExtraArgsError(CustomArgumentResult extra) => extra.Problem switch
    {
        CustomArgumentProblem.UnclosedQuote => Say("main.convert.extra-args.quote"),
        CustomArgumentProblem.Forbidden => Say("main.convert.extra-args.forbidden", extra.Offender),
        CustomArgumentProblem.StrayValue => Say("main.convert.extra-args.stray", extra.Offender),
        _ => null
    };

    private static TimeSpan? ParseTime(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : TimeSpan.MinValue;

    private (int Min, int Max) CrfLimits()
        => CodecModel.CrfRange(ReadConversionPlan().VideoCodec);

    private void OnQualitySliderChanged()
    {
        if (_syncing) return;
        _syncing = true;
        TxtQuality.Text = Math.Round(SliderQuality.Value).ToString(CultureInfo.InvariantCulture);
        _syncing = false;
        RefreshConversion();
    }

    private void OnQualityTextChanged()
    {
        if (_syncing) return;
        ApplyQualityRange();
        RefreshConversion();
    }

    private void OnQualityRangeChanged()
    {
        OzelDonusturAlanlari();
        if (_syncing) return;
        ApplyQualityRange();
        RefreshConversion();
    }

    private void ApplyQualityRange()
    {
        if (!int.TryParse(TxtQuality.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return;
        _syncing = true;
        var crfMode = CmbQualityMode.SelectedIndex != 1;
        var (min, max) = CrfLimits();
        SliderQuality.Maximum = crfMode ? max : Math.Max(10000, value);
        SliderQuality.Minimum = crfMode ? min : 50;
        SliderQuality.Value = Math.Clamp(value, SliderQuality.Minimum, SliderQuality.Maximum);
        _syncing = false;
    }

    private void OnConvertChanged()
    {
        OzelDonusturAlanlari();
        if (_syncing) return;
        RefreshConversion();
    }

    /// <summary>
    /// Özel boyut ve kare hızı yalnız "Özel" seçiliyken, ses bit hızı yalnız ses kodlanırken açık:
    /// <see cref="ReadConversionPlan"/> öbür durumda bu kutuları okumuyor, "Kaynak" seçiliyken
    /// dolu ve seçilebilir görünen 1280x720 ile 25 çıktıya hiçbir şey katmıyordu.
    /// </summary>
    private void OzelDonusturAlanlari()
    {
        TxtCustomResolution.IsEnabled = SelectedTag(CmbResolution) == "custom";
        TxtCustomFps.IsEnabled = SelectedTag(CmbConvertFps) == "custom";
        ChkConvertLetterbox.IsEnabled = SelectedTag(CmbResolution) == "custom";
        var plan = ReadConversionPlan();
        TxtAudioBitrate.IsEnabled = plan.AudioCodec is { } ses && ses != "copy";
        var kaliteOkunur = !IntermediateCodecs.IsIntermediate(plan.VideoCodec) && !plan.ImageSequenceOutput;
        CmbQualityMode.IsEnabled = SliderQuality.IsEnabled = TxtQuality.IsEnabled = kaliteOkunur;
        ChkConvertSingleFrame.IsVisible = plan.ImageSequenceOutput;
        ConvertGifPanel.IsVisible = plan.Gif;
        ConvertAudioVisualPanel.IsVisible = SesKaynagiAcik;
        BtnConvertChapters.IsVisible = ConversionChapters.Carries(plan.Container);
        DonusturKodlayiciAlanlari();
    }

    private void RefreshConversion()
    {
        if (ConvertInfo is not { } info) return;
        DonusturKirpmaYoklamasi();
        DonusturKodlayiciAlanlari();
        var plan = ReadConversionPlan();
        var errors = ConversionArguments.Validate(info, plan).Select(message => LanguageCatalog.Validation(message)).ToList();
        if (plan.Start == TimeSpan.MinValue || plan.End == TimeSpan.MinValue)
            errors.Add(LanguageCatalog.Validation(LanguageCatalog.TrimFormatError));
        if (ExtraArgsError(CustomArguments.Parse(TxtConvertExtraArgs.Text)) is { } extraError) errors.Add(extraError);
        if (ConvertChapterError() is { } chapterError) errors.Add(chapterError);

        var output = CommandOutputPath(plan, ConversionOutputPath(info, plan));
        RefreshConvertSubtitleState(plan);
        RefreshConvertAudioState(plan);
        TxtConvertValidation.Text = errors.Count == 0
            ? string.Join("\n", ConversionArguments.Notes(plan, info).Select(note => Say(ConversionNoteKey(note))).Concat(ConvertSubtitleNotes(plan)).Prepend(Say("main.convert.ready")))
            : string.Join("\n", errors);
        BtnConvert.IsEnabled = errors.Count == 0 && _cts is null;

        try
        {
            TxtConvertCommand.Text = errors.Count == 0
                ? DonusturKomutu(info, plan, output)
                : "";
        }
        catch (Exception ex)
        {
            TxtConvertValidation.Text = DescribeFailure(ex);
            BtnConvert.IsEnabled = false;
        }
    }

    private async void OnConvert(object? sender, RoutedEventArgs e)
    {
        if (ConvertInfo is not { } info || _cts is not null) return;

        var plan = ReadConversionPlan();
        var output = ConversionOutputPath(info, plan);
        QueueWatch.MarkOwn(output);
        var cts = new CancellationTokenSource();
        _cts = cts;
        var biten = new BitenIs(info.FilePath, IsSonucu.Hatali);
        try
        {
            SetRunning(true);
            TxtConvertResult.Text = "";
            BtnConvertReveal.IsVisible = false;
            var progress = new Progress<EncodeProgress>(p =>
            {
                ConvertProgress.Value = p.Fraction;
                _isKesri = p.Fraction;
                SetStage(TxtConvertStage, LocalizeStage(p.Stage));
            });

            var result = await new EncodeRunner().ConvertAsync(info, plan, output, progress, cts.Token);
            _lastOutput = FirstFrameOf(result.OutputPath);
            if (info.Sequence is null && !plan.ImageSequenceOutput) RefreshPreviewSource();
            TxtConvertResult.Text = Say("main.run.converted", Num(info.FileSizeMb, "0.0"), Num(result.OutputMb, "0.0"));
            BtnConvertReveal.IsVisible = true;
            biten = biten with { Sonuc = IsSonucu.Basarili };
        }
        catch (OperationCanceledException)
        {
            biten = biten with { Sonuc = IsSonucu.Iptal };
            TxtConvertResult.Text = Say("main.run.cancelled");
        }
        catch (Exception ex)
        {
            TxtConvertResult.Text = DescribeFailure(ex);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            SetRunning(false);
            RefreshConversion();
            FlushPendingMacFile();
            IsBittiHaberi(biten);
        }
    }

    private Func<bool>? _pencereEtkin;

    /// <summary>İş bitti haberinin sistem yüzü; testler sahtesini verir.</summary>
    internal IIsBildirimYuzu Bildirim { get; set; } = IsBittiBildirimi.Varsayilan();

    /// <summary>Pencere şu an kullanıcının önünde mi; testler kendi cevabını verir.</summary>
    internal Func<bool> PencereEtkin
    {
        get => _pencereEtkin ??= () => IsActive;
        set => _pencereEtkin = value;
    }

    /// <summary>
    /// Küçültme ya da dönüştürme bittiğinde tek haber. Karar <see cref="IsBittiBildirimi.Karar"/>'da;
    /// ayar Ayarlar sekmesindeki kutudan okunur.
    /// </summary>
    internal void IsBittiHaberi(BitenIs biten)
    {
        var karar = IsBittiBildirimi.Karar(new[] { biten }, PencereEtkin(), ChkNotifyWhenDone.IsChecked == true,
            (key, args) => string.Format(Strings.Culture, Say(key), args));
        if (IsBittiBildirimi.SesKarari(new[] { biten }, ChkFinishSound.IsChecked == true) is bool hata) Ses.Cal(hata, FinishSoundFileOrNull);
        if (karar is not null) Bildirim.Bildir(this, karar);
    }

    /// <summary>Bitiş sesinin yüzü; test konağında sessiz olan gelir.</summary>
    internal IBitisSesi Ses { get; set; } = IsBittiBildirimi.VarsayilanSes();

    private string DescribeFailure(Exception ex)
    {
        var raw = ex.Message ?? "";
        var lead = ClassifyFailure(raw) ?? Say("main.error.generic");
        return raw.Length == 0 ? lead : $"{lead}\n{raw}";
    }

    private string? ClassifyFailure(string raw)
    {
        if (Mentions(raw, "no space left", "not enough space", "enospc", "disk full", "insufficient disk space"))
            return Say("main.error.disk-full");

        if (Mentions(raw, "unknown encoder", "encoder not found", "does not support", "could not write header",
                "error initializing output stream", "automatic encoder selection failed", "incorrect codec parameters",
                "invalid argument", "muxer does not support"))
            return Say("main.error.encoder-container");

        if (Mentions(raw, "invalid data found", "moov atom not found", "could not find codec parameters",
                "decoder not found", "no such file or directory", "end of file", "unknown format"))
            return Say("main.error.undecodable");

        return null;
    }

    private static bool Mentions(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private void ShowEncodeProgress(EncodeProgress p)
    {
        Progress.Value = p.Fraction;
        _isKesri = p.Fraction;
        SetStage(TxtStage, LocalizeStage(p.Stage));
        TxtRemaining.Text = Saat.Kalan(p.Remaining);
        if (p.OutputMb > 0) TxtOutSize.Text = Say("main.unit.mb-value", Num(p.OutputMb, "0.0"));
        if (EncodeMarkerOf(p) is { } marker)
            Preview.Controls.SetEncodeProgress(marker.Fraction, marker.Pass, marker.PassCount, marker.Attempt);
    }

    /// <summary>
    /// Şeridin kodlama imleci. Motorun kesri bütün geçişlere yayılır (iki geçişte ilki 0–0,5);
    /// imleç videonun içinde o geçişin vardığı yeri gösterir. Aşama motorun kendi biçiminde
    /// değilse imleç yerinde kalır.
    /// </summary>
    internal static (double Fraction, int Pass, int PassCount, int Attempt)? EncodeMarkerOf(EncodeProgress p)
        => EncodeStage.Parse(p.Stage) is { } stage ? (stage.Within(p.Fraction), stage.Pass, stage.PassCount, stage.Attempt) : null;

    internal void ShowEncodeProgressForTest(EncodeProgress p)
    {
        SetRunning(true, true);
        ShowEncodeProgress(p);
    }

    internal void EndEncodeForTest() => SetRunning(false);

    /// <summary>SIK-P2: kodlama boyunca tutulan uyku isteği; testler sahte katmanlı nöbet verir.</summary>
    internal SleepGuard Uyku { get; set; } = SleepGuard.Shared;

    private IDisposable? _uykuIstegi;

    private void HoldSleep(bool running)
    {
        if (running && ChkPreventSleep.IsChecked == true)
        {
            _uykuIstegi ??= Uyku.Hold();
            return;
        }
        if (running) return;
        _uykuIstegi?.Dispose();
        _uykuIstegi = null;
    }

    /// <summary>SIK-P3: ana sekmede koşan kodlamayı askıya alan düğme; dönüştürme bunu kullanmaz.</summary>
    internal EncodePause RunPause { get; } = new();

    internal bool RunPauseEnabled => BtnPauseRun.IsVisible && BtnPauseRun.IsEnabled;

    internal void ToggleRunPause()
    {
        if (!RunPauseEnabled) return;
        if (RunPause.IsPaused) RunPause.Resume();
        else RunPause.Pause();
        ShowRunPause();
    }

    private void ShowRunPause()
        => BtnPauseRun.Content = Say(RunPause.IsPaused ? "main.action.resume-run" : "main.action.pause-run");

    private void OnPauseRun(object? sender, RoutedEventArgs e) => ToggleRunPause();

    private void SetRunning(bool running, bool pausable = false)
    {
        BtnStart.IsEnabled = !running && _info is not null;
        BtnConvert.IsEnabled = !running && _info is not null;
        // Pasif dugmenin ipucu sebebi soyler: is surerken "once video yukleyin" yanlisti.
        // Yerel deger is boyunca stilin ipucunu ezer, temizlenince :disabled stili geri gelir.
        foreach (var button in new[] { BtnStart, BtnConvert })
        {
            if (running) ToolTip.SetTip(button, Say("main.action.busy-tip"));
            else button.ClearValue(ToolTip.TipProperty);
        }
        BtnCancel.IsEnabled = BtnConvertCancel.IsEnabled = running;
        BtnPauseRun.IsVisible = BtnPauseRun.IsEnabled = running && pausable;
        if (!BtnPauseRun.IsEnabled) RunPause.Resume();
        HoldSleep(running);
        _isKesri = running ? 0 : null;
        if (running)
        {
            ShowRunPause();
            return;
        }
        Preview.Controls.ClearEncode();
        SetStage(TxtStage, Say("main.output.idle"));
        SetStage(TxtConvertStage, Say("main.output.idle"));
        TxtRemaining.Text = "-";
    }

    /// <summary>
    /// Motorun aşama satırı ("Attempt 2 · encoding") sayı ve İngilizce sözcük karışımıdır.
    /// Sözcükler motorun kendi belirteçleri: ekrana çıkarken karşılıklarıyla değiştirilir,
    /// karşılık sözlükten gelir.
    /// </summary>
    /// <summary>
    /// HDR10+ koprusu kostu ve cikistaki HDR10+ kare sayisi kaynaginkini tutmadi: is bitti ama
    /// eksik, sonuc satiri uyari olur (<c>docs/handbrake/fable-karar-hdr10plus-2026-09-23.md</c> madde 4).
    /// </summary>
    internal static Hdr10PlusCount? Hdr10PlusMissed(EncodeResult result)
        => result.Success && result.Hdr10Plus is { Carried: false } count ? count : null;

    /// <summary>
    /// Sonuc satirini uyari ya da govde olarak gosterir. Uyari renkle degil simge ve agirlikla
    /// ayrilir (<c>docs/netlestirme/018-uyari-rengi-27-palette-yok.md</c>); simgesiz satirda
    /// metin iki sutunu da kaplar.
    /// </summary>
    private void ShowResultWarning(bool warning)
    {
        if (Application.Current?.TryFindResource(warning ? "StatusWarning" : "Body", out var found) == true && found is ControlTheme theme)
            TxtResult.Theme = theme;
        ResultWarningGlyph.IsVisible = warning;
        Grid.SetColumn(TxtResult, warning ? 1 : 0);
        Grid.SetColumnSpan(TxtResult, warning ? 1 : 2);
    }

    private static readonly (string Token, string Key)[] StageWords =
    {
        ("GIF palette", "main.stage.gif-palette"),
        ("GIF encode", "main.stage.gif-encode"),
        ("encoding", "main.stage.encoding"),
        ("converting", "main.stage.converting"),
        ("pass", "main.stage.pass"),
        ("attempt", "main.stage.attempt"),
        (EncodeRunner.Hdr10PlusStage, "main.stage.hdr10plus-metadata")
    };

    private static string LocalizeStage(string stage) => LocalizeStageIn(stage, Strings.Language);

    /// <summary>
    /// Aşama satırı verilen dilde. İş penceresi kendi dilini taşıdığı için süreç genelindeki
    /// <see cref="Strings.Language"/> yerine bunu çağırır.
    /// </summary>
    internal static string LocalizeStageIn(string stage, string language)
    {
        foreach (var (token, key) in StageWords)
            stage = stage.Replace(token, Strings.GetIn(language, key), StringComparison.OrdinalIgnoreCase);

        return LanguageCatalog.Title(stage, language);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _probeCts?.Cancel();
        _cts?.Cancel();
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_lastOutput is null || !File.Exists(_lastOutput)) return;
        try
        {
            Platform.Reveal(_lastOutput);
        }
        catch (Exception ex)
        {
            TxtSystemStatus.Text = $"{Say("main.error.folder")}: {ex.Message}";
        }
    }
}

/// <summary>Skorun neye dayandığı. Panel üçünü ayrı kelimeyle söyler.</summary>
internal enum QualityBasis
{
    /// <summary>Video yüklü değil; skor yok.</summary>
    NoSource,

    /// <summary>Kalibrasyon ölçümü çalıştı, skor ölçüme dayanıyor.</summary>
    Measured,

    /// <summary>Ölçüm yok; skor kaynak bit hızından türetilmiş tahmin.</summary>
    Estimated,

    /// <summary>Kaynak zaten hedefin altında, yeniden kodlama yok.</summary>
    SourceUnderTarget
}

/// <summary>
/// Bir yonganın kalite paneline giren veri. Denetim burada bitiyor: panel yalnız bu
/// kaydı çizer, karar vermez.
///
/// <see cref="For"/> ffprobe çağırmaz: ölçülmüş bir profil verilmişse o kullanılır,
/// verilmemişse <see cref="PlanCalculator.BuildDetailed"/> kendi içinde
/// <see cref="ComplexityProfile.FromSourceBitrate"/>'a düşer; ikisi de saf aritmetik.
///
/// "ffmpeg de çağırmaz" cümlesi T130'a kadar yanlıştı — <c>BuildDetailed</c> kodlayıcı
/// yeteneğini soruyor, o da süreç doğuruyordu. Artık arayüz bu çağrıya gerçek yetenek
/// nesnesini değil <see cref="MainWindow.DeferredEncoderAvailability"/> geçidini
/// veriyor; geçit yalnız ısıtılmış cevabı okur.
/// </summary>
internal readonly record struct QualityHint(double? TargetMb, double? Score, QualityBasis Basis)
{
    /// <summary>Kaynak kalitesi 100 sayılır; kayıp o tavana göre okunur.</summary>
    private const double SourceQualityScore = 100.0;

    internal double LossPoints => Score is { } score ? SourceQualityScore - score : 0;

    internal static QualityHint None { get; } = new(null, null, QualityBasis.NoSource);

    internal static QualityHint For(
        MediaInfo? info,
        PlanOptions options,
        double? targetMb,
        ComplexityProfile? profile,
        IEncoderAvailability? availability)
    {
        if (info is null || targetMb is not { } target || target <= 0) return None;

        options.TargetMb = target;
        var result = PlanCalculator.BuildDetailed(info, options, profile, availability);
        var codes = result.Plan.ReasonCodes.Select(note => note.Code).ToList();

        var basis = codes.Contains(ReasonCode.PredictedQualityMeasured)
            ? QualityBasis.Measured
            : codes.Contains(ReasonCode.PredictedQualityEstimated)
                ? QualityBasis.Estimated
                : QualityBasis.SourceUnderTarget;

        return new QualityHint(target, result.PredictedQuality, basis);
    }
}

/// <summary>
/// Paylaş düğmesinin arkasındaki iş: yükle, kaydı tut, iptal et, yayını kapat. Pencere
/// yalnız ilerlemeyi ve sonucu çizer.
/// </summary>
/// <remarks>
/// Sağlayıcı dışarıdan veriliyor, çünkü ölçüm ağa çıkmadan koşmalı: sahte taşıyıcıya bağlı
/// gerçek sağlayıcı verilir ve tavan denetimi, iptal, hata sınıflandırması aynı kodla ölçülür.
/// Tavanı aşan dosyayı sağlayıcı zaten yüklemeye kalkışmadan reddeder; burada ikinci bir
/// denetim yok, tek karar yeri <c>ShareErrorClassifier.CheckSize</c>.
/// </remarks>
internal sealed class ShareFlow
{
    private readonly Func<CoreShare.ShareTarget, CoreShare.IShareProvider> _provider;
    private readonly CoreShare.ShareLedger _ledger;
    private CancellationTokenSource? _upload;

    internal ShareFlow(
        Func<CoreShare.ShareTarget, CoreShare.IShareProvider> provider,
        CoreShare.ShareLedger? ledger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ledger = ledger ?? new CoreShare.ShareLedger();
    }

    /// <summary>Açık olan son paylaşım. Kapatıldığında ya da hiç yapılmadığında boştur.</summary>
    internal CoreShare.ShareLink? Link { get; private set; }

    internal bool Running => _upload is not null;

    /// <summary>Elde silme jetonu var mı; silme düğmesi buna bakar.</summary>
    internal bool CanDelete => Link is { CanDelete: true };

    internal void Cancel() => _upload?.Cancel();

    internal async Task<CoreShare.ShareResult> ShareAsync(
        CoreShare.ShareTarget target,
        string filePath,
        int? retentionDays = null,
        IProgress<CoreShare.UploadProgress>? progress = null)
    {
        if (_upload is not null)
            return CoreShare.ShareResult.Failed(new CoreShare.ShareDiagnosis(
                CoreShare.ShareFailure.Unknown,
                Strings.Get("settings.share.upload-busy")));

        var cts = new CancellationTokenSource();
        _upload = cts;
        try
        {
            var result = await _provider(target).UploadAsync(filePath, retentionDays, progress, cts.Token);
            if (result.Ok && result.Link is { } link)
            {
                Link = link;
                TryRecord(link);
            }

            return result;
        }
        finally
        {
            _upload = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// Yayını kapatır. Jeton geçersizse silinecek bir şey kalmamıştır; kayıt yine düşürülür,
    /// yoksa kapatılamayan bir satır orada kalırdı.
    /// </summary>
    internal async Task<CoreShare.ShareResult> DeleteAsync(
        CoreShare.ShareTarget target,
        CancellationToken cancellationToken = default)
    {
        if (Link is not { } link)
            return CoreShare.ShareResult.Failed(new CoreShare.ShareDiagnosis(
                CoreShare.ShareFailure.Unknown, Strings.Get("settings.share.nothing-to-close")));

        var result = await _provider(target).DeleteAsync(link, cancellationToken);
        if (!result.Ok && result.Failure != CoreShare.ShareFailure.TokenExpired) return result;

        TryForget(link.FileId);
        Link = null;
        return result;
    }

    // Kayıt defteri yazılamazsa paylaşım yine de başarılıdır; kaybedilen tek şey uygulama
    // kapanıp açıldıktan sonra yayını kapatabilme imkânı.
    private void TryRecord(CoreShare.ShareLink link)
    {
        try { _ledger.Add(link); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void TryForget(string fileId)
    {
        try { _ledger.Remove(fileId); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
