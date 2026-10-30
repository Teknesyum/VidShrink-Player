using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.Player;

using VidShrink.Core;

namespace VidShrink.App.Playback;

internal partial class PlayerView : UserControl
{
    internal const int HistorySaveTicks = 10;

    private readonly ZoomGesture _zoom = new();
    private PencereKipi? _kip;
    private readonly StallWatch _stall = new();
    private readonly SeekCoalescer _seek;
    private readonly List<string> _trace = new();

    private IPlaybackEngine? _engine;
    private WriteableBitmap? _bitmap;
    private DispatcherTimer? _watchdog;
    private DispatcherTimer? _render;
    private PlaybackHistory _history = new();
    private long _shown;
    private long _drawn;

    internal long DrawnFrames => _drawn;

    internal event Action? IlkKareCizildi;
    private string? _path;
    private bool _playing;
    private DispatcherTimer? _pauseFlash;

    private double PauseGlyphOpacity => this.FindResource("PauseGlyphOpacity") is double d ? d : 0.5;

    private TimeSpan PauseGlyphHold => this.FindResource("PauseGlyphHold") is TimeSpan t ? t : TimeSpan.FromMilliseconds(500);

    private TimeSpan MotionInstant => this.FindResource("MotionInstant") is TimeSpan t ? t : TimeSpan.FromMilliseconds(40);

    private TimeSpan MotionFast => this.FindResource("MotionFast") is TimeSpan t ? t : TimeSpan.FromMilliseconds(160);

    private bool _trackPaused;
    private int _leftClicks;
    private int _watchdogTicks;
    private double _volume = 100;
    private bool _muted;
    private double _speed = 1;
    private double _loopStart = double.NaN;
    private double _loopEnd = double.NaN;

    public PlayerView()
    {
        InitializeComponent();
        _seek = new SeekCoalescer(RunSeekAsync);

        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Surface.SizeChanged += OnSurfaceSize;
        InitTracks();
        InitWindow();
        InitTools();
        InitFare();
        InitPlaylist();
        InitUyku();

        RefreshState();
    }

    internal SeekCoalescer Seek => _seek;

    internal ZoomGesture Zoom => _zoom;

    internal PencereKipi Kip
    {
        get => _kip ??= new PencereKipi(() => TopLevel.GetTopLevel(this) as Window, _trace.Add);
        set
        {
            _kip = value;
            value.Yansit ??= _trace.Add;
        }
    }

    internal FullscreenSwitch Fullscreen => Kip.Tam;

    internal StallWatch Stall => _stall;

    internal bool IsPlaying => _playing;

    internal int LeftClicks => _leftClicks;

    internal double PositionSeconds => _seek.Target;

    internal double? DurationSeconds => _path is not null && double.IsFinite(_seek.Duration) && _seek.Duration > 0 ? _seek.Duration : null;

    internal double ZoomScale => _zoom.PanelScale;

    internal bool FillMode => _zoom.Fill;

    internal double VolumeLevel => _volume;

    internal bool IsMuted => _muted;

    internal double SpeedFactor => _speed;

    internal double LoopStart => _loopStart;

    internal double LoopEnd => _loopEnd;

    internal PlaybackHistory History => _history;

    internal IReadOnlyList<string> Trace => _trace.ToArray();

    internal string? LoadedPath => _path;

    internal IPlaybackEngine? Engine => _engine;

    private Func<IPlaybackEngine>? _engineFactory;

    internal Func<IPlaybackEngine> EngineFactory
    {
        get
        {
            if (_engineFactory is { } factory) return factory;
            var options = new PlaybackOptions { MediaControls = MediaControlsOn };
            return () => new MpvEngine(options);
        }
        set => _engineFactory = value;
    }

    internal bool SystemMediaControls { get; set; } = true;

    internal static bool MediaControlsSupported => OperatingSystem.IsWindows();

    internal bool MediaControlsOn => SystemMediaControls && MediaControlsSupported && Settings.MediaControls;

    internal Func<string>? HistoryPath { get; set; }

    internal Func<int> PlayerTabIndex { get; set; } = () => 0;

    internal Func<int> CurrentTabIndex { get; set; } = () => 0;

    internal Action<int>? SelectTab { get; set; }

