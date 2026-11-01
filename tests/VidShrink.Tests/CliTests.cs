using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using VidShrink.App;
using VidShrink.Cli;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.Tests;

public sealed class CliTests
{
    private static string Sandbox() =>
        Path.Combine(TestPaths.OutputRoot, "hb-1a-cli", $"{Guid.NewGuid():N}", "settings.json");

    private static MediaInfo Source() => new()
    {
        FilePath = @"C:\Kayitlar\telefon-kaydi-1080p30.mp4",
        FileSizeBytes = 90_000_000L,
        DurationSeconds = 62.0,
        Width = 1920,
        Height = 1080,
        Fps = 30,
        VideoCodec = "h264",
        TotalBitrateBps = 11_600_000,
        AudioCodec = "aac",
        AudioBitrateBps = 128_000,
        AudioChannels = 2,
        PixelFormat = "yuv420p"
    };

    private static void SelectCodec(MainWindow window, CliCodec codec)
    {
        window.RbCodecAuto.IsChecked = codec is CliCodec.Auto or CliCodec.Hevc;
        window.RbCodecCompatible.IsChecked = codec == CliCodec.H264;
        window.RbCodecSmallest.IsChecked = codec == CliCodec.Av1;
        window.CmbAdvCodecLock.SelectedIndex = codec == CliCodec.Hevc
            ? 1 + FfmpegArguments.OfferedCodecs(CodecModel.CurrentPlatform).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList().IndexOf("libx265")
            : 0;
    }

    private sealed record GuiReading(string Command, double TargetMb, string Codec, CodecPreference Preference, string? Locked, string Options);

    private static string OptionsJson(PlanOptions options) => JsonSerializer.Serialize(options);

    private static GuiReading Gui(MediaInfo info, CliCodec codec, double? targetMb, double? quality) => AppHost.Run(() =>
    {
        var window = new MainWindow { SettingsPathOverride = Sandbox() };
        try
        {
            window.LoadWithoutProbing(info.FilePath, info);
            SelectCodec(window, codec);
            if (targetMb is { } mb) window.TxtTarget.Text = mb.ToString("0.##", CultureInfo.InvariantCulture);
            if (quality is { } score) window.TxtQualityTarget.Text = score.ToString("0.##", CultureInfo.InvariantCulture);
            window.RecalculateForTest();
            var options = window.PlanOptionsForTest();
            return new GuiReading(window.TxtCommand.Text ?? "", options.TargetMb, window.ActivePlanForTest!.Codec, options.Codec, options.LockedCodec, OptionsJson(options));
        }
        finally { window.Close(); }
    });

    private static CliDecision Cli(MediaInfo info, CliCodec codec, double? targetMb, double? quality)
        => CliApp.Decide(new CliRequest
        {
            Command = CliCommand.Plan,
            Input = info.FilePath,
            TargetMb = targetMb,
            Quality = quality,
            Codec = codec,
            SkipMeasurement = true,
            PreferredLanguage = "en"
        }, info, null, null, null);

    [Theory]
    [InlineData(CliCodec.Auto, 25d)]
    [InlineData(CliCodec.H264, 25d)]
    [InlineData(CliCodec.Hevc, 25d)]
    [InlineData(CliCodec.Av1, 25d)]
    [InlineData(CliCodec.H264, 8d)]
    [InlineData(CliCodec.Auto, 180d)]
    public void HedefteGuiVeCliAyniFfmpegKomutunuUretiyor(CliCodec codec, double targetMb)
    {
        var info = Source();

        var gui = Gui(info, codec, targetMb, null);
        var cli = Cli(info, codec, targetMb, null);

        Assert.False(string.IsNullOrWhiteSpace(gui.Command), "Pencere komut satırı yazmadı; ölçü ölü.");
        Assert.Equal(targetMb, gui.TargetMb, 3);
        Assert.Equal(gui.Preference, cli.Options.Codec);
        Assert.Equal(gui.Locked, cli.Options.LockedCodec);
        Assert.Equal(gui.Codec, cli.Plan.Codec);
        Assert.Equal(gui.Options, OptionsJson(cli.Options));
        Assert.Equal(gui.Command, FfmpegArguments.ToCommandLine(cli.Arguments));
    }

