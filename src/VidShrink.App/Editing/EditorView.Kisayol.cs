using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VidShrink.App.Localization;
using VidShrink.App.Playback;
using VidShrink.Core;

namespace VidShrink.App.Editing;

internal partial class EditorView
{
    internal Func<string, MediaInfo?>? KnownInfo { get; set; }

    internal static double SourceFps(double? known, double engine)
    {
        if (known is { } fps && double.IsFinite(fps) && fps > 0) return fps;
        return double.IsFinite(engine) && engine > 0 ? engine : double.NaN;
    }

    internal void UseDriver(EdlPreviewDriver driver)
    {
        if (!ReferenceEquals(_driver, driver)) _driver?.Dispose();
        _driver = driver;
    }

    internal bool HandleKey(Key key, KeyModifiers modifiers) => Run(EditorKeymap.For(key, modifiers));

    internal bool Run(EditorCommand command)
    {
        if (command == EditorCommand.None || _model is not { } model) return false;
        switch (command)
        {
            case EditorCommand.PlayPause: PlayPause(); break;
            case EditorCommand.ShuttleBack: ShuttleBack(); break;
            case EditorCommand.ShuttleStop: ShuttleStop(); break;
            case EditorCommand.ShuttleForward: ShuttleForward(); break;
            case EditorCommand.FrameBack: StepFrame(-1); break;
            case EditorCommand.FrameForward: StepFrame(1); break;
            case EditorCommand.GoToStart: SeekTo(0); break;
            case EditorCommand.GoToEnd: SeekTo(model.Duration); break;
            case EditorCommand.MarkIn: MarkIn(); break;
            case EditorCommand.MarkOut: MarkOut(); break;
            case EditorCommand.AddMarker: Timeline.AddMarker(Timeline.Playhead); MarkEdited(); break;
            case EditorCommand.Split: Split(); break;
            case EditorCommand.DeleteSelected: DeleteSelected(); break;
            case EditorCommand.SelectAll: Timeline.SelectAll(); break;
            case EditorCommand.Undo: Undo(); break;
            case EditorCommand.Redo: Redo(); break;
            case EditorCommand.ToggleSnap: Timeline.SnapEnabled = !Timeline.SnapEnabled; break;
            case EditorCommand.TrimHead: TrimToPlayhead(true); break;
            case EditorCommand.TrimTail: TrimToPlayhead(false); break;
            case EditorCommand.PrevEdit: GoToEdit(false); break;
            case EditorCommand.NextEdit: GoToEdit(true); break;
            case EditorCommand.PrevKeyframe: GoToKeyframe(false); break;
            case EditorCommand.NextKeyframe: GoToKeyframe(true); break;
            case EditorCommand.Back5: StepFrame(-5); break;
            case EditorCommand.Forward5: StepFrame(5); break;
            case EditorCommand.ZoomIn: Timeline.ZoomCentered(true); break;
            case EditorCommand.ZoomOut: Timeline.ZoomCentered(false); break;
            case EditorCommand.ZoomFit: Timeline.ZoomToFit(); break;
            case EditorCommand.GoToIn: GoToMark(true); break;
            case EditorCommand.GoToOut: GoToMark(false); break;
            case EditorCommand.FocusSpeed: FocusSpeed(); break;
            case EditorCommand.Extract: Extract(); break;
            case EditorCommand.ToolSelection: UseTool(EditorTool.Selection); break;
            case EditorCommand.ToolRazor: UseTool(EditorTool.Razor); break;
            case EditorCommand.ToolRipple: UseTool(EditorTool.Ripple); break;
            case EditorCommand.Export: RequestExport(); break;
            case EditorCommand.AddText: AddText(); break;
            case EditorCommand.Copy: CopySelected(); break;
            case EditorCommand.Cut: CutSelected(); break;
            case EditorCommand.Paste: PasteClipboard(); break;
            case EditorCommand.Duplicate: DuplicateSelected(); break;
            case EditorCommand.GroupClips: GroupSelected(); break;
            case EditorCommand.UngroupClips: UngroupSelected(); break;
            default: return false;
        }

        RefreshMonitor();
        return true;
    }

    private void PlayPause()
    {
        if (_driver is { Backward: true, Playing: true })
        {
            ShuttleStop();
            return;
        }

        if (_driver is { } driver && Math.Abs(driver.Rate - 1) > 1e-9) driver.SetRate(1);
        Preview.TogglePlay();
        SyncDriver();
    }

    private void ShuttleForward()
    {
        if (_driver is not { } driver) return;
        var rate = driver.Shuttle > 0 ? Math.Min(EditorKeymap.MaxShuttle, driver.Rate * 2) : 1;
        driver.SetRate(rate);
        if (driver.Backward || !driver.Playing) driver.Play();
        if (!Preview.IsPlaying) Preview.TogglePlay();
    }

    private void ShuttleBack()
    {
        if (_driver is not { } driver) return;
        var rate = driver.Shuttle < 0 ? Math.Min(EditorKeymap.MaxShuttle, driver.Rate * 2) : 1;
        if (Preview.IsPlaying) Preview.TogglePlay();
        driver.PlayBackward();
        driver.SetRate(rate);
    }

