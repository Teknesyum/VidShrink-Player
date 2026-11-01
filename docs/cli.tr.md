# VidShrink Komut Satırı

[README](../README.tr.md) içindeki *Komut satırı* bölümünün uzun hâli.
[English](cli.md)

Aynı paket, pencerenin yanında başsız bir komut satırı aracı da taşıyor: Windows ve
Linux'ta `vidshrink`, macOS'ta `vidshrink-cli`. Pencerenin çağırdığı karar motorunu
çağırıyor, yani aynı girdi aynı ffmpeg argümanlarını veriyor; bir test ikisini
karşılaştırıyor.

```bash
vidshrink kucult clip.mp4 --hedef 25MB              # boyuta küçült
vidshrink kucult clip.mp4 --kalite 80 --kodek av1   # kalite puanına küçült
vidshrink plan clip.mp4 --hedef 8MB --json          # yalnız plan ve argümanlar, kodlama yok
```

## Anahtarlar, İngilizce Takma Adlar Ve Çıkış Kodları

Anahtarlar: `--kodek auto|h264|hevc|av1`, `--cikti <yol>`, `--json`, `--olcumsuz` (yoklama
kodlamalarını atlar), `--vmaf` (ffmpeg'de libvmaf varsa sonucu ölçer), `--hizli`. İlerleme
stderr'e, boyut, süre, deneme sayısı ve VMAF stdout'a gidiyor. Yardım metni sistem dilini
izliyor, Türkçe ya da İngilizce. `--dil en` (`--lang en`) tek koşumluk olarak bunu eziyor;
bilinen kodlar `en` ve `tr`, tanınmayan kod sessizce İngilizce'ye düşmek yerine kullanım hatası
veriyor.

`--olcerek-ara` (`--search-by-measure`) `--kalite` ile çalışıyor: Gelişmiş paneldeki "Ölçerek
ara" kutusu gibi modelin hedefinden başlıyor, kısa örnekler kodlayıp ölçüyor, en çok 4 tur;
plan puanı tutan boyutu alıyor. Varsayılan kapalı; libvmaf yoksa modelin hedefi kalıyor. Turlar
stderr'e (`--json` ile `"type":"search"`), sonuç planın `search` alanına gidiyor.

`--crf N` ve `--on-ayar AD`, pencerede Gelişmiş panelinin kilitlediğini kilitliyor: kalite
değeri (0-63) ve kodlayıcı ön ayarı. Ön ayar adı planın seçtiği kodeğe ait olmalı —
x264/x265 için `slow`, NVENC için `p5`, SVT-AV1 için `8` — ait olmayan ad hata vermiyor,
plan gerekçesine bir satır düşülerek düşüyor.

`--modul N` (`--modulus N`) ölçeklenen kenarları 2, 4, 8 ya da 16'nın katına aşağı
yuvarlıyor. Varsayılan 2, çünkü kodlayıcı tek sayılı kenar kabul etmiyor; büyük çarpan eski
donanım kodlayıcılarının istediği şey ve kenardan biraz daha kırpıyor. Başka bir sayı
kullanım hatası.

Anamorfik kaynak — depolanan pikselleri kare olmayan bir DVD — ölçeklenmeden önce
düzleştiriliyor: yükseklik korunuyor, genişlik karenin ekranda göründüğü genişliğe
çevriliyor ve çıktı kare pikselli oluyor. Kare pikselli kaynakta hiçbir şey değişmiyor.

`--kes <baslangic>-<bitis>` kodlamadan önce kesiyor, böylece hedef boyut elde kalan parçaya
harcanıyor: `--kes 10-40`, `--kes 0:10-0:40`, `--kes 1:02:03-1:02:04`, sona kadar `--kes 90-`.

Uçlardan biri saat yerine kare numarası olabilir: `--kes 300f-900f` 300. kareden 900. kareye,
çevirim kaynağın kare hızıyla. `--bolum 2` ve `--bolum 2-4` aynı pencereyi kaynağın bölüm
işaretlerinden kurar; iki seçenek birlikte verilemez.

`--yak N` (`--burn N`) kaynağın N. altyazısını görüntüye yakıyor. Metin altyazıyı libass
çiziyor; resim altyazı (PGS, VOBSUB, DVB) karenin üstüne bindiriliyor, kare genişliğine
ölçekli ve alta hizalı. MP4 resim altyazı taşıyamıyor; `--yak` verilmezse düşüyor ve plan
bunu seçeneğin adını vererek söylüyor. Arayüz aynısını "Görüntüye yak" listesinden yapıyor;
liste metin ve resim altyazıyı birlikte sunuyor.

`--altyazi-tara` (`--subtitle-scan`) o izi sizin yerinize seçiyor: filmin yalnız yabancı dilde
konuşulan yerlerini çeviren altyazıyı. Zorunlu (forced) bayraklı iz varsa o; yoksa altyazı
paketleri sayılıyor ve aynı dildeki en dolu izin en çok %10'u kadar paket taşıyan tek iz
yakılıyor. Sonuç açık değilse hiçbir şey yakılmıyor ve nedeni bir satırla söyleniyor. `--yak`
ile birlikte verilemiyor. Uygulamada "Görüntüye yak" listesinin yanındaki "Yabancı dil
altyazısını bul" düğmesi aynı işi yapıyor ve bulduğu izi listede seçiyor.

Kapak resmi MP4 ve MKV çıktıda korunuyor (MKV'de png ve jpeg, ek olarak). MOV taşıyamıyor;
plan bunu söylüyor.

Aşağıdaki uzun anahtarların her birinin bir de İngilizce takma adı var; iki yazım aynı
anahtar, betik hangisini isterse onu kullanabiliyor. Tek istisna `--crf` ve `--json` ve `--vmaf`:
bunların tek yazımı var.

| Türkçe | İngilizce |
|---|---|
| `--yardim` | `--help` |
| `--surum` | `--version` |
| `--hedef` | `--target` |
| `--kalite` | `--quality` |
| `--kodek` | `--codec` |
| `--on-ayar` | `--preset` |
| `--modul` | `--modulus` |
| `--bolum` | `--chapters` |
| `--cikti` | `--output` |
| `--kes` | `--cut` |
| `--aralik` | `--interval` |
| `--bir-kez` | `--once` |
| `--gunluk` | `--log` |
| `--tarama` | `--scan` |
| `--baslik` | `--title` |
| `--ana-icerik` | `--main-feature` |
| `--asgari-sure` | `--min-duration` |
| `--azami-sure` | `--max-duration` |
| `--suzgec` | `--filters` |
| `--kirp` | `--crop` |
| `--kirpma-kipi` | `--crop-mode` |
| `--profil` | `--profile` |
| `--profil-dosyasi` | `--preset-file` |
| `--ses-kodek` | `--audio-codec` |
| `--ses-normal` | `--loudnorm` |
| `--ses-temizle` | `--denoise` |
| `--ses-kazanc` | `--gain` |
| `--altyazi` | `--subtitle` |
| `--yan-altyazi` | `--sidecar-subtitles` |
| `--ses-dosyasi` | `--audio-file` |
| `--ses-degistir` | `--replace-audio` |
| `--yak` | `--burn` |
| `--yak-srt` | `--srt-burn` |
| `--yak-ass` | `--ssa-burn` |
| `--yak-yazi-tipi` | `--burn-font` |
| `--yak-boyut` | `--burn-size` |
| `--yak-kutu` | `--burn-box` |
| `--meta-yok` | `--no-metadata` |
| `--altyazi-dil` | `--subtitle-lang` |
| `--ilk-altyazi` | `--first-subtitle` |
| `--altyazi-tara` | `--subtitle-scan` |
| `--sabit-kare` | `--cfr` |
| `--tavan-kare` | `--pfr` |
| `--kare-hizi` | `--fps` |
| `--ses-hizi` | `--arate` |
| `--ses-drc` | `--drc` |
| `--profiller` | `--presets` |
| `--olcumsuz` | `--no-measure` |
| `--olcerek-ara` | `--search-by-measure` |
| `--hizli` | `--fast` |
| `--oncelik` | `--priority` |
| `--is-parcacigi` | `--threads` |
| `--dil` | `--lang` |
| `--kap` | `--container` |
| `--bit-hizi` | `--bitrate` |
| `--cozunurluk` | `--resolution` |
| `--ses-bit-hizi` | `--audio-bitrate` |
| `--disa-aktar` | `--export` |
| `--ek-arguman` | `--extra-args` |
| `--etiket-baslik` | `--tag-title` |
| `--etiket-sanatci` | `--tag-artist` |
| `--etiket-yil` | `--tag-year` |
| `--etiket-yorum` | `--tag-comment` |
| `--kapak` | `--cover` |
| `--onar` | `--repair` |
| `--bolum-ekle` | `--add-chapter` |
| `--bolumsuz` | `--no-chapters` |
| `--kip` | `--mode` |

Çıkış kodları: bantta `0`, bandın altında `2` (kalite doyduğu için daha küçük dosya
saklandı), boy tavanı aşıldığında `3` (en küçük sonuç yine yazılıyor; JSON `output` ve
`overTarget: true` taşıyor), hatada `1`, yanlış kullanımda `64`, iptalde `130`.

## İzlenen Klasör

```bash
vidshrink izle ~/Gelen --cikti ~/Giden --hedef 25MB             # Ctrl+C'ye kadar koşar
vidshrink izle ~/Gelen --cikti ~/Giden --hedef 25MB --bir-kez   # klasörü boşaltır, çıkar
```

`izle`, klasöre düşen her videoyu küçültüyor. `--cikti` çıktı klasörüdür ve zorunludur;
izlenen klasörün kendisi olamaz.

`--aralik <saniye>` tarama aralığını belirliyor (varsayılan 2), `--bir-kez` beklenecek bir
şey kalmayınca çıkıyor, `kucult`'un öbür anahtarları her dosyaya uygulanıyor. `--json` ile
stdout NDJSON oluyor: dosya başına tek satır JSON nesnesi.

Bir dosya, boyu ve değişiklik saati üst üste iki tarama aralığı boyunca aynı kaldığında ve
onu tutan bir yazıcı olmadığında alınıyor — üçüncü tarama alıyor. Kodlanırken kaynak
değişirse çıktı siliniyor ve dosya durulunca yeniden ele alınıyor.

İlerleme, izlenen klasörün içindeki `.vidshrink-izle.json` dosyasında ada ve boya göre
tutuluyor; adı ya da boyu değişen dosya yeni sayılıyor. O klasör salt okunursa durum çıktı
klasörüne `.vidshrink-izle-<ozet>.json` olarak, o da tutmazsa ayar klasörüne
`izle-<ozet>.json` olarak yazılıyor.

`<ozet>`, izlenen klasörün yolunun SHA-256'sının ilk 16 onaltılık karakteri, küçük harfle.
Yol özete aşağıdaki iki kıyasla aynı kurala göre giriyor: koşan sistem harfi yok sayıyorsa
(Windows ve macOS) önce büyük harfe çevriliyor, Linux'ta olduğu gibi alınıyor. Yani `/gelen`
ile `/Gelen` Windows ve macOS'ta tek bir durum dosyasını paylaşıyor, Linux'ta iki ayrı dosya
alıyor; aynı klasör her koşumda aynı adı veriyor.

Başarısız olan dosya, bir sonraki açılışta bir kez yeniden denenir. Her şeyi yeniden
işlemek için durum dosyasını silin.

Koşan sistemin kuralına uyan tam iki kıyas var: izlenen klasörün çıktı klasörüyle
kıyası, ve bir adayın bu koşumun yazdığı çıktı adlarıyla kıyası. Bu ikisi Linux'ta
`Ordinal`, Windows ile macOS'ta `OrdinalIgnoreCase`.

Kuralın macOS yarısı varsayılan APFS bölümünü varsayıyor; o bölüm harf duyarsız ama harf
koruyordur. APFS harf DUYARLI da biçimlendirilebilir ve böyle bir bölümde — ya da harf
duyarlı bir dış bölümde — bu varsayım tutmuyor.

Dosya adının geri kalan her kullanımı **her platformda, Linux dahil** harfi yok sayıyor:
bekleyen, yeniden denenecek ve atlanan tabloları, tarama sırası ve işlenenlerin kaydı. Yani
`Klip.mp4` ile `klip.mp4` aynı izlenen klasörde, dosya sistemi ikisini iki ayrı dosya
olarak tutsa bile çakışıyor.

Çakışma taramanın içinde çözülüyor. Çakışan adlardan sıralı (ordinal) küçük olan kazanıyor
ve olağan akışta küçülüyor; öbürleri atlanıyor ve her biri için bir uyarı satırı yazılıyor:
`Atlandı: klip.mp4 — Klip.mp4 ile ad çakışıyor (harf farkı). Birini yeniden adlandırın.`
Satır dosya başına bir kez yazılıyor, sonraki taramalarda tekrarlanmıyor. Kazanan her
taramada aynı, yani koşum belirlenimli. Atlanan dosyayı yeniden adlandırınca izleyici onu
yeni dosya olarak görüp küçültüyor.

Çıkış kodları: bittiğinde `0`, `--bir-kez` bitip en az bir dosya başarısız olduğunda ya da
ad çakışması yüzünden atlandığında `4`, hatada `1`, yanlış kullanımda `64`, Ctrl+C ile
durdurulduğunda `130`. `--bir-kez` sonunda kaç dosyanın atlandığı bir özet satırında yazıyor.

## Dönüştür

`donustur` (`convert`) penceresiz Dönüştür sekmesi: aynı argüman üreticisi
(`ConversionArguments`), aynı kodek ve kap kümesi, aynı çıktı adı (kaynağın yanında
`<ad>_converted.<uzantı>`). Aynı ayarla komut satırı ve sekme aynı ffmpeg komutunu
yazıyor; bir test ikisini karşılaştırıyor.

```bash
vidshrink donustur clip.mov --kap mp4 --kodek hevc --crf 24
vidshrink donustur clip.mp4 -o clip.webm --kodek vp9 --bit-hizi 2000
vidshrink donustur clip.mp4 --kap gif --cozunurluk 480 --kare-hizi 15
vidshrink donustur clip.mkv --kap mp3 --ses-bit-hizi 192 --json
```

Anahtarlar: `--kap <kap>` (mp4, mkv, webm, mov, avi, gif, mp3, m4a, wav, flac, webp, avif,
mxf, png, jpg, opus, ogg, aiff, ac3; verilmezse `--cikti` uzantısı belirliyor, o da yoksa
mp4), `--kodek <kodek>` (h264, hevc, vp9, av1, prores, dnxhr, `copy` ya da `libx264`,
`h264_nvenc`, `prores_ks:hq` gibi kodlayıcı adı), `--crf <n>` (kodeğin kendi aralığında) ya
da `--bit-hizi <kbit/sn>` (ikisi birden değil), `--cozunurluk kaynak|<yükseklik>|<G>x<Y>`,
`--kare-hizi kaynak|<fps>`, `--ses-kodek <kodek>` (aac, opus, mp3, pcm, flac, ac3, eac3,
`copy`, `yok`), `--ses-bit-hizi <kbit/sn>`, `--kes <aralık>`, `--meta-yok`, `--cikti <yol>`,
`--ek-arguman "<seçenekler>"` (çıktı yolundan hemen önce yazılan ek ffmpeg seçenekleri; tırnak
boşluğu korur, bayrak tekrarlanınca eklenir; `-i`, `-y`, `-n`, `-f`, `-progress`, `-`, `--` ve
ikinci çıktı olacak başıboş değer `64` ile reddedilir), `--json`. Sekmenin reddedeceği bileşim (kabın taşımadığı ses kodeği, tek sayılı boyut) aynı
gerekçeyle ve `64` çıkış koduyla reddediliyor; yerel ffmpeg'de kodlayıcısı olmayan kap `1`
veriyor.

Etiket, kapak, Onar ve bölümler sekmenin Etiketler ve Bölümler açılırlarını izliyor:
`--etiket-baslik`, `--etiket-sanatci`, `--etiket-yil`, `--etiket-yorum` kabın başlık, sanatçı,
tarih ve yorum etiketini yazıyor (boş değer anahtar yazmıyor; `--meta-yok` onları silmiyor);
`--kapak <png|jpg>` kapak ekliyor (MKV'de ek, MP4'te yalnız video kopyasında kapak izi; dosya
yoksa `1`). `--onar` sekmenin Onar hazır ayarı: bozuk paket atılıyor, eksik zaman damgası
üretiliyor, çıktı sıfırdan başlıyor; `--kap`, `--kodek`, `--ses-kodek` verilmezse video ve ses
MKV'ye kopyalanıyor. `--bolum-ekle <zaman>=<ad>` (tekrarlanabilir, `90=Ana` ya da `1:30=Ana`)
kaynağın bölümlerinin yerine bu listeyi yazıyor; zaman kaynağın başından sayılıyor, `--kes`
kaydırıyor. `--bolumsuz` bölüm yazmıyor. İkisi mp4, mkv ya da mov istiyor ve birlikte
verilemiyor (`64`).

```bash
vidshrink donustur bozuk.ts --onar
vidshrink donustur konusma.mkv --etiket-baslik "Konuşma" --bolum-ekle 0=Giriş --bolum-ekle 12:30=Sorular
```

Komut satırı sekmenin GIF'ten GIF'e boyut hedefini, ses izi seçimini ve harici ses dosyasını
taşımıyor.

## Proje Teslimi

`teslim` (`export`) düzenleyicinin kaydettiği projeyi (`.vsproj.json`) pencere açmadan teslim
ediyor. Çizelge projeden kuruluyor (`EditProject.ToJob`): her parça, metin, müzik izi, ses izi
seçimi ve sonradan eklenen kaynak; plan ve koşu düzenleyicinin kendi teslim yolu
(`EditExportRunner`). Komut satırının `--kes` anahtarı tek aralık kesiyor; çok parçalı kesim
bu komutla, düzenleyicide kaydedilmiş projeden yineleniyor.

```bash
vidshrink teslim kesim.vsproj.json
vidshrink teslim kesim.vsproj.json --cikti son.mp4 --kip tam --json
```

Anahtarlar: `--kip hizli|akilli|tam` (`--mode fast|smart|full`; verilmezse projeye kayıtlı
kip, o da tanınmazsa akıllı), `--cikti <yol>` (verilmezse ilk kaynağın yanında
`<ad>-duzenlenmis.<uzantı>`), `--meta-yok`, `--json`. Metin, efekt, müzik ya da birden çok
kaynak düzenleyicide olduğu gibi kipi tama çeviriyor; stdout uygulanan kipi yazıyor.

Proje kaydedildikten sonra bir kaynağın boyu ya da yazma zamanı değiştiyse ya da kaynak
yoksa teslim başlamıyor ve çıkış kodu `1`; okunamayan proje, boş çizelge ve bellek sınırını
aşan ters klip de `1` veriyor. Çıktı yolu bir kaynağın ya da projenin kendisiyse `64`.
Parçaları ayrı dosyalara yazmak ve sıraya konan toplu teslim bu komutta yok.

## NDJSON İlerleme

`--json` verilince stderr ilerlemesi de NDJSON oluyor, satır başına bir sıkışık nesne;
`kucult`, `plan` ve `donustur` için. stdout'taki sonuç nesnesi değişmiyor. `izle`de dosya
başına ilerleme satırları aynı biçimde; izleyicinin kendi olay satırları metin kalıyor.

```json
{"type":"stage","stage":"probe"}
{"type":"progress","percent":42,"fraction":0.4213,"stage":"pass 1/2","remainingSeconds":12.5}
```

`stage` satırları adımı söylüyor (`probe`, `crop`, `measure`, `calibrate` vb.); `progress`
satırları yalnız yüzde ya da aşama değişince tekrarlanıyor, `remainingSeconds` bilinmezken
`null`.

## Ön Ayarları Dışa Aktarma

`profiller --disa-aktar <dosya>` kendi ön ayarlarınızı uygulamanın "İçe aktar"ının okuduğu
bir JSON dosyasına yazıyor (`PresetLibrary.Export`). `--profil <kimlik ya da ad>`
(tekrarlanabilir) bir kısmını seçiyor; verilmezse hepsi yazılıyor. Gömülü ön ayarlar her
kopyada zaten var ve dışa aktarılmıyor; birini istemek de bilinmeyen ad da `64` veriyor.

```bash
vidshrink profiller --disa-aktar profiller.json
vidshrink profiller --disa-aktar discord.json --profil "Discord klibi"
```

## Kabuk Tamamlama

`tamamlama <kabuk>` (`completion`) `bash`, `zsh`, `fish` ya da `powershell` (`pwsh`) için
tamamlama betiği yazdırıyor. Komutları, anahtarları ve kapalı kümeli anahtarların
değerlerini (kodek, kap, profil, dil) tamamlıyor.

```bash
source <(vidshrink tamamlama bash)                              # ~/.bashrc
source <(vidshrink tamamlama zsh)                               # ~/.zshrc
vidshrink tamamlama fish | source                               # ~/.config/fish/config.fish
vidshrink tamamlama powershell | Out-String | Invoke-Expression # $PROFILE
```
