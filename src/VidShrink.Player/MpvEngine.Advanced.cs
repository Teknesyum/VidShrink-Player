using System.Globalization;
using System.Text.RegularExpressions;

namespace VidShrink.Player;

public sealed partial class MpvEngine
{
    private const string ColorLabel = "vscolor";
    private const string HueLabel = "vshue";
    private const string SharpLabel = "vssharp";
    private const string CropLabel = "vscrop";
    private const string EqualizerLabel = "vseq";
    private const string NormalizeLabel = "vsnorm";
    private const string ClipGeometryLabel = "vsclipgeo";
    private const string ClipFadeLabel = "vsclipfade";
    private const string ClipAudioLabel = "vsclipaudio";

    private string? _editGeometry;
    private string? _editFades;
    private string? _editAudio;

    private const string MeterLabel = "vsmeter";
    private const string MeterFilter = "lavfi=[astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=Peak_level]";
    private volatile bool _metering;

    private const string ToneLabel = "vstone";
    private const string ToneFilter = "gpu";
    private readonly object _toneGate = new();
    private volatile bool _toneMapping = true;
    private bool _toneAttached;

    public bool ToneMapping => _toneMapping;

    internal bool ToneRefused { get; private set; }

    public bool ToneMappingActive
        => (GetProperty("vf") ?? "").Contains("@" + ToneLabel + ":", StringComparison.Ordinal)
           && !IsHdrTransfer(GetProperty("video-out-params/gamma"));

    public void SetToneMapping(bool on)
    {
        _toneMapping = on;
        ApplyToneMapping();
    }

    internal static bool IsHdrTransfer(string? transfer)
        => transfer?.Trim().ToLowerInvariant() is "pq" or "hlg";

    internal static bool NeedsToneMapping(bool wanted, string? transfer) => wanted && IsHdrTransfer(transfer);

    internal static readonly IReadOnlyList<(string Name, string Value)> ToneTarget = new[]
    {
        ("target-prim", "bt.709"),
        ("target-trc", "bt.1886")
    };

    private void ApplyToneMapping()
    {
        lock (_toneGate)
        {
            var wanted = NeedsToneMapping(_toneMapping, GetProperty("video-params/gamma"));
            if (wanted == _toneAttached) return;
            _toneAttached = wanted;
            CommandSync("vf", "remove", "@" + ToneLabel);
            ToneRefused = false;
            if (!wanted) return;
            if (!VulkanProbe.HasDevice)
            {
                ToneRefused = true;
                return;
            }

            foreach (var (name, value) in ToneTarget) TrySet(name, value);
            ToneRefused = CommandSync("vf", "pre", "@" + ToneLabel + ":" + ToneFilter) < 0;
        }
    }

    private const double HueSpan = 1.8;
    private const double SharpSpan = 0.015;

    /// <summary>
    /// Renk, keskinlik ve kirpma suzgec zincirinde durur: <c>vo=libmpv</c> yazilim render
    /// yolunda <c>brightness</c>/<c>saturation</c> gibi gpu-only ozellikler kareye dokunmaz.
    /// Her ayar etiketli bir libavfilter halkasidir, geri okuma <c>vf</c> dizgesinden yapilir.
    /// </summary>
    public PictureAdjust Picture
    {
        get
        {
            var chain = GetProperty("vf") ?? "";
            var color = Segment(chain, ColorLabel);
            var hue = Segment(chain, HueLabel);
            var sharp = Segment(chain, SharpLabel);
            var crop = Segment(chain, CropLabel);
            return new PictureAdjust(
                Percent(Field(color, "brightness", 0)),
                Percent(Field(color, "contrast", 1) - 1),
                Percent(Field(color, "saturation", 1) - 1),
                Percent(Field(color, "gamma", 1) - 1),
                Scaled(Field(hue, "h", 0), HueSpan),
                Scaled(Field(sharp, "luma_amount", 0), SharpSpan),
                GetProperty("deinterlace") is "yes",
                Percent(Factor(crop, "x")));
        }
    }

    /// <summary>
    /// Ekolayzer on <c>equalizer</c> halkasi, normallestirme <c>dynaudnorm</c>; ikisi de
    /// etiketli <c>af</c> halkasi. Kazanclar zincirdeki <c>g=</c> alanlarindan sirayla okunur.
    /// </summary>
    public SoundAdjust Sound
    {
        get
        {
            var chain = GetProperty("af") ?? "";
            return new SoundAdjust(
                Gains(Segment(chain, EqualizerLabel)),
                Segment(chain, NormalizeLabel) is not null,
                VolumeCeiling >= SoundAdjust.BoostCeiling);
        }
    }

