using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Playback;
using VidShrink.Player;

namespace VidShrink.App.Playback;

public sealed class EngineComparisonFrameSource : IComparisonFrameSource
{
    public const int DefaultRingCapacity = 4;

    private const int ForceNone = 0;
    private const int ForcePublish = 1;
    private const int ForceHold = 2;

    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultPairWait = TimeSpan.FromMilliseconds(100);

    private readonly Func<PlaybackOptions, IPlaybackEngine> _create;
    private readonly int _ringCapacity;
    private readonly TimeSpan _pairWait;
    private readonly object _gate = new();

    private ComparisonFrameRequest? _request;
    private FramePool? _pool;
    private FrameRing? _ring;
    private IPlaybackEngine? _left;
    private IPlaybackEngine? _right;
    private double _speed = 1;
    private Thread? _pump;
    private CancellationTokenSource? _life;

    private ComparisonSourceState _state = ComparisonSourceState.Bosta;
    private string? _messageKey;
    private string? _messageArg;
    private long _produced;
    private long _sequence;
    private double _feedFps;
    private long _windowStartTicks;
    private long _windowFrames;
    private volatile bool _wantPlay = true;
    private volatile bool _ready;
    private int _force;
    private bool _disposed;

    public EngineComparisonFrameSource()
        : this(options => new MpvEngine(options))
    {
    }

