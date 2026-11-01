using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VidShrink.App.Localization;
using VidShrink.App.Playback;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Player;

namespace VidShrink.App.Editing;

internal partial class EditorView
{
    internal const double MusicDefaultLevelDb = -12;

    private static readonly double[] MusicLevels = { -24, -18, -12, -6, 0, 6 };

    internal static readonly double[] DuckAmounts = { 6, 12, 18, 24 };

    internal static readonly (string Key, int Ms)[] DuckResponses =
    {
        ("editor.music.duck.fast", 150),
        ("editor.music.duck.medium", EditMusic.DefaultDuckReleaseMs),
        ("editor.music.duck.slow", 1000)
    };

    private readonly Stopwatch _duckClock = Stopwatch.StartNew();

    internal MusicPreview MusicPlayer { get; set; } = new();

    internal SoundClipsPreview SoundPlayer { get; set; } = new();

    internal PreviewDuck MusicDuck { get; } = new();

    internal double DuckKeyDb { get; private set; } = double.NaN;

    private void SyncMusic(EdlPreviewDriver driver)
    {
        var engine = Preview.Engine;
        var at = driver.TimelinePosition;
        var model = _model;
        var running = Preview.IsPlaying && driver.Playing && model is not null && at < model.Duration;
        var volume = engine?.Volume ?? 100;
        var muted = Preview.IsMuted;
        SoundPlayer.Sync(model?.AudioClips ?? Array.Empty<EditAudioClip>(), at, running && model!.Audible(EditTrack.Audio), driver.Rate, volume, muted, driver.Backward);
        var elapsed = _duckClock.Elapsed.TotalSeconds;
        _duckClock.Restart();
        DuckKeyDb = DuckKey(engine, driver, model);
        var gain = MusicDuck.Step(model?.Music, DuckKeyDb, elapsed);
        MusicPlayer.Sync(model?.Music, at, running && model!.Audible(EditTrack.Music), driver.Rate, volume, muted, driver.Backward, gain);
    }

    private void PauseSound()
    {
        MusicPlayer.Pause();
        SoundPlayer.Pause();
    }

    private double DuckKey(IPlaybackEngine? engine, EdlPreviewDriver driver, EditTimeline? model)
    {
        if (model is null) return double.NaN;
        var source = engine is { IsPaused: false } && model.Audible(EditTrack.Video) && !driver.Backward && driver.ReversingPart is null
            ? engine.AudioPeakDb
            : double.NaN;
        var narration = model.Audible(EditTrack.Audio) ? SoundPlayer.PeakDb : double.NaN;
        return Louder(source, narration);
    }

