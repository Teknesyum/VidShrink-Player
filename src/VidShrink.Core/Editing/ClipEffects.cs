namespace VidShrink.Core.Editing;

/// <summary>
/// Klibin kaynak ustune uygulanan ayarlari: kaynak koordinatinda kesirli kirpma, saat yonunde
/// 90 adimli dondurme, yatay ve dikey cevirme, desibel ses kazanci, sessize alma ve cizelge
/// tick'inde giris/cikis solmasi, parlaklik, kontrast, doygunluk ve renk sicakligi; ses temizleme (gurultu azaltma,
/// uc bantli ekolayzir, kompresor, yukseklik esitleme, sinirlayici). Sirasi sabittir:
/// kirp, dondur, cevir, sigdir, renk. Hazir gorunumler renk alanlarinin kayitli degerleridir. En-boy sigdirma
/// (<see cref="FitNum"/>:<see cref="FitDen"/>) kirpmayi dislar: kare olcekle kuculur, kalan alan
/// <see cref="PadColor"/> duz rengiyle dolar; renk veri olarak "RRGGBB" tutulur.
/// </summary>
public sealed partial record ClipEffects
{
    public const double MinVolumeDb = -60;
    public const double MaxVolumeDb = 20;
    public const int NeutralTemperature = 6500;
    public const int MinTemperature = 1000;
    public const int MaxTemperature = 40000;
    public const double MinEqDb = -12;
    public const double MaxEqDb = 12;
    public const string DefaultPadColor = "000000";
    public const double MaxFitRatio = 4;
    public const double MaxBlurSigma = 50;
    public const double DefaultBlurSigma = 12;
    public const double MinBlurSide = 0.02;
    public const double MinScale = 0.1;
    public const double MaxScale = 4;
    public const double MaxOffset = 0.5;
    public const double MaxAngle = 180;
    public const int MinLutMix = 0;
    public const int MaxLutMix = 100;

    public static ClipEffects None { get; } = new();

    public static ClipEffects BlackWhite { get; } = new() { Saturation = 0 };

    public static ClipEffects OldFilm { get; } = new() { Brightness = -0.05, Contrast = 1.1, Saturation = 0.35, Temperature = 4000 };

    public double CropLeft { get; init; }

    public double CropTop { get; init; }

    public double CropRight { get; init; }

    public double CropBottom { get; init; }

    public int Rotation { get; init; }

    public bool FlipH { get; init; }

    public bool FlipV { get; init; }

    public double VolumeDb { get; init; }

    public bool Muted { get; init; }

    public long FadeIn { get; init; }

    public long FadeOut { get; init; }

    public double Brightness { get; init; }

    public double Contrast { get; init; } = 1;

    public double Saturation { get; init; } = 1;

    public int Temperature { get; init; } = NeutralTemperature;

    public bool Denoise { get; init; }

    public bool Loudnorm { get; init; }

    public bool Compressor { get; init; }

    public bool Limiter { get; init; }

    public double BassDb { get; init; }

    public double MidDb { get; init; }

    public double TrebleDb { get; init; }

    public int FitNum { get; init; }

    public int FitDen { get; init; }

    public string PadColor { get; init; } = DefaultPadColor;

    public double BlurSigma { get; init; }

    public double BlurX { get; init; }

    public double BlurY { get; init; }

    public double BlurWidth { get; init; }

    public double BlurHeight { get; init; }

    public bool HasBlur => BlurSigma > 0;

    public bool HasBlurRegion => HasBlur && BlurWidth > 0 && BlurHeight > 0;

    public double Scale { get; init; } = 1;

    public double OffsetX { get; init; }

    public double OffsetY { get; init; }

    public double Angle { get; init; }

    public bool HasTransform => Scale != 1 || OffsetX != 0 || OffsetY != 0 || Angle != 0 || TransformKeys.Count > 0;

    public KeyframeTrack<VolumeKey> VolumeKeys { get; init; } = KeyframeTrack<VolumeKey>.Empty;

    public KeyframeTrack<CropKey> CropKeys { get; init; } = KeyframeTrack<CropKey>.Empty;

    public bool HasKeyframes => VolumeKeys.Count > 0 || CropKeys.Count > 0 || TransformKeys.Count > 0;

    public bool HasMovingCrop => CropKeys.Count > 1;

    public string? Lut { get; init; }

