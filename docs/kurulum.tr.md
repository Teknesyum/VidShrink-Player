# Kurulum ve güncelleme

Tam ayrıntı. Tek satırlık kurulum komutları [README](../README.tr.md) içinde.

## Kurulum

### Windows — VidShrinkPlayer-Setup.exe

Son sürümden (0.8.3 ve sonrası) [`VidShrinkPlayer-Setup.exe`](https://github.com/Teknesyum/VidShrink-Player/releases/latest/download/VidShrinkPlayer-Setup.exe)
dosyasını indirip çalıştırın. Aşağıdaki betikle aynı düzeni, kısayolları, sağ tık girdilerini
ve dosya ilişkilendirmelerini aynı kayıt anahtarlarına yazar; iki anahtar ağacını değer değer
karşılaştıran bir test var. Farkı: üç dosyayı paralel indirir ve yazarken sağlamasını alır,
arşivleri ara klasöre değil doğrudan kurulum köküne açar, eski kurulumu kenara alır ve kurulum
yarıda kalırsa geri koyar. `ffmpeg` bulunamazsa WinGet çağırmaz; sabitlenmiş GyanD 9.0
arşivinden yalnız `ffmpeg.exe` ile `ffprobe.exe`'yi aralıklı okuyup her birini SHA-256'ya karşı
doğrular. Kilitli klasör davranışı betiğinkiyle aynı. Kaldırmak için
`VidShrinkPlayer-Setup.exe --uninstall`, bütün seçenekler için `--help`.

### Windows — tek satır

```powershell
irm https://raw.githubusercontent.com/Teknesyum/VidShrink-Player/main/Install-VidShrink.ps1 | iex
```

Yönetici hakkı da .NET SDK'sı da gerekmez. Kurucu GitHub'dan son sürümü ister, `win-x64`
arşivini ve yanındaki başlatıcıyı indirir, ikisini de sürümün kendi SHA-256 listesine karşı
sınar ve özetlerden biri tutmuyorsa devam etmez.

Kurulum `%LOCALAPPDATA%\Programs\VidShrink` altına yapılır; FFmpeg ve FFprobe WinGet'ten
çekilir; Masaüstü ve Başlat Menüsü kısayolları başlatıcıyı gösterir; sağ tık girdisi de
eklenir. Aynı komutu yeniden koşmak kurulu uygulamayı en yeni sürümle değiştirir.

Windows için yalnız `win-x64` yayımlanıyor. ARM64 ya da 32 bit olduğu kesin olarak
anlaşılan makinede kurucu durur; güncellemeleri hiç bulunamayacak bir mimariyi kurmaktansa
durmak doğrudur. *Okunamayan* mimari ayrı bir durum: kurucu sırayla
`RuntimeInformation.OSArchitecture`, `PROCESSOR_ARCHITEW6432`, `PROCESSOR_ARCHITECTURE` ve
işletim sisteminin bit genişliğine bakar; hiçbiri ad vermezse 64 bitlik Windows `win-x64`
olarak devam eder ve bunu varsaydığını tek satırla yazar.

`irm | iex` betiği dosyadan değil bellekten koşturur, bu yüzden öntanımlı `Restricted`
ilkesi engel olmaz. Kurumsal bir ilke engelliyorsa
[`Install-VidShrink.ps1`](../Install-VidShrink.ps1) dosyasını indirin, okuyun ve aşağıdaki gibi
koşturun. `-File` yerine dosyayı açıkça UTF-8 okutun: betik imzasız UTF-8 saklanıyor,
Windows PowerShell 5.1 ise imzasız betiği sistem ANSI kod sayfasıyla okuyup ASCII dışı her
karakteri bozuyor.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText('C:\path\to\Install-VidShrink.ps1',[Text.Encoding]::UTF8))"
```

### Windows — çevrimdışı arşiv

`VidShrinkPlayer-Offline-win-x64.zip` (ya da `-win-arm64`) bağlantı olmadan kurar. Arşivi açın
ve o klasörden `VidShrinkPlayer-Setup.exe`'yi koşturun: yanındaki `vidshrink-offline.txt`'yi
görür, uygulamayı, başlatıcıyı, FFmpeg'i, libmpv'yi ve Vulkan yükleyicisini arşivden kurar,
GitHub'a istek atmaz. Her araç kurucuya pinlenmiş SHA-256 ile denetlenir; eksik ya da tutmayan
dosya indirilir, yani bağlantılı makinede en kötü durum olağan kurulumdur.

`Install-VidShrink.ps1` aynı kuralı uygular. Betiği açılmış klasöre kopyalayıp orada koşturun
(ya da klasörü `-OfflineSource C:\yol\klasor` ile verin); işaret dosyası yoksa eskisi gibi
davranır. İşaret dosyası sürümü ve hedefi söyler; başka mimarinin arşivine denk gelen betik ya
da kurucu durur ve indirilecek dosyanın adını söyler. FFmpeg ve libmpv lisans metinleri
arşivin `licenses` klasöründe.

### macOS / Linux — tek satır

```bash
curl -fsSL https://raw.githubusercontent.com/Teknesyum/VidShrink-Player/main/install-vidshrink.sh | sh
```

Kök yetkisi de .NET SDK'sı da gerekmez. Hedef `uname` çıktısından seçilir — `osx-arm64`,
`osx-x64` ya da `linux-x64` — arşiv sürümün sağlama listesine karşı doğrulanır,
`~/.local/share/vidshrink` altına kurulur ve `~/.local/bin/vidshrink` olarak bağlanır. Başka
her mimaride kurucu adını söyleyerek durur.

macOS'ta elinizde gerçek bir uygulama paketi kalır: Finder'da kendi adı ve simgesiyle açılan,
kendi kendine imzalanmış bir `~/Applications/VidShrink.app`. `--uninstall` paketi, yükü ve
kısayolu birlikte kaldırır. Linux'ta paket yok; başlatıcı bağlantısı her şeydir.

FFmpeg, bu kurucunun makinenize koymayacağı tek şey. `ffmpeg` ya da `ffprobe` yoksa paket
yöneticinizin komutunu yazar — `brew install ffmpeg`, `sudo apt install ffmpeg`,
`sudo dnf install ffmpeg` — ve başka hiçbir şey indirmeden durur.

macOS'ta kurucu önce Homebrew libmpv'sine bakar, varsa onu kullanır ve hiçbir şey indirmez; yoksa
`deps-libmpv-macos-mpvkit-1.0.0` sürümünden pinlenmiş
tek bir evrensel `libmpv.2.dylib` (MPVKit 1.0.0, LGPL yapı), SHA-256 doğrulamasıyla paketin
`tools/libmpv` klasörüne. İndirme ya da doğrulama tutmazsa dosya silinir; Homebrew'da libmpv
yoksa `brew install mpv` komutunu yazıp durur. Dylib ad-hoc imzalı, noterleme yok.

### Gerekenler

- Windows 10 ya da 11, macOS 14 ve üstü (macOS 14 ve 15'te sınandı; libmpv'nin yükleme komutları 13.0 diyor ama macOS 13'te koşulmadı), ya da X11/Wayland koşan bir Linux masaüstü
- Uygulamanın yanındaki `tools/ffmpeg` klasöründe ya da `PATH` üzerinde `ffmpeg` ve `ffprobe`
- .NET çalışma zamanı ve SDK gerekmez. Sürümler kendi kendine yeterli

Donanım kodlaması isteğe bağlıdır. Eksik ya da bozuk bir GPU kodlayıcısı bildirilir ve motor
sıradaki adaya geçer; ölümcül değildir.


### Güncel kalmak

Windows'ta uygulama kendini sormadan günceller. Kısayollar uygulamanın üstünde duran küçük
bir başlatıcıyı, `VidShrink.exe`'yi gösterir. Tipik bir sürüm 519 MB'lık kurulumun yaklaşık
1,7 MB'ını değiştirir; telden inen de yalnız o kadardır.

Açılış yolunda ağa çıkan hiçbir iş yok. Uygulama açılmadan önce başlatıcı yalnız yerel işi
yapar: yarım kalmış başlatıcı değişimini toplar ve hazır bekleyen güncellemeyi yerine taşır.
Bildirim çekmek ve indirmek uygulama ekrana geldikten **sonra** koşar; topladığı iş bir
sonraki açılışta uygulanır — doğrulanmış dosyaları taşımak milisaniyeler sürer.

```mermaid
flowchart TD
    S["Kısayol"] --> LA["VidShrink.exe başlatıcı"]
    LA --> RES["Hazır bekleyeni yerine taşı"]
    RES --> RUN["Kurulu uygulamayı başlat"]
    RUN --> MF["Bildirimi çek, 5 sn zaman aşımı"]
    MF -->|"çevrimdışı, hız sınırı ya da bozuk"| END["Sessizce vazgeç"]
    MF --> DIFF["Dosya dosya SHA-256 karşılaştır"]
    DIFF -->|"fark yok"| END
    DIFF --> DL["Yalnız değişen dosyaları ara klasöre indir"]
    DL --> VER{"Her özet doğrulanıyor mu?"}
    VER -->|hayır| END
    VER -->|evet| NEXT["Hazır; sonraki açılışta uygulanır"]
