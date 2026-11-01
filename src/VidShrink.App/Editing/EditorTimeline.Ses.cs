using System;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using VidShrink.Core.Editing;

namespace VidShrink.App.Editing;

internal sealed partial class EditorTimeline
{
    private enum AudioGrip
    {
        Body,
        Head,
        Tail
    }

    private int _audioSelected = -1;
    private int _audioPress = -1;
    private AudioGrip _audioGrip;
    private bool _audioMoved;
    private EditAudioClip? _audioGhost;

    internal event Action<int, long>? AudioMoved;

    internal event Action<int, bool, long>? AudioTrimmed;

    internal event Action<EditTrack>? TrackHeaderPressed;

    internal double AudioLaneTop => MusicBottom + Metric("EditorTrackGap");

    internal double AudioLaneHeight => AudioHeight;

    internal double AudioLaneBottom => AudioLaneTop + AudioLaneHeight;

    internal int SelectedAudio
    {
        get => _audioSelected;
        set
        {
            var next = _model is { } model && value >= 0 && value < model.AudioClips.Count ? value : -1;
            if (next == _audioSelected) return;
            _audioSelected = next;
            _canvas.InvalidateVisual();
        }
    }

    internal bool OnAudioLane(Point position) => position.Y >= AudioLaneTop && position.Y < AudioLaneBottom;

    internal int AudioAt(double x)
    {
        if (_model is not { } model || _ppt <= 0) return -1;
        var time = XToTime(x);
        for (var i = model.AudioClips.Count - 1; i >= 0; i--)
        {
            var clip = model.AudioClips[i];
            if (time >= clip.Start && time < clip.End) return i;
        }

        return -1;
    }

    internal bool AudioEdgeAt(Point position, out int index, out bool head)
    {
        index = -1;
        head = false;
        if (_model is not { } model || _ppt <= 0 || position.X < HeaderWidth || !OnAudioLane(position)) return false;
        var grip = Metric("EditorEdgeGripWidth");
        var best = double.PositiveInfinity;
        for (var i = model.AudioClips.Count - 1; i >= 0; i--)
        {
            var clip = model.AudioClips[i];
            var toStart = Math.Abs(position.X - TimeToX(clip.Start));
            var toEnd = Math.Abs(TimeToX(clip.End) - position.X);
            var near = Math.Min(toStart, toEnd);
            if (near > grip || near >= best) continue;
            best = near;
            index = i;
            head = toStart <= toEnd;
        }

        return index >= 0;
    }

    internal EditTrack? TrackAt(Point position)
    {
        if (position.Y >= TextTop && position.Y < TextTop + TextHeight) return EditTrack.Text;
        if (position.Y >= VideoTop && position.Y < TracksBottom) return EditTrack.Video;
        if (OnMusicRow(position)) return EditTrack.Music;
        if (OnAudioLane(position)) return EditTrack.Audio;
        return null;
    }

    internal bool LockedAt(Point position) => _model is { } model && TrackAt(position) is { } track && model.IsLocked(track);

    private bool PressHeader(Point position)
    {
        if (position.X >= HeaderWidth) return false;
        if (TrackAt(position) is { } track) TrackHeaderPressed?.Invoke(track);
        return true;
    }

    private bool PressAudio(Point position, bool plain)
    {
        if (!OnAudioLane(position) || _model is null) return false;
        _press = position;
        if (plain && AudioEdgeAt(position, out var edge, out var head))
        {
            BeginAudio(edge, head ? AudioGrip.Head : AudioGrip.Tail);
            SetCursor(StandardCursorType.SizeWestEast);
            return true;
        }

        var hit = AudioAt(position.X);
        SelectedAudio = hit;
        if (hit >= 0) BeginAudio(hit, AudioGrip.Body);
        return true;
    }

    private void BeginAudio(int index, AudioGrip grip)
    {
        SelectedAudio = index;
        _audioPress = index;
        _audioGrip = grip;
        _audioMoved = false;
        _audioGhost = _model!.AudioClips[index];
    }