    private void ShuttleStop()
    {
        if (Preview.IsPlaying) Preview.TogglePlay();
        if (_driver is not { } driver) return;
        driver.Pause();
        driver.SetRate(1);
    }

    private void StepFrame(int direction)
    {
        ShuttleStop();
        SeekTo(Timeline.RoundToFrame(Timeline.Playhead + (long)Math.Round(direction * Timeline.FrameTicks)));
    }

    private void FocusSpeed()
    {
        if (!TxtSpeed.IsEnabled) return;
        TxtSpeed.Focus();
        TxtSpeed.SelectAll();
    }

    private void SeekTo(long time)
    {
        Timeline.Playhead = time;
        if (_driver is { } driver) _ = driver.SeekAsync(Timeline.Playhead);
        RefreshMonitor();
    }

    private void SyncDriver()
    {
        if (_driver is not { } driver) return;
        if (driver.Backward)
        {
            if (Preview.IsPlaying) driver.Play();
            return;
        }

        if (Preview.IsPlaying && !driver.Playing) driver.Play();
        else if (!Preview.IsPlaying && driver.Playing) driver.Pause();
    }

    private void ShowGestures()
    {
        MnuSplit.InputGesture = Gesture(EditorCommand.Split);
        MnuDelete.InputGesture = Gesture(EditorCommand.DeleteSelected);
        MnuMarkIn.InputGesture = Gesture(EditorCommand.MarkIn);
        MnuMarkOut.InputGesture = Gesture(EditorCommand.MarkOut);
        MnuCopy.InputGesture = Gesture(EditorCommand.Copy);
        MnuCut.InputGesture = Gesture(EditorCommand.Cut);
        MnuPaste.InputGesture = Gesture(EditorCommand.Paste);
        MnuDuplicate.InputGesture = Gesture(EditorCommand.Duplicate);
        MnuGroup.InputGesture = Gesture(EditorCommand.GroupClips);
        MnuUngroup.InputGesture = Gesture(EditorCommand.UngroupClips);
        ToolTip.SetTip(BtnSplit, Tip("editor.split", EditorCommand.Split));
        ToolTip.SetTip(BtnUndo, Tip("editor.undo", EditorCommand.Undo));
        ToolTip.SetTip(BtnRedo, Tip("editor.redo", EditorCommand.Redo));
        ToolTip.SetTip(BtnAddText, Tip("editor.text.add", EditorCommand.AddText));
        ShowMonitorTips();
        RefreshExport();
    }

    private static KeyGesture? Gesture(EditorCommand command)
        => EditorKeymap.FirstKey(command) is { } row ? new KeyGesture(row.Key, row.Modifiers) : null;

    internal static string Tip(string labelKey, EditorCommand command)
        => Keymap.Tip(Strings.Get(labelKey), EditorKeymap.First(command)?.Input);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_resumeClock) _clock.Start();
        Strings.Changed -= OnLanguageChanged;
        Strings.Changed += OnLanguageChanged;
        ShortcutBindings.Changed -= OnLanguageChanged;
        ShortcutBindings.Changed += OnLanguageChanged;
        ShowGestures();
        AttachProject();
        Timeline.ResumeStrip();
        WatchClose(TopLevel.GetTopLevel(this));
        if (TopLevel.GetTopLevel(this) is { } top) top.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        RemoveHandler(PointerPressedEvent, OnButtonPress);
        AddHandler(PointerPressedEvent, OnButtonPress, RoutingStrategies.Tunnel);
    }

    private bool _resumeClock;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _resumeClock = _clock.IsEnabled;
        _clock.Stop();
        MusicPlayer.Close();
        SoundPlayer.Close();
        FlushProject();
        DetachProject();
        ForgetBlends();
        Timeline.PauseStrip();
        Strings.Changed -= OnLanguageChanged;
        ShortcutBindings.Changed -= OnLanguageChanged;
        RemoveHandler(PointerPressedEvent, OnButtonPress);
        if (TopLevel.GetTopLevel(this) is { } top) top.RemoveHandler(KeyDownEvent, OnKey);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) ShowLanguage();
        else Avalonia.Threading.Dispatcher.UIThread.Post(ShowLanguage);
    }

    private void ShowLanguage()
    {
        ShowGestures();
        SyncAudioTrackControls();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEffectivelyVisible || e.Source is TextBox) return;
        if (HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
    }

    internal static PlayerButton? PressedButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.MiddleButtonPressed => PlayerButton.Middle,
        PointerUpdateKind.XButton1Pressed => PlayerButton.Back,
        PointerUpdateKind.XButton2Pressed => PlayerButton.Forward,
        _ => null
    };

    private void OnButtonPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || PressedButton(e.GetCurrentPoint(this).Properties.PointerUpdateKind) is not { } button) return;
        if (Run(EditorKeymap.ForPress(button))) e.Handled = true;
    }
}
