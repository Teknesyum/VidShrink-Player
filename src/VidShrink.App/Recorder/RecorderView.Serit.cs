using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App.Recorder;

/// <summary>
/// Denetim şeridi: başlat, duraklat, sürdür, durdur. Dört düğme de motora
/// <see cref="RecorderSession"/> üzerinden ulaşıyor; şerit kendi kaydını tutmuyor,
/// oturumun halini okuyor.
///
/// <para><b>Şeridin okuduğu sayılar.</b> Kayıt sürerken yalnız geçen süre ve kare sayısı
/// akıyor. Yazılan boyut akmıyor: <c>-progress</c> akışında <c>total_size</c> gdigrab
/// yakalamasında on bir ilerleme bloğunun onunda sıfır kalıyor ve gerçek değer ancak son
/// blokta geliyor (8a ölçümü). Bu yüzden şeritte canlı boyut göstergesi <b>yok</b>; boyut
/// kayıt bitince <see cref="RecordResult.OutputMb"/>'den, yani dosyanın kendisinden
/// okunuyor.</para>
/// </summary>
internal partial class RecorderView
{
    private RecorderSession? _session;

    /// <summary>Oturum yoksa hal <see cref="RecorderState.Stopped"/>: şerit boşta okunur.</summary>
    internal RecorderState State => _session?.State ?? RecorderState.Stopped;

    internal bool HasSession => _session is not null;

    internal string ElapsedText => TxtElapsed.Text ?? string.Empty;

    internal string FramesText => TxtFrames.Text ?? string.Empty;

    internal string DroppedText => TxtDropped.Text ?? string.Empty;

    internal string ResultPathText => TxtResultPath.Text ?? string.Empty;

    internal string ResultText => TxtResult.Text ?? string.Empty;

    /// <summary>
    /// Geçen sürenin yazımı. Sayılar değişmez biçimde yazılıyor, yani dil değişince
    /// kaymıyor; bir saati geçen kayıt <c>01:02:12</c> biçimine geçer.
    /// </summary>
    internal static string Clock(TimeSpan at) => Saat.Ekran(at);

    private void InitSerit()
        => ShowProgress(new RecordProgress(TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0));

    private async void OnStart(object? sender, RoutedEventArgs e) => await StartAsync();

    private async void OnPause(object? sender, RoutedEventArgs e) => await PauseAsync();

    private async void OnResume(object? sender, RoutedEventArgs e) => await ResumeAsync();

    private async void OnStop(object? sender, RoutedEventArgs e) => await StopAsync();

    private async void OnSnapshot(object? sender, RoutedEventArgs e)
    {
        if (_session is not { State: RecorderState.Running } session) return;
        await SnapshotAsync(path => session.SnapshotAsync(path));
    }

    /// <summary>
    /// Kayıt sürerken tek kare alır. Kare kaydı kesmiyor; dosya kaydın klasörüne
    /// <see cref="RecorderSettings.SnapshotPath"/> adıyla yazılıyor ve yolu şeridin altında
    /// gösteriliyor. Kareyi alan iş parametre: şerit onu oturumdan veriyor, ölçü süreç
    /// açmadan kendi işini veriyor.
    /// </summary>
    internal async Task<bool> SnapshotAsync(Func<string, Task<bool>> take)
    {
        var path = _settings.SnapshotPath(DateTime.Now);
        bool ok;
        try { ok = await take(path); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ok = false;
        }

        if (ok)
        {
            TxtError.IsVisible = false;
            ShowNotice(Say("recorder.snapshot.saved", path));
        }
        else
        {
            TxtNotice.IsVisible = false;
            ShowError(Say("recorder.snapshot.failed"));
        }

        return ok;
    }

    internal Func<RecorderRequest, string, Task<bool>> TakeFrame { get; set; } = (request, path) => RecorderSession.CaptureAsync(request, path);

    internal async Task<bool> SnapshotNowAsync()
    {
        if (_session is { } session) return await SnapshotAsync(path => session.SnapshotAsync(path));
        if (CountingDown || BuildRequest() is not { } request) return false;
        return await SnapshotAsync(path => TakeFrame(request, path));
    }

