using System.Globalization;
using VidShrink.Core;
using VidShrink.Ffmpeg;

public static class YakinlastirmaTitremesi
{
    private const int En = 640;
    private const int Boy = 360;
    private const int Hiz = 30;
    private const double Sure = 4;
    private const double KutuX = 320;
    private const double KutuY = 180;

    public static async Task<int> RunAsync(string[] args)
    {
        var klasor = Path.Combine(Environment.CurrentDirectory, ".calisma", "yakinlastirma-titremesi");
        var filtreler = new List<(string Ad, string Filtre)>();
        for (var i = 1; i < args.Length; i++)
            if (args[i] == "--out" && i + 1 < args.Length) klasor = args[++i];
            else if (args[i] == "--filter" && i + 1 < args.Length) filtreler.Add(("ozel" + filtreler.Count, args[++i]));

        Directory.CreateDirectory(klasor);
        var bolgeler = new[] { new ZoomRegion(0.3, 1.9, 0.45, 0.45), new ZoomRegion(2.1, 3.7, 0.55, 0.55) };
        const double olcek = 2;
        var kaynak = Path.Combine(klasor, "kaynak.mkv");
        var uret = await FfmpegRunner.RunAsync(new[]
        {
            "-hide_banner", "-y", "-nostdin", "-f", "lavfi",
            "-i", Fmt($"color=c=black:s={En}x{Boy}:r={Hiz}:d={Sure},drawbox=x={KutuX - 4}:y={KutuY - 4}:w=8:h=8:color=white:t=fill"),
            "-c:v", "ffv1", "-pix_fmt", "yuv420p", "-threads", "2", kaynak
        });
        if (!uret.Ok)
        {
            Console.Error.WriteLine(FfmpegRunner.Tail(uret.StandardError));
            return 1;
        }

        filtreler.Insert(0, ("zoompan", AutoZoom.ZoomPanFilter(bolgeler, olcek, En, Boy, Hiz)));
        filtreler.Insert(1, ("perspective", AutoZoom.Filter(bolgeler, olcek, En, Boy, Hiz)));
        Console.WriteLine(Fmt($"kaynak {En}x{Boy} {Hiz} fps {Sure} s, kutu 8x8 merkez=({KutuX},{KutuY}), olcek={olcek}, bolgeler={string.Join(" ", bolgeler.Select(b => Fmt($"[{b.Start}-{b.End} ({b.CenterX},{b.CenterY})]")))}"));
        foreach (var (ad, filtre) in filtreler)
        {
            var ham = Path.Combine(klasor, ad + ".raw");
            var run = await FfmpegRunner.RunAsync(new[]
            {
                "-hide_banner", "-y", "-nostdin", "-i", kaynak, "-vf", filtre, "-threads", "2",
                "-f", "rawvideo", "-pix_fmt", "gray", ham
            });
            if (!run.Ok)
            {
                Console.WriteLine($"{ad}: ffmpeg dustu: {FfmpegRunner.Tail(run.StandardError)}");
                continue;
            }

            var bayt = File.ReadAllBytes(ham);
            var kare = bayt.Length / (En * Boy);
            var hatalar = new List<(double T, double Ex, double Ey, string Kol)>();
            for (var n = 0; n < kare; n++)
            {
                var t = n / (double)Hiz;
                var (z, cx, cy) = AutoZoom.At(bolgeler, olcek, t);
                var x0 = Math.Clamp(cx * En - En / z / 2, 0, En - En / z);
                var y0 = Math.Clamp(cy * Boy - Boy / z / 2, 0, Boy - Boy / z);
                var beklenenX = (KutuX - x0) * z;
                var beklenenY = (KutuY - y0) * z;
                if (Agirlik(bayt, n) is not { } olcu) continue;
                var kol = Kol(bolgeler, t);
                hatalar.Add((t, olcu.X - beklenenX, olcu.Y - beklenenY, kol));
            }

            Console.WriteLine($"{ad}: kare={kare}");
            foreach (var kol in new[] { "rampa", "kaydirma", "sabit" })
            {
                var secim = hatalar.Where(h => h.Kol == kol).ToList();
                if (secim.Count < 3) continue;
                var adim = new List<double>();
                for (var i = 1; i < secim.Count; i++)
                    adim.Add(Math.Max(Math.Abs(secim[i].Ex - secim[i - 1].Ex), Math.Abs(secim[i].Ey - secim[i - 1].Ey)));
                var enBuyuk = secim.Max(h => Math.Max(Math.Abs(h.Ex), Math.Abs(h.Ey)));
                Console.WriteLine(Fmt($"  {kol,-9} kare={secim.Count,3} sapma-en-buyuk={enBuyuk:F2} px  kareden-kareye-sicrama ort={adim.Average():F2} en-buyuk={adim.Max():F2} px"));
            }

            File.WriteAllLines(Path.Combine(klasor, ad + ".tsv"), hatalar.Select(h => Fmt($"{h.T:F3}\t{h.Kol}\t{h.Ex:F3}\t{h.Ey:F3}")));
        }

        await HizAsync(klasor, bolgeler, olcek);
        foreach (var sayi in new[] { 1, 10, 20, 30, 60 })
        {
            var cok = Enumerable.Range(0, sayi).Select(i => new ZoomRegion(i * 3.0, i * 3.0 + 2.5, 0.3 + 0.4 * (i % 2), 0.4)).ToList();
            var tam = AutoZoom.Filter(cok, olcek, 1920, 1080, 30);
            Console.WriteLine(Fmt($"uzunluk bolge={sayi,2} filtre={tam.Length,6} karakter secilen={(tam.StartsWith("fps=", StringComparison.Ordinal) ? "perspective" : "zoompan")} zoompan={AutoZoom.ZoomPanFilter(cok, olcek, 1920, 1080, 30).Length}"));
        }

        return 0;
    }

