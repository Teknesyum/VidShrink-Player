using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Player;

namespace VidShrink.App.Playback;

/// <summary>
/// Hız ve ses göstergesi: kullanıcı hızı ya da sesi değiştirince sahnenin sol üstünde kısa bir rozet
/// yeni değeri söyler ("Hız 1,25×", "Ses %80", "Sessiz"). Değer sınıra dayanıp değişmese de rozet
/// o anki değeri gösterir; kullanıcı sınırda olduğunu böyle görür. Ardışık değişimde metin
/// güncellenir ve sayaç baştan başlar. Rozet <c>PlaybackOsdHold</c> boyunca tam görünür, ardından
/// <c>MotionFast</c> içinde solar; hareketi azaltılmış pencerede canlandırma yoktur, rozet anında
/// belirir ve anında kaybolur. Sayılar arayüz dilinin kültürüyle yazılır, yüzde işaretinin yeri
/// de o kültürden gelir.
/// </summary>
internal partial class PlayerView
{
    private DispatcherTimer? _osdTimer;
    private Transitions? _osdTransitions;
    private int _osdSira;

    private TimeSpan PlaybackOsdHold => this.FindResource("PlaybackOsdHold") is TimeSpan t ? t : TimeSpan.FromMilliseconds(1200);

    internal string? OsdText => OsdBadge.IsVisible ? TxtOsd.Text : null;

    internal TimeSpan OsdHold => PlaybackOsdHold;

    private void ShowSpeedOsd()
        => ShowOsd(Strings.Get("main.player.osd.speed", Bicim.Kat(_speed, Strings.Culture)), true);

    /// <summary>
    /// Basılı tutma hızı rozette <see cref="Keymap.HoldSpeed"/> olarak durur ve bırakılana dek
    /// solmaz; bırakışta rozet eski hızı söyleyip olağan sürede solar.
    /// </summary>
    private void TutmaGostergesi()
    {
        ShowOsd(Strings.Get("main.player.osd.speed", Bicim.Kat(Keymap.HoldSpeed, Strings.Culture)), true, true);
        _osdTimer?.Stop();
    }

    internal bool OsdSoluyor => _osdTimer?.IsEnabled == true;

    /// <summary>
    /// Hız kipinin simgesi harf değil göstergedir: iki kipin o anki hızı karşılaştırılır, yüksek olan
    /// ibresi sağa yatık ve ucu turuncu "hızlı" simgesini, düşük olan "yavaş" simgesini alır. Kullanıcı
    /// bir kipin hızını ötekinin üstüne çıkarırsa simgeler yer değiştirir. Etkin kipin hızı ayardan değil
    /// oynayan değerden okunur, çünkü kaydırıcıyla değişen hız ayara ancak sonra yazılır. İki hız eşitse
    /// kip değiştirmek hızı değiştirmez; iki kip de aynı simgeyi alır ve karar normal hıza (1×) göre verilir.
    /// </summary>
    internal bool SpeedFast => HizliMi(_speed, OtherModeSpeed);

    private double OtherModeSpeed
        => Math.Clamp(_speedModeB ? Settings.SpeedA : Settings.SpeedB, Keymap.MinimumSpeed, Keymap.MaximumSpeed);

    internal static bool HizliMi(double etkin, double diger) => etkin == diger ? etkin > 1 : etkin > diger;

    private void ApplySpeedGlyph(Control kutu, Avalonia.Controls.Shapes.Path govde, Avalonia.Controls.Shapes.Path uc, bool? zorla = null)
    {
        var hizli = zorla ?? SpeedFast;
        govde.Data = Icon(hizli ? "IconSpeedFast" : "IconSpeedSlow");
        uc.IsVisible = hizli;
        AutomationProperties.SetName(kutu, Strings.Get(hizli ? "main.player.speed.fast" : "main.player.speed.slow"));
    }

