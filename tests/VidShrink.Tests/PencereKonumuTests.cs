using System;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using VidShrink.App;
using Xunit;
using Xunit.Abstractions;

namespace VidShrink.Tests;

/// <summary>
/// AYR-P1: pencerenin yeri <c>layout.json</c>'a yazılıp geri gelir. Ekranlar sahte
/// <see cref="EkranAlani"/> listesiyle verilir; gerçek ekran düzeni değişmez, pencere açılmaz.
/// Ayar dosyası <c>.calisma/worktree-agent-a7cce57401179f65c/pencere-konumu/</c> altında, test siler.
/// </summary>
public sealed class PencereKonumuTests
{
    private readonly ITestOutputHelper _output;

    public PencereKonumuTests(ITestOutputHelper output) => _output = output;

    private static readonly EkranAlani Birinci =
        new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0);

    private static readonly EkranAlani Ikinci =
        new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1400), 1.0);

    private static string Klasor()
    {
        var klasor = Path.Combine(TipSources.Root, ".calisma", "worktree-agent-a7cce57401179f65c",
            "pencere-konumu", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        return klasor;
    }

    private static void Sil(string klasor)
    {
        try { Directory.Delete(klasor, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void KaydedilenYerYeniPenceredeGeriGelirVePanelYuksekligiSilinmez()
    {
        var klasor = Klasor();
        var ayar = Path.Combine(klasor, "settings.json");
        var layout = Path.Combine(klasor, "layout.json");
        File.WriteAllText(layout, "{\"planPanelHeight\":412}");
        try
        {
            AppHost.Run(() =>
            {
                var ilk = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    ilk.WindowState = WindowState.Normal;
                    ilk.Position = new PixelPoint(2100, 140);
                    ilk.Width = 1300;
                    ilk.Height = 860;
                    ilk.KonumAcildiForTest();
                    ilk.PencereKonumunuKaydet(new[] { Birinci, Ikinci });
                    Assert.Equal(Ikinci.Sinir, ilk.KayitliKonum()!.Ekran);
                }
                finally { ilk.Close(); }

                _output.WriteLine(File.ReadAllText(layout));
                var kok = JsonNode.Parse(File.ReadAllText(layout))!;
                Assert.Equal(412, kok["planPanelHeight"]!.GetValue<double>());

                var ikinci = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    Assert.True(ikinci.PencereKonumunuGeriYukle(new[] { Birinci, Ikinci }));
                    _output.WriteLine($"geri: {ikinci.Position} {ikinci.Width}x{ikinci.Height} {ikinci.WindowState}");
                    Assert.Equal(new PixelPoint(2100, 140), ikinci.Position);
                    Assert.Equal(1300, ikinci.Width);
                    Assert.Equal(860, ikinci.Height);
                    Assert.Equal(WindowState.Normal, ikinci.WindowState);
                    Assert.Equal(WindowStartupLocation.Manual, ikinci.WindowStartupLocation);
                }
                finally { ikinci.Close(); }
            });
        }
        finally { Sil(klasor); }
    }

    [Fact]
    public void BuyutulmusPencereBuyutulmusAcilirVeNormalDikdortgeniKorunur()
    {
        var klasor = Klasor();
        var ayar = Path.Combine(klasor, "settings.json");
        try
        {
            AppHost.Run(() =>
            {
                var ilk = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    ilk.WindowState = WindowState.Normal;
                    ilk.Position = new PixelPoint(300, 200);
                    ilk.Width = 1200;
                    ilk.Height = 800;
                    ilk.KonumAcildiForTest();
                    ilk.PencereKonumunuKaydet(new[] { Birinci });
                    ilk.WindowState = WindowState.Maximized;
                    ilk.PencereKonumunuKaydet(new[] { Birinci });
                }
                finally { ilk.Close(); }

                var kayit = new MainWindow { SettingsPathOverride = ayar }.KayitliKonum();
                Assert.NotNull(kayit);
                Assert.True(kayit!.Buyutulmus);

                var ikinci = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    Assert.True(ikinci.PencereKonumunuGeriYukle(new[] { Birinci }));
                    Assert.Equal(WindowState.Maximized, ikinci.WindowState);
                    Assert.Equal(new PixelPoint(300, 200), ikinci.Position);
                }
                finally { ikinci.Close(); }
            });
        }
        finally { Sil(klasor); }
    }

    [Fact]
    public void IkinciEkranSokulunceVarsayilanBuyutulmusAcilisKalir()
    {
        var klasor = Klasor();
        var ayar = Path.Combine(klasor, "settings.json");
        try
        {
            AppHost.Run(() =>
            {
                var ilk = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    ilk.WindowState = WindowState.Normal;
                    ilk.Position = new PixelPoint(2400, 300);
                    ilk.Width = 1200;
                    ilk.Height = 800;
                    ilk.KonumAcildiForTest();
                    ilk.PencereKonumunuKaydet(new[] { Birinci, Ikinci });
                }
                finally { ilk.Close(); }

                var ikinci = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    Assert.False(ikinci.PencereKonumunuGeriYukle(new[] { Birinci }));
                    Assert.Equal(WindowState.Maximized, ikinci.WindowState);
                    Assert.Equal(WindowStartupLocation.CenterScreen, ikinci.WindowStartupLocation);
                }
                finally { ikinci.Close(); }

                var ucuncu = new MainWindow { SettingsPathOverride = ayar };
                try
                {
                    Assert.True(ucuncu.PencereKonumunuGeriYukle(new[] { Birinci, Ikinci }));
                    Assert.Equal(new PixelPoint(2400, 300), ucuncu.Position);
                }
                finally { ucuncu.Close(); }
            });
        }
        finally { Sil(klasor); }
    }

    [Fact]
    public void KapanisYeriYazarAmaAcilmamisPencereYazmaz()
    {
        var klasor = Klasor();
        var ayar = Path.Combine(klasor, "settings.json");
        var layout = Path.Combine(klasor, "layout.json");
        try
        {
            AppHost.Run(() =>
            {
                var acilmamis = new MainWindow { SettingsPathOverride = ayar };
                acilmamis.Close();
                Assert.False(File.Exists(layout));

                var pencere = new MainWindow { SettingsPathOverride = ayar };
                pencere.WindowState = WindowState.Normal;
                pencere.Position = new PixelPoint(120, 90);
                pencere.Width = 1200;
                pencere.Height = 800;
                pencere.KonumAcildiForTest();
                pencere.Close();
                var kayit = new MainWindow { SettingsPathOverride = ayar }.KayitliKonum();
                Assert.NotNull(kayit);
                Assert.Equal(120, kayit!.X);
                Assert.Equal(90, kayit.Y);
                Assert.False(kayit.Buyutulmus);
            });
        }
        finally { Sil(klasor); }
    }

    [Fact]
    public void SokulenEkrandakiYerHicbirEkranaDusmez()
    {
        var kayit = new PencereKonumu(2400, 300, 1200, 800, false, Ikinci.Sinir);
        Assert.Null(PencereKonumuKarari.Coz(kayit, new[] { Birinci }));
        Assert.NotNull(PencereKonumuKarari.Coz(kayit, new[] { Birinci, Ikinci }));
        Assert.Null(PencereKonumuKarari.Coz(kayit with { Buyutulmus = true }, new[] { Birinci }));
    }

    [Fact]
    public void KenardaKalanPencereGorunurAlanaCekilir()
    {
        var kayit = new PencereKonumu(1800, 960, 1200, 800, false, null);
        var cozulen = PencereKonumuKarari.Coz(kayit, new[] { Birinci });
        Assert.NotNull(cozulen);
        Assert.Equal(1920 - 1200, cozulen!.X);
        Assert.Equal(1040 - 800, cozulen.Y);
        Assert.Equal(Birinci.Sinir, cozulen.Ekran);

        var tasan = PencereKonumuKarari.Coz(new PencereKonumu(1900, 1030, 1200, 800, false, null), new[] { Birinci });
        Assert.Null(tasan);
    }

    [Fact]
    public void EkrandanBuyukPencereCalismaAlaninaKuculur()
    {
        var olcekli = new EkranAlani(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1.5);
        var cozulen = PencereKonumuKarari.Coz(new PencereKonumu(-50, -40, 1560, 1060, false, null), new[] { olcekli });
        Assert.NotNull(cozulen);
        Assert.Equal(0, cozulen!.X);
        Assert.Equal(0, cozulen.Y);
        Assert.Equal(1280, cozulen.Genislik, 3);
        Assert.Equal(688, cozulen.Yukseklik, 3);
    }

    [Fact]
    public void EkraniDolduranNormalPencereBuyutulmusAcilir()
    {
        var genis = new EkranAlani(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1400), 1.0);
        var dolu = PencereKonumuKarari.Coz(new PencereKonumu(0, 0, 2560, 1400, false, genis.Sinir), new[] { genis });
        Assert.True(dolu!.Buyutulmus);
        var yari = PencereKonumuKarari.Coz(new PencereKonumu(100, 100, 1600, 1000, false, genis.Sinir), new[] { genis });
        Assert.False(yari!.Buyutulmus);
    }

    [Fact]
    public void KayitliEkranHalaBagliysaOSecilir()
    {
        var sol = new EkranAlani(new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1.0);
        var kayit = new PencereKonumu(-1000, 100, 1200, 800, false, Birinci.Sinir);
        var cozulen = PencereKonumuKarari.Coz(kayit, new[] { sol, Birinci });
        Assert.Equal(Birinci.Sinir, cozulen!.Ekran);
        Assert.Equal(0, cozulen.X);

        var kayitsiz = PencereKonumuKarari.Coz(kayit with { Ekran = null }, new[] { sol, Birinci });
        Assert.Equal(sol.Sinir, kayitsiz!.Ekran);
        Assert.Equal(-1200, kayitsiz.X);
    }

    [Fact]
    public void BozukYadaEksikKayitOkunmaz()
    {
        Assert.Null(PencereKonumuKarari.Oku(JsonNode.Parse("{\"planPanelHeight\":300}")));
        Assert.Null(PencereKonumuKarari.Oku(JsonNode.Parse("{\"window\":{\"x\":\"a\",\"y\":1,\"width\":2,\"height\":3}}")));
        Assert.Null(PencereKonumuKarari.Oku(JsonNode.Parse("{\"window\":{\"x\":1}}")));
        Assert.Null(PencereKonumuKarari.Coz(new PencereKonumu(0, 0, double.NaN, 800, false, null), new[] { Birinci }));
        Assert.Null(PencereKonumuKarari.Coz(new PencereKonumu(0, 0, 1200, 800, false, null), Array.Empty<EkranAlani>()));

        var kok = new JsonObject();
        var konum = new PencereKonumu(-10, 20, 1234.5, 678.25, true, Ikinci.Sinir);
        PencereKonumuKarari.Yaz(kok, konum);
        Assert.Equal(konum, PencereKonumuKarari.Oku(JsonNode.Parse(kok.ToJsonString())));
    }
}
