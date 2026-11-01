using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App.Editing;
using VidShrink.App.Localization;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Core.Editing;
using Xunit;

namespace VidShrink.Tests;

/// <summary>
/// Kısayol atama: tuş kutusuna tıklanınca dinleme, ilk tuş ya da orta/geri/ileri fare düğmesi
/// yeni atama olur, Esc ve sol tık vazgeçer, çakışmada yeni atama kazanır ve öbür satır
/// atanmamış kalır, atama dosyaya yazılıp geri okunur, varsayılana dönülür. Dinlerken tuş
/// oynatıcıya ve düzenleyiciye ulaşmaz. Her ölçü kendi dosyasıyla koşar
/// (<see cref="ShortcutBindings.UseFile"/>), kullanıcı ayarına dokunmaz.
/// </summary>
public sealed class KisayolAtamaTests
{
    private static readonly Pointer Fare = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    private static string Kok => Path.Combine(TipSources.Root, ".calisma", "kisayol-atama");

    private static T Dosyayla<T>(Func<string, T> olcu)
    {
        Directory.CreateDirectory(Kok);
        var dosya = Path.Combine(Kok, Guid.NewGuid().ToString("N") + ".json");
        ShortcutBindings.UseFile(dosya);
        try
        {
            return olcu(dosya);
        }
        finally
        {
            ShortcutBindings.UseFile(null);
            if (File.Exists(dosya)) File.Delete(dosya);
        }
    }

    private static (Window Pencere, PlayerShortcutsPanel Panel) OynaticiPaneli()
    {
        Strings.Use("en");
        var panel = new PlayerShortcutsPanel();
        var pencere = new Window { Width = 900, Height = 1400, Content = panel };
        pencere.Show();
        Dispatcher.UIThread.RunJobs();
        return (pencere, panel);
    }

    private static ShortcutKeyButton Kutu(ShortcutTable tablo, PlayerInput varsayilan)
        => tablo.Buttons.First(b => ShortcutBindings.Same(b.Slot.Default, varsayilan) && b.Slot.Default.Symbol == varsayilan.Symbol);

