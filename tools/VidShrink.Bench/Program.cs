using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Threading.Channels;
using System.Text.Json;
using VidShrink.Core;
using VidShrink.Ffmpeg;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

if (int.TryParse(Environment.GetEnvironmentVariable("VIDSHRINK_BENCH_CEKIRDEK"), out var cekirdek) && cekirdek > 0 && OperatingSystem.IsWindows())
{
    using var kendi = Process.GetCurrentProcess();
    kendi.ProcessorAffinity = (nint)((1L << Math.Min(cekirdek, 62)) - 1);
    kendi.PriorityClass = ProcessPriorityClass.BelowNormal;
}

try
{
    return args[0] switch
    {
        "measure" => await MeasureAsync(args),
        "measure-tonemapped" => await MeasureAsync(args, true),
        "measure-window" => await MeasureWindowAsync(args),
        "probe-quality-cost" => await ProbeQualityCostAsync(args),
        "normalization-cost" => await NormalizationCostAsync(args),
        "sample-container-cost" => await SampleContainerCostAsync(args),
        "container-unit" => await ContainerUnitAsync(args),
        "search-cost" => SearchCost(args),
        "peak-curve" => await PeakCurveAsync(args),
        "shrink" => await ShrinkAsync(args),
        "compare" => Compare(args),
        "panel" => await PanelAsync(args),
        "play" => await PlayAsync(args),
        "bar-burst" => await CubukAtagi.RunAsync(args),
        "psy-args" => PsyArgs(args),
        "measure-pair" => await MeasurePairAsync(args),
        "smart-audio-drift" => await AkilliSesKaymasi.RunAsync(args),
        "smart-mpeg2" => await AkilliMpeg2.RunAsync(args),
        "color-frame" => await RenkKaresi.RunAsync(args),
        "quality-bands" => await KaliteBandi.RunAsync(args),
        "transition-preview" => await GecisOnizleme.RunAsync(args),
        "proxy-seek" => await VekilSarma.RunAsync(args),
        "pointer-sampling" => await ImlecOrnekleme.RunAsync(args),
        "zoom-jitter" => await YakinlastirmaTitremesi.RunAsync(args),
        _ => Unknown(args[0])
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  bench measure <referans> <test>");
    Console.WriteLine("  bench measure-tonemapped <HDR referans> <SDR test>");
    Console.WriteLine("  bench measure-window <referans> <test> <başlangıç-sn> <süre-sn>");
    Console.WriteLine("  bench probe-quality-cost <kaynak> [fast|quality]");
    Console.WriteLine("  bench normalization-cost <HDR kaynak> <başlangıç-sn> <süre-sn>");
    Console.WriteLine("  bench sample-container-cost <kaynak> <başlangıç-sn> <çıktı-klasörü>");
    Console.WriteLine("  bench container-unit <kaynak,...> [--start 5] [--fps 12,24,30,60] [--out .calisma/kap]");
    Console.WriteLine("  bench search-cost [--runs 5]");
    Console.WriteLine("  bench peak-curve <kaynak> [--codec hevc_nvenc] [--ratios 3,5,8,12] [--peaks 1.02,1.1,1.25,1.5] [--out .calisma/tepe]");
    Console.WriteLine("  bench shrink <kaynak> <hedefMb,...> --out <klasor> [--measured-quality] [--fill filltarget|qualityceiling] [--speed quality|fast] [--no-resolution-drop] [--no-fps-drop] [--force-codec libx265] [--intent archive|sharing|socialmedia] [--lock-codec hevc_nvenc] [--codec-preference auto|compatible|maxcompression|fast] [--wide-peak] [--threads N] [--no-psy] [--plan-only] [--source-size 1920x1080] [--source-mb 1000] [--no-calibrate] [--results <yol>]");
    Console.WriteLine("  bench compare <a.json> <b.json>");
    Console.WriteLine("  bench bar-burst [tekrar]");
    Console.WriteLine("  bench psy-args <kodlayıcı>");
    Console.WriteLine("  bench measure-pair <referans> <test> [--fps <kaynak-hızı>] [--out <json>]");
    Console.WriteLine("  bench smart-audio-drift [--codecs aac,ac3,eac3,mp3,opus] [--containers mkv,mp4,mov,m4v] [--threshold-ms 41.7] [--out .calisma/akilli-ses-kaymasi]");
    Console.WriteLine("  bench smart-mpeg2 [--arms kapali-mkv,kapali-mp4,kapali-ts,taramali-tt-mkv,taramali-bb-ts,acik-mp4] [--min-psnr 30] [--out .calisma/akilli-mpeg2]");
    Console.WriteLine("  bench color-frame [klasor]");
    Console.WriteLine("  bench quality-bands [klasor]");
    Console.WriteLine("  bench transition-preview [klasor] [tekrar]");
    Console.WriteLine("  bench panel <klip,...> --only o1,o2,o3,o4,o5,o6 [--panel-width 960] [--zoom 4] [--samples 12] [--target 20]");
    Console.WriteLine("  bench play <klipA,klipB> --only k2,p1,p1b,k3,p2,p3,p5,p6,p8,p9,p10,p11,p12 [--seconds 10] [--fps 60] [--runs 3] [--target 20] [--matrix klip,...]");
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Bilinmeyen komut: {command}");
    PrintUsage();
    return 1;
}

static async Task<int> MeasureAsync(string[] args, bool tonemapReference = false)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: bench measure <referans> <test>");
        return 1;
    }

    return await BenchOlcum.YazAsync(
        () => tonemapReference
            ? QualityMeter.MeasureTonemappedReferenceAsync(args[1], args[2], CancellationToken.None)
            : QualityMeter.MeasureAsync(args[1], args[2], CancellationToken.None),
        Console.Out, Console.Error);
}

static async Task<int> MeasureWindowAsync(string[] args)
{
    if (args.Length < 5) return 1;
    var start = double.Parse(args[3], CultureInfo.InvariantCulture);
    var duration = double.Parse(args[4], CultureInfo.InvariantCulture);
    return await BenchOlcum.YazAsync(
        () => QualityMeter.MeasureWindowAsync(args[1], args[2], start, duration, CancellationToken.None),
        Console.Out, Console.Error);
}

static async Task<int> ProbeQualityCostAsync(string[] args)
{
    if (args.Length < 2) return 1;
    var info = await FfprobeClient.ProbeAsync(args[1]);
    var speed = args.Length > 2 && args[2].Equals("quality", StringComparison.OrdinalIgnoreCase)
        ? SpeedMode.Quality : SpeedMode.Fast;

    static async Task<(ProbeResult Result, long Milliseconds)> Run(MediaInfo media, SpeedMode mode, bool quality)
    {
        var watch = Stopwatch.StartNew();
        var result = await ComplexityProbe.RunDetailedAsync(media, mode, measureQuality: quality);
        watch.Stop();
        return (result, watch.ElapsedMilliseconds);
    }

    var offFirst = await Run(info, speed, false);
    var onSecond = await Run(info, speed, true);
    var onFirst = await Run(info, speed, true);
    var offSecond = await Run(info, speed, false);
    var offMs = (offFirst.Milliseconds + offSecond.Milliseconds) / 2.0;
    var onMs = (onFirst.Milliseconds + onSecond.Milliseconds) / 2.0;

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Source = Path.GetFileName(args[1]),
        info.DurationSeconds,
        info.Width,
        info.Height,
        QualityOffMilliseconds = offMs,
        QualityOnMilliseconds = onMs,
        DifferenceMilliseconds = onMs - offMs,
        DifferencePercent = (onMs / offMs - 1) * 100,
        OrderRuns = new { OffFirst = offFirst.Milliseconds, OnSecond = onSecond.Milliseconds, OnFirst = onFirst.Milliseconds, OffSecond = offSecond.Milliseconds },
        QualityWindows = onFirst.Result.QualityMeasurements
    }));
    return offFirst.Result.Profile.Measured || onFirst.Result.Profile.Measured ? 0 : 2;
}

static async Task<int> NormalizationCostAsync(string[] args)
{
    if (args.Length < 4) return 1;
    var start = double.Parse(args[2], CultureInfo.InvariantCulture);
    var duration = double.Parse(args[3], CultureInfo.InvariantCulture);
    var offFirst = await QualityMeter.MeasureReferenceDecodeCostAsync(args[1], start, duration, normalize: false);
    var onSecond = await QualityMeter.MeasureReferenceDecodeCostAsync(args[1], start, duration, normalize: true);
    var onFirst = await QualityMeter.MeasureReferenceDecodeCostAsync(args[1], start, duration, normalize: true);
    var offSecond = await QualityMeter.MeasureReferenceDecodeCostAsync(args[1], start, duration, normalize: false);
    var without = (offFirst + offSecond) / 2.0;
    var with = (onFirst + onSecond) / 2.0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Source = Path.GetFileName(args[1]), StartSeconds = start, DurationSeconds = duration,
        WithoutNormalizationMilliseconds = without,
        WithNormalizationMilliseconds = with,
        DifferenceMilliseconds = with - without,
        DifferencePercent = (with / without - 1) * 100,
        OrderRuns = new { OffFirst = offFirst, OnSecond = onSecond, OnFirst = onFirst, OffSecond = offSecond }
    }));
    return 0;
}

static async Task<int> SampleContainerCostAsync(string[] args)
{
    if (args.Length < 4) return 1;
    var start = double.Parse(args[2], CultureInfo.InvariantCulture);
    Directory.CreateDirectory(args[3]);
    var raw = Path.Combine(args[3], "sample.h264");
    var muxed = Path.Combine(args[3], "sample.mkv");
    async Task Encode(string format, string output)
    {
        var ffargs = new[] { "-hide_banner", "-loglevel", "error", "-y", "-ss", start.ToString(CultureInfo.InvariantCulture), "-t", "2", "-i", args[1], "-an", "-sn", "-dn", "-c:v", "libx264", "-crf", "23", "-preset", "veryfast", "-f", format, output };
        var psi = new ProcessStartInfo
        {
            FileName = ToolLocator.Ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in ffargs) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await stderr);
    }
    await Encode("h264", raw);
    await Encode("matroska", muxed);
    var rawBytes = new FileInfo(raw).Length;
    var muxedBytes = new FileInfo(muxed).Length;
    Console.WriteLine(JsonSerializer.Serialize(new { RawBytes = rawBytes, MatroskaBytes = muxedBytes, DifferenceBytes = muxedBytes - rawBytes, DifferencePercent = (muxedBytes / (double)rawBytes - 1) * 100 }));
    return 0;
}

static async Task<int> ContainerUnitAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: bench container-unit <kaynak,...> [--start 5] [--fps 12,24,30,60] [--out .calisma/kap]");
        return 1;
    }

    var sources = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
    var starts = new[] { 5.0 };
    var fpsList = new[] { 12.0, 24.0, 30.0, 60.0 };
    var outDir = Path.Combine(".calisma", "kap");
    for (var i = 2; i < args.Length - 1; i++)
    {
        if (args[i] == "--start") starts = args[i + 1].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        else if (args[i] == "--fps") fpsList = args[i + 1].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        else if (args[i] == "--out") outDir = args[i + 1];
    }
    Directory.CreateDirectory(outDir);

    var start = starts[0];
    var window = ComplexityProfile.SampleWindowSeconds;
    var rows = new List<object>();
    var fitX = new List<double>();
    var fitY = new List<double>();

    foreach (var source in sources)
    {
        var info = await FfprobeClient.ProbeAsync(source);
        var name = Path.GetFileNameWithoutExtension(source);

        foreach (var fps in fpsList)
        {
            if (fps > info.Fps + 0.001) continue;
            var tag = fps.ToString("0.###", CultureInfo.InvariantCulture);
            var raw = Path.Combine(outDir, name + "-" + tag + ".h264");
            var mkv = Path.Combine(outDir, name + "-" + tag + ".mkv");
            var filter = "fps=" + tag;
            await EncodeSampleAsync(source, start, window, filter, "h264", raw);
            await EncodeSampleAsync(source, start, window, filter, "matroska", mkv);
            var rawBytes = new FileInfo(raw).Length;
            var mkvBytes = new FileInfo(mkv).Length;
            var frames = await CountPacketsAsync(mkv);
            if (frames <= 0) continue;
            fitX.Add(frames);
            fitY.Add(mkvBytes - rawBytes);
            rows.Add(new { Source = name, Fps = fps, Frames = frames, RawBytes = rawBytes, MatroskaBytes = mkvBytes, OverheadBytes = mkvBytes - rawBytes, OverheadPerFrame = (mkvBytes - rawBytes) / (double)frames });
        }
    }

    var n = fitX.Count;
    double a = 0, b = 0, r2 = 0;
    if (n >= 2)
    {
        var mx = fitX.Average();
        var my = fitY.Average();
        var sxx = fitX.Sum(x => (x - mx) * (x - mx));
        var sxy = fitX.Zip(fitY, (x, y) => (x - mx) * (y - my)).Sum();
        b = sxx > 0 ? sxy / sxx : 0;
        a = my - b * mx;
        var ssTot = fitY.Sum(y => (y - my) * (y - my));
        var ssRes = fitX.Zip(fitY, (x, y) => (y - (a + b * x)) * (y - (a + b * x))).Sum();
        r2 = ssTot > 0 ? 1 - ssRes / ssTot : 1;
    }

    var motion = new List<object>();
    var motionValues = new List<double>();
    foreach (var source in sources)
    {
        var info = await FfprobeClient.ProbeAsync(source);
        var name = Path.GetFileNameWithoutExtension(source);
        var halfFps = info.Fps * ComplexityProbe.MotionProbeFpsRatio;

        foreach (var offset in starts)
        {
        if (offset + window > info.DurationSeconds) continue;
        var stamp = offset.ToString("0.###", CultureInfo.InvariantCulture);
        var fullRaw = Path.Combine(outDir, name + "-m" + stamp + "full.h264");
        var fullMkv = Path.Combine(outDir, name + "-m" + stamp + "full.mkv");
        await EncodeSampleAsync(source, offset, window, null, "h264", fullRaw);
        await EncodeSampleAsync(source, offset, window, null, "matroska", fullMkv);
        var halfRaw = Path.Combine(outDir, name + "-m" + stamp + "half.h264");
        var halfMkv = Path.Combine(outDir, name + "-m" + stamp + "half.mkv");
        var halfFilter = "fps=" + halfFps.ToString("0.###", CultureInfo.InvariantCulture);
        await EncodeSampleAsync(source, offset, window, halfFilter, "h264", halfRaw);
        await EncodeSampleAsync(source, offset, window, halfFilter, "matroska", halfMkv);

        var fullFrames = await CountPacketsAsync(fullMkv);
        var halfFrames = await CountPacketsAsync(halfMkv);
        if (fullFrames <= 0 || halfFrames <= 0) continue;

        var fullMkvBytes = new FileInfo(fullMkv).Length;
        var halfMkvBytes = new FileInfo(halfMkv).Length;
        var fullRawBytes = new FileInfo(fullRaw).Length;
        var halfRawBytes = new FileInfo(halfRaw).Length;

        var mkvRatio = Math.Log2(halfMkvBytes / (double)halfFrames / (fullMkvBytes / (double)fullFrames));
        var rawRatio = Math.Log2(halfRawBytes / (double)halfFrames / (fullRawBytes / (double)fullFrames));
        var cleanFull = fullMkvBytes / (double)fullFrames - (ComplexityProfile.SampleContainerFixedBytes / fullFrames + ComplexityProfile.SampleContainerBytesPerFrame);
        var cleanHalf = halfMkvBytes / (double)halfFrames - (ComplexityProfile.SampleContainerFixedBytes / halfFrames + ComplexityProfile.SampleContainerBytesPerFrame);
        var cleanRatio = cleanFull > 0 && cleanHalf > 0 ? Math.Log2(cleanHalf / cleanFull) : double.NaN;

        motionValues.Add(rawRatio);
        motion.Add(new
        {
            Source = name,
            StartSeconds = offset,
            SourceFps = info.Fps,
            FullFrames = fullFrames,
            HalfFrames = halfFrames,
            Log2Matroska = mkvRatio,
            Log2RawElementaryStream = rawRatio,
            Log2MatroskaDecontaminated = cleanRatio,
            ResidualToRaw = cleanRatio - rawRatio
        });
        }
    }

    var sorted = motionValues.OrderBy(v => v).ToArray();
    object? distribution = null;
    if (sorted.Length > 0)
        distribution = new
        {
            Points = sorted.Length,
            Min = sorted[0],
            Median = sorted[sorted.Length / 2],
            Max = sorted[^1],
            Mean = sorted.Average(),
            Default = ComplexityProfile.DefaultMotionExponent
        };

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        WindowSeconds = window,
        Overhead = rows,
        Fit = new { FixedBytes = a, BytesPerFrame = b, Points = n, R2 = r2 },
        InUse = new { FixedBytes = ComplexityProfile.SampleContainerFixedBytes, BytesPerFrame = ComplexityProfile.SampleContainerBytesPerFrame },
        Motion = motion,
        MotionDistribution = distribution
    }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    return 0;
}

