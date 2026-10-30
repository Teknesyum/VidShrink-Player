using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App;
using VidShrink.App.Localization;
using VidShrink.Core;

namespace VidShrink.Tests;

public sealed class SurumDugmesiTests
{
    private static readonly string Klasor = Path.Combine(TipSources.Root, ".calisma", "test-ciktilari", "surum-dugmesi");

    private static string Manifest(string surum) =>
        $$"""{"version":"{{surum}}","commit":"x","built":"2026-10-01T00:00:00Z","rid":"win-x64","files":[]}""";

    private static string Kurulu => MainWindow.DisplayVersion(UpdateCheck.CurrentVersion(typeof(MainWindow).Assembly));

    private static MainWindow Pencere()
    {
        Directory.CreateDirectory(Klasor);
        return new MainWindow
        {
            SettingsPathOverride = Path.Combine(Klasor, Guid.NewGuid().ToString("N") + ".json"),
            DisAcici = _ => { }
        };
    }

    private static void Tikla(Button dugme) => dugme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Bekle(Task is_)
    {
        var saat = System.Diagnostics.Stopwatch.StartNew();
        while (!is_.IsCompleted && saat.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Assert.True(is_.IsCompleted);
        is_.GetAwaiter().GetResult();
    }

    [Fact]
    public void DugmeAdinSagindaTiklanirVeSurumuTasir()
    {
        var sonuc = AppHost.Run(() =>
        {
            Strings.Use("tr");
            var p = Pencere();
            try
            {
                UstSeritTikTests.Yerlestir(p, new Size(1560, 1060));
                var ad = p.TxtAppName;
                var dugme = p.BtnVersion;
                var katman = p.GetVisualDescendants().OfType<Border>().First(b => b.Name == "TitleBarLayer");
                var tutulur = dugme.IsHitTestVisible
                    && dugme.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, katman)).OfType<Control>().All(a => a.IsHitTestVisible);
                var kardes = ReferenceEquals(dugme.GetVisualParent(), ad.GetVisualParent());
                var sirada = ((Panel)ad.GetVisualParent()!).Children.IndexOf(dugme) == ((Panel)ad.GetVisualParent()!).Children.IndexOf(ad) + 1;
                var sagda = dugme.Bounds.Left >= ad.Bounds.Right && dugme.Bounds.Width > 0;
                var metin = dugme.Content as string ?? "";
                var surumu = metin.StartsWith('v') && Kurulu.Length > 0 && metin[1..] == Kurulu;
                var adi = AutomationProperties.GetName(dugme) == Etiketler.Get("update.check");
                return $"kardes:{kardes}|sirada:{sirada}|sagda:{sagda}|tutulur:{tutulur}|ad-gecirgen:{!ad.IsHitTestVisible}|surum:{surumu}|erisim:{adi}";
            }
            finally
            {
                p.Close();
                Strings.Use("en");
            }
        });

        Assert.Equal("kardes:True|sirada:True|sagda:True|tutulur:True|ad-gecirgen:True|surum:True|erisim:True", sonuc);
    }

    [Fact]
    public void TikDenetimiBirKezCagirirVeGuncelseBildirir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var p = Pencere();
            try
            {
                var istek = 0;
                var cevap = new TaskCompletionSource<string?>();
                p.OtomatikGuncellemeAcik = () => false;
                p.ManifestGetir = _ => { istek++; return cevap.Task; };
                Tikla(p.BtnVersion);
                Tikla(p.BtnVersion);
                Dispatcher.UIThread.RunJobs();
                var surerken = $"istek:{istek}|bildirim:{p.VersionToast.IsVisible}";
                cevap.SetResult(Manifest(Kurulu));
                Bekle(p.SurumDenetimIsi!);
                var beklenen = $"{Etiketler.Get("update.current")} (v{Kurulu})";
                var bitince = $"istek:{istek}|bildirim:{p.VersionToast.IsVisible}|metin:{p.TxtVersionToast.Text == beklenen}|basari:{UpdateHealth.Load(p.SettingsPathOverride).LastSuccess is not null}|serit:{p.UpdateNotice.IsVisible}";
                p.SurumBildiriminiKapat();
                return new[] { surerken, bitince, $"kapandi:{!p.VersionToast.IsVisible}" };
            }
            finally { p.Close(); }
        });

        Assert.Equal(new[]
        {
            "istek:1|bildirim:False",
            "istek:1|bildirim:True|metin:True|basari:True|serit:False",
            "kapandi:True"
        }, sonuc);
    }

    [Fact]
    public void DusenDenetimSebebiTekSatirBildirir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var p = Pencere();
            try
            {
                p.OtomatikGuncellemeAcik = () => false;
                p.ManifestGetir = _ => throw new HttpRequestException("ag\nyok");
                Tikla(p.BtnVersion);
                Bekle(p.SurumDenetimIsi!);
                var metin = p.TxtVersionToast.Text ?? "";
                var beklenen = $"{Etiketler.Get("update.error")}: HttpRequestException: ag yok";
                return $"bildirim:{p.VersionToast.IsVisible}|metin:{metin == beklenen}|tek-satir:{!metin.Contains('\n')}|dusus:{UpdateHealth.Load(p.SettingsPathOverride).Failures}";
            }
            finally { p.Close(); }
        });

        Assert.Equal("bildirim:True|metin:True|tek-satir:True|dusus:1", sonuc);
    }

    [Fact]
    public void SurumBildirimiSolUsttedirSurumDugmesininAltinda()
    {
        var sonuc = AppHost.Run(() =>
        {
            var p = Pencere();
            try
            {
                var t = p.VersionToast;
                return $"{t.HorizontalAlignment}/{t.VerticalAlignment}/{t.Margin}";
            }
            finally { p.Close(); }
        });

        Assert.Equal("Left/Top/16,36,0,0", sonuc);
    }

    [Fact]
    public void YeniSurumVarOlanGuncellemeYolunaDevredilir()
    {
        var sonuc = AppHost.Run(() =>
        {
            var satirlar = new List<string>();
            foreach (var otomatik in new[] { true, false })
            {
                var p = Pencere();
                try
                {
                    var sessiz = 0;
                    p.OtomatikGuncellemeAcik = () => otomatik;
                    p.SessizIndirici = () => sessiz++;
                    p.ManifestGetir = _ => Task.FromResult<string?>(Manifest("999.0.0"));
                    Tikla(p.BtnVersion);
                    Bekle(p.SurumDenetimIsi!);
                    satirlar.Add($"otomatik:{otomatik}|sessiz:{sessiz}|serit:{p.UpdateNotice.IsVisible}|surum:{p.TxtNoticeVersion.Text}|bildirim:{p.VersionToast.IsVisible}");
                }
                finally { p.Close(); }
            }
            return satirlar;
        });

        Assert.Equal(new[]
        {
            "otomatik:True|sessiz:1|serit:False|surum:|bildirim:False",
            "otomatik:False|sessiz:0|serit:True|surum:999.0.0|bildirim:False"
        }, sonuc);
    }
}
