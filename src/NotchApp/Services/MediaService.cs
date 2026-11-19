using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;

namespace NotchApp.Services;

/// <summary>
/// What's playing anywhere on the PC (Spotify, browsers, Media Player…) through Windows' media session API.
/// All events are raised on the UI thread.
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _s;
    private readonly System.Windows.Threading.Dispatcher _ui = Application.Current.Dispatcher;
    private int _refreshId;

    public event Action? Changed;

    public bool HasSession => _s != null && Title.Length > 0;
    public bool IsPlaying { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public BitmapSource? Art { get; private set; }
    /// <summary>A vivid colour taken from the album art, for the glow.</summary>
    public Color ArtColor { get; private set; } = Color.FromRgb(255, 98, 120);
    public TimeSpan Duration { get; private set; }
    private TimeSpan _pos;
    private DateTime _posAt = DateTime.UtcNow;

    public TimeSpan Position
    {
        get
        {
            var p = IsPlaying ? _pos + (DateTime.UtcNow - _posAt) : _pos;
            return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
        }
    }

    public async Task InitAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(Attach);
            Attach();
        }
        catch { /* no media session support: the music view simply never appears */ }
    }

    private void Attach()
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnPlayback;
            _s.TimelinePropertiesChanged -= OnTimeline;
        }
        _s = _mgr?.GetCurrentSession();
        if (_s == null)
        {
            Title = Artist = ""; Art = null; IsPlaying = false;
            Changed?.Invoke();
            return;
        }
        _s.MediaPropertiesChanged += OnProps;
        _s.PlaybackInfoChanged += OnPlayback;
        _s.TimelinePropertiesChanged += OnTimeline;
        _ = RefreshPropsAsync();
        ReadPlayback();
        ReadTimeline();
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ui.InvokeAsync(() => _ = RefreshPropsAsync());
    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ui.InvokeAsync(() => { ReadPlayback(); Changed?.Invoke(); });
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ui.InvokeAsync(() => { ReadTimeline(); Changed?.Invoke(); });

    private void ReadPlayback()
    {
        var info = _s?.GetPlaybackInfo();
        var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playing != IsPlaying) { _pos = Position; _posAt = DateTime.UtcNow; }
        IsPlaying = playing;
    }

    private void ReadTimeline()
    {
        var t = _s?.GetTimelineProperties();
        if (t == null) return;
        Duration = t.EndTime - t.StartTime;
        _pos = t.Position;
        _posAt = t.LastUpdatedTime.UtcDateTime > DateTime.MinValue.AddYears(1) ? t.LastUpdatedTime.UtcDateTime : DateTime.UtcNow;
    }

    private async Task RefreshPropsAsync()
    {
        var s = _s;
        if (s == null) return;
        int id = ++_refreshId;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (id != _refreshId) return;
            Title = p?.Title ?? "";
            Artist = p?.Artist ?? "";
            BitmapSource? art = null;
            Color col = ArtColor;
            if (p?.Thumbnail != null)
            {
                using var ras = await p.Thumbnail.OpenReadAsync();
                using var src = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await src.CopyToAsync(ms);
                ms.Position = 0;
                (art, col) = await Task.Run(() => Decode(ms));
            }
            if (id != _refreshId) return;
            Art = art;
            if (art != null) ArtColor = col;
            ReadPlayback();
            ReadTimeline();
            Changed?.Invoke();
        }
        catch { /* some players expose no properties */ }
    }

    private static (BitmapSource?, Color) Decode(MemoryStream ms)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return (bmp, Vivid(bmp));
        }
        catch { return (null, Color.FromRgb(255, 98, 120)); }
    }

    /// <summary>Average the art, weighted toward saturated pixels, then lift it so it reads as a glow.</summary>
    private static Color Vivid(BitmapSource src)
    {
        var small = new FormatConvertedBitmap(new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight)), PixelFormats.Bgra32, null, 0);
        int w = small.PixelWidth, h = small.PixelHeight;
        var px = new byte[w * h * 4];
        small.CopyPixels(px, w * 4, 0);
        double r = 0, g = 0, b = 0, wt = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double B = px[i] / 255.0, G = px[i + 1] / 255.0, R = px[i + 2] / 255.0;
            double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double sat = max == 0 ? 0 : (max - min) / max;
            double weight = 0.05 + sat * sat * max;
            r += R * weight; g += G * weight; b += B * weight; wt += weight;
        }
        r /= wt; g /= wt; b /= wt;
        // to HSV, push saturation and value up
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
        double hue = d == 0 ? 0 : mx == r ? 60 * (((g - b) / d) % 6) : mx == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (hue < 0) hue += 360;
        double s2 = Math.Max(0.55, mx == 0 ? 0 : d / mx), v2 = Math.Max(0.9, mx);
        double c = v2 * s2, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v2 - c;
        (double rr, double gg, double bb) = hue switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((rr + m) * 255), (byte)((gg + m) * 255), (byte)((bb + m) * 255));
    }

    /// <summary>Whether the player lets other apps move its position (many browsers and streams don't).</summary>
    public bool CanSeek
    {
        get { try { return Duration > TimeSpan.Zero && _s?.GetPlaybackInfo()?.Controls?.IsPlaybackPositionEnabled == true; } catch { return false; } }
    }

    public async void Seek(TimeSpan to)
    {
        var s = _s;
        if (s == null) return;
        to = to < TimeSpan.Zero ? TimeSpan.Zero : to > Duration ? Duration : to;
        // show it moved right away; the player's timeline update confirms (or corrects) it
        _pos = to; _posAt = DateTime.UtcNow;
        Changed?.Invoke();
        try
        {
            var start = s.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
            await s.TryChangePlaybackPositionAsync((start + to).Ticks);
        }
        catch { }
    }

    public async void PlayPause() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }
}
