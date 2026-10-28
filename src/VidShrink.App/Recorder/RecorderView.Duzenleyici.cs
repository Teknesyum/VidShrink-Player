using System;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App.Recorder;

internal enum RegionEditorState
{
    Closed,
    Hidden,
    Shown
}

internal partial class RecorderView
{
    private IRegionEditorHost _regionEditor = new RegionEditorHost();
    private bool _regionEditorWired;
    private WindowState? _stateBeforeEditor;

    /// <summary>
    /// Çizimden sonra bölgeyi ekranda tutan düzenleyici. Yalnız Windows'ta açılıyor; öteki
    /// sistemlerde pencere biçimi yok, iç alan tıklamayı geçiremez ve masaüstü kilitlenirdi.
    /// </summary>
    internal bool RegionEditorEnabled { get; set; } = OperatingSystem.IsWindows();

    internal IRegionEditorHost RegionEditor
    {
        get => _regionEditor;
        set
        {
            if (_regionEditorWired) Unwire(_regionEditor);
            _regionEditor = value;
            _regionEditorWired = false;
            _regionEditor.HideKey = HotkeyName(HotkeyAction.Frame);
            WireRegionEditor();
        }
    }

    internal bool EditingRegion { get; private set; }

    private void WireRegionEditor()
    {
        if (_regionEditorWired) return;
        _regionEditor.Changed += OnEditorChanged;
        _regionEditor.Committed += OnEditorCommitted;
        _regionEditor.StartRequested += OnEditorStart;
        _regionEditor.SettingsRequested += OnEditorSettings;
        _regionEditor.PauseRequested += OnEditorPause;
        _regionEditor.ResumeRequested += OnEditorResume;
        _regionEditor.StopRequested += OnEditorStop;
        _regionEditor.SnapshotRequested += OnEditorSnapshot;
        _regionEditor.HideRequested += OnEditorHide;
        _regionEditor.Dismissed += OnEditorDismissed;
        _regionEditor.CameraCommitted += OnCameraCommitted;
        _regionEditorWired = true;
    }

    private void Unwire(IRegionEditorHost host)
    {
        host.Changed -= OnEditorChanged;
        host.Committed -= OnEditorCommitted;
        host.StartRequested -= OnEditorStart;
        host.SettingsRequested -= OnEditorSettings;
        host.PauseRequested -= OnEditorPause;
        host.ResumeRequested -= OnEditorResume;
        host.StopRequested -= OnEditorStop;
        host.SnapshotRequested -= OnEditorSnapshot;
        host.HideRequested -= OnEditorHide;
        host.Dismissed -= OnEditorDismissed;
        host.CameraCommitted -= OnCameraCommitted;
    }

    private void InitDuzenleyici()
    {
        WireRegionEditor();
        CmbTarget.SelectionChanged += (_, _) => SyncRegionEditor();
        CmbAspect.SelectionChanged += (_, _) => SyncRegionEditor();
        foreach (var box in new[] { TxtRegionX, TxtRegionY, TxtRegionWidth, TxtRegionHeight })
            box.TextChanged += (_, _) =>
            {
                if (_quiet == 0) SyncRegionEditor();
            };
        BtnDrawMask.Click += async (_, _) => await DrawMaskAsync();
        BtnClearMasks.Click += (_, _) => ClearMasks();
        RefreshMasks();
    }

    /// <summary>
    /// Gizlilik maskesi çizer: ana pencere küçülür, seçici serbest oranla açılır, çizilen
    /// dikdörtgen masaüstü pikselinde maskelere eklenip hemen kaydedilir.
    /// </summary>
    internal async Task<bool> DrawMaskAsync()
    {
        if (_session is not null || CountingDown) return false;

        var host = TopLevel.GetTopLevel(this) is Window { IsVisible: true } shown ? shown : null;
        var state = host?.WindowState ?? WindowState.Normal;
        if (host is not null) host.WindowState = WindowState.Minimized;

        PixelRect? drawn = null;
        try { drawn = await DrawRegion(null); }
        finally
        {
            if (host is not null) host.WindowState = state;
        }

        if (drawn is not { } rect || !RegionDraw.Usable(rect)) return false;

        _settings.PrivacyMasks.Add(new RecorderRegion(rect.X, rect.Y, rect.Width, rect.Height));
        SaveChoicesIfChanged();
        RefreshMasks();
        return true;
    }

    private void ClearMasks()
    {
        if (_session is not null || CountingDown) return;
        _settings.PrivacyMasks.Clear();
        SaveChoicesIfChanged();
        RefreshMasks();
    }

    private void RefreshMasks()
    {
        TxtMaskCount.Text = Say("recorder.mask.count", _settings.PrivacyMasks.Count);
        BtnClearMasks.IsEnabled = _settings.PrivacyMasks.Count > 0;
        ApplySade();
    }

