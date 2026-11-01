using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App;

/// <summary>Ilerleme penceresinin gorunur durumu; olcunun sordugu tek alan.</summary>
internal enum ShrinkJobState
{
    Bekliyor,
    Kosuyor,
    Bitti,
    Gerekce,
    Hata
}

/// <summary>
/// Bes ret gerekcesini kullanicinin gordugu cumlenin anahtarina ceviren tek kapi. Bes uye
/// de burada adiyla gecer: bu switch olmadan gerekceler uretilir ama hicbiri ekrana ulasmaz.
/// </summary>
internal static class ShrinkProblemText
{
    internal const string NoTarget = "main.shrink-job.reason.no-target";
    internal const string TargetNotANumber = "main.shrink-job.reason.not-a-number";
    internal const string TargetNotPositive = "main.shrink-job.reason.not-positive";
    internal const string TargetNotInQuickList = "main.shrink-job.reason.not-in-quick-list";
    internal const string NoPath = "main.shrink-job.reason.no-path";

    internal static IReadOnlyList<ShrinkArgumentProblem> All { get; } = new[]
    {
        ShrinkArgumentProblem.NoTarget,
        ShrinkArgumentProblem.TargetNotANumber,
        ShrinkArgumentProblem.TargetNotPositive,
        ShrinkArgumentProblem.TargetNotInQuickList,
        ShrinkArgumentProblem.NoPath
    };

    internal static string Key(ShrinkArgumentProblem problem) => problem switch
    {
        ShrinkArgumentProblem.NoTarget => NoTarget,
        ShrinkArgumentProblem.TargetNotANumber => TargetNotANumber,
        ShrinkArgumentProblem.TargetNotPositive => TargetNotPositive,
        ShrinkArgumentProblem.TargetNotInQuickList => TargetNotInQuickList,
        ShrinkArgumentProblem.NoPath => NoPath,
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null)
    };

    internal static string QuickList()
        => string.Join(", ", ShellIntegration.QuickShrinkTargetsMegabytes.Select(ShellIntegration.FormatQuickShrinkLabel));
}

/// <summary>
/// Kabuk menusunden gelen kucultme isteginin tuketicisi. Ana pencere acilmaz: gorev
/// cubugunda duran bu kucuk pencere kodlamayi kosar, <see cref="EncodeProgress"/>
/// degerlerini gosterir ve is bitince kendini kapatir. Istek cozulemediyse pencere kalir,
/// tek satir gerekceyi ve ana pencereye gecis dugmesini gosterir.
/// </summary>
public partial class ShrinkJobWindow : Window
{
    internal static readonly TimeSpan Linger = TimeSpan.FromSeconds(6);

    private readonly ShellShrinkStartup _startup;
    private readonly ShrinkRequestQueue? _queue;
    private readonly string _language;
    private readonly AppSettings _appSettings = LoadAppSettings();
    private readonly JobPump<ShrinkRequest> _pump;
    private readonly List<AktifIs> _aktif = new();
    private readonly SemaphoreSlim _overshootGate = new(1, 1);
    private readonly List<string> _outputs = new();
    private DispatcherTimer? _closeTimer;
    private bool _started;
    private bool _closed;
    private int _accepted;
    private int _finished;
    private int _baslayan;
    private int? _parallelLimit;
    private PlanOptions? _template;
    private bool _ceilingTarget;
    private string? _extension;
    private bool _paused;
    private QueueEndChoice _whenDone;
    private DispatcherTimer? _countdown;
    private int? _countdownLeft;

    /// <summary>Uyut ve kapat bu kadar saniye geri sayar; "Vazgeç" arada durdurur.</summary>
    internal const int CountdownSeconds = 60;

    /// <summary>Kuyruk sonu eyleminin sistem yüzü; testler sahtesini verir.</summary>
    internal IQueueEndActions Actions { get; set; } = SystemQueueEndActions.Instance;

    private readonly List<BitenIs> _bitenler = new();
    private readonly List<ShrinkRequest> _dusenler = new();
    private Func<bool>? _pencereEtkin;

    /// <summary>İş bitti haberinin sistem yüzü; testler sahtesini verir.</summary>
    internal IIsBildirimYuzu Bildirim { get; set; } = IsBittiBildirimi.Varsayilan();

    /// <summary>Pencere şu an kullanıcının önünde mi; testler kendi cevabını verir.</summary>
    internal Func<bool> PencereEtkin
    {
        get => _pencereEtkin ??= () => IsActive;
        set => _pencereEtkin = value;
    }

    /// <summary>
    /// K6: aynı anda koşan iş sınırı. Ayardan okunur ve makinenin üst sınırına çekilir;
    /// testler kendi değerini verir.
    /// </summary>
    internal int ParallelLimit
    {
        get => _parallelLimit ?? ParallelJobs.Clamp(_appSettings.ParallelJobs, Environment.ProcessorCount);
        set => _parallelLimit = Math.Max(ParallelJobs.Default, value);
    }

    /// <summary>Bir isteği koşturan iş; testler sahtesini verir, gerçek ffmpeg çağrılmaz.</summary>
    internal Func<ShrinkRequest, CancellationToken, Task>? Kosturucu { get; set; }

    /// <summary>Şu an koşan istekler, başlama sırasıyla.</summary>
    internal IReadOnlyList<ShrinkRequest> RunningJobs => _pump.Running;

    /// <summary>SIK-P2: kodlama boyunca tutulan uyku isteği; testler sahte katmanlı nöbet verir.</summary>
    internal SleepGuard Uyku { get; set; } = SleepGuard.Shared;

    /// <summary>SIK-P2: Ayarlar'daki "kodlarken uyuma" kutusu; açılışta ayardan okunur.</summary>
    internal bool UykuyuEngelle { get; set; }

    private IDisposable? _uykuIstegi;

    /// <summary>SIK-P9: kuyruğun defteri; test konağında <c>null</c>, testler kendi klasörlü defterini verir.</summary>
    internal QueueJournal? Defter { get; set; } = QueueJournal.Shared;

    private readonly Dictionary<ShrinkRequest, SavedJob> _geriGelen = new(ReferenceEqualityComparer.Instance);
    private volatile bool _defterKapandi;

    /// <summary>K6: koşan tek işi iptal eder; ötekiler ve sıra sürer.</summary>
    internal bool CancelJob(ShrinkRequest request) => _pump.Cancel(request);

    /// <summary>SIK-P3: pencerenin bütün kodlamalarını askıya alan düğme; duraklat düğmesi bunu da çevirir.</summary>
    internal EncodePause EncodePause { get; } = new();

