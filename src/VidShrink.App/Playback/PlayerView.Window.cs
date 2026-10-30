using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VidShrink.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidShrink.App.Localization;
using VidShrink.Player;

namespace VidShrink.App.Playback;

internal partial class PlayerView
{
    internal static readonly IReadOnlyList<string> AspectRatios = new[] { "-1", "16:9", "4:3", "2.35:1" };

    private readonly int _shuffleSeed = Environment.TickCount;
    private PlayerSettings _settings = new();
    private RecentFiles _recent = new();
    private Favorites _favorites = new();
    private string? _settingsFolder;
    private bool _settingsRead;
    private bool _topmost;
    private bool _infoVisible;
    private int _aspectIndex;
    private int _rotation;
    private bool _mirrored;
    private bool _seekDragging;
    private string? _notice;
    private IReadOnlyList<double> _chapters = Array.Empty<double>();
    private IReadOnlyList<string> _chapterTitles = Array.Empty<string>();
    private PlayerSettings? _soundRestoredFrom;
    private const double ChapterSeekThreshold = 5;
    private IReadOnlyList<SeekMark> _marks = Array.Empty<SeekMark>();
    private string _marksKey = "";
    private Task<string?> _lastShot = Task.FromResult<string?>(null);
    private Task<bool> _lastCopy = Task.FromResult(false);
    private Bitmap? _copiedFrame;
    private Task _navigation = Task.CompletedTask;

    internal PlayerSettings Settings
    {
        get
        {
            EnsureSettings();
            return _settings;
        }
    }

    internal RecentFiles Recent
    {
        get
        {
            EnsureSettings();
            return _recent;
        }
    }

    internal Favorites FavoriteItems
    {
        get
        {
            EnsureSettings();
            return _favorites;
        }
    }

    internal bool IsTopmost => _topmost;

    internal bool InfoVisible => _infoVisible;

    internal string AspectRatio => AspectRatios[_aspectIndex];

    internal int RotationDegrees => _rotation;

    internal bool IsMirrored => _mirrored;

    internal IReadOnlyList<double> Chapters => _chapters;

    internal IReadOnlyList<SeekMark> Marks => _marks;

    internal Task<string?> LastScreenshot => _lastShot;

    internal Task<bool> LastFrameCopy => _lastCopy;

    internal Func<TopLevel?, Bitmap, Task<bool>> FrameCopier { get; set; } = CopyToClipboard;

    internal string? FrameScratchFolder { get; set; }

    internal Task Navigation => _navigation;

    internal string InfoText => TxtInfo.Text ?? "";

    internal string ViewStateText => TxtView.Text ?? "";

    internal void SaveSettings() => _settings.Save(SettingsFile());

    /// <summary>
    /// Ses düzeyi ve sessiz durumu ayar nesnesi başına bir kez ayardan alınır; dosya açılmadan basılan
    /// ses tuşu da kayıtlı düzeyin üstüne çalışır, varsayılan 100'ün üstüne değil.
    /// </summary>
    private void RestoreSound()
    {
        var settings = Settings;
        if (ReferenceEquals(settings, _soundRestoredFrom)) return;
        _soundRestoredFrom = settings;
        var volume = Math.Clamp(settings.Volume, 0, VolumeCeiling());
        if (volume != _volume)
        {
            _volume = volume;
            _engine?.SetVolume(_volume);
        }

        if (settings.Muted == _muted) return;
        _muted = settings.Muted;
        _engine?.SetMuted(_muted);
    }

    private void RememberSound()
    {
        var settings = Settings;
        if (settings.Volume == _volume && settings.Muted == _muted) return;
        settings.Volume = _volume;
        settings.Muted = _muted;
        SaveSettings();
    }

    internal static int ChapterTarget(IReadOnlyList<double> chapters, double position, bool backward)
    {
        if (chapters.Count == 0) return -1;
        var current = -1;
        for (var i = 0; i < chapters.Count; i++)
            if (chapters[i] <= position) current = i;
        if (!backward) return Math.Min(current + 1, chapters.Count - 1);
        if (current >= 0 && position - chapters[current] > ChapterSeekThreshold) return current;
        return Math.Max(current - 1, 0);
    }

