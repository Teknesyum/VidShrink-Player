using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using VidShrink.App.Recorder;
using VidShrink.Core;
using Xunit;
using static VidShrink.Tests.KaydediciAyarTests;

namespace VidShrink.Tests;

public sealed class KaydediciSeciciTests
{
    private static string Kanit
    {
        get
        {
            var yol = Path.Combine(GirdiKanit.Root, ".calisma", "paket-2");
            Directory.CreateDirectory(yol);
            return yol;
        }
    }

    /// <summary>Son asertten sonra çağrılır; kuralı <see cref="KanitKapanisi"/> anlatıyor.</summary>
    private static void Kapat(params string[] adlar) => KanitKapanisi.Kapat(Kanit, adlar);

    private static readonly PixelRect Masaustu = new(0, 0, 1024, 768);

    private static string? Dosyada(string dosya, string anahtar)
        => File.Exists(dosya) ? JsonNode.Parse(File.ReadAllText(dosya))?[anahtar]?.ToJsonString() : null;

    [Fact]
    public void SurukleSerbestteCiftBoyutaIner()
    {
        var ileri = RegionDraw.FromDrag(new PixelPoint(10, 20), new PixelPoint(651, 381), null, Masaustu);
        var geri = RegionDraw.FromDrag(new PixelPoint(651, 381), new PixelPoint(10, 20), null, Masaustu);

        Assert.Equal(new PixelRect(10, 20, 640, 360), ileri);
        Assert.Equal(new PixelRect(11, 21, 640, 360), geri);
        Assert.False(RegionDraw.Usable(RegionDraw.FromDrag(new PixelPoint(5, 5), new PixelPoint(6, 6), null, Masaustu)));
    }

    [Fact]
    public void OranKilidiSurukleyiOranaUydururVeEkrandaTutar()
    {
        var oranli = RegionDraw.FromDrag(new PixelPoint(0, 0), new PixelPoint(640, 500), RegionDraw.Ratio("16:9"), Masaustu);
        var serbest = RegionDraw.FromDrag(new PixelPoint(0, 0), new PixelPoint(640, 500), RegionDraw.Ratio("free"), Masaustu);
        var tasan = RegionDraw.FromDrag(new PixelPoint(900, 0), new PixelPoint(2000, 100), RegionDraw.Ratio("1:1"), Masaustu);
        var dikey = RegionDraw.FromDrag(new PixelPoint(0, 0), new PixelPoint(100, 700), RegionDraw.Ratio("9:16"), Masaustu);

        Assert.Equal(new PixelRect(0, 0, 888, 500), oranli);
        Assert.Equal(new PixelRect(0, 0, 640, 500), serbest);
        Assert.Equal(new PixelRect(900, 0, 124, 124), tasan);
        Assert.True(dikey.Bottom <= Masaustu.Bottom && dikey.Right <= Masaustu.Right);
        Assert.InRange(dikey.Width / (double)dikey.Height, 9 / 16.0 - 0.01, 9 / 16.0 + 0.01);
        Assert.Null(RegionDraw.Ratio("uydurma"));
        Assert.Equal(new PixelRect(-1920, 0, 2944, 1080), RegionDraw.Desktop(new[] { Masaustu, new PixelRect(-1920, 0, 1920, 1080) }));
    }

    [Fact]
    public void FareyleCizilenBolgeAyaraVeArgumanaGecer()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var istenenOran = new List<double?>();
            var once = new RecorderView(ayarYolu) { SkipAutoMeasure = true, RegionEditorEnabled = false };
            Elle(once);
            Bul<ComboBox>(once, "CmbAspect").SelectedIndex = Array.IndexOf(RegionDraw.Aspects, "16:9");
            once.DrawRegion = oran =>
            {
                istenenOran.Add(oran);
                return Task.FromResult<PixelRect?>(new PixelRect(100, 50, 641, 361));
            };
            var cizildi = once.DrawRegionAsync().GetAwaiter().GetResult();
            var kutular = string.Join(",", new[] { "TxtRegionX", "TxtRegionY", "TxtRegionWidth", "TxtRegionHeight" }.Select(a => Bul<TextBox>(once, a).Text));
            var dosya = (x: Dosyada(ayarYolu, "regionX"), w: Dosyada(ayarYolu, "regionWidth"), oran: Dosyada(ayarYolu, "regionAspect"), hedef: Dosyada(ayarYolu, "target"));

