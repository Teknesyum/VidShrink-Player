namespace VidShrink.Player;

public enum SeekPrecision
{
    Exact,
    Keyframe
}

public enum SeekOutcome
{
    Shown,
    Failed,
    TimedOut,
    Superseded,
    Canceled
}

public enum HardwareDecoding
{
    Off,
    AutoCopy
}

public enum PlaybackTrackKind
{
    Video,
    Audio,
    Subtitle
}

public sealed record PlaybackTrack(long Id, PlaybackTrackKind Kind, string? Title, string? Language, bool External, bool Selected, bool Forced = false);

public sealed record MediaTags(string? Title, string? Artist, string? Album)
{
    public static readonly MediaTags Empty = new(null, null, null);

    public bool IsEmpty => Title is null && Artist is null && Album is null;
}

public readonly record struct SeekResult(SeekOutcome Outcome, double LatencyMs);

public sealed record PlaybackFault(string MessageKey, string? MessageArg = null);

public sealed record PlaybackOptions
{
    public HardwareDecoding Hardware { get; init; } = HardwareDecoding.Off;

    public TimeSpan OpenTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan SeekTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public int RenderWidth { get; init; }

    public int RenderHeight { get; init; }

    public bool Audio { get; init; } = true;

    public bool Video { get; init; } = true;

    public bool Loop { get; init; }

    public bool MediaControls { get; init; }
}

public delegate void FrameCopy(IntPtr pixels, int width, int height, int stride);

public interface IPlaybackEngine : IDisposable
{
    string Name { get; }

    bool IsOpen { get; }

    double DurationSeconds { get; }

    bool HasAudio { get; }

    bool HasVideo => true;

    MediaTags Tags => MediaTags.Empty;

    /// <summary>Motorun yayın adı (etiket ya da liste girdisinin başlığı); yalnız dosya adına düşüyorsa <c>null</c>.</summary>
    string? MediaTitle => null;

    bool IsPaused { get; }

    bool EndReached { get; }

    double PositionSeconds { get; }

    double AudioVideoOffsetSeconds { get; }

    long FramesRendered { get; }

    event EventHandler<PlaybackFault>? Faulted;

    Task OpenAsync(string path, CancellationToken ct = default);

    void Play();

    void Pause();

    Task<SeekResult> SeekAsync(double seconds, SeekPrecision precision, CancellationToken ct = default);

    bool TryCopyLatest(ref long seen, FrameCopy copy);

    bool TryCopyLatest(ref long seen, FrameCopy copy, out double frameSeconds)
    {
        frameSeconds = PositionSeconds;
        return TryCopyLatest(ref seen, copy);
    }

    double Speed => 1;

    double Volume => 100;

    bool Muted => false;

    double FramesPerSecond => double.NaN;

    double LoopStartSeconds => double.NaN;

    double LoopEndSeconds => double.NaN;

    void SetSpeed(double speed) { }

    void SetVolume(double volume) { }

    void SetMuted(bool muted) { }

    void StepFrame(bool backward) { }

    void SetLoop(double startSeconds, double endSeconds) { }

    IReadOnlyList<PlaybackTrack> Tracks => Array.Empty<PlaybackTrack>();

    long AudioTrack => 0;

    long SubtitleTrack => 0;

    /// <summary>Birincille ayni anda cizilen ikinci altyazi izi (<c>secondary-sid</c>); kapaliyken 0.</summary>
    long SecondarySubtitleTrack => 0;

    double SubtitleDelaySeconds => 0;

    double AudioDelaySeconds => 0;

    double SubtitleScale => 1;

    double SubtitlePosition => 100;

    string SubtitleCodepage => "auto";

    void SetAudioTrack(long id) { }

    void SetSubtitleTrack(long id) { }

    void SetSecondarySubtitleTrack(long id) { }

    bool AddSubtitle(string path) => false;

    /// <summary>
    /// Duzenleyicinin metin katmanini (<c>.ass</c>) dis altyazi izi olarak ekler ve secer;
    /// izin kimligini dondurur, eklenemezse 0.
    /// </summary>
    long AddOverlay(string path) => 0;

