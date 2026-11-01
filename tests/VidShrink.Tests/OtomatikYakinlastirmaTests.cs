using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using VidShrink.App.Recorder;
using VidShrink.Core;
using VidShrink.Ffmpeg;
using Xunit;
using static VidShrink.Tests.KaydediciAyarTests;

namespace VidShrink.Tests;

/// <summary>
/// Later 114 (kayıttan sonra otomatik yakınlaştırma), ilk yarı: kayıt sürerken imleç, tıklama ve tuş
/// <see cref="PointerTrackRecorder"/> ile video saatine göre yan dosyaya (<c>.pointer.json</c>) yazılır,
/// <see cref="AutoZoom.Suggest"/> bölge önerir, <see cref="AutoZoom.Filter"/> perspective ifadesini kurar (uzunsa zoompan).
/// Canlı kol 320x240 lavfi klibinde 20 px kutunun 2x bölgede 40 px'e çıktığını ve yerini ham gri kareden ölçer;
/// bölge dışındaki kare olumsuz kontrol. Kayıt kolu gerçek gdigrab kaydında yan dosyayı açar, kutu kapalıyken yan dosya yok.
/// Kanıt <c>.calisma/worktree-agent-later-114-115/</c>, test siler.
/// </summary>
public sealed class OtomatikYakinlastirmaTests
{
    private const string Dal = "worktree-agent-later-114-115";

    private static string Kanit
    {
        get
        {
            var yol = Path.Combine(GirdiKanit.Root, ".calisma", Dal);
            Directory.CreateDirectory(yol);
            return yol;
        }
    }

    private static void Kapat(params string[] adlar) => KanitKapanisi.Kapat(Kanit, adlar);

