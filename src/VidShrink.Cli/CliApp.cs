using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.Cli;

public sealed class CliServices
{
    public static CliServices Default { get; } = new();

    public Func<string?> MissingTool { get; init; } = () => ToolLocator.IsAvailable(out var missing) ? null : missing;
    public Func<string, CancellationToken, Task<MediaInfo>> Probe { get; init; } = FfprobeClient.ProbeAsync;
    public Func<IEncoderAvailability?> Availability { get; init; } = () => EncoderCapabilities.Instance;
    public Func<MediaInfo, CancellationToken, Task<CropDetection>> DetectCrop { get; init; } = CropProbe.RunAsync;

    /// <summary>
    /// ffmpeg derlemesinde bu suzgec var mi. Yoklama okunamadiysa "var" sayilir: bilinmeyen,
    /// yok demek degildir ve kodlama kendi hatasini verir.
    /// </summary>
    public Func<string, bool> HasFilter { get; init; } = name => !EncoderCapabilities.Instance.Loaded || EncoderCapabilities.Instance.HasFilter(name);

    /// <summary>UTF-8 olmayan yakma altyazisinin BOM'suz tek baytli halinde varsayilan kod sayfasi.</summary>
    public Func<int> AnsiCodePage { get; init; } = () => SubtitleCharset.SystemAnsiCodePage;

    /// <summary>UTF-8'e cevrilen yakma altyazisinin yazildigi klasor; dosya is bitince silinir.</summary>
    public Func<string> BurnScratchFolder { get; init; } = Path.GetTempPath;

    /// <summary><c>--altyazi-tara</c>'nin paket sayimi; yalniz kaynakta zorunlu bayrakli altyazi yokken cagrilir.</summary>
    public Func<MediaInfo, CancellationToken, Task<IReadOnlyDictionary<int, int>?>> CountSubtitlePackets { get; init; }
        = (info, ct) => FfprobeClient.CountSubtitlePacketsAsync(info.FilePath, ct);
    public Func<IReadOnlyList<PresetProfile>> UserPresets { get; init; } = () =>
    {
        try { return PresetLibrary.LoadUser(PresetLibrary.DefaultUserPath); }
        catch (Exception ex) when (ex is PresetFileException or IOException or UnauthorizedAccessException) { return Array.Empty<PresetProfile>(); }
    };

    /// <summary><c>donustur</c>'un kodlaması; panelin çağırdığı <see cref="EncodeRunner.ConvertAsync"/>.</summary>
    public Func<MediaInfo, ConversionPlan, string, IProgress<EncodeProgress>, CancellationToken, Task<ConversionResult>> Convert { get; init; }
        = (info, plan, output, progress, ct) => new EncodeRunner().ConvertAsync(info, plan, output, progress, ct);

    public IWatchFileSystem WatchFileSystem { get; init; } = PhysicalWatchFileSystem.Instance;
    public IWatchClock WatchClock { get; init; } = SystemWatchClock.Instance;
    public Func<string?> WatchStateFallbackDirectory { get; init; } = () => Path.GetDirectoryName(UpdateSettings.DefaultPath) is { } settings ? Path.Combine(settings, "izle") : null;

    /// <summary><c>kaydet</c>'in okuduğu kaydedici ayar dosyası; pencerenin yazdığı yer.</summary>
    public Func<string?> RecorderSettingsPath { get; init; } = () => CliRecordSettings.DefaultPath;

    /// <summary><c>kaydet</c>'in makinesi: ffmpeg'in listelediği cihazlar ve çalıştığı ölçülmüş donanım kodlayıcıları.</summary>
    public Func<CliRecordMachine> RecordMachine { get; init; } = () =>
    {
        var devices = CaptureDevices.Instance;
        return new CliRecordMachine(CliRecordMachine.HostPlatform, devices.Audio, devices.Video,
            RecorderAutoProbe.WorkingHardwareEncoders(EncoderCapabilities.Instance), Environment.ProcessorCount);
    };

    /// <summary><c>kaydet</c>'in kaydı; varsayılanı <see cref="CliRecord.RecordAsync"/>.</summary>
    public Func<RecorderRequest, string, CancellationToken, Task<RecordResult>> Record { get; init; } = CliRecord.RecordAsync;

    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    /// <summary><c>denetim</c>'in istemcisi; varsayılanı açık pencerenin tek örnek borusu.</summary>
    public Func<ControlCommand, ControlReply> Control { get; init; } = CliControl.DefaultSend;
    /// <summary><c>teslim</c>: proje kaynaklarinin kimligi; testte sahte kaynakla degisir.</summary>
    public Func<string, VidShrink.Core.Editing.SourceStamp?> Stamp { get; init; } = VidShrink.Core.Editing.SourceStamp.Of;

    public Func<VidShrink.Core.Editing.EditProjectJob, string, bool, CancellationToken, Task<VidShrink.Core.Editing.ExportPlan>> EditPrepare { get; init; }
        = (job, output, dropMetadata, ct) => EditExportRunner.PrepareAsync(job, output, dropMetadata, ct);

    public Func<VidShrink.Core.Editing.ExportPlan, IProgress<EncodeProgress>, CancellationToken, Task> EditRun { get; init; } = EditExportRunner.RunAsync;

    /// <summary><c>--olcerek-ara</c>'nin olceri hazir mi; ffmpeg'de libvmaf yoksa arama kosmaz, modelin hedefi kalir.</summary>
    public Func<bool> MeterAvailable { get; init; } = () => QualityMeasurement.Instance.IsAvailable;

    /// <summary><c>--olcerek-ara</c>'nin aramasi; varsayilani pencerenin kosturdugu <see cref="QualitySearchSampler.RunAsync"/>.</summary>
    public Func<CliDecision, double, IEncoderAvailability?, IProgress<QualitySearchRound>, CancellationToken, Task<QualitySearchResult>> QualitySearch { get; init; }
        = CliApp.DefaultQualitySearch;
}

public sealed record CliDecision(
    MediaInfo Info,
    PlanOptions Options,
    PlanResult Result,
    ComplexityProfile Profile,
    SceneMapAttempt? Scenes,
    QualityTargetResult? QualityTarget,
    string OutputPath,
    IReadOnlyList<string> Arguments)
{
    public double TargetMb => Options.TargetMb;
    public EncodePlan Plan => Result.Plan;

    /// <summary><c>--olcerek-ara</c> kostuysa aramanin sonucu; hedef ondan gelir, olcum dustuyse modelin hedefi kalir.</summary>
    public QualitySearchResult? Search { get; init; }
}

