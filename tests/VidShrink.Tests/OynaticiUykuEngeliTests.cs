using Avalonia.Controls;
using Avalonia.Input;
using VidShrink.App.Playback;
using Xunit;

namespace VidShrink.Tests;

internal sealed class SahteUykuSaglayici : IUykuSaglayici
{
    public string Ad => "sahte";

    internal int Alinan { get; private set; }

    internal int Birakilan { get; private set; }

    internal bool Tutuluyor => Alinan > Birakilan;

    public bool Al()
    {
        Alinan++;
        return true;
    }

    public void Birak() => Birakilan++;
}

internal sealed class SahteImlecSaati : IHoverClock
{
    private Action? _fire;

    internal TimeSpan? Gecikme { get; private set; }

    internal bool Kurulu => _fire is not null;

    public void Start(TimeSpan delay, Action fire)
    {
        Gecikme = delay;
        _fire = fire;
    }

    public void Stop() => _fire = null;

    internal void Doldur()
    {
        var fire = _fire;
        _fire = null;
        fire?.Invoke();
    }
}

/// <summary>
/// OYA-P3: uyku engeli, imleci gizleme, küçültünce duraklatma ve yalnız oynatırken üstte tutma. Uyku engeli
/// gerçek sistem çağrısı yerine sahte sağlayıcıyla ölçülür; motor sahtedir, ses ve görüntü cihazı açılmaz.
/// </summary>
public sealed class OynaticiUykuEngeliTests
{
    private static PlayerView Ac(string ad, SahteUykuSaglayici saglayici, out Window pencere, UykuEngeli? engel = null)
    {
        var klasor = Path.Combine(BolumKanit.Folder, "uyku-" + ad);
        if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        Directory.CreateDirectory(klasor);
        var gecmis = Path.Combine(klasor, "gecmis.json");
        var motor = new BolumMotoru(Array.Empty<double>());
        var view = new PlayerView { EngineFactory = () => motor, HistoryPath = () => gecmis };
        view.UykuEngeli = engel ?? new UykuEngeli(saglayici);
        pencere = new Window { Width = 320, Height = 180, Content = view };
        pencere.Show();
        var acilis = view.OpenAsync(BolumKanit.Sahte("uyku-" + ad + ".mp4"));
        DenetimSurucu.Pump(view, () => acilis.IsCompleted, 10);
        acilis.GetAwaiter().GetResult();
        return view;
    }

    private static void Kapat(PlayerView view, Window pencere, string ad)
    {
        view.Close();
        pencere.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        BolumKanit.Kapat("uyku-" + ad + ".mp4", "uyku-" + ad);
    }

    [Fact]
    public void TestSurecindeVarsayilanSaglayiciSistemeDokunmaz()
    {
        Assert.IsType<BosUykuSaglayici>(UykuEngeli.Varsayilan.Saglayici);
        Assert.False(UykuEngeli.Varsayilan.Saglayici.Al());
    }

    [Fact]
    public void PaylasilanEngelIlkIsteyenleAlinirSonBirakanlaKalkar()
    {
        var sahte = new SahteUykuSaglayici();
        var engel = new UykuEngeli(sahte);
        object a = new(), b = new();

        engel.Yansit(a, true);
        engel.Yansit(a, true);
        engel.Yansit(b, true);
        Assert.Equal(1, sahte.Alinan);

        engel.Yansit(a, false);
        Assert.Equal(0, sahte.Birakilan);
        Assert.True(engel.Tutuluyor);

        engel.Yansit(b, false);
        engel.Yansit(b, false);
        Assert.Equal(1, sahte.Birakilan);
        Assert.False(engel.Tutuluyor);
    }

    [Fact]
    public void EngelOynatmadaAlinirDuraklamadaVeKapanistaBirakilir()
    {
        AppHost.Run(() =>
        {
            var sahte = new SahteUykuSaglayici();
            var view = Ac("oynat", sahte, out var pencere);
            Assert.True(view.IsPlaying);
            Assert.True(sahte.Tutuluyor);

            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.False(view.IsPlaying);
            Assert.False(sahte.Tutuluyor);

            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.True(sahte.Tutuluyor);
            Assert.Equal(2, sahte.Alinan);

            view.Close();
            Assert.False(sahte.Tutuluyor);
            Kapat(view, pencere, "oynat");
        });
    }

    [Fact]
    public void IkinciOynaticiBaslayincaIlkiDuraklarEngelTekSahipteKalir()
    {
        AppHost.Run(() =>
        {
            var sahte = new SahteUykuSaglayici();
            var engel = new UykuEngeli(sahte);
            var bir = Ac("bir", sahte, out var p1, engel);
            var iki = Ac("iki", sahte, out var p2, engel);
            Assert.Equal(1, engel.SahipSayisi);
            Assert.True(sahte.Tutuluyor);

            iki.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.False(sahte.Tutuluyor);
            Assert.Equal(0, engel.SahipSayisi);

            bir.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.True(sahte.Tutuluyor);
            Assert.Equal(1, engel.SahipSayisi);
            Kapat(bir, p1, "bir");
            Kapat(iki, p2, "iki");
        });
    }

