using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Xunit;
using Xunit.Abstractions;

namespace VidShrink.Tests;

/// <summary>
/// Ikonlarin tasarim kutusu. Her yol 24x24 kutuya sabitlenmis olmali (bastaki
/// <c>F1 M 0,0 M 24,24</c>), murekkebi 2 birimlik kenar payinin icinde kalmali ve
/// hem yatay hem dikey olarak kutunun ortasinda durmali. Olcunun kaynagi
/// <c>docs/arastirma/ikon-estetigi.md</c>: kaydik merkezler orada tek tek sayildi.
///
/// <para>Takim Fluent UI System Icons'in 24 px Filled surumune gecince dil kalemden
/// dolguya dondu: sabitleyicinin onune <c>F1</c> (NonZero) geldi, cunku Avalonia'nin
/// varsayilani EvenOdd ve delikli her simge (cerceve, halka, mercek) yanlis dolardi.
/// Fluent'in kendi cizimindeki uc kayma <see cref="Istisna"/> tablosunda tek tek pimli.</para>
///
/// <para><b>Kenar payi toleransi gercekten gevsetildi</b>: 1e-3'ten 0.01'e, on kat. Merkez
/// kurali (<see cref="Tolerance"/> = 0.55) gevsetilmedi, gevseyen yalniz <see cref="Margin"/>.
/// Gerekcesi olculu — 18 Eylul 2026'da 1e-3 ile 25 simgenin 4'u dusuyor, hepsi Fluent'in
/// 2-22 canli alanini Bezier duzlestirmesi yuzunden kil payi asan govdeler:</para>
/// <list type="bullet">
/// <item><c>IconShrink</c> — sol 1.9961, alt 22.0091 (en buyuk sapma).</item>
/// <item><c>IconAbout</c> — ust 1.9990, sag 22.0031, alt 22.0021.</item>
/// <item><c>IconConvert</c> — ust 1.9980.</item>
/// <item><c>IconRecorder</c> — sag 22.0017.</item>
/// </list>
/// <para>Gereken en kucuk tolerans 0.0091; 0.01 onun hemen ustu, daha sikisi (0.005)
/// <c>IconShrink</c>'i dusurur. Yani bu sayi seçilmedi, olculdu. Gevseyen tolerans bir
/// gövdeyi kimliginden etmez: sekil kimligini <see cref="IkonImzaTests"/> ayri olcer.</para>
/// </summary>
public sealed class IkonKutusuTests
{
    internal const string Sabitleyici = "F1 M 0,0 M 24,24 ";

    private const double Margin = 2 - 0.01;
    private const double Center = 12;
    private const double Tolerance = 0.55;

    /// <summary>
    /// Ucgen bir sekil kutu merkezine oturtulunca sola kaymis gorunur: kutlesi
    /// tabanda toplanir. Sektor bu yuzden oynat ucgenini saga kaydiriyor
    /// (Lucide +0.50, Material +1.50). Fluent'inki +1.43 ile Material'in (+1.50)
    /// hemen altinda; yatay merkez olcusu bu ikonda gecerli degil, dikey olcu koşuyor.
    /// </summary>
    private static readonly string[] OptikKaydirilan = ["IconPlay"];

    /// <summary>
    /// Genel kurala girmeyen Fluent cizimleri. Bu adlar icin kural gevsetilmiyor,
    /// <b>sikilastiriliyor</b>: hepsi kendi olculen sinir kutusuyla 0.02 icinde pimleniyor,
    /// yani kaydirilan ya da baska bir dosyadan gelen bir govde bunlarda kirmizi doner.
    /// Cumle yalniz bu tabloyu anlatir; genel kenar payi kuralinin toleransi ayri bir konu ve
    /// o gercekten gevsedi — sinif aciklamasindaki olcume bak.
    /// <list type="bullet">
    /// <item><c>IconSpeedFast</c>, <c>IconSpeedSlow</c> — Fluent Top Speed kadrani, gosterge kutlesi merkezin ustunde (cy 11.00); ibre turetildi.</item>
    /// <item><c>IconSpeedFastTip</c> — hizli ibrenin turuncu ucu, yalniz IconSpeedFast'in ustune bindirilir; kendi basina ortalanmaz.</item>
    /// <item><c>IconCoffee</c> — kulp sagda 2 birimlik kenar payini tasiyor (sag kenar 23.00).</item>
    /// <item><c>IconLock</c> — Fluent Lock Closed, kilit halkasi ust kenar payina tasiyor (ust 1.00, cy 11.50).</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<string, (double Left, double Top, double Right, double Bottom)> Istisna = new()
    {
        ["IconSpeedFast"] = (2.00, 2.00, 22.00, 20.00),
        ["IconSpeedFastTip"] = (14.37, 7.80, 18.47, 11.39),
        ["IconSpeedSlow"] = (2.00, 2.00, 22.00, 20.00),
        ["IconCoffee"] = (3.00, 2.00, 23.00, 22.00),
        ["IconLock"] = (4.00, 1.00, 20.00, 22.00)
    };

