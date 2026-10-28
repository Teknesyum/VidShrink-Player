using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VidShrink.Core;

namespace VidShrink.App.Recorder;

internal partial class RecorderRegionPicker : Window
{
    private readonly TaskCompletionSource<RecorderPick?> _done = new();
    internal Task<RecorderPick?> Result => _done.Task;
    private readonly double? _ratio;
    private PixelRect _desktop;
    private PixelPoint? _start;
    private PixelRect _current;
    private IReadOnlyList<PixelRect> _targets = Array.Empty<PixelRect>();
    private IReadOnlyList<PixelRect> _snapLines = Array.Empty<PixelRect>();
    private bool _snapOff;

    public RecorderRegionPicker() : this(null)
    {
    }

    internal RecorderRegionPicker(double? ratio, bool targets = false)
    {
        _ratio = ratio;
        InitializeComponent();
        Opened += (_, _) => Cover();
        Closed += (_, _) => _done.TrySetResult(null);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            _done.TrySetResult(null);
            Close();
        };
        InitModes(targets);
    }

    internal Task<RecorderPick?> Picked => _done.Task;

    internal static async Task<PixelRect?> PickAsync(double? ratio)
        => await ShowAsync(ratio, targets: false) is { Kind: RecorderTargetKind.Region } pick ? pick.Rect : null;

    internal static Task<RecorderPick?> PickTargetAsync(double? ratio) => ShowAsync(ratio, targets: true);

    private static async Task<RecorderPick?> ShowAsync(double? ratio, bool targets)
    {
        var frozen = await Freeze(CancellationToken.None);
        var picker = new RecorderRegionPicker(ratio, targets) { Frozen = frozen };
        picker.Closed += (_, _) => frozen?.Dispose();
        picker.Show();
        picker.Activate();
        return await picker.Picked;
    }

    private void Cover()
    {
        _desktop = Cover(this);
        ApplyFrozen();
        Scrim.Width = Width;
        Scrim.Height = Height;
        PlaceModeBar();
    }

    /// <summary>Pencereyi bütün masaüstünü kaplayacak yere ve boya koyar; masaüstünü piksel olarak döndürür.</summary>
    internal static PixelRect Cover(Window window)
    {
        var screens = window.Screens;
        var placements = screens.All
            .Select((s, i) => new ScreenPlacement(
                new ScreenBounds(i, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling))
            .ToList();

        var desktop = RegionDraw.Desktop(screens.All.Select(s => s.Bounds));
        var cover = RecorderLayout.Cover(placements);
        var yedekOlcek = screens.Primary?.Scaling ?? 1;

        window.Position = cover is null ? desktop.Position : new PixelPoint(cover.X, cover.Y);
        window.Width = cover?.Width ?? desktop.Width / yedekOlcek;
        window.Height = cover?.Height ?? desktop.Height / yedekOlcek;
        return desktop;
    }

    private PixelPoint ScreenPoint(PointerEventArgs e) => this.PointToScreen(e.GetPosition(this));

    private int Pixels(string key)
        => this.TryFindResource(key, out var value) && value is double d ? Math.Max(1, (int)Math.Ceiling(d * RenderScaling)) : 0;

    internal IReadOnlyList<PixelRect> SnapLines => _snapLines;

    internal static IReadOnlyList<PixelRect> SnapTargets(Window window)
    {
        var screens = window.Screens.All.Select(s => (s.Bounds, s.Scaling)).ToList();
        return RegionSnap.Targets(screens.Select(s => s.Bounds), RecorderWindows.SnapFrames(screens));
    }

    private bool SnapOff(PointerEventArgs e) => _snapOff = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (Mode != PickMode.Region)
        {
            if (TargetAt(ScreenPoint(e)) is not { } pick) return;
            _done.TrySetResult(pick);
            Close();
            return;
        }

        _targets = SnapTargets(this);
        _start = RegionSnap.Point(ScreenPoint(e), _targets, Pixels("RecorderRegionSnap"), SnapOff(e)).Point;
        e.Pointer.Capture(Surface);
        Draw(_start.Value);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (Mode != PickMode.Region)
        {
            Hover(ScreenPoint(e));
            return;
        }

        if (_start is null) return;
        SnapOff(e);
        Draw(ScreenPoint(e));
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_start is null) return;
        SnapOff(e);
        Draw(ScreenPoint(e));
        _start = null;
        _snapLines = Array.Empty<PixelRect>();
        ShowSnapLines();
        e.Pointer.Capture(null);
        if (!RegionDraw.Usable(_current)) return;
        _done.TrySetResult(new RecorderPick(RecorderTargetKind.Region, _current));
        Close();
    }

    private void Draw(PixelPoint end)
    {
        if (_start is not { } start) return;
        var snap = RegionSnap.Point(end, _targets, Pixels("RecorderRegionSnap"), _snapOff);
        _current = RegionDraw.FromDrag(start, snap.Point, _ratio, _desktop);
        _snapLines = RegionSnap.Lines(_current, snap.X, snap.Y, Pixels("RecorderRegionSnapLine"));
        ShowSnapLines();
        Outline(_current, Bicim.Cozunurluk(_current.Width, _current.Height));
    }

    private void Outline(PixelRect rect, string label)
    {
        var topLeft = this.PointToClient(rect.Position);
        var bottomRight = this.PointToClient(rect.BottomRight);
        Canvas.SetLeft(Selection, topLeft.X);
        Canvas.SetTop(Selection, topLeft.Y);
        Selection.Width = bottomRight.X - topLeft.X;
        Selection.Height = bottomRight.Y - topLeft.Y;
        Selection.IsVisible = true;
        TxtSize.Text = label;
        SizeTag.IsVisible = true;
        SizeTag.Measure(Size.Infinity);
        var yer = TagPosition(new Rect(topLeft, bottomRight), SizeTag.DesiredSize, Bounds.Size);
        Canvas.SetLeft(SizeTag, yer.X);
        Canvas.SetTop(SizeTag, yer.Y);
    }

    private void ShowSnapLines()
    {
        var controls = new Control[] { SnapX, SnapY };
        for (var i = 0; i < controls.Length; i++)
        {
            var show = i < _snapLines.Count;
            controls[i].IsVisible = show;
            if (!show) continue;
            var topLeft = this.PointToClient(_snapLines[i].Position);
            var bottomRight = this.PointToClient(_snapLines[i].BottomRight);
            Canvas.SetLeft(controls[i], topLeft.X);
            Canvas.SetTop(controls[i], topLeft.Y);
            controls[i].Width = Math.Max(0, bottomRight.X - topLeft.X);
            controls[i].Height = Math.Max(0, bottomRight.Y - topLeft.Y);
        }
    }

    /// <summary>
    /// Ölçü etiketi seçimin altına konur; altta yer yoksa seçimin üstüne, orada da yoksa
    /// ekranın üst kenarına. Yatayda ekranın içinde tutulur.
    /// </summary>
    internal static Point TagPosition(Rect selection, Size tag, Size area)
    {
        var y = selection.Bottom + tag.Height <= area.Height
            ? selection.Bottom
            : Math.Max(0, selection.Top - tag.Height);
        var x = Math.Clamp(selection.Left, 0, Math.Max(0, area.Width - tag.Width));
        return new Point(x, y);
    }
}
