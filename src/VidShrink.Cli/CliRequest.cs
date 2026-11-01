using System.Globalization;
using VidShrink.Core;

namespace VidShrink.Cli;

public enum CliCommand { Help, Version, Shrink, Plan, Watch, Gunluk, Profiller, Convert, Completion, Record, Edit }

public enum CliCodec { Auto, H264, Hevc, Av1, Vp9 }

public static class ExitCodes
{
    public const int InBand = 0;
    public const int Error = 1;
    public const int UnderBand = 2;
    public const int CeilingExceeded = 3;
    public const int WatchFailures = 4;
    public const int Usage = 64;
    public const int Cancelled = 130;
}

public sealed record CliRequest
{
    public CliCommand Command { get; init; } = CliCommand.Help;
    public string? Input { get; init; }
    public double? TargetMb { get; init; }
    public double? Quality { get; init; }
    public CliCodec Codec { get; init; } = CliCodec.Auto;
    public string? Output { get; init; }
    public string? OutputDirectory { get; init; }
    public double? PollSeconds { get; init; }
    public bool Once { get; init; }
    public bool Json { get; init; }
    public bool JsonLines { get; init; }
    public bool SkipMeasurement { get; init; }
    public bool MeasureVmaf { get; init; }

    /// <summary><c>--olcerek-ara</c>: <c>--kalite</c>'nin hedefini en cok dort kisa ornek kodlamayla olcerek arar.</summary>
    public bool SearchByMeasure { get; init; }
    public bool Fast { get; init; }
    public string? PreferredLanguage { get; init; }
    public double? TrimStartSeconds { get; init; }
    public double? TrimEndSeconds { get; init; }
    public double? Crf { get; init; }
    public string? Preset { get; init; }

    /// <summary>Olcek carpani (<c>--modul</c>). Bos birakilirsa motorun varsayilani.</summary>
    public int? ScaleModulus { get; init; }

    /// <summary>
    /// <c>--kes</c>'in kare yazimi (<c>300f-900f</c>). Kare saniyeye ayristirmada degil
    /// <see cref="Resolved"/>'da cevrilir: donusum kaynagin kare hizini ister, ayristirici
    /// dosyayi hic gormez. Iki uc bagimsiz — bir ucu kare, obur ucu saat olabilir.
    /// </summary>
    public double? TrimStartFrame { get; init; }
    public double? TrimEndFrame { get; init; }

    /// <summary>HandBrake'in <c>-c 1-3</c> karsiligi; tek bolum verilirse iki alan da ayni.</summary>
    public int? ChapterFrom { get; init; }
    public int? ChapterTo { get; init; }

    /// <summary>
    /// <c>--tarama</c>: kaynagin baslik envanterini basar, hicbir sey kodlamaz. Bayrak
    /// komutu degistirmez; kodlamayi durduran sart tek yerde, <see cref="CliApp"/>'te.
    /// </summary>
    public bool Scan { get; init; }

    /// <summary><c>--baslik</c>: kullanicinin sectigi baslik numarasi.</summary>
    public int? Title { get; init; }

    /// <summary><c>--ana-icerik</c>: en uzun basligi sec.</summary>
    public bool MainFeature { get; init; }

    /// <summary><c>--asgari-sure</c>: bu sureden kisa basliklar envanterden duser.</summary>
    public double? MinDurationSeconds { get; init; }

    /// <summary><c>--azami-sure</c>: bu sureden uzun basliklar envanterden duser.</summary>
    public double? MaxDurationSeconds { get; init; }

    /// <summary>
    /// <c>--suzgec</c>: cozumlenmis suzgec secenekleri. Ayristirma komut satiri okunurken yapilir,
    /// bozuk dizge komutu daha kosum baslamadan durdurur. <c>null</c> ise motor bugunku
    /// varsayilani kullanir; bos dizge de ayni kapiya cikar.
    /// </summary>
    public VideoFilterOptions? Filters { get; init; }

    /// <summary>
    /// <c>--kirp</c>: siyah bant yoklamasi kosar ve bulunan dikdortgen suzgec kirpmasina girer.
    /// <c>--suzgec crop=</c> ile birlikte verilirse elle verilen kazanir, yoklama hic kosmaz.
    /// </summary>
    public bool AutoCrop { get; init; }

    /// <summary>
    /// <c>--kirpma-kipi</c> (HandBrake <c>--crop-mode</c>); verilmediyse <c>null</c>.
    /// <see cref="CropMode.Auto"/> ve <see cref="CropMode.Conservative"/> yoklamayi kendileri acar.
    /// </summary>
    public CropMode? CropMode { get; init; }

    /// <summary>Siyah bant yoklamasi kosacak mi: <c>--kirp</c> ya da yoklayan bir kirpma kipi.</summary>
    public bool ProbesCrop => AutoCrop || CropMode is Core.CropMode.Auto or Core.CropMode.Conservative;

    /// <summary>
    /// <c>--profil</c>: on ayar kutuphanesindeki bir profilin kimligi. Profil plan
    /// secenegine taban olur; elle verilen her bayrak onun ustune yazar.
    /// </summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// <c>--profil-dosyasi</c>: VidShrink ya da HandBrake ön ayar dosyası. İçindeki profiller
    /// <see cref="ProfileId"/> aramasında kütüphaneden önce gelir; tek profil varsa kimlik gerekmez.
    /// </summary>
    public string? PresetFile { get; init; }

    /// <summary>
    /// <see cref="ProfileId"/> kutuphanede bulununca burada tasinir. Ayristirma kullanici
    /// profillerini goremez (dosya servisten gelir), o yuzden cozum <c>CliApp</c>te olur.
    /// </summary>
    public PresetProfile? Profile { get; init; }

    /// <summary><c>--ses-kodek</c>: yeniden kodlanan sesin kodegi; <c>null</c> ise motor secer.</summary>
    public AudioCodecChoice? AudioCodec { get; init; }