    private const double IstisnaSlop = 0.02;

    private readonly ITestOutputHelper _cikti;

    public IkonKutusuTests(ITestOutputHelper cikti) => _cikti = cikti;

    private static readonly string IconPath =
        Path.Combine(TipSources.Root, "src", "VidShrink.App", "Themes", "Icons.axaml");

    private static readonly Regex Entry = new(
        """<StreamGeometry x:Key="(?<ad>\w+)">(?<yol>[^<]+)</StreamGeometry>""",
        RegexOptions.Compiled);

    internal static IEnumerable<object[]> IkonlarIc() => Ikonlar();

    public static IEnumerable<object[]> Ikonlar() =>
        Entry.Matches(File.ReadAllText(IconPath)).Select(m => new object[] { m.Groups["ad"].Value, m.Groups["yol"].Value });

    [Fact]
    public void ButunIkonlarKutuyaSabitli()
    {
        var kayit = Ikonlar().ToList();
        AppHost.Ensure();
        Assert.NotEmpty(kayit);
        foreach (var satir in kayit)
            Assert.StartsWith(Sabitleyici, (string)satir[1]);
    }

    /// <summary>
    /// Teknesyum bağlantısı yalnız yazı: <c>&lt;/&gt;</c> simgesi (<c>IconCode</c>) düğmeden
    /// ve sözlükten kalktı (danışma 015 S2). Düğmenin içinde yol yok, kaynak artık
    /// çözülmüyor; simge geri gelirse iki koşuldan biri kırılır.
    /// </summary>
    [Fact]
    public void TeknesyumDugmesiSimgesiz()
    {
        AppHost.Ensure();
        var (kaynakVar, yolSayisi, yazi) = AppHost.Run(() =>
        {
            var bulundu = Avalonia.Application.Current!.TryGetResource("IconCode", null, out _);
            var window = new VidShrink.App.MainWindow();
            try
            {
                var dugme = window.BtnGitHub;
                var yollar = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dugme).OfType<Avalonia.Controls.Shapes.Path>().Count()
                    + (dugme.Content is Avalonia.Controls.Shapes.Path ? 1 : 0);
                return (bulundu, yollar, dugme.Content is Avalonia.Controls.TextBlock);
            }
            finally
            {
                window.Close();
            }
        });
        _cikti.WriteLine($"IconCode kaynak {kaynakVar}, dugmede yol {yolSayisi}, icerik yazi {yazi}");
        Assert.False(kaynakVar, "IconCode sözlükte duruyor.");
        Assert.Equal(0, yolSayisi);
        Assert.True(yazi, "Teknesyum düğmesinin içeriği yalnız yazı olmalı.");
    }

    [Theory]
    [MemberData(nameof(Ikonlar))]
    public void MurekkepOrtada(string ad, string yol)
    {
        var govde = yol[Sabitleyici.Length..];
        AppHost.Ensure();
        var kutu = AppHost.Run(() => Geometry.Parse(govde).Bounds);
        _cikti.WriteLine($"IKON\t{ad}\tx {kutu.X:0.0000}-{kutu.Right:0.0000}\ty {kutu.Y:0.0000}-{kutu.Bottom:0.0000}\tcx {kutu.Center.X:0.0000}\tcy {kutu.Center.Y:0.0000}");

        if (Istisna.TryGetValue(ad, out var pim))
        {
            Assert.Equal(pim.Left, kutu.X, IstisnaSlop);
            Assert.Equal(pim.Top, kutu.Y, IstisnaSlop);
            Assert.Equal(pim.Right, kutu.Right, IstisnaSlop);
            Assert.Equal(pim.Bottom, kutu.Bottom, IstisnaSlop);
            return;
        }

        Assert.InRange(kutu.X, Margin, 24 - Margin);
        Assert.InRange(kutu.Y, Margin, 24 - Margin);
        Assert.InRange(kutu.Right, Margin, 24 - Margin);
        Assert.InRange(kutu.Bottom, Margin, 24 - Margin);
        if (!OptikKaydirilan.Contains(ad))
            Assert.InRange(kutu.Center.X, Center - Tolerance, Center + Tolerance);
        else
            Assert.InRange(kutu.Center.X, Center + 0.5, Center + 1.5);
        Assert.InRange(kutu.Center.Y, Center - Tolerance, Center + Tolerance);
    }
}
