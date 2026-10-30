using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App.Recorder;

/// <summary>
/// Mini kip: kaydedici sekmesi TinyTask ölçüsünde bir şeride küçülüyor.
///
/// <para><b>Geçiş şeritten, ayardan değil.</b> ScreenToGif'in kompakt kipi Ayarlar
/// altındaki bir onay kutusu ve kullanıcı bulamazsa hiç kullanmıyor. Burada geçiş
/// şeridin kendi düğmesinde; tıkla-geç, tıkla-dön.</para>
///
/// <para><b>Sıcak tuşlar F7 ve F8.</b> ScreenToGif ve TinyTask'ın varsayılanı. Windows'ta
/// Oyun Çubuğu'nun <c>Win+Alt+R</c>'siyle çakışmıyor. Tanım büyük pencerede duruyor,
/// tuş mini kipte de çalışıyor: iki pencere de aynı <see cref="OnHotkey"/>'e bağlı.</para>
/// </summary>
internal partial class RecorderView
{
    private RecorderMini? _mini;

    /// <summary>Mini kip açık mı. Ölçüm kendi gördüğünü okuyabilsin diye açık.</summary>
    internal bool MiniOpen => _mini is not null;

    /// <summary>
    /// Şerit kayda girecek mi: kadrajda dışarısı yok (tam ekran ya da pencere kaydı) ve şerit
    /// yakalamadan çıkarılamadı. Çıkarıldıysa dışarısı olmasa da yanlış.
    /// </summary>
    internal bool MiniInFrame { get; private set; }

    internal static bool InFrame(PixelRect? region, bool captureExcluded) => region is null && !captureExcluded;

    private void InitMini()
    {
        BtnMini.Click += OnShrinkToMini;
        AddHandler(KeyDownEvent, OnHotkey, RoutingStrategies.Tunnel);
    }

    private async void OnHotkey(object? sender, KeyEventArgs e)
    {
        if (RecorderHotkeys.ActionOf(_hotkeys, e.Key, e.KeyModifiers) is not { } action || !CanRun(action)) return;
        e.Handled = true;
        await RunHotkeyAsync(action);
    }

    /// <summary>
    /// Tek düğmeye indirilmiş başlat/duraklat/sürdür. Saha taramasındaki 22 kompakt
    /// yüzeyin hepsinde bu üçü tek sütunu paylaşıyor: yarısı hep boş duran ikinci bir
    /// 40 piksel ayırmanın karşılığı yok.
    /// </summary>
    internal async System.Threading.Tasks.Task ToggleAsync()
    {
        if (CountingDown)
        {
            CancelCountdown();
            return;
        }

        switch (State)
        {
            case RecorderState.Running when HasSession: await PauseAsync(); break;
            case RecorderState.Paused when HasSession: await ResumeAsync(); break;
            default: await StartAsync(); break;
        }
    }

    private void OnShrinkToMini(object? sender, RoutedEventArgs e) => ShrinkToMini();

    /// <summary>
    /// Mini kipi açar ve ana pencereyi gizler. Şerit kadrajın dışına konumlanıyor;
    /// bölge tam ekransa dışarısı yok ve şerit yakalamadan da çıkarılamadıysa kullanıcıya
    /// şeridin kayda gireceği söyleniyor.
    /// </summary>
    internal void ShrinkToMini()
    {
        if (_mini is not null) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        _mini = new RecorderMini();
        _mini.ToggleRequested += async (_, _) => await ToggleAsync();
        _mini.StopRequested += async (_, _) => await StopAsync();
        _mini.ExpandRequested += (_, _) => ExpandFromMini();
        _mini.RegionRequested += async (_, _) => await DrawFromMiniAsync();
        _mini.OptionChanged += (_, option) => ApplyMiniOption(option);
        _mini.Closed += (_, _) => ExpandFromMini();
        _mini.AddHandler(KeyDownEvent, OnHotkey, RoutingStrategies.Tunnel);

        _mini.Show();

        var region = FrameRegion();
        MiniInFrame = InFrame(region, _mini.CaptureExcluded);
        if (MiniInFrame) ShowError(Say("recorder.mini.in-frame"));

        _mini.PlaceOutside(region);
        RefreshMini();

        owner?.Hide();
    }

    /// <summary>
    /// Şeritten bölge çizimi: şerit çizim sürerken saklanıyor, sonra yeni kadrajın dışına
    /// yeniden konuyor. Ana pencere gizli kalıyor.
    /// </summary>
    internal async System.Threading.Tasks.Task<bool> DrawFromMiniAsync()
    {
        var mini = _mini;
        if (mini is null) return false;

        mini.Hide();
        try { return await ChooseTargetAsync(); }
        finally
        {
            if (ReferenceEquals(_mini, mini))
            {
                mini.Show();
                var region = FrameRegion();
                MiniInFrame = InFrame(region, mini.CaptureExcluded);
                mini.PlaceOutside(region);
            }
        }
    }

    /// <summary>Mini kipi kapatır ve ana pencereyi geri getirir.</summary>
    internal void ExpandFromMini()
    {
        var mini = _mini;
        if (mini is null) return;

        _mini = null;
        MiniInFrame = false;

        mini.RemoveHandler(KeyDownEvent, OnHotkey);
        mini.Close();

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            owner.Show();
            owner.Activate();
        }
    }

    /// <summary>
    /// Şeridin dışına konacağı kadraj. Bölge seçilmemişse — yani tam ekran ya da pencere
    /// kaydı — dışarısı yok ve <c>null</c> dönüyor.
    /// </summary>
    private PixelRect? FrameRegion()
        => BuildRequest(applyAuto: false) is { Region: { } r }
            ? new PixelRect(r.X, r.Y, r.Width, r.Height)
            : null;

    private void RefreshMini()
    {
        if (_mini is null) return;
        _mini.Follow(State, TxtElapsed.Text ?? string.Empty, CountdownLeft);
        _mini.ShowChapter(ChapterText);
        _mini.ShowOptions(
            ChkShowClicks.IsChecked ?? false,
            ChkClickSound.IsChecked ?? false,
            ChkShowKeys.IsChecked ?? false,
            ChkOpenFolder.IsChecked ?? false,
            ChkCursor.IsChecked ?? false,
            _session is not null || CountingDown,
            ChkMagnifier.IsChecked ?? false);
        _mini.LimitOptions(ChkShowClicks.IsVisible, HooksAvailable);
    }

    internal RecorderMini? Mini => _mini;

    internal void ApplyMiniOption(MiniOption option)
    {
        var recording = _session is not null || CountingDown;
        if (option.Kind == MiniOptionKind.Cursor && recording)
        {
            RefreshMini();
            return;
        }

        switch (option.Kind)
        {
            case MiniOptionKind.ShowClicks: ChkShowClicks.IsChecked = option.Value; _settings.ShowClicks = option.Value; break;
            case MiniOptionKind.ClickSound: ChkClickSound.IsChecked = option.Value; _settings.ClickSound = option.Value; break;
            case MiniOptionKind.ShowKeys: ChkShowKeys.IsChecked = option.Value; _settings.ShowKeys = option.Value; break;
            case MiniOptionKind.Magnifier: ChkMagnifier.IsChecked = option.Value; _settings.ShowMagnifier = option.Value; break;
            case MiniOptionKind.OpenFolder: ChkOpenFolder.IsChecked = option.Value; _settings.OpenFolderWhenDone = option.Value; break;
            case MiniOptionKind.Cursor: ChkCursor.IsChecked = option.Value; break;
        }

        SaveChoicesIfChanged();
        SyncInput(_session is not null);
    }
}