```

Ara klasör başarısız turdan sağ çıkar. Kopan hat ya da zaman aşımı inen dosyaları yerinde
bırakır, sonraki tur onları özetinden tanıyıp atlar; böylece yavaş hat sıfırdan başlamak
yerine birkaç açılışta yakınsar. Ara klasör yalnız başka bir sürüm için toplanmışsa atılır.
Doğrulanmamış bayt hiç yazılmadığı için yarım bir klasör yanlış dosya taşıyamaz. Aynı anda
tek başlatıcı toplar; ikincisi işin zaten yürüdüğünü görüp hiç başlamaz.

Sürümler hata ayıklama simgesi taşımaz. `.pdb` dosyaları yapıyı ayıklamayan hiç kimseye
yaramaz; onlara hiç sahip olmamış bir kurulum ise her birini eksik dosya sayıp her açılışta
yeniden indiriyordu.

Başlatıcı uygulamanın açılmasını hiçbir zaman engellemez. Ağ yok, DNS çözülmüyor, hız
sınırı, bozuk bildirim, dolu disk: hepsinde sessizce vazgeçer, kurulu sürüm olduğu gibi
kalır. FFmpeg sürümlerle taşınmaz ve bir daha indirilmez; başlatıcı yalnız `ffmpeg.exe`
ile `ffprobe.exe` yerinde mi diye bakar.

Otomatik güncelleme öntanımlı olarak açıktır ve ayarlardan kapatılabilir. Anahtar
`%APPDATA%\VidShrink\settings.json` içinde, çalıştırılabilirin yanında değil öteki
ayarlarınızın yanında durur; bu yüzden yeniden kurmak onu sıfırlamaz. Kapalıyken Windows da
ötekiler gibi davranır: uygulama açılışta bir kez yeni sürüm var mı diye sorar ve size
söyler.

Başlatıcısı olan her kurulumda o uyarı bir **Yükle** düğmesi taşır. Uygulama kendini
güncelleyemez — kendi dll'lerini tutan süreç odur — bu yüzden düğme başlatıcıyı elle yükleme
kipinde açar ve uygulamayı kapatır; başlatıcı onun çıkışını bekler, güncellemeyi açılış
panelinin arkasında indirip uygular ve yeni sürümü açar. Tek tık, sonrasında ekrandaki sürüm
yeni olandır. Düğme kendiliğinden güncelleme anahtarını okumaz ve hiç yazmaz: elle bir kez
yüklemek tercihinizi olduğu gibi bırakır. Panelin tek eylemi budur; içinde kabuk komutu
yazmaz. Başlatıcı olmayan yerde — Linux kurulumu, düz bir macOS kopyası — aynı düğme yayın
sayfasını açar, çünkü orada sürülecek bir şey yok.

macOS'ta güncelleme paketin tamamını takas eder. Bir paketin imzası içindeki her dosyayı
kapsar; dosya dosya güncelleme imzayı bozar ve uygulama açılmayı reddeder. Yeni paket siz
çalışırken kurulu olanın yanında kurulur, imzası herhangi bir şey yer değiştirmeden *önce*
doğrulanır ve ancak ondan sonra ikisi atomik olarak yer değiştirir — üstelik uygulama
çıkarken, yani koşan bir sürecin altından çekilmeden. Kendini güncelleme yalnız güvenle
yapılabildiği yerde açıktır: `~/.local/share` altındaki düz yük kurulumu ya da macOS'un salt
okunur bir yola taşıdığı paket, anahtarı kapalı tutar ve yeni sürümü yalnız haber alır.

Linux'ta uygulama yalnız yeni sürüm olduğunu söyler. Kurulum komutunu yeniden koşarak
güncellersiniz.

| | Windows | macOS | Linux |
|---|---|---|---|
| Yayımlanan hedef | `win-x64` | `osx-arm64`, `osx-x64` | `linux-x64` |
| Kurucu | `Install-VidShrink.ps1` | `install-vidshrink.sh` | `install-vidshrink.sh` |
| Sağ tık menüsü | var | yok | yok |
| Kendini güncelleme | dosya düzeyinde, başlatıcıyla | paketin tamamını takas | yalnız haber |
| FFmpeg nereden | WinGet `Gyan.FFmpeg` | sizin `brew` | sizin `apt` ya da `dnf` |

![VidShrink macOS'ta kendi uygulama paketinden açılmış, altında Dock görünüyor](gorseller/macos-paket-uygulama.png)


## Dış ikililerin lisansı

FFmpeg kendi lisansı altında ayrı bir programdır ve VidShrink onu yeniden dağıtmaz.
Windows'ta kurucu WinGet'ten GPLv3 yapıları olan `Gyan.FFmpeg`'i ister; macOS ve Linux'ta
kurucu hiçbir şey kurmaz, paket yöneticinizin komutunu yazar. İki durumda da ikili kendi
makinenize, kendi koşullarıyla, kurulum anında iner. VidShrink `ffmpeg` ve `ffprobe`'u dış
süreç olarak koşturur ve AGPL-3.0 uygulamasının içine hiçbir GPL kodu bağlamaz.

Sürümler FFmpeg taşımaz; bunun nedeni lisans değil boyut: FFmpeg ve FFprobe 519 MB'lık
kurulumun 424 MB'ı ve VidShrink değişince onlar değişmiyor. FFmpeg içeren paketli bir sürüm
hazırlayan biri, lisansı bu paragrafa güvenerek değil o yapı için baştan çalışmalıdır.

Oynatıcı sekmesinin motoru libmpv de aynı biçimde ele alınır: VidShrink onu yeniden
dağıtmaz. Windows'ta kurucu [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake)
üzerinden pinlenmiş tek bir yapıyı doğrudan makinenize indirir. macOS'ta
[MPVKit](https://github.com/mpvkit/MPVKit) 1.0.0'ın LGPL statik kitaplıklarından (`-Dgpl=false`)
bağlanmış tek bir evrensel yapıyı indirir; ad-hoc imzalı, noterlenmemiş. Linux'ta paket
yöneticinizden gelir. VidShrink onu çalışma anında C arayüzünden yükler, kütüphanenin kendi
lisansı altında.
