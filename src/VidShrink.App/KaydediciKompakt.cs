using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VidShrink.App.Playback;

namespace VidShrink.App;

internal sealed class KaydediciKompakt
{
    internal readonly record struct Durum(WindowState State, PixelRect Rect, double MinWidth, double MinHeight);

    private readonly Func<Window?> _pencere;
    private DispatcherTimer? _timer;
    private Action? _bitince;

    internal KaydediciKompakt(Func<Window?> pencere) => _pencere = pencere;

    internal Durum? Once { get; private set; }

    internal bool Acik => Once is not null;

    internal bool Kayiyor => _timer is not null;

    internal static PixelRect Hedef(PixelRect eski, PixelRect alan, int en, int boy)
    {
        en = Math.Min(en, alan.Width);
        boy = Math.Min(boy, alan.Height);
        var x = eski.X + (eski.Width - en) / 2;
        var y = eski.Y + (eski.Height - boy) / 2;
        x = Math.Clamp(x, alan.X, alan.Right - en);
        y = Math.Clamp(y, alan.Y, alan.Bottom - boy);
        return new PixelRect(x, y, en, boy);
    }

    internal static double Yumusat(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    internal static PixelRect Ara(PixelRect a, PixelRect b, double t)
    {
        static int L(int x, int y, double t) => (int)Math.Round(x + (y - x) * t);
        return new PixelRect(L(a.X, b.X, t), L(a.Y, b.Y, t), L(a.Width, b.Width, t), L(a.Height, b.Height, t));
    }

    internal void Gir()
    {
        if (Acik || _pencere() is not { } pencere) return;
        if (pencere.WindowState is not (WindowState.Normal or WindowState.Maximized)) return;
        var ekran = pencere.Screens.ScreenFromWindow(pencere) ?? pencere.Screens.Primary;
        if (ekran is null) return;
        var olcek = ekran.Scaling > 0 ? ekran.Scaling : 1;
        var alan = ekran.WorkingArea;

        var buyuk = pencere.WindowState == WindowState.Maximized;
        var simdi = Simdi(pencere, olcek);
        Once = new Durum(pencere.WindowState, simdi, pencere.MinWidth, pencere.MinHeight);

        var baslangic = buyuk ? alan : simdi;
        if (buyuk)
        {
            pencere.WindowState = WindowState.Normal;
            Koy(pencere, baslangic, olcek);
        }

        pencere.MinWidth = 0;
        pencere.MinHeight = 0;
        var en = (int)Math.Round(Belirtec(pencere, "RecorderCompactWindowWidth") * olcek);
        var boy = (int)Math.Round(Belirtec(pencere, "RecorderCompactWindowHeight") * olcek);
        Kay(pencere, baslangic, Hedef(baslangic, alan, en, boy), olcek, null);
    }

    internal void Cik()
    {
        if (Once is not { } once || _pencere() is not { } pencere) return;
        var ekran = pencere.Screens.ScreenFromWindow(pencere) ?? pencere.Screens.Primary;
        var olcek = ekran?.Scaling is > 0 and var s ? s : 1;
        var hedef = once.State == WindowState.Maximized && ekran is not null ? ekran.WorkingArea : once.Rect;
        Kay(pencere, Simdi(pencere, olcek), hedef, olcek, () => Bitir(pencere, once));
    }

    internal void Hemen()
    {
        _timer?.Stop();
        _timer = null;
        _bitince = null;
        if (Once is { } once && _pencere() is { } pencere)
        {
            var olcek = (pencere.Screens.ScreenFromWindow(pencere) ?? pencere.Screens.Primary)?.Scaling is > 0 and var s ? s : 1;
            Koy(pencere, once.Rect, olcek);
            Bitir(pencere, once);
        }
    }

    internal void Unut(bool minimumlar)
    {
        _timer?.Stop();
        _timer = null;
        _bitince = null;
        if (Once is { } once && minimumlar && _pencere() is { } pencere)
        {
            pencere.MinWidth = once.MinWidth;
            pencere.MinHeight = once.MinHeight;
        }

        Once = null;
    }

    private void Bitir(Window pencere, Durum once)
    {
        Once = null;
        pencere.MinWidth = once.MinWidth;
        pencere.MinHeight = once.MinHeight;
        if (once.State == WindowState.Maximized) pencere.WindowState = WindowState.Maximized;
    }

    private void Kay(Window pencere, PixelRect bas, PixelRect son, double olcek, Action? bitince)
    {
        _timer?.Stop();
        _timer = null;
        _bitince = bitince;
        var sure = pencere.TryFindResource("MotionSlow", out var m) && m is TimeSpan t ? t : TimeSpan.Zero;
        if (HoverZone.MotionReduced || sure <= TimeSpan.Zero)
        {
            Koy(pencere, son, olcek);
            Tamam();
            return;
        }

        var saat = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => { });
        timer.Tick += (_, _) =>
        {
            var oran = saat.Elapsed.TotalMilliseconds / sure.TotalMilliseconds;
            Koy(pencere, Ara(bas, son, Yumusat(oran)), olcek);
            if (oran < 1) return;
            timer.Stop();
            if (ReferenceEquals(_timer, timer)) Tamam();
        };
        _timer = timer;
        timer.Start();
    }

    private void Tamam()
    {
        _timer = null;
        var bitince = _bitince;
        _bitince = null;
        bitince?.Invoke();
    }

    private static PixelRect Simdi(Window pencere, double olcek)
        => new(pencere.Position.X, pencere.Position.Y,
            (int)Math.Round((double.IsFinite(pencere.Width) ? pencere.Width : pencere.Bounds.Width) * olcek),
            (int)Math.Round((double.IsFinite(pencere.Height) ? pencere.Height : pencere.Bounds.Height) * olcek));

    private static void Koy(Window pencere, PixelRect r, double olcek)
    {
        pencere.Position = r.Position;
        pencere.Width = r.Width / olcek;
        pencere.Height = r.Height / olcek;
    }

    private static double Belirtec(Window pencere, string ad)
        => pencere.TryFindResource(ad, out var v) && v is double d ? d : 0;
}
