using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace NotchUser;

/// <summary>
/// What's playing, through Windows' own media controls (the same ones behind the volume
/// flyout): Spotify, browsers, Media Player and any app that shows up there.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _pinned;
    private string _trackKey = "";

    public event Action? Changed;
    public event Action? TrackChanged;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Album { get; private set; } = "";
    public bool Playing { get; private set; }
    public double Duration { get; private set; }
    public BitmapSource? Cover { get; private set; }
    private double _position;
    private DateTime _positionAt = DateTime.UtcNow;

    public bool HasMedia => _session != null && Title.Length > 0;
    public string SourceId => _session?.SourceAppUserModelId ?? "";

    /// <summary>Where the song is now, moving on from the last reading while it plays.</summary>
    public double LivePosition =>
        Math.Clamp(_position + (Playing ? (DateTime.UtcNow - _positionAt).TotalSeconds : 0), 0, Math.Max(Duration, 0));

    public IReadOnlyList<GlobalSystemMediaTransportControlsSession> Sessions =>
        _manager?.GetSessions().ToList() ?? new List<GlobalSystemMediaTransportControlsSession>();

    public async Task StartAsync()
    {
        try { _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { return; }
        // These events arrive on background threads; everything here runs on the UI thread.
        _manager.SessionsChanged += (_, _) => OnUi(Select);
        _manager.CurrentSessionChanged += (_, _) => OnUi(Select);
        Select();
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);

    /// <summary>The pinned app if it's still around, else the one Windows says is current.</summary>
    private void Select()
    {
        if (_manager == null) return;
        var sessions = _manager.GetSessions();
        var next = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _pinned)
                   ?? _manager.GetCurrentSession()
                   ?? sessions.FirstOrDefault();
        if (next == null || next.SourceAppUserModelId != _pinned) _pinned = null;

        if (next?.SourceAppUserModelId != _session?.SourceAppUserModelId || next == null)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _session = next;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnSessionChanged;
                _session.PlaybackInfoChanged += OnSessionChanged;
                _session.TimelinePropertiesChanged += OnSessionChanged;
            }
        }
        _ = RefreshAsync();
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession s, object args) => OnUi(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        var session = _session;
        if (session == null)
        {
            Title = Artist = Album = "";
            Playing = false;
            Cover = null;
            _trackKey = "";
            Changed?.Invoke();
            return;
        }
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var info = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            if (session != _session) return;  // switched apps meanwhile

            Title = props?.Title ?? "";
            Artist = props?.Artist ?? "";
            Album = props?.AlbumTitle ?? "";
            Playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
            _position = timeline.Position.TotalSeconds;
            _positionAt = timeline.LastUpdatedTime.UtcDateTime;

            var key = $"{session.SourceAppUserModelId}|{Title}|{Artist}";
            if (key != _trackKey)
            {
                var sameApp = _trackKey.StartsWith(session.SourceAppUserModelId + "|");
                _trackKey = key;
                Cover = await LoadCoverAsync(props);
                if (sameApp && Title.Length > 0) TrackChanged?.Invoke();
            }
        }
        catch
        {
            // An app can vanish mid-read; the next event refreshes again.
        }
        Changed?.Invoke();
    }

    private static async Task<BitmapSource?> LoadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        if (props?.Thumbnail == null) return null;
        try
        {
            using var stream = await props.Thumbnail.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 160;  // the biggest it's ever shown is 58 px
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    public void PlayPause() => Send(s => s.TryTogglePlayPauseAsync().AsTask());
    public void Next() => Send(s => s.TrySkipNextAsync().AsTask());
    public void Previous() => Send(s => s.TrySkipPreviousAsync().AsTask());

    public void Seek(double seconds)
    {
        if (_session == null) return;
        _position = seconds;
        _positionAt = DateTime.UtcNow;
        Send(s => s.TryChangePlaybackPositionAsync((long)(seconds * TimeSpan.TicksPerSecond)).AsTask());
    }

    /// <summary>
    /// A command to the player. The app can close mid-command; that must not take the
    /// notch down with it (an exception in an async void would end the process).
    /// </summary>
    private async void Send(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_session is not { } session) return;
        try { await command(session); }
        catch { }
    }

    /// <summary>Show this app's music (until another app starts playing).</summary>
    public void Pin(string sourceId)
    {
        _pinned = sourceId;
        Select();
    }

    /// <summary>"Spotify.exe" → "Spotify", Chrome/Edge/Firefox ids → their names.</summary>
    public static string AppName(string sourceId)
    {
        var id = sourceId.ToLowerInvariant();
        if (id.Contains("spotify")) return "Spotify";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
        if (id.Contains("opera")) return "Opera";
        if (id.Contains("zunemusic") || id.Contains("wmplayer")) return "Media Player";
        var name = sourceId.Split('!')[0].Split('\\').Last();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