    internal static double Louder(double a, double b)
        => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);

    private void InitMusic()
    {
        Timeline.MusicMoved += start => MoveMusic(start);
        TimelineMenu.Opening += (_, _) => BuildMusicMenu();
    }

    internal static bool IsMusicFile(MediaInfo info) => info.HasAudio && (info.IsAudioOnly || info.Width <= 0 || info.Height <= 0);

    internal bool PlaceMusic(string path, MediaInfo info)
    {
        if (_model is not { Duration: > 0 } model || Exporting) return false;
        var duration = EditTime.FromSeconds(info.DurationSeconds);
        if (duration <= 0) return false;
        var start = Timeline.RoundToFrame(Timeline.Playhead);
        if (start >= model.Duration - (long)Math.Ceiling(Timeline.FrameTicks)) start = 0;
        var level = model.Music?.LevelDb ?? MusicDefaultLevelDb;
        var placed = new EditMusic(path, duration, Math.Max(0, start), level);
        if (model.Music is { } previous) placed = placed with { Duck = previous.Duck, DuckDb = previous.DuckDb, DuckReleaseMs = previous.DuckReleaseMs };
        if (!Apply(m => m.SetMusic(placed), -1)) return false;
        ShowProjectStatus("StatusSuccess", string.Format(Strings.Culture, Strings.Get("editor.music.placed"), Path.GetFileName(path)));
        return true;
    }

    internal async Task<bool> AddMusicAsync(string path)
    {
        if (_model is null || Exporting) return false;
        var model = _model;
        var info = await ProbeSourceAsync(path).ConfigureAwait(true);
        if (!ReferenceEquals(model, _model) || Exporting) return false;
        if (info is { HasAudio: true } && PlaceMusic(path, info)) return true;
        ShowProjectStatus("StatusError", string.Format(Strings.Culture, Strings.Get("editor.music.failed"), Path.GetFileName(path)));
        return false;
    }

    internal bool SetMusicLevel(double levelDb)
    {
        if (_model?.Music is not { } music || Exporting) return false;
        var level = EditMusic.ClampLevel(levelDb);
        return Apply(m => m.SetMusic(music.Placed(music.Start, level)), -1);
    }

    internal bool MoveMusic(long start)
    {
        if (_model is not { Music: { } music } model || Exporting) return false;
        var at = Math.Clamp(start, 0, Math.Max(0, model.Duration - 1));
        return Apply(m => m.SetMusic(music.Placed(at, music.LevelDb)), -1);
    }

    internal bool SetMusicDuck(bool? on = null, double? duckDb = null, int? releaseMs = null)
    {
        if (_model?.Music is not { } music || Exporting) return false;
        return Apply(m => m.SetMusic(music with
        {
            Duck = on ?? music.Duck,
            DuckDb = duckDb is { } db ? EditMusic.ClampDuck(db) : music.DuckDb,
            DuckReleaseMs = releaseMs is { } ms ? EditMusic.ClampRelease(ms) : music.DuckReleaseMs
        }), -1);
    }

    internal bool RemoveMusic() => _model is { HasMusic: true } && !Exporting && Apply(m => m.SetMusic(null), -1);

    private void BuildMusicMenu()
    {
        MnuMusic.Items.Clear();
        var model = _model;
        var music = model?.Music;
        var idle = model is not null && !Exporting;

        var add = new MenuItem { Header = Strings.Get(music is null ? "editor.music.add" : "editor.music.replace"), IsEnabled = idle };
        add.Click += (_, _) => _ = AddMusicClickedAsync();
        MnuMusic.Items.Add(add);

        var levels = new MenuItem { Header = Strings.Get("editor.music.level"), IsEnabled = idle && music is not null };
        foreach (var preset in MusicLevels)
        {
            var value = preset;
            var item = new MenuItem
            {
                Header = EditorTimeline.MusicLevelText(value),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = music is not null && Math.Abs(music.LevelDb - value) < 0.05
            };
            item.Click += (_, _) => SetMusicLevel(value);
            levels.Items.Add(item);
        }

        MnuMusic.Items.Add(levels);
        MnuMusic.Items.Add(BuildDuckMenu(music, idle));

        var here = new MenuItem { Header = Strings.Get("editor.music.to-playhead"), IsEnabled = idle && music is not null };
        here.Click += (_, _) => MoveMusic(Timeline.RoundToFrame(Timeline.Playhead));
        MnuMusic.Items.Add(here);

        var remove = new MenuItem { Header = Strings.Get("editor.music.remove"), IsEnabled = idle && music is not null };
        remove.Click += (_, _) => RemoveMusic();
        MnuMusic.Items.Add(remove);
        MnuMusic.IsEnabled = model is not null;
    }

    private MenuItem BuildDuckMenu(EditMusic? music, bool idle)
    {
        var duck = new MenuItem { Header = Strings.Get("editor.music.duck"), IsEnabled = idle && music is not null };
        var on = new MenuItem
        {
            Header = Strings.Get("editor.music.duck.on"),
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = music?.Duck == true
        };
        on.Click += (_, _) => SetMusicDuck(on: !(_model?.Music?.Duck ?? false));
        duck.Items.Add(on);

        var amounts = new MenuItem { Header = Strings.Get("editor.music.duck.amount"), IsEnabled = music?.Duck == true };
        foreach (var preset in DuckAmounts)
        {
            var value = preset;
            var item = new MenuItem
            {
                Header = EditorTimeline.MusicLevelText(-value),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = music is not null && Math.Abs(music.DuckDb - value) < 0.05
            };
            item.Click += (_, _) => SetMusicDuck(duckDb: value);
            amounts.Items.Add(item);
        }

        duck.Items.Add(amounts);

        var responses = new MenuItem { Header = Strings.Get("editor.music.duck.response"), IsEnabled = music?.Duck == true };
        foreach (var (key, ms) in DuckResponses)
        {
            var value = ms;
            var item = new MenuItem
            {
                Header = string.Format(Strings.Culture, Strings.Get(key), value),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = music?.DuckReleaseMs == value
            };
            item.Click += (_, _) => SetMusicDuck(releaseMs: value);
            responses.Items.Add(item);
        }

        duck.Items.Add(responses);
        return duck;
    }

    private async Task AddMusicClickedAsync()
    {
        if (_model is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("editor.music.add"),
            AllowMultiple = false
        }).ConfigureAwait(true);
        if (files.Select(file => file.TryGetLocalPath()).FirstOrDefault(path => path is not null) is { } path) await AddMusicAsync(path).ConfigureAwait(true);
    }
}
