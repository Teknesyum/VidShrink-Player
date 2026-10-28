using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace VidShrink.App;

/// <summary>
/// AYR-P1: pencerenin yeri, boyu, ekranı ve büyütülmüş olup olmadığı kapanışta
/// <c>layout.json</c>'a yazılır, açılışta pencere gösterilmeden önce geri konur. Kayıtlı yer
/// bağlı ekranların hiçbirine düşmüyorsa dokunulmaz; pencere bugünkü varsayılanla
/// (büyütülmüş) açılır. Karar <see cref="PencereKonumuKarari"/>'nda, burada yalnız pencereye
/// bağlanır.
/// </summary>
public partial class MainWindow
{
    private PencereKonumu? _normalKonum;
    private bool _konumAcildi;

    private void PencereKonumunuIzle()
    {
        Opened += (_, _) => { _konumAcildi = true; NormalKonumuAl(); };
        PositionChanged += (_, _) => NormalKonumuAl();
    }

    private void NormalKonumuAl()
    {
        if (WindowState != WindowState.Normal || _kip is { Kucuk: true } || _kompakt is { Acik: true }) return;
        var boy = IsVisible ? ClientSize : new Size(Width, Height);
        if (!double.IsFinite(boy.Width) || !double.IsFinite(boy.Height) || boy.Width <= 0 || boy.Height <= 0) return;
        _normalKonum = new PencereKonumu(Position.X, Position.Y, boy.Width, boy.Height, false, null);
    }

    /// <summary>
    /// Kapanış anındaki yer. Büyütülmüş, tam ekran ya da simge durumundaki pencerede normal
    /// dikdörtgen son normal halden gelir; büyütülmüşlük simge durumunda geri dönülecek halden.
    /// </summary>
    internal PencereKonumu SimdikiKonum(IReadOnlyList<EkranAlani> ekranlar)
    {
        var buyutulmus = WindowState switch
        {
            WindowState.Maximized => true,
            WindowState.Normal => false,
            _ => _restoreState == WindowState.Maximized,
        };
        if (WindowState == WindowState.Normal && _kip is not { Kucuk: true }) NormalKonumuAl();
        var normal = _normalKonum
            ?? new PencereKonumu(Position.X, Position.Y, Width, Height, false, null);
        var ekran = EkranBul(normal, ekranlar);
        return normal with { Buyutulmus = buyutulmus, Ekran = ekran?.Sinir };
    }

    private static EkranAlani? EkranBul(PencereKonumu konum, IReadOnlyList<EkranAlani> ekranlar)
    {
        EkranAlani? secilen = null;
        var enIyi = -1L;
        foreach (var ekran in ekranlar)
        {
            var kesisim = PencereKonumuKarari.Dikdortgen(konum, ekran.Olcek).Intersect(ekran.Sinir);
            var alan = (long)kesisim.Width * kesisim.Height;
            if (alan > enIyi) { enIyi = alan; secilen = ekran; }
        }
        return secilen;
    }

    private IReadOnlyList<EkranAlani> BagliEkranlar()
    {
        try
        {
            return Screens.All.Select(s => new EkranAlani(s.Bounds, s.WorkingArea, s.Scaling)).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<EkranAlani>();
        }
    }

    internal void PencereKonumunuKaydet() => PencereKonumunuKaydet(BagliEkranlar());

    internal void PencereKonumunuKaydet(IReadOnlyList<EkranAlani> ekranlar)
    {
        if (!_konumAcildi) return;
        var konum = SimdikiKonum(ekranlar);
        LayoutYaz(kok => PencereKonumuKarari.Yaz(kok, konum));
    }

    /// <summary>
    /// Açılışta, pencere gösterilmeden önce çağrılır (<see cref="App"/>). Geçerli kayıt yoksa
    /// <c>false</c> döner ve XAML'daki varsayılan (büyütülmüş, ortada) yürürlükte kalır.
    /// </summary>
    internal bool PencereKonumunuGeriYukle() => PencereKonumunuGeriYukle(BagliEkranlar());

    internal bool PencereKonumunuGeriYukle(IReadOnlyList<EkranAlani> ekranlar)
    {
        var konum = PencereKonumuKarari.Coz(KayitliKonum(), ekranlar);
        if (konum is null) return false;

        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Position = new PixelPoint(konum.X, konum.Y);
        Width = konum.Genislik;
        Height = konum.Yukseklik;
        _normalKonum = konum with { Buyutulmus = false, Ekran = null };
        if (konum.Buyutulmus) WindowState = WindowState.Maximized;
        return true;
    }

    internal PencereKonumu? KayitliKonum()
    {
        try
        {
            if (!File.Exists(SplitterSettingsPath)) return null;
            return PencereKonumuKarari.Oku(JsonNode.Parse(File.ReadAllText(SplitterSettingsPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>layout.json</c>'a birleştirerek yazar: panel yüksekliği pencere yerini, pencere yeri
    /// panel yüksekliğini silmez. Okunamayan dosya boş nesne sayılır.
    /// </summary>
    private void LayoutYaz(Action<JsonObject> degistir)
    {
        try
        {
            JsonObject kok;
            try
            {
                kok = File.Exists(SplitterSettingsPath)
                    ? JsonNode.Parse(File.ReadAllText(SplitterSettingsPath)) as JsonObject ?? new JsonObject()
                    : new JsonObject();
            }
            catch (JsonException)
            {
                kok = new JsonObject();
            }
            degistir(kok);
            Directory.CreateDirectory(Path.GetDirectoryName(SplitterSettingsPath)!);
            File.WriteAllText(SplitterSettingsPath, kok.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_kapandi) TxtSystemStatus.Text = $"{Say("settings.error.save")}: {ex.Message}";
        }
    }

    internal void KonumAcildiForTest() => _konumAcildi = true;
}