static async Task EncodeSampleAsync(string source, double start, double seconds, string? filter, string format, string output)
{
    var list = new List<string>
    {
        "-hide_banner", "-loglevel", "error", "-y",
        "-ss", start.ToString(CultureInfo.InvariantCulture),
        "-t", seconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", source, "-an", "-sn", "-dn"
    };
    if (filter is not null) { list.Add("-vf"); list.Add(filter); }
    list.Add("-c:v"); list.Add("libx264");
    list.Add("-crf"); list.Add(ComplexityProfile.ProbeCrf.ToString("0.###", CultureInfo.InvariantCulture));
    list.Add("-preset"); list.Add(ComplexityProfile.ProbePreset);
    list.Add("-f"); list.Add(format);
    list.Add(output);

    using var process = new Process { StartInfo = BenchProcessInfo(ToolLocator.Ffmpeg, list) };
    process.Start();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException(await stderr);
}

static async Task<long> CountPacketsAsync(string path)
{
    var list = new[]
    {
        "-v", "error", "-select_streams", "v:0", "-count_packets",
        "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", path
    };
    using var process = new Process { StartInfo = BenchProcessInfo(ToolLocator.Ffprobe, list) };
    process.Start();
    var stdout = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    return long.TryParse(stdout.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

static ProcessStartInfo BenchProcessInfo(string fileName, IEnumerable<string> arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in arguments) psi.ArgumentList.Add(argument);
    return psi;
}

static int SearchCost(string[] args)
{
    var runs = 5;
    for (var i = 1; i < args.Length - 1; i++)
        if (args[i] == "--runs") runs = int.Parse(args[i + 1], CultureInfo.InvariantCulture);

    var worst = new MediaInfo
    {
        FilePath = "huge.mkv",
        FileSizeBytes = 4_194_304_000_000L,
        DurationSeconds = 28800,
        Width = 3840,
        Height = 2160,
        Fps = 60,
        VideoCodec = "h264",
        TotalBitrateBps = 1_200_000_000,
        AudioCodec = "aac",
        AudioBitrateBps = 128_000,
        AudioChannels = 2
    };
    var typical = new MediaInfo
    {
        FilePath = "capture.mkv",
        FileSizeBytes = 500_000_000L,
        DurationSeconds = 120,
        Width = 1920,
        Height = 1080,
        Fps = 60,
        VideoCodec = "h264",
        TotalBitrateBps = 35_000_000,
        AudioCodec = "aac",
        AudioBitrateBps = 128_000,
        AudioChannels = 2
    };

    var rows = new List<object>();
    foreach (var (name, info) in new[] { ("worst", worst), ("typical", typical) })
    {
        var options = new PlanOptions { Intent = Intent.Sharing };
        var ceilingMb = PlanCalculator.QualityCeilingTargetMb(info);
        var requested = PlanCalculator.BuildDetailed(info, new PlanOptions { Intent = Intent.Sharing, TargetMb = ceilingMb }, null).PredictedQuality - 0.01;

        _ = PlanCalculator.TargetMbForQuality(info, options, requested);

        var best = double.MaxValue;
        var evaluations = 0;
        for (var run = 0; run < runs; run++)
        {
            var watch = Stopwatch.StartNew();
            var result = PlanCalculator.TargetMbForQuality(info, options, requested);
            watch.Stop();
            evaluations = result.Evaluations;
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        rows.Add(new
        {
            Case = name,
            Evaluations = evaluations,
            Budget = PlanCalculator.QualitySearchMaxEvaluations,
            BestMilliseconds = best,
            MillisecondsPerEvaluation = best / Math.Max(evaluations, 1)
        });
    }

    Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static async Task<int> PeakCurveAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("kaynak gerekli.");
        return 1;
    }

    var source = Path.GetFullPath(args[1]);
    var codec = "hevc_nvenc";
    var outDir = Path.Combine(".calisma", "tepe");
    var ratios = new[] { 3.0, 5.0, 8.0, 12.0 };
    var peaks = new[] { 1.02, 1.10, 1.25, 1.50 };

    for (var i = 2; i < args.Length - 1; i++)
    {
        if (args[i] == "--codec") codec = args[i + 1];
        else if (args[i] == "--out") outDir = args[i + 1];
        else if (args[i] == "--ratios")
            ratios = args[i + 1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        else if (args[i] == "--peaks")
            peaks = args[i + 1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    }

    Directory.CreateDirectory(outDir);
    var info = await FfprobeClient.ProbeAsync(source);
    if (info is null)
    {
        Console.Error.WriteLine("kaynak okunamadi.");
        return 1;
    }

    var floorK = CodecModel.MinBitrateK(codec, info.Width, info.Height, info.Fps);
    Console.WriteLine($"kaynak {Path.GetFileNameWithoutExtension(source)} | {info.Width}x{info.Height}@{info.Fps:0.##} | {codec} | taban {floorK}k | donanim={CodecModel.IsHardware(codec)}");
    Console.WriteLine("| oran | tepe | uretim tepesi | b:v k | maxrate k | bufsize k | boyut MB | mean | harm | p10 | min | kelepce |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

    var rows = new List<object>();
    foreach (var ratio in ratios)
    {
        var bitrateK = (int)Math.Round(floorK * ratio);
        var produced = FfmpegArguments.PeakRateFactor(codec, bitrateK, info.Width, info.Height, info.Fps);
        foreach (var peak in peaks)
        {
            var maxrateK = (int)(bitrateK * peak);
            var bufsizeK = (int)(bitrateK * FfmpegArguments.BufferFactor(peak));
            var outputPath = Path.Combine(outDir,
                $"r{ratio.ToString("0.##", CultureInfo.InvariantCulture)}_p{peak.ToString("0.00", CultureInfo.InvariantCulture)}.mp4");

            var arguments = new List<string>
            {
                "-hide_banner", "-y", "-nostdin",
                "-i", source,
                "-c:v", codec,
                "-b:v", $"{bitrateK}k",
                "-maxrate", $"{maxrateK}k",
                "-bufsize", $"{bufsizeK}k"
            };
            arguments.AddRange(CodecModel.BitrateRateControlArgs(codec));
            arguments.Add("-an");
            arguments.Add(outputPath);

            var psi = BenchProcessInfo(ToolLocator.Ffmpeg, arguments);
            using (var process = Process.Start(psi)!)
            {
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"| {ratio:0.##} | {peak:0.00} | {produced:0.000} | {bitrateK} | {maxrateK} | {bufsizeK} | hata | - | - | - | - | - |");
                    Console.Error.WriteLine(error.Trim());
                    continue;
                }
            }

            var sizeMb = Megabayt.Oku(new FileInfo(outputPath).Length);
            var vmaf = await VmafNegAsync(source, outputPath, info.Width, info.Height);
            Console.WriteLine($"| {ratio:0.##} | {peak:0.00} | {produced:0.000} | {bitrateK} | {maxrateK} | {bufsizeK} | {sizeMb:0.###} | {Fmt(vmaf.Mean)} | {Fmt(vmaf.Harmonic)} | {Fmt(vmaf.P10)} | {Fmt(vmaf.Min)} | {vmaf.FloorClampedFrames} |");
            rows.Add(new
            {
                Ratio = ratio,
                Peak = peak,
                ProducedPeak = produced,
                BitrateK = bitrateK,
                MaxrateK = maxrateK,
                BufsizeK = bufsizeK,
                SizeMb = sizeMb,
                VmafNegMean = vmaf.Mean,
                VmafNegHarmonic = vmaf.Harmonic,
                VmafNegP10 = vmaf.P10,
                VmafNegMin = vmaf.Min,
                VmafNegFloorFrames = vmaf.FloorClampedFrames
            });
            try { File.Delete(outputPath); } catch { }
        }
    }

    await File.WriteAllTextAsync(Path.Combine(outDir, "tepe-egrisi.json"),
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static async Task<int> ShrinkAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: bench shrink <kaynak> <hedefMb,...> --out <klasor> [--fill filltarget|qualityceiling] [--no-calibrate] [--results <yol>]");
        return 1;
    }

    var source = args[1];
    var targets = args[2]
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => double.Parse(t, CultureInfo.InvariantCulture))
        .ToList();

    const int CalibrationRounds = 2;
    string? outDir = null;
    string? resultsPath = null;
    var calibrate = true;
    var fillPolicy = FillPolicy.FillTarget;
    var speedMode = SpeedMode.Quality;
    var allowResolutionDrop = true;
    var allowFpsDrop = true;
    string? forceCodec = null;
    var intent = Intent.Sharing;
    string? lockCodec = null;
    var codecPreference = CodecPreference.Auto;
    var widePeak = false;
    var planOnly = false;
    var noMeasure = false;
    var noPsy = false;
    var measuredQuality = false;
    var threadLimit = 0;
    (int Width, int Height)? sourceSize = null;
    double? sourceMb = null;
    var filters = VideoFilterOptions.Default;
    for (var i = 3; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--out" when i + 1 < args.Length:
                outDir = args[++i];
                break;
            case "--results" when i + 1 < args.Length:
                resultsPath = args[++i];
                break;
            case "--no-calibrate":
                calibrate = false;
                break;
            case "--speed" when i + 1 < args.Length:
                speedMode = args[++i].Equals("fast", StringComparison.OrdinalIgnoreCase) ? SpeedMode.Fast : SpeedMode.Quality;
                break;
            case "--no-resolution-drop":
                allowResolutionDrop = false;
                break;
            case "--no-fps-drop":
                allowFpsDrop = false;
                break;
            case "--intent" when i + 1 < args.Length:
                if (!Enum.TryParse(args[++i], ignoreCase: true, out intent))
                {
                    Console.Error.WriteLine($"bilinmeyen --intent: {args[i]} (Archive|Sharing|SocialMedia)");
                    return 1;
                }
                break;
            case "--lock-codec" when i + 1 < args.Length:
                lockCodec = args[++i];
                break;
            case "--force-codec" when i + 1 < args.Length:
                forceCodec = args[++i];
                break;
            case "--codec-preference" when i + 1 < args.Length:
                if (!Enum.TryParse<CodecPreference>(args[++i], ignoreCase: true, out codecPreference))
                {
                    Console.Error.WriteLine($"bilinmeyen --codec-preference: {args[i]} (Auto|Compatible|MaxCompression|Fast)");
                    return 1;
                }
                break;
            case "--wide-peak":
                widePeak = true;
                break;
            case "--plan-only":
                planOnly = true;
                break;
            case "--no-measure":
                noMeasure = true;
                break;
            case "--no-psy":
                noPsy = true;
                break;
            case "--filters" when i + 1 < args.Length:
                try { filters = VideoFilterChain.Parse(args[++i]); }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
                break;
            case "--measured-quality":
                measuredQuality = true;
                break;
            case "--threads" when i + 1 < args.Length:
                threadLimit = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--source-size" when i + 1 < args.Length:
                var dimensions = args[++i].Split('x', 'X');
                sourceSize = (int.Parse(dimensions[0], CultureInfo.InvariantCulture), int.Parse(dimensions[1], CultureInfo.InvariantCulture));
                break;
            case "--source-mb" when i + 1 < args.Length:
                sourceMb = double.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--fill" when i + 1 < args.Length:
                fillPolicy = args[++i].Trim().ToLowerInvariant() switch
                {
                    "qualityceiling" or "ceiling" => FillPolicy.QualityCeiling,
                    _ => FillPolicy.FillTarget
                };
                break;
        }
    }

    if (outDir is null)
    {
        Console.Error.WriteLine("--out <klasor> gerekli");
        return 1;
    }

    Directory.CreateDirectory(outDir);

    var info = await FfprobeClient.ProbeAsync(source);
    var probeWatch = Stopwatch.StartNew();
    var probed = await ComplexityProbe.RunDetailedAsync(info, speedMode, measuredQuality, measuredQuality ? QualityMeasurement.Instance : null);
    var complexity = probed.Profile;
    var anchors = probed.QualityMeasurements
        .Where(q => q is { Comparable: true, VmafNegMean: not null })
        .Select(q => q.VmafNegMean!.Value)
        .ToArray();
    if (measuredQuality && anchors.Length > 0) complexity = complexity.WithProbeQuality(anchors);
    if (sourceSize is { } overrideSize) info = info with { Width = overrideSize.Width, Height = overrideSize.Height };
    if (sourceMb is { } overrideMb) info = info with { FileSizeBytes = (long)(overrideMb * Megabayt.Bayt) };
    probeWatch.Stop();
    var results = new List<BenchResult>();
    var label = Path.GetFileNameWithoutExtension(source);
    Console.WriteLine($"kaynak {label} | fill={fillPolicy} | codec-tercihi={codecPreference} | prob {probeWatch.Elapsed.TotalSeconds:0.#}s | prob-ms {probeWatch.Elapsed.TotalMilliseconds:0} | luma {(complexity.MeanLuma is double luma ? luma.ToString("0.##", CultureInfo.InvariantCulture) : "yok")} | kalibre={complexity.Calibrated} | olculen kalite={(complexity.QualityMeasured ? string.Join("/", anchors.Select(a => a.ToString("0.##", CultureInfo.InvariantCulture))) : "yok")}");

    foreach (var targetMb in targets)
    {
        var options = new PlanOptions
        {
            TargetMb = targetMb,
            Codec = codecPreference,
            FillPolicy = fillPolicy,
            SpeedMode = speedMode,
            Intent = intent,
            AllowResolutionDrop = allowResolutionDrop,
            AllowFpsDrop = allowFpsDrop,
            LockedCodec = lockCodec,
            Filters = filters
        };
        var planWatch = Stopwatch.StartNew();
        var profile = complexity;
        var planResult = PlanCalculator.BuildDetailed(info, options, profile, EncoderCapabilities.Instance);
        if (calibrate)
        {
            var draft = planResult.Plan;
            var seed = profile;
            for (var round = 0; round < CalibrationRounds; round++)
            {
                var tuned = await CalibrationProbe.RunAsync(info, draft, seed, speedMode, CancellationToken.None);
                profile = tuned;
                planResult = PlanCalculator.BuildDetailed(info, options, profile, EncoderCapabilities.Instance);
                if (!tuned.Calibrated) break;

                var settled = planResult.Plan;
                var scale = info.Height <= 0 ? 1.0 : (double)settled.Height / info.Height;
                if (tuned.AppliesTo(settled.Codec, scale, settled.Fps)) break;

                draft = settled;
                seed = tuned.WithoutCalibration();
            }
        }
        planWatch.Stop();
        var plan = planResult.Plan;
        if (!string.IsNullOrWhiteSpace(forceCodec))
        {
            plan.Codec = forceCodec;
            plan.Preset = forceCodec == "libsvtav1" ? "6" : "slow";
            plan.Mode = "2pass";
            plan.Crf = null;
            var hdr = HdrResolver.Resolve(info, options.HdrPolicy, plan.Codec, EncoderCapabilities.Instance);
            plan.PixelFormat = CodecModel.OutputPixelFormat(plan.Codec, hdr.PixelFormat);
            plan.HdrVideoFilter = hdr.VideoFilter;
            plan.HdrColorArgs = hdr.ColorArgs.ToList();
        }
        if (widePeak && CodecModel.IsHardware(plan.Codec))
        {
            plan.ExtraArgs.AddRange(new[]
            {
                "-maxrate", $"{(int)(plan.VideoBitrateK * FfmpegArguments.WidePeakFactor)}k",
                "-bufsize", $"{(int)(plan.VideoBitrateK * FfmpegArguments.BufferFactor(FfmpegArguments.WidePeakFactor))}k"
            });
        }
        if (noPsy && plan.Codec.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
            plan.ExtraArgs.AddRange(new[] { "-spatial-aq", "0", "-temporal-aq", "0" });
        if (threadLimit > 0)
        {
            var sinir = threadLimit.ToString(CultureInfo.InvariantCulture);
            plan.ExtraArgs.AddRange(new[] { "-threads", sinir });
            if (plan.Codec.Equals("libx265", StringComparison.OrdinalIgnoreCase))
                plan.ExtraArgs.AddRange(new[] { "-x265-params", $"pools={sinir}:frame-threads={sinir}" });
            if (plan.Codec.Equals("libsvtav1", StringComparison.OrdinalIgnoreCase))
                plan.ExtraArgs.AddRange(new[] { "-svtav1-params", $"lp={sinir}" });
        }
        var band = FillBand.For(targetMb);

        var outputPath = Path.Combine(outDir, $"{label}_{targetMb.ToString("0.#", CultureInfo.InvariantCulture)}mb.mp4");
        var yoklamaGerekli = VideoFilterChain.NeedsInterlaceProbe(info, plan.Filters);
        var idet = yoklamaGerekli ? await InterlaceProbe.RunAsync(info) : null;
        plan.Filters = VideoFilterChain.ResolveInterlace(plan.Filters, info, idet);
        Console.WriteLine($"yoklama: idet={(yoklamaGerekli ? "kostu" : "kosmadi")} alan={info.FieldOrder ?? "-"} taramali={info.IsInterlaced}"
            + (idet is { } sayac ? $" tff={sayac.Tff} bff={sayac.Bff} progressive={sayac.Progressive} belirsiz={sayac.Undetermined} toplam={sayac.Total}" : string.Empty));
        Console.WriteLine($"filtre: deinterlace={plan.Filters.Deinterlace} zincir={string.Join(',', VideoFilterChain.Filters(info, plan))}");
        Console.WriteLine(KomutSatiri.Yaz(info, plan, outputPath, outDir, EncoderCapabilities.Instance));
        if (planOnly) continue;
        var stopwatch = Stopwatch.StartNew();
        var encodeResult = await new EncodeRunner().RunAsync(info, plan, outputPath, targetMb, null, CancellationToken.None, fillPolicy);
        stopwatch.Stop();
        var kaynakKare = info.DurationSeconds * info.Fps;
        Console.WriteLine($"hiz: kaynak-kare={kaynakKare:0} sure={stopwatch.Elapsed.TotalSeconds:0.##}s deneme={encodeResult.Attempts} fps={kaynakKare / stopwatch.Elapsed.TotalSeconds:0.##} gercek-zaman-kati={info.DurationSeconds / stopwatch.Elapsed.TotalSeconds:0.###}");

        foreach (var step in encodeResult.Trace ?? Array.Empty<EncodeAttempt>())
            Console.WriteLine($"  deneme {step.Number}: {step.Branch}, {step.Mode}, {step.VideoBitrateK}k, hedeflenen {step.AimMb:0.###} MB, cikan {step.ActualMb:0.###} MB");
        if (File.Exists(outputPath))
        {
            var anahtar = await KeyframeStampsAsync(outputPath);
            var araliklar = anahtar.Stamps.Zip(anahtar.Stamps.Skip(1), (x, y) => y - x).ToList();
            Console.WriteLine($"anahtar kare: adet={anahtar.Stamps.Count} en-uzun-aralik={(araliklar.Count > 0 ? araliklar.Max() : 0):0.###}s ortalama-aralik={(araliklar.Count > 0 ? araliklar.Average() : 0):0.###}s damgalar={string.Join(' ', anahtar.Stamps.Select(s => s.ToString("0.###", CultureInfo.InvariantCulture)))}");
        }

        var measureWatch = Stopwatch.StartNew();
        var vmaf = noMeasure ? VmafPool.Empty : await VmafNegAsync(source, outputPath, info.Width, info.Height);
        var planes = noMeasure ? (Y: (double?)null, U: (double?)null, V: (double?)null) : await XpsnrPlanesAsync(source, outputPath, info.Width, info.Height);
        measureWatch.Stop();
        var xpsnr = planes.Y is { } py && planes.U is { } pu && planes.V is { } pv
            ? (4 * py + pu + pv) / 6.0
            : (double?)null;

        var actual = encodeResult.OutputMb;
        var delivered = TeslimOzeti.Of(encodeResult);
        var result = new BenchResult(
            label,
            fillPolicy.ToString(),
            targetMb,
            actual,
            actual / targetMb * 100.0,
            band.LowerMb,
            band.HardFloorMb,
            actual <= targetMb && actual >= band.LowerMb,
            actual > targetMb,
            actual < band.HardFloorMb,
            delivered.Width,
            delivered.Height,
            delivered.Fps,
            delivered.Codec,
            delivered.Mode,
            delivered.CrfOrBitrate,
            encodeResult.Attempts,
            stopwatch.Elapsed.TotalSeconds,
            planWatch.Elapsed.TotalSeconds,
            probeWatch.Elapsed.TotalSeconds,
            measureWatch.Elapsed.TotalSeconds,
            profile.Calibrated,
            vmaf.Mean,
            vmaf.Harmonic,
            vmaf.P10,
            vmaf.Min,
            vmaf.FloorClampedFrames,
            xpsnr,
            planes.Y,
            planes.U,
            planes.V,
            encodeResult.Saturated,
            encodeResult.CeilingExceeded);
        results.Add(result);

        Console.WriteLine(
            $"{result.TargetMb:0.##} MB -> {result.ActualMb:0.##} MB ({result.FillPercent:0.#}%), " +
            $"bant={(result.InBand ? "ic" : "dis")} tasma={(result.OverTarget ? "VAR" : "yok")} taban={(result.BelowHardFloor ? "IHLAL" : "ok")}, " +
            $"{result.Width}x{result.Height}@{result.Fps:0.##}, {result.Codec}/{result.Mode}, {result.CrfOrBitrate}, deneme={result.Attempts}{(result.Saturated ? " DOYGUN" : "")}{(result.CeilingExceeded ? " TAVAN-ASILDI" : "")}, " +
            $"kalibre={(result.Calibrated ? "evet" : "hayir")}, plan={result.PlanSeconds:0.#}s, sure={result.EncodeSeconds:0.#}s, " +
            $"VMAF-NEG mean={Fmt(result.VmafNegMean)} harm={Fmt(result.VmafNegHarmonic)}{HarmonicWarning(result.VmafNegFloorFrames)} p10={Fmt(result.VmafNegP10)} min={Fmt(result.VmafNegMin)}, XPSNR={Fmt(result.Xpsnr)} (y={Fmt(result.XpsnrY)} u={Fmt(result.XpsnrU)} v={Fmt(result.XpsnrV)})");

        await WriteResultsAsync(results, outDir, resultsPath);
    }

    var finalPath = await WriteResultsAsync(results, outDir, resultsPath);
    Console.WriteLine($"sonuclar: {finalPath}");
    return 0;
}

static int PsyArgs(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: bench psy-args <kodlayıcı>");
        return 1;
    }
    Console.WriteLine(JsonSerializer.Serialize(FfmpegArguments.PsychovisualArgs(args[1], EncoderCapabilities.Instance)));
    return 0;
}

static async Task<int> MeasurePairAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: bench measure-pair <referans> <test> [--out <json>]");
        return 1;
    }
    string? outPath = null;
    string? testFps = null;
    for (var i = 3; i < args.Length; i++)
    {
        if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
        else if (args[i] == "--fps" && i + 1 < args.Length) testFps = args[++i];
    }

    var reference = await FfprobeClient.ProbeAsync(args[1]);
    var test = await FfprobeClient.ProbeAsync(args[2]);
    var vmaf = await VmafNegAsync(args[1], args[2], reference.Width, reference.Height, testFps);
    var planes = await XpsnrPlanesAsync(args[1], args[2], reference.Width, reference.Height, testFps);
    var dark = await KaranlikOlcu.OlcAsync(args[1], args[2], reference.Width, reference.Height, CancellationToken.None, testFps);
    var bytes = new FileInfo(args[2]).Length;
    var result = new
    {
        Referans = Path.GetFileName(args[1]),
        Test = Path.GetFileName(args[2]),
        Bayt = bytes,
        Mb = Megabayt.Oku(bytes),
        test.DurationSeconds,
        Kbps = test.DurationSeconds > 0 ? bytes * 8 / 1000.0 / test.DurationSeconds : (double?)null,
        test.Width,
        test.Height,
        VmafNegMean = vmaf.Mean,
        VmafNegHarmonic = vmaf.Harmonic,
        VmafNegP10 = vmaf.P10,
        VmafNegMin = vmaf.Min,
        Xpsnr = planes.Y is { } py && planes.U is { } pu && planes.V is { } pv ? (4 * py + pu + pv) / 6.0 : (double?)null,
        XpsnrY = planes.Y,
        Karanlik = dark
    };
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outPath is not null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outPath, json);
    }
    return vmaf.Mean is null ? 2 : 0;
}