    /// <summary>Verilen yolun sürücüsündeki boş bayt; okunamazsa <c>null</c>. Testler sahtesini verir.</summary>
    internal Func<string, long?> FreeSpace { get; set; } = path => DiskSpaceGuard.TryGetFreeBytes(path, out var free) ? free : null;

    internal static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(5);

    private DispatcherTimer? _diskTimer;

    /// <summary>Koşan bir işin ekrandaki durumu; satırı bir kez kurulur, ilerleme yerinde yazılır.</summary>
    private sealed class AktifIs
    {
        public AktifIs(ShrinkRequest request) => Request = request;
        public ShrinkRequest Request { get; }
        /// <summary>Hedef etiketi ve iş sırası; toplam gösterirken o anki kabul sayısından okunur.</summary>
        public string? TargetLabel { get; set; }
        public int Order { get; set; }
        public double Fraction { get; set; }
        public string Stage { get; set; } = "-";
        public string Remaining { get; set; } = "-";
        public Grid? Row { get; set; }
        public ProgressBar? Bar { get; set; }
        public TextBlock? Kalan { get; set; }
        public string? Yuzde { get; set; }
    }

    internal KaliteOlcer KaliteOlcer { get; set; } = KaliteSatiri.Varsayilan;

    internal Action<string> KaliteGunlugu { get; set; } = KaliteSatiri.GunlugeYaz;

    private bool? _kaliteyiOlc;

    internal bool KaliteyiOlc
    {
        get => _kaliteyiOlc ?? _appSettings.AdvMeasureQuality;
        set => _kaliteyiOlc = value;
    }

    internal string KaliteYuzdesi => TxtProgressPercent.IsVisible ? TxtProgressPercent.Text ?? "" : "";

    internal async Task<string> KaliteEkiAsync(ShrinkRequest request, string cikti, CancellationToken ct)
    {
        var job = new AktifIs(request);
        _aktif.Add(job);
        RefreshActive(true);
        try
        {
            return await KaliteEkiAsync(job, request.Path, cikti, ct);
        }
        finally
        {
            _aktif.Remove(job);
            RefreshActive(true);
        }
    }

    private async Task<string> KaliteEkiAsync(AktifIs job, string kaynak, string cikti, CancellationToken ct)
    {
        var kultur = Strings.CultureOf(_language);
        void Yaz(double kesir)
        {
            if (!_aktif.Contains(job)) return;
            job.Fraction = kesir;
            job.Yuzde = KaliteSatiri.Yuzde(kesir, kultur);
            RefreshActive(false);
        }
        job.Stage = Say("main.output.quality-check");
        job.Remaining = "-";
        Yaz(0);
        try
        {
            return await KaliteSatiri.OlcAsync(KaliteOlcer, kaynak, cikti, new ArayuzIlerlemesi(Yaz),
                olcum => KaliteSatiri.Metin(olcum, Say, kultur), Say("main.run.quality.failed"), KaliteGunlugu, ct);
        }
        finally
        {
            job.Yuzde = null;
        }
    }

    /// <summary>Bir işin sonucunu sıra sonundaki habere yazar.</summary>
    internal void IsBitti(string yol, IsSonucu sonuc) => _bitenler.Add(new BitenIs(yol, sonuc));

    /// <summary>
    /// Sıra boşaldığında, son haberden beri biten işler için tek haber. Karar
    /// <see cref="IsBittiBildirimi.Karar"/>'da; burası yalnız sonucu sistem yüzüne taşır.
    /// </summary>
    internal void HaberVer()
    {
        var karar = IsBittiBildirimi.Karar(_bitenler.ToList(), PencereEtkin(), _appSettings.NotifyWhenDone,
            (key, args) => string.Format(Strings.CultureOf(_language), Say(key), args));
        if (IsBittiBildirimi.SesKarari(_bitenler.ToList(), BitisSesi) is bool hata)
            Ses.Cal(hata, BitisSesiDosyasi.Length > 0 ? BitisSesiDosyasi : null);
        _bitenler.Clear();
        if (karar is not null) Bildirim.Bildir(this, karar);
    }

    public ShrinkJobWindow() : this(new ShellShrinkStartup(null, ShrinkArgumentProblem.NoTarget, null), null)
    {
    }

