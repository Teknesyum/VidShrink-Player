using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VidShrink.App.Playback;

internal enum PlayerButton
{
    Left,
    Middle,
    Right,
    Back,
    Forward
}

internal enum PlayerCommandKind
{
    None,
    Seek,
    Zoom,
    TogglePlay,
    ToggleFullscreen,
    ContextMenu,
    OpenSettings,
    LeaveFullscreen,
    ResetZoom,
    ToggleTopmost,
    AspectCycle,
    Rotate,
    Mirror,
    ToggleInfo,
    Screenshot,
    FileStep,
    ToggleShuffle,
    RepeatCycle,
    Volume,
    ToggleMute,
    Speed,
    SpeedReset,
    FrameStep,
    LoopStart,
    LoopEnd,
    LoopClear,
    BookmarkAdd,
    BookmarkNext,
    AudioCycle,
    SubtitleCycle,
    SubtitleDelay,
    AudioDelay,
    ClipExport,
    GifExport,
    MiniMode,
    OpenUrl,
    PasteUrl,
    Stop,
    GoToStart,
    Edit,
    CompactOrFullscreen,
    SpeedAb,
    ChapterStep,
    CopyFrame,
    SubtitleVisibility,
    SubtitleLine,
    SubtitleStep,
    SubtitleCopy,
    GoToTime,
    BookmarkList,
    ToggleFill,
    FrameExport,
    ContactSheet,
    ToggleSilenceSkip
}

internal readonly record struct PlayerCommand(PlayerCommandKind Kind, double Amount)
{
    internal static readonly PlayerCommand None = new(PlayerCommandKind.None, 0);
}

internal sealed class SeekCoalescer
{
    private readonly Func<double, Task> _seek;
    private readonly object _gate = new();
    private readonly List<double> _latencies = new();
    private readonly List<double> _targets = new();
    private readonly List<string> _failures = new();

    private double _target;
    private bool _pending;
    private bool _running;
    private Task _pump = Task.CompletedTask;

    internal SeekCoalescer(Func<double, Task> seek) => _seek = seek;

    internal double Duration { get; set; } = double.PositiveInfinity;

    internal double Target { get { lock (_gate) return _target; } }

    internal double Position { get; private set; }

    internal int SeekCalls { get; private set; }

    internal IReadOnlyList<double> LatenciesMs { get { lock (_gate) return _latencies.ToArray(); } }

    internal IReadOnlyList<double> IssuedTargets { get { lock (_gate) return _targets.ToArray(); } }

    internal IReadOnlyList<string> Failures { get { lock (_gate) return _failures.ToArray(); } }

    internal Task Idle { get { lock (_gate) return _pump; } }

    internal void Nudge(double deltaSeconds) => GoTo(Target + deltaSeconds);

    internal void Follow(double atSeconds)
    {
        lock (_gate)
        {
            if (_pending || _running) return;
            _target = atSeconds < 0 ? 0 : atSeconds > Duration ? Duration : atSeconds;
            Position = _target;
        }
    }

    internal void GoTo(double atSeconds)
    {
        lock (_gate)
        {
            var clamped = atSeconds < 0 ? 0 : atSeconds > Duration ? Duration : atSeconds;
            _target = clamped;
            _pending = true;
            if (_running) return;
            _running = true;
            _pump = PumpAsync();
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            double at;
            lock (_gate)
            {
                if (!_pending)
                {
                    _running = false;
                    return;
                }

                _pending = false;
                at = _target;
                SeekCalls++;
                _targets.Add(at);
            }

            var watch = Stopwatch.StartNew();
            try
            {
                await _seek(at).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate) _failures.Add(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                watch.Stop();
                lock (_gate) _latencies.Add(watch.Elapsed.TotalMilliseconds);
                Position = at;
            }
        }
    }
}

internal readonly record struct WindowSnapshot(
    int State,
    double X,
    double Y,
    double Width,
    double Height,
    int TabIndex);

