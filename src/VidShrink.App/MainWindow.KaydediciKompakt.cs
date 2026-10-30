using Avalonia.Controls;

namespace VidShrink.App;

public partial class MainWindow
{
    private KaydediciKompakt? _kompakt;
    private bool _kompaktBagli;

    internal KaydediciKompakt Kompakt => _kompakt ??= new KaydediciKompakt(() => this);

    private void KompaktIzle()
    {
        Tabs.SelectionChanged += (_, _) => KompaktUygula();
        Kip.Degisti += KipDegisti;
    }

    private bool KompaktIstenir
        => IsVisible
           && Tabs.SelectedItem is TabItem secili && ReferenceEquals(secili, TabRecorder)
           && _recorderPane is { AdvancedMode: false };

    internal void KompaktUygula()
    {
        if (Tabs.SelectedItem is TabItem secili && ReferenceEquals(secili, TabRecorder) && !_kompaktBagli && _recorderPane is { } pane)
        {
            _kompaktBagli = true;
            pane.LevelChanged += KompaktUygula;
        }

        if (Kip.TamEkran || Kip.Kucuk) return;
        if (KompaktIstenir) Kompakt.Gir();
        else if (_kompakt is { Acik: true }) _kompakt.Cik();
    }

    private void KipDegisti()
    {
        if (Kip.TamEkran || Kip.Kucuk)
        {
            _kompakt?.Durdur();
            return;
        }

        KompaktUygula();
    }

    private void KompaktKapanis()
    {
        if (_kompakt is { Acik: true }) _kompakt.Hemen();
    }
}