    /// <summary><c>--ses-normal</c>: yeniden kodlanan sese <c>loudnorm</c>.</summary>
    public bool AudioLoudnorm { get; init; }

    /// <summary><c>--ses-temizle</c>: yeniden kodlanan sese gurultu suzgeci (<c>afftdn</c>).</summary>
    public bool AudioDenoise { get; init; }

    /// <summary><c>--ses-kazanc</c>: yeniden kodlanan sese sabit kazanc, dB.</summary>
    public double? AudioGainDb { get; init; }

    /// <summary><c>--altyazi</c>: ciktiya iz olarak eklenecek dosyalar, verildigi sirayla.</summary>
    public IReadOnlyList<string> SubtitleFiles { get; init; } = Array.Empty<string>();

    /// <summary><c>--yan-altyazi</c>: girdinin yanindaki ayni adli altyazilar da eklenir.</summary>
    public bool SidecarSubtitles { get; init; }

    /// <summary><c>--ses-dosyasi</c>: ikinci girdi olarak eklenen dis ses dosyasi.</summary>
    public string? AudioFile { get; init; }

    /// <summary><c>--ses-degistir</c>: dis ses kaynagin sesinin yanina degil yerine konur.</summary>
    public bool ReplaceAudio { get; init; }

    /// <summary><c>--yak</c>: kaynagin bu altyazisi (1 tabanli) goruntuye yakilir.</summary>
    public int? BurnSubtitle { get; init; }

    /// <summary>
    /// <c>--yak-srt</c> (HandBrake <c>--srt-burn</c>) ya da <c>--yak-ass</c> (<c>--ssa-burn</c>):
    /// goruntuye yakilacak dis altyazi dosyasi. <see cref="ResolvedSubtitles"/> tam yola cevirir.
    /// </summary>
    public string? BurnFile { get; init; }

    /// <summary>
    /// <c>--altyazi-tara</c> (HandBrake <c>--subtitle scan</c>): yabanci ses altyazisi aranir ve
    /// bulunursa <see cref="BurnSubtitle"/> olarak yakilir; karar <see cref="ForeignAudioSearch"/>'te.
    /// </summary>
    public bool SubtitleScan { get; init; }

    /// <summary><c>--yak-yazi-tipi</c>: yakilan metin altyazinin yazi tipi (<c>force_style</c> <c>FontName</c>).</summary>
    public string? BurnFont { get; init; }

    /// <summary><c>--yak-boyut</c>: yakilan metin altyazinin boyutu (<c>force_style</c> <c>FontSize</c>).</summary>
    public int? BurnFontSize { get; init; }

    /// <summary><c>--yak-kutu</c>: yakilan metin altyazinin arkasina opak kutu (<c>BorderStyle=3</c>).</summary>
    public bool BurnBox { get; init; }

    /// <summary><c>--meta-yok</c>: kaynagin kap etiketleri ciktiya tasinmaz.</summary>
    public bool DropMetadata { get; init; }

    /// <summary><c>--altyazi-dil</c>: yalniz bu dillerdeki kaynak altyazilari tasinir.</summary>
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = Array.Empty<string>();

    /// <summary><c>--ilk-altyazi</c>: kaynagin yalniz ilk altyazisi tasinir.</summary>
    public bool FirstSubtitleOnly { get; init; }

    /// <summary><c>--sabit-kare</c> (HandBrake <c>--cfr</c>): cikti sabit kare hizinda yazilir.</summary>
    public bool ConstantFrameRate { get; init; }

    /// <summary><c>--tavan-kare</c> (HandBrake <c>--pfr</c>): degisken hiz korunur, <see cref="FrameRate"/> tavandir.</summary>
    public bool PeakFrameRate { get; init; }

    /// <summary>
    /// <c>--kare-hizi</c> (HandBrake <c>--rate</c>): kare hizi tavani. Kip verilmezse HandBrake'teki
    /// gibi tavanli degisken hiz sayilir.
    /// </summary>
    public double? FrameRate { get; init; }

    /// <summary><c>--ses-hizi</c> (HandBrake <c>--arate</c>): yeniden kodlanan sesin ornekleme hizi, Hz; <c>auto</c> ise <c>null</c>.</summary>
    public int? AudioSampleRate { get; init; }

    /// <summary><c>--ses-drc</c> (HandBrake <c>--drc</c>): AC-3/E-AC-3 kaynagin cozucusune DRC olcegi.</summary>
    public double? AudioDrcScale { get; init; }

    /// <summary><c>--oncelik</c>: kodlama surecinin isletim sistemi onceligi.</summary>
    public EncodePriority Priority { get; init; } = EncodePriority.Normal;

    /// <summary><c>kaydet --sure</c>: kaydın süre sınırı, saniye. Yoksa kayıt Ctrl+C ya da kayıtlı sınırla biter.</summary>
    public double? RecordSeconds { get; init; }

    /// <summary><c>teslim --kip</c>: projenin kayitli kipinin yerine gecen teslim kipi. Yoksa projenin kipi kalir.</summary>
    public VidShrink.Core.Editing.ExportMode? EditMode { get; init; }

    /// <summary><c>kaydet --ayarlar</c>: kaydedici ayar dosyası. Yoksa pencerenin kendi dosyası okunur.</summary>
    public string? RecordSettings { get; init; }

    /// <summary><c>--is-parcacigi</c>: siniri dinleyen kodlayiciya yazilan <c>-threads</c>; digerlerinde yok sayilir.</summary>
    public int? EncoderThreads { get; init; }

    /// <summary><c>donustur</c>: Dönüştür panelinin seçimleri, panelin ön ayar biçiminde; plan <see cref="ConversionPresetPlan.ToPlan"/> ile kurulur.</summary>
    public ConversionPreset? Conversion { get; init; }

    /// <summary><c>donustur --ek-arguman</c>: <see cref="CustomArguments"/> ile bölünmüş ek ffmpeg argümanları; çıktı yolundan önce yazılır.</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = Array.Empty<string>();

