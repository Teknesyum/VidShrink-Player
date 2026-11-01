namespace VidShrink.Core.Playback;

/// <summary>Kare kaynaginin durumu.</summary>
public enum ComparisonSourceState
{
    /// <summary>Hic baslatilmadi.</summary>
    Bosta,

    /// <summary>Surec kuruldu, ilk kare henuz gelmedi.</summary>
    Aciliyor,

    Oynuyor,

    /// <summary>Boru okunmuyor ama surec yasiyor.</summary>
    Duraklatildi,

    /// <summary>Kaynak bitti ya da durduruldu.</summary>
    Durdu,

    /// <summary>Bu makinede kurulamadi — sebebi <see cref="ComparisonSourceStatus"/> icinde.</summary>
    Kullanilamiyor
}

/// <summary>
/// Kaynagin o anki sayaclari ve durumu. Sayilar T37 olcum aracinin ciktisiyla
/// karsilastirilabilsin diye ayni adlarla verilir.
/// </summary>
public sealed record ComparisonSourceStatus(
    ComparisonSourceState State,
    long ProducedFrames,
    long DroppedFrames,
    double FeedFps,
    int ReadErrors,
    long PoolAllocations,
    string? MessageKey = null,
    string? MessageArg = null);

/// <summary>
/// Karsilastirma panelinin kare kaynagi. <b>Arayuz cizmez</b>, tek isi kare uretmek.
/// Hangi motorun kare urettigini cagiran bilmez.
/// </summary>
/// <remarks>
/// Tuketici <see cref="TryTake"/> ile siradaki kareyi alir, isini bitirince
/// <see cref="Return"/> ile havuza iade eder. Iade edilmeyen kare havuzu kurutur ve
/// uretici kare dusurmeye baslar.
/// </remarks>
public interface IComparisonFrameSource : IDisposable
{
    ComparisonSourceStatus Status { get; }

    event EventHandler<ComparisonSourceStatus>? StatusChanged;

    /// <summary>Akisi kurar. Motor bu makinede calismiyorsa durum <c>Kullanilamiyor</c> olur, istisna atmaz.</summary>
    Task StartAsync(ComparisonFrameRequest request, CancellationToken ct = default);

    /// <summary>Akisi durdurur ve motoru kapatir.</summary>
    Task StopAsync();

    /// <summary>Okumayi surdurur. Motor kapanmaz.</summary>
    void Play();

    /// <summary>Okumayi keser. Motor kapanmaz, yasar.</summary>
    void Pause();

    /// <summary>Iki tarafin oynatma hizini birlikte degistirir. Hiz bilmeyen kaynak yok sayar.</summary>
    void SetSpeed(double speed) { }

    /// <summary>Verilen konuma atlar.</summary>
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);

    /// <summary>Sirada kare varsa verir. Kare yoksa <c>false</c> doner, beklemez.</summary>
    bool TryTake(out PlaybackFrame frame);

    /// <summary>Kareyi havuza iade eder.</summary>
    void Return(PlaybackFrame frame);
}