static async Task<string> WriteResultsAsync(List<BenchResult> results, string outDir, string? resultsPath)
{
    var path = resultsPath ?? Path.Combine(outDir, "results.json");
    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    return path;
}

static async Task<VmafPool> VmafNegAsync(string referencePath, string testPath, int width, int height, string? testFps = null)
{
    var logPath = Path.Combine(Path.GetTempPath(), "vidshrink_bench_vmaf_" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        var escaped = "'" + logPath.Replace("\\", "/").Replace(":", "\\:") + "'";
        await RunLavfiAsync(referencePath, testPath, width, height,
            $"libvmaf=model=version=vmaf_v0.6.1neg:log_fmt=json:log_path={escaped}", testFps);

        if (!File.Exists(logPath)) return VmafPool.Empty;

        var scores = new List<double>();
        await using (var stream = File.OpenRead(logPath))
        {
            using var doc = await JsonDocument.ParseAsync(stream);
            foreach (var frame in doc.RootElement.GetProperty("frames").EnumerateArray())
            {
                var metrics = frame.GetProperty("metrics");
                if (metrics.TryGetProperty("vmaf", out var v) || metrics.TryGetProperty("vmaf_neg", out v))
                    scores.Add(v.GetDouble());
            }
        }

        return VmafPooling.Pool(scores);
    }
    finally
    {
        try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
    }
}