    private static bool Bas(Control hedef, Key tus, KeyModifiers ek = KeyModifiers.None)
    {
        var olay = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = tus, KeyModifiers = ek };
        hedef.RaiseEvent(olay);
        return olay.Handled;
    }

    private static bool FareBas(Control hedef, PointerUpdateKind tur, RawInputModifiers dugme)
    {
        var olay = new PointerPressedEventArgs(hedef, Fare, hedef, new Point(2, 2), 0, new PointerPointProperties(dugme, tur), KeyModifiers.None, 1)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        };
        hedef.RaiseEvent(olay);
        return olay.Handled;
    }

    private static bool FareBirak(Control hedef, MouseButton dugme)
    {
        var olay = new PointerReleasedEventArgs(hedef, Fare, hedef, new Point(2, 2), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, dugme)
        {
            RoutedEvent = InputElement.PointerReleasedEvent
        };
        hedef.RaiseEvent(olay);
        return olay.Handled;
    }

    private static PlayerAction? Eylem(PlayerInput girdi)
        => Keymap.Rows.FirstOrDefault(r => ShortcutBindings.Same(r.Input, girdi))?.Action;

    [Fact]
    public void SolTikSatirininAltindaCiftTikVeBasiliTutmaSatirlariDurur()
    {
        var sonuc = AppHost.Run(() =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var satirlar = panel.Shown.ToList();
                var sol = satirlar.FindIndex(s => s.Gesture == ShortcutKeyButton.Display(PlayerInput.OnPress(PlayerButton.Left)));
                var hucreler = panel.Table.Buttons.Count();
                panel.Table.Filter = "2×";
                var suzulen = panel.Shown.ToList();
                var sayim = panel.Table.Matches("Double-click");
                panel.Table.Filter = "";
                return (satirlar, sol, hucreler, suzulen, sayim);
            }
            finally
            {
                pencere.Close();
            }
        });

        Assert.True(sonuc.sol >= 0, "sol tık satırı yok");
        Assert.Equal(("<Double-click>", Bicim.Satir.Bagla("Full screen")), sonuc.satirlar[sonuc.sol + 1]);
        Assert.Equal(("<Hold left click>", Bicim.Satir.Bagla("Play at 2× until released")), sonuc.satirlar[sonuc.sol + 2]);
        Assert.Equal(1, sonuc.satirlar.Count(s => s.Gesture == "<Double-click>"));
        Assert.Equal(sonuc.satirlar.Count - 2 - sonuc.satirlar.Count(s => s.Gesture.Contains("Wheel", StringComparison.Ordinal)), sonuc.hucreler);
        Assert.Contains(("<Hold left click>", Bicim.Satir.Bagla("Play at 2× until released")), sonuc.suzulen);
        Assert.DoesNotContain(sonuc.suzulen, s => s.Gesture == "<Double-click>");
        Assert.Equal(1, sonuc.sayim);
    }

    [Fact]
    public void TusYakalanirDosyayaYazilirVeGeriOkunur()
    {
        var sonuc = AppHost.Run(() => Dosyayla(dosya =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var bosluk = PlayerInput.OnKey(Key.Space, KeyModifiers.None);
                var eylem = Eylem(bosluk);
                var kutu = Kutu(panel.Table, bosluk);
                var ilkMetin = kutu.Text;

                kutu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var dinliyor = ShortcutKeyButton.IsListening && kutu.Classes.Contains("listening");
                var dinlemeMetni = kutu.Text;

                var yutuldu = Bas(kutu, Key.F2, KeyModifiers.Control | KeyModifiers.Shift);
                Dispatcher.UIThread.RunJobs();
                var yeni = PlayerInput.OnKey(Key.F2, KeyModifiers.Control | KeyModifiers.Shift);
                var yeniEylem = Eylem(yeni);
                var eskiEylem = Eylem(bosluk);
                var yeniKutu = Kutu(panel.Table, bosluk);
                var yazilan = File.ReadAllText(dosya);

                ShortcutBindings.UseFile(dosya);
                Dispatcher.UIThread.RunJobs();
                var geriOkunan = Eylem(yeni);

                panel.Table.Reset();
                Dispatcher.UIThread.RunJobs();
                var sifir = (Eylem(bosluk), Eylem(yeni), ShortcutBindings.HasOverrides(ShortcutMap.Player), Kutu(panel.Table, bosluk).Text);

                return (eylem, ilkMetin, dinliyor, dinlemeMetni, yutuldu, yeniEylem, eskiEylem, yeniKutu.Text, dinlerSonra: ShortcutKeyButton.IsListening, yazilan, geriOkunan, sifir);
            }
            finally
            {
                pencere.Close();
            }
        }));

        Assert.NotNull(sonuc.eylem);
        Assert.Equal("<Space>", sonuc.ilkMetin);
        Assert.True(sonuc.dinliyor);
        Assert.Equal("Press a key…", sonuc.dinlemeMetni);
        Assert.True(sonuc.yutuldu);
        Assert.Same(sonuc.eylem, sonuc.yeniEylem);
        Assert.Null(sonuc.eskiEylem);
        Assert.Equal("<Ctrl+Shift+F2>", sonuc.Item8);
        Assert.False(sonuc.dinlerSonra);
        Assert.Contains("Key/F2/6", sonuc.yazilan);
        Assert.Contains("Key/Space/0", sonuc.yazilan);
        Assert.Same(sonuc.eylem, sonuc.geriOkunan);
        Assert.Same(sonuc.eylem, sonuc.sifir.Item1);
        Assert.Null(sonuc.sifir.Item2);
        Assert.False(sonuc.sifir.Item3);
        Assert.Equal("<Space>", sonuc.sifir.Item4);
    }

    [Theory]
    [InlineData(PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton, (int)PlayerButton.Middle, MouseButton.Middle)]
    [InlineData(PointerUpdateKind.XButton1Pressed, RawInputModifiers.XButton1MouseButton, (int)PlayerButton.Back, MouseButton.XButton1)]
    [InlineData(PointerUpdateKind.XButton2Pressed, RawInputModifiers.XButton2MouseButton, (int)PlayerButton.Forward, MouseButton.XButton2)]
    public void FareDugmesiAtanir(PointerUpdateKind tur, RawInputModifiers dugme, int dugmeNo, MouseButton birakilan)
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var girdi = PlayerInput.OnKey(Key.M, KeyModifiers.None);
                var eylem = Eylem(girdi);
                var kutu = Kutu(panel.Table, girdi);
                kutu.Listen();
                var basildi = FareBas(kutu, tur, dugme);
                var birakildi = FareBirak(panel, birakilan);
                var sonrakiBirakma = FareBirak(panel, birakilan);
                Dispatcher.UIThread.RunJobs();
                var atanan = Eylem(PlayerInput.OnPress((PlayerButton)dugmeNo));
                return (eylem, atanan, basildi, birakildi, sonrakiBirakma, Kutu(panel.Table, girdi).Text, ShortcutKeyButton.IsListening);
            }
            finally
            {
                pencere.Close();
            }
        }));

        Assert.NotNull(sonuc.eylem);
        Assert.Same(sonuc.eylem, sonuc.atanan);
        Assert.True(sonuc.basildi);
        Assert.True(sonuc.birakildi);
        Assert.False(sonuc.sonrakiBirakma);
        Assert.Equal(Keymap.Boxed(Keymap.Gesture(PlayerInput.OnPress((PlayerButton)dugmeNo))), sonuc.Item6);
        Assert.False(sonuc.Item7);
    }

    [Fact]
    public void EscVeSolTikVazgecerAtamaDegismez()
    {
        var sonuc = AppHost.Run(() => Dosyayla(dosya =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var girdi = PlayerInput.OnKey(Key.M, KeyModifiers.None);
                var kutu = Kutu(panel.Table, girdi);

                kutu.Listen();
                var escYutuldu = Bas(kutu, Key.Escape);
                var escSonra = (ShortcutKeyButton.IsListening, kutu.Classes.Contains("listening"), kutu.Text);

                kutu.Listen();
                var solYutuldu = FareBas(kutu, PointerUpdateKind.LeftButtonPressed, RawInputModifiers.LeftMouseButton);
                var solSonra = (ShortcutKeyButton.IsListening, kutu.Text);

                kutu.Listen();
                var degistirici = Bas(kutu, Key.LeftCtrl, KeyModifiers.Control);
                var degistiriciSonra = ShortcutKeyButton.IsListening;
                kutu.Stop();

                return (escYutuldu, escSonra, solYutuldu, solSonra, degistirici, degistiriciSonra,
                    ShortcutBindings.HasOverrides(ShortcutMap.Player), File.Exists(dosya), Eylem(girdi) is not null);
            }
            finally
            {
                pencere.Close();
            }
        }));

        Assert.True(sonuc.escYutuldu);
        Assert.False(sonuc.escSonra.Item1);
        Assert.False(sonuc.escSonra.Item2);
        Assert.Equal("<M>", sonuc.escSonra.Item3);
        Assert.True(sonuc.solYutuldu);
        Assert.False(sonuc.solSonra.Item1);
        Assert.Equal("<M>", sonuc.solSonra.Item2);
        Assert.True(sonuc.degistirici);
        Assert.True(sonuc.degistiriciSonra);
        Assert.False(sonuc.Item7);
        Assert.False(sonuc.Item8);
        Assert.True(sonuc.Item9);
    }

    [Fact]
    public void CakismadaYeniAtamaKazanirOburuAtanmamisKalir()
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var m = PlayerInput.OnKey(Key.M, KeyModifiers.None);
                var bosluk = PlayerInput.OnKey(Key.Space, KeyModifiers.None);
                var mEylemi = Eylem(m);
                var boslukEylemi = Eylem(bosluk);
                var mKutusu = Kutu(panel.Table, m);
                var calinan = Kutu(panel.Table, bosluk).Slot.Label;

                var bildirimOnce = panel.Table.Notice;
                mKutusu.Listen();
                Bas(mKutusu, Key.Space);
                Dispatcher.UIThread.RunJobs();

                var boslukSlotu = ShortcutBindings.Slots(ShortcutMap.Player).First(s => ShortcutBindings.Same(s.Default, bosluk) && s.Default.Symbol is null);
                return (mEylemi, boslukEylemi, simdiBosluk: Eylem(bosluk), eskiM: Eylem(m), bildirimOnce, bildirim: panel.Table.Notice,
                    calinan, calinanMetin: Kutu(panel.Table, bosluk).Text, boslukSlotu.Current, mEtiket: mKutusu.Slot.Label);
            }
            finally
            {
                pencere.Close();
            }
        }));

        Assert.NotSame(sonuc.mEylemi, sonuc.boslukEylemi);
        Assert.Same(sonuc.mEylemi, sonuc.simdiBosluk);
        Assert.Null(sonuc.eskiM);
        Assert.Null(sonuc.Current);
        Assert.Equal("", sonuc.bildirimOnce);
        Assert.Contains(sonuc.calinan, sonuc.bildirim);
        Assert.Contains(sonuc.mEtiket, sonuc.bildirim);
        Assert.Contains("<Space>", sonuc.bildirim);
        Assert.Equal("<unassigned>", sonuc.calinanMetin);
    }

    [Fact]
    public void DinlerkenOynaticiTusuGormez()
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var (pencere, panel) = OynaticiPaneli();
            var view = GirdiSurucu.Kur(out var oynaticiPenceresi);
            oynaticiPenceresi.Show();
            try
            {
                view.Seek.Duration = 100000;
                view.Seek.GoTo(1000);
                var kutu = Kutu(panel.Table, PlayerInput.OnKey(Key.M, KeyModifiers.None));

                var once = view.Trace.Count;
                GirdiSurucu.Key(view, Key.Right);
                var dinlemedenIz = view.Trace.Count - once;
                var dinlemedenKonum = view.PositionSeconds;

                kutu.Listen();
                once = view.Trace.Count;
                GirdiSurucu.Key(view, Key.Right);
                Kutu(panel.Table, PlayerInput.OnKey(Key.Space, KeyModifiers.None)).Listen();
                GirdiSurucu.Press(view, PointerUpdateKind.MiddleButtonPressed, RawInputModifiers.MiddleMouseButton);
                var dinlerkenIz = view.Trace.Count - once;
                var dinlerkenKonum = view.PositionSeconds;
                Dispatcher.UIThread.RunJobs();
                return (dinlemedenIz, dinlerkenIz, dinlemedenKonum, dinlerkenKonum, Eylem(PlayerInput.OnKey(Key.Right, KeyModifiers.None)), Eylem(PlayerInput.OnKey(Key.M, KeyModifiers.None)), orta: Eylem(PlayerInput.OnPress(PlayerButton.Middle)), bosluk: Eylem(PlayerInput.OnKey(Key.Space, KeyModifiers.None)));
            }
            finally
            {
                if (ShortcutKeyButton.Listening is { } dinleyen) dinleyen.Stop();
                view.Close();
                oynaticiPenceresi.Close();
                pencere.Close();
            }
        }));

        Assert.True(sonuc.dinlemedenIz > 0);
        Assert.Equal(0, sonuc.dinlerkenIz);
        Assert.Equal(sonuc.dinlemedenKonum, sonuc.dinlerkenKonum);
        Assert.Null(sonuc.Item6);
        Assert.NotNull(sonuc.Item5);
        Assert.NotNull(sonuc.orta);
        Assert.Null(sonuc.bosluk);
    }

    private sealed record Duzenleyici(Window Pencere, EditorView Gorunum, EditorShortcutsPanel Panel);

    private static Duzenleyici DuzenleyiciKur()
    {
        Strings.Use("en");
        var gorunum = new EditorView();
        var panel = new EditorShortcutsPanel();
        var kok = new DockPanel();
        DockPanel.SetDock(panel, Dock.Right);
        kok.Children.Add(panel);
        kok.Children.Add(gorunum);
        var pencere = new Window { Width = 1400, Height = 900, Content = kok };
        pencere.Show();
        gorunum.ShowTimeline(EditTimeline.FromSource(120 * EditTime.TicksPerSecond), 25);
        Dispatcher.UIThread.RunJobs();
        return new Duzenleyici(pencere, gorunum, panel);
    }

    [Fact]
    public void DuzenleyicideAtananTusVeFareDugmesiKomutuCalistirir()
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var d = DuzenleyiciKur();
            try
            {
                var cizelge = d.Gorunum.TimelineView;
                var i = PlayerInput.OnKey(Key.I, KeyModifiers.None);
                var markIn = Kutu(d.Panel.Table, i);
                markIn.Listen();
                Bas(markIn, Key.F3);
                Dispatcher.UIThread.RunJobs();

                cizelge.Playhead = 10 * EditTime.TicksPerSecond;
                Bas(cizelge, Key.I);
                var eskiTusSonra = cizelge.MarkIn;
                Bas(cizelge, Key.F3);
                var yeniTusSonra = cizelge.MarkIn;

                var marker = Kutu(d.Panel.Table, PlayerInput.OnKey(Key.M, KeyModifiers.None));
                marker.Listen();
                FareBas(marker, PointerUpdateKind.XButton2Pressed, RawInputModifiers.XButton2MouseButton);
                FareBirak(marker, MouseButton.XButton2);
                Dispatcher.UIThread.RunJobs();

                cizelge.Playhead = 5 * EditTime.TicksPerSecond;
                var markerOnce = cizelge.Markers.Count;
                FareBas(cizelge, PointerUpdateKind.XButton2Pressed, RawInputModifiers.XButton2MouseButton);
                FareBirak(cizelge, MouseButton.XButton2);
                var markerSonra = cizelge.Markers.Count;

                var ipucu = EditorView.Tip("editor.mark-in", EditorCommand.MarkIn);
                return (eskiTusSonra, yeniTusSonra, markerOnce, markerSonra, ipucu,
                    komut: EditorKeymap.For(Key.F3, KeyModifiers.None), eski: EditorKeymap.For(Key.I, KeyModifiers.None), fare: EditorKeymap.ForPress(PlayerButton.Forward));
            }
            finally
            {
                d.Pencere.Close();
            }
        }));

        Assert.NotEqual(10 * EditTime.TicksPerSecond, sonuc.eskiTusSonra);
        Assert.Equal(10 * EditTime.TicksPerSecond, sonuc.yeniTusSonra);
        Assert.Equal(sonuc.markerOnce + 1, sonuc.markerSonra);
        Assert.EndsWith("<F3>", sonuc.ipucu);
        Assert.Equal(EditorCommand.MarkIn, sonuc.komut);
        Assert.Equal(EditorCommand.None, sonuc.eski);
        Assert.Equal(EditorCommand.AddMarker, sonuc.fare);
    }

    [Fact]
    public void DinlerkenDuzenleyiciTusuGormez()
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var d = DuzenleyiciKur();
            try
            {
                var cizelge = d.Gorunum.TimelineView;
                cizelge.Playhead = 5 * EditTime.TicksPerSecond;
                var kutu = Kutu(d.Panel.Table, PlayerInput.OnKey(Key.I, KeyModifiers.None));
                var ilkMarkIn = cizelge.MarkIn;
                kutu.Listen();
                var yutuldu = Bas(cizelge, Key.M);
                var dinlerkenMarker = cizelge.Markers.Count;
                var dinlerkenMarkIn = cizelge.MarkIn;
                Dispatcher.UIThread.RunJobs();

                Bas(cizelge, Key.M);
                var sonraMarkIn = cizelge.MarkIn;
                var sonraMarker = cizelge.Markers.Count;
                return (yutuldu, dinlerkenMarker, ayni: dinlerkenMarkIn == ilkMarkIn, sonraMarkIn, sonraMarker, atanan: EditorKeymap.For(Key.M, KeyModifiers.None));
            }
            finally
            {
                d.Pencere.Close();
            }
        }));

        Assert.True(sonuc.yutuldu);
        Assert.Equal(0, sonuc.dinlerkenMarker);
        Assert.True(sonuc.ayni);
        Assert.Equal(EditorCommand.MarkIn, sonuc.atanan);
        Assert.Equal(0, sonuc.sonraMarker);
        Assert.Equal(5 * EditTime.TicksPerSecond, sonuc.sonraMarkIn);
    }

    [Fact]
    public void SatirKimlikleriTekilVeYazimGidisDonusu()
    {
        AppHost.Run(() => Dosyayla(_ =>
        {
            foreach (var map in new[] { ShortcutMap.Player, ShortcutMap.Editor })
            {
                var slots = ShortcutBindings.Slots(map);
                Assert.NotEmpty(slots);
                Assert.Equal(slots.Count, slots.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
                foreach (var slot in slots)
                {
                    var geri = ShortcutBindings.Parse(ShortcutBindings.Canon(slot.Default));
                    Assert.NotNull(geri);
                    Assert.True(ShortcutBindings.Same(slot.Default, geri!.Value));
                    Assert.Equal(slot.Default.Symbol, geri.Value.Symbol);
                }
            }

            Assert.Null(ShortcutBindings.Parse("Key/Uydurma/0"));
            Assert.Null(ShortcutBindings.Parse("Press/Yok"));
            Assert.Null(ShortcutBindings.Parse("Key/None/0"));
            Assert.Equal("<F>", Keymap.Boxed("F"));
            return 0;
        }));
    }

    [Fact]
    public void BozukDosyaVarsayilanaDuserBilinmeyenSatirAtilir()
    {
        var sonuc = AppHost.Run(() =>
        {
            Directory.CreateDirectory(Kok);
            var bozuk = Path.Combine(Kok, Guid.NewGuid().ToString("N") + ".json");
            var karisik = Path.Combine(Kok, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(bozuk, "{ bozuk");
            File.WriteAllText(karisik, "{\"player\":{\"Key/M/0\":\"Uydurma\",\"Key/Space/0\":\"Key/F4/0\"}}");
            try
            {
                ShortcutBindings.UseFile(bozuk);
                var bozukta = ShortcutBindings.HasOverrides(ShortcutMap.Player);
                ShortcutBindings.UseFile(karisik);
                var m = Eylem(PlayerInput.OnKey(Key.M, KeyModifiers.None));
                var f4 = Eylem(PlayerInput.OnKey(Key.F4, KeyModifiers.None));
                var bosluk = Eylem(PlayerInput.OnKey(Key.Space, KeyModifiers.None));
                return (bozukta, m, f4, bosluk);
            }
            finally
            {
                ShortcutBindings.UseFile(null);
                File.Delete(bozuk);
                File.Delete(karisik);
            }
        });

        Assert.False(sonuc.bozukta);
        Assert.NotNull(sonuc.m);
        Assert.NotNull(sonuc.f4);
        Assert.Null(sonuc.bosluk);
    }

    [Fact]
    public void DinlemeKutusuGorunurBicimdeAyrisir()
    {
        var sonuc = AppHost.Run(() => Dosyayla(_ =>
        {
            var (pencere, panel) = OynaticiPaneli();
            try
            {
                var kutu = Kutu(panel.Table, PlayerInput.OnKey(Key.M, KeyModifiers.None));
                var kok = kutu.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Root");
                kok.Transitions = null;
                var once = (kok.Background as ISolidColorBrush)?.Color;
                kutu.Listen();
                Dispatcher.UIThread.RunJobs();
                var dinlerken = (kok.Background as ISolidColorBrush)?.Color;
                var cerceve = (kok.BorderBrush as ISolidColorBrush)?.Color;
                kutu.Stop();
                Dispatcher.UIThread.RunJobs();
                var sonra = (kok.Background as ISolidColorBrush)?.Color;
                var dolgu = pencere.TryFindResource("NeonBlueFill", out var f) ? (f as ISolidColorBrush)?.Color : null;
                var kenar = pencere.TryFindResource("NeonBlue", out var k) ? (k as ISolidColorBrush)?.Color : null;
                return (once, dinlerken, cerceve, sonra, dolgu, kenar);
            }
            finally
            {
                pencere.Close();
            }
        }));

        Assert.NotNull(sonuc.dolgu);
        Assert.Equal(sonuc.dolgu, sonuc.dinlerken);
        Assert.Equal(sonuc.kenar, sonuc.cerceve);
        Assert.NotEqual(sonuc.dinlerken, sonuc.once);
        Assert.Equal(sonuc.once, sonuc.sonra);
    }

    /// <summary>
    /// Tuş metni tavana (<c>ShortcutKeyMaxWidth</c>) sığdıkça tek satır; sığmazsa yalnız boşlukta
    /// sarılır, boşluksuz tuş tavanı aşsa da bölünmez; metin değişince yeniden tek satırdan başlar.
    /// Tavansız tuş sütunu uk/el açıklamasını ~120 px'e sıkıştırıp sözcük ortasından bölüyordu;
    /// düz tavan "&lt;Ctrl+Shift+Rotellin|a&gt;"yı bölüyordu; hep sarılan tuş th'de ikinci satıra düşüyordu.
    /// </summary>
    [Fact]
    public void TusMetniYalnizBosluktaVeTavanaSigmazsaSarilir()
    {
        var olcu = AppHost.Run(() =>
        {
            var kok = new StackPanel();
            var metin = new VidShrink.App.Playback.KeyText { Text = "<F>", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            kok.Children.Add(metin);
            var pencere = new Window { Content = kok, Width = 1000, Height = 400 };
            pencere.Show();
            (int Satir, TextWrapping Kip, double En, bool Bolunmus) Olc(string yazi)
            {
                metin.Text = yazi;
                pencere.UpdateLayout();
                var satirlar = metin.TextLayout.TextLines;
                var bolunmus = false;
                var konum = 0;
                for (var i = 0; i + 1 < satirlar.Count; i++)
                {
                    konum += satirlar[i].Length;
                    if (konum < yazi.Length && yazi[konum - 1] != ' ' && yazi[konum] != ' ') bolunmus = true;
                }
                return (satirlar.Count, metin.TextWrapping, metin.DesiredSize.Width, bolunmus);
            }

            try
            {
                return (tavan: metin.Cap, kisa: Olc("<F>"), bosluklu: Olc("<Клацання середньою кнопкою миші на панелі>"),
                    bosluksuz: Olc("<Ctrl+Shift+Rotellina-Ctrl+Shift+Rotellina>"), geri: Olc("<Esc>"));
            }
            finally { pencere.Close(); }
        });

        Assert.True(double.IsFinite(olcu.tavan), "ShortcutKeyMaxWidth kaynağı bulunamadı");
        Assert.Equal((1, TextWrapping.NoWrap), (olcu.kisa.Satir, olcu.kisa.Kip));
        Assert.True(olcu.bosluklu.Satir > 1, $"tavanı aşan boşluklu tuş sarılmadı: en {olcu.bosluklu.En}, tavan {olcu.tavan}, satir {olcu.bosluklu.Satir}, kip {olcu.bosluklu.Kip}");
        Assert.True(olcu.bosluklu.En <= olcu.tavan, $"boşluklu tuş tavanı aştı: {olcu.bosluklu.En} > {olcu.tavan}");
        Assert.False(olcu.bosluklu.Bolunmus, "boşluklu tuş sözcük ortasından bölündü");
        Assert.Equal(1, olcu.bosluksuz.Satir);
        Assert.True(olcu.bosluksuz.En > olcu.tavan, "boşluksuz örnek tavanı aşmıyor; ölçü bir şey sınamıyor");
        Assert.Equal((1, TextWrapping.NoWrap), (olcu.geri.Satir, olcu.geri.Kip));
    }
}
