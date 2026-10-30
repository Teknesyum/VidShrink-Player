using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static VidShrink.Player.Native;

[assembly: InternalsVisibleTo("VidShrink.Tests")]

namespace VidShrink.Player;

public sealed partial class MpvEngine : IPlaybackEngine
{
    public const string FailedKey = "main.error.unusable";

    private const ulong OpenTag = 1;
    private const ulong ControlTag = 2;
    private const ulong ShotTag = 3;
    private const ulong SeekTag = 1UL << 40;
    private const string MirrorLabel = "vsmirror";
    private const string MirrorFilter = "@vsmirror:hflip";
    private const string RotateLabel = "vsrotate";
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(10);
    internal const int ScreenshotJpegQuality = 92;
    private const ulong EofId = 1;
    private const ulong TimeId = 2;
    private const ulong PauseId = 3;
    private const ulong PlaylistPosId = 4;
    private const int LogCapacity = 64;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static readonly TimeSpan MediaControlsSettle = TimeSpan.FromMilliseconds(100);    private static readonly IntPtr SwType = Marshal.StringToCoTaskMemUTF8("sw");
    private static readonly IntPtr BgraFormat = Marshal.StringToCoTaskMemUTF8("bgra");

    private readonly PlaybackOptions _options;
    private readonly AutoResetEvent _update = new(false);
    private readonly object _frameGate = new();
    private readonly object _seekGate = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly Thread _events;
    private readonly Thread _renderer;

    private GCHandle _updateHandle;
    private IntPtr _mpv;
    private IntPtr _render;
    private volatile bool _stopping;
    private int _disposed;

    private FrameBuffer? _front;
    private FrameBuffer? _back;
    private long _serial;
    private long _framesRendered;
    private long _restarts;
    private int _videoWidth;
    private int _videoHeight;

    private TaskCompletionSource? _open;
    private TaskCompletionSource<bool>? _shot;
    private volatile bool _isOpen;
    private volatile bool _hasAudio;
    private volatile bool _hasVideo = true;
    private volatile bool _paused = true;
    private volatile bool _eof;
    private double _duration;
    private double _position;

    private long _seekGen;
    private long _replyGen = -1;
    private long _armedGen = -1;
    private double _issuedAt;
    private TaskCompletionSource<SeekResult>? _seekDone;
    private long _restartGen = -1;
    private double _restartAt;
    private bool _newFrameInFlight;
    private long _newFramesShown;
    private long _newFramesAtIssue;
    private double _newFrameAt;
    private readonly object _handleGate = new();
    private string? _source;
    private readonly TimeSpan _createdAt = Clock.Elapsed;
    private long _neighborHome = -1;
    private (string? Previous, string? Next) _neighbors;

    public unsafe MpvEngine(PlaybackOptions? options = null)
    {
        _options = options ?? new PlaybackOptions();
        LibMpvLocator.EnsureLoaded();

        _mpv = mpv_create();
        if (_mpv == IntPtr.Zero) throw new PlaybackEngineUnavailableException("mpv_create returned NULL.");

        try
        {
            foreach (var (name, value) in OptionsFor(_options))
            {
                var rc = mpv_set_option_string(_mpv, name, value);
                if (!Absent(name, rc)) Check(rc, $"option {name}={value}");
            }

            Check(mpv_initialize(_mpv), "mpv_initialize");
            Check(mpv_request_log_messages(_mpv, "info"), "mpv_request_log_messages");
            Check(mpv_observe_property(_mpv, EofId, "eof-reached", MPV_FORMAT_FLAG), "observe eof-reached");
            Check(mpv_observe_property(_mpv, TimeId, "time-pos", MPV_FORMAT_DOUBLE), "observe time-pos");
            Check(mpv_observe_property(_mpv, PauseId, "pause", MPV_FORMAT_FLAG), "observe pause");
            if (_options.MediaControls) Check(mpv_observe_property(_mpv, PlaylistPosId, "playlist-pos", MPV_FORMAT_INT64), "observe playlist-pos");

            var parameters = stackalloc MpvRenderParam[2];
            parameters[0] = new MpvRenderParam(MPV_RENDER_PARAM_API_TYPE, SwType);
            parameters[1] = new MpvRenderParam(MPV_RENDER_PARAM_INVALID, IntPtr.Zero);
            IntPtr render;
            Check(mpv_render_context_create(&render, _mpv, parameters), "mpv_render_context_create");
            _render = render;

            _updateHandle = GCHandle.Alloc(_update);
            mpv_render_context_set_update_callback(_render, &OnUpdate, GCHandle.ToIntPtr(_updateHandle));
        }
        catch
        {
            Release();
            throw;
        }

        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _renderer = new Thread(RenderLoop) { IsBackground = true, Name = "mpv-render" };
        _events.Start();
        _renderer.Start();
    }

    /// <summary>
    /// Uzaktan oynatilan semalar: libmpv bunlari kendisi acar. Yerel yol eskisi gibi tam
    /// yola cevrilir; <c>file</c> semasi listede degil, cunku o da yerel yoldur.
    /// </summary>
    public static readonly IReadOnlyList<string> RemoteSchemes = new[] { "http", "https", "rtsp", "rtmp", "srt", "udp" };

    /// <summary>Bir kaynagin libmpv'ye verilecek hali: adres oldugu gibi, dosya tam yolla.</summary>
    public static string Target(string path)
        => path.StartsWith("edl://", StringComparison.OrdinalIgnoreCase)
            ? path
            : Uri.TryCreate(path, UriKind.Absolute, out var uri)
           && RemoteSchemes.Any(scheme => string.Equals(scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase))
            ? path.Trim()
            : Path.GetFullPath(path);