    internal void Apply(PlayerCommand command)
    {
        RestoreSpeedMode();
        RestoreSound();
        KullaniciIslemi();
        switch (command.Kind)
        {
            case PlayerCommandKind.Seek:
                _seek.Nudge(command.Amount);
                _trace.Add("seek " + Saat.Tani.Konum(command.Amount) + " -> " + Saat.Tani.Konum(_seek.Target));
                break;
            case PlayerCommandKind.Zoom:
                _zoom.Wheel(command.Amount, Surface.Bounds.Width / 2, Surface.Bounds.Height / 2);
                Resize();
                _trace.Add("zoom " + Saat.Tani.Konum(command.Amount) + " -> " + Saat.Tani.Konum(_zoom.PanelScale));
                break;
            case PlayerCommandKind.ToggleFill:
                _zoom.Fill = !_zoom.Fill;
                ResetPan();
                Resize();
                _trace.Add("fill -> " + _zoom.Fill);
                break;
            case PlayerCommandKind.TogglePlay:
                TogglePlay();
                _trace.Add("play -> " + _playing);
                break;
            case PlayerCommandKind.ToggleFullscreen:
                ToggleFullscreen();
                _trace.Add("fullscreen -> " + Kip.TamEkran);
                break;
            case PlayerCommandKind.CompactOrFullscreen:
                CompactOrFullscreen();
                _trace.Add("compact -> " + Kip.TamEkran);
                break;
            case PlayerCommandKind.ContextMenu:
                OpenMenu();
                _trace.Add("menu");
                break;
            case PlayerCommandKind.OpenSettings:
                _trace.Add("settings -> " + (OpenSettings is null ? "no" : "open"));
                OpenSettings?.Invoke();
                break;
            case PlayerCommandKind.ResetZoom:
                _zoom.Reset();
                Resize();
                _trace.Add("zoomreset -> " + Saat.Tani.Konum(_zoom.PanelScale));
                break;
            case PlayerCommandKind.LeaveFullscreen:
                Kip.Birak();
                _trace.Add("leavefullscreen -> " + Kip.TamEkran);
                break;
            case PlayerCommandKind.Volume:
                _volume = Math.Clamp(_volume + command.Amount, 0, VolumeCeiling());
                _engine?.SetVolume(_volume);
                _trace.Add("volume " + Saat.Tani.Konum(command.Amount) + " -> " + Saat.Tani.Konum(_volume));
                ShowSoundOsd(false);
                RememberSound();
                break;
            case PlayerCommandKind.ToggleMute:
                _muted = !_muted;
                _engine?.SetMuted(_muted);
                _trace.Add("mute -> " + _muted);
                ShowSoundOsd(true);
                RememberSound();
                break;
            case PlayerCommandKind.Speed:
                _speed = Math.Clamp(Math.Round(_speed + command.Amount, 2), Keymap.MinimumSpeed, Keymap.MaximumSpeed);
                _engine?.SetSpeed(_speed);
                RememberSpeed();
                _trace.Add("speed " + Saat.Tani.Konum(command.Amount) + " -> " + Saat.Tani.Konum(_speed));
                ShowSpeedOsd();
                break;
            case PlayerCommandKind.SpeedAb:
                SwitchSpeedMode();
                ShowSpeedOsd();
                break;
            case PlayerCommandKind.SpeedReset:
                _speed = 1;
                _engine?.SetSpeed(_speed);
                _trace.Add("speedreset -> " + Saat.Tani.Konum(_speed));
                ShowSpeedOsd();
                break;
            case PlayerCommandKind.FrameStep:
                StepFrame(command.Amount < 0);
                _trace.Add("frame " + Saat.Tani.Konum(command.Amount));
                break;
            case PlayerCommandKind.LoopStart:
                MarkLoopStart();
                break;
            case PlayerCommandKind.LoopEnd:
                MarkLoopEnd();
                break;
            case PlayerCommandKind.LoopClear:
                _loopStart = double.NaN;
                _loopEnd = double.NaN;
                _engine?.SetLoop(_loopStart, _loopEnd);
                _trace.Add("loopclear");
                ShowLoopOsd();
                break;
            case PlayerCommandKind.BookmarkAdd:
                AddBookmark();
                break;
            case PlayerCommandKind.BookmarkNext:
                StepBookmark(command.Amount < 0);
                break;
            case PlayerCommandKind.BookmarkList:
                ShowBookmarks();
                break;
            case PlayerCommandKind.ChapterStep:
                StepChapter(command.Amount < 0);
                break;
            case PlayerCommandKind.Stop:
                if (_playing) TogglePlay();
                _seek.GoTo(0);
                _trace.Add("stop -> " + _playing);
                break;
            case PlayerCommandKind.GoToStart:
                _seek.GoTo(0);
                _trace.Add("tostart");
                break;
            case PlayerCommandKind.GoToTime:
                ShowGoTo();
                break;
            case PlayerCommandKind.AudioCycle:
                CycleAudio();
                break;
            case PlayerCommandKind.SubtitleCycle:
                CycleSubtitle();
                break;
            case PlayerCommandKind.SubtitleDelay:
                ShiftSubtitleDelay(command.Amount);
                break;
            case PlayerCommandKind.AudioDelay:
                ShiftAudioDelay(command.Amount);
                break;
            case PlayerCommandKind.SubtitleVisibility:
                ToggleSubtitleVisible();
                break;
            case PlayerCommandKind.SubtitleLine:
                SeekSubtitleLine(command.Amount < 0);
                break;
            case PlayerCommandKind.SubtitleStep:
                StepSubtitleLine(command.Amount < 0);
                break;
            case PlayerCommandKind.SubtitleCopy:
                CopySubtitleText();
                break;
            default:
                if (!ApplyWindow(command) && !ApplyTools(command) && !ApplyEdit(command)) _trace.Add("none");
                break;
        }

        RefreshState();
    }

