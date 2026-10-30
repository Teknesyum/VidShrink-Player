param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\VidShrink Player'),
    [switch]$NoLaunch,
    [switch]$SkipShortcuts,
    [switch]$ShellMenuOnly,
    [switch]$RemoveShellMenu,
    [switch]$RemoveFileAssociation,
    [switch]$Uninstall,
    [switch]$Silent,
    [switch]$AddToPath,
    [switch]$RemoveSettings,
    [ValidateSet('auto', 'tr', 'en')]
    [string]$MenuLanguage = 'auto',
    [string]$RegistryRoot = 'HKCU:\Software\Classes',
    # Yalnız bağımlılık kolu: ffmpeg ve libmpv verilen klasöre indirilip sha256'ları
    # doğrulanır, kurulum yapılmaz. CI bu kolu gerçek bir arm64 koşucusunda koşuyor.
    [string]$DepsOnly,
    # Çevrimdışı arşivin (VidShrinkPlayer-Offline-<rid>.zip) açıldığı klasör. Boşsa betiğin
    # kendi klasörüne bakılır; orada vidshrink-offline.txt varsa araçlar oradan kurulur.
    [string]$OfflineSource,
    # Yalnız negatif kontrol için: beklenen sha256'yı bilerek bozar, doğrulamanın
    # gerçekten durdurduğunu gösterir.
    [string]$DepsSha256Override
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repository = 'Teknesyum/VidShrink-Player'

if ($Silent) { $NoLaunch = $true }

$uninstallKeyName = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\VidShrinkPlayer'
$commandShimFolder = 'bin'
$commandShimFile = 'vidshrink.cmd'
$commandShimLine = '@"%~dp0..\app\vidshrink.exe" %*'
$setupExecutableName = 'VidShrinkPlayer-Setup.exe'

$script:RemoveAttempts = 6
$script:RemoveFirstDelayMilliseconds = 200
$script:RemoveHolderWaitSeconds = 120

function Refresh-ProcessPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Test-WinGet {
    return [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
}

function Require-WinGet {
    if (-not (Test-WinGet)) {
        throw 'WinGet bulunamadı. Windows App Installer bileşenini yükleyip komutu yeniden çalıştırın.'
    }
}

function Install-WinGetPackage([string]$Id) {
    Require-WinGet
    $noApplicableInstaller = -1978335216
    & winget.exe install --id $Id --exact --scope user --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -eq $noApplicableInstaller) {
        Write-Host "$Id kullanıcı kapsamında sunulmuyor, makine kapsamı deneniyor (yönetici onayı isteyebilir)..." -ForegroundColor Yellow
        & winget.exe install --id $Id --exact --silent --accept-package-agreements --accept-source-agreements
    }
    if ($LASTEXITCODE -ne 0) { throw "$Id kurulamadı. WinGet çıkış kodu: $LASTEXITCODE" }
    Refresh-ProcessPath
}

# Aynı mimari adı kaynağa göre başka yazılıyor: .NET 'X64' der, PROCESSOR_ARCHITECTURE
# 'AMD64'. Tanınmayan ad null döner; tanınmamak reddedilmek değildir.
# Bu tablonun eşi UpdateCheck.cs içindeki ArchitectureChoice.Recognize.
function ConvertTo-ArchitectureName([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    switch ($Value.Trim().ToUpperInvariant()) {
        'X64' { return 'x64' }
        'AMD64' { return 'x64' }
        'X86_64' { return 'x64' }
        'EM64T' { return 'x64' }
        'ARM64' { return 'arm64' }
        'AARCH64' { return 'arm64' }
        'X86' { return 'x86' }
        'I386' { return 'x86' }
        'I486' { return 'x86' }
        'I586' { return 'x86' }
        'I686' { return 'x86' }
        'ARM' { return 'arm' }
        'ARMV6L' { return 'arm' }
        'ARMV7L' { return 'arm' }
    }
    return $null
}

# Mimari tek bir okumaya bırakılmıyor. Sıra ve gerekçesi:
# 1. RuntimeInformation.OSArchitecture — işletim sisteminin kendi mimarisi; 64 bit
#    Windows'ta koşan 32 bit bir süreçte bile doğrusunu verir, o yüzden ilk sırada.
#    Ama her makinede okunamıyor: Windows PowerShell 5.1'in altındaki .NET Framework
#    4.7.1'den eskiyse bu tip hiç yoktur, kısıtlı dil kipinde (ConstrainedLanguage,
#    AppLocker/WDAC) statik üyeye erişilemez. İkisinde de elde boş kalır ve kurulumu
#    düşüren okuma buydu.
# 2. PROCESSOR_ARCHITEW6432 — yalnız WOW64 altında dolu ve işletim sisteminin mimarisini
#    söyler. Doluysa bir alttakinden daha doğrudur, o yüzden ondan önce.
# 3. PROCESSOR_ARCHITECTURE — sürecin mimarisi. WOW64 altında 'x86' der, yani tek başına
#    yanıltır; ancak yukarıdaki ikisi susunca kullanılıyor.
# 4. Hiçbiri ad vermezse geriye bit genişliği kalıyor. Bu bir okuma değil varsayım, ve
#    varsayıldığı kullanıcıya söyleniyor.
function Resolve-Architecture {
    $candidates = @()

    try {
        $runtime = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($null -ne $runtime) { $candidates += [string]$runtime }
    }
    catch { }

    $candidates += $env:PROCESSOR_ARCHITEW6432
    $candidates += $env:PROCESSOR_ARCHITECTURE

    foreach ($candidate in $candidates) {
        $name = ConvertTo-ArchitectureName $candidate
        if ($name) {
            return @{ Outcome = 'Read'; Architecture = $name; Note = '' }
        }
    }

    # Bu okuma da engellenebilir. Engellenirse 64 bit varsayılıyor: 32 bit Windows artık
    # yok denecek kadar az ve K3'ün istediği, bilinmeyende durmak değil devam etmek.
    $is64Bit = $true
    try { $is64Bit = [Environment]::Is64BitOperatingSystem }
    catch { }

    if ($is64Bit) {
        return @{
            Outcome = 'Assumed'
            Architecture = 'x64'
            Note = 'Mimari okunamadı; işletim sistemi 64 bit olduğu için x64 varsayıldı.'
        }
    }

    return @{
        Outcome = 'Assumed'
        Architecture = 'x86'
        Note = 'Mimari okunamadı; işletim sistemi 32 bit olduğu için x86 kabul edildi.'
    }
}

# Windows tarafında iki hedef yayımlanıyor: win-x64 ve win-arm64. x86 için yayın yok. Yayının
# kendisi altı hedef taşıyor (osx-arm64, osx-x64, linux-x64, linux-arm64 da var) ama onlar bu
# betiğin işi değil. Desteklenmeyen bir mimari KESİN okunursa burada duruluyor:
# x64 arşivini oraya sessizce kurmak çalışan ama güncellenmeyen bir kurulum bırakır, çünkü
# güncelleyici kendi mimarisinin adını arar (UpdateCheck.Rid) ve o varlık yayında yoktur.
# Mimari okunamadıysa durulmuyor — bilinmeyen ile desteklenmeyen aynı şey değil.
function Get-RuntimeIdentifier {
    $decision = Resolve-Architecture

    if (@('x64', 'arm64') -notcontains $decision.Architecture) {
        if ($decision.Outcome -eq 'Read') {
            throw "Bu mimari için yayın yok: $($decision.Architecture). VidShrink Windows'ta şu an yalnız win-x64 ve win-arm64 için yayımlanıyor."
        }
        throw 'Mimari okunamadı ve işletim sistemi 32 bit görünüyor: win-x64 yayını bu makinede çalışmaz. VidShrink Windows''ta şu an yalnız win-x64 ve win-arm64 için yayımlanıyor.'
    }

    if ($decision.Note) { Write-Host $decision.Note -ForegroundColor Yellow }
    return "win-$($decision.Architecture)"
}

function Find-Tool([string]$Name) {
    $command = Get-Command "$Name.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $winGetLink = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\$Name.exe"
    if (Test-Path -LiteralPath $winGetLink) { return $winGetLink }
    return $null
}

function Get-LatestReleaseTagFromRedirect {
    $request = [Net.HttpWebRequest]::Create("https://github.com/$repository/releases/latest")
    $request.Method = 'HEAD'
    $request.AllowAutoRedirect = $false
    $request.UserAgent = 'VidShrink-Installer'
    $response = $request.GetResponse()
    try { $location = [string]$response.Headers['Location'] } finally { $response.Close() }
    $match = [regex]::Match($location, '/releases/tag/([^/?#]+)')
    if (-not $match.Success) { throw "Son yayın etiketi okunamadı: $location" }
    return [Uri]::UnescapeDataString($match.Groups[1].Value)
}

function Get-LatestRelease {
    $headers = @{ 'User-Agent' = 'VidShrink-Installer'; 'Accept' = 'application/vnd.github+json' }
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri "https://api.github.com/repos/$repository/releases/latest"
        $release = ConvertFrom-Json ([string]$response.Content)
    }
    catch {
        Write-Host "GitHub API yanıt vermedi ($($_.Exception.Message)); son yayın yönlendirmeden okunuyor..." -ForegroundColor Yellow
        $release = [pscustomobject]@{ tag_name = (Get-LatestReleaseTagFromRedirect); assets = $null }
    }
    if (-not $release.tag_name) { throw 'Yayın bilgisi okunamadı: etiket adı yok.' }
    return $release
}