    [Theory]
    [InlineData(CliCodec.Auto, 60d)]
    [InlineData(CliCodec.H264, 85d)]
    [InlineData(CliCodec.Av1, 40d)]
    public void KalitedeGuiVeCliAyniHedefiVeKomutuUretiyor(CliCodec codec, double quality)
    {
        var info = Source();

        var gui = Gui(info, codec, null, quality);
        var cli = Cli(info, codec, null, quality);

        Assert.False(string.IsNullOrWhiteSpace(gui.Command), "Pencere komut satırı yazmadı; ölçü ölü.");
        Assert.Equal(gui.TargetMb, cli.TargetMb, 6);
        Assert.Equal(gui.Options, OptionsJson(cli.Options));
        Assert.Equal(gui.Command, FfmpegArguments.ToCommandLine(cli.Arguments));
    }

    [Fact]
    public void NegatifKontrolFarkliGirdiFarkliKomutVeriyor()
    {
        var info = Source();
        var gui = Gui(info, CliCodec.H264, 25, null);

        var otherTarget = FfmpegArguments.ToCommandLine(Cli(info, CliCodec.H264, 12, null).Arguments);
        var otherCodec = FfmpegArguments.ToCommandLine(Cli(info, CliCodec.Av1, 25, null).Arguments);
        var otherHevc = FfmpegArguments.ToCommandLine(Cli(info, CliCodec.Hevc, 25, null).Arguments);
        var otherSource = FfmpegArguments.ToCommandLine(Cli(info with { DurationSeconds = 31 }, CliCodec.H264, 25, null).Arguments);

        Assert.NotEqual(gui.Command, otherTarget);
        Assert.NotEqual(gui.Command, otherCodec);
        Assert.NotEqual(gui.Command, otherHevc);
        Assert.NotEqual(gui.Command, otherSource);
        Assert.Contains("libx265", otherHevc, StringComparison.Ordinal);
    }

    [Fact]
    public void PencereKarariTekMotordanGeciyor()
    {
        var window = File.ReadAllText(TipSources.WindowCodePath);

        Assert.Contains("ShrinkEngine.CalibrateAsync(", window, StringComparison.Ordinal);
        Assert.Contains("=> ShrinkEngine.DisplayedArguments(", window, StringComparison.Ordinal);
        Assert.Contains("=> ShrinkEngine.UniqueOutputPath(", window, StringComparison.Ordinal);
        Assert.Contains("await ShrinkEngine.EncodeAsync(", window, StringComparison.Ordinal);
        Assert.DoesNotContain("new EncodeRunner().RunAsync", window, StringComparison.Ordinal);
        Assert.DoesNotContain("CalibrationProbe.RunAsync", window, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("25MB", 25d)]
    [InlineData("25", 25d)]
    [InlineData("8,5mb", 8.5d)]
    [InlineData("1.5GB", 1536d)]
    [InlineData("40M", 40d)]
    public void BoyutOkunuyor(string text, double expected)
    {
        Assert.True(CliParser.TryParseSize(text, out var mb));
        Assert.Equal(expected, mb, 6);
    }

    [Theory]
    [InlineData("0MB")]
    [InlineData("-3")]
    [InlineData("yirmi")]
    [InlineData("")]
    public void BozukBoyutReddediliyor(string text) => Assert.False(CliParser.TryParseSize(text, out _));

    [Fact]
    public void KucultKomutuTumSecenekleriOkuyor()
    {
        var parsed = CliParser.Parse(new[] { "kucult", "a.mp4", "--kalite", "72", "--kodek", "hevc", "--cikti", "b.mp4", "--json", "--olcumsuz", "--vmaf", "--hizli" });

        Assert.True(parsed.Ok, parsed.ErrorKey);
        var request = parsed.Request!;
        Assert.Equal(CliCommand.Shrink, request.Command);
        Assert.Equal("a.mp4", request.Input);
        Assert.Equal(72d, request.Quality);
        Assert.Null(request.TargetMb);
        Assert.Equal(CliCodec.Hevc, request.Codec);
        Assert.Equal("b.mp4", request.Output);
        Assert.True(request.Json && request.SkipMeasurement && request.MeasureVmaf && request.Fast);

        var options = request.ToPlanOptions(10);
        Assert.Equal("libx265", options.LockedCodec);
        Assert.Equal(SpeedMode.Fast, options.SpeedMode);
        Assert.Null(options.PreferredLanguage);
        Assert.Equal("tr", (request with { PreferredLanguage = "tr" }).ToPlanOptions(10).PreferredLanguage);
    }

    [Theory]
    [InlineData("error.target-or-quality", "kucult", "a.mp4")]
    [InlineData("error.target-or-quality", "plan", "a.mp4", "--hedef", "25MB", "--kalite", "60")]
    [InlineData("error.bad-quality", "plan", "a.mp4", "--kalite", "0")]
    [InlineData("error.bad-quality", "plan", "a.mp4", "--kalite", "101")]
    [InlineData("error.bad-codec", "plan", "a.mp4", "--hedef", "25", "--kodek", "vp8")]
    [InlineData("error.unknown-option", "plan", "a.mp4", "--hedef", "25", "--uydurma")]
    [InlineData("error.missing-value", "plan", "a.mp4", "--hedef")]
    [InlineData("error.no-input", "plan", "--hedef", "25")]
    [InlineData("error.extra-input", "plan", "a.mp4", "b.mp4", "--hedef", "25")]
    [InlineData("error.unknown-command", "sil", "a.mp4")]
    [InlineData("error.watch-no-output", "izle", "a.mp4")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--kes", "10")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--kes", "10-20-30")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--kes", "40-20")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--kes", "0:70-0:90")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--kes", "-20")]
    [InlineData("error.bad-range", "plan", "a.mp4", "--hedef", "25", "--cut", "abc-def")]
    public void YanlisKullanimAdiylaReddediliyor(string key, params string[] args)
    {
        var parsed = CliParser.Parse(args);

        Assert.False(parsed.Ok);
        Assert.Equal(key, parsed.ErrorKey);
    }

