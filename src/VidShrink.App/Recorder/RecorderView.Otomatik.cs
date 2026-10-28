using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App.Recorder;

/// <summary>
/// Otomatik kip: kodlama kolunu kullanıcı değil program yazıyor. Karar iki parçadan
/// geliyor — aday merdivenini <see cref="RecorderAutoPlan"/> (saf, Core), kazananı
/// <see cref="RecorderAutoProbe"/> (aday başına kısa gerçek kayıt, düşen kare sayımı)
/// üretiyor.
///
/// <para>Kip varsayılan: kullanıcı hiçbir şey seçmezse program seçiyor. Elle ayar paneli
/// yalnız "Kendim ayarlayacağım" işaretlenince görünüyor; devre dışı bırakılmış kutu
/// bırakmak yerine görünmüyor — deponun şeritte de izlediği kalıp bu.</para>

/// <para>Hedef süre ve hedef boyut isteğe bağlı. İkisi de doluysa
/// <see cref="RecorderBudget"/> bit hızını hesaplıyor ve aday merdiveninin üstüne
/// biniyor; biri boşsa kalite kolu olduğu gibi kalıyor.</para>
/// </summary>
internal partial class RecorderView
{
    private RecorderAutoChoice? _autoChoice;
    private RecorderAutoResult? _autoResult;
    private CancellationTokenSource? _autoStop;

    /// <summary>Otomatik kip açık mı; elle kip istenmediği sürece açıktır.</summary>
    internal bool AutoMode => !ManualMode;

    /// <summary>Kullanıcı kodlama kolunu kendi mi yazıyor.</summary>
    internal bool ManualMode => AdvancedMode && (RadManual.IsChecked ?? false);

    internal bool AdvancedMode => RadAdvanced.IsChecked ?? false;