    /// <summary>
    /// Kaydı başlatır. Başlamayan kaydın sebebi yutulmuyor: ffmpeg eksikse, istek
    /// doğrulamadan geçmiyorsa ya da ilk ilerleme bloğu gelmeden süreç ölüyorsa ekranda
    /// kendi satırı çıkıyor.
    /// </summary>
    internal async Task StartAsync()
    {
        if (_session is not null || CountingDown || ReplayRunning) return;

        ClearMessages();
        _chapterText = string.Empty;

        if (!ToolLocator.IsAvailable(out var missing))
        {
            ShowError(Say("recorder.error.no-ffmpeg", missing ?? string.Empty));
            return;
        }

        if (PrepareRecording() is not { } prepared) return;
        var (request, path) = prepared;

        var errors = RecorderArguments.Validate(request, path);
        if (errors.Count > 0)
        {
            ShowError(Say("recorder.error.invalid", string.Join(" ", errors)));
            return;
        }

        BeginDiskWatch(Path.GetDirectoryName(path) ?? _settings.ResolveFolder());

        request = await ChooseCaptureAsync(request);
        if (_session is not null) return;

        if (!await CountdownAsync() || _session is not null) return;

        HideDesktopIconsForRecording();
        PauseLevels();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _settings.ResolveFolder());
            PreparePreview(request.PreviewPath);
            PrepareFrame(request.FramePath);
            _audioShown = null;
            _session = await RecorderSession.StartAsync(
                request, path, new Progress<RecordProgress>(ShowProgress));            WriteDiagnostic(request, _session.StartArguments, null);

