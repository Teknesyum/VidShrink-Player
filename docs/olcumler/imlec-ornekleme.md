# İmleç Örneklemesi

Kayıt sonrası yakınlaştırma (later 114) için kayıt sırasında imleç konumu `PointerSampler` ile
`GetPhysicalCursorPos`'tan 30 Hz'de, BelowNormal öncelikli tek bir iş parçacığında okunur; tık ve
tuşlar zaten açık olan girdi kancasından gelir. Bu ölçü örneklemenin kayıt yükünü artırıp
artırmadığına bakar.

Komut: `dotnet run --project tools/VidShrink.Bench -c Release -- pointer-sampling --seconds 30 --runs 3`
(2026-10-21, 16 çekirdek, Windows 11, aynı makinede paralel çalışan başka ajanlar vardı).

## Örnekleyicinin Kendi Yükü

Süreç CPU zamanı (`Process.TotalProcessorTime`) örnekleyici kapalıyken ve açıkken 30 sn boyunca.

| Kol | Okuma | Gerçek hız | CPU |
|---|---|---|---|
| Kapalı | 0 | — | 0,0 ms |
| 30 Hz | 910 | 30,3 Hz | 0,0 ms |
| 120 Hz | 1933 | 64,4 Hz | 0,0 ms |

Windows süreç CPU sayacı 15,6 ms adımla ilerler; 30 sn'de bir adım bile dolmadı. Yani örnekleyici
saniyede 0,5 ms'den az CPU harcıyor, tek çekirdeğin binde beşinden az. 120 Hz istenince gerçek hız
64 Hz'de kalıyor (`Thread.Sleep` zamanlayıcı çözünürlüğü); uygulama 30 Hz kullandığı için önemli değil.

## Kayıtta Düşen Kare

gdigrab, 1280x720, 30 fps, 15 sn, libx264 ultrafast, `-threads 2`. Turlar sırayı değiştirerek
koşuldu (kapalı-açık, açık-kapalı, kapalı-açık).

| Tur | Örnekleyici | Kare | Düşen | ffmpeg CPU | Okuma |
|---|---|---|---|---|---|
| 1 | Kapalı | 449 | 1 | 172 ms | 0 |
| 1 | Açık | 450 | 0 | 266 ms | 457 |
| 2 | Açık | 450 | 0 | 62 ms | 458 |
| 2 | Kapalı | 450 | 0 | 203 ms | 0 |
| 3 | Kapalı | 449 | 1 | 156 ms | 0 |
| 3 | Açık | 446 | 4 | 1359 ms | 459 |

Aynı günün ilk koşusu (`--seconds 10 --runs 2`): kapalı 1 ve 2 düşen kare, açık 0 ve 0.

## Hüküm

Örnekleyicinin kendi yükü ölçüm çözünürlüğünün altında. Düşen kare iki kolda da 0-4 arasında
dağılıyor ve örnekleyiciye bağlı bir yön göstermiyor; 3. turdaki 4 kare ve 1359 ms ffmpeg CPU,
aynı anda koşan başka işlerin gürültüsü (ffmpeg CPU'su aynı kolda 62 ms ile 1359 ms arasında oynadı).
Kayıt yükü örneklemeyle artmıyor. Bu makine paylaşımlıydı; sessiz makinede yeniden koşmak ölçüyü
keskinleştirir ama hükmü değiştirmesi beklenmez.

## Yakınlaştırma Titremesi: zoompan Ve perspective

Komut: `dotnet run --project tools/VidShrink.Bench -c Release -- zoom-jitter` (2026-11-01, ffmpeg 9.0,
`-threads 2`). lavfi'den 640x360, 30 fps, 4 sn siyah kaynak, ortasında (320,180) 8x8 beyaz kutu; iki yan yana
bölge [0,3–1,9 sn, merkez (0,45; 0,45)] ve [2,1–3,7 sn, merkez (0,55; 0,55)], 2×. Her karede kutunun ağırlık
merkezi ham gri kareden okunur ve `AutoZoom.At`'ın verdiği yakınlık ve merkezden hesaplanan beklenen yerle
karşılaştırılır. "Sıçrama" bu sapmanın bir kareden ötekine değişimidir; titremenin kendisi.

| Filtre | Kol | Kare | En büyük sapma | Sıçrama ort. | Sıçrama en büyük |
|---|---|---|---|---|---|
| zoompan | rampa | 23 | 3,91 px | 1,36 px | 2,97 px |
| zoompan | kaydırma | 18 | 3,86 px | 1,67 px | 3,36 px |
| zoompan | sabit | 61 | 0,00 px | 0,00 px | 0,00 px |
| fps+perspective | rampa | 23 | 0,04 px | 0,03 px | 0,07 px |
| fps+perspective | kaydırma | 18 | 0,06 px | 0,04 px | 0,09 px |
| fps+perspective | sabit | 61 | 0,00 px | 0,00 px | 0,00 px |

`zoompan` kesit kutusunun köşesini giriş pikseline yuvarlıyor; 2×'te bu çıktıda 2 px'lik basamak, yakınlık
değişirken ölçek de yuvarlandığı için 3-4 px'e çıkıyor. Yakınlık ve merkez sabitken titreme yok; yalnız rampa
ve kaydırma sırasında görünür.

`crop`+`scale` zinciri denendi ve elendi: `scale=eval=frame` kare boyutunu her karede değiştiriyor, `crop` ise
giriş boyutunu yapılandırmada bir kez okuyup `x`'i eski genişliğe kırpıyor; kutu 252-380 px yanlış yerde çıktı.
Yerine `perspective` (`eval=frame`, kübik) aynı dört köşeyi alt pikselle örnekliyor. Saati `in` kare sayacından
gelir; `in` 1'den başladığı için `(in-1)/fps` kullanılır (ilk denemede `in/fps` bir kare önden gidiyordu, kaydırmada
11 px sapma), sabit kare hızı için önüne `fps` konur. Yarım piksel kayması köşeye `(1/z-1)/2` eklenerek giderildi
(önce sabit kolda 0,50 px).

Hız, 1920x1080 testsrc2, 120 kare, `-f null`, iki iş parçacığı:

| Filtre | Süre | Kare/sn |
|---|---|---|
| yok | 0,30 sn | 403 |
| zoompan | 0,29 sn | 412 |
| fps+perspective | 1,50 sn | 80 |

`perspective` beş kat yavaş ama gerçek zamanın iki katından hızlı; ardından gelen libx264 kodlaması yine
belirleyici. İfade her köşede yinelendiği için uzun: 1 bölge 1673, 10 bölge 6361, 30 bölge 16801 karakter.
24000'i (`AutoZoom.MaxPerspectiveLength`) aşınca Windows komut satırı sınırına (32767) yaklaşmamak için
`zoompan`'a düşülür; 60 ayrık bölgede bu oldu (7655 karakter).
