using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using VidShrink.App.Localization;
using VidShrink.App.Recorder;
using VidShrink.Core;
using VidShrink.Core.Editing;
using VidShrink.Ffmpeg;
using VidShrink.Player;

namespace VidShrink.App.Editing;

internal partial class EditorView
{
    private static readonly double[] SoundLevels = { -24, -18, -12, -6, 0, 6 };

    private NarrationRecorder? _narration;
    private long _narrationStart;

    internal bool Narrating => _narration is not null;

    private void InitSound()
    {
        Timeline.AudioMoved += (index, start) => MoveSound(index, start);
        Timeline.AudioTrimmed += (index, head, edge) => TrimSound(index, head, edge);
        Timeline.TrackHeaderPressed += OpenTrackMenu;
        TimelineMenu.Opening += (_, _) => BuildSoundMenu();
        DetachedFromVisualTree += (_, _) => DropNarration();
    }

    internal bool DetachSound()
    {
        if (_model is not { } model || Exporting || model.IsLocked(EditTrack.Video)) return false;
        var index = Timeline.SelectedIndex;
        if (index < 0 || index >= model.Clips.Count) return false;
        var source = model.Clips[index].Source;
        if (source < 0 || source >= SourcePaths.Count) return false;
        var path = SourcePaths[source];
        if (!Apply(m => m.DetachAudio(index, path), index))
        {
            ShowProjectStatus("StatusWarning", Strings.Get("editor.sound.detach.failed"));
            return false;
        }

        ShowProjectStatus("StatusSuccess", Strings.Get("editor.sound.detached"));
        return true;
    }

    internal bool MoveSound(int index, long start)
    {
        if (_model is not { } model || Exporting || model.IsLocked(EditTrack.Audio) || index < 0 || index >= model.AudioClips.Count) return false;
        var at = Math.Clamp(start, 0, Math.Max(0, model.Duration - 1));
        return Apply(m => m.MoveAudio(index, at), -1);
    }

    internal bool TrimSound(int index, bool head, long edge)
    {
        if (_model is not { } model || Exporting || model.IsLocked(EditTrack.Audio) || index < 0 || index >= model.AudioClips.Count) return false;
        return Apply(m => m.TrimAudio(index, head, edge), -1);
    }

    internal bool SetSoundLevel(int index, double levelDb)
    {
        if (_model is not { } model || Exporting || model.IsLocked(EditTrack.Audio) || index < 0 || index >= model.AudioClips.Count) return false;
        return Apply(m => m.SetAudioLevel(index, levelDb), -1);
    }

    internal bool RemoveSound(int index)
    {
        if (_model is not { } model || Exporting || model.IsLocked(EditTrack.Audio) || index < 0 || index >= model.AudioClips.Count) return false;
        if (!Apply(m =>
            {
                m.DeleteAudio(index);
                return true;
            }, -1)) return false;
        Timeline.RefreshAudio();
        return true;
    }

    internal bool SetTrack(EditTrack track, EditTrackState state)
    {
        if (_model is not { } model) return false;
        var shown = model.Visible(EditTrack.Video);
        if (!model.SetTrack(track, state)) return false;
        Timeline.Refresh();
        if (shown != model.Visible(EditTrack.Video)) _ = RestyleAsync();
        RefreshOverlay();
        if (Preview.Engine is { } engine) ApplyTrackSound(engine);
        MarkEdited();
        return true;
    }

    private void ApplyTrackSound(IPlaybackEngine engine)
        => engine.SetMuted(Preview.IsMuted || _model is { } model && !model.Audible(EditTrack.Video));

    internal async Task<bool> StartNarrationAsync()
    {
        if (_model is not { Duration: > 0 } model || Exporting || _narration is not null || Projects is not { } store) return false;
        var settings = RecorderSettings.Load(RecorderSettings.FilePath);
        var listed = await Task.Run(() => CaptureDevices.Instance.Audio).ConfigureAwait(true);
        if (!ReferenceEquals(model, _model) || _narration is not null) return false;
        if (NarrationArguments.PickMicrophone(listed, settings.MicrophoneName) is not { } microphone)
        {
            ShowProjectStatus("StatusWarning", Strings.Get("editor.sound.narrate.no-mic"));
            return false;
        }

        var output = Path.Combine(store.Folder, NarrationArguments.FolderName, NarrationArguments.FileName(DateTime.Now));
        try
        {
            var plan = NarrationArguments.Plan(microphone, listed, settings.AudioFilters);
            _narration = NarrationRecorder.Start(NarrationArguments.Build(plan, output), output);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            ShowProjectStatus("StatusError", string.Format(Strings.Culture, Strings.Get("editor.sound.narrate.failed"), ex.Message));
            return false;
        }

        _narrationStart = Timeline.RoundToFrame(Timeline.Playhead);
        ShowProjectStatus("StatusSuccess", Strings.Get("editor.sound.narrate.recording"));
        if (!Preview.IsPlaying) Preview.TogglePlay();
        return true;
    }

