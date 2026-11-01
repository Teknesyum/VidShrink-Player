using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Xunit;
using Xunit.Abstractions;

namespace VidShrink.Tests;

/// <summary>
/// Simgenin <b>şekil kimliği</b>. Sınır kutusu ölçüsü (<see cref="IkonKutusuTests"/>) bir gövdenin
/// nereye oturduğunu söyler, ne olduğunu değil: aynı kutuyu dolduran yabancı bir çizim —
/// başka bir Fluent dosyası, aynaya alınmış bir gövde, içi boşaltılmış bir halka — kutu ölçüsünden
/// sessizce geçer. Bu ölçü onu kapatır.
///
/// <para>İmza kutudan bağımsız: 24×24 tasarım karesi 16×16 hücreye bölünüyor, her hücrenin
/// merkezinde dolgu yoklanıyor (<c>FillContains</c>, sabitleyicinin <c>F1</c> NonZero kuralıyla)
/// ve 256 bit satır satır onaltılığa paketleniyor. Ölçülen şey mürekkebin <b>nerede</b> olduğu,
/// kutunun nerede bittiği değil.</para>
///
/// <para>Pim tek tek yazılı, çünkü kimlik ancak bir referansa karşı ölçülebilir. Pimin kendisi
/// de ölçülü: <see cref="ImzalarBirbirindenUzak"/> bütün çiftlerin birbirine olan Hamming
/// uzaklığını sayar, böylece <see cref="Slop"/> toleransının iki simgeyi karıştıracak kadar
/// gevşek olmadığı iddia değil ölçü olur.</para>
///
/// <para><see cref="Slop"/> = 2 bit: Bezier düzleştirmesi bir hücre merkezini kıl payı kenarın
/// içine ya da dışına düşürebilir. Takımdaki <b>en yakın</b> iki simge —
/// <c>IconVolume</c> ile <c>IconVolumeMute</c>, aynı hoparlör gövdesi, farkı yalnız dalgalar ile
/// çarpı — 18 Eylül 2026'da 14 bit ölçüldü; tolerans onun yedide biri.
/// <see cref="EnYakinCift"/> bu payın altına inmeyi kırmızı yapar.</para>
/// </summary>
public sealed class IkonImzaTests
{
    private const int Izgara = 16;
    private const int Slop = 2;
    private const int EnYakinCift = 10;

    private readonly ITestOutputHelper _cikti;

    public IkonImzaTests(ITestOutputHelper cikti) => _cikti = cikti;

    private static readonly Dictionary<string, string> Pim = new(StringComparer.Ordinal)
    {
        ["IconPlayer"] = "000001800ff01ff83ffc3dfc3c7c7c1e7c1e3c7c3dfc3ffc1ff80ff001800000",
        ["IconShrink"] = "0000000000c400c800d000e000fc007c3e003f0007000b001300230000000000",
        ["IconConvert"] = "00000040006000301ff80010002000400200040008001ff80c00060002000000",
        ["IconRecorder"] = "0000000000001f803fcc7fde7fde7fde7fde7fde7fde3fcc1f80000000000000",
        ["IconEditor"] = "000008100c300420026003400180018003c00e703e7c224422443e7c0c300000",
        ["IconAdvanced"] = "00000000007000f07ffe00f000000000000000000f007ffe0f000e0000000000",
        ["IconAbout"] = "000001800ff01ff83ffc3e7c3ffc7ffe7ffe3ffc3ffc3ffc1ff80ff001800000",
        ["IconSettings"] = "0000000003c013c81ff83ffc3e7c1c381c383e7c3ffc1ff813c803c000000000",
        ["IconPlay"] = "000000000c001f001fc01ff01ff81ffc1ffc1ff81ff01fc01f000c0000000000",
        ["IconPause"] = "000000001e781e781e781e781e781e781e781e781e781e781e781e7800000000",
        ["IconRewind"] = "000000000000030c073c0f7c3ffc7ffc7ffc3ffc0f7c073c030c000000000000",
        ["IconFastForward"] = "00000000000030c03ce03ef03ffc3ffe3ffe3ffc3ef03ce030c0000000000000",
        ["IconVolume"] = "0000000000c001c003cc3fc47fd47fca7fca7fd43fc403cc01c000c000000000",
        ["IconVolumeMute"] = "0000000000c001c003c03fc07fd67fdc7fdc7fd63fc003c001c000c000000000",
        ["IconSpeedFast"] = "000001800ff01998300c2014207471ee718e20042004300c1818000000000000",
        ["IconSpeedFastTip"] = "0000000000000000000000100030002000000000000000000000000000000000",
        ["IconSpeedSlow"] = "000001800ff01998300c28042e04778e718e20042004300c1818000000000000",
        ["IconFullScreen"] = "000000001c38300c20042004000000000000000020042004300c1c3800000000",
        ["IconChevronDown"] = "00000000000000000000100818180c30066003c0018000000000000000000000",
        ["IconChevronUp"] = "00000000000000000000018003c006600c301818100800000000000000000000",
        ["IconStop"] = "000000003ffc3ffc3ffc3ffc3ffc3ffc3ffc3ffc3ffc3ffc3ffc3ffc00000000",
        ["IconRestart"] = "00000000200c203c207c21fc23fc27fc27fc23fc21fc207c203c200c00000000",
        ["IconClose"] = "0000000000001008081004200240018001800240042008101008000000000000",
        ["IconMaximize"] = "000000001ff8300c20042004200420042004200420042004300c1ff800000000",
        ["IconMinimize"] = "00000000000000000000000000003ffc3ffc0000000000000000000000000000",
        ["IconCamera"] = "0000000007e01ff83ffc7e7e7dbe7bde7bde7bde7c3e7ffe7ffe3ffc00000000",
        ["IconEyeOff"] = "000040002000100009e01c1812482304278407c003e000100008000400020000",
        ["IconBookmark"] = "000000000ff01ff81ff81ff81ff81ff81ff81ff81ff81ff81e781c3810080000",
        ["IconRestore"] = "000007f80c0c00063fe27ff27ff27ff27ff27ff27ff67ff47ff03ff01fe00000",
        ["IconCoffee"] = "000014801680096009201ff03ff83ffe3ffa3ffa3ffe3ff01ff00fe003800000",
        ["IconLock"] = "000003c00420042004200ff01ff81ff81ff81e781e781ff81ff81ff80ff00000",
        ["IconWarning"] = "0000018003c003c007e00f700f701f781f783ffc3e7c7ffe7ffe3ffc00000000",
    };

