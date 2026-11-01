using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Core.Playback;

namespace VidShrink.App.Playback;

/// <summary>
/// Paneli gerçek kare kaynağına bağlayan taraf. Panel kaynağı bilmez, kaynak arayüz
/// çizmez; ikisini birbirine bu sınıf takar.
///
/// Akış: <see cref="IComparisonFrameSource.TryTake"/> ile kare alınır, panonun havuzundan
/// kiralanan tampona kopyalanır, <see cref="ComparisonSurface.Submit"/> ile sunuma verilir
/// ve kare aynı turda <see cref="IComparisonFrameSource.Return"/> ile kaynağın havuzuna
/// döner. İade edilmeyen kare havuzu kurutur, bu yüzden iade <c>finally</c> içindedir.
///
/// Sağ yarının üç durumu vardır ve sırası şudur: tam çıktı varsa o gösterilir; yoksa
/// <see cref="SegmentEncoder"/>'ın ürettiği kısa parça çifti gösterilir; o da yoksa perde
/// iner. Parça modunda <b>iki girdi de</b> aynı pencereye kesilmiş dosyalardır — hizanın
/// nasıl kurulduğu <see cref="PreviewClip"/> belgesinde.
/// </summary>
internal sealed class PanelHost : IDisposable
{
    /// <summary>Bunun altındaki panoya akış kurulmaz — ffmpeg'e verilecek anlamlı bir ölçü değil.</summary>
    private const int MinPanelEdge = 64;

    /// <summary>Bu kadarlık bir ölçü değişimi akışı yeniden kurmaya değmez.</summary>
    private const double ResizeTolerance = 8;

    private const int MaxFps = 60;

    /// <summary>Bir sunum turunda en çok kaç kare atlanabileceği — yetişme sınırlı kalsın.</summary>
    private const int CatchUpCeiling = 8;

    /// <summary>Pencerenin bu kadarı oynadığında sonraki pencere kodlanmaya başlar (K3).</summary>
    private const double PrefetchAtFraction = 0.5;

    /// <summary>
    /// Pencerenin sonuna bu kadar kalınca <b>sonraki pencerenin borusu açılır</b> — geçiş
    /// değil, yalnızca hazırlık. Ölçüldü (T50): ffmpeg süreç açılışından ilk kareye ~105 ms
    /// geçiyor, bunun ~32 ms'i sürecin kendisi. Öndeki pay bu ölçünün üstünde tutuluyor.
    ///
    /// <b>Bu pay içerik atlamaz</b>, çünkü geçişi tetikleyen şey o değil: boru burada
    /// açılıyor ama yüzey <see cref="SwapAt"/>'e, yani pencerenin son karesine kadar eski
    /// boruda kalıyor. Eski sabit <c>AdvanceTailSeconds = 0.08</c> ise doğrudan
    /// <b>geçişi</b> tetikliyordu ve pencerenin son 80 ms'i hiç gösterilmiyordu.
    ///
    /// T50 turu 2'de düzeltildi: geçiş bir ara "bekleyen boru ilk kareyi verir vermez"
    /// yapılıyordu ve bu, payın ortasında bir yere düşüyordu — ölçülen ~52 ms'lik yeni bir
    /// atlama. Şimdi hazırlık ile geçiş ayrı iki eşik.
    /// </summary>
    private const double HandoverLeadSeconds = 0.16;

    /// <summary>
    /// Devirden sonra kaç sunum turu <b>sırayla</b> tüketilir. Yeni boru açılış gecikmesini
    /// kapatmak için ilk kareleri topluca veriyor; o turlarda en yenisini almak o kareleri
    /// atlardı. Halka dört gözlü, 60 Hz'de sekiz tur birikimi eritmeye yeter.
    /// </summary>
    private const int OrderedTicksAfterHandover = 8;

    private readonly ComparisonPanel _panel;
    private readonly Func<IComparisonFrameSource> _factory;
    private readonly DispatcherTimer _settle;
    private readonly DispatcherTimer _segmentDelay;
    private readonly SegmentEncoder _segments;
    private readonly PreviewAudio _audio = new();

    private IComparisonFrameSource? _source;
    private double _hiz = 1;
    private string? _left;
    private string? _right;
    private double _aspect = 16.0 / 9.0;
    private TimeSpan _duration;
    private int _fps = 30;
    private PixelSize _panelSize = PixelSize.Empty;

    private Task _restart = Task.CompletedTask;
    private bool _open;
    private bool _awaitingFirst;
    private string _language = Strings.FallbackLanguage;
    private bool _disposed;
    private int _generation;

    private long _submitted;
    private long _windowFrames;
    private long _windowStart;
    private double _screenFps;

    private MediaInfo? _info;
    private EncodePlan? _plan;
    private ComplexityProfile? _profile;
    private PreviewClip? _clip;
    private PreviewClip? _ahead;
    private double _clipStart;
    private bool _clipRunning;
    private bool _aheadRunning;
    private EncodeFailure? _clipFailure;
    private string? _clipSignature;
    private string? _pendingSignature;
    private int _scheduled;
    private bool _araPlanErtelendi;
    private IComparisonFrameSource? _standby;
    private PreviewClip? _standbyClip;
    private bool _standbyOpening;
    private int _orderedTicks;
    private int _handovers;