    [Fact]
    public void ImlecFareDurunca_OynarkenGizlenir_HareketleVeDuraklamaylaDoner()
    {
        AppHost.Run(() =>
        {
            var view = Ac("imlec", new SahteUykuSaglayici(), out var pencere);
            var saat = new SahteImlecSaati();
            view.Imlec.Saat = saat;

            view.Imlec.Hareket();
            Assert.Equal(TimeSpan.FromSeconds(1), saat.Gecikme);
            saat.Doldur();
            Assert.True(view.ImlecGizli);
            Assert.NotNull(view.Cursor);

            view.Imlec.Hareket();
            Assert.False(view.ImlecGizli);
            Assert.Null(view.Cursor);

            saat.Doldur();
            Assert.True(view.ImlecGizli);
            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.False(view.ImlecGizli);
            Assert.False(saat.Kurulu);

            view.Imlec.Hareket();
            Assert.False(saat.Kurulu);
            Kapat(view, pencere, "imlec");
        });
    }

    [Fact]
    public void KucultuncePausaAyarKapaliykenOynatmaSurer_AcikkenDuraklar()
    {
        AppHost.Run(() =>
        {
            var view = Ac("kucult", new SahteUykuSaglayici(), out var pencere);
            Assert.False(view.Settings.PauseOnMinimize);

            pencere.WindowState = WindowState.Minimized;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsPlaying);

            pencere.WindowState = WindowState.Normal;
            view.TogglePauseOnMinimize();
            Assert.True(view.Settings.PauseOnMinimize);
            pencere.WindowState = WindowState.Minimized;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(view.IsPlaying);
            Assert.Contains("minimize -> pause", view.Trace);

            pencere.WindowState = WindowState.Normal;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(view.IsPlaying);
            Kapat(view, pencere, "kucult");
        });
    }

    [Fact]
    public void YalnizOynatirkenUstte_DuraklayincaBirakir_ElleUstteDokunulmaz()
    {
        AppHost.Run(() =>
        {
            var view = Ac("ustte", new SahteUykuSaglayici(), out var pencere);
            Assert.False(pencere.Topmost);

            view.ToggleTopmostWhilePlaying();
            Assert.True(pencere.Topmost);

            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.False(pencere.Topmost);

            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.True(pencere.Topmost);

            view.Apply(new PlayerCommand(PlayerCommandKind.ToggleTopmost, 0));
            Assert.True(view.IsTopmost);
            view.Apply(new PlayerCommand(PlayerCommandKind.TogglePlay, 0));
            Assert.True(pencere.Topmost);

            view.ToggleTopmostWhilePlaying();
            Kapat(view, pencere, "ustte");
        });
    }

    [Fact]
    public void AyarKapaliykenOynatmaUstteKoymaz()
    {
        AppHost.Run(() =>
        {
            var view = Ac("ustte-kapali", new SahteUykuSaglayici(), out var pencere);
            Assert.True(view.IsPlaying);
            Assert.False(pencere.Topmost);
            Kapat(view, pencere, "ustte-kapali");
        });
    }

    [Fact]
    public void IkiAyarVarsayilanKapaliGelirVeDosyadanGeriOkunur()
    {
        var klasor = Path.Combine(BolumKanit.Folder, "uyku-ayar");
        if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        Directory.CreateDirectory(klasor);
        var dosya = Path.Combine(klasor, PlayerSettings.FileName);
        var ilk = PlayerSettings.Load(dosya);
        Assert.False(ilk.PauseOnMinimize);
        Assert.False(ilk.TopmostWhilePlaying);

        ilk.PauseOnMinimize = true;
        ilk.TopmostWhilePlaying = true;
        ilk.Save(dosya);
        var geri = PlayerSettings.Load(dosya);
        Assert.True(geri.PauseOnMinimize);
        Assert.True(geri.TopmostWhilePlaying);
        BolumKanit.Kapat("uyku-ayar");
    }

    [Fact]
    public void AyarlarMenusundeIkiAnahtarVar()
    {
        AppHost.Run(() =>
        {
            var view = Ac("menu", new SahteUykuSaglayici(), out var pencere);
            var basliklar = view.SettingsItems().OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
            Assert.Contains(VidShrink.App.Localization.Strings.Get("player.view.pause-on-minimize"), basliklar);
            Assert.Contains(VidShrink.App.Localization.Strings.Get("player.view.topmost-while-playing"), basliklar);
            Kapat(view, pencere, "menu");
        });
    }
}