    internal static string Imza(string yol)
    {
        var geo = Geometry.Parse(yol);
        var adim = 24.0 / Izgara;
        var yazi = new StringBuilder(Izgara * Izgara / 4);
        for (var sy = 0; sy < Izgara; sy++)
        {
            var kume = 0;
            for (var sx = 0; sx < Izgara; sx++)
            {
                kume <<= 1;
                if (geo.FillContains(new Point((sx + 0.5) * adim, (sy + 0.5) * adim))) kume |= 1;
                if ((sx & 3) == 3) { yazi.Append(kume.ToString("x1")); kume = 0; }
            }
        }
        return yazi.ToString();
    }

    private static int Uzaklik(string a, string b)
    {
        if (a.Length != b.Length) return int.MaxValue;
        var sayi = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var fark = Convert.ToInt32(a[i].ToString(), 16) ^ Convert.ToInt32(b[i].ToString(), 16);
            sayi += System.Numerics.BitOperations.PopCount((uint)fark);
        }
        return sayi;
    }

    /// <summary>Her simgenin dolgu imzası pimiyle aynı; en çok <see cref="Slop"/> bit oynayabilir.</summary>
    [Theory]
    [MemberData(nameof(IkonKutusuTests.Ikonlar), MemberType = typeof(IkonKutusuTests))]
    public void SekilKimligiPimiyleAyni(string ad, string yol)
    {
        AppHost.Ensure();
        var imza = AppHost.Run(() => Imza(yol));
        Assert.True(Pim.TryGetValue(ad, out var pim), $"{ad}: şekil kimliği pimi yok.");
        var uzaklik = Uzaklik(imza, pim!);
        _cikti.WriteLine($"IMZA\t{ad}\t{imza}\tpim {pim}\tuzaklik {uzaklik}");
        Assert.True(uzaklik <= Slop, $"{ad}: gövde pimlenen şekil değil, {uzaklik} bit ayrışıyor.\nölçülen {imza}\npim      {pim}");
    }

    /// <summary>Pim tablosu katalogla birebir: ne fazla ne eksik ad.</summary>
    [Fact]
    public void PimTablosuKataloglaBirebir()
    {
        var katalog = IkonKutusuTests.Ikonlar().Select(o => (string)o[0]).ToHashSet(StringComparer.Ordinal);
        var pim = Pim.Keys.ToHashSet(StringComparer.Ordinal);
        _cikti.WriteLine($"katalog {katalog.Count} ad, pim {pim.Count} ad");
        Assert.True(katalog.SetEquals(pim),
            "Pimi olmayan: " + string.Join(", ", katalog.Except(pim))
            + " | Katalogda olmayan pim: " + string.Join(", ", pim.Except(katalog)));
    }

    /// <summary>
    /// Toleransın ölçüsü: hiçbir iki simge birbirine <see cref="EnYakinCift"/> bitten yakın değil,
    /// yani <see cref="Slop"/> bir simgeyi başka bir simgeye asla karıştıramaz.
    /// </summary>
    [Fact]
    public void ImzalarBirbirindenUzak()
    {
        AppHost.Ensure();
        var takim = AppHost.Run(() => IkonKutusuTests.Ikonlar()
            .Select(o => ((string)o[0], Imza((string)o[1]))).ToList());

        var enYakin = int.MaxValue;
        var cift = string.Empty;
        for (var i = 0; i < takim.Count; i++)
            for (var j = i + 1; j < takim.Count; j++)
            {
                var d = Uzaklik(takim[i].Item2, takim[j].Item2);
                if (d < enYakin) { enYakin = d; cift = $"{takim[i].Item1} ~ {takim[j].Item1}"; }
            }

        _cikti.WriteLine($"en yakin cift {cift} uzaklik {enYakin} bit (slop {Slop})");
        Assert.True(enYakin >= EnYakinCift, $"En yakın çift {cift} yalnız {enYakin} bit ayrı; tolerans {Slop} ile kimlik ölçüsü güvenilmez.");
    }
}
