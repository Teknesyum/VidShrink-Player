using System;
using Avalonia.Controls;
using Avalonia.Styling;
using VidShrink.Core;

namespace VidShrink.App.Recorder;

internal partial class RecorderView
{
    internal event Action? LevelChanged;

    private bool _sadeHazir;

    private void InitSade()
    {
        BtnQuickScreen.Click += (_, _) => QuickTarget(RecorderTargetKind.Screen);
        BtnQuickWindow.Click += (_, _) => QuickTarget(RecorderTargetKind.Window);
        BtnQuickRegion.Click += async (_, _) => await DrawRegionAsync();
        CmbTarget.SelectionChanged += (_, _) => ApplySade();
        CmbWebcam.SelectionChanged += (_, _) => ApplySade();
        ChkPushToTalk.IsCheckedChanged += (_, _) => ApplySade();
        _sadeHazir = true;
        ApplySade();
    }

    private void QuickTarget(RecorderTargetKind kind)
    {
        if (_session is not null || CountingDown) return;
        CmbTarget.SelectedIndex = (int)kind;
        StoreChoices();
    }

    private void ApplySade()
    {
        if (!_sadeHazir) return;
        var advanced = AdvancedMode;
        var target = SelectedTarget;
        var quick = target is RecorderTargetKind.Screen or RecorderTargetKind.Window or RecorderTargetKind.Region;

        RowTargetQuick.IsVisible = !advanced;
        RowTargetKind.IsVisible = advanced || !quick;
        RowWindowTitle.IsVisible = advanced;
        RowRegionShape.IsVisible = advanced;
        RowRegion.IsVisible = advanced && target == RecorderTargetKind.Region;
        RowMask.IsVisible = advanced || _settings.PrivacyMasks.Count > 0;
        RowTiming.IsVisible = advanced;
        StatFrames.IsVisible = advanced;
        StatDropped.IsVisible = advanced;
        BtnPrompter.IsVisible = advanced;
        ChkPushToTalk.IsVisible = advanced || ChkPushToTalk.IsChecked == true;
        PanelWebcam.IsVisible = advanced || CmbWebcam.SelectedIndex > 0;
        BtnAutoWizard.IsVisible = advanced && BtnStart.IsVisible;

        foreach (var (button, kind) in new[] { (BtnQuickScreen, RecorderTargetKind.Screen), (BtnQuickWindow, RecorderTargetKind.Window), (BtnQuickRegion, RecorderTargetKind.Region) })
            if (this.TryFindResource(kind == target ? "AccentButton" : "GhostButton", out var theme) && theme is ControlTheme ct)
                button.Theme = ct;
    }

    private void RaiseLevelChanged() => LevelChanged?.Invoke();
}