    /// <summary>Izi dosyadan yeniden okur (<c>sub-reload</c>); dosya yerinde yeniden yazildiktan sonra.</summary>
    bool ReloadOverlay(long id) => false;

    bool RemoveOverlay(long id) => false;

    /// <summary><c>sub-fonts-dir</c>; <c>null</c> varsayilana dondurur. Iz eklenmeden once verilir.</summary>
    void SetSubtitleFontsDir(string? directory) { }

    void SetSubtitleDelay(double seconds) { }

    void SetAudioDelay(double seconds) { }

    void SetSubtitleScale(double scale) { }

    void SetSubtitlePosition(double percent) { }

    void SetSubtitleCodepage(string codepage) { }

    /// <summary>Altyazinin gorunurlugu (<c>sub-visibility</c>); izi degistirmez.</summary>
    bool SubtitleVisible => true;

    void SetSubtitleVisible(bool visible) { }

    /// <summary>Sonraki ya da onceki altyazi satirinin basina atlar (<c>sub-seek</c>).</summary>
    bool SeekSubtitle(bool backward) => false;

    /// <summary>Gecikmeyi sonraki ya da onceki satir su anki konuma gelecek kadar kaydirir (<c>sub-step</c>).</summary>
    bool StepSubtitle(bool backward) => false;

    /// <summary>Ekrandaki altyazi satirinin metni (<c>sub-text</c>); satir yoksa <c>null</c>.</summary>
    string? SubtitleText => null;

    int Rotation => 0;

    bool Mirrored => false;

    double AspectOverride => -1;

    bool RepeatFile => false;

    MediaDetails? Details => null;

    /// <summary>Dusen kare, etkin cozucu, anlik bit hizi ve gosterilen kare hizi; motor olcmuyorsa <c>null</c>.</summary>
    PlaybackStats? Stats => null;

    IReadOnlyList<double> ChapterTimes => Array.Empty<double>();

    /// <summary><see cref="ChapterTimes"/> ile ayni sirada bolum adlari; adsiz bolum bos metindir.</summary>
    IReadOnlyList<string> ChapterTitles => Array.Empty<string>();

    /// <summary>Bir sonraki ya da onceki bolume atlar; bolum yoksa ya da son bolumden ileri gidilemiyorsa <c>false</c>.</summary>
    bool StepChapter(bool backward) => false;

    void SetRotation(int degrees) { }

    /// <summary>
    /// Duzenleyicinin o anki parcaya uyguladigi geometri zinciri (kirp, dondur, cevir, tuval);
    /// libavfilter dizgesi, <c>null</c> ya da bos kaldirir.
    /// </summary>
    void SetEditGeometry(string? chain) { }

    /// <summary>Duzenleyicinin EDL zamaninda <c>enable</c> pencereli solma ve ses zincirleri; <c>null</c> kaldirir.</summary>
    void SetEditTimed(string? video, string? audio) { }

    /// <summary>
    /// <see cref="SetEditTimed"/> ile konan ses zincirindeki <c>volume</c> suzgecinin duzeyini zinciri yeniden kurmadan
    /// degistirir (dB). Zincir yoksa ya da motor komutu tasimiyorsa <c>false</c>.
    /// </summary>
    bool SetEditAudioVolume(double db) => false;

    /// <summary>Oynatma yonunu degistirir; geri yonde ses de geri calar. Motor desteklemiyorsa <c>false</c>.</summary>
    bool SetPlayDirection(bool backward) => false;

    /// <summary>Sessiz bolumleri hizlandirma kipini acar, kapatir ya da esigini degistirir.</summary>
    void SetSilenceSkip(SilenceSkip skip) { }

    /// <summary>Ses tepe olcerini acar ya da kapatir; motor olcemiyorsa <c>false</c>.</summary>
    bool MeterAudio(bool on) => false;

    /// <summary>Son ses penceresinin tepe duzeyi (dBFS); sessizlikte eksi sonsuz, olcer kapaliyken <c>NaN</c>.</summary>
    double AudioPeakDb => double.NaN;

