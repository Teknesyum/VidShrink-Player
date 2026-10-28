using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using VidShrink.App.Localization;
using VidShrink.Core;

namespace VidShrink.App.Recorder;

internal readonly record struct RecorderPick(RecorderTargetKind Kind, PixelRect Rect, int Screen = 0, string? WindowTitle = null);

internal enum PickMode
{
    Region,
    Screen,
    Window
}

internal partial class RecorderRegionPicker
{
    private IReadOnlyList<(string Title, PixelRect Frame)> _windows = Array.Empty<(string, PixelRect)>();

    internal PickMode Mode { get; private set; } = PickMode.Region;

    private void InitModes(bool targets)
    {
        ModeBar.IsVisible = targets;
        BtnModeRegion.Click += (_, _) => SetMode(PickMode.Region);
        BtnModeScreen.Click += (_, _) => SetMode(PickMode.Screen);
        BtnModeWindow.Click += (_, _) => SetMode(PickMode.Window);
        ShowMode();
    }

    internal void SetMode(PickMode mode)
    {
        Mode = mode;
        _start = null;
        Selection.IsVisible = false;
        SizeTag.IsVisible = false;
        _snapLines = Array.Empty<PixelRect>();
        ShowSnapLines();
        if (mode == PickMode.Window)
            _windows = RecorderWindows.TitledFrames(Screens.All.Select(s => (s.Bounds, s.Scaling)).ToList());
        ShowMode();
    }

    private void ShowMode()
    {
        foreach (var (button, mode) in new[] { (BtnModeRegion, PickMode.Region), (BtnModeScreen, PickMode.Screen), (BtnModeWindow, PickMode.Window) })
            if (this.TryFindResource(mode == Mode ? "PrimaryButton" : "GhostButton", out var theme) && theme is ControlTheme ct)
                button.Theme = ct;

        TxtModeHint.Text = LanguageCatalog.Display(Strings.Get(Mode switch
        {
            PickMode.Screen => "recorder.picker.screen-hint",
            PickMode.Window => "recorder.picker.window-hint",
            _ => "recorder.target.region-draw-hint"
        }));
        PlaceModeBar();
    }

    private void PlaceModeBar()
    {
        if (!ModeBar.IsVisible) return;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null) return;
        ModeBar.Measure(Size.Infinity);
        var top = this.PointToClient(new PixelPoint(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y));
        var gap = this.TryFindResource("RecorderPickerBarGap", out var g) && g is double d ? d : 0;
        Canvas.SetLeft(ModeBar, top.X - ModeBar.DesiredSize.Width / 2);
        Canvas.SetTop(ModeBar, top.Y + gap);
    }

    internal static int? ScreenAt(IReadOnlyList<PixelRect> screens, PixelPoint point)
    {
        for (var i = 0; i < screens.Count; i++)
            if (RecorderWindows.Inside(screens[i], point)) return i;
        return null;
    }

    private RecorderPick? TargetAt(PixelPoint point)
    {
        if (Mode == PickMode.Screen)
        {
            var bounds = Screens.All.Select(s => s.Bounds).ToList();
            return ScreenAt(bounds, point) is { } index
                ? new RecorderPick(RecorderTargetKind.Screen, bounds[index], index)
                : null;
        }

        return RecorderWindows.At(_windows, point) is { } window
            ? new RecorderPick(RecorderTargetKind.Window, window.Frame, 0, window.Title)
            : null;
    }

    private void Hover(PixelPoint point)
    {
        if (TargetAt(point) is not { } pick)
        {
            Selection.IsVisible = false;
            SizeTag.IsVisible = false;
            return;
        }

        var size = Bicim.Cozunurluk(pick.Rect.Width, pick.Rect.Height);
        Outline(pick.Rect, pick.WindowTitle is { } title ? title + " · " + size : size);
    }
}
