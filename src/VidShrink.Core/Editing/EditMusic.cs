namespace VidShrink.Core.Editing;

/// <summary>
/// A2 muzik izi: tek ses dosyasi, cizelgede <see cref="Start"/> aninda baslar ve teslimin sonuna kadar calar (dosya kisaysa sessizlik
/// gelir). Duzeyi dB olarak <see cref="MinLevelDb"/>-<see cref="MaxLevelDb"/> araliginda; teslimde kaynak sesiyle <c>amix</c> ile
/// karisir ve yalniz Tam kipte kodlanir. Onizlemede ayri bir ses motoru cizelge konumuna kilitli calar; kisma onizlemede
/// <see cref="PreviewDuck"/> ile yaklasik duyulur.
/// </summary>
public sealed record EditMusic
{
    public const double MinLevelDb = -30;
    public const double MaxLevelDb = 6;
    public const double MinDuckDb = 3;
    public const double MaxDuckDb = 30;
    public const double DefaultDuckDb = 12;
    public const int MinDuckReleaseMs = 50;
    public const int MaxDuckReleaseMs = 3000;
    public const int DefaultDuckReleaseMs = 400;

    private readonly double _duckDb = DefaultDuckDb;
    private readonly int _duckReleaseMs = DefaultDuckReleaseMs;

    public EditMusic(string path, long duration, long start = 0, double levelDb = 0)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Muzik dosyasinin yolu bos", nameof(path));
        if (duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration), duration, "Muzik suresi pozitif olmalidir");
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), start, "Muzik cizelgenin basindan once baslayamaz");
        if (!double.IsFinite(levelDb) || levelDb < MinLevelDb || levelDb > MaxLevelDb)
            throw new ArgumentOutOfRangeException(nameof(levelDb), levelDb, "Muzik duzeyi -30 ile +6 dB arasinda olmalidir");
        Path = path;
        Duration = duration;
        Start = start;
        LevelDb = levelDb;
    }

    public string Path { get; }

    public long Duration { get; }

    public long Start { get; }

    public double LevelDb { get; }

    public bool Duck { get; init; }

    public double DuckDb
    {
        get => _duckDb;
        init => _duckDb = double.IsFinite(value) && value >= MinDuckDb && value <= MaxDuckDb
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DuckDb), value, "Kisma miktari 3 ile 30 dB arasinda olmalidir");
    }

    public int DuckReleaseMs
    {
        get => _duckReleaseMs;
        init => _duckReleaseMs = value >= MinDuckReleaseMs && value <= MaxDuckReleaseMs
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DuckReleaseMs), value, "Tepki suresi 50 ile 3000 ms arasinda olmalidir");
    }

    public bool HasDuckSettings => Duck || DuckDb != DefaultDuckDb || DuckReleaseMs != DefaultDuckReleaseMs;

    public EditMusic Placed(long start, double levelDb)
        => new(Path, Duration, start, levelDb) { Duck = Duck, DuckDb = DuckDb, DuckReleaseMs = DuckReleaseMs };

    /// <summary>Duzeyi araliga kirpip yazar.</summary>
    public static double ClampLevel(double levelDb) => double.IsFinite(levelDb) ? Math.Clamp(Math.Round(levelDb, 1), MinLevelDb, MaxLevelDb) : 0;

    public static double ClampDuck(double duckDb) => double.IsFinite(duckDb) ? Math.Clamp(Math.Round(duckDb, 1), MinDuckDb, MaxDuckDb) : DefaultDuckDb;

    public static int ClampRelease(int releaseMs) => Math.Clamp(releaseMs, MinDuckReleaseMs, MaxDuckReleaseMs);
}