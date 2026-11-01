# Fluent Simge Eşlemesi

Kaynak: [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) (MIT).

Bütün yollar **Filled** temanın **24 px** çiziminden geliyor; her dosya tek bir `<path>` gövdesi
taşıyor ve `assets/<Klasör>/SVG/<dosya>` yolundan ham olarak alındı. Gövde `Themes/Icons.axaml`'a
`F1` (NonZero) dolgu kuralı ve `M 0,0 M 24,24` sabitleyicisiyle giriyor; arayüz artık kalemle
değil `Fill` ile çiziyor, bu yüzden `IconStroke` belirteci düştü.

Sayı hiçbir yerde sabit yazılı değil. `docs/danisma/2026-09-17-fable-kararlar.md` §9 "26 yol"
diyor; o, 13 Eylül ölçümünün sayısıdır. Takım Fluent'e taşınırken `IconRestore` eklenmiş (27),
ardından kullanılmayan iki geometri düşürülmüştü; 18 Eylül 2026'da uyarı durumu için
`IconWarning` eklendi (26); 27 Eylül 2026'da Teknesyum düğmesi simgesiz kalınca `IconCode`
düştü; 27 Eylül 2026'da düzenleyici sekmesi için `IconEditor` eklendi; 28 Eylül 2026'da bölge düzenleyicinin kare al ve çerçeveyi gizle düğmeleri için `IconCamera` ile `IconEyeOff` eklendi; 2 Ekim 2026'da oynatıcının hız A/B harfi göstergeye dönünce `IconSpeed` yerini `IconSpeedFast`, `IconSpeedFastTip` ve `IconSpeedSlow`'a bıraktı (kadran Fluent Top Speed, ibre türetildi; kadranın sol üst çentiği yavaş ibreye çarpmasın diye düştü); 15 Ekim 2026'da oynatıcının yer imi listesi ve geçmiş penceresindeki yer imi işareti için `IconBookmark` eklendi; 1 Kasım 2026'da düzenleyicinin kilitli iz başlığı için `IconLock` eklendi — bugün depoda **32** yol var. `IconsTests` bu
tabloyla `Icons.axaml`'ı karşılıklı okur; tabloya girmeyen ya da tablodan düşen her anahtar
ölçüyü kırar.

| # | Anahtar | Fluent klasörü | Fluent dosyası | Sürüm / boy | Kullanıldığı yer |
|---|---|---|---|---|---|
| 1 | IconPlayer | `Play Circle` | `ic_fluent_play_circle_24_filled.svg` | Filled / 24 px | Oynatıcı sekmesi |
| 2 | IconShrink | `Arrow Minimize` | `ic_fluent_arrow_minimize_24_filled.svg` | Filled / 24 px | Sıkıştır sekmesi |
| 3 | IconConvert | `Arrow Swap` | `ic_fluent_arrow_swap_24_filled.svg` | Filled / 24 px | Dönüştür sekmesi |
| 4 | IconRecorder | `Video` | `ic_fluent_video_24_filled.svg` | Filled / 24 px | Kaydedici sekmesi |
| 5 | IconAdvanced | `Options` | `ic_fluent_options_24_filled.svg` | Filled / 24 px | Gelişmiş bölüm başlığı |
| 6 | IconAbout | `Info` | `ic_fluent_info_24_filled.svg` | Filled / 24 px | Hakkında sekmesi |
| 7 | IconSettings | `Settings` | `ic_fluent_settings_24_filled.svg` | Filled / 24 px | Ayarlar sekmesi (K4) |
| 8 | IconPlay | `Play` | `ic_fluent_play_24_filled.svg` | Filled / 24 px | Oynat |
| 9 | IconPause | `Pause` | `ic_fluent_pause_24_filled.svg` | Filled / 24 px | Duraklat |
| 10 | IconRewind | `Rewind` | `ic_fluent_rewind_24_filled.svg` | Filled / 24 px | Geri sar |
| 11 | IconFastForward | `Fast Forward` | `ic_fluent_fast_forward_24_filled.svg` | Filled / 24 px | İleri sar |
| 12 | IconVolume | `Speaker 2` | `ic_fluent_speaker_2_24_filled.svg` | Filled / 24 px | Ses açık |
| 13 | IconVolumeMute | `Speaker Mute` | `ic_fluent_speaker_mute_24_filled.svg` | Filled / 24 px | Ses kapalı |
| 14 | IconSpeedFast | `Top Speed` | `ic_fluent_top_speed_24_filled.svg` | Filled / 24 px | Hızlı kip (kadran Fluent, ibre türetildi: sağa 60°) |
| 15 | IconSpeedFastTip | `Top Speed` | `ic_fluent_top_speed_24_filled.svg` | Filled / 24 px | Hızlı ibrenin turuncu ucu (`EmberFlameGlyph`: alev, şerit zeminlerinde 4,5:1 tutana dek uyarı rengine karışır), IconSpeedFast üstüne bindirilir |
| 16 | IconSpeedSlow | `Top Speed` | `ic_fluent_top_speed_24_filled.svg` | Filled / 24 px | Yavaş kip (kadran Fluent, ibre türetildi: sola 60°) |
| 17 | IconFullScreen | `Full Screen Maximize` | `ic_fluent_full_screen_maximize_24_filled.svg` | Filled / 24 px | Tam ekran |
| 18 | IconChevronDown | `Chevron Down` | `ic_fluent_chevron_down_24_filled.svg` | Filled / 24 px | Açılır başlık (kapalı) |
| 19 | IconChevronUp | `Chevron Up` | `ic_fluent_chevron_up_24_filled.svg` | Filled / 24 px | Açılır başlık (açık) |
| 20 | IconStop | `Stop` | `ic_fluent_stop_24_filled.svg` | Filled / 24 px | Kaydı durdur |
| 21 | IconRestart | `Previous` | `ic_fluent_previous_24_filled.svg` | Filled / 24 px | Başa dön |
| 22 | IconClose | `Dismiss` | `ic_fluent_dismiss_24_filled.svg` | Filled / 24 px | Pencere kapat |
| 23 | IconMaximize | `Maximize` | `ic_fluent_maximize_24_filled.svg` | Filled / 24 px | Pencere büyüt |
| 24 | IconMinimize | `Subtract` | `ic_fluent_subtract_24_filled.svg` | Filled / 24 px | Pencere küçült |
| 25 | IconRestore | `Square Multiple` | `ic_fluent_square_multiple_24_filled.svg` | Filled / 24 px | Pencere geri al |
| 26 | IconCoffee | `Drink Coffee` | `ic_fluent_drink_coffee_24_filled.svg` | Filled / 24 px | Bağış bağlantısı |
| 27 | IconWarning | `Warning` | `ic_fluent_warning_24_filled.svg` | Filled / 24 px | Uyarı durumu (`StatusWarning`) |
| 28 | IconEditor | `Cut` | `ic_fluent_cut_24_filled.svg` | Filled / 24 px | Düzenleyici sekmesi |
| 29 | IconCamera | `Camera` | `ic_fluent_camera_24_filled.svg` | Filled / 24 px | Bölge düzenleyicide kare al |
| 30 | IconEyeOff | `Eye Off` | `ic_fluent_eye_off_24_filled.svg` | Filled / 24 px | Bölge düzenleyicide çerçeveyi gizle |
| 31 | IconBookmark | `Bookmark` | `ic_fluent_bookmark_24_filled.svg` | Filled / 24 px | Geçmiş penceresinde yer imli dosya işareti |
| 32 | IconLock | `Lock Closed` | `ic_fluent_lock_closed_24_filled.svg` | Filled / 24 px | Düzenleyici iz başlığında kilitli iz |

