# Raf

Sahibin sözü, 13 Ekim 2026:

> şimdi tüm piyasayı araştır her bir sekmedeki eksiklerimiz hakkında kapsamlı plan yap saat 8 e kadar durma şu aşamada plan yap sadece işlem 2. aşama olacak bulduklarını later a eklersin

Aşağıdaki her başlık, ikinci aşamada tek seferde teslim edilebilecek bir iş paketidir; pazar araştırmasından çıktı.
Sıra yapım sırasıdır. Bir paketin işi başlayınca başlığı buradan silinir.
Gerekçe, sayılar ve kanıt: [plan-pazar-eksikleri.md](plan-pazar-eksikleri.md).

**Dalga 2 — Karar Gerekmez, Küçük İş Öncelik 2 Ya Da Orta İş Öncelik 1**

## 42. Ekrana Çizim Ve Prompter

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P7 · Kaydedici · iş büyük · öncelik 1 · karar gerekmez

- Satırlar (EKB-P7): EKB-04 Ekrana çizim; EKB-15 Prompter. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Recorder/, src/VidShrink.App/Recorder/CaptureAffinity.cs, src/VidShrink.App/Recorder/RecorderView.Kisayol.cs, src/VidShrink.App/Recorder/RecorderSettings.cs, src/VidShrink.App/Locales/
- Adım 1 (çizim) 16 Ekim 2026'da main'de: `ScreenSketch`, `RecorderSketch`, kısayol Ctrl+F2 (`EkranaCizimTests`).
- Adım 2 (prompter) 16 Ekim 2026'da main'de: kaydedicide "Prompter" (`RecorderPrompter`, `PrompterScroll`); kayıttan çıkarılır, çıkarılamazsa pencerede uyarı; kayıt başlayınca akar, duraklayınca durur; yedi hız basamağı, metin ve hız ayarda (`PrompterTests`).
- Prompter görünümü 16 Ekim 2026'da main'de: ayna, ortada okuma çizgisi, üç basamaklı yazı boyu (belirteçlerden), ayarda (`PrompterLook`, `PrompterTests.AynaCizgiVeYaziBoyuUygulanirAyaraYazilir`).
- Plan: Çizim için ekranı kaplayan saydam bir pencere açılır; kısayolla çizim kipine girilir, kalem, ok, kutu, numara ve yazı çizilir, pencere kayda girer. Prompter (konuşma metnini akıtan pencere) ayrı penceredir ve `CaptureAffinity.Exclude` ile kayıttan çıkarılır; Windows 10 2004 öncesinde pencere kayıttan çıkmadığı için orada uyarı gösterilir (CaptureAffinity.cs:16-20). İki adımda teslim edilebilir: önce çizim, sonra prompter. Çizim araçları başsız pencere testiyle, kayıttan çıkarma bayrağı pencere başına ölçülür.
- Durum (20 Ekim 2026, `worktree-agent-later-42`): mürekkep kısayolu geldi (Ctrl+F3, yeniden atanabilir, `HotkeyAction.Ink`); çoklu ekranda katman alanı `SketchArea.Of` ile seçilir (bölge yoksa imlecin ekranı, ölçeği farklı iki ekrana taşan bölge tek ekrana kırpılır), saf hesapla ölçüldü (`EkranaCizimTests`).
- Kalan: gerçek çoklu ekranda (ölçeği farklı iki monitör) elle deneme ve araç panelinin görsel denetimi; bu makinede tek ekran var, ikisi de ekran başında yapılır.

## 50. Mac Ortam Denetimi Bağlantısı

13 Ekim 2026 · "bulduklarını later a eklersin" · OYB-P9 · Oynatıcı · iş orta · öncelik 2 · karar gerekmez

