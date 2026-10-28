using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.Ffmpeg;
using VidShrink.Core;

namespace VidShrink.App.Recorder;

/// <summary>
/// Kaydedici sekmesinin gövdesi. Kalıp <c>Playback/PlayerView</c> ile aynı: görünüm bir
/// <see cref="UserControl"/>, konular kısmi sınıflara bölünmüş — şerit
/// <c>RecorderView.Serit.cs</c>'de, hedef seçimi <c>RecorderView.Hedef.cs</c>'de, kalıcı
/// ayar <c>RecorderSettings.cs</c>'de.
///
/// <para>Görünüm ana pencereye bağlanmıyor: çıkış klasörünü ve ayar yolunu kendisi
/// çözüyor. Böylece <c>MainWindow</c> tarafında sekmeyi açan indeks alanından başka bir
/// kablo gerekmiyor.</para>
/// </summary>
internal partial class RecorderView : UserControl
{
    private readonly RecorderSettings _settings;

    private readonly string? _settingsPath;

    private string? _lastRecording;

    /// <summary>Biten kaydı küçültme sekmesine taşıyan kapı; ana pencere kuruyor.</summary>
    internal Func<string, Task>? OpenInShrink { get; set; }

    /// <summary>Biten kaydı oynatıcı sekmesinde açan kapı; ana pencere kuruyor.</summary>
    internal Func<string, Task>? OpenInPlayer { get; set; }

    public RecorderView() : this(RecorderSettings.FilePath)
    {
    }

    /// <summary>
    /// Ayar dosyasının yolunu açıkça alan kurucu. Yol örneğe bağlanıyor, çünkü statik
    /// <see cref="RecorderSettings.FilePath"/> süreç başına tek ve ölçüm sınıfları
    /// o tek dosya üzerinden birbirine karışıyordu. Suit içi paralellik kapalı, yani
    /// iki sınıf aynı anda koşmuyor; kanalı açan şey <c>PersistChoices</c>'ın
    /// <b>ertelenmiş</b> çalışması: iş tek Avalonia arayüz iş parçacığının kuyruğunda
    /// bekliyor ve ölçüm bittikten sonra, bir sonraki sınıfın ölçümü sırasında
    /// boşalabiliyor. Yolu örnekte tutmak o geç yazmayı da kapsıyor — görünüm hâlâ
    /// kurulurken aldığı dosyaya yazıyor, çağıranın kapısı kapanmış olsa bile.
    /// </summary>
    internal RecorderView(string? settingsPath)
    {
        _settingsPath = settingsPath;
        DiagnosticFolder = DefaultDiagnosticFolder;
        InitializeComponent();
        HideUnavailableInputOptions();
        _settings = RecorderSettings.Load(_settingsPath);
        ApplyDesktopIconsSupport(OperatingSystem.IsWindows());
        InitHedef();
        InitSecici();
        InitDuzenleyici();
        InitGelismis();
        InitKisayol();
        InitOtomatik();
        InitSes();
        InitSesSeviyesi();
        InitSerit();
        InitGeriSayim();
        InitSaatliKayit();
        InitBitince();
        InitPaylas();
        InitSonucKarti();
        InitHizliKirpma();
        InitYakinlastirma();
        InitSonucPaneli();
        InitTampon();
        InitMini();
        InitImlec();
        RefreshSerit();
        InitKalicilik();
        InitSade();
    }

    /// <summary>Sayfadaki hata satırı. Ölçüm kendi gördüğünü okuyabilsin diye açık.</summary>
    internal string ErrorText => TxtError.IsVisible ? TxtError.Text ?? string.Empty : string.Empty;

