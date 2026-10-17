using System.IO;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace TaskbarMedia;

public sealed record MediaState(
    bool HasSession,
    string Title,
    string Artist,
    BitmapImage? Cover,
    bool IsPlaying,
    bool CanPrevious,
    bool CanPlayPause,
    bool CanNext,
    bool CanShuffle,
    bool IsShuffle,
    bool CanRepeat,
    MediaPlaybackAutoRepeatMode Repeat,
    System.Windows.Media.Color? CoverColor)
{
    public static readonly MediaState Empty =
        new(false, "", "", null, false, false, false, false, false, false, false, MediaPlaybackAutoRepeatMode.None, null);
}

/// <summary>
/// Reads the current Windows media session (GSMTC) and sends playback commands to it.
/// <see cref="Updated"/> may be raised on any thread.
/// </summary>
public sealed class MediaService
{
    private readonly object _lock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Session? _session;
    private int _version;

    // Cover cache so play/pause toggles don't reload the thumbnail.
    private string _coverKey = "";
    private BitmapImage? _coverCache;
    private System.Windows.Media.Color? _coverColorCache;

    public event Action<MediaState>? Updated;

    /// <summary>The latest state, so windows created later (e.g. after Explorer restarts) start up to date.</summary>
    public MediaState Current { get; private set; } = MediaState.Empty;

    /// <summary>
    /// Connects to the media service. The connection can fail right after logon or when
    /// started from some environments, so keep retrying instead of crashing.
    /// </summary>
    public async Task StartAsync()
    {
        var delay = TimeSpan.FromSeconds(2);
        while (true)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                break;
            }
            catch (Exception)
            {
                await Task.Delay(delay);
                if (delay < TimeSpan.FromSeconds(30)) delay *= 2;
            }
        }

        _manager.CurrentSessionChanged += (_, _) => AttachCurrentSession();
        AttachCurrentSession();
    }

    private void AttachCurrentSession()
    {
        lock (_lock)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }

            _session = _manager?.GetCurrentSession();

            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            }
        }
        _ = RefreshAsync();
    }

    private void OnMediaPropertiesChanged(Session s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
    private void OnPlaybackInfoChanged(Session s, PlaybackInfoChangedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        int version = Interlocked.Increment(ref _version);
        var session = _session;
        var state = MediaState.Empty;

        if (session != null)
        {
            try
            {
                var info = session.GetPlaybackInfo();
                var props = await session.TryGetMediaPropertiesAsync();
                string title = props?.Title ?? "";
                string artist = props?.Artist ?? "";

                string key = session.SourceAppUserModelId + "|" + title + "|" + artist;
                BitmapImage? cover;
                System.Windows.Media.Color? coverColor;
                if (key == _coverKey && _coverCache != null)
                {
                    cover = _coverCache;
                    coverColor = _coverColorCache;
                }
                else
                {
                    cover = props?.Thumbnail != null ? await LoadImageAsync(props.Thumbnail) : null;
                    coverColor = cover != null ? ColorExtractor.Dominant(cover) : null;
                    _coverKey = key;
                    _coverCache = cover;
                    _coverColorCache = coverColor;
                }

                var c = info.Controls;
                state = new MediaState(
                    true, title, artist, cover,
                    info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    c.IsPreviousEnabled,
                    c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled,
                    c.IsNextEnabled,
                    c.IsShuffleEnabled,
                    info.IsShuffleActive ?? false,
                    c.IsRepeatEnabled,
                    info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None,
                    coverColor);
            }
            catch (Exception)
            {
                // The session can close while we read it; treat as empty.
            }
        }

        // Drop stale results if a newer refresh started meanwhile.
        if (version == Volatile.Read(ref _version))
        {
            Current = state;
            Updated?.Invoke(state);
        }
    }

    private static async Task<BitmapImage?> LoadImageAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var src = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await src.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 320; // sharp enough for the large panel's 240px cover
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task PreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync());
    public Task TogglePlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync());
    public Task NextAsync() => SendAsync(s => s.TrySkipNextAsync());

    public Task ToggleShuffleAsync() => SendAsync(s =>
        s.TryChangeShuffleActiveAsync(!(s.GetPlaybackInfo().IsShuffleActive ?? false)));

    /// <summary>Cycles repeat: none → all (list) → one (track) → none.</summary>
    public Task CycleRepeatAsync() => SendAsync(s =>
        s.TryChangeAutoRepeatModeAsync(s.GetPlaybackInfo().AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        }));

    /// <summary>
    /// Where the current track is (estimated forward from the app's last report while playing) and how long it is.
    /// Null when there is no track or the app gives no length (e.g. a live stream).
    /// </summary>
    public (TimeSpan Position, TimeSpan Duration, bool CanSeek)? GetTimeline()
    {
        var session = _session;
        if (session == null) return null;
        try
        {
            var t = session.GetTimelineProperties();
            var info = session.GetPlaybackInfo();
            var duration = t.EndTime - t.StartTime;
            if (duration <= TimeSpan.Zero) return null;

            var position = t.Position - t.StartTime;
            if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && t.LastUpdatedTime.Year > 2000)
                position += (DateTimeOffset.Now - t.LastUpdatedTime) * (info.PlaybackRate ?? 1);

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > duration) position = duration;
            return (position, duration, info.Controls.IsPlaybackPositionEnabled);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Jumps to <paramref name="position"/> from the start of the track.</summary>
    public Task SeekAsync(TimeSpan position) => SendAsync(s =>
        s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().StartTime + position).Ticks));

    private async Task SendAsync(Func<Session, IAsyncOperation<bool>> command)
    {
        var session = _session;
        if (session == null) return;
        try { await command(session); }
        catch (Exception) { }
    }
}