    /// <summary><c>donustur --etiket-*</c>, <c>--kapak</c>, <c>--onar</c>, <c>--bolum-ekle</c>, <c>--bolumsuz</c>: Dönüştür'ün etiket ve bölüm açılırlarının seçimi.</summary>
    public CliConvertTags ConvertTags { get; init; } = new();

    /// <summary><c>profiller --disa-aktar</c>: profillerin yazılacağı dosya.</summary>
    public string? ExportPath { get; init; }

    /// <summary><c>profiller --disa-aktar</c> ile verilen <c>--profil</c> kimlikleri; boşsa kullanıcının bütün profilleri.</summary>
    public IReadOnlyList<string> ExportIds { get; init; } = Array.Empty<string>();

    /// <summary><c>tamamlama</c>: betiği basılacak kabuk (<see cref="CliCompletion.Shells"/>).</summary>
    public string? Shell { get; init; }

    /// <summary>
    /// <see cref="SubtitleFiles"/> ve <see cref="SidecarSubtitles"/>'in diskten cozulmus hali;
    /// <see cref="ResolvedSubtitles"/> doldurur.
    /// </summary>
    public IReadOnlyList<ExternalSubtitle> ExternalSubtitles { get; init; } = Array.Empty<ExternalSubtitle>();

    /// <summary><see cref="AudioFile"/>'in diskten cozulmus hali; <see cref="ResolvedSubtitles"/> doldurur.</summary>
    public ExternalAudio? ExternalAudio { get; init; }

    /// <summary>
    /// Dis altyazi dosyalarini okur ve yakilacak izi kaynakta dogrular. Donen deger hata
    /// anahtaridir, <paramref name="argument"/> iletideki yer tutucudur; <c>null</c> ise
    /// <paramref name="resolved"/> kullanilabilir.
    /// </summary>
    public string? ResolvedSubtitles(MediaInfo info, out CliRequest resolved, out string? argument)
    {
        resolved = this;
        argument = null;
        var list = new List<ExternalSubtitle>();
        foreach (var path in SubtitleFiles)
        {
            if (ExternalSubtitle.FromFile(path) is not { } file)
            {
                argument = path;
                return "error.bad-subtitle";
            }
            list.Add(file);
        }
        if (SidecarSubtitles && Input is not null)
            list.AddRange(ExternalSubtitle.Sidecars(info.FilePath)
                .Where(side => !list.Any(file => string.Equals(file.Path, side.Path, StringComparison.OrdinalIgnoreCase))));
        if (BurnSubtitle is int burn)
        {
            var burnFilters = new VideoFilterOptions { BurnSubtitle = burn - 1 };
            if (VideoFilterChain.Validate(info, burnFilters).Count > 0)
            {
                argument = burn.ToString(CultureInfo.InvariantCulture);
                return "error.bad-burn";
            }
        }
        var burnFile = BurnFile;
        if (burnFile is not null)
        {
            if (!File.Exists(burnFile))
            {
                argument = burnFile;
                return "error.burn-file-missing";
            }
            burnFile = Path.GetFullPath(burnFile);
        }
        ExternalAudio? externalAudio = null;
        if (AudioFile is not null && (externalAudio = ExternalAudio.FromFile(AudioFile, ReplaceAudio)) is null)
        {
            argument = AudioFile;
            return "error.bad-audio-file";
        }
        resolved = this with { ExternalSubtitles = list, BurnFile = burnFile, ExternalAudio = externalAudio };
        return null;
    }

    /// <summary>
    /// Kare ve bolum kollarini kaynaktan cozup kesit pencerisini saniyeye indirir. Donen
    /// metin hata anahtaridir; <c>null</c> ise <paramref name="resolved"/> kullanilabilir.
    /// </summary>
    public string? Resolved(MediaInfo info, out CliRequest resolved)
    {
        resolved = this;
        if (ChapterFrom is int from)
        {
            var to = ChapterTo ?? from;
            if (info.Chapters.Count == 0) return "error.no-chapters";
            if (from < 1 || to > info.Chapters.Count) return "error.bad-chapter";
            resolved = this with
            {
                TrimStartSeconds = info.Chapters[from - 1].StartSeconds,
                TrimEndSeconds = info.Chapters[to - 1].EndSeconds
            };
            return null;
        }

        if (TrimStartFrame is null && TrimEndFrame is null) return null;
        if (!double.IsFinite(info.Fps) || info.Fps <= 0) return "error.no-fps";
        var start = TrimStartFrame is double sf ? sf / info.Fps : TrimStartSeconds;
        var end = TrimEndFrame is double ef ? ef / info.Fps : TrimEndSeconds;
        if (start is double a && end is double b && b <= a) return "error.bad-range";
        resolved = this with { TrimStartSeconds = start, TrimEndSeconds = end };
        return null;
    }