function Get-ReleaseAsset([string]$Tag, [string]$Name, [string]$Destination) {
    $uri = "https://github.com/$repository/releases/download/$Tag/$Name"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $Destination
    }
    catch {
        throw "Yayın varlığı indirilemedi: $Name ($uri). $($_.Exception.Message)"
    }
}

# checksums-<rid>.txt sha256sum biçimindedir: özet, iki boşluk, varlık adı.
function Read-Checksums([string]$Path) {
    $table = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        $match = [regex]::Match($line, '^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$')
        if ($match.Success) { $table[$match.Groups[2].Value] = $match.Groups[1].Value.ToLowerInvariant() }
    }
    return $table
}

function Assert-Checksum([hashtable]$Table, [string]$Name, [string]$Path) {
    if (-not $Table.ContainsKey($Name)) { throw "Sağlama listesinde $Name yok; indirilen dosya doğrulanamıyor." }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Table[$Name]) {
        throw "$Name sağlaması tutmuyor. Beklenen $($Table[$Name]), bulunan $actual. Kurulum durduruldu."
    }
}

function Get-OfflineBundle([string]$Directory, [string]$Rid) {
    if (-not $Directory) { return $null }
    $marker = Join-Path $Directory 'vidshrink-offline.txt'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { return $null }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $marker) {
        $split = $line.IndexOf('=')
        if ($split -gt 0) { $values[$line.Substring(0, $split).Trim()] = $line.Substring($split + 1).Trim() }
    }
    if (-not $values['tag'] -or -not $values['rid']) {
        throw "Çevrimdışı arşivin işaret dosyası okunamadı: $marker. Arşivi yeniden indirin."
    }
    if ($values['rid'] -ne $Rid) {
        throw "Bu çevrimdışı arşiv $($values['rid']) içindir; bu bilgisayar $Rid. VidShrinkPlayer-Offline-$Rid.zip dosyasını indirin."
    }
    return @{ Directory = (Resolve-Path -LiteralPath $Directory).ProviderPath; Tag = $values['tag']; Rid = $values['rid'] }
}

function Test-OfflineFile([string]$Path, [string]$Sha256) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Host "Çevrimdışı arşivde dosya yok, indirilecek: $Path" -ForegroundColor Yellow
        return $false
    }
    $actual = Get-FileSha256 $Path
    if ($actual -ne $Sha256.ToUpperInvariant()) {
        Write-Host "Çevrimdışı arşivdeki $(Split-Path -Leaf $Path) sağlaması tutmuyor (beklenen $Sha256, bulunan $actual); indirilecek." -ForegroundColor Yellow
        return $false
    }
    return $true
}

function Copy-OfflineFfmpeg([hashtable]$Bundle, [object[]]$Entries, [string]$Destination) {
    $folder = Join-Path $Bundle.Directory 'tools\ffmpeg'
    foreach ($item in $Entries) {
        if (-not (Test-OfflineFile (Join-Path $folder $item.Name) $item.Sha256)) { return $null }
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in $Entries) { Copy-Item -LiteralPath (Join-Path $folder $item.Name) -Destination (Join-Path $Destination $item.Name) -Force }
    return @{
        ffmpeg  = (Join-Path $Destination 'ffmpeg.exe')
        ffprobe = (Join-Path $Destination 'ffprobe.exe')
    }
}

function Get-VerifiedReleaseAsset([hashtable]$Bundle, [string]$Tag, [string]$Name, [string]$Destination, [hashtable]$Table) {
    if ($Bundle -and $Table) {
        $local = Join-Path $Bundle.Directory $Name
        if ((Test-Path -LiteralPath $local -PathType Leaf) -and $Table.ContainsKey($Name)) {
            Copy-Item -LiteralPath $local -Destination $Destination -Force
            if ((Get-FileSha256 $Destination) -eq $Table[$Name].ToUpperInvariant()) { return }
            Write-Host "Çevrimdışı arşivdeki $Name sağlaması tutmuyor; indirilecek." -ForegroundColor Yellow
        }
        else {
            Write-Host "Çevrimdışı arşivde $Name yok; indirilecek." -ForegroundColor Yellow
        }
    }
    Get-ReleaseAsset $Tag $Name $Destination
    if ($Table) { Assert-Checksum $Table $Name $Destination }
}

$libMpvUrl = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/mpv-dev-x86_64-20260903-git-69e63f425a.7z'
$libMpvFallbackUrl = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-dev-x86_64-20260903-git-69e63f425a.7z'
$libMpvArchiveSha256 = 'FAC135C68A35B7639E39D72C0C365104EDBAEBDEA39A0DFDD8C36E8C8E80FAEF'
$libMpvDllSha256 = '673E6397920AB64A9C5B3A618F7F16D38854EFE72B58665F1F84E4E873B763A4'
$libMpvFileName = 'libmpv-2.dll'
$libMpvZipUrl = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/libmpv-2-x86_64-20260903.zip'
$libMpvZipSha256 = '1FC71846BD6E63D280E4F52D212F6F80695339E98632698930CF4FFE9F4BDBAD'

# Khronos Vulkan Loader (LunarG VulkanRT 1.4.357.0). libmpv vulkan-1.dll'yi doğrudan içe
# aktarıyor; eski sürücünün System32'deki 1.0 yükleyicisinde vkGetPhysicalDeviceProperties2
# yok ve libmpv yüklenmiyor. libmpv'nin yanına konan yükleyici System32'dekinden önce gelir.
# Sayılar SetupModel.cs'deki VulkanLoaderPin ile aynı.
$vulkanLoaderZipUrl = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/vulkan-1-x86_64-1.4.357.0.zip'
$vulkanLoaderZipSha256 = '38A05198C4BB467BF81DC17731A5A9F7F79147E80FDAE638A18DA23F4D9A8AE9'
$vulkanLoaderDllSha256 = 'CD862090370454630B31B174E3D4EB474FDA38EA034998D1FE1767B0C99A8696'
$vulkanLoaderArm64ZipUrl = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/vulkan-1-aarch64-1.4.357.0.zip'
$vulkanLoaderArm64ZipSha256 = '2495AEA7EF927005CB34609A74A379537F9A154BEB9F308B2FEFD13C5B72C38B'
$vulkanLoaderArm64DllSha256 = 'CC5DD0BEC8A7AFEF013C61DDD511D31500A3B3A45C229150A9AE73EAD708AB76'

# arm64 kolunun pinleri. libmpv'nin aarch64 derlemesi aynı shinchiro sürümünden, kendi
# yayınımıza aynadan kopyalanmış hâliyle; ffmpeg ise BtbN'den, çünkü GyanD yalnız x86_64
# derliyor. BtbN'in kayan `latest` etiketi her gün üstüne yazıldığı için sabitleme olmaz;
# ay sonu autobuild etiketleri kalıcı (2024-10-31'den bugüne duruyor), gün içi olanlar
# budanıyor. Sayılar SetupModel.cs'deki FfmpegPin.Arm64 ve LibMpvPin.Arm64 ile aynı.
$libMpvArm64Url = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/mpv-dev-aarch64-20260903-git-69e63f425a.7z'
$libMpvArm64FallbackUrl = 'https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20260903/mpv-dev-aarch64-20260903-git-69e63f425a.7z'
$libMpvArm64ArchiveSha256 = '9D4E0CF7370FD1DD9A91A9D8139F24A88ECE9E58B00F5A9CA50B391D03114F2F'
$libMpvArm64DllSha256 = '3BFC5A042CC6EBE45ACE74992DBC135EE84E3E1B33AFAC070F8902A2D64A22E9'
$libMpvArm64ZipUrl = 'https://github.com/Teknesyum/VidShrink-Player/releases/download/deps-libmpv-20260903/libmpv-2-aarch64-20260903.zip'
$libMpvArm64ZipSha256 = 'A0DFCF27FA8468E4C52170779B27CEC3A6BF2BE57EAC2E021EC6D39D36228156'

$ffmpegArm64Url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-08-31-13-27/ffmpeg-n9.0.1-11-ge47273f4d9-winarm64-gpl-9.0.zip'
$ffmpegArm64Entries = @(
    @{ Entry = 'ffmpeg-n9.0.1-11-ge47273f4d9-winarm64-gpl-9.0/bin/ffmpeg.exe'; Name = 'ffmpeg.exe'; Sha256 = 'A169B9D26B2380BE66211022525C9AC16AFFC923BA05B561024E57C5ED4281F9' },
    @{ Entry = 'ffmpeg-n9.0.1-11-ge47273f4d9-winarm64-gpl-9.0/bin/ffprobe.exe'; Name = 'ffprobe.exe'; Sha256 = '6B0B738A2DF0186811F240A7036CD1279C0A08991981ADECCEEFE6A2C2B2236C' }
)