    private static async Task HizAsync(string klasor, IReadOnlyList<ZoomRegion> bolgeler, double olcek)
    {
        var kaynak = Path.Combine(klasor, "hiz-1080.mkv");
        var uret = await FfmpegRunner.RunAsync(new[]
        {
            "-hide_banner", "-y", "-nostdin", "-f", "lavfi", "-i", Fmt($"testsrc2=s=1920x1080:r={Hiz}:d={Sure}"),
            "-c:v", "ffv1", "-pix_fmt", "yuv420p", "-threads", "2", kaynak
        });
        if (!uret.Ok) return;
        foreach (var (ad, filtre) in new[]
                 {
                     ("yok", "null"),
                     ("zoompan", AutoZoom.ZoomPanFilter(bolgeler, olcek, 1920, 1080, Hiz)),
                     ("perspective", AutoZoom.Filter(bolgeler, olcek, 1920, 1080, Hiz))
                 })
        {
            var saat = System.Diagnostics.Stopwatch.StartNew();
            var run = await FfmpegRunner.RunAsync(new[]
            {
                "-hide_banner", "-y", "-nostdin", "-threads", "2", "-i", kaynak, "-vf", filtre, "-filter_threads", "2", "-f", "null", "-"
            });
            Console.WriteLine(Fmt($"hiz 1920x1080 {Sure}s {ad,-11} ok={run.Ok} sure={saat.Elapsed.TotalSeconds:F2} s ({Sure * Hiz / saat.Elapsed.TotalSeconds:F0} kare/s)"));
        }
    }

    private static string Kol(IReadOnlyList<ZoomRegion> bolgeler, double t)
    {
        var a = bolgeler[0];
        var b = bolgeler[1];
        if (t < a.Start || t >= b.End) return "dis";
        var orta = (a.End + b.Start) / 2;
        if (Math.Abs(t - orta) < AutoZoom.PanSeconds / 2) return "kaydirma";
        if (t < a.Start + AutoZoom.RampSeconds || t > b.End - AutoZoom.RampSeconds) return "rampa";
        return "sabit";
    }

    private static (double X, double Y)? Agirlik(byte[] bayt, int n)
    {
        double toplam = 0, sx = 0, sy = 0;
        var taban = n * En * Boy;
        for (var y = 0; y < Boy; y++)
        for (var x = 0; x < En; x++)
        {
            var v = bayt[taban + y * En + x] - 16.0;
            if (v < 24) continue;
            toplam += v;
            sx += v * (x + 0.5);
            sy += v * (y + 0.5);
        }

        return toplam > 0 ? (sx / toplam, sy / toplam) : null;
    }

    private static string Fmt(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