    /// <summary>
    /// Sessize alınınca yalnız "Sessiz"; sessizken ses düzeyi değişirse yeni düzey yanında durur,
    /// çünkü kullanıcı duymadığı bir değişikliği başka türlü göremez.
    /// </summary>
    private void ShowSoundOsd(bool muteToggled)
    {
        var volume = Bicim.Yuzde.Isaretli(Math.Round(_volume) / 100, Strings.Culture);
        if (!_muted) ShowOsd(Strings.Get("main.player.osd.volume", volume));
        else if (muteToggled) ShowOsd(Strings.Get("main.player.osd.muted"));
        else ShowOsd(Strings.Get("main.player.osd.mutedvolume", volume));
    }

    private string LoopText() => double.IsFinite(_loopStart)
        ? Strings.Get("main.player.loopstate", Saat.Konum(_loopStart, Strings.Culture), double.IsFinite(_loopEnd) ? Saat.Konum(_loopEnd, Strings.Culture) : "…")
        : Strings.Get("main.player.loopoff");

    private void ShowLoopOsd() => ShowOsd(LoopText());

    private void ShowChapterOsd(int index)
    {
        if (index >= 0 && index < _chapters.Count) ShowOsd(ChapterName(index));
    }

    private void ShowTrackOsd(PlaybackTrackKind kind, long id)
    {
        var key = kind == PlaybackTrackKind.Audio ? "main.player.osd.audio" : "main.player.osd.subtitle";
        var track = _engine?.Tracks.FirstOrDefault(t => t.Kind == kind && t.Id == id);
        var name = track is not null ? TrackName(track) : id > 0 ? Strings.Get("player.tracks.item", id) : Strings.Get("player.subtitle.off");
        ShowOsd(Strings.Get(key, name));
    }

    private bool OsdMotionReduced
        => TopLevel.GetTopLevel(this) is { } ust && ust.Classes.Contains("reduced-motion");

    private void ShowOsd(string text, bool hiz = false, bool? hizli = null)
    {
        OsdSpeedGlyph.IsVisible = hiz;
        if (hiz) ApplySpeedGlyph(OsdSpeedGlyph, GlyphOsdSpeed, GlyphOsdSpeedTip, hizli);
        _osdTransitions ??= OsdBadge.Transitions;
        _osdTimer ??= new DispatcherTimer();
        _osdTimer.Stop();
        _osdSira++;

        TxtOsd.Text = text;
        AutomationProperties.SetName(OsdBadge, text);
        OsdBadge.Transitions = OsdMotionReduced ? null : _osdTransitions;
        OsdBadge.IsVisible = true;
        OsdBadge.Opacity = 1;

        _osdTimer.Interval = PlaybackOsdHold;
        _osdTimer.Tick -= OnOsdDone;
        _osdTimer.Tick += OnOsdDone;
        _osdTimer.Start();
    }

    /// <summary>
    /// Solma geçişi render saatinin vuruşlarıyla ilerler; gizleme de aynı saatin damgasıyla sayılır.
    /// Duvar saatine bağlı bir gizleme, yük altında geç gelen ilk vuruştan önce düşüp solmayı kesiyordu.
    /// Saat hiç vurmazsa rozet <c>PlaybackOsdHold</c> sonunda yine gizlenir.
    /// </summary>
    private void OnOsdDone(object? sender, EventArgs e)
    {
        _osdTimer?.Stop();
        if (OsdMotionReduced)
        {
            OsdBadge.Transitions = null;
            OsdBadge.Opacity = 0;
            OsdBadge.IsVisible = false;
            return;
        }

        OsdBadge.Transitions = _osdTransitions;
        OsdBadge.Opacity = 0;
        var sira = _osdSira;
        if (TopLevel.GetTopLevel(this) is not { } ust)
        {
            OsdBadge.IsVisible = false;
            return;
        }

        TimeSpan? baslangic = null;
        void Kare(TimeSpan an)
        {
            if (sira != _osdSira) return;
            baslangic ??= an;
            if (an - baslangic.Value >= MotionFast) OsdBadge.IsVisible = false;
            else ust.RequestAnimationFrame(Kare);
        }

        ust.RequestAnimationFrame(Kare);
        DispatcherTimer.RunOnce(() =>
        {
            if (sira == _osdSira) OsdBadge.IsVisible = false;
        }, PlaybackOsdHold);
    }
}
