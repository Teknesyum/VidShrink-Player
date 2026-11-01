using System.Text;
using VidShrink.Core;

namespace VidShrink.Cli;

/// <summary>
/// <c>tamamlama</c> komutu: bash, zsh, fish ve PowerShell için Tab tamamlama betiği basar.
/// Komutlar, seçenekler ve seçeneklerin değer kümeleri tek tablodan gelir; tablonun ayrıştırıcıyla
/// aynı kaldığını <c>CliCiktiDonusturTests</c> kaynağı okuyarak ölçer.
/// </summary>
public static class CliCompletion
{
    public static IReadOnlyList<string> Shells { get; } = new[] { "bash", "zsh", "fish", "powershell" };

    public static bool TryShell(string text, out string shell)
    {
        var lower = text.Trim().ToLowerInvariant();
        shell = lower is "pwsh" or "ps" ? "powershell" : Shells.FirstOrDefault(s => s == lower) ?? "";
        return shell.Length > 0;
    }

    public sealed record Command(IReadOnlyList<string> Names, IReadOnlyList<string> Options, IReadOnlyList<string> ValueOptions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Values, IReadOnlyList<string> Positional);

    private static readonly string[] Language = { "--dil", "--lang" };

    private static readonly string[] ShrinkFlags =
    {
        "-h", "--yardim", "--help", "--kirp", "--crop", "--tarama", "--scan", "--ana-icerik", "--main-feature",
        "--ses-temizle", "--denoise", "--ses-normal", "--loudnorm", "--yan-altyazi", "--sidecar-subtitles",
        "--ses-degistir", "--replace-audio", "--yak-kutu", "--burn-box", "--altyazi-tara", "--subtitle-scan",
        "--meta-yok", "--no-metadata", "--ilk-altyazi", "--first-subtitle", "--sabit-kare", "--cfr", "--tavan-kare", "--pfr",
        "--json", "--olcumsuz", "--no-measure", "--vmaf", "--olcerek-ara", "--search-by-measure", "--hizli", "--fast"
    };

    private static readonly string[] ShrinkValues =
    {
        "--hedef", "--target", "--kalite", "--quality", "--kodek", "--codec", "--crf", "--on-ayar", "--preset",
        "--modul", "--modulus", "--cikti", "--output", "-o", "--kes", "--cut", "--bolum", "--chapters",
        "--profil", "--profile", "--profil-dosyasi", "--preset-file", "--kirpma-kipi", "--crop-mode",
        "--baslik", "--title", "--asgari-sure", "--min-duration", "--azami-sure", "--max-duration",
        "--suzgec", "--filters", "--ses-kodek", "--audio-codec", "--ses-kazanc", "--gain", "--ses-hizi", "--arate",
        "--ses-drc", "--drc", "--altyazi", "--subtitle", "--ses-dosyasi", "--audio-file", "--yak", "--burn",
        "--yak-srt", "--srt-burn", "--yak-ass", "--ssa-burn", "--yak-yazi-tipi", "--burn-font", "--yak-boyut", "--burn-size",
        "--altyazi-dil", "--subtitle-lang", "--kare-hizi", "--fps", "--oncelik", "--priority", "--is-parcacigi", "--threads"
    };

    private static readonly string[] WatchOnlyFlags = { "--bir-kez", "--once" };
    private static readonly string[] WatchOnlyValues = { "--aralik", "--interval" };

    private static readonly string[] ConvertFlags = { "-h", "--yardim", "--help", "--meta-yok", "--no-metadata", "--onar", "--repair", "--bolumsuz", "--no-chapters", "--json" };

    private static readonly string[] RecordFlags = { "-h", "--yardim", "--help", "--json" };

    private static readonly string[] RecordValues = { "--sure", "--duration", "--cikti", "--output", "-o", "--ayarlar", "--settings" };

    private static readonly string[] ConvertValues =
    {
        "--kap", "--container", "--kodek", "--codec", "--crf", "--bit-hizi", "--bitrate", "--cozunurluk", "--resolution",
        "--kare-hizi", "--fps", "--ses-kodek", "--audio-codec", "--ses-bit-hizi", "--audio-bitrate", "--kes", "--cut",
        "--cikti", "--output", "-o", "--ek-arguman", "--extra-args", "--etiket-baslik", "--tag-title", "--etiket-sanatci", "--tag-artist",
        "--etiket-yil", "--tag-year", "--etiket-yorum", "--tag-comment", "--kapak", "--cover", "--bolum-ekle", "--add-chapter"
    };

