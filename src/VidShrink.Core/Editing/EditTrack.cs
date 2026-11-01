namespace VidShrink.Core.Editing;

public enum EditTrack
{
    Video,
    Text,
    Music,
    Audio
}

public readonly record struct EditTrackState(bool Muted = false, bool Hidden = false, bool Locked = false, bool Solo = false)
{
    public bool IsDefault => !Muted && !Hidden && !Locked && !Solo;
}

public static class EditTracks
{
    public static IReadOnlyList<EditTrack> All { get; } = Enum.GetValues<EditTrack>();

    public static bool HasSound(EditTrack track) => track is EditTrack.Video or EditTrack.Music or EditTrack.Audio;

    public static bool CanHide(EditTrack track) => track is EditTrack.Video or EditTrack.Text;

    public static string Name(EditTrack track) => track switch
    {
        EditTrack.Video => "V1",
        EditTrack.Text => "T1",
        EditTrack.Music => "A2",
        EditTrack.Audio => "A3",
        _ => throw new ArgumentOutOfRangeException(nameof(track), track, null)
    };

    public static EditTrack? Parse(string? name)
        => All.Cast<EditTrack?>().FirstOrDefault(track => string.Equals(Name(track!.Value), name, StringComparison.Ordinal));

    public static EditTrackState Normalized(EditTrack track, EditTrackState state)
        => state with
        {
            Muted = state.Muted && HasSound(track),
            Solo = state.Solo && HasSound(track),
            Hidden = state.Hidden && CanHide(track)
        };
}
