using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using VidShrink.App;
using VidShrink.Cli;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.Tests;

public sealed class OlcerekAramaBaglantiTests
{
    private const double AramaMb = 7.5;

    private static MediaInfo Kaynak(string path) => new()
    {
        FilePath = path,
        FileSizeBytes = 180_000_000L,
        DurationSeconds = 120,
        Width = 1920,
        Height = 1080,
        Fps = 30,
        VideoCodec = "h264",
        TotalBitrateBps = 12_000_000,
        AudioCodec = "aac",
        AudioBitrateBps = 128_000,
        AudioChannels = 2,
        PixelFormat = "yuv420p"
    };

    private static QualitySearchResult Bulunan(double quality, double? measured = null)
        => new(AramaMb, measured ?? quality, quality,
            new[] { new QualitySearchRound(1, MeasuredQualitySearch.MaxRounds, 12, quality - 4), new QualitySearchRound(2, MeasuredQualitySearch.MaxRounds, AramaMb, measured ?? quality) },
            true);

    [Fact]
    public void TheFlagIsOffByDefaultAndNeedsAQualityTarget()
    {
        Assert.False(CliParser.Parse(new[] { "kucult", "a.mp4", "--kalite", "70" }).Request!.SearchByMeasure);
        Assert.True(CliParser.Parse(new[] { "kucult", "a.mp4", "--kalite", "70", "--olcerek-ara" }).Request!.SearchByMeasure);
        Assert.True(CliParser.Parse(new[] { "plan", "a.mp4", "--search-by-measure", "--quality", "70" }).Request!.SearchByMeasure);

        var hedefle = CliParser.Parse(new[] { "kucult", "a.mp4", "--hedef", "25MB", "--olcerek-ara" });
        Assert.False(hedefle.Ok);
        Assert.Equal("error.search-needs-quality", hedefle.ErrorKey);
    }

    private sealed record Kosu(int Exit, JsonElement Root, string Stderr, int Calls, double? StartMb, int? Threads, string Stdout);