    /// <summary>
    /// <paramref name="sourceDurationSeconds"/> kesitin acik ucunu kapatir (<c>--kes 10-</c>);
    /// 0 verilirse kesit yalnizca acikca verilen iki ucla kurulur.
    /// </summary>
    public PlanOptions ToPlanOptions(double targetMb, double sourceDurationSeconds = 0)
    {
        var options = new PlanOptions
        {
            TargetMb = targetMb,
            Intent = Intent.Sharing,
            Codec = Codec switch
            {
                CliCodec.H264 => CodecPreference.Compatible,
                CliCodec.Av1 => CodecPreference.MaxCompression,
                _ => CodecPreference.Auto
            },
            AllowResolutionDrop = true,
            AllowFpsDrop = true,
            HdrPolicy = HdrPolicy.Preserve,
            FillPolicy = FillPolicy.FillTarget,
            SpeedMode = Fast ? SpeedMode.Fast : SpeedMode.Quality,
            PreferredLanguage = PreferredLanguage,
            Priority = Priority,
            EncoderThreads = EncoderThreads
        };
        if (Profile is { } profil)
        {
            options.Intent = profil.Intent;
            options.FillPolicy = profil.Fill;
            options.FixedResolution = profil.MaxShortEdge;
            options.LockedAudioKbps = profil.AudioKbps;
            if (Codec == CliCodec.Auto)
            {
                options.Codec = profil.Codec;
                options.LockedCodec = profil.LockedCodec;
            }
        }

        options.DeliveredContainer = Output is { } cikti
            ? StreamMapping.ContainerOf(cikti)
            : PresetLibrary.DeliveredContainer(Profile?.Container);

        if (Filters is { } suzgecler) options.Filters = suzgecler;
        if (BurnSubtitle is int yak) options.Filters = (options.Filters ?? VideoFilterOptions.Default) with { BurnSubtitle = yak - 1 };
        if (BurnFile is { } yakDosya) options.Filters = (options.Filters ?? VideoFilterOptions.Default) with { BurnFile = yakDosya };
        if (BurnFont is not null || BurnFontSize is not null || BurnBox)
            options.Filters = (options.Filters ?? VideoFilterOptions.Default) with { BurnFont = BurnFont, BurnFontSize = BurnFontSize, BurnBox = BurnBox };
        if (AudioCodec is { } sesKodek) options.AudioCodec = sesKodek;
        options.AudioLoudnorm = AudioLoudnorm;
        options.AudioDenoise = AudioDenoise;
        options.AudioGainDb = AudioGainDb;
        options.AudioSampleRate = AudioSampleRate;
        options.AudioDrcScale = AudioDrcScale;
        options.ExternalSubtitles = ExternalSubtitles;
        options.ExternalAudio = ExternalAudio;
        options.DropMetadata = DropMetadata || Profile is { DropMetadata: true };
        options.SubtitleLanguages = SubtitleLanguages;
        options.FirstSubtitleOnly = FirstSubtitleOnly;
        options.MaxFps = FrameRate;
        options.FrameRate = ConstantFrameRate ? FrameRateMode.Constant
            : PeakFrameRate || FrameRate is not null ? FrameRateMode.Peak
            : FrameRateMode.Auto;
        if (Codec == CliCodec.Hevc) options.LockedCodec = "libx265";
        if (Codec == CliCodec.Vp9) options.LockedCodec = "libvpx-vp9";
        options.LockedCrf = Crf;
        options.LockedPreset = Preset;
        if (ScaleModulus is { } modul) options.ScaleModulus = modul;
        options.Trim = TrimWindow.Of(TrimStartSeconds, TrimEndSeconds, sourceDurationSeconds);
        return options;
    }
}

public sealed record CliParseResult(CliRequest? Request, string? ErrorKey, string? ErrorArgument)
{
    public bool Ok => Request is not null;
}

public static class CliParser
{
    public const double MinQuality = 1;
    public const double MaxQuality = 100;
    public const double MinCrf = 0;
    public const double MaxCrf = 63;
    public const double MinFrameRate = 1;
    public const double MaxFrameRate = 240;

    /// <summary>Motorun yazabildigi kaplar; <c>--cikti</c> baska bir uzanti tasiyorsa komut durur.</summary>
    public static readonly IReadOnlySet<string> OutputExtensions =
        new HashSet<string>(StringComparer.Ordinal) { ".mp4", ".mkv", ".webm", ".mov" };

    /// <summary>Yalniz tek dosya komutlarinda anlamli secenekler; <c>izle</c> bunlari bilir ama kabul etmez.</summary>
    public static readonly IReadOnlySet<string> NotInWatch = new HashSet<string>(StringComparer.Ordinal)
    {
        "--crf", "--on-ayar", "--preset", "--modul", "--modulus", "--kes", "--cut", "--bolum", "--chapters",
        "--profil", "--profile", "--profil-dosyasi", "--preset-file", "--kirp", "--crop", "--kirpma-kipi", "--crop-mode", "--tarama", "--scan",
        "--baslik", "--title", "--ana-icerik", "--main-feature", "--asgari-sure", "--min-duration",
        "--azami-sure", "--max-duration",
        "--suzgec", "--filters", "--ses-kodek", "--audio-codec", "--ses-normal", "--loudnorm", "--ses-temizle", "--denoise",
        "--ses-kazanc", "--gain", "--altyazi", "--subtitle", "--yan-altyazi", "--sidecar-subtitles",
        "--ses-dosyasi", "--audio-file", "--ses-degistir", "--replace-audio",
        "--yak", "--burn", "--yak-srt", "--srt-burn", "--yak-ass", "--ssa-burn", "--altyazi-tara", "--subtitle-scan",
        "--yak-yazi-tipi", "--burn-font", "--yak-boyut", "--burn-size", "--yak-kutu", "--burn-box",
        "--meta-yok", "--no-metadata",
        "--altyazi-dil", "--subtitle-lang", "--ilk-altyazi", "--first-subtitle",
        "--sabit-kare", "--cfr", "--tavan-kare", "--pfr", "--kare-hizi", "--fps",
        "--ses-hizi", "--arate", "--ses-drc", "--drc",
    };

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return Success(new CliRequest { Command = CliCommand.Help });

        var head = args[0];
        if (head is "-h" or "--yardim" or "help" or "yardim" or "--help")
            return Success(new CliRequest { Command = CliCommand.Help });
        if (head is "-v" or "--surum" or "version" or "surum" or "--version")
            return Success(new CliRequest { Command = CliCommand.Version });
        if (head is "--gunluk" or "--log" or "gunluk")
            return Success(new CliRequest { Command = CliCommand.Gunluk });
        if (head is "profiller" or "presets" or "--profiller" or "--presets")
            return ParseProfiller(args);
        if (head is "tamamlama" or "completion")
            return args.Count == 2 && CliCompletion.TryShell(args[1], out var kabuk)
                ? Success(new CliRequest { Command = CliCommand.Completion, Shell = kabuk })
                : Fail("error.bad-shell", args.Count > 1 ? args[1] : "-");
        if (head is "donustur" or "convert")
            return CliConvert.Parse(args);
        if (head is "kaydet" or "record")
            return CliRecord.Parse(args);
        if (head is "teslim" or "export")
            return CliEdit.Parse(args);

