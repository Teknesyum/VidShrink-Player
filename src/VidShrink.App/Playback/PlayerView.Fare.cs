using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VidShrink.Core;

namespace VidShrink.App.Playback;

internal partial class PlayerView
{
    private readonly ClickArbiter _click = new();
    private SolTikHareketi? _sol;
    private DispatcherTimer? _tutmaSaati;
    private bool _hizliTutma;

    private SurfacePan? _pan;
    private PointerPressedEventArgs? _pressArgs;
    private TranslateTransform? _shift;
    private Point _lastDrag;
    private string _dragMode = "";

    /// <summary>Menunun sol tik ile acilan ayarlar satirinin cagirdigi yol.</summary>
    internal Action? OpenSettings { get; set; }

    internal Func<List<Control>>? AppSettingsItems { get; set; }

    internal ClickArbiter Click => _click;

    internal SolTikHareketi Sol => _sol ??= new SolTikHareketi(_click, TutmaEsigi().TotalSeconds, CiftTikSuresi().TotalSeconds);

    internal Func<double> FareSaati { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    internal bool HizliTutuluyor => _hizliTutma;

    internal double OynayanHiz => _hizliTutma ? Keymap.HoldSpeed : _speed;

    internal SurfacePan Pan => _pan ??= new SurfacePan(SnapDip());

    /// <summary>Menu son kez neye yaslandi: fare konumuna mi, ust satirdaki dugmeye mi.</summary>
    internal string MenuAnchor { get; private set; } = "";

    internal string DragMode => _dragMode;

    internal bool NativeMoveDrag { get; set; } = OperatingSystem.IsWindows();

    private void InitFare()
    {
        AddHandler(PointerMovedEvent, OnFarePointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnFarePointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, e) => { if (ReferenceEquals(e.Source, this)) EndWindowDrag("capturelost"); TutmaKoptu(); }, Avalonia.Interactivity.RoutingStrategies.Direct | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    /// <summary>
    /// Sol basis yalniz kurulur; isini birakista yapar. Basili kalirsa tutma saati kurulur.
    /// </summary>
    internal void FarePress(PointerPressedEventArgs e, double x, double y)
    {
        _pressArgs = e;
        FarePress(x, y);
    }

    internal void FarePress(double x, double y)
    {
        _windowDrag = false;
        _leftClicks++;
        _dragMode = "";
        TutmaBitir();
        Sol.Bas(x, y, FareSaati());
        _lastDrag = new Point(x, y);
        TutmaSaatiKur(Sol.TutmaSn);
    }

    internal bool FareZaman()
    {
        var an = FareSaati();
        if (!Sol.Zaman(an))
        {
            if (_click.Down && !_click.Dragging && !Sol.Tutuyor) TutmaSaatiKur(Sol.Kalan(an));
            return false;
        }

        _hizliTutma = true;
        _engine?.SetSpeed(Keymap.HoldSpeed);
        _trace.Add("hold -> " + Saat.Tani.Konum(Keymap.HoldSpeed));
        TutmaGostergesi();
        return true;
    }

    private void TutmaSaatiKur(double saniye)
    {
        if (_tutmaSaati is null)
        {
            _tutmaSaati = new DispatcherTimer();
            _tutmaSaati.Tick += (_, _) =>
            {
                _tutmaSaati.Stop();
                FareZaman();
            };
        }

        _tutmaSaati.Stop();
        _tutmaSaati.Interval = TimeSpan.FromSeconds(Math.Max(saniye, 0.001));
        _tutmaSaati.Start();
    }

    private void TutmaKoptu()
    {
        if (!_hizliTutma) return;
        _tutmaSaati?.Stop();
        Sol.Iptal();
        TutmaBitir();
    }

    private void TutmaBitir()
    {
        if (!_hizliTutma) return;
        _hizliTutma = false;
        _engine?.SetSpeed(_speed);
        _trace.Add("hold -> " + Saat.Tani.Konum(_speed));
        ShowSpeedOsd();
    }

    private TimeSpan TutmaEsigi()
        => this.TryFindResource("PlayerHoldFastDelay", out var value) && value is TimeSpan esik && esik > TimeSpan.Zero ? esik : PauseGlyphHold;

    private static TimeSpan CiftTikSuresi()
        => Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Fare oynadi. Esik gecilmeden once bos doner; gecilince kip secilir: normal pencerede
    /// <c>window</c> (pencere tasinir), maksimize ya da tam ekranda <c>pan</c> (goruntu kayar).
    /// </summary>
    internal string FareMove(double x, double y)
    {
        if (!Sol.Oyna(x, y)) return "";

        if (_dragMode.Length == 0)
        {
            _dragMode = WindowIsNormal() ? "window" : "pan";
            _lastDrag = new Point(x, y);
            _trace.Add("drag -> " + _dragMode);
            return _dragMode;
        }

        if (_dragMode == "pan")
        {
            PanBy(x - _lastDrag.X, y - _lastDrag.Y);
            _lastDrag = new Point(x, y);
        }

        return _dragMode;
    }

    /// <summary>
    /// Birakis. Tik o anda duraklat/baslat olur; ikinci tik tam ekrani cevirip ilk tikin
    /// duraklatmasini geri alir; tutma hizi eski hiza dondurur ve tik sayilmaz.
    /// </summary>
    internal SolTikSonucu FareRelease()
    {
        _tutmaSaati?.Stop();
        var sonuc = Sol.Birak(FareSaati());
        _dragMode = "";
        switch (sonuc)
        {
            case SolTikSonucu.Tik:
                Apply(Keymap.ForPress(PlayerButton.Left));
                break;
            case SolTikSonucu.CiftTik:
                Apply(Keymap.ForPress(PlayerButton.Left));
                Apply(Keymap.Fullscreen.ToCommand());
                break;
            case SolTikSonucu.TutmaBitti:
                TutmaBitir();
                break;
        }

        return sonuc;
    }

    internal void ResetPan()
    {
        if (_pan is null) return;
        _pan.Reset();
        ShowPan();
    }

    private void PanBy(double deltaX, double deltaY)
    {
        var pan = Pan;
        pan.SetBounds(Frame.Bounds.Width, Frame.Bounds.Height, Surface.Bounds.Width, Surface.Bounds.Height);
        pan.Drag(deltaX, deltaY);
        ShowPan();
        _trace.Add(FormattableString.Invariant($"pan -> {pan.X:0.###},{pan.Y:0.###}"));
    }

    private void ShowPan()
    {
        if (_pan is null) return;
        _shift ??= new TranslateTransform();
        _shift.X = _pan.X;
        _shift.Y = _pan.Y;
        Frame.RenderTransform = _shift;
    }

    private double SnapDip()
        => this.TryFindResource("PlaybackBadgeMargin", out var value) && value is Thickness edge ? edge.Left : 0;

    private bool WindowIsNormal()
        => !Kip.TamEkran
           && (TopLevel.GetTopLevel(this) is not Window window || window.WindowState == WindowState.Normal);

    private void OnFarePointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
        {
            DokunOyna(e);
            return;
        }

        if (_windowDrag)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Pointer.Captured is null)
            {
                EndWindowDrag("nobutton");
                return;
            }

            if (TopLevel.GetTopLevel(this) is Window dragged) DragWindow(dragged, this.PointToScreen(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (_seekDragging || IsSeekBarSource(e.Source)) return;
        var point = e.GetPosition(this);
        var grab = _lastDrag;
        if (FareMove(point.X, point.Y) != "window") return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Sol.Iptal();
            _dragMode = "";
            return;
        }

        Sol.Iptal();
        if (NativeMoveDrag)
        {
            _dragMode = "";
            if (_pressArgs is not { } press)
            {
                _trace.Add("movedrag -> nopress");
                return;
            }

            _trace.Add("movedrag -> native");
            window.BeginMoveDrag(press);
            return;
        }

        e.Pointer.Capture(this);
        _dragMode = "window";
        _windowDrag = true;
        _dragWindowStart = window.Position;
        _dragPointerStart = this.PointToScreen(grab);
        DragWindow(window, this.PointToScreen(point));
        e.Handled = true;
    }

    private void EndWindowDrag(string reason)
    {
        if (!_windowDrag && _dragMode != "window") return;
        _windowDrag = false;
        _dragMode = "";
        Sol.Iptal();
        _trace.Add("dragend -> " + reason);
    }

    private bool _windowDrag;
    private PixelPoint _dragWindowStart;
    private PixelPoint _dragPointerStart;

    internal bool WindowDragging => _windowDrag;

    internal static PixelPoint DragPosition(PixelPoint windowStart, PixelPoint pointerStart, PixelPoint pointerNow)
        => new(windowStart.X + pointerNow.X - pointerStart.X, windowStart.Y + pointerNow.Y - pointerStart.Y);

    private void DragWindow(Window window, PixelPoint pointer)
    {
        var target = DragPosition(_dragWindowStart, _dragPointerStart, pointer);
        if (target == window.Position) return;
        window.Position = target;
        _trace.Add("move -> " + FormattableString.Invariant($"{target.X},{target.Y}"));
    }

    private void OnFarePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
        {
            DokunBirak(e);
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (_windowDrag)
        {
            _windowDrag = false;
            _dragMode = "";
            e.Handled = true;
        }

        if (IsSeekBarSource(e.Source)) return;
        FareRelease();
    }

    private readonly DokunmaHareketi _dokunma = new(ClickArbiter.DragThresholdDip);
    private double _kistirmaOlcegi = 1;

    private void DokunBas(PointerPressedEventArgs e)
    {
        var at = e.GetPosition(Surface);
        _dokunma.Bas(e.Pointer.Id, at.X, at.Y);
        if (_dokunma.Kip == DokunmaKipi.Kistirma) _kistirmaOlcegi = _zoom.PanelScale;
        e.Handled = true;
    }

    private void DokunOyna(PointerEventArgs e)
    {
        var at = e.GetPosition(Surface);
        var adim = _dokunma.Oyna(e.Pointer.Id, at.X, at.Y);
        var en = Surface.Bounds.Width;
        var boy = Surface.Bounds.Height;
        switch (adim.Kip)
        {
            case DokunmaKipi.Atlama when en > 0:
                Apply(new PlayerCommand(PlayerCommandKind.Seek, adim.Miktar / en * Keymap.Steps.Of(SeekStep.Medium)));
                break;
            case DokunmaKipi.Ses when boy > 0:
                Apply(new PlayerCommand(PlayerCommandKind.Volume, adim.Miktar / boy * VolumeCeiling()));
                break;
            case DokunmaKipi.Kistirma:
                _zoom.ScaleTo(_kistirmaOlcegi * adim.Miktar, _dokunma.OrtaX, _dokunma.OrtaY);
                Resize();
                _trace.Add("pinch " + Saat.Tani.Konum(adim.Miktar) + " -> " + Saat.Tani.Konum(_zoom.PanelScale));
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void DokunBirak(PointerReleasedEventArgs e)
    {
        if (_dokunma.Birak(e.Pointer.Id)) Apply(Keymap.ForPress(PlayerButton.Left));
        e.Handled = true;
    }
}
