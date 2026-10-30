using System;

namespace VidShrink.App.Playback;

/// <summary>
/// Uygulamada aynı anda tek yüzey oynar: oynatıcı, düzenleyicinin monitörü ya da küçültücünün
/// karşılaştırma paneli. Oynamaya başlayan yüzey odağı alır, önceki sahibi kendi duraklatma
/// yolundan durdurulur; böylece düğmesi ve durumu da duraklamış görünür.
/// </summary>
internal static class OynatmaOdagi
{
    private static object? _sahip;
    private static Action? _duraklat;

    internal static object? Sahip => _sahip;

    internal static void Al(object sahip, Action duraklat)
    {
        if (ReferenceEquals(_sahip, sahip))
        {
            _duraklat = duraklat;
            return;
        }

        var onceki = _duraklat;
        _sahip = sahip;
        _duraklat = duraklat;
        onceki?.Invoke();
    }

    internal static void Birak(object sahip)
    {
        if (!ReferenceEquals(_sahip, sahip)) return;
        _sahip = null;
        _duraklat = null;
    }
}