    internal PanelHost(ComparisonPanel panel, Func<IComparisonFrameSource> factory, SegmentEncoder? segments = null)
    {
        _panel = panel;
        _factory = factory;
        _segments = segments ?? new SegmentEncoder();

        // K3: pencere sürüklenirken her pikselde akış kurulmaz; yerleşme beklenir.
        _settle = new DispatcherTimer { Interval = Motion("MotionSlow", 360) };
        _settle.Tick += (_, _) => SettleElapsed();

        // K1: ayar değişimi ile kodlamanın başlaması arasındaki gecikme. Recalculate'in kendi
        // 160 ms'i kodlamadan kısa; kaydırıcı sürüklenirken her ara değer bir ffmpeg açardı.
        _segmentDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SegmentEncoder.DebounceMilliseconds) };
        _segmentDelay.Tick += (_, _) => { _ = SegmentDelayElapsed(); };

        _panel.Frames.SizeChanged += (_, _) => OnResized();
        _panel.Controls.PlayPauseRequested += (_, _) => ApplyPlayState();
        _panel.SpeedRequested += (_, speed) => ApplySpeed(speed);
        _panel.Controls.RestartRequested += (_, _) => Seek(TimeSpan.Zero);
        _panel.Controls.SeekRequested += (_, position) => Seek(position);

        _panel.SetCompact(true);
    }

    /// <summary>
    /// Yerleşme sayacı doldu: ölçü değişimi durulduğuna göre akış yeni ölçüyle kurulur.
    /// Ayrı bir yöntem, çünkü <see cref="DispatcherTimer"/> ileti döngüsü olmayan bir iş
    /// parçacığında hiç ateşlenmiyor ve T161/K4'ün boyut kolu tiki buradan sürüyor.
    /// </summary>
    internal void SettleElapsed()
    {
        _settle.Stop();
        Restart();
    }

    /// <summary>Ölçü değişimi yerleşme sayacını kurdu mu — <see cref="OnResized"/>'ın çıktısı.</summary>
    internal bool ResizeSettling => _settle.IsEnabled;

    /// <summary>
    /// Kodlama gecikmesi doldu: sıraya konan pencere kodlanır. <see cref="SettleElapsed"/>
    /// ile aynı sebeple ayrı yöntem; sıraya konan başlangıç anını burada okumak, gecikme
    /// boyunca gelen son isteğin kazanmasını sağlar.
    /// </summary>
    internal Task SegmentDelayElapsed()
    {
        _segmentDelay.Stop();
        return LoadClipAsync(_clipStart);
    }

    /// <summary>Kodlama gecikmesi kurulu mu — <see cref="ScheduleClip"/>'in çıktısı.</summary>
    internal bool ClipScheduled => _segmentDelay.IsEnabled;

    internal TimeSpan ClipDelay => _segmentDelay.Interval;

    /// <summary>Panel açık mı. Kapalıyken ayakta hiçbir ffmpeg süreci yoktur.</summary>
    internal bool IsOpen => _open;

    /// <summary>Ekrana konan kare sayısı — kaynak bağlandığından beri.</summary>
    internal long PresentedFrames => _submitted;

    /// <summary>Son saniyede ekrana konan kare.</summary>
    internal double ScreenFps => _screenFps;

    internal ComparisonSourceStatus? SourceStatus => _source?.Status;

    /// <summary>Sağ yarıyı besleyen kısa parça çifti; tam çıktı gösterilirken <c>null</c>.</summary>
    internal PreviewClip? ActiveClip => _right is null ? _clip : null;

    /// <summary>Önden hazırlanmış bir sonraki pencere (K3).</summary>
    internal PreviewClip? PreparedClip => _ahead;

    /// <summary>Kaç kez sınırda devir yapıldı — boru öldürülmeden yüzey değiştirildi.</summary>
    internal int Handovers => _handovers;

    /// <summary>Bekleyen boru ayakta mı.</summary>
    internal bool HasStandby => _standby is not null;

    /// <summary>Kaç kez kodlama sıraya kondu — T50/K3 ölçümü bunu okur.</summary>
    internal int ScheduledEncodes => _scheduled;

    /// <summary>Kısa parça kodlayıcısı — ölçüm iptal ve geçici dosya sayımını buradan okur.</summary>
    internal SegmentEncoder Segments => _segments;

    /// <summary>
    /// K4 rozetinin metni; rozet gerekmiyorsa <c>null</c>. Koşulu T47'nin döndürdüğü
    /// <see cref="PreviewClip.IsApproximate"/> sürer, panel kendi kararını vermez.
    ///
    /// K7: basılan sayı ffmpeg'e <b>gerçekten geçen</b> tam sayıdır
    /// (<c>PreviewSegment.Plan.Crf</c>), ham ondalık değer değil. Ondalık değeri basmak
    /// kullanıcıya kodlanandan farklı bir sayı gösterirdi. Kodlayıcının kalite ölçeği
    /// modellenmiyorsa sayı hiç gösterilmez.
    ///
    /// Kullanıcı 09-07: rozette yalnız "CRF x" yazar. Taraf adı (İŞLENMİŞ) panelin üstündeki
    /// etiket satırında durduğu için rozette tekrarlanmaz; sayı yoksa rozet de yoktur.
    ///
    /// K8 borcu 8: o karar <see cref="PreviewQuality.Yaklasik"/> halini konuşuyordu. Kodlayıcının
    /// kalite ölçeği hiç modellenmiyorsa sayı üretilemez ve lafzına uyulunca parça nihai
    /// çıktıdan <b>en çok</b> saptığı yerde hiç uyarı çıkmıyordu. O hal sayısız tek kelimeyle
    /// ayrı yazılır; "yaklaşık" denmez, çünkü sapmanın türü başkadır.
    /// </summary>
    internal string? ApproximateBadge => RozetMetni(ActiveClip);

    /// <summary>
    /// Rozetin saf gövdesi: panel açmadan ölçülebilsin diye ayrı duruyor. Karar yalnız
    /// parçaya bakar, panelin durumuna değil.
    /// </summary>
    internal static string? RozetMetni(PreviewClip? clip)
    {
        if (clip is null || !clip.IsApproximate) return null;
        if (clip.Crf is { } crf) return Strings.Get("main.plan.mode.crf-value", crf);
        return clip.QualityKind == PreviewQuality.Desteklenmiyor
            ? Strings.Get("main.preview.temsili")
            : null;
    }

    /// <summary>
    /// Ayar değiştiğinde çağrılır. Planın kendisi kodlama başlatmaz; gecikme dolduğunda
    /// oynatma konumundan başlayan pencere kodlanır (K1).
    /// </summary>
    internal void SetPlan(MediaInfo? info, EncodePlan? plan, ComplexityProfile? profile)
    {
        if (_disposed) return;
        _info = info;
        _plan = plan;
        _profile = profile;

        if (info is null || plan is null || _left is null)
        {
            _segmentDelay.Stop();
            _segments.Cancel();
            return;
        }

        // Tam çıktı varken parça üretilmez: sağ yarı zaten gerçek çıktıyı gösteriyor.
        if (_right is not null) { _segmentDelay.Stop(); _segments.Cancel(); return; }

        if (AraOlcum && (_clip is not null || _clipRunning)) { _araPlanErtelendi = true; return; }
        _araPlanErtelendi = false;

        // T50/K3: plan da pencere de aynıysa kodlama başlatılmaz. Dil düğmesi de, hedef
        // tuşu da Recalculate üzerinden buraya düşüyor ve çoğu zaman planı değiştirmiyor;
        // eskiden her çağrı iki dosyalık kodlama ve boru yeniden kurulumu ödetiyordu.
        if (ClipSignature(info, plan, _clipStart) is { } signature && signature == TargetSignature)
            return;

        ScheduleClip(_clipStart);
    }

    /// <summary>
    /// Bir pencerenin "aynı" olmasının ölçüsü: <b>o pencere için ffmpeg'e geçecek argüman
    /// dizisinin kendisi</b>. Alan alan karşılaştırma yerine bu seçildi çünkü kodlamanın
    /// çıktısını gerçekten belirleyen şey argümanlardır; plana yeni bir alan eklendiğinde
    /// karşılaştırma kendiliğinden onu da kapsar, güncellenmesi unutulan bir liste kalmaz.
    /// Kaynak yolu, başlangıç anı, süre, kodlayıcı, ölçü, kare hızı, kalite değeri ve ön
    /// ayar — hepsi argümanların içinde.
    ///
    /// Çıktı yolu iki tarafta da aynı yer tutucudur, yani karşılaştırmaya girmez. Pencere
    /// kaynağın dışına düşerse <c>null</c> döner ve çağıran "değişti" varsayar.
    /// </summary>
    internal bool AraOlcum { get; set; }

    internal bool OlcumPlani { get; set; }

    internal bool AraPlanErtelendi => _araPlanErtelendi;

    internal void ErtelenenPlaniUygula()
    {
        if (!_araPlanErtelendi) return;
        _araPlanErtelendi = false;
        OlcumPlani = true;
        try { SetPlan(_info, _plan, _profile); }
        finally { OlcumPlani = false; }
    }

    internal static readonly TimeSpan IlkParcaGecikmesi = TimeSpan.FromMilliseconds(1);

    private string? ClipSignature(MediaInfo info, EncodePlan plan, double startSeconds)
    {
        try
        {
            var segment = _segments.Describe(info, plan, Math.Max(0, startSeconds), SignatureOutput, _profile);
            return string.Join('', segment.Arguments);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>İmza hesabında kullanılan yer tutucu çıktı yolu; diske hiç dokunulmaz.</summary>
    private const string SignatureOutput = "<signature>";

    /// <summary>
    /// Önizlemenin psy/AQ bayraklarını aldığı ölçülmüş yetenek. Parça kodlayıcısında durur;
    /// imza hesabı ile kodlayan yol aynı değeri görsün diye burada yalnız iletilir.
    /// </summary>
    internal IEncoderAvailability? Availability
    {
        get => _segments.Availability;
        set => _segments.Availability = value;
    }

    /// <summary>
    /// Önizlemenin anahtar kare tavanını aldığı sahne haritası. Parça kodlayıcısında durur;
    /// nihai kodlama ile önizleme aynı tavanı görsün diye burada yalnız iletilir.
    /// </summary>
    internal SceneMap? Scenes
    {
        get => _segments.Scenes;
        set => _segments.Scenes = value;
    }

    /// <summary>
    /// Sağ yarının <b>şu an hedeflediği</b> pencere. Bir kodlama bekliyor ya da koşuyorsa
    /// hedef odur; hiçbiri yoksa hedef ekrandaki parçadır. Yeni istek bununla karşılaştırılır:
    /// aynıysa yapılacak iş yok.
    /// </summary>
    private string? TargetSignature
        => _segmentDelay.IsEnabled || _clipRunning ? _pendingSignature : _clipSignature;

    /// <summary>
    /// Panelin süreceği dosyalar. Sağ taraf için işlenmiş dosya yoksa <c>null</c> geçilir —
    /// o zaman sağ taraf sahte çıktı sunmaz, perde arkasında sebebini söyler.
    /// </summary>
    internal void SetFiles(string? source, string? processed, double aspect, TimeSpan duration, double sourceFps)
    {
        var left = string.IsNullOrWhiteSpace(source) || !File.Exists(source) ? null : source;
        var right = string.IsNullOrWhiteSpace(processed) || !File.Exists(processed) ? null : processed;

        // Aynı dosyalar yeniden verilirse akış kurulmaz: her hedef tuşu Recalculate
        // üzerinden buraya düşer ve akışı her tuşta yeniden kurmak boru israfıdır.
        if (left == _left && right == _right) return;

        // Kaynak değiştiyse eldeki parçalar başka bir dosyanın parçalarıdır; tam çıktı
        // geldiyse parçaya gerek kalmaz. İki durumda da defter sıfırlanır.
        if (left != _left || right is not null)
        {
            _segmentDelay.Stop();
            _segments.Cancel();
            _clip = null;
            _ahead = null;
            _ = DropStandby();
            _clipFailure = null;
            _clipSignature = null;
            if (left != _left) _clipStart = 0;
        }

        _left = left;
        _right = right;
        if (aspect > 0) _aspect = aspect;
        _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        _fps = (int)Math.Clamp(Math.Round(sourceFps <= 0 ? 30 : sourceFps), 1, MaxFps);

        if (_left is null) { Close(); return; }
        if (_open) { RefreshRight(); Restart(); }
        if (_right is null) ScheduleClip(_clipStart);
    }

    internal void Open()
    {
        if (_disposed || _open || _left is null) return;
        _open = true;
        _panel.SetCompact(false);
        Restart();
    }

    internal void Close()
    {
        _settle.Stop();
        _segmentDelay.Stop();
        _segments.Cancel();
        _ = Teardown();
        _audio.Close();
        _open = false;
        _panel.SetCompact(true);
        _panel.SetRightNotice(null);
        _panel.SetRightBadge(null);
        _panel.SetNotice(null);
        _panel.Frames.Blank();
        _panel.Controls.IsPlaying = false;
        _panel.RefreshEmptyState();
    }

    /// <summary>
    /// Dil değişimi. Rozet metni burada yeniden sürülür: panel rozeti olduğu gibi gösteriyor,
    /// birleşik dizgenin sözlükte karşılığı yok, yani çeviriyi barındıran taraf yapmalı.
    /// </summary>
    internal void SetLanguage(string language)
    {
        _language = language;
        _panel.SetLanguage(language);
        _panel.SetRightBadge(ApproximateBadge);
    }

    private string PlaybackText(string key)
        => LanguageCatalog.Title(Strings.GetIn(_language, key), _language);

    /// <summary>
    /// Akışı panelin o anki ölçüsüyle kurar. Kare ölçüsü sabit bir sayıdan değil, panonun
    /// kendi genişliğinden gelir; kaynağın en boy oranı korunur, yoksa ffmpeg görüntüyü
    /// panoya doğru esnetirdi.
    /// </summary>
    /// <summary>
    /// Kurulumlar zincire dizilir: yerleşme ve dosya değişimi aynı anda düşerse ikisi
    /// sırayla koşar. Yarışan iki kurulum, kaybedenin kaynağını öksüz bırakabilirdi.
    /// </summary>
    private void Restart()
        => _restart = Chain(_restart);

    private async Task Chain(Task previous)
    {
        try { await previous; } catch { }
        await RestartCore();
    }

    private async Task RestartCore()
    {
        if (_disposed || !_open || _left is null) return;

        var size = Measure();
        if (size.Width < MinPanelEdge || size.Height < MinPanelEdge) return;

        await Teardown();
        if (_disposed || !_open) return;
        var generation = ++_generation;
        _panelSize = size;

        _panel.Frames.Configure(new PixelSize(size.Width * 2, size.Height));
        RefreshRight();
        _panel.SetNotice("playback.panel.first-frame");
        _panel.Controls.Duration = _duration > TimeSpan.Zero ? _duration : null;
        _panel.Controls.IsPlaying = true;

        var source = _factory();
        _source = source;
        if (_hiz != 1) source.SetSpeed(_hiz);
        source.StatusChanged += OnStatusChanged;

        // Parça modunda iki girdi de aynı pencereye kesilmiş dosyalardır: ikisi de kendi
        // ekseninde sıfırdan akar, hstack hizayı kendiliğinden tutturur. Pencere kısa olduğu
        // için tekrar kapalı — dolduğunda başa sarmak yerine sonraki pencereye geçilir.
        var request = BuildRequest(size);

        try
        {
            await source.StartAsync(request);
        }
        catch (Exception ex)
        {
            if (generation == _generation) _panel.SetNotice($"{PlaybackText("playback.player-failed")}: {ex.Message}");
            return;
        }

        if (generation != _generation) return;

        _submitted = 0;
        _awaitingFirst = true;
        _windowFrames = 0;
        _windowStart = Environment.TickCount64;
        Report(source.Status);
        Pump(generation);
        _ = SyncAudioAsync(generation, _left, ActiveClip?.StartSeconds ?? 0);
    }

    /// <summary>
    /// Boruya verilecek istek. Sağ yarının üç durumu buradaki iki <c>??</c> dalıdır ve
    /// üçüncü dal — <c>_left</c> — <b>kasıtlı olarak duruyor</b> (T50/K4).
    ///
    /// Perde durumunda ne parça ne tam çıktı vardır, ama boru <c>hstack</c> ile tek süreçte
    /// iki girdi birleştiriyor: sağ girdi boş bırakılırsa
    /// <see cref="ComparisonFrameRequest.Validate"/> isteği reddeder ve <b>sol yarı da</b>
    /// akmaz. Bu yüzden sağ girdiye kaynağın kendisi konur; kullanıcı onu hiç görmez, çünkü
    /// aynı durumda <see cref="RightCurtain"/> sağ yarıyı zaten kapatıyor. Dalın kaldırılması
    /// perde durumunda paneli tümden karartır — bunu <c>Perde_durumunda_sag_girdi_kaynaktir</c>
    /// testi tutuyor.
    /// </summary>
    internal ComparisonFrameRequest BuildRequest(PixelSize size)
    {
        var clip = ActiveClip;
        var left = clip?.SourcePath ?? _left ?? throw new InvalidOperationException("The left input is missing, the stream cannot start.");
        return new ComparisonFrameRequest
        {
            LeftPath = left,
            RightPath = clip?.EncodedPath ?? _right ?? left,
            PanelWidth = size.Width,
            PanelHeight = size.Height,
            Fps = _fps,
            Realtime = true,
            Loop = ShouldLoop(left, clip?.EncodedPath ?? _right ?? left)
        };
    }

    /// <summary>
    /// Boru başa sarabilir mi. Başa sarma <b>iki motor örneğine ayrı ayrı</b> veriliyor,
    /// yani her girdi kendi uzunluğunda başa sarıyor.
    /// İki dosyanın süresi eşit değilse aradaki fark her turda birikir ve iki yarı
    /// birbirinden uzaklaşır — kayma sınırsızdır, izleme uzadıkça büyür. Ölçüldü: 4 sn'lik
    /// kaynağın karşısına 3,8 sn'lik bir çıktı konduğunda kayma tur başına 6 kaynak karesi
    /// artıyor.
    ///
    /// Süre farkının tek bir sebebi yok, o yüzden kural sebebe değil <b>duruma</b> bakıyor:
    /// kırpma, kaynağın süresinin kare ızgarasına tam oturmaması, değişken kare hızı,
    /// kabın sesi videodan uzun taşıması. (T58'de görülen fazladan kare hızı sapması
    /// T60'ta kapandı; kural onun için değil, sınıfın tamamı için duruyor.)
    ///
    /// Bu yüzden başa sarma yalnız <b>aynı dosya</b> iki girdiye de konduğunda açılır;
    /// perde durumunun kendisi budur ve orada iki yarı tanım gereği aynı uzunluktadır.
    /// Ayrı dosyalarda boru sonuncu karede biter ve o kare durur; kullanıcı baştan
    /// başlatmak isterse şeridin kendi düğmesi zaten var.
    /// </summary>
    internal static bool ShouldLoop(string leftPath, string rightPath)
        => string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Panonun ölçüsünden kare ölçüsü. Kaynağın oranı panoya sığdırılır (ZoomGesture de
    /// aynı sığdırmayı yapar), böylece boru zaten doğru orandaki kareyi verir.
    /// </summary>
    private PixelSize Measure()
    {
        var bounds = _panel.Frames.Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        if (width <= 0 || height <= 0) return PixelSize.Empty;

        if (width / height > _aspect) width = height * _aspect;
        else height = width / _aspect;

        // Tek panelin genişliği çift sayı olsun: bgra karesi de, birleşik genişlik de öyle.
        var w = Math.Max(MinPanelEdge, (int)Math.Round(width / 2) * 2);
        var h = Math.Max(MinPanelEdge, (int)Math.Round(height / 2) * 2);
        return new PixelSize(w, h);
    }

    private void OnResized()
    {
        if (!_open || _disposed) return;
        // K3: yakınlaştırma akışı yeniden kurmaz. Terfi de yakınlaştırmanın parçasıdır,
        // panel kök katmandayken ölçü değişimi kod çözmeye dokunmaz.
        if (_panel.IsPromoted) return;

        var size = Measure();
        if (size.Width < MinPanelEdge || size.Height < MinPanelEdge) return;
        if (Math.Abs(size.Width - _panelSize.Width) < ResizeTolerance &&
            Math.Abs(size.Height - _panelSize.Height) < ResizeTolerance) return;

        _settle.Stop();
        _settle.Start();
    }

    // ---- kısa parça: hazırlama, ilerleme, perde (K1, K2, K3, K6) ----------------------

    /// <summary>
    /// Perde metni. Tam çıktı da parça da yokken iner; kodlama hata verdiyse sebebini söyler,
    /// hata sessizce yutulmaz (K6).
    /// </summary>
    private string? RightCurtain()
    {
        if (_right is not null || ActiveClip is not null) return null;
        return _clipFailure is { } failure ? SampleFailureText(failure) : "playback.panel.pending";
    }

    /// <summary>
    /// Kodlayanın bıraktığı sebebin ekrandaki hâli. Perdenin iki parçası da anahtardan gelir;
    /// ffmpeg'in kendi tanı metni varsa <c>playback.error.engine</c> etiketinin biçim argümanı
    /// olarak <b>ham</b> taşınır — T91/K2 kararı, motorun metni çevrilmez ama etiketsiz de
    /// geçmez.
    /// </summary>
    internal string SampleFailureText(EncodeFailure failure)
    {
        var reason = failure.Detail is null
            ? PlaybackText(failure.Key)
            : string.Format(CultureInfo.InvariantCulture, PlaybackText(failure.Key), failure.Detail);

        return $"{PlaybackText("playback.sample-failed")}: {reason}";
    }

    /// <summary>
    /// Sag yarinin iki metnini birlikte tazeler: perde ve rozet ayni duruma bakiyor, ayri
    /// tazelenirse biri otekinden geride kalir.
    /// </summary>
    private void RefreshRight()
    {
        // Kodlama arayuz kuyrugunun disinda bitiyor; panel ozellikleri yalniz kendi
        // kuyrugundan yazilabilir.
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RefreshRight); return; }
        _panel.SetRightNotice(RightCurtain());
        _panel.SetRightBadge(ApproximateBadge);
    }

    /// <summary>Gecikmeyi kurar. Arka arkaya gelen istekler tek kodlamaya iner (K1).</summary>
    private void ScheduleClip(double startSeconds)
    {
        if (_disposed || _right is not null || _info is null || _plan is null || _left is null) return;
        _clipStart = Math.Max(0, startSeconds);
        _pendingSignature = _info is { } media && _plan is { } current ? ClipSignature(media, current, _clipStart) : null;
        _scheduled++;
        _segmentDelay.Stop();
        _segmentDelay.Interval = OlcumPlani || (_clip is null && _clipSignature is null && !_clipRunning)
            ? IlkParcaGecikmesi
            : TimeSpan.FromMilliseconds(SegmentEncoder.DebounceMilliseconds);
        _segmentDelay.Start();
    }

    /// <summary>
    /// Gösterilecek pencereyi kodlar; biterse akışı o çiftle yeniden kurar. Gecikme dolunca
    /// çağrılan iş budur — bekleyebilmek için <c>Task</c> döner.
    /// </summary>
    internal async Task LoadClipAsync(double startSeconds)
    {
        var info = _info;
        var plan = _plan;
        if (_disposed || info is null || plan is null || _left is null || _right is not null) return;

        // Gecikme bu işin kendisi için kuruluyordu; iş elden başlatıldığında sayaç durur.
        _segmentDelay.Stop();
        _pendingSignature = ClipSignature(info, plan, Math.Max(0, startSeconds));
        _ahead = null;
        _ = DropStandby();
        _clipRunning = true;
        try
        {
            AcilisIzi.Yaz("panel-parca-istek");
            var olcu = _profile;
            var clip = await _segments.RequestAsync(info, plan, startSeconds, olcu);
            if (_disposed || _right is not null) return;
            if (clip is null)
            {
                AcilisIzi.Yaz("panel-parca-iptal");
                // İptal edilen istek hata değildir; yerine yenisi zaten koşuyor.
                if (_segments.LastFailure is not { } failure) return;
                _clipFailure = failure;
                _clip = null;
                _clipSignature = null;
                if (_open) RefreshRight();
                return;
            }

            _clipFailure = null;
            _clip = clip;
            _clipStart = clip.StartSeconds;
            _clipSignature = ClipSignature(info, plan, clip.StartSeconds);
            _pendingSignature = null;
            AcilisIzi.Yaz("panel-parca");
            if (olcu is { Calibrated: true }) AcilisIzi.Yaz("panel-parca-kalibre");
            if (_open) { RefreshRight(); Restart(); }
        }
        catch (Exception ex)
        {
            _clipFailure = new EncodeFailure(SegmentEncoder.EngineMessageKey, ex.Message);
            if (_open) RefreshRight();
        }
        finally
        {
            _clipRunning = false;
        }
    }

    /// <summary>
    /// Bir sonraki pencereyi oynatma sürerken kodlar. En çok bir pencere ileriye bakılır;
    /// kuyruk kurulmaz. T161/K1 ölçümü bunu doğrudan çağırır: gerçek animasyon çerçevesi
    /// olmadan <see cref="Follow"/>'un tetiklediği çağrıyı elle üretmek için internal.
    /// </summary>
    internal async Task PrepareAheadAsync()
    {
        var info = _info;
        var plan = _plan;
        var clip = ActiveClip;
        if (_disposed || info is null || plan is null || clip is null) return;
        if (_aheadRunning || _clipRunning || _ahead is not null) return;

        var next = clip.EndSeconds;
        if (FreezesAtWindowEnd) return;

        _aheadRunning = true;
        try
        {
            var prepared = await _segments.RequestAsync(info, plan, next, _profile);
            if (_disposed || _right is not null) return;
            // Kullanıcı bu sırada başka bir ana atladıysa hazırlanan pencere atılır.
            if (prepared is not null && ActiveClip is { } current && Math.Abs(prepared.StartSeconds - current.EndSeconds) < 0.05)
                _ahead = prepared;
        }
        catch (Exception ex)
        {
            _clipFailure = new EncodeFailure(SegmentEncoder.EngineMessageKey, ex.Message);
        }
        finally
        {
            _aheadRunning = false;
        }
    }

    /// <summary>Pencere doldu: hazır olan sonrakine geçilir, yoksa o an kodlanır.</summary>
    private void AdvanceClip()
    {
        var clip = ActiveClip;
        if (clip is null) return;

        if (_ahead is { } ready)
        {
            _ahead = null;
            _clip = ready;
            _clipStart = ready.StartSeconds;
            _clipSignature = _info is { } media && _plan is { } current ? ClipSignature(media, current, ready.StartSeconds) : null;
            if (_open) Restart();
            return;
        }

        if (_clipRunning) return;
        if (FreezesAtWindowEnd) return;
        _ = LoadClipAsync(clip.EndSeconds);
    }

    /// <summary>
    /// Örnek penceresi dolduğunda sağ yarının ne yaptığı, tek yerden. Kaynağın sonunda
    /// değilsek pencere biter bitmez bir sonrakine geçilir — kullanıcı iki yarının da
    /// akmaya devam ettiğini görür, donma yoktur (<see cref="SwapToStandby"/> bekleyen
    /// boruyu hazır tutuyor).
    ///
    /// Kaynağın son penceresindeysek ilerlenecek yer yoktur: boru biter ve <b>iki yarı da
    /// son karesinde donar</b>. Başa sarmak bilerek yapılmıyor; sol ve sağ dosya ayrı ayrı
    /// başa sardığında hiza kaybolur (bkz. <see cref="ShouldLoop"/>). Baştan başlatmak
    /// şeridin kendi düğmesinde.
    /// </summary>
    internal bool FreezesAtWindowEnd
        => ActiveClip is { } window
           && _info is { DurationSeconds: > 0 } info
           && window.EndSeconds >= info.DurationSeconds - 0.05;

    private void Pump(int generation)
    {
        var top = TopLevel.GetTopLevel(_panel);
        if (top is null) return;

        void Tick(TimeSpan _)
        {
            if (_disposed || !_open || generation != _generation) return;
            Drain();
            top.RequestAnimationFrame(Tick);
        }

        top.RequestAnimationFrame(Tick);
    }

    /// <summary>
    /// Tek sunum turu. <see cref="Pump"/> bunu <c>RequestAnimationFrame</c> ile suruyor;
    /// olcum turu elle cevirebilsin diye ayri bir giris var — T161'in olcemedigi sey
    /// (<see cref="ControlStrip.Position"/>) ancak boyle deterministik okunuyor.
    /// </summary>
    internal void DrainOnce() => Drain();

    private void Drain()
    {
        var source = _source;
        if (source is null) return;

        PlaybackFrame frame;
        if (_orderedTicks > 0)
        {
            _orderedTicks--;
            if (!source.TryTake(out frame)) return;
        }
        else if (!TakeNewestReady(source, out frame)) return;

        var expected = _panel.Frames.FrameSize;
        var position = frame.Presentation;
        var fits = frame.Width == expected.Width && frame.Height == expected.Height;

        try
        {
            if (!fits) return;
            var buffer = _panel.Frames.Rent();
            Buffer.BlockCopy(frame.Buffer, 0, buffer, 0, Math.Min(frame.ByteLength, buffer.Length));
            _panel.Frames.Submit(buffer);
        }
        catch (InvalidOperationException)
        {
            // Pano bu turda yeniden yapılandırılıyor; kare iade edilir, tur atlanır.
            return;
        }
        finally
        {
            source.Return(frame);
        }

        _submitted++;
        var clip = ActiveClip;
        // Parça modunda boru penceresinin sıfırından akıyor; şerit kaynağın kendi eksenini
        // gösterir, yoksa 5 sn'lik pencere bütün videoymuş gibi görünürdü.
        _panel.Controls.Position = clip is null
            ? position
            : position + TimeSpan.FromSeconds(clip.StartSeconds);
        if (clip is not null) Follow(clip, position);
        // Boş durum kare panoya konduğunda kalkar, kare sıraya girdiğinde değil: sunum turu
        // bizim turumuzdan sonra çalışıyor, bir tur önce sorulursa cevap hâlâ "kare yok".
        if (_awaitingFirst && _panel.Frames.HasFrame)
        {
            _awaitingFirst = false;
            _panel.SetNotice(null);
            _panel.RefreshEmptyState();
        }
        SampleRate();
    }

    /// <summary>
    /// Pencerenin neresinde olduğumuza bakar: yarısı geçince sonraki pencere kodlanmaya
    /// başlar, sonuna gelince ona geçilir. Duraklatılmışken ileri hazırlık yapılmaz.
    /// </summary>
    private void Follow(PreviewClip clip, TimeSpan position)
    {
        if (!_panel.Controls.IsPlaying) return;
        var played = position.TotalSeconds;
        var swapAt = SwapAt(clip.DurationSeconds, _fps);

        if (played >= clip.DurationSeconds * PrefetchAtFraction) _ = PrepareAheadAsync();
        if (played >= clip.DurationSeconds - HandoverLeadSeconds) BeginHandover();
        if (played < swapAt) return;

        // Pencerenin son karesi ekrana kondu. Bekleyen boru hazırsa yüzey orada değişir;
        // hazır değilse eski yola düşülür ve boru yeniden kurulur.
        if (_standby is { } standby && standby.Status.ProducedFrames > 0) { SwapToStandby(); return; }
        AdvanceClip();
    }

    /// <summary>
    /// Yüzeyin değiştirileceği an: pencerenin <b>son karesi</b>. Boru pencereyi son kareye
    /// kadar akıtıyor ve son karenin damgası <c>süre - 1/fps</c>'tir — <c>süre</c>'nin
    /// kendisi hiç gelmez, o eşiğe bakan bir kural hiç tetiklenmez.
    ///
    /// Eşik tam <c>süre - 1/fps</c> değil, bir buçuk kare geride: damga
    /// <see cref=TimeSpan/> üzerinden 100 ns'e yuvarlanıyor ve son kare tam eşiğin
    /// milyarda bir kadar altına düşebiliyor. Bir buçuk kare, son kareyi geçirir ve
    /// sondan ikinciyi geçirmez.
    ///
    /// Geçişin daha erken yapılması pencerenin kuyruğunu atlar: T50 turu 1'de geçiş
    /// bekleyen borunun ilk karesine bağlıydı ve kaynağın ~52 ms'i hiç gösterilmiyordu.
    /// </summary>
    internal static double SwapAt(double durationSeconds, int fps)
        => Math.Max(0, durationSeconds - 1.5 / Math.Max(1, fps));

    /// <summary>Bekleyen borunun kaç saniye önceden açıldığı. Ölçüm aynı eşiği kullanır.</summary>
    internal static double HandoverLead => HandoverLeadSeconds;

    /// <summary>
    /// Sonraki pencerenin borusunu eski boru ayaktayken açar. Süreç açılışının 110 ms'i
    /// burada, oynatma sürerken ödenir; sınırda ödenmez.
    /// </summary>
    private void BeginHandover()
    {
        if (_standby is not null || _standbyOpening) return;
        if (_ahead is not { } next) return;
        if (_panelSize.Width < MinPanelEdge || _panelSize.Height < MinPanelEdge) return;
        if (_disposed || !_open) return;

        _standbyOpening = true;
        _standbyClip = next;
        _ = OpenStandbyAsync(next, _panelSize);
    }

    private async Task OpenStandbyAsync(PreviewClip next, PixelSize size)
    {
        var source = _factory();
        try
        {
            await source.StartAsync(new ComparisonFrameRequest
            {
                LeftPath = next.SourcePath,
                RightPath = next.EncodedPath,
                PanelWidth = size.Width,
                PanelHeight = size.Height,
                Fps = _fps,
                Realtime = true,
                Loop = false
            });
        }
        catch
        {
            // Devir açılamadıysa sessizce eski yola dönülür: pencere sonunda boru yeniden
            // kurulur. Kullanıcıya söylenecek yeni bir şey yok, gösterilen görüntü aynı.
            Abandon(source);
            return;
        }

        if (_disposed || !_open || !ReferenceEquals(_standbyClip, next)) { Abandon(source); return; }

        _standby = source;
        _standbyOpening = false;
    }

    private void Abandon(IComparisonFrameSource source)
    {
        _standbyOpening = false;
        _standbyClip = null;
        _ = Task.Run(source.Dispose);
    }

    /// <summary>
    /// Yüzeyi bekleyen boruya çevirir ve eskisini <b>arkada</b> öldürür. Sunum döngüsü
    /// kaynağı her turda okuduğu için döngü yeniden kurulmaz; kullanıcı bir kare bile
    /// donmuş görmez.
    /// </summary>
    private void SwapToStandby()
    {
        if (_standby is not { } next || _standbyClip is not { } clip) return;
        _standby = null;
        _standbyClip = null;
        if (ReferenceEquals(_ahead, clip)) _ahead = null;

        var old = _source;
        if (old is not null) old.StatusChanged -= OnStatusChanged;
        next.StatusChanged += OnStatusChanged;

        _clip = clip;
        _clipStart = clip.StartSeconds;
        _clipSignature = _info is { } media && _plan is { } current ? ClipSignature(media, current, clip.StartSeconds) : null;
        _source = next;
        if (_hiz != 1) next.SetSpeed(_hiz);
        _orderedTicks = OrderedTicksAfterHandover;
        _handovers++;

        if (old is not null) _ = Task.Run(old.Dispose);
        if (_left is { } path) _ = SyncAudioAsync(_generation, path, clip.StartSeconds);
        RefreshRight();
        Report(next.Status);
    }

    /// <summary>Bekleyen boruyu bırakır. Pencere, dosya ya da konum değiştiğinde çağrılır.</summary>
    private Task DropStandby()
    {
        var standby = _standby;
        _standby = null;
        _standbyClip = null;
        _standbyOpening = false;
        _orderedTicks = 0;
        return standby is null ? Task.CompletedTask : Task.Run(standby.Dispose);
    }

    /// <summary>
    /// Bir sunum turunda kaynaktan yalnız bir kare çekmek, tur hızı besleme hızının altına
    /// düştüğü anda kalıcı geri kalma üretiyordu: halka her turda taşıyor, ekrana konan kare
    /// giderek gerçek zamandan geriye kayıyordu. Burada tur başına kuyruk boşaltılıyor ve
    /// yalnız sonuncusu sunuluyor — atlananlar ekrana konmadan havuza iade ediliyor.
    ///
    /// Yetişiliyorken davranış değişmiyor: sırada tek kare varsa ikinci <c>TryTake</c> boş
    /// döner ve o tek kare sunulur. Atlama yalnız gerçekten geride kalınca oluyor.
    /// </summary>
    private static bool TakeNewestReady(IComparisonFrameSource source, out PlaybackFrame frame)
    {
        frame = null!;
        var skipped = 0;
        while (source.TryTake(out var next))
        {
            if (frame is not null)
            {
                source.Return(frame);
                skipped++;
            }
            frame = next;
            if (skipped >= CatchUpCeiling) break;
        }
        return frame is not null;
    }

    private void SampleRate()
    {
        _windowFrames++;
        var elapsed = Environment.TickCount64 - _windowStart;
        if (elapsed < 500) return;
        _screenFps = _windowFrames * 1000.0 / elapsed;
        _windowFrames = 0;
        _windowStart = Environment.TickCount64;
    }

    private void OnStatusChanged(object? sender, ComparisonSourceStatus status)
    {
        if (!ReferenceEquals(sender, _source)) return;
        Dispatcher.UIThread.Post(() => Report(status));
    }

    /// <summary>
    /// Kaynak kurulamadıysa panel sebebi söyler ve program çalışmaya devam eder — panelin
    /// yokluğu programı bozmaz (K2).
    ///
    /// <para>Oynuyor/duraklatıldı kolu şeridi kaynağa bağlar: kaynak kendi kararıyla
    /// duraklayabiliyor (ilk kare istek gelmeden üretildiğinde), o durumda düğme oynuyor
    /// derken görüntü duruyordu. Şerit düğmesi olayı yalnız kullanıcı bastığında
    /// yayıyor, buradan yazmak geri besleme kurmuyor.</para>
    /// </summary>
    internal void Report(ComparisonSourceStatus status)
    {
        if (_disposed) return;
        if (status.State == ComparisonSourceState.Kullanilamiyor)
        {
            _panel.SetNotice(status.MessageKey ?? "playback.player-failed", status.MessageArg);
            _panel.Controls.IsPlaying = false;
        }
        else if (status.State == ComparisonSourceState.Durdu && _submitted == 0)
        {
            _panel.SetNotice("playback.player-failed");
            _panel.Controls.IsPlaying = false;
        }
        else if (status.State is ComparisonSourceState.Oynuyor or ComparisonSourceState.Duraklatildi)
        {
            var oynuyor = status.State == ComparisonSourceState.Oynuyor;
            if (_panel.Controls.IsPlaying == oynuyor) return;
            _panel.Controls.IsPlaying = oynuyor;
            if (oynuyor) _audio.Play();
            else _audio.Pause();
        }
    }

    /// <summary>Onizlemenin ses yolu — olcum kuyunun takilip takilmadigini buradan okur.</summary>
    internal PreviewAudio Audio => _audio;

    /// <summary>
    /// Ses borusunu acik pencerenin anina getirir. Kaynak dosya bir kez baglanir; her
    /// pencere yenilenmesinde yalniz atlama yapilir. Sessiz klipte iki adim da sessizce
    /// hicbir sey yapmaz.
    /// </summary>
    private async Task SyncAudioAsync(int generation, string path, double atSeconds)
    {
        try
        {
            await _audio.AttachAsync(path).ConfigureAwait(false);
            if (_disposed || !_open || generation != _generation) return;
            if (!_audio.HasAudio) return;

            _audio.SeekTo(atSeconds);
            if (_hiz != 1) _audio.SetSpeed(_hiz);
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !_open || generation != _generation) return;
                if (_panel.Controls.IsPlaying) _audio.Play();
                else _audio.Pause();
            });
        }
        catch
        {
        }
    }

    /// <summary>Panelde basılı tutmanın hızı: iki taraf ve ses birlikte hızlanır, bırakışta birlikte döner.</summary>
    internal double Hiz => _hiz;

    private void ApplySpeed(double speed)
    {
        _hiz = speed;
        _source?.SetSpeed(speed);
        _audio.SetSpeed(speed);
    }

    private void ApplyPlayState()
    {
        var source = _source;
        if (source is null) return;
        // Duraklatma yalnız oynatmayı durdurur. Önden hazırlığı iptal etmek, sürüyorsa
        // kodladığı pencereyi atardı; baştan başlatma ayar değişmediği halde iş bastan
        // başlıyordu (T161). Hazırlık duraklatmada da arkada bitmeye bırakılır.
        if (_panel.Controls.IsPlaying) { source.Play(); _audio.Play(); }
        else { source.Pause(); _audio.Pause(); }
    }

    private async void Seek(TimeSpan position)
    {
        // Parça modunda boru yalnız 2 sn'lik pencereyi tanır; atlamanın karşılığı o ana
        // yeni bir pencere kodlamaktır. Önden hazırlanan pencere atılır (K3).
        if (ActiveClip is not null || (_right is null && _info is not null && _plan is not null))
        {
            _ahead = null;
            _ = DropStandby();
            ScheduleClip(position.TotalSeconds);
            return;
        }

        var source = _source;
        if (source is null) return;
        try { await source.SeekAsync(position); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { _panel.SetNotice(ex.Message); }
    }

    /// <summary>
    /// Kaynağı kapatır ve süreci öldürür.
    ///
    /// Kapatma <b>havuz kuyruğunda</b> yapılır, arayüz kuyruğunda değil: kaynağın kapatma
    /// yolu <c>await</c> ile yazılmış ve arayüz kuyruğundan çağrılınca sürdürmesi için o
    /// kuyruğu bekler — kapatmayı arayüz kuyruğunda beklemek program donduruyordu.
    /// </summary>
    private Task Teardown()
    {
        _ = DropStandby();
        var source = _source;
        _source = null;
        _generation++;
        if (source is null) return Task.CompletedTask;
        source.StatusChanged -= OnStatusChanged;
        _screenFps = 0;
        return Task.Run(source.Dispose);
    }

    private TimeSpan Motion(string key, double fallbackMs)
        => _panel.TryFindResource(key, out var value) && value is TimeSpan span
            ? span
            : TimeSpan.FromMilliseconds(fallbackMs);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settle.Stop();
        _segmentDelay.Stop();
        // Bekleyen boru da bir ffmpeg surecidir; gercekten olene kadar beklenir.
        DropStandby().Wait(TimeSpan.FromSeconds(3));
        // Uygulama kapanıyor: kalan parça dosyaları burada silinir (K5).
        _segments.Dispose();
        _audio.Dispose();
        // Pencere kapanıyor: süreç gerçekten ölene kadar beklenir, öksüz ffmpeg kalmaz.
        // Bekleme havuz kuyruğunda olduğu için arayüz kuyruğunu kilitlemez.
        Teardown().Wait(TimeSpan.FromSeconds(3));
    }
}