    public SubtitleStyle SubtitleLook => new(
        GetProperty("sub-font"),
        GetProperty("sub-color"),
        Reading(BorderName),
        Reading("sub-shadow-offset"),
        GetProperty("sub-back-color"),
        GetProperty("sub-ass-override") == "force",
        GetProperty("sub-filter-sdh") == "yes",
        GetProperty("sub-bold") == "yes",
        GetProperty("sub-italic") == "yes");

    public double VolumeCeiling => Finite(GetDouble("volume-max"), SoundAdjust.PlainCeiling);

    public void SetPicture(PictureAdjust picture)
    {
        var wanted = picture.Clamped();
        Swap(ColorLabel, wanted.Brightness == 0 && wanted.Contrast == 0 && wanted.Saturation == 0 && wanted.Gamma == 0
            ? null
            : "eq=brightness=" + F(wanted.Brightness / 100.0)
                + ":contrast=" + F(1 + wanted.Contrast / 100.0)
                + ":saturation=" + F(1 + wanted.Saturation / 100.0)
                + ":gamma=" + F(1 + wanted.Gamma / 100.0));

        Swap(HueLabel, wanted.Hue == 0 ? null : "hue=h=" + F(wanted.Hue * HueSpan));
        Swap(SharpLabel, wanted.Sharpness == 0
            ? null
            : "unsharp=luma_msize_x=5:luma_msize_y=5:luma_amount=" + F(wanted.Sharpness * SharpSpan));

        var keep = 1 - 2 * wanted.Crop / 100.0;
        var edge = wanted.Crop / 100.0;
        Swap(CropLabel, wanted.Crop == 0
            ? null
            : "crop=w=iw*" + F(keep) + ":h=ih*" + F(keep) + ":x=iw*" + F(edge) + ":y=ih*" + F(edge));

        TrySet("deinterlace", wanted.Deinterlace ? "yes" : "auto");
    }

    public void SetSound(SoundAdjust sound)
    {
        if (!sound.Valid) return;
        var wanted = sound.Clamped();
        Swap(EqualizerLabel, wanted.Bands.All(gain => gain == 0)
            ? null
            : "lavfi=[" + string.Join(",", wanted.Bands.Select((gain, index) =>
                "equalizer=f=" + SoundAdjust.BandHertz[index].ToString(CultureInfo.InvariantCulture)
                    + ":t=q:w=1.4:g=" + F(gain))) + "]", "af");

        Swap(NormalizeLabel, wanted.Normalize ? "lavfi=[dynaudnorm=f=150:g=15:p=0.9]" : null, "af");
        KeepMeterLast();
        TrySet("volume-max", F(wanted.Boost ? SoundAdjust.BoostCeiling : SoundAdjust.PlainCeiling));
        if (!wanted.Boost && Volume > SoundAdjust.PlainCeiling) SetVolume(SoundAdjust.PlainCeiling);
    }

    public void SetSubtitleStyle(SubtitleStyle style)
    {
        Write("sub-font", style.Font);
        Write("sub-color", style.Color);
        Write(BorderName, style.Outline is { } outline ? F(outline) : null);
        Write("sub-shadow-offset", style.Shadow is { } shadow ? F(shadow) : null);
        Write("sub-back-color", style.Background);
        Write("sub-ass-override", style.OverrideAss ? "force" : null);
        Write("sub-filter-sdh", style.HideSdh ? "yes" : null);
        Write("sub-bold", style.Bold ? "yes" : null);
        Write("sub-italic", style.Italic ? "yes" : null);
    }

    /// <summary>
    /// mpv 0.39 <c>sub-border-size</c>'i <c>sub-outline-size</c> yapti; ad calisma aninda
    /// secilir, yeni ad yoksa eskisi kullanilir.
    /// </summary>
    private string BorderName => GetProperty("sub-outline-size") is not null ? "sub-outline-size" : "sub-border-size";

    private double? Reading(string name)
    {
        var value = GetDouble(name);
        return double.IsFinite(value) ? value : null;
    }

    /// <summary>Deger <c>null</c> ise ozellik <c>option-info</c>'daki varsayilanina doner.</summary>
    private void Write(string name, string? value)
    {
        if (value is not null)
        {
            TrySet(name, value);
            return;
        }

        if (GetProperty("option-info/" + name + "/default-value") is { } fallback) TrySet(name, fallback);
    }

    public void SetEditGeometry(string? chain)
    {
        var wanted = string.IsNullOrEmpty(chain) ? null : chain;
        if (wanted == _editGeometry) return;
        _editGeometry = wanted;
        Swap(ClipGeometryLabel, wanted is null ? null : "lavfi=[" + wanted + "]");
        _update.Set();
    }

