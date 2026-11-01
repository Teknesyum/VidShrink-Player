using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Ffmpeg;
using VidShrink.Player;

namespace VidShrink.App.Editing;

internal partial class EditorView : UserControl
{
    private static readonly decimal[] SpeedPresets = { 0.5m, 1m, 2m };

    private readonly DispatcherTimer _clock;
    private EditTimeline? _model;
    private string? _source;
    private EdlPreviewDriver? _driver;
    private int _reloads;
    private CancellationTokenSource? _peaks;
    private TopLevel? _closingRoot;

    internal Func<string, CancellationToken, Task<AudioPeaks>>? PeakReader { get; set; }

    internal Task PeakLoad { get; private set; } = Task.CompletedTask;

    public EditorView()
    {
        InitializeComponent();
        Preview.KeyboardEnabled = false;
        Preview.SystemMediaControls = false;
        _clock = new DispatcherTimer { Interval = EdlPreviewDriver.DefaultInterval };
        _clock.Tick += (_, _) => Follow();

        Preview.FileDropped = path => _ = TakeFilesAsync(new[] { path });
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Timeline.Seek += OnSeek;
        Timeline.MoveRequested += (from, to) => Move(from, to);
        Timeline.SelectionChanged += RefreshToolbar;
        Timeline.Edited += () => Apply(_ => true, Timeline.SelectedIndex, reload: true);

        BtnUndo.Click += (_, _) => Undo();
        BtnRedo.Click += (_, _) => Redo();
        BtnZoomIn.Click += (_, _) => Timeline.ZoomCentered(true);
        BtnZoomOut.Click += (_, _) => Timeline.ZoomCentered(false);
        TxtSpeed.KeyDown += OnSpeedKey;
        InitExport();
        InitMonitor();
        InitText();
        InitClip();
        InitSilence();
        InitProject();
        InitSources();
        InitMusic();
        InitLayers();
        InitSound();
        InitStills();
        InitClipboard();
        InitProxy();

        MnuSplit.Click += (_, _) => Split();
        MnuDelete.Click += (_, _) => DeleteSelected();
        MnuMarkIn.Click += (_, _) => MarkIn();
        MnuMarkOut.Click += (_, _) => MarkOut();
        MnuDeleteRange.Click += (_, _) => DeleteRange();
        MnuReverse.Click += (_, _) => Reverse();
        TimelineMenu.Opening += (_, _) => BuildSpeedMenu();

        RefreshToolbar();
        ShowGestures();
    }

    internal EditTimeline? Model => _model;

    internal string? SourcePath => _source;

    internal EdlPreviewDriver? Driver => _driver;

    internal PlayerView Player => Preview;

    internal EditorTimeline TimelineView => Timeline;