- Satırlar (OYB-P9): OYB-30 macOS Now Playing. [Tablo](piyasa/2026-10-13/oynatici-kitaplik-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Player/MpvEngine.cs, src/VidShrink.App/Playback/
- Plan: mpv'nin ortam tuşları seçeneği (`input-media-keys`) gömülü kullanımda varsayılan kapalıdır; önce macOS'ta açılır ve klavye ortam tuşlarının, Denetim Merkezi'ndeki çalan ortam alanının oynatıcıyı yönetip yönetmediği ölçülür. Yetmezse macOS'un ortam denetimi arayüzüne ayrı bir bağ yazılır. Sonraki ve önceki tuşu yine uygulamanın kuyruğuna bağlanır. Bu iş Windows makinesinde doğrulanamaz; Mac'te koşan bir işçi ister.

## 69. Kamera Ana Kaynak Olarak

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P8 · Kaydedici · iş orta · öncelik 2 · karar gerekmez

- Satırlar (EKA-P8): EKA-06 Aygıt kaynağı. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.Ffmpeg/CaptureDevices.cs, src/VidShrink.App/Recorder/RecorderView.Hedef.cs, src/VidShrink.App/Recorder/RecorderView.Kamera.cs
- Plan: Hedef türlerine (`RecorderArguments.cs:24`) "aygıt" eklenir: web kamerası ya da yakalama kartı ekran girdisinin yerine ana görüntü olur. Windows'ta kamera girdisi zaten DirectShow ile kuruluyor (`RecorderArguments.cs:1203`); aynı kurucu ana girdi için yeniden kullanılır. Ölçü `tests/VidShrink.Tests/KaydediciHedefTests.cs` ve `KaydediciKameraTests.cs` yanına yazılır; gerçek deneme takılı bir kamerayla Windows'ta elle yapılır.
- Durum (20 Ekim 2026, `worktree-agent-later-69`): aygıt hedefi, çözünürlük/kare hızı seçimi (`-list_options` yoklaması, `-video_size`/`-framerate`) ve kayıt sürerken anlık kare (kayıt ffmpeg'inin saniyede bir yenilenen ikinci çıktısı) bitti; OBS Virtual Camera ile otomatik kayıt ve anlık kare ölçüldü (`KaydediciAygitKipiTests`). Açık: fiziksel bir kamera ya da yakalama kartıyla arayüzden elle deneme; bu makinede takılı fiziksel aygıt yok, yalnız sanal kameralar var.

## 70. Kamera Katmanı Windows Dışında

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P9 = EKB-P13 · Kaydedici · iş orta · öncelik 2 · karar gerekmez

- Satırlar (EKA-P9): EKA-07 Kamera katmanı Windows dışında. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Satırlar (EKB-P13): EKB-18 Kamera katmanı macOS ve Linux'ta. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.Ffmpeg/CaptureDevices.cs, src/VidShrink.App/Recorder/RecorderView.Kamera.cs
- Plan: Kamera girdisi macOS'ta `avfoundation`, Linux'ta `v4l2` ile kurulur (ffmpeg'in o sistemlerdeki aygıt girdileri) ve kamera paneli yalnız Windows koşulundan çıkar (`RecorderView.Kamera.cs:53`). Kardeş tablodaki EKB-18 ve onun paketi EKB-P13 aynı iştir; yalnız biri açılır. Argüman üretimi `tests/VidShrink.Tests/KaydediciKameraTests.cs` yanında her sistem için ölçülür; gerçek aygıt denemesi bu makinede yapılamaz, o sistemlerde elle yapılır.
- Durum (18 Ekim 2026, main): kamera katmanı ve aygıt hedefi macOS (`avfoundation`, 30 fps) ve Linux'ta (`v4l2`, liste `/dev/v4l/by-id`'den) aynı kurucudan kuruluyor (`KaydediciKameraSistemTests`). Açık: o makinelerde elle deneme; katmanın sürükleme düzenleyicisi yalnız Windows'ta.

## 78. Erişilebilirlik Denemesi

13 Ekim 2026 · "bulduklarını later a eklersin" · AYR-P7 · Ayarlar Ve Kabuk · iş orta · öncelik 2 · karar gerekmez

- Satırlar (AYR-P7): AYR-16 Ekran Okuyucu Denemesi; AYR-17 Klavyeyle Tam Kullanım Denemesi. [Tablo](piyasa/2026-10-13/ayarlar-kabuk-dogrulanmis.md)
- Dokunacağı yer: docs/olcumler/, src/VidShrink.App/MainWindow.axaml, src/VidShrink.App/Themes/Controls.axaml
- Plan: Her sekmede bir ana iş baştan sona önce yalnız klavyeyle, sonra ekran okuyucuyla (Windows'ta Anlatıcı ve NVDA) yapılır; sekme sırası, klavye tuzağı ve adı okunmayan denetimler tek tek yazılır. Bulgular `docs/olcumler` altına kayıt olarak girer; adı eksik denetim ve bozuk sekme sırası gibi küçük kusurlar aynı turda düzeltilir, büyükleri deftere yazılır. macOS'ta VoiceOver denemesi o makinede ayrıca yapılır.
- Kalan (20 Ekim 2026): yalnız insan dinlemesi. Anlatıcı ve NVDA ile sesli deneme (NVDA bu makinede kurulu değil, ses dinlenemiyor; özellikle alanlara yazılan yardım metninin ne zaman okunduğu) ve macOS'ta VoiceOver (Mac gerekir). Klavye tarafı bitti: Shift+Tab, alan başına tek Tab durağı, çip birimi, paylaşım kutusu adı (`docs/olcumler/erisilebilirlik-denemesi.md`).

## 81. Windows 11 İlk Menüde Küçültme

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P3 · Dağıtım Ve Otomasyon · iş orta · öncelik 2 · karar gerekmez

- Satırlar (DAG-P3): DAG-37 Windows 11 ilk menüsünde hızlı küçültme. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.ShellExtension/VidShrink.ShellExtension.cpp, src/VidShrink.ShellExtension/AppxManifest.template.xml, src/VidShrink.Core/ShellIntegration.cs, src/VidShrink.Core/Setup/SetupRunner.cs, Install-VidShrink.ps1
- Plan: Kabuk eklentisi (Windows 11'in ilk sağ tık menüsüne girdi koyan C++ bileşeni) bugün tek komut döndürüyor ve alt komut kolu boş. Açma girdisinin yanına "VidShrink ile Küçült" üst girdisi eklenir: `GetFlags` `ECF_HASSUBCOMMANDS` döndürür, `EnumSubCommands` altı hedefi `ShellIntegration.QuickShrinkMenu` sırasıyla sayar ve her biri başlatıcıyı `--kucult` ile çağırır. Etiketler var olan `ShellLabels` kayıt anahtarından okunur, böylece menü dili korunur. Doğrulama: listeyi üç kurucuyla karşılaştıran mevcut teste eklenti dizisi eklenir; Windows 11'de ilk menüde alt menünün görünmesi ve bir hedefin küçültmeyi başlatması elle bakılır.
- Durum (20 Ekim 2026): eklenti "VidShrink ile Küçült" alt menüsünü altı hedefle sayıyor (`yan/win11-ilk-menu`); kurucular (kurulum programı, sessiz kurulum, `Install-VidShrink.ps1`) `ShellLabels`'ı kurulum dilinde yazıyor, kaldırma siliyor (`worktree-agent-later-81`). Kalan: Windows 11'de elle deneme (paket kaydı, iki dosya, dil değişimi) — paket kaydı ve kabuk süreci test kökünde ölçülemiyor, gerçek masaüstünde bir kişinin bakması gerek.

## 83. Akış Kalitesi Ve Kesintisiz Çalma

13 Ekim 2026 · "bulduklarını later a eklersin" · OYB-P10 · Oynatıcı · iş büyük · öncelik 2 · karar gerekmez

- Satırlar (OYB-P10): OYB-21 Akış kalite seçimi; OYB-43 Kesintisiz çalma. [Tablo](piyasa/2026-10-13/oynatici-kitaplik-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Player/MpvEngine.cs, src/VidShrink.Player/IPlaybackEngine.cs, src/VidShrink.App/Playback/PlayerView.Tracks.cs, src/VidShrink.App/Playback/PlayerView.Kuyruk.cs
- Plan: İki adımda teslim edilir ve adımlar ayrı işçilere verilebilir. Önce akış kalitesi (orta): uyarlamalı yayının katmanlarının motorun iz listesinde nasıl göründüğü ölçülür, sonra iz menüsüne kalite seçimi eklenir (video izi değiştirme ya da `hls-bitrate` seçeneği); OYB-P1 bitmeden başlamaz. Sonra kesintisiz çalma (büyük): sıradaki dosyayı önden yükleme (`prefetch-playlist`, `gapless-audio`) kuyruğun motora verilmemesi kararına dokunur, bu yüzden önce o kararın gerekçesi (`PlayerView.Kuyruk.cs:13`) yeniden ölçülür. İki katmanlı yerel bir HLS yayını ve ardışık iki ses dosyasıyla doğrulanır.
- Durum (18 Ekim 2026, main): akış kalite seçimi ve kesintisiz çalma (`yan/akis-kesintisiz`). Açık: gerçek aygıtta dosyalar arası boşluk ölçülemedi.

## 88. İleri Renk Ve Bulanıklık

13 Ekim 2026 · "bulduklarını later a eklersin" · DZB-P13 · Düzenleyici · iş büyük · öncelik 2 · karar gerekmez

- Satırlar (DZB-P13): DZB-36 Eğriler ve renk tekerleri; DZB-38 Otomatik renk ve renk eşleme; DZB-28 Bulanıklık ve bölge gizleme. [Tablo](piyasa/2026-10-13/duzenleyici-kurgu-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/Editing/ClipEffects.cs, src/VidShrink.Core/Editing/ClipFilters.cs, src/VidShrink.Core/Editing/EditLook.cs, src/VidShrink.App/Editing/EditorView.Klip.cs, src/VidShrink.App/Editing/EditorView.Monitor.cs, src/VidShrink.App/Editing/EditorView.axaml, src/VidShrink.App/Locales/, tests/VidShrink.Tests/DuzenleyiciKlipOzellikTests.cs
- Plan: DZB-P3'ün üstüne gelir: eğriler ve üç bölgeli renk dengesi ffmpeg `curves` ve `colorbalance` süzgeçleriyle, tek tıkla düzeltme `normalize` ile yapılır. Bulanıklık `gblur` ile bütün kareye ya da monitörde çizilen dikdörtgene uygulanır (kırp, bulanıklaştır, geri bindir). İki çekimin rengini eşlemek için hazır süzgeç bilinmiyor; o yarı ayrı ölçüm ister. Paket ikiye bölünebilir: renk (DZB-36, DZB-38) ile bulanıklık (DZB-28); doğrulama argüman testleri ve kare karşılaştırması.
- Durum (20 Ekim 2026, `worktree-agent-later-88-89`): DZB-36, DZB-38 ve DZB-28 bitti; renk eşleme referans klibin ortalama ve sapmasını `colorlevels` ile öteki kliplere taşır ([ölçüm](olcumler/renk-esleme.md)), bölge çizilirken monitörde canlı dikdörtgen görünür. Açık yalnız bölge izleme: dikdörtgen zamanla nesneyi izlemiyor, sabit kalır.

**Sahibin Kararını Bekleyenler**

## 95. Çift Tık Ve Basılı Tutma

13 Ekim 2026 · "bulduklarını later a eklersin" · OYA-P6 · Oynatıcı · iş orta · öncelik 1 · karar sahibin

- Satırlar (OYA-P6): OYA-02 Çift tıkla tam ekran; OYA-38 Basılı tutarak geçici hızlandırma. [Tablo](piyasa/2026-10-13/oynatici-cekirdek-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Playback/PlayerView.Fare.cs, src/VidShrink.App/Playback/Keymap.cs, src/VidShrink.App/Playback/PlayerInputMap.cs, src/VidShrink.App/Playback/PlayerShortcutsPanel.axaml.cs
- Sahibin kararı (verildi: üçü de evet, tek tıka gecikme eklenmez): sol tık üç anlam taşısın mı: tek tık oynat ya da duraklat (bugünkü), çift tık tam ekran, basılı tutunca bırakana dek hızlı oynatma
- Durum (20 Ekim 2026, `worktree-agent-later-95`): yüzeyde tek tık bırakışta duraklat/oynat (gecikme yok), çift tık tam ekranı çevirip ilk tıkın duraklatmasını geri alır, basılı tutma `PlayerHoldFastDelay` (0,5 sn) eşiğini aşınca hız 2x, bırakınca eski hıza döner ve tık sayılmaz. Mantık saf `SolTikHareketi` (zaman girdili), sürükleme ayrımı `ClickArbiter`da kaldı; çift tık süresi platformun `GetDoubleTapTime`ından. Kalan: kısayol panelinde çift tık ve basılı tutma satırı yok (yeni metin anahtarı ister); 2x sırasında hız göstergesi çıkmıyor; karşılaştırma paneli eski tek tık davranışında. Tek tık gecikmesi ölçüldü (21 Ekim, `docs/olcumler/tek-tik-gecikmesi.md`): ortanca 0,0103 → 0,0158 ms, p95 0,0357 → 0,0396 ms; çift tıkta ilk tıkın duraklatması ikinci bırakışa dek görüntüyü dondurur, kaldırılıp kaldırılmayacağı sahibin kararı.

## 96. HDR Ton Eşleme

13 Ekim 2026 · "bulduklarını later a eklersin" · OYA-P7 · Oynatıcı · iş orta · öncelik 1 · karar sahibin

- Satırlar (OYA-P7): OYA-11 HDR videoyu SDR ekranda doğru gösterme. [Tablo](piyasa/2026-10-13/oynatici-cekirdek-dogrulanmis.md)
- Dokunacağı yer: tools/VidShrink.Bench/, docs/olcumler/, src/VidShrink.Player/MpvEngine.Advanced.cs, src/VidShrink.App/Playback/PlayerAdvancedPanel.axaml.cs
- Sahibin kararı: HDR video işlemcide ton eşlemeyle gösterilsin mi; 4K'da kare düşürürse yalnız elle açılan bir seçenek mi olsun
- Plan: Önce ölçüm: bir HDR10 ve bir HLG örnek bugünkü yolda açılıp kare kaydedilir, soluk ya da patlak görünüp görünmediği `docs/olcumler/`e yazılır. Sonra etiketli bir `vf` halkası (ffmpeg `zscale` ve `tonemap` süzgeçleri) eklenir; halka yalnız aktarım eğrisi PQ ya da HLG iken takılır (MediaDetails.cs:26–38). Süzgeçler libmpv derlemesinde yoksa paket burada durur ve sahibe döner. Doğrulama: aynı karede eşlemeli ve eşlemesiz ortalama parlaklık farkı; 1080p ve 4K'da `Stats` ile düşen kare sayısı.
- Durum (18 Ekim 2026, `yan/oynatici-hdr`): PQ/HLG videoda `@vstone:gpu` halkası (hedef BT.709/BT.1886) varsayılan açık, Gelişmiş panelde anahtar; ölçüm `docs/olcumler/oynatici-hdr-ton-esleme.md`. Açık: HDR ekrana geçişte halkanın kalkması, `target-peak` değeri (sahibin kararı), GPU'suz makinede düşen kare ölçümü.

## 99. Windows Ortam Denetimi Bağlantısı

13 Ekim 2026 · "bulduklarını later a eklersin" · OYB-P6 · Oynatıcı · iş orta · öncelik 1 · karar sahibin

- Satırlar (OYB-P6): OYB-29 SMTC ve odak dışı ortam tuşları. [Tablo](piyasa/2026-10-13/oynatici-kitaplik-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Player/MpvEngine.cs, src/VidShrink.App/Playback/Keymap.cs, src/VidShrink.App/Playback/PlayerView.axaml.cs
- Sahibin kararı: libmpv'nin kendi ortam denetimi anahtarı yetmezse Windows SDK bağı eklensin mi
- Yapıldı (20 Ekim 2026): oynatıcı motoru `media-controls=yes` ile kuruluyor (Ayarlar menüsünde "Windows Ortam Denetimlerinde Göster", varsayılan açık, yalnız Windows; düzenleyici önizlemesi kapalı). Ölçüldü: Windows ses kutucuğunda oturum açılıyor, başlık dosyanın başlığı, oynat/duraklat düğmesi motoru duraklatıyor. Seçenek açıkken hemen kapatılan motor libmpv'de kilitleniyordu; kapanış kurulumdan sonra en az 100 ms bekliyor. [Ölçüm](olcumler/oynatici-ortam-denetimi-ve-muzik-onizleme.md)
- Yapıldı (21 Ekim 2026): SMTC'nin sonraki/önceki düğmesi uygulamanın kuyruğunu izliyor, SDK eklenmedi. mpv düğmeyi tuş olayı olarak atmıyor (`keybind NEXT` denendi, olay gelmedi); kuyruğun iki komşusu mpv listesine yansıyor, `playlist-pos` değişimi tek istek. Ses dosyasında sanatçı etiketi geçiyor. Kapanışta `stop` oturumu kapatıyor (kapatılmayan oturuma basmak süreci düşürüyordu).
- Kalan: videoda sanatçı alanı SDK'sız yazılamıyor (mpv videoya yalnız başlık yazıyor). Bölümlü dosyada düğme önce bölüm atlıyor (mpv'nin davranışı). Fiziksel ortam tuşu pencere arkadayken elle denenmedi. Kapanışta oturum yaklaşık 60 kapanıştan birinde kalıyor (mpv'nin küçük resim üretimi pencere iş parçacığını tutuyor).

## 100. Kayıpsız Birleştirme Ölçümü

13 Ekim 2026 · "bulduklarını later a eklersin" · DZA-P6 · Düzenleyici · iş küçük · öncelik 1

- Satırlar (DZA-P6): DZA-01 Kayıpsız birleştirme. [Tablo](piyasa/2026-10-13/duzenleyici-kesim-dogrulanmis.md)
- Yapıldı (18 Ekim 2026): `Core/Editing/MergeCompatibility` kodek, profil, başlık verisi özeti (SPS/PPS), çözünürlük, piksel oranı ve biçimi, kare hızı, zaman tabanı ve ses tutan kaynakları Hızlı ve Akıllı kipte concat ile kopyalıyor; tutmayan bugünkü yeniden kodlamaya gidiyor.
- Kalan: danışma 017'nin şartı. Gerçek örnek kümede (telefon, ekran kaydı, iki ayrı kodlayıcı) birleşim noktasında zaman damgası sıçraması, ses-görüntü kayması ve libmpv/VLC/Chrome oynatması `tools/VidShrink.Bench` ile ölçülür, `docs/olcumler/` altına yazılır. Hata çıkarsa kopya yolu kapanır, K7 döner.

## 103. Müzik İzi

13 Ekim 2026 · "bulduklarını later a eklersin" · DZB-P7 · Düzenleyici · iş orta · öncelik 1 · karar sahibin

- Satırlar (DZB-P7): DZB-01 Müzik izi. [Tablo](piyasa/2026-10-13/duzenleyici-kurgu-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/Editing/EditTimeline.cs, src/VidShrink.Core/Editing/EditExport.cs, src/VidShrink.Core/Editing/EdlPreview.cs, src/VidShrink.Core/Editing/EditProject.cs, src/VidShrink.App/Editing/EditorTimeline.cs, src/VidShrink.App/Editing/EditorView.Kaynak.cs, src/VidShrink.Player/MpvEngine.cs, tests/VidShrink.Tests/
- Sahibin kararı: Düzenleyici tek şeritten çıkıp videonun altına ikinci bir ses izi (müzik, anlatım) alsın mı?
- Plan: Çizelgeye A2 izi eklenir; görüntüsü olmayan dosya artık reddedilmez (EditorView.Kaynak.cs:89-93), bu ize başlangıç anı ve düzeyiyle konur. Teslimde iki ses ffmpeg `amix` süzgeciyle karıştırılır. Önizlemede iki sesi birlikte duymanın yolu ölçülmedi: mpv tek ses izi çalar, karıştırma `lavfi-complex` ister ve bunun `edl://` ile çalışması ilk iş denenir. Doğrulama: teslim dosyasında iki sesin de duyulduğunu gösteren düzey ölçümü ve proje dosyasını yazıp geri okuma testi.
- Durum (18 Ekim 2026, `yan/muzik-izi`): A2 izi, görüntüsüz dosyanın A2'ye inmesi (oynatma başı, -12 dB), sürükleme ve sağ tık menüsü (düzey, başa taşı, değiştir, kaldır) yapıldı; model, `amix` teslimi ve proje kaydı önceden vardı.
- Yapıldı (20 Ekim 2026): önizleme müziği çalıyor. `lavfi-complex` + `edl://` denendi ve bırakıldı (müzik EDL zamanına bağlı: hızlı klipte hızlanıyor, başlangıç anı aramada sıfırlanıyor); yerine ayrı, yalnız sesli bir motor (`App/Playback/MusicPreview`) çizelge konumuna kilitli çalıyor, düzey `af` ile, hız oynatma hızıyla. [Ölçüm](olcumler/oynatici-ortam-denetimi-ve-muzik-onizleme.md)
- Kalan: kısma önizlemede yok (yalnız teslimde); geri ve ters oynatmada müzik susuyor; iki motor arasında 0,25 sn'ye kadar kayma kabul ediliyor.

## 105. Uygulama Başına Ses

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P2 · Kaydedici · iş orta · öncelik 1 · karar sahibin

- Satırlar (EKA-P2): EKA-01 Uygulama başına ses. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Ffmpeg/WasapiLoopbackCapture.cs, src/VidShrink.Ffmpeg/SystemAudioCapture.cs, src/VidShrink.Ffmpeg/LoopbackFeed.cs, src/VidShrink.App/Recorder/RecorderView.Ses.cs
- Durum (20 Ekim 2026, `worktree-agent-later-105-106`): yapıldı. Ses kutusunda "Uygulama: <ad>" öğeleri (`AppAudioSessions`, ses oturumu açık programlar); kaynak `ProcessLoopbackCapture` (`ActivateAudioInterfaceAsync` + `VAD\Process_Loopback`), aynı boru yolu. Windows 10 2004 (yapı 19041) altında ve Windows dışında öğe kapalı görünür, ipucu `recorder.audio.app.requires`. Sürüm kapısı ve argüman üretimi `UygulamaSesiTests`te. Açık: iki program aynı anda ses çalarken elle deneme; program yeniden açılıp pid değişince yeniden bağlanma gerçekte denenmedi.

## 106. Kayıt Çerçevesi

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P8 · Kaydedici · iş orta · öncelik 1 · karar sahibin

- Satırlar (EKB-P8): EKB-02 Kayıt çerçevesi. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/, src/VidShrink.App/Recorder/RecorderView.axaml, src/VidShrink.App/Recorder/RecorderView.axaml.cs, src/VidShrink.App/Recorder/RecorderSettings.cs, src/VidShrink.App/Locales/
- Durum (20 Ekim 2026, `worktree-agent-later-105-106`): yapıldı. Sonuç panelinde "Çerçevele" düğmesi; düz renk (`SurfaceToneColor`), palet belirteçlerinden dört `gradients` geçişi ve dosyadan görsel; dolgu, köşe, gölge seçimi `recorder-settings.json` `frame` nesnesinde. Grafik `Core/RecordingFrame.cs`, çıktı boyutu ve köşe/merkez pikseli lavfi klibiyle ölçülüyor (`KayitCercevesiTests`). Açık: gerçek bir kaydın elle görsel denetimi.

## 107. Winget, Scoop Ve Chocolatey Paketleri

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P5 · Dağıtım Ve Otomasyon · iş orta · öncelik 1 · karar sahibin

- Satırlar (DAG-P5): DAG-01 winget paketi; DAG-08 Scoop paketi; DAG-12 Chocolatey paketi. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: .github/workflows/release.yml, yeni packaging/winget/, packaging/scoop/, packaging/chocolatey/; depo dışında microsoft/winget-pkgs PR'ı ve kova deposu
- Sahibin kararı: Windows paket dizinlerinde yayın yapılsın mı; winget için PR sahibin hesabından açılır ve Microsoft katkı sözleşmesi onaylanır, Scoop için kova deposu açılır, Chocolatey için hesap gerekir.
- Plan: DAG-P1'den sonra yapılır: winget etkileşimsiz kurulum ister ve kurulu sürümü kaldırma kaydından tanır. winget bildirimi (paketin adını, sürümünü, kurucu adresini ve sha256'sını taşıyan YAML dosyaları) `VidShrink-Setup.exe` için yazılır; Scoop için tek JSON, Chocolatey için paket tanımı ve kurulum betiği hazırlanır. `release.yml` her sürümde üç bildirimin sürüm ve özet alanını tazeler. Doğrulama: bildirim doğrulama komutu ve temiz bir Windows'ta kurma, yükseltme ve kaldırma.

## 108. Windows Kod İmzası

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P6 · Dağıtım Ve Otomasyon · iş orta · öncelik 1 · karar sahibin

- Satırlar (DAG-P6): DAG-02 Windows kod imzası. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: .github/workflows/release.yml, docs/imzalama.md, README.md, src/VidShrink.ShellExtension/AppxManifest.template.xml
- Sahibin kararı: SignPath Foundation'a başvurulsun mu (ücretsiz; başvuruyu ve her imza onayını sahip yapar), yoksa ücretli sertifika mı alınsın.
- Plan: Başvurunun onayı dışarıda beklediği için karar erken verilmeli; öteki paketler bu sırada yürür. Onaydan sonra `release.yml`'e imza adımı girer: kurucu, başlatıcı, uygulama, komut satırı aracı ve kabuk eklentisi, x64 ve ARM64 için; sha256 listeleri imzadan sonra üretilir. README'deki "başvuru sürüyor" cümlesi gerçek duruma çekilir. Doğrulama: yayın dosyalarında imza denetimi geçerli döner; temiz makinede SmartScreen ve Akıllı Uygulama Denetimi davranışı elle bakılır.

## 110. İkinci Video İzi Ve Yeşil Perde

13 Ekim 2026 · "bulduklarını later a eklersin" · DZB-P12 · Düzenleyici · iş büyük · öncelik 1 · karar verildi

- Yapıldı (21 Ekim 2026, main): V2 izi, boy ve yer seçimi, yeşil perde, önizleme yaması (`docs/olcumler/v2-izi.md`).
- Kalan: geçiş bölgesinde katman önizlemesi, katman sesi, OTIO dışa aktarımı.

## 111. Mac Sistem Sesi Yardımcısı

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P12 · Kaydedici · iş büyük · öncelik 1 · karar sahibin

- Satırlar (EKA-P12): EKA-03 macOS'ta sistem sesi. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: tools/ altında yeni yardımcı proje, src/VidShrink.Ffmpeg/SystemAudioCapture.cs, src/VidShrink.Ffmpeg/CaptureDevices.cs, src/VidShrink.Core/LoopbackAudio.cs, macOS kurucusu
- Sahibin kararı: macOS için ScreenCaptureKit kullanan yerel bir yardımcı ikili yazılıp pakete eklensin mi.
- Plan: ScreenCaptureKit (macOS'un ekranı, tek pencereyi ve sistem sesini veren yakalama arabirimi) ffmpeg'de olmadığı için küçük bir yardımcı ikili yazılır; ham sesi verir, uygulama onu Windows'taki düzenle ffmpeg'e aktarır (`SystemAudioCapture.cs:36` bugün Windows dışında kaynak açmıyor). Sanal ses aygıtı yolu (`CaptureDevices.cs:59`) yedek olarak kalır. Bu makinede koşulamaz: derleme CI'ın macOS koşucusunda, ses denemesi gerçek Mac'te elle yapılır.

## 112. Mac Pencere Yakalama

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P13 · Kaydedici · iş büyük · öncelik 1 · karar sahibin

- Satırlar (EKA-P13): EKA-04 macOS'ta pencere yakalama. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: tools/ altındaki yardımcı proje, src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.App/Recorder/RecorderWindows.cs, src/VidShrink.App/Recorder/RecorderWindowsMac.cs
- Sahibin kararı: macOS'ta pencere yakalama EKA-P12'deki yardımcı ikiliye bağlansın mı; o karar hayır çıkarsa bu paket düşer.
- Plan: EKA-P12'nin yardımcısı seçilen pencerenin karelerini de verir ve ffmpeg onları ham görüntü girdisi olarak okur; ekran kırpması (`RecorderWindows.cs:93`, `RecorderArguments.cs:1134`) yerini bu kaynağa bırakır. Argüman üretimi `tests/VidShrink.Tests/KaydediciPencereTests.cs` yanında ölçülür. Pencere taşınınca ve üstü örtülünce kaydın bozulmadığı gerçek Mac'te elle denenir; EKA-P12 bitmeden başlamaz.

## 113. Wayland Yakalama Yolu

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P14 · Kaydedici · iş büyük · öncelik 1 · karar sahibin

- Satırlar (EKA-P14): EKA-02 Wayland'da yakalama. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.App/Recorder/RecorderWindowsX11.cs, src/VidShrink.App/Recorder/RecorderView.Secici.cs, Linux kurucusu
- Sahibin kararı: Wayland için PipeWire portalını konuşan bir yardımcı ikili ya da yeni bağımlılık eklensin mi, yoksa yalnız açık bir uyarıyla yetinilsin mi.
- Plan: Wayland oturumunda (`RecorderWindowsX11.cs:22` bunu zaten anlıyor) ekran, kullanıcının onay verdiği portal üzerinden istenir ve gelen PipeWire akışı bir yardımcıyla ffmpeg'e boru olarak verilir; ffmpeg'in kendi PipeWire girişi yok. Karar "yalnız uyarı" çıkarsa iş küçülür: ekran ve bölge kaydı için de pencere seçicideki gibi açık hata gösterilir. Deneme Wayland ile açılan gerçek bir Linux masaüstünde elle yapılır.

## 114. Otomatik Yakınlaştırma: Düzenleyici Ve Yumuşak Kaydırma

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P9 · Kaydedici · iş orta · öncelik 1 · karar verildi: kayıttan sonra

- Satırlar (EKB-P9): EKB-01 Otomatik yakınlaştırma; EKB-13 Canlı yakınlaştırma. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Yapılan (21 Ekim 2026, `worktree-agent-later-114-115`): "Otomatik yakınlaştırma için imleci kaydet" kutusu (Büyüteç'in yanındaki oktan) açıkken imleç 30 Hz'de, tık ve tuşlar kancadan `<kayıt>.pointer.json`'a yazılır (`Core/PointerTrack.cs`, `Core/PointerSampler.cs`). Saat kaydın `Captured` süresine hizalı, duraklama sayılmaz. Sonuç panelindeki "Otomatik yakınlaştırma" düğmesi bölgeleri önerir (`Core/AutoZoom.cs`), kutularla seçilir, 1,5×–3× düzeyiyle `zoompan` `-zoom` dosyası üretir (`Ffmpeg/AutoZoomer.cs`). Ölçüm `docs/olcumler/imlec-ornekleme.md`.
- Kalan: bölgeyi düzenleyicide zaman çizelgesinde sürükleyerek başlangıç, bitiş ve merkez düzeltme; yan yana bölgeler arasında yakınlaşmayı bırakmadan yumuşak kaydırma; geçmişten ve "at" akışından kayıt silinince yan dosyanın da silinmesi; `zoompan`'ın tamsayı yuvarlamasından gelebilecek titremenin ölçülmesi (gerekirse `crop`+`scale` zinciri).
- Dokunacağı yer: src/VidShrink.App/Editing/, src/VidShrink.Core/AutoZoom.cs, src/VidShrink.App/Recorder/RecorderView.Gecmis*.cs, src/VidShrink.App/Locales/
- Plan: Düzenleyici `-zoom` yerine bölge listesini `pointer.json` yanında taşır, önizleme aynı filtreyi uygular. Kaydırma iki bölge merkezi arasında `st/ld` rampasıyla ara değer üretir. Titreme lavfi kutusunun karedeki yerinin kare kare oynamasıyla ölçülür.

## 115. İmleç Cilası Ve Hareket Bulanıklığı

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P10 · Kaydedici · iş büyük · öncelik 1 · karar verildi: imleç ayrı veri

- Satırlar (EKB-P10): EKB-03 İmleç cilası; EKB-31 Hareket bulanıklığı. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Hazır olan: 114'ün iz dosyası imleci 30 Hz'de kaydın saatine hizalı tutuyor (`PointerTrack.PositionAt`). İmleç henüz kayda yakılıyor.
- Dokunacağı yer: src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.App/Recorder/RecorderView.Yakinlastirma.cs, src/VidShrink.Core/, src/VidShrink.App/Editing/, src/VidShrink.App/Locales/
- Plan: İz açıkken kayıt `-draw_mouse 0` ile alınır, imleç şekli (`GetCursorInfo`/`CopyIcon`) iz dosyasına eklenir. İz yumuşatılır, imleç seçilen boyutta yeniden çizilip `overlay` ile bindirilir; yakınlaştırma uygulanırken imleç aynı dönüşümle taşınır. Hızlı geçişlere kare karıştırma (`tmix`) ile bulanıklık eklenir. İmleç gömülmediği için iz dosyası kaybolursa imleçsiz kayıt kalır; bu yüzden imleç bindirmesi dışa aktarmada varsayılan açık olur. Yumuşatma saf fonksiyon olarak, imlecin karedeki yeri lavfi ile ölçülür.

## 118. Flathub Ve AppImage Paketleri

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P8 · Dağıtım Ve Otomasyon · iş büyük · öncelik 1 · karar sahibin

- Satırlar (DAG-P8): DAG-04 Flathub paketi; DAG-09 AppImage. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: yeni packaging/flatpak/, packaging/appimage/, .github/workflows/release.yml, src/VidShrink.Core/UpdateCheck.cs, install-vidshrink.sh
- Sahibin kararı: Flathub'a uygulama başvurusu yapılsın mı (sahibin hesabından; ffmpeg ve libmpv paketin içinde gelir) ve yayına AppImage eklensin mi.
- Plan: Flatpak bildirimi (uygulamanın nasıl derleneceğini ve hangi izinlerle çalışacağını söyleyen dosya) yazılır; ffmpeg ve libmpv Flathub çalışma ortamından ya da bildirimde kaynaktan derlenerek gelir, uygulama kimliği, tanıtım dosyası ve simge eklenir. Paketin içinde kendini güncelleme kapatılır, güncellemeyi Flathub yapar. AppImage aynı yayın çıktısından `release.yml` içinde üretilir ve sha256 listesine girer. Doğrulama: CI'da paketi kurup küçültme ve oynatma denemesi; AppImage için temiz bir Ubuntu koşucusunda açılış.

## 126. Kayıtta Bildirim Ve Simge Gizleme

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P15 · Kaydedici · iş orta · öncelik 2 · karar sahibin

- Satırlar (EKB-P15): EKB-20 Bildirim susturma; EKB-21 Masaüstü simgelerini gizleme. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Durum (21 Ekim 2026, `worktree-agent-later-126`): EKB-21 bitti — kaydedicide "Masaüstü simgelerini gizle" seçeneği, varsayılan kapalı; kayıt başlarken simgeler gizlenir, bitince, iptalde ve çökmeden sonraki açılışta geri gelir (durum `masaustu-simgeleri.gizlendi` dosyasında), kullanıcı zaten gizlediyse dokunulmaz, Windows dışında kutu kapalı ve ipuçlu (`KaydediciMasaustuSimgeTests`). Durum dosyası "Tüm verileri sıfırla" listesinde (sıfırlama önce simgeleri geri getirir), ayar yedeğine girmez (`AppDataArchive.MachineStateFiles`). Yeni kutu 1024 × 1060 Gelişmiş tr'de orta sütunu 899 px'e çıkarıp sayfayı kaydırıyordu (sınır 886); imleç seçenekleri ızgarası `SpaceXs` satır aralığına, tampon düğme satırı `WrapItemMargin` yerine `ItemSpacing`/`LineSpacing`'e geçti, orta sütun 859 px (40 px kazanç). EKB-20 bildirim susturma — karar: hayır; Windows'ta belgeli yolu yok, yapılmayacak. Açık: gerçek Windows masaüstünde (Windows 10 ve 11) bir kayıtla elle deneme.
- Dokunacağı yer: src/VidShrink.App/Recorder/, src/VidShrink.App/Recorder/RecorderSettings.cs, src/VidShrink.App/Recorder/RecorderView.axaml, src/VidShrink.App/Locales/
- Sahibin kararı: Uygulama kayıt sırasında sistemin bildirim ayarına ve masaüstü simgelerine dokunsun mu.
- Plan: Kayıt başlarken bildirimler susturulur ve masaüstü simgeleri gizlenir, bitince eski durum geri yüklenir; eski durum diske yazılır ki çökmeden sonra açılışta geri alınsın. Windows'ta bildirim susturmanın belgeli yolu doğrulanmadı; bulunamazsa EKB-20 yapılmaz, yalnız simge gizleme teslim edilir. EKB-20 kardeş tablodaki EKA-27 ile aynı iştir. Geri yükleme sahte sistem katmanıyla, çökme senaryosu dahil ölçülür.

## 127. Çeviri Sitesi Bağlantısı

13 Ekim 2026 · "bulduklarını later a eklersin" · AYR-P10 · Ayarlar Ve Kabuk · iş küçük · öncelik 2 · karar sahibin

- Satırlar (AYR-P10): AYR-04 Çeviri Katkı Platformu. AYR-25 (gözden geçmemiş dil işareti) bitti: `Strings.ReviewedLanguages`, `CeviriTamlikIsaretiTests`. [Tablo](piyasa/2026-10-13/ayarlar-kabuk-dogrulanmis.md)
- Dokunacağı yer: README.md, .weblate, .github/workflows/
- Sahibin kararı: bir çeviri sitesinde (Weblate, Crowdin ya da Transifex) proje hesabı açılsın mı; hesabı sahip açar.
- Plan: Seçilen site depodaki `Locales` klasörüne bağlanır, README'deki katkı yolu oraya çevrilir. Doğrulama: siteden gelen ilk deneme çevirisinin dizgi tamlığı testinden geçmesi.

## 131. macOS İzin Akışı

13 Ekim 2026 · "bulduklarını later a eklersin" · PLT-P4 · Platform Eşitliği · iş orta · öncelik 2 · karar sahibin

- Satırlar (PLT-P4): PLT-04 macOS izin akışı. [Tablo](piyasa/2026-10-13/platform-esitligi-dogrulanmis.md)
- Dokunacağı yer: macos-app-bundle.sh, src/VidShrink.App/Recorder/, src/VidShrink.App/Locales/
- Sahibin kararı: pakete izin açıklamaları eklenip kayıttan önce izin yoklaması gösterilsin mi
- Plan: Paketin Info.plist dosyasına (macOS paketinin kimlik ve izin bildirimi) mikrofon açıklaması eklenir; kamera paketi açılırsa kamera açıklaması da buraya gelir. Kayıt başlamadan Ekran Kaydı izni yoklanır, eksikse kayıt başlamaz ve Sistem Ayarları'nın ilgili sayfasına götüren hata satırı çıkar. EKA-P9, EKA-P12 ve EKA-P13'ün izin anahtarları da bu pakette toplanır. Paket içeriği testle, izin akışı yalnız gerçek Mac'te doğrulanır.

## 132. macOS Kapanışta Sessiz Güncelleme

13 Ekim 2026 · "bulduklarını later a eklersin" · PLT-P5 · Platform Eşitliği · iş orta · öncelik 2 · karar sahibin

- Satırlar (PLT-P5): PLT-21 macOS'ta otomatik sessiz güncelleme. [Tablo](piyasa/2026-10-13/platform-esitligi-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/MainWindow.KapanistaGuncelleme.cs, src/VidShrink.Core/UpdateCheck.cs, src/VidShrink.Core/UpdateHealth.cs
- Sahibin kararı: macOS'ta otomatik güncelleme varsayılan olarak açılsın mı
- Plan: Elle Yükle'nin kullandığı paket takası, `MacUpdate.CanSwap` doğruysa kapanış yoluna da bağlanır: sahne açılış bittikten sonra sessiz iner, çıkışta takas edilir. Her düşüş `UpdateHealth` sayacına yazılır; sayılmayan sessiz yol eklenmez. Düz kurulumda ve salt okunur yola taşınmış pakette kapalı kalır; Sparkle eklenmez. Birim testleri ve gerçek Mac'te bir sürüm atlamasıyla doğrulanır.
- Yapıldı (21 Ekim 2026, main): macOS'ta otomatik güncelleme varsayılan açık (`UpdateSettings.DefaultOn`), eski kapalı ayar bir kez açılır; `MacUpdate` açılıştan sonra sessiz iner, çıkışta takas eder, düşüş `UpdateHealth`'e sayılır. Kalan: gerçek Mac'te bir sürüm atlamasıyla elle deneme.

## 134. VideoToolbox Hızlı Kip Kapısı

13 Ekim 2026 · "bulduklarını later a eklersin" · PLT-P7 · Platform Eşitliği · iş orta · öncelik 2 · karar sahibin

- Satırlar (PLT-P7): PLT-37 macOS'ta VideoToolbox'ın otomatik sıraya girmesi. [Tablo](piyasa/2026-10-13/platform-esitligi-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/CodecModel.cs, src/VidShrink.Core/PlanCalculator.cs, tools/VidShrink.Bench, docs/olcumler/videotoolbox-hizli.md
- Sahibin kararı: ölçüm için Mac ayrılsın mı ve kapıdan geçerse Hızlı kipin Mac'teki varsayılan kodlayıcısı değişsin mi
- Plan: Önceki ölçüm beş ölçütün ikisinden kaldı (docs/olcumler/videotoolbox-hizli.md:60, 62); kapı değişmeden VideoToolbox'a kalite ölçeği eklenir ve ölçüm gerçek Mac'te yinelenir. Geçerse `hevc_videotoolbox` yalnız macOS'ta ve yalnız Hızlı kipte otomatik sıraya girer; geçmezse satır kapanır. Rapora giren her sayı Bench'ten (ölçüm aracımız) çıkar.

## 135. Linux Ölçüm Turu: VAAPI Ve Sistem Sesi

13 Ekim 2026 · "bulduklarını later a eklersin" · PLT-P8 · Platform Eşitliği · iş orta · öncelik 2 · karar sahibin

- Satırlar (PLT-P8): PLT-38 Linux'ta VAAPI ölçümü; PLT-43 Linux'ta sistem sesi ölçümü. [Tablo](piyasa/2026-10-13/platform-esitligi-dogrulanmis.md)
- Dokunacağı yer: tools/VidShrink.Bench, docs/olcumler/, src/VidShrink.Core/CodecModel.cs, src/VidShrink.Ffmpeg/CaptureDevices.cs, README.md
- Sahibin kararı: ölçüm için Intel ya da AMD ekran kartlı gerçek bir Linux makinesi ayrılsın mı
- Plan: Aynı Linux makinesinde iki ölçüm yapılır: VAAPI kolu açık seçimle koşturulup hız ve kalite Bench'le ölçülür; pulse `.monitor` kaynağından kısa kayıt alınıp ses seviyesi ölçülür. Sonuç, VAAPI'nin otomatik sıraya alınıp alınmayacağını ve README'deki "sistem sesi yalnız Windows'ta" cümlesini belirler. Defterdeki VAAPI satırı bu turla kapanır.

## 136. Wayland'da Genel Kısayol

13 Ekim 2026 · "bulduklarını later a eklersin" · PLT-P9 · Platform Eşitliği · iş orta · öncelik 2 · karar sahibin

- Satırlar (PLT-P9): PLT-08 Linux'ta genel kısayol. [Tablo](piyasa/2026-10-13/platform-esitligi-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Recorder/RecorderView.Kisayol.cs, src/VidShrink.App/Recorder/
- Sahibin kararı: GlobalShortcuts portalı için D-Bus istemcisi bağımlılığı eklensin mi
- Plan: X11 kolu EKA-P10 içinde; bu paket yalnız Wayland kolunu kapsar. Kısayol GlobalShortcuts portalı üzerinden kaydedilir; portalla D-Bus (Linux masaüstünde programların birbirine mesaj yolladığı hat) üzerinden konuşulur. EKA-P14 gelmeden açılmaz, çünkü Wayland'da kaydedicinin kendisi çalışmıyor. Portalı sunan gerçek bir Wayland masaüstünde doğrulanır.

## 137. Dış Denetim Ve Eklenti Yüzeyleri

13 Ekim 2026 · "bulduklarını later a eklersin" · OYB-P11 · Oynatıcı · iş büyük · öncelik 2 · karar sahibin

- Satırlar (OYB-P11): OYB-31 Linux MPRIS; OYB-35 Yeni pencere ve çoklu örnek seçeneği; OYB-39 Uzaktan denetim; OYB-40 Çalışan oynatıcıya komut gönderme; OYB-41 Betik ve eklenti sistemi. [Tablo](piyasa/2026-10-13/oynatici-kitaplik-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Program.cs, src/VidShrink.Core/SingleInstanceChannel.cs, src/VidShrink.App/Integration/, src/VidShrink.Player/MpvEngine.cs
- Sahibin kararı: oynatıcı dışarıdan yönetilebilen bir yüzey açsın mı (komut kanalı, Linux ortam denetimi, ağdan uzaktan denetim, eklenti) ve tek örnek kuralına "yeni pencere" seçeneği gelsin mi
- Plan: Bu bir karar paketidir; karar gelince satır başına ayrı işlere bölünür. En ucuz ilk adım çalışan oynatıcıya komut göndermektir (OYB-40): tek örnek kanalı bugün yalnız yol taşıyor, aynı kanala duraklat, sar ve sıraya ekle komutları eklenir; yeni pencere seçeneği (OYB-35) aynı dosyalara dokunur. Linux ortam denetimi (OYB-31) ve ağdan uzaktan denetim (OYB-39) bu komut katmanının üstüne kurulur. Eklenti sistemi (OYB-41) bilerek kapatılmış `load-scripts=no` seçeneğini geri açtığı için en sona kalır.
- Durum (21 Ekim 2026, `worktree-agent-later-137`): OYB-40 denetim kanalıyla (later 43) zaten vardı; OYB-31 Linux MPRIS ve OYB-35 yeni pencere geldi. MPRIS'i libmpv vermiyor (`media-controls` yalnız Windows SMTC, dosyada `org.mpris` yok; Linux'ta canlı ölçü CI'nın `mpris-linux` işinde), oynatıcı kendi `org.mpris.MediaPlayer2.vidshrinkplayer` nesnesini Avalonia'nın zaten getirdiği Tmds.DBus.Protocol ile açıyor, komutlar denetim kanalının yolundan geçiyor. Yeni pencere sağ tıkta ve `--yeni-pencere` ile; ikinci süreç ayarların kopyasına yazar, kuyruk ve güncelleme sahibinde kalır. Açık: gerçek bir Linux masaüstünde (GNOME, KDE) medya tuşlarıyla elle deneme; ikinci pencerenin ayar değişikliği kapanınca kaybolur, paylaşım geçmişi kopyaya değil ortak dosyaya yazılır.
- OYB-39 ağdan uzaktan denetim — karar: hayır. Ağa açılan her kapı kimlik doğrulama, eşleştirme ve şifreleme ister; oynatılan dosyanın adı ve yolu ağa çıkar. Bu yük ve mahremiyet bedeli tek kullanıcılı bir masaüstü oynatıcısında karşılığını vermiyor; denetim kanalı bilerek yalnız aynı kullanıcının yerel borusunda.
- OYB-41 betik ve eklenti sistemi — karar: hayır. Eklenti bilerek kapatılmış `load-scripts=no` kararını geri açar: mpv betiği oynatıcının içinde denetimsiz kod koşturur, yayımlanan pakette neyin çalıştığı artık bizim elimizde olmaz.

## 138. Kitaplık Ve Ağ Kaynakları

13 Ekim 2026 · "bulduklarını later a eklersin" · OYB-P12 · Oynatıcı · karar verildi, 21 Ekim 2026

- Durum: OYB-09 adlı listeler yapıldı (`Core/NamedPlaylists`, `App/Playback/PlaylistsWindow`; ayar klasöründe `oynatma-listeleri/`, m3u/m3u8 içe ve dışa aktarım, eksik dosya işaretli kalır). Kalan üç satır sahibin kararıyla kapandı.
- OYB-17 Yerel kitaplık görünümü — karar: hayır. Klasör tarayan kitaplık yeni bir sekme ister; oynatıcı tek pencere ve klasör gezintisiyle kalır.
- OYB-22 Site bağlantısı çözme — karar: hayır. Kapalı K1 kararının aracını getirir ve bilerek yazılmış `ytdl=no` seçeneğini geri açar.
- OYB-24 Ağ paylaşımında gezme — karar: hayır. Paylaşıma giriş parola saklamak demektir; bu yön istenmedi.
- [Tablo](piyasa/2026-10-13/oynatici-kitaplik-dogrulanmis.md)

## 139. Çok İz Üstüne Gelenler

13 Ekim 2026 · "bulduklarını later a eklersin" · DZB-P15 · Düzenleyici · iş büyük · öncelik 2 · karar sahibin

Karar (21 Ekim 2026, sahip): evet; 109 ölçümünden sonra başlar. 139'da ses ekleri ve iz denetimleri evet, boşluklu çizelge hayır.

- Yapıldı (21 Ekim 2026, main): sesi A3 izine ayırma (DZB-10), otomatik müzik kısma (DZB-46), seslendirme kaydı (DZB-47), V1/T1/A2/A3 iz denetimleri (DZB-06); testler `DuzenleyiciSesEkleriTests`. DZB-07 kapandı.
- Kalan: A3 önizlemede çalmıyor (yalnız teslimde duyulur), V1 gizleme yok, kilit simgesi yok, seslendirme akışı elle denenmedi.

## 140. Vekil, Hazır İçerik Ve Model

13 Ekim 2026 · "bulduklarını later a eklersin" · DZB-P16 · Düzenleyici · iş büyük · öncelik 2 · karar sahibin

- Satırlar (DZB-P16): DZB-51 Vekil dosyalarla kurgu; DZB-22 Hazır başlık şablonları ve alt bant; DZB-52 Proje şablonları; DZB-53 Hazır varlık kitaplığı; DZB-44 Modelli konuşma temizleme; DZB-69 Otomatik yeniden kadraj; DZB-70 Arka plan silme ve nesne maskesi; DZB-71 Hareket izleme; DZB-72 Metinden sese. [Tablo](piyasa/2026-10-13/duzenleyici-kurgu-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/Editing/, src/VidShrink.Core/IntermediateCodecs.cs, src/VidShrink.App/Editing/
- Sahibin kararı: Uygulama kullanıcının diskine büyük ek dosya (vekil kopya, hazır içerik paketi, yapay zekâ modeli) üretsin ya da indirsin mi; hangisi istekle, hangisi hiç?
- Durum (21 Ekim 2026, `worktree-agent-later-140`): DZB-51 vekil kopya bitti. Yalnız kullanıcı "Vekil oluştur" deyince üretilir, düzenleyici proje klasörünün `vekiller/` alt klasörüne yazılır (`ProxyMedia`, `ProxyRunner`, `EditorView.Vekil.cs`); önizleme vekilden oynar, dışa aktarım her zaman asıl dosyadan (`DuzenleyiciVekilTests`). 4K HEVC 10 bit klipte kesin sarma ortancası 245 ms'den 1,6 ms'ye indi ([vekil-sarma.md](olcumler/vekil-sarma.md)).
- Hazır içerik (DZB-22, DZB-52, DZB-53) — karar: hayır. Gerekçe: hazır başlık, şablon ve varlıkların lisansı her paket için ayrı denetim ister ve paket uygulamanın indirme boyutunu büyütür.
- Modelli işler (DZB-44, DZB-69, DZB-70, DZB-71, DZB-72) — karar: hayır. Gerekçe: her biri ayrıca indirilen bir model ve onu koşturacak çalışma zamanı ister; indirme boyutu ve makine yükü uygulamanın sınırını aşar.
- Plan: Bu paket bir karar demetidir, tek seferde teslim edilmez: sahip hangi satırı açarsa o satır kendi paketi olur. Vekil dosya (ağır kaynağın kurgu sırasında kullanılan küçük kopyası; DZB-51) indirme istemez, yalnız disk yeri ister ve en bağımsız parçadır. Hazır içerik (DZB-22, DZB-52, DZB-53) lisans ve paket boyutu, modelli işler (DZB-44, DZB-69, DZB-70, DZB-71, DZB-72) indirme ve çalışma zamanı sorusudur. Doğrulama her satır için ayrı kurulur.

## 141. Ölçümle Hedef Kalite Araması — Kalanlar

13 Ekim 2026 · SIK-P11 · Küçült · iş orta · öncelik 3

- Yapıldı (20 Ekim 2026): Gelişmiş'te "Ölçerek ara" (varsayılan kapalı, en çok 4 tur, tur ilerlemede); çekirdek `src/VidShrink.Core/BracketSearch.cs` ve `MeasuredQualitySearch.cs`, örnekleyici `src/VidShrink.Ffmpeg/QualitySearchSampler.cs`, ölçüm `docs/olcumler/olcerek-arama.md`.
- Kalan 1: CLI'a bağlanmadı (`src/VidShrink.Cli/CliApp.cs`); ShrinkJobWindow kuyruğu da aramayı koşturmuyor.
- Kalan 2: Sabit dosya kümesinde tahmin yolu ile arama yolu yan yana ölçülmedi; tek sentetik klip, iki puan var. Küçük hedefte puan MB ile tekdüze değil (70 hedefinde 4 turda yakınsamadı), kıskaç tekdüzelik varsayıyor.
- Kalan 3: Örnek kodlama tek geçişli; iki geçişli gerçek kodlamayla farkı ölçülmedi.

## 142. Donanım Kodlayıcıda Dolby Vision

13 Ekim 2026 · "bulduklarını later a eklersin" · SIK-P13 · Küçült · iş büyük · öncelik 2 · karar sahibin

- Satırlar (SIK-P13): SIK-19 Donanım kodlayıcıda Dolby Vision. [Tablo](piyasa/2026-10-13/kucult-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/HdrResolver.cs, src/VidShrink.Core/FfmpegArguments.cs, tools/VidShrink.Bench/, docs/olcumler/
- Sahibin kararı: Dolby Vision kapsamı donanım kodlayıcılarına genişlesin mi, gerekirse ffmpeg dışında ayrı bir araçla?
- Plan: Önce ölçülür: ekran kartı kodlayıcılarıyla yapılan kodlamada ffmpeg Dolby Vision üst verisini çıktıya yazıyor mu; sonuç `docs/olcumler/` altına girer. Yazıyorsa `src/VidShrink.Core/HdrResolver.cs:46` satırındaki kodlayıcı koşulu genişletilir; yazmıyorsa iş ayrı araç zinciri kararına döner ve paket orada durur. Doğrulama: çıktının Dolby Vision kaydını ffprobe ile okuyan test.

## 144. Oyun Yakalama

13 Ekim 2026 · "bulduklarını later a eklersin" · EKA-P16 · Kaydedici · iş büyük · öncelik 2 · karar sahibin

- Satırlar (EKA-P16): EKA-05 Oyun yakalama. [Tablo](piyasa/2026-10-13/kaydedici-yakalama-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.Core/RecorderArguments.cs, src/VidShrink.Core/RecorderWizard.cs, src/VidShrink.App/Recorder/RecorderView.Sihirbaz.cs, docs/olcumler/
- Sahibin kararı: Oyun sürecine kanca atan ayrı bir yakalama yolu yapılsın mı, yoksa ekran kaydı yolu ölçülüp yeterli mi sayılsın.
- Plan: Sihirbazın "Oyun" cevabı oyunun ekranını kendiliğinden seçiyor (`RecorderWizard.GameScreen`, 18 Ekim 2026). Kalan: tam ekran bir oyunun bugünkü hızlı yolla kaydı `tools/VidShrink.Bench` ile ölçülür, `docs/olcumler/` altına yazılır; kayıt bozuksa kanca yolu (oyunun içine girip çizdiği kareyi alan yöntem) sahibin kararına çıkar, hile koruması olan oyunlarda risk taşır.

## 146. Girdi Efektleri Mac Ve Linux

13 Ekim 2026 · "bulduklarını later a eklersin" · EKB-P17 · Kaydedici · iş büyük · öncelik 2 · karar sahibin

- Satırlar (EKB-P17): EKB-14 Girdi efektleri macOS ve Linux'ta. [Tablo](piyasa/2026-10-13/kaydedici-sunum-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Recorder/RecorderInput.cs, src/VidShrink.App/Recorder/RecorderView.Girdi.cs, src/VidShrink.App/Recorder/RecorderMagnifier.cs, src/VidShrink.App/Locales/
- Sahibin kararı: macOS'ta girdi izleme izni istensin mi, ve Wayland'da genel tuş dinleme yolu yoksa Linux desteği X11 ile mi sınırlansın.
- Plan: Boş dinleyicinin yerine macOS ve X11 için gerçek fare ve klavye dinleyicileri yazılır (RecorderView.Girdi.cs:14-22); Wayland ve X11, Linux'un iki ayrı pencere sistemidir. Büyüteç için ekran kopyası o sistemlerin kendi çağrısıyla alınır. İzin verilmediğinde efekt kapalı kalır ve kullanıcıya söylenir. Dinleyiciler arayüzün arkasında sahte olay akışıyla ölçülür; gerçek deneme o sistemlerde elle yapılır.

## 147. Arayüz Ölçeği Ve Yüksek Karşıtlık

13 Ekim 2026 · "bulduklarını later a eklersin" · AYR-P11 · Ayarlar Ve Kabuk · iş büyük · öncelik 2 · karar sahibin

- Satırlar (AYR-P11): AYR-12 Arayüz Ölçeği Ya Da Yazı Boyutu; AYR-13 Yüksek Karşıtlık Desteği. [Tablo](piyasa/2026-10-13/ayarlar-kabuk-dogrulanmis.md)
- Dokunacağı yer: src/VidShrink.App/Themes/Theme.axaml, src/VidShrink.App/Themes/PaletteCatalog.cs, src/VidShrink.App/Themes/Palette/, src/VidShrink.App/MainWindow.axaml
- Yapılan: `HighContrast` paleti (AAA testi `YuksekKarsitlikVeYaziOlcegiTests`), Ayarlar'da yazı boyutu %100/%125/%150 (`FontScale`), %150 varsayılan boyutta beş dilde kusursuz (`YuzdeElliYazidaVarsayilanBoyuttaKesikCakismaTasmaYok`). Yazı boyutu varsayılan hedef boyutun yanına oturdu, hedef boyutun ipucu bilgi rozetine taşındı; Ayarlar 1920x1040'ta kaydırmasız kalır (`KaydirmasizSekmeTests`). Kural istisnası açılmadı.
- Kalan: %150 küçük kipte ve en dar pencerede, %125 hiçbir boyutta ölçülmedi; yazı boyutu yeniden başlatınca uygulanıyor, çalışırken değil.
- Plan: `YerlesimDenetimiTests`'in küçük kip ve en dar kollarına `yazi: 150` kolu eklenir, çıkan kusurlar belirteç ve yerleşimle düzeltilir. Çalışırken uygulama için `FontScale.Apply` sonrası açık pencerelerin yeniden ölçülmesi denenir.

## 149. Tam Paket Ve Çevrimdışı Kurucu

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P9 · Dağıtım Ve Otomasyon · iş büyük · öncelik 2 · karar sahibin

- Satırlar (DAG-P9): DAG-10 Taşınabilir sürüm; DAG-11 Çevrimdışı kurucu; DAG-20 macOS ve Linux'ta ffmpeg kurulumu. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: .github/workflows/release.yml, src/VidShrink.Core/UpdateCheck.cs, src/VidShrink.Setup/Program.cs, src/VidShrink.Core/Setup/SetupDownloads.cs, install-vidshrink.sh, docs/kurulum.md
- Sahibin kararı: ffmpeg ve libmpv ikilileri kendi yayın dosyalarımızın içinde dağıtılsın mı (yüzlerce MB ek boyut ve lisans yükümlülüğü).
- Durum (21 Ekim 2026, `worktree-agent-later-149`): DAG-11 bitti. Sahibin kararı: ana paketler küçük kalır, indirici yol durur, yayına ayrı çevrimdışı arşiv eklenir. `release.yml`'nin `offline` işi Windows x64 ve arm64 için `VidShrinkPlayer-Offline-<rid>.zip` üretir (kurucu, uygulama, ffmpeg, libmpv, `vulkan-1.dll`, `licenses/`); kurucu yanındaki `vidshrink-offline.txt`'yi görünce araçları arşivden sha256 denetimiyle kurar, tutmayanı indirir. macOS ve Linux'ta kurucu yok, arşiv okuyacak yer olmadığı için dışarıda kaldı. DAG-10 taşınabilir kip zaten var. `Install-VidShrink.ps1` çevrimdışı arşivi tanıyor (betiğin klasöründe ya da `-OfflineSource` ile) ve `docs/kurulum.md` ile `kurulum.tr.md` arşivi anıyor. Açık: DAG-20 (macOS ve Linux betiğinin sabitlenmiş ffmpeg indirmesi), ilk yayında arşivin gerçek boyutu (x64 tahmini ~310 MB, arm64 ölçülmedi).
- Plan: `release.yml` her hedef için uygulama, ffmpeg ve libmpv'yi bir arada taşıyan ikinci bir arşiv üretir; taşınabilir kip (komşu tabloda AYR-05), uygulama klasöründe bir işaret dosyası görünce ayarı yanına yazar ve kendini güncellemeyi kapatır. Çevrimdışı kurucu aynı arşivi kurucunun var olan `--asset-source` yoluyla kullanır. macOS ve Linux betiği ffmpeg yoksa durmak yerine sha256'sı sabitlenmiş derlemeyi indirir. Doğrulama: ağ kapalıyken kurulum ve ilk küçültme, taşınabilir kopyada kullanıcı klasörüne dosya yazılmadığının testi.

## 150. macOS İmza, Dmg Ve Homebrew

13 Ekim 2026 · "bulduklarını later a eklersin" · DAG-P10 · Dağıtım Ve Otomasyon · iş büyük · öncelik 2 · karar sahibin

- Satırlar (DAG-P10): DAG-05 macOS geliştirici imzası ve noterleme; DAG-06 macOS disk görüntüsü (dmg); DAG-07 Homebrew cask. [Tablo](piyasa/2026-10-13/dagitim-otomasyon-dogrulanmis.md)
- Dokunacağı yer: macos-app-bundle.sh, .github/workflows/release.yml, install-vidshrink.sh, src/VidShrink.Core/UpdateCheck.cs; depo dışında tap deposu
- Sahibin kararı: ücretli Apple geliştirici üyeliği alınsın mı; alınırsa dmg yayımlanır ve Homebrew için kendi tap depomuz açılır.
- Plan: `macos-app-bundle.sh` ad-hoc imza yerine Developer ID ile imzalar ve paketi noterlemeye gönderir (noterleme: Apple'ın paketi tarayıp onay damgası vermesi); libmpv ve öteki kitaplıklar da imzalanır. İmzalı paketten sürükle bırak dmg üretilir ve yayına eklenir. Homebrew ana dizini en az 75 yıldız, sahibin kendi başvurusunda 225 yıldız arıyor ve bizde 12 var; bu yüzden cask (Homebrew'un hazır uygulama tarifi) kendi tap depomuzda (projeye ait Homebrew paket deposu) yayımlanır ve `release.yml` her sürümde sürüm ile sha256'yı tazeler. Doğrulama: CI'da Gatekeeper ve damga denetimi, temiz Mac'te tarayıcıdan indirip çift tıkla açma elle.