internal sealed class FullscreenSwitch
{
    private WindowSnapshot? _previous;

    internal bool IsFullscreen => _previous is not null;

    internal WindowSnapshot? Previous => _previous;

    internal WindowSnapshot Toggle(WindowSnapshot current, int fullscreenState, int playerTabIndex)
    {
        if (_previous is { } restore)
        {
            _previous = null;
            return restore;
        }

        _previous = current;
        return current with { State = fullscreenState, TabIndex = playerTabIndex };
    }

    internal WindowSnapshot? Leave()
    {
        if (_previous is not { } restore) return null;
        _previous = null;
        return restore;
    }
}

internal readonly record struct CompactRect(int X, int Y, int Width, int Height);

internal static class CompactWindow
{
    internal static CompactRect Fit(int areaX, int areaY, int areaWidth, int areaHeight, double share)
    {
        var k = Math.Sqrt(Math.Clamp(share, 0, 1));
        var width = (int)Math.Round(areaWidth * k);
        var height = (int)Math.Round(areaHeight * k);
        return new CompactRect(areaX + (areaWidth - width) / 2, areaY + (areaHeight - height) / 2, width, height);
    }

    internal static CompactRect Fit(int areaX, int areaY, int areaWidth, int areaHeight, double share, double aspect)
    {
        if (!(aspect > 0) || double.IsInfinity(aspect) || areaWidth <= 0 || areaHeight <= 0)
            return Fit(areaX, areaY, areaWidth, areaHeight, share);

        var surface = (double)areaWidth * areaHeight * Math.Clamp(share, 0, 1);
        var w = Math.Sqrt(surface * aspect);
        var h = w / aspect;
        if (w > areaWidth)
        {
            w = areaWidth;
            h = w / aspect;
        }

        if (h > areaHeight)
        {
            h = areaHeight;
            w = h * aspect;
        }

        var width = Math.Max(1, (int)Math.Round(w));
        var height = Math.Max(1, Math.Min(areaHeight, (int)Math.Round(width / aspect)));
        return new CompactRect(areaX + (areaWidth - width) / 2, areaY + (areaHeight - height) / 2, width, height);
    }
}

internal sealed class StallWatch
{
    private readonly double _limitSeconds;
    private long _frames = -1;
    private double _since;

    internal StallWatch(double limitSeconds = 2.0) => _limitSeconds = limitSeconds;

    internal bool Stalled { get; private set; }

    internal void Reset()
    {
        _frames = -1;
        Stalled = false;
    }

    internal bool Observe(bool playing, long framesDecoded, double nowSeconds)
    {
        if (!playing)
        {
            _frames = framesDecoded;
            _since = nowSeconds;
            Stalled = false;
            return false;
        }

        if (framesDecoded != _frames)
        {
            _frames = framesDecoded;
            _since = nowSeconds;
            Stalled = false;
            return false;
        }

        Stalled = nowSeconds - _since >= _limitSeconds;
        return Stalled;
    }
}

/// <summary>Birakisin sonucu: tiklama mi, surukleme mi, yoksa bize ait olmayan bir birakis mi.</summary>
internal enum ReleaseOutcome
{
    None,
    Click,
    Drag
}

/// <summary>
/// Sol basistan iki is dogar: tiklama duraklat/baslat, basili tutup surukleme pencere tasima
/// ya da pan. Tiklama birakista hemen islenir. Oynatici ve karsilastirma paneli bu sinifi
/// paylasir; ikisinde de cift tik ve basili tutma <see cref="SolTikHareketi"/> ile ustune eklenir.
///
/// Esik uydurulmadi: <see cref="DragThresholdDip"/> Windows'un kendi surukleme esigi
/// (SM_CXDRAG/SM_CYDRAG varsayilani 4 piksel). Bu kadar oynamayan bir basis tiklamadir.
/// </summary>
internal sealed class ClickArbiter
{
    /// <summary>Windows SM_CXDRAG/SM_CYDRAG varsayilani. Bunun altindaki oynama tiklamadir.</summary>
    internal const double DragThresholdDip = 4;