    internal int Reloads => _reloads;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Height))
        {
            var rows = Layout.RowDefinitions;
            var splitter = double.IsFinite(PreviewSplitter.Height) ? PreviewSplitter.Height : 0;
            rows[2].MaxHeight = Math.Max(0, availableSize.Height - rows[0].MinHeight - splitter);
            LowerScroll.MaxHeight = rows[2].MaxHeight;
        }

        return base.MeasureOverride(availableSize);
    }

    internal void Activate(string? path)
    {
        _clock.Start();
        if (path is not null && !CurrentMedia.SamePath(path, _source)) _ = OpenSourceAsync(path);
    }

    internal void Deactivate()
    {
        _clock.Stop();
        _resumeClock = false;
        if (Preview.IsPlaying) Preview.TogglePlay();
        _driver?.Pause();
        PauseSound();
        FlushProject();
    }

    internal async Task EditAsync(string path, double startSeconds)
    {
        var start = double.IsFinite(startSeconds) && startSeconds > 0 ? EditTime.FromSeconds(startSeconds) : 0;
        if (!CurrentMedia.SamePath(path, _source))
        {
            await OpenSourceAsync(path, start).ConfigureAwait(true);
            return;
        }

        if (_model is not { } model) return;
        var at = Math.Clamp(start, 0, model.Duration);
        Timeline.Playhead = at;
        if (_driver is { } driver) await driver.SeekAsync(at).ConfigureAwait(true);
    }

    internal Task OpenSourceAsync(string path, long start = 0) => OpenSourceAsync(path, start, null);

    private async Task OpenSourceAsync(string path, long start, EditProject? project)
    {
        ForgetProject();
        CloseDriver();
        ForgetOverlay();
        ForgetBlends();
        ForgetSaved();
        ForgetSilence();
        ForgetSources();
        ForgetClipboard();
        ForgetAudioTracks();
        CancelProxy();
        _source = path;
        _model = null;
        Timeline.Show(null);
        RefreshToolbar();
        LoadPeaks(path);
        LoadStrip(path);

        try
        {
            await Preview.OpenAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PlaybackEngineUnavailableException or UnauthorizedAccessException)
        {
            return;
        }

        if (!CurrentMedia.SamePath(path, _source) || Preview.Engine is not { } engine || !(engine.DurationSeconds > 0)) return;
        CaptureAudioTracks(engine);
        ShowTimeline(OpenedTimeline(path, EditTime.FromSeconds(engine.DurationSeconds), project), SourceFps(KnownInfo?.Invoke(path)?.Fps, engine.FramesPerSecond));
        var at = Math.Clamp(start, 0, _model!.Duration);
        Timeline.Playhead = at;
        await ReloadAsync(at).ConfigureAwait(true);
    }

    private void LoadPeaks(string path)
    {
        _peaks?.Cancel();
        _peaks?.Dispose();
        _peaks = new CancellationTokenSource();
        Timeline.Peaks = null;
        PeakLoad = LoadPeaksAsync(path, 0, _peaks.Token);
    }

    private void WatchClose(TopLevel? root)
    {
        if (ReferenceEquals(root, _closingRoot)) return;
        if (_closingRoot is { } old) old.Closed -= OnRootClosed;
        _closingRoot = root;
        if (root is not null) root.Closed += OnRootClosed;
    }

    private void OnRootClosed(object? sender, EventArgs e)
    {
        WatchClose(null);
        _peaks?.Cancel();
        _keyframes?.Cancel();
        foreach (var scan in _extraScans) scan.Cancel();
        _proxyCts?.Cancel();
    }

    private async Task LoadPeaksAsync(string path, int index, CancellationToken ct)
    {
        var reader = PeakReader;
        if (reader is null)
        {
            if (!ToolLocator.IsAvailable(out _)) return;
            var ffmpeg = ToolLocator.Ffmpeg;
            reader = (source, token) => AudioPeaks.LoadAsync(ffmpeg, source, token);
        }

        AudioPeaks peaks;
        try
        {
            peaks = await Task.Run(() => reader(path, ct), ct).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return;
        }

        if (ct.IsCancellationRequested || !IsSource(index, path)) return;
        Timeline.SetPeaks(index, peaks);
    }

    internal void ShowTimeline(EditTimeline model, double fps)
    {
        _model = model;
        Timeline.Fps = fps;
        Timeline.Show(model);
        Timeline.SetMarkers(_pendingMarkers);
        _pendingMarkers = Array.Empty<EditMarker>();
        RefreshToolbar();
    }

    internal bool Split()
    {
        var at = Timeline.Playhead;
        return Apply(model => at > 0 && at < model.Duration && model.Split(at), -1);
    }

    internal bool DeleteSelected()
    {
        if (Timeline.SelectedText >= 0) return DeleteSelectedText();
        var all = Timeline.AllSelected;
        var selected = Timeline.SelectedIndices;
        if (selected.Count == 0) return false;
        return Apply(model =>
        {
            if (selected.Any(i => i < 0 || i >= model.Clips.Count)) return false;
            if (!all && model.WithGroups(selected).Count >= model.Clips.Count) return false;
            return model.DeleteMany(selected);
        }, Math.Max(0, selected.Min() - 1));
    }

    internal bool TrimToPlayhead(bool head)
    {
        var at = Timeline.Playhead;
        var index = Timeline.ClipAt(at);
        return Apply(model =>
        {
            if (index < 0 || index >= model.Clips.Count) return false;
            var start = model.ClipStart(index);
            if (!(head ? model.RippleTrimHead(at) : model.RippleTrimTail(at))) return false;
            if (head) Timeline.Playhead = start;
            return true;
        }, -1);
    }

    internal bool Extract() => DeleteRange();

    internal bool GoToMark(bool markIn)
    {
        if ((markIn ? Timeline.MarkIn : Timeline.MarkOut) is not { } at) return false;
        SeekTo(at);
        return true;
    }

    internal bool GoToEdit(bool next)
    {
        if ((next ? Timeline.NextEditPoint() : Timeline.PrevEditPoint()) is not { } at) return false;
        SeekTo(at);
        return true;
    }

    internal bool GoToKeyframe(bool next)
    {
        if (_model is not { } model) return false;
        var times = TimelineStrip.AllKeyframes(model, Timeline.KeyframeSources());
        if (TimelineStrip.NeighborKeyframe(times, Timeline.Playhead, next, Timeline.KeyframeTolerance) is not { } at) return false;
        SeekTo(at);
        return true;
    }

    internal bool DeleteRange()
    {
        if (Timeline.MarkIn is not { } start || Timeline.MarkOut is not { } end) return false;
        var done = Apply(model => end > start && end - start < model.Duration && model.DeleteRange(start, end), -1);
        if (!done) return false;
        Timeline.SetMarks(null, null);
        Timeline.Playhead = start;
        return true;
    }

    internal bool SetSpeed(decimal speed)
    {
        var selected = Timeline.SelectedIndex;
        if (Timeline.AllSelected) return Apply(model => ValidSpeed(speed) && model.SetSpeedAll(speed), -1);
        return Apply(model =>
        {
            if (selected < 0 || selected >= model.Clips.Count || speed == 0) return false;
            var magnitude = decimal.Round(Math.Abs(speed), 2, MidpointRounding.AwayFromZero);
            if (magnitude < EditClip.MinSpeed || magnitude > EditClip.MaxSpeed) return false;
            return model.SetSpeed(selected, speed);
        }, selected);
    }

    private static bool ValidSpeed(decimal speed)
    {
        if (speed == 0) return false;
        var magnitude = decimal.Round(Math.Abs(speed), 2, MidpointRounding.AwayFromZero);
        return magnitude >= EditClip.MinSpeed && magnitude <= EditClip.MaxSpeed;
    }

    internal bool Reverse()
    {
        var selected = ActiveIndex;
        if (_model is not { } model || selected < 0 || selected >= model.Clips.Count) return false;
        var clip = model.Clips[selected];
        return SetSpeed(clip.Reversed ? clip.Speed : -clip.Speed);
    }

    internal bool Move(int from, int to) => Apply(model => model.Move(from, to), to);

    internal bool Undo() => Apply(model => model.Undo(), Timeline.SelectedIndex);

    internal bool Redo() => Apply(model => model.Redo(), Timeline.SelectedIndex);

    internal void MarkIn() => Timeline.SetMarks(Timeline.Playhead, Timeline.MarkOut is { } end && end > Timeline.Playhead ? end : null);

    internal void MarkOut() => Timeline.SetMarks(Timeline.MarkIn is { } start && start < Timeline.Playhead ? start : null, Timeline.Playhead);

    private bool Apply(Func<EditTimeline, bool> edit, int select, bool reload = false)
    {
        if (_model is not { } model) return false;
        var before = model.Clips.ToArray();
        var frame = model.Frame;
        if (!edit(model)) return false;

        var at = Math.Min(Timeline.Playhead, model.Duration);
        Timeline.Refresh();
        if (select >= 0) Timeline.SelectedIndex = Math.Min(select, model.Clips.Count - 1);
        Timeline.Playhead = at;
        ShowTextPanel();
        ShowClipPanel();
        RefreshToolbar();
        if (reload || !SameCuts(before, model.Clips)) _ = ReloadAsync(at);
        else
        {
            if (!before.SequenceEqual(model.Clips) || frame != model.Frame) _ = RestyleAsync();
            RefreshOverlay();
        }

        MarkEdited();
        return true;
    }

    private async Task ReloadAsync(long at)
    {
        if (_model is not { } model || _source is null || Preview.Engine is not { } engine) return;
        if (model.Clips.Count == 0)
        {
            if (Preview.IsPlaying) Preview.TogglePlay();
            _driver?.Dispose();
            _driver = null;
            return;
        }

        var playing = Preview.IsPlaying;
        _driver?.Dispose();
        var preview = PreviewFor(model);
        var driver = new EdlPreviewDriver(engine, preview, look: LookFor(preview, engine));
        _driver = driver;
        _reloads++;
        try
        {
            await driver.OpenAsync().ConfigureAwait(true);
            if (!ReferenceEquals(_driver, driver)) return;
            ApplyAudioTrack(engine);
            ApplyTrackSound(engine);
            RefreshOverlay(reopened: true);
            Preview.RefreshDuration();
            await driver.SeekAsync(Math.Clamp(at, 0, model.Duration)).ConfigureAwait(true);
            if (playing) driver.Play();
            QueueBlends(model);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    internal void Follow()
    {
        if (_driver is not { } driver)
        {
            PauseSound();
            return;
        }

        SyncDriver();
        SyncMusic(driver);
        RefreshMeter();
        if (Timeline.Scrubbing) return;
        Timeline.Playhead = driver.TimelinePosition;
        if (driver.Playing) Timeline.FollowPlayhead();
        RefreshMonitor();
    }

    private IPlaybackEngine? _meteredEngine;
    private bool _meterOn;

    /// <summary>
    /// Ses duzeyi gostergesi: motor degisince olceri acar, sonra her izlemede son tepe degeri (dBFS)
    /// gostergenin -60..0 araligina kirpilarak yazilir. Motor olcemiyorsa gosterge gizli kalir.
    /// </summary>
    private void RefreshMeter()
    {
        var engine = Preview.Engine;
        if (!ReferenceEquals(engine, _meteredEngine))
        {
            _meteredEngine = engine;
            _meterOn = engine?.MeterAudio(true) == true;
        }

        LevelMeter.IsVisible = _meterOn;
        if (!_meterOn || engine is null) return;
        var peak = engine.AudioPeakDb;
        LevelMeter.Value = double.IsNaN(peak) ? LevelMeter.Minimum : Math.Clamp(peak, LevelMeter.Minimum, LevelMeter.Maximum);
    }

    private void OnSeek(long time, bool final)
    {
        if (_driver is not { } driver) return;
        _ = driver.SeekAsync(time, final ? SeekPrecision.Exact : SeekPrecision.Keyframe);
    }

    private void OnSpeedKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (decimal.TryParse(TxtSpeed.Text, NumberStyles.Number, Strings.Culture, out var speed)
            || decimal.TryParse(TxtSpeed.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out speed))
            SetSpeed(speed);
        RefreshToolbar();
        e.Handled = true;
    }

    private void BuildSpeedMenu()
    {
        MnuSpeed.Items.Clear();
        foreach (var preset in SpeedPresets)
        {
            var value = preset;
            var item = new MenuItem { Header = Bicim.Kat((double)value, Strings.Culture) + "×" };
            item.Click += (_, _) => SetSpeed(Reversed() ? -value : value);
            MnuSpeed.Items.Add(item);
        }

        AddRampItems();

        var hasClip = ActiveIndex >= 0;
        MnuSpeed.IsEnabled = hasClip;
        MnuReverse.IsEnabled = hasClip;
        MnuDelete.IsEnabled = _canDelete;
        MnuSplit.IsEnabled = BtnSplit.IsEnabled;
        MnuAddSource.IsEnabled = BtnAddSource.IsEnabled;
        MnuDeleteRange.IsEnabled = Timeline.MarkIn is { } a && Timeline.MarkOut is { } b && b > a;
    }

    private void AddRampItems()
    {
        var index = ActiveIndex;
        if (_model is not { } model || index < 0 || index >= model.Clips.Count) return;
        var clip = model.Clips[index];
        var fast = clip.Speed;
        var slow = Math.Max(EditClip.MinSpeed, decimal.Round(fast / SpeedRamp.DefaultFactor, 2, MidpointRounding.AwayFromZero));
        string Range(decimal a, decimal b) => " (" + Bicim.Kat((double)a, Strings.Culture) + "× → " + Bicim.Kat((double)b, Strings.Culture) + "×)";

        MnuSpeed.Items.Add(new Separator());
        var down = new MenuItem { Header = Strings.Get("editor.speed-ramp-down") + Range(fast, slow), IsEnabled = fast != slow };
        down.Click += (_, _) => RampSpeed(fast, slow);
        MnuSpeed.Items.Add(down);
        var up = new MenuItem { Header = Strings.Get("editor.speed-ramp-up") + Range(slow, fast), IsEnabled = fast != slow };
        up.Click += (_, _) => RampSpeed(slow, fast);
        MnuSpeed.Items.Add(up);
        var smooth = new MenuItem { Header = Strings.Get("editor.smooth-slow"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = clip.Smooth };
        smooth.Click += (_, _) => SetSmooth(!clip.Smooth);
        MnuSpeed.Items.Add(smooth);
    }

    internal bool RampSpeed(decimal from, decimal to)
    {
        var index = ActiveIndex;
        return Apply(model => index >= 0 && index < model.Clips.Count && model.RampSpeed(index, from, to), index);
    }

    internal bool SetSmooth(bool smooth)
    {
        var targets = ClipTargets();
        return Apply(model => targets.Count > 0 && model.SetSmooth(targets, smooth), -1);
    }

    internal int ActiveIndex => Timeline.AllSelected && _model is { Clips.Count: > 0 } ? 0 : Timeline.SelectedIndex;

    private bool Reversed()
    {
        var selected = ActiveIndex;
        return _model is { } model && selected >= 0 && selected < model.Clips.Count && model.Clips[selected].Reversed;
    }

    private void RefreshToolbar()
    {
        var model = _model;
        var selected = ActiveIndex;
        var hasClip = model is not null && selected >= 0 && selected < model.Clips.Count;
        BtnSplit.IsEnabled = model is not null;
        BtnAddSource.IsEnabled = model is not null && !Exporting;
        RefreshMedia();
        var chosen = Timeline.SelectedIndices.Count;
        _canDelete = model is { Clips.Count: > 0 } && chosen > 0 && (Timeline.AllSelected || chosen < model.Clips.Count);
        ShowFrame();
        BtnUndo.IsEnabled = model?.CanUndo ?? false;
        BtnRedo.IsEnabled = model?.CanRedo ?? false;
        BtnZoomIn.IsEnabled = model is not null;
        BtnZoomOut.IsEnabled = model is not null;
        BtnZoomFit.IsEnabled = model is not null;
        TxtSpeed.IsEnabled = hasClip;
        if (hasClip)
        {
            var clip = model!.Clips[selected];
            var speed = clip.Reversed ? -clip.Speed : clip.Speed;
            TxtSpeed.Text = Bicim.Kat((double)speed, Strings.Culture);
        }
        else
        {
            TxtSpeed.Text = string.Empty;
        }

        RefreshExport();
        RefreshProxy();
        RefreshMonitor();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = PoolSource(e) is not null ? (_model is null ? DragDropEffects.None : DragDropEffects.Copy)
            : DroppedFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (PoolSource(e) is { } source)
        {
            DropOnTimeline(e, source);
            return;
        }

        var files = DroppedFiles(e);
        if (files.Count > 0) _ = TakeFilesAsync(files);
    }

    private static IReadOnlyList<string> DroppedFiles(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList();
        if (items is null || items.Count == 0 || items.Any(item => item is IStorageFolder)) return Array.Empty<string>();
        var paths = items.Select(item => item.TryGetLocalPath()).ToArray();
        return paths.All(path => path is not null && File.Exists(path)) ? paths!.ToArray<string>() : Array.Empty<string>();
    }

    private void CloseDriver()
    {
        _driver?.Dispose();
        _driver = null;
    }
}