    /// <summary>
    /// <c>error.bad-range</c>'i hicbir test okumuyordu (denetim bulgusu): anahtari bozan
    /// bir mutasyon sessizce geciyordu. Burada hem reddedilen degerin anahtari hem de
    /// anahtarin iki dil dosyasindaki karsiligi okunur; kabul edilen kesitler ise ayni
    /// yolun <b>kirmizi olmadigini</b> gosterir, boylece "her sey bad-range" mutasyonu da
    /// kirilir.
    /// </summary>
    [Theory]
    [InlineData("10-40", 10.0, 40.0)]
    [InlineData("0:10-0:40", 10.0, 40.0)]
    [InlineData("90-", 90.0, null)]
    [InlineData("1:02:03-1:02:04", 3723.0, 3724.0)]
    public void KabulEdilenKesitBadRangeVermez(string kesit, double start, double? end)
    {
        var parsed = CliParser.Parse(new[] { "plan", "a.mp4", "--hedef", "25", "--kes", kesit });

        Assert.True(parsed.Ok, parsed.ErrorKey);
        Assert.Equal(start, parsed.Request!.TrimStartSeconds);
        Assert.Equal(end, parsed.Request!.TrimEndSeconds);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void BadRangeCumlesiDilDosyasindanGeliyorVeDegeriTasiyor(string dil)
    {
        var parsed = CliParser.Parse(new[] { "plan", "a.mp4", "--hedef", "25", "--kes", "40-20" });

        Assert.Equal("error.bad-range", parsed.ErrorKey);
        Assert.Equal("40-20", parsed.ErrorArgument);

        var cumle = CliText.ForLanguage(dil).Format(parsed.ErrorKey!, parsed.ErrorArgument!);
        Assert.Contains("40-20", cumle, StringComparison.Ordinal);
        Assert.Contains("0:10-0:40", cumle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YanlisKullanim64IleDonuyorVeStdoutBosKaliyor()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync(new[] { "izle", "a.mp4" }, stdout, stderr, CliText.ForLanguage("en"), Unreachable(), CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal("", stdout.ToString());
        Assert.Contains("izle", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CikisKodlariKosucununSonucundanGeliyor()
    {
        var plan = new EncodePlan();
        Assert.Equal(0, CliApp.ExitCodeFor(new EncodeResult(true, "o.mp4", 24.6, plan, 1, null)));
        Assert.Equal(2, CliApp.ExitCodeFor(new EncodeResult(true, "o.mp4", 18.0, plan, 3, null, UnderBand: true)));
        Assert.Equal(3, CliApp.ExitCodeFor(new EncodeResult(false, "o.mp4", 27.0, plan, 4, null, CeilingExceeded: true)));
        Assert.Equal(1, CliApp.ExitCodeFor(new EncodeResult(false, "o.mp4", 0, plan, 1, "ffmpeg failed")));
        Assert.Equal(new[] { 0, 1, 2, 3, 64, 130 }, new[] { ExitCodes.InBand, ExitCodes.Error, ExitCodes.UnderBand, ExitCodes.CeilingExceeded, ExitCodes.Usage, ExitCodes.Cancelled });
    }

    [Fact]
    public void IkiDilinAnahtarlariAyni()
    {
        var english = CliText.Load("en").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var turkish = CliText.Load("tr").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(english);
        Assert.Equal(english, turkish);
    }

    [Theory]
    [InlineData("tr-TR", "tr", "Kullanım:")]
    [InlineData("en-US", "en", "Usage:")]
    [InlineData("de-DE", "en", "Usage:")]
    public async Task YardimSistemDilineGoreSeciliyor(string culture, string language, string marker)
    {
        var text = CliText.For(CultureInfo.GetCultureInfo(culture));
        var stdout = new StringWriter();

        var exit = await CliApp.RunAsync(new[] { "--help" }, stdout, new StringWriter(), text, Unreachable(), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(language, text.Language);
        Assert.Contains(marker, stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("izle", stdout.ToString(), StringComparison.Ordinal);
        foreach (var code in new[] { "0 ", "1 ", "2 ", "3 ", "4 ", "64 ", "130 " })
            Assert.Contains(code, stdout.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public async Task YardimdakiHerSatirTamBirKezGeciyor(string language)
    {
        var stdout = new StringWriter();

        await CliApp.RunAsync(new[] { "--help" }, stdout, new StringWriter(), CliText.ForLanguage(language), Unreachable(), CancellationToken.None);

        var lines = stdout.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).ToList();
        Assert.True(lines.Count > 20, $"{language}: {lines.Count}");
        var repeated = lines.GroupBy(line => line, StringComparer.Ordinal).Where(group => group.Count() != 1)
            .Select(group => $"{group.Count()}x {group.Key}").ToList();
        Assert.Empty(repeated);
        Assert.Single(lines, line => line.Contains("vidshrink izle ", StringComparison.Ordinal));
        Assert.Single(lines, line => line.Contains("--bir-kez", StringComparison.Ordinal) && !line.Contains("izle --bir-kez", StringComparison.Ordinal));
        Assert.Single(lines, line => line.TrimStart().StartsWith("--aralik", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FfmpegYoksaHata1VeKodlamaYok()
    {
        var probed = false;
        var services = new CliServices
        {
            MissingTool = () => "ffmpeg",
            Probe = (_, _) => { probed = true; return Task.FromResult(Source()); },
            Availability = () => null
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.RunAsync(new[] { "kucult", "a.mp4", "--hedef", "25MB" }, stdout, stderr, CliText.ForLanguage("tr"), services, CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exit);
        Assert.False(probed);
        Assert.Equal("", stdout.ToString());
        Assert.Contains("ffmpeg", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanJsonuStdoutaIlerlemeStderreGidiyor()
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "hb-1a-cli", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "kaynak.mp4");
        File.WriteAllBytes(input, new byte[16]);
        try
        {
            var info = Source() with { FilePath = input };
            var services = new CliServices { MissingTool = () => null, Probe = (_, _) => Task.FromResult(info), Availability = () => null };
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = await CliApp.RunAsync(new[] { "plan", input, "--hedef", "25MB", "--kodek", "h264", "--olcumsuz", "--json" },
                stdout, stderr, CliText.ForLanguage("en"), services, CancellationToken.None);

            Assert.Equal(0, exit);
            using var json = JsonDocument.Parse(stdout.ToString());
            var root = json.RootElement;
            var expected = Cli(info, CliCodec.H264, 25, null);
            Assert.Equal("plan", root.GetProperty("command").GetString());
            Assert.Equal(expected.Arguments, root.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToList());
            Assert.Equal(expected.Plan.Codec, root.GetProperty("plan").GetProperty("codec").GetString());
            Assert.Equal(expected.Plan.ReasonCodes.Select(r => r.Code.ToString()), root.GetProperty("plan").GetProperty("reasonCodes").EnumerateArray().Select(r => r.GetString()!));
            Assert.Contains(CliProgress.StageLine("probe"), stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Reading the source", stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("\"stage\"", stdout.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(expected.OutputPath));
        }
        finally { Directory.Delete(dir, true); }
    }

    [FfmpegFact]
    public async Task GercekKlipKuculuyorJsonSonucVeCikisKoduTutarli()
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "hb-1a-cli", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = await MakeClipAsync(dir);
            var output = Path.Combine(dir, "cikti.mp4");
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exit = await CliApp.RunAsync(new[] { "kucult", input, "--hedef", HalfSize(input), "--kodek", "h264", "--olcumsuz", "--json", "--cikti", output },
                stdout, stderr, CliText.ForLanguage("tr"), CliServices.Default, CancellationToken.None);

            using var json = JsonDocument.Parse(stdout.ToString());
            var result = json.RootElement.GetProperty("result");
            Assert.Equal(exit, result.GetProperty("exitCode").GetInt32());
            Assert.Contains(exit, new[] { ExitCodes.InBand, ExitCodes.UnderBand, ExitCodes.CeilingExceeded });
            var attempts = result.GetProperty("attempts").GetInt32();
            Assert.True(attempts >= 1);
            Assert.Equal(attempts, result.GetProperty("trace").GetArrayLength());
            Assert.Equal("deneme 1", result.GetProperty("trace")[0].GetProperty("label").GetString());
            Assert.True(result.GetProperty("elapsedSeconds").GetDouble() > 0);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("vmaf").ValueKind);
            if (result.GetProperty("success").GetBoolean())
            {
                Assert.True(File.Exists(output));
                Assert.InRange(result.GetProperty("outputMb").GetDouble() - Megabayt.Oku(new FileInfo(output).Length), -0.0006, 0.0006);
            }
            Assert.Contains("\"type\":\"progress\"", stderr.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("\"type\":\"progress\"", stdout.ToString(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [FfmpegFact]
    public async Task SurecOlarakPlanKomutuAyniArgumanlariVeriyor()
    {
        var dir = Path.Combine(TestPaths.OutputRoot, "hb-1a-cli", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = await MakeClipAsync(dir);
            var cli = Path.Combine(AppContext.BaseDirectory, "vidshrink.dll");
            Assert.True(File.Exists(cli), cli);

            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { cli, "plan", input, "--hedef", HalfSize(input), "--kodek", "av1", "--olcumsuz", "--json" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.True(process.ExitCode == 0, stderr);
            using var json = JsonDocument.Parse(stdout);
            var root = json.RootElement;
            var arguments = root.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToList();
            var output = root.GetProperty("output").GetString()!;
            Assert.Equal(Path.Combine(dir, "klip_shrunk.mp4"), output);
            Assert.Contains(input, arguments);
            Assert.Contains(output, arguments);
            Assert.Equal(FfmpegArguments.ToCommandLine(arguments), root.GetProperty("commandLine").GetString());
            Assert.True(root.GetProperty("targetMb").GetDouble() < Megabayt.Oku(new FileInfo(input).Length));
            Assert.False(string.IsNullOrWhiteSpace(stderr));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static async Task<string> MakeClipAsync(string dir)
    {
        var path = Path.Combine(dir, "klip.mp4");
        var start = new ProcessStartInfo(ToolLocator.Ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=15:duration=2",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8",
                     "-c:a", "aac", "-b:a", "96k", "-shortest", path })
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

    private static string HalfSize(string path)
        => (Megabayt.Oku(new FileInfo(path).Length) / 2).ToString("0.###", CultureInfo.InvariantCulture) + "MB";

    private static CliServices Unreachable() => new()
    {
        MissingTool = () => throw new InvalidOperationException("tool lookup must not run"),
        Probe = (_, _) => throw new InvalidOperationException("probe must not run"),
        Availability = () => throw new InvalidOperationException("availability must not run")
    };

    private const int BeklenenTakmaAdSayisi = 66;

    /// <summary>
    /// <para>Ingilizce takma adlar iki CLI belgesinde (<c>docs/cli.md</c>, <c>docs/cli.tr.md</c>) yaziliydi diye degil, <b>kaynaktan
    /// sayilarak</b> pimleniyor: liste testte tekrarlanmaz, <c>CliParser.Parse</c>'in switch
    /// kollarindan cikarilir.</para>
    /// <para><b>Turetme kurali istisnasiz</b>: bir kolun <i>uzun</i> yazimlari (<c>--</c> ile
    /// baslayanlar) sirayla alinir; ilki kanonik, kalan her uzun yazim onun takma adidir. Kisa
    /// yazimlar (<c>-h</c>, <c>-o</c>) kolun neresinde durursa dursun sayima girmez, dolayisiyla
    /// bir kola kisa bayrak eklemek sayimi degistirmez — onceki kural "ilk yazim uzun olmali"
    /// dedigi icin <c>-h</c> ile baslayan yardim kolu disarda kaliyor, <c>--hedef</c> koluna
    /// <c>-t</c> eklenince sayim sessizce dusuyordu.</para>
    /// <para>Sayinin kendisi de pimli (<see cref="BeklenenTakmaAdSayisi"/>), boylece kaynaga
    /// eklenen yeni bir takma ad belgesiz kalamaz. Sayim tutmazsa hata iletisi <b>her kolu ve
    /// elenme sebebini</b> yazar.</para>
    /// <para>SIK-P5 (yakma bicimi) uc cift ekledi: <c>--yak-yazi-tipi</c>/<c>--burn-font</c>,
    /// <c>--yak-boyut</c>/<c>--burn-size</c>, <c>--yak-kutu</c>/<c>--burn-box</c>; sayim 46 → 49.</para>
    /// <para>SIK-P4 (dis ses) iki cift ekledi: <c>--ses-dosyasi</c>/<c>--audio-file</c>, <c>--ses-degistir</c>/<c>--replace-audio</c>; sayim 49 → 51.</para>
    /// <para>DAG-P2 (donustur, profil disa aktarma) bes cift ekledi: <c>--kap</c>/<c>--container</c>, <c>--bit-hizi</c>/<c>--bitrate</c>,
    /// <c>--cozunurluk</c>/<c>--resolution</c>, <c>--ses-bit-hizi</c>/<c>--audio-bitrate</c>, <c>--disa-aktar</c>/<c>--export</c>; sayim 51 → 56.
    /// <c>donustur</c> kollari <c>CliConvert.cs</c>'te durdugu icin o dosyanin <c>case</c> kollari da taranir.</para>
    /// <para>Later 62 (ek arguman) bir cift ekledi: <c>--ek-arguman</c>/<c>--extra-args</c>; sayim 56 → 57.</para>
    /// <para>Later 44 (18 Ekim 2026, bolum duzenleyicinin komut satiri yuzu) sekiz cift ekledi: <c>--etiket-baslik</c>,
    /// <c>--etiket-sanatci</c>, <c>--etiket-yil</c>, <c>--etiket-yorum</c>, <c>--kapak</c>, <c>--onar</c>, <c>--bolum-ekle</c>,
    /// <c>--bolumsuz</c>; sayim 57 → 65.</para>
    /// <para>Later 141 (30 Ekim 2026, olcerek arama) bir cift ekledi: <c>--olcerek-ara</c>/<c>--search-by-measure</c>; sayim 65 → 66.</para>
    /// <para>Belgelerin "hepsi" demesi de veriyle pimli: takma adi olmayan uzun anahtarlar
    /// (<c>--json</c>, <c>--vmaf</c>) kaynaktan cikarilip cumlede istisna olarak araniyor.</para>
    /// </summary>
    [Fact]
    public void IngilizceTakmaAdlarIkiBelgedeDeYaziyor()
    {
        var (adlar, tekiller, rapor) = TakmaAdKollari();
        Assert.True(
            adlar.Count == BeklenenTakmaAdSayisi,
            $"CliRequest.cs'te {adlar.Count} takma ad cifti bulundu, beklenen {BeklenenTakmaAdSayisi}.\nKol dokumu:\n{rapor}");

        var ingilizce = Belge("docs/cli.md");
        var turkce = Belge("docs/cli.tr.md");

        foreach (var (kanonik, takma) in adlar)
        {
            Assert.Contains($"| `{kanonik}` | `{takma}` |", ingilizce, StringComparison.Ordinal);
            Assert.Contains($"| `{kanonik}` | `{takma}` |", turkce, StringComparison.Ordinal);
        }

        Assert.Contains(
            MetinPimi.Duz($"{string.Join(" and ", tekiller.Select(a => $"`{a}`"))} are the exceptions: they have a single spelling."),
            MetinPimi.Duz(ingilizce),
            StringComparison.Ordinal);
        Assert.Contains(
            MetinPimi.Duz($"Tek istisna {string.Join(" ve ", tekiller.Select(a => $"`{a}`"))}: bunların tek yazımı var."),
            MetinPimi.Duz(turkce),
            StringComparison.Ordinal);
    }

    private static (IReadOnlyList<(string Kanonik, string Takma)> Ciftler, IReadOnlyList<string> Tekiller, string Rapor) TakmaAdKollari()
    {
        var kaynak = File.ReadAllText(Path.Combine(TipSources.Root, "src", "VidShrink.Cli", "CliRequest.cs"));
        var basi = kaynak.IndexOf("switch (arg)", StringComparison.Ordinal);
        Assert.True(basi > 0, "CliRequest.cs icinde 'switch (arg)' bulunamadi");

        var basKollari = Regex.Matches(kaynak[..basi], @"head\s+is\s+(""[^""]+""(?:\s+or\s+""[^""]+"")*)");
        Assert.True(
            basKollari.Count > 0,
            "CliRequest.cs'te 'switch (arg)' oncesinde hicbir 'head is' kolu bulunamadi; tarama bas kollarini kacirir");

        var ciftler = new List<(string, string)>();
        var tekiller = new List<string>();
        var rapor = new StringBuilder();
        var kollar = basKollari
            .Select(k => (Ad: "head is", Yazimlar: k.Groups[1].Value))
            .Concat(Regex.Matches(kaynak[basi..], @"case\s+(""[^""]+""(?:\s+or\s+""[^""]+"")*)")
                .Select(k => (Ad: "case", Yazimlar: k.Groups[1].Value)))
            .Concat(Regex.Matches(
                    File.ReadAllText(Path.Combine(TipSources.Root, "src", "VidShrink.Cli", "CliConvert.cs")),
                    @"case\s+(""[^""]+""(?:\s+or\s+""[^""]+"")*)")
                .Select(k => (Ad: "donustur case", Yazimlar: k.Groups[1].Value)));

        foreach (var kol in kollar)
        {
            var yazimlar = Regex.Matches(kol.Yazimlar, @"""([^""]+)""")
                .Select(e => e.Groups[1].Value).ToList();
            var uzun = yazimlar.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToList();
            var ad = $"{kol.Ad} {string.Join(" or ", yazimlar.Select(a => $"\"{a}\""))}";

            if (uzun.Count == 0)
            {
                rapor.AppendLine($"  elendi  {ad} — uzun yazimi yok");
                continue;
            }

            if (uzun.Count == 1)
            {
                if (!tekiller.Contains(uzun[0])) tekiller.Add(uzun[0]);
                rapor.AppendLine($"  elendi  {ad} — tek uzun yazim ({uzun[0]}), takma adi yok");
                continue;
            }

            foreach (var takma in uzun.Skip(1))
            {
                if (ciftler.Contains((uzun[0], takma)))
                {
                    rapor.AppendLine($"  yinelendi {uzun[0]} -> {takma}");
                    continue;
                }

                ciftler.Add((uzun[0], takma));
                rapor.AppendLine($"  cift    {uzun[0]} -> {takma}");
            }
        }

        return (ciftler, tekiller, rapor.ToString());
    }

    private static string Belge(string ad) =>
        File.ReadAllText(Path.Combine(TipSources.Root, ad)).Replace("\r\n", "\n", StringComparison.Ordinal);
}
