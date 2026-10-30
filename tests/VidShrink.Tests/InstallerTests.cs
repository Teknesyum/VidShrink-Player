using System.Text;
using System.Text.RegularExpressions;
using VidShrink.Core;
using VidShrink.Core.Setup;

namespace VidShrink.Tests;

/// <summary>
/// T45: kurulum betikleri kaynaktan derlemeyi bıraktı, yayınlanmış arşivi indiriyor.
/// Betikler bu makinede koşturulmaz — koştukları anda buradaki kurulumu silip yeniden
/// kurarlar. Ölçüm metin üzerinden: indirilen varlık adları güncelleyicinin beklediği
/// adlarla aynı mı, kurulan düzen başlatıcının beklediği düzen mi, SDK indirme izi
/// kaldı mı.
/// </summary>
public sealed class InstallerTests
{
    private static readonly string Root = TipSources.Root;

    private static string WindowsInstaller => File.ReadAllText(Path.Combine(Root, "Install-VidShrink.ps1"));
    private static string UnixInstaller => File.ReadAllText(Path.Combine(Root, "install-vidshrink.sh"));
    private static string ReleaseWorkflow => File.ReadAllText(Path.Combine(Root, ".github", "workflows", "release.yml"));
    private static string LauncherProgram => File.ReadAllText(Path.Combine(Root, "src", "VidShrink.Launcher", "Program.cs"));

    /// <summary>
    /// Varlık adı tek bir yerde tanımlı: <see cref="UpdateCheck"/>. Betikteki ad kabuk
    /// değişkeniyle kuruluyor, o yüzden karşılaştırma sabit parçalar üzerinden yapılıyor —
    /// <c>UpdateCheck</c> tarafındaki bir ad değişikliği bu ölçümü düşürür.
    /// </summary>
    private static (string Prefix, string Suffix) Split(string template)
    {
        var index = template.IndexOf("RID", StringComparison.Ordinal);
        return (template[..index], template[(index + 3)..]);
    }

