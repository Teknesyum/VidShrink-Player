using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using VidShrink.App;
using VidShrink.App.Localization;
using VidShrink.Core;
using Xunit.Abstractions;

namespace VidShrink.Tests;

/// <summary>
/// T192 K2 ve K3.
///
/// <para><b>K2 — sayi bicimi kulturden gelir.</b> Iki yanlis vardi. <c>MainWindow.Num</c>
/// sabit <see cref="System.Globalization.CultureInfo.InvariantCulture"/> kullaniyordu, yani
/// Turkce arayuzde de nokta yaziyordu; buna karsilik ekranin bir bolumu <c>Num</c>'dan hic
/// gecmiyor, C# ara deger dizgesiyle (<c>$"{x:0.0}"</c>) yaziliyordu ve o dizge
/// <b>makinenin</b> kulturunu kullandigi icin Ingilizce arayuzde <c>15,6 MB</c> ciktisi
/// veriyordu. Olcu sabiti sabitle karsilastirmiyor: gercek bicimlendiriciyi iki dilde
/// cagirip ciktilarin birbirinden <b>farkli</b> oldugunu da tutuyor, boylece kulturu yeniden
/// sabitlemek bu olcuyu kirar.</para>
///
/// <para><b>K3 — baslik kurali cumleye uygulanmaz.</b> <c>fd6fe0c1</c>'in kurdugu
/// <c>ReadsAsProse</c> yalniz cumle isaretine bakiyordu; nokta tasimayan govde cumleleri
/// ("Load a file to see the two sides") baslik kolundan geciyordu. Ayni mekanizma
/// genisletildi: islev sozcugu tasiyan metin de govde sayilir.</para>
/// </summary>
public sealed class BiciminTests : IDisposable
{
    private readonly string _onceki = Strings.Language;

    public void Dispose() => Strings.Use(_onceki);

    [Theory]
    [InlineData("en", "15.6")]
    [InlineData("tr", "15,6")]
    public void OndalikAyiriciDildenGelir(string dil, string beklenen)
    {
        Strings.Use(dil);

        Assert.Equal(beklenen, MainWindow.Num(15.6, "0.0"));
    }

    [Theory]
    [InlineData("en", "3.7%")]
    [InlineData("tr", "%3,7")]
    public void YuzdeIsaretininYeriDildenGelir(string dil, string beklenen)
    {
        Strings.Use(dil);

        Assert.Equal(beklenen, MainWindow.Percent(0.037));
    }

    /// <summary>
    /// Sabit bir kulture geri donulurse iki dilin ciktisi esitlenir. Bu olcu esitligi
    /// reddediyor, dolayisiyla kulturu sabitleyen bir mutasyon kirmizi doner.
    /// </summary>
    [Fact]
    public void IkiDilinBicimiAyniDegildir()
    {
        Strings.Use("en");
        var ingilizceSayi = MainWindow.Num(15.6, "0.0");
        var ingilizceYuzde = MainWindow.Percent(0.037);

        Strings.Use("tr");
        var turkceSayi = MainWindow.Num(15.6, "0.0");
        var turkceYuzde = MainWindow.Percent(0.037);

        Assert.NotEqual(ingilizceSayi, turkceSayi);
        Assert.NotEqual(ingilizceYuzde, turkceYuzde);
    }

    /// <summary>
    /// <b>Ne olculuyor:</b> aralik satirinin <b>bilesenleri</b> ve bunlarin yan yana
    /// gelmis hali — ekran yolu degil. Bicim dizgesi burada kopyalanmis durumda,
    /// dolayisiyla bu olcu <c>RefreshEstimateView</c>'deki bir degisikligi yakalamaz.
    /// Ekranin kendi satiri
    /// <see cref="KareYerlesimTests.TahminAraligiSatiriEkrandaDileUyar"/> ile olculuyor;
    /// T192 tur 2'ye kadar bu ayrim rapor tablosunda yaziyordu ama testin adinda
    /// yazmiyordu.
    /// </summary>
    [Theory]
    [InlineData("en", "15.4 - 15.8 MB · 3.7%")]
    [InlineData("tr", "15,4 - 15,8 MB · %3,7")]
    public void AralikSatirininBilesenleriDileUyar(string dil, string beklenen)
    {
        Strings.Use(dil);

        var dusuk = MainWindow.Num(15.4, "0.0");
        var yuksek = MainWindow.Num(15.8, "0.0");
        var satir = $"{dusuk} - {yuksek} MB · " + MainWindow.Percent(0.037);

        Assert.Equal(beklenen, satir);
    }

    /// <summary>
    /// T192 tur 2 K9: <c>DescribeBytes</c> paylasim tavanini ekrana yaziyor
    /// (<c>TxtShareCeiling</c>). Tam ikilik katlar (128 MiB, 25 GiB) ondalik tasimadigi
    /// icin <c>SettingsTabTests</c>'teki pimler bu degisiklikten etkilenmiyor; ondalikli
    /// bir tavan ise dile uyar.
    /// </summary>
    [Theory]
    [InlineData("en", "1.5 GiB")]
    [InlineData("tr", "1,5 GiB")]
    public void PaylasimTavaniDileUyar(string dil, string beklenen)
    {
        Strings.Use(dil);

        Assert.Equal(beklenen, MainWindow.DescribeBytes(1_610_612_736L));
    }

    [Theory]
    [InlineData("playback.panel.hint", false)]
    [InlineData("main.plan.title", false)]
    [InlineData("main.section.quality", false)]
    [InlineData("main.section.frame", false)]
    [InlineData("playback.panel.hint", true)]
    [InlineData("main.section.quality", true)]
    public void GovdeCumlesiDilDosyasindakiGibiKalir(string anahtar, bool turkce)
    {
        var ham = Strings.GetIn(turkce ? "tr" : "en", anahtar);

        Assert.True(LanguageCatalog.ReadsAsProse(ham), $"'{ham}' govde sayilmadi");
        Assert.Equal(ham, LanguageCatalog.Title(ham, turkce ? "tr" : "en"));
    }

    /// <summary>
    /// Karsi yon: gercek basliklar kelime kelime buyutulmeye devam eder. Bu olmasa
    /// <c>ReadsAsProse</c>'u her zaman <c>true</c> dondurmek yesil kalirdi.
    /// </summary>
    [Theory]
    [InlineData("main.info.video-codec", false, "Video Codec")]
    [InlineData("main.info.fps", false, "FPS")]
    [InlineData("main.output.current-size", false, "Current Output Size")]
    [InlineData("main.info.video-codec", true, "Video Kodeği")]
    public void BaslikKelimeKelimeBuyutulmeyeDevamEder(string anahtar, bool turkce, string beklenen)
    {
        var ham = Strings.GetIn(turkce ? "tr" : "en", anahtar);

        Assert.False(LanguageCatalog.ReadsAsProse(ham), $"'{ham}' yanlislikla govde sayildi");
        Assert.Equal(beklenen, LanguageCatalog.Title(ham, turkce ? "tr" : "en"));
    }
}

/// <summary>
/// T192 K4 ve dorduncu madde. Olcum basiz pencerede, gercek yerlesim motoruyla.
///
/// <para><b>Kaynak bilgi izgarasi.</b> <c>InfoGrid</c> bir <see cref="SutunIzgara"/>:
/// en fazla dort sutun, her sutun kendi en genis hucresi kadar; toplam sigmazsa iki, sonra bir sutuna iner
/// (2026-09-23: sabit dort sutunda deger her dilde ucnoktayla kesiliyordu). Once
/// hucre kirpmiyordu. Turkce etiket Ingilizceden uzun
/// oldugu icin "Video kodeği" kendi hucresinden tasip yanindaki "Ses" etiketinin
/// uzerine biniyordu. Olcu her etiketin genisligini kendi hucresinin genisligiyle
/// karsilastirir; tasma varsa kirmizi doner.</para>
///
/// <para><b>Katlanmis bolum ozeti.</b> Baslik satiri yatay bir <see cref="StackPanel"/>
/// idi; yatay yigin cocuguna sonsuz genislik verir, dolayisiyla ozet kendi istedigi
/// genislikte olculur ve panel kenarinda dumduz kesilirdi ("Kare Hızı D…" degil,
/// harfin ortasindan). Satir <see cref="Grid"/>'e cevrildi; S20 taramasi ucnoktali ozetin
/// dar pencerede 10 px'e indigini gosterdi, ozet artik basligin altinda kendi satirinda
/// sariliyor ve bossa gizleniyor.</para>
///
/// <para><b>Turetme satiri.</b> Satir <c>TxtTarget.Text</c>'i okur ama yalnizca boyut
/// tavani kapaliyken yenileniyordu; kutuda 24 yazarken satir "Hedef 16 MB" diye
/// kaliyordu.</para>
/// </summary>
public sealed class KareYerlesimTests
{
    /// <summary>
    /// Karenin cekildigi olcu. Pencerenin izin verdigi en dar olcu (1040x720,
    /// <c>MainWindow.axaml</c>, <c>MinWidth="1040"</c>) de olculuyor: T194 karari tek satir,
    /// sigmayan metin ucnoktayla kisalir ve tam hali balonda durur.
    /// </summary>
    public static readonly Size Genis = new(1600, 1000);

    public static readonly Size Dar = new(1040, 720);

    public static TheoryData<string, double, double> IkiDilTekOlcu() => new()
    {
        { "tr", 1600, 1000 },
        { "en", 1600, 1000 },
        { "tr", 1040, 720 },
        { "en", 1040, 720 }
    };

    private static T Read<T>(string dil, Func<MainWindow, T> read) => Read(dil, Genis, read);

    private static T Read<T>(string dil, Size olcu, Func<MainWindow, T> read) =>
        AppHost.Run(() =>
        {
            var onceki = Strings.Language;
            Strings.Use(dil);
            try
            {
                var window = new MainWindow
                {
                    Width = olcu.Width,
                    Height = olcu.Height
                };

                if (window.Content is Layoutable govde)
                {
                    govde.Width = olcu.Width;
                    govde.Height = olcu.Height;
                }

                window.Measure(olcu);
                window.Arrange(new Rect(olcu));
                window.UpdateLayout();

                Visual? node = window.GetVisualDescendants().OfType<SutunIzgara>()
                    .Single(grid => grid.Name == "InfoGrid");
                while (node is not null)
                {
                    if (node is Control control) control.IsVisible = true;
                    node = node.GetVisualParent();
                }

                window.Measure(olcu);
                window.Arrange(new Rect(olcu));
                window.UpdateLayout();
                return read(window);
            }
            finally
            {
                Strings.Use(onceki);
            }
        });

    private static T Named<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>
    /// Etiketin <b>sarilmamis</b> genisligi olculuyor. T192 tur 1'de olcu
    /// <c>label.TextLayout.Width</c>'e bakiyordu, ama etiketlerde
    /// <c>TextWrapping="Wrap"</c> var (<c>MainWindow.axaml:262-291</c>): sarilan metnin
    /// yerlesim genisligi hucreyi <b>hic asamaz</b>, dolayisiyla kosul her zaman yanlis
    /// donuyordu ve olcu mutasyona oluydu. Sarma kapatilip yeniden olculuyor; boylece
    /// etiket uzarsa olcu kirmiziya doner.
    /// <see cref="OlcuUzunEtiketiYakalar"/> bunu ayrica kanitliyor.
    /// </summary>
    [Theory]
    [MemberData(nameof(IkiDilTekOlcu))]
    public void KaynakBilgiEtiketleriKendiHucresindeKalir(string dil, double genislik, double yukseklik)
    {
        var olcu = new Size(genislik, yukseklik);
        var (tasan, satirlar, sutun) = Read(dil, olcu, window =>
        {
            var (t, s) = Tasanlar(window, olcu, null, kirpmaKapali: false, balonKapali: false);
            return (t, s, Named<SutunIzgara>(window, "InfoGrid").Columns);
        });

        var klasor = Path.Combine(TipSources.Root, ".calisma", "t194");
        var ad = $"infogrid-{dil}-{genislik:0}x{yukseklik:0}.txt";
        Directory.CreateDirectory(klasor);
        File.WriteAllLines(Path.Combine(klasor, ad), satirlar);

        Assert.True(tasan.Count == 0, string.Join(Environment.NewLine, tasan));
        Assert.DoesNotContain(satirlar, satir => satir.Contains("kisaldi", StringComparison.Ordinal));
        if (olcu == Dar) Assert.True(sutun < 4, $"dar pencerede {sutun} sutun");

        Kapat(klasor, ad);
    }

    /// <summary>
    /// Olcunun mutasyon sinavi: hucreye sigmayacak kadar uzun bir etiketin kirpmasi ya da
    /// balonu kaldirilinca olcu <b>kirmizi</b> donmeli. Donmezse yukaridaki yesil bir sey
    /// soylemiyor demektir.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OlcuUzunEtiketiYakalar(bool kirpmaKapali, bool balonKapali)
    {
        var (tasan, _) = Read("tr", Genis, window => Tasanlar(window, Genis, string.Concat(Enumerable.Repeat("Cok Uzun Bir Kaynak Bilgi Etiketi Ornegi Daha Da Uzun ", 6)), kirpmaKapali, balonKapali));

        Assert.NotEmpty(tasan);
    }

    /// <summary>
    /// Her hucrenin metinlerini yeniden yerlestirir. Kural: tek gorsel satir, hucre
    /// genisligini asmaz; dogal genisligi hucreyi asan metin ucnoktayla kisalir ve balonu
    /// metnin tam halini tasir. <paramref name="mutasyon"/> verilirse ilk etiketin metni
    /// onunla degistirilir.
    /// </summary>
    private static (List<string> Tasan, List<string> Satirlar) Tasanlar(MainWindow window, Size olcu, string? mutasyon, bool kirpmaKapali, bool balonKapali)
    {
        var grid = window.GetVisualDescendants().OfType<SutunIzgara>()
            .Single(g => g.Name == "InfoGrid");

        var hucreler = grid.Children
            .OfType<StackPanel>()
            .SelectMany(cell => cell.Children.OfType<TextBlock>().Select(text => (cell, text)))
            .ToList();

        foreach (var (_, text) in hucreler)
            if (string.IsNullOrEmpty(text.Text)) text.Text = "hevc (Main 10) · 3840x2160";

        if (mutasyon is not null)
        {
            var ilk = hucreler[0].text;
            ilk.Text = mutasyon;
            if (kirpmaKapali) ilk.TextTrimming = TextTrimming.None;
            if (balonKapali) ToolTip.SetTip(ilk, null);
        }

        window.Measure(olcu);
        window.Arrange(new Rect(olcu));
        window.UpdateLayout();

        var tasan = new List<string>();
        var satirlar = new List<string>();
        foreach (var (cell, text) in hucreler)
        {
            var dogal = new TextBlock
            {
                Text = text.Text,
                FontFamily = text.FontFamily,
                FontSize = text.FontSize,
                FontWeight = text.FontWeight,
                FontStyle = text.FontStyle,
                LetterSpacing = text.LetterSpacing,
                TextWrapping = TextWrapping.NoWrap
            };
            dogal.Measure(Size.Infinity);
            var dogalGenislik = dogal.DesiredSize.Width;
            var hucre = cell.Bounds.Width;
            var kisaldi = dogalGenislik > hucre + 0.5;
            var satir = text.TextLayout.TextLines.Count;
            var cizilen = text.TextLayout.Width;
            var balon = ToolTip.GetTip(text) as string;

            satirlar.Add($"{text.Text}: dogal {dogalGenislik:0.#} px, cizilen {cizilen:0.#} px, hucre {hucre:0.#} px, satir {satir}, {(kisaldi ? "kisaldi" : "sigdi")}, balon {(balon == text.Text ? "tam" : balon ?? "yok")}");

            if (satir != 1) tasan.Add($"{text.Text}: {satir} satir");
            if (cizilen > hucre + 0.5) tasan.Add($"{text.Text}: cizilen {cizilen:0.#} px, hucre {hucre:0.#} px");
            if (kisaldi && text.TextTrimming == TextTrimming.None) tasan.Add($"{text.Text}: kisalmasi gerekiyor, kirpma yok");
            if (kisaldi && balon != text.Text) tasan.Add($"{text.Text}: kisaldi ama balonda tam metin yok ({balon ?? "yok"})");
        }

        return (tasan, satirlar);
    }

    /// <summary>
    /// Uzun bir ozet metni panelin disina tasmaz. Metin dogrudan yaziliyor cunku olculen
    /// sey ozetin icerigi degil, satirin uzun metni nasil tasidigi.
    ///
    /// <para>O3: satirin icinde kalmak tek basina sarmayi olcmuyor — <c>TextWrapping="Wrap"</c>
    /// yerine kirpma konsa metin yine satirin icinde kalirdi. Bu yuzden sarma niteligi kendi
    /// pimini aldi: uzun metin birden fazla satira boluniyor ve blokta kirpma yok.</para>
    /// </summary>
    [Theory]
    [InlineData("TxtFrameSummary")]
    [InlineData("TxtQualitySummary")]
    [InlineData("TxtAudioSummary")]
    [InlineData("TxtAdvancedSummary")]
    public void KatlanmisBolumOzetiSatirinIcindeKalir(string ad)
    {
        var okunan = Read("tr", window =>
        {
            var ozet = Named<TextBlock>(window, ad);
            ozet.Text = "Çözünürlük Düşürülebilir · Kare Hızı Düşürülebilir · Ses Yeniden Kodlanabilir · Altyazı Korunur · Bölümler Korunur";

            window.Measure(Genis);
            window.Arrange(new Rect(Genis));
            window.UpdateLayout();

            var satir = (Layoutable)ozet.GetVisualParent()!;
            return (sag: ozet.Bounds.Left + ozet.TextLayout.Width, genislik: satir.Bounds.Width,
                metinSatiri: ozet.TextLayout.TextLines.Count, kirpma: ozet.TextTrimming);
        });

        Assert.True(okunan.sag <= okunan.genislik + 0.5,
            $"ozet metni {okunan.sag:0.#} px'te bitiyor, satir {okunan.genislik:0.#} px");
        Assert.True(okunan.metinSatiri > 1, $"{ad} sarmadi: {okunan.metinSatiri} satir");
        Assert.Equal(TextTrimming.None, okunan.kirpma);
    }

    private const string OrnekYol = @"C:\Kayitlar\tatil-cekimi-2160p60.mkv";

    /// <summary>
    /// <c>WindowLayoutTests.Sample</c> ile ayni tasiyici kaynak. Yoklama cagrilmiyor;
    /// olcum diskteki hicbir dosyaya ve hicbir dis araca bagli degil.
    /// </summary>
    private static MediaInfo Ornek() => new()
    {
        FilePath = OrnekYol,
        FileSizeBytes = 420_000_000L,
        DurationSeconds = 187.5,
        Width = 3840,
        Height = 2160,
        Fps = 59.94,
        VideoCodec = "hevc",
        TotalBitrateBps = 18_800_000,
        AudioCodec = "aac",
        AudioBitrateBps = 192_000,
        AudioChannels = 2,
        PixelFormat = "yuv420p"
    };

    /// <summary>
    /// Gercek yukleme yolu: <c>LoadWithoutProbing</c> -> <c>ApplyLoaded</c> -> <c>ShowInfo</c>
    /// ve <c>Recalculate</c>. Ekrana yazan kod bu yoldan geciyor, dolayisiyla bicim dizgesi
    /// testte kopyalanmiyor.
    /// </summary>
    private static T Yuklu<T>(string dil, Func<MainWindow, T> read) => Yuklu(dil, Ornek(), read);

    private static T Yuklu<T>(string dil, MediaInfo bilgi, Func<MainWindow, T> read) =>
        AppHost.Run(() =>
        {
            var onceki = Strings.Language;
            Strings.Use(dil);
            try
            {
                var window = new MainWindow
                {
                    Width = Genis.Width,
                    Height = Genis.Height
                };

                if (window.Content is Layoutable govde)
                {
                    govde.Width = Genis.Width;
                    govde.Height = Genis.Height;
                }

                window.LoadWithoutProbing(OrnekYol, bilgi);
                window.SettleFades();
                window.Measure(Genis);
                window.Arrange(new Rect(Genis));
                window.UpdateLayout();
                return read(window);
            }
            finally
            {
                Strings.Use(onceki);
            }
        });

    /// <summary>
    /// T192 tur 2 K9. <c>TxtFps</c> sabit <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
    /// ile yaziliyordu; Turkce arayuzde kare hizi <c>29.97</c> gorunuyordu. Olcu gercek
    /// yukleme yolundan geciyor.
    /// </summary>
    [Theory]
    [InlineData("en", "59.94")]
    [InlineData("tr", "59,94")]
    public void KaynakBilgiKareHiziDileUyar(string dil, string beklenen)
        => Assert.Equal(beklenen, Yuklu(dil, window => Named<TextBlock>(window, "TxtFps").Text));

    /// <summary>
    /// Kaynak bilgisindeki cozunurluk ailenin carpi isaretini kullanir. Bu satirin hic
    /// pimi yoktu: ayni uygulama <c>1920x1080</c> (kaynak bilgisi, plan gercegi, gelismis
    /// panel), <c>1920×1080</c> (oynatici, kaydedici ozeti) ve <c>1920 × 1080</c> (bolge
    /// secicileri) diye uc turlu yaziyordu. Kultur almaz: tam sayida basamak ayraci
    /// istemiyoruz, iki dilde ayni yazim bekleniyor.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void KaynakBilgiCozunurluguCarpiIsaretiyle(string dil)
    {
        var yazi = Yuklu(dil, window => Named<TextBlock>(window, "TxtResolution").Text ?? "");

        Assert.Equal("3840×2160", yazi);
        Assert.DoesNotContain("x", yazi);
    }

    /// <summary>
    /// Plan panelinin ve gelismis panelin cozunurluk satirlari da ailenin yazimini
    /// kullanir. Ikisi de <c>$"{Width}x{Height}"</c> ile kuruluyordu ve **hicbir olcu
    /// okumuyordu**: adim 5'in mutasyonunda ikisi de 0 kirmizi verdi. Deger satiri
    /// anahtarin yaninda duruyor; "x yok" iddiasi tum panele degil o hucreye yazili
    /// (kodek adi <c>libx264</c> panelde x tasiyor).
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void PlanVeGelismisPanelCozunurluguCarpiIsaretiyle(string dil)
    {
        var okunan = Yuklu(dil, window =>
        {
            var hucreler = window.FindControl<Panel>("PlanFacts")!.Children.OfType<TextBlock>()
                .Select(t => t.Text ?? "").ToArray();
            return (hucreler, gelismis: Named<TextBlock>(window, "TxtAdvMinResolutionNow").Text ?? "");
        });

        Strings.Use(dil);
        var anahtar = Array.IndexOf(okunan.hucreler, Strings.Get("main.plan.fact.resolution"));

        Assert.True(anahtar >= 0, string.Join(" | ", okunan.hucreler));

        var deger = okunan.hucreler[anahtar + 1];

        Assert.Contains("×", deger);
        Assert.DoesNotContain("x", deger);
        Assert.Contains("×", okunan.gelismis);
        Assert.DoesNotContain("x", okunan.gelismis);
    }

    /// <summary>
    /// Bit hizi ekranda <b>yuvarlanir</b>, kirpilmaz. Bolme tamsayi yapildiginda
    /// 1.499.600 bps ekrana 1499 kbps diye cikiyordu; ayni deger baska bir akista
    /// <c>double</c> ile bolunup 1500 yaziyordu, yani tek kaynak iki sayi gosterebiliyordu.
    /// Govde (<see cref="Bicim.BitHizi.BpsToKbps"/>) tek bolme yapar. Ses izi ayri
    /// pimli cunku iki satir iki ayri cagri yeri.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    public void KaynakBilgiBitHiziYuvarlanir(string dil)
    {
        var bilgi = Ornek() with { TotalBitrateBps = 1_499_600, AudioBitrateBps = 191_500 };

        var okunan = Yuklu(dil, bilgi, window => (
            hiz: Named<TextBlock>(window, "TxtBitrate").Text ?? "",
            ses: Named<TextBlock>(window, "TxtAudio").Text ?? ""));

        Assert.Contains("1500", okunan.hiz);
        Assert.DoesNotContain("1499", okunan.hiz);
        Assert.Contains("192", okunan.ses);
        Assert.DoesNotContain("191", okunan.ses);
    }

    /// <summary>
    /// T192 tur 2, tur 1 borcu 3: tahmin araligi satirinin <b>ekrandaki</b> hali.
    /// <c>RefreshEstimateView</c> gercekten kosuyor; bicim dizgesi testte kopyalanmiyor.
    /// </summary>
    [Theory]
    [InlineData("en", ".", ",")]
    [InlineData("tr", ",", ".")]
    public void TahminAraligiSatiriEkrandaDileUyar(string dil, string ayirici, string yabanci)
    {
        var satir = Yuklu(dil, window => Named<TextBlock>(window, "TxtEstimateRange").Text ?? "");

        Assert.Contains("MB", satir);
        Assert.Contains(ayirici, satir);
        Assert.DoesNotContain(yabanci, satir);
    }

