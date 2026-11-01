namespace VidShrink.Core.Editing;

public sealed partial class EditTimeline
{
    private IReadOnlyList<EditAudioClip> _audioClips = Array.Empty<EditAudioClip>();
    private readonly Dictionary<EditTrack, EditTrackState> _tracks = new();

    public IReadOnlyList<EditAudioClip> AudioClips => _audioClips;

    public bool HasAudioClips => _audioClips.Count > 0;

    public int AddAudio(EditAudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Execute(new AudioCommand(SetAudioList, _audioClips, _audioClips.Append(clip).ToArray()));
        return _audioClips.Count - 1;
    }

    public bool MoveAudio(int index, long start)
    {
        CheckAudio(index);
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), start, "Ses cizelgenin basindan once baslayamaz");
        return ReplaceAudio(index, _audioClips[index].MovedTo(start));
    }

    public bool TrimAudio(int index, bool head, long edge)
    {
        CheckAudio(index);
        return ReplaceAudio(index, _audioClips[index].TrimmedTo(head, edge));
    }

    public bool SetAudioLevel(int index, double levelDb)
    {
        CheckAudio(index);
        return ReplaceAudio(index, _audioClips[index].WithLevel(levelDb));
    }

    public void DeleteAudio(int index)
    {
        CheckAudio(index);
        Execute(new AudioCommand(SetAudioList, _audioClips, _audioClips.Where((_, i) => i != index).ToArray()));
    }

    public bool DetachAudio(int index, string path)
    {
        CheckIndex(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var clip = _clips[index];
        if (clip.Effects.Muted || clip.Speed != 1m || clip.Reversed) return false;
        var duration = Math.Max(DurationOf(clip.Source) ?? clip.SourceEnd, clip.SourceEnd);
        var piece = new EditAudioClip(path, duration, clip.SourceStart, clip.SourceEnd, ClipStart(index), EditAudioClip.ClampLevel(clip.Effects.VolumeDb));
        var muted = clip with { Effects = clip.Effects with { Muted = true } };
        Execute(new CompositeCommand(new IEditCommand[]
        {
            new ReplaceCommand(index, clip, muted),
            new AudioCommand(SetAudioList, _audioClips, _audioClips.Append(piece).ToArray())
        }));
        return true;
    }

    public bool Restore(IEnumerable<EditClip> clips, IEnumerable<TextLayer> texts, EditMusic? music, EditFrame frame, IEnumerable<EditAudioClip> audio)
        => Restore(clips, texts, music, frame, Layers, audio);

    public bool Restore(IEnumerable<EditClip> clips, IEnumerable<TextLayer> texts, EditMusic? music, EditFrame frame, IEnumerable<VideoLayer> layers, IEnumerable<EditAudioClip> audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var next = audio.ToArray();
        if (next.Any(a => a is null)) throw new ArgumentException("Listede bos ses parcasi var", nameof(audio));
        var body = Restore(clips, texts, music, frame, layers);
        if (next.SequenceEqual(_audioClips)) return body;
        var step = new AudioCommand(SetAudioList, _audioClips, next);
        if (!body)
        {
            Execute(step);
            return true;
        }

        var last = _undo.Pop();
        step.Apply(_clips);
        Record(new CompositeCommand(new[] { last, step }));
        return true;
    }

    public IReadOnlyDictionary<EditTrack, EditTrackState> TrackStates => _tracks;

    public EditTrackState Track(EditTrack track) => _tracks.TryGetValue(track, out var state) ? state : default;

    public bool SetTrack(EditTrack track, EditTrackState state)
    {
        var next = EditTracks.Normalized(track, state);
        if (next == Track(track)) return false;
        if (next.IsDefault) _tracks.Remove(track);
        else _tracks[track] = next;
        return true;
    }

    public bool IsLocked(EditTrack track) => Track(track).Locked;

    public bool Visible(EditTrack track) => !Track(track).Hidden;

    public bool Audible(EditTrack track)
    {
        if (!EditTracks.HasSound(track)) return false;
        var state = Track(track);
        return !state.Muted && (state.Solo || !_tracks.Values.Any(s => s.Solo));
    }

    public EditTimeline ForExport()
    {
        var audible = Audible(EditTrack.Video);
        var shown = Visible(EditTrack.Video);
        var clips = _clips.Select(c => c with { Effects = Seen(Heard(c.Effects, audible), shown) }).ToArray();
        var copy = new EditTimeline(clips, texts: Visible(EditTrack.Text) ? _texts.ToArray() : null, frame: Frame, layers: Layers.ToArray())
        {
            Music = Audible(EditTrack.Music) ? Music : null,
            _audioClips = Audible(EditTrack.Audio) ? _audioClips : Array.Empty<EditAudioClip>()
        };
        return copy;
    }

    /// <summary>
    /// Onizlemenin gordugu cizelge: V1 gizli degilse kendisi, gizliyse V1 parcalari <see cref="ClipEffects.Blackout"/> ile kararmis bir kopya.
    /// Yazi, katman, muzik ve ses parcalari aynen kalir; sesi motor ayrica susturur.
    /// </summary>
    public EditTimeline Shown()
    {
        if (Visible(EditTrack.Video)) return this;
        var clips = _clips.Select(c => c with { Effects = c.Effects.Blackout() }).ToArray();
        return new EditTimeline(clips, texts: _texts.ToArray(), frame: Frame, layers: Layers.ToArray())
        {
            Music = Music,
            _audioClips = _audioClips
        };
    }

    private static ClipEffects Heard(ClipEffects effects, bool audible) => audible ? effects : effects with { Muted = true };

    private static ClipEffects Seen(ClipEffects effects, bool shown) => shown ? effects : effects.Blackout();

    private void SetAudioList(IReadOnlyList<EditAudioClip> clips) => _audioClips = clips;

    private bool ReplaceAudio(int index, EditAudioClip after)
    {
        if (after == _audioClips[index]) return false;
        var next = _audioClips.ToArray();
        next[index] = after;
        Execute(new AudioCommand(SetAudioList, _audioClips, next));
        return true;
    }

    private void CheckAudio(int index)
    {
        if (index < 0 || index >= _audioClips.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Ses parcasi sirasi listenin disinda");
    }

    private sealed class AudioCommand : IEditCommand
    {
        private readonly Action<IReadOnlyList<EditAudioClip>> _set;
        private readonly IReadOnlyList<EditAudioClip> _before;
        private readonly IReadOnlyList<EditAudioClip> _after;

        public AudioCommand(Action<IReadOnlyList<EditAudioClip>> set, IReadOnlyList<EditAudioClip> before, IReadOnlyList<EditAudioClip> after)
        {
            _set = set;
            _before = before;
            _after = after;
        }

        public void Apply(List<EditClip> clips) => _set(_after);

        public void Revert(List<EditClip> clips) => _set(_before);
    }
}
