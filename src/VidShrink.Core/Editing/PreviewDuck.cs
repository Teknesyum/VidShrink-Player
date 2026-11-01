using System.Globalization;

namespace VidShrink.Core.Editing;

/// <summary>
/// Onizlemedeki muzik kismasi. Teslimdeki <see cref="EditExport.DuckFilter"/> gibi anahtar kaynak sesi ve A3 izidir; onizlemede
/// o ses ayri motorda caldigi icin anahtar motorlarin tepe olcerinden (dBFS) okunur. Anahtar <see cref="KeyThresholdDb"/>'yi
/// asinca hedef <c>-DuckDb</c>, asmayinca 0 dB; kazanc hedefe saldiri (<see cref="EditExport.DuckAttackMs"/>) ya da birakma
/// (<see cref="EditMusic.DuckReleaseMs"/>) zaman sabitiyle ustel yaklasir. Esik teslimin <c>threshold / level_sc</c> oranidir.
/// </summary>
public sealed class PreviewDuck
{
    public static double KeyThresholdDb { get; } = 20 * Math.Log10(
        double.Parse(EditExport.DuckThreshold, CultureInfo.InvariantCulture) / double.Parse(EditExport.DuckSidechainLevel, CultureInfo.InvariantCulture));

    public static double AttackMs { get; } = double.Parse(EditExport.DuckAttackMs, CultureInfo.InvariantCulture);

    public double GainDb { get; private set; }

    public static bool Keyed(double keyPeakDb) => double.IsFinite(keyPeakDb) && keyPeakDb > KeyThresholdDb;

    public double Step(EditMusic? music, double keyPeakDb, double elapsedSeconds)
    {
        if (music is not { Duck: true })
        {
            GainDb = 0;
            return GainDb;
        }

        var target = Keyed(keyPeakDb) ? -music.DuckDb : 0;
        var tau = (target < GainDb ? AttackMs : music.DuckReleaseMs) / 1000.0;
        var dt = double.IsFinite(elapsedSeconds) ? Math.Max(0, elapsedSeconds) : 0;
        var next = GainDb + (target - GainDb) * (1 - Math.Exp(-dt / tau));
        GainDb = Math.Clamp(next, -music.DuckDb, 0);
        return GainDb;
    }

    public void Reset() => GainDb = 0;
}