static async Task<string> RunLavfiAsync(string referencePath, string testPath, int width, int height, string filterChain, string? testFps = null)
{
    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in new[]
             {
                 "-hide_banner", "-nostdin",
                 "-i", testPath,
                 "-i", referencePath,
                 "-lavfi", BenchMeasureFilterGraph.Build(width, height, filterChain, testFps),
                 "-f", "null", "-"
             })
        psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    process.Start();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(stdoutTask, stderrTask);
    var stderr = await stderrTask;
    await process.WaitForExitAsync();
    return stderr;
}

static async Task<(double? Y, double? U, double? V)> XpsnrPlanesAsync(string referencePath, string testPath, int width, int height, string? testFps = null)
{
    var stderr = await RunLavfiAsync(referencePath, testPath, width, height, "xpsnr", testFps);

    var match = System.Text.RegularExpressions.Regex.Match(
        stderr, @"XPSNR\s+y:\s*([\d.]+)\s*u:\s*([\d.]+)\s*v:\s*([\d.]+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (!match.Success) return (null, null, null);

    return (double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
        double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
        double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
}

static int Compare(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: bench compare <a.json> <b.json>");
        return 1;
    }

    var a = JsonSerializer.Deserialize<List<BenchResult>>(File.ReadAllText(args[1])) ?? new List<BenchResult>();
    var b = JsonSerializer.Deserialize<List<BenchResult>>(File.ReadAllText(args[2])) ?? new List<BenchResult>();

    var count = Math.Max(a.Count, b.Count);
    for (var i = 0; i < count; i++)
    {
        var ar = i < a.Count ? a[i] : null;
        var br = i < b.Count ? b[i] : null;
        if (ar is null || br is null)
        {
            Console.WriteLine($"[{i}] eslesme yok (a:{ar is not null} b:{br is not null})");
            continue;
        }

        Console.WriteLine($"[{i}] {ar.Source} hedef {ar.TargetMb:0.##}MB -> {br.TargetMb:0.##}MB");
        Console.WriteLine($"  fill mod   {ar.FillPolicy} -> {br.FillPolicy}");
        Console.WriteLine($"  kalibre    {ar.Calibrated} -> {br.Calibrated}");
        Console.WriteLine($"  gercekMb   {ar.ActualMb:0.##} -> {br.ActualMb:0.##}");
        Console.WriteLine($"  doluluk%   {ar.FillPercent:0.#} -> {br.FillPercent:0.#}");
        Console.WriteLine($"  bant ici   {ar.InBand} -> {br.InBand}");
        Console.WriteLine($"  tasma      {ar.OverTarget} -> {br.OverTarget}");
        Console.WriteLine($"  taban ihl. {ar.BelowHardFloor} -> {br.BelowHardFloor}");
        Console.WriteLine($"  cozunurluk {ar.Width}x{ar.Height} -> {br.Width}x{br.Height}");
        Console.WriteLine($"  fps        {ar.Fps:0.##} -> {br.Fps:0.##}");
        Console.WriteLine($"  codec/mod  {ar.Codec}/{ar.Mode} -> {br.Codec}/{br.Mode}");
        Console.WriteLine($"  crf/bitrate {ar.CrfOrBitrate} -> {br.CrfOrBitrate}");
        Console.WriteLine($"  sure(s)    {ar.EncodeSeconds:0.#} -> {br.EncodeSeconds:0.#}");
        Console.WriteLine($"  vmaf harm  {Fmt(ar.VmafNegHarmonic)}{HarmonicWarning(ar.VmafNegFloorFrames)} -> {Fmt(br.VmafNegHarmonic)}{HarmonicWarning(br.VmafNegFloorFrames)}");
        Console.WriteLine($"  vmaf min   {Fmt(ar.VmafNegMin)} -> {Fmt(br.VmafNegMin)}");
        Console.WriteLine($"  vmaf p10   {Fmt(ar.VmafNegP10)} -> {Fmt(br.VmafNegP10)}");
        Console.WriteLine($"  xpsnr      {Fmt(ar.Xpsnr)} -> {Fmt(br.Xpsnr)}");
        Console.WriteLine($"  xpsnr yuv  {Fmt(ar.XpsnrY)}/{Fmt(ar.XpsnrU)}/{Fmt(ar.XpsnrV)} -> {Fmt(br.XpsnrY)}/{Fmt(br.XpsnrU)}/{Fmt(br.XpsnrV)}");
        Console.WriteLine($"  plan(s)    {ar.PlanSeconds:0.##} -> {br.PlanSeconds:0.##}");
        Console.WriteLine($"  olcum(s)   {ar.MeasureSeconds:0.#} -> {br.MeasureSeconds:0.#}");
        Console.WriteLine($"  prob(s)    {ar.ProbeSeconds:0.##} -> {br.ProbeSeconds:0.##}");
    }

    return 0;
}

static string Fmt(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";

static string HarmonicWarning(int floorFrames) =>
    floorFrames > 0 ? $"(SUPHELI: {floorFrames} kare {VmafPooling.HarmonicFloor:0.##} altinda, tabana kelepcelendi)" : string.Empty;

// --- T30: karsilastirma paneli olcum kapisi -----------------------------------------
// Bu komut yalniz sayi uretir. src/ altina hicbir sey yazmaz.

static async Task<int> PanelAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: bench panel <klip,...> --only o1,o2,o3,o4,o5,o6");
        return 1;
    }

    var clips = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    var only = new HashSet<string> { "o1", "o2", "o3", "o4", "o5", "o6" };
    var panelWidth = 960;
    var zoom = 4;
    var samples = 12;
    var targetMb = 20.0;
    var outDir = Path.Combine(Path.GetTempPath(), "vidshrink-panel");

    for (var i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--only" when i + 1 < args.Length:
                only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToHashSet();
                break;
            case "--panel-width" when i + 1 < args.Length:
                panelWidth = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--zoom" when i + 1 < args.Length:
                zoom = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--samples" when i + 1 < args.Length:
                samples = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--target" when i + 1 < args.Length:
                targetMb = double.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--out" when i + 1 < args.Length:
                outDir = args[++i];
                break;
        }
    }

    Directory.CreateDirectory(outDir);
    Console.WriteLine($"<!-- makine: {Environment.MachineName} Â· ffmpeg {ToolLocator.GetFfmpegVersion()} Â· panel {panelWidth}px Â· zoom {zoom}x Â· n={samples} -->");

    if (only.Contains("o1")) await Measure1Async(clips, panelWidth, samples);
    if (only.Contains("o4")) await Measure4Async(clips);
    if (only.Contains("o5")) await Measure5Async(clips, panelWidth, zoom, samples);
    if (only.Contains("o6")) await Measure6Async(clips, panelWidth, samples);
    if (only.Contains("o3")) await Measure3Async(clips);
    if (only.Contains("o2")) await Measure2Async(clips, panelWidth, targetMb, outDir);
    return 0;
}

/// <summary>
/// Tek kare cekme. stdout'tan ikili veri gelir, stderr **ayni anda** bosaltilir; iki gorev de
/// beklemeden once baslatilir. Bosaltilmazsa boru dolar ve ffmpeg asilir (Jellyfin #17429,
/// docs/taramalar/RAPOR.md:27).
/// </summary>
static async Task<(long Bytes, double Ms, string Error)> GrabAsync(string path, double at, int? width, string format)
{
    var a = new List<string>
    {
        "-hide_banner", "-nostdin", "-loglevel", "error",
        "-ss", at.ToString("0.###", CultureInfo.InvariantCulture),
        "-i", path, "-frames:v", "1", "-an", "-sn", "-dn"
    };

    if (width is { } w)
    {
        a.Add("-vf");
        a.Add($"scale={w}:-2:flags=bilinear");
    }

    if (format == "png") a.AddRange(new[] { "-f", "image2pipe", "-vcodec", "png", "-" });
    else a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in a) psi.ArgumentList.Add(arg);

    var stopwatch = Stopwatch.StartNew();
    using var process = new Process { StartInfo = psi };
    process.Start();

    var stdoutTask = DrainAsync(process.StandardOutput.BaseStream);
    var stderrTask = process.StandardError.ReadToEndAsync();
    var bytes = await stdoutTask;
    var error = await stderrTask;
    await process.WaitForExitAsync();
    stopwatch.Stop();

    return (bytes, stopwatch.Elapsed.TotalMilliseconds, process.ExitCode == 0 ? "" : error.Trim());
}

static async Task<long> DrainAsync(Stream stream)
{
    var buffer = new byte[128 * 1024];
    long total = 0;
    int read;
    while ((read = await stream.ReadAsync(buffer)) > 0) total += read;
    return total;
}

static double Percentile(IEnumerable<double> values, double percent)
{
    var sorted = values.OrderBy(v => v).ToList();
    if (sorted.Count == 0) return double.NaN;
    var index = (int)Math.Ceiling(percent / 100.0 * sorted.Count) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
}

static IEnumerable<double> Timestamps(double duration, int count)
{
    var usable = Math.Max(0.5, duration - 1.5);
    for (var i = 0; i < count; i++) yield return 0.5 + usable * (i + 0.5) / count;
}

static string Label(string path) => Path.GetFileNameWithoutExtension(path);

static async Task Measure1Async(List<string> clips, int panelWidth, int samples)
{
    Console.WriteLine();
    Console.WriteLine("## O1 - Tek kare cekme gecikmesi (PNG, stdout)");
    Console.WriteLine();
    Console.WriteLine("| Klip | Cozunurluk | Codec | Durum | n | Medyan ms | p95 ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var stamps = Timestamps(info.DurationSeconds, samples).ToList();

        var cold = new List<double>();
        var warm = new List<double>();
        foreach (var t in stamps)
        {
            var first = await GrabAsync(clip, t, panelWidth, "png");
            if (first.Error.Length > 0) { Console.Error.WriteLine($"{Label(clip)} @{t:0.##}: {first.Error}"); continue; }
            cold.Add(first.Ms);
        }

        foreach (var t in stamps)
        {
            var second = await GrabAsync(clip, t, panelWidth, "png");
            if (second.Error.Length > 0) continue;
            warm.Add(second.Ms);
        }

        Console.WriteLine($"| {Label(clip)} | {info.Width}x{info.Height} | {info.VideoCodec} | soguk | {cold.Count} | {Percentile(cold, 50):0.#} | {Percentile(cold, 95):0.#} |");
        Console.WriteLine($"| {Label(clip)} | {info.Width}x{info.Height} | {info.VideoCodec} | sicak | {warm.Count} | {Percentile(warm, 50):0.#} | {Percentile(warm, 95):0.#} |");
    }
}

static async Task Measure4Async(List<string> clips)
{
    Console.WriteLine();
    Console.WriteLine("## O4 - Bitmap bellek maliyeti (BGRA)");
    Console.WriteLine();
    Console.WriteLine("| Klip | Cozunurluk | Olculen bayt | MB | Beklenen WxHx4 |");
    Console.WriteLine("|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var raw = await GrabAsync(clip, info.DurationSeconds / 2.0, null, "bgra");
        var expected = (long)info.Width * info.Height * 4;
        Console.WriteLine($"| {Label(clip)} | {info.Width}x{info.Height} | {raw.Bytes} | {raw.Bytes / 1024.0 / 1024.0:0.##} | {expected} |");
    }
}

static async Task Measure5Async(List<string> clips, int panelWidth, int zoom, int samples)
{
    Console.WriteLine();
    Console.WriteLine("## O5 - Yakinlastirmanin kare talebine etkisi");
    Console.WriteLine();
    Console.WriteLine("| Klip | Istenen px | Teslim px | Kaynak tavani | n | Medyan ms | p95 ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var stamps = Timestamps(info.DurationSeconds, samples).ToList();

        foreach (var requested in new[] { panelWidth, panelWidth * zoom })
        {
            var delivered = Math.Min(requested, info.Width);
            var capped = delivered < requested;
            var times = new List<double>();
            foreach (var t in stamps)
            {
                var grab = await GrabAsync(clip, t, delivered, "png");
                if (grab.Error.Length > 0) { Console.Error.WriteLine($"{Label(clip)} @{t:0.##}/{delivered}: {grab.Error}"); continue; }
                times.Add(grab.Ms);
            }

            Console.WriteLine($"| {Label(clip)} | {requested} | {delivered} | {(capped ? "evet" : "hayir")} | {times.Count} | {Percentile(times, 50):0.#} | {Percentile(times, 95):0.#} |");
        }
    }
}