    internal ShrinkJobWindow(ShellShrinkStartup startup, ShrinkRequestQueue? queue)
    {
        _startup = startup;
        _queue = queue;
        _pump = new JobPump<ShrinkRequest>(RunAwakeAsync, () => ParallelLimit);
        _pump.Changed += SaveQueue;
        UykuyuEngelle = _appSettings.PreventSleepWhileEncoding;
        BitisSesi = _appSettings.FinishSound;
        BitisSesiDosyasi = _appSettings.FinishSoundFile;
        _pump.Drained += OnDrained;

        var language = MainWindow.ResolveLanguage(null, CultureInfo.CurrentUICulture.Name);
        try
        {
            var settings = UpdateSettings.Load();
            language = MainWindow.ResolveLanguage(settings.Language, CultureInfo.CurrentUICulture.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        _language = language;
        Strings.Use(_language);

        InitializeComponent();
        if (Playback.HoverZone.MotionReduced) Classes.Add("reduced-motion");

        FlowDirection = Strings.IsRightToLeftLanguage(_language)
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;
        Classes.Set("untracked", Strings.IsUntrackedLanguage(_language));

        if (OperatingSystem.IsMacOS()) WindowDecorations = WindowDecorations.Full;
        JobShell.PointerPressed += OnShellPointerPressed;

        BtnClose.Click += (_, _) => Close();
        BtnOpenInApp.Click += OnOpenInApp;
        BtnReveal.Click += OnReveal;
        BtnShare.Click += OnShare;
        BtnShareCancel.Click += OnShareCancel;
        BtnShareCopy.Click += OnCopyShareLink;
        BtnPause.Click += (_, _) => SetPaused(!_paused);
        BtnCountdownCancel.Click += (_, _) =>
        {
            if (_scheduleAt is not null) CancelSchedule();
            else CancelCountdown();
        };
        BtnOvershootAccept.Click += (_, _) => AnswerOvershoot(true);
        BtnOvershootStop.Click += (_, _) => AnswerOvershoot(false);
        WireWatch();
        WireSchedule();
        WireEdit();
        CmbWhenDone.ItemsSource = new[]
        {
            Say("main.shrink-job.done.nothing"),
            Say("main.shrink-job.done.open-folder"),
            Say("main.shrink-job.done.sleep"),
            Say("main.shrink-job.done.power-off"),
            Say("main.shrink-job.done.quit")
        };
        CmbWhenDone.SelectedIndex = 0;
        CmbWhenDone.SelectionChanged += (_, _) => _whenDone = (QueueEndChoice)Math.Max(0, CmbWhenDone.SelectedIndex);

        TxtHeadline.Text = Say("main.shrink-job.waiting");
        TxtTarget.Text = "";
        TxtStage.Text = "-";
        TxtRemaining.Text = "-";
        TxtMessage.Text = "";
        TxtNotice.Text = "";
    }

    /// <summary>
    /// C1-3: ana pencereye birden çok video ya da klasör bırakıldığında açılan kuyruk. Her dosya
    /// pencerenin o anki seçeneklerinin kopyasıyla kurulur; boyut tavanı olmayan yongada hedef
    /// dosyanın kendi kalite tavanından hesaplanır. <paramref name="extension"/> ön ayar kabından gelir.
    /// </summary>
    internal ShrinkJobWindow(IReadOnlyList<string> paths, PlanOptions template, bool ceilingTarget, string? extension, bool autoCrop = false)
        : this(new ShellShrinkStartup(paths.Select(path => new ShrinkRequest(0, path)).ToList(), null, paths.FirstOrDefault()), null)
    {
        _template = template;
        _ceilingTarget = ceilingTarget;
        _extension = extension;
        _autoCrop = autoCrop;
        TxtHeadline.Text = Say("main.shrink-job.batch", paths.Count);
    }

    private bool _autoCrop;

    /// <summary>
    /// SIK-P9: ölmüş süreçten kalan kuyruk. Soru sorulmaz: pencere kendiliğinden, duraklatılmış açılır;
    /// koşarken kesilen iş baştan ve en önde gelir. Defter boş ya da bozuksa <c>null</c>.
    /// </summary>
    internal static ShrinkJobWindow? Restore(QueueJournal journal, Func<int, bool> alive)
    {
        var jobs = journal.TakeOrphans(alive);
        if (jobs.Count == 0) return null;
        var pairs = jobs.Select(job => (new ShrinkRequest((int)Math.Ceiling(job.TargetMb), job.Path), job)).ToList();
        return new ShrinkJobWindow(pairs) { Defter = journal };
    }

    private ShrinkJobWindow(IReadOnlyList<(ShrinkRequest Request, SavedJob Job)> jobs)
        : this(new ShellShrinkStartup(jobs.Select(pair => pair.Request).ToList(), null, jobs[0].Job.Path), null)
    {
        foreach (var (request, job) in jobs) _geriGelen[request] = job;
        TxtHeadline.Text = Say("main.shrink-job.batch", jobs.Count);
        SetPaused(true, Say("main.shrink-job.restored"));
    }

    private SavedJob Saved(ShrinkRequest request)
        => _duzenlenen.TryGetValue(request, out var edited) ? new SavedJob(request.Path, edited.Mb, false)
            : _geriGelen.TryGetValue(request, out var job) ? job
            : _template is null ? new SavedJob(request.Path, request.TargetMegabytes, false)
            : new SavedJob(request.Path, _template.TargetMb, _ceilingTarget);

    private void SaveQueue()
    {
        if (_defterKapandi || Defter is not { } defter) return;
        defter.Update(this, _pump.Running.Concat(_pump.Pending).Select(Saved).ToList());
    }

    /// <summary>
    /// SIK-P9: pencere deftere son kez dokunur. Kullanıcı kapattıysa kuyruk silinir; uygulama ya da
    /// sistem kapanırken (<paramref name="keep"/>) dosya kalır ki açılışta geri gelsin.
    /// </summary>
    internal void CloseJournal(bool keep)
    {
        if (_defterKapandi) return;
        _defterKapandi = true;
        if (!keep) Defter?.Remove(this);
    }

    /// <summary>
    /// HB #36: kuyruk "Siyah bantları kırp" kutusunu bayrak olarak taşır, dikdörtgeni değil —
    /// bantlar dosyaya özgü. Her dosya kendi yoklamasıyla kırpılır; elle yazılmış <c>crop=</c> korunur.
    /// </summary>
    internal async Task<PlanOptions> KirpmaylaAsync(PlanOptions options, MediaInfo info, CancellationToken ct)
    {
        if (!_autoCrop || options.Filters.Crop is not null) return options;
        options.Filters = OtomatikKirpma.Uygula(options.Filters, true, await OtomatikKirpma.BulAsync(info, ct));
        return options;
    }

    /// <summary>İsteğin plan seçenekleri ve hedefi. Kabuk isteği yalnız hedefi taşır; bırakılan kuyruk pencerenin ayarlarını.</summary>
    internal (PlanOptions Options, double TargetMb) OptionsFor(ShrinkRequest request, MediaInfo info)
    {
        if (_duzenlenen.TryGetValue(request, out var edited)) return (EditedOptions(edited.Mb, edited.Codec), edited.Mb);
        if (_geriGelen.TryGetValue(request, out var saved))
        {
            var hedef = saved.CeilingTarget ? PlanCalculator.QualityCeilingTargetMb(info) : saved.TargetMb;
            return (new PlanOptions
            {
                TargetMb = hedef,
                Priority = _appSettings.EncodePriority,
                EncoderThreads = _appSettings.EncoderThreads > 0 ? _appSettings.EncoderThreads : null
            }, hedef);
        }
        if (_template is null)
            return (new PlanOptions
            {
                TargetMb = request.TargetMegabytes,
                Priority = _appSettings.EncodePriority,
                EncoderThreads = _appSettings.EncoderThreads > 0 ? _appSettings.EncoderThreads : null
            }, request.TargetMegabytes);
        var targetMb = _ceilingTarget ? PlanCalculator.QualityCeilingTargetMb(info) : _template.TargetMb;
        return (PlanCalculator.WithTarget(_template, targetMb), targetMb);
    }

    internal string ExtensionFor(EncodePlan plan) => _extension ?? plan.Streams?.Extension ?? "mp4";

    private string TargetLabel(double targetMb)
        => _template is null
            ? ShellIntegration.FormatQuickShrinkLabel((int)targetMb)
            : Bicim.Boyut.Hedef(targetMb, Strings.CultureOf(_language)) + " MB";

    private void OnShellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    /// <summary>
    /// Bu pencerenin metinlerini urettigi dil. Kurulusta bir kez cozulur ve bir daha
    /// degismez: <see cref="Say(string)"/> surec genelindeki <see cref="Strings.Language"/>
    /// degerini okumaz, yoksa baska bir is parcaciginda kosan bir olcu dili degistirdiginde
    /// pencerenin metni koşudan koşuya kayar.
    /// </summary>
    internal string Language => _language;

    /// <summary>Pencerenin gorunur durumu; olcu buna bakar, piksele degil.</summary>
    internal ShrinkJobState State { get; private set; } = ShrinkJobState.Bekliyor;

    /// <summary>Bir dosyanın işi bitti: kaynak yolu ve teslim edilen çıktı; başarısız ya da iptal edilen işte çıktı <c>null</c>.</summary>
    internal event Action<string, string?>? JobFinished;

    /// <summary>Bu pencerenin teslim ettigi cikti yollari, kabul sirasiyla.</summary>
    internal IReadOnlyList<string> Outputs => _outputs;

    /// <summary>Kuyruga girmis istek sayisi; ikinci surecin boruyla verdikleri dahil.</summary>
    internal int AcceptedCount => _accepted;

    /// <summary>Kodlamasız koşan iş: başlık ve "dosya N / toplam" satırı <c>RunOneAsync</c>'teki gibi kurulur.</summary>
    internal void ShowActiveForTest(ShrinkRequest request, double targetMb)
    {
        _aktif.Add(new AktifIs(request) { TargetLabel = TargetLabel(targetMb), Order = ++_baslayan });
        RefreshActive(true);
    }

    internal string TargetLineForTest => TxtTarget.Text ?? "";

    internal string MessageText => TxtMessage.Text ?? "";

    /// <summary>Henüz başlamamış istekler, koşacakları sırayla. Koşan dosya burada değil.</summary>
    internal IReadOnlyList<ShrinkRequest> Pending => _pump.Pending;

    internal bool Paused => _paused;

    internal string PausedText => TxtPaused.Text ?? "";

    internal QueueEndChoice WhenDone
    {
        get => _whenDone;
        set => CmbWhenDone.SelectedIndex = (int)value;
    }

    /// <summary>Geri sayımda kalan saniye; geri sayım yoksa <c>null</c>.</summary>
    internal int? CountdownLeft => _countdownLeft;

    internal string CountdownText => TxtCountdown.Text ?? "";

    /// <summary>Bulunamayan yollar icin basilan uyari satiri; bos ise satir gorunmez.</summary>
    internal string NoticeText => TxtNotice.Text ?? "";

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Begin();
    }

    /// <summary>
    /// Isteklerin tuketildigi yer. Gerekce varsa pencere kalir; ayni argv'den gelen her
    /// istek kuyruga girer ve kuyrugun sahibiysek boru da acilir, ikinci surecin istegi
    /// buraya duser.
    /// </summary>
    internal void Begin()
    {
        if (_started) return;
        _started = true;

        ShowMissing();

        if (_startup.Problem is { } problem)
        {
            QueueControls.IsVisible = false;
            ShowProblem(problem);
            return;
        }

        foreach (var request in _startup.Items) Accept(request);
        RestoreWatch();
        _diskTimer = new DispatcherTimer { Interval = DiskCheckInterval };
        _diskTimer.Tick += (_, _) => CheckDiskSpace();
        _diskTimer.Start();

        if (_queue is null) return;
        try
        {
            _queue.StartOwning(incoming => Dispatcher.UIThread.Post(() => Accept(incoming)));
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Diskte bulunamayan argv parcalari. Kodlama satirinin ustunde ayri bir satirda durur:
    /// <c>TxtMessage</c> her istekte silindigi icin uyari orada kaybolurdu.
    /// </summary>
    private void ShowMissing()
    {
        if (_startup.Missing.Count == 0) return;
        TxtNotice.Text = Say("main.shrink-job.missing-paths",
            _startup.Missing.Count,
            string.Join(", ", _startup.Missing));
        TxtNotice.IsVisible = true;
    }

    private void Accept(ShrinkRequest request)
    {
        _accepted++;
        _pump.Enqueue(request);
        RefreshPending();
        RefreshActive(false);
    }

    /// <summary>C1-2: bekleyen isteği sıradan çıkarır. Koşan dosyaya dokunmaz.</summary>
    internal void RemovePending(int index)
    {
        if (!_pump.RemovePending(index)) return;
        _accepted--;
        RefreshPending();
        RefreshActive(false);
    }

    /// <summary>C1-2: bekleyen isteği <paramref name="delta"/> kadar kaydırır; sınırın dışına taşımaz.</summary>
    internal void MovePending(int index, int delta)
    {
        if (!_pump.MovePending(index, delta)) return;
        RefreshPending();
    }

    /// <summary>
    /// C1-2, SIK-P3: duraklatılan kuyrukta koşan kodlama askıya alınır, sıradaki başlamaz. Sürdürünce
    /// askı kalkar ve pompa yeniden açılır.
    /// </summary>
    internal void SetPaused(bool paused)
    {
        if (!paused) ClearSchedule();
        SetPaused(paused, Say("main.shrink-job.paused"));
    }

    private void SetPaused(bool paused, string text)
    {
        _paused = paused;
        BtnPause.Content = Say(paused ? "main.shrink-job.resume" : "main.shrink-job.pause");
        TxtPaused.Text = text;
        TxtPaused.IsVisible = paused;
        if (paused) EncodePause.Pause();
        else EncodePause.Resume();
        _pump.Paused = paused;
        RefreshPending();
    }

    /// <summary>
    /// SIK-P3: koşan işlerin çıktı sürücüsünde boş alan <see cref="DiskSpaceGuard.PauseBelowBytes"/>
    /// altına inmişse kuyruk kendiliğinden duraklar. Okunamayan sürücü duraklatmaz.
    /// </summary>
    internal void CheckDiskSpace()
    {
        if (_paused) return;
        foreach (var folder in _pump.Running.Select(OutputFolderFor).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (FreeSpace(folder) is not long free || !DiskSpaceGuard.ShouldPause(free)) continue;
            PauseForDisk(free);
            return;
        }
    }

    /// <summary>
    /// SIK-P3: iş başlamadan çıktının sürücüsünde yer var mı. Yoksa kuyruk duraklar ve işin sonuç
    /// satırı döner; varsa ya da sürücü okunamazsa <c>null</c>.
    /// </summary>
    internal string? SpaceBeforeStart(string output, double targetMb)
    {
        if (FreeSpace(output) is not long free || DiskSpaceGuard.HasEnoughSpace(free, targetMb)) return null;
        PauseForDisk(free);
        return Say("main.run.no-space", Megabayt.Oku(DiskSpaceGuard.RequiredBytes(targetMb)).ToString("0", Strings.CultureOf(_language)));
    }

    private void PauseForDisk(long freeBytes)
        => SetPaused(true, Say("main.shrink-job.disk-paused", (long)Math.Floor(freeBytes / Megabayt.Bayt)));

    private string OutputFolderFor(ShrinkRequest request)
        => MainWindow.UsableFixedFolder(_appSettings.OutputFolderMode, _appSettings.OutputFolder)
           ?? Path.GetDirectoryName(request.Path) ?? "";

    private void RefreshPending()
    {
        var pending = _pump.Pending;
        PendingPanel.IsVisible = pending.Count > 0;
        TxtPending.Text = Say("main.shrink-job.pending", pending.Count);
        PendingList.Children.Clear();
        for (var i = 0; i < pending.Count; i++) PendingList.Children.Add(PendingRow(pending, i));
    }

    /// <summary>SIK-24: hata veren işler, bitiş sırasıyla. Satırdaki düğme işi tek tıkla yeniden sıraya alır.</summary>
    internal IReadOnlyList<ShrinkRequest> FailedJobs => _dusenler;

    internal void Dustu(ShrinkRequest request)
    {
        _dusenler.Add(request);
        RefreshFailed();
    }

    /// <summary>SIK-24: düşen işi listeden çıkarıp aynı seçenekleriyle sıranın sonuna ekler.</summary>
    internal void RetryFailed(int index)
    {
        if (index < 0 || index >= _dusenler.Count) return;
        var request = _dusenler[index];
        _dusenler.RemoveAt(index);
        RefreshFailed();
        CancelCountdown();
        Accept(request);
    }

    private void RefreshFailed()
    {
        FailedPanel.IsVisible = _dusenler.Count > 0;
        TxtFailed.Text = Say("main.shrink-job.failed", _dusenler.Count);
        FailedList.Children.Clear();
        for (var i = 0; i < _dusenler.Count; i++)
        {
            var index = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Token("SpaceXs") };
            var name = new SarilanMetin
            {
                Text = Path.GetFileName(_dusenler[i].Path),
                FontSize = Token("FontSizeSm"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(name, _dusenler[i].Path);
            row.Children.Add(name);
            AddRowButton(row, 1, "IconRestart", "main.shrink-job.retry", true, () => RetryFailed(index));
            FailedList.Children.Add(row);
        }
    }

    private Grid PendingRow(IReadOnlyList<ShrinkRequest> pending, int index)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = Token("SpaceXs") };
        var name = new SarilanMetin
        {
            Text = Path.GetFileName(pending[index].Path),
            FontSize = Token("FontSizeSm"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(name, pending[index].Path);
        row.Children.Add(name);
        AddRowButton(row, 1, "IconChevronUp", "main.shrink-job.move-up", index > 0, () => MovePending(index, -1));
        AddRowButton(row, 2, "IconChevronDown", "main.shrink-job.move-down", index < pending.Count - 1, () => MovePending(index, 1));
        AddRowButton(row, 3, "IconSettings", "main.shrink-job.edit", true, () => BeginEdit(index));
        AddRowButton(row, 4, "IconClose", "main.shrink-job.remove", true, () => RemovePending(index));
        return row;
    }

    private void AddRowButton(Grid row, int column, string icon, string key, bool enabled, Action click)
    {
        var size = Token("IconSizeSm");
        var button = new Button
        {
            Content = new PathIcon { Data = this.FindResource(icon) as Geometry, Width = size, Height = size },
            Padding = this.FindResource("ChipPadding") is Avalonia.Thickness padding ? padding : default,
            IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, Say(key));
        ToolTip.SetTip(button, Say(key));
        button.Click += (_, _) => click();
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private double Token(string key) => this.FindResource(key) is double value ? value : 0;

    private void ShowProblem(ShrinkArgumentProblem problem)
    {
        State = ShrinkJobState.Gerekce;
        TxtHeadline.Text = Say("main.shrink-job.rejected");
        TxtTarget.Text = "";
        RowFacts.IsVisible = false;
        Progress.IsVisible = false;
        TxtMessage.Text = problem == ShrinkArgumentProblem.TargetNotInQuickList
            ? Say(ShrinkProblemText.Key(problem), ShrinkProblemText.QuickList())
            : Say(ShrinkProblemText.Key(problem));
        BtnOpenInApp.IsVisible = true;
    }

    /// <summary>
    /// K6: pompanın "boşaldı" olayı. Kaç iş aynı anda koşmuş olursa olsun haber ve kuyruk sonu
    /// eylemi bütün işler bitince bir kez gelir. Kapanan pencere eylem başlatmaz.
    /// </summary>
    private void OnDrained()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnDrained);
            return;
        }
        if (_closed) return;
        HaberVer();
        QueueDrained();
    }

    /// <summary>K6: koşan işin satırı — ad, kalan süre, iptal düğmesi ve altında kendi çubuğu.</summary>
    private Grid ActiveRow(AktifIs job)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = Token("SpaceXs"),
            RowSpacing = Token("SpaceXs")
        };
        var name = new SarilanMetin
        {
            Text = Path.GetFileName(job.Request.Path),
            FontSize = Token("FontSizeSm"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(name, job.Request.Path);
        row.Children.Add(name);
        job.Kalan = new SarilanMetin { Text = job.Remaining, FontSize = Token("FontSizeSm"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(job.Kalan, 1);
        row.Children.Add(job.Kalan);
        AddRowButton(row, 2, "IconClose", "main.action.cancel", true, () => CancelJob(job.Request));
        job.Bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = job.Fraction, Height = Token("ProgressBarHeight") };
        Grid.SetRow(job.Bar, 1);
        Grid.SetColumnSpan(job.Bar, 3);
        row.Children.Add(job.Bar);
        return row;
    }

    /// <summary>
    /// Koşan işleri ekrana yazar. Tek iş koşarken pencere bugünkü gibidir; birden çoğunda başlık
    /// sayıyı, çubuk ortalamayı gösterir ve her işin kendi satırı olur. Satırlar yalnız sınır
    /// 1'den büyükken görünür.
    /// </summary>
    private void RefreshActive(bool rebuild)
    {
        var rows = ParallelLimit > ParallelJobs.Default && _aktif.Count > 0;
        ActivePanel.IsVisible = rows;
        if (rebuild)
        {
            ActiveList.Children.Clear();
            if (rows)
                foreach (var job in _aktif) ActiveList.Children.Add(job.Row ??= ActiveRow(job));
        }
        if (_aktif.Count == 0)
        {
            TxtProgressPercent.IsVisible = false;
            return;
        }

        foreach (var job in _aktif)
        {
            if (job.Bar is { } bar) bar.Value = job.Fraction;
            if (job.Kalan is { } kalan) kalan.Text = job.Yuzde ?? job.Remaining;
        }

        var many = _aktif.Count > 1;
        Progress.IsVisible = true;
        RowFacts.IsVisible = !many;
        TxtProgressPercent.IsVisible = !many && _aktif[0].Yuzde is not null;
        TxtProgressPercent.Text = many ? "" : _aktif[0].Yuzde ?? "";
        if (many)
        {
            TxtHeadline.Text = Say("main.shrink-job.running", _aktif.Count);
            TxtTarget.Text = TargetText(_aktif[^1]);
            Progress.Value = _aktif.Average(job => job.Fraction);
            return;
        }

        var tek = _aktif[0];
        TxtHeadline.Text = Path.GetFileName(tek.Request.Path);
        TxtTarget.Text = TargetText(tek);
        Progress.Value = tek.Fraction;
        TxtStage.Text = tek.Stage;
        TxtRemaining.Text = tek.Remaining;
    }

    /// <summary>
    /// "Dosya N / toplam". Toplam iş başlarken dondurulmuyor: kabuktan gelen istekler ilk iş
    /// koşarken sıraya düşüyor ve dondurulan başlık "1 / 1" derken altında "Bekleyen: 2" yazıyordu.
    /// </summary>
    internal double? TepsiKesri => _aktif.Count > 0 ? Progress.Value : null;

    private string TargetText(AktifIs job)
        => job.TargetLabel is null ? "" : Say("main.shrink-job.target", job.TargetLabel, job.Order, _accepted);

    /// <summary>
    /// C1-4: sıra boşaldığında seçilen eylem. Uyut ve kapat geri sayımla gelir ve pencere bu
    /// sırada kapanmaz; klasör açma son çıktının klasörünü açar.
    /// </summary>
    internal void QueueDrained()
    {
        if (_pump.PendingCount > 0 || _paused || _pump.RunningCount > 0) return;
        switch (_whenDone)
        {
            case QueueEndChoice.Sleep:
            case QueueEndChoice.PowerOff:
                StartCountdown();
                return;
            case QueueEndChoice.Quit:
                if (_quitAsked) return;
                _quitAsked = true;
                ReleaseSleep();
                Actions.Quit();
                return;
            case QueueEndChoice.OpenFolder when _outputs.Count > 0:
                Reveal(_outputs[^1]);
                break;
        }
        if (State == ShrinkJobState.Bitti) ArmClose();
    }

    private void StartCountdown()
    {
        _countdownLeft = CountdownSeconds;
        ShowCountdown();
        _countdown ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick -= OnCountdownTick;
        _countdown.Tick += OnCountdownTick;
        _countdown.Start();
    }

    private void OnCountdownTick(object? sender, EventArgs e) => CountdownTick();

    /// <summary>Geri sayımın bir saniyesi. Sıfıra inince eylem bir kez çalışır.</summary>
    internal void CountdownTick()
    {
        if (_countdownLeft is not int left) return;
        left--;
        if (left > 0)
        {
            _countdownLeft = left;
            ShowCountdown();
            return;
        }
        var choice = _whenDone;
        CancelCountdown();
        ReleaseSleep();
        if (choice == QueueEndChoice.Sleep) Actions.Sleep();
        else if (choice == QueueEndChoice.PowerOff) Actions.PowerOff();
    }

    internal void CancelCountdown()
    {
        _countdown?.Stop();
        _countdownLeft = null;
        CountdownRow.IsVisible = false;
    }

    private void ShowCountdown()
    {
        CountdownRow.IsVisible = true;
        TxtCountdown.Text = Core.Bicim.Satir.Bagla(Say(_whenDone == QueueEndChoice.Sleep ? "main.shrink-job.countdown.sleep" : "main.shrink-job.countdown.power-off", _countdownLeft));
    }

    /// <summary>
    /// SIK-P2: istek ilk iş başlarken alınır. Sıradaki iş hemen başlayacaksa bırakılmaz; son koşan iş
    /// bitince, iptalde ya da duraklatılmış kuyrukta koşan kalmayınca geri verilir.
    /// </summary>
    private async Task RunAwakeAsync(ShrinkRequest request, CancellationToken ct)
    {
        if (UykuyuEngelle && Volatile.Read(ref _uykuIstegi) is null)
        {
            var istek = Uyku.Hold();
            if (Interlocked.CompareExchange(ref _uykuIstegi, istek, null) is not null) istek.Dispose();
        }
        try
        {
            await (Kosturucu ?? RunOneAsync)(request, ct);
        }
        finally
        {
            if (_pump.RunningCount <= 1 && (_pump.PendingCount == 0 || _pump.Paused)) ReleaseSleep();
        }
    }

    private void ReleaseSleep() => Interlocked.Exchange(ref _uykuIstegi, null)?.Dispose();

    private async Task RunOneAsync(ShrinkRequest request, CancellationToken ct)
    {
        var job = new AktifIs(request);
        var sira = ++_baslayan;
        _aktif.Add(job);
        State = ShrinkJobState.Kosuyor;
        BtnReveal.IsVisible = false;
        ResetShare(false);
        TxtMessage.Text = "";
        RefreshPending();
        RefreshActive(true);

        var sonuc = IsSonucu.Hatali;
        var son = ShrinkJobState.Hata;
        string? teslim = null;
        string? ayrilan = null;
        string? mesaj = null;
        QualitySearchResult? arama = null;
        var uygulamadaAc = false;
        IDisposable? yuva = null;
        try
        {
            var info = await FfprobeClient.ProbeAsync(request.Path, ct);
            var (options, targetMb) = OptionsFor(request, info);
            options = await KirpmaylaAsync(options, info, ct);
            (options, targetMb, arama) = await AramaylaAsync(request, info, options, targetMb, tur => AramaTuru(job, tur), ct);
            job.TargetLabel = TargetLabel(targetMb);
            job.Order = sira;
            RefreshActive(false);
            var plan = PlanCalculator.Build(info, options);
            var output = OutputReservations.Shared.Reserve(taken =>
                UniqueOutputPath(request.Path, _appSettings, plan, targetMb, ExtensionFor(plan), taken, sira));
            ayrilan = output;
            QueueWatch.MarkOwn(output);
            if (SpaceBeforeStart(output, targetMb) is { } alanYok)
            {
                mesaj = alanYok;
                return;
            }
            yuva = await EncoderSlots.Shared.EnterAsync(plan.Codec, ct);

            var progress = new Progress<EncodeProgress>(step => ShowProgress(job, step));
            var soruAdi = ParallelLimit > ParallelJobs.Default ? Path.GetFileName(request.Path) : null;

            var result = await new EncodeRunner { Pause = EncodePause, KaynakTarihleriniKoru = _appSettings.KeepSourceDates }.RunAsync(
                info, plan, output, targetMb, progress, ct, options.FillPolicy,
                askBeforeRetry: (prompt, token) => AskOvershootAsync(soruAdi, prompt, token));

            if (result.Success)
            {
                QueueWatch.MarkOwn(result.OutputPath);
                _outputs.Add(result.OutputPath);
                teslim = result.OutputPath;
                son = ShrinkJobState.Bitti;
                sonuc = IsSonucu.Basarili;
                mesaj = BittiSatiri(result, targetMb, plan) + AramaOzeti(arama);
                if (KaliteyiOlc && await KaliteEkiAsync(job, request.Path, result.OutputPath, ct) is { Length: > 0 } kalite)
                    mesaj += " " + kalite;
            }
            else
            {
                mesaj = HataSatiri(result, targetMb, plan);
                uygulamadaAc = true;
            }
        }
        catch (OperationCanceledException)
        {
            sonuc = IsSonucu.Iptal;
            mesaj = Say("main.run.cancelled");
        }
        catch (Exception ex)
        {
            mesaj = ex.Message;
            uygulamadaAc = true;
        }
        finally
        {
            yuva?.Dispose();
            if (ayrilan is not null) OutputReservations.Shared.Release(ayrilan);
            _finished++;
            IsBitti(request.Path, sonuc);
            if (sonuc == IsSonucu.Hatali) Dustu(request);
            _aktif.Remove(job);
            RefreshActive(true);
            if (_aktif.Count == 0)
            {
                RowFacts.IsVisible = true;
                TxtHeadline.Text = Path.GetFileName(request.Path);
                if (teslim is not null) Progress.Value = 1;
            }
            State = _aktif.Count > 0 ? ShrinkJobState.Kosuyor : son;
            TxtMessage.Text = mesaj ?? "";
            if (uygulamadaAc) BtnOpenInApp.IsVisible = true;
            if (teslim is not null)
            {
                BtnReveal.IsVisible = true;
                ResetShare(true);
            }
            JobFinished?.Invoke(request.Path, teslim);
        }
    }

    /// <summary>
    /// Motorun ilerleme satırı. Aşama adı motorun İngilizce belirteçleriyle gelir; ana pencereyle
    /// aynı sözlükten ve bu pencerenin dilinde yazılır.
    /// </summary>
    internal void ShowProgress(EncodeProgress step)
    {
        Progress.Value = step.Fraction;
        TxtStage.Text = MainWindow.LocalizeStageIn(step.Stage, _language);
        TxtRemaining.Text = Saat.Kalan(step.Remaining);
    }

    /// <summary>K6: ilerleme işin kendi kaydına yazılır; biten işin geç gelen satırı ekrana dokunmaz.</summary>
    private void ShowProgress(AktifIs job, EncodeProgress step)
    {
        if (!_aktif.Contains(job)) return;
        job.Fraction = step.Fraction;
        job.Stage = MainWindow.LocalizeStageIn(step.Stage, _language);
        job.Remaining = Saat.Kalan(step.Remaining);
        RefreshActive(false);
    }

    internal string StageText => TxtStage.Text ?? "";

    /// <summary>
    /// Kullanıcıya sorulmadan verilebilecek cevap. Deneme hakkı varken motor ana pencerede de
    /// yeniden dener; hedefin altında kalmış bir sonuç varsa "bırak" onu teslim eder. Geriye
    /// yalnız hedefi aşan dosyayı teslim etmek kalıyorsa <c>null</c>: o karar kullanıcının.
    /// </summary>
    internal static OvershootChoice? UnaskedChoice(RetryPrompt prompt)
        => prompt.CanRetry ? OvershootChoice.Retry
            : prompt.HasUnderBandFallback ? OvershootChoice.Leave
            : null;

    private TaskCompletionSource<OvershootChoice>? _overshootDecision;

    internal bool OvershootAsked => OvershootPanel.IsVisible;

    /// <summary>
    /// Ana penceredeki aşım sorusunun bu penceredeki karşılığı. Eskiden motor burada sorusuz
    /// koşuyordu ve hedefi aşan en küçük dosyayı teslim ediyordu; "hedeften büyük dosya asla
    /// verilmez" diyen satırla çelişiyordu.
    /// </summary>
    internal Task<OvershootChoice> AskOvershootAsync(RetryPrompt prompt, CancellationToken ct)
        => AskOvershootAsync(null, prompt, ct);

    /// <summary>
    /// K6: aynı anda iki iş sorabilir; panel tek, bu yüzden sorular sıraya girer ve her biri
    /// <paramref name="dosya"/> verilmişse hangi dosyayı sorduğunu ilk satırda söyler.
    /// </summary>
    internal async Task<OvershootChoice> AskOvershootAsync(string? dosya, RetryPrompt prompt, CancellationToken ct)
    {
        if (UnaskedChoice(prompt) is { } unasked) return unasked;

        await _overshootGate.WaitAsync(ct);
        try
        {
            var decision = new TaskCompletionSource<OvershootChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
            _overshootDecision = decision;
            await Dispatcher.UIThread.InvokeAsync(() => ShowOvershoot(prompt, dosya));
            using var cancellation = ct.Register(() => decision.TrySetCanceled(ct));
            try
            {
                return await decision.Task;
            }
            finally
            {
                _overshootDecision = null;
                await Dispatcher.UIThread.InvokeAsync(HideOvershoot);
            }
        }
        finally
        {
            _overshootGate.Release();
        }
    }

    private void ShowOvershoot(RetryPrompt prompt, string? dosya)
    {
        var kultur = Strings.CultureOf(_language);
        TxtOvershoot.Text = (dosya is null ? "" : dosya + Environment.NewLine) + Say("main.retry.outcome",
            prompt.Attempt,
            prompt.MaxAttempts,
            Bicim.Boyut.Mb(prompt.ActualMb, kultur),
            Bicim.Boyut.Hedef(prompt.TargetMb, kultur),
            Bicim.Boyut.Mb(prompt.OverMb, kultur),
            Bicim.Yuzde.Hazir(prompt.OverPercent, kultur),
            Saat.Ekran(prompt.AttemptDuration));
        TxtOvershootMeaning.Text = Say("main.retry.meaning-without-fallback");
        var kabul = Say("main.retry.accept", Bicim.Boyut.Mb(prompt.ActualMb, kultur));
        BtnOvershootAccept.Content = kabul;
        AutomationProperties.SetName(BtnOvershootAccept, kabul);
        TxtStage.Text = Say("main.output.waiting");
        OvershootPanel.IsVisible = true;
    }

    private void HideOvershoot() => OvershootPanel.IsVisible = false;

    internal void AnswerOvershoot(bool accept)
        => _overshootDecision?.TrySetResult(accept ? OvershootChoice.AcceptLarger : OvershootChoice.Leave);

    private void ArmClose()
    {
        if (_closeTimer is not null) return;
        _closeTimer = new DispatcherTimer { Interval = Linger };
        _closeTimer.Tick += (_, _) =>
        {
            if ((_shareFlow?.Running ?? false) || _countdownLeft is not null || Watching) return;
            _closeTimer?.Stop();
            if (_pump.PendingCount == 0 && _pump.RunningCount == 0) Close();
        };
        _closeTimer.Start();
    }

    private void OnOpenInApp(object? sender, RoutedEventArgs e)
    {
        var window = new MainWindow(_startup.FallbackPath ?? _startup.Request?.Path);
        window.Show();
        Close();
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_outputs.Count > 0) Reveal(_outputs[^1]);
    }

    private void Reveal(string path)
    {
        try
        {
            Actions.Reveal(path);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            TxtMessage.Text = Say("main.error.folder");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closeTimer?.Stop();
        _countdown?.Stop();
        _scheduleTimer?.Stop();
        _diskTimer?.Stop();
        StopWatch();
        _closed = true;
        CloseJournal(e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown);
        _pump.CancelAll();
        ReleaseSleep();
        _shareFlow?.Cancel();
        _queue?.Dispose();
        base.OnClosing(e);
    }

    /// <summary>
    /// Kuyruk da ana pencereyle aynı yoldan geçer: sabit çıktı klasörü ve adlandırma deseni
    /// burada da okunur. Eskiden bu pencerenin kendi kopyası vardı; ikisi de kullanıcının
    /// ayarını görmüyordu.
    /// </summary>
    private static AppSettings LoadAppSettings()
    {
        try { return AppSettings.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new AppSettings(); }
    }

    internal static string UniqueOutputPath(string inputPath, AppSettings settings, EncodePlan? plan, double targetMb, string extension = "mp4",
        Func<string, bool>? taken = null, int sayac = 1)
        => ShrinkEngine.UniqueOutputPath(
            inputPath,
            "shrunk",
            extension,
            MainWindow.UsableFixedFolder(settings.OutputFolderMode, settings.OutputFolder),
            AdlandirmaDeseni.Uygula(settings.OutputNamePattern, new AdBilgisi(ShrinkEngine.KaynakAdi(inputPath))
            {
                HedefMb = targetMb,
                Crf = plan?.Crf,
                VideoBitrateK = plan?.VideoBitrateK,
                Yukseklik = plan?.Height,
                Kodek = plan?.Codec,
                KareHizi = plan?.Fps > 0 ? plan.Fps : null,
                KaynakKlasoru = ShrinkEngine.KaynakKlasoru(inputPath),
                Sayac = sayac
            }),
            taken);

    /// <summary>
    /// İş bittiğinde yazılan satır. Arayüzden ayrı duruyor ki ölçülebilsin: hedefi aşan
    /// teslimde sapma <see cref="Bicim.Boyut.Sapma"/> ile, pencerenin <b>kendi</b>
    /// dilinin kültüründe yazılıyor. Eskiden burada <c>InvariantCulture</c> vardı ve
    /// Türkçe arayüz sapmayı <c>1.23</c> diye noktayla okuyordu. Aşım, motorun "hedefin
    /// üzerinde" dediği etkin hedefe göre (<see cref="EncodeRunner.EffectiveTargetMb"/>).
    /// </summary>
    internal string BittiSatiri(EncodeResult result, double hedefMb, EncodePlan plan)
    {
        var kultur = Strings.CultureOf(_language);
        if (result.OverTarget)
        {
            var etkinMb = EncodeRunner.EffectiveTargetMb(hedefMb, plan);
            return result.OutputPath + " " + Say("main.run.accepted-larger",
                Bicim.Boyut.Sapma(result.OutputMb - etkinMb, kultur), Bicim.Boyut.Hedef(etkinMb, kultur));
        }
        return MainWindow.ShowsSaturated(result)
            ? result.OutputPath + " " + Say("main.run.saturated", Bicim.Boyut.Mb(result.OutputMb, kultur), Bicim.Boyut.Hedef(hedefMb, kultur))
            : result.OutputPath;
    }

    /// <summary>
    /// İş düştüğünde yazılan satır. Boyutlar ailenin yazımıyla (<see cref="Bicim.Boyut"/>) ve
    /// pencerenin dilinin kültüründe; hedef, motorun altına inemediği etkin hedef.
    /// </summary>
    internal string HataSatiri(EncodeResult result, double hedefMb, EncodePlan plan)
    {
        var kultur = Strings.CultureOf(_language);
        if (result.Kusur is { } kusur)
            return kusur.Cozulemedi
                ? Say("main.run.output-unreadable")
                : Say("main.run.output-short", kusur.OkunanSaniye!.Value.ToString("0.#", kultur), kusur.BeklenenSaniye.ToString("0.#", kultur));
        return result.CeilingExceeded
            ? Say("main.run.over-ceiling", Bicim.Boyut.Hedef(EncodeRunner.EffectiveTargetMb(hedefMb, plan), kultur), result.Attempts, Bicim.Boyut.Mb(result.OutputMb, kultur))
            : Say("main.run.ended");
    }

    private string Say(string key)
        => LanguageCatalog.Title(Strings.GetIn(_language, key), _language);

    private string Say(string key, params object?[] args)
        => LanguageCatalog.Title(Strings.GetIn(_language, key, args), _language);
}
