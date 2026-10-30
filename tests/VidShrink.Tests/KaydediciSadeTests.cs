using Avalonia;
using Avalonia.Controls;
using VidShrink.App;
using VidShrink.App.Recorder;
using Xunit;
using static VidShrink.Tests.KaydediciAyarTests;

namespace VidShrink.Tests;

public sealed class KaydediciSadeTests
{
    [Fact]
    public void SadeGorunumYalnizHedefSesVeBaslatGosterir()
    {
        var olcu = AyarDosyasiyla(ayarYolu => AppHost.Run(() =>
        {
            var view = new RecorderView(ayarYolu) { SkipAutoMeasure = true, RegionEditorEnabled = false };
            var sade = (view.AdvancedMode,
                hizli: Bul<Control>(view, "RowTargetQuick").IsVisible,
                tur: Bul<Control>(view, "RowTargetKind").IsVisible,
                zaman: Bul<Control>(view, "RowTiming").IsVisible,
                kare: Bul<Control>(view, "StatFrames").IsVisible);
            var seviye = 0;
            view.LevelChanged += () => seviye++;
            Bul<RadioButton>(view, "RadAdvanced").IsChecked = true;
            var gelismis = (view.AdvancedMode,
                hizli: Bul<Control>(view, "RowTargetQuick").IsVisible,
                tur: Bul<Control>(view, "RowTargetKind").IsVisible,
                zaman: Bul<Control>(view, "RowTiming").IsVisible);
            return (sade, gelismis, seviye);
        }));

        Assert.False(olcu.sade.AdvancedMode);
        Assert.True(olcu.sade.hizli);
        Assert.False(olcu.sade.tur);
        Assert.False(olcu.sade.zaman);
        Assert.False(olcu.sade.kare);
        Assert.True(olcu.gelismis.AdvancedMode);
        Assert.False(olcu.gelismis.hizli);
        Assert.True(olcu.gelismis.tur);
        Assert.True(olcu.gelismis.zaman);
        Assert.True(olcu.seviye > 0);
    }

    [Fact]
    public void KompaktHedefEskiPencereninOrtasinaOturur()
    {
        var alan = new PixelRect(0, 0, 1920, 1040);
        Assert.Equal(new PixelRect(640, 160, 640, 720), KaydediciKompakt.Hedef(alan, alan, 640, 720));
        Assert.Equal(new PixelRect(0, 0, 640, 720), KaydediciKompakt.Hedef(new PixelRect(-500, -500, 200, 200), alan, 640, 720));
        Assert.Equal(new PixelRect(1280, 320, 640, 720), KaydediciKompakt.Hedef(new PixelRect(1800, 900, 400, 400), alan, 640, 720));
        Assert.Equal(new PixelRect(0, 0, 800, 600), KaydediciKompakt.Hedef(new PixelRect(0, 0, 800, 600), new PixelRect(0, 0, 800, 600), 1000, 900));
    }

    [Fact]
    public void YumusatmaUclardaSabitAradaArtar()
    {
        Assert.Equal(0, KaydediciKompakt.Yumusat(0));
        Assert.Equal(1, KaydediciKompakt.Yumusat(1));
        Assert.Equal(1, KaydediciKompakt.Yumusat(2));
        Assert.True(KaydediciKompakt.Yumusat(0.5) > 0.5);
        var a = new PixelRect(0, 0, 1920, 1040);
        var b = new PixelRect(640, 160, 640, 720);
        Assert.Equal(a, KaydediciKompakt.Ara(a, b, 0));
        Assert.Equal(b, KaydediciKompakt.Ara(a, b, 1));
        Assert.Equal(new PixelRect(320, 80, 1280, 880), KaydediciKompakt.Ara(a, b, 0.5));
    }
}
