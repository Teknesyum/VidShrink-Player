using System.Diagnostics;
using Avalonia.Threading;
using VidShrink.App.Recorder;
using VidShrink.App.Share;
using VidShrink.Core;
using VidShrink.Core.Editing;
using Xunit;

namespace VidShrink.Tests;

/// <summary>
/// later.md #41, EKB-25: kaydedicinin son kayıtları. Liste sahte kayıt klasöründen okunur; süre, küçük resim, açma, klasörde
/// gösterme ve çöpe taşıma sahte; ffmpeg koşmaz, dosya çöpe gitmez. Geçici dosyalar <c>.calisma/test-ciktilari/kayit-gecmisi/</c>.
/// </summary>
public sealed class KayitGecmisiTests : IDisposable
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _klasor = Path.Combine(TestPaths.OutputRoot, "kayit-gecmisi", Guid.NewGuid().ToString("N")[..8]);
    private readonly DateTime _simdi = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    public KayitGecmisiTests() => Directory.CreateDirectory(_klasor);

    public void Dispose()
    {
        try { Directory.Delete(_klasor, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Dosya(string ad, double saatOnce, int bayt = 10)
    {
        var yol = Path.Combine(_klasor, ad);
        File.WriteAllBytes(yol, new byte[bayt]);
        File.SetLastWriteTimeUtc(yol, _simdi.AddHours(-saatOnce));
        return yol;
    }

    /// <summary>
    /// Yalnız kaydedicinin adlandırdığı ve yazdığı kaplar listeye girer, en yenisi başta; kareler, başka videolar ve
    /// tanınmayan uzantı girmez. Sınır en yenileri tutar; olmayan klasör boş liste verir.
    /// </summary>
    [Fact]
    public void ListeYalnizKayitlariEnYeniBastaVerir()
    {
        var a = Dosya("kayit_a.mp4", 3);
        var b = Dosya("kayit_b.mkv", 1);
        var c = Dosya("kayit_c.gif", 2);
        var e = Dosya("kayit_e.MOV", 4);
        Dosya("kare_x.png", 0);
        Dosya("baska.mp4", 0);
        Dosya("kayit_d.txt", 0);

        Assert.Equal(new[] { b, c, a, e }, RecentRecordings.List(_klasor, 10).Select(k => k.Path));
        Assert.Equal(new[] { b, c, a }, RecentRecordings.List(_klasor, 3).Select(k => k.Path));
        Assert.Equal(_simdi.AddHours(-1), RecentRecordings.List(_klasor, 1)[0].WrittenUtc);
        Assert.Empty(RecentRecordings.List(Path.Combine(_klasor, "yok"), 10));
        Assert.Empty(RecentRecordings.List(_klasor, 0));
    }

    private sealed class Sahte : IThumbnailSource
    {
        public readonly List<string> Acilan = new();
        public readonly List<string> Gosterilen = new();
        public Func<string, Task<byte[]?>> Resim = _ => Task.FromResult<byte[]?>(null);
        public Action<string> Cop = File.Delete;

        public Task<byte[]?> ReadAsync(string source, long sourceTick, int height, CancellationToken ct) => Resim(source);
    }

    private RecordingHistoryWindow Pencere(Sahte sahte, Func<string, CancellationToken, Task<double>> sure)
        => new(new RecordingHistorySources(
            () => RecentRecordings.List(_klasor, 20),
            sure,
            sahte,
            yol => { sahte.Acilan.Add(yol); return Task.CompletedTask; },
            sahte.Gosterilen.Add,
            yol => sahte.Cop(yol),
            new ShareSession(() => null)));

    /// <summary>
    /// Satırlar önce boyut ve tarihle görünür; süre ve küçük resim sonra gelir. Süresi okunamayan kayıt süresiz,
    /// resmi gelmeyen kayıt resimsiz kalır; satır düşmez.
    /// </summary>
    [Fact]
    public void SatirSureBoyutVeResimTasir()
    {
        var a = Dosya("kayit_a.mp4", 2, 2048);
        var b = Dosya("kayit_b.mp4", 1, 10);
        var sonuc = AppHost.Run(() =>
        {
            var sahte = new Sahte { Resim = yol => Task.FromResult<byte[]?>(yol == a ? Png : null) };
            var kapi = new TaskCompletionSource();
            var pencere = Pencere(sahte, async (yol, _) =>
            {
                await kapi.Task;
                return yol == a ? 65d : throw new IOException("bozuk");
            });
            try
            {
                pencere.Show();
                var once = pencere.DetailOf(a);
                kapi.SetResult();
                Dongu(() => pencere.Loading.IsCompleted, 30);
                return (Sira: pencere.ShownPaths.ToArray(), Once: once, A: pencere.DetailOf(a), B: pencere.DetailOf(b),
                    ResimA: pencere.HasThumbnail(a), ResimB: pencere.HasThumbnail(b), Secili: pencere.Selected);
            }
            finally
            {
                pencere.Close();
            }
        });

        var sure = Saat.Ekran(TimeSpan.FromSeconds(65));
        Assert.Equal(new[] { b, a }, sonuc.Sira);
        Assert.DoesNotContain(sure, sonuc.Once);
        Assert.StartsWith(sure + " · ", sonuc.A);
        Assert.Contains(Bicim.Boyut.Bayt(2048, VidShrink.App.Localization.Strings.Culture), sonuc.A);
        Assert.Contains(Bicim.Boyut.Bayt(10, VidShrink.App.Localization.Strings.Culture), sonuc.B);
        Assert.DoesNotContain(sure, sonuc.B);
        Assert.True(sonuc.ResimA);
        Assert.False(sonuc.ResimB);
        Assert.Equal(b, sonuc.Secili);
    }

    /// <summary>
    /// Çöpe taşınan kayıt listeden düşer, seçim bir alttaki kayda geçer ve durum satırı söyler. Taşınamayan kayıt yerinde
    /// kalır, liste değişmez, neden durum satırındadır.
    /// </summary>
    [Fact]
    public void CopeTasinanDuserTasinamayanKalir()
    {
        var a = Dosya("kayit_a.mp4", 3);
        var b = Dosya("kayit_b.mp4", 2);
        var c = Dosya("kayit_c.mp4", 1);
        var sonuc = AppHost.Run(() =>
        {
            var sahte = new Sahte();
            var pencere = Pencere(sahte, (_, _) => Task.FromResult(1d));
            try
            {
                pencere.Select(b);
                pencere.TrashSelected();
                var tasindi = (Liste: pencere.ShownPaths.ToArray(), Secili: pencere.Selected, Durum: pencere.StatusText);
                sahte.Cop = _ => throw new IOException("kilitli");
                pencere.TrashSelected();
                return (Tasindi: tasindi, Liste: pencere.ShownPaths.ToArray(), Secili: pencere.Selected, Durum: pencere.StatusText);
            }
            finally
            {
                pencere.Close();
            }
        });

        Assert.Equal(new[] { c, a }, sonuc.Tasindi.Liste);
        Assert.Equal(a, sonuc.Tasindi.Secili);
        Assert.Contains("kayit_b.mp4", sonuc.Tasindi.Durum);
        Assert.False(File.Exists(b));
        Assert.Equal(new[] { c, a }, sonuc.Liste);
        Assert.Equal(a, sonuc.Secili);
        Assert.Contains("kayit_a.mp4", sonuc.Durum);
        Assert.NotEqual(sonuc.Tasindi.Durum.Replace("kayit_b.mp4", ""), sonuc.Durum.Replace("kayit_a.mp4", ""));
        Assert.True(File.Exists(a));
    }

    /// <summary>Çöpe taşınan kaydın imleç izi de çöpe gider; başka kaydın izi yerinde kalır. Kayıt taşınamazsa izi de kalır.</summary>
    [Fact]
    public void CopeTasinanKaydinImlecIziDeGider()
    {
        Dosya("kayit_a.mp4", 2);
        var b = Dosya("kayit_b.mp4", 1);
        var izA = Dosya("kayit_a" + PointerTrack.Suffix, 2);
        var izB = Dosya("kayit_b" + PointerTrack.Suffix, 1);
        var tasinan = AppHost.Run(() =>
        {
            var sahte = new Sahte();
            var liste = new List<string>();
            sahte.Cop = yol => { liste.Add(yol); File.Delete(yol); };
            var pencere = Pencere(sahte, (_, _) => Task.FromResult(1d));
            try
            {
                pencere.Select(b);
                pencere.TrashSelected();
                sahte.Cop = _ => throw new IOException("kilitli");
                pencere.TrashSelected();
                return liste.ToArray();
            }
            finally
            {
                pencere.Close();
            }
        });

        Assert.Equal(new[] { b, izB }, tasinan);
        Assert.False(File.Exists(izB));
        Assert.True(File.Exists(izA));
    }

    /// <summary>Seçili kayıt oynatıcıya verilir ve pencere kapanır; klasöründen kalkmış kayıt verilmez, durum satırı söyler.</summary>
    [Fact]
    public void AcmaVarOlanKaydiOynaticiyaVerir()
    {
        var a = Dosya("kayit_a.mp4", 2);
        var b = Dosya("kayit_b.mp4", 1);
        var sonuc = AppHost.Run(() =>
        {
            var sahte = new Sahte();
            var pencere = Pencere(sahte, (_, _) => Task.FromResult(1d));
            File.Delete(b);
            pencere.OpenSelectedAsync().GetAwaiter().GetResult();
            var kayip = (Acilan: sahte.Acilan.ToArray(), Durum: pencere.StatusText, Liste: pencere.ShownPaths.ToArray(), Acik: pencere.IsVisible);
            pencere.Show();
            pencere.OpenSelectedAsync().GetAwaiter().GetResult();
            return (Kayip: kayip, Acilan: sahte.Acilan.ToArray(), Acik: pencere.IsVisible);
        });

        Assert.Empty(sonuc.Kayip.Acilan);
        Assert.Contains("kayit_b.mp4", sonuc.Kayip.Durum);
        Assert.Equal(new[] { a }, sonuc.Kayip.Liste);
        Assert.Equal(new[] { a }, sonuc.Acilan);
        Assert.False(sonuc.Acik);
    }

    /// <summary>Kaydedicinin düğmesi pencereyi ayardaki kayıt klasörüyle açar; ikinci basış ikinci pencere açmaz.</summary>
    [Fact]
    public void KaydediciDugmesiKlasoruOkur()
    {
        var a = Dosya("kayit_a.mp4", 1);
        Dosya("baska.mp4", 0);
        using var ayar = new KaydediciAyarTests.OzelAyar();
        var kayit = RecorderSettings.Load(ayar.Yol);
        kayit.OutputFolder = _klasor;
        kayit.Save(ayar.Yol);
        var sonuc = AppHost.Run(() =>
        {
            var view = new RecorderView(ayar.Yol) { ResultThumbnails = new Sahte(), QuickTrimMeasure = (_, _) => Task.FromResult(1d) };
            var pencere = view.ShowHistory();
            try
            {
                return (Liste: pencere.ShownPaths.ToArray(), Ayni: ReferenceEquals(pencere, view.ShowHistory()));
            }
            finally
            {
                pencere.Close();
            }
        });

        Assert.Equal(new[] { a }, sonuc.Liste);
        Assert.True(sonuc.Ayni);
    }

    private static void Dongu(Func<bool> bitti, int saniye)
    {
        var saat = Stopwatch.StartNew();
        while (!bitti() && saat.Elapsed < TimeSpan.FromSeconds(saniye)) Dispatcher.UIThread.RunJobs();
        Assert.True(bitti(), "pencere işi süresinde bitmedi");
    }
}
