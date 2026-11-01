using System.Globalization;
using System.Text;

namespace VidShrink.Core;

public readonly record struct ZoomRegion(double Start, double End, double CenterX, double CenterY)
{
    public double Length => End - Start;
}

public static class AutoZoom
{
    public const double DefaultScale = 2.0;

    public const double MinScale = 1.1;

    public const double MaxScale = 4.0;

    public static readonly IReadOnlyList<double> Scales = new[] { 1.5, 2.0, 2.5, 3.0 };

    public const double LeadSeconds = 0.5;

    public const double HoldSeconds = 1.5;

    public const double MergeGapSeconds = 1.0;

    public const double MergeDistance = 0.25;

    public const double RampSeconds = 0.4;

    public const double MinRegionSeconds = 0.8;

    public const double PanGapSeconds = 1.0;

    public const double PanSeconds = 0.6;

    public const int MaxRegions = 60;

    public const int MaxPerspectiveLength = 24000;

    public const string TargetSuffix = "-zoom";

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static IReadOnlyList<ZoomRegion> Suggest(PointerTrack track, double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0) return Array.Empty<ZoomRegion>();

        var triggers = new List<(double Time, double X, double Y)>();
        foreach (var click in track.Clicks)
            if (Inside(track, click.X, click.Y)) triggers.Add((click.Ms / 1000.0, click.X / (double)track.Width, click.Y / (double)track.Height));
        foreach (var key in track.Keys)
            if (track.PositionAt(key) is { } at && Inside(track, at.X, at.Y))
                triggers.Add((key / 1000.0, at.X / (double)track.Width, at.Y / (double)track.Height));
        triggers.Sort((a, b) => a.Time.CompareTo(b.Time));

        var groups = new List<(double Start, double End, double SumX, double SumY, int Count)>();
        foreach (var (time, x, y) in triggers)
        {
            var start = Math.Max(0, time - LeadSeconds);
            var end = Math.Min(durationSeconds, time + HoldSeconds);
            if (end <= start) continue;

            if (groups.Count > 0)
            {
                var last = groups[^1];
                var cx = last.SumX / last.Count;
                var cy = last.SumY / last.Count;
                var near = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) <= MergeDistance;
                if (near && start - last.End <= MergeGapSeconds)
                {
                    groups[^1] = (last.Start, Math.Max(last.End, end), last.SumX + x, last.SumY + y, last.Count + 1);
                    continue;
                }

                if (start < last.End)
                {
                    var border = (start + last.End) / 2;
                    groups[^1] = last with { End = border };
                    start = border;
                }
            }

