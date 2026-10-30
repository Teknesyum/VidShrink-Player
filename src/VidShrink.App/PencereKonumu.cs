using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Avalonia;

namespace VidShrink.App;

/// <summary>
/// Pencerenin kapanıştaki yeri: konum fiziksel piksel, boy mantıksal birim (normal haldeki
/// dikdörtgen), büyütülmüş olup olmadığı ve üstünde durduğu ekranın sınırı. Panel yüksekliğiyle
/// aynı <c>layout.json</c> dosyasına yazılır.
/// </summary>
internal sealed record PencereKonumu(int X, int Y, double Genislik, double Yukseklik, bool Buyutulmus, PixelRect? Ekran);

/// <summary>Bağlı bir ekran: dış sınırı, görev çubuğu dışındaki çalışma alanı ve ölçeği.</summary>
internal readonly record struct EkranAlani(PixelRect Sinir, PixelRect CalismaAlani, double Olcek);

internal static class PencereKonumuKarari
{
    /// <summary>
    /// Pencerenin tutulabilmesi için bir ekranda görünmesi gereken en küçük parça (fiziksel
    /// piksel). Bundan azı görünen pencere kayıp sayılır ve varsayılan açılışa düşülür.
    /// </summary>
    internal const int EnAzGorunen = 64;

    /// <summary>
    /// Kayıtlı yeri bugünkü ekranlara oturtur. Kayıtlı ekran hâlâ bağlıysa o, değilse pencereyle
    /// en çok kesişen ekran seçilir; pencere hiçbir ekranın çalışma alanına düşmüyorsa (ikinci
    /// monitör sökülmüş gibi) <c>null</c> döner ve açılış bugünkü varsayılanla (büyütülmüş) olur.
    /// Kısmen taşan pencere seçilen ekranın çalışma alanına çekilir, sığmıyorsa küçültülür.
    /// </summary>
    internal static PencereKonumu? Coz(PencereKonumu? kayit, IReadOnlyList<EkranAlani> ekranlar)
    {
        if (kayit is null || ekranlar.Count == 0) return null;
        if (!double.IsFinite(kayit.Genislik) || !double.IsFinite(kayit.Yukseklik)) return null;
        if (kayit.Genislik <= 0 || kayit.Yukseklik <= 0) return null;

        EkranAlani? secilen = null;
        var enIyi = 0L;
        foreach (var ekran in ekranlar)
        {
            var kesisim = Dikdortgen(kayit, ekran.Olcek).Intersect(ekran.CalismaAlani);
            if (kesisim.Width < EnAzGorunen || kesisim.Height < EnAzGorunen) continue;
            var alan = (long)kesisim.Width * kesisim.Height;
            var ayniEkran = kayit.Ekran is { } kayitli && kayitli == ekran.Sinir;
            if (ayniEkran) { secilen = ekran; break; }
            if (alan > enIyi) { enIyi = alan; secilen = ekran; }
        }
        if (secilen is not { } hedef) return null;

        var olcek = Olcek(hedef.Olcek);
        var calisma = hedef.CalismaAlani;
        var genislik = Math.Min(kayit.Genislik, calisma.Width / olcek);
        var yukseklik = Math.Min(kayit.Yukseklik, calisma.Height / olcek);
        var pikselGenislik = (int)Math.Ceiling(genislik * olcek);
        var pikselYukseklik = (int)Math.Ceiling(yukseklik * olcek);
        var x = Math.Clamp(kayit.X, calisma.X, Math.Max(calisma.X, calisma.Right - pikselGenislik));
        var y = Math.Clamp(kayit.Y, calisma.Y, Math.Max(calisma.Y, calisma.Bottom - pikselYukseklik));
        var dolu = pikselGenislik >= calisma.Width * DoluPay && pikselYukseklik >= calisma.Height * DoluPay;
        return kayit with { X = x, Y = y, Genislik = genislik, Yukseklik = yukseklik, Ekran = hedef.Sinir, Buyutulmus = kayit.Buyutulmus || dolu };
    }

    /// <summary>
    /// Çalışma alanının bu payını iki yönde de kaplayan normal pencere büyütülmüş açılır. Küçük
    /// kipten Esc ile dönen pencere ekranı dolduran bir normal dikdörtgen olarak kaydediliyordu;
    /// açılışta kenarları boşluklu, büyütülmemiş geliyordu.
    /// </summary>
    internal const double DoluPay = 0.9;

    internal static PixelRect Dikdortgen(PencereKonumu konum, double olcek)
        => new(konum.X, konum.Y,
            (int)Math.Ceiling(konum.Genislik * Olcek(olcek)),
            (int)Math.Ceiling(konum.Yukseklik * Olcek(olcek)));

    private static double Olcek(double olcek) => olcek > 0 && double.IsFinite(olcek) ? olcek : 1;

    internal const string Anahtar = "window";

    internal static void Yaz(JsonObject kok, PencereKonumu konum)
    {
        var dugum = new JsonObject
        {
            ["x"] = konum.X,
            ["y"] = konum.Y,
            ["width"] = konum.Genislik,
            ["height"] = konum.Yukseklik,
            ["maximized"] = konum.Buyutulmus,
        };
        if (konum.Ekran is { } ekran)
            dugum["screen"] = new JsonObject
            {
                ["x"] = ekran.X,
                ["y"] = ekran.Y,
                ["width"] = ekran.Width,
                ["height"] = ekran.Height,
            };
        kok[Anahtar] = dugum;
    }

    internal static PencereKonumu? Oku(JsonNode? kok)
    {
        try
        {
            if (kok?[Anahtar] is not JsonObject dugum) return null;
            var konum = new PencereKonumu(
                dugum["x"]!.GetValue<int>(),
                dugum["y"]!.GetValue<int>(),
                dugum["width"]!.GetValue<double>(),
                dugum["height"]!.GetValue<double>(),
                dugum["maximized"]?.GetValue<bool>() ?? false,
                null);
            if (dugum["screen"] is JsonObject ekran)
                konum = konum with
                {
                    Ekran = new PixelRect(
                        ekran["x"]!.GetValue<int>(),
                        ekran["y"]!.GetValue<int>(),
                        ekran["width"]!.GetValue<int>(),
                        ekran["height"]!.GetValue<int>())
                };
            return konum;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or NullReferenceException)
        {
            return null;
        }
    }
}