    private string ChapterName(int index)
    {
        var name = Strings.Get("main.player.osd.chapter", index + 1);
        var title = index < _chapterTitles.Count ? _chapterTitles[index] : "";
        return string.IsNullOrWhiteSpace(title) ? name : name + " · " + title;
    }

    private string? ChapterTitleAt(double seconds)
    {
        var index = -1;
        for (var i = 0; i < _chapters.Count; i++)
            if (_chapters[i] <= seconds) index = i;
        if (index < 0 || index >= _chapterTitles.Count) return null;
        return string.IsNullOrWhiteSpace(_chapterTitles[index]) ? null : _chapterTitles[index];
    }

    internal string SeekLabel(double seconds)
    {
        var time = Saat.Ekran(TimeSpan.FromSeconds(Math.Max(0, seconds)), TimeSpan.FromHours(1));
        return ChapterTitleAt(seconds) is { } title ? time + " · " + title : time;
    }

    private MenuItem ChapterMenu()
    {
        var menu = new MenuItem { Header = Strings.Get("main.player.menu.chapters"), IsEnabled = _chapters.Count > 0 };
        for (var i = 0; i < _chapters.Count; i++)
        {
            var index = i;
            var item = new MenuItem { Header = ChapterName(index) };
            item.Click += (_, _) => SeekToChapter(index);
            menu.Items.Add(item);
        }

        return menu;
    }

    internal void SeekToChapter(int index)
    {
        if (_engine is null || index < 0 || index >= _chapters.Count) return;
        _trackPaused = false;
        KullaniciIslemi();
        _seek.GoTo(_chapters[index]);
        _trace.Add("chapter -> " + index.ToString(CultureInfo.InvariantCulture));
        ShowChapterOsd(index);
        RefreshState();
    }

