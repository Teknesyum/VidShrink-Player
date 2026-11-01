using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace VidShrink.App.Playback;

/// <summary>
/// Panel yüzeyinde sol tık oynatıcı yüzeyiyle aynı üç anlamı taşır: tek tık bırakışta
/// oynat/duraklat, çift tık tam ekranı çevirip ilk tıkın duraklatmasını geri alır, basılı
/// tutma <c>PlayerHoldFastDelay</c> eşiğini aşınca bırakana dek <see cref="Keymap.HoldSpeed"/>.
/// Hızı panel uygulamaz; <see cref="SpeedRequested"/> ile kaynağın sahibine söyler.
/// </summary>
internal partial class ComparisonPanel
{
    private SolTikHareketi? _sol;
    private DispatcherTimer? _tutmaSaati;
    private bool _hizliTutma;

    internal SolTikHareketi Sol => _sol ??= new SolTikHareketi(_click, TutmaEsigi().TotalSeconds, CiftTikSuresi().TotalSeconds);

    internal Func<double> FareSaati { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    internal bool HizliTutuluyor => _hizliTutma;

    internal event EventHandler<double>? SpeedRequested;

    internal bool FareZaman()
    {
        var an = FareSaati();
        if (!Sol.Zaman(an))
        {
            if (_click.Down && !_click.Dragging && !Sol.Tutuyor) TutmaSaatiKur(Sol.Kalan(an));
            return false;
        }

        _hizliTutma = true;
        SpeedRequested?.Invoke(this, Keymap.HoldSpeed);
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
        _tutmaSaati?.Stop();
        Sol.Iptal();
        TutmaBitir();
    }

    private void TutmaBitir()
    {
        if (!_hizliTutma) return;
        _hizliTutma = false;
        SpeedRequested?.Invoke(this, 1);
    }

    private void SolBas(Point at)
    {
        TutmaBitir();
        Sol.Bas(at.X, at.Y, FareSaati());
        TutmaSaatiKur(Sol.TutmaSn);
    }

    private SolTikSonucu SolBirak()
    {
        _tutmaSaati?.Stop();
        var sonuc = Sol.Birak(FareSaati());
        switch (sonuc)
        {
            case SolTikSonucu.Tik:
                Strip.TogglePlay();
                break;
            case SolTikSonucu.CiftTik:
                Strip.TogglePlay();
                ToggleFullScreen();
                break;
            case SolTikSonucu.TutmaBitti:
                TutmaBitir();
                break;
        }

        return sonuc;
    }

    private TimeSpan TutmaEsigi()
        => this.TryFindResource("PlayerHoldFastDelay", out var value) && value is TimeSpan esik && esik > TimeSpan.Zero ? esik : TimeSpan.FromMilliseconds(500);

    private static TimeSpan CiftTikSuresi()
        => Application.Current?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500);
}