    private static string Klasor(string ad)
    {
        var yol = Path.Combine(Kanit, ad + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(yol);
        return yol;
    }

    private static PointerTrack Iz(IEnumerable<PointerPress> tiklar, IEnumerable<long>? tuslar = null, IEnumerable<PointerSample>? hareket = null)
        => new(1000, 500, (hareket ?? Array.Empty<PointerSample>()).ToArray(), tiklar.ToArray(), (tuslar ?? Array.Empty<long>()).ToArray());

    [Fact]
    public void IzDosyasiGidipGelirBozukDosyaBosDoner()
    {
        var iz = new PointerTrack(1920, 1080,
            new[] { new PointerSample(0, 10, 20), new PointerSample(500, 30, 40) },
            new[] { new PointerPress(250, 15, 25, 1) },
            new long[] { 700 });

        var geri = PointerTrack.Parse(iz.ToJson());

        Assert.NotNull(geri);
        Assert.Equal(iz.ToString(), geri!.ToString());
        Assert.Equal(1920, geri.Width);
        Assert.Equal(new PointerPress(250, 15, 25, 1), Assert.Single(geri.Clicks));
        Assert.Equal(700, Assert.Single(geri.Keys));
        Assert.Null(PointerTrack.Parse("{"));
        Assert.Null(PointerTrack.Parse("{\"v\":1,\"w\":0,\"h\":10,\"moves\":[],\"clicks\":[],\"keys\":[]}"));
        Assert.Null(PointerTrack.Parse("{\"v\":9,\"w\":10,\"h\":10,\"moves\":[],\"clicks\":[],\"keys\":[]}"));
        Assert.Equal(Path.Combine("d", "kayit.pointer.json"), PointerTrack.SidecarOf(Path.Combine("d", "kayit.mp4")));

        Assert.Equal((10, 20), geri.PositionAt(-1));
        Assert.Null(Iz(Array.Empty<PointerPress>()).PositionAt(0));
        Assert.Equal((10, 20), geri.PositionAt(0));
        Assert.Equal((10, 20), geri.PositionAt(499));
        Assert.Equal((30, 40), geri.PositionAt(9000));
    }

    [Fact]
    public void KaydediciVideoSaatineHizalanirDuraklamayiSaymaz()
    {
        long saat = 1000;
        var kayit = new PointerTrackRecorder(100, 200, 640, 480, () => saat);

        saat = 1300;
        kayit.Sync(TimeSpan.FromMilliseconds(500));
        kayit.Move(110, 210);
        kayit.Move(110, 210);
        saat = 1500;
        kayit.Click(150, 260, 2);
        kayit.Pause();
        saat = 4500;
        kayit.Move(300, 300);
        kayit.Key();
        kayit.Resume();
        saat = 4600;
        kayit.Key();
        kayit.Sync(TimeSpan.FromSeconds(30));

        var iz = kayit.Build();

        Assert.Equal(new PointerSample(500, 10, 10), Assert.Single(iz.Moves));
        Assert.Equal(new PointerPress(700, 50, 60, 2), Assert.Single(iz.Clicks));
        Assert.Equal(800, Assert.Single(iz.Keys));
        Assert.Equal((640, 480), (iz.Width, iz.Height));
    }

    [Fact]
    public void YakinTiklamalarBirlesirUzakOlanlarOrtadanBolunur()
    {
        var iz = Iz(new[]
        {
            new PointerPress(1000, 500, 250, 1),
            new PointerPress(1800, 520, 260, 1),
            new PointerPress(3600, 900, 450, 1),
            new PointerPress(5000, -5, 100, 1),
        });

        var bolgeler = AutoZoom.Suggest(iz, 10);

        Assert.Equal(2, bolgeler.Count);
        Assert.Equal(0.5, bolgeler[0].Start, 6);
        Assert.Equal(3.2, bolgeler[0].End, 6);
        Assert.Equal(0.51, bolgeler[0].CenterX, 6);
        Assert.Equal(0.51, bolgeler[0].CenterY, 6);
        Assert.Equal(3.2, bolgeler[1].Start, 6);
        Assert.Equal(5.1, bolgeler[1].End, 6);
        Assert.Equal(0.9, bolgeler[1].CenterX, 6);

        var bolunen = AutoZoom.Suggest(Iz(new[] { new PointerPress(1000, 100, 100, 1), new PointerPress(2000, 900, 400, 1) }), 10);
        Assert.Equal(2, bolunen.Count);
        Assert.Equal(2.0, bolunen[0].End, 6);
        Assert.Equal(2.0, bolunen[1].Start, 6);
    }

    [Fact]
    public void TusImlecinYerindenBolgeVerirSureVeKisaBolgeKirpilir()
    {
        var iz = Iz(Array.Empty<PointerPress>(), new long[] { 2000, 9900 }, new[] { new PointerSample(0, 250, 125), new PointerSample(9000, 750, 375) });

        var bolgeler = AutoZoom.Suggest(iz, 10);

        var tek = Assert.Single(bolgeler);
        Assert.Equal(1.5, tek.Start, 6);
        Assert.Equal(3.5, tek.End, 6);
        Assert.Equal(0.25, tek.CenterX, 6);
        Assert.Equal(0.25, tek.CenterY, 6);
        Assert.Empty(AutoZoom.Suggest(iz, 0));
        Assert.Empty(AutoZoom.Suggest(iz, double.NaN));
        Assert.Empty(AutoZoom.Suggest(Iz(Array.Empty<PointerPress>(), new long[] { 1000 }), 10));
    }

    [Fact]
    public void FiltreVeArgumanlarBolgeOlcekVeIplikTasir()
    {
        var bolgeler = new[] { new ZoomRegion(1, 3, 0.75, 0.25) };

        var filtre = AutoZoom.Filter(bolgeler, 2, 320, 240, 10);
        var dar = AutoZoom.Filter(new[] { new ZoomRegion(1, 1.4, 0.5, 0.5) }, 99, 320, 240, double.NaN);
        var argumanlar = AutoZoom.Build("k.mp4", "k-zoom.mp4", filtre, hasAudio: true, threads: 2).ToList();
        var sessiz = AutoZoom.Build("k.mkv", "k-zoom.mkv", filtre, hasAudio: false).ToList();

        var eski = AutoZoom.ZoomPanFilter(bolgeler, 2, 320, 240, 10);
        var cok = Enumerable.Range(0, AutoZoom.MaxRegions).Select(i => new ZoomRegion(i * 3.0, i * 3.0 + 1.5, 0.1 + 0.8 * (i % 2), 0.5)).ToList();
        var uzun = AutoZoom.Filter(cok, 2, 320, 240, 10);

        Assert.StartsWith("fps=10,perspective=x0='st(4,(in-1)/10);st(2,1/(1+1*(0+(st(0,clip((ld(4)-1)/0.4,0,1)*clip((3-ld(4))/0.4,0,1))", filtre);
        Assert.Contains("+gte(ld(4),1)*lt(ld(4),3)*(0.25)", filtre);
        Assert.Contains("+gte(ld(4),1)*lt(ld(4),3)*(-0.25)", filtre);
        Assert.Contains("-ld(2)/2,0,1-ld(2))+(ld(2)-1)/2+W*ld(2)':y1=", filtre);
        Assert.EndsWith(":interpolation=cubic:eval=frame", filtre);
        Assert.InRange(filtre.Length, 1, AutoZoom.MaxPerspectiveLength);
        Assert.StartsWith("fps=30,perspective=", dar);
        Assert.Contains("st(2,1/(1+3*(", dar);
        Assert.Contains("/0.2,0,1)", dar);
        Assert.StartsWith("zoompan=z='1+1*(0+(st(0,clip((it-1)/0.4,0,1)*clip((3-it)/0.4,0,1))", eski);
        Assert.Contains("+gte(it,1)*lt(it,3)*(0.25)", eski);
        Assert.EndsWith(":d=1:s=320x240:fps=10", eski);
        Assert.StartsWith("zoompan=", uzun);
        Assert.Equal(AutoZoom.ZoomPanFilter(cok, 2, 320, 240, 10), uzun);
        Assert.Equal(1.1, AutoZoom.ClampScale(0.5));
        Assert.Equal(AutoZoom.DefaultScale, AutoZoom.ClampScale(double.NaN));

        Assert.Equal(filtre, argumanlar[argumanlar.IndexOf("-vf") + 1]);
        Assert.Equal("2", argumanlar[argumanlar.IndexOf("-threads") + 1]);
        Assert.Equal("0:a?", argumanlar[argumanlar.LastIndexOf("-map") + 1]);
        Assert.Contains("+faststart", argumanlar);
        Assert.DoesNotContain("-threads", sessiz);
        Assert.DoesNotContain("0:a?", sessiz);
        Assert.DoesNotContain("+faststart", sessiz);

        var dolu = new HashSet<string> { Path.Combine("d", "k-zoom.mp4") };
        Assert.Equal(Path.Combine("d", "k-zoom_2.mp4"), AutoZoom.Target(Path.Combine("d", "k.mp4"), dolu.Contains));
        Assert.Equal(Path.Combine("d", "k-zoom.mkv"), AutoZoom.Target(Path.Combine("d", "k.webm"), _ => false));
    }

    /// <summary>
    /// Arası <see cref="AutoZoom.PanGapSeconds"/>'tan kısa iki bölge tek çekimdir: yakınlık sınırda bırakılmaz, merkez
    /// sınırın ortasında <see cref="AutoZoom.PanSeconds"/> içinde sıçramadan kayar. Arası uzun iki bölgede yakınlık
    /// aradaki boşlukta bırakılır; olumsuz kontrol.
    /// </summary>
    [Fact]
    public void YanYanaBolgelerdeYakinlikBirakilmadanYumusakKaydirilir()
    {
        var yanYana = new[] { new ZoomRegion(3.5, 6, 0.75, 0.6), new ZoomRegion(1, 3, 0.25, 0.4) };
        var uzak = new[] { new ZoomRegion(1, 3, 0.25, 0.4), new ZoomRegion(5, 7, 0.75, 0.6) };

        for (var t = 1.4; t < 5.6; t += 0.01)
            Assert.Equal(2, AutoZoom.At(yanYana, 2, t).Zoom, 6);
        Assert.Equal((2, 0.25, 0.4), AutoZoom.At(yanYana, 2, 2.9));
        Assert.Equal(0.5, AutoZoom.At(yanYana, 2, 3.25).CenterX, 6);
        Assert.Equal(0.5, AutoZoom.At(yanYana, 2, 3.25).CenterY, 6);
        Assert.Equal(0.75, AutoZoom.At(yanYana, 2, 3.6).CenterX, 9);
        Assert.Equal(0.6, AutoZoom.At(yanYana, 2, 3.6).CenterY, 9);

        var onceki = AutoZoom.At(yanYana, 2, 2.9).CenterX;
        var enBuyukAdim = 0.0;
        for (var t = 2.91; t <= 3.6; t += 0.01)
        {
            var simdi = AutoZoom.At(yanYana, 2, t).CenterX;
            Assert.True(simdi >= onceki - 1e-12, $"t={t} geri kaydı");
            enBuyukAdim = Math.Max(enBuyukAdim, simdi - onceki);
            onceki = simdi;
        }

        Assert.InRange(enBuyukAdim, 0.001, 1.5 * 0.5 / AutoZoom.PanSeconds * 0.01 + 1e-9);

        Assert.Equal((1, 0.5, 0.5), AutoZoom.At(uzak, 2, 4));
        Assert.Equal((1, 0.5, 0.5), AutoZoom.At(yanYana, 2, 0.5));
        Assert.Equal(0.25, AutoZoom.At(uzak, 2, 2.9).CenterX);
        Assert.Equal(0.75, AutoZoom.At(uzak, 2, 5.1).CenterX);

        var kisa = new[] { new ZoomRegion(0, 1, 0.2, 0.5), new ZoomRegion(1, 1.2, 0.8, 0.5) };
        Assert.Equal(0.2, AutoZoom.At(kisa, 2, 0.9).CenterX);
        Assert.Equal(0.8, AutoZoom.At(kisa, 2, 1.1).CenterX, 9);

        var tekCekim = AutoZoom.Filter(yanYana, 2, 320, 240, 30);
        var ikiCekim = AutoZoom.Filter(uzak, 2, 320, 240, 30);
        static int Zarf(string filtre) => filtre.Split("x0='")[1].Split("':y0=")[0].Split("st(0,").Length - 1;
        Assert.Equal(1, Zarf(tekCekim));
        Assert.Equal(2, Zarf(ikiCekim));
        Assert.Contains("st(1,clip((ld(4)-2.95)/0.6,0,1))", tekCekim);
        Assert.DoesNotContain("st(1,", ikiCekim);
    }

    private static (int X0, int Y0, int X1, int Y1) Kutu(byte[] gri, int en, int boy)
    {
        int x0 = en, y0 = boy, x1 = -1, y1 = -1;
        for (var y = 0; y < boy; y++)
        for (var x = 0; x < en; x++)
        {
            if (gri[y * en + x] < 128) continue;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }

        return (x0, y0, x1, y1);
    }

    private static async Task<byte[]> Kare(string video, double an, string ham)
    {
        var run = await FfmpegRunner.RunAsync(new[]
        {
            "-hide_banner", "-y", "-nostdin", "-ss", an.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", video,
            "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "-threads", "2", ham
        });
        Assert.True(run.Ok, FfmpegRunner.Tail(run.StandardError));
        return File.ReadAllBytes(ham);
    }

    [FfmpegFact]
    public async Task CanliKlipteKutuBolgedeIkiKatBuyurDisindaYerindeKalir()
    {
        var klasor = Klasor("canli");
        try
        {
            var kaynak = Path.Combine(klasor, "kaynak.mp4");
            var uret = await FfmpegRunner.RunAsync(new[]
            {
                "-hide_banner", "-y", "-nostdin", "-f", "lavfi",
                "-i", "color=c=black:s=320x240:r=10:d=3,drawbox=x=240:y=40:w=20:h=20:color=white:t=fill",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-threads", "2", kaynak
            });
            Assert.True(uret.Ok, FfmpegRunner.Tail(uret.StandardError));

            var sonuc = await AutoZoomer.RunAsync(kaynak, new[] { new ZoomRegion(1, 3, 250 / 320.0, 50 / 240.0) }, 2, threads: 2);
            Assert.True(sonuc.Ok, sonuc.Error);
            Assert.Equal(Path.Combine(klasor, "kaynak-zoom.mp4"), sonuc.Target);

            var bilgi = await FfprobeClient.ProbeAsync(sonuc.Target!);
            var once = Kutu(await Kare(sonuc.Target!, 0.3, Path.Combine(klasor, "once.raw")), 320, 240);
            var icinde = Kutu(await Kare(sonuc.Target!, 2.0, Path.Combine(klasor, "icinde.raw")), 320, 240);
            var bos = await AutoZoomer.RunAsync(kaynak, Array.Empty<ZoomRegion>(), 2, threads: 2);

            File.WriteAllLines(Path.Combine(Kanit, "canli-yakinlastirma.txt"), new[]
            {
                $"cikti {bilgi.Width}x{bilgi.Height} sure={bilgi.DurationSeconds:0.00}",
                $"bolge disi t=0.3 kutu={once}",
                $"bolge ici t=2.0 kutu={icinde}",
                $"bos bolge ok={bos.Ok} hedef={bos.Target ?? "yok"}"
            });

            Assert.Equal((320, 240), (bilgi.Width, bilgi.Height));
            Assert.InRange(bilgi.DurationSeconds, 2.8, 3.2);
            Assert.InRange(once.X0, 238, 242);
            Assert.InRange(once.X1, 257, 261);
            Assert.InRange(once.Y0, 38, 42);
            Assert.InRange(icinde.X0, 157, 163);
            Assert.InRange(icinde.X1, 196, 202);
            Assert.InRange(icinde.Y0, 77, 83);
            Assert.InRange(icinde.Y1, 116, 122);
            Assert.True(bos.Ok);
            Assert.Null(bos.Target);

            Kapat("canli-yakinlastirma.txt");
        }
        finally
        {
            if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        }
    }

    [Fact]
    public void KutuAyarDosyasinaYazilirYeniGorunumOkur()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows };
            var gorunur = Bul<Button>(view, "BtnZoomTrack").IsVisible;
            Bul<CheckBox>(view, "ChkZoomTrack").IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var dosya = JsonNode.Parse(File.ReadAllText(ayarYolu))?["zoomTrack"]?.ToJsonString();
            var yeni = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Windows };
            var linux = new RecorderView(ayarYolu) { SkipAutoMeasure = true, CapturePlatform = RecorderPlatform.Linux };
            return (gorunur, dosya, yeni: Bul<CheckBox>(yeni, "ChkZoomTrack").IsChecked, linux: Bul<Button>(linux, "BtnZoomTrack").IsVisible);
        }));

        Assert.True(olcu.gorunur);
        Assert.Equal("true", olcu.dosya);
        Assert.True(olcu.yeni);
        Assert.False(olcu.linux);
    }

    [Fact]
    public void SonucPanelindeBolgelerSecilirSecilenlerUygulanir()
    {
        var klasor = Klasor("panel");
        try
        {
            var dosya = Path.Combine(klasor, "kayit.mp4");
            File.WriteAllBytes(dosya, new byte[] { 1 });
            var izsiz = Path.Combine(klasor, "izsiz.mp4");
            File.WriteAllBytes(izsiz, new byte[] { 1 });
            var gif = Path.Combine(klasor, "hareket.gif");
            File.WriteAllBytes(gif, new byte[] { 1 });
            var tiksiz = Path.Combine(klasor, "tiksiz.mp4");
            File.WriteAllBytes(tiksiz, new byte[] { 1 });
            Iz(new[] { new PointerPress(1000, 100, 100, 1), new PointerPress(6000, 900, 400, 1) }).Save(PointerTrack.SidecarOf(dosya));
            Iz(new[] { new PointerPress(1000, 100, 100, 1) }).Save(PointerTrack.SidecarOf(gif));
            Iz(Array.Empty<PointerPress>()).Save(PointerTrack.SidecarOf(tiksiz));

            var olcu = AppHost.Run(() =>
            {
                IReadOnlyList<ZoomRegion>? verilen = null;
                double olcek = 0;
                var view = new RecorderView
                {
                    RevealFolder = _ => { },
                    ZoomProbe = (_, _) => Task.FromResult(10.0),
                    ZoomRun = (_, bolgeler, o) => { verilen = bolgeler; olcek = o; return Task.FromResult(new AutoZoomResult(true, Path.Combine(klasor, "kayit-zoom.mp4"), string.Empty)); },
                };

                view.ShowResult(new RecordResult(true, izsiz, 1, false, 0, string.Empty, 1));
                var izsizde = view.AutoZoomVisible;
                view.ShowResult(new RecordResult(true, gif, 1, false, 0, string.Empty, 1));
                var gifte = view.AutoZoomVisible;
                view.ShowResult(new RecordResult(true, dosya, 1, true, 0, string.Empty, 1));
                var yarimda = view.AutoZoomVisible;

                view.ShowResult(new RecordResult(true, tiksiz, 1, false, 0, string.Empty, 1));
                var tiksizde = view.AutoZoomVisible;
                _ = view.OpenAutoZoomAsync();
                var tiksizSatir = view.ZoomRegionRows;
                var bosYazi = view.ZoomEmptyVisible;

                view.ShowResult(new RecordResult(true, dosya, 1, false, 0, string.Empty, 1));
                var tamda = view.AutoZoomVisible;
                var acildi = view.OpenAutoZoomAsync();
                var satir = view.ZoomRegionRows;
                var bolge = view.ZoomRegions.ToList();
                view.SetZoomRegionChecked(0, false);
                view.SelectedZoomScale = 3;
                var uygulandi = view.ApplyAutoZoomAsync();
                var bildirim = view.NoticeText;

                view.ZoomRun = (_, _, _) => Task.FromResult(new AutoZoomResult(false, null, "bozuk"));
                _ = view.ApplyAutoZoomAsync();
                var hata = view.ErrorText;
                return (izsizde, gifte, yarimda, tiksizde, tiksizSatir, bosYazi, tamda, acildi.IsCompleted, satir, bolge, uygulandi.IsCompleted, verilen, olcek, bildirim, hata);
            });

            Assert.False(olcu.izsizde);
            Assert.False(olcu.gifte);
            Assert.False(olcu.yarimda);
            Assert.True(olcu.tiksizde);
            Assert.Equal(0, olcu.tiksizSatir);
            Assert.True(olcu.bosYazi);
            Assert.True(olcu.tamda);
            Assert.True(olcu.Item8);
            Assert.Equal(2, olcu.satir);
            Assert.Equal(AutoZoom.Suggest(PointerTrack.Load(PointerTrack.SidecarOf(dosya))!, 10), olcu.bolge);
            Assert.True(olcu.Item11);
            Assert.Equal(olcu.bolge[1], Assert.Single(olcu.verilen!));
            Assert.Equal(3, olcu.olcek);
            Assert.Contains("kayit-zoom.mp4", olcu.bildirim);
            Assert.Contains("bozuk", olcu.hata);
        }
        finally
        {
            if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        }
    }

    private sealed class SahteKanca : IInputHooks
    {
        public List<string> Cagrilar { get; } = new();
        public Action<PixelPoint, PointerButton>? Tik;
        public Action<uint, bool>? Tus;

        public bool Start(bool clicks, bool moves, bool keys, Action<PixelPoint, PointerButton> clicked, Action<PixelPoint> moved, Action<uint, bool> key)
        {
            Cagrilar.Add($"basla fare={clicks} klavye={keys}");
            Tik = clicked;
            Tus = key;
            return true;
        }

        public void Stop() => Cagrilar.Add("dur");
    }

    private sealed class SahteBindirme : IInputOverlay
    {
        public void Style(PointerStyle style) { }
        public void Ring(PixelPoint point, PointerButton button) { }
        public void Pointer(PixelPoint point, PixelRect area) { }
        public void Keys(string text, PixelRect region) { }
        public void CaptionStyle(KeyCaptionStyle style) { }
        public void Hide() { }
    }

    private sealed class SahteSes : IClickSound
    {
        public void Play() { }
    }

    private static void Bekle(int ms)
    {
        var saat = Stopwatch.StartNew();
        while (saat.ElapsedMilliseconds < ms)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }

    private static (bool Oturum, string Kanca, string? Yan, string Hata, bool Dugme) Kaydet(string klasor, bool iz)
        => AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var kanca = new SahteKanca();
            var view = new RecorderView(ayarYolu)
            {
                SkipAutoMeasure = true,
                CapturePlatform = RecorderPlatform.Windows,
                InputHooks = kanca,
                InputOverlay = new SahteBindirme(),
                ClickSound = new SahteSes(),
                PointerRead = () => (50, 60),
            };
            Elle(view);
            Bul<ComboBox>(view, "CmbTarget").SelectedIndex = (int)RecorderTargetKind.Region;
            Yaz(view, "TxtRegionX", "0");
            Yaz(view, "TxtRegionY", "0");
            Yaz(view, "TxtRegionWidth", "320");
            Yaz(view, "TxtRegionHeight", "240");
            Sec(view, "CmbCodec", "libx264");
            Sec(view, "CmbPreset", "ultrafast");
            Yaz(view, "TxtOutputFolder", klasor);
            Bul<CheckBox>(view, "ChkZoomTrack").IsChecked = iz;

            var basla = view.StartAsync();
            var saat = Stopwatch.StartNew();
            while (!basla.IsCompleted && saat.ElapsedMilliseconds < 15000) Bekle(20);
            var oturum = view.HasSession;
            Bekle(400);
            kanca.Tik?.Invoke(new PixelPoint(100, 100), PointerButton.Left);
            kanca.Tus?.Invoke(0x10, true);
            kanca.Tus?.Invoke(0x41, true);
            kanca.Tus?.Invoke(0x41, false);
            Bekle(1200);
            var dur = view.StopAsync();
            saat.Restart();
            while (!dur.IsCompleted && saat.ElapsedMilliseconds < 15000) Bekle(20);
            var yan = Directory.GetFiles(klasor, "*" + PointerTrack.Suffix).SingleOrDefault();
            return (oturum, string.Join(" | ", kanca.Cagrilar), yan, view.ErrorText, view.AutoZoomVisible);
        }));

    [KayitFact]
    public void KayittaIzYanDosyayaYazilirKutuKapaliykenYazilmaz()
    {
        var acik = Klasor("kayit-acik");
        var kapali = Klasor("kayit-kapali");
        try
        {
            var izli = Kaydet(acik, true);
            var izsiz = Kaydet(kapali, false);
            var iz = izli.Yan is null ? null : PointerTrack.Load(izli.Yan);

            var satirlar = new[]
            {
                $"kutu acik oturum={izli.Oturum} kanca=[{izli.Kanca}] yan={(izli.Yan is null ? "yok" : Path.GetFileName(izli.Yan))} dugme={izli.Dugme} iz={iz} hata={izli.Hata}",
                $"kutu kapali oturum={izsiz.Oturum} kanca=[{izsiz.Kanca}] yan={(izsiz.Yan is null ? "yok" : Path.GetFileName(izsiz.Yan))} dugme={izsiz.Dugme} hata={izsiz.Hata}"
            };
            File.WriteAllLines(Path.Combine(Kanit, "kayit-izi.txt"), satirlar);
            var ozet = string.Join(Environment.NewLine, satirlar);

            Assert.True(izli.Oturum, ozet);
            Assert.True(izli.Kanca == "basla fare=True klavye=True | dur", ozet);
            Assert.True(iz is not null, ozet);
            Assert.True((iz!.Width, iz.Height) == (320, 240), ozet);
            Assert.True(iz.Clicks.Count == 1 && iz.Keys.Count == 1, ozet);
            var tik = iz.Clicks[0];
            Assert.Equal((100, 100, (int)PointerButton.Left), (tik.X, tik.Y, tik.Button));
            Assert.Equal(new[] { (50, 60) }, iz.Moves.Select(m => (m.X, m.Y)));
            Assert.InRange(tik.Ms, 1, 10000);
            Assert.True(izli.Dugme, ozet);

            Assert.True(izsiz.Oturum, ozet);
            Assert.Equal(string.Empty, izsiz.Kanca);
            Assert.Null(izsiz.Yan);
            Assert.False(izsiz.Dugme);

            Kapat("kayit-izi.txt");
        }
        finally
        {
            Sil(acik);
            Sil(kapali);
        }
    }

    private static void Sil(string klasor)
    {
        for (var deneme = 0; deneme < 20 && Directory.Exists(klasor); deneme++)
        {
            try { Directory.Delete(klasor, true); }
            catch (IOException) { Thread.Sleep(150); }
            catch (UnauthorizedAccessException) { Thread.Sleep(150); }
        }
    }
}