    public EngineComparisonFrameSource(Func<PlaybackOptions, IPlaybackEngine> create, int ringCapacity = DefaultRingCapacity, TimeSpan? pairWait = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (ringCapacity < FrameRing.MinimumCapacity) throw new ArgumentOutOfRangeException(nameof(ringCapacity));
        if (pairWait is { } wait && wait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pairWait));
        _create = create;
        _ringCapacity = ringCapacity;
        _pairWait = pairWait ?? DefaultPairWait;
    }

    public event EventHandler<ComparisonSourceStatus>? StatusChanged;

    public ComparisonSourceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new ComparisonSourceStatus(
                    _state,
                    Interlocked.Read(ref _produced),
                    _ring?.Dropped ?? 0,
                    _feedFps,
                    0,
                    _pool?.Allocations ?? 0,
                    _messageKey,
                    _messageArg);
            }
        }
    }

    public IPlaybackEngine? LeftEngine { get { lock (_gate) return _left; } }

    public IPlaybackEngine? RightEngine { get { lock (_gate) return _right; } }

    public async Task StartAsync(ComparisonFrameRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await StopAsync().ConfigureAwait(false);

        var options = new PlaybackOptions
        {
            Audio = false,
            RenderWidth = request.PanelWidth,
            RenderHeight = request.PanelHeight,
            Loop = request.Loop
        };

        IPlaybackEngine left;
        IPlaybackEngine right;
        try
        {
            left = _create(options);
        }
        catch (Exception ex)
        {
            SetUnavailable(KeyFor(ex), ex.Message);
            return;
        }

        try
        {
            right = _create(options);
        }
        catch (Exception ex)
        {
            left.Dispose();
            SetUnavailable(KeyFor(ex), ex.Message);
            return;
        }

        lock (_gate)
        {
            _request = request;
            _state = ComparisonSourceState.Bosta;
            _left = left;
            _right = right;
            _pool = new FramePool(_ringCapacity + 2, request.FrameBytes);
            _ring = new FrameRing(_ringCapacity, _pool);
            _messageKey = null;
            _messageArg = null;
            _sequence = 0;
            _feedFps = 0;
            _ready = false;
            Interlocked.Exchange(ref _produced, 0);
        }

        SetState(ComparisonSourceState.Aciliyor);
        double speed;
        lock (_gate) speed = _speed;
        if (speed != 1) Hizla(left, right, speed);

        try
        {
            await Task.WhenAll(left.OpenAsync(request.LeftPath, ct), right.OpenAsync(request.RightPath, ct)).ConfigureAwait(false);
            if (request.Position > TimeSpan.Zero)
                await Task.WhenAll(
                    left.SeekAsync(request.Position.TotalSeconds, SeekPrecision.Exact, ct),
                    right.SeekAsync(request.Position.TotalSeconds, SeekPrecision.Exact, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await ReleaseAsync().ConfigureAwait(false);
            SetUnavailable(MpvEngine.FailedKey, ex.Message);
            return;
        }

        await FirstFramesAsync(left, right, ct).ConfigureAwait(false);

        var life = new CancellationTokenSource();
        var pump = new Thread(() => Pump(left, right, request, life.Token))
        {
            IsBackground = true,
            Name = "vidshrink-compare-engine",
            Priority = ThreadPriority.AboveNormal
        };

        lock (_gate)
        {
            if (!ReferenceEquals(_left, left)) { life.Dispose(); return; }
            _life = life;
            _pump = pump;
            _windowStartTicks = Stopwatch.GetTimestamp();
            _windowFrames = 0;
        }

        Interlocked.Exchange(ref _force, ForcePublish);
        _ready = true;
        pump.Start();
        if (_wantPlay) PlayBoth(left, right);
    }

    private static async Task FirstFramesAsync(IPlaybackEngine left, IPlaybackEngine right, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while ((left.FramesRendered == 0 || right.FramesRendered == 0) && clock.Elapsed < FirstFrameTimeout)
            await Task.Delay(2, ct).ConfigureAwait(false);
    }

    private static void PlayBoth(IPlaybackEngine left, IPlaybackEngine right)
    {
        try
        {
            left.Play();
            right.Play();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void PauseBoth(IPlaybackEngine left, IPlaybackEngine right)
    {
        try
        {
            left.Pause();
            right.Pause();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Pump(IPlaybackEngine left, IPlaybackEngine right, ComparisonFrameRequest request, CancellationToken ct)
    {
        FramePool pool;
        FrameRing ring;
        lock (_gate)
        {
            if (_pool is null || _ring is null) return;
            pool = _pool;
            ring = _ring;
        }

        var panelWidth = request.PanelWidth;
        var panelHeight = request.PanelHeight;
        var halfBytes = panelWidth * panelHeight * 4;
        var leftHalf = new byte[halfBytes];
        var rightHalf = new byte[halfBytes];
        var leftSeen = 0L;
        var rightSeen = 0L;
        var leftSeconds = double.NaN;
        var rightSeconds = double.NaN;
        var leftHas = false;
        var rightHas = false;
        var lastLeft = double.NaN;
        var lastRight = double.NaN;
        var leftStep = double.NaN;
        var rightStep = double.NaN;
        var holdSince = 0L;
        var ended = false;

        FrameCopy intoLeft = (pixels, width, height, stride) => CopyHalf(pixels, width, height, stride, leftHalf, panelWidth, panelHeight);
        FrameCopy intoRight = (pixels, width, height, stride) => CopyHalf(pixels, width, height, stride, rightHalf, panelWidth, panelHeight);

        while (!ct.IsCancellationRequested)
        {
            var force = Volatile.Read(ref _force);
            if (force == ForcePublish)
            {
                leftSeen = 0;
                rightSeen = 0;
            }

            bool gotLeft;
            bool gotRight;
            double ls;
            double rs;
            try
            {
                gotLeft = left.TryCopyLatest(ref leftSeen, intoLeft, out ls);
                gotRight = right.TryCopyLatest(ref rightSeen, intoRight, out rs);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (gotLeft) { leftStep = Step(leftSeconds, ls, leftStep); leftHas = true; leftSeconds = ls; }
            if (gotRight) { rightStep = Step(rightSeconds, rs, rightStep); rightHas = true; rightSeconds = rs; }
            if (!gotLeft && !gotRight)
            {
                if (!ended && !request.Loop && left.EndReached && right.EndReached)
                {
                    ended = true;
                    SetState(ComparisonSourceState.Durdu);
                }
                Thread.Sleep(1);
                if (holdSince == 0) continue;
            }

            if (!leftHas || !rightHas || force == ForceHold) continue;

            if (force == ForcePublish)
            {
                if (!(gotLeft && gotRight))
                {
                    Thread.Sleep(1);
                    continue;
                }
                if (Interlocked.CompareExchange(ref _force, ForceNone, ForcePublish) != ForcePublish) continue;
            }
            else if (SameTime(leftSeconds, lastLeft) && SameTime(rightSeconds, lastRight))
            {
                holdSince = 0;
                continue;
            }
            else if (Lags(leftSeconds, rightSeconds, leftStep, left) || Lags(rightSeconds, leftSeconds, rightStep, right))
            {
                if (holdSince == 0) holdSince = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(holdSince) < _pairWait) continue;
            }
            holdSince = 0;

            if (!TryAcquire(pool, ring, out var frame)) continue;
            Compose(leftHalf, rightHalf, frame.Buffer, panelWidth, panelHeight);
            var sequence = Interlocked.Increment(ref _sequence) - 1;
            frame.Describe(panelWidth * 2, panelHeight, panelWidth, Seconds(leftSeconds), Seconds(rightSeconds), sequence);
            ring.Publish(frame);
            lastLeft = leftSeconds;
            lastRight = rightSeconds;
            if (ended && (!left.EndReached || !right.EndReached)) ended = false;

            Interlocked.Increment(ref _produced);
            SampleFeedRate();
            if (Interlocked.Read(ref _produced) == 1)
                SetState(_wantPlay ? ComparisonSourceState.Oynuyor : ComparisonSourceState.Duraklatildi);
        }
    }

    private static bool SameTime(double a, double b)
        => (double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) < 1e-6;

    private static double Step(double previous, double next, double step)
    {
        var delta = next - previous;
        return delta > 1e-6 && delta < 1.0 ? delta : step;
    }

    private static bool Lags(double seconds, double other, double step, IPlaybackEngine engine)
        => seconds < other - 1e-6
            && !engine.EndReached
            && (double.IsNaN(step) || seconds + step <= other + step / 2);

    private static TimeSpan Seconds(double value)
        => double.IsFinite(value) && value > 0 ? TimeSpan.FromSeconds(value) : TimeSpan.Zero;

    private static void CopyHalf(IntPtr pixels, int width, int height, int stride, byte[] half, int panelWidth, int panelHeight)
    {
        var rowBytes = Math.Min(width, panelWidth) * 4;
        var rows = Math.Min(height, panelHeight);
        for (var y = 0; y < rows; y++)
            Marshal.Copy(pixels + y * stride, half, y * panelWidth * 4, rowBytes);
    }

    private static void Compose(byte[] left, byte[] right, byte[] target, int panelWidth, int panelHeight)
    {
        var half = panelWidth * 4;
        for (var y = 0; y < panelHeight; y++)
        {
            Buffer.BlockCopy(left, y * half, target, y * half * 2, half);
            Buffer.BlockCopy(right, y * half, target, y * half * 2 + half, half);
        }
    }

    private static bool TryAcquire(FramePool pool, FrameRing ring, out PlaybackFrame frame)
    {
        if (pool.TryRent(out frame)) return true;
        return ring.TryEvictOldest(out frame);
    }

    private void SampleFeedRate()
    {
        long frames;
        long startTicks;
        lock (_gate)
        {
            _windowFrames++;
            frames = _windowFrames;
            startTicks = _windowStartTicks;
        }

        var elapsed = Stopwatch.GetElapsedTime(startTicks);
        if (elapsed.TotalMilliseconds < 250) return;

        lock (_gate)
        {
            _feedFps = frames / elapsed.TotalSeconds;
            _windowFrames = 0;
            _windowStartTicks = Stopwatch.GetTimestamp();
        }
        RaiseStatus();
    }

    public bool TryTake(out PlaybackFrame frame)
    {
        FrameRing? ring;
        lock (_gate) ring = _ring;
        if (ring is null)
        {
            frame = null!;
            return false;
        }
        return ring.TryTake(out frame);
    }

    public void Return(PlaybackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        FramePool? pool;
        lock (_gate) pool = _pool;
        pool?.Return(frame);
    }

    public void Play()
    {
        if (_disposed) return;
        IPlaybackEngine? left;
        IPlaybackEngine? right;
        lock (_gate)
        {
            if (_state is ComparisonSourceState.Kullanilamiyor or ComparisonSourceState.Bosta) return;
            left = _left;
            right = _right;
        }
        _wantPlay = true;
        if (_ready && left is not null && right is not null) PlayBoth(left, right);
        SetState(ComparisonSourceState.Oynuyor);
    }

    public void SetSpeed(double speed)
    {
        if (_disposed || !(speed > 0)) return;
        IPlaybackEngine? left;
        IPlaybackEngine? right;
        lock (_gate)
        {
            _speed = speed;
            left = _left;
            right = _right;
        }
        Hizla(left, right, speed);
    }

    private static void Hizla(IPlaybackEngine? left, IPlaybackEngine? right, double speed)
    {
        try
        {
            left?.SetSpeed(speed);
            right?.SetSpeed(speed);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Pause()
    {
        if (_disposed) return;
        IPlaybackEngine? left;
        IPlaybackEngine? right;
        lock (_gate)
        {
            if (_state is not (ComparisonSourceState.Oynuyor or ComparisonSourceState.Aciliyor)) return;
            left = _left;
            right = _right;
        }
        _wantPlay = false;
        if (left is not null && right is not null) PauseBoth(left, right);
        SetState(ComparisonSourceState.Duraklatildi);
    }

    public async Task SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (position < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(position));

        IPlaybackEngine? left;
        IPlaybackEngine? right;
        lock (_gate)
        {
            left = _left;
            right = _right;
            if (_request is not null) _request = _request with { Position = position };
        }
        if (left is null || right is null) return;

        Interlocked.Exchange(ref _force, ForceHold);
        var resume = _wantPlay;
        PauseBoth(left, right);
        var seconds = position.TotalSeconds;
        try
        {
            await Task.WhenAll(
                left.SeekAsync(seconds, SeekPrecision.Exact, ct),
                right.SeekAsync(seconds, SeekPrecision.Exact, ct)).ConfigureAwait(false);

            lock (_gate)
            {
                if (!ReferenceEquals(_left, left)) return;
                _ring?.Clear();
                _sequence = 0;
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _force, ForcePublish, ForceHold);
        }

        if (resume && _wantPlay) PlayBoth(left, right);
    }

    public async Task StopAsync()
    {
        await ReleaseAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _ring?.Clear();
            if (_state is not (ComparisonSourceState.Bosta or ComparisonSourceState.Kullanilamiyor))
                _state = ComparisonSourceState.Durdu;
        }
        RaiseStatus();
    }

    private async Task ReleaseAsync()
    {
        IPlaybackEngine? left;
        IPlaybackEngine? right;
        Thread? pump;
        CancellationTokenSource? life;
        lock (_gate)
        {
            left = _left;
            right = _right;
            pump = _pump;
            life = _life;
            _left = null;
            _right = null;
            _pump = null;
            _life = null;
            _ready = false;
        }

        life?.Cancel();
        if (pump is not null && pump.IsAlive) await Task.Run(() => pump.Join(2000)).ConfigureAwait(false);
        await Task.Run(() =>
        {
            try { left?.Dispose(); } catch { }
            try { right?.Dispose(); } catch { }
        }).ConfigureAwait(false);
        life?.Dispose();
    }

    internal static string KeyFor(Exception ex)
        => ex is PlaybackEngineUnavailableException { MessageKey: { } key } ? key : MpvEngine.FailedKey;

    private void SetUnavailable(string key, string? arg = null)
    {
        lock (_gate)
        {
            _state = ComparisonSourceState.Kullanilamiyor;
            _messageKey = key;
            _messageArg = arg;
        }
        RaiseStatus();
    }

    private void SetState(ComparisonSourceState state)
    {
        lock (_gate)
        {
            if (_state == state || _state == ComparisonSourceState.Kullanilamiyor) return;
            _state = state;
        }
        RaiseStatus();
    }

    private void RaiseStatus() => StatusChanged?.Invoke(this, Status);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ReleaseAsync().GetAwaiter().GetResult(); } catch { }
        lock (_gate)
        {
            _ring?.Clear();
            _ring = null;
            _pool = null;
        }
    }
}
