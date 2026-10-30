using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace VidShrink.App.Playback;

internal partial class PlayerView
{
    private UykuEngeli? _uykuEngeli;
    private ImlecGizleme? _imlec;
    private Window? _izlenenPencere;
    private bool _ustteBizKoyduk;
    private bool _oynatmaYansidi;

    /// <summary>OYA-01: bu oynatıcının paylaştığı uyku engeli; verilmezse süreç varsayılanı.</summary>
    internal UykuEngeli UykuEngeli
    {
        get => _uykuEngeli ??= UykuEngeli.Varsayilan;
        set
        {
            _uykuEngeli?.Yansit(this, false);
            _uykuEngeli = value;
            _oynatmaYansidi = false;
            OynatmaDurumunuYansit();
        }
    }

    internal ImlecGizleme Imlec => _imlec ??= new ImlecGizleme(
        () => this.FindResource("PlaybackCursorHideDelay") is TimeSpan t ? t : TimeSpan.FromSeconds(1),
        () => !_pointerOnSerit && !MenuOpen && !_seekDragging,
        gizle => Cursor = gizle ? new Cursor(StandardCursorType.None) : null);

    internal bool ImlecGizli => Imlec.Gizli;

    private bool OynatmaSuruyor => _playing && _engine is not null;

    private void InitUyku()
    {
        AddHandler(PointerMovedEvent, (_, _) => Imlec.Hareket(), RoutingStrategies.Tunnel, true);
        AttachedToVisualTree += (_, _) => PencereyiIzle(TopLevel.GetTopLevel(this) as Window);
        DetachedFromVisualTree += (_, _) =>
        {
            PencereyiIzle(null);
            UykuEngeli.Yansit(this, false);
            _oynatmaYansidi = false;
        };
    }

    /// <summary>
    /// Oynat ya da duraklat olayının tek kapısı: uyku engeli, imleç saati ve "yalnız oynatırken üstte"
    /// aynı durumdan beslenir. Aynı durum ikinci kez gelirse hiçbir şey yapmaz.
    /// </summary>
    private void OynatmaDurumunuYansit()
    {
        var oynuyor = OynatmaSuruyor;
        if (_playing) OynatmaOdagi.Al(this, () => { if (_playing) TogglePlay(false); });
        else OynatmaOdagi.Birak(this);
        Imlec.Oynatma(oynuyor);
        if (_oynatmaYansidi == oynuyor) return;
        _oynatmaYansidi = oynuyor;
        UykuEngeli.Yansit(this, oynuyor);
        UstteTut(oynuyor);
    }

    /// <summary>
    /// OYA-49: ayar açıkken oynatma sürdükçe pencere üstte kalır, duraklayınca bırakılır. Yalnız bu yolun
    /// koyduğu üstte değeri geri alınır; elle açılmış "hep üstte" ya da mini kip dokunulmadan kalır.
    /// </summary>
    private void UstteTut(bool oynuyor)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        var iste = oynuyor && Settings.TopmostWhilePlaying;
        if (iste && !window.Topmost)
        {
            window.Topmost = true;
            _ustteBizKoyduk = true;
            _trace.Add("topmostplaying -> on");
        }
        else if (!iste && _ustteBizKoyduk)
        {
            _ustteBizKoyduk = false;
            if (_topmost) return;
            window.Topmost = false;
            _trace.Add("topmostplaying -> off");
        }
    }

    private void PencereyiIzle(Window? pencere)
    {
        if (ReferenceEquals(pencere, _izlenenPencere)) return;
        if (_izlenenPencere is not null) _izlenenPencere.PropertyChanged -= OnPencereDurumu;
        _izlenenPencere = pencere;
        if (pencere is not null) pencere.PropertyChanged += OnPencereDurumu;
    }

    private void OnPencereDurumu(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty || e.NewValue is not WindowState durum) return;
        PencereDurumuDegisti(durum);
    }

    /// <summary>OYA-50: pencere simge durumuna inince ayar açıksa oynatma duraklar; geri açılınca kendiliğinden sürmez.</summary>
    internal void PencereDurumuDegisti(WindowState durum)
    {
        if (durum != WindowState.Minimized || !OynatmaSuruyor || !Settings.PauseOnMinimize) return;
        TogglePlay(false);
        _trace.Add("minimize -> pause");
        RefreshState();
    }

    internal void TogglePauseOnMinimize()
    {
        EnsureSettings();
        _settings.PauseOnMinimize = !_settings.PauseOnMinimize;
        SaveSettings();
        _trace.Add("pauseonminimize -> " + _settings.PauseOnMinimize);
        AdvancedChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ToggleTopmostWhilePlaying()
    {
        EnsureSettings();
        _settings.TopmostWhilePlaying = !_settings.TopmostWhilePlaying;
        SaveSettings();
        _trace.Add("topmostwhileplaying -> " + _settings.TopmostWhilePlaying);
        UstteTut(OynatmaSuruyor);
        AdvancedChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ToggleMediaControls()
    {
        EnsureSettings();
        _settings.MediaControls = !_settings.MediaControls;
        SaveSettings();
        _trace.Add("mediacontrols -> " + _settings.MediaControls);
        AdvancedChanged?.Invoke(this, EventArgs.Empty);
    }
}