            var vazgecen = new RecorderView(ayarYolu) { SkipAutoMeasure = true, DrawRegion = _ => Task.FromResult<PixelRect?>(null) };
            var vazgecildi = vazgecen.DrawRegionAsync().GetAwaiter().GetResult();
            var vazgecKutu = Bul<TextBox>(vazgecen, "TxtRegionWidth").Text;

            var sonra = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows };
            var hazir = sonra.PrepareRecording()!.Value;
            var args = RecorderArguments.Build(hazir.Request, hazir.Path);
            return (cizildi, istenenOran, kutular, dosya, vazgecildi, vazgecKutu, aspect: Bul<ComboBox>(sonra, "CmbAspect").SelectedIndex,
                x: Deger(args, "-offset_x"), y: Deger(args, "-offset_y"), boyut: Deger(args, "-video_size"));
        }));

        File.WriteAllLines(Path.Combine(Kanit, "bolge-cizme.txt"), new[]
        {
            $"cizildi={olcu.cizildi} oran={string.Join(";", olcu.istenenOran)} kutular={olcu.kutular}",
            $"json regionX={olcu.dosya.x} regionWidth={olcu.dosya.w} regionAspect={olcu.dosya.oran} target={olcu.dosya.hedef}",
            $"arguman -offset_x {olcu.x} -offset_y {olcu.y} -video_size {olcu.boyut}",
            $"vazgec: sonuc={olcu.vazgecildi} genislik={olcu.vazgecKutu}"
        });

        Assert.True(olcu.cizildi);
        Assert.Equal(16 / 9.0, olcu.istenenOran.Single()!.Value, 3);
        Assert.Equal("100,50,640,360", olcu.kutular);
        Assert.Equal(("100", "640", "\"16:9\"", "\"Region\""), olcu.dosya);
        Assert.Equal(("100", "50", "640x360"), (olcu.x, olcu.y, olcu.boyut));
        Assert.Equal(Array.IndexOf(RegionDraw.Aspects, "16:9"), olcu.aspect);
        Assert.False(olcu.vazgecildi);
        Assert.Equal("640", olcu.vazgecKutu);

        Kapat("bolge-cizme.txt");
    }

    /// <summary>
    /// Mini şeritteki Bölge seç düğmesi çizimi açar: çizim sürerken şerit gizli, sonra şerit
    /// açık kalır ve bölge kutulara yazılır. Kayıt sürerken düğme görünmez.
    /// </summary>
    [Fact]
    public void MiniSerittenBolgeCizilirSeritAcikKalir()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            bool? cizerkenGorunur = null;
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true };
            view.ShrinkToMini();
            var mini = view.Mini!;
            view.PickTarget = _ =>
            {
                cizerkenGorunur = mini.IsVisible;
                return Task.FromResult<RecorderPick?>(new RecorderPick(RecorderTargetKind.Region, new PixelRect(100, 50, 641, 361)));
            };
            var bostaDugme = mini.BtnRegion.IsVisible;
            mini.BtnRegion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var sonra = (acik: view.MiniOpen, gorunur: mini.IsVisible, genislik: Bul<TextBox>(view, "TxtRegionWidth").Text);
            mini.Follow(VidShrink.Ffmpeg.RecorderState.Running, "00:00:01");
            var kayittaDugme = mini.BtnRegion.IsVisible;
            view.ExpandFromMini();
            return (bostaDugme, cizerkenGorunur, sonra, kayittaDugme);
        }));

        File.WriteAllLines(Path.Combine(Kanit, "mini-bolge.txt"), new[]
        {
            $"bosta dugme={olcu.bostaDugme} cizerken serit gorunur={olcu.cizerkenGorunur}",
            $"sonra: mini acik={olcu.sonra.acik} gorunur={olcu.sonra.gorunur} genislik={olcu.sonra.genislik}",
            $"kayitta dugme={olcu.kayittaDugme}"
        });

        Assert.True(olcu.bostaDugme);
        Assert.False(olcu.cizerkenGorunur);
        Assert.Equal((true, true, "640"), olcu.sonra);
        Assert.False(olcu.kayittaDugme);

        Kapat("mini-bolge.txt");
    }

    [Fact]
    public void MiniSeciciEkranVePencereHedefiniYazar()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true };
            view.ShrinkToMini();
            var mini = view.Mini!;
            var hedef = Bul<ComboBox>(view, "CmbTarget");
            var ekranSayisi = Bul<ComboBox>(view, "CmbScreen").ItemCount;

            view.PickTarget = _ => Task.FromResult<RecorderPick?>(new RecorderPick(RecorderTargetKind.Window, new PixelRect(0, 0, 800, 600), 0, "Not Defteri"));
            mini.BtnRegion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var pencere = (hedef.SelectedIndex, Bul<TextBox>(view, "TxtWindowTitle").Text, mini.IsVisible);

            view.PickTarget = _ => Task.FromResult<RecorderPick?>(new RecorderPick(RecorderTargetKind.Screen, new PixelRect(0, 0, 1920, 1080), ekranSayisi - 1));
            mini.BtnRegion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var ekran = (hedef.SelectedIndex, Bul<ComboBox>(view, "CmbScreen").SelectedIndex, mini.IsVisible);

            view.PickTarget = _ => Task.FromResult<RecorderPick?>(null);
            mini.BtnRegion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var vazgec = hedef.SelectedIndex;
            view.ExpandFromMini();
            return (pencere, ekran, ekranSayisi, vazgec);
        }));

        File.WriteAllLines(Path.Combine(Kanit, "mini-hedef.txt"), new[]
        {
            $"pencere: hedef={olcu.pencere.Item1} baslik={olcu.pencere.Item2} mini={olcu.pencere.Item3}",
            $"ekran: hedef={olcu.ekran.Item1} ekran={olcu.ekran.Item2}/{olcu.ekranSayisi} mini={olcu.ekran.Item3}",
            $"vazgec: hedef={olcu.vazgec}"
        });

        Assert.Equal(((int)RecorderTargetKind.Window, "Not Defteri", true), olcu.pencere);
        Assert.Equal((int)RecorderTargetKind.Screen, olcu.ekran.Item1);
        Assert.Equal(olcu.ekranSayisi - 1, olcu.ekran.Item2);
        Assert.True(olcu.ekran.Item3);
        Assert.Equal((int)RecorderTargetKind.Screen, olcu.vazgec);

        Kapat("mini-hedef.txt");
    }

    [Fact]
    public void SeciciEkranSirasiniNoktadanBulur()
    {
        var ekranlar = new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(1920, 0, 2560, 1440) };
        Assert.Equal(0, RecorderRegionPicker.ScreenAt(ekranlar, new PixelPoint(10, 10)));
        Assert.Equal(1, RecorderRegionPicker.ScreenAt(ekranlar, new PixelPoint(1920, 5)));
        Assert.Null(RecorderRegionPicker.ScreenAt(ekranlar, new PixelPoint(-5, 5)));
    }

    [Fact]
    public void PencereSecimiUstteOlaniDondurur()
    {
        var pencereler = RecorderWindows.Titled(new[]
        {
            new WindowEntry("  Üst  ", true, false, false, 7, false, new PixelRect(100, 100, 400, 300)),
            new WindowEntry("Araç", true, false, true, 7, false, new PixelRect(0, 0, 2000, 2000)),
            new WindowEntry("Kendi", true, false, false, 1, false, new PixelRect(0, 0, 2000, 2000)),
            new WindowEntry("Alt", true, false, false, 7, false, new PixelRect(0, 0, 1000, 800))
        }, ownProcessId: 1);

        Assert.Equal(new[] { "Üst", "Alt" }, pencereler.Select(p => p.Title));
        Assert.Equal("Üst", RecorderWindows.At(pencereler, new PixelPoint(150, 150))?.Title);
        Assert.Equal("Alt", RecorderWindows.At(pencereler, new PixelPoint(50, 50))?.Title);
        Assert.Null(RecorderWindows.At(pencereler, new PixelPoint(1500, 50)));
    }

    /// <summary>
    /// Hazir boyut listesi ve cizim penceresinin olcu etiketi ailenin cozunurluk
    /// yazimini kullanir. Ikisi de <c>"{0} × {1}"</c> ile bosluklu yaziyordu; ayni
    /// uygulamanin kaynak bilgisi ve oynaticisi ayni seyi bosluksuz yaziyor. Sifir
    /// pimli iki cagri yeriydi, yazim sessizce ayrisabiliyordu.
    /// </summary>
    [Fact]
    public void SeciciOlculeriAileninCozunurlukYazimiylaYazilir()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true };
            var etiketler = Bul<ComboBox>(view, "CmbRegionSize").ItemsSource!.Cast<string>().ToArray();

            var secici = new RecorderRegionPicker(null);
            secici.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Cizdir(secici, new PixelPoint(0, 0), new PixelPoint(1920, 1080));
            var etiket = secici.FindControl<TextBlock>("TxtSize")!.Text ?? "";
            secici.Close();

            return (etiketler, etiket);
        }));

        var hazir = olcu.etiketler.Where(e => e.Contains('×')).ToArray();

        Assert.NotEmpty(hazir);
        Assert.All(hazir, e => Assert.DoesNotContain(" × ", e));
        Assert.Contains("1920×1080", hazir);
        Assert.Equal("1920×1080", olcu.etiket);
    }

    /// <summary>
    /// Cizim penceresinin olcu etiketini isaretci olaylarini kurmadan surer: ozel
    /// <c>_start</c>/<c>_desktop</c> alanlari doldurulup <c>Draw</c> cagrilir. Amac
    /// surukleme davranisini olcmek degil — o <c>BolgeCizme</c>'de — yalnizca etiketin
    /// yazimini okumak.
    /// </summary>
    private static void Cizdir(RecorderRegionPicker secici, PixelPoint baslangic, PixelPoint bitis)
    {
        const System.Reflection.BindingFlags Ozel =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        var tur = typeof(RecorderRegionPicker);
        tur.GetField("_desktop", Ozel)!.SetValue(secici, new PixelRect(0, 0, 3840, 2160));
        tur.GetField("_start", Ozel)!.SetValue(secici, baslangic);
        tur.GetMethod("Draw", Ozel)!.Invoke(secici, new object[] { bitis });
    }

    /// <summary>
    /// Ölçü etiketi ekranın dışına taşmaz: seçim alt kenara dayanınca seçimin üstüne çıkar,
    /// sağ kenara dayanınca sola çekilir. Ortadaki seçimde yeri seçimin sol alt köşesidir
    /// (olumsuz kontrol).
    /// </summary>
    [Fact]
    public void OlcuEtiketiEkranIcindeKalir()
    {
        var alan = new Size(1920, 1080);
        var etiket = new Size(90, 24);

        var orta = RecorderRegionPicker.TagPosition(new Rect(100, 100, 400, 300), etiket, alan);
        var altta = RecorderRegionPicker.TagPosition(new Rect(100, 700, 400, 380), etiket, alan);
        var sagda = RecorderRegionPicker.TagPosition(new Rect(1880, 100, 40, 40), etiket, alan);
        var tamEkran = RecorderRegionPicker.TagPosition(new Rect(0, 0, 1920, 1080), etiket, alan);

        Assert.Equal(new Point(100, 400), orta);
        Assert.Equal(new Point(100, 676), altta);
        Assert.Equal(new Point(1830, 140), sagda);
        Assert.Equal(new Point(0, 0), tamEkran);
    }

    [Fact]
    public void CizimPenceresiMasaustunuKaplarVeEscVazgecer()
    {
        var olcu = AppHost.Run(() =>
        {
            var secici = new RecorderRegionPicker(null);
            secici.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var masaustu = RegionDraw.Desktop(secici.Screens.All.Select(s => s.Bounds));
            var konum = secici.Position;
            var olcek = secici.Screens.Primary?.Scaling ?? 1;
            var genislik = (int)Math.Round(secici.Width * olcek);
            var ekran = secici.Screens.ScreenCount;
            secici.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return (masaustu, konum, genislik, ekran, bitti: secici.Result.IsCompleted, sonuc: secici.Result.IsCompleted ? secici.Result.Result : new RecorderPick(RecorderTargetKind.Region, new PixelRect(1, 1, 1, 1)), acik: secici.IsVisible);
        });

        File.WriteAllLines(Path.Combine(Kanit, "cizim-penceresi.txt"), new[] { $"ekran={olcu.ekran} masaustu={olcu.masaustu} konum={olcu.konum} genislik={olcu.genislik} esc: bitti={olcu.bitti} sonuc={olcu.sonuc} acik={olcu.acik}" });

        Assert.True(olcu.ekran > 0);
        Assert.Equal(olcu.masaustu.Position, olcu.konum);
        Assert.Equal(olcu.masaustu.Width, olcu.genislik);
        Assert.True(olcu.bitti);
        Assert.Null(olcu.sonuc);
        Assert.False(olcu.acik);

        Kapat("cizim-penceresi.txt");
    }

    [Fact]
    public void HazirBoyutBolgeKutularinaVeAyaraYazilir()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true };
            var kutu = Bul<ComboBox>(view, "CmbRegionSize");
            var oncesi = Bul<TextBox>(view, "TxtRegionWidth").Text;
            kutu.SelectedIndex = 1 + Array.IndexOf(RegionDraw.Sizes, (1080, 1920));
            return (oncesi, w: Bul<TextBox>(view, "TxtRegionWidth").Text, h: Bul<TextBox>(view, "TxtRegionHeight").Text,
                jw: Dosyada(ayarYolu, "regionWidth"), jh: Dosyada(ayarYolu, "regionHeight"), sayi: kutu.ItemCount);
        }));

        Assert.NotEqual("1080", olcu.oncesi);
        Assert.Equal(("1080", "1920", "1080", "1920"), (olcu.w, olcu.h, olcu.jw, olcu.jh));
        Assert.Equal(RegionDraw.Sizes.Length + 1, olcu.sayi);
    }

    [Fact]
    public void PencereSeciciBasligiIstegeYazar()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var once = new RecorderView(ayarYolu) { SkipAutoMeasure = true, ListWindows = () => new[] { "Not Defteri", "Hesap Makinesi" } };
            Elle(once);
            Bul<ComboBox>(once, "CmbTarget").SelectedIndex = (int)RecorderTargetKind.Window;
            var liste = Bul<ComboBox>(once, "CmbWindow");
            var ogeler = liste.ItemsSource!.Cast<string>().ToArray();
            liste.SelectedIndex = 1;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var kutu = Bul<TextBox>(once, "TxtWindowTitle").Text;
            var dosyada = Dosyada(ayarYolu, "windowTitle");

            var sonra = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows, ListWindows = () => new[] { "Hesap Makinesi" } };
            sonra.RefreshWindowList();
            var hazir = sonra.PrepareRecording()!.Value;
            var args = RecorderArguments.Build(hazir.Request, hazir.Path);
            return (ogeler, kutu, dosyada, secili: Bul<ComboBox>(sonra, "CmbWindow").SelectedItem as string,
                girdi: Deger(args, "-i"));
        }));

        File.WriteAllLines(Path.Combine(Kanit, "pencere-secici.txt"), new[]
        {
            $"liste={string.Join(" | ", olcu.ogeler)} kutu={olcu.kutu} json={olcu.dosyada} yeniden={olcu.secili} -i {olcu.girdi}"
        });

        Assert.Equal(new[] { "Not Defteri", "Hesap Makinesi" }, olcu.ogeler);
        Assert.Equal("Hesap Makinesi", olcu.kutu);
        Assert.Equal("\"Hesap Makinesi\"", olcu.dosyada);
        Assert.Equal("Hesap Makinesi", olcu.secili);
        Assert.Equal("title=Hesap Makinesi", olcu.girdi);

        Kapat("pencere-secici.txt");
    }

    [Fact]
    public void PencereListesiGorunmeyeniKendiniVeBosuEler()
    {
        var pencereler = new[]
        {
            new WindowEntry("Tarayıcı", true, false, false, 10, false),
            new WindowEntry("Gizli", false, false, false, 10, false),
            new WindowEntry("Başka masaüstü", true, true, false, 10, false),
            new WindowEntry("Araç", true, false, true, 10, false),
            new WindowEntry("VidShrink", true, false, false, 99, false),
            new WindowEntry("Simge", true, false, false, 10, true),
            new WindowEntry("   ", true, false, false, 10, false),
            new WindowEntry("Tarayıcı", true, false, false, 11, false)
        };

        Assert.Equal(new[] { "Tarayıcı" }, RecorderWindows.Pick(pencereler, 99));
        Assert.Equal(2, RecorderWindows.Pick(pencereler, 1).Count);

        var gercek = RecorderWindows.Titles();
        Assert.All(gercek, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(gercek.Count, gercek.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EkranSecimiOfsetliBolgeyeCevrilir()
    {
        IReadOnlyList<ScreenBounds> Ekranlar() => new[] { new ScreenBounds(0, 0, 0, 1024, 768), new ScreenBounds(1, 1024, 0, 1920, 1080) };

        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var degismeyen = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows, ScreenSource = Ekranlar };
            Elle(degismeyen);
            degismeyen.RefreshScreens();
            var ilkHazir = degismeyen.PrepareRecording()!.Value;
            var ilk = RecorderArguments.Build(ilkHazir.Request, ilkHazir.Path);

            var once = new RecorderView(ayarYolu) { SkipAutoMeasure = true, ScreenSource = Ekranlar };
            once.RefreshScreens();
            var kutu = Bul<ComboBox>(once, "CmbScreen");
            var etiketler = kutu.ItemsSource!.Cast<string>().ToArray();
            kutu.SelectedIndex = 1;
            var dosyada = Dosyada(ayarYolu, "screenIndex");

            var sonra = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows, ScreenSource = Ekranlar };
            sonra.RefreshScreens();
            var hazir = sonra.PrepareRecording()!.Value;
            var args = RecorderArguments.Build(hazir.Request, hazir.Path);
            return (etiketler, dosyada, secili: Bul<ComboBox>(sonra, "CmbScreen").SelectedIndex, gorunur: Bul<Control>(sonra, "RowScreen").IsVisible,
                x: Deger(args, "-offset_x"), boyut: Deger(args, "-video_size"), ilkX: Deger(ilk, "-offset_x"));
        }));

        File.WriteAllLines(Path.Combine(Kanit, "ekran-secimi.txt"), new[]
        {
            $"etiketler={string.Join(" | ", olcu.etiketler)} json screenIndex={olcu.dosyada} yeniden={olcu.secili}",
            $"arguman -offset_x {olcu.x} -video_size {olcu.boyut}; ilk ekran -offset_x {olcu.ilkX} (iki monitorlu masaustunde ilk ekran da ofsetleniyor)"
        });

        Assert.Equal(2, olcu.etiketler.Length);
        Assert.Contains("1920", olcu.etiketler[1]);
        Assert.Equal("1", olcu.dosyada);
        Assert.Equal(1, olcu.secili);
        Assert.True(olcu.gorunur);
        Assert.Equal(("1024", "1920x1080"), (olcu.x, olcu.boyut));
        Assert.Equal("0", olcu.ilkX);

        Kapat("ekran-secimi.txt");
    }
}
