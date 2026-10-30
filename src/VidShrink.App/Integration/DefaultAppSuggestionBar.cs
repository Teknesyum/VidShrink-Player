using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using VidShrink.App.Localization;
using VidShrink.Core;
using VidShrink.Core.Setup;

namespace VidShrink.App.Integration;

/// <summary>
/// "VidShrink varsayılan değil" önerisi. Bir cümle ve üç düğme taşır: biri sahip
/// olunabilecek tüm ses+video uzantılarını tek komutla atayan yönlendirmeyi panoya
/// kopyalar, biri Windows'un varsayılan uygulamalar sayfasını açar, biri öneriyi kalıcı
/// olarak kapatır.
///
/// <para>"Tümünü ata" düğmesi VidShrink içinden PowerShell çalıştırmaz, dosya indirmez:
/// bütünlüğü sabitlenmiş komutu panoya yazar, kullanıcı kendi kabuğunda çalıştırır. Böylece
/// imzasız exe hiçbir davranışsal bayrağa dokunmaz (<c>docs/olcumler/varsayilan-atama-av.md</c>).</para>
///
/// <para>Renk ve ölçü <c>Themes/Theme.axaml</c> belirteçlerinden dinamik kaynak olarak
/// alınır; şeridin kendi sayısı yoktur. Metin dil dosyasından gelir ve dil değişince
/// kendiliğinden yenilenir.</para>
/// </summary>
internal sealed class DefaultAppSuggestionBar : UserControl
{
    private const string MessageKey = "settings.default-app.suggestion";
    private const string AllKey = "settings.default-app.all";
    private const string AllCopiedKey = "settings.default-app.all-copied";
    private const string OpenKey = "settings.default-app.open";
    private const string DismissKey = "settings.default-app.dismiss";

    private readonly string? _settingsPath;
    private readonly TextBlock _message;

    internal DefaultAppSuggestionBar(string? settingsPath = null)
    {
        _settingsPath = settingsPath;

        _message = new SarilanMetin { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _message.Bind(TextBlock.TextProperty, Text(MessageKey));
        _message.Bind(TextBlock.ForegroundProperty, Token("TextBody"));
        _message.Bind(TextBlock.FontSizeProperty, Token("FontSizeSm"));

        var all = Action(AllKey);
        all.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            var extensions = ShellIntegration.BulkDefaultExtensions;
            if (OperatingSystem.IsWindows() && Environment.ProcessPath is { Length: > 0 } executable)
            {
                var missing = DefaultApp.NotOurs(executable, extensions);
                if (missing.Count > 0) extensions = missing;
            }
            var command = BulkAssociationCommand.Build(FileAssociation.ProgId, extensions);
            await clipboard.SetTextAsync(command).ConfigureAwait(true);
            _message.Bind(TextBlock.TextProperty, Text(AllCopiedKey));
        };

        var open = Action(OpenKey);
        open.Click += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) DefaultApp.OpenSettings();
        };

        var dismiss = Action(DismissKey);
        dismiss.Click += (_, _) =>
        {
            DefaultAppSuggestion.Dismiss(_settingsPath);
            IsVisible = false;
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Bind(StackPanel.SpacingProperty, Token("SpaceSm"));
        actions.Children.Add(all);
        actions.Children.Add(open);
        actions.Children.Add(dismiss);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(_message);

        var frame = new Border { Child = row };
        frame.Bind(Border.BackgroundProperty, Token("PanelSurface"));
        frame.Bind(Border.BorderBrushProperty, Token("NeonBlueBorder"));
        frame.Bind(Border.BorderThicknessProperty, Token("BorderThin"));
        frame.Bind(Border.CornerRadiusProperty, Token("RadiusControl"));
        frame.Bind(Border.PaddingProperty, Token("ButtonPaddingSm"));

        Content = frame;
        Bind(MarginProperty, Token("NoticeMargin"));
    }

    /// <summary>
    /// Şerit görünür mü. Kararı <see cref="DefaultAppSuggestion.ShouldShow"/> verir;
    /// buradaki iş yalnız üç girdiyi toplamaktır.
    /// </summary>
    internal static bool Wanted(string executablePath, IReadOnlyList<string> extensions, string? settingsPath = null)
        => DefaultAppSuggestion.ShouldShow(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsWindows() && DefaultApp.IsDefault(executablePath, extensions),
            DefaultAppSuggestion.Dismissed(settingsPath));

    private static Button Action(string key)
    {
        var button = new Button { VerticalAlignment = VerticalAlignment.Center };
        button.Bind(StyledElement.ThemeProperty, Token("GhostButton"));
        button.Bind(ContentControl.ContentProperty, Text(key));
        button.Bind(TemplatedControl.PaddingProperty, Token("ButtonPaddingSm"));
        button.Bind(TemplatedControl.FontSizeProperty, Token("FontSizeSm"));
        return button;
    }

    private static BindingBase Text(string key) => LocalizedText.For(key).Binding;

    private static DynamicResourceExtension Token(string key) => new(key);
}