    /// <summary>Kutulardaki bölge; okunamıyorsa ya da kullanılamayacak kadar küçükse <c>null</c>.</summary>
    internal PixelRect? RegionInBoxes()
    {
        static bool Read(TextBox box, out int value)
            => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        if (!Read(TxtRegionX, out var x) || !Read(TxtRegionY, out var y)
            || !Read(TxtRegionWidth, out var w) || !Read(TxtRegionHeight, out var h)) return null;
        var rect = new PixelRect(x, y, w, h);
        return RegionDraw.Usable(rect) ? rect : null;
    }

    private void WriteRegion(PixelRect rect)
    {
        rect = new PixelRect(rect.X, rect.Y, rect.Width - rect.Width % 2, rect.Height - rect.Height % 2);
        Quietly(() =>
        {
            TxtRegionX.Text = rect.X.ToString(CultureInfo.InvariantCulture);
            TxtRegionY.Text = rect.Y.ToString(CultureInfo.InvariantCulture);
            TxtRegionWidth.Text = rect.Width.ToString(CultureInfo.InvariantCulture);
            TxtRegionHeight.Text = rect.Height.ToString(CultureInfo.InvariantCulture);
        });
    }

    /// <summary>
    /// Düzenleyiciyi duruma uydurur: hedef bölge değilse kapanır; tampon sürerken gizlenir;
    /// geri sayım ve kayıt sürerken bölge kilitli, panelinde durdur (ve duraklat/sürdür)
    /// görünür; boştayken kutulardaki bölgeyle düzenlenir.
    /// </summary>
    internal void SyncRegionEditor()
    {
        var region = RegionInBoxes();
        switch (EditorWanted(EditingRegion, SelectedTarget == RecorderTargetKind.Region, region is not null, ReplayRunning,
                    FrameHiddenByUser && _session is not null))
        {
            case RegionEditorState.Shown:
                _regionEditor.ShowCamera(ChosenWebcam() is null ? null : ChosenPlacement, ChosenShape);
                _regionEditor.Show(region!.Value, RegionDraw.Ratio(SelectedAspect),
                    EditorPhase(_session is not null, State, CountingDown));
                break;
            case RegionEditorState.Hidden:
                _regionEditor.Hide();
                break;
            default:
                EditingRegion = false;
                if (_regionEditor.IsOpen) _regionEditor.Close();
                break;
        }
    }

    internal static RegionEditorState EditorWanted(bool editing, bool targetIsRegion, bool regionReadable, bool busy, bool hidden = false)
        => !editing || !targetIsRegion || !regionReadable ? RegionEditorState.Closed
            : busy || hidden ? RegionEditorState.Hidden
            : RegionEditorState.Shown;

    internal static RegionEditorPhase EditorPhase(bool recording, RecorderState state, bool countingDown)
        => countingDown ? RegionEditorPhase.Counting
            : !recording ? RegionEditorPhase.Idle
            : state == RecorderState.Paused ? RegionEditorPhase.Paused
            : RegionEditorPhase.Running;

    /// <summary>Düzenleyiciyi kapatır; ana pencereye dokunmaz (uygulama kapanışı, hedef değişimi).</summary>
    internal void CloseRegionEditor()
    {
        EditingRegion = false;
        _stateBeforeEditor = null;
        _regionEditor.Close();
    }

    private void OnEditorChanged(object? sender, PixelRect rect) => WriteRegion(rect);

    private void OnEditorCommitted(object? sender, PixelRect rect)
    {
        WriteRegion(rect);
        StoreChoices();
    }

    private async void OnEditorStart(object? sender, EventArgs e) => await StartAsync();

    private async void OnEditorPause(object? sender, EventArgs e) => await PauseAsync();

    private async void OnEditorResume(object? sender, EventArgs e) => await ResumeAsync();

    private async void OnEditorStop(object? sender, EventArgs e) => await StopAsync();

    private async void OnEditorSnapshot(object? sender, EventArgs e) => await SnapshotNowAsync();

    private void OnEditorHide(object? sender, EventArgs e) => ToggleFrame();

    private void OnEditorSettings(object? sender, EventArgs e) => RestoreHostWindow();

    private void OnEditorDismissed(object? sender, EventArgs e)
    {
        EditingRegion = false;
        RestoreHostWindow();
        _stateBeforeEditor = null;
    }

    /// <summary>Düzenleyici açılırken küçültülen ana pencereyi eski haline getirip öne alır.</summary>
    internal void RestoreHostWindow()
    {
        if (TopLevel.GetTopLevel(this) is not Window host) return;
        var state = _stateBeforeEditor ?? WindowState.Normal;
        host.WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
        host.Show();
        host.Activate();
    }
}