## Ölçülen Sınır Kutuları

Sabitleyici çıkarıldıktan sonra `Geometry.Parse(...).Bounds` ile okunan değerler
`IkonKutusuTests` içinde pimli. Fluent'in kendi çiziminden gelen ya da ondan türetilen altı istisna var:

- `IconPlay` — üçgen optik olarak sağa kaydırılmış (cx 13,43).
- `IconSpeedFast`, `IconSpeedSlow` — gösterge kütlesi merkezin üstünde (cy 11,00).
- `IconSpeedFastTip` — yalnız hızlı ibrenin ucu (x 14,37–18,47, y 7,80–11,39); tek başına çizilmez, `IconSpeedFast`'in üstüne bindirilir.
- `IconCoffee` — kulp sağda 2 birimlik kenar payını taşıyor (sağ kenar 23,00).
- `IconLock` — kilit halkası üst kenar payına taşıyor (üst 1,00, cy 11,50).

Bu altısı ayrı ayrı, ölçülen kutularıyla pimlendi; kalan 26 simge genel kurala (mürekkep
2–22 aralığında, merkez 12±0,55) uyuyor.

## Kaldırılan Geometriler (18 Eylül 2026)

İki geometri hiçbir yerden çağrılmıyordu: ne bir AXAML `StaticResource`'unda ne `.cs` içinde tek
atıf vardı. İkisi de Fluent'e taşınmadan önce de ölüydü; taşıma onları öldürmedi, yalnız taşıdı.

| Anahtar | Fluent dosyası | Neden kaldırıldı |
|---|---|---|
| `IconMenu` | `ic_fluent_more_vertical_24_filled.svg` | Uygulamada taşma menüsü düğmesi yok; oynatıcı menüsü düğmeyle değil bağlam menüsüyle (sağ tık / Apps tuşu) açılıyor. Simgenin asılacağı bir yer yok. |
| `IconCamera` | `ic_fluent_camera_24_filled.svg` | Kare yakalama işlevi var (`BtnSnapshot`, oynatıcıda Ctrl+E) ama ikisi de **metin** etiketli; kaydedici şeridinin bütün düğmeleri metin. Tek düğmeyi simgeye çevirmek şeridin dilini bozar ve bu turun işi değil. **Geri geldi (28 Eylül 2026):** bölge düzenleyicisinin simgeli panelindeki kare düğmesi için, tablodaki 27. satır. |

Geri gerekirse: yukarıdaki dosya adı Fluent deposundaki yolu birebir verir, gövde ham olarak
`Themes/Icons.axaml`'a `F1 M 0,0 M 24,24` sabitleyicisiyle girer ve tabloya bir satır eklenir.