    private void InitLevel()
    {
        RadAdvanced.IsChecked = _settings.AdvancedMode;
        RadSimple.IsChecked = !_settings.AdvancedMode;
        RadAdvanced.IsCheckedChanged += OnLevelToggled;
        foreach (var kart in new Control[] { PanelWebcam, PanelEncoding })
            kart.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) KartSutunlari();
            };
        RecorderCards.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && DarDuvar != _darDuvar) KartYerlesimi();
        };
        ApplyLevel();
    }

    private void OnLevelToggled(object? sender, RoutedEventArgs e)
    {
        _settings.AdvancedMode = AdvancedMode;
        _settings.Save(_settingsPath);
        ApplyLevel();
        ApplyAutoVisibility();
    }

    private void ApplyLevel()
    {
        var advanced = AdvancedMode;
        PanelOptions.IsVisible = advanced;
        PanelAdvanced.IsVisible = advanced;
        ChkOpenFolder.IsVisible = advanced;
        ChkResultCard.IsVisible = advanced;
        ApplyReplayVisibility();
        ApplySade();
        KartYerlesimi();
        RaiseLevelChanged();
    }

    private bool _darDuvar;

    private bool DarDuvar => RecorderCards.Bounds.Width > 0
        && RecorderCards.ColumnsFor(RecorderCards.Bounds.Width) < RecorderCards.MaxColumns;

    private void KartYerlesimi()
    {
        _darDuvar = DarDuvar;
        var gelismis = AdvancedMode;
        var otomatik = gelismis && AutoMode;
        var ucSutun = _darDuvar && otomatik;
        Diz(CardsLeft, ucSutun ? new Control[] { PanelTarget, PanelWebcam }
            : gelismis ? new Control[] { PanelTarget, PanelAudio } : new Control[] { PanelTarget });
        Diz(CardsMiddle, ucSutun ? new Control[] { PanelOptions, PanelReplay }
            : gelismis ? new Control[] { PanelOptions } : new Control[] { PanelOptions, PanelAudio });
        Diz(CardsRight, ucSutun ? new Control[] { PanelAdvanced, PanelAudio }
            : otomatik ? new Control[] { PanelAdvanced, PanelReplay } : new Control[] { PanelAdvanced, PanelReplay, PanelWebcam });
        Diz(CardsExtra, otomatik && !ucSutun ? new Control[] { PanelWebcam, PanelEncoding }
            : new Control[] { PanelEncoding });
        KartSutunlari();
    }

    /// <summary>
    /// Kartlar dört sütunluk duvarda durur. Basit kipte seçenek ve gelişmiş kartları gizli
    /// olduğu için ses ikinci, kamera üçüncü sütuna geçer ve dördüncü sütun yer tutmaz; Gelişmiş
    /// kipte ses hedefin altına iner, orta iki sütun seçeneklere ve gelişmiş ayarlara kalır,
    /// kamera dördüncü sütuna çıkar. Ayarları elle seçince dördüncü sütunu kodlayıcı kartı alır,
    /// kamera gelişmiş ayarların altına iner. Görünür kartı olmayan sütun yer tutmaz. Duvar üç
    /// sütuna düşünce dördüncü sütun ilk üçün en kısasının altına girip 1024 px pencerede
    /// sayfayı 1319 px'e uzatıyordu (görünen 1008); orada kamera hedefin, tampon seçeneklerin,
    /// ses gelişmiş ayarların altına iner ve dördüncü sütun boş kalır.
    /// <para>İmleç vurgusu ayrı kartta kameranın altına, gizlilik maskesi kendi satırında hedefin
    /// içine girince 1024 × 1060 pencerede sol sütun 1018 px'e çıkıp sayfayı 1140 px'e uzatıyordu
    /// (görünen 1008). İmleç seçenekleri seçenek kartına katıldı, "kayıt bitince klasörü aç" bitiş
    /// eyleminin altına, maske başlığı düğmelerin satırına geçti; en uzun sütun 875 px.</para>
    /// </summary>
    private static void Diz(StackPanel sutun, Control[] kartlar)
    {
        for (var i = 0; i < kartlar.Length; i++)
        {
            var kart = kartlar[i];
            if (ReferenceEquals(kart.Parent, sutun) && sutun.Children.IndexOf(kart) == i) continue;
            (kart.Parent as Panel)?.Children.Remove(kart);
            sutun.Children.Insert(Math.Min(i, sutun.Children.Count), kart);
        }
    }

    /// <summary>
    /// Boş sütun gizlenir; <see cref="KartDuvari"/> görünen sütunları sırayla dizer. Sütun en az
    /// bir alan (<c>FieldWidth</c>) genişliğinde: dört eşit sütunda 1136 px pencerede
    /// "1. ekran — 1920 × 1080" seçimi 165 px isterken kutusunda 162 px, 1024 px'te seçim düğmeleri
    /// 50 px kalıyordu. Daha dar pencerede sütun sayısı düşer, kartlar en kısa sütunun altına girer.
    /// </summary>
    private void KartSutunlari()
    {
        foreach (var sutun in new[] { CardsLeft, CardsMiddle, CardsRight, CardsExtra })
            sutun.IsVisible = sutun.Children.Any(c => c.IsVisible);
    }

    /// <summary>Kullanıcının verdiği hedeften çıkan bütçe; hedef yoksa hükmü <c>NotRequested</c>.</summary>
    internal RecorderBudget Budget => RecorderBudget.From(TargetMegabytes, TargetSeconds, AudioTrackCount());

    private int AudioTrackCount()
    {
        var selection = Selection;
        return (selection.Microphone is null ? 0 : 1) + (selection.SystemAudio is null ? 0 : 1);
    }

    private int? TargetSeconds =>
        int.TryParse(TxtTargetSeconds.Text, NumberStyles.Integer, Strings.Culture, out var sn) ? sn
        : string.IsNullOrWhiteSpace(TxtTargetSeconds.Text) ? null
        : 0;

    private double? TargetMegabytes =>
        double.TryParse(TxtTargetMegabytes.Text, NumberStyles.Float, Strings.Culture, out var mb) ? mb
        : string.IsNullOrWhiteSpace(TxtTargetMegabytes.Text) ? null
        : 0;

    /// <summary>Yazılacak aday; ölçüm koşmadıysa merdivenin ilk adayı.</summary>
    internal RecorderAutoChoice? AutoChoice
    {
        get => _autoChoice;
        set => _autoChoice = value;
    }

    private RecorderAutoChoice? _guess;

    internal RecorderAutoChoice PlannedChoice => _autoChoice ?? (_guess ??= AutoCandidates()[0]);

    private IReadOnlyList<RecorderAutoChoice> AutoCandidates()
    {
        var machine = Machine();
        var candidates = RecorderAutoPlan.Candidates(machine);
        return WizardAnswers is { } answers ? RecorderWizard.Shape(candidates, answers, machine) : candidates;
    }

    /// <summary>Ölçümün kendisi; ölçüm koşmadıysa <c>null</c>.</summary>
    internal RecorderAutoResult? AutoResult => _autoResult;

    internal string AutoSummaryText => TxtAutoSummary.Text ?? string.Empty;

    private void InitOtomatik()
    {
        RadManual.IsChecked = _settings.ManualMode;
        RadAuto.IsChecked = !_settings.ManualMode;
        TxtTargetSeconds.Text = _settings.TargetSeconds?.ToString(Strings.Culture) ?? string.Empty;
        TxtTargetMegabytes.Text = _settings.TargetMegabytes?.ToString(Strings.Culture) ?? string.Empty;
        RadManual.IsCheckedChanged += OnAutoToggled;
        InitLevel();
        TxtTargetSeconds.TextChanged += OnBudgetChanged;
        TxtTargetMegabytes.TextChanged += OnBudgetChanged;
        ApplyAutoVisibility();
        ShowBudgetNote();
    }

    /// <summary>
    /// Hedef kutuları her değiştiğinde hüküm ekranda güncelleniyor: kullanıcı kaydı
    /// başlatana kadar hedefin tutup tutmadığını bilmeden beklemiyor.
    /// </summary>
    private void OnBudgetChanged(object? sender, TextChangedEventArgs e)
    {
        _settings.TargetSeconds = TargetSeconds is { } sn && sn > 0 ? sn : null;
        _settings.TargetMegabytes = TargetMegabytes is { } mb && mb > 0 ? mb : null;
        _settings.Save(_settingsPath);
        ShowBudgetNote();
    }

    private void ShowBudgetNote()
    {
        var budget = Budget;
        TxtBudgetNote.Text = budget.Verdict switch
        {
            RecorderBudgetVerdict.Usable => Say("recorder.budget.result", Strings.BitHizi(budget.VideoKbps)),
            RecorderBudgetVerdict.TooSmall => Say("recorder.budget.too-small", Strings.BitHizi(RecorderBudget.MinimumVideoKbps)),
            RecorderBudgetVerdict.Invalid => Say("recorder.budget.invalid"),
            _ => SingleTargetNote()
        };

        TxtBudgetNote.IsVisible = TxtBudgetNote.Text.Length > 0;
    }

    private string SingleTargetNote()
    {
        var seconds = TargetSeconds;
        var megabytes = TargetMegabytes;
        if (seconds is null && megabytes is null) return string.Empty;
        if (seconds is <= 0 || megabytes is <= 0) return Say("recorder.budget.invalid");
        return seconds is { } sn
            ? Say("recorder.budget.duration-only", Saat.TamSaniye(sn, Strings.Culture))
            : Say("recorder.budget.size-only", megabytes!.Value.ToString("0.#", Strings.Culture));
    }

    /// <summary>
    /// Elle kip kapanınca ölçüm kendiliğinden koşuyor. Açılınca elle panel geri geliyor ve
    /// ölçüm sonucu düşüyor.
    /// </summary>
    private async void OnAutoToggled(object? sender, RoutedEventArgs e)
    {
        _settings.ManualMode = RadManual.IsChecked ?? false;
        _settings.Save(_settingsPath);
        ApplyAutoVisibility();

        if (!AutoMode)
        {
            _autoChoice = null;
            _autoResult = null;
            ShowSummary(string.Empty);
            return;
        }

        if (SkipAutoMeasure) return;
        await MeasureAsync();
    }

    internal bool SkipAutoMeasure { get; set; }

    internal bool MeasuredOnOpen { get; private set; }

    internal async Task MeasureOnOpenAsync(bool desktop)
    {
        if (MeasuredOnOpen || SkipAutoMeasure || !desktop || !AutoMode || _autoChoice is not null || _session is not null) return;
        MeasuredOnOpen = true;
        await (OpenMeasure ?? MeasureAsync)();
    }

    internal Func<Task>? OpenMeasure { get; set; }

    private async void OnAutoMeasure(object? sender, RoutedEventArgs e) => await MeasureAsync();

    private void ApplyAutoVisibility()
    {
        var auto = AutoMode;
        PanelManualOptions.IsVisible = !auto;
        PanelAdvancedEncoding.IsVisible = !auto;
        PanelAutoResult.IsVisible = auto;
        BtnAutoMeasure.IsVisible = auto;
        TxtTargetSeconds.IsEnabled = auto;
        TxtTargetMegabytes.IsEnabled = auto;
        KartYerlesimi();
    }

    /// <summary>
    /// Merdiveni kurar, denemeleri koşar ve kazananı yazar. ffmpeg yoksa ya da deneme
    /// kaydı tamamlanmazsa sebep yutulmuyor: merdivenin ilk adayı yazılıyor ve ölçülmediği
    /// ekranda söyleniyor.
    /// </summary>
    internal async Task MeasureAsync()
    {
        if (_session is not null) return;

        var measured = BuildRequest(applyAuto: false) is { } built && ToolLocator.IsAvailable(out _)
            ? await ChooseCaptureAsync(built, notify: false)
            : null;
        _guess = null;

        var candidates = AutoCandidates();
        _autoChoice = candidates[0];
        _autoResult = null;

        if (measured is not { } request || _session is not null)
        {
            ShowAutoSummary(null);
            return;
        }

        ShowSummary(Say("recorder.auto.measuring"));
        BtnAutoMeasure.IsEnabled = false;

        _autoStop?.Cancel();
        _autoStop = new CancellationTokenSource();

        try
        {
            var scratch = Path.Combine(Path.GetTempPath(), AppIdentity.FolderName, "auto");
            var result = await RecorderAutoProbe.ChooseAsync(
                request, scratch, candidates, RecorderAutoProbe.TrialSeconds, _autoStop.Token);

            _autoResult = result;
            _autoChoice = result.Choice;
            ShowAutoSummary(result);
        }
        catch (OperationCanceledException)
        {
            ShowAutoSummary(null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowError(Say("recorder.error.start", ex.Message));
            ShowAutoSummary(null);
        }
        finally
        {
            BtnAutoMeasure.IsEnabled = true;
        }
    }

    private void ShowAutoSummary(RecorderAutoResult? result)
    {
        if (_autoChoice is not { } choice)
        {
            ShowSummary(string.Empty);
            return;
        }

        var size = choice.Scale is { } scale
            ? Bicim.Cozunurluk(scale.Width, scale.Height)
            : Say("recorder.auto.native");

        var head = Say(
            "recorder.auto.summary",
            choice.VideoCodec,
            choice.Fps.ToString(CultureInfo.InvariantCulture),
            size,
            RecorderArguments.Extension(choice.Container));

        var tail = result is { Measured: true }
            ? Say(
                "recorder.auto.trials",
                result.Trials.Count.ToString(CultureInfo.InvariantCulture),
                result.Trials.First(t => t.Choice == result.Choice).DroppedFrames.ToString("N0", Strings.Culture))
            : Say("recorder.auto.unmeasured");

        ShowSummary(head + " — " + tail + " " + string.Join(" · ", choice.Notes.Select(NoteText)));
    }

    /// <summary>
    /// Özet satırı. Söylenecek bir şey yokken gizleniyor: boş bir metin düğümü ekranda
    /// yer kaplar ve altındaki her şeyi aşağı iter.
    /// </summary>
    private void ShowSummary(string text)
    {
        TxtAutoSummary.Text = text;
        TxtAutoSummary.IsVisible = text.Length > 0;
    }

    private static string NoteText(RecorderAutoNote note) => note switch
    {
        RecorderAutoNote.HardwareEncoderChosen => Say("recorder.auto.note.hardware"),
        RecorderAutoNote.SoftwareEncoderFallback => Say("recorder.auto.note.software"),
        RecorderAutoNote.FpsFollowsRefreshRate => Say("recorder.auto.note.refresh"),
        RecorderAutoNote.FpsSteppedDown => Say("recorder.auto.note.stepped"),
        RecorderAutoNote.ResolutionKept => Say("recorder.auto.note.kept"),
        RecorderAutoNote.ResolutionHalved => Say("recorder.auto.note.halved"),
        RecorderAutoNote.ContainerSurvivesKill => Say("recorder.auto.note.container"),
        RecorderAutoNote.PresetFollowsEncoder => Say("recorder.auto.note.preset"),
        _ => string.Empty
    };

    /// <summary>
    /// Otomatik kipin girdisi. Yakalama boyutu seçilen hedeften, yenileme hızı işletim
    /// sisteminden, çalışan donanım kolları yoklamadan geliyor; hiçbiri varsayılmıyor.
    /// Windows'ta 30 kare tavanı yalnız <c>gdigrab</c> içindir: <c>ddagrab</c> yoklaması
    /// geçtiyse (<see cref="DdagrabWorks"/>) tavan yok.
    /// </summary>
    internal RecorderMachine Machine()
    {
        var (width, height) = CaptureSize();
        return new RecorderMachine(
            width,
            height,
            ScreenRefresh.PrimaryHz(),
            Environment.ProcessorCount,
            WorkingEncoders(),
            OperatingSystem.IsWindows() && DdagrabWorks != true ? RecorderAutoPlan.GdigrabMaxFps : 0);
    }

    private (int Width, int Height) CaptureSize()
    {
        if (SelectedTarget == RecorderTargetKind.Region
            && int.TryParse(TxtRegionWidth.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
            && int.TryParse(TxtRegionHeight.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            && w > 0 && h > 0)
            return (w, h);

        var screens = TopLevel.GetTopLevel(this)?.Screens;
        if (screens is null || screens.All.Count == 0) return (0, 0);

        var index = _settings.ScreenIndex >= 0 && _settings.ScreenIndex < screens.All.Count ? _settings.ScreenIndex : 0;
        var bounds = screens.All[index].Bounds;
        return (bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Yoklamanın çalıştığını gördüğü donanım kolları. ffmpeg yoksa boş liste dönüyor ve
    /// merdiven yazılım koluna düşüyor.
    /// </summary>
    private static IReadOnlyList<string> WorkingEncoders()
    {
        if (!ToolLocator.IsAvailable(out _)) return Array.Empty<string>();

        try { return RecorderAutoProbe.WorkingHardwareEncoders(EncoderCapabilities.Instance); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { return Array.Empty<string>(); }
    }
}
