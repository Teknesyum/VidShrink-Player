namespace VidShrink.App.Playback;

internal sealed class SeekSmoother
{
    private const double SnapAhead = 0.4;
    private const double SnapBehind = 0.15;
    private const double Settle = 0.12;
    private const double MaxStep = 0.1;

    private double _shown;
    private double _last;
    private bool _has;

    internal double Shown => _shown;

    internal void Reset() => _has = false;

    internal double Update(double target, double nowSeconds, bool playing, double speed)
    {
        if (!_has || !playing)
        {
            _shown = target;
            _last = nowSeconds;
            _has = true;
            return _shown;
        }

        var dt = System.Math.Clamp(nowSeconds - _last, 0, MaxStep);
        _last = nowSeconds;
        var predicted = _shown + dt * speed;
        var error = target - predicted;
        if (error > SnapAhead || error < -SnapBehind)
        {
            _shown = target;
            return _shown;
        }

        predicted += error * (1 - System.Math.Exp(-dt / Settle));
        if (predicted > _shown) _shown = predicted;
        return _shown;
    }
}