    [Fact]
    public void TheArchiveNameTheScriptsFetchIsTheNameTheUpdaterExpects()
    {
        var (prefix, suffix) = Split(UpdateCheck.ArchiveAssetName("RID"));

        Assert.Contains($"\"{prefix}$runtimeIdentifier{suffix}\"", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains($"\"{prefix}$runtime{suffix}\"", UnixInstaller, StringComparison.Ordinal);
        Assert.Contains($"{prefix}${{{{ matrix.rid }}}}{suffix}", ReleaseWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheManifestNameTheWorkflowWritesIsTheNameTheUpdaterExpects()
    {
        var (prefix, suffix) = Split(UpdateCheck.ManifestAssetName("RID"));

        Assert.Contains($"{prefix}${{{{ matrix.rid }}}}{suffix}", ReleaseWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseCarriesTheLauncherForWindows()
    {
        Assert.Contains("src/VidShrink.Launcher/VidShrink.Launcher.csproj", ReleaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("vidshrink-launcher-${{ matrix.rid }}.zip", ReleaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("checksummed=\"$checksummed vidshrink-launcher-${{ matrix.rid }}.zip\"", ReleaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("vidshrink-launcher-$runtimeIdentifier.zip", WindowsInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowsInstallerLaysOutWhatTheLauncherLooksFor()
    {
        // Başlatıcı kendi klasörünün altında app\ arar ve ffmpeg'i tools\ffmpeg'de bekler.
        Assert.Contains("\"app\"", LauncherProgram, StringComparison.Ordinal);
        Assert.Contains("\"tools\", \"ffmpeg\"", LauncherProgram, StringComparison.Ordinal);

        Assert.Contains("Join-Path $stageRoot 'app'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Join-Path $stageRoot 'tools\\ffmpeg'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Join-Path $resolvedInstallRoot 'VidShrinkPlayer.exe'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("app\\VidShrinkPlayer.App.exe", WindowsInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShortcutsPointAtTheLauncherAndNotAtTheApplication()
    {
        foreach (var line in WindowsInstaller.Split('\n'))
        {
            if (!Regex.IsMatch(line, @"\.TargetPath\s*=")) continue;
            Assert.Contains("$installedExe", line, StringComparison.Ordinal);
        }

        Assert.Contains("$installedExe = Join-Path $resolvedInstallRoot 'VidShrinkPlayer.exe'", WindowsInstaller, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dotnet publish")]
    [InlineData("dotnet-install")]
    [InlineData("--list-sdks")]
    [InlineData("archive/refs/heads/main")]
    public void NeitherScriptBuildsFromSourceAnyMore(string trace)
    {
        Assert.DoesNotContain(trace, WindowsInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain(trace, UnixInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void BothScriptsVerifyWhatTheyDownloaded()
    {
        Assert.Contains("checksums-$runtimeIdentifier.txt", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("checksums-$runtime.txt", UnixInstaller, StringComparison.Ordinal);
        Assert.Contains("sha256_of", UnixInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void BothScriptsWriteTheVersionMarker()
    {
        Assert.Contains(UpdateCheck.VersionMarkerName, WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains(UpdateCheck.VersionMarkerName, UnixInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// Windows PowerShell 5.1'in <c>-Encoding UTF8</c> anahtarı bayt sırası işareti yazar.
    /// İşareti okuyan taraf <see cref="UpdateCheck.ReadVersionMarker"/>; işaretli dosyayı da
    /// temiz okuduğu burada ölçülüyor, yoksa kurulan sürüm hiçbir sürümle eşleşmez.
    /// </summary>
    [Fact]
    public void AVersionMarkerWrittenWithAByteOrderMarkStillReadsClean()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vidshrink-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, UpdateCheck.VersionMarkerName),
                "0.1.0",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Assert.Equal("0.1.0", UpdateCheck.ReadVersionMarker(directory));
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// A4: yayın altı hedef taşıyor, arm64 de içinde. Betikler desteklenmeyen mimaride
    /// yanlış arşivi kurmak yerine duruyor; durmazlarsa kurulum çalışır ama güncelleme
    /// hiç bulunmaz. Yayımlanan mimarilerin adı betiklerde geçmek zorunda.
    /// </summary>
    [Fact]
    public void AnUnsupportedArchitectureStopsTheInstallation()
    {
        Assert.Contains("Bu mimari için yayın yok", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Bu mimari için yayın yok", UnixInstaller, StringComparison.Ordinal);
        Assert.Contains("win-arm64", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", UnixInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("win-x86", WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// A4: arm64 makinede güncelleyici arm64 paketini seçmeli. Karar, RID ve varlık adı
    /// tek zincir: mimari okuması <c>ARM64</c> olduğunda manifest ve arşiv adları arm64
    /// yayınını gösteriyor ve o RID yayın matrisinde bulunuyor. Zincirin herhangi bir
    /// halkası x64'e düşerse arm64 makine x64 paketini indirir ve emülasyonda koşar.
    /// </summary>
    [Theory]
    [InlineData("windows", "win-arm64")]
    [InlineData("linux", "linux-arm64")]
    [InlineData("macos", "osx-arm64")]
    public void AnArm64MachineChoosesTheArm64Package(string platform, string beklenenRid)
    {
        var karar = ArchitectureChoice.Decide("ARM64", null, null, true);
        Assert.Equal("arm64", karar.Architecture);
        Assert.Equal(ArchitectureOutcome.Read, karar.Outcome);

        var rid = UpdateCheck.RidFor(platform, karar.Architecture);
        Assert.Equal(beklenenRid, rid);
        Assert.Contains(rid, UpdateCheck.ReleasedRids);
        Assert.Equal($"manifest-{beklenenRid}.json", UpdateCheck.ManifestAssetName(rid));
        Assert.Equal($"vidshrink-{beklenenRid}.zip", UpdateCheck.ArchiveAssetName(rid));
    }

    /// <summary>
    /// Aynı zincirin x64 ucu: arm64 eklenirken x64 makinelerin arm64 paketine kaymadığı
    /// da ölçülüyor.
    /// </summary>
    [Fact]
    public void AnX64MachineStillChoosesTheX64Package()
    {
        var karar = ArchitectureChoice.Decide("X64", null, null, true);
        Assert.Equal("x64", karar.Architecture);
        Assert.Equal("win-x64", UpdateCheck.RidFor("windows", karar.Architecture));
        Assert.Equal("linux-x64", UpdateCheck.RidFor("linux", karar.Architecture));
    }

    /// <summary>
    /// A4: kurucu exe'si arm64'te de arm64 RID'ini seçiyor, desteklenmeyen mimaride
    /// duruyor. Setup exe'si x64 kalıp emülasyonla koştuğu için karar
    /// <see cref="ArchitectureChoice"/> okumasından geliyor, kendi mimarisinden değil.
    /// </summary>
    [Fact]
    public void TheSetupExeChoosesTheArm64RuntimeIdentifierOnArm64()
    {
        var gunluk = new List<string>();
        Assert.Equal("win-arm64", SetupRunner.RuntimeIdentifier(
            ArchitectureChoice.Decide("ARM64", null, null, true), gunluk.Add));
        Assert.Equal("win-x64", SetupRunner.RuntimeIdentifier(
            ArchitectureChoice.Decide("X64", null, null, true), gunluk.Add));

        var hata = Assert.Throws<SetupException>(() => SetupRunner.RuntimeIdentifier(
            ArchitectureChoice.Decide("X86", null, null, true), gunluk.Add));
        Assert.Equal(SetupText.Get("setup.arch.unsupported", "x86"), hata.Message);
    }

    /// <summary>
    /// A4: kurucu arm64'te arm64 bağımlılıklarını çekiyor. x64 pinleri aarch64 dosyasına
    /// eşit olmamalı; olursa arm64 makineye x64 libmpv/ffmpeg iner ve oynatıcı hiç açılmaz.
    /// </summary>
    [Fact]
    public void TheDependencyPinsFollowTheArchitecture()
    {
        Assert.Equal(LibMpvPin.Arm64, LibMpvPin.For("arm64"));
        Assert.Equal(LibMpvPin.Arm64, LibMpvPin.For("ARM64"));
        Assert.Equal(LibMpvPin.X64, LibMpvPin.For("x64"));
        Assert.Equal(FfmpegPin.Arm64, FfmpegPin.For("arm64"));
        Assert.Equal(FfmpegPin.X64, FfmpegPin.For("x64"));

        Assert.NotEqual(LibMpvPin.X64.DllSha256, LibMpvPin.Arm64.DllSha256);
        Assert.NotEqual(LibMpvPin.X64.ArchiveSha256, LibMpvPin.Arm64.ArchiveSha256);
        Assert.NotEqual(FfmpegPin.X64.Url, FfmpegPin.Arm64.Url);
        Assert.Contains("aarch64", LibMpvPin.Arm64.Urls[0], StringComparison.Ordinal);
        Assert.Contains("winarm64", FfmpegPin.Arm64.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// BtbN'in <c>latest</c> etiketi her gün üstüne yazılıyor: o etiketten sabitlemek
    /// sabitleme değil. Pin tarihli bir autobuild etiketinde durmalı.
    /// </summary>
    [Fact]
    public void TheArm64FfmpegPinDoesNotUseAFloatingTag()
    {
        Assert.DoesNotContain("/download/latest/", FfmpegPin.Arm64.Url, StringComparison.Ordinal);
        Assert.Matches(@"/download/autobuild-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}/", FfmpegPin.Arm64.Url);
        Assert.Equal(2, FfmpegPin.Arm64.Entries.Count);
        foreach (var entry in FfmpegPin.Arm64.Entries)
            Assert.Matches("^[0-9a-f]{64}$", entry.Sha256);
    }

    /// <summary>
    /// v0.1.0 başlatıcı taşımıyor. Betik varlığı bulamazsa yarım kurulum bırakmıyor.
    /// </summary>
    [Fact]
    public void AMissingAssetStopsTheInstallationInsteadOfLeavingItHalfDone()
    {
        Assert.Contains("Yayın $tag bu varlığı taşımıyor", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Yayın $tag bu varlığı taşımıyor", UnixInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// T56: bir kullanıcının makinesinde mimari okuması boş döndü ve boş değer
    /// "desteklenmeyen mimari" sayılıp kurulum reddedildi. Karar artık
    /// <see cref="ArchitectureChoice"/> içinde ve betikten ayrı sınanabiliyor;
    /// betiğin kendisi koşturulmuyor, koştuğu anda buradaki kurulumu değiştirir.
    /// </summary>
    [Theory]
    [InlineData("X64")]
    [InlineData("AMD64")]
    [InlineData("x86_64")]
    public void AKnownX64ReadingIsX64(string reading)
    {
        var decision = ArchitectureChoice.Decide(reading, null, null, is64BitOperatingSystem: true);

        Assert.Equal(ArchitectureOutcome.Read, decision.Outcome);
        Assert.Equal("x64", decision.Architecture);
        Assert.Equal(string.Empty, decision.NoteKey);
    }

    [Theory]
    [InlineData("Arm64")]
    [InlineData("aarch64")]
    public void AKnownArm64ReadingIsArm64(string reading)
    {
        var decision = ArchitectureChoice.Decide(reading, null, null, is64BitOperatingSystem: true);

        Assert.Equal(ArchitectureOutcome.Read, decision.Outcome);
        Assert.Equal("arm64", decision.Architecture);
    }

    [Theory]
    [InlineData("X86")]
    [InlineData("i686")]
    public void AKnownX86ReadingIsX86(string reading)
    {
        var decision = ArchitectureChoice.Decide(reading, null, null, is64BitOperatingSystem: false);

        Assert.Equal(ArchitectureOutcome.Read, decision.Outcome);
        Assert.Equal("x86", decision.Architecture);
    }

    /// <summary>
    /// Sözleşmenin sebebi olan durum: hiçbir kaynak ad vermiyor. Reddedilmiyor, varsayılıyor —
    /// ve varsayıldığı söylenecek bir cümle geri geliyor.
    /// </summary>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", null, "")]
    [InlineData("RiscV64", null, "SPARC")]
    public void AnUnreadableArchitectureIsAssumedOn64BitAndSaidOutLoud(
        string? runtime, string? architew6432, string? processor)
    {
        var decision = ArchitectureChoice.Decide(runtime, architew6432, processor, is64BitOperatingSystem: true);

        Assert.Equal(ArchitectureOutcome.Assumed, decision.Outcome);
        Assert.Equal("x64", decision.Architecture);
        Assert.Equal("setup.arch.assumed-x64", decision.NoteKey);
    }

    [Fact]
    public void AnUnreadableArchitectureOn32BitIsX86()
    {
        var decision = ArchitectureChoice.Decide(null, null, null, is64BitOperatingSystem: false);

        Assert.Equal(ArchitectureOutcome.Assumed, decision.Outcome);
        Assert.Equal("x86", decision.Architecture);
        Assert.Equal("setup.arch.assumed-x86", decision.NoteKey);
    }

    /// <summary>
    /// WOW64: 64 bit Windows üzerinde koşan 32 bit bir süreçte PROCESSOR_ARCHITECTURE
    /// sürecin mimarisini söyler, işletim sisteminkini PROCESSOR_ARCHITEW6432 söyler.
    /// Sıra tersine dönerse 64 bit makine x86 sanılır ve kurulum yine düşer.
    /// </summary>
    [Fact]
    public void TheOperatingSystemsArchitectureBeatsTheProcessArchitecture()
    {
        var decision = ArchitectureChoice.Decide(null, "AMD64", "x86", is64BitOperatingSystem: true);

        Assert.Equal(ArchitectureOutcome.Read, decision.Outcome);
        Assert.Equal("x64", decision.Architecture);
    }

    /// <summary>Mimari adı hiçbir sonuçta boş değil; boş bir değer iletiye giremez.</summary>
    [Fact]
    public void TheDecisionNeverCarriesAnEmptyArchitecture()
    {
        foreach (var is64Bit in new[] { true, false })
        foreach (var reading in new string?[] { null, "", "   ", "X64", "ARM64", "X86", "BILINMEYEN" })
            Assert.False(string.IsNullOrWhiteSpace(
                ArchitectureChoice.Decide(reading, null, null, is64Bit).Architecture));
    }

    /// <summary>Güncelleyici ile kurucu aynı kuralı okuyor: <c>Rid</c> kararın kendisini taşır.</summary>
    [Fact]
    public void TheUpdaterUsesTheSameDecision()
    {
        Assert.EndsWith("-" + ArchitectureChoice.Decide().Architecture, UpdateCheck.Rid, StringComparison.Ordinal);
    }

    /// <summary>Betik tek okumaya güvenmiyor: dört kaynağın dördü de içinde geçiyor.</summary>
    [Theory]
    [InlineData("RuntimeInformation]::OSArchitecture")]
    [InlineData("$env:PROCESSOR_ARCHITEW6432")]
    [InlineData("$env:PROCESSOR_ARCHITECTURE")]
    [InlineData("[Environment]::Is64BitOperatingSystem")]
    public void TheWindowsInstallerReadsTheArchitectureFromMoreThanOnePlace(string source)
    {
        Assert.Contains(source, WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// Düşen sürümde ileti <c>$architecture</c> değişkenini basıyordu ve o değişken boştu.
    /// Mimari adı artık yalnız tanınan bir addan geliyor.
    /// </summary>
    [Fact]
    public void TheWindowsInstallerNoLongerPrintsARawArchitectureReading()
    {
        Assert.DoesNotContain("$architecture", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("$($decision.Architecture)", WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>Okunamayan mimari kurulumu durdurmuyor; ne varsayıldığı tek satırla söyleniyor.</summary>
    [Fact]
    public void AnUnreadableArchitectureDoesNotStopTheWindowsInstaller()
    {
        Assert.Contains("Mimari okunamadı; işletim sistemi 64 bit olduğu için x64 varsayıldı.", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains("if ($decision.Note) { Write-Host $decision.Note", WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// K5: aynı tuzak <c>install-vidshrink.sh</c> içinde de vardı — <c>uname -m</c> boş dönerse
    /// boş değer "desteklenmeyen mimari" olarak basılıyordu. Orada da okunamama ayrı söyleniyor.
    /// </summary>
    [Fact]
    public void TheUnixInstallerSeparatesUnreadableFromUnsupported()
    {
        Assert.Contains("uname -m 2>/dev/null", UnixInstaller, StringComparison.Ordinal);
        Assert.Contains("Mimari okunamadı: uname -m ve uname -p boş döndü.", UnixInstaller, StringComparison.Ordinal);
        Assert.Contains("İşletim sistemi okunamadı: uname -s boş döndü.", UnixInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParameterSurfaceOfTheWindowsInstallerIsUnchanged()
    {
        foreach (var parameter in new[] { "$InstallRoot", "$NoLaunch", "$SkipShortcuts" })
            Assert.Contains(parameter, WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// Betik kurucunun <c>--silent</c>, <c>--add-to-path</c>, <c>--remove-settings</c> seceneklerini ayni adla tasir;
    /// PATH sarmali ve kaldirma kaydinin anahtari <see cref="CommandShim"/> ve <see cref="SetupOptions"/> ile ayni.
    /// </summary>
    [Fact]
    public void TheWindowsInstallerCarriesTheSetupOptionsOfTheExe()
    {
        var parameters = WindowsInstaller[..WindowsInstaller.IndexOf("\n)", StringComparison.Ordinal)];
        foreach (var parameter in new[] { "[switch]$Silent", "[switch]$AddToPath", "[switch]$RemoveSettings" })
            Assert.Contains(parameter, parameters, StringComparison.Ordinal);

        Assert.Contains($"'{CommandShim.Line}'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains($"'{CommandShim.FolderName}'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains($"'{CommandShim.FileName}'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains($"'{SetupOptions.DefaultUninstallKey}'", WindowsInstaller, StringComparison.Ordinal);
        Assert.Contains($"'{ShellRegistration.SetupExecutableName}'", WindowsInstaller, StringComparison.Ordinal);
    }

    /// <summary>
    /// Kaldirma kolu kurucunun <c>UnregisterApp</c>'i gibi "Yuklu uygulamalar" kaydini ve PATH'teki <c>bin</c> girdisini
    /// siler; <c>-RemoveSettings</c> verilince ayar klasorunu de.
    /// </summary>
    [Fact]
    public void TheWindowsInstallerUninstallClearsTheEntryAndThePath()
    {
        var start = WindowsInstaller.IndexOf("\nif ($Uninstall) {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = WindowsInstaller.IndexOf("\n    return\n}", start, StringComparison.Ordinal);
        if (end < 0) end = WindowsInstaller.IndexOf("\n    return\r\n}", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var block = WindowsInstaller[start..end];

        Assert.Contains("Remove-UninstallEntry $RegistryRoot", block, StringComparison.Ordinal);
        Assert.Contains("Remove-UserPathEntry $RegistryRoot", block, StringComparison.Ordinal);
        Assert.Contains("if ($RemoveSettings)", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// Betik çevrimdışı arşivi kurucuyla aynı kuralla tanır: işaret dosyası, <c>tools</c> klasörü, hedef uyuşmazlığında durma,
    /// sha256 tutmayanı indirme. Windows PowerShell 5.1 ayrıştıramayacağı <c>&amp;&amp;</c>, <c>||</c> ve üçlü işleç yok.
    /// </summary>
    [Fact]
    public void TheWindowsInstallerReadsTheOfflineArchiveTheSameWayTheExeDoes()
    {
        var script = WindowsInstaller;

        Assert.Contains($"'{OfflineBundle.MarkerFileName}'", script, StringComparison.Ordinal);
        Assert.Contains($"'{OfflineBundle.ToolsFolder}\\ffmpeg'", script, StringComparison.Ordinal);
        Assert.Contains($"'{OfflineBundle.ToolsFolder}\\libmpv'", script, StringComparison.Ordinal);
        Assert.Contains(OfflineBundle.ArchiveName("$Rid"), script, StringComparison.Ordinal);
        Assert.Contains("[string]$OfflineSource", script[..script.IndexOf("\n)", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("$offlineDirectory = $PSScriptRoot", script, StringComparison.Ordinal);
        Assert.Contains("$release = [pscustomobject]@{ tag_name = $offline.Tag; assets = $null }", script, StringComparison.Ordinal);
        Assert.Contains("Get-ReleaseAsset $Tag $Name $Destination", script, StringComparison.Ordinal);
        Assert.Contains("Test-OfflineFile (Join-Path $bundleLibMpvFolder 'vulkan-1.dll') $vulkanLoaderDllSha256", script, StringComparison.Ordinal);

        var code = string.Join('\n', script.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
        Assert.DoesNotContain(" && ", code, StringComparison.Ordinal);
        Assert.DoesNotContain(" || ", code, StringComparison.Ordinal);
    }
}
