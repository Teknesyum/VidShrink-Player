using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using VidShrink.App;
using VidShrink.App.Localization;
using VidShrink.App.Performance;
using VidShrink.App.Playback;
using VidShrink.Ffmpeg;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace VidShrink.Tests;

/// <summary>
/// Kaynaktaki dil dosyalarının okuyucusu. Ölçümler çalışan uygulamanın kopyasına değil
/// depodaki dosyaya bakar: çıktıya kopyalanmayan bir düzeltme yeşil vermesin.
/// </summary>
internal static class Locales
{
    internal static readonly string Folder =
        Path.Combine(TipSources.Root, "src", "VidShrink.App", "Locales");

    internal static readonly string[] Domains = { "main", "playback", "performance", "recorder", "settings" };

    internal static IReadOnlyList<string> Languages =>
        Directory.GetDirectories(Folder).Select(path => Path.GetFileName(path)!).OrderBy(name => name).ToList();

    internal static IReadOnlyDictionary<string, string> Domain(string language, string domain)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
               File.ReadAllText(Path.Combine(Folder, language, domain + ".json")))
           ?? new Dictionary<string, string>();

    /// <summary>Bir dilin bütün alanları tek sözlükte; hangi anahtarın hangi dosyadan geldiğiyle.</summary>
    internal static IReadOnlyDictionary<string, (string Domain, string Value)> Read(string language)
    {
        var all = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var domain in Domains)
            foreach (var (key, value) in Domain(language, domain))
                all[key] = (domain, value);

        return all;
    }

    internal static IReadOnlyDictionary<string, string> Values(string language)
        => Read(language).ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

    /// <summary>
    /// İngilizce metnin anahtarı üzerinden Türkçesi. Sözlük artık İngilizceyle anahtarlanmıyor,
    /// bu yüzden eşleştirme değerden yapılıyor; iki anahtar aynı İngilizceyi taşıyorsa ölçüm
    /// bunu söyler, sessizce birini seçmez.
    /// </summary>
    internal static string TurkishFor(string english)
    {
        var keys = Values("en")
            .Where(pair => string.Equals(pair.Value, english, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();

        if (keys.Count != 1)
            throw new InvalidOperationException($"\"{english}\" için {keys.Count} anahtar bulundu.");

        return Values("tr")[keys[0]];
    }
}

/// <summary>
/// T83: ekrandaki her metin anahtardan gelir. Ölçümler üç soruyu ayrı ayrı soruyor —
/// kaynakta gömülü metin kaldı mı, Türkçe eksik mi, ve dil düğmesi gerçekten her şeyi
/// çeviriyor mu.
/// </summary>
public sealed class LanguageTests : IDisposable
{
    public LanguageTests() => Strings.Reset();

    public void Dispose() => Strings.Reset();

    // ---- K2: gömülü metin kalmıyor -------------------------------------------------

    /// <summary>
    /// Biçimlemede kullanıcıya çıkan bir öznitelik ya bir bağdır (<c>{...}</c> ile başlar)
    /// ya da harf içermez. Kaçınılmaz olanlar tek tek adıyla yazılı: marka parçaları,
    /// birim ve kısaltma adları. Örüntüyle toptan muafiyet yok — listeye bir cümle
    /// eklendiği anda gözle görünür.
    /// </summary>
    private static readonly string[] XamlNamesThatStayAsWritten =
    {
        "Vid", "Shrink", "Player",
        "MB", "CRF", "1280x720",
        "MP4", "MKV", "WebM", "MOV", "AVI", "GIF", "MP3", "M4A", "WAV", "FLAC", "WebP", "AVIF", "MXF",
        "H.264", "H.265", "VP9", "AV1", "AAC", "Opus", "PCM",
        "OGG", "AIFF", "AC3", "E-AC3", "44100 Hz", "48000 Hz",
        "ProRes 422 Proxy", "ProRes 422 LT", "ProRes 422", "ProRes 422 HQ", "ProRes 4444",
        "DNxHR LB", "DNxHR SQ", "DNxHR HQ", "DNxHR HQX", "DNxHR 444",
        "H.264 Nvidia", "H.265 Nvidia", "AV1 Nvidia", "H.264 Intel", "H.265 Intel", "AV1 Intel",
        "H.264 AMD", "H.265 AMD", "AV1 AMD",
        "Lanczos", "Spline",
        "APNG", "FFV1", "CineForm",
        "Sierra-2-4A", "Bayer", "Heckbert", "Floyd-Steinberg", "Sierra-2", "Sierra-3", "Burkes", "Atkinson",
        "BT.601", "BT.709", "BT.2020",
        "T"
    };

    private static readonly Regex ScreenAttribute = new(
        "(?:^|\\s)((?:\\w+:)?(?:Text|Content|Header|ToolTip\\.Tip|Watermark|AutomationProperties\\.Name|Bullets\\.Text))=\"([^\"]*)\"",
        RegexOptions.Compiled);
    /// <summary>
    /// Öğe gövdesi. Desen bir zamanlar <c>{</c> ve <c>}</c> geçen gövdeleri atlıyordu, yani
    /// <c>&lt;TextBlock&gt;Hazır {0}&lt;/TextBlock&gt;</c> görünmüyordu; süslü parantez artık
    /// gövdeyi taramadan düşürmüyor. Gövdenin kendisi bir bağ ise (<c>{loc:Text ...}</c>)
    /// ayıklama aşamasında eleniyor.
    /// </summary>
    private static readonly Regex ScreenBody = new(
        @">([^<>]*)<",
        RegexOptions.Compiled);

    private static readonly Regex AnyLetter = new(@"\p{L}", RegexOptions.Compiled);

    /// <summary>
    /// Biçim yer tutucusu: <c>{0}</c>, <c>{1,-8}</c>, <c>{0:0.0}</c>. Ekran metni değildir,
    /// çalışma anında dolar. Metnin geri kalanı ekran metnidir ve taranır.
    /// </summary>
    private static readonly Regex FormatPlaceholder = new(
        @"\{\d+(?:,-?\d+)?(?::[^{}]*)?\}", RegexOptions.Compiled);

    /// <summary>
    /// Değerin <b>tamamı</b> bir biçimleme bağı: <c>{loc:Text main.x}</c>,
    /// <c>{TemplateBinding Content}</c>, <c>{DynamicResource X}</c>. Bunlar ekrana metin
    /// yazmaz, bağladıkları şeyi yazar.
    ///
    /// <para>Ölçüt eskiden yalnız "<c>{</c> ile başlıyor mu" idi; <c>{0} dosya hazır</c> de
    /// bağ sayılıyor ve gövde hiç taranmıyordu. Bugün yer tutucuyla <b>başlayan</b> metin de
    /// taranır: yer tutucunun kendisi metin sayılmaz, etrafı sayılır.</para>
    /// </summary>
    private static readonly Regex MarkupBinding = new(
        @"^\{(?:\w+:)?[A-Za-z]\w*(?:[^{}]*)?\}$", RegexOptions.Compiled);

    /// <summary>Yer tutucular düşürüldükten sonra geriye kalan ekran metni.</summary>
    private static string ScreenTextOf(string value)
        => FormatPlaceholder.Replace(value, " ").Trim();

    /// <summary>
    /// Taranan biçimleme dosyaları. Ölçüm bir zamanlar yalnız <c>MainWindow.axaml</c>'i
    /// okuyordu; oynatma paneli, denetim şeridi ve tema sözlükleri görünmüyordu. Bugün
    /// uygulamanın altındaki <b>her</b> <c>.axaml</c> taranıyor: bugün delik olmaması
    /// yarınki gömülü metni yakalamamak için gerekçe değil.
    /// </summary>
    internal static IReadOnlyList<string> MarkupFiles()
    {
        var app = Path.Combine(TipSources.Root, "src", "VidShrink.App");
        return Directory.GetFiles(app, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    public static TheoryData<string> ScannedMarkup()
    {
        var data = new TheoryData<string>();
        foreach (var file in MarkupFiles())
            data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(ScannedMarkup))]
    public void BicimlemedeKullaniciyaGorunenDuzMetinKalmadi(string path)
    {
        var app = Path.Combine(TipSources.Root, "src", "VidShrink.App");
        var relative = Path.GetRelativePath(app, path);
        var xaml = StripXmlComments(File.ReadAllText(path));
        var stray = new List<string>();

        foreach (Match match in ScreenAttribute.Matches(xaml))
        {
            var value = TipSources.DecodeXml(match.Groups[2].Value).Trim();
            if (MarkupBinding.IsMatch(value)) continue;

            var text = ScreenTextOf(value);
            if (!AnyLetter.IsMatch(text)) continue;
            if (XamlNamesThatStayAsWritten.Contains(text, StringComparer.Ordinal)) continue;

            stray.Add($"{match.Groups[1].Value}=\"{value}\"");
        }

        foreach (Match match in ScreenBody.Matches(xaml))
        {
            var value = TipSources.DecodeXml(match.Groups[1].Value).Trim();
            if (MarkupBinding.IsMatch(value)) continue;

            var text = ScreenTextOf(value);
            if (!AnyLetter.IsMatch(text)) continue;
            if (XamlNamesThatStayAsWritten.Contains(text, StringComparer.Ordinal)) continue;
            if (IsResourceValue(xaml, match.Index)) continue;

            stray.Add($">{value}<");
        }

        Assert.True(stray.Count == 0,
            $"{relative} içinde anahtardan gelmeyen metin var:\n" + string.Join("\n", stray));
    }

    private static string StripXmlComments(string markup)
        => Regex.Replace(markup, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// Sözlüğe yazılan ve çevrilmeyecek olan kaynak değerlerinin <b>adları</b>: renk,
    /// geometri, gölge, yazı tipi ailesi, bağlantı adresi. Muafiyet eskiden örüntüydü —
    /// açılış etiketinde <c>x:Key=</c> geçen her gövde taranmadan düşüyordu, yani tema
    /// dosyalarına yazılan bir cümle de düşerdi. Bugün yalnız bu adlar düşer; listede
    /// olmayan her <c>x:Key</c> gövdesi taranır.
    ///
    /// <para>Listeye bir ad eklemek görünür bir iştir ve
    /// <see cref="Kaynak_muafiyetinde_kullanilmayan_ad_yok"/> ölü adı düşürür.</para>
    /// </summary>
    private static readonly string[] ResourceKeysThatCarryNoScreenText =
    {
        "NeonBlueColor", "NeonPinkColor", "NeonPurpleColor", "NeonSuccessColor",
        "SurfaceToneColor", "AppBgColor", "TextBodyColor", "TextDisabledColor",
        "OnNeonColor", "PinkTextColor",
        "NeonBlueFillColor", "NeonBlueHoverColor", "NeonBlueActiveColor",
        "NeonBlueBorderColor", "NeonBlueBorderStrongColor", "NeonPinkFillColor",
        "NeonPurpleBorderColor", "NeonEmberColor", "EmberFlameColor", "EmberBlazeColor", "AtmosHotColor", "AtmosMidColor", "AtmosEdgeColor",
        "EmberDeepColor", "EmberMidColor", "EmberEdgeColor",
        "EmberBarDeepColor", "EmberBarMidColor", "EmberBarEdgeColor", "EmberFlameGlyphColor",
        "FontMono",
        "GlowBlue", "GlowPink", "GlowPurple",
        "LinkGitHub", "LinkRepo", "LinkSponsor", "AppIconUri",
        "PlaybackMaximizeIcon", "PlaybackFullScreenIcon",
        "PlaybackScrimColor", "PlaybackScrimEdgeColor", "PlaybackTitleScrimColor", "PauseGlossColor", "PauseGlossFadeColor",
        "IconPlayer", "IconShrink", "IconConvert", "IconRecorder", "IconAdvanced",
        "IconAbout", "IconSettings",
        "IconPlay", "IconPause", "IconRewind", "IconFastForward",
        "IconVolume", "IconVolumeMute", "IconSpeedFast", "IconSpeedFastTip", "IconSpeedSlow", "IconFullScreen",
        "IconChevronDown", "IconChevronUp", "IconStop", "IconRestart",
        "IconClose", "IconMaximize", "IconMinimize", "IconRestore",
        "IconCoffee", "IconWarning", "IconEditor", "IconCamera", "IconEyeOff", "IconBookmark", "IconLock"
    };

    private static readonly Regex KeyAttribute = new(
        "x:Key=\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// Gövdenin ekrana değil sözlüğe yazıldığı durum. Ölçüt yapı değil <b>ad</b>: öğenin
    /// <c>x:Key</c> değeri <see cref="ResourceKeysThatCarryNoScreenText"/> içinde geçiyorsa
    /// gövde kaynak değeridir, geçmiyorsa ekran metni sayılır ve taranır.
    /// </summary>
    private static bool IsResourceValue(string markup, int bodyStart)
    {
        var open = markup.LastIndexOf('<', bodyStart);
        if (open < 0) return false;

        var tag = markup[open..(bodyStart + 1)];
        var key = KeyAttribute.Match(tag);
        return key.Success
            && ResourceKeysThatCarryNoScreenText.Contains(key.Groups[1].Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Muafiyet listesi büyümesin diye: listedeki her ad gerçekten taranan bir dosyada
    /// kaynak gövdesi olarak duruyor mu. Bir ad kaldırıldığında ya da yeniden
    /// adlandırıldığında liste kendiliğinden eskir; bu ölçü eskimeyi söyler.
    /// </summary>
    [Fact]
    public void Kaynak_muafiyetinde_kullanilmayan_ad_yok()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in MarkupFiles())
        {
            var xaml = StripXmlComments(File.ReadAllText(file));
            foreach (Match match in ScreenBody.Matches(xaml))
            {
                var value = TipSources.DecodeXml(match.Groups[1].Value).Trim();
                if (!AnyLetter.IsMatch(ScreenTextOf(value))) continue;

                var open = xaml.LastIndexOf('<', match.Index);
                if (open < 0) continue;
                var key = KeyAttribute.Match(xaml[open..(match.Index + 1)]);
                if (key.Success) used.Add(key.Groups[1].Value);
            }
        }

        var dead = ResourceKeysThatCarryNoScreenText.Where(name => !used.Contains(name)).ToList();
        Assert.True(dead.Count == 0, "muafiyet listesinde karşılığı olmayan ad:\n" + string.Join("\n", dead));
    }

    /// <summary>
    /// Arka kodda ekrana çıkan her metin bir sözlük çağrısından gelir. Geriye kalan
    /// İngilizce dizgeler ekran metni değil <b>motorun kimlikleri</b>: ffmpeg'in hata
    /// satırında aranan iğneler ve ilerleme satırındaki aşama sözcükleri. Hepsi burada
    /// adıyla sayılı.
    /// </summary>
    private static readonly string[] CodeNamesThatStayInCode =
    {
        "Dolby Digital (AC-3)", "Dolby Digital Plus (E-AC-3)",
        "Windows ARM64", "macOS Apple Silicon (arm64)", "macOS Intel (x64)", "Linux ARM64",
        "GIF palette", "GIF encode",
        "Noto Sans Thai", // YaziTipiYedegi: yazı tipi ailesinin adı, arayüz metni değil
        "powrprof.dll,SetSuspendState 0,1,0", "-e \"tell application \\\"System Events\\\" to shut down\"",
        "display notification   with title",
        "no space left", "not enough space", "disk full", "insufficient disk space",
        "unknown encoder", "encoder not found", "does not support", "could not write header",
        "error initializing output stream", "automatic encoder selection failed",
        "incorrect codec parameters", "invalid argument", "muxer does not support",
        "invalid data found", "moov atom not found", "could not find codec parameters",
        "decoder not found", "no such file or directory", "end of file", "unknown format",
        "Trim times must use HH:MM:SS format.",
        "Start time cannot be negative.",
        "End time must be greater than zero.",
        "End time must be after start time.",
        "Start time must be before the end of the source.",
        "Resolution dimensions must be positive.",
        "Resolution dimensions must be even for the selected pixel format.",
        "Frame rate must be greater than zero.",
        "Stream copy cannot change resolution or frame rate.",
        "GIF requires video encoding and cannot use stream copy.",
        "Image sequence output requires video encoding and cannot use stream copy.",
        "Burning subtitles requires video encoding.",
        "The source has no audio stream to copy.",
        "The source has no audio stream to extract.",
        "The source has no video stream; choose an audio container.",
        "The trim end must come after the trim start.",
        "^The (.+) container does not support the selected (.+) video encoder.$",
        "^The (.+) container does not support the selected (.+) audio encoder.$",
        "^The (.+) container does not support copying the source (.*) video stream.$",
        "^The (.+) container does not support copying the source (.*) audio stream.$",
        "No audio track is selected.",
        "^The (.+) container carries only one audio track.$",
        "Audio channel count must be positive.",
        "The MXF container carries only 48000 Hz audio.",
        "GIF palette colors must be between 2 and 256.",
        "GIF loop count must be between -1 and 65535.",
        "The GIF dither mode is not supported.",
        "Cover art must be a PNG or JPEG image.",
        "Cover art can be written only to MP4 or MKV.",
        "Cover art in MP4 requires video stream copy.",
        "Making video from audio needs an H.264, H.265, VP9 or AV1 encoder.",
        "The audio file has no embedded cover; pick a cover image.",
        "^The (.+) audio encoder does not support (.+) Hz.$",
        "^The (.+) encoder does not support the (.+) setting.$",
        "Localization key ' ' is missing in ' ' and in ' '."
    };

    /// <summary>
    /// Palet başlıkları özel ad sayılır, çevrilmez (<c>docs/netlestirme/027-palet-birlesimi.md</c>).
    /// Muafiyet elle yazılı değil, <c>seeds.json</c>'daki <c>baslik</c>tan okunur ve yalnız tam
    /// eşleşmeye geçer: başlığa benzeyen bir arayüz cümlesi taramaya yine yakalanır.
    /// </summary>
    private static readonly HashSet<string> PaletteTitles = VidShrink.PaletteGen.PaletteSeed
        .Load(Path.Combine(TipSources.Root, "src", "VidShrink.App", "Themes", "Palette", "seeds.json"))
        .Select(seed => seed.Title)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static readonly Regex CsLiteral = new("(?<interpolated>\\$)?\"(?<body>(?:[^\"\\\\\n]|\\\\.)*)\"", RegexOptions.Compiled);
    private static readonly Regex CsWord = new(@"\p{L}{3,}", RegexOptions.Compiled);

    /// <summary>
    /// <c>_trace.Add(...)</c> <b>çağrısı</b> taramanın dışında — satırın tamamı değil. İz dizgesi
    /// ekrana çıkmaz, çeviriye girmez ve dili sabittir: testin okuduğu makine kaydı. Muafiyet
    /// çağrıya göre, ada göre değil; iz metni bölünerek gizlenmesin diye açıkça burada duruyor.
    /// Aynı satırda iz çağrısından sonra yazılan gerçek bir arayüz cümlesi taramaya yakalanır.
    /// </summary>
    private static readonly Regex TraceCall = new(@"_trace\.Add\((?:[^()""]|""(?:[^""\\]|\\.)*""|\((?:[^()""]|""(?:[^""\\]|\\.)*"")*\))*\)", RegexOptions.Compiled);

    private static string StripTraceCalls(string source) => TraceCall.Replace(source, string.Empty);

    public static TheoryData<string> ScannedCode()
    {
        var app = Path.Combine(TipSources.Root, "src", "VidShrink.App");
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
            data.Add(file);
        return data;
    }

    [Theory]
    [MemberData(nameof(ScannedCode))]
    public void ArkaKoddaCumleKalmadi(string path)
    {
        var app = Path.Combine(TipSources.Root, "src", "VidShrink.App");
        var relative = Path.GetRelativePath(app, path);
        var source = StripInterpolations(StripTraceCalls(Strip(File.ReadAllText(path))));
        var stray = new List<string>();

        foreach (Match match in CsLiteral.Matches(source))
        {
            var body = match.Groups["body"].Value;
            var value = TipSources.Unescape(body).Trim();
            if (!value.Contains(' ')) continue;
            if (CsWord.Matches(value).Count < 2) continue;
            if (CodeNamesThatStayInCode.Contains(value, StringComparer.Ordinal)) continue;
            if (PaletteTitles.Contains(value) && relative == Path.Combine("Themes", "PaletteCatalog.cs")) continue;

            stray.Add(value);
        }

        Assert.True(stray.Count == 0,
            $"{relative} içinde anahtardan gelmeyen cümle var:\n" + string.Join("\n", stray.Distinct()));
    }

    /// <summary>
    /// <c>throw</c> ifadelerinin içi. Eskiden bütün ifade taramadan siliniyordu ve içindeki
    /// metin hiçbir ölçünün görüş alanına girmiyordu.
    ///
    /// <para>Kesme iletisi ekran metni değildir; koddan koda konuşur ve kodun dili
    /// İngilizcedir. Türkçe bir cümle orada kullanıcı metninin sızdığını söyler. Argüman
    /// sözleşmesi ailesi (<c>ArgumentException</c>, <c>ArgumentNullException</c>,
    /// <c>ArgumentOutOfRangeException</c>) dışarıda: bunlar çağıranın parametresini adlandırır,
    /// çalışma zamanı iletiyi parametre adıyla biçimler ve yol bir programlama hatasıdır.
    /// Muafiyet ada göre değil <b>kesme türüne</b> göre; listeye cümle eklenmiyor.</para>
    ///
    /// <para>İkinci ölçüt: kesme iletisi sözlükteki bir değerin ikizi olamaz — olsaydı ekran
    /// metninin kopyası olurdu.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ScannedCode))]
    public void KesmeIletisiEkranMetniDegil(string path)
    {
        var app = Path.Combine(TipSources.Root, "src", "VidShrink.App");
        var relative = Path.GetRelativePath(app, path);
        var known = new HashSet<string>(
            Locales.Languages.SelectMany(language => Locales.Values(language).Values), StringComparer.Ordinal);
        var stray = new List<string>();

        foreach (var statement in ThrowStatements(StripComments(File.ReadAllText(path))))
        {
            var contract = ArgumentContractThrow.IsMatch(statement);
            foreach (Match match in CsLiteral.Matches(StripInterpolations(statement)))
            {
                var value = TipSources.Unescape(match.Groups["body"].Value).Trim();
                if (!value.Contains(' ')) continue;
                if (CsWord.Matches(value).Count < 2) continue;

                if (known.Contains(value))
                {
                    stray.Add($"sözlükteki metnin ikizi: \"{value}\"");
                    continue;
                }

                if (!contract && NonAscii.IsMatch(value))
                    stray.Add($"kesme iletisi kod dilinde değil: \"{value}\"");
            }
        }

        Assert.True(stray.Count == 0,
            $"{relative} içindeki kesme iletileri:\n" + string.Join("\n", stray.Distinct()));
    }

    private static readonly Regex ArgumentContractThrow = new(
        @"throw new Argument(Null|OutOfRange)?Exception\b", RegexOptions.Compiled);

    private static readonly Regex NonAscii = new(@"[^\p{IsBasicLatin}]", RegexOptions.Compiled);

    /// <summary>
    /// Cümle taramasından düşenler: yorumlar ve <c>throw</c> ifadeleri. İkincisi artık
    /// <b>silinip unutulmuyor</b> — kesme iletileri <see cref="KesmeIletisiEkranMetniDegil"/>
    /// ölçüsüne veriliyor. Eski desen (<c>throw new [^;]*;</c>) iletinin içindeki noktalı
    /// virgülde duruyordu; geriye eşi kalmamış bir tırnak bırakıp dosyanın geri kalanındaki
    /// dizgeleri yanlış eşleştiriyordu.
    /// </summary>
    private static string Strip(string source)
    {
        source = StripComments(source);
        foreach (var statement in ThrowStatements(source))
            source = source.Replace(statement, string.Empty, StringComparison.Ordinal);

        return source;
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(source, @"//[^\n]*", string.Empty);
    }

    /// <summary>
    /// Kaynaktaki <c>throw new ...;</c> ifadeleri. Tarama dizge farkındadır: iletinin içindeki
    /// <c>;</c> ifadeyi erken bitirmez.
    /// </summary>
    private static IReadOnlyList<string> ThrowStatements(string source)
    {
        var found = new List<string>();
        var at = 0;
        while ((at = source.IndexOf("throw new ", at, StringComparison.Ordinal)) >= 0)
        {
            var index = at;
            var quoted = false;
            for (; index < source.Length; index++)
            {
                var current = source[index];
                if (current == '"' && (index == 0 || source[index - 1] != '\\')) quoted = !quoted;
                else if (current == ';' && !quoted) break;
            }

            if (index >= source.Length) break;
            found.Add(source[at..(index + 1)]);
            at = index + 1;
        }

        return found;
    }

    private static string StripInterpolations(string source)
    {
        var output = new StringBuilder(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            if (index + 1 >= source.Length || source[index] != '$' || source[index + 1] != '"')
            {
                output.Append(source[index]);
                continue;
            }

            output.Append("$\"");
            index += 2;
            var depth = 0;
            var quoted = false;
            for (; index < source.Length; index++)
            {
                var current = source[index];
                if (depth == 0 && current == '"')
                {
                    output.Append('"');
                    break;
                }

                if (depth == 0)
                {
                    if (current == '{' && index + 1 < source.Length && source[index + 1] == '{')
                    {
                        output.Append("{{");
                        index++;
                    }
                    else if (current == '{')
                    {
                        depth = 1;
                        output.Append(' ');
                    }
                    else
                    {
                        output.Append(current);
                    }
                    continue;
                }

                if (current == '"' && (index == 0 || source[index - 1] != '\\')) quoted = !quoted;
                if (quoted) continue;
                if (current == '{') depth++;
                else if (current == '}') depth--;
            }
        }

        return output.ToString();
    }

    // ---- K3: Türkçe eksiksiz -------------------------------------------------------

    /// <summary>
    /// Gerçekten iki dilde aynı olan girdiler. İki sebep var ve ikisi de tek tek karar
    /// gerektirir, örüntü yok: marka/ürün adları (<c>VidShrink</c>, <c>FFmpeg</c>, <c>.NET</c>)
    /// çevrilmez; teknik terimin Türkçesi İngilizcesiyle aynı yazılıyorsa (<c>CRF</c> ffmpeg
    /// kısaltması, <c>Stereo</c> ve <c>Mono</c> TDK'nın da yazdığı hâlleriyle) uydurma
    /// karşılık yazmaktansa aynı bırakılır. T163 gelişmiş ayarlar paneliyle son üçünü getirdi.
    /// S9 birim kalıplarını getirdi (<c>MB</c>, <c>kbps</c>, <c>FPS</c>, <c>/100</c>, <c>AI</c>, <c>CRF {0}</c>):
    /// Türkçe arayüz de bunları İngilizcesiyle aynı yazar; başka dil kendi birimini yazar (fr <c>Mo</c>, ru <c>МБ</c>).
    /// Sağ tık hedef yongaları marka adı ve MB taşır (<c>WhatsApp (16 MB)</c>); Türkçesi de aynı yazılır.
    /// OYA-05 zamana git kutusunun ipucu örnek saattir (<c>0:00:00</c>); rakam ve iki nokta her dilde aynı.
    /// Later 41 hızlı kırpmanın aralık satırı iki saat arasında yalnız tire taşır (<c>{0} – {1}</c>).
    /// </summary>
    private static readonly string[] SameInEveryLanguage =
    {
        "VidShrink", "FFmpeg", ".NET",
        "CRF", "Stereo", "Mono", "180°",
        "{0} ms",
        "MB", "{0} MB", "{0} - {1} MB", "{0} kbps", "{0} FPS", "{0}k", "/100", "{0}/100", "AI", "CRF {0}",
        "8 MB", "100 MB", "WhatsApp (16 MB)", "Discord (20 MB)", "Gmail (25 MB)", "Discord Nitro Basic (50 MB)",
        "0:00:00", "{0} – {1}"
    };

    [Fact]
    public void HerAnahtarinTurkcesiIngilizcesindenFarkli()
    {
        var english = Locales.Read("en");
        var turkish = Locales.Values("tr");
        var complaints = new StringBuilder();

        foreach (var (key, (domain, value)) in english)
        {
            Assert.True(turkish.TryGetValue(key, out var written), $"'{key}' Türkçede yok.");
            if (!string.Equals(value, written, StringComparison.Ordinal)) continue;
            if (SameInEveryLanguage.Contains(value.Trim(), StringComparer.Ordinal)) continue;

            complaints.AppendLine($"{domain}: '{key}' iki dilde aynı — \"{value}\"");
        }

        Assert.True(complaints.Length == 0, "Türkçesi yazılmamış anahtarlar:\n" + complaints);
    }

    /// <summary>
    /// Başlık çubuğundaki tekerlek düğmesi kalktı: Ayarlar artık şeridin kendi sekmesi.
    /// Kısayol iki dili taşıyor, geri kalanı ayarlarda. Ölçü giriş noktasını çağırıp hangi
    /// sekmenin açıldığına bakıyor — sekme numarasını sabit yazmıyor, sekmeyi kendi
    /// adından buluyor.
    /// </summary>
    [Fact]
    public void DilGirisiAyarlarSekmesindekiDilSecicisineGoturur()
    {
        var (chosenTab, settingsTab, shortcuts, listed) = AppHost.Run(() =>
        {
            var window = new MainWindow();
            Relayout(window, new Size(1400, 1000));

            window.OpenLanguageSettings();

            var settings = window.Tabs.Items
                .OfType<TabItem>()
                .Select((item, at) => (item, at))
                .Single(pair => pair.item.Name == "TabSettings")
                .at;

            return (
                window.Tabs.SelectedIndex,
                settings,
                window.LangSwitch.Children.OfType<Button>().Select(b => (string)b.Tag!).ToList(),
                window.CmbLanguage.ItemsSource!.Cast<object?>().Count());
        });

        Assert.Equal(settingsTab, chosenTab);
        Assert.Equal(Strings.ShortcutLanguages, shortcuts);
        Assert.Equal(Strings.Languages.Count, listed);
    }

    /// <summary>
    /// Dil pencereden önce seçildiyse (iş penceresinin "Uygulamada aç"ı böyle kurar) değişim
    /// olayı gelmez; karşılaştırma paneli yine de o dilde açılmalı.
    /// </summary>
    [Fact]
    public void DilZatenSeciliykenAcilanPencereninKarsilastirmaPaneliODilde()
    {
        var baslik = AppHost.Run(() =>
        {
            Strings.Use("tr");
            var window = new MainWindow();
            try
            {
                return window.Preview.EmptyTitle.Text;
            }
            finally
            {
                window.Close();
                Strings.Use("en");
            }
        });

        Assert.Equal(LanguageCatalog.Title(Strings.GetIn("tr", "playback.panel.title"), "tr"), baslik);
    }
    /// <summary>
    /// Türkçe kullanıcı hedefe "15,5" yazar. Kutu değişmez kültürle okunduğunda bu metin
    /// ayrıştırılamıyor ve plan sessizce WhatsApp varsayılanına düşüyordu; kutuya yazılan
    /// sayı da Türkçe ekranda noktalı (51.8) çıkıyordu.
    /// </summary>
    [Fact]
    public void TurkceHedefKutusuVirgulluSayiyiOkurVeVirgulleYazar()
    {
        var (okunan, noktali, yazilan) = AppHost.Run(() =>
        {
            Strings.Use("tr");
            var window = new MainWindow();
            try
            {
                window.TxtTarget.Text = "15,5";
                var virgul = window.PlanTargetMb();
                window.TxtTarget.Text = "15.5";
                return (virgul, window.PlanTargetMb(), MainWindow.FormatBoxNumber(51.8));
            }
            finally
            {
                window.Close();
                Strings.Use("en");
            }
        });

        Assert.Equal(15.5, okunan);
        Assert.Equal(15.5, noktali);
        Assert.Equal("51,8", yazilan);
    }
    // ---- K7: üçüncü dil kod değişmeden ---------------------------------------------

    private readonly record struct Reading(IReadOnlyList<string> Offered, string Chosen);

    [Fact]
    public void UcuncuDilKlasoruKopyalayinca_UygulamaOnuListelerVeSecebilir()
    {
        var sandbox = Path.Combine(TestPaths.OutputRoot, "dil", Guid.NewGuid().ToString("N"));

        foreach (var language in new[] { "en", "tr" })
        {
            Directory.CreateDirectory(Path.Combine(sandbox, language));
            foreach (var domain in Locales.Domains)
                File.Copy(
                    Path.Combine(Locales.Folder, language, domain + ".json"),
                    Path.Combine(sandbox, language, domain + ".json"));
        }

        // Üçüncü dil: İngilizcenin kopyası, her değerin başına bir işaret. Kodda hiçbir
        // yerde "zz" yazmıyor — klasörün varlığı yeterli olmalı.
        Directory.CreateDirectory(Path.Combine(sandbox, "zz"));
        foreach (var domain in Locales.Domains)
        {
            var texts = Locales.Domain("en", domain)
                .ToDictionary(pair => pair.Key, pair => "zz " + pair.Value, StringComparer.Ordinal);
            texts["main.language.name"] = "Zzyzx";
            File.WriteAllText(
                Path.Combine(sandbox, "zz", domain + ".json"),
                JsonSerializer.Serialize(texts, new JsonSerializerOptions { WriteIndented = true }));
        }

        try
        {
            Strings.UseRoot(sandbox);
            Assert.Contains("zz", Strings.Languages, StringComparer.OrdinalIgnoreCase);

            var read = AppHost.Run(() =>
            {
                var window = new MainWindow();
                Relayout(window, new Size(1400, 1000));

                // Üst şerit yalnız kısayolu taşır; üçüncü dil ayarlardaki tam listede
                // görünür ve oradan seçilir. Ölçü listeyi okuyup seçimi oradan yapıyor.
                var picker = window.CmbLanguage;

                var offered = picker.ItemsSource!.Cast<object?>()
                    .Select(item => item?.ToString() ?? string.Empty)
                    .ToList();

                picker.SelectedIndex = offered.IndexOf(Strings.LanguageLabel("zz"));

                return new Reading(offered, window.TxtDropTitle.Text ?? string.Empty);
            });

            var (offered, chosen) = (read.Offered, read.Chosen);

            Assert.Contains(Strings.LanguageLabel("zz"), offered);
            Assert.Contains("Zzyzx", Strings.LanguageLabel("zz"), StringComparison.Ordinal);
            Assert.Equal("zz", Strings.Language);
            Assert.StartsWith("Zz ", chosen, StringComparison.Ordinal);
        }
        finally
        {
            Strings.Reset();
            try { Directory.Delete(sandbox, recursive: true); } catch (IOException) { }
        }
    }

    // ---- K4: kullanıcının saydığı dört cümle ---------------------------------------

    /// <summary>
    /// Beklenen metin sabitten değil <c>Locales/&lt;dil&gt;/main.json</c>'daki anahtardan
    /// okunup biçimleniyor. Sabit karşılaştıran ölçü anahtarı sökülse de yeşil kalırdı;
    /// bugün sayıların yerini ya da sözcük sırasını değiştiren her düzenleme ölçüyü ilgilendirir.
    /// </summary>
    [Fact]
    public void KodlamaImleciMetniDilAnahtarindanGelir()
    {
        Assert.Equal(Marker("en", 1, 2, 3), LanguageCatalog.EncodeMarker("en", 1, 2, 3));
        Assert.Equal(Marker("tr", 1, 2, 3), LanguageCatalog.EncodeMarker("tr", 1, 2, 3));
    }

    private static string Marker(string language, int pass, int passCount, int attempt)
    {
        var pattern = Locales.Values(language)["main.playback.encode-marker"];

        return LanguageCatalog.Title(
            string.Format(CultureInfo.GetCultureInfo(language), pattern, pass, passCount, attempt),
            language);
    }

    /// <summary>
    /// T91/K1-K3: örnek hatası İngilizce arayüzde <b>tümüyle</b> İngilizce çıkar. Ölçü
    /// kodlayanın ürettiği sebebi alıp paneli barındıran tarafın ekrana yazdığı dizgeyi
    /// okuyor; iki uç da üretim kodu, arada sabit yok.
    ///
    /// <para>Beklenen parçalar dil dosyasından geliyor: İngilizce değerin sabit parçaları
    /// dizgede bulunmalı, Türkçe değerin sabit parçaları bulunmamalı.</para>
    /// </summary>
    [Fact]
    public void OrnekHatasiIngilizceArayuzdeTumuyleIngilizce()
    {
        var failures = new[]
        {
            SegmentEncoder.FirstFailure(new[] { new FfmpegRun(false, 3, string.Empty, TimeSpan.Zero) }),
            SegmentEncoder.FirstFailure(new[]
                { new FfmpegRun(false, 1, "Invalid data found when processing input", TimeSpan.Zero) }),
            SegmentEncoder.FirstFailure(new[] { new FfmpegRun(true, 0, string.Empty, TimeSpan.Zero) })
        };

        var (english, turkish) = AppHost.Run(() =>
        {
            var host = new PanelHost(
                new ComparisonPanel(),
                () => throw new NotSupportedException("The frame source is not needed by this measurement."));

            host.SetLanguage("en");
            var first = failures.Select(host.SampleFailureText).ToList();
            host.SetLanguage("tr");
            var second = failures.Select(host.SampleFailureText).ToList();
            host.Dispose();
            return (first, second);
        });

        var en = Locales.Values("en");
        var tr = Locales.Values("tr");
        var complaints = new StringBuilder();

        for (var index = 0; index < failures.Length; index++)
        {
            var shown = english[index];
            var key = failures[index].Key;

            foreach (var piece in Shown("en", en["playback.sample-failed"]).Concat(Shown("en", en[key])))
                if (!shown.Contains(piece, StringComparison.Ordinal))
                    complaints.AppendLine($"'{key}' İngilizce parçası eksik — \"{piece}\" ∉ \"{shown}\"");

            // Yalnız Türkçeye özgü parçalar: iki dilde aynı yazılan ad (ffmpeg) ölçüt olamaz.
            var englishSide = string.Join(" ",
                Shown("en", en["playback.sample-failed"]).Concat(Shown("en", en[key])));

            var onlyTurkish = Shown("tr", tr["playback.sample-failed"]).Concat(Shown("tr", tr[key]))
                .Where(piece => !englishSide.Contains(piece, StringComparison.Ordinal))
                .ToList();

            foreach (var piece in onlyTurkish)
                if (shown.Contains(piece, StringComparison.Ordinal))
                    complaints.AppendLine($"'{key}' İngilizce arayüzde Türkçe taşıyor — \"{piece}\" ∈ \"{shown}\"");

            if (NonAscii.IsMatch(shown))
                complaints.AppendLine($"'{key}' İngilizce arayüzde Latin dışı harf taşıyor — \"{shown}\"");

            // Dil düğmesi gerçekten çeviriyor mu: aynı sebep Türkçede Türkçe okunmalı.
            foreach (var piece in Shown("tr", tr[key]))
                if (!turkish[index].Contains(piece, StringComparison.Ordinal))
                    complaints.AppendLine($"'{key}' Türkçe arayüzde çevrilmiyor — \"{piece}\" ∉ \"{turkish[index]}\"");
        }

        Assert.True(complaints.Length == 0, "Örnek hatası perdesi:\n" + complaints);
    }

    /// <summary>
    /// Bir dil değerinin ekranda okunacak sabit parçaları: metin dilin büyük harf kuralından
    /// geçirilir, biçim yuvaları atılır, kalan anlamlı parçalar döner. Ölçü bu parçaları
    /// arıyor, üretimin ürettiği dizgeyi yeniden kurmuyor.
    /// </summary>
    private static IReadOnlyList<string> Shown(string language, string pattern)
        => Regex.Split(LanguageCatalog.Title(pattern, language), @"\{\d+\}")
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length >= 3)
            .ToList();

    /// <summary>
    /// T91/K4: motorun doğrulama iletileri ile <c>LanguageCatalog</c>'un anahtar sözlüğü
    /// karşılaştırılıyor. İki yön de ölçülüyor — Core'a eklenen yeni bir ileti karşılıksız
    /// kalamaz, sözlükte karşılığı olmayan ölü anahtar da duramaz. Tek yön ölçülseydi
    /// ayıklayıcı boş dönünce ölçüm sessizce yeşil verirdi.
    /// </summary>
    [Fact]
    public void MotorunDogrulamaIletilerinePerdeArkasindaAnahtarVar()
    {
        Strings.Use("en");
        var messages = CoreValidationMessages();
        var complaints = new StringBuilder();

        foreach (var message in messages)
        {
            var shown = LanguageCatalog.Validation(message);
            var untranslated = LanguageCatalog.Display(Strings.Get("main.validation.untranslated-engine", message));
            if (string.Equals(shown, untranslated, StringComparison.Ordinal))
                complaints.AppendLine($"Core iletisinin anahtarı yok — \"{message}\"");
        }

        var produced = messages.Concat(CatalogMessageConstants()).ToList();
        foreach (var known in KnownValidationMessages())
            if (!produced.Contains(known, StringComparer.Ordinal))
                complaints.AppendLine($"Sözlükteki anahtar motorda karşılıksız — \"{known}\"");

        Assert.True(complaints.Length == 0, "ConversionArguments.Validate ↔ ValidationKeys:\n" + complaints);
    }

    /// <summary>
    /// <c>ConversionArguments.Validate</c>'in ürettiği iletiler. Interpolasyon yuvaları
    /// <see cref="StripInterpolations"/> ile boşaltılıyor; kalan biçim örüntüyle eşleşir.
    /// </summary>
    private static IReadOnlyList<string> CoreValidationMessages()
    {
        var path = Path.Combine(TipSources.Root, "src", "VidShrink.Core", "ConversionArguments.cs");
        var source = StripComments(File.ReadAllText(path));
        var start = source.IndexOf("IReadOnlyList<string> Validate(", StringComparison.Ordinal);
        Assert.True(start >= 0, "ConversionArguments.Validate bulunamadı.");

        var end = source.IndexOf("\n    public static", start + 1, StringComparison.Ordinal);
        var body = StripInterpolations(end < 0 ? source[start..] : source[start..end]);

        return ErrorAdd.Matches(body)
            .Select(match => TipSources.Unescape(match.Groups["body"].Value).Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static readonly Regex ErrorAdd = new(
        "errors\\.Add\\(\\$?\"(?<body>(?:[^\"\\\\\n]|\\\\.)*)\"\\)", RegexOptions.Compiled);

    /// <summary>
    /// <c>LanguageCatalog</c>'un düz eşleşen doğrulama anahtarları. Sözlük gizli; ölçü onu
    /// kopyalamıyor, yansımayla okuyor — kopyalasa iki taraf ayrışırdı.
    /// </summary>
    /// <summary>
    /// Motorun iletisi olduğu hâlde <c>Validate</c>'ten çıkmayanlar. Kırpma biçimi hatası
    /// ayrıştırıcıda üretiliyor ve <c>LanguageCatalog</c>'da bir sabit olarak duruyor; ölçü
    /// onu adıyla değil, sınıftaki <c>const string</c> bildirimlerinden okuyor.
    /// </summary>
    private static IReadOnlyList<string> CatalogMessageConstants()
    {
        var source = StripComments(File.ReadAllText(TipSources.CatalogPath));
        return Regex.Matches(source, "const string \\w+ = \"(?<body>(?:[^\"\\\\\n]|\\\\.)*)\";")
            .Select(match => TipSources.Unescape(match.Groups["body"].Value))
            .ToList();
    }

    private static IReadOnlyList<string> KnownValidationMessages()
    {
        var field = typeof(LanguageCatalog).GetField("ValidationKeys",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        var map = (IReadOnlyDictionary<string, string>)field!.GetValue(null)!;
        return map.Keys.ToList();
    }

    [Fact]
    public void OynatmaSeridininErisilebilirAdlariDilAnahtarindanGelir()
    {
        var (english, turkish) = AppHost.Run(() =>
        {
            var strip = new ControlStrip();
            strip.SetLanguage("en");
            var first = new[]
            {
                AutomationProperties.GetName(strip.FindControl<Button>("Restart")!),
                AutomationProperties.GetName(strip.FindControl<Grid>("Timeline")!),
                AutomationProperties.GetName(strip.FindControl<Border>("Bar")!)
            };
            strip.SetLanguage("tr");
            var second = new[]
            {
                AutomationProperties.GetName(strip.FindControl<Button>("Restart")!),
                AutomationProperties.GetName(strip.FindControl<Grid>("Timeline")!),
                AutomationProperties.GetName(strip.FindControl<Border>("Bar")!)
            };
            return (first, second);
        });

        Assert.Contains("Back to the start", english);
        Assert.Contains("Control Strip", english);
        Assert.Contains("Başa Dön", turkish);
        Assert.Contains("Denetim Şeridi", turkish);
    }

    [Fact]
    public void TaninmayanMotorIletisiSessizceIngilizceyeDusmez()
    {
        Strings.Use("tr");
        Assert.Equal("Çevrilmemiş Motor İletisi: Codec Surprise", LanguageCatalog.Validation("codec surprise"));
    }

    [Fact]
    public void TaninmayanAsamaHamMotorMetniOlarakKorunur()
    {
        Strings.Use("tr");
        var method = typeof(MainWindow).GetMethod("LocalizeStage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal("Mystery Stage", method!.Invoke(null, new object[] { "mystery stage" }));
    }

    /// <summary>
    /// Kalite bölümündeki kaynak-yok uyarısı. Ölçü sözlüğe değil <b>ekrana</b> bakıyor:
    /// pencere Türkçeyken bloğun kendi metni okunuyor.
    /// </summary>
    [Fact]
    public void KaliteUyarisiEkrandaTurkce()
    {
        var shown = OnScreen(window => window.TxtQualityTargetNotice.Text ?? string.Empty);

        Assert.Equal(Cased("tr", "main.quality.no-source"), shown);
        Assert.DoesNotContain("Load A Video", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManşetOlculmediginde_EkrandaTurkce()
    {
        var shown = PerformanceLines();

        Assert.Contains(Cased("tr", "performance.headline.not-enough"), shown);
        Assert.DoesNotContain(shown, line => line.Contains("enough measured here", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HicOlculmediSatiri_EkrandaTurkce()
    {
        var shown = PerformanceLines();

        Assert.Contains(Cased("tr", "performance.line.not-measured"), shown);
        Assert.DoesNotContain(shown, line => line.Contains("on this machine yet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SinirCumlesi_EkrandaTurkce()
    {
        var shown = PerformanceLines();

        Assert.Contains(Cased("tr", "performance.boundary"), shown);
        Assert.DoesNotContain(shown, line => line.Contains("does not capture video", StringComparison.OrdinalIgnoreCase));
    }

    // ---- K5: dil düğmesi her şeyi çeviriyor ----------------------------------------

    /// <summary>
    /// Pencere açılır, tr → en → tr yapılır ve her turda <b>bütün</b> sekmelerin görünen
    /// metin düğümleri toplanır. Ölçüt: hiçbir düğüm öteki dilin metnini taşımıyor.
    /// Ağaç gezen çeviri kalktığı için bu ancak bağlar gerçekten tazelenirse yeşil verir.
    /// </summary>
    [Fact]
    public void DilDegisinceEkrandaOtekiDilinMetniKalmiyor()
    {
        var (first, second, third, neutral) = AppHost.Run(() =>
        {
            var window = new MainWindow();
            window.UseTurkish();

            var a = Sweep(window);
            var tuslarTr = Keymap.Rows.Select(row => Keymap.Gesture(row.Input)).ToHashSet(StringComparer.Ordinal);
            Strings.Use("en");
            var b = Sweep(window);
            var tuslarEn = Keymap.Rows.Select(row => Keymap.Gesture(row.Input)).ToHashSet(StringComparer.Ordinal);
            Strings.Use("tr");
            var c = Sweep(window);

            tuslarTr.IntersectWith(tuslarEn);
            return (a, b, c, tuslarTr);
        });

        AssertNoForeignText("tr", first, neutral);
        AssertNoForeignText("en", second, neutral);
        AssertNoForeignText("tr", third, neutral);
    }

    private static void AssertNoForeignText(string language, IReadOnlyCollection<string> shown, IReadOnlySet<string> neutral)
    {
        var other = string.Equals(language, "tr", StringComparison.Ordinal) ? "en" : "tr";
        var mine = Locales.Values(language);
        var theirs = Locales.Values(other);

        var foreign = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in theirs)
        {
            // Dil düğmesi kasten öteki dilin adını taşıyor: her dil kendi adını yazar.
            if (key == "main.language.name") continue;
            if (mine.TryGetValue(key, out var same) && string.Equals(same, value, StringComparison.Ordinal)) continue;
            foreign.Add(LanguageCatalog.Title(value, string.Equals(language, "tr", StringComparison.Ordinal) ? "en" : "tr"));
        }

        foreign.ExceptWith(neutral);
        var caught = shown.Where(foreign.Contains).Distinct().ToList();

        Assert.True(caught.Count == 0,
            $"Dil '{language}' iken ekranda öteki dilin metni duruyor:\n" + string.Join("\n", caught));
    }

    /// <summary>Bütün sekmeleri sırayla açıp görünen metin düğümlerini toplar.</summary>
    private static IReadOnlyList<string> Sweep(MainWindow window)
    {
        var size = new Size(1400, 1000);
        var texts = new List<string>();
        Relayout(window, size);
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();

        for (var index = 0; index < tabs.ItemCount; index++)
        {
            tabs.SelectedIndex = index;
            Relayout(window, size);

            texts.AddRange(window.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsShown())
                .Select(block => block.Text ?? string.Empty));

            texts.AddRange(window.GetVisualDescendants().OfType<ContentControl>()
                .Select(control => control.Content as string ?? string.Empty));
        }

        tabs.SelectedIndex = 0;
        Relayout(window, size);

        return texts.Where(text => text.Length > 0).ToList();
    }

    // ---- ortak düzenek --------------------------------------------------------------

    private static string Cased(string language, string key)
        => LanguageCatalog.Title(Locales.Values(language)[key].Replace(VidShrink.App.Localization.Strings.AppToken, VidShrink.Core.AppIdentity.Name), language);

    private static T OnScreen<T>(Func<MainWindow, T> read) =>
        AppHost.Run(() =>
        {
            var window = new MainWindow();
            window.UseTurkish();
            Relayout(window, new Size(1400, 1000));
            return read(window);
        });

    /// <summary>
    /// Başarım paneli Gelişmiş sekmesinde. Sekme adıyla değil <b>içeriğiyle</b> bulunuyor:
    /// ad artık dile göre değişiyor ve ölçüm o adı bilmek zorunda değil.
    /// </summary>
    private static IReadOnlyList<string> PerformanceLines() =>
        OnScreen(window =>
        {
            var size = new Size(1400, 1000);
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();

            for (var index = 0; index < tabs.ItemCount; index++)
            {
                tabs.SelectedIndex = index;
                Relayout(window, size);

                var panel = window.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(border => border.Name == "PerformancePanel");

                if (panel is not null)
                    return (IReadOnlyList<string>)panel.GetVisualDescendants().OfType<TextBlock>()
                        .Select(block => block.Text ?? string.Empty)
                        .ToList();
            }

            throw new InvalidOperationException("PerformancePanel hiçbir sekmede bulunamadı.");
        });

    /// <summary>
    /// Başsız ölçümde giriş canlandırması <see cref="Visual.TranslatePoint"/> sonucunu
    /// yalancı yapıyor. Ölçüm onu ortamdan devralmıyor: geçişten önce her panelin
    /// dönüşümü burada sıfırlanıyor.
    /// </summary>
    private static void Relayout(MainWindow window, Size size)
    {
        window.Width = double.NaN;
        window.Height = double.NaN;

        window.Measure(size);
        window.Arrange(new Rect(size));
        window.UpdateLayout();
        window.SettleFades();

        foreach (var node in window.GetVisualDescendants().OfType<Layoutable>()) node.InvalidateMeasure();

        var root = (Layoutable)window.GetVisualChildren().Single();
        root.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(new Rect(size));

        foreach (var node in window.GetVisualDescendants().OfType<Visual>())
            node.RenderTransform = null;
    }

    /// <summary>
    /// Baslik kurali govde cumlesine uygulanmaz: cumle isareti tasiyan metin dil dosyasinda
    /// yazildigi gibi kalir, yalniz satir basi buyur. Basliklarda kelime kelime buyutme surer.
    /// </summary>
    [Theory]
    [InlineData("tr", "Arayüz dilini değiştirir ve bir sonraki açılışta hatırlanır.")]
    [InlineData("tr", "Sabit klasör her çıktıyı kaynağın yanı yerine hep aynı yere gönderir.")]
    [InlineData("tr", "Bir dosya yükleyin; motorun verdiği her karar burada madde madde listelenir.")]
    [InlineData("en", "Changes the interface language and is remembered next time.")]
    [InlineData("en", "Load a file and every decision the engine makes is listed here.")]
    [InlineData("de", "Pausiert: Die aktuelle Datei wird fertig, die nächste wartet")]
    [InlineData("fr", "En pause : le fichier actuel se termine, le suivant attend")]
    public void GovdeCumlesiKelimeKelimeBuyutulmez(string language, string sentence)
    {
        Assert.True(LanguageCatalog.ReadsAsProse(sentence));
        Assert.Equal(sentence, LanguageCatalog.Title(sentence, language));
    }

    [Theory]
    [InlineData("tr", "doldurma politikası", "Doldurma Politikası")]
    [InlineData("tr", "ses hedefi (kbps)", "Ses Hedefi (kbps)")]
    [InlineData("en", "fill policy", "Fill Policy")]
    [InlineData("de", "status: bereit", "Status: Bereit")]
    [InlineData("en", "stage: pass two", "Stage: Pass Two")]
    public void BaslikKelimeKelimeBuyutulmeyeDevamEder(string language, string heading, string expected)
    {
        Assert.False(LanguageCatalog.ReadsAsProse(heading));
        Assert.Equal(expected, LanguageCatalog.Title(heading, language));
    }

    /// <summary>
    /// Nokta her zaman cumle isareti degil: <c>H.264</c> ve <c>storage.to</c> bir sozcugun
    /// icinde durur, olcu bunlari govde saymamali.
    /// </summary>
    [Theory]
    [InlineData("H.264")]
    [InlineData("storage.to")]
    [InlineData("1280x720")]
    public void SozcukIcindekiNoktaGovdeSaymaz(string text) => Assert.False(LanguageCatalog.ReadsAsProse(text));

    /// <summary>
    /// Her iki dil dosyasindaki cumle isaretli her deger icin: satir basindaki sozcuk
    /// disinda **hicbir sozcuk degismemeli** — bildirilmis ad yazimlari haric.
    /// Tuttugu sey kelime kelime buyutmenin govdeye geri gelmemesi.
    ///
    /// <para>T192 tur 3'te yeniden temellendirildi. Once satir basi <c>Names</c> kuralina
    /// tabiydi, govde degildi; sonuc, ayni sozcugun cumlenin neresinde durduguna gore
    /// farkli yazilmasiydi ("Ffmpeg'in" satir basinda "FFmpeg'in", ortada "ffmpeg").
    /// Ad yazimi konuma bagli olamaz, bu yuzden <c>Names</c> gecidi artik her sozcukte
    /// gecerli ve olcu onu bozulma degil <b>ad duzeltmesi</b> diye sayiyor. Iki sayi da
    /// ekranda: kac sozcuk bozuldu, kac sozcuk ad olarak duzeldi.</para>
    /// </summary>
    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void DilDosyasindakiButunGovdeCumleleriOlduguGibiKalir(string language)
    {
        var prose = Locales.Values(language)
            .Where(pair => LanguageCatalog.ReadsAsProse(pair.Value))
            .ToList();

        Assert.NotEmpty(prose);

        var bozulan = new List<string>();
        var adDuzelen = new List<string>();
        var karsilastirilan = 0;

        foreach (var (key, value) in prose)
        {
            var shown = LanguageCatalog.Title(value, language);

            foreach (var (kaynak, ekran) in value.Split('\n').Zip(shown.Split('\n')))
            {
                var kaynakSozcukler = kaynak.Split(' ');
                var ekranSozcukler = ekran.Split(' ');

                if (kaynakSozcukler.Length != ekranSozcukler.Length)
                {
                    bozulan.Add($"{key}: sozcuk sayisi degisti");
                    continue;
                }

                var basAtlandi = false;
                for (var i = 0; i < kaynakSozcukler.Length; i++)
                {
                    if (!basAtlandi && kaynakSozcukler[i].Any(char.IsLetterOrDigit))
                    {
                        basAtlandi = true;
                        continue;
                    }

                    karsilastirilan++;
                    if (string.Equals(kaynakSozcukler[i], ekranSozcukler[i], StringComparison.Ordinal))
                        continue;

                    var kayit = $"{key}: '{kaynakSozcukler[i]}' -> '{ekranSozcukler[i]}'";
                    if (AdYazimiDuzeltmesi(kaynakSozcukler[i], ekranSozcukler[i]))
                        adDuzelen.Add(kayit);
                    else
                        bozulan.Add(kayit);
                }
            }
        }

        Assert.True(karsilastirilan > 1000, $"Karsilastirilan sozcuk sayisi cok dusuk: {karsilastirilan}");
        Assert.True(bozulan.Count == 0,
            $"Dil '{language}': {prose.Count} govde degeri, {karsilastirilan} sozcuk; "
            + $"{bozulan.Count} sozcuk bozuldu:\n" + string.Join("\n", bozulan)
            + $"\nAd duzeltmesi ({adDuzelen.Count}):\n" + string.Join("\n", adDuzelen));
    }

    /// <summary>
    /// Iki sozcuk yalnizca bildirilmis ad yazimi yuzunden mi ayriliyor? Fark buyuk/kucuk
    /// harften ibaret olmali ve ekrandaki sozcugun bas tarafi <c>Names</c>'deki yazimla
    /// baslamali. Baska her fark bozulmadir.
    /// </summary>
    private static bool AdYazimiDuzeltmesi(string kaynak, string ekran)
    {
        if (!string.Equals(kaynak, ekran, StringComparison.OrdinalIgnoreCase)) return false;

        var offset = 0;
        while (offset < ekran.Length && !char.IsLetter(ekran[offset]))
        {
            if (char.IsDigit(ekran[offset])) return false;
            offset++;
        }

        if (offset == ekran.Length) return false;

        var govde = ekran[offset..];
        var token = new string(govde.TakeWhile(char.IsLetterOrDigit).ToArray());
        var bare = new string(govde.TakeWhile(char.IsLetter).ToArray());
        if (LanguageCatalog.Names.TryGetValue(token, out var ad))
            return govde.StartsWith(ad, StringComparison.Ordinal);
        if (LanguageCatalog.Names.TryGetValue(bare, out var isim))
            return govde.StartsWith(isim, StringComparison.Ordinal);
        return false;
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("fa")]
    [InlineData("he")]
    [InlineData("ur")]
    public void Sagdan_sola_diller_taninir(string language)
    {
        Assert.True(Strings.IsRightToLeftLanguage(language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("zh-Hans")]
    [InlineData("ja")]
    [InlineData("")]
    [InlineData(null)]
    public void Soldan_saga_diller_sagdan_sola_sayilmaz(string? language)
    {
        Assert.False(Strings.IsRightToLeftLanguage(language));
    }

    [Fact]
    public void Sagdan_sola_listesi_kurulumdaki_dillerin_altkumesi()
    {
        var shipped = Strings.Languages;
        foreach (var code in Strings.RightToLeftLanguages)
            Assert.Contains(shipped, l => string.Equals(l, code, StringComparison.OrdinalIgnoreCase));
    }
}
