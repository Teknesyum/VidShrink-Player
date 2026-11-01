using System;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Player;

namespace VidShrink.App.Playback;

internal sealed class PreviewAudio : IDisposable
{
    private readonly Func<PlaybackOptions, IPlaybackEngine> _create;
    private readonly object _gate = new();

    private IPlaybackEngine? _engine;
    private string? _path;
    private int _seeks;
    private bool _silent;
    private bool _disposed;

    internal PreviewAudio()
        : this(options => new MpvEngine(options))
    {
    }

    internal PreviewAudio(Func<PlaybackOptions, IPlaybackEngine> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    internal bool HasAudio
    {
        get { lock (_gate) return _engine?.HasAudio == true; }
    }

    internal bool Attached
    {
        get { lock (_gate) return _engine is not null || _silent; }
    }

    internal int Seeks => Volatile.Read(ref _seeks);

    internal string? Path
    {
        get { lock (_gate) return _path; }
    }

    internal IPlaybackEngine? Engine
    {
        get { lock (_gate) return _engine; }
    }

    internal async Task AttachAsync(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path)) return;

        lock (_gate)
        {
            if ((_engine is not null || _silent) && string.Equals(_path, path, StringComparison.Ordinal)) return;
        }

        Close();

        IPlaybackEngine engine;
        try
        {
            engine = _create(new PlaybackOptions { Video = false });
        }
        catch
        {
            return;
        }

        try
        {
            await engine.OpenAsync(path).ConfigureAwait(false);
        }
        catch
        {
            engine.Dispose();
            lock (_gate)
            {
                if (!_disposed)
                {
                    _path = path;
                    _silent = true;
                }
            }
            return;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _engine = engine;
                _path = path;
                return;
            }
        }

        engine.Dispose();
    }

    internal void SeekTo(double atSeconds)
    {
        IPlaybackEngine? engine;
        lock (_gate) engine = _engine;
        if (engine is null || !engine.HasAudio) return;

        _ = engine.SeekAsync(Math.Max(0, atSeconds), SeekPrecision.Exact);
        Interlocked.Increment(ref _seeks);
    }

    internal void Play()
    {
        IPlaybackEngine? engine;
        lock (_gate) engine = _engine;
        if (engine is null || !engine.HasAudio) return;
        try { engine.Play(); } catch (ObjectDisposedException) { }
    }

    internal void SetSpeed(double speed)
    {
        IPlaybackEngine? engine;
        lock (_gate) engine = _engine;
        if (engine is null || !engine.HasAudio) return;
        try { engine.SetSpeed(speed); } catch (ObjectDisposedException) { }
    }

    internal void Pause()
    {
        IPlaybackEngine? engine;
        lock (_gate) engine = _engine;
        if (engine is null || !engine.HasAudio) return;
        try { engine.Pause(); } catch (ObjectDisposedException) { }
    }

    internal void Close()
    {
        IPlaybackEngine? engine;
        lock (_gate)
        {
            engine = _engine;
            _engine = null;
            _path = null;
            _silent = false;
        }

        try { engine?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
