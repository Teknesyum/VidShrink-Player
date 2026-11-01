using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VidShrink.App.Localization;
using VidShrink.App.Share;
using VidShrink.Core;
using VidShrink.Core.Editing;
using CoreShare = VidShrink.Core.Share;

namespace VidShrink.App.Recorder;

/// <summary>Son kayıtlar penceresinin dış dünyası; testler sahtesini verir.</summary>
internal sealed record RecordingHistorySources(
    Func<IReadOnlyList<RecordingEntry>> List,
    Func<string, CancellationToken, Task<double>> Duration,
    IThumbnailSource Thumbnails,
    Func<string, Task>? Open,
    Action<string> Reveal,
    Action<string> Trash,
    ShareSession Share);

/// <summary>
/// EKB-25: kaydedicinin son kayıtları. Satırda küçük resim, ad, süre, boyut ve tarih; seçili kayıt oynatıcıda açılır,
/// klasörde gösterilir, paylaşılır ya da çöpe taşınır. Çöpe taşıma kalıcı silmez; taşınamazsa dosya yerinde kalır ve
/// neden durum satırında yazar. Süre ve küçük resim satırlar göründükten sonra sırayla gelir.
/// </summary>
internal sealed partial class RecordingHistoryWindow : Window
{
    private readonly RecordingHistorySources _sources;
    private readonly Dictionary<string, (Image Thumb, SarilanMetin Detail)> _rows = new(StringComparer.Ordinal);
    private CancellationTokenSource _loading = new();

    public RecordingHistoryWindow() : this(new RecordingHistorySources(Array.Empty<RecordingEntry>, (_, _) => Task.FromResult(0d),
        new FfmpegThumbnailSource(() => VidShrink.Ffmpeg.ToolLocator.Ffmpeg), null, _ => { }, _ => { }, new ShareSession(() => null)))
    {
    }

    internal RecordingHistoryWindow(RecordingHistorySources sources)
    {
        _sources = sources;
        InitializeComponent();
        Rows.SelectionChanged += (_, _) => UpdateButtons();
        Rows.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        KeyDown += OnKey;
        BtnOpen.Click += async (_, _) => await OpenSelectedAsync();
        BtnReveal.Click += (_, _) => RevealSelected();
        BtnShare.Click += async (_, _) => await ShareSelectedAsync();
        BtnTrash.Click += (_, _) => TrashSelected();
        Refill();
    }

    internal Task Loading { get; private set; } = Task.CompletedTask;

    internal IEnumerable<string> ShownPaths => Rows.Items.OfType<ListBoxItem>().Select(item => (string)item.Tag!);

    internal string DetailOf(string path) => _rows.TryGetValue(path, out var row) ? row.Detail.Text ?? "" : "";

    internal bool HasThumbnail(string path) => _rows.TryGetValue(path, out var row) && row.Thumb.Source is not null;

    internal string StatusText => TxtStatus.IsVisible ? TxtStatus.Text ?? "" : "";

    internal string? Selected => (Rows.SelectedItem as ListBoxItem)?.Tag as string;