    void SetMirrored(bool mirrored) { }

    void SetAspectOverride(string ratio) { }

    void SetRepeatFile(bool repeat) { }

    void SetNeighbors(string? previous, string? next) { }

    event EventHandler<bool>? NeighborRequested { add { } remove { } }

    HardwareDecoding Hardware => HardwareDecoding.Off;

    /// <summary>Donanim cozmeyi calan dosyada da degistirir; motor desteklemiyorsa yok sayilir.</summary>
    void SetHardwareDecoding(HardwareDecoding mode) { }

    bool ToneMapping => false;

    bool ToneMappingActive => false;

    void SetToneMapping(bool on) { }

    Task<bool> SaveScreenshotAsync(string path, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>Kareyi altyaziyla (<paramref name="withSubtitles"/>) ya da yalniz goruntuyle kaydeder.</summary>
    Task<bool> SaveScreenshotAsync(string path, bool withSubtitles, CancellationToken ct = default) => SaveScreenshotAsync(path, ct);

    PictureAdjust Picture => PictureAdjust.Neutral;

    SoundAdjust Sound => SoundAdjust.Neutral;

    SubtitleStyle SubtitleLook => SubtitleStyle.Inherited;

    double VolumeCeiling => SoundAdjust.PlainCeiling;

    void SetPicture(PictureAdjust picture) { }

    void SetSound(SoundAdjust sound) { }

    void SetSubtitleStyle(SubtitleStyle style) { }

    IReadOnlyList<string> RecentLog => Array.Empty<string>();

    bool ReloadAudio() => false;

    Task<bool> ReopenAsync(double atSeconds, bool playing, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>Uyarlamali yayinda calan katmanin bant genisligi (izin <c>hls-bitrate</c>'i); katman yoksa 0.</summary>
    long StreamBitrate => 0;

    /// <summary>
    /// Katmani bant genisligiyle secer (<c>hls-bitrate</c>; 0 en yuksek) ve yayini ayni konumdan yeniden acar.
    /// Iz kimlikleri katmana bagli oldugu icin eski ses ve altyazi kimligi geri yazilmaz.
    /// </summary>
    Task<bool> SetStreamBitrateAsync(long bitrate, CancellationToken ct = default) => Task.FromResult(false);

    /// <summary>Motorun gordugu ses cikis aygitlari; ilk satir otomatik secimdir.</summary>
    IReadOnlyList<AudioDevice> AudioDevices => Array.Empty<AudioDevice>();

    string CurrentAudioDevice => AudioDevice.Auto;

    /// <summary>Ses cikis aygitini secer; motor desteklemiyorsa ya da aygit reddedilirse false.</summary>
    bool SetAudioDevice(string name) => false;

    AudioOutput Output => AudioOutput.Plain;

    /// <summary>Passthrough, ozel kip ve stereoya indirme seceneklerini calan dosyada da degistirir.</summary>
    void SetAudioOutput(AudioOutput output) { }
}

public sealed record AudioDevice(string Name, string Description)
{
    public const string Auto = "auto";
}

public sealed record AudioOutput(bool Passthrough = false, bool Exclusive = false, bool Stereo = false)
{
    public static AudioOutput Plain { get; } = new();

    public bool IsPlain => Equals(Plain);
}

public sealed class PlaybackOpenException : Exception
{
    public PlaybackOpenException(string message) : base(message) { }
}

public sealed class PlaybackEngineUnavailableException : Exception
{
    public PlaybackEngineUnavailableException(string message, Exception? inner = null) : base(message, inner) { }

    public PlaybackEngineUnavailableException(string message, string messageKey, Exception? inner = null) : base(message, inner)
        => MessageKey = messageKey;

    /// <summary>
    /// Kullanıcıya gösterilecek iletinin anahtarı. Boşsa ileti motorun genel
    /// "kullanılamıyor" anahtarıyla yazılır.
    /// </summary>
    public string? MessageKey { get; }
}