            _session.TitleChapters(LanguageCatalog.Display(Strings.Get("recorder.chapter.title")));
            _frameRegion = RegionOf(request);
            BeginTrack();
            _ = FollowEndAsync(_session.Ended, _session);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _session = null;
            RestoreDesktopIcons();
            WriteDiagnostic(request, null, ex.Message);
            ShowError(Say("recorder.error.start", ex.Message));
        }
        finally
        {
            ResumeLevels();
        }

        RefreshSerit();
    }

    /// <summary>O anki parçayı nazikçe kapatır; dosya kapalı ve oynatılabilir kalır.</summary>
    internal async Task PauseAsync()
    {
        if (_session is null) return;
        try { await _session.PauseAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { ShowError(Say("recorder.error.start", ex.Message)); }
        RefreshSerit();
    }

    /// <summary>Duraklatılmış kayda yeni bir parça açar.</summary>
    internal async Task ResumeAsync()
    {
        if (_session is null) return;
        try { await _session.ResumeAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { ShowError(Say("recorder.error.start", ex.Message)); }
        RefreshSerit();
    }

    /// <summary>
    /// Kaydı bitirir ve teslim edilen dosyayı gösterir. Yarım dosya da gösteriliyor:
    /// <see cref="RecordResult.Partial"/> doğruyken yol yine görünür, yanına da dosyanın
    /// açılıp açılamayacağı yazılır — ayrım kap türünden değil, motorun ffprobe yoklamasından
    /// (<see cref="RecordResult.Playable"/>, <c>docs/netlestirme/019-yarim-kayit-metni.md</c>).
    /// </summary>
    internal async Task StopAsync()
    {
        if (CountingDown)
        {
            CancelCountdown();
            return;
        }

        if (_session is null || _stopping) return;

        var session = _session;
        _stopping = true;
        try
        {
            Deliver(await session.StopAsync());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowError(Say("recorder.error.start", ex.Message));
        }
        finally
        {
            _session = null;
            _frameRegion = null;
            EndTrack(null);
            _stopping = false;
            FrameHiddenByUser = false;
            RestoreDesktopIcons();
            RestartLevels();
            RefreshSerit();
        }
    }

    private bool _stopping;

    /// <summary>İptal edilen kaydın dosyalarını siler; silinemeyip diskte kalanları döndürür.</summary>
    internal static IReadOnlyList<string> DeleteRecording(RecordResult result)
    {
        var kalan = new List<string>();
        var files = (result.Files ?? Array.Empty<string>()).Append(result.OutputPath);
        if (!string.IsNullOrEmpty(result.OutputPath)) files = files.Append(PointerTrack.SidecarOf(result.OutputPath));
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (File.Exists(file)) kalan.Add(file);
        }

        return kalan;
    }

    /// <summary>
    /// "Dosya silindi" yalnız hiçbir dosya diskte kalmadıysa söylenir; kalan varsa yollarıyla
    /// hata satırında gösterilir.
    /// </summary>
    internal void ReportDiscard(IReadOnlyList<string> kalan)
    {
        if (kalan.Count == 0) ShowNotice(Say("recorder.discarded"));
        else ShowError(Say("recorder.discarded-kept", string.Join(Environment.NewLine, kalan)));
    }

    internal async Task<bool> DiscardAsync()
    {
        if (CountingDown)
        {
            CancelCountdown();
            return true;
        }

        if (_session is null || _stopping) return false;

        var session = _session;
        _stopping = true;
        try
        {
            session.DropMarks();
            EndTrack(null);
            var kalan = DeleteRecording(await session.StopAsync());
            ClearMessages();
            ExpandFromMini();
            ReportDiscard(kalan);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowError(Say("recorder.error.start", ex.Message));
            return false;
        }
        finally
        {
            _session = null;
            _frameRegion = null;
            EndTrack(null);
            _stopping = false;
            FrameHiddenByUser = false;
            RestoreDesktopIcons();
            RestartLevels();
            RefreshSerit();
        }
    }

    internal Action<string> RevealFolder { get; set; } = VidShrink.App.Platform.Reveal;

    /// <summary>
    /// Klasörü açar; açamazsa hata satırına yazar. Linux'ta <c>xdg-open</c> kurulu değilse
    /// <c>Process.Start</c> atıyordu: düğmede uygulama çöküyor, teslimde bitince eylemi koşmuyordu.
    /// </summary>
    internal void RevealSafely(string path)
    {
        try
        {
            RevealFolder(path);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError($"{Say("main.error.folder")}: {ex.Message}");
        }
    }

    internal void Deliver(RecordResult result)
    {
        EndTrack(result);
        ExpandFromMini();
        ShowResult(result);
        if (_settings.OpenFolderWhenDone && Delivered() is { } done) RevealSafely(done);
        if (result.Ok && !result.Partial && Delivered() is { } finished && RecordingDelivered is { } follow) _ = follow(finished);
        StartFinishAction(result);
        ShowResultCard(result);
    }

    internal Func<string, Task>? RecordingDelivered { get; set; }

    internal async Task FollowEndAsync(Task ended, object session)
    {
        await ended;
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => FollowEndAsync(Task.CompletedTask, session));
            return;
        }

        if (ReferenceEquals(_session, session)) await StopAsync();
    }

    private void ShowProgress(RecordProgress progress)
    {
        TxtElapsed.Text = Clock(progress.Elapsed);
        TxtFrames.Text = progress.Frames.ToString("N0", Strings.Culture);
        TxtDropped.Text = progress.DroppedFrames.ToString("N0", Strings.Culture);
        _track?.Sync(progress.Captured);
        ShowSystemAudio(_session?.SystemAudio);
        CheckDiskWhileRecording();
        RefreshMini();
        SyncTray();
    }

    /// <summary>
    /// Şeridin yüzü. Düğmeler duruma göre görünür ya da görünmez oluyor; devre dışı düğme
    /// bırakılmıyor, çünkü basılamayan bir düğme kendi başına bir açıklama borcu doğurur.
    /// </summary>
    private void RefreshSerit()
    {
        var state = State;
        var running = _session is not null && state == RecorderState.Running;
        var paused = _session is not null && state == RecorderState.Paused;

        var counting = CountingDown;

        BtnStart.IsVisible = _session is null && !counting && !ReplayRunning;
        BtnAutoWizard.IsVisible = BtnStart.IsVisible;
        if (!BtnAutoWizard.IsVisible) CloseWizard();
        SyncReplayButtons(_session is null && !counting);
        BtnCountdownCancel.IsVisible = counting;
        BtnPause.IsVisible = running;
        BtnSnapshot.IsVisible = running;
        BtnResume.IsVisible = paused;
        BtnStop.IsVisible = running || paused;

        LiveDot.IsVisible = running;
        TxtState.Text = counting ? Say("recorder.countdown.left", CountdownLeft)
            : running ? Say("recorder.strip.live")
            : paused ? Say("recorder.strip.paused")
            : ScheduleUpcoming is { } planned ? Say("recorder.schedule.armed", ScheduleClockText(planned))
            : Say("recorder.strip.idle");

        SyncFrame();
        SyncRegionEditor();
        SyncPreview(running);
        SyncTrack(running);
        SyncInput(_session is not null);
        SyncSketch(_session is not null);
        SyncPrompter(running);
        SyncTray();
        RefreshMini();
    }
}