    internal async Task<bool> StopNarrationAsync()
    {
        if (_narration is not { } recorder) return false;
        _narration = null;
        if (Preview.IsPlaying) Preview.TogglePlay();
        var run = await recorder.StopAsync().ConfigureAwait(true);
        await recorder.DisposeAsync().ConfigureAwait(true);
        var name = Path.GetFileName(recorder.Output);
        if (!run.Ok)
        {
            ShowProjectStatus("StatusError", string.Format(Strings.Culture, Strings.Get("editor.sound.narrate.failed"), run.StandardError));
            return false;
        }

        var info = await ProbeSourceAsync(recorder.Output).ConfigureAwait(true);
        var duration = info is null ? 0 : EditTime.FromSeconds(info.DurationSeconds);
        if (duration <= 0 || _model is null)
        {
            ShowProjectStatus("StatusError", string.Format(Strings.Culture, Strings.Get("editor.sound.narrate.failed"), name));
            return false;
        }

        var start = _narrationStart;
        var clip = new EditAudioClip(recorder.Output, duration, 0, duration, start, 0, EditAudioKind.Narration);
        if (!Apply(m =>
            {
                m.AddAudio(clip);
                return true;
            }, -1)) return false;
        ShowProjectStatus("StatusSuccess", string.Format(Strings.Culture, Strings.Get("editor.sound.narrate.placed"), name));
        return true;
    }

    private void DropNarration()
    {
        if (_narration is not { } recorder) return;
        _narration = null;
        _ = recorder.DisposeAsync().AsTask();
    }

    private void BuildSoundMenu()
    {
        MnuSound.Items.Clear();
        var model = _model;
        var idle = model is not null && !Exporting;
        var unlocked = idle && !model!.IsLocked(EditTrack.Audio);

        var detach = new MenuItem
        {
            Header = Strings.Get("editor.sound.detach"),
            IsEnabled = idle && !model!.IsLocked(EditTrack.Video) && Timeline.SelectedIndex >= 0
        };
        detach.Click += (_, _) => DetachSound();
        MnuSound.Items.Add(detach);

        var narrate = new MenuItem
        {
            Header = Strings.Get(Narrating ? "editor.sound.narrate.stop" : "editor.sound.narrate"),
            IsEnabled = Narrating || unlocked && Projects is not null
        };
        narrate.Click += (_, _) => _ = Narrating ? StopNarrationAsync() : StartNarrationAsync();
        MnuSound.Items.Add(narrate);

        var index = Timeline.SelectedAudio;
        var clip = model is not null && index >= 0 && index < model.AudioClips.Count ? model.AudioClips[index] : null;
        var levels = new MenuItem { Header = Strings.Get("editor.sound.level"), IsEnabled = unlocked && clip is not null };
        foreach (var preset in SoundLevels)
        {
            var value = preset;
            var item = new MenuItem
            {
                Header = EditorTimeline.MusicLevelText(value),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = clip is not null && Math.Abs(clip.LevelDb - value) < 0.05
            };
            item.Click += (_, _) => SetSoundLevel(index, value);
            levels.Items.Add(item);
        }

        MnuSound.Items.Add(levels);

        var here = new MenuItem { Header = Strings.Get("editor.sound.to-playhead"), IsEnabled = unlocked && clip is not null };
        here.Click += (_, _) => MoveSound(index, Timeline.RoundToFrame(Timeline.Playhead));
        MnuSound.Items.Add(here);

        var remove = new MenuItem { Header = Strings.Get("editor.sound.remove"), IsEnabled = unlocked && clip is not null };
        remove.Click += (_, _) => RemoveSound(index);
        MnuSound.Items.Add(remove);
        MnuSound.IsEnabled = model is not null;

        MnuTracks.Items.Clear();
        foreach (var track in EditTracks.All)
        {
            var item = new MenuItem { Header = EditTracks.Name(track), IsEnabled = model is not null };
            foreach (var toggle in TrackToggles(track)) item.Items.Add(toggle);
            MnuTracks.Items.Add(item);
        }

        MnuTracks.IsEnabled = model is not null;
    }

    private void OpenTrackMenu(EditTrack track)
    {
        if (_model is null) return;
        var menu = new ContextMenu();
        foreach (var toggle in TrackToggles(track)) menu.Items.Add(toggle);
        menu.Open(Timeline);
    }

    private MenuItem[] TrackToggles(EditTrack track)
    {
        var state = _model?.Track(track) ?? default;
        var items = new System.Collections.Generic.List<MenuItem>();
        if (EditTracks.HasSound(track))
        {
            items.Add(Toggle("editor.track.mute", state.Muted, () => SetTrack(track, Current(track) with { Muted = !Current(track).Muted })));
            items.Add(Toggle("editor.track.solo", state.Solo, () => SetTrack(track, Current(track) with { Solo = !Current(track).Solo })));
        }

        if (EditTracks.CanHide(track))
            items.Add(Toggle("editor.track.hide", state.Hidden, () => SetTrack(track, Current(track) with { Hidden = !Current(track).Hidden })));
        items.Add(Toggle("editor.track.lock", state.Locked, () => SetTrack(track, Current(track) with { Locked = !Current(track).Locked })));
        return items.ToArray();
    }

    private EditTrackState Current(EditTrack track) => _model?.Track(track) ?? default;

    private static MenuItem Toggle(string key, bool on, Action click)
    {
        var item = new MenuItem { Header = Strings.Get(key), ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        item.Click += (_, _) => click();
        return item;
    }
}
