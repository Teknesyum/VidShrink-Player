using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace VidShrink.App.Integration;

/// <summary>
/// Bir uzantıyı bugün hangi programın açtığını okur ve kullanıcıyı Windows'un kendi
/// varsayılan uygulama sayfasına götürür.
///
/// <para>Okuma kayıt defterinden değil <c>AssocQueryString</c>'ten yapılır: kullanıcının
/// seçimini taşıyan anahtar imzalı bir karma tutar, işletim sistemi onu kendi çözer ve
/// aynı cevabı bu çağrıdan verir. Yazma tarafı hiç yoktur; bu sınıf seçime dokunmaz.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DefaultApp
{
    /// <summary>Windows'un varsayılan uygulamalar sayfasının adresi.</summary>
    internal const string SettingsPage = "ms-settings:defaultapps";

    /// <summary>
    /// Kurucunun <c>HKCU\Software\RegisteredApplications</c> altına yazdığı ad
    /// (<c>Install-VidShrink.ps1</c>, <c>$fileAssociationName</c>). Windows'un derin
    /// bağlantısı bu adla VidShrink'in kendi sayfasını açar.
    /// </summary>
    internal const string RegisteredName = Core.Setup.ShellRegistration.AssociationName;

    /// <summary>
    /// Kullanıcı başına kurulmuş uygulamanın sayfasını açan sorgu anahtarı. Windows 11
    /// 21H2 (2023-04 toplu güncellemesi) ve sonrasında tanınır; tanımayan sürüm sorguyu
    /// yok sayıp genel sayfayı açar, yani eski Windows'ta davranış bugünküyle aynı kalır.
    /// Belge: https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-default-apps-settings
    /// </summary>
    private const string UserScopedQuery = "registeredAppUser";

    private const int Executable = 2;

    private const int ProgIdString = 20;

    /// <summary>
    /// <paramref name="extension"/> uzantısını bugün açan programın tam yolu; cevap
    /// alınamazsa <c>null</c>. Uzantı noktasız verilir.
    /// </summary>
    internal static string? Handler(string extension)
    {
        var size = 1024u;
        var buffer = new StringBuilder((int)size);
        var result = AssocQueryStringW(0, Executable, "." + extension, null, buffer, ref size);
        return result == 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }

    internal static string? HandlerProgId(string extension)
    {
        var size = 256u;
        var buffer = new StringBuilder((int)size);
        var result = AssocQueryStringW(0, ProgIdString, "." + extension, null, buffer, ref size);
        return result == 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }

    internal static bool IsOurs(string? progId, string? handler, string executablePath, string launchTarget)
    {
        if (string.Equals(progId, FileAssociation.ProgId, StringComparison.OrdinalIgnoreCase)) return true;
        if (handler is null) return false;
        return string.Equals(handler, executablePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(handler, launchTarget, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verilen uzantıların tamamı VidShrink'in ProgID'sine ya da <paramref name="executablePath"/>'e gidiyorsa doğru;
    /// ProgID karşılaştırması geliştirme ve taşınabilir build'de de tutar. Tek uzantı başka programa gidiyorsa yanlış.
    /// </summary>
    internal static bool IsDefault(string executablePath, IReadOnlyList<string> extensions)
    {
        if (extensions.Count == 0) return false;
        var target = FileAssociation.LaunchTarget(executablePath);
        foreach (var extension in extensions)
            if (!IsOurs(HandlerProgId(extension), Handler(extension), executablePath, target)) return false;
        return true;
    }

    internal static IReadOnlyList<string> NotOurs(string executablePath, IReadOnlyList<string> extensions)
    {
        var target = FileAssociation.LaunchTarget(executablePath);
        return extensions
            .Where(extension => !IsOurs(HandlerProgId(extension), Handler(extension), executablePath, target))
            .ToArray();
    }

    /// <summary>
    /// Varsayılan uygulamalar sayfasının VidShrink'in kendi girdisine inen adresi.
    /// Windows o sayfada yalnız bizim kaydettiğimiz uzantıları listeler ve hepsi video
    /// biçimidir; kullanıcı listeyi kendi süzmek zorunda kalmaz.
    /// </summary>
    internal static string SettingsPageForThisApp() =>
        $"{SettingsPage}?{UserScopedQuery}={Uri.EscapeDataString(RegisteredName)}";

    /// <summary>Ayar sayfasını açar. Açılamazsa yanlış döner, hata fırlatmaz.</summary>
    internal static bool OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(SettingsPageForThisApp()) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(int flags, int str, string association, string? extra,
        StringBuilder? output, ref uint size);
}