        var command = head switch
        {
            "kucult" or "shrink" => CliCommand.Shrink,
            "plan" => CliCommand.Plan,
            "izle" or "watch" => CliCommand.Watch,
            _ => (CliCommand?)null
        };
        if (command is null) return Fail("error.unknown-command", head);

        var request = new CliRequest { Command = command.Value };
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--yardim" or "--help":
                    return Success(new CliRequest { Command = CliCommand.Help });
                case "--hedef" or "--target":
                    if (!TryValue(args, ref i, out var target)) return Fail("error.missing-value", arg);
                    if (!TryParseSize(target, out var mb)) return Fail("error.bad-size", target);
                    request = request with { TargetMb = mb };
                    break;
                case "--kalite" or "--quality":
                    if (!TryValue(args, ref i, out var quality)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(quality, out var score) || score < MinQuality || score > MaxQuality)
                        return Fail("error.bad-quality", quality);
                    request = request with { Quality = score };
                    break;
                case "--kodek" or "--codec":
                    if (!TryValue(args, ref i, out var codec)) return Fail("error.missing-value", arg);
                    if (!TryParseCodec(codec, out var parsed)) return Fail("error.bad-codec", codec);
                    request = request with { Codec = parsed };
                    break;
                case "--crf" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var crf)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(crf, out var crfValue) || crfValue < MinCrf || crfValue > MaxCrf)
                        return Fail("error.bad-crf", crf);
                    request = request with { Crf = crfValue };
                    break;
                case "--on-ayar" or "--preset" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var preset)) return Fail("error.missing-value", arg);
                    if (!FfmpegArguments.IsKnownPreset(preset)) return Fail("error.bad-preset", preset);
                    request = request with { Preset = preset };
                    break;
                case "--modul" or "--modulus" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var modul)) return Fail("error.missing-value", arg);
                    if (!int.TryParse(modul, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var modulDegeri)
                        || !Olcek.GecerliModul(modulDegeri))
                        return Fail("error.bad-modulus", modul);
                    request = request with { ScaleModulus = modulDegeri };
                    break;
                case "--cikti" or "--output" or "-o":
                    if (!TryValue(args, ref i, out var output)) return Fail("error.missing-value", arg);
                    if (command != CliCommand.Watch && !OutputExtensions.Contains(Path.GetExtension(output).ToLowerInvariant()))
                        return Fail("error.bad-output-extension", output);
                    request = request with { Output = output };
                    break;
                case "--kes" or "--cut" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var cut)) return Fail("error.missing-value", arg);
                    if (request.ChapterFrom is not null) return Fail("error.chapter-and-cut", cut);
                    if (!TryParseRange(cut, out var cutStart, out var cutEnd, out var cutStartFrame, out var cutEndFrame))
                        return Fail("error.bad-range", cut);
                    request = request with
                    {
                        TrimStartSeconds = cutStart, TrimEndSeconds = cutEnd,
                        TrimStartFrame = cutStartFrame, TrimEndFrame = cutEndFrame
                    };
                    break;
                case "--bolum" or "--chapters" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var chapter)) return Fail("error.missing-value", arg);
                    if (request.TrimStartSeconds is not null || request.TrimStartFrame is not null)
                        return Fail("error.chapter-and-cut", chapter);
                    if (!TryParseChapters(chapter, out var chapterFrom, out var chapterTo))
                        return Fail("error.bad-chapter", chapter);
                    request = request with { ChapterFrom = chapterFrom, ChapterTo = chapterTo };
                    break;
                case "--aralik" or "--interval" when command == CliCommand.Watch:
                    if (!TryValue(args, ref i, out var interval)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(interval, out var seconds) || seconds <= 0 || seconds > 86400) return Fail("error.bad-interval", interval);
                    request = request with { PollSeconds = seconds };
                    break;
                case "--bir-kez" or "--once" when command == CliCommand.Watch:
                    request = request with { Once = true };
                    break;
                case "--profil" or "--profile" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var profil)) return Fail("error.missing-value", arg);
                    request = request with { ProfileId = profil };
                    break;
                case "--profil-dosyasi" or "--preset-file" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var profilDosyasi)) return Fail("error.missing-value", arg);
                    request = request with { PresetFile = profilDosyasi };
                    break;
                case "--kirp" or "--crop" when command != CliCommand.Watch:
                    request = request with { AutoCrop = true };
                    break;
                case "--kirpma-kipi" or "--crop-mode" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var kirpmaKipi)) return Fail("error.missing-value", arg);
                    if (!TryParseCropMode(kirpmaKipi, out var kip)) return Fail("error.bad-crop-mode", kirpmaKipi);
                    request = request with { CropMode = kip };
                    break;
                case "--tarama" or "--scan" when command != CliCommand.Watch:
                    request = request with { Scan = true };
                    break;
                case "--baslik" or "--title" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var baslik)) return Fail("error.missing-value", arg);
                    if (!int.TryParse(baslik, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baslikNo)
                        || baslikNo < 1)
                        return Fail("error.bad-title", baslik);
                    if (request.MainFeature) return Fail("error.title-and-main-feature", baslik);
                    request = request with { Title = baslikNo };
                    break;
                case "--ana-icerik" or "--main-feature" when command != CliCommand.Watch:
                    if (request.Title is not null) return Fail("error.title-and-main-feature", arg);
                    request = request with { MainFeature = true };
                    break;
                case "--asgari-sure" or "--min-duration" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var asgari)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(asgari, out var asgariSn) || asgariSn < 0 || asgariSn > 86400)
                        return Fail("error.bad-min-duration", asgari);
                    request = request with { MinDurationSeconds = asgariSn };
                    break;
                case "--azami-sure" or "--max-duration" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var azami)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(azami, out var azamiSn) || azamiSn <= 0 || azamiSn > 86400)
                        return Fail("error.bad-max-duration", azami);
                    request = request with { MaxDurationSeconds = azamiSn };
                    break;
                case "--suzgec" or "--filters" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var suzgec)) return Fail("error.missing-value", arg);
                    VideoFilterOptions cozulen;
                    try
                    {
                        cozulen = VideoFilterChain.Parse(suzgec);
                    }
                    catch (ArgumentException)
                    {
                        return Fail("error.bad-filter", suzgec);
                    }
                    request = request with { Filters = cozulen };
                    break;
                case "--ses-kodek" or "--audio-codec" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var sesKodek)) return Fail("error.missing-value", arg);
                    AudioCodecChoice? secilen = sesKodek.ToLowerInvariant() switch
                    {
                        "aac" => AudioCodecChoice.Aac,
                        "ac3" => AudioCodecChoice.Ac3,
                        "eac3" => AudioCodecChoice.Eac3,
                        "flac" => AudioCodecChoice.Flac,
                        _ => null
                    };
                    if (secilen is null) return Fail("error.bad-audio-codec", sesKodek);
                    request = request with { AudioCodec = secilen };
                    break;
                case "--ses-temizle" or "--denoise" when command != CliCommand.Watch:
                    request = request with { AudioDenoise = true };
                    break;
                case "--ses-normal" or "--loudnorm" when command != CliCommand.Watch:
                    request = request with { AudioLoudnorm = true };
                    break;
                case "--ses-kazanc" or "--gain" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var kazanc)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(kazanc, out var db) || db < StreamMapping.MinGainDb || db > StreamMapping.MaxGainDb)
                        return Fail("error.bad-gain", kazanc);
                    request = request with { AudioGainDb = db };
                    break;
                case "--ses-hizi" or "--arate" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var sesHizi)) return Fail("error.missing-value", arg);
                    if (sesHizi.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        request = request with { AudioSampleRate = null };
                        break;
                    }
                    if (!TryParseNumber(sesHizi, out var khz)) return Fail("error.bad-arate", sesHizi);
                    var hz = (int)Math.Round(khz < 1000 ? khz * 1000 : khz);
                    if (!StreamMapping.SampleRates.Contains(hz)) return Fail("error.bad-arate", sesHizi);
                    request = request with { AudioSampleRate = hz };
                    break;
                case "--ses-drc" or "--drc" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var sesDrc)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(sesDrc, out var drc) || drc < StreamMapping.MinDrcScale || drc > StreamMapping.MaxDrcScale)
                        return Fail("error.bad-drc", sesDrc);
                    request = request with { AudioDrcScale = drc };
                    break;
                case "--altyazi" or "--subtitle" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var altyazi)) return Fail("error.missing-value", arg);
                    if (!ExternalSubtitle.Extensions.Contains(Path.GetExtension(altyazi).ToLowerInvariant()))
                        return Fail("error.bad-subtitle", altyazi);
                    request = request with { SubtitleFiles = request.SubtitleFiles.Append(altyazi).ToList() };
                    break;
                case "--yan-altyazi" or "--sidecar-subtitles" when command != CliCommand.Watch:
                    request = request with { SidecarSubtitles = true };
                    break;
                case "--ses-dosyasi" or "--audio-file" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var sesDosyasi)) return Fail("error.missing-value", arg);
                    if (!ExternalAudio.Extensions.Contains(Path.GetExtension(sesDosyasi).ToLowerInvariant()))
                        return Fail("error.bad-audio-file", sesDosyasi);
                    request = request with { AudioFile = sesDosyasi };
                    break;
                case "--ses-degistir" or "--replace-audio" when command != CliCommand.Watch:
                    request = request with { ReplaceAudio = true };
                    break;
                case "--yak" or "--burn" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var yak)) return Fail("error.missing-value", arg);
                    if (!int.TryParse(yak, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yakNo) || yakNo < 1)
                        return Fail("error.bad-burn", yak);
                    request = request with { BurnSubtitle = yakNo };
                    break;
                case "--yak-srt" or "--srt-burn" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var yakSrt)) return Fail("error.missing-value", arg);
                    if (request.BurnFile is not null) return Fail("error.burn-conflict", null);
                    if (!Path.GetExtension(yakSrt).Equals(".srt", StringComparison.OrdinalIgnoreCase))
                        return Fail("error.bad-burn-file", yakSrt);
                    request = request with { BurnFile = yakSrt };
                    break;
                case "--yak-ass" or "--ssa-burn" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var yakAss)) return Fail("error.missing-value", arg);
                    if (request.BurnFile is not null) return Fail("error.burn-conflict", null);
                    if (Path.GetExtension(yakAss).ToLowerInvariant() is not (".ass" or ".ssa"))
                        return Fail("error.bad-burn-file", yakAss);
                    request = request with { BurnFile = yakAss };
                    break;
                case "--yak-yazi-tipi" or "--burn-font" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var yaziTipi)) return Fail("error.missing-value", arg);
                    if (!VideoFilterChain.IsBurnFontName(yaziTipi)) return Fail("error.bad-burn-font", yaziTipi);
                    request = request with { BurnFont = yaziTipi };
                    break;
                case "--yak-boyut" or "--burn-size" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var yakBoyut)) return Fail("error.missing-value", arg);
                    if (!int.TryParse(yakBoyut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boyut)
                        || boyut < VideoFilterChain.BurnFontSizeMin || boyut > VideoFilterChain.BurnFontSizeMax)
                        return Fail("error.bad-burn-size", yakBoyut);
                    request = request with { BurnFontSize = boyut };
                    break;
                case "--yak-kutu" or "--burn-box" when command != CliCommand.Watch:
                    request = request with { BurnBox = true };
                    break;
                case "--altyazi-tara" or "--subtitle-scan" when command != CliCommand.Watch:
                    request = request with { SubtitleScan = true };
                    break;
                case "--meta-yok" or "--no-metadata" when command != CliCommand.Watch:
                    request = request with { DropMetadata = true };
                    break;
                case "--altyazi-dil" or "--subtitle-lang" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var altyaziDil)) return Fail("error.missing-value", arg);
                    var diller = altyaziDil.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (diller.Length == 0 || diller.Any(dil => dil.Length is < 2 or > 3 || !dil.All(char.IsAsciiLetter)))
                        return Fail("error.bad-subtitle-lang", altyaziDil);
                    request = request with { SubtitleLanguages = diller.Select(dil => dil.ToLowerInvariant()).ToList() };
                    break;
                case "--ilk-altyazi" or "--first-subtitle" when command != CliCommand.Watch:
                    request = request with { FirstSubtitleOnly = true };
                    break;
                case "--sabit-kare" or "--cfr" when command != CliCommand.Watch:
                    request = request with { ConstantFrameRate = true };
                    break;
                case "--tavan-kare" or "--pfr" when command != CliCommand.Watch:
                    request = request with { PeakFrameRate = true };
                    break;
                case "--kare-hizi" or "--fps" when command != CliCommand.Watch:
                    if (!TryValue(args, ref i, out var kareHizi)) return Fail("error.missing-value", arg);
                    if (!TryParseNumber(kareHizi, out var hiz) || hiz < MinFrameRate || hiz > MaxFrameRate)
                        return Fail("error.bad-fps", kareHizi);
                    request = request with { FrameRate = hiz };
                    break;
                case "--json":
                    request = request with { Json = true };
                    break;
                case "--olcumsuz" or "--no-measure":
                    request = request with { SkipMeasurement = true };
                    break;
                case "--vmaf":
                    request = request with { MeasureVmaf = true };
                    break;
                case "--olcerek-ara" or "--search-by-measure":
                    request = request with { SearchByMeasure = true };
                    break;
                case "--hizli" or "--fast":
                    request = request with { Fast = true };
                    break;
                case "--oncelik" or "--priority":
                    if (!TryValue(args, ref i, out var oncelik)) return Fail("error.missing-value", arg);
                    if (!TryParsePriority(oncelik, out var sinif)) return Fail("error.bad-priority", oncelik);
                    request = request with { Priority = sinif };
                    break;
                case "--is-parcacigi" or "--threads":
                    if (!TryValue(args, ref i, out var isParcacigi)) return Fail("error.missing-value", arg);
                    if (!int.TryParse(isParcacigi, NumberStyles.None, CultureInfo.InvariantCulture, out var adet) || adet < 1 || adet > 64)
                        return Fail("error.bad-threads", isParcacigi);
                    request = request with { EncoderThreads = adet };
                    break;
                default:
                    if (command == CliCommand.Watch && NotInWatch.Contains(arg)) return Fail("error.not-in-watch", arg);
                    if (arg.StartsWith('-') && arg.Length > 1) return Fail("error.unknown-option", arg);
                    if (request.Input is not null) return Fail("error.extra-input", arg);
                    request = request with { Input = arg };
                    break;
            }
        }

        if (request.MinDurationSeconds is { } enAz && request.MaxDurationSeconds is { } enCok && enCok < enAz)
            return Fail("error.bad-max-duration", enCok.ToString(CultureInfo.InvariantCulture));
        if (request.BurnSubtitle is not null && request.BurnFile is not null) return Fail("error.burn-conflict", null);
        if (request.ReplaceAudio && request.AudioFile is null) return Fail("error.replace-audio-needs-file", null);
        if (request.SubtitleScan && (request.BurnSubtitle is not null || request.BurnFile is not null)) return Fail("error.scan-burn-conflict", null);
        if ((request.BurnFont is not null || request.BurnFontSize is not null || request.BurnBox)
            && request.BurnSubtitle is null && request.BurnFile is null && !request.SubtitleScan)
            return Fail("error.burn-style-needs-burn", null);
        if (CropModeConflict(request) is { } kirpmaHatasi) return Fail(kirpmaHatasi, CropModeText(request.CropMode!.Value));
        if (request.ConstantFrameRate && request.PeakFrameRate) return Fail("error.cfr-and-pfr", null);
        if (request.PeakFrameRate && request.FrameRate is null) return Fail("error.pfr-needs-fps", null);
        if (request.Input is null) return Fail(command == CliCommand.Watch ? "error.watch-no-folder" : "error.no-input", null);
        if (command == CliCommand.Watch && request.Output is null) return Fail("error.watch-no-output", null);
        if (request.TargetMb is not null && request.Quality is not null) return Fail("error.target-or-quality", null);
        if (request.ProfileId is null && request.PresetFile is null && request.TargetMb is null && request.Quality is null)
            return Fail("error.target-or-quality", null);
        if (request.SearchByMeasure && request.Quality is null) return Fail("error.search-needs-quality", null);
        return Success(request);
    }

    /// <summary>
    /// <c>profiller</c>: listeler; <c>--disa-aktar DOSYA</c> verilince profilleri çekirdeğin
    /// <see cref="PresetLibrary.Export"/>'u ile dosyaya yazar. <c>--profil</c> yalnız dışa aktarmada anlamlı.
    /// </summary>
    private static CliParseResult ParseProfiller(IReadOnlyList<string> args)
    {
        var request = new CliRequest { Command = CliCommand.Profiller };
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--disa-aktar" or "--export":
                    if (!TryValue(args, ref i, out var dosya)) return Fail("error.missing-value", arg);
                    request = request with { ExportPath = dosya };
                    break;
                case "--profil" or "--profile":
                    if (!TryValue(args, ref i, out var kimlik)) return Fail("error.missing-value", arg);
                    request = request with { ExportIds = request.ExportIds.Append(kimlik).ToList() };
                    break;
                default:
                    return Fail(arg.StartsWith('-') && arg.Length > 1 ? "error.unknown-option" : "error.extra-input", arg);
            }
        }

        if (request.ExportIds.Count > 0 && request.ExportPath is null) return Fail("error.export-needs-file", null);
        return Success(request);
    }

    public static bool TryParseSize(string text, out double mb)
    {
        mb = 0;
        var value = text.Trim();
        var factor = 1.0;
        if (value.EndsWith("GB", StringComparison.OrdinalIgnoreCase)) { factor = 1024; value = value[..^2]; }
        else if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        else if (value.EndsWith("M", StringComparison.OrdinalIgnoreCase)) value = value[..^1];
        if (!TryParseNumber(value.Trim(), out var number) || number <= 0) return false;
        mb = number * factor;
        return double.IsFinite(mb);
    }

    public static bool TryParseCodec(string text, out CliCodec codec)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "auto" or "otomatik": codec = CliCodec.Auto; return true;
            case "h264" or "avc" or "x264": codec = CliCodec.H264; return true;
            case "hevc" or "h265" or "x265": codec = CliCodec.Hevc; return true;
            case "av1": codec = CliCodec.Av1; return true;
            case "vp9" or "libvpx-vp9": codec = CliCodec.Vp9; return true;
            default: codec = CliCodec.Auto; return false;
        }
    }

    public static bool TryParsePriority(string text, out EncodePriority priority)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "normal": priority = EncodePriority.Normal; return true;
            case "normalin-alti" or "below-normal": priority = EncodePriority.BelowNormal; return true;
            case "dusuk" or "low": priority = EncodePriority.Low; return true;
            default: priority = EncodePriority.Normal; return false;
        }
    }

    /// <summary>Kirpma kipinin HandBrake yazimi; hata iletisi ayni adlari kullanir; secenek <c>docs/cli.md</c> tablosunda.</summary>
    public static readonly IReadOnlyList<string> CropModes = new[] { "auto", "conservative", "none", "custom" };

    public static string CropModeText(CropMode mode) => CropModes[(int)mode];

    public static bool TryParseCropMode(string text, out CropMode mode)
    {
        var index = text.Trim().ToLowerInvariant() switch
        {
            "otomatik" => 0,
            "temkinli" => 1,
            "yok" => 2,
            "elle" => 3,
            var value => CropModes.ToList().IndexOf(value)
        };
        mode = index < 0 ? CropMode.Auto : (CropMode)index;
        return index >= 0;
    }

    /// <summary>
    /// Kip ile obur kirpma bayraklarinin celiskisi. Elle dikdortgen yalniz <see cref="CropMode.Custom"/>
    /// ile yasar; <see cref="CropMode.None"/> yoklamayi da dikdortgeni de reddeder. Donen deger hata anahtaridir.
    /// </summary>
    private static string? CropModeConflict(CliRequest request)
    {
        var manual = request.Filters?.Crop is not null;
        return request.CropMode switch
        {
            CropMode.None => manual || request.AutoCrop ? "error.crop-mode-none-conflict" : null,
            CropMode.Custom => request.AutoCrop ? "error.crop-mode-custom-conflict"
                : manual ? null : "error.crop-mode-needs-crop",
            null => null,
            _ => manual ? "error.crop-mode-manual-conflict" : null
        };
    }

    internal static bool TryParseNumber(string text, out double value)
        => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value);

    /// <summary>
    /// <c>BAS-SON</c> kesiti. Uclar saniye (<c>12.5</c>) ya da saat gosterimi (<c>1:23</c>,
    /// <c>1:02:03</c>) olabilir; son uc bos birakilirsa (<c>10-</c>) kaynagin sonuna kadar.
    /// </summary>
    internal static bool TryParseRange(string text, out double? start, out double? end,
        out double? startFrame, out double? endFrame)
    {
        start = null;
        end = null;
        startFrame = null;
        endFrame = null;
        var parts = text.Split('-');
        if (parts.Length != 2) return false;

        if (TryParseFrame(parts[0], out var fromFrame)) startFrame = fromFrame;
        else if (TryParseClock(parts[0], out var from)) { if (from < 0) return false; start = from; }
        else return false;

        if (parts[1].Length > 0)
        {
            if (TryParseFrame(parts[1], out var toFrame)) endFrame = toFrame;
            else if (TryParseClock(parts[1], out var to)) end = to;
            else return false;
        }

        if (start is double a && end is double b && b <= a) return false;
        if (startFrame is double c && endFrame is double d && d <= c) return false;
        return true;
    }

    /// <summary>
    /// <c>300f</c> yazimi. Ek yoksa kare degil; eski saniye yazimi hic degismedi.
    /// Kesirli kare kabul edilmez — kare sayilir, olculmez.
    /// </summary>
    private static bool TryParseFrame(string text, out double frame)
    {
        frame = 0;
        if (text.Length < 2 || (text[^1] != 'f' && text[^1] != 'F')) return false;
        var body = text[..^1];
        if (!int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0) return false;
        frame = value;
        return true;
    }

    /// <summary>HandBrake'in <c>-c</c> yazimi: <c>2</c> ya da <c>2-4</c>.</summary>
    private static bool TryParseChapters(string text, out int from, out int to)
    {
        from = 0;
        to = 0;
        var parts = text.Split('-');
        if (parts.Length > 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out from) || from < 1) return false;
        if (parts.Length == 1) { to = from; return true; }
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out to) || to < from) return false;
        return true;
    }

    internal static bool TryParseClock(string text, out double seconds)
    {
        seconds = 0;
        if (text.Length == 0) return false;
        var fields = text.Split(':');
        if (fields.Length > 3) return false;
        var total = 0.0;
        for (var i = 0; i < fields.Length; i++)
        {
            if (!TryParseNumber(fields[i], out var field) || field < 0) return false;
            if (fields.Length > 1 && i > 0 && field >= 60) return false;
            total = total * 60 + field;
        }
        seconds = total;
        return true;
    }

    internal static bool TryValue(IReadOnlyList<string> args, ref int index, out string value)
    {
        value = "";
        if (index + 1 >= args.Count) return false;
        index++;
        value = args[index];
        return true;
    }

    internal static CliParseResult Success(CliRequest request) => new(request, null, null);

    internal static CliParseResult Fail(string key, string? argument) => new(null, key, argument);
}