public static class CliApp
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr,
        CliText text, CliServices services, CancellationToken ct)
    {
        var selection = CliText.SplitLanguage(args);
        if (selection.Invalid is { } badLanguage)
        {
            stderr.WriteLine(text.Format("error.bad-language", badLanguage, string.Join(", ", CliText.Languages)));
            stderr.WriteLine(text["usage.hint"]);
            return ExitCodes.Usage;
        }

        if (selection.Language is { } chosen) text = CliText.ForLanguage(chosen);
        args = selection.Rest;

        if (CliControl.Matches(args)) return CliControl.Run(args, stdout, stderr, text, services.Control);

        var parsed = CliParser.Parse(args);
        if (!parsed.Ok)
        {
            stderr.WriteLine(text.Format(parsed.ErrorKey!, parsed.ErrorArgument));
            stderr.WriteLine(text["usage.hint"]);
            return ExitCodes.Usage;
        }

        var request = parsed.Request! with { PreferredLanguage = text.Language };
        switch (request.Command)
        {
            case CliCommand.Help:
                stdout.WriteLine(text.Format("help", CliText.Version));
                return ExitCodes.InBand;
            case CliCommand.Version:
                stdout.WriteLine(CliText.Version);
                return ExitCodes.InBand;
            case CliCommand.Profiller when request.ExportPath is { } disaAktarilan:
                return ProfilDisaAktar(request, disaAktarilan, services, stdout, stderr, text);
            case CliCommand.Profiller:
                stdout.Write(ProfilListesi(services, text));
                return ExitCodes.InBand;
            case CliCommand.Completion:
                stdout.Write(CliCompletion.Script(request.Shell!));
                return ExitCodes.InBand;
            case CliCommand.Gunluk:
            {
                var gunluk = Gunluk.Oku();
                if (gunluk.Length == 0)
                {
                    stdout.WriteLine(text["gunluk.bos"]);
                    return ExitCodes.InBand;
                }
                stdout.Write(gunluk);
                return ExitCodes.InBand;
            }
        }

        IReadOnlyList<PresetProfile> dosyadan = Array.Empty<PresetProfile>();
        if (request.PresetFile is { } profilDosyasi)
        {
            if (ProfilDosyasi(profilDosyasi, stderr, text) is not { } okunan) return ExitCodes.Error;
            dosyadan = okunan;
            if (request.ProfileId is null)
            {
                if (dosyadan.Count != 1)
                {
                    stderr.WriteLine(text.Format("error.preset-file-choose", string.Join(", ", dosyadan.Select(p => p.Name ?? p.Id))));
                    stderr.WriteLine(text["usage.hint"]);
                    return ExitCodes.Usage;
                }
                request = request with { ProfileId = dosyadan[0].Id };
            }
        }

        if (request.ProfileId is { } istenenProfil)
        {
            var bulunan = dosyadan.FirstOrDefault(p => string.Equals(p.Id, istenenProfil, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(p.Name, istenenProfil, StringComparison.OrdinalIgnoreCase))
                ?? PresetLibrary.BuiltIn.Find(istenenProfil)
                ?? services.UserPresets().FirstOrDefault(p => string.Equals(p.Id, istenenProfil, StringComparison.OrdinalIgnoreCase));
            if (bulunan is null)
            {
                stderr.WriteLine(text.Format("error.bad-profile", istenenProfil));
                stderr.WriteLine(text["usage.hint"]);
                return ExitCodes.Usage;
            }

            if (request.TargetMb is null && request.Quality is null && bulunan.TargetMb is null)
            {
                stderr.WriteLine(text.Format("error.profile-no-target", istenenProfil));
                stderr.WriteLine(text["usage.hint"]);
                return ExitCodes.Usage;
            }

            request = request with { Profile = bulunan, TargetMb = request.TargetMb ?? (request.Quality is null ? bulunan.TargetMb : null) };
        }

        try
        {
            if (services.MissingTool() is { } missing)
            {
                stderr.WriteLine(text.Format("error.tool-missing", missing));
                return ExitCodes.Error;
            }

            if (request.Command == CliCommand.Watch)
                return await WatchAsync(request, services, stdout, stderr, text, ct);
            if (request.Command == CliCommand.Convert)
                return await CliConvert.RunAsync(request, services, stdout, stderr, text, ct);
            if (request.Command == CliCommand.Record)
                return await CliRecord.RunAsync(request, services, stdout, stderr, text, ct);
            if (request.Command == CliCommand.Edit)
                return await CliEdit.RunAsync(request, services, stdout, stderr, text, ct);

            return (await ProcessFileAsync(request, services, stdout, stderr, text, ct)).ExitCode;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine(text["result.cancelled"]);
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            stderr.WriteLine(text.Format("error.failed", ex.Message));
            return ExitCodes.Error;
        }
    }

    private sealed record FileRun(int ExitCode, string? Output, string? Error);

    private static async Task<FileRun> ProcessFileAsync(CliRequest request, CliServices services, TextWriter stdout,
        TextWriter stderr, CliText text, CancellationToken ct)
    {
        var scratch = new List<string>();
        try
        {
            return await ProcessFileAsync(request, services, stdout, stderr, text, scratch, ct);
        }
        finally
        {
            foreach (var file in scratch)
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Yakilacak dis altyazi UTF-8 degilse UTF-8 kopyasini yazar ve istegi ona cevirir; kopya
    /// <paramref name="scratch"/> listesine girer. Kodlama cikarilamazsa hata anahtari doner.
    /// </summary>
    public static string? PrepareBurnFile(ref CliRequest request, CliServices services, List<string> scratch, out string? encoding)
    {
        encoding = null;
        if (request.BurnFile is not { } file) return null;
        var read = SubtitleCharset.Read(File.ReadAllBytes(file), services.AnsiCodePage());
        if (read.Kind == SubtitleCharsetKind.Utf8) return null;
        if (read.Kind == SubtitleCharsetKind.Unknown || read.Kind != SubtitleCharsetKind.Converted) return BurnEncodingError;
        var folder = services.BurnScratchFolder();
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, "vidshrink_altyazi_" + Guid.NewGuid().ToString("N") + Path.GetExtension(file).ToLowerInvariant());
        scratch.Add(copy);
        File.WriteAllText(copy, read.Text, new UTF8Encoding(false));
        request = request with { BurnFile = copy };
        encoding = read.Name;
        return null;
    }

    internal const string BurnEncodingError = "error.burn-file-encoding";
    internal const string BurnEncodingNote = "result.burn-charset";

    private static async Task<FileRun> ProcessFileAsync(CliRequest request, CliServices services, TextWriter stdout,
        TextWriter stderr, CliText text, List<string> scratch, CancellationToken ct)
    {
        var input = Path.GetFullPath(request.Input!);
        if (!File.Exists(input))
        {
            stderr.WriteLine(text.Format("error.input-missing", input));
            return new FileRun(ExitCodes.Error, null, text.Format("error.input-missing", input));
        }

        CliProgress.Stage(stderr, text, "progress.probe", request.Json);
        var info = await services.Probe(input, ct);

        if (request.Scan)
        {
            stdout.Write(request.Json ? TaramaJson(info, request) : TaramaMetni(info, request, text));
            return new FileRun(ExitCodes.InBand, null, null);
        }

        var baslikSoruluyor = request.Title is not null || request.MainFeature || request.MinDurationSeconds is not null
            || request.MaxDurationSeconds is not null;
        if (info.Titles.Count > 0 || baslikSoruluyor)
        {
            if (SourceTitles.Sec(info.Titles, request.Title, request.MainFeature,
                    request.MinDurationSeconds, out var baslik, request.MaxDurationSeconds) is { } baslikHatasi)
            {
                var ileti = text.Format(baslikHatasi, request.Title?.ToString(CultureInfo.InvariantCulture));
                stderr.WriteLine(ileti);
                return new FileRun(ExitCodes.Usage, null, ileti);
            }
            if (baslik is not null) info = SourceTitles.Uygula(info, baslik);
        }

        if (request.Resolved(info, out request) is { } trimError)
        {
            var trimMessage = text.Format(trimError, request.ChapterTo is int son && son != request.ChapterFrom
                ? $"{request.ChapterFrom}-{son}"
                : request.ChapterFrom?.ToString());
            stderr.WriteLine(trimMessage);
            return new FileRun(ExitCodes.Usage, null, trimMessage);
        }
        if (request.ResolvedSubtitles(info, out request, out var altyaziArgumani) is { } altyaziHatasi)
        {
            var altyaziIletisi = text.Format(altyaziHatasi, altyaziArgumani);
            stderr.WriteLine(altyaziIletisi);
            return new FileRun(ExitCodes.Usage, null, altyaziIletisi);
        }
        if (request.SubtitleScan)
        {
            var bulunan = ForeignAudioSearch.Flagged(info, request.PreferredLanguage);
            if (bulunan is null && ForeignAudioSearch.Decide(info, null, request.PreferredLanguage).Outcome == ForeignAudioOutcome.NotMeasured)
            {
                CliProgress.Stage(stderr, text, "progress.subtitle-scan", request.Json);
                bulunan = ForeignAudioSearch.Decide(info, await services.CountSubtitlePackets(info, ct), request.PreferredLanguage);
            }
            bulunan ??= new ForeignAudioPick(ForeignAudioOutcome.NoSubtitles);
            stderr.WriteLine(text.Format("result.subtitle-scan." + bulunan.Slug, bulunan.Number, bulunan.Packets, bulunan.FullestPackets));
            if (bulunan.Number is int taranan) request = request with { BurnSubtitle = taranan };
        }
        var resimYakma = request.BurnSubtitle is int yakilan
            && VideoFilterChain.ImageBurnStream(info, new VideoFilterOptions { BurnSubtitle = yakilan - 1 }) is not null;
        if ((request.BurnFile is not null || request.BurnSubtitle is not null) && !resimYakma && !services.HasFilter(VideoFilterChain.BurnFilterName))
        {
            var yakmaIletisi = text["error.no-libass"];
            stderr.WriteLine(yakmaIletisi);
            return new FileRun(ExitCodes.Error, null, yakmaIletisi);
        }
        if (request.AudioDenoise && !services.HasFilter(AudioCaptureArguments.NoiseSuppressionFilterName))
        {
            var temizlemeIletisi = text["error.no-afftdn"];
            stderr.WriteLine(temizlemeIletisi);
            return new FileRun(ExitCodes.Error, null, temizlemeIletisi);
        }
        var yakilanDosya = request.BurnFile;
        if (PrepareBurnFile(ref request, services, scratch, out var okunanKodlama) is { } kodlamaHatasi)
        {
            var kodlamaIletisi = text.Format(kodlamaHatasi, yakilanDosya);
            stderr.WriteLine(kodlamaIletisi);
            return new FileRun(ExitCodes.Usage, null, kodlamaIletisi);
        }
        if (okunanKodlama is not null) stderr.WriteLine(text.Format(BurnEncodingNote, okunanKodlama));
        if (request.ProbesCrop && request.Filters?.Crop is null)
        {
            CliProgress.Stage(stderr, text, "progress.crop", request.Json);
            var detection = await services.DetectCrop(info, ct);
            var conservative = request.CropMode == CropMode.Conservative;
            var found = conservative
                ? CropProbe.DecideConservative(detection.Samples, info.Width, info.Height)
                : detection.Rect;
            if (found is { } rect)
            {
                request = request with { Filters = (request.Filters ?? new VideoFilterOptions()).WithCrop(rect) };
                stderr.WriteLine(text.Format(conservative ? "result.crop-conservative" : "result.crop", rect.ToString()));
            }
            else stderr.WriteLine(text[conservative && detection.Rect is not null ? "result.crop-conservative-none" : "result.crop-none"]);
        }

        var availability = services.Availability();
        var decision = request.SkipMeasurement
            ? Decide(request, info, null, null, availability)
            : await MeasureAndDecideAsync(request, info, availability, stderr, text, ct);
        if (request.SearchByMeasure && request.Quality is { } istenenKalite)
            decision = await SearchAsync(request, decision, istenenKalite, availability, services, stderr, text, ct);

        if (PathEquals(decision.OutputPath, info.FilePath))
        {
            stderr.WriteLine(text["error.same-output"]);
            return new FileRun(ExitCodes.Usage, null, text["error.same-output"]);
        }

        if (request.Output is { } istenen && !PathEquals(Path.GetFullPath(istenen), decision.OutputPath))
            stderr.WriteLine(text.Format("output.not-webm", decision.Plan.Codec, Path.GetExtension(decision.OutputPath)));

        if (decision.Plan.ModeEnum != EncodeMode.PassThrough
            && StreamMapping.ForOutput(decision.Info, decision.Plan, decision.OutputPath).RejectedSampleRate() is { } reddedilen)
        {
            var hizIletisi = text.Format("error.arate-codec", reddedilen.Codec, reddedilen.SampleRate,
                string.Join(", ", StreamMapping.SampleRatesFor(reddedilen.Codec)!));
            stderr.WriteLine(hizIletisi);
            return new FileRun(ExitCodes.Usage, null, hizIletisi);
        }

        if (request.Command == CliCommand.Plan)
        {
            stdout.Write(request.Json ? PlanJson(request, decision) : PlanText(request, decision, text));
            return new FileRun(ExitCodes.InBand, null, null);
        }

        return await ShrinkAsync(request, decision, stdout, stderr, text, ct);
    }

    /// <summary>
    /// <c>--tarama</c>'nin metni. Kodlama yok: envanter basilir ve is biter. Asgari sure
    /// verilmisse elenen basliklar hic yazilmaz — kullanici listede gordugu her numarayi
    /// <c>--baslik</c> ile verebilsin.
    /// </summary>
    private static string TaramaMetni(MediaInfo info, CliRequest request, CliText text)
    {
        var basliklar = SourceTitles.Ele(info.Titles, request.MinDurationSeconds, request.MaxDurationSeconds);
        var sb = new StringBuilder();
        sb.AppendLine(text.Format("scan.titles", basliklar.Count.ToString(CultureInfo.InvariantCulture)));
        foreach (var b in basliklar)
        {
            sb.Append("  ").Append(b.Number.ToString(CultureInfo.InvariantCulture)).Append("  ")
              .Append(Saat.Ekran(TimeSpan.FromSeconds(b.DurationSeconds), TimeSpan.FromHours(1)))
              .Append("  ").Append(b.Width.ToString(CultureInfo.InvariantCulture)).Append('x')
              .Append(b.Height.ToString(CultureInfo.InvariantCulture))
              .Append("  ").Append(text.Format("scan.streams", b.StreamCount.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(b.Label)) sb.Append("  ").Append(b.Label);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string TaramaJson(MediaInfo info, CliRequest request)
        => Json(!request.JsonLines, writer =>
        {
            writer.WriteString("command", "scan");
            writer.WriteStartArray("titles");
            foreach (var b in SourceTitles.Ele(info.Titles, request.MinDurationSeconds, request.MaxDurationSeconds))
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", b.Number);
                writer.WriteNumber("durationSeconds", Math.Round(b.DurationSeconds, 3));
                writer.WriteNumber("width", b.Width);
                writer.WriteNumber("height", b.Height);
                writer.WriteNumber("streams", b.StreamCount);
                writer.WriteNumber("chapters", b.ChapterCount);
                if (b.Label is { Length: > 0 }) writer.WriteString("label", b.Label);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });

    private static async Task<int> WatchAsync(CliRequest request, CliServices services, TextWriter stdout,
        TextWriter stderr, CliText text, CancellationToken ct)
    {
        var fs = services.WatchFileSystem;
        var clock = services.WatchClock;
        var watchDirectory = Path.GetFullPath(request.Input!);
        var outputDirectory = Path.GetFullPath(request.Output!);
        if (!fs.DirectoryExists(watchDirectory))
        {
            stderr.WriteLine(text.Format("error.watch-missing", watchDirectory));
            return ExitCodes.Error;
        }
        if (WatchFolder.ValidateFolders(watchDirectory, outputDirectory) is { } folderError)
        {
            stderr.WriteLine(text[folderError]);
            return ExitCodes.Usage;
        }
        fs.CreateDirectory(outputDirectory);

        var location = WatchFolder.OpenState(fs, watchDirectory, outputDirectory, services.WatchStateFallbackDirectory(), clock.UtcNow);
        var load = location.Load;
        if (load.CorruptBackup is { } backup) stderr.WriteLine(text.Format("watch.corrupt-state", backup));
        if (!location.Writable) stderr.WriteLine(text.Format("watch.state-not-saved", location.Path, "-"));
        else if (!string.Equals(location.Path, Path.Combine(watchDirectory, WatchFolder.StateFileName), WatchFolder.PathComparison))
            stderr.WriteLine(text.Format("watch.state-elsewhere", location.Path));

        var interval = TimeSpan.FromSeconds(request.PollSeconds ?? 2);
        var watcher = new WatchFolder(new WatchOptions
        {
            WatchDirectory = watchDirectory,
            OutputDirectory = outputDirectory,
            StatePath = location.Path,
            PollInterval = interval,
            StableFor = interval,
            Once = request.Once
        }, fs, clock, load.State);

        stderr.WriteLine(text.Format("watch.started", watchDirectory, outputDirectory));
        var result = await watcher.RunAsync(async (path, token) =>
        {
            var fileRequest = request with { Command = CliCommand.Shrink, Input = path, Output = null, OutputDirectory = outputDirectory, JsonLines = request.Json };
            var run = await ProcessFileAsync(fileRequest, services, stdout, stderr, text, token);
            return new WatchOutcome(run.ExitCode, run.ExitCode is ExitCodes.InBand or ExitCodes.UnderBand or ExitCodes.CeilingExceeded,
                run.Output, run.Error);
        }, e =>
        {
            var name = Path.GetFileName(e.Path);
            switch (e.Kind)
            {
                case WatchEventKind.Waiting: stderr.WriteLine(text.Format("watch.waiting", name)); break;
                case WatchEventKind.Processing: stderr.WriteLine(text.Format("watch.processing", name)); break;
                case WatchEventKind.Done: stderr.WriteLine(text.Format("watch.done", name, e.Detail is null ? "-" : Path.GetFileName(e.Detail))); break;
                case WatchEventKind.Failed: stderr.WriteLine(text.Format("watch.failed", name, e.Detail)); break;
                case WatchEventKind.Skipped: stderr.WriteLine(text.Format("watch.skipped", name)); break;
                case WatchEventKind.Collided: stderr.WriteLine(text.Format("watch.collided", name, e.Detail)); break;
                case WatchEventKind.Changed: stderr.WriteLine(text.Format("watch.changed", name)); break;
                case WatchEventKind.StateNotSaved: stderr.WriteLine(text.Format("watch.state-not-saved", e.Path, e.Detail)); break;
                case WatchEventKind.Stopped: stderr.WriteLine(text["watch.stopped"]); break;
            }
        }, ct);

        if (watcher.CollidedCount > 0) stderr.WriteLine(text.Format("watch.collided.summary", watcher.CollidedCount));

        return result switch
        {
            WatchRunResult.Finished => watcher.FailedCount > 0 || watcher.CollidedCount > 0 ? ExitCodes.WatchFailures : ExitCodes.InBand,
            WatchRunResult.Cancelled => ExitCodes.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }

    public static CliDecision Decide(CliRequest request, MediaInfo info, ComplexityProfile? profile,
        SceneMapAttempt? scenes, IEncoderAvailability? availability)
    {
        var baseTarget = request.TargetMb ?? new PlanOptions().TargetMb;
        var settled = ShrinkEngine.SettledProfile(info, request.ToPlanOptions(baseTarget, info.DurationSeconds), profile, availability);

        QualityTargetResult? qualityTarget = null;
        var target = baseTarget;
        if (request.Quality is { } quality)
        {
            qualityTarget = ShrinkEngine.TargetForQuality(info, request.ToPlanOptions(baseTarget, info.DurationSeconds), quality, settled, availability);
            var rounded = ShrinkEngine.RoundedTargetMb(qualityTarget);
            target = rounded > 0 ? rounded : qualityTarget.TargetMb;
        }

        return Finish(request, info, settled, scenes, qualityTarget, target, availability);
    }

    private static CliDecision Finish(CliRequest request, MediaInfo info, ComplexityProfile settled, SceneMapAttempt? scenes,
        QualityTargetResult? qualityTarget, double target, IEncoderAvailability? availability)
    {
        var options = request.ToPlanOptions(target, info.DurationSeconds);
        var result = ShrinkEngine.Decide(info, options, settled, availability);
        var extension = PresetLibrary.DeliveredExtension(request.Profile?.Container)
            ?? result.Plan.Streams?.Extension ?? "mp4";
        var output = request.Output is { } path ? WebmOrWhatCarries(Path.GetFullPath(path), result.Plan)
            : request.OutputDirectory is { } directory ? ShrinkEngine.UniqueOutputPath(Path.Combine(Path.GetFullPath(directory), Path.GetFileName(info.FilePath)), extension: extension)
            : ShrinkEngine.UniqueOutputPath(info.FilePath, extension: extension);
        var arguments = ShrinkEngine.DisplayedArguments(info, result.Plan, output, availability, scenes?.Map);
        return new CliDecision(info, options, result, settled, scenes, qualityTarget, output, arguments);
    }

    /// <summary>
    /// <c>--cikti x.webm</c> istendi ama planin kodegi WebM'e girmiyorsa (H.264, HEVC) dosya
    /// planin kendi kabinin uzantisiyla yazilir; webm adli bir mp4 ya da ffmpeg'in reddettigi bir
    /// komut teslim edilmez.
    /// </summary>
    public static string WebmOrWhatCarries(string output, EncodePlan plan) =>
        StreamMapping.ContainerOf(output) == OutputContainer.WebM && plan.Streams is { } streams && streams.Container != OutputContainer.WebM
            ? Path.ChangeExtension(output, streams.Extension)
            : output;

    public static async Task<CliDecision> MeasureAndDecideAsync(CliRequest request, MediaInfo info,
        IEncoderAvailability? availability, TextWriter stderr, CliText text, CancellationToken ct)
    {
        CliProgress.Stage(stderr, text, "progress.scenes", request.Json);
        var scenes = await EncodeRunner.TryBuildSceneMapAsync(info, ct: ct);
        var draft = Decide(request, info, null, scenes, availability);

        CliProgress.Stage(stderr, text, "progress.measure", request.Json);
        var profile = await ShrinkEngine.CalibrateAsync(info, scenes, draft.Options.SpeedMode,
            () => request.ToPlanOptions(draft.TargetMb, info.DurationSeconds), availability,
            (stage, _) =>
            {
                if (stage == ShrinkMeasureStage.Probed) CliProgress.Stage(stderr, text, "progress.calibrate", request.Json);
                return true;
            }, ct);

        return Decide(request, info, profile, scenes, availability);
    }

    /// <summary>
    /// <c>--olcerek-ara</c>: modelin kalite hedefinden baslayip kisa ornek kodlamalari olcerek en cok
    /// <see cref="MeasuredQualitySearch.MaxRounds"/> tur arar; olcum alinamazsa karar degismez.
    /// </summary>
    public static async Task<CliDecision> SearchAsync(CliRequest request, CliDecision decision, double quality,
        IEncoderAvailability? availability, CliServices services, TextWriter stderr, CliText text, CancellationToken ct)
    {
        if (!services.MeterAvailable())
        {
            stderr.WriteLine(text["search.no-meter"]);
            return decision;
        }

        CliProgress.Stage(stderr, text, "progress.search", request.Json);
        var result = await services.QualitySearch(decision, quality, availability, new CliProgress.Search(stderr, text, request.Json), ct);
        var searched = result.MeasuredQuality is null
            ? decision
            : Finish(request, decision.Info, decision.Profile, decision.Scenes, decision.QualityTarget, result.TargetMb, availability);
        return searched with { Search = result };
    }

    public static Task<QualitySearchResult> DefaultQualitySearch(CliDecision decision, double quality,
        IEncoderAvailability? availability, IProgress<QualitySearchRound> progress, CancellationToken ct)
        => QualitySearchSampler.RunAsync(decision.Info, decision.Options, quality, decision.TargetMb, decision.Profile, availability,
            ShrinkEngine.ProbeMeter(decision.Scenes?.Map), decision.Options.SpeedMode, decision.Scenes?.Map, progress, ct,
            decision.Options.EncoderThreads);

    internal static string? SearchLine(CliDecision decision, CliText text)
        => decision.Search is not { } search ? null
            : search.MeasuredQuality is { } measured
                ? text.Format("plan.search", Num(search.TargetMb, "0.##", text), search.SearchRounds.Count, Num(measured, "0.0", text), Num(search.RequestedQuality, "0.#", text))
                : text["plan.search-failed"];

    public static int ExitCodeFor(EncodeResult result)
        => result.CeilingExceeded ? ExitCodes.CeilingExceeded
            : !result.Success ? ExitCodes.Error
            : result.UnderBand ? ExitCodes.UnderBand
            : ExitCodes.InBand;

    private static async Task<FileRun> ShrinkAsync(CliRequest request, CliDecision decision, TextWriter stdout,
        TextWriter stderr, CliText text, CancellationToken ct)
    {
        var targetMb = decision.TargetMb;
        if (DiskSpaceGuard.TryGetFreeBytes(decision.OutputPath, out var freeBytes) && !DiskSpaceGuard.HasEnoughSpace(freeBytes, targetMb))
        {
            stderr.WriteLine(text.Format("error.no-space", Num(Megabayt.Oku(DiskSpaceGuard.RequiredBytes(targetMb)), "0", text)));
            return new FileRun(ExitCodes.Error, null, text.Format("error.no-space", Num(Megabayt.Oku(DiskSpaceGuard.RequiredBytes(targetMb)), "0", text)));
        }

        var clock = Stopwatch.StartNew();
        var result = await ShrinkEngine.EncodeAsync(decision.Info, decision.Plan, decision.OutputPath, targetMb,
            new CliProgress.Encode(stderr, text, request.Json), ct, decision.Options.FillPolicy, decision.Profile, null, decision.Scenes?.Map);
        clock.Stop();

        QualityScore? vmaf = null;
        if (request.MeasureVmaf && result.Success && File.Exists(result.OutputPath) && QualityMeasurement.Instance.IsAvailable)
        {
            try { vmaf = await MeasureVmafAsync(decision.Info.FilePath, result.OutputPath, decision.Scenes?.Map, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { stderr.WriteLine(text.Format("error.failed", ex.Message)); }
        }

        var exit = ExitCodeFor(result);
        stdout.Write(request.Json
            ? ShrinkJson(request, decision, result, clock.Elapsed, vmaf, exit)
            : ShrinkText(decision, result, clock.Elapsed, vmaf, text));
        if (!result.Success && !result.CeilingExceeded && result.Error is { } error)
            stderr.WriteLine(text.Format("error.failed", error));
        return new FileRun(exit, result.Success ? result.OutputPath : null, result.Success ? null : result.Error);
    }

    public static Task<QualityScore> MeasureVmafAsync(string source, string output, SceneMap? scenes, CancellationToken ct)
        => QualityMeter.MeasureAsync(source, output, scenes, ct);

    public static string VmafLine(QualityScore? vmaf, CliText text)
        => vmaf?.VmafNegMean is { } score
            ? text.Format("result.vmaf", Num(score, "0.0", text))
            : text["result.vmaf-none"];

    public static string PlanText(CliRequest request, CliDecision decision, CliText text)
    {
        var info = decision.Info;
        var plan = decision.Plan;
        var estimate = decision.Result.Estimate;
        var builder = new StringBuilder();
        builder.AppendLine(text.Format("plan.input", info.FilePath));
        builder.AppendLine(text.Format("plan.source", info.Width, info.Height, Num(info.Fps, "0.##", text), Num(info.FileSizeMb, "0.0", text), Clock(TimeSpan.FromSeconds(info.DurationSeconds))));
        if (decision.QualityTarget is { } quality)
            builder.AppendLine(text.Format("plan.quality-target", Num(quality.RequestedQuality, "0.#", text), Num(decision.TargetMb, "0.#", text), quality.Bound));
        else
            builder.AppendLine(text.Format("plan.target", Num(decision.TargetMb, "0.##", text)));
        if (SearchLine(decision, text) is { } arama) builder.AppendLine(arama);
        builder.AppendLine(text.Format("plan.video", plan.Codec, plan.Mode, plan.Width, plan.Height, Num(plan.Fps, "0.##", text), plan.VideoBitrateK));
        foreach (var note in EncoderNotes(plan, text)) builder.AppendLine(note);
        if (FrameRateText(info, plan, text) is { } kare) builder.AppendLine(kare);
        if (plan.AudioCodec is not null)
            builder.AppendLine(text.Format("plan.audio", plan.AudioCodec, plan.AudioBitrateK));
        foreach (var note in AudioOptionNotes(decision, text)) builder.AppendLine(note);
        builder.AppendLine(text.Format("plan.estimate", Num(estimate.ExpectedMb, "0.0", text), Num(estimate.LowMb, "0.0", text), Num(estimate.HighMb, "0.0", text)));
        builder.AppendLine(text.Format("plan.quality", Num(decision.Result.PredictedQuality, "0.0", text), Basis(estimate.Measured, text)));
        if (plan.ReasonCodes.Count > 0)
            builder.AppendLine(text.Format("plan.reasons", string.Join(", ", plan.ReasonCodes.Select(note => note.Code))));
        if (plan.Streams is { } streams)
            foreach (var note in streams.Notes.Distinct())
                builder.AppendLine(text.Format("plan.stream-note", text["plan.stream." + StreamNotes.Slug(note)]));
        builder.AppendLine(text.Format("plan.output", plan.ModeEnum == EncodeMode.PassThrough
            ? EncodeRunner.PassThroughPath(info.FilePath, decision.OutputPath)
            : decision.OutputPath));
        if (PassThroughNote(decision, text) is { } kept) builder.AppendLine(kept);
        builder.AppendLine(text["plan.command"]);
        builder.AppendLine(FfmpegArguments.ToCommandLine(decision.Arguments));
        return builder.ToString();
    }

    /// <summary>Uygulanan kare hizi kipinin plan satiri; otomatik kipte ve kopyalanan planda yoktur.</summary>
    public static string? FrameRateText(MediaInfo info, EncodePlan plan, CliText text)
        => VideoFilterChain.FrameRateNote(info, plan) is { } note
            ? text.Format(note.Mode == FrameRateMode.Constant ? "plan.frame-rate.constant" : "plan.frame-rate.peak", Num(note.Fps, "0.##", text))
            : null;

    public static string PlanJson(CliRequest request, CliDecision decision)
        => Json(!request.JsonLines, writer =>
        {
            writer.WriteString("command", "plan");
            WriteDecision(writer, request, decision);
        });

    /// <summary>
    /// <c>--profil-dosyasi</c> okuması (<c>docs/plan.md</c> C1-8). VidShrink dosyası doğrudan,
    /// HandBrake dosyası çeviriyle geliyor; HandBrake'te ön ayar başına özet stderr'e yazılıyor,
    /// böylece <c>--json</c> çıktısı bozulmuyor. Kullanıcının kitaplığına yazılmıyor.
    /// </summary>
    internal static IReadOnlyList<PresetProfile>? ProfilDosyasi(string path, TextWriter stderr, CliText text)
    {
        try
        {
            try
            {
                return PresetLibrary.Import(path);
            }
            catch (PresetFileException ex) when (ex.Error == PresetFileError.HandBrakeFile)
            {
                var ceviriler = HandBrakePresetImport.TranslateFile(path);
                foreach (var ceviri in ceviriler)
                {
                    var alanlar = ceviri.Notes.Where(n => n.Reason != PresetNoteReason.Structural).ToList();
                    stderr.WriteLine(text.Format("preset-file.handbrake", ceviri.PresetName,
                        alanlar.Count(n => n.Outcome == PresetNoteOutcome.Carried),
                        alanlar.Count(n => n.Outcome == PresetNoteOutcome.Approximated),
                        alanlar.Count(n => n.Outcome == PresetNoteOutcome.Dropped)));
                    var yaklasik = alanlar.Where(n => n.Outcome == PresetNoteOutcome.Approximated).Select(n => n.Field).ToList();
                    if (yaklasik.Count > 0) stderr.WriteLine(text.Format("preset-file.approximated", string.Join(", ", yaklasik)));
                }
                return ceviriler.Select(c => c.Profile).ToList();
            }
        }
        catch (Exception ex) when (ex is PresetFileException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine(text.Format("error.preset-file", path, ex is PresetFileException preset ? text[preset.Error switch
            {
                PresetFileError.NotJson => "preset-file.reason.not-json",
                PresetFileError.SchemaTooNew => "preset-file.reason.too-new",
                PresetFileError.NotPresetFile or PresetFileError.SchemaMissing or PresetFileError.NoHandBrakePreset => "preset-file.reason.not-preset",
                _ => "preset-file.reason.invalid"
            }] : ex.Message));
            return null;
        }
    }

    /// <summary>
    /// <c>profiller</c> komutunun govdesi. Kutuphane <b>turune gore</b> bolumleniyor:
    /// <see cref="PresetKind"/>'in dort uyesi de burada okunuyor. Bos bolum hic yazilmiyor,
    /// boylece kullanici profili olmayan kosumda son baslik cikmiyor.
    /// </summary>
    public static string ProfilListesi(CliServices services, CliText text)
    {
        var builder = new StringBuilder();
        var kullanici = services.UserPresets();
        foreach (var (kind, anahtar) in new[]
                 {
                     (PresetKind.General, "presets.kind.general"),
                     (PresetKind.Platform, "presets.kind.platform"),
                     (PresetKind.Device, "presets.kind.device"),
                     (PresetKind.User, "presets.kind.user")
                 })
        {
            var profiller = kind == PresetKind.User
                ? kullanici
                : PresetLibrary.BuiltIn.OfKind(kind).ToList();
            if (profiller.Count == 0) continue;

            builder.AppendLine(text[anahtar]);
            foreach (var profil in profiller)
            {
                var ayrinti = new List<string>();
                if (profil.Name is { Length: > 0 } ad) ayrinti.Add(ad);
                if (profil.TargetMb is { } mb) ayrinti.Add(text.Format("presets.target", Num(mb, "0.##", text)));
                else ayrinti.Add(text["presets.no-target"]);
                if (profil.MaxShortEdge is { } kenar) ayrinti.Add(text.Format("presets.short-edge", kenar.ToString(text.Culture)));
                builder.AppendLine($"  {profil.Id,-26}{string.Join(", ", ayrinti)}");
            }

            builder.AppendLine();
        }

        builder.AppendLine(text["presets.hint"]);
        return builder.ToString();
    }

    /// <summary>
    /// <c>profiller --disa-aktar</c>: kullanıcı profillerini çekirdeğin <see cref="PresetLibrary.Export"/>'u ile yazar.
    /// Yerleşik profil aktarılmaz; <see cref="PresetLibrary.Import"/> yerleşik kimliği reddettiği için dosya geri okunamazdı.
    /// </summary>
    private static int ProfilDisaAktar(CliRequest request, string path, CliServices services, TextWriter stdout, TextWriter stderr, CliText text)
    {
        var kullanici = services.UserPresets();
        var secilen = new List<PresetProfile>();
        foreach (var kimlik in request.ExportIds)
        {
            var bulunan = kullanici.FirstOrDefault(p => string.Equals(p.Id, kimlik, StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(p.Name, kimlik, StringComparison.OrdinalIgnoreCase));
            if (bulunan is null)
            {
                stderr.WriteLine(text.Format(PresetLibrary.BuiltIn.Find(kimlik) is null ? "error.bad-profile" : "error.export-builtin", kimlik));
                stderr.WriteLine(text["usage.hint"]);
                return ExitCodes.Usage;
            }
            if (!secilen.Contains(bulunan)) secilen.Add(bulunan);
        }
        if (request.ExportIds.Count == 0) secilen.AddRange(kullanici);
        if (secilen.Count == 0)
        {
            stderr.WriteLine(text["error.export-empty"]);
            return ExitCodes.Error;
        }

        var hedef = Path.GetFullPath(path);
        try
        {
            PresetLibrary.Export(secilen, hedef);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            stderr.WriteLine(text.Format("error.export-failed", hedef, ex.Message));
            return ExitCodes.Error;
        }

        stdout.WriteLine(text.Format("presets.exported", secilen.Count, hedef));
        return ExitCodes.InBand;
    }

    public static string ShrinkText(CliDecision decision, EncodeResult result, TimeSpan elapsed, QualityScore? vmaf, CliText text)
    {
        var builder = new StringBuilder();
        builder.AppendLine(text.Format("result.output", result.OutputPath));
        builder.AppendLine(text.Format("result.size", Num(result.OutputMb, "0.0", text), Num(decision.TargetMb, "0.##", text), Num(decision.Info.FileSizeMb, "0.0", text)));
        builder.AppendLine(text.Format("result.duration", elapsed.TotalSeconds < 60 ? Num(elapsed.TotalSeconds, "0.0", text) + " s" : Clock(elapsed)));
        builder.AppendLine(result.DeliveredAttemptNumber == result.Attempts
            ? text.Format("result.attempts", result.Attempts)
            : text.Format("result.attempts-delivered", result.Attempts, result.DeliveredAttemptNumber));
        builder.AppendLine(text.Format("result.encoder", result.PlanUsed.Codec));
        foreach (var note in EncoderNotes(decision.Plan, text)) builder.AppendLine(note);
        foreach (var note in AudioOptionNotes(decision, text)) builder.AppendLine(note);
        if (result.Success && PassThroughNote(decision, text) is { } kept) builder.AppendLine(kept);
        builder.AppendLine(VmafLine(vmaf, text));
        builder.AppendLine(text.Format("result.predicted", Num(decision.Result.PredictedQuality, "0.0", text), Basis(decision.Result.Estimate.Measured, text)));
        if (SearchLine(decision, text) is { } arama) builder.AppendLine(arama);
        if (vmaf?.Alignment is { } alignment && alignment.Shifted)
            builder.AppendLine(text.Format("result.alignment", Num(alignment.ShiftMilliseconds, "0.###", text), Num(alignment.ShiftFrames, "0.###", text)));
        if (result.Success && result.UnderBand) builder.AppendLine(text["result.under-band"]);
        if (result.CeilingExceeded)
            builder.AppendLine(result.Success
                ? text.Format("result.ceiling-kept", result.Attempts, Num(result.OutputMb, "0.000", text))
                : text.Format("result.ceiling", result.Attempts));
        return builder.ToString();
    }

    /// <summary>
    /// Plandaki kodlayici yedegi insan diliyle: istenen kodlayici, yerine gecen ve nedeni.
    /// Gerekce satiri yalniz kod adini tasiyor; bu satir kullaniciya ne oldugunu soyler.
    /// </summary>
    internal static IEnumerable<string> EncoderNotes(EncodePlan plan, CliText text)
        => plan.ReasonCodes
            .Where(note => note.Code == ReasonCode.EncoderFallback)
            .Select(note => text.Format("note.line", text.Format(note.FallbackCause switch
            {
                EncoderFallbackCause.NotInBuild => "note.encoder-fallback-not-in-build",
                EncoderFallbackCause.NotMeasured => "note.encoder-fallback-not-measured",
                _ => "note.encoder-fallback-not-working"
            }, note.RequestedCodec, note.FallbackCodec)));

    /// <summary>
    /// <c>--ses-hizi</c> ve <c>--ses-drc</c> istenip de ciktida karsiligi olmayan her durum bir satir:
    /// ses izi yok, kaynak AC-3 degil, ya da aac dusuk hizda istenen bit hizini yazamadigi icin iz
    /// kelepcelendi. Kaynak zaten istenen hizdaysa not yok, cunku cikti istenen hizda.
    /// </summary>
    internal static IEnumerable<string> AudioOptionNotes(CliDecision decision, CliText text)
    {
        var options = decision.Options;
        if (options.AudioSampleRate is null && options.AudioDrcScale is null) yield break;
        var plan = decision.Plan;
        var copied = plan.ModeEnum == EncodeMode.PassThrough;
        var tracks = copied
            ? Array.Empty<AudioTrack>()
            : StreamMapping.ForOutput(decision.Info, plan, decision.OutputPath).Audio;
        var silent = copied ? !decision.Info.HasAudio : tracks.Count == 0;

        if (options.AudioSampleRate is int rate)
        {
            if (silent) yield return text.Format("note.line", text["note.arate-no-audio"]);
            else if (tracks.FirstOrDefault(track => !track.Copies && track.Codec == "aac" && track.BitrateK < plan.AudioBitrateK
                         && StreamMapping.AacCeilingK(rate, track.Channels ?? decision.Info.AudioChannels) == track.BitrateK) is { } capped)
                yield return text.Format("note.line", text.Format("note.arate-bitrate-capped", rate, capped.BitrateK));
        }

        if (options.AudioDrcScale is not null)
        {
            if (silent) yield return text.Format("note.line", text["note.drc-no-audio"]);
            else if (!tracks.Any(track => !track.Copies && track.DrcScale is not null))
                yield return text.Format("note.line", text.Format("note.drc-not-dolby", decision.Info.AudioCodec));
        }
    }

    /// <summary>
    /// Kopyalama kolu kaynagin kabinda kalir (<see cref="EncodeRunner.PassThroughPath"/>); istenen
    /// uzanti baskaysa bunu soyleyen not, degilse <c>null</c>.
    /// </summary>
    internal static string? PassThroughNote(CliDecision decision, CliText text)
    {
        if (decision.Plan.ModeEnum != EncodeMode.PassThrough) return null;
        var kept = EncodeRunner.PassThroughPath(decision.Info.FilePath, decision.OutputPath);
        if (kept == decision.OutputPath) return null;
        return text.Format("note.line", text.Format("note.passthrough-extension",
            Path.GetExtension(kept), Path.GetExtension(decision.OutputPath)));
    }

    public static string ShrinkJson(CliRequest request, CliDecision decision, EncodeResult result, TimeSpan elapsed,
        QualityScore? vmaf, int exit)
        => Json(!request.JsonLines, writer =>
        {
            writer.WriteString("command", "kucult");
            WriteDecision(writer, request, decision);
            writer.WriteStartObject("result");
            writer.WriteNumber("exitCode", exit);
            writer.WriteBoolean("success", result.Success);
            writer.WriteString("output", result.OutputPath);
            writer.WriteNumber("outputMb", Math.Round(result.OutputMb, 3));
            writer.WriteNumber("elapsedSeconds", Math.Round(elapsed.TotalSeconds, 2));
            writer.WriteNumber("attempts", result.Attempts);
            writer.WriteBoolean("underBand", result.UnderBand);
            writer.WriteBoolean("ceilingExceeded", result.CeilingExceeded);
            writer.WriteBoolean("overTarget", result.OverTarget);
            if (result.Error is null) writer.WriteNull("error"); else writer.WriteString("error", result.Error);
            if (vmaf?.VmafNegMean is { } score) writer.WriteNumber("vmaf", Math.Round(score, 2)); else writer.WriteNull("vmaf");
            if (vmaf?.Alignment is { } alignment && alignment.Shifted)
            {
                writer.WriteStartObject("alignment");
                writer.WriteNumber("shiftMs", Math.Round(alignment.ShiftMilliseconds, 3));
                writer.WriteNumber("shiftFrames", Math.Round(alignment.ShiftFrames, 3));
                writer.WriteEndObject();
            }
            else writer.WriteNull("alignment");
            writer.WriteStartArray("trace");
            foreach (var attempt in result.Trace ?? Array.Empty<EncodeAttempt>())
            {
                writer.WriteStartObject();
                writer.WriteString("label", $"deneme {attempt.Number}");
                writer.WriteNumber("number", attempt.Number);
                writer.WriteString("branch", attempt.Branch);
                writer.WriteNumber("aimMb", Math.Round(attempt.AimMb, 3));
                writer.WriteNumber("actualMb", Math.Round(attempt.ActualMb, 3));
                writer.WriteNumber("videoBitrateK", attempt.VideoBitrateK);
                writer.WriteString("mode", attempt.Mode);
                writer.WriteNumber("seconds", Math.Round(attempt.Seconds, 2));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("droppedOptions");
            foreach (var dropped in result.DroppedOptions ?? Array.Empty<string>()) writer.WriteStringValue(dropped);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static void WriteDecision(Utf8JsonWriter writer, CliRequest request, CliDecision decision)
    {
        var info = decision.Info;
        var plan = decision.Plan;
        var estimate = decision.Result.Estimate;
        writer.WriteString("input", info.FilePath);
        writer.WriteString("output", decision.OutputPath);
        writer.WriteBoolean("measured", !request.SkipMeasurement);
        writer.WriteNumber("targetMb", decision.TargetMb);
        if (request.Quality is { } quality) writer.WriteNumber("requestedQuality", quality); else writer.WriteNull("requestedQuality");
        if (decision.QualityTarget is { } bound) writer.WriteString("qualityBound", bound.Bound.ToString()); else writer.WriteNull("qualityBound");
        if (decision.Search is { } search)
        {
            writer.WriteStartObject("search");
            writer.WriteNumber("targetMb", Math.Round(search.TargetMb, 3));
            if (search.MeasuredQuality is { } measured) writer.WriteNumber("measuredQuality", Math.Round(measured, 2)); else writer.WriteNull("measuredQuality");
            writer.WriteBoolean("converged", search.Converged);
            writer.WriteStartArray("rounds");
            foreach (var round in search.SearchRounds)
            {
                writer.WriteStartObject();
                writer.WriteNumber("round", round.Round);
                writer.WriteNumber("targetMb", Math.Round(round.TargetMb, 3));
                if (round.MeasuredQuality is { } score) writer.WriteNumber("measuredQuality", Math.Round(score, 2)); else writer.WriteNull("measuredQuality");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else writer.WriteNull("search");
        writer.WriteStartObject("source");
        writer.WriteNumber("sizeMb", Math.Round(info.FileSizeMb, 3));
        writer.WriteNumber("durationSeconds", info.DurationSeconds);
        writer.WriteNumber("width", info.Width);
        writer.WriteNumber("height", info.Height);
        writer.WriteNumber("fps", info.Fps);
        writer.WriteString("videoCodec", info.VideoCodec);
        writer.WriteEndObject();
        writer.WriteStartObject("plan");
        writer.WriteString("codec", plan.Codec);
        writer.WriteString("mode", plan.Mode);
        writer.WriteNumber("videoBitrateK", plan.VideoBitrateK);
        if (plan.Crf is { } crf) writer.WriteNumber("crf", crf); else writer.WriteNull("crf");
        writer.WriteNumber("width", plan.Width);
        writer.WriteNumber("height", plan.Height);
        writer.WriteNumber("fps", plan.Fps);
        writer.WriteString("preset", plan.Preset);
        if (plan.AudioCodec is null) writer.WriteNull("audioCodec"); else writer.WriteString("audioCodec", plan.AudioCodec);
        writer.WriteNumber("audioBitrateK", plan.AudioBitrateK);
        writer.WriteStartArray("reasonCodes");
        foreach (var note in plan.ReasonCodes) writer.WriteStringValue(note.Code.ToString());
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteStartObject("estimate");
        writer.WriteNumber("expectedMb", Math.Round(estimate.ExpectedMb, 3));
        writer.WriteNumber("lowMb", Math.Round(estimate.LowMb, 3));
        writer.WriteNumber("highMb", Math.Round(estimate.HighMb, 3));
        writer.WriteNumber("predictedQuality", Math.Round(decision.Result.PredictedQuality, 2));
        writer.WriteString("basis", estimate.Measured ? "measured" : "estimated");
        writer.WriteEndObject();
        writer.WriteStartArray("arguments");
        foreach (var argument in decision.Arguments) writer.WriteStringValue(argument);
        writer.WriteEndArray();
        writer.WriteString("commandLine", FfmpegArguments.ToCommandLine(decision.Arguments));
    }

    private static string Json(bool indented, Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static string Basis(bool measured, CliText text) => text[measured ? "basis.measured" : "basis.estimated"];

    private static string Num(double value, string format, CliText text) => value.ToString(format, text.Culture);

    private static string Clock(TimeSpan span) => Saat.Ekran(span);

    private static bool PathEquals(string left, string right) => PathEquality.Same(left, right);
}