    public int LutMix { get; init; } = MaxLutMix;

    public bool HasLut => Lut is { Length: > 0 } && LutMix > MinLutMix;

    public bool HasFit => FitNum > 0 && FitDen > 0;

    public bool HasEqualizer => BassDb != 0 || MidDb != 0 || TrebleDb != 0;

    public bool HasCleanup => Denoise || Loudnorm || Compressor || Limiter || HasEqualizer;

    public bool HasCrop => CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0 || CropKeys.Count > 0;

    public bool HasGeometry => HasCrop || HasFit || Rotation != 0 || FlipH || FlipV || HasTransform;

    public bool HasAudio => Muted || VolumeDb != 0 || VolumeKeys.Count > 0 || HasCleanup;

    public bool HasColor => Brightness != 0 || Contrast != 1 || Saturation != 1 || Temperature != NeutralTemperature || HasGrade || HasLut || HasMatch;

    public bool IsNeutral => !HasGeometry && !HasAudio && !HasColor && !HasBlur && Lut is null && FadeIn == 0 && FadeOut == 0;

    /// <summary>Degerleri gecerli araliga ceker: kirpma kenarlari toplamda karenin %95'ini asmaz, aci 90'in katina iner,
    /// renk alanlari araliga kirpilip iki ondaliga yuvarlanir; sayi olmayan renk degeri notre doner. Ekolayzir bantlari
    /// <see cref="MinEqDb"/>..<see cref="MaxEqDb"/> araligina kirpilip bir ondaliga yuvarlanir. Sigdirma orani
    /// 1/<see cref="MaxFitRatio"/>..<see cref="MaxFitRatio"/> disindaysa kalkar; dolgu rengi alti haneli buyuk harf onaltiliga
    /// iner, okunamazsa siyah olur, sigdirma yoksa varsayilana doner.</summary>
    public ClipEffects Normalized()
    {
        static double Edge(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 0.95) : 0;
        var left = Edge(CropLeft);
        var right = Math.Min(Edge(CropRight), 0.95 - left);
        var top = Edge(CropTop);
        var bottom = Math.Min(Edge(CropBottom), 0.95 - top);
        var rotation = ((Rotation / 90 * 90) % 360 + 360) % 360;
        var volume = double.IsFinite(VolumeDb) ? Math.Round(Math.Clamp(VolumeDb, MinVolumeDb, MaxVolumeDb), 1) : 0;
        return (BlurOnly(FitOnly()) with
        {
            CropLeft = left,
            CropRight = right,
            CropTop = top,
            CropBottom = bottom,
            Rotation = rotation,
            VolumeDb = volume,
            FadeIn = Math.Max(0, FadeIn),
            FadeOut = Math.Max(0, FadeOut),
            Brightness = Color(Brightness, 0, -1, 1),
            Contrast = Color(Contrast, 1, 0, 2),
            Saturation = Color(Saturation, 1, 0, 3),
            Temperature = Math.Clamp(Temperature, MinTemperature, MaxTemperature),
            BassDb = Band(BassDb),
            MidDb = Band(MidDb),
            TrebleDb = Band(TrebleDb),
            Scale = double.IsFinite(Scale) ? Math.Round(Math.Clamp(Scale, MinScale, MaxScale), 3) : 1,
            OffsetX = Offset(OffsetX),
            OffsetY = Offset(OffsetY),
            Angle = Turn(Angle),
            VolumeKeys = VolumeKeys.Map(k => k with { Db = double.IsFinite(k.Db) ? Math.Round(Math.Clamp(k.Db, MinVolumeDb, MaxVolumeDb), 1) : 0 }),
            CropKeys = FitOnly().HasFit ? KeyframeTrack<CropKey>.Empty : CropKeys.Map(CropEdges),
            TransformKeys = TransformKeys.Map(TransformEdges),
            Lut = string.IsNullOrWhiteSpace(Lut) ? null : Lut.Trim(),
            LutMix = Math.Clamp(LutMix, MinLutMix, MaxLutMix),
        }).GradeNormalized().MatchNormalized();
    }

    public ClipEffects SlicedKeys(long from, long to)
        => HasKeyframes ? this with { VolumeKeys = VolumeKeys.Slice(from, to), CropKeys = CropKeys.Slice(from, to), TransformKeys = TransformKeys.Slice(from, to) } : this;