    private void InitWindow()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DropEvent, OnFileDrop);
        SeekBar.AddHandler(PointerPressedEvent, OnSeekPressed);
        SeekBar.AddHandler(PointerMovedEvent, OnSeekMoved);
        SeekBar.AddHandler(PointerReleasedEvent, OnSeekReleased);
        SeekBar.PointerCaptureLost += (_, _) =>
        {
            if (!_seekDragging) return;
            _seekDragging = false;
            HoldSerit();
        };
        SeekBar.SizeChanged += (_, _) =>
        {
            _marksKey = "";
            RefreshSeekBar();
        };
    }

    private bool ApplyWindow(PlayerCommand command)
    {
        switch (command.Kind)
        {
            case PlayerCommandKind.ToggleTopmost:
                _topmost = !_topmost;
                if (TopLevel.GetTopLevel(this) is Window window) window.Topmost = _topmost;
                _trace.Add("topmost -> " + _topmost);
                return true;
            case PlayerCommandKind.AspectCycle:
                _aspectIndex = (_aspectIndex + 1) % AspectRatios.Count;
                _engine?.SetAspectOverride(AspectRatios[_aspectIndex]);
                _trace.Add("aspect -> " + AspectRatios[_aspectIndex]);
                return true;
            case PlayerCommandKind.Rotate:
                _rotation = (((_rotation + (int)command.Amount) % 360) + 360) % 360;
                _engine?.SetRotation(_rotation);
                _trace.Add("rotate -> " + _rotation.ToString(CultureInfo.InvariantCulture));
                return true;
            case PlayerCommandKind.Mirror:
                _mirrored = !_mirrored;
                _engine?.SetMirrored(_mirrored);
                _trace.Add("mirror -> " + _mirrored);
                return true;
            case PlayerCommandKind.ToggleInfo:
                _infoVisible = !_infoVisible;
                InfoPanel.IsVisible = _infoVisible;
                RefreshInfo();
                _trace.Add("info -> " + _infoVisible);
                return true;
            case PlayerCommandKind.Screenshot:
                _trace.Add("screenshot -> " + TakeScreenshot());
                return true;
            case PlayerCommandKind.CopyFrame:
                _trace.Add("copyframe -> " + CopyFrame());
                return true;
            case PlayerCommandKind.FileStep:
                _trace.Add("file " + command.Amount.ToString("0", CultureInfo.InvariantCulture) + " -> " + StepFile(command.Amount > 0));
                return true;
            case PlayerCommandKind.ToggleSilenceSkip:
                EnsureSettings();
                _settings.SilenceSkip = !_settings.SilenceSkip;
                SaveSettings();
                _engine?.SetSilenceSkip(_settings.Silence);
                _trace.Add("silenceskip -> " + _settings.SilenceSkip);
                return true;
            case PlayerCommandKind.ToggleShuffle:
                EnsureSettings();
                _settings.Shuffle = !_settings.Shuffle;
                SaveSettings();
                _trace.Add("shuffle -> " + _settings.Shuffle);
                SyncMediaNeighbors();
                return true;
            case PlayerCommandKind.RepeatCycle:
                EnsureSettings();
                _settings.Repeat = _settings.Repeat switch
                {
                    RepeatMode.Off => RepeatMode.All,
                    RepeatMode.All => RepeatMode.One,
                    _ => RepeatMode.Off
                };
                _engine?.SetRepeatFile(_settings.Repeat == RepeatMode.One);
                SaveSettings();
                _trace.Add("repeat -> " + _settings.Repeat);
                SyncMediaNeighbors();
                return true;
            default:
                return false;
        }
    }

    private void AfterOpen(string path, IPlaybackEngine engine)
    {
        EnsureSettings();
        if (!IsAddress(path) && _settings.KeepHistory)
        {
            _recent.Add(path);
            _recent.Save(RecentFile());
        }
        _chapters = engine.ChapterTimes;
        _chapterTitles = engine.ChapterTitles;
        _marksKey = "";
        _aspectIndex = 0;
        _rotation = 0;
        _mirrored = false;
        _notice = null;
        if (_settings.Repeat == RepeatMode.One) engine.SetRepeatFile(true);
        SyncMediaNeighbors();
        RefreshInfo();
    }

    private void AfterEnd()
    {
        EnsureSettings();
        if (_settings.Repeat != RepeatMode.All && !_settings.Shuffle) return;
        _trace.Add("autonext -> " + StepFile(true));
    }

    /// <summary>
    /// Sondayken oynat: klasörde sonraki dosya varsa ona geçer ve true döner; yoksa başa
    /// sarar, oynatmayı çağıran sürdürür.
    /// </summary>
    private bool PlayFromEnd()
    {
        EnsureSettings();
        if (_path is { } path && StepFrom(path, true) is { } next)
        {
            _trace.Add("end -> " + Path.GetFileName(next));
            _navigation = OpenQuietlyAsync(next);
            return true;
        }

        _seek.GoTo(0);
        _trace.Add("end -> tostart");
        return false;
    }

    private string StepFile(bool forward)
    {
        EnsureSettings();
        if (_path is not { } path) return "no";
        var next = StepFrom(path, forward);
        if (next is null)
        {
            _notice = Strings.Get("player.list.end");
            return "no";
        }

        _notice = null;
        _navigation = OpenQuietlyAsync(next);
        return Path.GetFileName(next);
    }

    internal bool OpenDropped(string file)
    {
        if (!File.Exists(file)) return false;
        _trace.Add("drop -> " + Path.GetFileName(file));
        _navigation = OpenQuietlyAsync(file);
        return true;
    }

    internal async Task OpenQuietlyAsync(string path)
    {
        try
        {
            await OpenAsync(path).ConfigureAwait(true);
        }
        catch (PlaybackEngineUnavailableException ex) when (ex.MessageKey is not null)
        {
        }
        catch (Exception ex) when (ex is PlaybackOpenException or PlaybackEngineUnavailableException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            TxtStall.IsVisible = true;
            TxtStall.Text = LanguageCatalog.Display(Strings.Get(MpvEngine.FailedKey) + ": " + ex.Message);
        }
    }

    private string TakeScreenshot()
    {
        if (_engine is not { IsOpen: true } engine || _path is not { } media) return "no";
        EnsureSettings();
        var target = _settings.ScreenshotPath(media, CurrentPosition());
        _lastShot = SaveScreenshotAsync(engine, target, _settings.ScreenshotSubtitles);
        return target;
    }

    private async Task<string?> SaveScreenshotAsync(IPlaybackEngine engine, string target, bool withSubtitles)
    {
        var saved = false;
        try
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            saved = await engine.SaveScreenshotAsync(target, withSubtitles).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
        }

        _notice = saved ? Strings.Get("player.view.screenshot-saved", target) : Strings.Get("player.view.screenshot-failed");
        ShowOsd(saved ? Strings.Get("player.view.screenshot-saved", Path.GetFileName(target)) : _notice);
        RefreshState();
        return saved ? target : null;
    }

    private string CopyFrame()
    {
        if (_engine is not { IsOpen: true } engine || _path is null) return "no";
        _lastCopy = CopyFrameAsync(engine);
        return "ok";
    }

    private async Task<bool> CopyFrameAsync(IPlaybackEngine engine)
    {
        var copied = false;
        var folder = FrameScratchFolder ?? Path.Combine(Path.GetTempPath(), AppIdentity.FolderName);
        var scratch = Path.Combine(folder, "frame-" + Guid.NewGuid().ToString("N") + PlayerSettings.PngExtension);
        try
        {
            Directory.CreateDirectory(folder);
            if (await engine.SaveScreenshotAsync(scratch).ConfigureAwait(true))
            {
                Bitmap frame;
                using (var stream = File.OpenRead(scratch)) frame = new Bitmap(stream);
                _copiedFrame?.Dispose();
                _copiedFrame = frame;
                copied = await FrameCopier(TopLevel.GetTopLevel(this), frame).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException or ArgumentException or TimeoutException or ExternalException)
        {
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        _notice = Strings.Get(copied ? "player.view.screenshot-copied" : "player.view.screenshot-copy-failed");
        ShowOsd(_notice);
        RefreshState();
        return copied;
    }

    private static async Task<bool> CopyToClipboard(TopLevel? top, Bitmap frame)
    {
        if (top?.Clipboard is not { } clipboard) return false;
        await clipboard.SetBitmapAsync(frame).ConfigureAwait(true);
        return true;
    }

    internal void ToggleScreenshotJpg()
    {
        EnsureSettings();
        _settings.ScreenshotFormat = _settings.ScreenshotFormat == ScreenshotFormat.Jpg ? ScreenshotFormat.Png : ScreenshotFormat.Jpg;
        SaveSettings();
        _trace.Add("screenshotformat -> " + _settings.ScreenshotFormat);
        AdvancedChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ToggleScreenshotSubtitles()
    {
        EnsureSettings();
        _settings.ScreenshotSubtitles = !_settings.ScreenshotSubtitles;
        SaveSettings();
        _trace.Add("screenshotsubtitles -> " + _settings.ScreenshotSubtitles);
        AdvancedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureSettings()
    {
        var folder = SettingsFolder();
        if (_settingsRead && string.Equals(folder, _settingsFolder, StringComparison.Ordinal)) return;
        _settingsRead = true;
        _settingsFolder = folder;
        _settings = PlayerSettings.Load(SettingsFile());
        _recent = RecentFiles.Load(RecentFile());
        _favorites = Favorites.Load(FavoritesFile());
        if (folder is not null) Keymap.Steps = _settings.SeekSteps;
    }

    private string? SettingsFolder()
    {
        var history = HistoryPath?.Invoke();
        return string.IsNullOrEmpty(history) ? null : Path.GetDirectoryName(Path.GetFullPath(history));
    }

    private string? SettingsFile() => SettingsFolder() is { } folder ? Path.Combine(folder, PlayerSettings.FileName) : null;

    private string? RecentFile() => SettingsFolder() is { } folder ? Path.Combine(folder, RecentFiles.FileName) : null;

    private string? FavoritesFile() => SettingsFolder() is { } folder ? Path.Combine(folder, Favorites.FileName) : null;

    private bool? ToggleState(PlayerAction action)
    {
        EnsureSettings();
        return action.Command switch
        {
            PlayerCommandKind.ToggleTopmost => _topmost,
            PlayerCommandKind.Mirror => _mirrored,
            PlayerCommandKind.ToggleInfo => _infoVisible,
            PlayerCommandKind.ToggleShuffle => _settings.Shuffle,
            PlayerCommandKind.ToggleSilenceSkip => _settings.SilenceSkip,
            _ => null
        };
    }

    private MenuItem RecentMenu()
    {
        EnsureSettings();
        var recent = new MenuItem { Header = Strings.Get("player.list.recent") };
        var files = _recent.Items;
        if (files.Count == 0) recent.Items.Add(new MenuItem { Header = Strings.Get("player.list.recent-empty"), IsEnabled = false });
        foreach (var path in files)
        {
            var entry = new MenuItem { Header = new SarilanMetin { Text = Path.GetFileName(path) }, Tag = path };
            ToolTip.SetTip(entry, path);
            entry.Click += OnRecentRow;
            recent.Items.Add(entry);
        }

        recent.Items.Add(new Separator());
        var history = new MenuItem { Header = Strings.Get("player.list.history") };
        history.Click += (_, _) => ShowHistory();
        recent.Items.Add(history);
        var clear = new MenuItem { Header = Strings.Get("player.list.clear-history") };
        clear.Click += (_, _) => ClearHistory();
        recent.Items.Add(clear);
        return recent;
    }

    /// <summary>OYB-16: sık kullanılanlar alt menüsü; açık dosyayı ya da klasörünü ekler, listede varsa çıkarır.</summary>
    internal MenuItem FavoritesMenu()
    {
        EnsureSettings();
        var menu = new MenuItem { Header = Strings.Get("player.favorites.title") };
        var items = _favorites.Items;
        if (items.Count == 0) menu.Items.Add(new MenuItem { Header = Strings.Get("player.favorites.empty"), IsEnabled = false });
        foreach (var item in items)
        {
            var row = new MenuItem { Header = new SarilanMetin { Text = FavoriteName(item) }, Tag = item };
            ToolTip.SetTip(row, item);
            row.Click += (_, _) => OpenFavorite(item);
            menu.Items.Add(row);
        }

        menu.Items.Add(new Separator());
        var file = _path;
        var fileOn = file is not null && _favorites.Contains(file);
        var fileRow = new MenuItem
        {
            Header = Strings.Get(fileOn ? "player.favorites.remove-file" : "player.favorites.add-file"),
            IsEnabled = file is not null
        };
        fileRow.Click += (_, _) => ToggleFavorite(file);
        menu.Items.Add(fileRow);

        var folder = file is not null && !IsAddress(file) ? Path.GetDirectoryName(Path.GetFullPath(file)) : null;
        var folderOn = folder is not null && _favorites.Contains(folder);
        var folderRow = new MenuItem
        {
            Header = Strings.Get(folderOn ? "player.favorites.remove-folder" : "player.favorites.add-folder"),
            IsEnabled = folder is not null
        };
        folderRow.Click += (_, _) => ToggleFavorite(folder);
        menu.Items.Add(folderRow);
        return menu;
    }

    private static string FavoriteName(string item)
    {
        if (IsAddress(item)) return MediaAddress.Name(item);
        var name = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length == 0 ? item : name;
    }

    /// <summary>Listede yoksa ekler, varsa çıkarır; kayıt hemen diske yazılır.</summary>
    internal bool ToggleFavorite(string? item)
    {
        EnsureSettings();
        if (Favorites.Normalize(item) is not { } key) return false;
        var added = !_favorites.Contains(key);
        if (added) _favorites.Add(key);
        else _favorites.Remove(key);
        _favorites.Save(FavoritesFile());
        _trace.Add("favorite -> " + (added ? "add " : "remove ") + Favorites.KindOf(key));
        return added;
    }

    internal bool OpenFavorite(string item)
    {
        EnsureSettings();
        switch (Favorites.KindOf(item))
        {
            case FavoriteKind.Address:
                return OpenAddress(item);
            case FavoriteKind.Folder:
                return OpenFolder(item);
        }

        if (!File.Exists(item))
        {
            _notice = Strings.Get("player.list.missing");
            RefreshState();
            return false;
        }

        _navigation = OpenQuietlyAsync(item);
        return true;
    }

    private void OnRecentRow(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path }) OpenRecent(path);
    }

    internal bool OpenRecent(string path)
    {
        EnsureSettings();
        if (!File.Exists(path))
        {
            _recent.Remove(path);
            _recent.Save(RecentFile());
            _notice = Strings.Get("player.list.missing");
            RefreshState();
            return false;
        }

        _navigation = OpenQuietlyAsync(path);
        return true;
    }

    private async void OnPickScreenshotFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;
        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = Strings.Get("player.view.screenshot-folder")
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } local) return;
            EnsureSettings();
            _settings.ScreenshotFolder = local;
            SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private async void OnPickFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage) return;
        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = Strings.Get("player.menu.open-folder")
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } local) return;
            OpenFolder(local);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DroppedFile(e) is not null || DroppedItems(e) is not null || DroppedAddress(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    internal Action<string>? FileDropped { get; set; }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        var file = DroppedFile(e);
        e.Handled = true;
        if (file is null)
        {
            if (FileDropped is null && DroppedItems(e) is { } items) OpenDroppedItems(items);
            else if (DroppedAddress(e) is { } address) OpenDroppedAddress(address);
            return;
        }

        if (FileDropped is { } hook) hook(file);
        else OpenDropped(file);
    }

    internal bool OpenDroppedAddress(string? text)
    {
        if (MediaAddress.FromText(text) is not { } address) return false;
        _trace.Add("drop -> address");
        return OpenAddress(address);
    }

    private static string? DroppedAddress(DragEventArgs e) => MediaAddress.FromText(e.DataTransfer.TryGetText());

    private static IReadOnlyList<string>? DroppedItems(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList();
        if (items is null || items.Count == 0) return null;
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path is not null && (Directory.Exists(path) || File.Exists(path))) paths.Add(path);
        }

        return paths.Count > 0 && (paths.Count > 1 || Directory.Exists(paths[0])) ? paths : null;
    }

    private static string? DroppedFile(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles()?.ToList();
        if (items is null || items.Count != 1 || items[0] is IStorageFolder) return null;
        var path = items[0].TryGetLocalPath();
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Basis denetim seridinden mi geliyor. Serit panonun ustunde duruyor ve panonun
    /// kendi tiklamasi oynatmayi duraklatiyor; seritteki dugmeye basmak o yolu
    /// tetiklemesin diye seridin butun icerigi ayriliyor. Zaman cubugu de seridin
    /// icinde, yine de adiyla ayrica soruluyor: cubuk seritten cikarsa kural kalir. Bilgi
    /// panosundaki "tum ayrintilar" dugmesi de ayni nedenle ayrilir.
    /// </summary>
    private bool IsSeekBarSource(object? source)
        => source is Visual visual
           && (ReferenceEquals(visual, SeekBar) || SeekBar.IsVisualAncestorOf(visual)
               || ReferenceEquals(visual, StripBar) || StripBar.IsVisualAncestorOf(visual)
               || ReferenceEquals(visual, BtnInfoDetails) || BtnInfoDetails.IsVisualAncestorOf(visual));

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SeekBar).Properties.IsLeftButtonPressed) return;
        _seekDragging = true;
        e.Pointer.Capture(SeekBar);
        HoldSerit();
        SeekToPointer(e.GetPosition(SeekBar).X);
        e.Handled = true;
    }

    private void OnSeekMoved(object? sender, PointerEventArgs e)
    {
        if (!_seekDragging) return;
        SeekToPointer(e.GetPosition(SeekBar).X);
        e.Handled = true;
    }

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_seekDragging) return;
        _seekDragging = false;
        e.Pointer.Capture(null);
        HoldSerit();
        HideThumbnail();
        e.Handled = true;
    }

    internal void SeekToPointer(double x)
    {
        var duration = _seek.Duration;
        if (_engine is null || !double.IsFinite(duration) || duration <= 0) return;
        var at = SeekMarks.SecondsAt(x, SeekBar.Bounds.Width, duration);
        _trackPaused = false;
        KullaniciIslemi();
        _seekSmooth.Reset();
        _seek.GoTo(at);
        _trace.Add("seekbar -> " + at.ToString("0.###", CultureInfo.InvariantCulture));
        RefreshState();
    }

    private void RefreshWindowState()
    {
        if (TxtView is null) return;
        RefreshSeekBar();
        EnsureSettings();

        var parts = new List<string>();
        if (_aspectIndex > 0) parts.Add(Strings.Get("player.view.state-aspect", AspectRatios[_aspectIndex]));
        if (_rotation != 0) parts.Add(Strings.Get("player.view.state-rotate", _rotation));
        if (_mirrored) parts.Add(Strings.Get("player.view.state-mirror"));
        if (_topmost) parts.Add(Strings.Get("player.view.state-topmost"));
        if (_settings.Repeat == RepeatMode.All) parts.Add(Strings.Get("player.list.repeat-all"));
        if (_settings.Repeat == RepeatMode.One) parts.Add(Strings.Get("player.list.repeat-one"));
        if (_settings.Shuffle) parts.Add(Strings.Get("player.list.state-shuffle"));
        if (_settings.SilenceSkip) parts.Add(Strings.Get("player.view.state-silenceskip"));
        if (_notice is not null) parts.Add(_notice);

        TxtView.Text = LanguageCatalog.Display(string.Join(" - ", parts));
        TxtView.IsVisible = parts.Count > 0;
        RefreshSerit();
    }

    private void RefreshSeekBar()
    {
        if (SeekBar is null) return;
        var duration = _engine is null ? 0 : _seek.Duration;
        var known = double.IsFinite(duration) && duration > 0;
        var width = SeekBar.Bounds.Width;
        SeekTrack.Classes.Set("unknown", !known);
        SeekFill.IsVisible = known;
        SeekHead.IsVisible = known;
        if (known) PlaceSeekHead(duration, width);

        var bookmarks = _path is null ? Array.Empty<double>() : _history.Bookmarks(_path);
        var key = string.Join(",", _chapters) + "|" + string.Join(",", bookmarks) + "|"
            + width.ToString("0.#", CultureInfo.InvariantCulture) + "|" + duration.ToString("0.###", CultureInfo.InvariantCulture);
        if (key == _marksKey) return;
        _marksKey = key;
        _marks = known ? SeekMarks.Build(_chapters, bookmarks, duration) : Array.Empty<SeekMark>();
        LayoutMarks(width, duration);
    }

    private readonly SeekSmoother _seekSmooth = new();
    private DispatcherTimer? _shimmerClock;
    private long _shimmerAt;

    private void PlaceSeekHead(double duration, double width)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        var at = _seekSmooth.Update(_seek.Target, now, _playing && !_seekDragging, _speed);
        var offset = SeekMarks.Offset(at, duration, width);
        SeekFill.Width = offset;
        SeekSpan.Width = width;
        var half = SeekHead.Width / 2;
        SeekHead.Margin = new Thickness(Math.Max(0, offset - half), 0, 0, 0);
        SeekShimmer.Margin = new Thickness(Math.Max(0, offset - SeekShimmer.Width), 0, 0, 0);
    }

    private void TickSeekHead()
    {
        if (SeekBar is null || _engine is null) return;
        var duration = _seek.Duration;
        if (!double.IsFinite(duration) || duration <= 0) return;
        PlaceSeekHead(duration, SeekBar.Bounds.Width);
        PulseShimmer();
    }

    private void PulseShimmer()
    {
        var now = Environment.TickCount64;
        if (now - _shimmerAt < 14000) return;
        _shimmerAt = now;
        SeekShimmer.IsVisible = true;
        SeekShimmer.Classes.Set("run", false);
        SeekShimmer.Classes.Set("run", true);
        _shimmerClock ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _shimmerClock.Tick -= EndShimmer;
        _shimmerClock.Tick += EndShimmer;
        _shimmerClock.Start();
    }

    private void EndShimmer(object? sender, EventArgs e)
    {
        _shimmerClock?.Stop();
        SeekShimmer.Classes.Set("run", false);
        SeekShimmer.IsVisible = false;
    }

    private void LayoutMarks(double width, double duration)
    {
        SeekMarkLayer.Children.Clear();
        if (_marks.Count == 0 || width <= 0) return;
        var markWidth = Resource("PlaybackCursorWidth");
        var markHeight = Resource("PlaybackSeekBarHeight");
        var chapterBrush = this.TryFindResource("TextBody", out var body) ? body as IBrush : null;
        var bookmarkBrush = this.TryFindResource("NeonPink", out var pink) ? pink as IBrush : null;

        foreach (var mark in _marks)
        {
            var tick = new Border
            {
                Width = markWidth,
                Height = markHeight,
                Background = mark.Chapter ? chapterBrush : bookmarkBrush,
                IsHitTestVisible = false,
                Tag = mark
            };
            Canvas.SetLeft(tick, SeekMarks.Offset(mark.Seconds, duration, width) - markWidth / 2);
            Canvas.SetTop(tick, 0);
            SeekMarkLayer.Children.Add(tick);
        }
    }

    private double Resource(string key)
        => this.TryFindResource(key, out var value) && value is double number ? number : 0;

    private void RefreshInfo()
    {
        if (TxtInfo is null || !_infoVisible) return;
        BtnInfoDetails.IsVisible = DetailsPath is not null;
        var text = Describe(_engine?.Details, _engine?.Stats);
        if (TxtInfo.Text != text) TxtInfo.Text = text;
    }

    internal static string Describe(MediaDetails? details, PlaybackStats? stats = null)
    {
        if (details is null) return Strings.Get("player.info.none");
        var unknown = Strings.Get("player.info.unknown");
        var size = details.Width > 0 && details.Height > 0
            ? Bicim.Cozunurluk(details.Width, details.Height)
            : unknown;
        var fps = double.IsFinite(details.FramesPerSecond) && details.FramesPerSecond > 0
            ? Bicim.Kare(details.FramesPerSecond, Strings.Culture)
            : unknown;
        var rate = double.IsFinite(details.BitsPerSecond) && details.BitsPerSecond > 0
            ? Strings.BitHizi(Bicim.BitHizi.BpsToKbps((long)Math.Round(details.BitsPerSecond)))
            : unknown;

        var lines = new List<string>
        {
            Strings.Get("player.info.codec", details.VideoCodec ?? unknown),
            Strings.Get("player.info.resolution", size),
            Strings.Get("player.info.framerate", fps),
            Strings.Get("player.info.bitrate", rate),
            Strings.Get("player.info.range", details.DynamicRange ?? unknown),
            Strings.Get("player.info.color", details.ColorParts.Count > 0 ? string.Join(", ", details.ColorParts) : unknown),
            details.AudioCodec is null
                ? Strings.Get("player.info.noaudio")
                : Strings.Get("player.info.audio", details.AudioCodec, details.AudioChannels, details.AudioSampleRate)
        };
        if (stats is not null) lines.AddRange(DescribeLive(stats, unknown));
        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> DescribeLive(PlaybackStats stats, string unknown)
    {
        var decoder = !stats.DecoderKnown
            ? unknown
            : stats.HardwareDecoded
                ? Strings.Get("player.info.hardware", stats.Decoder)
                : Strings.Get("player.info.software");
        var rate = double.IsFinite(stats.VideoBitsPerSecond) && stats.VideoBitsPerSecond > 0
            ? Strings.BitHizi(Bicim.BitHizi.BpsToKbps((long)Math.Round(stats.VideoBitsPerSecond)))
            : unknown;
        var fps = double.IsFinite(stats.DisplayedFramesPerSecond) && stats.DisplayedFramesPerSecond > 0
            ? Bicim.Kare(stats.DisplayedFramesPerSecond, Strings.Culture)
            : unknown;

        yield return Strings.Get("player.info.decoder", decoder);
        yield return Strings.Get("player.info.dropped", Math.Max(0, stats.DroppedFrames).ToString(Strings.Culture));
        yield return Strings.Get("player.info.decoderdropped", Math.Max(0, stats.DecoderDroppedFrames).ToString(Strings.Culture));
        yield return Strings.Get("player.info.livebitrate", rate);
        yield return Strings.Get("player.info.livefps", fps);
    }
}