    private static async Task<Kosu> PlanKos(bool json, bool meter, Func<double, QualitySearchResult>? sonuc, params string[] ekler)
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "olcerek-arama-cli", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "kaynak.mp4");
        File.WriteAllBytes(input, new byte[16]);
        try
        {
            var calls = 0;
            double? start = null;
            int? threads = null;
            var services = new CliServices
            {
                MissingTool = () => null,
                Probe = (_, _) => Task.FromResult(Kaynak(input)),
                Availability = () => null,
                MeterAvailable = () => meter,
                QualitySearch = (decision, quality, _, progress, _) =>
                {
                    calls++;
                    start = decision.TargetMb;
                    threads = decision.Options.EncoderThreads;
                    var result = (sonuc ?? throw new InvalidOperationException("search must not run"))(quality);
                    foreach (var round in result.SearchRounds)
                    {
                        progress.Report(round with { MeasuredQuality = null });
                        progress.Report(round);
                    }
                    return Task.FromResult(result);
                }
            };
            var args = new List<string> { "plan", input, "--kodek", "h264", "--olcumsuz" };
            if (json) args.Add("--json");
            args.AddRange(ekler);
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = await CliApp.RunAsync(args, stdout, stderr, CliText.ForLanguage("en"), services, CancellationToken.None);
            var root = json ? JsonDocument.Parse(stdout.ToString()).RootElement.Clone() : default;
            return new Kosu(exit, root, stderr.ToString(), calls, start, threads, stdout.ToString().Replace(dir, "<dir>"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task WithTheFlagTheSearchedTargetDrivesThePlanAndItsArguments()
    {
        var model = await PlanKos(true, true, null, "--kalite", "70");
        var aranan = await PlanKos(true, true, q => Bulunan(q), "--kalite", "70", "--olcerek-ara", "--is-parcacigi", "2");

        Assert.Equal(0, model.Exit);
        Assert.Equal(0, aranan.Exit);
        Assert.Equal(0, model.Calls);
        Assert.Equal(1, aranan.Calls);
        Assert.Equal(model.Root.GetProperty("targetMb").GetDouble(), aranan.StartMb);
        Assert.Equal(2, aranan.Threads);
        Assert.Equal(JsonValueKind.Null, model.Root.GetProperty("search").ValueKind);

        Assert.Equal(AramaMb, aranan.Root.GetProperty("targetMb").GetDouble());
        var search = aranan.Root.GetProperty("search");
        Assert.Equal(AramaMb, search.GetProperty("targetMb").GetDouble());
        Assert.Equal(70, search.GetProperty("measuredQuality").GetDouble());
        Assert.True(search.GetProperty("converged").GetBoolean());
        Assert.Equal(2, search.GetProperty("rounds").GetArrayLength());
        Assert.NotEqual(
            model.Root.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToList(),
            aranan.Root.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToList());

        var turlar = aranan.Stderr.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("\"type\":\"search\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, turlar.Count);
        Assert.Contains(CliProgress.StageLine("search"), aranan.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("\"type\":\"search\"", aranan.Root.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutTheMeterTheModelsTargetStaysAndTheReasonIsSaid()
    {
        var model = await PlanKos(true, true, null, "--kalite", "70");
        var olcersiz = await PlanKos(true, false, null, "--kalite", "70", "--olcerek-ara");

        Assert.Equal(0, olcersiz.Calls);
        Assert.Equal(model.Root.GetProperty("targetMb").GetDouble(), olcersiz.Root.GetProperty("targetMb").GetDouble());
        Assert.Equal(JsonValueKind.Null, olcersiz.Root.GetProperty("search").ValueKind);
        Assert.Contains(CliText.ForLanguage("en")["search.no-meter"], olcersiz.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedMeasurementKeepsTheModelsTargetAndTheTextSaysSo()
    {
        var text = CliText.ForLanguage("en");
        var model = await PlanKos(false, true, null, "--kalite", "70");
        var dusen = await PlanKos(false, true, q => new QualitySearchResult(30, null, q,
            new[] { new QualitySearchRound(1, MeasuredQualitySearch.MaxRounds, 30, null) }, false), "--kalite", "70", "--olcerek-ara");
        var bulan = await PlanKos(false, true, q => Bulunan(q, 70.4), "--kalite", "70", "--olcerek-ara");

        Assert.Equal(1, dusen.Calls);
        Assert.Equal(model.Stdout, dusen.Stdout.Replace(text["plan.search-failed"] + Environment.NewLine, ""));
        Assert.Contains(text["plan.search-failed"], dusen.Stdout, StringComparison.Ordinal);
        Assert.Contains(text.Format("plan.search", AramaMb.ToString("0.##", text.Culture), 2, "70.4", "70"), bulan.Stdout, StringComparison.Ordinal);
        Assert.Contains(text.Format("progress.search-round-done", 2, MeasuredQualitySearch.MaxRounds, "7.5", "70.4"), bulan.Stderr, StringComparison.Ordinal);
    }

    [FfmpegFact]
    public async Task OnARealClipTheSearchRunsAtMostFourRoundsWhenTheMeterIsThere()
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "olcerek-arama-cli", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = await KlipAsync(dir);
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = await CliApp.RunAsync(new[] { "plan", input, "--kalite", "60", "--kodek", "h264", "--olcumsuz", "--olcerek-ara", "--is-parcacigi", "2", "--json" },
                stdout, stderr, CliText.ForLanguage("en"), CliServices.Default, CancellationToken.None);

            Assert.True(exit == 0, stderr.ToString());
            using var json = JsonDocument.Parse(stdout.ToString());
            var search = json.RootElement.GetProperty("search");
            if (!QualityMeasurement.Instance.IsAvailable)
            {
                Assert.Equal(JsonValueKind.Null, search.ValueKind);
                Assert.Contains(CliText.ForLanguage("en")["search.no-meter"], stderr.ToString(), StringComparison.Ordinal);
                return;
            }
            var rounds = search.GetProperty("rounds").GetArrayLength();
            Assert.InRange(rounds, 1, MeasuredQualitySearch.MaxRounds);
            Assert.Equal(json.RootElement.GetProperty("targetMb").GetDouble(),
                search.GetProperty("measuredQuality").ValueKind == JsonValueKind.Null
                    ? json.RootElement.GetProperty("targetMb").GetDouble()
                    : search.GetProperty("targetMb").GetDouble(), 3);
            Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "vidshrink_search_*"));
        }
        finally { Directory.Delete(dir, true); }
    }

    internal static async Task<string> KlipAsync(string dir)
    {
        var path = Path.Combine(dir, "klip.mp4");
        var start = new ProcessStartInfo(ToolLocator.Ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=15:duration=2",
                     "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-threads", "2", path })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var drainOut = process.StandardOutput.ReadToEndAsync();
        var drainErr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await drainOut;
        var log = await drainErr;
        Assert.True(process.ExitCode == 0, log);
        return path;
    }

    private sealed record Kuyruk(int Calls, double TargetMb, bool Searched, double? Start);

    private static Kuyruk KuyrukAramasi(double? kalite, bool kutu, bool olcer, double? olculen = 70)
        => AppHost.Run(() =>
        {
            var window = new ShrinkJobWindow(new[] { @"C:\Kayitlar\a.mp4" }, new PlanOptions { TargetMb = 25 }, false, null)
            {
                IstenenKalite = kalite,
                OlcerekAra = kutu,
                OlcerHazir = () => olcer
            };
            try
            {
                double? start = null;
                window.OlcerekAramaKosucu = (info, options, q, s, progress, ct) =>
                {
                    start = s;
                    return Task.FromResult(new QualitySearchResult(AramaMb, olculen, q,
                        new[] { new QualitySearchRound(1, MeasuredQualitySearch.MaxRounds, AramaMb, olculen) }, olculen is not null));
                };
                var info = Kaynak(@"C:\Kayitlar\a.mp4");
                var (options, mb, arama) = window.AramaylaAsync(new ShrinkRequest(25, info.FilePath), info,
                    new PlanOptions { TargetMb = 25 }, 25, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(mb, options.TargetMb);
                return new Kuyruk(window.OlcerekAramaCagrisi, mb, arama is not null, start);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void AQueueJobSearchesOnlyWhenTheBoxIsOnTheQueueCameFromQualityAndTheMeterIsThere()
    {
        var on = KuyrukAramasi(70, true, true);
        Assert.Equal(1, on.Calls);
        Assert.Equal(AramaMb, on.TargetMb);
        Assert.Equal(PlanCalculator.TargetMbForQuality(Kaynak(@"C:\Kayitlar\a.mp4"), new PlanOptions { TargetMb = 25 }, 70).TargetMb, on.Start);

        foreach (var off in new[] { KuyrukAramasi(70, false, true), KuyrukAramasi(null, true, true), KuyrukAramasi(70, true, false) })
        {
            Assert.Equal(0, off.Calls);
            Assert.Equal(25, off.TargetMb);
            Assert.False(off.Searched);
        }
    }

    [Fact]
    public void AFailedQueueSearchKeepsTheJobsTarget()
    {
        var dusen = KuyrukAramasi(70, true, true, olculen: null);

        Assert.Equal(1, dusen.Calls);
        Assert.Equal(25, dusen.TargetMb);
        Assert.True(dusen.Searched);
    }

    [Fact]
    public void TheBatchCarriesTheQualityOnlyWhenQualityDrivesTheTarget()
        => AppHost.Run(() =>
        {
            var window = new MainWindow { SettingsPathOverride = Path.Combine(Path.GetTempPath(), $"vidshrink-arama-{Guid.NewGuid():N}", "settings.json") };
            try
            {
                window.LoadWithoutProbing(@"C:\Kayitlar\a.mp4", Kaynak(@"C:\Kayitlar\a.mp4"));
                window.ChkAdvSearchByMeasure.IsChecked = true;
                window.TxtQualityTarget.Text = "72";
                var kaliteden = window.OpenBatch(new[] { @"C:\Kayitlar\a.mp4", @"C:\Kayitlar\b.mp4" });
                window.TxtTarget.Text = "30";
                var boyuttan = window.OpenBatch(new[] { @"C:\Kayitlar\a.mp4", @"C:\Kayitlar\b.mp4" });
                try
                {
                    Assert.Equal(72, kaliteden.IstenenKalite);
                    Assert.True(kaliteden.OlcerekAra);
                    Assert.Null(boyuttan.IstenenKalite);
                }
                finally
                {
                    kaliteden.Close();
                    boyuttan.Close();
                }
                return 0;
            }
            finally { window.Close(); }
        });

    [FfmpegFact]
    public async Task TheQueueEncodesAtTheSearchedTarget()
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "olcerek-arama-kuyruk", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = await KlipAsync(dir);
            var aranan = Megabayt.Oku(new FileInfo(input).Length) / 2;
            var (calls, mesaj, cikti) = AppHost.Run(() =>
            {
                var window = new ShrinkJobWindow(new[] { input }, new PlanOptions { TargetMb = 25, EncoderThreads = 2 }, false, null)
                {
                    IstenenKalite = 60,
                    OlcerekAra = true,
                    OlcerHazir = () => true,
                    KaliteyiOlc = false
                };
                var calls = 0;
                window.OlcerekAramaKosucu = (info, options, q, s, progress, ct) =>
                {
                    calls++;
                    progress.Report(new QualitySearchRound(1, MeasuredQualitySearch.MaxRounds, aranan, null));
                    return Task.FromResult(new QualitySearchResult(aranan, 61, q,
                        new[] { new QualitySearchRound(1, MeasuredQualitySearch.MaxRounds, aranan, 61) }, true));
                };
                string? teslim = null;
                var bitti = false;
                window.JobFinished += (_, output) => { teslim = output; bitti = true; };
                try
                {
                    window.Begin();
                    var saat = Stopwatch.StartNew();
                    while (!bitti && saat.Elapsed < TimeSpan.FromSeconds(60))
                    {
                        Dispatcher.UIThread.RunJobs();
                        Thread.Sleep(20);
                    }
                    Dispatcher.UIThread.RunJobs();
                    return (calls, window.MessageText, teslim);
                }
                finally { window.Close(); }
            });

            Assert.Equal(1, calls);
            Assert.NotNull(cikti);
            Assert.True(Megabayt.Oku(new FileInfo(cikti!).Length) <= aranan * 1.05, mesaj);
            Assert.Contains(aranan.ToString("0.0", CultureInfo.CurrentCulture).Split(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator)[0], mesaj, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }
}