    public void SetEditTimed(string? video, string? audio)
    {
        var wantedVideo = string.IsNullOrEmpty(video) ? null : video;
        var wantedAudio = string.IsNullOrEmpty(audio) ? null : audio;
        if (wantedVideo != _editFades)
        {
            _editFades = wantedVideo;
            Swap(ClipFadeLabel, wantedVideo is null ? null : Lavfi(wantedVideo));
            _update.Set();
        }

        if (wantedAudio == _editAudio) return;
        _editAudio = wantedAudio;
        Swap(ClipAudioLabel, wantedAudio is null ? null : "lavfi=[" + wantedAudio + "]", "af");
        KeepMeterLast();
    }

    public bool SetEditAudioVolume(double db)
    {
        if (_editAudio is null || !double.IsFinite(db)) return false;
        return CommandSync("af-command", ClipAudioLabel, "volume", db.ToString("0.0#", CultureInfo.InvariantCulture) + "dB", "volume") >= 0;
    }

    public bool SetPlayDirection(bool backward) => TrySet("play-direction", backward ? "backward" : "forward");

    /// <summary>
    /// Ses zincirinin sonuna <c>astats</c> halkasi koyar; tepe duzeyi her ses cercevesinde
    /// <c>af-metadata/vsmeter</c> altina yazilir, <see cref="AudioPeakDb"/> oradan okur.
    /// </summary>
    public bool MeterAudio(bool on)
    {
        _metering = on;
        Swap(MeterLabel, on ? MeterFilter : null, "af");
        return !on || (GetProperty("af") ?? "").Contains("@" + MeterLabel, StringComparison.Ordinal);
    }

    public double AudioPeakDb
    {
        get
        {
            if (!_metering) return double.NaN;
            var raw = GetProperty("af-metadata/" + MeterLabel + "/lavfi.astats.Overall.Peak_level");
            if (raw is null) return double.NaN;
            if (raw.Contains("inf", StringComparison.OrdinalIgnoreCase)) return raw.StartsWith('-') ? double.NegativeInfinity : double.NaN;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) ? db : double.NaN;
        }
    }

    private void KeepMeterLast()
    {
        if (_metering) Swap(MeterLabel, MeterFilter, "af");
    }

    private void Swap(string label, string? filter, string property = "vf")
    {
        CommandSync(property, "remove", "@" + label);
        if (filter is not null) CommandSync(property, "add", "@" + label + ":" + filter);
    }

    internal static string Lavfi(string graph)
        => graph.Contains(']', StringComparison.Ordinal)
            ? "lavfi=%" + System.Text.Encoding.UTF8.GetByteCount(graph).ToString(CultureInfo.InvariantCulture) + "%" + graph
            : "lavfi=[" + graph + "]";

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);


    private static int Percent(double value) => (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);

    private static int Scaled(double value, double span) => (int)Math.Round(value / span, MidpointRounding.AwayFromZero);

    private static string? Segment(string chain, string label)
    {
        var mark = "@" + label;
        var at = chain.IndexOf(mark, StringComparison.Ordinal);
        if (at < 0) return null;
        var rest = chain[(at + mark.Length)..];
        var end = rest.IndexOf(",@", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    private static double Field(string? segment, string name, double fallback)
    {
        if (segment is null) return fallback;
        var match = Regex.Match(segment, "(?<![\\w-])" + Regex.Escape(name) + "=" + Escape + "(-?[0-9]+(?:\\.[0-9]+)?)");
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    /// <summary>
    /// mpv suzgec dizgesini geri verirken degerleri <c>%uzunluk%</c> onekiyle kacirir
    /// (<c>brightness=%3%0.1</c>); okuma bu oneki istege bagli gecer.
    /// </summary>
    private const string Escape = "(?:%[0-9]+%)?";

    private static double Factor(string? segment, string name)
    {
        if (segment is null) return 0;
        var match = Regex.Match(segment, "(?<![\\w-])" + Regex.Escape(name) + "=" + Escape + "i[wh]\\*(-?[0-9]+(?:\\.[0-9]+)?)");
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static IReadOnlyList<int> Gains(string? segment)
    {
        var bands = new int[SoundAdjust.BandCount];
        if (segment is null) return bands;
        var matches = Regex.Matches(segment, "(?<![\\w-])g=(-?[0-9]+(?:\\.[0-9]+)?)");
        for (var index = 0; index < bands.Length && index < matches.Count; index++)
            if (double.TryParse(matches[index].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var gain))
                bands[index] = (int)Math.Round(gain, MidpointRounding.AwayFromZero);

        return bands;
    }
}
