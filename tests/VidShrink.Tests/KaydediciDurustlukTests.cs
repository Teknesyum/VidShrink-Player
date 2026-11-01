using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using VidShrink.App.Recorder;
using VidShrink.Core;
using VidShrink.Ffmpeg;
using Xunit;
using Sekil = Avalonia.Controls.Shapes;
using static VidShrink.Tests.KaydediciAyarTests;

namespace VidShrink.Tests;

public sealed class KilitFactAttribute : FactAttribute
{
    public KilitFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "FileShare.None kilidi yalniz Windows'ta silmeyi ve tasimayi engelliyor.";
    }
}

/// <summary>
/// Kaydedicinin kullaniciya soyledigi ile yaptigi ayni olmali (<c>.calisma/yalan-yok/envanter.md</c>
/// madde 13, 14, 44, 45, 46, 47). Her olcunun olumsuz kolu yaninda: dogru durumda eski metin
/// yine cikiyor, yani metin kosuldan bagimsiz sabit degil.
/// </summary>
public sealed class KaydediciDurustlukTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "vidshrink-durustluk-" + Guid.NewGuid().ToString("N"));

    public KaydediciDurustlukTests() => Directory.CreateDirectory(_klasor);

    public void Dispose()
    {
        try { Directory.Delete(_klasor, recursive: true); } catch (IOException) { }
    }

    private string Dosya(string ad, int bayt = 16)
    {
        var yol = Path.Combine(_klasor, ad);
        File.WriteAllBytes(yol, new byte[bayt]);
        return yol;
    }

    private static string Metin(string anahtar, params object[] args)
        => VidShrink.App.LanguageCatalog.Display(VidShrink.App.Localization.Strings.Get(anahtar, args));

    private static string Baslik(string anahtar)
        => Metin(anahtar, Bicim.Boyut.Mb(1, VidShrink.App.Localization.Strings.Culture), "1");

    private static RecordResult Sonuc(string yol, bool ok = true, bool yarim = false, int cikis = 0,
        string? tasimaHatasi = null, string? eksikGif = null, bool? oynar = null)
        => new(ok, yol, 1, yarim, cikis, string.Empty, 1, DeliveryError: tasimaHatasi, MissingGif: eksikGif, Playable: oynar);

    private sealed record Ekran(string Baslik, string Uyari, bool UyariGorunur, bool Simge, string Hata, string Not);

    private static Ekran Ciz(Action<RecorderView> yap) => AppHost.Run(() =>
    {
        using var ayar = new OzelAyar();
        var gorunum = new RecorderView(ayar.Yol);
        yap(gorunum);
        return new Ekran(
            gorunum.ResultText,
            gorunum.FindControl<TextBlock>("TxtWarning")!.Text ?? string.Empty,
            gorunum.FindControl<Grid>("WarningRow")!.IsVisible,
            gorunum.FindControl<Sekil.Path>("WarningGlyph")!.IsVisible,
            gorunum.ErrorText,
            gorunum.NoticeText);
    });

    [KilitFact]
    public void SilinemeyenKayitYoluylaSoylenir()
    {
        var silinir = Dosya("silinir.mkv");
        var kilitli = Dosya("kilitli.mkv");
        IReadOnlyList<string> kalan;
        using (new FileStream(kilitli, FileMode.Open, FileAccess.Read, FileShare.None))
            kalan = RecorderView.DeleteRecording(Sonuc(kilitli) with { Files = new[] { silinir, kilitli } });

        Assert.Equal(new[] { kilitli }, kalan);
        Assert.False(File.Exists(silinir));
        Assert.True(File.Exists(kilitli));

        var ekran = Ciz(v => v.ReportDiscard(kalan));
        Assert.Equal(Metin("recorder.discarded-kept", kilitli), ekran.Hata);
        Assert.NotEqual(Metin("recorder.discarded"), ekran.Not);
    }

    [Fact]
    public void SilinenKayitSilindiDer()
    {
        var yol = Dosya("temiz.mkv");
        var kalan = RecorderView.DeleteRecording(Sonuc(yol));

        Assert.Empty(kalan);
        Assert.False(File.Exists(yol));
        var ekran = Ciz(v => v.ReportDiscard(kalan));
        Assert.Equal(Metin("recorder.discarded"), ekran.Not);
        Assert.Equal(string.Empty, ekran.Hata);
    }

    [Fact]
    public void AtilanKaydinImlecIziDeSilinir()
    {
        var yol = Dosya("izli.mkv");
        var iz = Dosya("izli" + PointerTrack.Suffix);
        var baska = Dosya("baska" + PointerTrack.Suffix);

        var kalan = RecorderView.DeleteRecording(Sonuc(yol));

        Assert.Empty(kalan);
        Assert.False(File.Exists(yol));
        Assert.False(File.Exists(iz));
        Assert.True(File.Exists(baska));
    }

    [Fact]
    public void BasarisizKayitBasligiBasarisizDer()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"), ok: false, cikis: 1)));

        Assert.Equal(Baslik("recorder.output.done-failed"), ekran.Baslik);
        Assert.NotEqual(Baslik("recorder.output.done"), ekran.Baslik);
    }

    [Fact]
    public void YarimKayitBasligiYarimDer()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"), yarim: true, oynar: true)));

        Assert.Equal(Baslik("recorder.output.done-partial"), ekran.Baslik);
    }

    [Fact]
    public void BasariliKayitBasligiBittiDer()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"))));

        Assert.Equal(Baslik("recorder.output.done"), ekran.Baslik);
        Assert.False(ekran.UyariGorunur);
    }

    [Fact]
    public void UcBaslikMetniAyri()
    {
        var bitti = Baslik("recorder.output.done");
        Assert.NotEqual(bitti, Baslik("recorder.output.done-failed"));
        Assert.NotEqual(bitti, Baslik("recorder.output.done-partial"));
    }

    [KilitFact]
    public void TasinamayanParcaGercekYoluVeSebebiDondurur()
    {
        var parca = Dosya("parca-000.mkv");
        var hedef = Dosya("hedef.mkv");
        (string Path, string? Error) teslim;
        using (new FileStream(hedef, FileMode.Open, FileAccess.Read, FileShare.None))
            teslim = RecorderSession.Deliver(parca, hedef);

        Assert.Equal(parca, teslim.Path);
        Assert.False(string.IsNullOrEmpty(teslim.Error));
        Assert.True(File.Exists(teslim.Path));
    }

    [Fact]
    public void TasinanParcaHedefYoluDondurur()
    {
        var parca = Dosya("parca-000.mkv");
        var hedef = Path.Combine(_klasor, "hedef.mkv");

        var teslim = RecorderSession.Deliver(parca, hedef);

        Assert.Equal(hedef, teslim.Path);
        Assert.Null(teslim.Error);
        Assert.True(File.Exists(hedef));
    }

    [KilitFact]
    public void BolumlerdenBiriTasinamazsaListeGercekYoluTasir()
    {
        var p0 = Dosya("parca-000.mkv");
        var p1 = Dosya("parca-001.mkv");
        var h0 = Path.Combine(_klasor, "bolum-1.mkv");
        var h1 = Dosya("bolum-2.mkv");
        IReadOnlyList<string> yollar;
        string? hata;
        using (new FileStream(h1, FileMode.Open, FileAccess.Read, FileShare.None))
            yollar = RecorderSession.DeliverAll(new[] { (p0, h0), (p1, h1) }, out hata);

        Assert.Equal(new[] { h0, p1 }, yollar);
        Assert.NotNull(hata);
    }

    [Fact]
    public void TasimaHatasiUyariSatirindaSoylenir()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "parca-000.mkv"), tasimaHatasi: "kilitli")));

        Assert.Equal(Metin("recorder.output.not-moved", "kilitli"), ekran.Uyari);
        Assert.True(ekran.UyariGorunur);
        Assert.True(ekran.Simge);
    }

    [FfmpegFact]
    public async Task GecerliMatroskaPaketVerir()
    {
        var yol = Path.Combine(_klasor, "gecerli.mkv");
        var run = await FfmpegRunner.RunAsync(new[]
        {
            "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=10", "-t", "1", "-c:v", "ffv1", yol
        });
        Assert.True(run.Ok, run.StandardError);

        Assert.True(await RecorderSession.HasPacketAsync(yol));
    }

    [FfmpegFact]
    public async Task CopDosyaPaketVermez()
    {
        var yol = Path.Combine(_klasor, "cop.mkv");
        var cop = new byte[4096];
        new Random(7).NextBytes(cop);
        File.WriteAllBytes(yol, cop);

        Assert.False(await RecorderSession.HasPacketAsync(yol));
        Assert.False(await RecorderSession.HasPacketAsync(Path.Combine(_klasor, "yok.mkv")));
    }

    [Fact]
    public void YoklanamayanYarimKayitOynatilabilirDemez()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"), yarim: true, oynar: null)));

        Assert.Equal(Metin("recorder.output.partial-unverified"), ekran.Uyari);
        Assert.NotEqual(Metin("recorder.output.partial"), ekran.Uyari);
    }

    [Fact]
    public void OkunamayanYarimMatroskaOynatilamazDer()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"), yarim: true, oynar: false)));

        Assert.Equal(Metin("recorder.output.partial-broken-mkv"), ekran.Uyari);
    }

    [FfmpegFact]
    public async Task CevrilemeyenGifMkvyiKorurVeSoyler()
    {
        var mkv = Dosya("yakalama.mkv", 4096);
        var gif = Path.Combine(_klasor, "kayit.gif");

        var sonuc = await RecorderSession.ConvertToGifAsync(Sonuc(mkv), gif, 10, CancellationToken.None);

        Assert.False(sonuc.Ok);
        Assert.Equal(gif, sonuc.MissingGif);
        Assert.Equal(mkv, sonuc.OutputPath);
        Assert.True(File.Exists(mkv));
        Assert.False(File.Exists(gif));

        var ekran = Ciz(v => v.ShowResult(sonuc));
        Assert.Equal(Metin("recorder.output.gif-failed", sonuc.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)), ekran.Uyari);
        Assert.Equal(Baslik("recorder.output.done"), ekran.Baslik);
    }

    [Fact]
    public void BasarisizKayitGifUyarisiVermez()
    {
        var ekran = Ciz(v => v.ShowResult(Sonuc(Path.Combine(_klasor, "a.mkv"), ok: false, cikis: 1)));

        Assert.Equal(Metin("recorder.output.failed", "1"), ekran.Uyari);
    }

    private static (string Not, string Hedef) TamponNotu(RecorderContainer kap)
        => AyarDosyasiyla(ayarYolu =>
        {
            File.WriteAllText(ayarYolu, "{\"containerFormat\":\"" + kap + "\"}");
            return AppHost.Run(() =>
            {
                var view = new RecorderView(ayarYolu);
                var tampon = new Tampon();
                view.ReplayStarter = (_, _, _) => Task.FromResult<IReplayBuffer>(tampon);
                Assert.True(view.StartReplayAsync().GetAwaiter().GetResult(), view.ErrorText);
                view.SaveReplayAsync().GetAwaiter().GetResult();
                var not = view.NoticeText;
                view.StopReplayAsync().GetAwaiter().GetResult();
                return (not, tampon.Hedef ?? string.Empty);
            });
        });

    [FfmpegFact]
    public void GifSeciliykenTamponMkvOldugunuSoyler()
    {
        var (not, hedef) = TamponNotu(RecorderContainer.Gif);

        Assert.EndsWith(".mkv", hedef);
        Assert.Equal(Metin("recorder.replay.saved-mkv", hedef), not);
    }

    [FfmpegFact]
    public void MkvSeciliykenTamponDuzKaydedildiDer()
    {
        var (not, hedef) = TamponNotu(RecorderContainer.Mkv);

        Assert.Equal(Metin("recorder.replay.saved", hedef), not);
        Assert.NotEqual(Metin("recorder.replay.saved", ""), Metin("recorder.replay.saved-mkv", ""));
    }

    private sealed class Tampon : IReplayBuffer
    {
        public int Seconds => 15;

        public string? Hedef { get; private set; }

        public Task<ReplaySaveResult> SaveAsync(string target, CancellationToken ct = default)
        {
            Hedef = target;
            return Task.FromResult(new ReplaySaveResult(true, target, 8, string.Empty));
        }

        public Task StopAsync() => Task.CompletedTask;
    }
}
