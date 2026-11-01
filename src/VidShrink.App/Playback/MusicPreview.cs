using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Editing;
using VidShrink.Player;

namespace VidShrink.App.Playback;

internal sealed record PreviewCue(string Path, long Start, long In, long Length, double LevelDb)
{
    internal static PreviewCue From(EditMusic music) => new(music.Path, music.Start, 0, music.Duration, music.LevelDb);

    internal static PreviewCue From(EditAudioClip clip) => new(clip.Path, clip.Start, clip.In, clip.Length, clip.LevelDb);

    internal long End => Start + Length;
}

internal sealed class MusicPreview : IDisposable
{
    internal const double DriftSeconds = 0.25;

    internal const double PreloadSeconds = 1;

    internal const double CommandStepDb = 0.05;

    internal const double FilterStepDb = 1;

    private readonly Func<PlaybackOptions, IPlaybackEngine> _create;
    private IPlaybackEngine? _engine;
    private string? _path;
    private string? _failed;
    private Task _opening = Task.CompletedTask;
    private Task _seeking = Task.CompletedTask;
    private double? _level;
    private double _applied;
    private bool _commandless;
    private double _volume = 100;
    private bool _muted;
    private bool _playing;
    private bool _backward;
    private int _seeks;
    private int _generation;
    private bool _disposed;

    internal MusicPreview()
        : this(options => new MpvEngine(options))
    {
    }

    internal MusicPreview(Func<PlaybackOptions, IPlaybackEngine> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    internal bool Metered { get; init; }

    internal IPlaybackEngine? Engine => Volatile.Read(ref _engine);

    internal string? Path => _path;

    internal Task Opening => _opening;

    internal bool Playing => _playing;

    internal bool Backward => _backward;

    internal int Seeks => _seeks;

    internal double AppliedDb => _applied;

    internal double PeakDb => _playing && Metered && Engine is { } engine ? engine.AudioPeakDb : double.NaN;

    internal static string LevelFilter(double levelDb)
        => "volume=" + levelDb.ToString("0.0", CultureInfo.InvariantCulture) + "dB";

    internal static double? Offset(EditMusic music, long timeline) => Offset(PreviewCue.From(music), timeline);

    internal static double? Offset(PreviewCue cue, long timeline)
    {
        var local = timeline - cue.Start;
        return local >= 0 && local < cue.Length ? EditTime.ToSeconds(cue.In + local) : null;
    }

    internal static bool Upcoming(PreviewCue cue, long timeline)
    {
        var ahead = cue.Start - timeline;
        return ahead > 0 && ahead <= EditTime.FromSeconds(PreloadSeconds);
    }

    internal void Sync(EditMusic? music, long timeline, bool playing, double rate, double volume, bool muted, bool backward = false, double gainDb = 0)
        => Sync(music is null ? null : PreviewCue.From(music), timeline, playing, rate, volume, muted, backward, gainDb);

    internal void Sync(PreviewCue? cue, long timeline, bool playing, double rate, double volume, bool muted, bool backward = false, double gainDb = 0)
    {
        if (_disposed) return;
        if (cue is null)
        {
            Close();
            return;
        }

        if (_path is not null && !string.Equals(cue.Path, _path, StringComparison.Ordinal)) Close();

        var target = Offset(cue, timeline);
        if (!playing || target is not { } at || !double.IsFinite(rate) || rate <= 0)
        {
            if (playing && !backward && _path is null && Upcoming(cue, timeline)) Open(cue.Path);
            Pause();
            return;
        }

        if (_path is null)
        {
            Open(cue.Path);
            return;
        }

        if (Engine is not { } engine) return;

        var turned = false;
        if (_backward != backward)
        {
            if (!engine.SetPlayDirection(backward))
            {
                Pause();
                return;
            }

            _backward = backward;
            turned = true;
        }

        if (_level != cue.LevelDb)
        {
            _level = cue.LevelDb;
            _applied = cue.LevelDb;
            engine.SetEditTimed(null, LevelFilter(cue.LevelDb));
        }

        ApplyGain(engine, cue.LevelDb + gainDb, Math.Abs(gainDb) <= CommandStepDb);

        if (Math.Abs(_volume - volume) > 1e-9)
        {
            _volume = volume;
            engine.SetVolume(volume);
        }

        if (_muted != muted)
        {
            _muted = muted;
            engine.SetMuted(muted);
        }

        if (Math.Abs(engine.Speed - rate) > 1e-9) engine.SetSpeed(rate);
        if (!_playing || turned || (_seeking.IsCompleted && Math.Abs(engine.PositionSeconds - at) > DriftSeconds))
        {
            _seeking = engine.SeekAsync(at, SeekPrecision.Exact);
            _seeks++;
        }

        if (!_playing)
        {
            engine.Play();
            _playing = true;
        }
    }

    private void ApplyGain(IPlaybackEngine engine, double wanted, bool settled)
    {
        var gap = Math.Abs(wanted - _applied);
        if (gap <= CommandStepDb) return;
        if (!_commandless && engine.SetEditAudioVolume(wanted))
        {
            _applied = wanted;
            return;
        }

        _commandless = true;
        if (gap < FilterStepDb && !settled) return;
        engine.SetEditTimed(null, LevelFilter(wanted));
        _applied = Math.Round(wanted, 1);
    }

    internal void Pause()
    {
        if (!_playing) return;
        _playing = false;
        try { Engine?.Pause(); } catch (ObjectDisposedException) { }
    }

    internal void Close()
    {
        _generation++;
        var engine = Interlocked.Exchange(ref _engine, null);
        _path = null;
        _level = null;
        _applied = 0;
        _commandless = false;
        _volume = 100;
        _muted = false;
        _playing = false;
        _backward = false;
        _seeking = Task.CompletedTask;
        try { engine?.Dispose(); } catch (ObjectDisposedException) { }
    }

    private void Open(string path)
    {
        if (string.Equals(path, _failed, StringComparison.Ordinal)) return;
        _path = path;
        _opening = OpenAsync(path, ++_generation);
    }

    private async Task OpenAsync(string path, int generation)
    {
        IPlaybackEngine? engine = null;
        try
        {
            engine = await Task.Run(() => _create(new PlaybackOptions { Video = false })).ConfigureAwait(true);
            await engine.OpenAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PlaybackEngineUnavailableException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
        {
            engine?.Dispose();
            if (generation == _generation)
            {
                _failed = path;
                _path = null;
            }

            return;
        }

        if (_disposed || generation != _generation || !engine.HasAudio)
        {
            if (generation == _generation && !engine.HasAudio)
            {
                _failed = path;
                _path = null;
            }

            engine.Dispose();
            return;
        }

        if (Metered) engine.MeterAudio(true);
        engine.Pause();
        Volatile.Write(ref _engine, engine);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
