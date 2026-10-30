using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using VidShrink.App.Playback;

namespace VidShrink.App;

/// <summary>
/// Pencerenin normal → tam ekran → küçük → tam ekran döngüsü. Orta tuş her sekmede, üst panelde
/// çift tık ve oynatıcının kendi orta tuşu buraya iner; sekme seçimine dokunmaz. Küçük kip
/// çalışma alanının <c>WindowCompactAreaShare</c> payı kadar, ortalı ve verilen en-boy oranında
/// (0 ise ekranın oranında) bir dikdörtgendir; en küçük pencere boyutu o süre kalkar (hedefe eşit taban
/// konursa Windows'un genişletilmiş istemci alanında boy 7 px büyük çıkar) ve
/// DWM kenarlık rengi kaldırılır. Esc tam ekrandan önceki boyuta, küçükten döngü başlamadan
/// önceki normal dikdörtgene döner.
/// </summary>
internal sealed class PencereKipi
{
    internal const int DwmBorderColorAttribute = 34;
    internal const uint DwmColorNone = 0xFFFFFFFE;
    internal const uint DwmColorDefault = 0xFFFFFFFF;

    private readonly Func<Window?> _pencere;
    private readonly FullscreenSwitch _tam = new();
    private readonly List<string> _trace = new();
    private Size? _min;
    private WindowSnapshot? _normal;

    internal PencereKipi(Func<Window?> pencere, Action<string>? yansit = null)
    {
        _pencere = pencere;
        Yansit = yansit;
    }

    internal Action<string>? Yansit { get; set; }

    internal event Action? Degisti;

    internal FullscreenSwitch Tam => _tam;

    internal bool TamEkran => _tam.IsFullscreen;

    internal bool Kucuk => _min is not null;

    internal IReadOnlyList<string> Iz => _trace.ToArray();

    internal uint? BorderColorRequest { get; private set; }

    internal int BorderColorWrites { get; private set; }

    private static WindowSnapshot Anlik(Window? pencere)
        => pencere is null
            ? new WindowSnapshot(0, 0, 0, 0, 0, -1)
            : new WindowSnapshot((int)pencere.WindowState, pencere.Position.X, pencere.Position.Y, pencere.Width, pencere.Height, -1);

    internal void TamEkranDegistir()
    {
        var pencere = _pencere();
        var simdi = Anlik(pencere);
        if (!_tam.IsFullscreen && _min is null) _normal = simdi;
        var sonraki = _tam.Toggle(simdi, (int)WindowState.FullScreen, simdi.TabIndex);

        if (pencere is not null)
        {
            pencere.WindowState = (WindowState)sonraki.State;
            if (sonraki.State == (int)WindowState.Normal)
            {
                pencere.Position = new PixelPoint((int)sonraki.X, (int)sonraki.Y);
                pencere.Width = sonraki.Width;
                pencere.Height = sonraki.Height;
            }
        }

        _trace.Add("tam -> " + _tam.IsFullscreen);
        Degisti?.Invoke();
    }

    internal void KucukVeyaTamEkran(double oran)
    {
        if (!_tam.IsFullscreen)
        {
            TamEkranDegistir();
            return;
        }

        _tam.Leave();
        var pencere = _pencere();
        if (pencere is not null)
        {
            pencere.WindowState = WindowState.Normal;
            var ekran = pencere.Screens.ScreenFromWindow(pencere) ?? pencere.Screens.Primary;
            if (ekran is not null)
            {
                var pay = pencere.TryFindResource("WindowCompactAreaShare", out var deger) && deger is double d ? d : 1;
                var alan = ekran.WorkingArea;
                var r = CompactWindow.Fit(alan.X, alan.Y, alan.Width, alan.Height, pay, oran);
                var en = r.Width / ekran.Scaling;
                var boy = r.Height / ekran.Scaling;
                _min ??= new Size(pencere.MinWidth, pencere.MinHeight);
                pencere.MinWidth = 0;
                pencere.MinHeight = 0;
                pencere.Width = en;
                pencere.Height = boy;
                pencere.Position = new PixelPoint(r.X, r.Y);
                Kenarlik(pencere, true);
            }
        }

        _trace.Add("kucuk -> " + Kucuk);
        Degisti?.Invoke();
    }

    /// <summary>Esc: tam ekrandan önceki boyuta, küçükten döngü öncesi normal dikdörtgene.</summary>
    internal bool Birak()
    {
        if (_tam.IsFullscreen)
        {
            TamEkranDegistir();
            return true;
        }

        if (_min is null) return false;
        var normal = _normal;
        if (normal is { State: (int)WindowState.Maximized } && _pencere() is { } tam)
        {
            tam.WindowState = WindowState.Maximized;
            KucukBitir();
        }
        else
        {
            KucukBitir();
            if (_pencere() is { } pencere && normal is { State: (int)WindowState.Normal, Width: > 0, Height: > 0 } n)
            {
                pencere.Position = new PixelPoint((int)n.X, (int)n.Y);
                pencere.Width = n.Width;
                pencere.Height = n.Height;
            }
        }

        _trace.Add("birak -> normal");
        return true;
    }

    internal void KucukBitir()
    {
        if (_min is not { } min) return;
        _min = null;
        if (_pencere() is { } pencere)
        {
            Kenarlik(pencere, false);
            pencere.MinWidth = min.Width;
            pencere.MinHeight = min.Height;
        }

        Degisti?.Invoke();
    }

    private void Kenarlik(Window pencere, bool kucuk)
    {
        var renk = kucuk ? DwmColorNone : DwmColorDefault;
        BorderColorRequest = renk;
        var satir = "border -> " + (kucuk ? "none" : "default");
        _trace.Add(satir);
        Yansit?.Invoke(satir);
        if (!OperatingSystem.IsWindows()) return;
        if (pencere.TryGetPlatformHandle() is not { } tutamak || tutamak.Handle == IntPtr.Zero) return;
        if (WriteBorderColor(tutamak.Handle, renk) == 0) BorderColorWrites++;
    }

    internal static int WriteBorderColor(IntPtr hwnd, uint color)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, DwmBorderColorAttribute, ref color, sizeof(uint));
        }
        catch (DllNotFoundException)
        {
            return -1;
        }
        catch (EntryPointNotFoundException)
        {
            return -1;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
}
