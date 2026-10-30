using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App;
using VidShrink.App.Editing;
using VidShrink.App.Playback;
using VidShrink.Core;
using Xunit;
using Xunit.Abstractions;
using static VidShrink.Tests.OynaticiOdakYoluTests;

namespace VidShrink.Tests;

/// <summary>
/// Pencere hareketleri her sekmede (2 Ekim 2026 isteği): boş zeminde sol basılı sürükleme
/// pencereyi taşır, orta tuş bulunulan sekmede normal → tam ekran → küçük → tam ekran döngüsünü
/// sürer ve sekmeyi değiştirmez, Esc tam ekrandan küçüğe, küçükten döngü öncesi boyuta döner,
/// üst panelde çift tık aynı döngüye iner. Hepsi ham girdiyle (<c>InputHitTest</c> ile bulunan
/// noktaya platform olayı) koşar; noktalar sahne çizildikten sonra aranır. Döküm
/// <c>.calisma/sekme-pencere/</c> altına.
/// </summary>
public sealed class SekmePencereKipiTests
{
    private readonly ITestOutputHelper _output;

    public SekmePencereKipiTests(ITestOutputHelper output) => _output = output;

    private const string SamplePath = @"C:\Kayitlar\tatil-cekimi-2160p60.mkv";

    private static string Klasor => Path.Combine(GirdiKanit.Root, ".calisma", "sekme-pencere");

