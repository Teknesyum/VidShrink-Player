using VidShrink.App.Playback;
using Xunit;

namespace VidShrink.Tests;

public class SeekYumusatmaTests
{
    [Fact]
    public void Kareye_kilitli_konum_cubukta_basamak_olmaz_ve_geri_gitmez()
    {
        var yumusatici = new SeekSmoother();
        var goruldu = new List<double>();
        var now = 0.0;
        yumusatici.Update(0, now, true, 1);
        for (var i = 1; i <= 120; i++)
        {
            now = i / 120.0;
            var motor = Math.Floor(now * 30) / 30;
            goruldu.Add(yumusatici.Update(motor, now, true, 1));
        }

        Assert.All(goruldu.Zip(goruldu.Skip(1)), p => Assert.True(p.Second >= p.First));
        var adimlar = goruldu.Zip(goruldu.Skip(1), (a, b) => b - a).Where(d => d > 0).ToList();
        Assert.True(adimlar.Max() < 0.02, $"en buyuk adim {adimlar.Max():F4} sn");
        Assert.InRange(goruldu[^1], 0.95, 1.05);
    }

    [Fact]
    public void Sarma_ve_duraklatma_aninda_hedefe_oturur()
    {
        var yumusatici = new SeekSmoother();
        yumusatici.Update(1, 0, true, 1);
        yumusatici.Update(1.1, 0.1, true, 1);

        Assert.Equal(8, yumusatici.Update(8, 0.2, true, 1));
        Assert.Equal(3, yumusatici.Update(3, 0.3, false, 1));
        Assert.Equal(3, yumusatici.Update(3, 0.4, true, 1), 1);
    }
}