static async Task Measure3Async(List<string> clips)
{
    Console.WriteLine();
    Console.WriteLine("## O3 - Ornek kodlama suresi (CalibrationProbe)");
    Console.WriteLine();
    Console.WriteLine("| Klip | Plan | Kalibrasyon toplam s | Ornek fps | Kare | 2 sn pencere s |");
    Console.WriteLine("|---|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var complexity = await ComplexityProbe.RunAsync(info, SpeedMode.Quality);
        var options = new PlanOptions { TargetMb = 20 };
        var draft = PlanCalculator.BuildDetailed(info, options, complexity, EncoderCapabilities.Instance).Plan;

        var stopwatch = Stopwatch.StartNew();
        var calibrated = await CalibrationProbe.RunAsync(info, draft, complexity, SpeedMode.Quality, CancellationToken.None);
        stopwatch.Stop();

        var speed = calibrated.Speed;
        var window = speed is { FramesPerSecond: > 0 } ? (2.0 * draft.Fps / speed.FramesPerSecond).ToString("0.##", CultureInfo.InvariantCulture) : "olculemedi";
        Console.WriteLine(
            $"| {Label(clip)} | {draft.Width}x{draft.Height}@{draft.Fps:0.##} {draft.Codec}/{draft.Preset} | " +
            $"{stopwatch.Elapsed.TotalSeconds:0.##} | {Fmt(speed?.FramesPerSecond)} | {speed?.Frames.ToString() ?? "-"} | {window} |");
    }
}

// --- T32/K1: anahtar kare hizali cekme -----------------------------------------------
// T30 p95'i medyanin 4-5 kati buldu ve sebebi anahtar kare uzakligina bagladi ama olcmedi.
// Bu olcum ayni damgalari iki kez cekiyor: bir kez istenen anda (hizasiz), bir kez o andan
// once gelen en yakin anahtar karede (hizali).

/// <summary>
/// Anahtar kare damgalarini ffprobe paket listesinden cikarir. stdout ve stderr **ayni anda**
/// bosaltilir; ayni boru tuzagi (docs/taramalar/RAPOR.md:27) ffprobe icin de gecerli.
/// </summary>
static async Task<(List<double> Stamps, double Ms, string Error)> KeyframeStampsAsync(string path)
{
    var a = new[]
    {
        "-hide_banner", "-v", "error",
        "-select_streams", "v:0",
        "-show_entries", "packet=pts_time,flags",
        "-of", "csv=p=0",
        path
    };

    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffprobe,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in a) psi.ArgumentList.Add(arg);

    var stopwatch = Stopwatch.StartNew();
    using var process = new Process { StartInfo = psi };
    process.Start();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stdout = await stdoutTask;
    var error = await stderrTask;
    await process.WaitForExitAsync();
    stopwatch.Stop();

    var stamps = new List<double>();
    foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = line.Trim().Split(',');
        if (parts.Length < 2) continue;
        if (!parts[1].Contains('K')) continue;
        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pts)) stamps.Add(pts);
    }

    stamps.Sort();
    return (stamps, stopwatch.Elapsed.TotalMilliseconds, process.ExitCode == 0 ? "" : error.Trim());
}

/// <summary>Dosyayi acar, hicbir kare cozmez. Kuyrugun surec acilisindan mi geldigini ayirir.</summary>
static async Task<(double Ms, string Error)> OpenOnlyAsync(string path)
{
    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffprobe,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in new[] { "-hide_banner", "-v", "error", "-show_format", "-of", "csv=p=0", path })
        psi.ArgumentList.Add(arg);

    var stopwatch = Stopwatch.StartNew();
    using var process = new Process { StartInfo = psi };
    process.Start();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    _ = await stdoutTask;
    var error = await stderrTask;
    await process.WaitForExitAsync();
    stopwatch.Stop();
    return (stopwatch.Elapsed.TotalMilliseconds, process.ExitCode == 0 ? "" : error.Trim());
}

static double FloorKeyframe(List<double> keyframes, double at)
{
    var chosen = keyframes.Count > 0 ? keyframes[0] : 0;
    foreach (var k in keyframes)
    {
        if (k > at + 1e-6) break;
        chosen = k;
    }
    return chosen;
}

static async Task Measure6Async(List<string> clips, int panelWidth, int samples)
{
    Console.WriteLine();
    Console.WriteLine("## O6 - Anahtar kare dizini cikarma maliyeti");
    Console.WriteLine();
    Console.WriteLine("| Klip | Anahtar kare | Ortalama aralik s | Ilk cikarma ms | Tekrar ms |");
    Console.WriteLine("|---|---|---|---|---|");

    var index = new Dictionary<string, List<double>>();
    foreach (var clip in clips)
    {
        var first = await KeyframeStampsAsync(clip);
        if (first.Error.Length > 0) { Console.Error.WriteLine($"{Label(clip)}: {first.Error}"); continue; }
        var second = await KeyframeStampsAsync(clip);
        index[clip] = first.Stamps;

        var info = await FfprobeClient.ProbeAsync(clip);
        var gap = first.Stamps.Count > 1 ? info.DurationSeconds / (first.Stamps.Count - 1) : double.NaN;
        Console.WriteLine($"| {Label(clip)} | {first.Stamps.Count} | {gap:0.##} | {first.Ms:0.#} | {second.Ms:0.#} |");
    }

    // Kuyruk kod cozmeden mi geliyor yoksa surec acilisindan mi? Ayni dosyayi acan ama hic
    // kare cozmeyen bir cagri tabani verir; kuyruk burada da varsa kod cozmenin sucu degil.
    Console.WriteLine();
    Console.WriteLine("## O6 - Kod cozmesiz taban (ffprobe -show_format, ayni dosya)");
    Console.WriteLine();
    Console.WriteLine("| Klip | n | p50 ms | p90 ms | p95 ms | max ms | >400ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var times = new List<double>();
        for (var i = 0; i < samples; i++)
        {
            var probe = await OpenOnlyAsync(clip);
            if (probe.Error.Length == 0) times.Add(probe.Ms);
        }

        Console.WriteLine(
            $"| {Label(clip)} | {times.Count} | {Percentile(times, 50):0.#} | {Percentile(times, 90):0.#} | " +
            $"{Percentile(times, 95):0.#} | {(times.Count > 0 ? times.Max() : double.NaN):0.#} | {times.Count(v => v > 400)} |");
    }

    Console.WriteLine();
    Console.WriteLine("## O6 - Hizali ve hizasiz kare cekme gecikmesi (PNG, stdout)");
    Console.WriteLine();
    Console.WriteLine("| Klip | Cozunurluk | Codec | Hizalama | Durum | n | p50 ms | p90 ms | p95 ms | max ms | >400ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");

    var pooled = new Dictionary<string, List<double>>();

    foreach (var clip in clips)
    {
        if (!index.TryGetValue(clip, out var keyframes)) continue;
        var info = await FfprobeClient.ProbeAsync(clip);
        var stamps = Timestamps(info.DurationSeconds, samples).ToList();
        var aligned = stamps.Select(t => FloorKeyframe(keyframes, t)).ToList();

        foreach (var (mode, targets) in new[] { ("hizasiz", stamps), ("hizali", aligned) })
        {
            var poolKey = Label(clip) + "/" + mode;
            if (!pooled.ContainsKey(poolKey)) pooled[poolKey] = new List<double>();
            var cold = new List<double>();
            var warm = new List<double>();

            foreach (var t in targets)
            {
                var grab = await GrabAsync(clip, t, panelWidth, "png");
                if (grab.Error.Length > 0) { Console.Error.WriteLine($"{Label(clip)} {mode} @{t:0.##}: {grab.Error}"); continue; }
                cold.Add(grab.Ms);
            }

            foreach (var t in targets)
            {
                var grab = await GrabAsync(clip, t, panelWidth, "png");
                if (grab.Error.Length > 0) continue;
                warm.Add(grab.Ms);
            }

            pooled[poolKey].AddRange(cold);
            pooled[poolKey].AddRange(warm);
            Row("soguk", cold);
            Row("sicak", warm);

            void Row(string state, List<double> times) => Console.WriteLine(
                $"| {Label(clip)} | {info.Width}x{info.Height} | {info.VideoCodec} | {mode} | {state} | {times.Count} | " +
                $"{Percentile(times, 50):0.#} | {Percentile(times, 90):0.#} | {Percentile(times, 95):0.#} | " +
                $"{(times.Count > 0 ? times.Max() : double.NaN):0.#} | {times.Count(v => v > 400)} |");
        }
    }

    // Soguk ve sicak gecis ayni dagilimdan geliyor; kapi karari icin ikisi havuzlanir.
    // Kuyruk bandi (600-1000 ms) ayri sayilir: kod cozmesiz tabanda da gorunen durakalma.
    Console.WriteLine();
    Console.WriteLine("## O6 - Havuzlanmis (soguk+sicak) ve kuyruk bandi");
    Console.WriteLine();
    Console.WriteLine("| Klip/hizalama | n | p50 ms | p90 ms | p95 ms | p99 ms | 600-1000ms | >400ms % |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|");

    foreach (var (key, times) in pooled)
        Console.WriteLine(
            $"| {key} | {times.Count} | {Percentile(times, 50):0.#} | {Percentile(times, 90):0.#} | " +
            $"{Percentile(times, 95):0.#} | {Percentile(times, 99):0.#} | " +
            $"{times.Count(v => v is > 600 and < 1000)} | {100.0 * times.Count(v => v > 400) / Math.Max(1, times.Count):0.#} |");

    var dumpPath = Path.Combine(Path.GetTempPath(), "vidshrink-o6-ornekler.csv");
    var lines = new List<string> { "klip_hizalama,ms" };
    foreach (var (key, times) in pooled)
        lines.AddRange(times.Select(v => $"{key},{v.ToString("0.###", CultureInfo.InvariantCulture)}"));
    await File.WriteAllLinesAsync(dumpPath, lines);
    Console.WriteLine();
    Console.WriteLine($"Ham ornekler: {dumpPath}");
}

static async Task Measure2Async(List<string> clips, int panelWidth, double targetMb, string outDir)
{
    Console.WriteLine();
    Console.WriteLine("## O2 - Kodlama surerken ayni islem");
    Console.WriteLine();
    Console.WriteLine("| Klip | Bos kodlama s | Kare cekerken kodlama s | Kodlama yavaslamasi % | Cekim n | Medyan ms | p95 ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var complexity = await ComplexityProbe.RunAsync(info, SpeedMode.Quality);
        var options = new PlanOptions { TargetMb = targetMb };
        var plan = PlanCalculator.BuildDetailed(info, options, complexity, EncoderCapabilities.Instance).Plan;
        var outputPath = Path.Combine(outDir, Label(clip) + "_o2.mp4");

        var baseline = Stopwatch.StartNew();
        await new EncodeRunner().RunAsync(info, plan, outputPath, targetMb, null, CancellationToken.None);
        baseline.Stop();

        var times = new List<double>();
        using var stop = new CancellationTokenSource();
        var during = Stopwatch.StartNew();
        var encodeTask = new EncodeRunner().RunAsync(info, plan, outputPath, targetMb, null, CancellationToken.None);
        var grabTask = Task.Run(async () =>
        {
            var index = 0;
            while (!stop.IsCancellationRequested)
            {
                var at = 0.5 + (index++ * 1.7) % Math.Max(1.0, info.DurationSeconds - 1.5);
                var grab = await GrabAsync(clip, at, panelWidth, "png");
                if (grab.Error.Length == 0) times.Add(grab.Ms);
            }
        });

        await encodeTask;
        during.Stop();
        stop.Cancel();
        await grabTask;

        var slowdown = (during.Elapsed.TotalSeconds / baseline.Elapsed.TotalSeconds - 1.0) * 100.0;
        Console.WriteLine(
            $"| {Label(clip)} | {baseline.Elapsed.TotalSeconds:0.##} | {during.Elapsed.TotalSeconds:0.##} | {slowdown:0.#} | " +
            $"{times.Count} | {Percentile(times, 50):0.#} | {Percentile(times, 95):0.#} |");
    }
}

// --- T33: oynatma mimarisi olcum kapisi ----------------------------------------------
// Aday A: tek ffmpeg sureci, iki girdi, hstack, BGRA boru.
// Aday B (libmpv) bu araca girmiyor; ikili indirilemedigi icin raporda ayrica anlatiliyor.
// Bu komut da yalniz sayi uretir, src/ altina hicbir sey yazmaz.

static async Task<int> PlayAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: bench play <klipA,klipB> --only k2,p1,p2,p3,p5,p6,p8,p9,p10,p11 [--runs N] [--seconds S] [--fps N] [--target MB]");
        return 1;
    }

    var clips = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    var only = new HashSet<string> { "k2", "p1", "p1b", "k3", "p2", "p3", "p5", "p6", "p11", "p12" };
    var seconds = 10.0;
    var runs = 3;
    var fps = 60;
    var targetMb = 20.0;
    var matrix = new List<string>();
    var outDir = Path.Combine(Path.GetTempPath(), "vidshrink-play");

    for (var i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--only" when i + 1 < args.Length:
                only = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToLowerInvariant()).ToHashSet();
                break;
            case "--seconds" when i + 1 < args.Length:
                seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--runs" when i + 1 < args.Length:
                runs = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--fps" when i + 1 < args.Length:
                fps = int.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--target" when i + 1 < args.Length:
                targetMb = double.Parse(args[++i], CultureInfo.InvariantCulture);
                break;
            case "--matrix" when i + 1 < args.Length:
                matrix = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                break;
            case "--out" when i + 1 < args.Length:
                outDir = args[++i];
                break;
        }
    }

    Directory.CreateDirectory(outDir);
    var left = clips[0];
    var right = clips.Count > 1 ? clips[1] : clips[0];
    var frames = (int)Math.Round(seconds * fps);

    Console.WriteLine($"<!-- makine: {Environment.MachineName} Â· {Environment.ProcessorCount} mantiksal cekirdek Â· ffmpeg {ToolLocator.GetFfmpegVersion()} Â· hedef {fps} fps Â· {frames} kare -->");

    if (only.Contains("k2")) await ProbeFiltersAsync(left);
    if (only.Contains("p1")) await PlayPipeSizesAsync(left, right, fps, frames);
    if (only.Contains("p1b")) await PlayWallAsync(left, right, fps, frames);
    if (only.Contains("k3")) await PlayLadderAsync(left, right, frames);
    if (only.Contains("p6")) await PlayAllocAsync(left, right, fps, frames);
    if (only.Contains("p3")) await PlayHwAsync(left, right, fps, frames);
    if (only.Contains("p5")) await PlayMatrixAsync(matrix.Count > 0 ? matrix : clips, fps, frames);
    if (only.Contains("p2")) await PlayDuringEncodeAsync(left, right, fps, frames, targetMb, outDir);
    if (only.Contains("p10")) await PlayCeilingAsync(left, right, fps, frames, runs);
    if (only.Contains("p8")) await PlayBufferAsync(left, right, fps, frames, runs);
    if (only.Contains("p9")) await PlaySharedAsync(left, right, fps, frames, runs);
    if (only.Contains("p11")) await PlayGateAsync(left, right, fps, frames, runs);
    if (only.Contains("p12")) await PlayQueueAsync(left, right, fps, frames, runs);
    return 0;
}