    private static readonly string[] EditFlags = { "-h", "--yardim", "--help", "--meta-yok", "--no-metadata", "--json" };

    private static readonly string[] EditValues = { "--kip", "--mode", "--cikti", "--output", "-o" };

    private static readonly string[] ExportValues = { "--disa-aktar", "--export", "--profil", "--profile" };

    private static Dictionary<string, IReadOnlyList<string>> Pairs(params (string[] Options, IReadOnlyList<string> Values)[] rows)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (options, values) in rows)
            foreach (var option in options)
                map[option] = values;
        return map;
    }

    private static readonly IReadOnlyList<string> Priorities = new[] { "normal", "normalin-alti", "dusuk", "below-normal", "low" };

    public static IReadOnlyList<Command> Commands { get; } = Build();

    private static IReadOnlyList<Command> Build()
    {
        var builtIn = PresetLibrary.BuiltIn.Profiles.Select(p => p.Id).ToList();
        var shared = Pairs(
            (new[] { "--kodek", "--codec" }, new[] { "auto", "otomatik", "h264", "hevc", "av1", "vp9" }),
            (new[] { "--ses-kodek", "--audio-codec" }, new[] { "aac", "ac3", "eac3", "flac" }),
            (new[] { "--oncelik", "--priority" }, Priorities),
            (new[] { "--kirpma-kipi", "--crop-mode" }, CliParser.CropModes),
            (new[] { "--profil", "--profile" }, builtIn),
            (Language, CliText.Languages));
        var watchValues = Pairs(
            (new[] { "--kodek", "--codec" }, new[] { "auto", "otomatik", "h264", "hevc", "av1", "vp9" }),
            (new[] { "--oncelik", "--priority" }, Priorities),
            (Language, CliText.Languages));
        var convertValues = Pairs(
            (new[] { "--kap", "--container" }, CliConvert.Containers),
            (new[] { "--kodek", "--codec" }, CliConvert.VideoCodecs),
            (new[] { "--ses-kodek", "--audio-codec" }, CliConvert.AudioCodecs),
            (new[] { "--cozunurluk", "--resolution" }, new[] { "kaynak", "source", "2160", "1440", "1080", "720", "480", "360" }),
            (new[] { "--kare-hizi", "--fps" }, new[] { "kaynak", "source", "24", "25", "30", "50", "60" }),
            (Language, CliText.Languages));

        var shrinkValues = ShrinkValues.Concat(Language).ToList();
        var watchValueOptions = ShrinkValues.Where(o => !CliParser.NotInWatch.Contains(o)).Concat(WatchOnlyValues).Concat(Language).ToList();
        var watchFlags = ShrinkFlags.Where(o => !CliParser.NotInWatch.Contains(o)).Concat(WatchOnlyFlags).ToList();

        return new[]
        {
            new Command(new[] { "kucult", "shrink", "plan" }, ShrinkFlags.Concat(shrinkValues).ToList(), shrinkValues, shared, Array.Empty<string>()),
            new Command(new[] { "izle", "watch" }, watchFlags.Concat(watchValueOptions).ToList(), watchValueOptions, watchValues, Array.Empty<string>()),
            new Command(new[] { "donustur", "convert" }, ConvertFlags.Concat(ConvertValues).Concat(Language).ToList(),
                ConvertValues.Concat(Language).ToList(), convertValues, Array.Empty<string>()),
            new Command(new[] { "kaydet", "record" }, RecordFlags.Concat(RecordValues).Concat(Language).ToList(),
                RecordValues.Concat(Language).ToList(), Pairs((Language, CliText.Languages)), Array.Empty<string>()),
            new Command(new[] { "denetim", "control" }, new[] { "--json" }.Concat(Language).ToList(), Language,
                Pairs((Language, CliText.Languages)), CliControl.Actions.SelectMany(a => new[] { a.Turkish, a.English }).ToList()),
            new Command(new[] { "teslim", "export" }, EditFlags.Concat(EditValues).Concat(Language).ToList(),
                EditValues.Concat(Language).ToList(), Pairs((new[] { "--kip", "--mode" }, CliEdit.Modes), (Language, CliText.Languages)), Array.Empty<string>()),
            new Command(new[] { "profiller", "presets" }, ExportValues.Concat(Language).ToList(), ExportValues.Concat(Language).ToList(),
                Pairs((Language, CliText.Languages)), Array.Empty<string>()),
            new Command(new[] { "tamamlama", "completion" }, Array.Empty<string>(), Array.Empty<string>(),
                new Dictionary<string, IReadOnlyList<string>>(), Shells),
            new Command(new[] { "gunluk" }, Language, Language, Pairs((Language, CliText.Languages)), Array.Empty<string>()),
            new Command(new[] { "yardim", "help" }, Language, Language, Pairs((Language, CliText.Languages)), Array.Empty<string>()),
            new Command(new[] { "surum", "version" }, Array.Empty<string>(), Array.Empty<string>(),
                new Dictionary<string, IReadOnlyList<string>>(), Array.Empty<string>())
        };
    }

    public static IEnumerable<string> CommandNames => Commands.SelectMany(c => c.Names);

    public static string Script(string shell) => shell switch
    {
        "bash" => Bash(),
        "zsh" => Zsh(),
        "fish" => Fish(),
        "powershell" => PowerShell(),
        _ => throw new ArgumentOutOfRangeException(nameof(shell))
    };

    private static string Words(IEnumerable<string> words) => string.Join(" ", words);

    private static string BashBody()
    {
        var sb = new StringBuilder();
        sb.Append("_vidshrink() {\n");
        sb.Append("    local cur prev cmd opts\n");
        sb.Append("    COMPREPLY=()\n");
        sb.Append("    cur=\"${COMP_WORDS[COMP_CWORD]}\"\n");
        sb.Append("    prev=\"${COMP_WORDS[COMP_CWORD-1]}\"\n");
        sb.Append("    if [[ $COMP_CWORD -eq 1 ]]; then\n");
        sb.Append("        COMPREPLY=( $(compgen -W \"").Append(Words(CommandNames)).Append("\" -- \"$cur\") )\n");
        sb.Append("        return 0\n");
        sb.Append("    fi\n");
        sb.Append("    cmd=\"${COMP_WORDS[1]}\"\n");
        sb.Append("    opts=\"\"\n");
        sb.Append("    case \"$cmd\" in\n");
        foreach (var command in Commands)
        {
            sb.Append("        ").Append(string.Join("|", command.Names)).Append(")\n");
            if (command.Positional.Count > 0)
            {
                sb.Append("            COMPREPLY=( $(compgen -W \"").Append(Words(command.Positional)).Append("\" -- \"$cur\") )\n");
                sb.Append("            return 0\n");
                sb.Append("            ;;\n");
                continue;
            }
            if (command.Values.Count > 0)
            {
                sb.Append("            case \"$prev\" in\n");
                foreach (var group in command.Values.GroupBy(pair => pair.Value, pair => pair.Key))
                {
                    sb.Append("                ").Append(string.Join("|", group)).Append(")\n");
                    sb.Append("                    COMPREPLY=( $(compgen -W \"").Append(Words(group.Key)).Append("\" -- \"$cur\") )\n");
                    sb.Append("                    return 0\n");
                    sb.Append("                    ;;\n");
                }
                sb.Append("            esac\n");
            }
            sb.Append("            opts=\"").Append(Words(command.Options)).Append("\"\n");
            sb.Append("            ;;\n");
        }
        sb.Append("    esac\n");
        sb.Append("    if [[ \"$cur\" == -* ]]; then\n");
        sb.Append("        COMPREPLY=( $(compgen -W \"$opts\" -- \"$cur\") )\n");
        sb.Append("        return 0\n");
        sb.Append("    fi\n");
        sb.Append("    COMPREPLY=( $(compgen -f -- \"$cur\") )\n");
        sb.Append("}\n");
        sb.Append("complete -o filenames -o bashdefault -F _vidshrink vidshrink vidshrink-cli\n");
        return sb.ToString();
    }

    private static string Bash() => "# vidshrink bash tamamlama: source <(vidshrink tamamlama bash)\n" + BashBody();

    private static string Zsh()
        => "# vidshrink zsh tamamlama: source <(vidshrink tamamlama zsh)\n"
           + "(( $+functions[compdef] )) || { autoload -Uz compinit && compinit; }\n"
           + "autoload -U +X bashcompinit && bashcompinit\n"
           + BashBody();

    private static string Fish()
    {
        var sb = new StringBuilder();
        sb.Append("# vidshrink fish tamamlama: vidshrink tamamlama fish | source\n");
        sb.Append("complete -c vidshrink -n \"__fish_use_subcommand\" -f -a \"").Append(Words(CommandNames)).Append("\"\n");
        foreach (var command in Commands)
        {
            var condition = "__fish_seen_subcommand_from " + string.Join(" ", command.Names);
            if (command.Positional.Count > 0)
                sb.Append("complete -c vidshrink -n \"").Append(condition).Append("\" -f -a \"").Append(Words(command.Positional)).Append("\"\n");
            foreach (var option in command.Options)
            {
                sb.Append("complete -c vidshrink -n \"").Append(condition).Append("\" ")
                  .Append(option.StartsWith("--", StringComparison.Ordinal) ? "-l " + option[2..] : "-s " + option[1..]);
                if (command.Values.TryGetValue(option, out var values)) sb.Append(" -x -a \"").Append(Words(values)).Append('"');
                else if (command.ValueOptions.Contains(option)) sb.Append(" -r");
                sb.Append('\n');
            }
        }
        sb.Append("complete -c vidshrink-cli -w vidshrink\n");
        return sb.ToString();
    }

    private static string PsList(IEnumerable<string> words) => "@(" + string.Join(", ", words.Select(w => "'" + w.Replace("'", "''") + "'")) + ")";

    private static string PowerShell()
    {
        var sb = new StringBuilder();
        sb.Append("# vidshrink PowerShell tamamlama: vidshrink tamamlama powershell | Out-String | Invoke-Expression\n");
        sb.Append("Register-ArgumentCompleter -Native -CommandName 'vidshrink', 'vidshrink.exe', 'vidshrink-cli' -ScriptBlock {\n");
        sb.Append("    param($wordToComplete, $commandAst, $cursorPosition)\n");
        sb.Append("    $commands = ").Append(PsList(CommandNames)).Append('\n');
        sb.Append("    $options = @{\n");
        foreach (var command in Commands)
            foreach (var name in command.Names)
                sb.Append("        '").Append(name).Append("' = ").Append(PsList(command.Options)).Append('\n');
        sb.Append("    }\n");
        sb.Append("    $positional = @{\n");
        foreach (var command in Commands.Where(c => c.Positional.Count > 0))
            foreach (var name in command.Names)
                sb.Append("        '").Append(name).Append("' = ").Append(PsList(command.Positional)).Append('\n');
        sb.Append("    }\n");
        sb.Append("    $values = @{\n");
        foreach (var command in Commands)
            foreach (var name in command.Names)
                foreach (var pair in command.Values)
                    sb.Append("        '").Append(name).Append('|').Append(pair.Key).Append("' = ").Append(PsList(pair.Value)).Append('\n');
        sb.Append("    }\n");
        sb.Append("    $elements = @($commandAst.CommandElements | Where-Object { $_.Extent.EndOffset -le $cursorPosition } | Select-Object -Skip 1 | ForEach-Object { $_.ToString() })\n");
        sb.Append("    if ($wordToComplete -ne '' -and $elements.Count -gt 0) { $elements = @($elements | Select-Object -SkipLast 1) }\n");
        sb.Append("    $candidates = @()\n");
        sb.Append("    if ($elements.Count -eq 0) { $candidates = $commands }\n");
        sb.Append("    else {\n");
        sb.Append("        $cmd = $elements[0]\n");
        sb.Append("        $key = $cmd + '|' + $elements[$elements.Count - 1]\n");
        sb.Append("        if ($values.ContainsKey($key)) { $candidates = $values[$key] }\n");
        sb.Append("        elseif ($elements.Count -eq 1 -and $positional.ContainsKey($cmd)) { $candidates = $positional[$cmd] }\n");
        sb.Append("        elseif ($wordToComplete.StartsWith('-') -and $options.ContainsKey($cmd)) { $candidates = $options[$cmd] }\n");
        sb.Append("    }\n");
        sb.Append("    $candidates | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object {\n");
        sb.Append("        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)\n");
        sb.Append("    }\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}
