using System.Text;
using System.Text.Json;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.Cli;

/// <summary>
/// stderr ilerlemesi. <c>--json</c> verilince her satır tek bir JSON nesnesidir (NDJSON):
/// aşama <c>{"type":"stage","stage":"probe"}</c>, kodlama
/// <c>{"type":"progress","percent":42,"fraction":0.42,"stage":"pass 1/2","remainingSeconds":12.5}</c>.
/// stdout'taki sonuç değişmez.
/// </summary>
public static class CliProgress
{
    private const string StagePrefix = "progress.";

    public static void Stage(TextWriter stderr, CliText text, string key, bool json)
        => stderr.WriteLine(json ? StageLine(key.StartsWith(StagePrefix, StringComparison.Ordinal) ? key[StagePrefix.Length..] : key) : text[key]);

    public static string StageLine(string stage)
        => Line(writer =>
        {
            writer.WriteString("type", "stage");
            writer.WriteString("stage", stage);
        });

    public static string ProgressLine(int percent, double fraction, string stage, TimeSpan? remaining)
        => Line(writer =>
        {
            writer.WriteString("type", "progress");
            writer.WriteNumber("percent", percent);
            writer.WriteNumber("fraction", Math.Round(Math.Clamp(fraction, 0, 1), 4));
            writer.WriteString("stage", stage);
            if (remaining is { } left) writer.WriteNumber("remainingSeconds", Math.Round(left.TotalSeconds, 1));
            else writer.WriteNull("remainingSeconds");
        });

    public static string SearchLine(QualitySearchRound round)
        => Line(writer =>
        {
            writer.WriteString("type", "search");
            writer.WriteNumber("round", round.Round);
            writer.WriteNumber("maxRounds", round.MaxRounds);
            writer.WriteNumber("targetMb", Math.Round(round.TargetMb, 3));
            if (round.MeasuredQuality is { } score) writer.WriteNumber("measuredQuality", Math.Round(score, 2));
            else writer.WriteNull("measuredQuality");
        });

    /// <summary><c>--olcerek-ara</c>'nin turlari: tur basinda hedef, olcumden sonra puan.</summary>
    public sealed class Search(TextWriter stderr, CliText text, bool json) : IProgress<QualitySearchRound>
    {
        private readonly object _gate = new();

        public void Report(QualitySearchRound value)
        {
            lock (_gate)
            {
                stderr.WriteLine(json
                    ? SearchLine(value)
                    : value.MeasuredQuality is { } score
                        ? text.Format("progress.search-round-done", value.Round, value.MaxRounds,
                            value.TargetMb.ToString("0.##", text.Culture), score.ToString("0.0", text.Culture))
                        : text.Format("progress.search-round", value.Round, value.MaxRounds, value.TargetMb.ToString("0.##", text.Culture)));
            }
        }
    }

    private static string Line(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public sealed class Encode(TextWriter stderr, CliText text, bool json) : IProgress<EncodeProgress>
    {
        private readonly object _gate = new();
        private int _lastPercent = -1;
        private string _lastStage = "";

        public void Report(EncodeProgress value)
        {
            var percent = (int)Math.Clamp(Math.Floor(value.Fraction * 100), 0, 100);
            lock (_gate)
            {
                if (percent == _lastPercent && value.Stage == _lastStage) return;
                _lastPercent = percent;
                _lastStage = value.Stage;
                stderr.WriteLine(json
                    ? ProgressLine(percent, value.Fraction, value.Stage, value.Remaining)
                    : text.Format("progress.encode", percent, value.Stage, value.Remaining is { } left ? Saat.Ekran(left) : "-"));
            }
        }
    }
}