    /// <summary>
    /// S9: dile göre <b>değişen</b> birimler (MB, AI, <c>score-suffix</c>,
    /// <c>score-value</c>, <c>advanced.mode.crf</c>) gerçek yükleme yolundan okunur.
    /// Fransızca "Mo" ve "IA", Rusça "МБ" ve "ИИ" yazar; İngilizce sabit ("MB", "AI")
    /// o dillerde görünmez. <c>kbps-value</c>, <c>fps-value</c>, <c>k-value</c> ve
    /// <c>plan.mode.crf-value</c> şablonları 42 dilde bayt bayt aynı, bu yüzden dil kolu
    /// onları ayirt edemez; burada yalnız metnin biriminde bittiği pimlenir, şablonun dil
    /// dosyasından geldiği <c>HizKareKbitVeCrfBirimleriDilDosyasindanGelir</c>'de enjeksiyonla
    /// ölçülür.
    /// </summary>
    [Theory]
    [InlineData("en", "MB", "AI")]
    [InlineData("fr", "Mo", "IA")]
    [InlineData("ru", "МБ", "ИИ")]
    public void BirimlerDilDosyasindanGelir(string dil, string mb, string ai)
    {
        var okunan = Yuklu(dil, window =>
        {
            var hedef = Named<TextBox>(window, "TxtTarget");
            var hedefIzgara = ((Grid)hedef.Parent!).Children;
            var hedefBirimi = (hedefIzgara[hedefIzgara.IndexOf(hedef) + 1] as TextBlock)?.Text ?? "";
            var kalite = Named<TextBox>(window, "TxtQualityTarget");
            var kaliteIzgara = ((Grid)kalite.Parent!).Children;
            var kaliteBirimi = (kaliteIzgara[kaliteIzgara.IndexOf(kalite) + 1] as TextBlock)?.Text ?? "";
            var kip = (window.FindControl<ComboBox>("CmbQualityMode")!.Items[0] as ComboBoxItem)?.Content?.ToString() ?? "";
            var plan = string.Join(" | ", window.FindControl<Panel>("PlanFacts")!.Children.OfType<TextBlock>().Select(t => t.Text));
            return (hedefBirimi, kaliteBirimi, kip,
                boyut: Named<TextBlock>(window, "TxtSize").Text ?? "",
                hiz: Named<TextBlock>(window, "TxtBitrate").Text ?? "",
                aralik: Named<TextBlock>(window, "TxtEstimateRange").Text ?? "",
                not: Named<TextBlock>(window, "TxtEstimateNote").Text ?? "",
                plan);
        });

        Strings.Use(dil);
        try
        {
            var kanit = Path.Combine(GirdiKanit.Root, ".calisma", "s9-birimler");
            Directory.CreateDirectory(kanit);
            var dosya = Path.Combine(kanit, dil + ".txt");
            File.WriteAllText(dosya, okunan.ToString());

            Assert.Equal(ai, Strings.Get("main.plan.ai"));
            Assert.Equal(mb, okunan.hedefBirimi);
            Assert.EndsWith(" " + mb, okunan.boyut);
            Assert.EndsWith(" " + mb, okunan.aralik.Split(" · ")[0]);
            Assert.Equal(Strings.Get("main.unit.score-suffix"), okunan.kaliteBirimi);
            Assert.EndsWith(Strings.Get("main.unit.score-value", ""), okunan.not);
            Assert.Equal(Strings.Get("main.advanced.mode.crf"), okunan.kip);
            Assert.Equal(Strings.Get("main.unit.kbps-value", "18800"), okunan.hiz);
            Assert.DoesNotContain("kbps", okunan.hiz, StringComparison.Ordinal);
            Assert.Contains(" FPS", okunan.plan);
            if (dil != "en")
            {
                Assert.DoesNotContain(" MB", okunan.boyut + okunan.aralik + okunan.plan);
                Assert.NotEqual("MB", okunan.hedefBirimi);
            }

            File.Delete(dosya);
            if (Directory.GetFileSystemEntries(kanit).Length == 0) Directory.Delete(kanit);
        }
        finally
        {
            Strings.Use("en");
        }
    }

    /// <summary>
    /// Dil dosyalarinin tamamini <c>.calisma</c> altina kopyalar ve Ingilizce katalogda
    /// verilen sablonlari degistirir. Sablonu koddan yazan bir mutasyon bu enjeksiyondan
    /// etkilenmez, dolayisiyla olcum sablonun gercekten dil dosyasindan okundugunu tutar.
    /// </summary>
    /// <summary>
    /// <see cref="Kopya"/>'nin biraktigi <c>.calisma/&lt;ad&gt;</c> klasorunu kapatir. Denetim
    /// borcu: 42 dilin tam kopyasi ve kanit dosyasi agacta kaliyordu. <see cref="Tut"/> acikken
    /// silinmez — testler iddialardan <b>sonra</b> kapatir, boylece kirmizi bir kosumun kaniti
    /// yerinde durur, yesil kosum kendi biraktigini siler.
    /// </summary>
    private sealed class DilKopyasi : IDisposable
    {
        public DilKopyasi(string klasor, string locales) { Klasor = klasor; Locales = locales; }

        public string Klasor { get; }

        public string Locales { get; }

        public bool Tut { get; set; } = true;