    /// <summary>
    /// Kullanıcının "boş zemin" dediği yer: etkileşimli denetim, liste, metin kutusu, kaydırma
    /// çubuğu, oynatıcı yüzeyi, karşılaştırma sahnesi, zaman çizelgesi ve el imleçli tıklanır
    /// alanlar dışında kalan her şey (kart arka planı, panel boşluğu, etiket).
    /// </summary>
    internal static bool BosZemin(MainWindow pencere, Visual? isabet)
    {
        if (isabet is null) return false;
        for (var v = isabet; v is not null && !ReferenceEquals(v, pencere); v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, pencere.TitleBar)) return false;
            if (EtkilesimliDenetim.Mi(v)) return false;
            if (v is SelectingItemsControl or TextBox or ScrollBar or PlayerView or ComparisonPanel or ComparisonSurface
                or ControlStrip or EditorTimeline or EditorTrackCanvas) return false;
            if (v is InputElement { Cursor: { } imlec } && !ReferenceEquals(imlec, Cursor.Default)) return false;
        }

        return true;
    }

    private static List<Point> Noktalar(MainWindow pencere, Func<Visual, bool> uygun, int adet, double aralik)
    {
        var bulunan = new List<Point>();
        var ust = pencere.TitleBar.Bounds.Height;
        for (var y = ust + 6; y < pencere.Bounds.Height - 6; y += 14)
        for (var x = 6.0; x < pencere.Bounds.Width - 12; x += 14)
        {
            var p = new Point(x, y);
            if (bulunan.Any(q => Math.Abs(q.X - p.X) < aralik && Math.Abs(q.Y - p.Y) < aralik)) continue;
            if (!new[] { 0.0, 4, 8 }.All(dx => pencere.InputHitTest(p + new Vector(dx, 0)) is Visual v && uygun(v))) continue;
            bulunan.Add(p);
            if (bulunan.Count == adet) return bulunan;
        }

        return bulunan;
    }

    private static string Zincir(MainWindow pencere, Visual? v)
    {
        var parcalar = new List<string>();
        for (; v is not null && !ReferenceEquals(v, pencere) && parcalar.Count < 6; v = v.GetVisualParent())
            parcalar.Add(v is Control { Name: { Length: > 0 } ad } ? v.GetType().Name + "#" + ad : v.GetType().Name);
        return string.Join(" < ", parcalar);
    }

    private static MainWindow Ac(string klasor)
    {
        var pencere = new MainWindow { SettingsPathOverride = Path.Combine(klasor, "settings.json"), Width = 1280, Height = 800, WindowState = WindowState.Normal };
        pencere.Classes.Add("reduced-motion");
        pencere.LoadWithoutProbing(SamplePath, Sample());
        pencere.Show();
        pencere.Player.NativeMoveDrag = false;
        return pencere;
    }

    private static void Sec(MainWindow pencere, int sira)
    {
        pencere.Tabs.SelectedIndex = sira;
        DenetimSurucu.Wait(pencere.Player, 0.6);
        pencere.SettleFades();
        KareSayaci.SahneCizilsin(pencere);
    }

    private static List<(int Sira, string Ad)> Sekmeler(MainWindow pencere)
    {
        var liste = new List<(int, string)>();
        for (var sira = 0; sira < pencere.Tabs.ItemCount; sira++)
            if (pencere.Tabs.ContainerFromIndex(sira) is TabItem { IsVisible: true } oge)
                liste.Add((sira, MainWindow.TabHeaderText(oge)));
        return liste;
    }

    private static void Olay(MainWindow pencere, RawPointerEventType tur, Point nokta, RawInputModifiers tus)
    {
        HamFare(pencere, tur, nokta, tus);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Kanitla<T>(Func<string, T> olc)
    {
        var klasor = Path.Combine(Klasor, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        try
        {
            return AppHost.Run(() => olc(klasor));
        }
        finally
        {
            try { Directory.Delete(klasor, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void HerSekmeninBosZemininiSolSuruklemeTasir()
    {
        var (dokum, hatalar) = Kanitla(klasor =>
        {
            var pencere = Ac(klasor);
            var satirlar = new StringBuilder();
            var kusur = new List<string>();
            try
            {
                foreach (var (sira, ad) in Sekmeler(pencere))
                {
                    Sec(pencere, sira);
                    if (sira == pencere.PlayerTabIndex)
                    {
                        var orta = Noktalar(pencere, v => v.FindAncestorOfType<PlayerView>(true) is not null && !EtkilesimliDenetim.Icinde(v, pencere.Player), 1, 1);
                        var once = pencere.Player.Trace.Count;
                        if (orta.Count == 1)
                        {
                            Olay(pencere, RawPointerEventType.Move, orta[0], RawInputModifiers.None);
                            Olay(pencere, RawPointerEventType.LeftButtonDown, orta[0], RawInputModifiers.LeftMouseButton);
                            Olay(pencere, RawPointerEventType.Move, orta[0] + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
                            Olay(pencere, RawPointerEventType.LeftButtonUp, orta[0] + new Vector(12, 0), RawInputModifiers.None);
                        }

                        var tasidi = pencere.Player.Trace.Skip(once).Contains("drag -> window");
                        satirlar.AppendLine($"{ad}\toynatici yuzeyi\t{(tasidi ? "tasiyor" : "TASIMIYOR")}");
                        if (!tasidi) kusur.Add($"{ad}: oynatici yuzeyinde surukleme pencereyi tasimadi");
                        continue;
                    }

                    var noktalar = Noktalar(pencere, v => BosZemin(pencere, v), 14, 70);
                    var tutan = 0;
                    foreach (var n in noktalar)
                    {
                        var baslatma = 0;
                        pencere.TasimaBaslatici = _ => baslatma++;
                        var isabet = pencere.InputHitTest(n) as Visual;
                        Olay(pencere, RawPointerEventType.Move, n, RawInputModifiers.None);
                        Olay(pencere, RawPointerEventType.LeftButtonDown, n, RawInputModifiers.LeftMouseButton);
                        Olay(pencere, RawPointerEventType.Move, n + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
                        Olay(pencere, RawPointerEventType.LeftButtonUp, n + new Vector(12, 0), RawInputModifiers.None);
                        if (baslatma == 1) tutan++;
                        else kusur.Add($"{ad}: ({n.X:0},{n.Y:0}) {Zincir(pencere, isabet)} tasimadi");
                    }

                    satirlar.AppendLine($"{ad}\tzemin noktasi {noktalar.Count}\ttasiyan {tutan}");
                    if (noktalar.Count < 3) kusur.Add($"{ad}: yalniz {noktalar.Count} bos zemin noktasi bulundu");
                }
            }
            finally
            {
                pencere.TasimaBaslatici = null;
                pencere.Close();
                Dispatcher.UIThread.RunJobs();
            }

            return (satirlar.ToString(), kusur);
        });

        Directory.CreateDirectory(Klasor);
        var metin = dokum + string.Join(Environment.NewLine, hatalar);
        File.WriteAllText(Path.Combine(Klasor, "zemin-tasima.txt"), metin);
        _output.WriteLine(metin);
        Assert.True(hatalar.Count == 0, metin);
    }

    [Fact]
    public void OrtaTusHerSekmedeDonguyuSurerSekmeDegismezEscGeriDoner()
    {
        var (dokum, hatalar) = Kanitla(klasor =>
        {
            var pencere = Ac(klasor);
            var satirlar = new StringBuilder();
            var kusur = new List<string>();
            try
            {
                foreach (var (sira, ad) in Sekmeler(pencere))
                {
                    Sec(pencere, sira);
                    var once = (pencere.Position, pencere.Width, pencere.Height, pencere.MinWidth, pencere.MinHeight);

                    var adimlar = new List<string>();
                    void Orta()
                    {
                        var hedef = sira == pencere.PlayerTabIndex
                            ? Noktalar(pencere, v => v.FindAncestorOfType<PlayerView>(true) is not null && !EtkilesimliDenetim.Icinde(v, pencere.Player), 1, 1)
                            : Noktalar(pencere, v => v.FindAncestorOfType<ComparisonPanel>(true) is null && v.FindAncestorOfType<PlayerView>(true) is null, 1, 1);
                        if (hedef.Count == 0)
                        {
                            adimlar.Add("nokta yok");
                            return;
                        }

                        Olay(pencere, RawPointerEventType.Move, hedef[0], RawInputModifiers.None);
                        Olay(pencere, RawPointerEventType.MiddleButtonDown, hedef[0], RawInputModifiers.MiddleMouseButton);
                        Olay(pencere, RawPointerEventType.MiddleButtonUp, hedef[0], RawInputModifiers.None);
                        DenetimSurucu.Wait(pencere.Player, 0.2);
                        KareSayaci.SahneCizilsin(pencere);
                    }

                    void Esc()
                    {
                        HamTus(pencere, Key.Escape, RawInputModifiers.None, null);
                        Dispatcher.UIThread.RunJobs();
                        DenetimSurucu.Wait(pencere.Player, 0.2);
                        KareSayaci.SahneCizilsin(pencere);
                    }

                    string Durum() => $"{pencere.WindowState}/{(pencere.Kip.Kucuk ? "kucuk" : "-")}/sekme {pencere.Tabs.SelectedIndex}/kenar {pencere.WindowShell.BorderThickness.Left:0}";

                    void Bekle(string adim, WindowState durum, bool kucuk)
                    {
                        var olcu = Durum();
                        adimlar.Add(adim + " " + olcu);
                        if (pencere.WindowState != durum || pencere.Kip.Kucuk != kucuk || pencere.Tabs.SelectedIndex != sira)
                            kusur.Add($"{ad}: {adim} sonrasi {olcu}, beklenen {durum}/{(kucuk ? "kucuk" : "-")}/sekme {sira}");
                        if ((durum == WindowState.FullScreen || kucuk) && pencere.WindowShell.BorderThickness != new Thickness(0))
                            kusur.Add($"{ad}: {adim} sonrasi kabuk kenarligi {pencere.WindowShell.BorderThickness}");
                    }

                    Orta();
                    Bekle("orta1", WindowState.FullScreen, false);
                    Orta();
                    Bekle("orta2", WindowState.Normal, true);
                    if (pencere.Kip.BorderColorRequest != PencereKipi.DwmColorNone) kusur.Add($"{ad}: kucuk kipte DWM kenarligi kalkmadi");
                    var ekran = pencere.Screens.ScreenFromWindow(pencere) ?? pencere.Screens.Primary;
                    if (ekran is not null && pencere.Kip.Kucuk)
                    {
                        var oran = sira == pencere.PlayerTabIndex ? pencere.Player.VideoAspect : 0;
                        var r = CompactWindow.Fit(ekran.WorkingArea.X, ekran.WorkingArea.Y, ekran.WorkingArea.Width, ekran.WorkingArea.Height, 0.3333, oran);
                        if (Math.Abs(pencere.Width - r.Width / ekran.Scaling) > 2 || Math.Abs(pencere.Height - r.Height / ekran.Scaling) > 2)
                            kusur.Add($"{ad}: kucuk boyut {pencere.Width:0}x{pencere.Height:0}, beklenen {r.Width / ekran.Scaling:0}x{r.Height / ekran.Scaling:0}");
                    }

                    Orta();
                    Bekle("orta3", WindowState.FullScreen, true);
                    Esc();
                    Bekle("esc1", WindowState.Normal, true);
                    Esc();
                    Bekle("esc2", WindowState.Normal, false);
                    var sonra = (pencere.Position, pencere.Width, pencere.Height, pencere.MinWidth, pencere.MinHeight);
                    if (sonra != once) kusur.Add($"{ad}: Esc sonrasi {sonra}, dongu oncesi {once}");

                    satirlar.AppendLine($"{ad}\t{string.Join(" | ", adimlar)}");
                }
            }
            finally
            {
                pencere.Kip.KucukBitir();
                pencere.Close();
                Dispatcher.UIThread.RunJobs();
            }

            return (satirlar.ToString(), kusur);
        });

        Directory.CreateDirectory(Klasor);
        var metin = dokum + string.Join(Environment.NewLine, hatalar);
        File.WriteAllText(Path.Combine(Klasor, "orta-tus.txt"), metin);
        _output.WriteLine(metin);
        Assert.True(hatalar.Count == 0, metin);
    }

    [Fact]
    public void BuyutulmusPencereKucuktenEscIleBuyutulmusDoner()
    {
        var sonuc = Kanitla(klasor =>
        {
            var pencere = Ac(klasor);
            try
            {
                pencere.WindowState = WindowState.Maximized;
                Dispatcher.UIThread.RunJobs();
                var min = (pencere.MinWidth, pencere.MinHeight);
                var kip = pencere.Kip;
                var olcu = new List<string>();
                var tabanDurumu = new List<WindowState>();
                pencere.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Window.MinWidthProperty && pencere.MinWidth > 0) tabanDurumu.Add(pencere.WindowState);
                };

                kip.KucukVeyaTamEkran(0);
                olcu.Add($"orta1 {pencere.WindowState}");
                kip.Birak();
                olcu.Add($"esc1 {pencere.WindowState} {pencere.Width}x{pencere.Height}");
                var tamSonra = (pencere.WindowState, kip.Kucuk, pencere.MinWidth, pencere.MinHeight);

                kip.KucukVeyaTamEkran(0);
                kip.KucukVeyaTamEkran(0);
                olcu.Add($"kucuk {pencere.WindowState}/{kip.Kucuk}/{pencere.MinWidth}");
                var kucukBoyut = (pencere.Width, pencere.Height);
                kip.Birak();
                Dispatcher.UIThread.RunJobs();
                olcu.Add($"esc2 {pencere.WindowState}/{kip.Kucuk}/{pencere.MinWidth}x{pencere.MinHeight}");
                return (tamSonra, ikinci: (pencere.WindowState, kip.Kucuk, pencere.MinWidth, pencere.MinHeight), min, kucukBoyut, tabanDurumu, dokum: string.Join(" | ", olcu));
            }
            finally
            {
                pencere.Kip.KucukBitir();
                pencere.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });

        _output.WriteLine(sonuc.dokum);
        Assert.Equal((WindowState.Maximized, false, sonuc.min.Item1, sonuc.min.Item2), sonuc.tamSonra);
        Assert.Equal((WindowState.Maximized, false, sonuc.min.Item1, sonuc.min.Item2), sonuc.ikinci);
        Assert.Equal(new[] { WindowState.Maximized }, sonuc.tabanDurumu);
    }

    [Fact]
    public void UstPanelCiftTikOrtaTusunYolunuCagirir()
    {
        var sonuc = Kanitla(klasor =>
        {
            var pencere = Ac(klasor);
            try
            {
                Sec(pencere, pencere.ShrinkTabIndex);
                var iz = new List<string>();
                void CiftTik()
                {
                    var bos = Noktalar(pencere, v => ReferenceEquals(v, pencere.TitleBar), 1, 1);
                    var p = bos.Count == 1 ? bos[0] : new Point(-1, -1);
                    if (bos.Count == 0)
                    {
                        for (var x = pencere.Bounds.Width / 2; x < pencere.Bounds.Width - 200; x += 7)
                            if (pencere.InputHitTest(new Point(x, pencere.TitleBar.Bounds.Height / 2)) is Visual v && ReferenceEquals(v, pencere.TitleBar))
                            {
                                p = new Point(x, pencere.TitleBar.Bounds.Height / 2);
                                break;
                            }
                    }

                    if (p.X < 0)
                    {
                        iz.Add("baslikta bos nokta yok");
                        return;
                    }

                    Olay(pencere, RawPointerEventType.Move, p, RawInputModifiers.None);
                    for (var i = 0; i < 2; i++)
                    {
                        Olay(pencere, RawPointerEventType.LeftButtonDown, p, RawInputModifiers.LeftMouseButton);
                        Olay(pencere, RawPointerEventType.LeftButtonUp, p, RawInputModifiers.None);
                    }

                    DenetimSurucu.Wait(pencere.Player, 0.2);
                    KareSayaci.SahneCizilsin(pencere);
                    iz.Add($"{pencere.WindowState}/{(pencere.Kip.Kucuk ? "kucuk" : "-")}/sekme {pencere.Tabs.SelectedIndex}");
                }

                CiftTik();
                CiftTik();
                CiftTik();
                return (iz, kip: pencere.Kip.Iz.ToList(), sekme: pencere.ShrinkTabIndex);
            }
            finally
            {
                pencere.Kip.KucukBitir();
                pencere.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });

        var metin = string.Join(Environment.NewLine, sonuc.iz) + Environment.NewLine + string.Join(" | ", sonuc.kip);
        _output.WriteLine(metin);
        Assert.Equal(new[]
        {
            $"FullScreen/-/sekme {sonuc.sekme}",
            $"Normal/kucuk/sekme {sonuc.sekme}",
            $"FullScreen/kucuk/sekme {sonuc.sekme}"
        }, sonuc.iz);
        Assert.Equal(new[] { "tam -> True", "kucuk -> True", "tam -> True" }, sonuc.kip.Where(s => !s.StartsWith("border", StringComparison.Ordinal)));
    }

    private static MediaInfo Sample() => new()
    {
        FilePath = SamplePath,
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
}