    internal void Select(string path)
        => Rows.SelectedItem = Rows.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag! == path);

    private void Refill(string? keep = null)
    {
        _loading.Cancel();
        _loading.Dispose();
        _loading = new CancellationTokenSource();
        foreach (var (thumb, _) in _rows.Values) (thumb.Source as IDisposable)?.Dispose();
        _rows.Clear();
        Rows.Items.Clear();

        var entries = _sources.List();
        foreach (var entry in entries) Rows.Items.Add(Row(entry));
        TxtEmpty.IsVisible = entries.Count == 0;
        Rows.SelectedIndex = entries.Count == 0 ? -1 : Math.Max(0, entries.ToList().FindIndex(entry => entry.Path == keep));
        UpdateButtons();
        Loading = LoadAsync(entries, _loading.Token);
    }

    private ListBoxItem Row(RecordingEntry entry)
    {
        var thumb = new Image { Width = Token("RecorderResultCardThumbWidth"), Height = Token("RecorderResultCardThumbHeight"), Stretch = Stretch.Uniform };
        var name = new SarilanMetin { Text = Path.GetFileName(entry.Path), TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new SarilanMetin { Text = Detail(entry, null), TextTrimming = TextTrimming.CharacterEllipsis };
        if (this.TryFindResource("Hint", out var hint) && hint is Avalonia.Styling.ControlTheme theme) detail.Theme = theme;
        var text = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children = { name, detail } };
        Grid.SetColumn(text, 1);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = Token("SpaceSm"), Children = { thumb, text } };
        _rows[entry.Path] = (thumb, detail);
        var item = new ListBoxItem { Tag = entry.Path, Content = content };
        ToolTip.SetTip(item, entry.Path);
        Avalonia.Automation.AutomationProperties.SetName(item, Path.GetFileName(entry.Path));
        return item;
    }

    private static string Detail(RecordingEntry entry, double? seconds)
    {
        var parts = new List<string>(3);
        if (seconds is > 0) parts.Add(Saat.Ekran(TimeSpan.FromSeconds(seconds.Value)));
        parts.Add(Bicim.Boyut.Bayt(entry.Bytes, Strings.Culture));
        parts.Add(Bicim.Damga(new DateTimeOffset(entry.WrittenUtc, TimeSpan.Zero), Strings.Culture));
        return string.Join(" · ", parts);
    }

    private async Task LoadAsync(IReadOnlyList<RecordingEntry> entries, CancellationToken ct)
    {
        var height = (int)Math.Max(1, Math.Round(Token("RecorderResultCardThumbHeight") * RenderScaling));
        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested) return;
            double? seconds = null;
            byte[]? image = null;
            try { seconds = await _sources.Duration(entry.Path, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            catch (OperationCanceledException) { return; }
            try { image = await _sources.Thumbnails.ReadAsync(entry.Path, 0, height, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested || !_rows.TryGetValue(entry.Path, out var row)) return;
            row.Detail.Text = Detail(entry, seconds);
            if (image is null) continue;
            try
            {
                using var stream = new MemoryStream(image);
                row.Thumb.Source = new Bitmap(stream);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { }
        }
    }

    private void UpdateButtons()
    {
        var any = Selected is not null;
        BtnOpen.IsEnabled = any && _sources.Open is not null;
        BtnReveal.IsEnabled = any;
        BtnShare.IsEnabled = any && !_sources.Share.Running;
        BtnTrash.IsEnabled = any;
    }

    internal async Task OpenSelectedAsync()
    {
        if (Selected is not { } path || _sources.Open is not { } open) return;
        if (!File.Exists(path))
        {
            ShowStatus(Say("recorder.history.missing", Path.GetFileName(path)), error: true);
            Refill();
            return;
        }

        await open(path);
        Close();
    }

    private void RevealSelected()
    {
        if (Selected is not { } path) return;
        try
        {
            _sources.Reveal(path);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus(Say("main.error.folder"), error: true);
        }
    }

    internal void TrashSelected()
    {
        if (Selected is not { } path) return;
        var name = Path.GetFileName(path);
        var index = Rows.SelectedIndex;
        try
        {
            _sources.Trash(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                       or OperationCanceledException or PlatformNotSupportedException)
        {
            ShowStatus(Say("recorder.history.trash-failed", name), error: true);
            return;
        }

        var sidecar = PointerTrack.SidecarOf(path);
        try { if (File.Exists(sidecar)) _sources.Trash(sidecar); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                       or OperationCanceledException or PlatformNotSupportedException) { }

        ShowStatus(Say("recorder.history.trashed", name), error: false);
        var next = ShownPaths.Where(shown => shown != path).Skip(Math.Max(0, index)).FirstOrDefault()
                   ?? ShownPaths.LastOrDefault(shown => shown != path);
        Refill(next);
    }

    internal async Task ShareSelectedAsync()
    {
        if (Selected is not { } path) return;
        var session = _sources.Share;
        if (session.Running) return;
        if (session.Target() is not { } target)
        {
            ShowStatus(ShareSession.MissingTargets(), error: true);
            return;
        }

        BtnShare.IsEnabled = false;
        ShareProgress.Value = 0;
        ShareProgress.IsVisible = true;
        ShowStatus(ShareSession.Uploading(), error: false);
        var progress = new Progress<CoreShare.UploadProgress>(step => ShareProgress.Value = step.Fraction);
        var result = await session.ShareAsync(target, path, progress);
        ShareProgress.IsVisible = false;
        var status = ShareSession.Status(result);
        ShowStatus(result.Ok && result.Link is { } link ? status + " " + link.Url : status, error: !result.Ok);
        UpdateButtons();
    }

    private void ShowStatus(string text, bool error)
    {
        TxtStatus.Text = text;
        TxtStatus.IsVisible = text.Length > 0;
        TxtStatus.Theme = error && this.TryFindResource("StatusError", out var found) && found is Avalonia.Styling.ControlTheme theme ? theme : null;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = OpenSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            TrashSelected();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _loading.Cancel();
        _sources.Share.Cancel();
        foreach (var (thumb, _) in _rows.Values) (thumb.Source as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    private double Token(string key) => this.TryFindResource(key, out var value) && value is double number ? number : 0;

    private static string Say(string key, params object?[] args)
        => args.Length == 0
            ? LanguageCatalog.Display(Strings.Get(key))
            : string.Format(Strings.Culture, LanguageCatalog.Display(Strings.Get(key)), args);
}