    /// <summary>
    /// Dil değişimine abonelik. Şeridin durum etiketi ve hedef kutusunun öğeleri XAML'dan
    /// değil kodla yazılıyor; abonelik olmadan bu iki yer eski dilde kalıyor ve
    /// <c>LanguageTests.DilDegisinceEkrandaOtekiDilinMetniKalmiyor</c> kırmızı veriyor.
    /// Kalıp <c>Playback/PlayerAdvancedPanel</c> ile aynı.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Strings.Changed -= OnLanguageChanged;
        Strings.Changed += OnLanguageChanged;
        RefreshLanguage();
        RefreshScreens();
        ActivateTray();
        ActivateHotkeys();
        ActivateLevels();
        if (_session is null) ScanOrphans(_settings.ResolveFolder());
        _ = MeasureOnOpenAsync(Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Strings.Changed -= OnLanguageChanged;
        DeactivateTray();
        DeactivateHotkeys();
        DeactivateLevels();
        CloseRegionEditor();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) RefreshLanguage();
        else Dispatcher.UIThread.Post(RefreshLanguage);
    }

    /// <summary>Kodla yazılan bütün metinleri yeniden üretir; seçimler korunuyor.</summary>
    private void RefreshLanguage()
        => Quietly(() =>
        {
            RefreshTargetLabels();
            RefreshSeciciLabels();
            RefreshMasks();
            RefreshAudioBoxes();
            RefreshCountdownLabels();
            RefreshScheduleLabels();
            RefreshFinishLabels();
            RefreshShareLabels();
            RefreshReplayLabels();
            RefreshAdvancedLabels();
            RefreshHotkeyLabels();
            RefreshPointerLabels();
            RefreshWizard();
            RefreshSerit();
        });

    internal RecorderSettings Settings => _settings;

    private static string Say(string key) => LanguageCatalog.Display(Strings.Get(key));

    private static string Say(string key, params object?[] args)
        => string.Format(Strings.Culture, LanguageCatalog.Display(Strings.Get(key)), args);

    private void ClearMessages()
    {
        TxtError.IsVisible = false;
        TxtError.Text = string.Empty;
        TxtNotice.IsVisible = false;
        TxtNotice.Text = string.Empty;
        ResultPanel.IsVisible = false;
        WarningRow.IsVisible = false;
        DiskRow.IsVisible = false;
        _lastRecording = null;
        ResetShare();
        HideFinish();
        InitGecmis();
        InitPrompter();
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.IsVisible = true;
    }

    internal string NoticeText => TxtNotice.IsVisible ? TxtNotice.Text ?? string.Empty : string.Empty;

    private void ShowNotice(string message)
    {
        TxtNotice.Text = message;
        TxtNotice.IsVisible = true;
    }

    private void HideNotice()
    {
        TxtNotice.IsVisible = false;
        TxtNotice.Text = string.Empty;
    }

    /// <summary>
    /// Biten kaydın teslimi. Yol her koşulda görünür oluyor — yarım dosyada bile — çünkü
    /// kullanıcının aradığı ilk şey dosyanın nereye yazıldığı.
    /// </summary>
    internal void ShowResult(RecordResult result)
    {
        ResultPanel.IsVisible = true;
        _lastRecording = result.OutputPath;
        TxtResultPath.Text = result.OutputPath;
        BtnToMp4.IsVisible = VidShrink.Core.RecorderArguments.ContainerOf(result.OutputPath) == VidShrink.Core.RecorderContainer.Mkv;
        BtnTrimIdle.IsVisible = result.Ok && !result.Partial
                                && VidShrink.Core.RecorderArguments.ContainerOf(result.OutputPath) != VidShrink.Core.RecorderContainer.Gif;
        ResetQuickTrim(result);
        ResetAutoZoom(result);
        ResetSonucPaneli(result);
        ResetShare();
        HideFinish();
        TxtResult.Text = Say(
            BaslikAnahtari(result),
            Bicim.Boyut.Mb(result.OutputMb, Strings.Culture),
            result.Segments.ToString(CultureInfo.InvariantCulture));

        BtnToPlayer.IsEnabled = !result.Partial || result.Playable != false;
        BtnToShrink.IsEnabled = !result.Partial || result.Playable != false;
        var exitCode = result.ExitCode.ToString(CultureInfo.InvariantCulture);
        var warned = true;

        if (result.Partial)
        {
            TxtWarning.Text = Say(YarimAnahtari(result.Playable,
                VidShrink.Core.RecorderArguments.ContainerOf(result.OutputPath) == VidShrink.Core.RecorderContainer.Mkv));
            DurumuGoster(uyari: true);
        }
        else if (result.MissingGif is not null)
        {
            TxtWarning.Text = Say("recorder.output.gif-failed", exitCode);
            DurumuGoster(uyari: true);
        }
        else if (!result.Ok)
        {
            TxtWarning.Text = Say("recorder.output.failed", exitCode);
            DurumuGoster(uyari: false);
        }
        else if (result.DeliveryError is { } neden)
        {
            TxtWarning.Text = Say("recorder.output.not-moved", neden);
            DurumuGoster(uyari: true);
        }
        else
        {
            warned = false;
        }

        ShowLostChapters(result, warned);
    }

    /// <summary>
    /// Sonuc basligi kaydin nasil bittigini soyler: yarim kayit "yarim kaldi", basarisiz kayit
    /// "basarisiz" der. GIF'e cevrilemeyen kayit bitmistir, eksigi uyari satirinda soylenir.
    /// </summary>
    internal static string BaslikAnahtari(RecordResult result) => result switch
    {
        { Partial: true } => "recorder.output.done-partial",
        { Ok: false, MissingGif: null } => "recorder.output.done-failed",
        _ => "recorder.output.done"
    };

    /// <summary>
    /// Oldurulen kaydin oynatilabilir oldugu kap turunden cikarilmaz: motor dosyayi ffprobe ile
    /// yoklar (<see cref="RecordResult.Playable"/>). Okundu ise "oynatilabilir", okunamadi ise
    /// "oynatilamaz"; yoklanamadiysa hicbiri soylenmez. Okunamayan kayit zaten MKV ise "sonraki
    /// kayit icin MKV secin" onerisi yalan olur; o dal oneriyi tasimayan metni secer.
    /// Karar <c>docs/netlestirme/019-yarim-kayit-metni.md</c>.
    /// </summary>
    internal static string YarimAnahtari(bool? oynar, bool mkv) => oynar switch
    {
        true => "recorder.output.partial",
        false when mkv => "recorder.output.partial-broken-mkv",
        false => "recorder.output.partial-broken",
        null => "recorder.output.partial-unverified"
    };

    /// <summary>
    /// Yarim kayit uyaridir, basarisiz kayit hatadir. Ayrimi renk tek basina tasimaz:
    /// uyari govde renginde ve ucgen-unlem simgesiyle, hata kirmizi ve simgesiz cikar.
    /// Karar <c>docs/netlestirme/018-uyari-rengi-27-palette-yok.md</c>. Simgesiz satirda metin
    /// iki sutunu da kaplar, bos simge sutununun araligi kaymaz.
    /// </summary>
    private void DurumuGoster(bool uyari)
    {
        var anahtar = uyari ? "StatusWarning" : "StatusError";
        if (Application.Current?.TryFindResource(anahtar, out var kaynak) == true && kaynak is ControlTheme tema)
            TxtWarning.Theme = tema;

        WarningGlyph.IsVisible = uyari;
        Grid.SetColumn(TxtWarning, uyari ? 1 : 0);
        Grid.SetColumnSpan(TxtWarning, uyari ? 1 : 2);
        WarningRow.IsVisible = true;
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (Delivered() is { } path) RevealSafely(path);
    }

    /// <summary>
    /// Teslim edilmiş dosya. Yol kutudan değil alandan okunuyor: kutu salt okunur olsa da
    /// bir metin kutusudur, sonraki işin girdisi ekrandaki metne bağlanmaz.
    /// </summary>
    private string? Delivered()
        => !string.IsNullOrWhiteSpace(_lastRecording) && File.Exists(_lastRecording) ? _lastRecording : null;

    private async void OnToShrink(object? sender, RoutedEventArgs e)
    {
        if (Delivered() is { } path && OpenInShrink is { } gate) await gate(path);
    }

    private async void OnToPlayer(object? sender, RoutedEventArgs e)
    {
        if (Delivered() is { } path && OpenInPlayer is { } gate) await gate(path);
    }

    internal bool Mp4Visible => BtnToMp4.IsVisible;

    private async void OnToMp4(object? sender, RoutedEventArgs e)
        => await SaveAsMp4Async(async args => (await FfmpegRunner.RunAsync(args)).Ok);

    internal async Task<string?> SaveAsMp4Async(Func<System.Collections.Generic.IReadOnlyList<string>, Task<bool>> run)
    {
        if (Delivered() is not { } source) return null;

        var target = VidShrink.Core.RecorderArguments.RemuxTarget(source, File.Exists);
        bool ok;
        try { ok = await run(VidShrink.Core.RecorderArguments.BuildRemuxToMp4(source, target)); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ok = false;
        }

        if (ok)
        {
            TxtError.IsVisible = false;
            ShowNotice(Say("recorder.output.mp4-saved", target));
            return target;
        }

        TxtNotice.IsVisible = false;
        ShowError(Say("recorder.output.mp4-failed"));
        return null;
    }

    /// <summary>
    /// Çıkış klasörünü seçer. Kalıp <c>Playback/PlayerView.Window.cs</c>'deki klasör
    /// seçiciyle aynı: seçici yoksa iş sessizce bırakılıyor, kutunun içindeki yol duruyor.
    /// </summary>
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;

        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Say("recorder.output.browse"),
                AllowMultiple = false
            });

            if (folders.Count == 0) return;
            var picked = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(picked)) return;

            TxtOutputFolder.Text = picked;
            _settings.OutputFolder = picked;
            _settings.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError(Say("recorder.error.folder", ex.Message));
        }
    }
}