    public static IReadOnlyList<(string Name, string Value)> OptionsFor(PlaybackOptions options)
    {
        var list = new List<(string Name, string Value)>(BaseOptions(options));
        if (OperatingSystem.IsLinux()) list.Add(("ao", LinuxAudioOutputs));
        if (!options.Audio) list.Add(("aid", "no"));
        if (!options.Video) list.Add(("vid", "no"));
        if (options.Loop) list.Add(("loop-file", "inf"));
        list.Add(("media-controls", options.MediaControls ? "yes" : "no"));
        return list;
    }

    /// <summary>
    /// Linux'ta ses cikisi sirasi SDL'siz. Ses sunucusu yokken (kok kullanici, masaustu oturumu
    /// disi) libmpv'nin varsayilan sirasi SDL'e iner; SDL'in ses acilisi is parcacigi guvenli
    /// degil ve oynatici ile karsilastirma motorlari ayni anda acilinca SDL icinde bos isaretciye
    /// atlayip sureci dusuruyordu (iki dosya ayni anda iletilince 2 kosumdan 1'i). Listede
    /// olmayan surucu atlanir, hicbiri acilmazsa <c>audio-fallback-to-null</c> sessiz surer.
    /// </summary>
    public const string LinuxAudioOutputs = "pulse,pipewire,alsa,jack";

    /// <summary>
    /// <c>hwdec</c> degeri. Cizim yazilimda (<c>vo=libmpv</c>, SW render) oldugu icin donanim
    /// cozme yalniz kopyali kipte calisir; kopyasiz kip kareyi bellege indirmez.
    /// </summary>
    public static string HardwareValue(HardwareDecoding mode) => mode == HardwareDecoding.AutoCopy ? "auto-copy" : "no";

    private static (string Name, string Value)[] BaseOptions(PlaybackOptions options) => new[]
    {
        ("vo", "libmpv"),
        ("hwdec", HardwareValue(options.Hardware)),
        ("keep-open", "always"),
        ("idle", "yes"),
        ("pause", "yes"),
        ("terminal", "no"),
        ("input-default-bindings", "no"),
        ("input-vo-keyboard", "no"),
        ("load-scripts", "no"),
        ("ytdl", "no"),
        ("osd-level", "0"),
        ("osd-bar", "no"),
        ("sub-auto", "no"),
        ("sid", "no"),
        ("audio-display", "embedded-first"),
        ("audio-fallback-to-null", "yes")
    };

    public event EventHandler<PlaybackFault>? Faulted;

    public event EventHandler<bool>? NeighborRequested;

    internal Action<int>? BeforeEvent { get; set; }

    public string Name => "libmpv";

    public bool IsOpen => _isOpen;

    public double DurationSeconds => Volatile.Read(ref _duration);

    public bool HasAudio => _hasAudio;

    public bool HasVideo => _hasVideo;

    public MediaTags Tags => _isOpen ? new MediaTags(Tag("title"), Tag("artist"), Tag("album")) : MediaTags.Empty;

    public string? MediaTitle
    {
        get
        {
            if (!_isOpen || GetProperty("media-title") is not { Length: > 0 } raw) return null;
            var title = raw.Trim();
            return title.Length == 0 || string.Equals(title, GetProperty("filename"), StringComparison.Ordinal) ? null : title;
        }
    }

    private string? Tag(string key) => GetProperty("metadata/by-key/" + key) is { Length: > 0 } value ? value.Trim() : null;

    public bool IsPaused => _paused;

    public bool EndReached => _eof;

    public double PositionSeconds => Volatile.Read(ref _position);

    public long FramesRendered => Interlocked.Read(ref _framesRendered);

    public long PlaybackRestarts => Interlocked.Read(ref _restarts);

    public IReadOnlyList<string> RecentLog => _log.ToArray();

    public double AudioVideoOffsetSeconds
    {
        get
        {
            if (!_hasAudio) return double.NaN;
            lock (_handleGate)
            {
                if (!HandleAlive) return double.NaN;
                var video = GetDouble("time-pos");
                var audio = GetDouble("audio-pts");
                return double.IsFinite(video) && double.IsFinite(audio) ? video - audio : double.NaN;
            }
        }
    }

    public string? GetProperty(string name)
    {
        lock (_handleGate)
        {
            if (!HandleAlive) return null;
            var p = mpv_get_property_string(_mpv, name);
            if (p == IntPtr.Zero) return null;
            var value = Marshal.PtrToStringUTF8(p);
            mpv_free(p);
            return value;
        }
    }

    public void SetProperty(string name, string value)
    {
        lock (_handleGate)
        {
            ObjectDisposedException.ThrowIf(!HandleAlive, this);
            Check(mpv_set_property_string(_mpv, name, value), $"set {name}={value}");
        }
    }

    private bool HandleAlive => Volatile.Read(ref _disposed) == 0 && _mpv != IntPtr.Zero;

    public async Task OpenAsync(string path, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _open, open);
        var target = Target(path);
        Volatile.Write(ref _source, target);
        Interlocked.Exchange(ref _neighborHome, -1);
        _neighbors = default;
        Command(OpenTag, "loadfile", target);

        var timeout = Task.Delay(_options.OpenTimeout, ct);
        var finished = await Task.WhenAny(open.Task, timeout).ConfigureAwait(false);
        if (finished != open.Task)
        {
            Interlocked.CompareExchange(ref _open, null, open);
            ct.ThrowIfCancellationRequested();
            throw new PlaybackOpenException(Describe($"libmpv did not load the file within {_options.OpenTimeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s."));
        }