    internal void FeedWheel(double notches, KeyModifiers modifiers)
        => Apply(Keymap.ForWheel(notches, modifiers));

    internal void FeedPress(PlayerButton button)
    {
        if (button == PlayerButton.Left)
        {
            FarePress(0, 0);
            return;
        }

        Apply(Keymap.ForPress(button));
    }

    internal bool FeedKey(Key key, KeyModifiers modifiers = KeyModifiers.None, string? symbol = null)
    {
        var command = Keymap.ForKey(key, modifiers, symbol);
        if (command.Kind == PlayerCommandKind.None) return false;
        Apply(command);
        return true;
    }

    internal double CurrentPosition()
    {
        if (_engine is { IsOpen: true } engine && _seek.Idle.IsCompleted)
        {
            var at = engine.PositionSeconds;
            if (double.IsFinite(at)) return at;
        }

        return _seek.Target;
    }

    private void StepFrame(bool backward)
    {
        if (_engine is not { } engine) return;
        _playing = false;
        _trackPaused = true;
        _stall.Reset();
        engine.StepFrame(backward);
    }

    private void MarkLoopStart()
    {
        _loopStart = CurrentPosition();
        if (double.IsFinite(_loopEnd) && _loopEnd <= _loopStart) _loopEnd = double.NaN;
        _engine?.SetLoop(_loopStart, _loopEnd);
        _trace.Add("loop a -> " + Saat.Tani.Konum(_loopStart));
        ShowLoopOsd();
    }

    private void MarkLoopEnd()
    {
        var at = CurrentPosition();
        var start = double.IsFinite(_loopStart) ? _loopStart : 0;
        if (at <= start)
        {
            _trace.Add("loop b -> no");
            return;
        }

        _loopStart = start;
        _loopEnd = at;
        _engine?.SetLoop(_loopStart, _loopEnd);
        _trace.Add("loop b -> " + Saat.Tani.Konum(_loopEnd));
        ShowLoopOsd();
    }

    private void AddBookmark()
    {
        if (_path is not { } path)
        {
            _trace.Add("bookmarkadd -> no");
            return;
        }

        var at = CurrentPosition();
        _history.AddBookmark(path, at);
        _history.Save(HistoryPath?.Invoke());
        _trace.Add("bookmarkadd -> " +Saat.Tani.Konum(at));
    }

    private void StepBookmark(bool backward)
    {
        var name = backward ? "bookmarkprev -> " : "bookmarknext -> ";
        var at = CurrentPosition();
        var mark = _path is not { } path
            ? null
            : backward ? _history.PreviousBookmark(path, at) : _history.NextBookmark(path, at);
        if (mark is not { } next)
        {
            _trace.Add(name + "no");
            return;
        }

        _trackPaused = false;
        _seek.GoTo(next);
        _trace.Add(name + Saat.Tani.Konum(next));
    }

