using System;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Ffmpeg;

namespace VidShrink.App;

public partial class ShrinkJobWindow
{
    private double? _istenenKalite;
    private bool? _olcerekAra;

    /// <summary>Later 141: kuyruğu açan pencerede hedefi kalite sürüyorsa istenen puan; boyut yazılmışsa <c>null</c>.</summary>
    internal double? IstenenKalite
    {
        get => _istenenKalite;
        set => _istenenKalite = value;
    }

    /// <summary>Gelişmiş'teki "Ölçerek ara" kutusu; açılışta ayardan okunur, testler kendi değerini verir.</summary>
    internal bool OlcerekAra
    {
        get => _olcerekAra ?? _appSettings.AdvSearchByMeasure;
        set => _olcerekAra = value;
    }

    internal Func<bool> OlcerHazir { get; set; } = () => QualityMeasurement.Instance.IsAvailable;

    /// <summary>Aramanın kendisi; testler sahtesini verir, gerçek ffmpeg çağrılmaz.</summary>
    internal MainWindow.OlcerekAramaKosusu? OlcerekAramaKosucu { get; set; }

    internal int OlcerekAramaCagrisi { get; private set; }

    private static Task<QualitySearchResult> VarsayilanArama(MediaInfo info, PlanOptions options, double requestedQuality,
        double startMb, IProgress<QualitySearchRound> progress, CancellationToken ct)
        => Task.Run(() => QualitySearchSampler.RunAsync(info, options, requestedQuality, startMb, null, null,
            ShrinkEngine.ProbeMeter(null), options.SpeedMode, null, progress, ct, options.EncoderThreads), ct);

    /// <summary>
    /// Kuyruktaki işin hedefi. Kutu açık, kuyruk kaliteden açılmış ve ölçer hazırsa her dosya kendi
    /// kalite hedefinden başlayıp en çok <see cref="MeasuredQualitySearch.MaxRounds"/> tur ölçülerek aranır;
    /// elle düzenlenen ve defterden dönen iş boyutunu taşıdığı için aranmaz. Ölçüm düşerse hedef değişmez.
    /// </summary>
    internal async Task<(PlanOptions Options, double TargetMb, QualitySearchResult? Arama)> AramaylaAsync(
        ShrinkRequest request, MediaInfo info, PlanOptions options, double targetMb, Action<QualitySearchRound>? tur, CancellationToken ct)
    {
        if (_istenenKalite is not { } istenen || _duzenlenen.ContainsKey(request) || _geriGelen.ContainsKey(request)
            || !MeasuredQualitySearch.ShouldRun(OlcerekAra, true, OlcerHazir(), false))
            return (options, targetMb, null);

        OlcerekAramaCagrisi++;
        var baslangic = PlanCalculator.TargetMbForQuality(info, options, istenen).TargetMb;
        var kosucu = OlcerekAramaKosucu ?? VarsayilanArama;
        var sonuc = await kosucu(info, options, istenen, baslangic, new Progress<QualitySearchRound>(r => tur?.Invoke(r)), ct);
        if (sonuc.MeasuredQuality is null) return (options, targetMb, sonuc);
        return (PlanCalculator.WithTarget(options, sonuc.TargetMb), sonuc.TargetMb, sonuc);
    }

    private void AramaTuru(AktifIs job, QualitySearchRound round)
    {
        if (!_aktif.Contains(job)) return;
        job.Fraction = Math.Clamp((round.Round - (round.MeasuredQuality is null ? 1.0 : 0.0)) / round.MaxRounds, 0.0, 1.0);
        job.Stage = round.MeasuredQuality is { } puan
            ? Say("main.run.search-round-done", round.Round, round.MaxRounds, round.TargetMb.ToString("0.0", Strings.CultureOf(_language)), puan.ToString("0.0", Strings.CultureOf(_language)))
            : Say("main.run.search-round", round.Round, round.MaxRounds, round.TargetMb.ToString("0.0", Strings.CultureOf(_language)));
        job.Remaining = "-";
        RefreshActive(false);
    }

    private string AramaOzeti(QualitySearchResult? arama)
    {
        if (arama is null) return "";
        var kultur = Strings.CultureOf(_language);
        return arama.MeasuredQuality is { } olculen
            ? " " + Say("main.run.search-result", arama.TargetMb.ToString("0.0", kultur), arama.SearchRounds.Count, olculen.ToString("0.0", kultur), arama.RequestedQuality.ToString("0.#", kultur))
            : " " + Say("main.run.search-failed");
    }
}