$ffmpegX64Url = 'https://github.com/GyanD/codexffmpeg/releases/download/9.0/ffmpeg-9.0-full_build.zip'
$ffmpegX64Entries = @(
    @{ Entry = 'ffmpeg-9.0-full_build/bin/ffmpeg.exe'; Name = 'ffmpeg.exe'; Sha256 = '05F4251BCE9293C2AB492CB17CA7724A0FFD0D06C881BA2EE83B82A89C2FC740' },
    @{ Entry = 'ffmpeg-9.0-full_build/bin/ffprobe.exe'; Name = 'ffprobe.exe'; Sha256 = '51E0780CD881F83749B029ED716CBB841C2EAC6289F418050F2F2961B158896B' }
)

function Use-Arm64Pins {
    $script:libMpvUrl = $libMpvArm64Url
    $script:libMpvFallbackUrl = $libMpvArm64FallbackUrl
    $script:libMpvArchiveSha256 = $libMpvArm64ArchiveSha256
    $script:libMpvDllSha256 = $libMpvArm64DllSha256
    $script:libMpvZipUrl = $libMpvArm64ZipUrl
    $script:libMpvZipSha256 = $libMpvArm64ZipSha256
    $script:vulkanLoaderZipUrl = $vulkanLoaderArm64ZipUrl
    $script:vulkanLoaderZipSha256 = $vulkanLoaderArm64ZipSha256
    $script:vulkanLoaderDllSha256 = $vulkanLoaderArm64DllSha256
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

# winget'in Gyan.FFmpeg paketi arm64 kurucusu sunmuyor; sunduğu x64 ikilisi emülasyonla
# koşar ve kodlama hızını düşürür. arm64'te ffmpeg pinli arşivden, sha256'sı dosya dosya
# doğrulanarak alınıyor.
function Install-FfmpegArm64([string]$WorkRoot, [string]$Destination) {
    return Install-FfmpegPinned $ffmpegArm64Url $ffmpegArm64Entries 'winarm64' $WorkRoot $Destination
}

function Install-FfmpegX64([string]$WorkRoot, [string]$Destination) {
    return Install-FfmpegPinned $ffmpegX64Url $ffmpegX64Entries 'win64, ~240 MB' $WorkRoot $Destination
}

function Install-FfmpegPinned([string]$Url, [object[]]$Entries, [string]$Label, [string]$WorkRoot, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination, $WorkRoot -Force | Out-Null
    $zip = Join-Path $WorkRoot 'ffmpeg-pinned.zip'
    Write-Host "FFmpeg ve FFprobe indiriliyor ($Label)..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $zip
    }
    catch {
        throw "ffmpeg indirilemedi: $Url. $($_.Exception.Message)"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($item in $Entries) {
            $entry = $archive.GetEntry($item.Entry)
            if (-not $entry) { throw "ffmpeg arşivinde $($item.Entry) yok." }
            $target = Join-Path $Destination $item.Name
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            $expected = if ($DepsSha256Override) { $DepsSha256Override.ToUpperInvariant() } else { $item.Sha256 }
            $actual = Get-FileSha256 $target
            if ($actual -ne $expected) {
                throw "$($item.Name) sağlaması tutmuyor. Beklenen $expected, bulunan $actual. Kurulum durduruldu."
            }
        }
    }
    finally {
        $archive.Dispose()
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    }

    return @{
        ffmpeg  = (Join-Path $Destination 'ffmpeg.exe')
        ffprobe = (Join-Path $Destination 'ffprobe.exe')
    }
}

function Install-LibMpv([string]$WorkRoot, [string]$Destination, [string]$Existing) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $target = Join-Path $Destination $libMpvFileName
    if ($Existing -and (Test-Path -LiteralPath $Existing) -and (Get-FileSha256 $Existing) -eq $libMpvDllSha256) {
        Copy-Item -LiteralPath $Existing -Destination $target -Force
        return 'reused'
    }

    $zipArchive = Join-Path $WorkRoot 'libmpv.zip'
    $zipExtract = Join-Path $WorkRoot 'libmpv-zip'
    $ProgressPreference = 'SilentlyContinue'
    Write-Host 'libmpv indiriliyor...' -ForegroundColor Cyan
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $libMpvZipUrl -OutFile $zipArchive
        if ((Get-FileSha256 $zipArchive) -eq $libMpvZipSha256) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            New-Item -ItemType Directory -Path $zipExtract -Force | Out-Null
            $zip = [System.IO.Compression.ZipFile]::OpenRead($zipArchive)
            try {
                $entry = $zip.Entries | Where-Object { $_.Name -eq $libMpvFileName } | Select-Object -First 1
                if ($entry) { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $zipExtract $libMpvFileName), $true) }
            }
            finally { $zip.Dispose() }
            $zipDll = Join-Path $zipExtract $libMpvFileName
            if ((Test-Path -LiteralPath $zipDll) -and (Get-FileSha256 $zipDll) -eq $libMpvDllSha256) {
                Copy-Item -LiteralPath $zipDll -Destination $target -Force
                return 'downloaded'
            }
        }
        Write-Host "libmpv sağlaması tutmadı, yedek kaynak deneniyor: $libMpvZipUrl" -ForegroundColor Yellow
    }
    catch {
        Write-Host "libmpv indirilemedi, yedek kaynak deneniyor: $libMpvZipUrl" -ForegroundColor Yellow
    }

    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path -LiteralPath $tar)) { throw "libmpv arşivini açmak için $tar gerekli; bu Windows'ta yok." }

    $archive = Join-Path $WorkRoot 'mpv-dev.7z'
    Write-Host 'libmpv indiriliyor...' -ForegroundColor Cyan
    $ProgressPreference = 'SilentlyContinue'
    $actual = $null
    $failures = @()
    foreach ($source in @($libMpvUrl, $libMpvFallbackUrl)) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $source -OutFile $archive
        }
        catch {
            $failures += $source
            Write-Host "libmpv indirilemedi, yedek kaynak deneniyor: $source" -ForegroundColor Yellow
            continue
        }
        $actual = Get-FileSha256 $archive
        if ($actual -eq $libMpvArchiveSha256) { break }
        Write-Host "libmpv sağlaması tutmadı, yedek kaynak deneniyor: $source" -ForegroundColor Yellow
    }

    if ($null -eq $actual) {
        throw "libmpv indirilemedi: $($failures -join ', ')"
    }
    if ($actual -ne $libMpvArchiveSha256) {
        throw "libmpv arşivinin sağlaması tutmuyor. Beklenen $libMpvArchiveSha256, bulunan $actual. Kurulum durduruldu."
    }

    $extract = Join-Path $WorkRoot 'libmpv'
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    & $tar -xf $archive -C $extract $libMpvFileName
    if ($LASTEXITCODE -ne 0) { throw "libmpv arşivi açılamadı (tar çıkış kodu $LASTEXITCODE)." }

    $dll = Join-Path $extract $libMpvFileName
    if (-not (Test-Path -LiteralPath $dll)) { throw "libmpv arşivinde $libMpvFileName yok." }
    $dllActual = Get-FileSha256 $dll
    if ($dllActual -ne $libMpvDllSha256) {
        throw "$libMpvFileName sağlaması tutmuyor. Beklenen $libMpvDllSha256, bulunan $dllActual. Kurulum durduruldu."
    }

    Copy-Item -LiteralPath $dll -Destination $target -Force
    return 'downloaded'
}

function Install-VulkanLoader([string]$WorkRoot, [string]$Destination, [string]$ExistingDirectory) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $target = Join-Path $Destination 'vulkan-1.dll'
    $existing = if ($ExistingDirectory) { Join-Path $ExistingDirectory 'vulkan-1.dll' } else { '' }
    if ($existing -and (Test-Path -LiteralPath $existing) -and (Get-FileSha256 $existing) -eq $vulkanLoaderDllSha256) {
        Copy-Item -LiteralPath $existing -Destination $target -Force
        $existingLicense = Join-Path $ExistingDirectory 'VulkanRT-License.txt'
        if (Test-Path -LiteralPath $existingLicense) { Copy-Item -LiteralPath $existingLicense -Destination $Destination -Force }
        return 'reused'
    }

    $archive = Join-Path $WorkRoot 'vulkan-1.zip'
    $extract = Join-Path $WorkRoot 'vulkan'
    $ProgressPreference = 'SilentlyContinue'
    Write-Host 'Vulkan yükleyicisi indiriliyor...' -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri $vulkanLoaderZipUrl -OutFile $archive
    $actual = Get-FileSha256 $archive
    if ($actual -ne $vulkanLoaderZipSha256) {
        throw "Vulkan yükleyici arşivinin sağlaması tutmuyor. Beklenen $vulkanLoaderZipSha256, bulunan $actual. Kurulum durduruldu."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($name in 'vulkan-1.dll', 'VulkanRT-License.txt') {
            $entry = $zip.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if ($entry) { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $extract $name), $true) }
        }
    }
    finally { $zip.Dispose() }

    $dll = Join-Path $extract 'vulkan-1.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'Vulkan yükleyici arşivinde vulkan-1.dll yok.' }
    $dllActual = Get-FileSha256 $dll
    if ($dllActual -ne $vulkanLoaderDllSha256) {
        throw "vulkan-1.dll sağlaması tutmuyor. Beklenen $vulkanLoaderDllSha256, bulunan $dllActual. Kurulum durduruldu."
    }

    Copy-Item -LiteralPath $dll -Destination $target -Force
    $license = Join-Path $extract 'VulkanRT-License.txt'
    if (Test-Path -LiteralPath $license) { Copy-Item -LiteralPath $license -Destination $Destination -Force }
    return 'downloaded'
}

