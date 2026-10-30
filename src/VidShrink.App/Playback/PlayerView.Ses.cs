using System.IO;
using System.Linq;
using Avalonia.Layout;
using VidShrink.Player;

namespace VidShrink.App.Playback;

/// <summary>
/// Görüntüsüz dosya (müzik): kare gelmediği için süre çubuğu motorun konumundan ilerler,
/// takılma bekçisi susar, orta alanda başlık ve sanatçı durur. Gömülü kapak varsa motor onu
/// tek kare olarak çizer; kart o zaman üste çekilir.
/// </summary>
internal partial class PlayerView
{
    private int _audioTick = -1;
    private bool _ilkGoruntuBildirildi;

    internal bool AudioOnly => _engine is { IsOpen: true, HasVideo: false };

    internal string? AudioTitle => AudioCard.IsVisible ? TxtAudioTitle.Text : null;

    internal string? AudioSubtitle => AudioCard.IsVisible && TxtAudioArtist.IsVisible ? TxtAudioArtist.Text : null;

    private void IlkGoruntu()
    {
        if (_ilkGoruntuBildirildi) return;
        _ilkGoruntuBildirildi = true;
        IlkKareCizildi?.Invoke();
    }

    private void FollowAudio(IPlaybackEngine engine)
    {
        AudioBars.Classes.Set("playing", _playing);
        if (!_playing) return;
        var at = engine.PositionSeconds;
        if (!double.IsFinite(at)) return;
        _seek.Follow(at);
        var tick = (int)(at * 10);
        if (tick == _audioTick) return;
        _audioTick = tick;
        RefreshState();
    }

    private void ShowAudioCard(string path, IPlaybackEngine engine)
    {
        if (engine.HasVideo)
        {
            HideAudioCard();
            return;
        }

        var tags = engine.Tags;
        TxtAudioTitle.Text = tags.Title ?? Path.GetFileNameWithoutExtension(path);
        var line = string.Join(" - ", new[] { tags.Artist, tags.Album }.Where(part => part is not null));
        TxtAudioArtist.Text = line;
        TxtAudioArtist.IsVisible = line.Length > 0;
        Frame.Source = null;
        AudioCard.VerticalAlignment = VerticalAlignment.Center;
        AudioCard.IsVisible = true;
        AudioCard.Opacity = 1;
        IlkGoruntu();
    }

    private void HideAudioCard()
    {
        AudioCard.IsVisible = false;
        AudioCard.Opacity = 0;
        _audioTick = -1;
    }

    private void RaiseAudioCardOverCover()
    {
        if (AudioCard.IsVisible) AudioCard.VerticalAlignment = VerticalAlignment.Top;
    }
}
