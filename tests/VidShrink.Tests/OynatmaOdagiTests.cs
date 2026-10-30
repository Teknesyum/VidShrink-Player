using VidShrink.App.Playback;
using Xunit;

namespace VidShrink.Tests;

public sealed class OynatmaOdagiTests
{
    [Fact]
    public void YeniOynayanOncekiniDuraklatirAyniSahipKendiniDuraklatmaz()
    {
        var oynatici = new object();
        var duzenleyici = new object();
        var oynaticiDurdu = 0;
        var duzenleyiciDurdu = 0;

        OynatmaOdagi.Al(oynatici, () => oynaticiDurdu++);
        OynatmaOdagi.Al(oynatici, () => oynaticiDurdu++);
        Assert.Equal(0, oynaticiDurdu);

        OynatmaOdagi.Al(duzenleyici, () => duzenleyiciDurdu++);
        Assert.Equal(1, oynaticiDurdu);
        Assert.Same(duzenleyici, OynatmaOdagi.Sahip);

        OynatmaOdagi.Birak(oynatici);
        Assert.Same(duzenleyici, OynatmaOdagi.Sahip);
        OynatmaOdagi.Birak(duzenleyici);
        Assert.Null(OynatmaOdagi.Sahip);

        OynatmaOdagi.Al(oynatici, () => oynaticiDurdu++);
        Assert.Equal(0, duzenleyiciDurdu);
        OynatmaOdagi.Birak(oynatici);
    }
}