    internal void DragAudio(double x)
    {
        if (_audioPress < 0 || _model is not { } model || _audioPress >= model.AudioClips.Count || _ppt <= 0) return;
        if (!_audioMoved)
        {
            if (Math.Abs(x - _press.X) < Metric("EditorDragThresholdPx")) return;
            _audioMoved = true;
        }

        var clip = model.AudioClips[_audioPress];
        var delta = (long)Math.Round((x - _press.X) / _ppt);
        long? snapped;
        _audioGhost = _audioGrip switch
        {
            AudioGrip.Head => clip.TrimmedTo(true, Math.Max(0, Snapped(clip.Start + delta, out snapped))),
            AudioGrip.Tail => clip.TrimmedTo(false, Snapped(clip.End + delta, out snapped)),
            _ => clip.MovedTo(Math.Clamp(Snapped(clip.Start + delta, out snapped), 0, Math.Max(0, model.Duration - 1)))
        };
        SnapLine = snapped;
        _canvas.InvalidateVisual();
        _overlay.InvalidateVisual();
    }

    private void CommitAudio()
    {
        var index = _audioPress;
        var grip = _audioGrip;
        var moved = _audioMoved;
        var ghost = _audioGhost;
        var before = _model is { } model && index >= 0 && index < model.AudioClips.Count ? model.AudioClips[index] : null;
        EndAudio();
        if (!moved || ghost is null || before is null || ghost == before) return;
        if (grip == AudioGrip.Body) AudioMoved?.Invoke(index, ghost.Start);
        else AudioTrimmed?.Invoke(index, grip == AudioGrip.Head, grip == AudioGrip.Head ? ghost.Start : ghost.End);
    }

    private void EndAudio()
    {
        if (_audioPress < 0) return;
        _audioPress = -1;
        _audioMoved = false;
        _audioGhost = null;
        SnapLine = null;
        _canvas.InvalidateVisual();
        _overlay.InvalidateVisual();
    }

    private void ResetAudio()
    {
        _audioSelected = -1;
        EndAudio();
    }

    internal void RefreshAudio()
    {
        if (_audioSelected >= (_model?.AudioClips.Count ?? 0)) _audioSelected = -1;
        _canvas.InvalidateVisual();
    }

    private void DrawAudioClips(DrawingContext context)
    {
        if (_model is not { HasAudioClips: true } model) return;
        var top = AudioLaneTop;
        var height = AudioLaneHeight;
        var pad = Metric("EditorClipPadding");
        for (var i = 0; i < model.AudioClips.Count; i++)
        {
            var clip = i == _audioPress && _audioMoved && _audioGhost is { } ghost ? ghost : model.AudioClips[i];
            var x = TimeToX(clip.Start);
            var w = Math.Max(0, Math.Min(clip.Length, model.Duration - clip.Start) * _ppt);
            var box = new Rect(x, top, w, height);
            var chosen = i == _audioSelected;
            var pen = new Pen(Paint(chosen ? "NeonBlue" : "NeonBlueBorder"), Metric(chosen ? "EditorClipSelectedBorder" : "EditorClipBorder"));
            context.DrawRectangle(Paint("NeonBlueFill"), pen, new RoundedRect(box, Metric("EditorClipRadius")));
            if (w <= pad * 2) continue;
            var name = System.IO.Path.GetFileName(clip.Path) + "  " + MusicLevelText(clip.LevelDb);
            var label = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(EditorTokens.Font(this, "FontMono")), Metric("FontSizeSm"), Paint("TextBody"));
            var fit = HeaderLabelScale(label.Height, height);
            using (context.PushClip(box.Deflate(pad)))
            using (context.PushTransform(Matrix.CreateScale(fit, fit) * Matrix.CreateTranslation(x + pad, top + (height - label.Height * fit) / 2)))
                context.DrawText(label, default);
        }
    }

    private void DrawTrackHeader(DrawingContext context, EditTrack track, string? icon, double top, double height, string name)
    {
        var state = _model?.Track(track) ?? default;
        var key = state.Muted ? "IconVolumeMute" : state.Hidden ? "IconEyeOff" : state.Locked ? "IconLock" : icon;
        var paint = state.Locked ? "TextDisabled" : state.Solo ? "NeonBlue" : "TextBody";
        DrawHeaderIcon(context, key, top, height, name, paint);
    }

    private void UpdateAudioCursor(Point position)
        => SetCursor(AudioEdgeAt(position, out _, out _) ? StandardCursorType.SizeWestEast : AudioAt(position.X) >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
}
