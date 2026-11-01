using VidShrink.Core;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using VidShrink.App.Playback;
using VidShrink.Player;
using Xunit;

namespace VidShrink.Tests;

public sealed class OynaticiKurulumTests
{
    private static readonly string Root = TipSources.Root;

    private static string Oku(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    private static string Sabit(string text, string name)
    {
        var match = Regex.Match(text, "\\$" + name + " = '([^']+)'");
        Assert.True(match.Success, $"{name} Install-VidShrink.ps1 icinde yok");
        return match.Groups[1].Value;
    }

    private static string KurulumKoku(string name)
    {
        var path = Path.Combine(MotorKanit.Folder, "konum-" + name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(Path.Combine(path, "app"));
        return path;
    }

    /// <summary>Son asertten sonra: yeşil koşum kurduğu sahte kurulumu siler, kırmızı koşum bırakır.</summary>
    private static void Kapat(params string[] adlar)
        => KanitKapanisi.Kapat(MotorKanit.Folder, adlar.Select(a => "konum-" + a).ToArray());

    [Fact]
    public void KurulanDuzendeLibmpvKokunToolsKlasorundenBulunur()
    {
        var kok = KurulumKoku("kurulu");
        var dll = Path.Combine(kok, "tools", "libmpv", LibMpvLocator.FileNames[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllBytes(dll, new byte[] { 0 });

        var ilk = LibMpvLocator.Candidates(null, Path.Combine(kok, "app"), macOs: false).First(File.Exists);

        Assert.Equal(Path.GetFullPath(dll), Path.GetFullPath(ilk));
        Kapat("kurulu");
    }

    [Fact]
    public void YayinKlasorundekiLibmpvKokunToolsKlasorundenOnceGelir()
    {
        var kok = KurulumKoku("yayin");
        var yanindaki = Path.Combine(kok, "app", LibMpvLocator.FileNames[0]);
        var tools = Path.Combine(kok, "tools", "libmpv", LibMpvLocator.FileNames[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        File.WriteAllBytes(yanindaki, new byte[] { 0 });
        File.WriteAllBytes(tools, new byte[] { 0 });

        var ilk = LibMpvLocator.Candidates(null, Path.Combine(kok, "app"), macOs: false).First(File.Exists);

        Assert.Equal(Path.GetFullPath(yanindaki), Path.GetFullPath(ilk));
        Kapat("yayin");
    }

    [Fact]
    public void OrtamDegiskeniKurulanDuzendenOnceGelir()
    {
        var kok = KurulumKoku("ortam");
        var tools = Path.Combine(kok, "tools", "libmpv", LibMpvLocator.FileNames[0]);
        var ozel = Path.Combine(kok, "ozel", LibMpvLocator.FileNames[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(tools)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ozel)!);
        File.WriteAllBytes(tools, new byte[] { 0 });
        File.WriteAllBytes(ozel, new byte[] { 0 });

        var ilk = LibMpvLocator.Candidates(Path.GetDirectoryName(ozel), Path.Combine(kok, "app"), macOs: false).First(File.Exists);

        Assert.Equal(Path.GetFullPath(ozel), Path.GetFullPath(ilk));
        Kapat("ortam");
    }

    [Fact]
    public void MacosHomebrewKlasorleriUygulamaKlasorlerindenSonraDenenir()
    {
        var kok = KurulumKoku("mac");
        var adaylar = LibMpvLocator.Candidates(null, Path.Combine(kok, "app"), macOs: true);
        var ortamli = LibMpvLocator.Candidates(Path.Combine(kok, "yok.dylib"), Path.Combine(kok, "app"), macOs: true);
        var windows = LibMpvLocator.Candidates(null, Path.Combine(kok, "app"), macOs: false);

        var homebrew = adaylar.ToList().FindIndex(a => a.StartsWith("/opt/homebrew/lib", StringComparison.Ordinal));
        var sonUygulama = adaylar.ToList().FindLastIndex(a => a.StartsWith(kok, StringComparison.Ordinal));
        Assert.True(homebrew > sonUygulama, $"homebrew {homebrew}, son uygulama adayi {sonUygulama}");
        Assert.Contains(adaylar, a => a.StartsWith("/usr/local/lib", StringComparison.Ordinal));
        Assert.DoesNotContain(ortamli, a => a.StartsWith("/opt/homebrew/lib", StringComparison.Ordinal));
        Assert.DoesNotContain(windows, a => a.StartsWith("/opt/homebrew/lib", StringComparison.Ordinal));
        Kapat("mac");
    }

    [Fact]
    public void WindowsKurulumuCiIleAyniSabitLibmpvyiDogrulayipToolsAltinaKoyar()
    {
        var kurulum = Oku("Install-VidShrink.ps1");
        var ci = Oku(".github", "workflows", "ci.yml");
        var release = Oku(".github", "workflows", "release.yml");

        var url = Sabit(kurulum, "libMpvUrl");
        var yedek = Sabit(kurulum, "libMpvFallbackUrl");
        var arsiv = Sabit(kurulum, "libMpvArchiveSha256");
        var dll = Sabit(kurulum, "libMpvDllSha256");
        var arsivAdi = url[(url.LastIndexOf('/') + 1)..];
        var surum = Regex.Match(arsivAdi, @"^mpv-dev-x86_64-(\d{8})-").Groups[1].Value;
        Assert.Equal($"https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-{surum}/{arsivAdi}", url);
        Assert.StartsWith($"https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/{surum}/", yedek, StringComparison.Ordinal);
        Assert.Equal(arsivAdi, yedek[(yedek.LastIndexOf('/') + 1)..]);
        Assert.Contains("foreach ($source in @($libMpvUrl, $libMpvFallbackUrl))", kurulum, StringComparison.Ordinal);
        Assert.Equal(new[] { url, yedek }, VidShrink.Core.Setup.LibMpvPin.Default.Urls);
        foreach (var (ad, metin) in new[] { ("ci.yml", ci), ("release.yml", release) })
        {
            Assert.True(metin.Contains($"$url = '{url}'", StringComparison.Ordinal), $"{ad} libmpv birincil adresi farkli");
            Assert.True(metin.Contains($"$fallbackUrl = '{yedek}'", StringComparison.Ordinal), $"{ad} libmpv yedek adresi farkli");
            Assert.True(metin.Contains("foreach ($source in @($url, $fallbackUrl))", StringComparison.Ordinal), $"{ad} once kendi surumumuzu denemiyor");
            Assert.True(metin.Contains($"$expectedSha256 = '{arsiv}'", StringComparison.Ordinal), $"{ad} libmpv arsiv sha256 farkli");
            Assert.True(metin.Contains($"$expectedDllSha256 = '{dll}'", StringComparison.Ordinal), $"{ad} libmpv-2.dll sha256 farkli");
        }

        Assert.Contains("Join-Path $stageRoot 'tools\\libmpv'", kurulum, StringComparison.Ordinal);
        Assert.Contains("Install-LibMpv $workRoot $libMpvRoot $libMpvExisting", kurulum, StringComparison.Ordinal);
        Assert.Contains("$actual -ne $libMpvArchiveSha256", kurulum, StringComparison.Ordinal);
        Assert.Contains("$dllActual -ne $libMpvDllSha256", kurulum, StringComparison.Ordinal);
    }

    /// <summary>
    /// libmpv <c>vulkan-1.dll</c>'yi doğrudan içe aktarıyor; eski sürücünün yükleyicisinde
    /// <c>vkGetPhysicalDeviceProperties2</c> yok. Kurucu, kurucu exe ve CI aynı Khronos
    /// yükleyicisini aynı sağlamayla libmpv'nin yanına koyar.
    /// </summary>
    [Fact]
    public void VulkanYukleyicisiUcYerdeAyniSabitleLibmpvYaninaKonur()
    {
        var kurulum = Oku("Install-VidShrink.ps1");
        var ci = Oku(".github", "workflows", "ci.yml");
        var release = Oku(".github", "workflows", "release.yml");
        var x64 = VidShrink.Core.Setup.VulkanLoaderPin.X64;
        var arm64 = VidShrink.Core.Setup.VulkanLoaderPin.Arm64;

        Assert.Same(x64, VidShrink.Core.Setup.LibMpvPin.X64.Vulkan);
        Assert.Same(arm64, VidShrink.Core.Setup.LibMpvPin.Arm64.Vulkan);
        var libmpvSurum = Regex.Match(Sabit(kurulum, "libMpvUrl"), @"/(deps-libmpv-\d{8})/").Groups[1].Value;
        Assert.Equal($"https://github.com/Teknesyum/VidShrink-Player/releases/download/{libmpvSurum}/vulkan-1-x86_64-1.4.357.0.zip", x64.Url);
        Assert.Equal($"https://github.com/Teknesyum/VidShrink-Player/releases/download/{libmpvSurum}/vulkan-1-aarch64-1.4.357.0.zip", arm64.Url);
        Assert.NotEqual(x64.DllSha256, arm64.DllSha256);

        Assert.Equal(x64.Url, Sabit(kurulum, "vulkanLoaderZipUrl"));
        Assert.Equal(x64.ZipSha256.ToUpperInvariant(), Sabit(kurulum, "vulkanLoaderZipSha256"));
        Assert.Equal(x64.DllSha256.ToUpperInvariant(), Sabit(kurulum, "vulkanLoaderDllSha256"));
        Assert.Equal(arm64.Url, Sabit(kurulum, "vulkanLoaderArm64ZipUrl"));
        Assert.Equal(arm64.ZipSha256.ToUpperInvariant(), Sabit(kurulum, "vulkanLoaderArm64ZipSha256"));
        Assert.Equal(arm64.DllSha256.ToUpperInvariant(), Sabit(kurulum, "vulkanLoaderArm64DllSha256"));
        foreach (var (ad, metin) in new[] { ("ci.yml", ci), ("release.yml", release) })
        {
            Assert.True(metin.Contains($"$vulkanUrl = '{x64.Url}'", StringComparison.Ordinal), $"{ad} vulkan adresi farkli");
            Assert.True(metin.Contains($"$expectedVulkanZipSha256 = '{x64.ZipSha256.ToUpperInvariant()}'", StringComparison.Ordinal), $"{ad} vulkan zip sha256 farkli");
            Assert.True(metin.Contains($"$expectedVulkanDllSha256 = '{x64.DllSha256.ToUpperInvariant()}'", StringComparison.Ordinal), $"{ad} vulkan-1.dll sha256 farkli");
            var vulkan = metin.IndexOf("$vulkanUrl", StringComparison.Ordinal);
            var ortam = metin.IndexOf("VIDSHRINK_LIBMPV=", vulkan, StringComparison.Ordinal);
            Assert.True(ortam > vulkan, $"{ad} vulkan-1.dll libmpv ortam degiskeninden sonra iniyor");
        }

        Assert.Contains("Install-VulkanLoader $workRoot $libMpvRoot $vulkanExisting", kurulum, StringComparison.Ordinal);
        Assert.Contains("$dllActual -ne $vulkanLoaderDllSha256", kurulum, StringComparison.Ordinal);
        Assert.Contains("$script:vulkanLoaderDllSha256 = $vulkanLoaderArm64DllSha256", kurulum, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eksik giriş noktalı bağımlılık: <c>msimg32.dll</c> kopyasının <c>GDI32.dll</c> içe aktarımı
    /// yanındaki <c>GDZ32.dll</c>'ye (aslında <c>version.dll</c>) çevrilir. Yükleme 0x7F ile düşer,
    /// iletişim kutusu yerine hata döner, iş parçacığının hata kipi eski haline gelir.
    /// Kutu açılsaydı yükleme beklerdi; 20 sn'lik sınır bunu kırmızıya çevirir.
    /// </summary>
    [Fact]
    public void EksikGirisNoktasiIletisimKutusuAcmadanHataDoner()
    {
        if (!OperatingSystem.IsWindows()) return;
        var kok = KurulumKoku("giris-noktasi");
        var sistem = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var ikili = File.ReadAllBytes(Path.Combine(sistem, "msimg32.dll"));
        var eski = System.Text.Encoding.ASCII.GetBytes("GDI32.dll\0");
        var yeni = System.Text.Encoding.ASCII.GetBytes("GDZ32.dll\0");
        var degisen = 0;
        for (var i = 0; i + eski.Length <= ikili.Length; i++)
        {
            if (!ikili.AsSpan(i, eski.Length).SequenceEqual(eski)) continue;
            yeni.CopyTo(ikili, i);
            degisen++;
        }
        Assert.True(degisen > 0, "msimg32.dll GDI32.dll adini tasimiyor");
        var sonda = Path.Combine(kok, "vidshrink-sonda.dll");
        File.WriteAllBytes(sonda, ikili);
        File.Copy(Path.Combine(sistem, "version.dll"), Path.Combine(kok, "GDZ32.dll"));

        uint once = 0, sonra = 0, icerde = 0;
        var sonuc = false;
        string? hata = null;
        var is_ = new Thread(() =>
        {
            LoaderErrorMode.SetThreadErrorMode(0, out _);
            once = LoaderErrorMode.Current;
            using (LoaderErrorMode.Suppress()) icerde = LoaderErrorMode.Current;
            sonuc = LibMpvLocator.TryLoadQuietly(sonda, out var tutamac, out hata);
            if (sonuc) NativeLibrary.Free(tutamac);
            sonra = LoaderErrorMode.Current;
        }) { IsBackground = true };
        is_.Start();

        Assert.True(is_.Join(TimeSpan.FromSeconds(20)), "yukleme 20 sn icinde donmedi: sistem iletisim kutusu acik kalmis olabilir");
        Assert.False(sonuc, "eksik giris noktali kitaplik yuklendi");
        Assert.Contains("0x8007007F", hata ?? "", StringComparison.Ordinal);
        Assert.Equal(0u, once);
        Assert.Equal(LoaderErrorMode.FailCriticalErrors | LoaderErrorMode.NoOpenFileErrorBox, icerde);
        Assert.Equal(once, sonra);
        Kapat("giris-noktasi");
    }

    [Fact]
    public void MacosKurulumuSabitlenmisMpvkitLibmpvyiToolsAltinaKoyar()
    {
        var kurulum = Oku("install-vidshrink.sh");
        var kilit = Oku("tools", "mpvkit-macos", "mpvkit-1.0.0.lock");

        var cikti = Regex.Match(kilit, @"^# output (\S+) ([0-9a-f]{64}) (\S+)[ \t]*\r?$", RegexOptions.Multiline);
        Assert.True(cikti.Success, "kilit dosyasinda # output satiri yok");
        var ad = cikti.Groups[1].Value;
        var sha = cikti.Groups[2].Value;
        var url = cikti.Groups[3].Value;

        Assert.Equal($"https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-macos-mpvkit-1.0.0/{ad}", url);
        Assert.Contains($"mac_libmpv_url='{url}'", kurulum, StringComparison.Ordinal);
        Assert.Contains($"mac_libmpv_sha256='{sha}'", kurulum, StringComparison.Ordinal);
        Assert.Contains("\"$stage_root/tools/libmpv/libmpv.2.dylib\"", kurulum, StringComparison.Ordinal);
        Assert.Contains("cp -R \"$payload/.\" \"$bundle/Contents/MacOS/\"", Oku("macos-app-bundle.sh"), StringComparison.Ordinal);
        var macos = Path.Combine(KurulumKoku("macos-paket"), AppIdentity.MacBundleName, "Contents", "MacOS");
        Assert.Contains(Path.Combine(macos, "tools", "libmpv"), LibMpvLocator.AppDirectories(macos));
        Kapat("macos-paket");
    }

    [Fact]
    public void MacosAltSurumuDortBelgedeOnDortDiyor()
    {
        var beklenen = new (string Dosya, string Dize)[]
        {
            ("README.md", "macOS 14 or newer"),
            ("README.tr.md", "macOS 14 ve"),
            (Path.Combine("docs", "kurulum.md"), "macOS 14 or newer"),
            (Path.Combine("docs", "kurulum.tr.md"), "macOS 14 ve üstü"),
            (Path.Combine("docs", "YOL-HARITASI.md"), "macOS alt sürümü: 14"),
        };

        foreach (var (dosya, dize) in beklenen)
        {
            var metin = Oku(dosya);
            Assert.Contains(dize, metin, StringComparison.Ordinal);
            var eski = Regex.Matches(metin, @"macOS 15 (or newer|ve üstü|ve\b)|macOS alt sürümü: 15");
            Assert.True(eski.Count == 0, $"{dosya} hala macOS 15 tabani soyluyor: {string.Join(" | ", eski.Select(m => m.Value))}");
        }

        Assert.Contains("MPVKit yolu denendi ve tuttu", Oku(Path.Combine("docs", "YOL-HARITASI.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void MacosKurucusuOnceBrewuYokluyorVeBelgeAyniSiraiAnlatiyor()
    {
        var kurulum = Oku("install-vidshrink.sh");
        var govde = kurulum[kurulum.IndexOf("require_libmpv() {", StringComparison.Ordinal)..];
        var brew = govde.IndexOf("if has_libmpv; then", StringComparison.Ordinal);
        var indirme = govde.IndexOf("download_mac_libmpv", StringComparison.Ordinal);
        Assert.True(brew >= 0 && indirme > brew, $"kurucu once brew'u yoklamiyor: has_libmpv {brew}, download_mac_libmpv {indirme}");

        var ingilizce = Oku(Path.Combine("docs", "kurulum.md"));
        var turkce = Oku(Path.Combine("docs", "kurulum.tr.md"));
        Assert.Contains("first looks for a Homebrew libmpv", ingilizce, StringComparison.Ordinal);
        Assert.Contains("Homebrew `mpv` when already installed", ingilizce, StringComparison.Ordinal);
        Assert.Contains("önce Homebrew libmpv'sine bakar", turkce, StringComparison.Ordinal);
        Assert.DoesNotContain("On macOS it downloads libmpv itself", ingilizce, StringComparison.Ordinal);
        Assert.DoesNotContain("macOS'ta libmpv'yi kurucu kendisi indirir", turkce, StringComparison.Ordinal);
    }

    [Fact]
    public void KurucuYayinSorgusundaJetonuIsteneSeKullanir()
    {
        var kurulum = Oku("install-vidshrink.sh");

        Assert.Contains("github_token=${GITHUB_TOKEN:-${GH_TOKEN:-}}", kurulum, StringComparison.Ordinal);
        Assert.Contains("${github_token:+-H \"Authorization: Bearer $github_token\"}", kurulum, StringComparison.Ordinal);

        var jeton = kurulum.IndexOf("Authorization: Bearer", StringComparison.Ordinal);
        var api = kurulum.IndexOf("https://api.github.com/repos/$repository/releases/latest", StringComparison.Ordinal);
        Assert.True(jeton > 0 && api > jeton && api - jeton < 200, $"jeton basligi api cagrisina bagli degil: jeton {jeton}, api {api}");
        Assert.DoesNotContain("Authorization: Bearer $github_token\" \\\n    \"https://github.com", kurulum, StringComparison.Ordinal);
    }

    [Fact]
    public void GuncellemeDenetimiYayinListesiNumaralandirmaz()
    {
        var kaynaklar = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories);
        Assert.True(kaynaklar.Length > 50, $"src altinda {kaynaklar.Length} kaynak bulundu");

        foreach (var yol in kaynaklar)
        {
            var metin = File.ReadAllText(yol);
            var ad = Path.GetRelativePath(Root, yol);
            Assert.False(metin.Contains("per_page", StringComparison.Ordinal), $"{ad} sayfali yayin listesi cagiriyor (per_page)");
            foreach (var m in Regex.Matches(metin, @"/releases(?<son>/latest|/tag/|/download/|)").Cast<Match>())
                Assert.True(m.Groups["son"].Value.Length > 0, $"{ad} yayin listesini numaralandiriyor: {metin.Substring(Math.Max(0, m.Index - 40), Math.Min(80, metin.Length - Math.Max(0, m.Index - 40)))}");
        }

        var denetim = Oku("src", "VidShrink.Core", "UpdateCheck.cs");
        Assert.Contains("releases/latest", denetim, StringComparison.Ordinal);
    }

    [Fact]
    public void UnixKurulumuLibmpvYoksaKomutuSoyleyipDurur()
    {
        var kurulum = Oku("install-vidshrink.sh");

        var ffmpeg = kurulum.IndexOf("\nrequire_ffmpeg\n", StringComparison.Ordinal);
        var libmpv = kurulum.IndexOf("\nrequire_libmpv\n", StringComparison.Ordinal);
        var indirme = kurulum.IndexOf("\nwork_root=", StringComparison.Ordinal);
        Assert.True(ffmpeg > 0 && libmpv > ffmpeg && indirme > libmpv, $"sira: ffmpeg {ffmpeg}, libmpv {libmpv}, indirme {indirme}");
        Assert.Contains("brew install mpv", kurulum, StringComparison.Ordinal);
        Assert.Contains("sudo apt install libmpv2", kurulum, StringComparison.Ordinal);
        Assert.Contains("libmpv\\.so\\.2", kurulum, StringComparison.Ordinal);
        foreach (var dir in LibMpvLocator.MacLibraryDirectories)
            Assert.Contains(dir, kurulum, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lua'sız libmpv'de <c>ytdl</c> seçeneği yok; yalnız o seçeneğin "bulunamadı" dönüşü yutulur.
    /// Önceden motor hiç açılmıyordu (kaynaktan Lua'sız derlenen 0.40, "option ytdl=no failed:
    /// option not found"). Başka seçenekte ya da başka hatada motor yine reddedilir (olumsuz kontrol).
    /// </summary>
    [Fact]
    public void DerlemedeOlmayanSecenekMotoruDusurmez()
    {
        Assert.True(MpvEngine.Absent("ytdl", -5));
        Assert.False(MpvEngine.Absent("ytdl", -7));
        Assert.False(MpvEngine.Absent("vo", -5));
        Assert.False(MpvEngine.Absent("hwdec", -5));
        Assert.All(MpvEngine.OptionalOptions, ad => Assert.Contains(MpvEngine.OptionsFor(new PlaybackOptions()), o => o.Name == ad));
    }

    /// <summary>
    /// Linux'ta ses çıkışı SDL'e inmez: ses sunucusu olmayan oturumda oynatıcı ile karşılaştırma
    /// motorları aynı anda açılınca SDL'in ses açılışı süreci düşürüyordu (gdb: libmpv `core` →
    /// libSDL2 → 0x0; iki eş zamanlı iletimde 3/5). Sıra yalnız Linux'ta yazılır, ses sunucusu
    /// olan oturumda varsayılanla aynı sürücü (pulse) seçilir.
    /// </summary>
    [Fact]
    public void LinuxSesCikisiSdleInmez()
    {
        var secenekler = MpvEngine.OptionsFor(new PlaybackOptions());
        var ao = secenekler.Where(o => o.Name == "ao").Select(o => o.Value).ToArray();
        if (!OperatingSystem.IsLinux())
        {
            Assert.Empty(ao);
            return;
        }

        var surucu = Assert.Single(ao).Split(',');
        Assert.DoesNotContain("sdl", surucu);
        Assert.DoesNotContain("", surucu);
        Assert.Equal("pulse", surucu[0]);
        Assert.Contains(("audio-fallback-to-null", "yes"), secenekler);
    }
}

public sealed class OynaticiGorunumuFabrikaTests
{
    [Fact]
    public void AcilamayanMotorAtilirVeGorunumBosKalir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var view = new PlayerView();
            var sahte = new AcilmayanMotor();
            view.EngineFactory = () => sahte;
            var open = view.OpenAsync("yok.mp4");
            var saat = Stopwatch.StartNew();
            while (!open.IsCompleted && saat.Elapsed < TimeSpan.FromSeconds(5))
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            return (open, sahte, view.Engine, bosMetin: view.EngineUnavailableText);
        });

        Assert.True(sonuc.open.IsFaulted, "OpenAsync hatayi yutmamali");
        Assert.IsType<PlaybackOpenException>(sonuc.open.Exception!.InnerException);
        Assert.Equal(1, sonuc.sahte.Acilis);
        Assert.True(sonuc.sahte.Atildi, "acilamayan motor Dispose edilmedi");
        Assert.Equal(0, sonuc.sahte.FaultedAboneleri);
        Assert.Null(sonuc.Item3);
        Assert.Null(sonuc.bosMetin);
    }

    /// <summary>
    /// libmpv bulundu ama yüklenemedi (eski vulkan-1.dll): görünüm boş durum yerine
    /// <c>StatusError</c> temalı Türkçe iletiyi gösterir; anahtarsız hata iletiyi açmaz.
    /// </summary>
    [Fact]
    public void YuklenemeyenMotorHataTemaliIletiyiGosterir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var view = new PlayerView();
            view.EngineFactory = () => throw new PlaybackEngineUnavailableException("libmpv could not be loaded", LibMpvLocator.LoadFailedKey);
            var open = view.OpenAsync("yok.mp4");
            var saat = Stopwatch.StartNew();
            while (!open.IsCompleted && saat.Elapsed < TimeSpan.FromSeconds(5))
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            var tema = Avalonia.Controls.ResourceNodeExtensions.TryFindResource(Avalonia.Application.Current!, "StatusError", out var deger) ? deger : null;
            var beklenen = VidShrink.App.LanguageCatalog.Display(VidShrink.App.Localization.Strings.Get(LibMpvLocator.LoadFailedKey));

            var anahtarsiz = new PlayerView();
            anahtarsiz.EngineFactory = () => throw new PlaybackEngineUnavailableException("libmpv not found");
            var ikinci = anahtarsiz.OpenAsync("yok.mp4");
            while (!ikinci.IsCompleted && saat.Elapsed < TimeSpan.FromSeconds(10))
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            return (open, view.EngineUnavailableText, beklenen, AyniTema: ReferenceEquals(view.TxtEngine.Theme, tema), view.TxtEmpty.IsVisible, ikinci, Anahtarsiz: anahtarsiz.EngineUnavailableText);
        });

        Assert.True(sonuc.open.IsFaulted, "OpenAsync hatayi yutmamali");
        Assert.IsType<PlaybackEngineUnavailableException>(sonuc.open.Exception!.InnerException);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.beklenen));
        Assert.NotEqual(LibMpvLocator.LoadFailedKey, sonuc.beklenen);
        Assert.Equal(sonuc.beklenen, sonuc.EngineUnavailableText);
        Assert.True(sonuc.AyniTema, "ileti StatusError temasinda degil");
        Assert.False(sonuc.IsVisible, "bos durum metni iletinin yaninda gorunuyor");
        Assert.True(sonuc.ikinci.IsFaulted);
        Assert.Null(sonuc.Anahtarsiz);
    }

    private sealed class AcilmayanMotor : IPlaybackEngine
    {
        private EventHandler<PlaybackFault>? _faulted;

        public int Acilis { get; private set; }

        public bool Atildi { get; private set; }

        public int FaultedAboneleri => _faulted?.GetInvocationList().Length ?? 0;

        public string Name => "sahte";

        public bool IsOpen => false;

        public double DurationSeconds => 0;

        public bool HasAudio => false;

        public bool IsPaused => true;

        public bool EndReached => false;

        public double PositionSeconds => 0;

        public double AudioVideoOffsetSeconds => double.NaN;

        public long FramesRendered => 0;

        public event EventHandler<PlaybackFault>? Faulted
        {
            add => _faulted += value;
            remove => _faulted -= value;
        }

        public Task OpenAsync(string path, CancellationToken ct = default)
        {
            Acilis++;
            return Task.FromException(new PlaybackOpenException("sahte motor acilamadi"));
        }

        public void Play() { }

        public void Pause() { }

        public Task<SeekResult> SeekAsync(double seconds, SeekPrecision precision, CancellationToken ct = default)
            => Task.FromResult(new SeekResult(SeekOutcome.Failed, 0));

        public bool TryCopyLatest(ref long seen, FrameCopy copy) => false;

        public void Dispose() => Atildi = true;
    }
}
