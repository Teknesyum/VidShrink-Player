using System;
using System.Collections.Generic;
using System.Linq;
using VidShrink.Core.Editing;
using VidShrink.Player;

namespace VidShrink.App.Playback;

/// <summary>
/// A3 izinin onizlemesi: o an calan ya da <see cref="MusicPreview.PreloadSeconds"/> icinde baslayacak her parca icin ayri,
/// yalniz sesli bir <see cref="MusicPreview"/> yuvasi. Ust uste binen parcalar ayri yuvada birlikte calar; yuva sayisi
/// <see cref="MaxSlots"/> ile sinirli, fazlasi en gec baslayandan atilir. Yuvalar olculu acilir; tepe duzeyleri muzik kismasinin
/// anahtaridir.
/// </summary>
internal sealed class SoundClipsPreview : IDisposable
{
    internal const int MaxSlots = 4;

    private readonly Func<PlaybackOptions, IPlaybackEngine> _create;
    private readonly List<MusicPreview> _slots = new();
    private bool _disposed;

    internal SoundClipsPreview()
        : this(options => new MpvEngine(options))
    {
    }

    internal SoundClipsPreview(Func<PlaybackOptions, IPlaybackEngine> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    internal IReadOnlyList<MusicPreview> Slots => _slots;

    internal double PeakDb
    {
        get
        {
            var peak = double.NaN;
            foreach (var slot in _slots)
                if (slot.PeakDb is var db && !double.IsNaN(db) && (double.IsNaN(peak) || db > peak)) peak = db;
            return peak;
        }
    }

    internal static IReadOnlyList<PreviewCue> Wanted(IReadOnlyList<EditAudioClip> clips, long timeline, bool backward = false)
    {
        var active = new List<PreviewCue>();
        var upcoming = new List<PreviewCue>();
        foreach (var clip in clips)
        {
            var cue = PreviewCue.From(clip);
            if (MusicPreview.Offset(cue, timeline) is not null) active.Add(cue);
            else if (!backward && MusicPreview.Upcoming(cue, timeline)) upcoming.Add(cue);
        }

        return active.OrderBy(c => c.Start).Concat(upcoming.OrderBy(c => c.Start)).Take(MaxSlots).ToArray();
    }

    internal void Sync(IReadOnlyList<EditAudioClip> clips, long timeline, bool playing, double rate, double volume, bool muted, bool backward = false)
    {
        if (_disposed) return;
        if (!playing)
        {
            Pause();
            return;
        }

        var wanted = Wanted(clips, timeline, backward);
        var owner = new MusicPreview?[wanted.Count];
        var taken = new bool[_slots.Count];
        for (var i = 0; i < wanted.Count; i++)
        {
            var same = -1;
            for (var j = 0; j < _slots.Count && same < 0; j++)
                if (!taken[j] && string.Equals(_slots[j].Path, wanted[i].Path, StringComparison.Ordinal)) same = j;
            if (same < 0) continue;
            taken[same] = true;
            owner[i] = _slots[same];
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (owner[i] is not null) continue;
            var free = Array.FindIndex(taken, t => !t);
            if (free >= 0)
            {
                taken[free] = true;
                owner[i] = _slots[free];
                continue;
            }

            var slot = new MusicPreview(_create) { Metered = true };
            _slots.Add(slot);
            Array.Resize(ref taken, _slots.Count);
            taken[^1] = true;
            owner[i] = slot;
        }

        for (var j = 0; j < _slots.Count; j++)
            if (!taken[j]) _slots[j].Close();
        for (var i = 0; i < wanted.Count; i++)
            owner[i]!.Sync(wanted[i], timeline, true, rate, volume, muted, backward);
    }

    internal void Pause()
    {
        foreach (var slot in _slots) slot.Pause();
    }

    internal void Close()
    {
        foreach (var slot in _slots) slot.Dispose();
        _slots.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}