/// <summary>
/// Tek ffmpeg sureci, iki girdi, hstack, BGRA ham boru. Tuketici kareyi **sabit havuzdan**
/// okur: kare basina yeni tampon ayrilmaz. stderr ayni anda bosaltilir, yoksa boru dolar
/// ve ffmpeg asilir (docs/taramalar/RAPOR.md:27).
/// Sure olcumu **ilk kare geldikten sonra** baslar; surec acilisi ayri raporlanir, cunku
/// kalici tek surecte o bedel bir kez odenir.
/// </summary>
static async Task<PipeStats> PipeAsync(
    string left, string right, int width, int height, int fps, int maxFrames,
    string? hwaccel = null, bool explicitDownload = false, bool naiveAlloc = false, bool realtime = false,
    bool loop = false, int chunkBytes = 64 * 1024, CancellationToken ct = default)
{
    var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };

    void AddInput(string path)
    {
        // -re girisi gercek zamanli okur: boru azami hizda degil oynatma hizinda akar.
        // Kodlamayla yarisi olcerken dogru olan bu; uretimde panel 60 fps ister, 300 degil.
        if (realtime) a.Add("-re");
        if (loop) { a.Add("-stream_loop"); a.Add("-1"); }

        if (hwaccel is { } hw)
        {
            a.Add("-hwaccel");
            a.Add(hw);
            if (explicitDownload)
            {
                a.Add("-hwaccel_output_format");
                a.Add(hw == "d3d11va" ? "d3d11" : hw);
            }
        }

        a.Add("-i");
        a.Add(path);
    }

    AddInput(left);
    AddInput(right);

    var head = explicitDownload ? "hwdownload,format=nv12," : "";
    var chain = $"{head}fps={fps},scale={width}:{height}:flags=bilinear,format=bgra";
    a.Add("-filter_complex");
    a.Add($"[0:v]{chain}[l];[1:v]{chain}[r];[l][r]hstack=inputs=2[o]");
    a.AddRange(new[] { "-map", "[o]", "-an", "-sn", "-dn", "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in a) psi.ArgumentList.Add(arg);

    var frameBytes = width * 2 * height * 4;
    var pool = new byte[frameBytes];
    var intervals = new List<double>(Math.Min(maxFrames, 1 << 16));

    using var process = new Process { StartInfo = psi };
    var launch = Stopwatch.StartNew();
    process.Start();
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stream = process.StandardOutput.BaseStream;

    var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
    var count = 0;
    var startupMs = double.NaN;
    var clock = new Stopwatch();
    var previous = 0.0;

    while (count < maxFrames && !ct.IsCancellationRequested)
    {
        var buffer = naiveAlloc ? new byte[frameBytes] : pool;
        var filled = 0;
        while (filled < frameBytes)
        {
            var want = Math.Min(chunkBytes, frameBytes - filled);
            var read = await stream.ReadAtLeastAsync(buffer.AsMemory(filled, want), want, throwOnEndOfStream: false);
            if (read < want) break;
            filled += read;
        }

        if (filled < frameBytes) break;

        if (count == 0)
        {
            startupMs = launch.Elapsed.TotalMilliseconds;
            clock.Start();
        }
        else
        {
            var now = clock.Elapsed.TotalMilliseconds;
            intervals.Add(now - previous);
            previous = now;
        }

        count++;
    }

    clock.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { /* surec kapanmis olabilir */ }

    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    var error = await stderrTask;
    await process.WaitForExitAsync();

    var elapsed = clock.Elapsed.TotalSeconds;
    var achieved = elapsed > 0 ? (count - 1) / elapsed : 0;
    var mbps = achieved * frameBytes / 1024.0 / 1024.0;
    var cpuPercent = elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0;

    return new PipeStats(
        count, startupMs, elapsed, achieved,
        Percentile(intervals, 50), Percentile(intervals, 95), Percentile(intervals, 99),
        intervals.Count > 0 ? intervals.Max() : double.NaN,
        intervals.Count > 0 ? intervals.Count(v => v > 25.0) * 100.0 / intervals.Count : double.NaN,
        mbps, cpuPercent,
        count > 0 ? (double)allocated / count : double.NaN,
        count == 0 ? error.Trim() : "");
}

/// <summary>K2: filtreyi liste sorgusuyla degil kucuk bir girdiyle grafigi kurarak sinar.</summary>
static async Task<(bool Ok, string Error)> TryGraphAsync(IEnumerable<string> arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in arguments) psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    process.Start();
    var stdoutTask = DrainAsync(process.StandardOutput.BaseStream);
    var stderrTask = process.StandardError.ReadToEndAsync();
    _ = await stdoutTask;
    var error = await stderrTask;
    await process.WaitForExitAsync();

    var text = error.Trim();
    var bad = process.ExitCode != 0
        || text.Contains("Error parsing option", StringComparison.OrdinalIgnoreCase)
        || text.Contains("No such filter", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase);

    var firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
    return (!bad, bad ? firstLine : "");
}