        public void Dispose()
        {
            if (Tut) return;
            try { if (Directory.Exists(Klasor)) Directory.Delete(Klasor, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static DilKopyasi Kopya(string ad, Dictionary<string, string> degisim)
    {
        var kaynak = Path.Combine(AppContext.BaseDirectory, "Locales");
        var klasor = Path.Combine(GirdiKanit.Root, ".calisma", ad);
        var kopya = Path.Combine(klasor, "Locales");
        foreach (var dosya in Directory.GetFiles(kaynak, "*", SearchOption.AllDirectories))
        {
            var hedef = Path.Combine(kopya, Path.GetRelativePath(kaynak, dosya));
            Directory.CreateDirectory(Path.GetDirectoryName(hedef)!);
            File.Copy(dosya, hedef, overwrite: true);
        }

        var enAna = Path.Combine(kopya, "en", "main.json");
        var metin = File.ReadAllText(enAna);
        foreach (var (arama, yazi) in degisim)
        {
            Assert.Contains(arama, metin);
            metin = metin.Replace(arama, yazi);
        }

        File.WriteAllText(enAna, metin);
        return new DilKopyasi(klasor, kopya);
    }

    /// <summary>
    /// S9 borcu: kalite yongasının balonundaki "tahmini kalite" satırı <c>/100</c>'ü koddan
    /// yazıyordu. Dil dosyasının kopyasında <c>main.unit.score-value</c> başka bir biçime
    /// çevrilir; yüklenen pencerenin balon ızgarasında o biçim okunur, <c>/100</c> okunmaz.
    /// </summary>
    [Fact]
    public void KaliteBalonundakiPuanDilDosyasindanGelir()
    {
        using var kopya = Kopya("s9-puan", new Dictionary<string, string>
        {
            ["\"main.unit.score-value\": \"{0}/100\""] = "\"main.unit.score-value\": \"{0} of 100 pts\""
        });

        List<string> satirlar;
        AppHost.Run(() => Strings.UseRoot(kopya.Locales));
        try
        {
            satirlar = Yuklu("en", window =>
            {
                var cip = Named<Button>(window, "Chip25");
                var balon = (StackPanel)ToolTip.GetTip(cip)!;
                return balon.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            });
        }
        finally
        {
            AppHost.Run(() => Strings.UseRoot(null));
        }

        Assert.True(satirlar.Any(s => s.EndsWith(" of 100 pts", StringComparison.Ordinal)), string.Join(" | ", satirlar));
        Assert.DoesNotContain(satirlar, s => s.EndsWith("/100", StringComparison.Ordinal));

        kopya.Tut = false;
    }

    /// <summary>
    /// O2: <c>main.unit.kbps-value</c>, <c>main.unit.fps-value</c> ve
    /// <c>main.plan.mode.crf-value</c> sablonlari 42 dilde bayt bayt ayni, bu yuzden dil
    /// kollari onlari ayirt edemiyor; sabiti koda geri yazan iki mutasyon dil kollarindan
    /// sag kaliyordu. Burada dil dosyasinin kopyasinda ucu de baska bir bicime cevrilir;
    /// yuklenen pencerenin hiz satiri, ses satiri ve plan izgarasi o bicimleri okur,
    /// Ingilizce sabitler hicbirinde gorunmez.
    /// </summary>
    [Fact]
    public void HizKareKbitVeCrfBirimleriDilDosyasindanGelir()
    {
        using var kopya = Kopya("s9-birim", new Dictionary<string, string>
        {
            ["\"main.unit.kbps-value\": \"{0} kbit/s\""] = "\"main.unit.kbps-value\": \"{0} kilobit/s\"",
            ["\"main.unit.fps-value\": \"{0} FPS\""] = "\"main.unit.fps-value\": \"{0} kare/s\"",
            ["\"main.plan.mode.crf-value\": \"CRF {0}\""] = "\"main.plan.mode.crf-value\": \"kalite carpani {0}\""
        });

        List<string> satirlar;
        AppHost.Run(() => Strings.UseRoot(kopya.Locales));
        try
        {
            satirlar = Yuklu("en", window =>
            {
                var okunan = new List<string>
                {
                    Named<TextBlock>(window, "TxtBitrate").Text ?? "",
                    Named<TextBlock>(window, "TxtAudio").Text ?? ""
                };
                okunan.AddRange(window.FindControl<Panel>("PlanFacts")!.Children.OfType<TextBlock>().Select(t => t.Text ?? ""));
                window.AdvModeIndex = 1;
                window.RecalculateForTest();
                window.UpdateLayout();
                okunan.AddRange(window.FindControl<Panel>("PlanFacts")!.Children.OfType<TextBlock>().Select(t => t.Text ?? ""));
                return okunan;
            });
        }
        finally
        {
            AppHost.Run(() => Strings.UseRoot(null));
        }

        var govde = string.Join(" | ", satirlar);
        var kanit = Path.Combine(GirdiKanit.Root, ".calisma", "s9-birim");
        Directory.CreateDirectory(kanit);
        File.WriteAllText(Path.Combine(kanit, "okunan.txt"), govde);

        Assert.EndsWith(" kilobit/s", satirlar[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(" kilobit/s", satirlar[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" kare/s", govde, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kalite carpani ", govde, StringComparison.OrdinalIgnoreCase);
        var kucuk = govde.ToLowerInvariant();
        Assert.Equal(5, kucuk.Split(" kilobit/s", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, kucuk.Split(" kare/s", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("kbit/s", govde, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fps", govde, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crf ", govde, StringComparison.OrdinalIgnoreCase);

        kopya.Tut = false;
    }

    /// <summary>
    /// <para>Denetim borcu: <c>main.unit.score-suffix</c>, <c>main.unit.score-value</c> ve
    /// <c>main.advanced.mode.crf</c> 42 dilde bayt bayt ayni ("/100", "{0}/100", "CRF").
    /// Tek degerli olduklari icin dil kollari onlari ayirt edemiyor; ucunu de koda sabit
    /// yazan bir mutasyon 369 testi yesil birakiyordu.</para>
    /// <para><b>Karar</b>: "bilincli tek degerli" ilan etmek yerine <b>sozlukten okundugu
    /// pimlenir</b>. Gerekce: ucu de ekranda kullaniciya gorunen metin ve ceviriye acik —
    /// "/100" yerine "0-100 puan", "CRF" yerine "kalite carpani" yazmak isteyen bir dil
    /// tamamen mesru. Tek degerli olmalari bugunku ceviri durumu, kalici bir kural degil;
    /// birim sabitini koda tasimak o kapiyi kapatirdi. Olcu, <c>Kopya</c> ile dil
    /// dosyasinin kopyasinda ucunu de baska bicime cevirir: sabiti koddan yazan bir mutasyon
    /// enjeksiyondan etkilenmez ve burada kirmizi verir.</para>
    /// </summary>
    [Fact]
    public void PuanBirimiVeKipAdiDilDosyasindanGelir()
    {
        using var kopya = Kopya("s9-puan-kip", new Dictionary<string, string>
        {
            ["\"main.unit.score-suffix\": \"/100\""] = "\"main.unit.score-suffix\": \" pts (max 100)\"",
            ["\"main.unit.score-value\": \"{0}/100\""] = "\"main.unit.score-value\": \"{0} pts (max 100)\"",
            ["\"main.advanced.mode.crf\": \"CRF\""] = "\"main.advanced.mode.crf\": \"Quality factor\""
        });

        (string birim, string not, string kip) okunan;
        AppHost.Run(() => Strings.UseRoot(kopya.Locales));
        try
        {
            okunan = Yuklu("en", window =>
            {
                var kalite = Named<TextBox>(window, "TxtQualityTarget");
                var izgara = ((Grid)kalite.Parent!).Children;
                var birim = (izgara[izgara.IndexOf(kalite) + 1] as TextBlock)?.Text ?? "";
                var kip = (window.FindControl<ComboBox>("CmbQualityMode")!.Items[0] as ComboBoxItem)?.Content?.ToString() ?? "";
                return (birim, not: Named<TextBlock>(window, "TxtEstimateNote").Text ?? "", kip);
            });
        }
        finally
        {
            AppHost.Run(() => Strings.UseRoot(null));
        }

        var kanit = Path.Combine(GirdiKanit.Root, ".calisma", "s9-puan-kip");
        Directory.CreateDirectory(kanit);
        File.WriteAllText(Path.Combine(kanit, "okunan.txt"), string.Join("\n", okunan.birim, okunan.not, okunan.kip));

        Assert.Equal(" pts (max 100)", okunan.birim.Replace('\u00A0', ' '), StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith(" pts (max 100)", okunan.not.Replace('\u00A0', ' '), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Quality factor", okunan.kip, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/100", okunan.birim + okunan.not, StringComparison.Ordinal);
        Assert.DoesNotContain("CRF", okunan.kip, StringComparison.OrdinalIgnoreCase);

        kopya.Tut = false;
    }

    [Fact]
    public void TuretmeSatiriHedefKutusunuIzler()
    {
        var satir = Read("en", window =>
        {
            Named<TextBox>(window, "TxtTarget").Text = "24";
            window.UpdateLayout();
            return Named<TextBlock>(window, "TxtChipDerivation").Text ?? "";
        });

        Assert.Contains("24", satir);
        Assert.DoesNotContain("16", satir);
    }

    /// <summary>
    /// <para>Kodek seridindeki etiket, o secim gercekten hangi kodlayiciya gidiyorsa onu
    /// soylemeli. "En kucuk" kolu <c>CodecPreference.MaxCompression</c>'a, o da
    /// <c>PlanCalculator.PreferredCodecFor</c> icinde <c>libsvtav1</c>'e gidiyor; etiket
    /// yillarca <c>H.265</c> yaziyordu. <c>libx265</c> yalnizca AV1 derlemede yoksa devreye
    /// giren <b>yedek</b> (<c>FallbackCodecFor</c>), tercih degil.</para>
    ///
    /// <para>Olcu sabit karsilastirmiyor: etiketi ekrandan, kodlayiciyi gercek esleme
    /// zincirinden (<c>CodecFromIndex</c> -> <c>PreferredCodecFor</c>) okuyup ikisini
    /// karsilastiriyor. <c>RbCodecSmallest</c>'in <c>loc:Text</c> anahtarini degistiren bir
    /// mutasyon etiketi baska bir ailenin adina cevirir ve olcu kirmizi doner.</para>
    ///
    /// <para><b>Dil, pencere kuruldaktan sonra sabitlenir.</b> <c>Read("en", ...)</c>'in
    /// basindaki <c>Strings.Use</c> yetmiyor: <c>MainWindow</c> yapicisi kayitli arayuz
    /// dilini geri yukleyip uzerine yaziyor, dolayisiyla olcu kendi basina kosunca Turkce
    /// etiketi okuyordu ve <c>Locales/en/main.json</c>'a yapilan mutasyon <b>yesil</b>
    /// kaliyordu. Geri cagirmanin govdesindeki <c>Strings.Use("en")</c> bunu kapatir;
    /// <c>loc:Text</c> bir <c>LocalizedText</c> bagi verdigi icin kurulmus icerik de
    /// tazelenir.</para>
    /// </summary>
    [Fact]
    public void KodekEtiketiSecilenKodlayiciyiSoyler()
    {
        var etiket = Read("en", window =>
        {
            Strings.Use("en");
            return new[]
            {
                Named<RadioButton>(window, "RbCodecAuto").Content?.ToString() ?? "",
                Named<RadioButton>(window, "RbCodecCompatible").Content?.ToString() ?? "",
                Named<RadioButton>(window, "RbCodecSmallest").Content?.ToString() ?? ""
            };
        });

        var aile = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["libx264"] = "H.264",
            ["libx265"] = "H.265",
            ["libsvtav1"] = "AV1"
        };

        var kanit = new List<string>();
        foreach (var kol in new[] { 1, 2 })
        {
            var tercih = MainWindow.CodecFromIndex(kol);
            var kodlayici = PlanCalculator.PreferredCodecFor(tercih);
            kanit.Add($"{kol}\t{tercih}\t{kodlayici}\t{etiket[kol]}");
        }

        var dizin = Path.Combine(GirdiKanit.Root, ".calisma", "kodek-etiketi");
        Directory.CreateDirectory(dizin);
        File.WriteAllText(Path.Combine(dizin, "okunan.txt"), string.Join(Environment.NewLine, kanit));

        Assert.Equal("libsvtav1", PlanCalculator.PreferredCodecFor(MainWindow.CodecFromIndex(2)));
        foreach (var kol in new[] { 1, 2 })
        {
            var kodlayici = PlanCalculator.PreferredCodecFor(MainWindow.CodecFromIndex(kol));
            Assert.True(aile.ContainsKey(kodlayici), $"aile tablosunda yok: {kodlayici}");
            Assert.StartsWith(aile[kodlayici], etiket[kol], StringComparison.Ordinal);
        }

        Assert.Equal(3, etiket.Distinct(StringComparer.Ordinal).Count());

        Kapat(dizin, "okunan.txt");
    }

    /// <summary>
    /// Son asertten sonra çağrılır: yeşil koşum kendi bıraktığını siler, kırmızı koşum
    /// kanıtını korur çünkü düşen asert buraya hiç gelmez. Klasör boşalınca o da gider.
    /// </summary>
    private static void Kapat(string klasor, params string[] adlar) => KanitKapanisi.Kapat(klasor, adlar);

    /// <summary>
    /// <para>WhatsApp sohbetteki videoyu kendi dusuk bit hizli kodlayicisiyla yeniden
    /// kodluyor; "belge olarak gonder" yolu bunu atliyor. Ipucu, WhatsApp hedefli cikti
    /// uretildiginde sonuc cumlesinin (<c>TxtResult</c>) arkasina ekleniyor.</para>
    ///
    /// <para>Olcu <c>HizKareKbitVeCrfBirimleriDilDosyasindanGelir</c>'in desenini kullanir:
    /// dil dosyasinin kopyasinda cumle baskasiyla degistirilir ve ekrana <b>o</b> cumlenin
    /// dustugu gorulur. Metin koda sabitlenirse eski cumle gelir ve olcu kirmizi doner.
    /// Negatif kol da tutuluyor: WhatsApp hedefi olmayan bir boyutta ipucu hic cikmaz,
    /// yoksa kosul dalini silen mutasyon sessizce gecerdi.</para>
    ///
    /// <para><c>Strings.Use("en")</c> pencere kuruldu<b>ktan sonra</b> bir kez daha
    /// cagriliyor: <c>MainWindow</c>'un kurucusu kayitli arayuz dilini geri yukluyor ve
    /// <c>Read</c>'in bastaki secimini eziyor. Bu olcu tek basina kosuldugunda Turkce
    /// cumleyi okuyup kirmizi donuyordu; sinif filtresi icinde onceki testler dili
    /// birakmis oldugu icin yesil gorunuyordu.</para>
    /// </summary>
    [Fact]
    public void WhatsAppBelgeIpucuDilDosyasindanGelir()
    {
        using var kopya = Kopya("s-whatsapp-belge", new Dictionary<string, string>
        {
            ["\"main.run.whatsapp-document\": \"Send this on WhatsApp as a document"] =
                "\"main.run.whatsapp-document\": \"Belge kolundan gonder"
        });

        (string Onalti, string Yuz) okunan;
        AppHost.Run(() => Strings.UseRoot(kopya.Locales));
        try
        {
            okunan = Read("en", window =>
            {
                Strings.Use("en");
                return (window.WhatsAppDocumentHintForTest(16),
                    window.WhatsAppDocumentHintForTest(100));
            });
        }
        finally
        {
            AppHost.Run(() => Strings.UseRoot(null));
        }

        var dizin = Path.Combine(GirdiKanit.Root, ".calisma", "s-whatsapp-belge");
        Directory.CreateDirectory(dizin);
        File.WriteAllText(Path.Combine(dizin, "okunan.txt"), $"16 MB: [{okunan.Onalti}]{Environment.NewLine}100 MB: [{okunan.Yuz}]");

        Assert.StartsWith(" Belge kolundan gonder", okunan.Onalti, StringComparison.Ordinal);
        Assert.DoesNotContain("Send this on WhatsApp", okunan.Onalti, StringComparison.Ordinal);
        Assert.Equal("", okunan.Yuz);

        kopya.Tut = false;
    }

    /// <summary>
    /// <para>Kucultme seridi artik <c>AV1</c> diyor ve AV1 eski cihazlarda cozulmez; secimin
    /// bedelini soyleyen bir yer yoktu. Donusum sekmesinin kodek ipucu bu bilgiyi 42 dilde
    /// zaten tasiyordu, <c>main.codec.tip</c> onun H.264 ve AV1 maddelerinden uretildi.</para>
    ///
    /// <para>Olcu ipucunun <b>varligini</b> degil kaynagini tutuyor: dil agacinin kopyasinda
    /// <c>en</c> degeri degistirilip <c>Strings.UseRoot</c> ile enjekte ediliyor, ekrandaki
    /// balon o degeri gostermezse kirmizi doner. Metin koda sabitlenirse olcu duser.</para>
    /// </summary>
    [Fact]
    public void KodekSeridiIpucuDilDosyasindanGelir()
    {
        using var kopya = Kopya("s-kodek-ipucu", new Dictionary<string, string>
        {
            ["\"main.codec.tip\": \"• H.264"] = "\"main.codec.tip\": \"• Balon kolundan H.264"
        });

        string okunan;
        AppHost.Run(() => Strings.UseRoot(kopya.Locales));
        try
        {
            okunan = Read("en", window =>
            {
                Strings.Use("en");
                var satir = Named<Grid>(window, "CodecChoiceRow");
                var balon = ToolTip.GetTip(satir) as StackPanel;
                Assert.NotNull(balon);
                var metin = balon!.Children.OfType<TextBlock>().First();
                return string.Concat(metin.Inlines?.OfType<Run>().Select(kosu => kosu.Text) ?? Array.Empty<string?>());
            });
        }
        finally
        {
            AppHost.Run(() => Strings.UseRoot(null));
        }

        var dizin = Path.Combine(GirdiKanit.Root, ".calisma", "s-kodek-ipucu");
        Directory.CreateDirectory(dizin);
        File.WriteAllText(Path.Combine(dizin, "okunan.txt"), okunan);

        Assert.Contains("Balon kolundan H.264", okunan, StringComparison.Ordinal);
        Assert.Contains("AV1", okunan, StringComparison.Ordinal);

        kopya.Tut = false;
    }
}

/// <summary>
/// T192 tur 2 K8 — baslik kuralinin kapsami.
///
/// <para>Tur 1'de <c>ReadsAsProse</c> genisletildi ve rapor dort satirlik bir tablo
/// verdi. Yan etkinin <b>olcusu</b> yoktu: dil dosyasindaki her metin ayni geciten
/// geciyor, dolayisiyla degisen satir sayisi dortten cok fazla. Bu olcu butun dil
/// dosyalarini gercek <c>LanguageCatalog.Title</c> uzerinden gezer, kol degistiren her
/// anahtari sayar ve doker.</para>
///
/// <para><b>Kol degistiren</b> = <c>fd6fe0c1</c>'in kuralina (yalniz cumle isareti) gore
/// baslik, bugunku kurala gore govde. Eski kural burada tek yerde, alti satirda
/// tekrarlanir — kurulan mekanizma degil, <b>karsilastirma tabani</b> odur.
/// Ciktinin gercekten degistigi ayrica olculdu: ayni dokum bir de
/// <c>origin/main</c>'in <c>LanguageCatalog.cs</c>'siyle alinip <c>diff</c>'lendi,
/// ham cikti <c>.calisma/T192/k8-fark.txt</c>.</para>
///
/// <para>Sayi <b>pimlidir</b>. Dil dosyasina metin eklendiginde ya da bir metin
/// degistiginde bu pim kirilir; kirilinca yapilacak sey susturmak degil, yeni sayiyi
/// <c>docs/olcumler/kare-kusurlari.md</c>'ye yazmaktir.</para>
/// </summary>
public sealed class BaslikKapsamiTests
{
    private readonly ITestOutputHelper _cikti;

    public BaslikKapsamiTests(ITestOutputHelper cikti) => _cikti = cikti;

    /// <summary><c>fd6fe0c1</c>'in kurali: yalniz cumle isareti.</summary>
    private static bool EskiKuralaGoreGovde(string metin)
    {
        for (var index = 0; index < metin.Length; index++)
        {
            if (metin[index] is not ('.' or ';' or '!' or '?')) continue;
            if (index + 1 == metin.Length || char.IsWhiteSpace(metin[index + 1])) return true;
        }

        return false;
    }

    /// <summary>
    /// Sozcugun bas tarafi <c>Names</c>'de bildirilmis bir ad mi? Sinir kurali
    /// <c>LanguageCatalog.KnownSpelling</c> ile ayni: bastaki harf disi imler atlanir,
    /// once bir rakam gelirse sozcuk olcudur ve ada bakilmaz, ad yalniz <b>bastaki</b>
    /// harf/rakam dizisinde aranir. Boylece <c>tools/ffmpeg</c> bir yoldur, ad degil.
    /// </summary>
    private static (string Govde, string? Dogru) AdiCagriliyorsa(string sozcuk)
    {
        var offset = 0;
        while (offset < sozcuk.Length && !char.IsLetter(sozcuk[offset]))
        {
            if (char.IsDigit(sozcuk[offset])) return (sozcuk, null);
            offset++;
        }

        if (offset == sozcuk.Length) return (sozcuk, null);

        var govde = sozcuk[offset..];
        var token = new string(govde.TakeWhile(char.IsLetterOrDigit).ToArray());
        var bare = new string(govde.TakeWhile(char.IsLetter).ToArray());
        if (LanguageCatalog.Names.TryGetValue(token, out var ad)) return (govde, ad);
        if (LanguageCatalog.Names.TryGetValue(bare, out var isim)) return (govde, isim);
        return (govde, null);
    }

    /// <summary>
    /// <para>Sinif <c>BaslikKapsamiTests</c>, dosya <c>BiciminTests.cs</c> (dosya uc sinif tasiyor:
    /// <c>BiciminTests</c>, <c>KareYerlesimTests</c>, <c>BaslikKapsamiTests</c>). Filtre sinif adiyla
    /// eslesir, dosya adiyla eslesmez: <c>--filter "FullyQualifiedName~VidShrink.Tests.BiciminTests"</c>
    /// yalniz 19 test kosturur (25 ms) ve bu sayimlara hic girmez. Bu pinleri kosturan filtre
    /// <c>--filter "FullyQualifiedName~VidShrink.Tests.BaslikKapsamiTests"</c>.</para>
    /// <para><b>18 Eylul 2026 pimleri: toplam 1628, en 193, tr 66.</b> On ayar kutuphanesi
    /// turunun dil basina on dort anahtari kol degistiren kumeyi de buyuttu: 1628 - 1598 = 30,
    /// en 189 + 4, tr 64 + 2. Ayni turda <c>main.preset.add.tip</c> kirk dilde kisaltildi
    /// (ipucu tavani olcusu iki satir tasiyordu); kisaltma kol degistiren kumeyi degistirmedi,
    /// sayilar kisaltmadan once ve sonra ayni cikti.</para>
    /// <para><b>19 Eylul 2026 pimleri: toplam 1649, en 195, tr 67.</b> Paylasim hatalarinin
    /// dili (<c>5ba0e973</c>) dil basina otuz yedi <c>share.*</c> anahtari ekledi; bunlarin
    /// kol degistiren kismi 1649 - 1628 = 21, en 193 + 2, tr 66 + 1. Bit hizi biriminin
    /// birlestirilmesi ayni turda <c>main.unit.k-value</c>'yu dusurdu, o anahtar govde degil
    /// olcu ("{0}k") oldugu icin kol degistiren kumeye hic girmiyordu: sayilar oynamadi.</para>
    /// <para><b>19 Eylul 2026, ikinci yenileme: toplam 1666, en 196, tr 68.</b> Olcek carpani
    /// (<c>b7b485c4</c>) ve cikti adi deseni (<c>1546c9e3</c>) turlari birlikte dil basina on
    /// anahtar ekledi; kol degistiren kismi 1666 - 1649 = 17, en 195 + 1, tr 67 + 1. Iki tur
    /// da pimi yenilemedi, CI iki kosum kirmizi kaldi.</para>
    ///
    /// <para><b>19 Eylul 2026, ucuncu yenileme: toplam 1677, en 198, tr 68.</b> Tani gunlugu
    /// (E7) her dile bes <c>settings.log.*</c> anahtari ekledi; kol degistiren kismi 1677 - 1666 = 11,
    /// en 196 + 2. Turkce tarafta yeni anahtarlarin hicbiri kol degistirmiyor, tr 68'de kaliyor.</para>
    ///
    /// <para><b>19 Eylul 2026, dorduncu yenileme: toplam 1682, en 198, tr 68.</b> Suzgec yuzeyi
    /// (E8) her dile uc <c>main.advanced.filters.*</c> anahtari ekledi; 126 anahtarin yalniz
    /// besi kol degistiriyor (es/pt <c>label</c>, hu/ro <c>bad</c>, ro <c>label</c>). Ingilizce
    /// "Picture filters" ve Turkce "Gorunt&#252; suzgecleri" zaten kurala uygun yazildi, o yuzden
    /// en ve tr sayilari oynamadi.</para>
    ///
    /// <para><b>19 Eylul 2026, besinci yenileme: toplam 1690, en 200, tr 69.</b> Paylasimin
    /// yeniden deneme yuzu uc <c>settings.share.retry*</c>, onizleme rozeti bir
    /// <c>main.preview.temsili</c> anahtari ekledi; 172 anahtarin sekizi kol degistiriyor
    /// (en <c>retry-in</c> ve <c>retry-with</c>, tr <c>retry-with</c>, de/sw <c>retry-in</c>,
    /// lt <c>retry-with</c>, pt <c>retry</c> ve <c>retry-in</c>). <c>main.preview.temsili</c>
    /// hicbir dilde kol degistirmiyor.</para>
    ///
    /// <para><b>19 Eylul 2026, altinci yenileme: toplam 1727, en 205, tr 72.</b> B1a ses kodegi
    /// kolu dort, tasma dugmelerinin ekran okuyucu adi iki anahtar ekledi; 258 anahtarin 37si
    /// kol degistiriyor (<c>dolby-not-in-container</c> 11, <c>dolby-below-channel-floor</c> 9,
    /// <c>retry.trim.name</c> 7, <c>audio-channels.source</c> 6, <c>audio-codec.label</c> 3,
    /// <c>retry.accept.name</c> 1). en besi, tr ucu.</para>
    ///
    /// <para><b>22 Eylul 2026, yedinci yenileme: toplam 1726, en 205, tr 71.</b> Yerlesim
    /// denetcisi (<c>YerlesimDenetimiTests</c>) tr ve en'de 32 basligin (en 22, tr 10) kucuk
    /// harfle baslayan sozcuklerini buyuttu. Yeni anahtar yok; tr'de buyutulen dort anahtardan biri
    /// (<c>player.advanced.*</c> ucu ya da <c>settings-tab.opensubtitles.account</c>) artik duz
    /// yazi okunmuyor ve koldan cikti. en sayisi oynamadi.</para>
    ///
    /// <para><b>22 Eylul 2026, sekizinci yenileme: toplam 1880, en 222, tr 75.</b> Pin main'de
    /// C1-2 kuyruk duzenlemesinden beri kirmiziydi (a26a3b3a CI: 1784). 154 fazlanin 53'u C1-5
    /// suzgec ve C1-6 iz anahtarlari (en 4, tr 2; en cok <c>subtitles.burn</c> 7, iki
    /// <c>filters.rotate.*</c> 5'er), 101'i C1-1..C1-4 kuyruk ve on ayar turunun anahtarlari.
    /// Sonuc durumu denetiminde en'de dokuz <c>retry.*</c>/<c>action.*</c> dugmesi Title Case'e
    /// alindi; islev sozcugu tasidiklari icin kolda kaldilar, sayi oynamadi.</para>
    ///
    /// <para><b>23 Eylul 2026, dokuzuncu yenileme: toplam 1918, en 225, tr 78.</b> B3 (VP9
    /// kucultme) ve B4 (HDR dinamik veri) dil basina uc gerekce anahtari ekledi:
    /// <c>main.reason.hdr-dynamic-dropped</c>, <c>main.reason.stream.webm-audio-opus</c>,
    /// <c>main.reason.stream.webm-stream-dropped</c>. Ucu de noktalama tasimiyor (eski kural
    /// govde sayardi) ama govde sozcugu tasiyor (en <c>the</c>/<c>or</c>/<c>so</c>, tr
    /// <c>ve</c>/<c>ya</c>/<c>da</c>), yani yeni kural ile kol degistiriyor: en ve tr'de ucu de
    /// (1880 + 38 = 1918, en 222 + 3 = 225, tr 75 + 3 = 78). Kalan 38 - 3 - 3 = 32 diger
    /// dillerde, aynı uc anahtarin ceviri sozcuklerine bagli.</para>
    /// <para>2026-09-23: <c>main.reason.vp9-crf-unmeasured</c> (VP9'da CRF yerine iki gecis)
    /// 12 dilde kol degistiriyor, en dahil tr haric: 1918 + 12 = 1930, en 225 + 1 = 226.</para>
    /// <para>2026-09-23, ikinci duzeltme: <c>main.reason.vp9-crf-unmeasured</c> metni CRF
    /// kalite olceginin artik olculdugunu, olculmeyenin CRF-boyut cevirisi oldugunu anlatacak
    /// sekilde yeniden yazildi. Islev sozcugu eslesmesi rastlantisal: cs'de yeni metindeki
    /// "do" (Cekce "-e"), et ve fi'de "on" (Estonca/Fince "-dir") ingilizce govde listesiyle
    /// carpisip kola giriyor (+1'er), de'de eski metindeki "in" (Almanca "-de") kalkiyor ve
    /// govdeden cikiyor (-1). Toplam 1930 + 1 + 1 + 1 - 1 = 1932; en ve tr metni ayni
    /// govde/kol siniri icinde kaldigi icin 226 ve 78 degismedi.</para>
    /// <para>2026-09-23, HDR10+ koprusu: <c>main.reason.hdr10plus-cut</c> 18 dilde,
    /// <c>main.reason.hdr10plus-routed-x265</c> 14 dilde kol degistiriyor, ikisi de en ve tr
    /// dahil: 1932 + 32 = 1964, en 226 + 2 = 228, tr 78 + 2 = 80. Oto-kirpmanin iki anahtari
    /// (<c>main.advanced.filters.autocrop</c>, <c>main.reason.auto-crop</c>) hicbir dilde kola girmiyor.</para>
    /// <para>2026-09-23, tek gecis etiketi: donanim kodlayicisinin tek gecisini soyleyen yedi
    /// anahtardan dordu kola giriyor: <c>main.estimate.mode.enforced-single-pass</c> 8 dilde,
    /// <c>main.reason.budget-below-ceiling-single-pass</c> 10, <c>-fill-band-center-single-pass</c> 10,
    /// <c>-fill-band-narrow-single-pass</c> 15. 1964 + 8 + 10 + 10 + 15 = 2007; en dordunde de
    /// (228 + 4 = 232), tr yalniz band merkezinde (80 + 1 = 81). Yeniden yazilan
    /// <c>main.output.estimated-output.tip</c> ne once ne sonra kolda.</para>
    /// <para>2026-09-23, iki nokta sonrasi cumle (15fa18ac, <c>CarriesClauseAfterColon</c>): iki noktadan sonra
    /// harf tasiyan en az uc sozcuk varsa metin cumle sayilir. 519 anahtar-dil cifti kola girdi, hepsi iki noktadan
    /// sonra cumle ("Dropped: codec not supported", "Duraklatıldı: şimdiki dosya biter, sıradaki bekler"); en cok
    /// <c>main.preset.import.summary</c> 41, <c>main.reason.auto-crop</c> 40. 2007 + 519 = 2526; en 232 + 5 = 237,
    /// tr 81 + 8 = 89. Kurali kaldirmak sayimi 2007/232/81'e geri indiriyor (olculdu).</para>
    /// <para>2026-09-23, Media Foundation: <c>main.reason.no-quality-scale</c> 15 dilde kola giriyor,
    /// en dahil tr haric: iki nokta kuraliyla birlikte 2526 + 15 = 2541, en 237 + 1 = 238, tr 89.</para>
    /// <para>2026-09-24, yalan yok: <c>main.output.remaining</c> en'de "Remaining" iken "Left in this attempt" oldu ve
    /// kola giriyor (tr "Kalan" kaldi): 2541 + 1 = 2542, en 239, tr 89.</para>
    /// <para>2026-09-24, soylenen ile yapilan: <c>main.reason.stream.vp9-fell-back-to-mp4</c> 18 dilde kola
    /// giriyor ("VP9 could not be used, ..."), en dahil tr haric; yeni iki anahtar kolun disinda.
    /// ustune yalan yok CLI satiri: 2542 + 18 = 2560, en 239 + 1 = 240, tr 89.</para>
    /// <para>2026-09-24, kaydedici durustlugu: yedi yeni kaydedici anahtarindan dordu kola giriyor:
    /// <c>recorder.output.done-partial</c> 34 dilde, <c>recorder.output.done-failed</c> 33,
    /// <c>recorder.replay.saved-mkv</c> 7, <c>recorder.discarded-kept</c> 5. 2541 + 79 = 2620; en dordunde de
    /// (238 + 4 = 242), tr yalniz <c>recorder.discarded-kept</c>'te (89 + 1 = 90); ustteki iki satirla birlikte 2560 + 79 = 2639, en 244, tr 90.</para>
    /// <para>2026-09-24, yalan yok kalan uclar: uc yeni anahtardan yalniz <c>main.reason.stream.vp9-fell-back</c>
    /// kola giriyor, 12 dilde (cs en fr hr hu lt nl pl pt ro sk sr), en dahil tr haric;
    /// <c>-image-subtitle-dropped-by-container</c> ve <c>recorder.output.partial-broken-mkv</c> kolun disinda,
    /// yeniden yazilan <c>main.run.over-ceiling</c> ne once ne sonra kolda: 2639 + 12 = 2651, en 245, tr 90.</para>
    /// <para>2026-09-25, bolge duzenleyici: dort yeni <c>recorder.region.*</c> anahtarindan yalniz <c>recorder.region.title</c>
    /// kola giriyor, 4 dilde (es fr pt ro: "Editor de región"), en ve tr haric: 2651 + 4 = 2655, en 245, tr 90.</para>
    /// <para>2026-09-25, libmpv yukleme hatasi: <c>main.player.engine.loadfailed</c> 5 dilde kola giriyor (bn hi ja th ur),
    /// en ve tr haric: 2655 + 5 = 2660, en 245, tr 90.</para>
    /// <para>2026-09-25, Indir ve Yukle: <c>main.action.downloadinstall</c> 5 dilde kola giriyor (cs en sk sl tr):
    /// 2660 + 5 = 2665, en 246, tr 91.</para>
    /// <para>2026-09-26, paylasim QR'i: <c>main.share.qr</c> 7 dilde kola giriyor (en fr hu nb nl pt ro), tr haric;
    /// <c>main.share.qr-hint</c> kolun disinda: 2665 + 7 = 2672, en 247, tr 91.</para>
    /// <para>2026-09-26, bolge kaydi kare tavani: tr <c>recorder.auto.note.stepped</c> "kare hızı düşürüldü" oldu, uc kelimeyle
    /// duzyazi sayilmiyor ve koldan cikiyor: 2672 - 1 = 2671, en 247, tr 90.</para>
    /// <para>2026-09-26, kapanista guncelleme: <c>main.update.ready-on-exit</c> 2 dilde kola giriyor (en hu); yeniden yazilan
    /// <c>settings.update.auto-effect</c> 3 dilde koldan cikiyor (bn hi ur; eski metin kolda, yenisi degil, eski Locales ile kosularak olculdu):
    /// 2672 + 2 - 3 = 2671, en 248, tr 91; bolge kaydi birlesmesinin tr eksigiyle 2670, en 248, tr 90.</para>
    /// <para>2026-09-27, oynatici sag menusu: <c>player.menu.loop</c> 5 dilde kola giriyor (cs en sk sl tr), <c>player.menu.reveal</c>
    /// yalniz pt'de; <c>player.menu.view</c> ve <c>player.menu.playback</c> kolun disinda: 2670 + 6 = 2676, en 249, tr 91.</para>
    /// <para>2026-09-27, duzenleyici D3: on dort <c>editor.*</c> anahtarindan 10 kol 7 dilde (en: mark-in, zoom-in; es: timeline;
    /// fr: mark-out, playhead; pt: playhead, timeline; ro: playhead; sv ve vi: zoom-in), <c>main.tab.editor</c> kolun disinda:
    /// 2676 + 10 = 2686, en 251, tr 91.</para>
    /// <para>2026-09-28, duzenleyici D5: yedi yeni <c>editor.*</c> anahtarindan (kisayol adlari, liste basligi ve ipucu)
    /// 17 kol: 2686 + 17 = 2703, en 254, tr 91.</para>
    /// <para>2026-09-28, duzenleyici D4: on <c>editor.export.*</c> anahtarindan 7 kol 6 dilde (es pt sw: mode; ro: mode, failed;
    /// fr: failed; lt: fast): 2686 + 7 = 2693, en 251, tr 91.</para>
    /// <para>2026-09-28, D4 ile D5 birlikte: 2686 + 17 + 7 = 2710.</para>
    /// <para>2026-09-28, duzenle/kaydet/paylas: <c>editor.export</c> ("Export…", kolun disinda) yerine <c>editor.save</c> ve
    /// <c>editor.save-as</c>; yalniz en <c>editor.save-as</c> ("Save As…") kola giriyor, <c>player.menu.edit</c> ve
    /// <c>recorder.output.to-editor</c> kolun disinda: 2710 + 1 = 2711, en 255, tr 91.</para>
    /// <para>2026-09-28, bolge dali ile birlikte: recorder.region.hide ve recorder.region.stop-close, 2711 + 5 = 2716, en 256, tr 92.</para>
    /// <para>2026-09-29, arayuz turu: yeni <c>recorder.advanced.encoder-title</c> fr, pt ve sw cevirilerinde islev sozcugu tasiyor (de, do, ya): 2716 + 3 = 2719, en 256, tr 92.</para>
    /// <para>2026-09-29, kaydedici sihirbazi: 21 yeni <c>recorder.wizard.*</c> ve <c>recorder.replay.seconds</c> anahtari: 2719 + 25 = 2744, en 256 + 5 = 261, tr 92 + 3 = 95.</para>
    /// <para>2026-09-30, kaydedici EN tasmasi: <c>recorder.advanced.audio-mixed</c> "Mix into one track" yerine "Single track", islev sozcugu dustu ve koldan cikiyor: 2744 - 1 = 2743, en 260, tr 95.</para>
    /// <para>2026-09-30, duzenleyici Premiere uyumu (dalga 1, 2A, 2B/3B): on sekiz yeni <c>editor.*</c> anahtari (araclar, monitor, zaman kodu, sure, yapisma, on kisayol adi) 14 dilde 66 kol: 2743 + 66 = 2809, en 260 + 5 = 265, tr 95 + 2 = 97 (<c>editor.key.snap</c>, <c>editor.key.trim-head</c>).</para>
    /// <para>2026-09-30, oynatma listesi oge menusu: bes yeni <c>player.list.*</c> anahtari (sirala, sonra oynat, kaldir, yolu kopyala, oynat) 13 kol: 2809 + 13 = 2822, en 265 + 2 = 267, tr 97.</para>
    /// <para>2026-09-30, oynatici alt seridi: uc yeni anahtar (sure ipucu, hiz A/B menusu, hiz A/B ipucu) 55 kol: 2822 + 55 = 2877, en 267 + 2 = 269, tr 97 + 2 = 99.</para>
    /// <para>2026-09-30, oynatici kurtarma: <c>main.player.recoveryfailed</c> 42 dilde cumle, koldan cikmiyor (olculen SAYIM toplam 2877, en 269, tr 99): 2877 + 0 = 2877, en 269, tr 99.</para>
    /// <para>2026-09-30, kisayol atama: yedi yeni anahtardan yalniz dinleme metni (shortcuts.listening, cumle yazimli ipucu) en ve tr'de kola girdi: 2877 + 2 = 2879, en 269 + 1 = 270, tr 99 + 1 = 100. Varsayilanlara Don dugmesi Title Case, oteki bes anahtar kola girmedi.</para>
    /// <para>2026-09-30, kurtarma ile kisayol atama birlikte: 2877 + 0 + 2 = 2879, en 270, tr 100.</para>
    /// <para>2026-09-30, kayit bitince eylemi: dort <c>recorder.finish.*</c> anahtari 27 kol (copy 7, shrink-copy 9, title 5, upload-copy 6); sag tik hedef yongalari ve ddagrab dususu kola girmedi: 2879 + 27 = 2906, en 270 + 4 = 274, tr 100 + 2 = 102.</para>
    /// <para>2026-09-30, metin katmani (dalga 1): alti yeni anahtardan (<c>editor.text.add</c>, <c>-content</c>, <c>-size</c>, <c>-color</c>, <c>-panel</c>, <c>editor.export.text-full</c>) yalniz pt <c>editor.text.panel</c> ("Propriedades do texto") kola giriyor; gerekce notu noktali cumle, tek sozcukler duz yazi okunmuyor: 2906 + 1 = 2907, en 274, tr 102.</para>
    /// <para>2026-10-01, klip ozellikleri: on iki yeni anahtardan (on bir <c>editor.clip.*</c>, <c>editor.export.effects-full</c>) 10 kol 5 dilde (en: fade-in; fr: rotate; it: fade-in, fade-out, flip-h, flip-v; pt: fade-in, fade-out, panel; sv: fade-in), tr haric: 2907 + 10 = 2917, en 274 + 1 = 275, tr 102.</para>
    /// <para>2026-10-01, sessizlik kesme: on bes yeni <c>editor.silence.*</c> anahtarindan 13 kol 12 dilde (panel: cs, en, es, pt, ro, sk, sl, tr; failed: fr, hu, ro; scanning: it; padding: sw): 2917 + 13 = 2930, en 275 + 1 = 276, tr 102 + 1 = 103.</para>
    /// <para>2026-10-02, hiz ibresi: <c>main.player.speedab.tip</c> "Speed A {0} · B {1}" yerine "Fast {0} · Slow {1}" oldu, 43 dilde koldan cikiyor; yeni hiz, OSD ve medya adi anahtarlari kola girmiyor (25405768 tabaniyla KOL dokumu karsilastirilarak olculdu): 2930 - 43 = 2887, en 276 - 1 = 275, tr 103 - 1 = 102.</para>
    /// <para>2026-10-05, oynatici bolum atlama: iki yeni anahtardan (<c>main.player.menu.chapternext</c>, <c>main.player.menu.chapterprev</c>) 5 kol 3 dilde (en ve pl ikisi, hu yalniz chapternext), yanlarindaki yer imi anahtarlariyla ayni dagilim, tr haric: 2887 + 5 = 2892, en 275 + 2 = 277, tr 102.</para>
    /// <para>2026-10-05, kuyruk izleme klasoru: bes yeni <c>main.shrink-job.watch*</c> anahtarindan 16 kol 11 dilde (hint: cs, de, en, es, hu, it, nl, pl, sk, sl; lost: en, es, hr, hu, nl, pt): 2892 + 16 = 2908, en 277 + 2 = 279, tr 102.</para>
    /// <para>2026-10-05, kaydedici kisayol atama: sekiz yeni <c>recorder.hotkeys.*</c> anahtarindan 12 kol 8 dilde (discard: en, pt; frame: en, nl, pt, tr; replay-save: en, es, fr, pt, ro, sw); tus adi yer tutucuya donen <c>recorder.replay.running</c> th'de koldan cikiyor (HEAD dil dosyalariyla KOL dokumu karsilastirilarak olculdu): 2887 + 12 - 1 = 2898, en 275 + 3 = 278, tr 102 + 1 = 103.</para>
    /// <para>2026-10-05, bolum atlama, izleme klasoru ve kisayol atama birlikte: 2887 + 5 + 16 + 11 = 2919, en 275 + 2 + 2 + 3 = 282, tr 103.</para>
    /// <para>2026-10-05, duzenleyici proje kaydi: yedi yeni anahtardan ikisi (<c>editor.project.failed</c>, <c>editor.project.unreadable</c>) 9 kol 6 dilde (en, hu, ro ikisi; es, fr, pt yalniz unreadable), KOL dokumunden sayildi, tr girmiyor: 2919 + 9 = 2928, en 282 + 2 = 284, tr 103.</para>
    /// <para>2026-10-05, kareyi panoya kopyalama ve JPG ekran goruntusu: dort yeni <c>player.view.screenshot-*</c> anahtarindan 21 kol 8 dilde (copy ve copied: cs, de, en, hu, pl, pt, sk; copy-failed: en, hu, ro; jpg: en, es, pt, ro), tr haric: 2919 + 21 = 2940, en 282 + 4 = 286, tr 103.</para>
    /// <para>2026-10-05, proje kaydi ve kare panosu birlikte: 2919 + 9 + 21 = 2949, en 282 + 2 + 4 = 288.</para>
    /// <para>2026-10-05, ikincil altyazi: tek yeni anahtar <c>player.subtitle.secondary</c> yalniz sw'de kola giriyor ("Manukuu ya pili", <c>ya</c> islev sozcugu), SAYIM dokumunden okundu: 2928 + 1 = 2929, en 284, tr 103.</para>
    /// <para>2026-10-05, proje kaydi, kare panosu ve ikincil altyazi birlikte: 2919 + 9 + 21 + 1 = 2950, en 288, tr 103.</para>
    /// <para>2026-10-05, oynatici liste kaydi: dort yeni <c>player.list.*</c> anahtarindan (<c>save</c>, <c>saved</c>, <c>save-failed</c>, <c>files</c>) 22 kol 7 dilde (es, fr, pt, ro, sw dordu de; en ve hu yalniz save-failed), KOL dokumunden sayildi, tr girmiyor. Kare panosuyla birlikte: 2949 + 22 = 2971, en 288 + 1 = 289, tr 103.</para>
    /// <para>2026-10-05, ikincil altyazi ve liste kaydi birlikte: 2950 + 22 = 2972, en 288 + 1 = 289.</para>
    /// <para>2026-10-05, is bitince haber: alti yeni anahtardan (<c>main.notify.*</c> dort, <c>settings-tab.notify-done.*</c> iki) iki kol, biri en'de, SAYIM dokumunden okundu, tr girmiyor. Liste kaydiyla birlikte: 2972 + 2 = 2974, en 289 + 1 = 290, tr 103.</para>
    /// <para>2026-10-05, oynatici bilgi panelinde HDR alani ve canli istatistik: dokuz yeni <c>player.info.*</c> anahtarindan dordu 14 kol 8 dilde (decoderdropped: en, hu, nl, ro; livebitrate: ms, pt, ro, sw; livefps: es, ms, pt, ro, sw; range: sw), KOL dokumunden sayildi, tr girmiyor: 2949 + 14 = 2963, en 288 + 1 = 289, tr 103.</para>
    /// <para>2026-10-05, liste kaydi ve bilgi paneli birlikte: 2972 + 14 = 2986, en 289 + 1 = 290.</para>
    /// <para>2026-10-05, liste kaydi, bilgi paneli ve is bitince haber birlikte: 2986 + 2 = 2988, en 290 + 1 = 291.</para>
    /// <para>2026-10-05, meta veriyi sil kutusu: iki yeni anahtardan yalniz <c>main.advanced.drop-metadata.tip</c> 5 dilde kola giriyor (bn, hi, ja, th, ur), KOL dokumunden sayildi, en ve tr girmiyor: 2988 + 5 = 2993, en 291, tr 103.</para>
    /// <para>2026-10-06, parcalari ayri dosyalara yazma: uc yeni <c>editor.export.*</c> anahtarindan (<c>separate</c>, <c>segments-done</c>, <c>segment-failed</c>) 15 kol 10 dilde (separate: cs, en, pl, sk; segments-done: cs, en, it, pl, sk; segment-failed: en, fr, hr, nl, ro, sl), KOL dokumunden sayildi, tr girmiyor. Meta veri kutusuyla birlikte: 2993 + 15 = 3008, en 291 + 3 = 294, tr 103.</para>
    /// <para>2026-10-06, sahne degisimlerinden bolme: uc yeni <c>editor.scene.*</c> anahtarindan yalniz <c>editor.scene.split</c> 7 dilde kola giriyor (de, en, es, fr, pt, ro, sw), KOL dokumunden sayildi, tr girmiyor: Parcalari ayri yazmayla birlikte: 3008 + 7 = 3015, en 294 + 1 = 295, tr 103.</para>
    /// <para>2026-10-06, kaydedici ses seviyesi gostergesi: uc yeni <c>recorder.audio.level.*</c> anahtarindan ikisi 4 kol 3 dilde (microphone: pt; system: pt, ro, sw), KOL dokumunden sayildi, en ve tr girmiyor. Sahne bolmeyle birlikte: 3015 + 4 = 3019, en 295, tr 103.</para>
    /// <para>2026-10-06, kayitta bolum isareti: bes yeni anahtardan (<c>recorder.hotkeys.chapter</c>, <c>recorder.chapter.marked</c>, <c>.title</c>, <c>.unsupported</c>, <c>.failed</c>) yalniz <c>recorder.hotkeys.chapter</c> 2 dilde kola giriyor (en, sw), KOL dokumunden sayildi, tr girmiyor. Ses gostergesiyle birlikte: 3019 + 2 = 3021, en 295 + 1 = 296, tr 103.</para>
    /// <para>2026-10-07, kapak notu: tek yeni anahtar <c>main.reason.stream.cover-dropped</c> 11 dilde kola giriyor (en, fr, hr, hu, nl, pl, pt, ro, sl, sw, tr), KOL dokumunden sayildi: 3021 + 11 = 3032, en 296 + 1 = 297, tr 103 + 1 = 104.</para>
    /// <para>2026-10-07, arayuzde yabanci dil altyazisi taramasi: sekiz yeni <c>main.subtitles.scan*</c> anahtarindan 12 kol 8 dilde (dugme <c>scan</c>: es, nb, pl, pt, sw; <c>flagged</c> ve <c>sparse</c>: bn, hi, ur; <c>sparse</c>: th), KOL dokumunden sayildi, en ve tr girmiyor: 3032 + 12 = 3044, en 297, tr 104.</para>
    /// <para>2026-10-07, WebP bit hizi notu: tek yeni anahtar <c>main.convert.note.webp-bitrate-ignored</c> 3 dilde kola giriyor (bn, hi, ur), KOL dokumunden sayildi, en ve tr girmiyor: 3044 + 3 = 3047, en 297, tr 104.</para>
    /// <para>2026-10-07, Donustur ve duzenleyicide meta veriyi sil, kare hizi kipi gerekcesi: uc yeni anahtardan 20 kol 15 dilde (<c>main.convert.drop-metadata.tip</c>: bn, hi, ja, th, ur; <c>main.reason.frame-rate-constant</c>: fr, ms, pt, ro, sw; <c>main.reason.frame-rate-peak</c>: da, en, es, hu, it, ms, pl, pt, ro, sw), KOL dokumunden sayildi, tr girmiyor: 3047 + 20 = 3067, en 297 + 1 = 298, tr 104.</para>
    /// <para>2026-10-10, oynaticida adres: uc yeni anahtardan (<c>player.tools.paste</c>, <c>player.tools.paste-none</c>, <c>player.address.unsupported</c>) 13 kol 10 dilde (paste: sv; paste-none: cs, de, en, hu, pt; unsupported: da, en, it, nb, nl, pt, ro), KOL dokumunden sayildi, tr girmiyor: 3067 + 13 = 3080, en 298 + 2 = 300, tr 104.</para>
    /// <para>2026-10-10, iki dugmeli guncelleme paneli (teknesyum-ui 0.35.0): <c>main.action.download</c> ve <c>main.action.downloadinstall</c> kalkti, yerlerine <c>main.action.update</c> ve <c>main.action.later</c> geldi. Kalkan <c>downloadinstall</c> 5 dilde koldaydi (cs en sk sl tr); yeni iki anahtar hicbir dilde kola girmiyor, KOL dokumunden sayildi: 3067 - 5 = 3062, en 298 - 1 = 297, tr 104 - 1 = 103.</para>
    /// <para>2026-10-10, K6 eszamanli kodlama: uc yeni anahtardan (<c>main.shrink-job.running</c>, <c>settings-tab.parallel-jobs.label</c>, <c>settings-tab.parallel-jobs.hint</c>) 3 kol 3 dilde (running: pt; label: en, es), KOL dokumunden sayildi, tr girmiyor: 3075 + 3 = 3078, en 299 + 1 = 300, tr 103.</para>
    /// <para>2026-10-10, K2 sistem sesi yakalama: uc yeni anahtardan (<c>recorder.audio.loopback</c>, <c>.switched</c>, <c>.lost</c>) yalniz <c>recorder.audio.loopback</c> 5 dilde kola giriyor (en, hu, it, sl, tr), KOL dokumunden sayildi. Onceki pim 3081 idi: 3081 + 5 = 3086, en 300 + 1 = 301, tr 103 + 1 = 104.</para>
    /// <para>2026-10-14, hizli kip kayma bildirimi: dort yeni anahtardan 9 kol 9 dilde (<c>editor.export.keyframe-lead</c>: bn, hi, ja, th, ur; <c>editor.on-keyframe</c>: en, it; <c>editor.key.prev-keyframe</c> ve <c>next-keyframe</c>: hr), KOL dokumunden sayildi, tr girmiyor: 3086 + 9 = 3095, en 301 + 1 = 302, tr 104.</para>
    /// <para>2026-10-14, kodlama sureci onceligi (SIK-P1): yedi yeni anahtardan 7 kol 4 dilde (es: encode-priority.label, .below-normal; fr: encoder-threads.label; pt: encode-priority.label, .below-normal, encoder-threads.label; sw: encode-priority.below-normal), KOL dokumunden sayildi, en ve tr girmiyor: 3095 + 7 = 3102, en 302, tr 104.</para>
    /// <para>2026-10-14, dalga 1 birlesmesi: OYA-P1 (4), OYB-P2 (1) ve DZB-P1 (7) anahtarlari pinleri guncellemeden geldi; birlesik agacta olculdu: 3102 + 5 = 3107, en 302, tr 104.</para>
    /// <para>2026-10-14, ARC-P1 gurultu temizleme ve yukseklik olcumu: bes yeni <c>main.audio.*</c> anahtarindan 9 kol 8 dilde (denoise: es, fr, pt, ro; loudness.measure: vi; loudness.result: bn, hi, th, ur), KOL dokumunden sayildi, en ve tr girmiyor; dalga 1 birlesik agacinda 3107 + 9 = 3116, en 302, tr 104.</para>
    /// <para>2026-10-14, gecmis penceresi ve devam ayari (OYB-P4): dokuz yeni anahtardan 14 kol 11 dilde (history.empty: en, et, fi, nl; history.title: es, fr, pt, ro, sw; view.resume: en, it, nl, pt, ro), KOL dokumunden sayildi, tr girmiyor: 3116 + 14 = 3130, en 302 + 2 = 304, tr 104.</para>
    /// <para>2026-10-14, adli Donustur on ayari (DON-P5): uc yeni <c>main.convert.preset.*</c> anahtarindan yalniz <c>main.convert.preset.placeholder</c> 1 dilde kola giriyor (en), KOL dokumunden sayildi, tr girmiyor: 3116 + 1 = 3117, en 302 + 1 = 303, tr 104.</para>
    /// <para>2026-10-14, AYR-P2 ayar yedegi: on yeni <c>settings.backup.*</c>, <c>settings.recovery.*</c>, <c>settings.error.export/import</c> anahtarindan 19 kol 10 dilde (cs 1, en 3, es 1, fr 1, hu 3, nl 3, pt 3, ro 1, sk 1, sr 2), KOL dokumunden sayildi, tr girmiyor; 3116 + 19 = 3135, en 305, tr 104.</para>
    /// <para>2026-10-14, yakilan altyazida bicim secimi (SIK-P5): dort yeni <c>main.subtitles.style.*</c> anahtarindan 4 kol 4 dilde (box: es, fr, pt, ro), KOL dokumunden sayildi, en ve tr girmiyor: 3116 + 4 = 3120, en 302, tr 104.</para>
    /// <para>2026-10-14, OYA-P2 altyazi satiri ve tuslari: dokuz yeni <c>player.subtitle.*</c> anahtarindan 22 kol 9 dilde (de, en, es, fr, hu, lt, pt, ro, sw; en yalniz stepprev/stepnext), KOL dokumunden sayildi, tr girmiyor; 3116 + 22 = 3138, en 304, tr 104.</para>
    /// <para>2026-10-14, DZA-P2 otomatik bolme ve parca adlari: on uc yeni <c>editor.*</c> anahtarindan 24 kol 10 dilde (en: split.count; es, fr: split.count, split.value, scene.threshold, export.part-name; pt: ayni dordu ve marker.name; ro: split.count, split.value, scene.threshold; sw: split.count, split.value; bn, hi, ja, th, ur: export.part-name.hint), KOL dokumunden sayildi, tr girmiyor; 3116 + 24 = 3140, en 303, tr 104.</para>
    /// <para>2026-10-14, kosan kodlamayi duraklatma (SIK-P3): uc yeni anahtardan 6 kol 6 dilde (<c>main.shrink-job.disk-paused</c>: bn, hi, ja, th, ur; <c>main.action.pause-run</c>: it), yeniden yazilan <c>main.shrink-job.paused</c> kol sayisini degistirmedi, KOL dokumunden sayildi, en ve tr degismiyor: 3116 + 6 = 3122, en 302, tr 104.</para>
    /// <para>2026-10-14, EKB-P2 imlec vurgusu ve spot isigi: on bir yeni anahtardan 16 kol 9 dilde (en: option.cursor-highlight; da, nb: pointer.left-color, .right-color; es: pointer.highlight-color; fr: pointer.highlight-color, .size; pt: pointer.title, .highlight-color, .left-color, .right-color, .size; sw: pointer.highlight-color, .left-color, .right-color), KOL dokumunden sayildi: 3116 + 16 = 3132, en 303, tr 104.</para>
    /// <para>2026-10-14, ses cikisi ve cikis secenekleri (OYA-P4): bes yeni anahtardan (<c>player.advanced.passthrough</c>, <c>.exclusive</c>, <c>.stereo</c>, <c>player.tracks.output</c>, <c>.output-auto</c>) 13 kol 9 dilde (cs, en, it, pl, sk: stereo; es: passthrough, exclusive, stereo, output; pt: passthrough, exclusive, output; ro: passthrough), KOL dokumunden sayildi, tr girmiyor: 3206 + 13 = 3219, en 311 + 1 = 312, tr 104.</para>
    /// <para>2026-10-14, OYA-P3 uyku engeli ve pencere davranisi: iki yeni anahtardan 5 kol 3 dilde (en: pause-on-minimize, topmost-while-playing; it: ikisi; pt: topmost-while-playing), KOL dokumunden sayildi, tr girmiyor; olculen SAYIM toplam 3211, en 313, tr 104: 3206 + 5 = 3211, en 311 + 2 = 313, tr 104.</para>
    /// <para>2026-10-14, liste penceresinde arama ve siralama (OYB-P3): yedi yeni anahtardan 7 kol 5 dilde (en ve tr: sort-name, sort-date; hr ve it: item.move-up; hu: search), KOL dokumunden sayildi; olculen 3206 + 7 = 3213, en 311 + 2 = 313, tr 104 + 2 = 106.</para>
    /// <para>2026-10-14, gizlilik maskesi (EKB-P4): bes yeni <c>recorder.mask.*</c> anahtarindan 4 kol 4 dilde (<c>recorder.mask.title</c>: es, fr, pt, ro), KOL dokumunden sayildi, en ve tr degismiyor: 3206 + 4 = 3210, en 311, tr 104.</para>
    /// <para>2026-10-14, temel renk ve hazir gorunum (DZB-P3): sekiz yeni <c>editor.clip.*</c> anahtarindan (brightness, contrast, saturation, temperature, look, look.none, look.bw, look.old-film) 2 kol 2 dilde (look.bw: en; look.old-film: sw), KOL dokumunden sayildi, tr girmiyor: 3206 + 2 = 3208, en 311 + 1 = 312, tr 104.</para>
    /// <para>2026-10-14, dis ses izi (SIK-P4): bes yeni <c>main.audio.external.*</c> anahtarindan 9 kol 4 dilde (es: add, file-type, remove; hu: replace; pt: add, file-type, remove; sw: label, replace), KOL dokumunden sayildi, en ve tr girmiyor; dalga 2 agacinda olculen 3206 + 9 = 3215, en 311, tr 104.</para>
    /// <para>2026-10-14, Donustur toplu kuyrugu (DON-P6): dokuz yeni <c>main.convert-queue.*</c> anahtarindan 11 kol 10 dilde (setting: da, en, fr, hu, nb, tr; headline: en, it, pl, ro; waiting: it), KOL dokumunden sayildi; birlesik agacta 3206 + 11 = 3217, en 311 + 2 = 313, tr 104 + 1 = 105.</para>
    /// <para>2026-10-14, disa aktarim ayarlari ve on ayarlar (DZB-P2): on dokuz yeni <c>editor.export.*</c> anahtarindan 17 kol 7 dilde (en: source; es: audio-bitrate, codec, fps; hu: audio-only, source; ms: audio-bitrate, fps; pt: audio-bitrate, codec, fps, preset.name; ro: audio-bitrate, fps; sw: audio-bitrate, codec, fps), KOL dokumunden sayildi, tr girmiyor; olculen 3206 + 17 = 3223, en 311 + 1 = 312, tr 104.</para>
    /// <para>2026-10-14, tarama araliklarini tek tek yonetme (DZA-P3): bes yeni anahtardan 45 kol (<c>editor.silence.hint</c> 41 dilde, en ve tr dahil; <c>editor.silence.track</c>: es, pt; <c>editor.silence.padding-start</c> ve <c>.padding-end</c>: sw), kaldirilan <c>editor.silence.padding</c> 1 kol goturdu, KOL dokumunden sayildi: 3206 + 45 - 1 = 3250, en 312, tr 105.</para>
    /// <para>2026-10-14, klip gruplama ve pano (DZB-P4): alti yeni anahtardan (<c>editor.copy</c>, <c>.cut</c>, <c>.paste</c>, <c>.duplicate</c>, <c>.group</c>, <c>.ungroup</c>) 1 kol 1 dilde (paste: sv), KOL dokumunden sayildi, en ve tr girmiyor; 887cb448 agacinda olculen 3334 + 1 = 3335, en 322, tr 108.</para>
    /// <para>2026-10-14, medya bilgisi ve denetim raporu (ARC-P2): 56 yeni <c>mediainfo.*</c> anahtarindan 82 kol 14 dilde (cs 1, en 2: failed, save; es 16, fr 6, hr 2, hu 1, id 1, lt 1, ms 4, pt 19, ro 12, sl 2, sr 4, sw 11), KOL dokumunden sayildi, tr girmiyor: 3334 + 82 = 3416, en 322 + 2 = 324, tr 108.</para>
    /// <para>2026-10-14, kamera bicimi ve konumu (EKB-P5): kaldirilan dort kose anahtarindan (<c>recorder.webcam.top-left</c>, <c>.top-right</c>, <c>.bottom-left</c>, <c>.bottom-right</c>) 12 kol 4 dilde gitti (es 4, hr 2, it 4, sr 2); bes yeni anahtar (<c>recorder.webcam.shape</c>, <c>.shape.rectangle</c>, <c>.shape.rounded</c>, <c>.shape.circle</c>, <c>.mirror</c>) hicbir dilde kola girmiyor. Taban dil dosyalariyla ve yenileriyle iki KOL dokumunden sayildi: 3334 - 12 = 3322, en 322, tr 108.</para>
    /// <para>2026-10-14, iz secim paneli (DZA-P5): uc yeni anahtardan (<c>editor.export.audio-track</c>, <c>.none</c>, <c>editor.monitor.audio-track</c>) 6 kol 4 dilde (es: export ve monitor; fr: monitor; lt: none; pt: export ve monitor), KOL dokumunden sayildi, en ve tr girmiyor: taban pimi 3334 + 6 = 3340, en 322, tr 108.</para>
    /// <para>2026-10-15, acilis iz dili (OYA-P5): iki yeni anahtardan (<c>player.view.audio-language</c>, <c>.subtitle-language</c>) 16 kol 8 dilde (de, en, es, fr, hu, nl, pt, sw; ikisi de), KOL dokumunden sayildi, tr girmiyor: 3411 + 16 = 3427, en 324 + 2 = 326, tr 108.</para>
    /// <para>2026-10-15, ceviri tamlik isareti (AYR-25): yeni <c>main.language.unreviewed</c> anahtarindan 1 kol 1 dilde (sw), KOL dokumunden sayildi, en ve tr girmiyor; 723bb1b8 agacinda olculen 3411 + 1 = 3412, en 324, tr 108.</para>
    /// <para>2026-10-15, kodlarken uyumama (SIK-P2): iki yeni <c>settings-tab.prevent-sleep.*</c> anahtarindan 5 kol 5 dilde, hepsi <c>label</c> (en hr hu pt sl), KOL dokumunden sayildi, tr girmiyor: 723bb1b8 agacinda olculen 3411 + 5 = 3416, en 324 + 1 = 325, tr 108.</para>
    /// <para>2026-10-15, kuyruk saklama (SIK-P9): yeni <c>main.shrink-job.restored</c> anahtarindan 8 kol 8 dilde (cs en hu it ro sk sl tr), KOL dokumunden sayildi: 3416 + 8 = 3424, en 325 + 1 = 326, tr 108 + 1 = 109.</para>
    /// <para>2026-10-15, tasinabilir kip (AYR-P8): iki yeni anahtardan 8 kol 8 dilde (<c>main.update.download-page</c>: es, fr, pt, ro; <c>settings.update.portable-effect</c>: bn, hi, th, ur), KOL dokumunden sayildi, en ve tr girmiyor: 3411 + 8 = 3419.</para>
    /// <para>2026-10-14, Donustur'de altyazi izleri (DON-P2): yedi yeni <c>main.convert.subtitles.*</c> anahtarindan 14 kol 13 dilde (<c>main.convert.subtitles</c>: es, fr, pt, ro; <c>.extract</c>: cs, de, en, it, pl, sk; <c>.extract-failed</c>: en, hu, pt, ro), KOL dokumunden sayildi, tr girmiyor; olculen 3334 + 14 = 3348, en 322 + 2 = 324, tr 108.</para>
    /// <para>2026-10-15, tum izleri varsayilan tasima (DZA-P7): yeni anahtar <c>editor.export.audio-track.all</c> 1 kol (pt: Todas as faixas), KOL dokumunden; taban pimi 3411 + 1 = 3412.</para>
    /// <para>2026-10-15, capraz gecis teslimi (DZB-P6): sekiz yeni anahtardan (<c>editor.clip.transition</c>, <c>.none</c>, <c>.dissolve</c>, <c>.wipe</c>, <c>.slide</c>, <c>.iris</c>, <c>.duration</c>, <c>editor.export.transitions-full</c>) 4 kol, hepsi <c>editor.clip.transition.duration</c> (es, fr, nl, pt), KOL dokumunden; taban pimi 3412 + 4 = 3416.</para>
    /// <para>2026-10-15, HDR yakalama (EKA-P15): uc yeni anahtardan (<c>recorder.advanced.hdr-to-sdr</c>, <c>.hdr-to-sdr-hint</c>, <c>recorder.capture.hdr-lost</c>) yalniz <c>recorder.advanced.hdr-to-sdr</c> 9 dilde kola giriyor (cs, de, en, es, it, pl, pt, sk, sw), KOL dokumunden sayildi, tr girmiyor: taban pimi 3411 + 9 = 3420, en 324 + 1 = 325, tr 108.</para>
    /// <para>2026-10-15, tus gosterimi suzgeci (EKB-P1): on bir yeni <c>recorder.keys.*</c> anahtarindan 5 satir kola giriyor (hr <c>.place.top</c>; it <c>.place.bottom</c>, <c>.place.top</c>, <c>.title</c>; pt <c>.size</c>), KOL dokumunden sayildi, en ve tr girmiyor: taban pimi 3420 + 5 = 3425, en 325, tr 108.</para>
    /// <para>2026-10-15, kayan sonuc karti (EKB-P14): dort yeni <c>recorder.card.*</c> anahtarindan 14 satir kola giriyor (en <c>.show</c>; es, fr, hu <c>.show</c> ve <c>.never</c>; hr, sl, sr, tr <c>.never</c>; nl, pt, sw <c>.show</c>), KOL dokumunden sayildi: taban pimi 3425 + 14 = 3439, en 325 + 1 = 326, tr 108 + 1 = 109.</para>
    /// <para>2026-10-15, origin/main birlesmesinden sonra (EKB-P14 dali): main tarafinin pimi 3449 / en 328 / tr 109 ile bu dalin artislari bir arada; testin kendi SAYIM satirindan olculdu: toplam 3477, en 330, tr 110.</para>
    /// <para>2026-10-15, ses dosyasi ve GIF girdisi (ARC-P3): uc yeni anahtardan (<c>main.convert.note.gif-target</c>, <c>main.error.audio-only-shrink</c>, <c>main.validation.no-video-container</c>) 4 kol, KOL dokumunden sayildi, en ve tr girmiyor: olculen 3479 + 4 = 3483, en 331, tr 109.</para>
    /// <para>2026-10-15, altyazi gomme ve bicemi (DON-P3): alti yeni anahtar (<c>main.subtitles.style.color.*</c> bes renk, <c>main.validation.burn-encode</c>), dusen yok; olculen 70563 + 43 x 6 = 70821. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, kaydedici disk uyarisi ve kayit kurtarma (EKA-P1): bes yeni anahtar (<c>recorder.disk.low</c>, <c>recorder.recover.*</c> dort), dusen yok; olculen 70821 + 43 x 5 = 71036. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, dosya basina hatirlama ve gezinme (OYA-05): dort yeni anahtar (<c>main.player.menu.goto</c>, <c>player.view.seek-steps</c>, <c>player.goto.hint</c>, <c>player.goto.invalid</c>), dusen yok; olculen 70821 + 43 x 4 = 70993. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, dosya basina hatirlama ve gezinme (OYA-05): dort yeni anahtardan 7 kol (<c>main.player.menu.goto</c>: en, es, pl; <c>player.view.seek-steps</c>: es, fr, pt, ro), KOL dokumunden sayildi, tr girmiyor: taban pimi 3516 + 7 = 3523, en 334 + 1 = 335, tr 110.</para>
    /// <para>2026-10-15, iki dalin birlesmesi: 70821 + 215 + 172 = 71208.</para>
    /// <para>2026-10-15, SIK-P6 birlesmesi: olculen toplam 3523 + 10 = 3533, en 336, tr 110, gezilen 71208 + 258 = 71466.</para>
    /// <para>2026-10-15, DZA-P8 birlesmesi: olculen toplam 3533 + 3 = 3536, en 337, tr 110, gezilen 71466 + 86 = 71552.</para>
    /// <para>2026-10-15, AYR-P4 birlesmesi: olculen toplam 3536 + 2 = 3538, en 337, tr 110, gezilen 71552 + 258 = 71810.</para>
    /// <para>2026-10-15, DON-P9 birlesmesi: olculen toplam 3538 + 4 = 3542, en 337, tr 110, gezilen 71810 + 258 = 72068.</para>
    /// <para>2026-10-15, OYA-P10: olculen toplam 3542 + 16 = 3558, en 338, tr 110, gezilen 72068 + 215 = 72283.</para>
    /// <para>2026-10-15, guncelleme, hakkinda ve tanilama (AYR-P3): yirmi iki yeni anahtar (<c>settings.log.report</c>, <c>settings.diag.*</c>, <c>main.about.notes</c>, <c>.licenses</c>, <c>.privacy</c>, <c>main.about.check.*</c>, <c>main.update.whats-new</c>), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3542 + 29 = 3571, en 337 + 6 = 343, tr 110, gezilen 72068 + 43 x 22 = 73014.</para>
    /// <para>2026-10-15, OYA-P10 ile AYR-P3 birlesmesinden sonra: iki dalin kaymasi toplandi, dusen yok; olculen toplam 3542 + 16 + 29 = 3587, en 337 + 1 + 6 = 344, tr 110, gezilen 72068 + 215 + 946 = 73229.</para>
    /// <para>2026-10-15, DON-P10 birlesmesinden sonra: dalin kaymasi eklendi, dusen yok; olculen toplam 3587 + 16 = 3603, en 344 + 1 = 345, tr 110, gezilen 73229 + 387 = 73616.</para>
    /// <para>2026-10-15, SIK-P7 birlesmesinden sonra: dalin kaymasi eklendi, dusen yok; olculen toplam 3603 + 19 = 3622, en 345 + 6 = 351, tr 110 + 1 = 111, gezilen 73616 + 602 = 74218.</para>
    /// <para>2026-10-15, DZB-P5 ve OYB-P8 birlesmelerinden sonra: iki dalin kaymasi toplandi, dusen yok; olculen toplam 3622 + 2 + 28 = 3652, en 351 + 5 = 356, tr 111 + 7 = 118, gezilen 74218 + 344 + 860 = 75422.</para>
    /// <para>2026-10-15, medya havuzu ve araya ekleme (DZB-P11): bes yeni <c>editor.media.*</c> anahtari; 4 kol (<c>editor.media.mode</c>: en; <c>editor.media.open</c>: es, pt, sw), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3652 + 4 = 3656, en 356 + 1 = 357, tr 118, gezilen 75422 + 43 x 5 = 75637.</para>
    /// <para>2026-10-15, ara kare (ARC-P4): iki yeni anahtar (<c>main.convert.interpolate</c>, <c>main.convert.interpolate.tip</c>), toplam ve en/tr kaymadi; olculen gezilen 75422 + 43 x 2 = 75508.</para>
    /// <para>2026-10-15, kabukta coklu acma ve siraya ekleme (OYB-P7): bir yeni anahtar (<c>shell.menu.enqueue</c>) 9 dilde kola giriyor (cs, en, es, fr, hu, pl, pt, sk, sw), KOL dokumunden ve SAYIM satirindan olculdu, tr girmiyor: toplam 3652 + 9 = 3661, en 356 + 1 = 357, tr 118, gezilen 75422 + 43 = 75465.</para>
    /// <para>2026-10-15, ses temizleme ve duzey (DZB-P5): sekiz yeni <c>editor.clip.*</c> anahtari (gurultu, ses esitleme, sikistirici, sinirlayici, uc bant, duzey olcer), testin SAYIM satirindan olculdu: toplam 3622 + 2 = 3624, gezilen 74218 + 344 = 74562.</para>
    /// <para>2026-10-15, yer imi listesi, sik kullanilanlar ve acilista sorma (later 49 + 159): yirmi yeni anahtar (<c>player.bookmarks.*</c>, <c>player.favorites.*</c>, <c>player.view.resume-*</c>, <c>player.resume.*</c>), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3622 + 28 = 3650, en 351 + 5 = 356, tr 111 + 7 = 118, gezilen 74218 + 860 = 75078.</para>
    /// <para>2026-10-15, kuyrukta zamanlama, duzenleme ve bitis (SIK-P7): on dort yeni anahtar (<c>main.shrink-job.*</c> 12, <c>settings-tab.finish-sound.*</c> 2), testin SAYIM satirindan olculdu: toplam 3542 + 19 = 3561, en 337 + 6 = 343, tr 110 + 1 = 111, gezilen 72068 + 43 x 14 = 72670.</para>
    /// <para>2026-10-15, cikti tarihi, adi ve dogrulamasi (SIK-P6): alti yeni anahtardan (<c>main.shrink-job.failed</c>, <c>.retry</c>, <c>main.run.output-unreadable</c>, <c>.output-short</c>, <c>settings-tab.keep-dates.label</c>, <c>.hint</c>) 10 kol (<c>main.shrink-job.retry</c>: es, hu, it, nl, pl, pt; <c>settings-tab.keep-dates.label</c>: en, es, hu, pt), KOL dokumunden sayildi, tr girmiyor: olculen 3516 + 10 = 3526, en 334 + 1 = 335, tr 110.</para>
    /// <para>2026-10-15, bolum yazma (DZA-P8): iki yeni anahtardan (<c>editor.export.chapters</c>, <c>.tip</c>) yalniz <c>editor.export.chapters</c> 3 dilde kola giriyor (en, hu, pt), KOL dokumunden sayildi, tr girmiyor: olculen 3516 + 3 = 3519, en 334 + 1 = 335, tr 110.</para>
    /// <para>2026-10-15, sistem temasi ve hareket ayari (AYR-P4): alti yeni anahtar (<c>settings-tab.follow-system-theme.*</c>, <c>settings-tab.theme.light</c>, <c>.dark</c>, <c>settings-tab.reduce-motion.*</c>); yalniz <c>settings-tab.follow-system-theme.label</c> 2 dilde kola giriyor (pt, sw), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3516 + 2 = 3518, en 334, tr 110. Gezilen 70821 + 43 x 6 = 71079, <c>kayip</c> 0.</para>
    /// <para>2026-10-15, Donustur ses secenekleri (DON-P9): alti yeni anahtar (<c>main.convert.audio-loudnorm</c>, <c>.audio-channels</c>, <c>.audio-sample-rate</c>, <c>main.validation.audio-channels-positive</c>, <c>.mxf-sample-rate</c>, <c>.audio-sample-rate</c>), testin SAYIM satirindan olculdu: toplam 3520, en 334, tr 110.</para>
    /// <para>2026-10-15, Donustur'de goruntu suzgecleri (DON-P10): dokuz yeni <c>main.convert.*</c> anahtari (olcekleme, kare hizi kipi, dolgu, suzgec notu), testin SAYIM satirindan olculdu: toplam 3558, en 338, tr 110.</para>
    /// <para>2026-10-15, doldurma kipi (OYA-P11): <c>player.view.fill</c> 3 dilde kola giriyor (en, nl, tr), KOL dokumunden sayildi: olculen 3652 + 3 = 3655, en 356 + 1 = 357, tr 118 + 1 = 119.</para>
    /// <para>2026-10-15, OYA-P11 ve DZB-P11 birlesmesinden sonra: iki dalin kaymasi toplandi, dusen yok; olculen toplam 3652 + 3 + 4 = 3659, en 356 + 1 + 1 = 358, tr 118 + 1 = 119, gezilen 75422 + 43 + 215 = 75680.</para>
    /// <para>2026-10-15, ARC-P4 ve OYB-P7 birlesmesinden sonra: olculen toplam 3659 + 9 = 3668, en 358 + 1 = 359, tr 119, gezilen 75680 + 86 + 43 = 75809.</para>
    /// <para>2026-10-15, kare ve tablo (later 82): iki yeni anahtar (<c>player.tools.frames</c>, <c>.sheet</c>); yalniz <c>player.tools.sheet</c> 5 dilde kola giriyor (es, fr, pt, ro, sw), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3668 + 5 = 3673, en 359, tr 119, gezilen 75809 + 43 x 2 = 75895.</para>
    /// <para>2026-10-15, later 65 kare hizi kipi: <c>recorder.advanced.frame-rate-mode</c> 6 dilde kola giriyor (es, fr, ms, pt, ro, sw), KOL dokumunden sayildi; en ve tr degismedi. Olculen toplam 3668 + 6 = 3674, gezilen 75809 + 215 = 76024.</para>
    /// <para>2026-10-15, later 82 ve later 65 birlesmesinden sonra: iki dalin kaymasi toplandi, dusen yok; olculen toplam 3668 + 5 + 6 = 3679, en 359, tr 119, gezilen 75809 + 86 + 215 = 76110.</para>
    /// <para>2026-10-15, later 84 birlesmesinden sonra: kesim listesi anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3679 + 35 = 3714, en 359 + 2 = 361, tr 119, gezilen 76110 + 344 = 76454.</para>
    /// <para>2026-10-15, later 73 sigdirma: dort yeni anahtar (<c>editor.clip.fit</c>, <c>.pad</c>, <c>.pad.black</c>, <c>.pad.white</c>) 42 dilde; olculen, en ve tr degismedi, gezilen 76454 + 172 = 76626 (yerel kosumun gercek degeri).</para>
    /// <para>2026-10-15, kap ici iz isleri (DZA-85 ilk yarisi): on yedi yeni <c>editor.remux.*</c> anahtarindan 7 kol (bn, hi, ur <c>invalid</c>; es, fr, pt <c>track-title</c>; pt <c>file-title</c>), KOL dokumunden ve SAYIM satirindan olculdu, en ve tr girmiyor: toplam 3714 + 7 = 3721, en 361, tr 119, gezilen 76454 + 43 x 17 = 77185.</para>
    /// <para>2026-10-15, later 73 ve later 85 birlesmesinden sonra: iki dalin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3714 + 7 = 3721, en 361, tr 119, gezilen 76454 + 172 + 731 = 77357.</para>
    /// <para>2026-10-15, later 61 (arşiv kodeği, APNG, GIF ayarı): 11 yeni anahtar; ölçülen toplam 3714 + 1 = 3715, tek kol tr main.convert.gif-loop.once ("Bir kez"), en 361, tr 119 + 1 = 120, gezilen 76454 + 473 = 76927.</para>
    /// <para>2026-10-15, kesim listesi (DZA-P10): sekiz yeni <c>editor.cutlist.*</c> anahtarindan 35 kol (en 2, es 7, fr 6, hu 2, pt 7, ro 6, sw 5), KOL dokumunden ve SAYIM satirindan olculdu, tr girmiyor: toplam 3668 + 35 = 3703, en 359 + 2 = 361, tr 119.</para>
    /// <para>2026-10-15, metin konumu ve hareketi (later 86): on dort yeni <c>editor.text.*</c> anahtarindan 8 kol (es, it, pl, pt <c>.motion.typewriter</c>; hr <c>.keyframe</c>; ro <c>.motion.bottom</c>, <c>.motion.top</c>, <c>.motion.typewriter</c>), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3714 + 8 = 3722, en 361, tr 119.</para>
    /// <para>2026-10-15, later 64 yalniz ses ve ses secenekleri: dort yeni kaydedici anahtari 3 kol ekliyor, en ve tr degismedi; olculen toplam 3714 + 3 = 3717, gezilen 76454 + 172 = 76626.</para>
    /// <para>2026-10-15, altyazi aktarimi (DZB-62): yedi yeni <c>editor.subtitle.*</c> anahtarindan 9 kol (es, ro <c>files</c>; fr <c>files</c>, <c>unreadable</c>; hu <c>failed</c>, <c>unreadable</c>; pt <c>failed</c>, <c>files</c>, <c>unreadable</c>), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3733 + 9 = 3742, en 361, tr 120.</para>
    /// <para>2026-10-15, kap ici iz isleri ikinci yarisi (DZA-25, 26, 44): on uc yeni <c>editor.remux.*</c> anahtarindan 18 kol (<c>include</c> cs, de, en, hu, nl, pl, sk; <c>add-title</c> da, en, hu, it, sw; <c>cover-title</c> es, fr, pt, ro, sw; <c>extract</c> it), KOL dokumunden ve SAYIM satirindan olculdu, tr girmiyor: toplam 3733 + 18 = 3751, en 361 + 2 = 363, tr 120.</para>
    /// <para>2026-10-15, uzun iste oncelik ve bitis sesi (later 76): dort yeni anahtardan 5 kol (es, pt, sw <c>settings-tab.encode-priority.above-normal</c>; pt, sw <c>settings-tab.finish-sound-file.reset</c>), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3733 + 5 = 3738, en 361, tr 120.</para>
    /// <para>2026-10-15, bas konus (later 68): iki yeni anahtar (<c>recorder.audio.push-to-talk</c>, <c>.hint</c>) 6 kol ekliyor (en <c>push-to-talk</c> dahil), tr degismedi; testin SAYIM satirindan olculdu: toplam 3733 + 6 = 3739, en 361 + 1 = 362, tr 120.</para>
    /// <para>2026-10-15, saatli kayit (later 67): on iki yeni <c>recorder.schedule.*</c> anahtarindan 5 kol (hu <c>.off</c>; pt <c>.weekly</c>; tr <c>.once</c>, <c>.daily</c>, <c>.weekly</c>), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3733 + 5 = 3738, en 361, tr 120 + 3 = 123.</para>
    /// <para>2026-10-16, hizli kirpma (later 41): dokuz yeni <c>recorder.output.quick-trim*</c> anahtarindan 2 kol (it <c>quick-trim-running</c>, ro <c>quick-trim-failed</c>), KOL dokumunden sayildi, en ve tr girmiyor: olculen 3781 + 2 = 3783, en 364, tr 123.</para>
    /// <para>2026-10-16, onizlemede elle kirpma (later 57): iki yeni anahtar (<c>main.advanced.filters.manualcrop</c>, <c>.tip</c>) kola girmiyor; en etiketi "Crop in preview" iken kola girip baslik denetiminde kusur sayildi, "Manual crop" yapildi. SAYIM satirindan olculdu: toplam 3781, en 364, tr 123.</para>
    /// <para>2026-10-18, biten ciktinin kalite olcumu (later 122): dokuz yeni anahtardan (<c>main.advanced.quality-check.*</c>, <c>main.output.quality-check</c>, <c>main.run.quality*</c>) 6 kol (en, hu, pt, sw <c>main.advanced.quality-check.label</c>; fr, pt <c>main.output.quality-check</c>), KOL dokumunden ve SAYIM satirindan olculdu, tr girmiyor: toplam 3888 + 6 = 3894, en 373 + 1 = 374, tr 126.</para>
    /// <para>2026-10-12, Linux Wayland: <c>recorder.error.window-wayland</c> metni artik her kaydi reddediyor ve "Wayland" ile basliyor; bn, hi, th, ur kola giriyor (KOL dokumu). 3086 + 4 = 3090, en ve tr degismedi.</para>
    /// <para>2026-10-18, cizim arac paneli (later 42): dokuz yeni <c>recorder.sketch.*</c> anahtarindan yalniz <c>recorder.sketch.toolbar</c> 4 dilde kola giriyor (es, fr, pt, ro), KOL dokumunden ve SAYIM satirindan olculdu, en ve tr girmiyor: toplam 3948 + 4 = 3952, en 381, tr 127.</para>
    /// <para>2026-10-18, oynatici HDR ton esleme (later 96): yeni <c>player.advanced.tone-mapping</c> es ("Mapeo de tonos HDR") ve pt ("Mapeamento de tons HDR") dillerinde kola giriyor, en ve tr haric: 3888 + 2 = 3890, en 373, tr 126.</para>
    /// <para>2026-10-18, OTIO aktarimi (yan/otio): sekiz yeni <c>editor.otio.*</c> anahtarindan 12 kol (en 4, ro 4, fr, nl, pt, tr birer), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3970 + 12 = 3982, en 383 + 4 = 387, tr 129 + 1 = 130.</para>
    /// <para>2026-10-18, kamera ana kaynak (later 69): iki yeni anahtar (<c>recorder.target.device</c>, <c>.device-pick</c>); yalniz <c>recorder.target.device</c> 6 dilde kola giriyor (en, nl, pt, ro, sw, tr), KOL dokumunden ve SAYIM satirindan olculdu: toplam 3948 + 6 = 3954, en 381 + 1 = 382, tr 127 + 1 = 128.</para>
    /// <para>2026-10-18, akis kalite secimi (later 83): iki yeni anahtar (<c>player.tracks.quality</c>, <c>.rate</c>); yalniz <c>player.tracks.quality</c> es ve pt dillerinde kola giriyor, KOL dokumunden ve SAYIM satirindan olculdu, en ve tr girmiyor: toplam 3970 + 2 = 3972, en 383, tr 129.</para>
    /// <para>2026-10-20, later 78: <c>main.share.link</c> 42 dilde; fr, pt, ro kola giriyor (KOL dokumu), en ve tr degismedi: toplam 4074 + 3 = 4077.</para>
    /// <para>2026-10-18, ad VidShrink Player oldu: <c>{app}</c> iki sozcuge acilinca ja ve th dillerinde bes anahtar kola giriyor (<c>main.preset.handbrake.dropped.engine-decides</c>, <c>.no-equivalent</c> ikiser, ja <c>settings.update.portable-effect</c>), cs <c>shell.menu.open</c> cikiyor; KOL dokumu main ile karsilastirildi, en ve tr degismedi; testin olctugu toplam 4069'dan 4074'e cikti.</para>
    /// <para>2026-10-20, murekkep kisayolu (later 42): yeni <c>recorder.hotkeys.ink</c> 42 dilde, dusen yok; es, fr, pt, ro, sw kola giriyor (KOL dokumu): toplam 4074 + 5 = 4079, en 393, tr 132 degismedi.</para>
    /// <para>2026-10-20, kamera kipi (later 69): uc yeni anahtardan (<c>recorder.target.device-mode</c>, <c>.auto</c>, <c>.item</c>) 12 kol (<c>device-mode</c>: cs, en, es, ms, pt, ro, sk, sl, sw, tr; <c>.auto</c>: fr, pt), KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 12 = 4086, en 393 + 1 = 394, tr 132 + 1 = 133.</para>
    /// <para>2026-10-20, later 88 ve 89 kalani: yedi yeni anahtar (<c>editor.clip.keys.add-transform</c>, alti <c>editor.clip.match.*</c>) 42 dilde, <c>editor.clip.keys.count</c> ucuncu sayiyi aldi; SAYIM satirindan olculdu: toplam 4074 + 53 = 4127, en 393 + 4 = 397, tr 132 + 1 = 133.</para>
    /// <para>2026-10-20, uygulama basina ses ve kayit cercevesi (later 105, 106): 21 yeni kaydedici anahtari 43 dilde; KOL dokumunden olculdu: toplam 4074 + 18 = 4092; en 393 + 3 = 396 (recorder.audio.app.requires, .output.frame-image, .frame-running), tr 132 + 1 = 133 (recorder.audio.app.requires).</para>
    /// <para>2026-10-20, durgun klip ayarlari ve slayt sihirbazi (later 87, 90): yirmi yeni anahtar (<c>editor.still.*</c> dokuz, <c>editor.clip.move</c>, <c>.tip</c>, <c>editor.slides.*</c> dokuz) 42 dilde; 43 kol (en 5: <c>editor.clip.move</c>, <c>editor.slides.build</c>, <c>.fit</c>, <c>.working</c>, <c>editor.still.image-seconds</c>; tr 1: <c>editor.still.image-seconds</c>; es 7, pt 7, sw 4, da 3, nb 3, de 2, fr 2, ro 2; cs, hu, lt, nl, pl, sk, sl birer), KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 43 = 4117, en 393 + 5 = 398, tr 132 + 1 = 133.</para>
    /// <para>2026-10-20, olcerek arama (later 141): alti yeni anahtardan <c>main.run.search-round-done</c> 40 dilde (hu ve zh-Hans haric) ve en <c>main.advanced.search-by-measure.label</c> kola giriyor, KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 41 = 4115, en 393 + 2 = 395, tr 132 + 1 = 133.</para>
    /// <para>2026-10-21, birlesme turu (later 42, 69, 78, 87+90, 88+89, 105+106, 141): dallarin farklari toplandi ve birlesik agacta olculdu: toplam 4074 + 175 = 4249, en 393 + 15 = 408, tr 132 + 5 = 137.</para>
    /// <para>2026-10-21, later 140 birlesmesi: dokuz <c>editor.proxy.*</c> anahtari; toplam 4249 + 5 = 4254, en 408 + 1 = 409, tr 137.</para>
    /// <para>2026-10-21, later 147 birlesmesi: yazi boyutu anahtarlari; toplam 4254 + 1 = 4255 (pt), gezilen 92622 + 86 = 92708.</para>
    /// <para>2026-10-21, later 137 birlesmesi: <c>player.menu.new-window</c>; toplam 4255 + 4 = 4259, en 409 + 1 = 410, gezilen 92708 + 43 = 92751.</para>
    /// <para>2026-10-21, later 126 birlesmesi: masaustu simgesi anahtarlari; toplam 4259 + 2 = 4261, gezilen 92751 + 129 = 92880.</para>
    /// <para>2026-10-21, later 110 birlesmesi: V2 izi anahtarlari; toplam 4261 + 32 = 4293, en 410 + 3 = 413, gezilen 92880 + 645 = 93525.</para>
    /// <para>2026-10-21, later 99+103 birlesmesi: medya denetimi anahtari; toplam 4293 + 7 = 4300, en 413 + 1 = 414, gezilen 93525 + 43 = 93568.</para>
    /// <para>2026-10-21, later 138 birlesmesi: adli liste anahtarlari; toplam 4300 + 22 = 4322, en 414 + 1 = 415, gezilen 93568 + 645 = 94213.</para>
    /// <para>2026-10-21, later 114+115 birlesmesi: <c>recorder.output.zoom*</c> anahtarlari; toplam 4322 + 12 = 4334, en 415 + 1 = 416, tr 137 + 1 = 138, gezilen 94213 + 430 = 94643.</para>
    /// <para>2026-10-21, later 139 birlesmesi: ses ekleri ve iz denetimi anahtarlari; toplam 4334 + 152 = 4486, en 416 + 5 = 421, tr 138 + 4 = 142, gezilen 94643 + 774 = 95417.</para>
    /// <para>2026-10-21, later 140 vekil kopya: dokuz <c>editor.proxy.*</c> anahtari 42 dile eklendi; <c>editor.proxy.failed</c> en, fr, hu, nl ve ro dillerinde kola giriyor: olculen toplam 4079, en 394, tr 132.</para>
    /// <para>2026-10-20, yazi boyutu (later 147): iki yeni anahtar (<c>settings-tab.text-size.label</c>, <c>.hint</c>); yalniz <c>settings-tab.text-size.label</c> pt dilinde kola giriyor ("Tamanho do texto"), KOL dokumunden ve SAYIM satirindan olculdu, en ve tr girmiyor: toplam 4074 + 1 = 4075, en 393, tr 132.</para>
    /// <para>2026-10-21, yeni pencerede ac (later 137): yeni <c>player.menu.new-window</c> 4 dilde kola giriyor (de, en, it, nl), KOL dokumunden ve SAYIM satirindan olculdu, tr girmiyor: toplam 4074 + 4 = 4078, en 393 + 1 = 394, tr 132.</para>
    /// <para>2026-10-21, kayitta masaustu simgelerini gizleme (later 126): uc yeni <c>recorder.option.hide-desktop-icons*</c> anahtarindan yalniz etiket pt ve ro dillerinde kola giriyor, KOL dokumunden ve SAYIM satirindan olculdu, en ve tr girmiyor: toplam 4074 + 2 = 4076, en 393, tr 132.</para>
    /// <para>2026-10-21, V2 izi (later 110): on bes yeni <c>editor.layer*</c> ve <c>editor.export.layers-full</c> anahtari 42 dilde; 32 kol (en 3: <c>.add</c>, <c>.remove</c>, <c>.to-playhead</c>; tr girmiyor), KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 32 = 4106, en 393 + 3 = 396, tr 132.</para>
    /// <para>2026-10-20, Windows ortam denetimi anahtari (later 99): bir yeni anahtar (<c>player.view.media-controls</c>) 42 dilde; de, en, es, fr, hu, nl, pt kola giriyor (KOL dokumu), tr girmiyor: toplam 4074 + 7 = 4081, en 393 + 1 = 394, tr 132.</para>
    /// <para>2026-10-21, adli oynatma listeleri (later 138, OYB-09): on bes yeni <c>player.playlists.*</c> anahtari 42 dilde, dusen yok; es, fr, pt, ro ve hu basta olmak uzere 22 kol (en ve sw yalniz <c>.import-failed</c>), KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 22 = 4096, en 393 + 1 = 394, tr 132.</para>
    /// <para>2026-10-21, otomatik yakinlastirma (later 114 ilk yarisi): on yeni anahtar (<c>recorder.option.zoom-track</c>, <c>.zoom-track-hint</c>, <c>recorder.output.zoom*</c>) 42 dilde; 12 kol (en, nb, pl, sv, tr <c>zoom-track</c>; es, fr, pt, ro <c>zoom-level</c>; hu, ro <c>zoom-failed</c>; it <c>zoom-running</c>), KOL dokumunden ve SAYIM satirindan olculdu: toplam 4074 + 12 = 4086, en 393 + 1 = 394, tr 132 + 1 = 133.</para>
    /// <para>2026-10-21, Later 139: on sekiz editor.sound / editor.track anahtari 42 dile girdi; 152 yeni KOL satirinin 127'si "A3" iz adini tasiyan uc anahtardan (editor.sound, .detached, .narrate.placed), 25'i dile ozgu yazimdan; toplam 4074'ten 4226'ya, en 393'ten 398'e, tr 132'den 136'ya cikti.</para>
    /// </summary>
    [Fact]
    public void KolDegistirenAnahtarlarSayilir()
    {
        var toplam = 0;
        var dilBasina = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var dil in Strings.Languages.OrderBy(d => d, StringComparer.Ordinal))
        {
            dilBasina[dil] = 0;
            foreach (var anahtar in Strings.KeysOf(dil).OrderBy(k => k, StringComparer.Ordinal))
            {
                var ham = Strings.GetIn(dil, anahtar);
                if (EskiKuralaGoreGovde(ham)) continue;
                if (!LanguageCatalog.ReadsAsProse(ham)) continue;

                toplam++;
                dilBasina[dil]++;
                _cikti.WriteLine($"KOL\t{dil}\t{anahtar}\t{ham.ReplaceLineEndings(" ")}\t{LanguageCatalog.Title(ham, dil).ReplaceLineEndings(" ")}");
            }
        }

        foreach (var (dil, sayi) in dilBasina) _cikti.WriteLine($"SAYIM\t{dil}\t{sayi}");
        _cikti.WriteLine($"SAYIM\ttoplam\t{toplam}");

        Assert.Equal(4486, toplam);
        Assert.Equal(421, dilBasina["en"]);
        Assert.Equal(142, dilBasina["tr"]);
    }

    /// <summary>
    /// Butun anahtarlarin gercek <c>Title</c> ciktisini doker. Eski/yeni farkinin ham
    /// tabani bu dokum: ayni test <c>origin/main</c>'in <c>LanguageCatalog.cs</c>'siyle
    /// bir kez daha kosuluyor ve iki dokum <c>diff</c>'leniyor.
    /// </summary>
    /// <summary>
    /// T192 tur 3 K11 — ad ve birim yazimi metnin neresinde durursa dursun korunur.
    ///
    /// <para>Tur 2'de <c>Names</c> gecidi yalniz <c>CapitaliseWord</c> icindeydi ve
    /// <c>Sentence</c> onu sadece satir basi sozcugu icin cagiriyordu. Sonuc: baslik
    /// kolundan govde koluna gecen metinlerde (o turda 124, kirk dil eklendikten sonra 784, tagline anahtarlari silinince 778, oynatici 1. dalga anahtarlariyla 844, 3. dalga anahtarlariyla 898, 2. dalga parca anahtarlariyla 950, 4. dalga arac ve gelismis anahtarlariyla 985, 8b kaydedici anahtarlariyla 1078, 8d ses anahtarlariyla 1090, sr'nin yedi ses satiri Kiril'den dosyanin geri kalaniyla ayni Latin yazimina dondurulunce 1091, Kesit A main.language.settings'i dusurunce 1086, Kesit B main.player.title ve main.player.menu'yu dusurunce 1084, Kesit D kaydedicinin otomatik kipine 16 recorder.auto.* anahtari ekleyince 1125, guncelleme rozetinin dort anahtari ve kaydedicinin hedef butcesi eklenince 1135, oynatici kisayollarinin dort menu anahtari 1217'den 1221'e: en bookmarkprev ve tostart, pl bookmarkprev, pt settings-all)
    /// ilk sozcuk disindaki <c>ffmpeg</c>
    /// dil dosyasindaki yazimiyla kaliyordu — <c>en/main.drop.hint</c>,
    /// <c>en|tr/main.reason.encoder-fallback-not-in-build</c>. Bu olcu butun dillerin butun anahtarlarinin (bugun 38313 kalem, sayim BaslikKapsamiTests'te)
    /// <b>tamamini</b> gezer, tek bir kalemi bile atlamaz.</para>
    /// <para>Sayim yansimanin gordugu 43 dil uzerinden: 42 dil klasoru ve gomulu kaynak
    /// adindan gelen bir dil daha, her biri 692 anahtar. 5. dalga ffmpeg oynatma borusunu
    /// cop kutusuna tasiyinca okuyucusu kalmayan dokuz <c>playback.pipe.*</c> /
    /// <c>playback.source.*</c> anahtari kataloglardan cikti: 43 x 486 = 20898'den
    /// 43 x 477 = 20511'e. 1. dalga 32 oynatici anahtari ekledi: 43 x 509 = 21887. 6. dalga Unix kapamasi
    /// <c>main.instance.waiting</c> ekledi: 43 x 510 = 21930. 3. dalga 32 anahtar daha ekledi: 43 x 542 = 23306.
    /// 2. dalga 38 altyazi ve ses parcasi anahtari ekledi: 43 x 580 = 24940.
    /// 4. dalga 39 gelismis ve 12 arac anahtari ekledi: 43 x 631 = 27133.
    /// 7. dalga fare isi sol tik jestini ve menunun ayarlar satirini ekledi: 43 x 633 = 27219. ui-kilavuz sozlesmesi
    /// disabled-affordance icin 4 (main.action.cancel/shrink/convert.disabled-tip, settings.share.delete.disabled-tip)
    /// ve unnamed-interactive/component-without-motion icin 4 (playback.panel.maximize/fullscreen, playback.zoom.out/in)
    /// anahtar ekledi: 43 x 641 = 27563. main.sponsor.label denendi, BrandSpellingTests ile cakisti, geri alindi.
    /// 8. dalga 8b kolu kaydedici sekmesini ekledi: 43 recorder anahtari ve sekme basligi
    /// main.tab.recorder, 43 x 685 = 29455. 8d kolu ses girdisini motora baglayinca arayuze
    /// secim yuzeyi girdi: recorder.audio.* alti anahtar ve recorder.error.audio,
    /// 43 x 692 = 29756. 0.4.4 yeni surum uyarisina Yukle dugmesini ekledi (main.action.install,
    /// 43 x 693 = 29799); ayni turda Ayarlar seride kendi sekmesi olunca baslik cubugundaki
    /// tekerlek dustu ve main.language.settings sahipsiz kaldi, silindi: 43 x 692 = 29756.
    /// Kesit B oynatici basligini kaldirdi (baslik yazisi, parca dugmeleri, uc nokta);
    /// main.player.title ve main.player.menu sahipsiz kaldi, silindi: 43 x 690 = 29670.
    /// Kesit D kaydediciye otomatik kipi ekledi: recorder.auto.* on alti anahtar
    /// (kutu, ipucu, olcum dugmesi, ozet ve sekiz gerekce satiri), 43 x 706 = 30358.
    /// Guncelleme rozeti dort main.update.* anahtari ekledi: 43 x 710 = 30530. Hedef boyut butcesi
    /// dokuz recorder.budget.* / recorder.auto.manual anahtari ekledi, sahipsiz kalan recorder.auto.enable
    /// ve recorder.auto.hint dustu: 43 x 717 = 30831. Iki adimli guncelleme indirme dugmesini, rozet/durum ve dort gunluk satirini ekledi
    /// (main.action.download, main.update.badge/downloading/ready/failed, main.update.log.*): 43 x 732 = 31476'dan 43 x 741 = 31863'e.
    /// Tasma karari dokuz main.retry.* / main.run.* anahtari ekledi (kabul, kesme seridi, iki sonuc satiri): 43 x 742 = 31906'dan 43 x 751 = 32293'e.
    /// Hakkinda'nin platform satirlari iki main.about.platforms.* anahtari ekledi: 43 x 753 = 32379.
    /// Oynatici kisayollari dort main.player.menu.* anahtari ekledi (settings-all, bookmarkprev, stop, tostart): 43 x 757 = 32551.
    /// HandBrake 1c dalgasi main.advanced.keep-tracks.label'i ekledi: 43 x 742 = 31906'dan 43 x 743 = 31949'a;
    /// iz kararlarinin sekiz gerekce notu (main.reason.stream.*) 43 x 751 = 32293'e, kol degistiren toplami 1218'den 1260'a (en 139'dan 143'e, tr 52'den 56'ya). Birlesik: gezilen 33067, toplam 1295 (en 152, tr 58).
    /// Paket 2 kaydedicisi 80 recorder.* anahtari ekledi (Basit/Gelismis, geri sayim, cerceve, tepsi, kisayol, girdi gosterimi, webcam, buyutec ve gelismis panelin on kolu): 43 x 80 = 3440, gezilen 36507; kol degistiren toplami 1406 (en 160, tr 59).
    /// Paket 2b 23 anahtar ekledi (kayit odagi, bosluk kirpma, canli onizleme, kayit tamponu: iki main.*, yirmi bir recorder.*): 43 x 23 = 989, gezilen 37496; kol degistiren toplami 1440 (en 165, tr 59). Karanlik gecis main.reason.dark-content-hevc gerekce notunu ekledi: 43 x 1 = 43, gezilen 37539; kol degistiren toplami 1441 (en 166, tr 59). Kaydedici pencere secicisi iki recorder.error.* anahtari ekledi (window-wayland, window-missing): 43 x 2 = 86, gezilen 37625; kol degistiren toplami degismedi. HandBrake A2 on ayar kutuphanesi 15 main.preset.* anahtari ekledi: 43 x 15 = 645, gezilen 38270; kol degistiren toplami 1471 (en 169, tr 60). Yol D main.update.maintenance-failed bakim hatasi cumlesini ekledi: 43 x 1 = 43, gezilen 38313; kol degistiren toplami 1480 (en 170, tr 61; cs, es, hu, nl, pt, ro, sk birer).</para>
    /// <para>Bu daldaki (<c>t0/yol-b-kabuk</c>) son adim, <c>origin/main</c> (<c>9c4b9907</c>) dala
    /// birlesince olculdu. Dal S9'un on anahtarini ekledi (<c>main.unit.mb</c>, <c>mb-value</c>,
    /// <c>mb-range</c>, <c>kbps-value</c>, <c>fps-value</c>, <c>k-value</c>, <c>score-suffix</c>,
    /// <c>score-value</c>, <c>main.plan.ai</c>, <c>main.plan.mode.crf-value</c>) ve P3
    /// <c>main.player.menu.settings-all</c>'i dusurdu; dalin tepesinde (<c>006c8702</c>)
    /// <c>main.json</c> 500 anahtarli. <c>origin/main</c>'de 492 (HandBrake A2'nin 15
    /// <c>main.preset.*</c> anahtari ve Yol D'nin <c>main.update.maintenance-failed</c>'i dahil).
    /// Birlesik katalog 501; 42 dil dosyasinin hepsi 501'de esit, tek anahtar dusmedi.
    /// <c>origin/main</c>'in pinine gore fark dil basina 501 - 492 = 9 anahtar:
    /// 43 x 9 = 387, gezilen 38313 + 387 = 38700. Kol degistiren toplam: <c>origin/main</c>'in
    /// 1480'i uzerine dalin iki kolu eklenir (<c>de</c> ve <c>nb</c> <c>main.plan.ai</c> = "KI",
    /// dokumde olculdu) ve dusen <c>pt main.player.menu.settings-all</c> bir kol goturur:
    /// 1480 + 2 - 1 = 1481. Diller 43, dil dosyasi klasoru 42: <c>zh-Hans</c> ve <c>zh_Hans</c>
    /// ayni dosyayi iki adla gezer.</para>
    /// <para>Metin duzeltmeleri dali (<c>t0/metin-duzeltmeleri</c>) tek anahtar ekledi:
    /// <c>main.run.whatsapp-document</c>, WhatsApp hedefli cikti bitince sonuc cumlesinin
    /// arkasina eklenen "belge olarak gonder" ipucu. 42 dil dosyasinin hepsine girdi,
    /// dusen anahtar yok: 43 x 501 = 21543 degil, bu testin saydigi <b>butun</b> katalog
    /// kalemleri 38700 + 43 x 1 = 38743. Ayni dal <c>main.codec.smallest</c>'in
    /// <c>H.265</c> yazan govdesini <c>AV1</c>'e cevirdi; anahtar sayisini degistirmedi ve
    /// <c>AV1</c> zaten <c>LanguageCatalog.Names</c>'de bildirildigi icin <c>kayip</c> 0
    /// kaldi. Kol degistiren toplam da degismedi (1481, en 170, tr 61): eklenen cumleler
    /// nokta tasidigi icin eski kurala gore zaten govde sayiliyor.</para>
    /// <para>Ayni dalin denetim borclari <b>ikinci</b> anahtari ekledi:
    /// <c>main.codec.tip</c>, kucultme seridine asilan uyumluluk balonu. Metni uydurulmadi,
    /// 42 dilde <c>main.convert.video-codec.tip</c>'in H.264 ve AV1 maddelerinden kesildi.
    /// Yine 42 dosyanin hepsine girdi, dusen yok: 38743 + 43 x 1 = 38786. Ayni borc
    /// <c>main.advice.codec-upgrade</c>'in iki <c>H.265</c> gecisini <c>AV1</c>'e cevirdi —
    /// anahtar sayisini degistirmez, <c>kayip</c> 0 kalir, kol degistiren toplam 1481 /
    /// en 170 / tr 61 yerinde durur.</para>
    /// <para>P28 (<c>t0/p28-altyazi</c>) iki turda anahtar ekledi. Ilk tur on iki
    /// <c>player.subtitle.download*</c> ve uc <c>settings-tab.opensubtitles.*</c> anahtari
    /// (15); Yol A ikinci turda on <c>settings-tab.opensubtitles.*</c> oturum alani ve uc
    /// oturuma bagli hata kolu (<c>badlogin</c>, <c>expired</c>, <c>toofast.wait</c>) ekledi (13).
    /// Sayi dalin tabanindan (<c>d0aea4d2</c>) turetildi, olcunun fiili ciktisindan degil:
    /// taban dil basina 900 anahtar, dalin tepesi 928, dusen yok: 928 - 900 = 28,
    /// 43 x 28 = 1204, gezilen 38700 + 1204 = 39904.</para>
    /// <para><b>Birlesme sonrasi pim 39990.</b> Dalin pini 39904 kendi tabanina (38700)
    /// gore dogruydu; main ayni surede dil basina iki anahtar ekledi (43 x 2 = 86).
    /// Dil basina 900 + 2 + 28 = 930, 43 x 930 = 39990 (= 38786 + 1204 = 39904 + 86).
    /// Sayi elle hesaplanip yazilmadi: cozum bu degerle konuldu ve olcu yesil dondu,
    /// yani <c>SAYIM gezilen</c> satirinin kendisi 39990 raporladi.</para>
    /// <para>Kol degistiren toplam bu yirmi sekizin <c>Title</c> altinda kol degistiren
    /// kismindan gelir; dokumun KOL satirlari anahtar kumesi basina ve dil basina ayri sayildi.
    /// Ilk turun on besi: en 11, pt 7, es 6, hu 5, nl 5, ro 5, fr 4, nb 4, da 3, et 3, it 3,
    /// de 2, sk 2, sw 2, tr 2, cs 1, fi 1, pl 1 = 67. Ikinci turun on ucu: en 8, es 5,
    /// fr 4, hu 4, pt 4, pl 3, ro 3, cs 2, da 2, it 2, nb 2, nl 2, sk 2, sv 2, sw 2,
    /// de 1, et 1, tr 1 = 50. Toplam 1481 + 67 + 50 = 1598, en 170 + 11 + 8 = 189,
    /// tr 61 + 2 + 1 = 64. Ceviriler kucuk harfle basladigi icin ikinci turda da 43 dilin
    /// 18'i kol degistiriyor; ayni sayi, ayni kume degil: <c>fi</c> ikinci turda kol
    /// degistirmiyor, <c>sv</c> degistiriyor.</para>
    /// <para><b>18 Eylul 2026 pimi 40592.</b> On ayar kutuphanesi turu dil basina on dort
    /// anahtar ekledi: 40592 - 39990 = 602, 602 / 43 = 14, yani dil basina 930 + 14 = 944.
    /// Sayi yine olcunun kendi <c>SAYIM gezilen</c> satirindan alindi; gezilen metin
    /// uzunluguna degil anahtar sayisina bagli oldugu icin ceviri kisaltmalari bu sayiyi
    /// oynatmaz.</para>
    /// <para><b>19 Eylul 2026 pimi 42140.</b> Paylasim hatalarinin dili dil basina otuz yedi
    /// anahtar ekledi, bit hizi biriminin birlestirilmesi <c>main.unit.k-value</c>'yu dusurdu:
    /// dil basina 944 + 37 - 1 = 980, 43 x 980 = 42140. Sayi yine olcunun kendi
    /// <c>SAYIM gezilen</c> satirindan alindi, <c>kayip</c> 0 kaldi.</para>
    /// <para><b>19 Eylul 2026, ikinci yenileme: 42570.</b> Ayni iki tur dil basina on anahtar
    /// ekledi: 980 + 10 = 990, 43 x 990 = 42570. <c>kayip</c> yine 0.</para>
    ///
    /// <para><b>19 Eylul 2026, ucuncu yenileme: 42785.</b> Tani gunlugu dil basina bes anahtar
    /// ekledi: 990 + 5 = 995, 43 x 995 = 42785. <c>kayip</c> yine 0.</para>
    ///
    /// <para><b>19 Eylul 2026, dorduncu yenileme: 42828.</b> Baslik secici (E4) dil basina tek
    /// <c>main.title.label</c> anahtari ekledi: 995 + 1 = 996, 43 x 996 = 42828. <c>kayip</c> yine 0.</para>
    /// <para><b>19 Eylul 2026, besinci yenileme: 42957.</b> Suzgec yuzeyi (E8) dil basina uc
    /// <c>main.advanced.filters.*</c> anahtari ekledi: 996 + 3 = 999, 43 x 999 = 42957. <c>kayip</c> yine 0.</para>
    /// <para><b>19 Eylul 2026, altinci yenileme: 43129.</b> Paylasimin yeniden deneme yuzu
    /// uc <c>settings.share.retry*</c> anahtari, onizleme rozeti bir <c>main.preview.temsili</c>
    /// ekledi: 999 + 4 = 1003, 43 x 1003 = 43129. <c>kayip</c> yine 0.</para>
    /// <para><b>19 Eylul 2026, yedinci yenileme: 43387.</b> B1a ses kodegi kolu dort
    /// (<c>audio-codec.label</c>, <c>audio-channels.source</c>, iki <c>dolby-*</c> gerekcesi), tasma
    /// dugmeleri iki <c>main.retry.*.name</c> anahtari ekledi: 1003 + 6 = 1009, 43 x 1009 = 43387. <c>kayip</c> yine 0.</para>
    /// <para><b>22 Eylul 2026, sekizinci yenileme: 45881.</b> C1-1..C1-6 turu 58 anahtar ekledi:
    /// 43 x 1067 = 45881. <c>kayip</c> 0.</para>
    /// <para><b>23 Eylul 2026, dokuzuncu yenileme: 46010.</b> B4 dil basina bir
    /// <c>main.reason.hdr-dynamic-dropped</c>, B3 dil basina iki <c>main.reason.stream.webm-*</c>
    /// anahtari ekledi: dil basina 1067 + 3 = 1070, 43 x 1070 = 46010. <c>kayip</c> yine 0.</para>
    /// <para>2026-09-23: <c>main.reason.vp9-crf-unmeasured</c> dil basina 1070 + 1 = 1071,
    /// 43 x 1071 = 46053.</para>
    /// <para>2026-09-23: otomatik kirpma (HandBrake #36) dil basina <c>main.advanced.filters.autocrop</c>
    /// ve <c>main.reason.auto-crop</c>: 1071 + 2 = 1073, 43 x 1073 = 46139. <c>kayip</c> yine 0.</para>
    /// <para>2026-09-23: HDR10+ koprusu dil basina bes anahtar
    /// (<c>main.reason.hdr10plus-routed-x265</c>, <c>-svtav1</c>, <c>-cut</c>,
    /// <c>main.stage.hdr10plus-metadata</c>, <c>main.run.hdr10plus-short</c>): 1073 + 5 = 1078,
    /// 43 x 1078 = 46354.</para>
    /// <para>2026-09-23: tek gecis etiketi dil basina yedi anahtar (<c>main.plan.mode.single-pass</c>,
    /// <c>main.estimate.mode.enforced-single-pass</c>, <c>main.estimate.mode.copy</c>, uc
    /// <c>main.reason.*-single-pass</c>, <c>main.advice.single-pass</c>): 1078 + 7 = 1085,
    /// 43 x 1085 = 46655. <c>kayip</c> yine 0.</para>
    /// <para>2026-09-23: Media Foundation dil basina bir anahtar (<c>main.reason.no-quality-scale</c>):
    /// 1085 + 1 = 1086, 43 x 1086 = 46698.</para>
    /// <para>2026-09-24, yalan yok: dil basina <c>settings.share.link-copied</c>: 1086 + 1 = 1087, 43 x 1087 = 46741.</para>
    /// <para>2026-09-24: soylenen ile yapilan dil basina uc anahtar (<c>main.advice.encoder-fallback-hardware</c>,
    /// <c>main.reason.stream.extra-audio-dropped-by-container</c>, <c>main.reason.stream.vp9-fell-back-to-mp4</c>):
    /// ustune yalan yok CLI anahtari: 1087 + 3 = 1090, 43 x 1090 = 46870.</para>
    /// <para>2026-09-24: kaydedici durustlugu dil basina yedi anahtar (<c>recorder.discarded-kept</c>,
    /// <c>recorder.output.done-partial</c>, <c>-done-failed</c>, <c>-partial-unverified</c>, <c>-gif-failed</c>,
    /// <c>-not-moved</c>, <c>recorder.replay.saved-mkv</c>): 1086 + 7 = 1093, 43 x 1093 = 46999; ustteki iki satirla birlikte 1090 + 7 = 1097, 43 x 1097 = 47171.</para>
    /// <para>2026-09-24, yalan yok kalan uclar: dil basina uc anahtar (<c>main.reason.stream.image-subtitle-dropped-by-container</c>,
    /// <c>main.reason.stream.vp9-fell-back</c>, <c>recorder.output.partial-broken-mkv</c>): 1097 + 3 = 1100, 43 x 1100 = 47300.</para>
    /// <para>2026-09-25, bolge duzenleyici: dil basina dort anahtar (<c>recorder.region.title</c>, <c>-start</c>, <c>-settings</c>,
    /// <c>-close</c>): 1100 + 4 = 1104, 43 x 1104 = 47472.</para>
    /// <para>2026-09-25, mini serit: dil basina iki anahtar (<c>recorder.mini.offer</c>, <c>recorder.mini.region</c>):
    /// 1104 + 2 = 1106, 43 x 1106 = 47558.</para>
    /// <para>2026-09-25, libmpv yukleme hatasi: dil basina <c>main.player.engine.loadfailed</c>: 1106 + 1 = 1107, 43 x 1107 = 47601.</para>
    /// <para>2026-09-25, Indir ve Yukle: dil basina <c>main.action.downloadinstall</c>: 1107 + 1 = 1108, 43 x 1108 = 47644.</para>
    /// <para>2026-09-26, paylasim QR'i: dil basina iki anahtar (<c>main.share.qr</c>, <c>main.share.qr-hint</c>): 1108 + 2 = 1110, 43 x 1110 = 47730.</para>
    /// <para>2026-09-26, kapanista guncelleme: dil basina <c>main.update.ready-on-exit</c>: 1110 + 1 = 1111, 43 x 1111 = 47773.</para>
    /// <para>2026-09-27, oynatici sag menusu: dil basina dort anahtar (<c>player.menu.reveal</c>, <c>-view</c>, <c>-playback</c>,
    /// <c>-loop</c>): pimli 47816 = 43 x 1112 uzerine 1112 + 4 = 1116, 43 x 1116 = 47988.</para>
    /// <para>2026-09-27, duzenleyici D3: <c>main.tab.editor</c> ve on dort <c>editor.*</c> anahtari: 1116 + 15 = 1131, 43 x 1131 = 48633.</para>
    /// <para>2026-09-28, duzenleyici D5: yedi <c>editor.*</c> anahtari (mekik, isaretci, tumunu sec, sona git, kisayol listesi): 1131 + 7 = 1138, 43 x 1138 = 48934.</para>
    /// <para>2026-09-28, duzenleyici D4: on <c>editor.export.*</c> anahtari: 1131 + 10 = 1141, 43 x 1141 = 49063.</para>
    /// <para>2026-09-28, D4 ile D5 birlikte: 1131 + 7 + 10 = 1148, 43 x 1148 = 49364.</para>
    /// <para>2026-09-28, duzenle/kaydet/paylas: <c>editor.export</c> gitti, <c>editor.save</c>, <c>editor.save-as</c>,
    /// <c>player.menu.edit</c> ve <c>recorder.output.to-editor</c> geldi: 1148 - 1 + 4 = 1151, 43 x 1151 = 49493.</para>
    /// <para>2026-09-28, bolge dali ile birlikte: 1151 + 2 = 1153, 43 x 1153 = 49579.</para>
    /// <para>2026-09-29, arayuz turu: <c>recorder.advanced.encoder-title</c>: 1153 + 1 = 1154, 43 x 1154 = 49622.</para>
    /// <para>2026-09-29, Tümünü Ata: <c>settings.default-app.all</c> ve <c>settings.default-app.all-copied</c>: 1154 + 2 = 1156, 43 x 1156 = 49708.</para>
    /// <para>2026-09-29, kaydedici sihirbazi: 21 yeni anahtar: 1156 + 21 = 1177, 43 x 1177 = 50611.</para>
    /// <para>2026-09-30, duzenleyici Premiere uyumu: on sekiz yeni <c>editor.*</c> anahtari, dusen ve degisen yok: 1177 + 18 = 1195, 43 x 1195 = 51385.</para>
    /// <para>2026-09-30, oynatma listesi oge menusu: bes yeni <c>player.list.*</c> anahtari: 1195 + 5 = 1200, 43 x 1200 = 51600.</para>
    /// <para>2026-09-30, oynatici alt seridi: uc yeni anahtar: 1200 + 3 = 1203, 43 x 1203 = 51729.</para>
    /// <para>2026-09-30, oynatici kurtarma: bir yeni anahtar (<c>main.player.recoveryfailed</c>): 1203 + 1 = 1204, 43 x 1204 = 51772.</para>
    /// <para>2026-09-30, kisayol atama: yedi yeni anahtar (bes shortcuts.*, iki main.player.input.*): 1203 + 7 = 1210, 43 x 1210 = 52030.</para>
    /// <para>2026-09-30, kurtarma ile kisayol atama birlikte: 1203 + 1 + 7 = 1211, 43 x 1211 = 52073.</para>
    /// <para>2026-09-30, kayit bitince eylemi 14, sag tik hedef yongalari 6, ddagrab dususu 2 yeni anahtar: 1211 + 22 = 1233, 43 x 1233 = 53019.</para>
    /// <para>2026-09-30, metin katmani (dalga 1): alti yeni anahtar (bes <c>editor.text.*</c>, <c>editor.export.text-full</c>), dusen yok: 1233 + 6 = 1239, 43 x 1239 = 53277. <c>kayip</c> 0.</para>
    /// <para>2026-10-01, klip ozellikleri: on iki yeni anahtar (on bir <c>editor.clip.*</c>, <c>editor.export.effects-full</c>), dusen yok: 1239 + 12 = 1251, 43 x 1251 = 53793. <c>kayip</c> 0.</para>
    /// <para>2026-10-01, sessizlik kesme: on bes yeni anahtar (<c>editor.silence.*</c>), dusen yok: 1251 + 15 = 1266, 43 x 1266 = 54438. <c>kayip</c> 0.</para>
    /// <para>2026-10-02, oynatici hiz ibresi, OSD ve medya adi: yedi yeni anahtar (<c>main.player.speed.fast</c>, <c>main.player.speed.slow</c>, <c>main.player.osd.speed</c>, <c>main.player.osd.volume</c>, <c>main.player.osd.muted</c>, <c>main.player.osd.mutedvolume</c>, <c>player.title.copy-name</c>), dusen yok: 1266 + 7 = 1273, 43 x 1273 = 54739. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, oynatici bolum atlama: iki yeni anahtar (<c>main.player.menu.chapternext</c>, <c>main.player.menu.chapterprev</c>), dusen yok: 1273 + 2 = 1275, 43 x 1275 = 54825. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, kuyruk izleme klasoru: bes yeni anahtar (<c>main.shrink-job.watch</c>, <c>.choose</c>, <c>.none</c>, <c>.hint</c>, <c>.lost</c>), dusen yok: 1275 + 5 = 1280, 43 x 1280 = 55040. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, kaydedici kisayol atama: sekiz yeni anahtar (<c>recorder.hotkeys.title</c>, <c>-frame</c>, <c>-discard</c>, <c>-replay-save</c>, <c>-reset</c>, <c>-duplicate</c>, <c>-rejected</c>, <c>-unsupported</c>), dusen yok: 1273 + 8 = 1281, 43 x 1281 = 55083. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, uc is birlikte: 1273 + 2 + 5 + 8 = 1288, 43 x 1288 = 55384.</para>
    /// <para>2026-10-05, duzenleyici proje kaydi: yedi yeni anahtar (<c>editor.project.*</c>), dusen yok: 1288 + 7 = 1295, 43 x 1295 = 55685. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, oynatici donanim cozme: bir yeni anahtar (<c>player.advanced.hardware</c>), dusen yok. Proje kaydiyla birlikte: 1295 + 1 = 1296, 43 x 1296 = 55728. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, kareyi panoya kopyalama ve JPG ekran goruntusu: dort yeni anahtar (<c>player.view.screenshot-copy</c>, <c>-copied</c>, <c>-copy-failed</c>, <c>player.view.screenshot-jpg</c>), dusen yok: 1288 + 4 = 1292, 43 x 1292 = 55556. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, proje kaydi, donanim cozme ve kare panosu birlikte: 1288 + 7 + 1 + 4 = 1300, 43 x 1300 = 55900.</para>
    /// <para>2026-10-05, ikincil altyazi: bir yeni anahtar (<c>player.subtitle.secondary</c>), dusen yok. Donanim cozmeyle birlikte: 1296 + 1 = 1297, 43 x 1297 = 55771. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, kare panosu ve ikincil altyazi birlikte: 1300 + 1 = 1301, 43 x 1301 = 55943.</para>
    /// <para>2026-10-05, oynatici liste kaydi: dort yeni anahtar (<c>player.list.save</c>, <c>-saved</c>, <c>-save-failed</c>, <c>-files</c>), dusen yok. Kare panosuyla birlikte: 1300 + 4 = 1304, 43 x 1304 = 56072. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, ikincil altyazi ve liste kaydi birlikte: 1301 + 4 = 1305, 43 x 1305 = 56115.</para>
    /// <para>2026-10-05, is bitince haber: alti yeni anahtar (<c>main.notify.*</c> dort, <c>settings-tab.notify-done.*</c> iki), dusen yok. Liste kaydiyla birlikte: 1305 + 6 = 1311, 43 x 1311 = 56373. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, oynatici bilgi panelinde HDR alani ve canli istatistik: dokuz yeni anahtar (<c>player.info.range</c>, <c>.color</c>, <c>.decoder</c>, <c>.software</c>, <c>.hardware</c>, <c>.dropped</c>, <c>.decoderdropped</c>, <c>.livebitrate</c>, <c>.livefps</c>), dusen yok: 1300 + 9 = 1309, 43 x 1309 = 56287. <c>kayip</c> 0.</para>
    /// <para>2026-10-05, liste kaydi ve bilgi paneli birlikte: 1305 + 9 = 1314, 43 x 1314 = 56502.</para>
    /// <para>2026-10-05, liste kaydi, bilgi paneli ve is bitince haber birlikte: 1314 + 6 = 1320, 43 x 1320 = 56760.</para>
    /// <para>2026-10-05, meta veriyi sil kutusu: iki yeni anahtar (<c>main.advanced.drop-metadata.label</c>, <c>-tip</c>), dusen yok: 1320 + 2 = 1322, 43 x 1322 = 56846. <c>kayip</c> 0.</para>
    /// <para>2026-10-06, parcalari ayri dosyalara yazma: uc yeni anahtar (<c>editor.export.separate</c>, <c>.segments-done</c>, <c>.segment-failed</c>), dusen yok. Meta veri kutusuyla birlikte: 1322 + 3 = 1325, 43 x 1325 = 56975. <c>kayip</c> 0.</para>
    /// <para>2026-10-06, sahne degisimlerinden bolme: uc yeni anahtar (<c>editor.scene.split</c>, <c>-none</c>, <c>-done</c>), dusen yok. Parcalari ayri yazmayla birlikte: 1325 + 3 = 1328, 43 x 1328 = 57104. <c>kayip</c> 0.</para>
    /// <para>2026-10-06, kaydedici ses seviyesi gostergesi: uc yeni anahtar (<c>recorder.audio.level.microphone</c>, <c>.system</c>, <c>.hint</c>), dusen yok. Sahne bolmeyle birlikte: 1328 + 3 = 1331, 43 x 1331 = 57233. <c>kayip</c> 0.</para>
    /// <para>2026-10-06, kayitta bolum isareti: bes yeni anahtar (<c>recorder.hotkeys.chapter</c>, <c>recorder.chapter.marked</c>, <c>.title</c>, <c>.unsupported</c>, <c>.failed</c>), dusen yok. Ses gostergesiyle birlikte: 1331 + 5 = 1336, 43 x 1336 = 57448. <c>kayip</c> 0.</para>
    /// <para>2026-10-07, kapak notu: tek yeni anahtar (<c>main.reason.stream.cover-dropped</c>), dusen yok: 1336 + 1 = 1337, 43 x 1337 = 57491. <c>kayip</c> 0.</para>
    /// <para>2026-10-07, arayuzde yabanci dil altyazisi taramasi: sekiz yeni anahtar (<c>main.subtitles.scan</c>, <c>.running</c> ve alti sonuc), dusen yok: 1337 + 8 = 1345, 43 x 1345 = 57835. <c>kayip</c> 0.</para>
    /// <para>2026-10-07, WebP bit hizi notu: tek yeni anahtar (<c>main.convert.note.webp-bitrate-ignored</c>), dusen yok: 1345 + 1 = 1346, 43 x 1346 = 57878. <c>kayip</c> 0.</para>
    /// <para>2026-10-07, Donustur ve duzenleyicide meta veriyi sil, kare hizi kipi gerekcesi: uc yeni anahtar (<c>main.convert.drop-metadata.tip</c>, <c>main.reason.frame-rate-constant</c>, <c>main.reason.frame-rate-peak</c>), dusen yok: 1346 + 3 = 1349, 43 x 1349 = 58007. <c>kayip</c> 0.</para>
    /// <para>2026-10-07, akilli kesimde ses nedeni: tek yeni anahtar (<c>editor.export.audio-full</c>), dusen yok: 1349 + 1 = 1350, 43 x 1350 = 58050. <c>kayip</c> 0.</para>
    /// <para>2026-10-10, oynaticida adres: uc yeni anahtar (<c>player.tools.paste</c>, <c>player.tools.paste-none</c>, <c>player.address.unsupported</c>), dusen yok: 1350 + 3 = 1353, 43 x 1353 = 58179. <c>kayip</c> 0.</para>
    /// <para>2026-10-10, K6 eszamanli kodlama: uc yeni anahtar (<c>main.shrink-job.running</c>, <c>settings-tab.parallel-jobs.label</c>, <c>settings-tab.parallel-jobs.hint</c>), dusen yok: 1353 + 3 = 1356, 43 x 1356 = 58308. <c>kayip</c> 0.</para>
    /// <para>2026-10-10, K2 sistem sesi yakalama: uc yeni anahtar (<c>recorder.audio.loopback</c>, <c>.switched</c>, <c>.lost</c>), dusen yok. Onceki pim 58437 = 43 x 1359 idi: 1359 + 3 = 1362, 43 x 1362 = 58566. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, hizli kip kayma bildirimi: dort yeni anahtar (<c>editor.key.prev-keyframe</c>, <c>next-keyframe</c>, <c>editor.on-keyframe</c>, <c>editor.export.keyframe-lead</c>), dusen yok: 1362 + 4 = 1366, 43 x 1366 = 58738. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, kodlama sureci onceligi (SIK-P1): yedi yeni anahtar (<c>settings-tab.encode-priority.label</c>, <c>.hint</c>, <c>.normal</c>, <c>.below-normal</c>, <c>.low</c>, <c>settings-tab.encoder-threads.label</c>, <c>.hint</c>), dusen yok: 1366 + 7 = 1373, 43 x 1373 = 59039. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, dalga 1 birlesmesi: OYA-P1, OYB-P2 ve DZB-P1 on iki anahtari pinsiz getirdi; birlesik agacta olculdu: 1373 + 12 = 1385, 43 x 1385 = 59555. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, ARC-P1 gurultu temizleme ve yukseklik olcumu: bes yeni anahtar (<c>main.audio.denoise</c>, <c>main.audio.loudness.measure</c>, <c>.running</c>, <c>.result</c>, <c>.failed</c>), dusen yok; dalga 1 birlesik agacinda 1385 + 5 = 1390, 43 x 1390 = 59770. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, gecmis penceresi ve devam ayari (OYB-P4): dokuz yeni anahtar (<c>player.list.history</c>, <c>.clear-history</c>, <c>.history-cleared</c>, <c>player.history.title</c>, <c>.search</c>, <c>.open</c>, <c>.empty</c>, <c>player.view.keep-history</c>, <c>.resume</c>), dusen yok: 1390 + 9 = 1399, 43 x 1399 = 60157. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, adli Donustur on ayari (DON-P5): uc yeni anahtar (<c>main.convert.preset.label</c>, <c>.placeholder</c>, <c>.add.tip</c>), dusen yok: 1390 + 3 = 1393, 43 x 1393 = 59899. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, AYR-P2 ayar yedegi: on yeni <c>settings.*</c> anahtari, dusen yok; 1390 + 10 = 1400, 43 x 1400 = 60200. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, yakilan altyazida bicim secimi (SIK-P5): dort yeni anahtar (<c>main.subtitles.style.font</c>, <c>.size</c>, <c>.box</c>, <c>.default</c>), dusen yok; 1390 + 4 = 1394, 43 x 1394 = 59942. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, OYA-P2 altyazi satiri ve tuslari: dokuz yeni <c>player.subtitle.*</c> anahtari (visibility, lineprev, linenext, stepprev, stepnext, copy, hidden, shown, copied), dusen yok; 1390 + 9 = 1399, 43 x 1399 = 60157. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, DZA-P2 otomatik bolme ve parca adlari: on uc yeni <c>editor.*</c> anahtari (<c>editor.split.*</c> sekiz, <c>editor.scene.threshold</c>, <c>editor.clip.label</c>, <c>editor.marker.name</c>, <c>editor.export.part-name</c>, <c>.hint</c>), dusen yok; 1390 + 13 = 1403, 43 x 1403 = 60329. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, kosan kodlamayi duraklatma (SIK-P3): uc yeni anahtar (<c>main.action.pause-run</c>, <c>main.action.resume-run</c>, <c>main.shrink-job.disk-paused</c>), <c>main.shrink-job.paused</c> yeniden yazildi, dusen yok: 1390 + 3 = 1393, 43 x 1393 = 59899. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, EKB-P2 imlec vurgusu ve spot isigi: on bir yeni anahtar (<c>recorder.option.cursor-highlight</c>, <c>.spotlight</c>, <c>recorder.pointer.title</c>, <c>.size</c>, <c>.size.small</c>, <c>.size.medium</c>, <c>.size.large</c>, <c>.highlight-color</c>, <c>.left-color</c>, <c>.right-color</c>, <c>.color</c>), dusen yok: 1390 + 11 = 1401, 43 x 1401 = 60243. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, ses cikisi ve cikis secenekleri (OYA-P4): bes yeni anahtar (<c>player.advanced.passthrough</c>, <c>.exclusive</c>, <c>.stereo</c>, <c>player.tracks.output</c>, <c>.output-auto</c>), dusen yok: 1441 + 5 = 1446, 43 x 1446 = 62178. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, OYA-P3 uyku engeli ve pencere davranisi: iki yeni anahtar (<c>player.view.pause-on-minimize</c>, <c>player.view.topmost-while-playing</c>), dusen yok; olculen SAYIM gezilen 62049: 1441 + 2 = 1443, 43 x 1443 = 62049. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, liste penceresinde arama ve siralama (OYB-P3): yedi yeni anahtar (<c>player.list.search</c>, <c>player.list.item.move-up</c>, <c>.move-down</c>, <c>player.list.sort</c>, <c>.sort-name</c>, <c>.sort-date</c>, <c>.sort-reverse</c>), dusen yok; olculen 61963 + 43 x 7 = 62264. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, gizlilik maskesi (EKB-P4): bes yeni <c>recorder.mask.*</c> anahtari (title, draw, draw-hint, clear, count), dusen yok; 43 x 5 = 215, 61963 + 215 = 62178. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, temel renk ve hazir gorunum (DZB-P3): sekiz yeni <c>editor.clip.*</c> anahtari, dusen yok; 61963 + 43 x 8 = 62307. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, dis ses izi (SIK-P4): bes yeni anahtar (<c>main.audio.external.label</c>, <c>.add</c>, <c>.replace</c>, <c>.remove</c>, <c>.file-type</c>), dusen yok; dalga 2 agacinda olculen 61963 + 43 x 5 = 62178. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, Donustur toplu kuyrugu (DON-P6): dokuz yeni <c>main.convert-queue.*</c> anahtari (headline, shared, setting, waiting, running, done, failed, cancelled, summary), dusen yok; birlesik agacta 61963 + 43 x 9 = 62350. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, disa aktarim ayarlari ve on ayarlar (DZB-P2): on dokuz yeni <c>editor.export.*</c> anahtari (preset, preset.custom/1080p/720p/vertical, preset.name, resolution, source, fps, codec, quality, quality.high/balanced/small, audio-bitrate, audio-only, audio-only.tip, audio-only.no-audio, settings-full), dusen yok; olculen 61963 + 43 x 19 = 62780. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, tarama araliklarini tek tek yonetme (DZA-P3): bes yeni <c>editor.silence.*</c> anahtari (<c>padding-start</c>, <c>padding-end</c>, <c>track</c>, <c>applied</c>, <c>hint</c>), <c>editor.silence.padding</c> dustu; 1441 + 5 - 1 = 1445, 43 x 1445 = 62135. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, klip gruplama ve pano (DZB-P4): alti yeni <c>editor.*</c> anahtari (copy, cut, paste, duplicate, group, ungroup), dusen yok; 887cb448 agacinda olculen 65188 + 43 x 6 = 65446. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, medya bilgisi ve denetim raporu (ARC-P2): 56 yeni <c>mediainfo.*</c> anahtari, dusen yok; olculen 65188 + 43 x 56 = 67596. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, kamera bicimi ve konumu (EKB-P5): yedi anahtar dustu (<c>recorder.webcam.size</c>, <c>.width</c>, <c>.corner</c> ve dort kose), bes yeni geldi (<c>recorder.webcam.shape</c>, <c>.shape.rectangle</c>, <c>.shape.rounded</c>, <c>.shape.circle</c>, <c>.mirror</c>); 43 x (5 - 7) = -86, olculen 65188 - 86 = 65102. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, iz secim paneli (DZA-P5): uc yeni anahtar (<c>editor.export.audio-track</c>, <c>.none</c>, <c>editor.monitor.audio-track</c>), dusen yok; taban pimi 65188 + 43 x 3 = 65317. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, acilis iz dili (OYA-P5): iki yeni anahtar (<c>player.view.audio-language</c>, <c>.subtitle-language</c>), dusen yok; olculen 67897 + 43 x 2 = 67983. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, ceviri tamlik isareti (AYR-25): bir yeni anahtar (<c>main.language.unreviewed</c>), dusen yok; 723bb1b8 agacinda olculen 67897 + 43 x 1 = 67940. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, kodlarken uyumama (SIK-P2): iki yeni <c>settings-tab.prevent-sleep.*</c> anahtari (label, hint), dusen yok; 723bb1b8 agacinda olculen 67897 + 43 x 2 = 67983. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, kuyruk saklama (SIK-P9): bir yeni <c>main.shrink-job.restored</c> anahtari, dusen yok; 67983 + 43 = 68026. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, tasinabilir kip (AYR-P8): iki yeni anahtar (<c>main.update.download-page</c>, <c>settings.update.portable-effect</c>), dusen yok; olculen 67897 + 43 x 2 = 67983. <c>kayip</c> 0.</para>
    /// <para>2026-10-14, Donustur'de altyazi izleri (DON-P2): yedi yeni <c>main.convert.subtitles.*</c> anahtari (bolum basligi, <c>.none</c>, <c>.not-carried</c>, <c>.extract</c>, <c>.extracted</c>, <c>.extract-failed</c>, <c>.image-dropped</c>), dusen yok; olculen 65188 + 43 x 7 = 65489. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, tum izleri varsayilan tasima (DZA-P7): yeni anahtar <c>editor.export.audio-track.all</c>, dusen yok; taban pimi 67897 + 43 = 67940. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, capraz gecis teslimi (DZB-P6): sekiz yeni anahtar, dusen yok; taban pimi 67940 + 43 x 8 = 68284. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, muzik izi (DZB-P7): bir yeni anahtar, dusen yok; taban pimi 68284 + 43 = 68327. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, HDR yakalama (EKA-P15): uc yeni anahtar (<c>recorder.advanced.hdr-to-sdr</c>, <c>.hdr-to-sdr-hint</c>, <c>recorder.capture.hdr-lost</c>), dusen yok; taban pimi 67897 + 43 x 3 = 68026. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, tus gosterimi suzgeci (EKB-P1): on bir yeni <c>recorder.keys.*</c> anahtari, dusen yok; taban pimi 68026 + 43 x 11 = 68499.</para>
    /// <para>2026-10-15, kayan sonuc karti (EKB-P14): dort yeni <c>recorder.card.*</c> anahtari, dusen yok; taban pimi 68499 + 43 x 4 = 68671.</para>
    /// <para>2026-10-15, origin/main birlesmesinden sonra (EKB-P14 dali): main tarafinin pimi 68241 ile bu dalin anahtarlari bir arada, dusen yok; olculen gezilen 69015.</para>
    /// <para>2026-10-15, ses dosyasi ve GIF girdisi (ARC-P3): uc yeni anahtar (<c>main.convert.note.gif-target</c>, <c>main.error.audio-only-shrink</c>, <c>main.validation.no-video-container</c>), dusen yok; olculen 69402 + 43 x 3 = 69531. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, cikti tarihi, adi ve dogrulamasi (SIK-P6): alti yeni anahtar (<c>main.shrink-job.failed</c>, <c>.retry</c>, <c>main.run.output-unreadable</c>, <c>.output-short</c>, <c>settings-tab.keep-dates.label</c>, <c>.hint</c>), dusen yok; olculen 70821 + 43 x 6 = 71079. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, bolum yazma (DZA-P8): iki yeni anahtar (<c>editor.export.chapters</c>, <c>.tip</c>), dusen yok; olculen 70821 + 43 x 2 = 70907. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, Donustur ses secenekleri (DON-P9): alti yeni anahtar (ses yuksekligi, kanal, ornekleme hizi ve uc dogrulama iletisi), dusen yok; olculen 70821 + 43 x 6 = 71079. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, Donustur'de goruntu suzgecleri (DON-P10): dokuz yeni anahtar (<c>main.convert.scaler</c> ve uc yontem adi, <c>.fps-mode</c>, <c>.fps-mode.constant</c>, <c>.fps-mode.variable</c>, <c>.letterbox</c>, <c>main.convert.note.filters-reencode</c>), dusen yok; olculen 72068 + 43 x 9 = 72455. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, doldurma kipi (OYA-P11): bir yeni anahtar (<c>player.view.fill</c>), dusen yok; olculen 75422 + 43 = 75465. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, ARC-P4 birlesmesinden sonra: toplam, en ve tr kaymadi; olculen gezilen 75680 + 43 x 2 = 75766.</para>
    /// <para>2026-10-15, kesim listesi (DZA-P10): sekiz yeni <c>editor.cutlist.*</c> anahtari, dusen yok; olculen 75809 + 43 x 8 = 76153. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, metin konumu ve hareketi (later 86): on dort yeni <c>editor.text.*</c> anahtari, dusen yok; olculen 76454 + 43 x 14 = 77056. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, later 86 birlesmesinden sonra: metin konumu anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3721 + 8 = 3729, en 361, tr 119, gezilen 77357 + 602 = 77959.</para>
    /// <para>2026-10-15, later 64 birlesmesinden sonra: kaydedici ses anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3729 + 3 = 3732, en 361, tr 119, gezilen 77959 + 172 = 78131.</para>
    /// <para>2026-10-15, later 61 birlesmesinden sonra: arsiv kodegi ve GIF anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3732 + 1 = 3733, en 361, tr 119 + 1 = 120, gezilen 78131 + 473 = 78604.</para>
    /// <para>2026-10-15, altyazi aktarimi (DZB-62): yedi yeni <c>editor.subtitle.*</c> anahtari, dusen yok; olculen 78604 + 43 x 7 = 78905. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, kap ici iz isleri ikinci yarisi (DZA-25, 26, 44): on uc yeni <c>editor.remux.*</c> anahtari, dusen yok; olculen 78604 + 43 x 13 = 79163. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, later 86 ve later 85 ikinci yari birlesmesinden sonra: iki dalin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3733 + 9 + 18 = 3760, en 361 + 2 = 363, tr 120, gezilen 78604 + 301 + 559 = 79464.</para>
    /// <para>2026-10-15, later 76 birlesmesinden sonra: oncelik ve bitis sesi anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3760 + 5 = 3765, en 363, tr 120, gezilen 79464 + 172 = 79636.</para>
    /// <para>2026-10-15, later 68 birlesmesinden sonra: bas konus anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3765 + 6 = 3771, en 363 + 1 = 364, tr 120, gezilen 79636 + 86 = 79722.</para>
    /// <para>2026-10-15, later 62 birlesmesinden sonra: ek arguman anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3771 + 5 = 3776, en 364, tr 120, gezilen 79722 + 215 = 79937.</para>
    /// <para>2026-10-15, later 67 birlesmesinden sonra: saatli kayit anahtarlarinin kaymasi main'in uzerine toplandi, dusen yok; olculen toplam 3776 + 5 = 3781, en 364, tr 120 + 3 = 123, gezilen 79937 + 516 = 80453.</para>
    /// <para>2026-10-16, later 41 ve 57 birlesmesinden sonra: hizli kirpma metinleri toplam 3781 + 2 = 3783, gezilen 80453 + 387 = 80840; elle kirpma anahtarlari gezilen 80840 + 86 = 80926; en 364, tr 123 degismedi.</para>
    /// <para>2026-10-16, later 44 birlesmesinden sonra: etiket ve onar metinleri toplam 3783 + 2 = 3785, gezilen 80926 + 774 = 81700; en 364, tr 123 degismedi.</para>
    /// <para>2026-10-16, Linux turu birlesmesinden sonra: Wayland metni toplam 3785 + 4 = 3789, gezilen 81700 + 86 = 81786; en 364, tr 123 degismedi.</para>
    /// <para>2026-10-16, toplu teslim (later 40): sekiz yeni anahtar (<c>editor.batch.*</c>) 42 dilde, dusen yok; olculen toplam 3789 + 19 = 3808, en 364 + 3 = 367, tr 123 + 1 = 124, gezilen 81786 + 344 = 82130.</para>
    /// <para>2026-10-16, kayit gecmisi (later 41): alti yeni anahtar (<c>recorder.history.*</c>) 42 dilde, dusen yok; olculen toplam 3808 + 10 = 3818, en 367 + 1 = 368, tr 124 degismedi, gezilen 82130 + 258 = 82388.</para>
    /// <para>2026-10-16, ekrana cizim (later 42): iki yeni anahtar (<c>recorder.hotkeys.draw</c>, <c>recorder.sketch.tools</c>) 42 dilde, dusen yok; olculen toplam 3818 + 3 = 3821, en 368 + 1 = 369, tr 124 degismedi, gezilen 82388 + 86 = 82474.</para>
    /// <para>2026-10-16, prompter (later 42 adim 2): on iki yeni anahtar (<c>recorder.prompter.*</c>) 42 dilde, dusen yok; olculen toplam 3821 + 47 = 3868, en 369 + 3 = 372, tr 124 + 2 = 126, gezilen 82474 + 516 = 82990.</para>
    /// <para>2026-10-16, bolum listesi (later 44): on bir yeni anahtar (<c>main.convert.chapters*</c>) 42 dilde, dusen yok; olculen toplam 3868 + 2 = 3870, en 372, tr 126 degismedi, gezilen 82990 + 473 = 83463.</para>
    /// <para>2026-10-18, muzik izi (later 103): sekiz yeni anahtar (<c>editor.music.*</c>) 42 dilde, dusen yok; etiket 6 dilde 13 kola girdi (en 1, es 3, fr 2, hu 1, pl 1, pt 5): olculen toplam 3888 + 13 = 3901, en 373 + 1 = 374, tr 126 degismedi, gezilen 84280 + 8 x 43 = 84624. <c>kayip</c> 0.</para>
    /// <para>2026-10-17, kaydedici HDR rengi: alti yeni anahtar (<c>recorder.capture.fallback-hdr</c>, <c>hdr-no-tonemap</c>, <c>sdr-white-assumed</c>, <c>hdr-no-hevc</c>, <c>hdr-kept-hevc</c>, <c>hdr-kept</c>) 42 dilde, dusen yok; olculen toplam 3874 + 3 = 3877, tepsi turuyla birlesince 3885 + 3 = 3888 (th ve ur <c>hdr-kept-hevc</c>, ur <c>recorder.advanced.hdr-to-sdr-hint</c>), en ve tr degismedi, gezilen 83678 + 258 = 83936, tepsi turuyla birlesince 83936 + 258 = 84194, <c>kayip</c> 0.</para>
    /// <para>2026-10-16, atanmamis tus ve prompter gorunumu: bes yeni anahtar (<c>recorder.hotkeys.unassigned</c>, <c>recorder.prompter.smaller-text</c>/<c>larger-text</c>/<c>mirror</c>/<c>line</c>) 42 dilde, dusen yok; olculen toplam 3870 + 4 = 3874, en 372, tr 126 degismedi, gezilen 83463 + 215 = 83678.</para>
    /// <para>2026-10-17, tepsiye kucultme (later 120): alti yeni anahtar (<c>settings-tab.minimize-to-tray.*</c>, <c>main.tray.*</c>) 42 dilde, dusen yok; etiket 11 dilde kola girdi: olculen toplam 3874 + 11 = 3885, en 372 + 1 = 373, tr 126 degismedi, gezilen 83678 + 258 = 83936.</para>
    /// <para>2026-10-17, sessiz bolum hizlandirma: iki yeni anahtar (<c>player.menu.silenceskip</c>, <c>player.view.state-silenceskip</c>) 42 dilde, dusen yok; gezilen 84194 + 86 = 84280.</para>
    /// <para>2026-10-18, biten ciktinin kalite olcumu (later 122): dokuz yeni anahtar, dusen yok; gezilen 84280 + 9 x 43 = 84667.</para>
    /// <para>2026-10-18, 122 ve 103 birlikte: toplam 3888 + 6 + 13 = 3907, en 375, tr 126; gezilen 84280 + 387 + 344 = 85011.</para>
    /// <para>2026-10-18, sonuc paneli (later 72): sekiz yeni anahtar (<c>recorder.output.copy-file</c>/<c>to-gif</c>/<c>gif-*</c>, <c>recorder.name.*</c>) 42 dilde, dusen yok; olculen toplam 3888 + 15 = 3903, en 373 + 2 = 375, tr 126 degismedi, gezilen 84280 + 344 = 84624.</para>
    /// <para>2026-10-18, 122, 103 ve 72 birlikte: toplam 3907 + 15 = 3922, en 377, tr 126; gezilen 85011 + 344 = 85355.</para>
    /// <para>2026-10-18, ve 96 (HDR ton esleme, tek anahtar): toplam 3922 + 2 = 3924, en 377, tr 126; gezilen 85355 + 43 = 85398.</para>
    /// <para>2026-10-18, ve 77 (kabuk kisayollari, on anahtar): toplam 3924 + 24 = 3948, en 381, tr 127; gezilen 85398 + 430 = 85828.</para>
    /// <para>2026-10-18, cizim arac paneli (later 42): dokuz yeni <c>recorder.sketch.*</c> anahtari 42 dilde, dusen yok; olculen gezilen 85828 + 9 x 43 = 86215. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, kamera ana kaynak (later 69): iki yeni anahtar (<c>recorder.target.device</c>, <c>.device-pick</c>) 42 dilde, dusen yok; gezilen 85828 + 2 x 43 = 85914. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, 42 ve 69 birlikte: toplam 3948 + 4 + 6 = 3958, en 382, tr 128; gezilen 85828 + 387 + 86 = 86301.</para>
    /// <para>2026-10-18, ve 90 (sesten video, yedi anahtar: <c>main.convert.audio-visual*</c>, <c>main.validation.audio-video-*</c>): olculen toplam 3948 + 6 = 3954, en 381, tr 127 degismedi; gezilen 85828 + 7 x 43 = 86129.</para>
    /// <para>2026-10-18, 90 ile: toplam 3958 + 6 = 3964, en 382, tr 128; gezilen 86301 + 301 = 86602.</para>
    /// <para>2026-10-18, paylasim zinciri (later 72, EKB-33): yeni <c>recorder.finish.shrink-upload-copy</c> 42 dilde, dusen yok; cs, en, nl, sk, sl, tr kola giriyor (KOL dokumu): toplam 3948 + 6 = 3954, en 381 + 1 = 382, tr 127 + 1 = 128; gezilen 85828 + 43 = 85871.</para>
    /// <para>2026-10-18, 72 kalan ile: toplam 3964 + 6 = 3970, en 383, tr 129; gezilen 86602 + 43 = 86645.</para>
    /// <para>2026-10-18, resim klibi (later 87, DZB-04/05/49): bes yeni <c>editor.still.*</c> anahtari 42 dilde, dusen yok; olculen toplam 3970 + 7 = 3977, en 383, tr 129; gezilen 86645 + 43 x 5 = 86860. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, ileri renk (later 88, DZB-36/38): on yeni <c>editor.clip.*</c> anahtari (<c>auto-color</c>, <c>curve-*</c>, <c>*-hue</c>, <c>*-amount</c>) 42 dilde, dusen yok; yalniz sw <c>curve-mids</c> kola giriyor (KOL dokumu): toplam 3970 + 1 = 3971, en 383, tr 129 degismedi; gezilen 86645 + 10 x 43 = 87075.</para>
    /// <para>2026-10-18, 87 ve 88 birlikte: toplam 3970 + 7 + 1 = 3978, en 383, tr 129; gezilen 86645 + 215 + 430 = 87290.</para>
    /// <para>2026-10-18, hiz rampasi (later 89, DZB-48/50): uc yeni anahtar (<c>editor.speed-ramp-down</c>, <c>.speed-ramp-up</c>, <c>editor.smooth-slow</c>) 42 dilde, dusen yok; es 2, fr 1, pt 2, ro 2 kola giriyor (KOL dokumu): olculen toplam 3970 + 7 = 3977, en 383, tr 129 degismedi; gezilen 86645 + 3 x 43 = 86774.</para>
    /// <para>2026-10-18, ve 89: toplam 3978 + 7 = 3985, en 383, tr 129; gezilen 87290 + 129 = 87419.</para>
    /// <para>2026-10-18, akis kalite secimi (later 83): iki yeni anahtar (<c>player.tracks.quality</c>, <c>.rate</c>) 42 dilde, dusen yok; olculen gezilen 86645 + 2 x 43 = 86731. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, ve 83: toplam 3985 + 2 = 3987, en 383, tr 129; gezilen 87419 + 86 = 87505.</para>
    /// <para>2026-10-18, ve 60 (kodlayici, HDR ve renk; dokuz anahtar 42 dilde, dusen yok): toplam 3948 + 14 = 3962, en 382, tr 128; gezilen 85828 + 387 = 86215.</para>
    /// <para>2026-10-18, ve 60: toplam 3987 + 14 = 4001, en 384, tr 130; gezilen 87505 + 387 = 87892.</para>
    /// <para>2026-10-18, LUT (yedi <c>editor.clip.lut.*</c> anahtari 42 dilde, dusen yok): en, fr, hu, ro <c>.unreadable</c>, lt <c>.none</c>, sv <c>.load</c> kola giriyor (KOL dokumu): toplam 3970 + 6 = 3976, en 384, tr 129; gezilen 86645 + 7 x 43 = 86946.</para>
    /// <para>2026-10-18, 87, 88, 89, ses kisma, OTIO ve LUT main'de birlikte: olculen toplam 4044, en 393, tr 132; gezilen 89249.</para>
    /// <para>2026-10-20, later 78: <c>main.share.link</c> 42 dilde: gezilen 89698 + 43 = 89741.</para>
    /// <para>2026-10-18, main CI duzeltmesi: 87 ve 88'in on iki duzenleyici anahtari 40 dile cevrildi, player.tracks.quality.rate dustu (birim main.unit.kbps-value'dan): olculen toplam 4070, en 393, tr 132; gezilen 89698.</para>
    /// <para>2026-10-20, kamera kipi (later 69): uc yeni <c>recorder.target.device-mode*</c> anahtari 43 dilde, dusen yok; gezilen 89698 + 3 x 43 = 89827.</para>
    /// <para>2026-10-20, later 88 ve 89 kalani: yedi yeni anahtar 42 dilde, <c>editor.clip.keys.count</c> uzadi; olculen gezilen 89698 + 301 = 89999.</para>
    /// <para>2026-10-20, later 105 ve 106: 21 kaydedici anahtari x 43 = 903; gezilen 89698 + 903 = 90601.</para>
    /// <para>2026-10-20, durgun klip ayarlari ve slayt sihirbazi (later 87, 90): yirmi yeni anahtar 42 dilde, dusen yok; olculen gezilen 89698 + 20 x 43 = 90558. <c>kayip</c> 0.</para>
    /// <para>2026-10-20, olcerek arama (later 141): alti yeni anahtar 42 dilde, dusen yok; olculen gezilen 89698 + 6 x 43 = 89956. <c>kayip</c> 0.</para>
    /// <para>2026-10-21, later 140 vekil kopya: dokuz <c>editor.proxy.*</c> anahtari 42 dile eklendi; gezilen 90085.</para>
    /// <para>2026-10-21, 147 yazi boyutu (iki <c>settings-tab.text-size.*</c> anahtari 43 dilde): gezilen 89698 + 2 x 43 = 89784.</para>
    /// <para>2026-10-21, later 126: uc yeni <c>recorder.option.hide-desktop-icons*</c> anahtari, gezilen 89698 + 43 x 3 = 89827.</para>
    /// <para>2026-10-21, V2 izi (later 110): on bes yeni anahtar 42 dilde, dusen yok: 89698 + 15 x 43 = 90343. <c>kayip</c> 0.</para>
    /// <para>2026-10-20, later 99: <c>player.view.media-controls</c> 42 dilde, dusen yok; olculen gezilen 89698 + 43 = 89741.</para>
    /// <para>2026-10-21, otomatik yakinlastirma (later 114 ilk yarisi): on yeni kaydedici anahtari 42 dilde, dusen yok; olculen gezilen 89698 + 10 x 43 = 90128. <c>kayip</c> 0.</para>
    /// <para>2026-10-21, Later 139: on sekiz ses izi ve iz denetimi anahtari 42 dile girdi; gezilen 90472.</para>
    /// <para>2026-10-18, ad yer tutucusu: uygulama adi cevirilerde {app} oldu, ekli adlar tur adiyla kuruldu; cs shell.menu.open "ve VidShrinku" yerine "v aplikaci {app}" oldugu icin kol sayimindan dustu: olculen toplam 4069, en 393, tr 132.</para>
    /// <para>2026-10-16, later 44 (etiket ve Onar): on sekiz yeni anahtar; olculen toplam 3781 + 2 = 3783 (lt main.convert.tags.cover.none, ro main.convert.tags.year), en 364, tr 123, gezilen 80453 + 774 = 81227.</para>
    /// <para>2026-10-15, uzun iste oncelik ve bitis sesi (later 76): dort yeni anahtar (<c>settings-tab.encode-priority.above-normal</c>, <c>settings-tab.finish-sound-file.choose</c>, <c>.hint</c>, <c>.reset</c>) 42 dilde, dusen yok; olculen gezilen 78604 + 172 = 78776.</para>
    /// <para>2026-10-15, bas konus (later 68): iki yeni anahtar (<c>recorder.audio.push-to-talk</c>, <c>.hint</c>), dusen yok; olculen 78604 + 43 x 2 = 78690. <c>kayip</c> 0.</para>
    /// <para>2026-10-15, ek arguman (later 62): bes yeni <c>main.convert.extra-args*</c> anahtari, dusen yok; olculen toplam 3733 + 5 = 3738, en 361, tr 120, gezilen 78604 + 215 = 78819.</para>
    /// <para>2026-10-15, saatli kayit (later 67): on iki yeni <c>recorder.schedule.*</c> anahtari, dusen yok; olculen 78604 + 43 x 12 = 79120.</para>
    /// <para>2026-10-16, hizli kirpma (later 41): dokuz yeni <c>recorder.output.quick-trim*</c> anahtari, dusen yok; olculen 80453 + 387 = 80840. <c>kayip</c> 0.</para>
    /// <para>2026-10-16, onizlemede elle kirpma (later 57): iki yeni anahtar (<c>main.advanced.filters.manualcrop</c>, <c>.tip</c>), dusen yok; olculen gezilen 80453 + 43 x 2 = 80539. <c>kayip</c> 0.</para>
    /// <para>2026-10-12, Linux turu: is surerken pasif Kucult/Donustur ipucu <c>main.action.busy-tip</c>, dusen yok. 1362 + 1 = 1363, 43 x 1363 = 58609. <c>kayip</c> 0.</para>
    /// <para>2026-10-12, Linux'ta ekran acilamayinca temiz cikis: tek yeni anahtar (<c>main.startup.no-display</c>, <c>Program.EkranYokIletisi</c>), dusen yok: 1363 + 1 = 1364, 43 x 1364 = 58652. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, oynatici HDR ton esleme (later 96): tek yeni anahtar (<c>player.advanced.tone-mapping</c>), dusen yok: 84280 + 43 = 84323. <c>kayip</c> 0.</para>
    /// <para>2026-10-18, bulaniklik ve bolge gizleme (later 88, DZB-28): bes yeni anahtar yalniz en ve tr'de, dusen yok: 86645 + 5 x 2 = 86655. <c>kayip</c> 0.</para>
    /// <para>2026-10-20, murekkep kisayolu (later 42): yeni <c>recorder.hotkeys.ink</c> 42 dilde, dusen yok: 89698 + 43 = 89741. <c>kayip</c> 0.</para>
    /// <para>2026-10-21, yeni pencerede ac (later 137): tek yeni anahtar (<c>player.menu.new-window</c>) 42 dilde ve zh_Hans takma adinda, dusen yok: 89698 + 43 = 89741. <c>kayip</c> 0.</para>
    /// <para>2026-10-21, adli oynatma listeleri (later 138, OYB-09): on bes yeni <c>player.playlists.*</c> anahtari 42 dilde, dusen yok: 89698 + 15 x 43 = 90343. <c>kayip</c> 0.</para>
    /// </summary>
    [Fact]
    public void AdVeBirimYazimiCumleOrtasindaDaKorunur()
    {
        var kayip = new List<string>();
        var gezilen = 0;

        foreach (var dil in Strings.Languages.OrderBy(d => d, StringComparer.Ordinal))
        {
            foreach (var anahtar in Strings.KeysOf(dil).OrderBy(k => k, StringComparer.Ordinal))
            {
                var cikti = LanguageCatalog.Title(Strings.GetIn(dil, anahtar), dil);
                gezilen++;

                foreach (var sozcuk in cikti.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var (govde, dogru) = AdiCagriliyorsa(sozcuk);
                    if (dogru is null || govde.StartsWith(dogru, StringComparison.Ordinal)) continue;
                    kayip.Add($"{dil}	{anahtar}	{govde[..dogru.Length]} yerine {dogru}	{cikti}");
                }
            }
        }

        foreach (var satir in kayip) _cikti.WriteLine("KAYIP	" + satir);
        _cikti.WriteLine($"SAYIM	gezilen	{gezilen}");
        _cikti.WriteLine($"SAYIM	kayip	{kayip.Count}");

        Assert.Equal(95589, gezilen);
        Assert.Empty(kayip);
    }

    /// <summary>
    /// <para>Kodek adi bir metinde gecerken kapali listeyle aranmaz; bicimden cikarilir:
    /// <c>H.264</c>/<c>H.265</c> gibi <c>H.</c> + uc rakam, <c>AV1</c>/<c>VP9</c> gibi iki
    /// harf + rakam. Boylece yarin eklenen bir kodek listeyi guncellemeyi unuttugumuz icin
    /// sessizce gozden kacmaz.</para>
    ///
    /// <para>Olcunun kurali: <c>main.advice.codec-upgrade</c> tavsiyesi kullaniciyi kodek
    /// seridinde bir secenege yolluyor, dolayisiyla andigi her kodek adi seridin uc
    /// etiketinden birinde gecmek zorunda. Tavsiye 42 dilde yillarca <c>H.265</c> diyordu;
    /// serit <c>AV1</c>'e cekilince tavsiye var olmayan bir secenegi gosterir oldu. Her dil
    /// ayri gezilir, adsiz kalan dil de kirmizi doner.</para>
    /// </summary>
    [Fact]
    public void TavsiyeninGosterdigiKodekSeritteBulunur()
    {
        var desen = new Regex(@"H\.\d{3}|[A-Z]{2}\d", RegexOptions.CultureInvariant);
        var etiketAnahtarlari = new[] { "main.codec.automatic", "main.codec.compatible", "main.codec.smallest" };

        var eksik = new List<string>();
        var gezilen = 0;

        foreach (var dil in Strings.Languages.OrderBy(d => d, StringComparer.Ordinal))
        {
            var seritte = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var anahtar in etiketAnahtarlari)
                foreach (Match esleme in desen.Matches(Strings.GetIn(dil, anahtar)))
                    seritte.Add(esleme.Value);

            var tavsiyede = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match esleme in desen.Matches(Strings.GetIn(dil, "main.advice.codec-upgrade")))
                tavsiyede.Add(esleme.Value);

            gezilen++;
            _cikti.WriteLine($"SERIT\t{dil}\t{string.Join(",", seritte)}\ttavsiye\t{string.Join(",", tavsiyede)}");

            if (tavsiyede.Count == 0)
            {
                eksik.Add($"{dil}\ttavsiye hicbir kodek adi anmiyor");
                continue;
            }

            foreach (var ad in tavsiyede)
                if (!seritte.Contains(ad))
                    eksik.Add($"{dil}\t{ad}\tseritte yok: {string.Join(",", seritte)}");
        }

        foreach (var satir in eksik) _cikti.WriteLine("EKSIK\t" + satir);
        _cikti.WriteLine($"SAYIM\tdil\t{gezilen}");

        Assert.Equal(Strings.Languages.Count(), gezilen);
        Assert.Empty(eksik);
    }

    [Fact]
    public void TumCiktiDokulur()
    {
        var satir = 0;
        foreach (var dil in Strings.Languages.OrderBy(d => d, StringComparer.Ordinal))
        {
            foreach (var anahtar in Strings.KeysOf(dil).OrderBy(k => k, StringComparer.Ordinal))
            {
                _cikti.WriteLine($"DOKUM\t{dil}\t{anahtar}\t{LanguageCatalog.Title(Strings.GetIn(dil, anahtar), dil).ReplaceLineEndings(" ")}");
                satir++;
            }
        }

        Assert.True(satir > 0);
    }

    /// <summary>
    /// Sayidan sonra gelen birim buyutulmuyor: kaydedicinin tampon listesinde "30 S"
    /// cikiyordu. Kaydedici metni bicimlemeden once buyuttugu icin birimin onunde sayi degil
    /// yer tutucu (<c>{0}</c>) durur; o da sayi sayilir. Formul: onceki sozcuk rakam ve
    /// ayiraclardan ya da <c>{n}</c>den ibaretse ve sozcuk en cok uc kucuk harfse birimdir.
    /// Karsi yon: sayidan ya da yer tutucudan sonra gelen uzun sozcuk basliktaki gibi buyur.
    /// </summary>
    [Theory]
    [InlineData("30 s", "en", "30 s")]
    [InlineData("5 sn", "tr", "5 sn")]
    [InlineData("10 mp", "hu", "10 mp")]
    [InlineData("save last 30 s", "en", "Save Last 30 s")]
    [InlineData("top 10 videos", "en", "Top 10 Videos")]
    [InlineData("{0} s", "en", "{0} s")]
    [InlineData("save last {0} s", "en", "Save Last {0} s")]
    [InlineData("{0} videos", "en", "{0} Videos")]
    public void SayidanSonrakiBirimBuyutulmez(string ham, string dil, string beklenen)
        => Assert.Equal(beklenen, LanguageCatalog.Title(ham, dil));
}