    public ClipEffects ScaledKeys(double factor)
        => HasKeyframes ? this with { VolumeKeys = VolumeKeys.Scaled(factor), CropKeys = CropKeys.Scaled(factor), TransformKeys = TransformKeys.Scaled(factor) } : this;

    private static CropKey CropEdges(CropKey key)
    {
        static double Edge(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 0.95) : 0;
        var left = Edge(key.Left);
        var top = Edge(key.Top);
        return key with { Left = left, Right = Math.Min(Edge(key.Right), 0.95 - left), Top = top, Bottom = Math.Min(Edge(key.Bottom), 0.95 - top) };
    }

    /// <summary>Kirpma dikdortgeni, depolanan degil gorunen piksel olarak; kenarlar cift, en az 2x2.</summary>
    public (int X, int Y, int Width, int Height) CropRect(int width, int height)
    {
        var (cl, ct, cr, cb) = CropKeys.Count > 0 ? (CropKeys[0].Left, CropKeys[0].Top, CropKeys[0].Right, CropKeys[0].Bottom) : (CropLeft, CropTop, CropRight, CropBottom);
        var x = Even(width * cl);
        var y = Even(height * ct);
        var w = Math.Max(2, Even(width * (1 - cl - cr)));
        var h = Math.Max(2, Even(height * (1 - ct - cb)));
        w = Math.Min(w, EvenFloor(width - x));
        h = Math.Min(h, EvenFloor(height - y));
        return (x, y, Math.Max(2, w), Math.Max(2, h));
    }

    /// <summary>Kirpma, dondurme ve sigdirma sonrasi cikti boyutu; 90 ve 270'te genislik ile yukseklik yer degistirir.
    /// Sigdirmada uzun kenar kalir, oteki kenar orana gore cift sayiya iner.</summary>
    public (int Width, int Height) OutputSize(int width, int height)
    {
        var rect = CropRect(width, height);
        var (w, h) = Rotation is 90 or 270 ? (rect.Height, rect.Width) : (rect.Width, rect.Height);
        if (!HasFit) return (w, h);
        var side = Math.Max(w, h);
        var ratio = (double)FitNum / FitDen;
        return ratio >= 1 ? (side, Math.Max(2, Even(side / ratio))) : (Math.Max(2, Even(side * ratio)), side);
    }

    /// <summary>Uzunlugu <paramref name="length"/> olan klipte etkin giris ve cikis solmasi; ikisi toplamda klibi asmaz.</summary>
    public (long In, long Out) Fades(long length)
    {
        var fadeIn = Math.Clamp(FadeIn, 0, Math.Max(0, length));
        var fadeOut = Math.Clamp(FadeOut, 0, Math.Max(0, length - fadeIn));
        return (fadeIn, fadeOut);
    }

    /// <summary>Kaynak kareden <paramref name="num"/>:<paramref name="den"/> oranli, ortali kirpma; donmus goruntunun oranina gore.</summary>
    public ClipEffects WithAspect(int num, int den, int width, int height)
    {
        var cleared = this with { CropLeft = 0, CropTop = 0, CropRight = 0, CropBottom = 0, FitNum = 0, FitDen = 0, PadColor = DefaultPadColor };
        if (num <= 0 || den <= 0 || width <= 0 || height <= 0) return cleared;

        var target = Rotation is 90 or 270 ? (double)den / num : (double)num / den;
        var source = (double)width / height;
        if (Math.Abs(target - source) < 1e-6) return cleared;

        if (target < source)
        {
            var cut = (1 - target / source) / 2;
            return cleared with { CropLeft = cut, CropRight = cut };
        }

        var vertical = (1 - source / target) / 2;
        return cleared with { CropTop = vertical, CropBottom = vertical };
    }

    /// <summary><paramref name="num"/>:<paramref name="den"/> oranli cikti kare; kirpma kalkar, goruntu butunuyle sigar ve
    /// kalan alan <paramref name="color"/> ("RRGGBB") ile dolar. Oran gecersizse sigdirma da kirpma da kalkar.</summary>
    public ClipEffects WithFit(int num, int den, string color)
        => (this with { CropLeft = 0, CropTop = 0, CropRight = 0, CropBottom = 0, CropKeys = KeyframeTrack<CropKey>.Empty, FitNum = num, FitDen = den, PadColor = color }).FitOnly();