$shellMenuKeyName = 'VidShrinkPlayer'
$shellShrinkMenuKeyName = 'VidShrinkPlayerKucult'
$shellEnqueueMenuKeyName = 'VidShrinkPlayerSirayaEkle'
$shellPackageName = 'Teknesyum.VidShrinkPlayer.Shell'
$shellCommandClsid = '7B8B4A16-E3F5-4C4A-A8D2-26B2F895BE58'
$shellShrinkCommandClsid = '565576F4-81A6-4FE2-B3E2-02E8072B334A'
$shellShrinkTargets = @(
    @{ Key = '01-size-8'; Preset = 'size-8'; Megabytes = 8; Fallback = '8 MB' }
    @{ Key = '02-whatsapp-chat'; Preset = 'whatsapp-chat'; Megabytes = 16; Fallback = 'WhatsApp (16 MB)' }
    @{ Key = '03-discord-free'; Preset = 'discord-free'; Megabytes = 20; Fallback = 'Discord (20 MB)' }
    @{ Key = '04-email-gmail'; Preset = 'email-gmail'; Megabytes = 25; Fallback = 'Gmail (25 MB)' }
    @{ Key = '05-discord-nitro-basic'; Preset = 'discord-nitro-basic'; Megabytes = 50; Fallback = 'Discord Nitro Basic (50 MB)' }
    @{ Key = '06-size-100'; Preset = 'size-100'; Megabytes = 100; Fallback = '100 MB' }
)
$shellShrinkFlag = '--kucult'
$shellEnqueueFlag = '--siraya-ekle'

$shellMenuExtensions = @(
    'mp4', 'mkv', 'mov', 'avi', 'webm', 'wmv', 'flv', 'm4v', 'mpg', 'mpeg', 'ts', 'm2ts',
    '3gp', 'ogv', 'vob', 'asf', 'rm', 'rmvb', 'divx', 'mxf', 'f4v', 'mts', 'dav', 'gif'
)

$shellPlayerExtensions = @(
    'mp3', 'm4a', 'm4b', 'aac', 'adt', 'adts', 'flac', 'wav', 'wma', 'ogg', 'oga', 'opus',
    'mka', 'aif', 'aifc', 'aiff', 'au', 'snd', 'mp2', 'mpa', 'ac3', 'ape', 'wv', 'weba',
    'm3u', 'm3u8', 'pls', 'wpl', 'zpl', 'asx', 'wax', 'wvx', 'wmx'
)

$defaultAppExtensions = @('mp4', 'mkv', 'mov', 'avi', 'webm')

function Test-Windows11 {
    return [Environment]::OSVersion.Version.Build -ge 22000
}