    private void StepChapter(bool backward)
    {
        var name = backward ? "chapter -1 -> " : "chapter 1 -> ";
        if (_engine is not { } engine || _chapters.Count == 0)
        {
            _trace.Add(name + "no");
            return;
        }

        var target = ChapterTarget(_chapters, CurrentPosition(), backward);
        if (!engine.StepChapter(backward))
        {
            _trace.Add(name + "no");
            return;
        }

        _trackPaused = true;
        _stall.Reset();
        _trace.Add(name + "ok");
        ShowChapterOsd(target);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var notches = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (notches == 0) return;
        if (!WheelOnSerit(e, notches)) FeedWheel(notches, e.KeyModifiers);
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSeekBarSource(e.Source)) return;
        var point = e.GetCurrentPoint(this);
        PlayerButton button;
        if (point.Properties.IsRightButtonPressed) button = PlayerButton.Right;
        else if (point.Properties.IsMiddleButtonPressed) button = PlayerButton.Middle;
        else if (point.Properties.IsLeftButtonPressed) button = PlayerButton.Left;
        else if (point.Properties.IsXButton1Pressed) button = PlayerButton.Back;
        else if (point.Properties.IsXButton2Pressed) button = PlayerButton.Forward;
        else return;

        if (MenuPress(e, button))
        {
            e.Handled = true;
            return;
        }

        if (TitlePress(e, button))
        {
            e.Handled = true;
            return;
        }

        if (button == PlayerButton.Left)
        {
            if (EtkilesimliDenetim.Icinde(e.Source as Visual, this)) return;
            if (e.Pointer.Type == PointerType.Touch)
            {
                DokunBas(e);
                return;
            }

            var at = point.Position;
            FarePress(e, at.X, at.Y);
            return;
        }

