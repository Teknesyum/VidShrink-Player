using Avalonia.Controls;

namespace VidShrink.App;

public partial class MainWindow
{
    private KaydediciKompakt? _kompakt;
    private bool _kompaktBagli;
    private bool _kompaktMinBekliyor;
    private double _kompaktMinWidth;
    private double _kompaktMinHeight;

    internal KaydediciKompakt Kompakt => _kompakt ??= new KaydediciKompakt(() => this);

    private void KompaktIzle()
    {
        Tabs.SelectionChanged += (_, _) => KompaktUygula();
        Kip.Degisti += KipDegisti;
    }

    private bool KompaktIstenir
        => IsVisible
           && Tabs.SelectedItem is TabItem secili && ReferenceEquals(secili, TabRecorder)
           && _recorderPane is { AdvancedMode: false }
           && !Kip.TamEkran && !Kip.Kucuk;

    internal void KompaktUygula()
    {
        if (Tabs.SelectedItem is TabItem secili && ReferenceEquals(secili, TabRecorder) && !_kompaktBagli && _recorderPane is { } pane)
        {
            _kompaktBagli = true;
            pane.LevelChanged += KompaktUygula;
        }

        if (KompaktIstenir) Kompakt.Gir();
        else if (_kompakt is { Acik: true }) _kompakt.Cik();
    }

    private void KipDegisti()
    {
        if (_kompakt is { Acik: true, Once: { } once })
        {
            _kompaktMinBekliyor = Kip.Kucuk;
            _kompaktMinWidth = once.MinWidth;
            _kompaktMinHeight = once.MinHeight;
            _kompakt.Unut(minimumlar: !Kip.Kucuk);
            return;
        }

        if (_kompaktMinBekliyor && !Kip.Kucuk)
        {
            _kompaktMinBekliyor = false;
            MinWidth = _kompaktMinWidth;
            MinHeight = _kompaktMinHeight;
        }
    }

    private void KompaktKapanis()
    {
        if (_kompakt is { Acik: true }) _kompakt.Hemen();
    }
}