function Test-DefaultRegistryRoot([string]$Root) {
    return $Root.TrimEnd('\') -eq 'HKCU:\Software\Classes'
}

function Remove-Windows11ShellMenu([string]$Root) {
    if (-not (Test-DefaultRegistryRoot $Root)) { return 0 }
    $packages = @(Get-AppxPackage -Name $shellPackageName -ErrorAction SilentlyContinue)
    foreach ($package in $packages) {
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
    return $packages.Count
}

function Get-Windows11ShellManifest([string]$template) {
    $verbs = foreach ($extension in $shellMenuExtensions) {
        "            <desktop5:ItemType Type=`".$extension`"><desktop5:Verb Id=`"$shellMenuKeyName$extension`" Clsid=`"$shellCommandClsid`" /><desktop5:Verb Id=`"$shellShrinkMenuKeyName$extension`" Clsid=`"$shellShrinkCommandClsid`" /></desktop5:ItemType>"
    }
    return $template.Replace('__ITEM_TYPES__', ($verbs -join [Environment]::NewLine))
}

function Write-Windows11ShellMenu([string]$Root, [string]$InstallDirectory) {
    if (-not (Test-Windows11)) { return $false }

    $InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
    $shellRoot = Join-Path $InstallDirectory 'shell'
    $templatePath = Join-Path $shellRoot 'AppxManifest.template.xml'
    $extensionPath = Join-Path $shellRoot 'VidShrink.ShellExtension.dll'
    if (-not (Test-Path -LiteralPath $templatePath) -or -not (Test-Path -LiteralPath $extensionPath)) {
        Write-Host "Bu yayin Windows 11 kabuk paketini tasimiyor; klasik menu yazildi." -ForegroundColor Yellow
        return $false
    }

    if (-not (Test-DefaultRegistryRoot $Root)) { return $false }

    $manifestPath = Join-Path $shellRoot 'AppxManifest.xml'
    $template = [IO.File]::ReadAllText($templatePath)
    [IO.File]::WriteAllText($manifestPath, (Get-Windows11ShellManifest $template), [Text.UTF8Encoding]::new($false))

    Remove-Windows11ShellMenu $Root | Out-Null
    try {
        Add-AppxPackage -Register $manifestPath -ExternalLocation $InstallDirectory -ErrorAction Stop
    }
    catch {
        Write-Host "Windows 11 birincil sağ tık menüsü eklenemedi (imzasız paket için geliştirici modu gerekiyor); klasik menü 'Daha fazla seçenek göster' altında çalışır." -ForegroundColor Yellow
        return $false
    }
    return $true
}

function Resolve-ShellMenuLanguage([string]$Language, [string]$LocalesFolder) {
    $adaylar = @()
    if ($Language -and $Language -ne 'auto') { $adaylar += $Language }
    try { $adaylar += (Get-UICulture).Name } catch { }

    foreach ($aday in $adaylar) {
        $parcalar = @($aday -split '-' | Where-Object { $_ })
        for ($uzunluk = $parcalar.Count; $uzunluk -ge 1; $uzunluk--) {
            $etiket = ($parcalar[0..($uzunluk - 1)] -join '-')
            if ($LocalesFolder -and (Test-Path -LiteralPath $LocalesFolder)) {
                $klasor = Get-ChildItem -LiteralPath $LocalesFolder -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -eq $etiket } | Select-Object -First 1
                if ($klasor) { return $klasor.Name }
            }
            elseif ($etiket -eq 'tr' -or $etiket -eq 'en') { return $etiket.ToLowerInvariant() }
        }
    }
    return 'en'
}

function Get-LocalizedShellText([string]$Language, [string]$LocalesFolder, [string]$Key) {
    if (-not $LocalesFolder) { return $null }
    $dosya = Join-Path (Join-Path $LocalesFolder $Language) 'main.json'
    if (-not (Test-Path -LiteralPath $dosya)) { return $null }
    try {
        $tablo = Get-Content -LiteralPath $dosya -Raw -Encoding UTF8 | ConvertFrom-Json
        $metin = $tablo.$Key
        if ($metin) { return $metin }
    } catch { }
    return $null
}

function Get-ShellMenuLabel([string]$Language, [string]$LocalesFolder) {
    $metin = Get-LocalizedShellText $Language $LocalesFolder 'shell.menu.open'
    if ($metin) { return $metin }
    if ($Language -eq 'tr') { return 'Bu Videoyu VidShrink Player ile A' + [char]0x00E7 }
    return 'Open this video with VidShrink Player'
}

function Get-ShellMenuAssociationRoot([string]$Root) {
    return (Join-Path $Root 'SystemFileAssociations')
}

function Get-ShellShrinkMenuLabel([string]$Language, [string]$LocalesFolder) {
    $metin = Get-LocalizedShellText $Language $LocalesFolder 'shell.menu.shrink'
    if ($metin) { return $metin }
    if ($Language -eq 'tr') { return 'VidShrink Player ile K' + [char]0x00FC + [char]0x00E7 + [char]0x00FC + 'lt' }
    return 'Shrink with VidShrink Player'
}

function Get-ShellEnqueueMenuLabel([string]$Language, [string]$LocalesFolder) {
    $metin = Get-LocalizedShellText $Language $LocalesFolder 'shell.menu.enqueue'
    if ($metin) { return $metin }
    if ($Language -eq 'tr') { return 'VidShrink Player S' + [char]0x0131 + 'ras' + [char]0x0131 + 'na Ekle' }
    return 'Add to VidShrink Player queue'
}

function Get-QuickShrinkLabel($Target, [string]$Language, [string]$LocalesFolder) {
    $metin = Get-LocalizedShellText $Language $LocalesFolder ('shell.menu.target.' + $Target.Preset)
    if ($metin) { return $metin }
    return $Target.Fallback
}

function Get-ShellLabelsKey([string]$Root) {
    return "$(Get-AssociationSoftwareRoot $Root)\Teknesyum\VidShrinkPlayer\ShellLabels"
}

function Write-ShellLabels([string]$Root, [string]$Language, [string]$LocalesFolder) {
    $labels = [ordered]@{
        'open' = Get-ShellMenuLabel $Language $LocalesFolder
        'enqueue' = Get-ShellEnqueueMenuLabel $Language $LocalesFolder
        'shrink' = Get-ShellShrinkMenuLabel $Language $LocalesFolder
    }
    foreach ($target in $shellShrinkTargets) { $labels['target.' + $target.Key] = Get-QuickShrinkLabel $target $Language $LocalesFolder }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey((Get-ShellLabelsKey $Root))
    try {
        foreach ($name in $labels.Keys) { $key.SetValue($name, $labels[$name], [Microsoft.Win32.RegistryValueKind]::String) }
    } finally { $key.Dispose() }
}

function Remove-ShellMenu([string]$Root) {
    $associations = Get-ShellMenuAssociationRoot $Root
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree((Get-ShellLabelsKey $Root), $false)
    if (-not (Test-Path -LiteralPath $associations)) { return 0 }

    $removed = 0
    foreach ($association in Get-ChildItem -LiteralPath $associations) {
        $shell = Join-Path $association.PSPath 'shell'
        $touched = $false
        foreach ($menuKeyName in @($shellMenuKeyName, $shellShrinkMenuKeyName, $shellEnqueueMenuKeyName)) {
            $key = Join-Path $shell $menuKeyName
            if (-not (Test-Path -LiteralPath $key)) { continue }
            Remove-Item -LiteralPath $key -Recurse -Force
            $touched = $true
        }
        if ($touched) { $removed++ }

        foreach ($parent in $shell, $association.PSPath) {
            if (-not (Test-Path -LiteralPath $parent)) { break }
            $item = Get-Item -LiteralPath $parent
            if ($item.SubKeyCount -gt 0 -or $item.ValueCount -gt 0) { break }
            Remove-Item -LiteralPath $parent -Force
        }
    }
    return $removed
}

function Write-ShellMenu([string]$Root, [string]$Executable, [string]$Label, [string]$EnqueueLabel) {
    $extensions = @($shellMenuExtensions) + @($shellPlayerExtensions)
    foreach ($extension in $extensions) {
        $key = Join-Path (Get-ShellMenuAssociationRoot $Root) ".$extension\shell\$shellMenuKeyName"
        New-Item -Path $key -Force | Out-Null
        Set-ItemProperty -LiteralPath $key -Name 'MUIVerb' -Value $Label -Type String
        Set-ItemProperty -LiteralPath $key -Name 'Icon' -Value $Executable -Type String
        Set-ItemProperty -LiteralPath $key -Name 'MultiSelectModel' -Value 'Player' -Type String

        $command = Join-Path $key 'command'
        New-Item -Path $command -Force | Out-Null
        Set-Item -LiteralPath $command -Value ('"{0}" "%1"' -f $Executable)

        $enqueue = Join-Path (Get-ShellMenuAssociationRoot $Root) ".$extension\shell\$shellEnqueueMenuKeyName"
        New-Item -Path $enqueue -Force | Out-Null
        Set-ItemProperty -LiteralPath $enqueue -Name 'MUIVerb' -Value $EnqueueLabel -Type String
        Set-ItemProperty -LiteralPath $enqueue -Name 'Icon' -Value $Executable -Type String
        Set-ItemProperty -LiteralPath $enqueue -Name 'MultiSelectModel' -Value 'Player' -Type String

        $enqueueCommand = Join-Path $enqueue 'command'
        New-Item -Path $enqueueCommand -Force | Out-Null
        Set-Item -LiteralPath $enqueueCommand -Value ('"{0}" "%1" {1}' -f $Executable, $shellEnqueueFlag)
    }
    return $extensions.Count
}

function Write-ShellShrinkMenu([string]$Root, [string]$Executable, [string]$Label, [string]$Language, [string]$LocalesFolder) {
    $written = 0
    foreach ($extension in $shellMenuExtensions) {
        $verbKey = Join-Path (Get-ShellMenuAssociationRoot $Root) ".$extension\shell\$shellShrinkMenuKeyName"
        New-Item -Path $verbKey -Force | Out-Null
        Set-ItemProperty -LiteralPath $verbKey -Name 'MUIVerb' -Value $Label -Type String
        Set-ItemProperty -LiteralPath $verbKey -Name 'Icon' -Value $Executable -Type String
        Set-ItemProperty -LiteralPath $verbKey -Name 'SubCommands' -Value '' -Type String
        Set-ItemProperty -LiteralPath $verbKey -Name 'MultiSelectModel' -Value 'Player' -Type String

        foreach ($target in $shellShrinkTargets) {
            $targetKey = Join-Path $verbKey "shell\$($target.Key)"
            New-Item -Path $targetKey -Force | Out-Null
            Set-ItemProperty -LiteralPath $targetKey -Name 'MUIVerb' -Value (Get-QuickShrinkLabel $target $Language $LocalesFolder) -Type String
            Set-ItemProperty -LiteralPath $targetKey -Name 'MultiSelectModel' -Value 'Player' -Type String

            $command = Join-Path $targetKey 'command'
            New-Item -Path $command -Force | Out-Null
            Set-Item -LiteralPath $command -Value ('"{0}" {1} {2} "%1"' -f $Executable, $shellShrinkFlag, $target.Megabytes)
            $written++
        }
    }
    return $written
}

function Update-ShellMenu([string]$Root, [string]$Executable, [string]$Language) {
    Remove-ShellMenu $Root | Out-Null
    $locales = Join-Path (Join-Path (Split-Path -Parent $Executable) 'app') 'Locales'
    $dil = Resolve-ShellMenuLanguage $Language $locales
    $written = Write-ShellMenu $Root $Executable (Get-ShellMenuLabel $dil $locales) (Get-ShellEnqueueMenuLabel $dil $locales)
    $shrinkWritten = Write-ShellShrinkMenu $Root $Executable (Get-ShellShrinkMenuLabel $dil $locales) $dil $locales
    Write-ShellLabels $Root $dil $locales
    $modern = Write-Windows11ShellMenu $Root (Split-Path -Parent $Executable)
    $path = if ($modern) { 'Windows 11 birincil ve klasik' } else { 'Windows 10 klasik' }
    Write-Host "Sağ tık menüsü $written uzantıya, küçültme alt menüsü $shrinkWritten girdiye yazıldı ($path menü)." -ForegroundColor Green
}

$fileAssociationProgId = 'Teknesyum.VidShrinkPlayer.Video'
$fileAssociationName = 'VidShrink Player'
$fileAssociationCapabilities = 'Teknesyum\VidShrinkPlayer\Capabilities'

function Get-CurrentUserSubKey([string]$Path) {
    $trimmed = $Path.TrimEnd('\')
    if ($trimmed -notmatch '^HKCU:\\(.+)$') { throw "Kayit koku HKCU:\ altinda olmali: $Path" }
    return $Matches[1]
}

function Get-AssociationSoftwareRoot([string]$Root) {
    if (Test-DefaultRegistryRoot $Root) { return 'Software' }
    return (Get-CurrentUserSubKey $Root)
}

function Open-CurrentUserKey([string]$SubKey) {
    return [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($SubKey)
}

function Set-RegistryString([string]$SubKey, [string]$Name, [string]$Value) {
    $key = Open-CurrentUserKey $SubKey
    try { $key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String) }
    finally { $key.Close() }
}

function Remove-EmptyCurrentUserKey([string]$SubKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($null -eq $key) { return }
    $empty = $key.SubKeyCount -eq 0 -and $key.ValueCount -eq 0
    $key.Close()
    if ($empty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($SubKey, $false) }
}

function Send-AssociationChanged([string]$Root) {
    if (-not (Test-DefaultRegistryRoot $Root)) { return }
    try {
        Add-Type -Namespace VidShrinkKurulum -Name Kabuk -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr first, IntPtr second);' -ErrorAction Stop
        [VidShrinkKurulum.Kabuk]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch { }
}

function Get-AppRegistrationKey([string]$Root, [string]$Name, [string]$Default) {
    if (Test-DefaultRegistryRoot $Root) { return $Default }
    $classes = Get-CurrentUserSubKey $Root
    $slash = $classes.LastIndexOf('\')
    if ($slash -lt 0) { return $Name }
    return $classes.Substring(0, $slash) + '\' + $Name
}

function Remove-UninstallEntry([string]$Root) {
    $subKey = Get-AppRegistrationKey $Root 'Uninstall\VidShrinkPlayer'$uninstallKeyName
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey)
    if ($null -eq $key) { return $false }
    $key.Close()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($subKey, $false)
    return $true
}

function Split-UserPath($Value) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { return @() }
    return @($Value.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Test-SameUserPathEntry([string]$Entry, [string]$Directory) {
    $left = $Entry.Trim().Trim('"').TrimEnd([char[]]@('\', '/'))
    $right = $Directory.Trim().Trim('"').TrimEnd([char[]]@('\', '/'))
    return [string]::Equals($left, $right, [StringComparison]::OrdinalIgnoreCase)
}

function Read-UserPath([string]$SubKey) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if ($null -eq $key) { return $null }
    try { return $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
    finally { $key.Close() }
}

function Test-UserPathContains([string]$Root, [string]$Directory) {
    $current = Read-UserPath (Get-AppRegistrationKey $Root 'Environment' 'Environment')
    return @(Split-UserPath $current | Where-Object { Test-SameUserPathEntry $_ $Directory }).Count -gt 0
}

function Send-EnvironmentChanged([string]$Root) {
    if (-not (Test-DefaultRegistryRoot $Root)) { return }
    try {
        Add-Type -Namespace VidShrinkKurulum -Name Ortam -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);' -ErrorAction Stop
        $result = [IntPtr]::Zero
        [VidShrinkKurulum.Ortam]::SendMessageTimeoutW([IntPtr]0xffff, 0x001A, [IntPtr]::Zero, 'Environment', 0x0002, 5000, [ref]$result) | Out-Null
    }
    catch { }
}

function Add-UserPathEntry([string]$Root, [string]$Directory) {
    $subKey = Get-AppRegistrationKey $Root 'Environment' 'Environment'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($subKey)
    try {
        $raw = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -ne $raw -and $raw -isnot [string]) { return $false }
        if (@(Split-UserPath $raw | Where-Object { Test-SameUserPathEntry $_ $Directory }).Count -gt 0) { return $false }
        if ([string]::IsNullOrWhiteSpace($raw)) { $next = $Directory }
        else { $next = $raw.TrimEnd().TrimEnd(';') + ';' + $Directory }
        if ($null -eq $raw) { $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString }
        else { $kind = $key.GetValueKind('Path') }
        $key.SetValue('Path', $next, $kind)
    }
    finally { $key.Close() }
    Send-EnvironmentChanged $Root
    return $true
}

function Remove-UserPathEntry([string]$Root, [string]$Directory) {
    $subKey = Get-AppRegistrationKey $Root 'Environment' 'Environment'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey, $true)
    if ($null -eq $key) { return $false }
    try {
        $current = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $entries = @(Split-UserPath $current)
        $kept = @($entries | Where-Object { -not (Test-SameUserPathEntry $_ $Directory) })
        if ($kept.Count -eq $entries.Count) { return $false }
        if ($kept.Count -eq 0) { $key.DeleteValue('Path', $false) }
        else { $key.SetValue('Path', ($kept -join ';'), $key.GetValueKind('Path')) }
    }
    finally { $key.Close() }
    Send-EnvironmentChanged $Root
    return $true
}

function Write-CommandShim([string]$Root) {
    $folder = Join-Path $Root $commandShimFolder
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $path = Join-Path $folder $commandShimFile
    [IO.File]::WriteAllText($path, $commandShimLine + "`r`n", [Text.UTF8Encoding]::new($false))
    return $path
}

function Remove-SetupCopy {
    $copy = Join-Path $env:LOCALAPPDATA (Join-Path 'VidShrink Player' $setupExecutableName)
    if (-not (Test-Path -LiteralPath $copy)) { return }
    try { Remove-Item -LiteralPath $copy -Force -ErrorAction Stop }
    catch { }
}

function Get-OpenCommandTarget([string]$Executable) {
    if ([IO.Path]::GetFileName($Executable) -ine 'VidShrinkPlayer.exe') { return $Executable }
    return [IO.Path]::Combine([IO.Path]::GetDirectoryName($Executable), 'app\VidShrinkPlayer.App.exe')
}

function Write-FileAssociation([string]$Root, [string]$Executable) {
    $classes = Get-CurrentUserSubKey $Root
    $software = Get-AssociationSoftwareRoot $Root
    $command = '"{0}" "%1"' -f (Get-OpenCommandTarget $Executable)
    $progId = "$classes\$fileAssociationProgId"
    $application = "$classes\Applications\$([IO.Path]::GetFileName($Executable))"
    $capabilities = "$software\$fileAssociationCapabilities"

    Set-RegistryString $progId '' $fileAssociationName
    Set-RegistryString $progId 'FriendlyTypeName' $fileAssociationName
    Set-RegistryString "$progId\DefaultIcon" '' "$Executable,0"
    Set-RegistryString "$progId\shell\open\command" '' $command

    Set-RegistryString $application 'FriendlyAppName' $fileAssociationName
    Set-RegistryString "$application\shell\open\command" '' $command

    Set-RegistryString $capabilities 'ApplicationName' $fileAssociationName
    Set-RegistryString $capabilities 'ApplicationDescription' $fileAssociationName

    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("$capabilities\FileAssociations", $false)
    foreach ($extension in $defaultAppExtensions) {
        Set-RegistryString "$capabilities\FileAssociations" ".$extension" $fileAssociationProgId
    }

    foreach ($extension in $shellMenuExtensions) {
        Set-RegistryString "$application\SupportedTypes" ".$extension" ''

        $list = Open-CurrentUserKey "$classes\.$extension\OpenWithProgids"
        try { $list.SetValue($fileAssociationProgId, [byte[]]@(), [Microsoft.Win32.RegistryValueKind]::None) }
        finally { $list.Close() }
    }

    Set-RegistryString "$software\RegisteredApplications" $fileAssociationName $capabilities
    Send-AssociationChanged $Root
    return $shellMenuExtensions.Count
}

function Remove-FileAssociation([string]$Root) {
    $classes = Get-CurrentUserSubKey $Root
    $software = Get-AssociationSoftwareRoot $Root
    $user = [Microsoft.Win32.Registry]::CurrentUser
    $removed = 0

    foreach ($extension in $shellMenuExtensions) {
        $listPath = "$classes\.$extension\OpenWithProgids"
        $list = $user.OpenSubKey($listPath, $true)
        if ($null -eq $list) { continue }
        $had = @($list.GetValueNames()) -contains $fileAssociationProgId
        if ($had) { $list.DeleteValue($fileAssociationProgId, $false); $removed++ }
        $list.Close()
        Remove-EmptyCurrentUserKey $listPath
        Remove-EmptyCurrentUserKey "$classes\.$extension"
    }

    $user.DeleteSubKeyTree("$classes\$fileAssociationProgId", $false)
    $user.DeleteSubKeyTree("$classes\Applications\VidShrinkPlayer.exe", $false)
    Remove-EmptyCurrentUserKey "$classes\Applications"
    $user.DeleteSubKeyTree("$software\$fileAssociationCapabilities", $false)
    Remove-EmptyCurrentUserKey "$software\Teknesyum\VidShrinkPlayer"
    Remove-EmptyCurrentUserKey "$software\Teknesyum"

    $registered = $user.OpenSubKey("$software\RegisteredApplications", $true)
    if ($null -ne $registered) {
        $registered.DeleteValue($fileAssociationName, $false)
        $registered.Close()
        if (-not (Test-DefaultRegistryRoot $Root)) { Remove-EmptyCurrentUserKey "$software\RegisteredApplications" }
    }

    Send-AssociationChanged $Root
    return $removed
}

function Remove-Shortcut([string]$Path, [string]$Root) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $shell = New-Object -ComObject WScript.Shell
    $target = $shell.CreateShortcut($Path).TargetPath
    if (-not $target -or -not $target.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    Remove-Item -LiteralPath $Path -Force
    return $true
}

function Test-ProgramsInstallRoot([string]$Root) {
    $programsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
    return ($Root -ne $programsRoot -and
        $Root.StartsWith($programsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
}

function Get-InstallRootHolder([string]$Root) {
    $holders = @()
    foreach ($processName in 'VidShrinkPlayer.App', 'VidShrinkPlayer', 'VidShrink.App', 'VidShrink') {
        $holders += @(Get-Process $processName -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase) })
    }
    return $holders
}

function Remove-InstallRoot([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return }

    $delay = $script:RemoveFirstDelayMilliseconds
    $waited = 0
    $lastMessage = ''
    $holderRounds = 0

    for ($attempt = 1; $attempt -le $script:RemoveAttempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastMessage = $_.Exception.Message
        }

        $holders = @(Get-InstallRootHolder $Root)
        if ($holders.Count -gt 0) { $holderRounds++ } else { $holderRounds = 0 }
        if ($holderRounds -ge 2) {
            $names = ($holders | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ', '
            Write-Host "VidShrink kapanmayı bekliyor ($names); virüs taraması sürüyorsa en çok $script:RemoveHolderWaitSeconds sn beklenecek..." -ForegroundColor Yellow
            $holders | Stop-Process -Force -ErrorAction SilentlyContinue
            $holders | Wait-Process -Timeout $script:RemoveHolderWaitSeconds -ErrorAction SilentlyContinue
            $still = @(Get-InstallRootHolder $Root)
            if ($still.Count -gt 0) {
                $names = ($still | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ', '
                throw ("Kurulum klasörü silinemedi: VidShrink $script:RemoveHolderWaitSeconds sn sonra hâlâ açık - $names. " +
                    "Virüs programı dosyayı tarıyorsa taramanın bitmesini bekleyip komutu yeniden çalıştırın. Klasör: $Root")
            }
            $holderRounds = 0
            try {
                Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction Stop
                return
            }
            catch {
                $lastMessage = $_.Exception.Message
            }
        }

        if ($attempt -lt $script:RemoveAttempts) {
            Write-Host "Kurulum klasörü kilitli, $delay ms sonra yeniden denenecek ($attempt/$script:RemoveAttempts)..." -ForegroundColor Yellow
            Start-Sleep -Milliseconds $delay
            $waited += $delay
            $delay = $delay * 2
        }
    }

    throw ("Kurulum klasörü $script:RemoveAttempts denemede ve $waited ms beklemede silinemedi: $Root. " +
        'Bir dosya başka bir süreçte açık - genellikle virüs taraması ya da Gezgin önizlemesi; ' +
        "birkaç saniye sonra komutu yeniden çalıştırın. Son hata: $lastMessage")
}

if ($Uninstall) {
    $resolvedUninstallRoot = [IO.Path]::GetFullPath($InstallRoot)
    foreach ($processName in 'VidShrinkPlayer.App', 'VidShrinkPlayer', 'VidShrink.App', 'VidShrink') {
        Get-Process $processName -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($resolvedUninstallRoot, [StringComparison]::OrdinalIgnoreCase) } |
            Stop-Process -Force
    }

    $cleared = Remove-ShellMenu $RegistryRoot
    $packages = Remove-Windows11ShellMenu $RegistryRoot
    $unlinked = Remove-FileAssociation $RegistryRoot
    Write-Host "Sağ tık menüsü ($cleared uzantı, $packages paket) ve dosya ilişkilendirmesi ($unlinked uzantı) kaldırıldı." -ForegroundColor Green

    if (Remove-UninstallEntry $RegistryRoot) {
        Write-Host 'Yüklü uygulamalar kaydı kaldırıldı.' -ForegroundColor Green
    }
    $uninstallShimFolder = Join-Path $resolvedUninstallRoot $commandShimFolder
    if (Remove-UserPathEntry $RegistryRoot $uninstallShimFolder) {
        Write-Host "PATH girdisi kaldırıldı: $uninstallShimFolder" -ForegroundColor Green
    }

    if (-not $SkipShortcuts) {
        Remove-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'VidShrink Player.lnk') $resolvedUninstallRoot | Out-Null
        $startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'VidShrink Player'
        if (Remove-Shortcut (Join-Path $startMenuDirectory 'VidShrink Player.lnk') $resolvedUninstallRoot) {
            if (-not (Get-ChildItem -LiteralPath $startMenuDirectory -Force)) { Remove-Item -LiteralPath $startMenuDirectory -Force }
        }
    }

    if (Test-ProgramsInstallRoot $resolvedUninstallRoot) {
        Remove-InstallRoot $resolvedUninstallRoot
        Write-Host "VidShrink kaldırıldı: $resolvedUninstallRoot" -ForegroundColor Green
    }
    else {
        Write-Host "Kurulum klasörü LocalAppData\Programs altında değil, silinmedi: $resolvedUninstallRoot" -ForegroundColor Yellow
    }

    if ($RemoveSettings) {
        $settingsFolder = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'VidShrink Player'
        if (Test-Path -LiteralPath $settingsFolder) {
            Remove-Item -LiteralPath $settingsFolder -Recurse -Force
            Write-Host "Ayar klasörü silindi: $settingsFolder" -ForegroundColor Green
        }
    }
    if (Test-DefaultRegistryRoot $RegistryRoot) { Remove-SetupCopy }
    return
}

if ($RemoveShellMenu -or $RemoveFileAssociation) {
    if ($RemoveShellMenu) {
        $cleared = Remove-ShellMenu $RegistryRoot
        $packages = Remove-Windows11ShellMenu $RegistryRoot
        Write-Host "Sağ tık menüsü kaldırıldı: $cleared uzantı, $packages paket." -ForegroundColor Green
    }
    if ($RemoveFileAssociation) {
        $unlinked = Remove-FileAssociation $RegistryRoot
        Write-Host "Dosya ilişkilendirmesi kaldırıldı: $unlinked uzantı." -ForegroundColor Green
    }
    return
}

if ($ShellMenuOnly) {
    if ($SkipShortcuts) {
        Write-Host 'SkipShortcuts verildi; kabuğa dokunulmadı.' -ForegroundColor Yellow
        return
    }
    $shellMenuExecutable = Join-Path $InstallRoot 'VidShrinkPlayer.exe'
    if (-not (Test-Path -LiteralPath $shellMenuExecutable)) {
        throw "Kurulu VidShrinkPlayer.exe bulunamadı: $shellMenuExecutable. Önce kurulumu çalıştırın."
    }
    Update-ShellMenu $RegistryRoot $shellMenuExecutable $MenuLanguage
    $associated = Write-FileAssociation $RegistryRoot $shellMenuExecutable
    Write-Host "Dosya ilişkilendirmesi $associated uzantıya yazıldı." -ForegroundColor Green
    return
}

Write-Host 'VidShrink kurulumu hazırlanıyor...' -ForegroundColor Cyan

$runtimeIdentifier = Get-RuntimeIdentifier
$isArm64 = $runtimeIdentifier -eq 'win-arm64'
if ($isArm64) { Use-Arm64Pins }

if ($DepsOnly) {
    $depsRoot = [IO.Path]::GetFullPath($DepsOnly)
    $depsWork = Join-Path $depsRoot 'is'
    New-Item -ItemType Directory -Path $depsRoot, $depsWork -Force | Out-Null
    Write-Host "Bağımlılık kolu: $runtimeIdentifier" -ForegroundColor Cyan

    if ($isArm64) {
        $depsFfmpeg = Install-FfmpegArm64 $depsWork (Join-Path $depsRoot 'ffmpeg')
        Write-Host "ffmpeg hazır (sha256 doğrulandı): $($depsFfmpeg.ffmpeg)" -ForegroundColor Green
    }
    elseif (-not (Test-WinGet)) {
        $depsFfmpeg = Install-FfmpegX64 $depsWork (Join-Path $depsRoot 'ffmpeg')
        Write-Host "WinGet yok; ffmpeg pinli arşivden hazır (sha256 doğrulandı): $($depsFfmpeg.ffmpeg)" -ForegroundColor Green
    }
    else {
        Write-Host 'x64 kolunda ffmpeg winget ile geliyor; bağımlılık kolu yalnız libmpv indirir.' -ForegroundColor Yellow
    }

    $depsLibMpv = Join-Path $depsRoot 'libmpv'
    Install-LibMpv $depsWork $depsLibMpv '' | Out-Null
    Write-Host "libmpv hazır (sha256 doğrulandı): $(Join-Path $depsLibMpv $libMpvFileName)" -ForegroundColor Green
    Install-VulkanLoader $depsWork $depsLibMpv '' | Out-Null
    Write-Host "Vulkan yükleyicisi hazır (sha256 doğrulandı): $(Join-Path $depsLibMpv 'vulkan-1.dll')" -ForegroundColor Green
    return
}

$offlineDirectory = $OfflineSource
if (-not $offlineDirectory) { $offlineDirectory = $PSScriptRoot }
$offline = Get-OfflineBundle $offlineDirectory $runtimeIdentifier
if ($offline) { Write-Host "Çevrimdışı arşiv: araçlar $($offline.Directory) klasöründen kurulacak." -ForegroundColor Cyan }

$ffmpeg = $null
$ffprobe = $null
if ($offline) {
    $offlineEntries = if ($isArm64) { $ffmpegArm64Entries } else { $ffmpegX64Entries }
    $offlineFfmpegWork = Join-Path ([IO.Path]::GetTempPath()) ("vidshrink-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
    $bundledFfmpeg = Copy-OfflineFfmpeg $offline $offlineEntries (Join-Path $offlineFfmpegWork 'bin')
    if ($bundledFfmpeg) {
        $ffmpeg = $bundledFfmpeg.ffmpeg
        $ffprobe = $bundledFfmpeg.ffprobe
    }
}
if (-not $ffmpeg -or -not $ffprobe) {
    $ffmpeg = Find-Tool 'ffmpeg'
    $ffprobe = Find-Tool 'ffprobe'
}
if (-not $ffmpeg -or -not $ffprobe) {
    if ($isArm64) {
        $ffmpegWork = Join-Path ([IO.Path]::GetTempPath()) ("vidshrink-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
        $fetchedFfmpeg = Install-FfmpegArm64 $ffmpegWork (Join-Path $ffmpegWork 'bin')
        $ffmpeg = $fetchedFfmpeg.ffmpeg
        $ffprobe = $fetchedFfmpeg.ffprobe
    }
    else {
        Write-Host 'FFmpeg ve FFprobe yükleniyor...' -ForegroundColor Cyan
        $wingetFailure = $null
        if (Test-WinGet) {
            try {
                Install-WinGetPackage 'Gyan.FFmpeg'
                $ffmpeg = Find-Tool 'ffmpeg'
                $ffprobe = Find-Tool 'ffprobe'
            }
            catch {
                $wingetFailure = $_.Exception.Message
            }
        }
        else {
            $wingetFailure = 'WinGet bulunamadı'
        }
        if (-not $ffmpeg -or -not $ffprobe) {
            if ($wingetFailure) { Write-Host "$wingetFailure; FFmpeg sabitlenmiş arşivden indirilecek." -ForegroundColor Yellow }
            $ffmpegWork = Join-Path ([IO.Path]::GetTempPath()) ("vidshrink-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
            $fetchedFfmpeg = Install-FfmpegX64 $ffmpegWork (Join-Path $ffmpegWork 'bin')
            $ffmpeg = $fetchedFfmpeg.ffmpeg
            $ffprobe = $fetchedFfmpeg.ffprobe
        }
    }
}
if (-not $ffmpeg -or -not $ffprobe) { throw 'FFmpeg veya FFprobe kurulumdan sonra bulunamadı.' }

$resolvedInstallRoot = [IO.Path]::GetFullPath($InstallRoot)
if (-not (Test-ProgramsInstallRoot $resolvedInstallRoot)) {
    throw "Güvenlik nedeniyle kurulum yolu LocalAppData\Programs altında olmalıdır: $resolvedInstallRoot"
}

$archiveName = "vidshrink-$runtimeIdentifier.zip"
$launcherArchiveName = "vidshrink-launcher-$runtimeIdentifier.zip"
$checksumsName = "checksums-$runtimeIdentifier.txt"

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("vidshrink-install-" + [Guid]::NewGuid().ToString('N'))
$stageRoot = Join-Path $workRoot 'stage'

try {
    New-Item -ItemType Directory -Path $workRoot, $stageRoot -Force | Out-Null

    if ($offline) {
        $release = [pscustomobject]@{ tag_name = $offline.Tag; assets = $null }
    }
    else {
        Write-Host 'Son yayın aranıyor...' -ForegroundColor Cyan
        $release = Get-LatestRelease
    }
    $tag = [string]$release.tag_name
    $version = $tag.TrimStart('v')
    Write-Host "Kurulacak sürüm: $version" -ForegroundColor Cyan

    # Başlatıcı yayında yoksa kurulum yarım kalır: kısayolun göstereceği program olmaz
    # ve otogüncelleme hiç çalışmaz. Kaynaktan derlemeye düşmek yerine burada durulur.
    $assetNames = @($release.assets | ForEach-Object { [string]$_.name })
    foreach ($required in $archiveName, $launcherArchiveName, $checksumsName) {
        if ($null -ne $release.assets -and $assetNames -notcontains $required) {
            throw "Yayın $tag bu varlığı taşımıyor: $required. Başlatıcısız kurulum yapılmaz; başlatıcıyı da içeren bir yayın çıkana kadar bekleyin."
        }
    }

    $archivePath = Join-Path $workRoot $archiveName
    $launcherArchivePath = Join-Path $workRoot $launcherArchiveName
    $checksumsPath = Join-Path $workRoot $checksumsName

    Write-Host 'Yayın paketi indiriliyor...' -ForegroundColor Cyan
    if ($offline -and (Test-Path -LiteralPath (Join-Path $offline.Directory $checksumsName) -PathType Leaf)) {
        Copy-Item -LiteralPath (Join-Path $offline.Directory $checksumsName) -Destination $checksumsPath -Force
    }
    else {
        Get-ReleaseAsset $tag $checksumsName $checksumsPath
    }
    $checksums = Read-Checksums $checksumsPath

    Write-Host 'İndirilenler doğrulanıyor...' -ForegroundColor Cyan
    Get-VerifiedReleaseAsset $offline $tag $archiveName $archivePath $checksums
    Get-VerifiedReleaseAsset $offline $tag $launcherArchiveName $launcherArchivePath $checksums

    # Kurulum düzeni: kökte başlatıcı ve ffmpeg, app\ altında güncellenen uygulama.
    # Çalışan bir exe ve yüklü dll'ler üzerine yazılamadığı için güncelleme, uygulama
    # yüklenmeden önce başlatıcı tarafından app\ klasörüne uygulanır.
    $appStageRoot = Join-Path $stageRoot 'app'
    Expand-Archive -LiteralPath $archivePath -DestinationPath $appStageRoot -Force
    Expand-Archive -LiteralPath $launcherArchivePath -DestinationPath $stageRoot -Force

    # Kurulan sürümün işareti. Bu dosya olmadan ilk açılışta güncelleyici kurulu klasörü
    # yayınla dosya dosya karşılaştırır ve arşivin neredeyse tamamını yeniden indirir.
    Set-Content -LiteralPath (Join-Path $appStageRoot '.update-version') -Value $version -Encoding UTF8 -NoNewline
    Set-Content -LiteralPath (Join-Path $stageRoot '.launcher-version') -Value $version -Encoding UTF8 -NoNewline

    $toolsRoot = Join-Path $stageRoot 'tools\ffmpeg'
    New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
    Copy-Item -LiteralPath $ffmpeg -Destination (Join-Path $toolsRoot 'ffmpeg.exe') -Force
    Copy-Item -LiteralPath $ffprobe -Destination (Join-Path $toolsRoot 'ffprobe.exe') -Force

    $libMpvRoot = Join-Path $stageRoot 'tools\libmpv'
    $installedLibMpv = Join-Path $resolvedInstallRoot "tools\libmpv\$libMpvFileName"
    $libMpvExisting = $installedLibMpv
    $vulkanExisting = Join-Path $resolvedInstallRoot 'tools\libmpv'
    if ($offline) {
        $bundleLibMpvFolder = Join-Path $offline.Directory 'tools\libmpv'
        if (Test-OfflineFile (Join-Path $bundleLibMpvFolder $libMpvFileName) $libMpvDllSha256) {
            $libMpvExisting = Join-Path $bundleLibMpvFolder $libMpvFileName
        }
        if (Test-OfflineFile (Join-Path $bundleLibMpvFolder 'vulkan-1.dll') $vulkanLoaderDllSha256) {
            $vulkanExisting = $bundleLibMpvFolder
        }
    }
    $libMpvSource = Install-LibMpv $workRoot $libMpvRoot $libMpvExisting
    Write-Host "libmpv hazır ($libMpvSource, sha256 doğrulandı)." -ForegroundColor Cyan
    $vulkanSource = Install-VulkanLoader $workRoot $libMpvRoot $vulkanExisting
    Write-Host "Vulkan yükleyicisi hazır ($vulkanSource, sha256 doğrulandı)." -ForegroundColor Cyan

    foreach ($processName in 'VidShrinkPlayer.App', 'VidShrinkPlayer', 'VidShrink.App', 'VidShrink') {
        Get-Process $processName -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($resolvedInstallRoot, [StringComparison]::OrdinalIgnoreCase) } |
            Stop-Process -Force
    }

    Remove-Windows11ShellMenu $RegistryRoot | Out-Null
    Remove-InstallRoot $resolvedInstallRoot
    New-Item -ItemType Directory -Path $resolvedInstallRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $stageRoot '*') -Destination $resolvedInstallRoot -Recurse -Force

    # Kısayollar başlatıcıyı gösterir; uygulamayı doğrudan gösterirlerse güncelleme hiç çalışmaz.
    $installedExe = Join-Path $resolvedInstallRoot 'VidShrinkPlayer.exe'
    if (-not (Test-Path -LiteralPath $installedExe)) { throw 'Kurulan VidShrinkPlayer.exe bulunamadı.' }
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedInstallRoot 'app\VidShrinkPlayer.App.exe'))) {
        throw 'Kurulan app\VidShrinkPlayer.App.exe bulunamadı.'
    }

    $installShimFolder = Join-Path $resolvedInstallRoot $commandShimFolder
    if ($AddToPath -or (Test-UserPathContains $RegistryRoot $installShimFolder)) {
        Write-CommandShim $resolvedInstallRoot | Out-Null
    }
    if ($AddToPath -and (Add-UserPathEntry $RegistryRoot $installShimFolder)) {
        Write-Host "PATH'e eklendi: $installShimFolder" -ForegroundColor Green
    }

    if (-not $SkipShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $desktopShortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'VidShrink Player.lnk'))
        $desktopShortcut.TargetPath = $installedExe
        $desktopShortcut.WorkingDirectory = $resolvedInstallRoot
        $desktopShortcut.IconLocation = "$installedExe,0"
        $desktopShortcut.Save()

        $startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'VidShrink Player'
        New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
        $startMenuShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory 'VidShrink Player.lnk'))
        $startMenuShortcut.TargetPath = $installedExe
        $startMenuShortcut.WorkingDirectory = $resolvedInstallRoot
        $startMenuShortcut.IconLocation = "$installedExe,0"
        $startMenuShortcut.Save()

        Update-ShellMenu $RegistryRoot $installedExe $MenuLanguage
        $associated = Write-FileAssociation $RegistryRoot $installedExe
        Write-Host "Dosya ilişkilendirmesi $associated uzantıya yazıldı." -ForegroundColor Green
    }

    Write-Host "VidShrink $version kuruldu: $resolvedInstallRoot" -ForegroundColor Green
    if (-not $NoLaunch) { Start-Process -FilePath $installedExe }
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($ffmpegWork -and (Test-Path -LiteralPath $ffmpegWork)) {
        Remove-Item -LiteralPath $ffmpegWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