static async Task ProbeFiltersAsync(string clip)
{
    Console.WriteLine();
    Console.WriteLine("## K2 - Bu makinedeki ffmpeg'de gercekten ne var");
    Console.WriteLine();
    Console.WriteLine("| Yetenek | Deneme | Sonuc | stderr |");
    Console.WriteLine("|---|---|---|---|");

    var tiny = new[] { "-f", "lavfi", "-i", "testsrc2=s=64x64:r=10:d=0.2" };

    var cases = new List<(string Name, string What, string[] Args)>
    {
        ("hstack", "iki lavfi girdi hstack=inputs=2",
            tiny.Concat(tiny).Concat(new[] { "-filter_complex", "[0:v][1:v]hstack=inputs=2[o]", "-map", "[o]", "-frames:v", "1", "-f", "null", "-" }).ToArray()),
        ("scale+format=bgra", "scale=64:36,format=bgra",
            tiny.Concat(new[] { "-vf", "scale=64:36,format=bgra", "-frames:v", "1", "-f", "null", "-" }).ToArray()),
        ("fps", "fps=60",
            tiny.Concat(new[] { "-vf", "fps=60", "-frames:v", "1", "-f", "null", "-" }).ToArray()),
        ("zscale", "zscale=w=32:h=32",
            tiny.Concat(new[] { "-vf", "zscale=w=32:h=32", "-frames:v", "1", "-f", "null", "-" }).ToArray()),
        // T32: libx264/libx265 uzerinden verilen -color_trc/-color_primaries sessizce dusuyor.
        // HDR girdisi setparams ile kuruluyor; yoksa zscale "no path between colorspaces" der
        // ve tonemap yokmus gibi gorunur.
        ("tonemap", "setparams=HDR -> zscale=t=linear,tonemap=hable,zscale=t=bt709",
            tiny.Concat(new[]
            {
                "-vf",
                "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc," +
                "zscale=t=linear:npl=100,tonemap=hable,zscale=t=bt709:m=bt709:p=bt709:r=tv",
                "-frames:v", "1", "-f", "null", "-"
            }).ToArray()),
        ("rawvideo/bgra boru", "-f rawvideo -pix_fmt bgra -",
            tiny.Concat(new[] { "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "bgra", "-" }).ToArray())
    };

    foreach (var hw in new[] { "d3d11va", "dxva2", "qsv", "cuda", "vulkan" })
    {
        cases.Add(($"-hwaccel {hw}", "gercek dosyada tek kare",
            new[] { "-hide_banner", "-v", "error", "-hwaccel", hw, "-i", clip, "-frames:v", "1", "-f", "null", "-" }));
    }

    cases.Add(("d3d11va + hwdownload", "-hwaccel_output_format d3d11 -vf hwdownload,format=nv12",
        new[] { "-hide_banner", "-v", "error", "-hwaccel", "d3d11va", "-hwaccel_output_format", "d3d11", "-i", clip, "-vf", "hwdownload,format=nv12", "-frames:v", "1", "-f", "null", "-" }));

    foreach (var (name, what, arguments) in cases)
    {
        var full = arguments[0] == "-hide_banner" ? arguments : new[] { "-hide_banner", "-v", "error" }.Concat(arguments).ToArray();
        var result = await TryGraphAsync(full);
        var note = result.Error.Length > 80 ? result.Error[..80] + "..." : result.Error;
        Console.WriteLine($"| {name} | {what} | {(result.Ok ? "**var**" : "yok")} | {(note.Length == 0 ? "-" : note)} |");
    }
}

static string Row(string label, PipeStats s) =>
    $"| {label} | {s.Frames} | {s.StartupMs:0.#} | {s.Fps:0.#} | {s.P50:0.##} | {s.P95:0.##} | {s.P99:0.##} | {s.MaxMs:0.#} | {s.MBps:0.#} | {s.CpuPercent:0.#} |" +
    (s.Error.Length > 0 ? $" <!-- {s.Error} -->" : "");

static void Header(string first)
{
    Console.WriteLine($"| {first} | Kare | Acilis ms | Surdurulen fps | Aralik p50 ms | p95 ms | p99 ms | max ms | MB/s | CPU % |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
}

/// <summary>Sozlesmedeki uc panel boyutu. Kapilar G1 ilkine, G2 sonuncusuna bakiyor.</summary>
static (int W, int H)[] PanelSizes() => new[] { (960, 540), (1280, 720), (1920, 1080) };

static async Task PlayPipeSizesAsync(string left, string right, int fps, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## P1 - Boru akisinin hizi (tek surec, iki girdi, hstack, BGRA)");
    Console.WriteLine();
    Header("Boyut");

    foreach (var (w, h) in PanelSizes())
    {
        var stats = await PipeAsync(left, right, w, h, fps, frames);
        Console.WriteLine(Row($"2x{w}x{h}", stats));
    }
}

/// <summary>
/// Ayni grafik, ama kareler boruya yazilmiyor (<c>-f null -</c>). Kod cozme+olcekleme+hstack
/// kapasitesini boru tasima kapasitesinden ayirir: iki sayi arasindaki fark **borunun duvari**.
/// </summary>
static async Task<(double Fps, double CpuPercent, string Error)> NullSinkAsync(
    string left, string right, int width, int height, int fps, int maxFrames)
{
    var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", left, "-i", right };
    var chain = $"fps={fps},scale={width}:{height}:flags=bilinear,format=bgra";
    a.Add("-filter_complex");
    a.Add($"[0:v]{chain}[l];[1:v]{chain}[r];[l][r]hstack=inputs=2[o]");
    a.AddRange(new[] { "-map", "[o]", "-an", "-sn", "-dn", "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture), "-f", "null", "-" });

    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in a) psi.ArgumentList.Add(arg);

    using var process = new Process { StartInfo = psi };
    var clock = Stopwatch.StartNew();
    process.Start();
    var stdoutTask = DrainAsync(process.StandardOutput.BaseStream);
    var stderrTask = process.StandardError.ReadToEndAsync();
    _ = await stdoutTask;
    var error = await stderrTask;
    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { }
    await process.WaitForExitAsync();
    clock.Stop();

    var elapsed = clock.Elapsed.TotalSeconds;
    return (elapsed > 0 ? maxFrames / elapsed : 0, elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0,
        process.ExitCode == 0 ? "" : error.Trim());
}

static async Task PlayWallAsync(string left, string right, int fps, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## P1b - Duvar nerede: boru mu, kod cozme mi");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Boruyla fps | Borusuz (-f null) fps | Boru kaybi % | Boru MB/s | Borusuz CPU % |");
    Console.WriteLine("|---|---|---|---|---|---|");

    foreach (var (w, h) in PanelSizes())
    {
        var piped = await PipeAsync(left, right, w, h, fps, frames);
        var raw = await NullSinkAsync(left, right, w, h, fps, frames);
        var loss = raw.Fps > 0 ? (1.0 - piped.Fps / raw.Fps) * 100.0 : double.NaN;
        Console.WriteLine($"| 2x{w}x{h} | {piped.Fps:0.#} | {raw.Fps:0.#} | {loss:0.#} | {piped.MBps:0.#} | {raw.CpuPercent:0.#} |");
    }
}

/// <summary>
/// K3: duvara carpildiginda fps mi cozunurluk mu dusurulecek? Ayni cozunurlukte fps
/// merdiveni cikip her basamakta hedefin tutulup tutulmadigina bakilir.
/// </summary>
static async Task PlayLadderAsync(string left, string right, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## K3 - fps merdiveni (cozunurluk sabit)");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Hedef fps | Surdurulen fps | Hedefi tutuyor mu | Aralik p99 ms | MB/s | CPU % |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var (w, h) in new[] { (1920, 1080), (1280, 720) })
    {
        foreach (var target in new[] { 60, 48, 30, 24 })
        {
            // Klip 20 sn; dusuk hedefte 600 kare klibi asar, o yuzden 15 sn'lik icerikle sinirla.
            var capped = Math.Min(frames, target * 15);
            var stats = await PipeAsync(left, right, w, h, target, capped);
            var keeps = stats.Fps >= target * 0.98 ? "**evet**" : "hayir";
            Console.WriteLine($"| 2x{w}x{h} | {target} | {stats.Fps:0.#} | {keeps} | {stats.P99:0.##} | {stats.MBps:0.#} | {stats.CpuPercent:0.#} |");
        }
    }
}

static async Task PlayAllocAsync(string left, string right, int fps, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## P6 - Kare basina bellek ayirma");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Okuma bicimi | Kare | Kare basi ayrilan bayt | Kare boyutu bayt | Surdurulen fps |");
    Console.WriteLine("|---|---|---|---|---|---|");

    foreach (var (w, h) in PanelSizes())
    {
        foreach (var naive in new[] { false, true })
        {
            var stats = await PipeAsync(left, right, w, h, fps, frames, naiveAlloc: naive);
            var name = naive ? "kare basi yeni tampon" : "**sabit havuz**";
            Console.WriteLine($"| 2x{w}x{h} | {name} | {stats.Frames} | {stats.BytesPerFrame:0} | {w * 2 * h * 4} | {stats.Fps:0.#} |");
        }
    }
}

static async Task PlayHwAsync(string left, string right, int fps, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## P3 - Donanim kod cozme");
    Console.WriteLine();
    Header("Yol");

    var (w, h) = (1920, 1080);
    var variants = new (string Name, string? Hw, bool Download)[]
    {
        ("yazilim (taban)", null, false),
        ("d3d11va ortuk indirme", "d3d11va", false),
        ("d3d11va + hwdownload", "d3d11va", true),
        ("dxva2 ortuk indirme", "dxva2", false)
    };

    foreach (var (name, hw, download) in variants)
    {
        var stats = await PipeAsync(left, right, w, h, fps, frames, hw, download);
        Console.WriteLine(Row($"2x{w}x{h} Â· {name}", stats));
    }
}

static async Task PlayMatrixAsync(List<string> clips, int fps, int frames)
{
    Console.WriteLine();
    Console.WriteLine("## P5 - Codec matrisi (ayni klip iki kez, 2x960x540 panel)");
    Console.WriteLine();
    Header("Klip");

    foreach (var clip in clips)
    {
        var info = await FfprobeClient.ProbeAsync(clip);
        var stats = await PipeAsync(clip, clip, 960, 540, fps, frames);
        Console.WriteLine(Row($"{Label(clip)} ({info.Width}x{info.Height} {info.VideoCodec})", stats));
    }

    Console.WriteLine();
    Console.WriteLine("### P5 - Ayni klipler 2x1920x1080 panelde");
    Console.WriteLine();
    Header("Klip");

    foreach (var clip in clips)
    {
        var stats = await PipeAsync(clip, clip, 1920, 1080, fps, frames);
        Console.WriteLine(Row(Label(clip), stats));
    }
}

static async Task PlayDuringEncodeAsync(string left, string right, int fps, int frames, double targetMb, string outDir)
{
    Console.WriteLine();
    Console.WriteLine("## P2 - Kodlama koserken");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Boru kipi | Bos kodlama s | Boru koserken kodlama s | Kodlama yavaslamasi % | Akis fps | fps kaybi % | p99 ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|");

    var info = await FfprobeClient.ProbeAsync(left);
    var complexity = await ComplexityProbe.RunAsync(info, SpeedMode.Quality);
    var options = new PlanOptions { TargetMb = targetMb };
    var plan = PlanCalculator.BuildDetailed(info, options, complexity, EncoderCapabilities.Instance).Plan;
    var outputPath = Path.Combine(outDir, Label(left) + "_p2.mp4");

    async Task<double> EncodeMedianAsync(int runs)
    {
        var times = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var clock = Stopwatch.StartNew();
            await new EncodeRunner().RunAsync(info, plan, outputPath, targetMb, null, CancellationToken.None);
            clock.Stop();
            times.Add(clock.Elapsed.TotalSeconds);
        }

        return Percentile(times, 50);
    }

    var baseline = await EncodeMedianAsync(3);

    foreach (var (w, h) in PanelSizes())
    {
        // Uretimde panel oynatma hizinda akar ve **tek kalici surectir**. Azami hizli boru
        // ust siniri gosterir, -re ile gercek zamanli tek boru ise fiilen kurulacak olan sey.
        // Kapi ikincisine bakar.
        foreach (var realtime in new[] { false, true })
        {
            var wanted = fps;
            var alone = await PipeAsync(left, right, w, h, wanted, frames, realtime: realtime);

            // Tek boru surekli koser; kodlama uc kez yanibasinda calisir.
            using var stop = new CancellationTokenSource();
            var pipeTask = PipeAsync(left, right, w, h, wanted, int.MaxValue, realtime: realtime, loop: true, ct: stop.Token);

            var during = await EncodeMedianAsync(3);
            stop.Cancel();
            var pipe = await pipeTask;

            var slowdown = (during / baseline - 1.0) * 100.0;
            var loss = alone.Fps > 0 ? (1.0 - pipe.Fps / alone.Fps) * 100.0 : double.NaN;
            var mode = realtime ? $"**-re {wanted} fps**" : "azami hiz";
            Console.WriteLine(
                $"| 2x{w}x{h} | {mode} | {baseline:0.##} | {during:0.##} | {slowdown:0.#} | " +
                $"{pipe.Fps:0.#} | {loss:0.#} | {pipe.P99:0.##} |");
        }
    }
}

static (double Mean, double Sd) MeanSd(IReadOnlyList<double> values)
{
    if (values.Count == 0) return (double.NaN, double.NaN);
    var mean = values.Average();
    if (values.Count < 2) return (mean, 0);
    return (mean, Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1)));
}

static List<string> GraphArgs(string left, string right, int width, int height, int fps)
{
    var a = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", left, "-i", right };
    var chain = $"fps={fps},scale={width}:{height}:flags=bilinear,format=bgra";
    a.Add("-filter_complex");
    a.Add($"[0:v]{chain}[l];[1:v]{chain}[r];[l][r]hstack=inputs=2[o]");
    a.AddRange(new[] { "-map", "[o]", "-an", "-sn", "-dn" });
    return a;
}

static Process StartFfmpeg(IEnumerable<string> arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = ToolLocator.Ffmpeg,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in arguments) psi.ArgumentList.Add(arg);
    var process = new Process { StartInfo = psi };
    process.Start();
    return process;
}

static async Task<(double Fps, double CpuPercent, string Error)> SinkAsync(
    string left, string right, int width, int height, int fps, int maxFrames, params string[] tail)
{
    var a = GraphArgs(left, right, width, height, fps);
    a.Add("-frames:v");
    a.Add(maxFrames.ToString(CultureInfo.InvariantCulture));
    a.AddRange(tail);

    var clock = Stopwatch.StartNew();
    using var process = StartFfmpeg(a);
    var stdoutTask = DrainAsync(process.StandardOutput.BaseStream);
    var stderrTask = process.StandardError.ReadToEndAsync();
    _ = await stdoutTask;
    var error = await stderrTask;
    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { }
    await process.WaitForExitAsync();
    clock.Stop();

    var elapsed = clock.Elapsed.TotalSeconds;
    return (elapsed > 0 ? maxFrames / elapsed : 0, elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0,
        process.ExitCode == 0 ? "" : error.Trim());
}

static async Task<(double Fps, double CpuPercent, string Error)> PipeDrainAsync(
    string left, string right, int width, int height, int fps, int maxFrames, int blockBytes)
{
    var a = GraphArgs(left, right, width, height, fps);
    a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var frameBytes = (long)width * 2 * height * 4;
    var total = frameBytes * maxFrames;
    var buffer = new byte[blockBytes];

    using var process = StartFfmpeg(a);
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stream = process.StandardOutput.BaseStream;

    long read = 0;
    long timed = 0;
    var clock = new Stopwatch();
    while (read < total)
    {
        var n = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
        if (n <= 0) break;
        read += n;
        if (clock.IsRunning) timed += n; else clock.Start();
        if (n < buffer.Length) break;
    }
    clock.Stop();

    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { }
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    var error = await stderrTask;
    await process.WaitForExitAsync();

    var elapsed = clock.Elapsed.TotalSeconds;
    var delivered = timed / (double)frameBytes;
    return (elapsed > 0 ? delivered / elapsed : 0,
        elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0,
        read >= frameBytes ? "" : error.Trim());
}

static async Task<(double Fps, double CpuPercent, string Error)> PipeChunkedAsync(
    string left, string right, int width, int height, int fps, int maxFrames, int chunkBytes)
{
    var a = GraphArgs(left, right, width, height, fps);
    a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var frameBytes = width * 2 * height * 4;
    var pool = new byte[frameBytes];

    using var process = StartFfmpeg(a);
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stream = process.StandardOutput.BaseStream;

    var count = 0;
    var clock = new Stopwatch();
    var broken = false;
    while (count < maxFrames && !broken)
    {
        var filled = 0;
        while (filled < frameBytes)
        {
            var want = Math.Min(chunkBytes, frameBytes - filled);
            var n = await stream.ReadAtLeastAsync(pool.AsMemory(filled, want), want, throwOnEndOfStream: false);
            if (n < want) { broken = true; break; }
            filled += n;
        }

        if (broken) break;
        if (count == 0) clock.Start();
        count++;
    }
    clock.Stop();

    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { }
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    var error = await stderrTask;
    await process.WaitForExitAsync();

    var elapsed = clock.Elapsed.TotalSeconds;
    return (elapsed > 0 ? (count - 1) / elapsed : 0,
        elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0,
        count > 1 ? "" : error.Trim());
}

static async Task<(double Fps, double CpuPercent, string Error)> PipeSharedAsync(
    string left, string right, int width, int height, int fps, int maxFrames, int slots)
{
    var a = GraphArgs(left, right, width, height, fps);
    a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var frameBytes = width * 2 * height * 4;
    using var mmf = MemoryMappedFile.CreateNew(null, (long)frameBytes * slots);
    var views = new MemoryMappedViewStream[slots];
    for (var i = 0; i < slots; i++) views[i] = mmf.CreateViewStream((long)frameBytes * i, frameBytes);

    var freeCh = System.Threading.Channels.Channel.CreateBounded<int>(slots);
    var fillCh = System.Threading.Channels.Channel.CreateBounded<int>(slots);
    for (var i = 0; i < slots; i++) freeCh.Writer.TryWrite(i);

    using var process = StartFfmpeg(a);
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stream = process.StandardOutput.BaseStream;

    var producer = Task.Run(async () =>
    {
        var staging = new byte[frameBytes];
        for (var i = 0; i < maxFrames; i++)
        {
            var n = await stream.ReadAtLeastAsync(staging, frameBytes, throwOnEndOfStream: false);
            if (n < frameBytes) break;
            var slot = await freeCh.Reader.ReadAsync();
            views[slot].Position = 0;
            views[slot].Write(staging, 0, frameBytes);
            await fillCh.Writer.WriteAsync(slot);
        }
        fillCh.Writer.Complete();
    });

    var sink = new byte[frameBytes];
    var count = 0;
    var clock = new Stopwatch();
    await foreach (var slot in fillCh.Reader.ReadAllAsync())
    {
        views[slot].Position = 0;
        views[slot].ReadExactly(sink, 0, frameBytes);
        if (count == 0) clock.Start();
        count++;
        freeCh.Writer.TryWrite(slot);
        if (count >= maxFrames) break;
    }
    clock.Stop();

    var cpu = TimeSpan.Zero;
    try { cpu = process.TotalProcessorTime; } catch { }
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    var error = await stderrTask;
    await process.WaitForExitAsync();
    try { await producer; } catch { }
    foreach (var view in views) view.Dispose();

    var elapsed = clock.Elapsed.TotalSeconds;
    return (elapsed > 0 ? (count - 1) / elapsed : 0,
        elapsed > 0 ? cpu.TotalSeconds / elapsed * 100.0 : 0,
        count > 0 ? "" : error.Trim());
}

static async Task RepeatAsync(string label, int runs, Func<Task<(double Fps, double CpuPercent, string Error)>> once)
{
    var values = new List<double>();
    var cpus = new List<double>();
    var error = "";
    for (var i = 0; i < runs; i++)
    {
        var (fps, cpu, err) = await once();
        if (err.Length > 0) error = err;
        values.Add(fps);
        cpus.Add(cpu);
    }

    var (mean, sd) = MeanSd(values);
    var (cpuMean, _) = MeanSd(cpus);
    var each = string.Join(" / ", values.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)));
    Console.WriteLine($"| {label} | {runs} | {mean:0.#} | {sd:0.##} | {each} | {cpuMean:0.#} |" +
        (error.Length > 0 ? $" <!-- {error} -->" : ""));
}

static void RunHeader(string first)
{
    Console.WriteLine($"| {first} | Kosu | Ortalama fps | Sapma | Tek tek | ffmpeg CPU % |");
    Console.WriteLine("|---|---|---|---|---|---|");
}