    public (int X, int Y, int Width, int Height) BlurRect(int width, int height)
    {
        if (!HasBlurRegion) return (0, 0, width, height);
        var x = Math.Min(EvenFloor((int)(width * BlurX)), EvenFloor(width - 2));
        var y = Math.Min(EvenFloor((int)(height * BlurY)), EvenFloor(height - 2));
        var w = Math.Clamp(Even(width * BlurWidth), 2, Math.Max(2, EvenFloor(width - x)));
        var h = Math.Clamp(Even(height * BlurHeight), 2, Math.Max(2, EvenFloor(height - y)));
        return (x, y, w, h);
    }

    public ClipEffects WithBlurRegion(double x1, double y1, double x2, double y2)
        => (this with
        {
            BlurSigma = HasBlur ? BlurSigma : DefaultBlurSigma,
            BlurX = Math.Min(x1, x2),
            BlurY = Math.Min(y1, y2),
            BlurWidth = Math.Abs(x2 - x1),
            BlurHeight = Math.Abs(y2 - y1),
        }).Normalized();

    private static ClipEffects BlurOnly(ClipEffects e)
    {
        var sigma = double.IsFinite(e.BlurSigma) ? Math.Round(Math.Clamp(e.BlurSigma, 0, MaxBlurSigma), 1) : 0;
        static double Unit(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
        var x = Unit(e.BlurX);
        var y = Unit(e.BlurY);
        var w = Math.Min(Unit(e.BlurWidth), 1 - x);
        var h = Math.Min(Unit(e.BlurHeight), 1 - y);
        var region = sigma > 0 && w >= MinBlurSide && h >= MinBlurSide;
        return e with
        {
            BlurSigma = sigma,
            BlurX = region ? Math.Round(x, 4) : 0,
            BlurY = region ? Math.Round(y, 4) : 0,
            BlurWidth = region ? Math.Round(w, 4) : 0,
            BlurHeight = region ? Math.Round(h, 4) : 0,
        };
    }

    private ClipEffects FitOnly()
    {
        var fit = FitNum > 0 && FitDen > 0 && (double)FitNum / FitDen is >= 1 / MaxFitRatio and <= MaxFitRatio;
        return this with { FitNum = fit ? FitNum : 0, FitDen = fit ? FitDen : 0, PadColor = fit ? Hex(PadColor) : DefaultPadColor };
    }

    /// <summary>Yalniz renk alanlarini <paramref name="look"/>'tan alir; geometri, ses ve solma kalir.</summary>
    public ClipEffects WithColor(ClipEffects look)
        => this with { Brightness = look.Brightness, Contrast = look.Contrast, Saturation = look.Saturation, Temperature = look.Temperature };

    /// <summary>Gizli V1 icin: renk, egri, eslesme ve LUT notre doner, ustune parlaklik -1 ve kontrast 0 kareyi siyaha basar; dolgu siyah olur.
    /// Geometri, ses ve solma kalir, boylece sure ve ses degismez.</summary>
    public ClipEffects Blackout()
        => WithColor(Dark).WithGrade(None).WithoutMatch() with { Lut = null, LutMix = MaxLutMix, PadColor = DefaultPadColor };

    private static ClipEffects Dark { get; } = new() { Brightness = -1, Contrast = 0, Saturation = 0 };

    private static double Color(double value, double neutral, double min, double max)
        => double.IsFinite(value) ? Math.Round(Math.Clamp(value, min, max), 2) : neutral;

    private static double Offset(double value) => double.IsFinite(value) ? Math.Round(Math.Clamp(value, -MaxOffset, MaxOffset), 3) : 0;

    private static double Turn(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var turned = Math.Round(value, 1) % 360;
        if (turned > MaxAngle) turned -= 360;
        if (turned <= -MaxAngle) turned += 360;
        return turned == 0 ? 0 : turned;
    }

    private static double Band(double value) => double.IsFinite(value) ? Math.Round(Math.Clamp(value, MinEqDb, MaxEqDb), 1) : 0;

    private static string Hex(string? value)
    {
        var text = (value ?? string.Empty).Trim().TrimStart('#');
        return text.Length == 6 && text.All(Uri.IsHexDigit) ? text.ToUpperInvariant() : DefaultPadColor;
    }

    private static int Even(double value) => Math.Max(0, (int)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2);

    private static int EvenFloor(int value) => Math.Max(0, value / 2 * 2);
}