    private double _originX;
    private double _originY;
    private bool _down;
    private bool _dragging;

    /// <summary>Basis surukleme esigini gecti mi.</summary>
    internal bool Dragging => _dragging;

    internal bool Down => _down;

    internal double TravelX { get; private set; }

    internal double TravelY { get; private set; }

    internal void Press(double x, double y)
    {
        _down = true;
        _dragging = false;
        _originX = x;
        _originY = y;
        TravelX = 0;
        TravelY = 0;
    }

    /// <summary>Fare oynadi. Esigi ilk gecisten sonra hep <c>true</c> doner.</summary>
    internal bool Move(double x, double y)
    {
        if (!_down) return false;
        TravelX = x - _originX;
        TravelY = y - _originY;
        if (_dragging) return true;
        _dragging = Math.Abs(TravelX) >= DragThresholdDip || Math.Abs(TravelY) >= DragThresholdDip;
        return _dragging;
    }

    internal ReleaseOutcome Release()
    {
        if (!_down) return ReleaseOutcome.None;
        _down = false;
        if (_dragging)
        {
            _dragging = false;
            return ReleaseOutcome.Drag;
        }

        return ReleaseOutcome.Click;
    }

    internal void Cancel()
    {
        _down = false;
        _dragging = false;
    }
}

/// <summary>
/// Maksimize ve tam ekran kipinde surukleneni: pencere degil, panonun icindeki goruntu.
/// Kaydirma iki yone de tasma kadar, yani goruntunun panodan tasan yarisi kadar; tasma
/// yoksa kaydirilacak yer de yoktur.
///
/// Miknatis: merkeze <see cref="SnapDip"/> kadar yaklasan kaydirma sifira oturur, yani
/// goruntu kendiliginden ortalanir. Esik disaridan verilir — sinif hicbir olcu uydurmaz;
/// cagiran taraf onu <c>PlaybackBadgeMargin</c> belirtecinden okur.
/// </summary>
internal sealed class SurfacePan
{
    private readonly double _snap;

    internal SurfacePan(double snapDip)
    {
        if (snapDip < 0) throw new ArgumentOutOfRangeException(nameof(snapDip));
        _snap = snapDip;
    }

    /// <summary>Miknatis esigi. Yapicidan gelir, burada uretilmez.</summary>
    internal double SnapDip => _snap;

    internal double X { get; private set; }

    internal double Y { get; private set; }

    /// <summary>Yatay tasmanin yarisi: kaydirma bu araliktan disari cikamaz.</summary>
    internal double LimitX { get; private set; }

    internal double LimitY { get; private set; }

    /// <summary>Goruntu tam ortada mi — miknatisin tuttugu yer.</summary>
    internal bool Centered => X == 0 && Y == 0;

    /// <summary>Kaydirilacak yer var mi.</summary>
    internal bool CanPan => LimitX > 0 || LimitY > 0;

    internal void SetBounds(double contentWidth, double contentHeight, double viewportWidth, double viewportHeight)
    {
        LimitX = Math.Max(0, (contentWidth - viewportWidth) / 2);
        LimitY = Math.Max(0, (contentHeight - viewportHeight) / 2);
        Apply(X, Y);
    }

    internal bool Drag(double deltaX, double deltaY) => Apply(X + deltaX, Y + deltaY);

    internal bool Reset() => Apply(0, 0);

    private bool Apply(double x, double y)
    {
        var beforeX = X;
        var beforeY = Y;
        x = Clamp(x, -LimitX, LimitX);
        y = Clamp(y, -LimitY, LimitY);

        if (Math.Abs(x) <= _snap && Math.Abs(y) <= _snap)
        {
            x = 0;
            y = 0;
        }

        X = x;
        Y = y;
        return X != beforeX || Y != beforeY;
    }

    private static double Clamp(double value, double low, double high)
        => value < low ? low : value > high ? high : value;
}