static async Task PlayCeilingAsync(string left, string right, int fps, int frames, int runs)
{
    Console.WriteLine();
    Console.WriteLine("## P10 - Tasima tavani: duvar boru mu, tuketici mi");
    Console.WriteLine();

    foreach (var (w, h) in new[] { (1920, 1080), (960, 540) })
    {
        Console.WriteLine($"### 2x{w}x{h} - {(long)w * 2 * h * 4 / 1024 / 1024.0:0.#} MB kare");
        Console.WriteLine();
        RunHeader("Cikis yolu");

        await RepeatAsync("`-f null -` (kare hic paketlenmiyor)", runs,
            () => SinkAsync(left, right, w, h, fps, frames, "-f", "null", "-"));
        await RepeatAsync("`-f rawvideo` -> NUL (paketlenir, boru yok)", runs,
            () => SinkAsync(left, right, w, h, fps, frames, "-f", "rawvideo", "-pix_fmt", "bgra", "NUL"));
        await RepeatAsync("boru -> ham bosaltma, 1 MB blok, kare siniri yok", runs,
            () => PipeDrainAsync(left, right, w, h, fps, frames, 1 << 20));
        await RepeatAsync("boru -> kare hizali havuz okumasi (kare boyu tek okuma)", runs,
            async () =>
            {
                var s = await PipeAsync(left, right, w, h, fps, frames, chunkBytes: w * 2 * h * 4);
                return (s.Fps, s.CpuPercent, s.Error);
            });
        await RepeatAsync("boru -> kare havuzu, 64 KB parcalarla toplanir", runs,
            async () =>
            {
                var s = await PipeAsync(left, right, w, h, fps, frames);
                return (s.Fps, s.CpuPercent, s.Error);
            });

        Console.WriteLine();
    }
}

static async Task PlayBufferAsync(string left, string right, int fps, int frames, int runs)
{
    Console.WriteLine();
    Console.WriteLine("## P8 - Okuma blogu buyutulunce ne oluyor");
    Console.WriteLine();

    foreach (var (w, h) in new[] { (1920, 1080), (960, 540) })
    {
        var frameBytes = w * 2 * h * 4;
        Console.WriteLine($"### 2x{w}x{h} - kare {frameBytes / 1024 / 1024.0:0.#} MB");
        Console.WriteLine();
        RunHeader("Okuma blogu");

        foreach (var (name, block) in new (string, int)[]
                 {
                     ("64 KB", 64 * 1024),
                     ("1 MB", 1 << 20),
                     ("1 kare", frameBytes),
                     ("2 kare", frameBytes * 2),
                     ("4 kare", frameBytes * 4)
                 })
        {
            await RepeatAsync(name, runs, () => PipeDrainAsync(left, right, w, h, fps, frames, block));
        }

        foreach (var (name, chunk) in new (string, int)[]
                 {
                     ("kare havuzu, 64 KB parcalarla toplanir", 64 * 1024),
                     ("kare havuzu, 256 KB parcalarla toplanir", 256 * 1024),
                     ("kare havuzu, 1 MB parcalarla toplanir", 1 << 20)
                 })
        {
            await RepeatAsync(name, runs, () => PipeChunkedAsync(left, right, w, h, fps, frames, chunk));
        }

        Console.WriteLine();
    }
}

static async Task<(double Fps, double MissPercent, double LateP99, double LateMax, string Error)> PipeQueuedAsync(
    string left, string right, int width, int height, int fps, int maxFrames, int depth)
{
    var a = GraphArgs(left, right, width, height, fps);
    a.AddRange(new[] { "-f", "rawvideo", "-pix_fmt", "bgra", "-" });

    var frameBytes = width * 2 * height * 4;
    var ready = Channel.CreateBounded<byte[]>(depth);
    var spare = Channel.CreateBounded<byte[]>(depth + 2);
    for (var i = 0; i < depth + 2; i++) spare.Writer.TryWrite(new byte[frameBytes]);

    using var stop = new CancellationTokenSource();
    using var process = StartFfmpeg(a);
    var stderrTask = process.StandardError.ReadToEndAsync();
    var stream = process.StandardOutput.BaseStream;

    var reader = Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                var buffer = await spare.Reader.ReadAsync(stop.Token);
                var filled = 0;
                while (filled < frameBytes)
                {
                    var want = Math.Min(64 * 1024, frameBytes - filled);
                    var n = await stream.ReadAtLeastAsync(buffer.AsMemory(filled, want), want, throwOnEndOfStream: false);
                    if (n < want) break;
                    filled += n;
                }

                if (filled < frameBytes) break;
                await ready.Writer.WriteAsync(buffer, stop.Token);
            }
        }
        catch (Exception)
        {
        }

        ready.Writer.TryComplete();
    });

    var period = 1000.0 / fps;
    var lateness = new List<double>(Math.Min(maxFrames, 1 << 16));
    var misses = 0;
    var taken = 0;

    var first = await ready.Reader.ReadAsync();
    spare.Writer.TryWrite(first);
    var clock = Stopwatch.StartNew();

    while (taken < maxFrames)
    {
        var deadline = (taken + 1) * period;
        var wait = deadline - clock.Elapsed.TotalMilliseconds;
        if (wait > 1) await Task.Delay((int)wait);
        while (clock.Elapsed.TotalMilliseconds < deadline) { }

        if (!ready.Reader.TryRead(out var frame))
        {
            misses++;
            try { frame = await ready.Reader.ReadAsync(); }
            catch (ChannelClosedException) { break; }
        }

        var late = clock.Elapsed.TotalMilliseconds - deadline;
        lateness.Add(late);
        spare.Writer.TryWrite(frame);
        taken++;
    }

    clock.Stop();
    stop.Cancel();
    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    spare.Writer.TryComplete();
    var error = await stderrTask;
    await process.WaitForExitAsync();
    try { await reader; } catch { }

    var elapsed = clock.Elapsed.TotalSeconds;
    return (elapsed > 0 ? taken / elapsed : 0,
        taken > 0 ? misses * 100.0 / taken : double.NaN,
        Percentile(lateness, 99),
        lateness.Count > 0 ? lateness.Max() : double.NaN,
        taken > 1 ? "" : error.Trim());
}

static async Task PlayGateAsync(string left, string right, int fps, int frames, int runs)
{
    Console.WriteLine();
    Console.WriteLine("## P11 - Kapi olcumu: parcali okumada surdurulen fps ve aralik p99");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Boru kipi | Kosu | Ortalama fps | Sapma | Tek tek fps | p50 ms | p95 ms | Ortalama p99 ms | En kotu p99 ms | En kotu max ms | 25 ms ustu kare % | MB/s | CPU % |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

    foreach (var (w, h) in PanelSizes())
    {
        foreach (var realtime in new[] { false, true })
        {
            var fpsValues = new List<double>();
            var p50 = new List<double>();
            var p95 = new List<double>();
            var p99 = new List<double>();
            var maxMs = new List<double>();
            var late = new List<double>();
            var mbps = new List<double>();
            var cpus = new List<double>();
            var error = "";

            for (var i = 0; i < runs; i++)
            {
                var s = await PipeAsync(left, right, w, h, fps, frames, realtime: realtime);
                if (s.Error.Length > 0) error = s.Error;
                fpsValues.Add(s.Fps);
                p50.Add(s.P50);
                p95.Add(s.P95);
                p99.Add(s.P99);
                maxMs.Add(s.MaxMs);
                late.Add(s.LatePercent);
                mbps.Add(s.MBps);
                cpus.Add(s.CpuPercent);
            }

            var (mean, sd) = MeanSd(fpsValues);
            var each = string.Join(" / ", fpsValues.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)));
            var mode = realtime ? $"**-re {fps} fps**" : "azami hiz";
            Console.WriteLine(
                $"| 2x{w}x{h} | {mode} | {runs} | {mean:0.#} | {sd:0.##} | {each} | {p50.Average():0.##} | " +
                $"{p95.Average():0.##} | {p99.Average():0.##} | {p99.Max():0.##} | {maxMs.Max():0.#} | " +
                $"{late.Average():0.##} | {mbps.Average():0.#} | {cpus.Average():0.#} |" +
                (error.Length > 0 ? $" <!-- {error} -->" : ""));
        }
    }
}

static async Task PlayQueueAsync(string left, string right, int fps, int frames, int runs)
{
    Console.WriteLine();
    Console.WriteLine("## P12 - Kuyruklu tuketici: 60 fps son tarihinde kare hazir mi");
    Console.WriteLine();
    Console.WriteLine("| Boyut | Kuyruk derinligi | Kosu | Teslim fps | Son tarihi kaciran kare % | Gecikme p99 ms | En kotu gecikme ms |");
    Console.WriteLine("|---|---|---|---|---|---|---|");

    foreach (var (w, h) in PanelSizes())
    {
        foreach (var depth in new[] { 1, 3, 8 })
        {
            var fpsValues = new List<double>();
            var miss = new List<double>();
            var p99 = new List<double>();
            var worst = new List<double>();
            var error = "";

            for (var i = 0; i < runs; i++)
            {
                var r = await PipeQueuedAsync(left, right, w, h, fps, frames, depth);
                if (r.Error.Length > 0) error = r.Error;
                fpsValues.Add(r.Fps);
                miss.Add(r.MissPercent);
                p99.Add(r.LateP99);
                worst.Add(r.LateMax);
            }

            Console.WriteLine(
                $"| 2x{w}x{h} | {depth} | {runs} | {fpsValues.Average():0.#} | {miss.Average():0.##} | " +
                $"{p99.Max():0.##} | {worst.Max():0.#} |" + (error.Length > 0 ? $" <!-- {error} -->" : ""));
        }
    }
}

static async Task PlaySharedAsync(string left, string right, int fps, int frames, int runs)
{
    Console.WriteLine();
    Console.WriteLine("## P9 - Paylasimli bellek boruyu geciyor mu");
    Console.WriteLine();

    foreach (var (w, h) in new[] { (1920, 1080), (960, 540) })
    {
        Console.WriteLine($"### 2x{w}x{h}");
        Console.WriteLine();
        RunHeader("Yol");

        await RepeatAsync("boru, tek is parcacigi (P1 taban)", runs,
            async () =>
            {
                var s = await PipeAsync(left, right, w, h, fps, frames);
                return (s.Fps, s.CpuPercent, s.Error);
            });

        foreach (var slots in new[] { 2, 4, 8 })
        {
            await RepeatAsync($"boru -> MMF halka, {slots} yuva", runs,
                () => PipeSharedAsync(left, right, w, h, fps, frames, slots));
        }

        Console.WriteLine();
    }
}

sealed record PipeStats(
    int Frames,
    double StartupMs,
    double Seconds,
    double Fps,
    double P50,
    double P95,
    double P99,
    double MaxMs,
    double LatePercent,
    double MBps,
    double CpuPercent,
    double BytesPerFrame,
    string Error);

sealed record BenchResult(
    string Source,
    string FillPolicy,
    double TargetMb,
    double ActualMb,
    double FillPercent,
    double BandLowerMb,
    double HardFloorMb,
    bool InBand,
    bool OverTarget,
    bool BelowHardFloor,
    int Width,
    int Height,
    double Fps,
    string Codec,
    string Mode,
    string CrfOrBitrate,
    int Attempts,
    double EncodeSeconds,
    double PlanSeconds,
    double ProbeSeconds,
    double MeasureSeconds,
    bool Calibrated,
    double? VmafNegMean,
    double? VmafNegHarmonic,
    double? VmafNegP10,
    double? VmafNegMin,
    int VmafNegFloorFrames,
    double? Xpsnr,
    double? XpsnrY,
    double? XpsnrU,
    double? XpsnrV,
    bool Saturated = false,
    bool CeilingExceeded = false);

public sealed record VmafPool(
    int Count,
    double? Mean,
    double? Harmonic,
    double? P10,
    double? Min,
    int FloorClampedFrames)
{
    public static readonly VmafPool Empty = new(0, null, null, null, null, 0);

    public bool Suspect => FloorClampedFrames > 0;
}

public static class VmafPooling
{
    public const double HarmonicFloor = 1.0;

    public static VmafPool Pool(IReadOnlyList<double> scores)
    {
        if (scores is null) throw new ArgumentNullException(nameof(scores));
        if (scores.Count == 0) return VmafPool.Empty;

        var sum = 0.0;
        var reciprocalSum = 0.0;
        var min = double.PositiveInfinity;
        var clamped = 0;
        foreach (var raw in scores)
        {
            if (double.IsNaN(raw)) throw new ArgumentException("VMAF dizisi NaN iceriyor.", nameof(scores));
            sum += raw;
            if (raw < min) min = raw;
            if (raw < HarmonicFloor) clamped++;
            reciprocalSum += 1.0 / Math.Max(raw, HarmonicFloor);
        }

        var sorted = scores.OrderBy(x => x).ToArray();
        var rank = 0.10 * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        var p10 = lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);

        return new VmafPool(
            scores.Count,
            sum / scores.Count,
            scores.Count / reciprocalSum,
            p10,
            min,
            clamped);
    }
}

public static class BenchMeasureFilterGraph
{
    public static string Build(int width, int height, string filterChain, string? testFps = null)
    {
        if (string.IsNullOrWhiteSpace(filterChain))
            throw new ArgumentException("Karsilastirma filtresi bos olamaz.", nameof(filterChain));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Olcum cozunurlugu pozitif olmali.");

        var testChain = $"scale=w={width}:h={height}:flags=lanczos";
        if (!string.IsNullOrWhiteSpace(testFps)) testChain = $"fps={testFps},{testChain}";
        return MeasureFilterGraph.Build(testChain, "null", filterChain);
    }
}

public static class BenchOlcum
{
    public const int Basarili = 0;
    public const int Basarisiz = 2;

    private static readonly JsonSerializerOptions Bicim = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static async Task<int> YazAsync(Func<Task<QualityScore>> olcum, TextWriter cikti, TextWriter hata)
    {
        try
        {
            cikti.WriteLine(JsonSerializer.Serialize(await olcum(), Bicim));
            return Basarili;
        }
        catch (QualityMeasurementFailedException basarisizlik)
        {
            if (basarisizlik.PartialScore is { } kismi)
                cikti.WriteLine(JsonSerializer.Serialize(kismi, Bicim));
            hata.WriteLine(basarisizlik.Message);
            return Basarisiz;
        }
    }
}