            groups.Add((start, end, x, y, 1));
        }

        var regions = new List<ZoomRegion>();
        foreach (var group in groups)
        {
            if (group.End - group.Start < MinRegionSeconds) continue;
            regions.Add(new ZoomRegion(group.Start, group.End, Math.Clamp(group.SumX / group.Count, 0, 1), Math.Clamp(group.SumY / group.Count, 0, 1)));
            if (regions.Count == MaxRegions) break;
        }

        return regions;
    }

    private static bool Inside(PointerTrack track, int x, int y) => x >= 0 && y >= 0 && x < track.Width && y < track.Height;

    public static double ClampScale(double scale) => double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : DefaultScale;

    private sealed record Pan(double From, double Length, double Dx, double Dy);

    private sealed record Shot(double Start, double End, double Ramp, double X, double Y, List<Pan> Pans);

    private static List<Shot> Shots(IReadOnlyList<ZoomRegion> regions)
    {
        var shots = new List<Shot>();
        ZoomRegion? previous = null;
        foreach (var region in regions.Where(r => double.IsFinite(r.Start) && double.IsFinite(r.End) && r.End > r.Start).OrderBy(r => r.Start))
        {
            var x = Math.Clamp(region.CenterX, 0, 1);
            var y = Math.Clamp(region.CenterY, 0, 1);
            if (previous is { } last && region.Start - last.End <= PanGapSeconds)
            {
                var shot = shots[^1];
                var mid = (last.End + region.Start) / 2;
                var half = Math.Min(PanSeconds / 2, Math.Min(mid - (last.Start + last.End) / 2, (region.Start + region.End) / 2 - mid));
                var fromX = shot.X + shot.Pans.Sum(pan => pan.Dx);
                var fromY = shot.Y + shot.Pans.Sum(pan => pan.Dy);
                if (half > 0) shot.Pans.Add(new Pan(mid - half, 2 * half, x - fromX, y - fromY));
                var end = Math.Max(shot.End, region.End);
                shots[^1] = shot with { End = end, Ramp = Math.Min(RampSeconds, (end - shot.Start) / 2) };
            }
            else
            {
                shots.Add(new Shot(region.Start, region.End, Math.Min(RampSeconds, region.Length / 2), x, y, new List<Pan>()));
            }

            previous = region;
        }

        return shots;
    }

    private static double Smooth(double value)
    {
        var v = Math.Clamp(value, 0, 1);
        return v * v * (3 - 2 * v);
    }

    /// <summary>
    /// <see cref="Filter"/>'ın <paramref name="time"/> anında uyguladığı yakınlık ve görüntü merkezi (0–1). Aralarında
    /// <see cref="PanGapSeconds"/>'tan az boşluk olan bölgeler tek çekimdir: yakınlık bırakılmaz, merkez sınırın
    /// ortasında <see cref="PanSeconds"/> içinde yumuşakça kayar.
    /// </summary>
    public static (double Zoom, double CenterX, double CenterY) At(IReadOnlyList<ZoomRegion> regions, double scale, double time)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var amount = ClampScale(scale) - 1;
        foreach (var shot in Shots(regions))
        {
            if (time < shot.Start || time >= shot.End) continue;
            var rise = Math.Clamp((time - shot.Start) / shot.Ramp, 0, 1) * Math.Clamp((shot.End - time) / shot.Ramp, 0, 1);
            var x = shot.X;
            var y = shot.Y;
            foreach (var pan in shot.Pans)
            {
                var k = Smooth((time - pan.From) / pan.Length);
                x += pan.Dx * k;
                y += pan.Dy * k;
            }

            return (1 + amount * Smooth(rise), x, y);
        }

        return (1, 0.5, 0.5);
    }

    private static string Envelope(List<Shot> shots, string clock)
    {
        var envelope = new StringBuilder("0");
        foreach (var shot in shots)
        {
            var s = Number(shot.Start);
            var e = Number(shot.End);
            var r = Number(shot.Ramp);
            envelope.Append($"+(st(0,clip(({clock}-{s})/{r},0,1)*clip(({e}-{clock})/{r},0,1));ld(0)*ld(0)*(3-2*ld(0)))");
        }

        return envelope.ToString();
    }

    private static string Centre(List<Shot> shots, string clock, bool horizontal)
    {
        var centre = new StringBuilder("0.5");
        foreach (var shot in shots)
        {
            var inside = $"gte({clock},{Number(shot.Start)})*lt({clock},{Number(shot.End)})";
            var offset = new StringBuilder(Number((horizontal ? shot.X : shot.Y) - 0.5));
            foreach (var pan in shot.Pans)
            {
                var k = $"(st(1,clip(({clock}-{Number(pan.From)})/{Number(pan.Length)},0,1));ld(1)*ld(1)*(3-2*ld(1)))";
                offset.Append($"+({Number(horizontal ? pan.Dx : pan.Dy)})*{k}");
            }

            centre.Append($"+{inside}*({offset})");
        }

        return centre.ToString();
    }

    /// <summary>
    /// Yakınlaştırma filtresi: <c>fps</c> ile sabit kare hızı, sonra karedeki yeri alt piksel kesinlikte süren
    /// <c>perspective</c>. <c>zoompan</c> kutuyu giriş pikseline yuvarladığı için kare kare 3 px'e kadar titrer
    /// (<c>docs/olcumler/imlec-ornekleme.md</c>); ifade <see cref="MaxPerspectiveLength"/>'i aşarsa komut satırına
    /// sığsın diye yine <c>zoompan</c>'a düşülür.
    /// </summary>
    public static string Filter(IReadOnlyList<ZoomRegion> regions, double scale, int width, int height, double fps)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var rate = double.IsFinite(fps) && fps > 0 ? fps : 30;
        var amount = ClampScale(scale) - 1;
        var shots = Shots(regions);
        var smooth = Perspective(shots, amount, rate);
        return smooth.Length <= MaxPerspectiveLength ? smooth : ZoomPan(shots, amount, width, height, rate);
    }

    public static string ZoomPanFilter(IReadOnlyList<ZoomRegion> regions, double scale, int width, int height, double fps)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return ZoomPan(Shots(regions), ClampScale(scale) - 1, width, height, double.IsFinite(fps) && fps > 0 ? fps : 30);
    }

    private static string ZoomPan(List<Shot> shots, double amount, int width, int height, double rate)
        => $"zoompan=z='1+{Number(amount)}*({Envelope(shots, "it")})'"
           + $":x='clip(({Centre(shots, "it", true)})*iw-iw/zoom/2,0,iw-iw/zoom)'"
           + $":y='clip(({Centre(shots, "it", false)})*ih-ih/zoom/2,0,ih-ih/zoom)'"
           + $":d=1:s={width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}:fps={Number(rate)}";

    private static string Perspective(List<Shot> shots, double amount, double rate)
    {
        var head = $"st(4,(in-1)/{Number(rate)});st(2,1/(1+{Number(amount)}*({Envelope(shots, "ld(4)")})));";
        var left = $"{head}W*clip(({Centre(shots, "ld(4)", true)})-ld(2)/2,0,1-ld(2))+(ld(2)-1)/2";
        var top = $"{head}H*clip(({Centre(shots, "ld(4)", false)})-ld(2)/2,0,1-ld(2))+(ld(2)-1)/2";
        var right = left + "+W*ld(2)";
        var bottom = top + "+H*ld(2)";
        return $"fps={Number(rate)},perspective=x0='{left}':y0='{top}':x1='{right}':y1='{top}':x2='{left}':y2='{bottom}':x3='{right}':y3='{bottom}'"
               + ":interpolation=cubic:eval=frame";
    }

    public static string Target(string source, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var folder = Path.GetDirectoryName(source) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(source) + TargetSuffix;
        var extension = Path.GetExtension(source).ToLowerInvariant() is ".mp4" or ".mov" or ".mkv" ? Path.GetExtension(source) : ".mkv";
        var candidate = Path.Combine(folder, stem + extension);
        for (var index = 2; exists(candidate); index++)
            candidate = Path.Combine(folder, stem + "_" + index.ToString(CultureInfo.InvariantCulture) + extension);
        return candidate;
    }

    public static IReadOnlyList<string> Build(string source, string target, string filter, bool hasAudio, int threads = 0)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source path is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Target path is required.", nameof(target));
        if (string.IsNullOrWhiteSpace(filter)) throw new ArgumentException("Filter is required.", nameof(filter));

        var args = new List<string>
        {
            "-hide_banner", "-y", "-nostdin",
            "-i", source,
            "-vf", filter,
            "-map", "0:v:0",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p"
        };
        if (threads > 0) args.AddRange(new[] { "-threads", threads.ToString(CultureInfo.InvariantCulture) });
        if (hasAudio) args.AddRange(new[] { "-map", "0:a?", "-c:a", "copy" });
        if (Path.GetExtension(target).ToLowerInvariant() is ".mp4" or ".mov") args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(target);
        return args;
    }
}