        if (button == PlayerButton.Right)
        {
            MenuAtPointer = true;
            _menuPointer = TopLevel.GetTopLevel(this) is null ? null : this.PointToScreen(point.Position);
        }
        FeedPress(button);
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Strings.Changed -= OnLanguageChanged;
        Strings.Changed += OnLanguageChanged;
        if (TopLevel.GetTopLevel(this) is { } top)
            top.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Strings.Changed -= OnLanguageChanged;
        if (TopLevel.GetTopLevel(this) is { } top)
            top.RemoveHandler(KeyDownEvent, OnKey);
        SaveHistory(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEffectivelyVisible || !KeyboardEnabled) return;
        if (e.Source is TextBox) return;
        if (FeedKey(e.Key, e.KeyModifiers, e.KeySymbol)) e.Handled = true;
    }

    internal bool KeyboardEnabled { get; set; } = true;

    internal bool StripVisible
    {
        get => StripBar.IsVisible;
        set => StripBar.IsVisible = value;
    }

    internal bool MenuAtPointer { get; set; }

    private MenuFlyout? _menu;

    internal bool MenuOpen => _menu?.IsOpen ?? false;

    internal List<Control> SettingsItems()
    {
        var items = new List<Control>();
        if (AppSettingsItems?.Invoke() is { Count: > 0 } app)
        {
            items.AddRange(app);
            items.Add(new Separator());
        }

        var folder = new MenuItem { Header = Strings.Get("player.view.screenshot-folder") };
        folder.Click += OnPickScreenshotFolder;
        items.Add(folder);
        items.Add(Switch(Strings.Get("player.view.screenshot-jpg"), Settings.ScreenshotFormat == ScreenshotFormat.Jpg, ToggleScreenshotJpg));
        items.Add(Switch(Strings.Get("player.view.screenshot-subtitles"), Settings.ScreenshotSubtitles, ToggleScreenshotSubtitles));
        items.Add(Switch(Strings.Get("player.view.keep-history"), Settings.KeepHistory, ToggleKeepHistory));
        items.Add(ResumeMenu());
        items.Add(Switch(Strings.Get("player.view.audio-language"), Settings.AudioInInterfaceLanguage, ToggleAudioLanguage));
        items.Add(Switch(Strings.Get("player.view.subtitle-language"), Settings.SubtitleInInterfaceLanguage, ToggleSubtitleLanguage));
        items.Add(Switch(Strings.Get("player.view.pause-on-minimize"), Settings.PauseOnMinimize, TogglePauseOnMinimize));
        items.Add(Switch(Strings.Get("player.view.topmost-while-playing"), Settings.TopmostWhilePlaying, ToggleTopmostWhilePlaying));
        if (SystemMediaControls && MediaControlsSupported) items.Add(Switch(Strings.Get("player.view.media-controls"), Settings.MediaControls, ToggleMediaControls));
        items.Add(SeekStepsMenu());
        items.Add(AdvancedMenu());
        items.Add(ShortcutsMenu());
        return items;
    }

    /// <summary>
    /// P3: Ayarlar sekmesindeki oynatıcı kısayolları bölümü menüde alt menü. Satır tablodan
    /// (<see cref="Keymap.Rows"/>) gelir; sekmeye götüren <see cref="Keymap.Settings"/> eylemi
    /// tabloda değil yalnız <see cref="Keymap.MenuActions"/>'da olduğu için menüde kendiliğinden
    /// yok — D1'de bu satırları süzen ölü koşul kaldırıldı.
    /// </summary>
    internal MenuItem ShortcutsMenu()
    {
        var menu = new MenuItem { Header = Strings.Get("settings.player-shortcuts.title") };
        foreach (var row in Keymap.Rows)
        {
            var item = new MenuItem { Tag = row.Action };
            if (row.Input.Kind == PlayerInputKind.Key)
            {
                item.Header = Keymap.Label(row);
                item.InputGesture = new KeyGesture(row.Input.Key, row.Input.Modifiers);
            }
            else
            {
                item.Header = Keymap.Tip(Keymap.Label(row), row.Input);
            }

            item.Click += OnMenuRow;
            menu.Items.Add(item);
        }

        return menu;
    }

    private void OnMenuRow(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: PlayerAction action })
            Apply(action.ToCommand());
    }

    private void OpenMenu()
    {
        var flyout = BuildMenu();
        _menu = flyout;
        var pointer = MenuAtPointer ? _menuPointer : null;
        var focused = MenuAtPointer ? null : KeyboardMenuTarget();
        MenuAnchor = MenuAtPointer ? "pointer" : focused is null ? "surface" : "focus";
        flyout.Opened += (_, _) => OpenPlaylist(flyout, pointer, focused);
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, flyout) || _menu is null) ClosePlaylist();
        };
        try
        {
            flyout.ShowAt(focused ?? Surface, MenuAtPointer);
        }
        catch (InvalidOperationException) { }

        MenuAtPointer = false;
    }

    internal void ToggleFullscreen() => Kip.TamEkranDegistir();

    internal bool IsCompact => Kip.Kucuk;

    internal void CompactOrFullscreen() => Kip.KucukVeyaTamEkran(VideoAspect);

    internal void RestoreWindowMin() => Kip.KucukBitir();

    internal void TogglePlay() => TogglePlay(true);

    private void TogglePlay(bool feedback)
    {
        TogglePlayCore(feedback);
        OynatmaDurumunuYansit();
    }

    private void TogglePlayCore(bool feedback)
    {
        if (!_playing && _engine is { EndReached: true } && PlayFromEnd()) return;
        _playing = !_playing;
        _trackPaused = false;
        if (_playing) HideResumePrompt();
        if (_engine is not { } engine) return;

        _stall.Reset();
        if (_playing)
        {
            engine.Play();
            if (feedback) FlashPause(true);
        }
        else
        {
            _seek.Follow(engine.PositionSeconds);
            engine.Pause();
            SaveHistory(false);
            if (feedback) FlashPause(false);
        }
    }

    /// <summary>
    /// Oynatip duraklatinca sahnenin ortasinda kisa bir oynatma ya da duraklatma simgesi belirir: girisi ve
    /// cikisi <c>MotionFast</c> (giris <c>MotionInstant</c>, iki kat hizli), ekranda toplam kalisi giris ve cikis dahil
    /// <c>PauseGlyphHold</c> (tarif: 0,5 sn). Ust uste basista eski cikisin gizlemesi
    /// yeni simgeyi kapatmasin diye her parlama bir sira numarasi tasir.
    /// </summary>
    private void FlashPause(bool playing)
    {
        _pauseFlash ??= new DispatcherTimer();
        _pauseFlash.Stop();
        _pauseFlashSira++;

        var simge = playing ? "IconPlay" : "IconPause";
        if ((this.TryFindResource(simge, out var kaynak) || Application.Current?.TryFindResource(simge, out kaynak) == true)
            && kaynak is Geometry geometri)
            PauseGlyphIcon.Data = geometri;
        PauseGlyph.IsVisible = true;
        GlyphMotion(MotionInstant);
        PauseGlyph.Opacity = PauseGlyphOpacity;
        PauseGlyph.RenderTransform = TransformOperations.Parse("scale(1)");

        var hold = PauseGlyphHold - MotionFast;
        _pauseFlash.Interval = hold > TimeSpan.Zero ? hold : TimeSpan.Zero;
        _pauseFlash.Tick -= OnPauseFlashDone;
        _pauseFlash.Tick += OnPauseFlashDone;
        _pauseFlash.Start();
    }

    private int _pauseFlashSira;

    private void GlyphMotion(TimeSpan duration)
    {
        if (PauseGlyph.Transitions is not { } list) return;
        foreach (var transition in list)
            if (transition is TransitionBase timed) timed.Duration = duration;
    }

    private void OnPauseFlashDone(object? sender, EventArgs e)
    {
        _pauseFlash?.Stop();
        GlyphMotion(MotionFast);
        PauseGlyph.Opacity = 0;
        PauseGlyph.RenderTransform = TransformOperations.Parse("scale(0.8)");

        var sira = _pauseFlashSira;
        DispatcherTimer.RunOnce(() =>
        {
            if (sira != _pauseFlashSira) return;
            PauseGlyph.IsVisible = false;
        }, MotionFast);
    }

    /// <summary>Çizim saatinin olağan adımı; ekranın kendi hızı.</summary>
    private const int RenderFrameMs = 16;

    /// <summary>
    /// İlk karenin beklediği adım. Saat bir tam kare beklerse ilk görüntü en kötü halde
    /// 16 ms gecikiyor ve bu gecikme motorun kendi payının üstüne biniyor; ilk kare gelene
    /// kadar saat sıkı koşuyor, kare düştükten sonra olağan adıma dönüyor.
    /// </summary>
    private const int FirstFrameMs = 1;

    private void StartRender()
    {
        _shown = 0;
        if (_render is null)
        {
            _render = new DispatcherTimer();
            _render.Tick += (_, _) =>
            {
                var vardi = _shown > 0;
                RenderLatest();
                if ((!vardi && _shown > 0 || AudioOnly) && _render is { } saat)
                    saat.Interval = TimeSpan.FromMilliseconds(RenderFrameMs);
            };
        }
        _render.Interval = TimeSpan.FromMilliseconds(FirstFrameMs);
        _render.Start();
        RenderLatest();
    }

    internal bool RenderLatest()
    {
        if (_engine is not { } engine) return false;
        var drawn = engine.TryCopyLatest(ref _shown, DrawFrame);
        if (drawn && (_playing || _trackPaused)) _seek.Follow(engine.PositionSeconds);
        if (drawn) RefreshState();
        else if (AudioOnly) FollowAudio(engine);
        if (_playing) TickSeekHead();
        if (_playing && engine.EndReached)
        {
            TogglePlay(false);
            SaveHistory(true);
            PlayEndCue();
            AfterEnd();
        }
        return drawn;
    }

    internal async Task OpenAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await OpenCoreAsync(ResolveQueue(path), ct).ConfigureAwait(true);
        }
        catch (PlaybackEngineUnavailableException ex) when (ex.MessageKey is { } key)
        {
            ShowEngineUnavailable(key);
            throw;
        }
    }

    /// <summary>
    /// Motor bu süreçte kullanılamıyor (libmpv bulundu ama yüklenemedi): oynatıcı alanı
    /// boş durum yerine <c>StatusError</c> temalı iletiyi gösterir.
    /// </summary>
    internal void ShowEngineUnavailable(string key)
    {
        TxtEmpty.IsVisible = false;
        TxtEngine.Text = LanguageCatalog.Display(Strings.Get(key));
        TxtEngine.IsVisible = true;
    }

    internal string? EngineUnavailableText => TxtEngine.IsVisible ? TxtEngine.Text : null;

    private async Task OpenCoreAsync(string path, CancellationToken ct)
    {
        Close();
        IPlaybackEngine? engine = null;
        var onden = AcilisMotoru.Devral(path);
        try
        {
            if (onden is not null)
            {
                engine = await onden.ConfigureAwait(true);
                engine.Faulted += OnFaulted;
            }
            else
            {
                engine = await Task.Run(EngineFactory).ConfigureAwait(true);
                engine.Faulted += OnFaulted;
                await engine.OpenAsync(path, ct).ConfigureAwait(true);
            }
        }
        catch when (onden is not null && engine is null)
        {
            engine = await Task.Run(EngineFactory).ConfigureAwait(true);
            engine.Faulted += OnFaulted;
            await engine.OpenAsync(path, ct).ConfigureAwait(true);
        }
        catch
        {
            if (engine is not null)
            {
                engine.Faulted -= OnFaulted;
                engine.Dispose();
            }

            throw;
        }

        _engine = engine;
        _path = path;
        engine.NeighborRequested += OnNeighborRequested;
        _seek.Duration = engine.DurationSeconds > 0 ? engine.DurationSeconds : double.PositiveInfinity;
        _loopStart = double.NaN;
        _loopEnd = double.NaN;
        if (HistoryPath?.Invoke() is { } file) _history = PlaybackHistory.Load(file);
        ApplyAdvanced(engine);
        ApplyHardwareDecoding(engine);
        ApplyToneMapping(engine);
        ApplyAudioOutput(engine);
        RestoreSound();
        if (_volume != 100) engine.SetVolume(_volume);
        if (_muted) engine.SetMuted(true);
        RestoreSpeedMode();
        EnsureSettings();
        if (_settings.SilenceSkip) engine.SetSilenceSkip(_settings.Silence);
        if (_speed != 1) engine.SetSpeed(_speed);
        ApplyTrackOptions(engine);

        TxtEmpty.IsVisible = false;
        ShowAudioCard(path, engine);
        ShowMediaTitle(path, engine);
        LoadStreamVariants(path);
        StartWatchdog();
        StartRender();
        var mode = HistoryPath is null ? ResumeMode.Off : Settings.Resume;
        var resume = mode == ResumeMode.Off ? 0 : _history.ResumeFor(path, engine.DurationSeconds);
        var ask = mode == ResumeMode.Ask && resume > 0;
        _seek.GoTo(ask ? 0 : resume);
        if (resume > 0 && !ask) _trace.Add("resume -> " + Saat.Tani.Konum(resume));
        if (ask) AskResume(resume);
        else if (!_playing && !_openPaused) TogglePlay(false);
        AfterOpen(path, engine);
        RefreshState();
        SaveQueue();
        Opened?.Invoke(path);
    }

    internal event Action<string>? Opened;

    internal void RefreshDuration()
    {
        if (_engine is not { } engine) return;
        _seek.Duration = engine.DurationSeconds > 0 ? engine.DurationSeconds : double.PositiveInfinity;
        RefreshState();
    }

    private void SaveHistory(bool finished)
    {
        SaveQueue();
        if (_path is not { } path || HistoryPath?.Invoke() is not { } file || !Settings.KeepHistory) return;
        _history.Remember(path, _pendingResume ?? CurrentPosition(), finished, _seek.Duration, CurrentTracks());
        _history.Save(file);
    }

    private void OnFaulted(object? sender, PlaybackFault fault)
        => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _engine)) return;
            var text = Strings.Get(fault.MessageKey);
            if (!string.IsNullOrEmpty(fault.MessageArg)) text += ": " + fault.MessageArg;
            _uyari = (LanguageCatalog.Display(text), _engine?.FramesRendered ?? 0);
            UyariyiGoster();
        });

    private void StartWatchdog()
    {
        _watchdog?.Stop();
        _watchdogTicks = 0;
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _watchdog.Tick += (_, _) => OnWatchdog();
        _watchdog.Start();
    }

    internal void OnWatchdog()
    {
        PollStall(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        RefreshInfo();
        if (!_playing) return;
        _watchdogTicks++;
        if (_watchdogTicks % HistorySaveTicks == 0) SaveHistory(false);
    }

    private async Task RunSeekAsync(double atSeconds)
    {
        var engine = _engine;
        if (engine is null) return;

        var result = await engine.SeekAsync(atSeconds, SeekPrecision.Exact).ConfigureAwait(false);
        if (result.Outcome is not (SeekOutcome.Failed or SeekOutcome.TimedOut)) return;

        void ShowFailure() => SeekFailed(engine, result.Outcome);

        if (Dispatcher.UIThread.CheckAccess()) ShowFailure();
        else Dispatcher.UIThread.Post(ShowFailure);
    }

    private void DrawFrame(IntPtr pixels, int width, int height, int stride)
    {
        var fresh = _bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height;
        if (fresh)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
        }

        using (var buffer = _bitmap!.Lock())
            FramePixels.CopyRows(pixels, stride, buffer.Address, buffer.RowBytes, Math.Min(height, buffer.Size.Height));

        if (fresh || !ReferenceEquals(Frame.Source, _bitmap))
        {
            _zoom.SetSource(width, height);
            Frame.Source = _bitmap;
            Resize();
        }
        Frame.InvalidateVisual();
        _drawn++;
        IlkGoruntu();
        if (AudioOnly) RaiseAudioCardOverCover();
        TxtEmpty.IsVisible = false;
        RefreshState();
    }

    /// <summary>
    /// Goruntuyu panonun bugunku olcusune sigdirir. Olcu iki yerden degisir: yeni kare
    /// gelince ve pano yeniden boyutlanınca. Ikisi de buraya girer, boylece pencere
    /// buyudugunde duraklatilmis goruntu de buyur.
    /// Oynaticida panel terfisi yok: Alt+teker her kademede goruntuyu buyutur, bu yuzden
    /// olcu <c>ZoomGesture.Scale</c>'den degil taban olcek (sigdir ya da doldur) carpi panel olceginden gelir.
    /// </summary>
    private void Resize()
    {
        if (Frame.Source is null) return;
        _zoom.SetViewport(Surface.Bounds.Width, Surface.Bounds.Height);
        var scale = _zoom.BaseScale * _zoom.PanelScale;
        Frame.Width = _zoom.SourceWidth * scale;
        Frame.Height = _zoom.SourceHeight * scale;
        Frame.InvalidateVisual();
    }

    private void OnSurfaceSize(object? sender, SizeChangedEventArgs e) => Resize();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshState();
        else Dispatcher.UIThread.Post(RefreshState);
    }

    internal void RefreshState()
    {
        OynatmaDurumunuYansit();
        if (TxtState is null) return;
        TxtState.Text = Strings.Get(
            "main.player.state",
            Saat.Konum(_seek.Target, Strings.Culture),
            _playing ? Strings.Get("main.player.playing") : Strings.Get("main.player.paused"),
            Bicim.Yuzde.Tam(_zoom.PanelScale, Strings.Culture));

        if (TxtControls is null) return;
        var parts = new List<string> { Strings.Get("main.player.volume", Bicim.Yuzde.HazirTam(_volume, Strings.Culture)) };
        if (_muted) parts.Add(Strings.Get("main.player.muted"));
        parts.Add(Strings.Get("main.player.speed", Bicim.Kat(_speed, Strings.Culture)));
        parts.Add(LoopText());
        parts.Add(Strings.Get("main.player.bookmarkcount", _path is null ? 0 : _history.Bookmarks(_path).Count));
        AppendTrackState(parts);
        AppendAdvancedState(parts);
        TxtControls.Text = string.Join(" - ", parts);
        RefreshWindowState();
    }

    internal void Close()
    {
        SaveHistory(false);
        ClosePreview();
        _watchdog?.Stop();
        _watchdog = null;
        _render?.Stop();
        _stall.Reset();
        KurtarmayiSifirla();
        HideAudioCard();
        HideMediaTitle();
        if (_engine is { } engine)
        {
            engine.Faulted -= OnFaulted;
            engine.NeighborRequested -= OnNeighborRequested;
            engine.Dispose();
        }

        _engine = null;
        _shown = 0;
        _path = null;
        _playing = false;
        HideResumePrompt();
        _trackPaused = false;
        OynatmaDurumunuYansit();
    }
}