        await open.Task.ConfigureAwait(false);
    }

    public double Speed => RestSpeed is var rest && double.IsFinite(rest) ? rest : Finite(GetDouble("speed"), 1);

    public double Volume => Finite(GetDouble("volume"), 100);

    public bool Muted => GetProperty("mute") == "yes";

    public double FramesPerSecond
    {
        get
        {
            var container = GetDouble("container-fps");
            return double.IsFinite(container) && container > 0 ? container : GetDouble("estimated-vf-fps");
        }
    }

    public double LoopStartSeconds => GetDouble("ab-loop-a");

    public double LoopEndSeconds => GetDouble("ab-loop-b");

    public void SetSpeed(double speed)
    {
        if (!SpeedWhileRushing(speed)) TrySet("speed", Number(speed));
    }

    public void SetVolume(double volume) => TrySet("volume", Number(volume));

    public void SetMuted(bool muted) => TrySet("mute", muted ? "yes" : "no");

    public void StepFrame(bool backward)
    {
        if (!_isOpen) return;
        CommandRc(ControlTag, backward ? "frame-back-step" : "frame-step");
    }

    public bool StepChapter(bool backward)
    {
        if (!_isOpen) return false;
        var count = ReadInt("chapter-list/count");
        if (count <= 0) return false;
        if (!backward && ReadInt("chapter") >= count - 1) return false;
        return CommandRc(ControlTag, "add", "chapter", backward ? "-1" : "1") >= 0;
    }

    public void SetLoop(double startSeconds, double endSeconds)
    {
        TrySet("ab-loop-a", double.IsFinite(startSeconds) ? Number(Math.Max(0, startSeconds)) : "no");
        TrySet("ab-loop-b", double.IsFinite(endSeconds) ? Number(Math.Max(0, endSeconds)) : "no");
    }

    public IReadOnlyList<PlaybackTrack> Tracks
    {
        get
        {
            var tracks = new List<PlaybackTrack>();
            if (!long.TryParse(GetProperty("track-list/count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) return tracks;
            for (var i = 0; i < count; i++)
            {
                PlaybackTrackKind? kind = GetProperty($"track-list/{i}/type") switch
                {
                    "audio" => PlaybackTrackKind.Audio,
                    "sub" => PlaybackTrackKind.Subtitle,
                    "video" => PlaybackTrackKind.Video,
                    _ => null
                };
                if (kind is null) continue;
                if (!long.TryParse(GetProperty($"track-list/{i}/id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                tracks.Add(new PlaybackTrack(
                    id,
                    kind.Value,
                    GetProperty($"track-list/{i}/title"),
                    GetProperty($"track-list/{i}/lang"),
                    GetProperty($"track-list/{i}/external") == "yes",
                    GetProperty($"track-list/{i}/selected") == "yes",
                    GetProperty($"track-list/{i}/forced") == "yes"));
            }

            return tracks;
        }
    }

    public long AudioTrack => TrackId("current-tracks/audio/id");

    public long SubtitleTrack => TrackId("current-tracks/sub/id");

    public long SecondarySubtitleTrack => TrackId("current-tracks/sub2/id");

    public double SubtitleDelaySeconds => Finite(GetDouble("sub-delay"), 0);

    public double AudioDelaySeconds => Finite(GetDouble("audio-delay"), 0);

    public double SubtitleScale => Finite(GetDouble("sub-scale"), 1);

    public double SubtitlePosition => Finite(GetDouble("sub-pos"), 100);

    public string SubtitleCodepage => GetProperty("sub-codepage") ?? "auto";

    public void SetAudioTrack(long id) => TrySet("aid", TrackValue(id));

    public void SetSubtitleTrack(long id) => TrySet("sid", TrackValue(id));

    public void SetSecondarySubtitleTrack(long id) => TrySet("secondary-sid", TrackValue(id));

    public bool AddSubtitle(string path)
    {
        if (!_isOpen || !File.Exists(path)) return false;
        return CommandSync("sub-add", Path.GetFullPath(path), "select") >= 0;
    }

    public long AddOverlay(string path)
    {
        if (!_isOpen || !File.Exists(path)) return 0;
        var full = Path.GetFullPath(path);
        if (CommandSync("sub-add", full, "select") < 0) return 0;
        var count = ReadInt("track-list/count");
        for (var i = count - 1; i >= 0; i--)
        {
            if (GetProperty($"track-list/{i}/type") != "sub") continue;
            if (!string.Equals(GetProperty($"track-list/{i}/external-filename"), full, StringComparison.OrdinalIgnoreCase)) continue;
            if (long.TryParse(GetProperty($"track-list/{i}/id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return id;
        }

        return 0;
    }

    public bool ReloadOverlay(long id)
        => id > 0 && _isOpen && CommandSync("sub-reload", id.ToString(CultureInfo.InvariantCulture)) >= 0;

    public bool RemoveOverlay(long id)
        => id > 0 && _isOpen && CommandSync("sub-remove", id.ToString(CultureInfo.InvariantCulture)) >= 0;

    public void SetSubtitleFontsDir(string? directory) => TrySet("sub-fonts-dir", directory ?? string.Empty);

    public void SetSubtitleDelay(double seconds) => TrySet("sub-delay", Number(seconds));

    public void SetAudioDelay(double seconds) => TrySet("audio-delay", Number(seconds));

    public void SetSubtitleScale(double scale) => TrySet("sub-scale", Number(scale));

    public void SetSubtitlePosition(double percent) => TrySet("sub-pos", Number(Math.Round(percent)));

    public void SetSubtitleCodepage(string codepage)
    {
        if (!TrySet("sub-codepage", codepage) || !_isOpen) return;
        foreach (var track in Tracks)
            if (track is { Kind: PlaybackTrackKind.Subtitle, External: true })
                CommandSync("sub-reload", track.Id.ToString(CultureInfo.InvariantCulture));
    }

    public bool SubtitleVisible => GetProperty("sub-visibility") != "no";

    public void SetSubtitleVisible(bool visible) => TrySet("sub-visibility", visible ? "yes" : "no");

    public bool SeekSubtitle(bool backward) => _isOpen && CommandSync("sub-seek", backward ? "-1" : "1") >= 0;

    public bool StepSubtitle(bool backward) => _isOpen && CommandSync("sub-step", backward ? "-1" : "1") >= 0;

    public string? SubtitleText => GetProperty("sub-text");

    private long TrackId(string name)
        => long.TryParse(GetProperty(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    private static string TrackValue(long id) => id > 0 ? id.ToString(CultureInfo.InvariantCulture) : "no";

    public int Rotation => Segment(GetProperty("vf") ?? "", RotateLabel) switch
    {
        null => 0,
        var s when s.Contains("cclock", StringComparison.Ordinal) => 270,
        var s when s.Contains("clock", StringComparison.Ordinal) => 90,
        var s when s.Contains("vflip", StringComparison.Ordinal) => 180,
        _ => 0
    };

    public bool Mirrored => (GetProperty("vf") ?? "").Contains(MirrorLabel, StringComparison.Ordinal);

    public double AspectOverride => Finite(GetDouble("video-aspect-override"), -1);

    public bool RepeatFile => GetProperty("loop-file") is "inf" or "yes";

    public HardwareDecoding Hardware
        => GetProperty("hwdec") == HardwareValue(HardwareDecoding.AutoCopy) ? HardwareDecoding.AutoCopy : HardwareDecoding.Off;

    public void SetHardwareDecoding(HardwareDecoding mode) => TrySet("hwdec", HardwareValue(mode));

    public IReadOnlyList<double> ChapterTimes
    {
        get
        {
            var count = ReadInt("chapter-list/count");
            var times = new List<double>();
            for (var i = 0; i < count; i++)
            {
                var at = GetDouble($"chapter-list/{i}/time");
                if (double.IsFinite(at)) times.Add(at);
            }

            return times;
        }
    }

    public IReadOnlyList<string> ChapterTitles
    {
        get
        {
            var count = ReadInt("chapter-list/count");
            var titles = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (!double.IsFinite(GetDouble($"chapter-list/{i}/time"))) continue;
                titles.Add(GetProperty($"chapter-list/{i}/title") ?? "");
            }

            return titles;
        }
    }

    public MediaDetails? Details
    {
        get
        {
            if (!_isOpen) return null;
            string? videoCodec = null;
            string? audioCodec = null;
            var width = 0;
            var height = 0;
            var fps = double.NaN;
            var channels = 0;
            var sampleRate = 0;
            var profile = 0;
            var videoSelected = false;
            var audioSelected = false;

            var count = ReadInt("track-list/count");
            for (var i = 0; i < count; i++)
            {
                var type = GetProperty($"track-list/{i}/type");
                var selected = GetProperty($"track-list/{i}/selected") == "yes";
                if (type == "video" && GetProperty($"track-list/{i}/albumart") != "yes" && (videoCodec is null || (selected && !videoSelected)))
                {
                    videoCodec = GetProperty($"track-list/{i}/codec");
                    width = (int)ReadInt($"track-list/{i}/demux-w");
                    height = (int)ReadInt($"track-list/{i}/demux-h");
                    fps = GetDouble($"track-list/{i}/demux-fps");
                    profile = (int)ReadInt($"track-list/{i}/dolby-vision-profile");
                    videoSelected = selected;
                }
                else if (type == "audio" && (audioCodec is null || (selected && !audioSelected)))
                {
                    audioCodec = GetProperty($"track-list/{i}/codec");
                    channels = (int)ReadInt($"track-list/{i}/demux-channel-count");
                    sampleRate = (int)ReadInt($"track-list/{i}/demux-samplerate");
                    audioSelected = selected;
                }
            }

            if (!double.IsFinite(fps) || fps <= 0) fps = FramesPerSecond;
            var size = ReadInt("file-size");
            var duration = DurationSeconds;
            var bits = size > 0 && duration > 0 ? size * 8.0 / duration : GetDouble("video-bitrate");
            return new MediaDetails(videoCodec, width, height, fps, bits, audioCodec, channels, sampleRate,
                GetProperty("video-params/primaries"),
                GetProperty("video-params/gamma"),
                MediaDetails.BitDepthOf(GetProperty("video-params/pixelformat")),
                profile);
        }
    }

    public PlaybackStats? Stats
        => _isOpen
            ? new PlaybackStats(
                ReadInt("frame-drop-count"),
                ReadInt("decoder-frame-drop-count"),
                GetProperty("hwdec-current"),
                GetDouble("video-bitrate"),
                GetDouble("estimated-vf-fps"))
            : null;

    public void SetRotation(int degrees)
    {
        var normal = ((degrees % 360) + 360) % 360;
        Swap(RotateLabel, normal switch
        {
            90 => "lavfi=[transpose=clock]",
            180 => "lavfi=[hflip,vflip]",
            270 => "lavfi=[transpose=cclock]",
            _ => null
        });
        _update.Set();
    }

    public void SetMirrored(bool mirrored)
    {
        if (mirrored) CommandSync("vf", "add", MirrorFilter);
        else CommandSync("vf", "remove", "@" + MirrorLabel);
    }

    public void SetAspectOverride(string ratio) => TrySet("video-aspect-override", string.IsNullOrWhiteSpace(ratio) ? "-1" : ratio);

    public void SetRepeatFile(bool repeat) => TrySet("loop-file", repeat ? "inf" : "no");

    public void SetNeighbors(string? previous, string? next)
    {
        if (!_options.MediaControls) return;
        _neighbors = (previous, next);
        ApplyNeighbors();
    }

    private void ApplyNeighbors()
    {
        if (!_options.MediaControls || !_isOpen) return;
        lock (_handleGate)
        {
            if (!HandleAlive) return;
            Interlocked.Exchange(ref _neighborHome, -1);
            if (CommandSync("playlist-clear") < 0) return;
            var (previous, next) = _neighbors;
            var home = 0;
            if (previous is not null && CommandSync("loadfile", Target(previous), "append") >= 0)
            {
                if (CommandSync("playlist-move", "1", "0") >= 0) home = 1;
                else CommandSync("playlist-remove", "1");
            }

            if (next is not null) CommandSync("loadfile", Target(next), "append");
            Interlocked.Exchange(ref _neighborHome, home);
        }
    }

    internal void OnPlaylistPos(long position)
    {
        var home = Interlocked.Read(ref _neighborHome);
        if (home < 0 || position < 0 || position == home) return;
        position = ReadInt("playlist-pos");
        if (position < 0 || position == home) return;
        if (Interlocked.CompareExchange(ref _neighborHome, -1, home) != home) return;
        NeighborRequested?.Invoke(this, position > home);
    }

    public Task<bool> SaveScreenshotAsync(string path, CancellationToken ct = default) => SaveScreenshotAsync(path, false, ct);

    public async Task<bool> SaveScreenshotAsync(string path, bool withSubtitles, CancellationToken ct = default)
    {
        if (!_isOpen || Volatile.Read(ref _disposed) != 0) return false;
        var target = Path.GetFullPath(path);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _shot, done)?.TrySetResult(false);
        TrySet("screenshot-jpeg-quality", ScreenshotJpegQuality.ToString(CultureInfo.InvariantCulture));

        if (CommandRc(ShotTag, "screenshot-to-file", target, withSubtitles ? "subtitles" : "video") < 0)
        {
            Interlocked.CompareExchange(ref _shot, null, done);
            return false;
        }

        var finished = await Task.WhenAny(done.Task, Task.Delay(ScreenshotTimeout, ct)).ConfigureAwait(false);
        if (finished != done.Task)
        {
            Interlocked.CompareExchange(ref _shot, null, done);
            return false;
        }

        return await done.Task.ConfigureAwait(false) && File.Exists(target);
    }

    private unsafe long ReadInt(string name)
    {
        lock (_handleGate)
        {
            if (!HandleAlive) return 0;
            long value;
            return mpv_get_property(_mpv, name, MPV_FORMAT_INT64, &value) < 0 ? 0 : value;
        }
    }

    private unsafe int CommandSync(params string[] args)
    {
        var pointers = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            lock (_handleGate)
            {
                if (!HandleAlive) return -1;
                fixed (IntPtr* p = pointers) return mpv_command(_mpv, p);
            }
        }
        finally
        {
            foreach (var pointer in pointers)
                if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private bool TrySet(string name, string value)
    {
        lock (_handleGate)
        {
            if (!HandleAlive) return false;
            return mpv_set_property_string(_mpv, name, value) >= 0;
        }
    }

    public void Play() => Command(ControlTag, "set", "pause", "no");

    public void Pause() => Command(ControlTag, "set", "pause", "yes");

    public bool ReloadAudio()
    {
        if (!_isOpen || !_hasAudio) return false;
        return CommandSync("ao-reload") >= 0;
    }

    public Task<bool> ReopenAsync(double atSeconds, bool playing, CancellationToken ct = default)
        => ReopenCoreAsync(atSeconds, playing, true, ct);

    public long StreamBitrate => TrackId("current-tracks/video/hls-bitrate") is > 0 and var video ? video : TrackId("current-tracks/audio/hls-bitrate");

    public static string StreamBitrateValue(long bitrate) => bitrate > 0 ? bitrate.ToString(CultureInfo.InvariantCulture) : "max";

    public async Task<bool> SetStreamBitrateAsync(long bitrate, CancellationToken ct = default)
    {
        if (!_isOpen || !TrySet("hls-bitrate", StreamBitrateValue(bitrate))) return false;
        return await ReopenCoreAsync(PositionSeconds, !IsPaused, false, ct).ConfigureAwait(false);
    }

    private async Task<bool> ReopenCoreAsync(double atSeconds, bool playing, bool restoreTracks, CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _source) is not { } source) return false;

        var audio = AudioTrack;
        var subtitle = SubtitleTrack;
        var secondary = SecondarySubtitleTrack;
        var external = ExternalSubtitles();
        if (!TrySet("pause", playing ? "no" : "yes")) return false;

        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _open, open);
        var start = "start=" + Number(Math.Max(0, double.IsFinite(atSeconds) ? atSeconds : 0));
        Interlocked.Exchange(ref _neighborHome, -1);
        // mpv 0.38 loadfile'a <index> argumanini ekledi; 0.37 (Ubuntu 24.04, Debian 12'nin
        // sistem libmpv'si) ucuncu argumani secenek sayip "-1"i reddediyor ve takilma kurtarmasinin
        // yeniden acmasi Linux'ta hic calismiyordu. Ret eszamanli doner; eski bicimle bir kez daha denenir.
        if (CommandRc(OpenTag, "loadfile", source, "replace", "-1", start) < 0
            && CommandRc(OpenTag, "loadfile", source, "replace", start) < 0)
        {
            Interlocked.CompareExchange(ref _open, null, open);
            return false;
        }

        var finished = await Task.WhenAny(open.Task, Task.Delay(_options.OpenTimeout, ct)).ConfigureAwait(false);
        if (finished != open.Task || open.Task.IsFaulted)
        {
            Interlocked.CompareExchange(ref _open, null, open);
            return false;
        }

        foreach (var file in external) CommandSync("sub-add", file, "auto");
        ApplyNeighbors();
        if (!restoreTracks) return true;
        if (audio > 0 && AudioTrack != audio) SetAudioTrack(audio);
        if (SubtitleTrack != subtitle) SetSubtitleTrack(subtitle);
        if (SecondarySubtitleTrack != secondary) SetSecondarySubtitleTrack(secondary);
        return true;
    }

    private List<string> ExternalSubtitles()
    {
        var files = new List<string>();
        var count = ReadInt("track-list/count");
        for (var i = 0; i < count; i++)
        {
            if (GetProperty($"track-list/{i}/type") != "sub" || GetProperty($"track-list/{i}/external") != "yes") continue;
            if (GetProperty($"track-list/{i}/external-filename") is { Length: > 0 } file) files.Add(file);
        }

        return files;
    }

    public async Task<SeekResult> SeekAsync(double seconds, SeekPrecision precision, CancellationToken ct = default)
    {
        if (!_isOpen || Volatile.Read(ref _disposed) != 0) return new SeekResult(SeekOutcome.Failed, 0);

        var done = new TaskCompletionSource<SeekResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        long gen;
        TaskCompletionSource<SeekResult>? previous;
        lock (_seekGate)
        {
            gen = ++_seekGen;
            previous = _seekDone;
            _seekDone = done;
            _issuedAt = Now;
            _newFramesAtIssue = _newFramesShown;
        }

        previous?.TrySetResult(new SeekResult(SeekOutcome.Superseded, 0));

        var target = Math.Max(0, seconds).ToString("0.######", CultureInfo.InvariantCulture);
        var flags = precision == SeekPrecision.Exact ? "absolute+exact" : "absolute+keyframes";
        var rc = CommandRc(SeekTag + (ulong)gen, "seek", target, flags);
        if (rc < 0) Finish(gen, new SeekResult(SeekOutcome.Failed, 0));

        using var registration = ct.Register(() => Finish(gen, new SeekResult(SeekOutcome.Canceled, 0)));
        var timeout = Task.Delay(_options.SeekTimeout);
        var finished = await Task.WhenAny(done.Task, timeout).ConfigureAwait(false);
        if (finished != done.Task) Finish(gen, new SeekResult(SeekOutcome.TimedOut, _options.SeekTimeout.TotalMilliseconds));
        return await done.Task.ConfigureAwait(false);
    }

    public unsafe bool TryCopyLatest(ref long seen, FrameCopy copy)
    {
        lock (_frameGate)
        {
            if (_front is not { } front || _serial == seen) return false;
            copy((IntPtr)front.Pixels, front.Width, front.Height, front.Stride);
            seen = _serial;
            return true;
        }
    }

    public unsafe bool TryCopyLatest(ref long seen, FrameCopy copy, out double frameSeconds)
    {
        lock (_frameGate)
        {
            frameSeconds = double.NaN;
            if (_front is not { } front || _serial == seen) return false;
            copy((IntPtr)front.Pixels, front.Width, front.Height, front.Stride);
            frameSeconds = front.Seconds;
            seen = _serial;
            return true;
        }
    }

    private static double Now => Clock.Elapsed.TotalMilliseconds;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnUpdate(IntPtr state)
    {
        if (GCHandle.FromIntPtr(state).Target is AutoResetEvent signal) signal.Set();
    }

    private void Finish(long gen, SeekResult result)
    {
        TaskCompletionSource<SeekResult>? done = null;
        lock (_seekGate)
        {
            if (gen == _seekGen && _seekDone is not null)
            {
                done = _seekDone;
                _seekDone = null;
            }
        }

        done?.TrySetResult(result);
    }

    private unsafe void EventLoop()
    {
        while (!_stopping)
        {
            var ev = mpv_wait_event(_mpv, 0.25);
            BeforeEvent?.Invoke(ev->EventId);
            switch (ev->EventId)
            {
                case MPV_EVENT_SHUTDOWN:
                    return;
                case MPV_EVENT_LOG_MESSAGE:
                    OnLog((MpvEventLogMessage*)ev->Data);
                    break;
                case MPV_EVENT_COMMAND_REPLY:
                    OnReply(ev->ReplyUserdata, ev->Error);
                    break;
                case MPV_EVENT_FILE_LOADED:
                    OnLoaded();
                    break;
                case MPV_EVENT_END_FILE:
                    OnEnded(*(MpvEventEndFile*)ev->Data);
                    break;
                case MPV_EVENT_VIDEO_RECONFIG:
                    ReadVideoSize();
                    ApplyToneMapping();
                    break;
                case MPV_EVENT_SEEK:
                    OnSeek();
                    break;
                case MPV_EVENT_PLAYBACK_RESTART:
                    OnRestart();
                    break;
                case MPV_EVENT_PROPERTY_CHANGE:
                    OnProperty(ev->ReplyUserdata, (MpvEventProperty*)ev->Data);
                    break;
            }
        }
    }

    private unsafe void OnLog(MpvEventLogMessage* message)
    {
        var line = $"{Marshal.PtrToStringUTF8(message->Prefix)}/{Marshal.PtrToStringUTF8(message->Level)}: {Marshal.PtrToStringUTF8(message->Text)?.TrimEnd()}";
        _log.Enqueue(line);
        while (_log.Count > LogCapacity) _log.TryDequeue(out _);
    }

    private void OnReply(ulong tag, int error)
    {
        if (tag == OpenTag)
        {
            if (error < 0) Volatile.Read(ref _open)?.TrySetException(new PlaybackOpenException(Describe(Error(error))));
            return;
        }

        if (tag == ShotTag)
        {
            Interlocked.Exchange(ref _shot, null)?.TrySetResult(error >= 0);
            return;
        }

        if (tag < SeekTag) return;
        var gen = (long)(tag - SeekTag);
        if (error < 0)
        {
            Finish(gen, new SeekResult(SeekOutcome.Failed, 0));
            return;
        }

        lock (_seekGate)
        {
            if (gen == _seekGen) _replyGen = gen;
        }
    }

    private void OnSeek()
    {
        SilenceSeeked();
        lock (_seekGate)
        {
            if (_seekDone is null || _replyGen != _seekGen || _armedGen == _seekGen) return;
            _armedGen = _seekGen;
        }
    }

    private void OnRestart()
    {
        var position = GetDouble("time-pos");
        if (double.IsFinite(position)) Volatile.Write(ref _position, position);
        Interlocked.Increment(ref _restarts);

        TaskCompletionSource<SeekResult>? done;
        double latency;
        lock (_seekGate)
        {
            if (_seekDone is null || _armedGen != _seekGen || _restartGen == _seekGen) return;
            _restartGen = _seekGen;
            _restartAt = Now;
            done = TakeShown(out latency);
        }

        done?.TrySetResult(new SeekResult(SeekOutcome.Shown, latency));
    }

    private TaskCompletionSource<SeekResult>? TakeShown(out double latency)
    {
        latency = 0;
        if (_seekDone is null || _restartGen != _seekGen || _newFrameInFlight) return null;
        if (_hasVideo && _newFramesShown <= _newFramesAtIssue) return null;
        var done = _seekDone;
        _seekDone = null;
        latency = Math.Max(_newFrameAt, _restartAt) - _issuedAt;
        return done;
    }

    private void OnLoaded()
    {
        var duration = GetDouble("duration");
        Volatile.Write(ref _duration, double.IsFinite(duration) && duration > 0 ? duration : 0);
        var (audio, video) = CountTracks();
        _hasAudio = audio > 0;
        _hasVideo = video > 0;
        ReadVideoSize();
        _isOpen = true;
        Interlocked.Exchange(ref _open, null)?.TrySetResult();
    }

    private unsafe (int Audio, int Video) CountTracks()
    {
        long count;
        if (mpv_get_property(_mpv, "track-list/count", MPV_FORMAT_INT64, &count) < 0) return (0, 0);
        var audio = 0;
        var video = 0;
        for (var i = 0; i < count; i++)
        {
            var type = GetProperty($"track-list/{i}/type");
            if (type == "audio") audio++;
            else if (type == "video" && GetProperty($"track-list/{i}/albumart") != "yes") video++;
        }

        return (audio, video);
    }

    private void OnEnded(MpvEventEndFile end)
    {
        if (end.Reason != MPV_END_FILE_REASON_ERROR) return;
        var reason = Error(end.Error);
        var open = Interlocked.Exchange(ref _open, null);
        if (open is not null)
        {
            open.TrySetException(new PlaybackOpenException(Describe(reason)));
            return;
        }

        _isOpen = false;
        Faulted?.Invoke(this, new PlaybackFault(FailedKey, reason));
    }

    private unsafe void OnProperty(ulong id, MpvEventProperty* property)
    {
        if (property->Data == IntPtr.Zero) return;
        switch (id)
        {
            case EofId when property->Format == MPV_FORMAT_FLAG:
                _eof = *(int*)property->Data != 0;
                break;
            case PlaylistPosId when property->Format == MPV_FORMAT_INT64:
                OnPlaylistPos(*(long*)property->Data);
                break;
            case PauseId when property->Format == MPV_FORMAT_FLAG:
                _paused = *(int*)property->Data != 0;
                break;
            case TimeId when property->Format == MPV_FORMAT_DOUBLE:
                OnTimePosChanged(*(double*)property->Data);
                WatchSilence();
                break;
        }
    }

    internal void OnTimePosChanged(double reported)
    {
        var current = GetDouble("time-pos");
        Volatile.Write(ref _position, double.IsFinite(current) ? current : reported);
    }

    private unsafe void ReadVideoSize()
    {
        long w, h;
        if (mpv_get_property(_mpv, "dwidth", MPV_FORMAT_INT64, &w) < 0) return;
        if (mpv_get_property(_mpv, "dheight", MPV_FORMAT_INT64, &h) < 0) return;
        if (w <= 0 || h <= 0) return;
        Volatile.Write(ref _videoWidth, (int)w);
        Volatile.Write(ref _videoHeight, (int)h);
        _update.Set();
    }

    private void RenderLoop()
    {
        var due = false;
        while (!_stopping)
        {
            _update.WaitOne(100);
            if (_stopping) break;
            if ((mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) != 0) due = true;
            if (!due) continue;
            if (Volatile.Read(ref _videoWidth) <= 0 || Volatile.Read(ref _videoHeight) <= 0) continue;
            due = false;
            RenderFrame();
        }
    }

    private unsafe void RenderFrame()
    {
        var width = Volatile.Read(ref _videoWidth);
        var height = Volatile.Read(ref _videoHeight);
        if (width <= 0 || height <= 0) return;
        if (_options.RenderWidth > 0 && _options.RenderHeight > 0)
        {
            width = _options.RenderWidth;
            height = _options.RenderHeight;
        }

        var back = _back;
        if (back is null || back.Width != width || back.Height != height)
        {
            back?.Dispose();
            back = new FrameBuffer(width, height);
            _back = back;
        }

        MpvRenderFrameInfo info = default;
        var infoRc = mpv_render_context_get_info(_render, new MpvRenderParam(MPV_RENDER_PARAM_NEXT_FRAME_INFO, (IntPtr)(&info)));
        var isNew = infoRc < 0
            || ((info.Flags & MPV_RENDER_FRAME_INFO_PRESENT) != 0 && (info.Flags & MPV_RENDER_FRAME_INFO_REDRAW) == 0);

        var size = stackalloc int[2];
        size[0] = width;
        size[1] = height;
        var stride = (nuint)back.Stride;
        var block = 1;
        var parameters = stackalloc MpvRenderParam[6];
        parameters[0] = new MpvRenderParam(MPV_RENDER_PARAM_SW_SIZE, (IntPtr)size);
        parameters[1] = new MpvRenderParam(MPV_RENDER_PARAM_SW_FORMAT, BgraFormat);
        parameters[2] = new MpvRenderParam(MPV_RENDER_PARAM_SW_STRIDE, (IntPtr)(&stride));
        parameters[3] = new MpvRenderParam(MPV_RENDER_PARAM_SW_POINTER, (IntPtr)back.Pixels);
        parameters[4] = new MpvRenderParam(MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, (IntPtr)(&block));
        parameters[5] = new MpvRenderParam(MPV_RENDER_PARAM_INVALID, IntPtr.Zero);

        if (isNew)
        {
            lock (_seekGate) _newFrameInFlight = true;
        }

        var rendered = mpv_render_context_render(_render, parameters) >= 0;
        var end = Now;

        if (rendered)
        {
            back.Seconds = Volatile.Read(ref _position);
            lock (_frameGate)
            {
                _back = _front;
                _front = back;
                _serial++;
            }
        }

        if (!isNew) return;
        if (rendered) Interlocked.Increment(ref _framesRendered);

        TaskCompletionSource<SeekResult>? done;
        double latency;
        lock (_seekGate)
        {
            _newFrameInFlight = false;
            if (rendered)
            {
                _newFramesShown++;
                _newFrameAt = end;
            }

            done = TakeShown(out latency);
        }

        done?.TrySetResult(new SeekResult(SeekOutcome.Shown, latency));
    }

    private unsafe double GetDouble(string name)
    {
        lock (_handleGate)
        {
            if (!HandleAlive) return double.NaN;
            double value;
            return mpv_get_property(_mpv, name, MPV_FORMAT_DOUBLE, &value) < 0 ? double.NaN : value;
        }
    }

    private void Command(ulong tag, params string[] args)
        => Check(CommandRc(tag, args), "command " + args[0]);

    private unsafe int CommandRc(ulong tag, params string[] args)
    {
        var pointers = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            lock (_handleGate)
            {
                if (!HandleAlive) return -1;
                fixed (IntPtr* p = pointers) return mpv_command_async(_mpv, tag, p);
            }
        }
        finally
        {
            foreach (var pointer in pointers)
                if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private string Describe(string reason)
    {
        var log = _log.Where(IsProblem).ToArray();
        return log.Length == 0 ? reason : reason + " | " + string.Join(" | ", log.TakeLast(4));
    }

    private static bool IsProblem(string line)
        => line.Contains("/warn:", StringComparison.Ordinal)
           || line.Contains("/error:", StringComparison.Ordinal)
           || line.Contains("/fatal:", StringComparison.Ordinal);

    private const int OptionNotFound = -5;

    /// <summary>
    /// Yalniz derlemede bulunmayan ozelligi kapatan secenekler. <c>ytdl</c> Lua betigi
    /// (ytdl_hook) ile gelir; Lua'siz libmpv'de secenek yok ve "option not found" motoru hic
    /// actirmiyordu (kaynaktan derlenen 0.40'ta olculdu). <c>media-controls</c> eski libmpv'de
    /// yok; orada sistem ortam denetimi de yok. Secenek yoksa kapattigi ozellik de yok.
    /// </summary>
    internal static readonly IReadOnlySet<string> OptionalOptions = new HashSet<string>(StringComparer.Ordinal) { "ytdl", "media-controls" };

    internal static bool Absent(string name, int rc) => rc == OptionNotFound && OptionalOptions.Contains(name);

    private static void Check(int rc, string what)
    {
        if (rc < 0) throw new PlaybackEngineUnavailableException($"libmpv {what} failed: {Error(rc)}");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stopping = true;
        if (_mpv != IntPtr.Zero) mpv_wakeup(_mpv);
        _update.Set();
        _events?.Join();
        _renderer?.Join();

        Interlocked.Exchange(ref _open, null)?.TrySetException(new ObjectDisposedException(nameof(MpvEngine)));
        Interlocked.Exchange(ref _shot, null)?.TrySetResult(false);
        TaskCompletionSource<SeekResult>? pending;
        lock (_seekGate)
        {
            pending = _seekDone;
            _seekDone = null;
        }

        pending?.TrySetResult(new SeekResult(SeekOutcome.Canceled, 0));
        _isOpen = false;
        if (_options.MediaControls) CloseMediaSession();
        if (_options.MediaControls && MediaControlsSettle - (Clock.Elapsed - _createdAt) is { Ticks: > 0 } settle) Thread.Sleep(settle);
        Release();
    }

    private unsafe void CloseMediaSession()
    {
        var pointers = new[] { Marshal.StringToCoTaskMemUTF8("stop"), IntPtr.Zero };
        try
        {
            lock (_handleGate)
            {
                if (_mpv == IntPtr.Zero) return;
                fixed (IntPtr* p = pointers) mpv_command(_mpv, p);
                var waited = Stopwatch.StartNew();
                while (waited.Elapsed < MediaControlsSettle && !IdleActive()) Thread.Sleep(1);
                Thread.Sleep(MediaControlsSettle);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointers[0]);
        }
    }

    private unsafe bool IdleActive()
    {
        int idle;
        return mpv_get_property(_mpv, "idle-active", MPV_FORMAT_FLAG, &idle) >= 0 && idle != 0;
    }

    private void Release()
    {
        lock (_handleGate)
        {
            if (_render != IntPtr.Zero)
            {
                mpv_render_context_free(_render);
                _render = IntPtr.Zero;
            }

            if (_mpv != IntPtr.Zero)
            {
                mpv_terminate_destroy(_mpv);
                _mpv = IntPtr.Zero;
            }
        }

        if (_updateHandle.IsAllocated) _updateHandle.Free();

        lock (_frameGate)
        {
            _front?.Dispose();
            _back?.Dispose();
            _front = null;
            _back = null;
        }

        _update.Dispose();
    }

    private sealed unsafe class FrameBuffer : IDisposable
    {
        public FrameBuffer(int width, int height)
        {
            Width = width;
            Height = height;
            Stride = 4 * width;
            Pixels = (byte*)NativeMemory.AlignedAlloc((nuint)Stride * (nuint)height, 64);
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public byte* Pixels { get; private set; }

        public double Seconds { get; set; }

        public void Dispose()
        {
            if (Pixels == null) return;
            NativeMemory.AlignedFree(Pixels);
            Pixels = null;
        }
    }
}
