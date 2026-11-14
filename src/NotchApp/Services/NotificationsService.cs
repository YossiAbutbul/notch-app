using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NotchApp.Services;

public sealed record NotifItem(uint Id, string App, string AppId, string Title, string Body, DateTimeOffset At, ImageSource? Logo, Color Tint);

/// <summary>
/// Reads the notifications other apps post to Windows (with the user's permission) and raises one event per new one.
/// Windows still shows its own banner too; Notch can't hide another app's notification.
/// </summary>
public sealed class NotificationsService
{
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seen = [];
    private readonly Dictionary<string, (ImageSource? Logo, Color Tint)> _logos = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _busy;

    public UserNotificationListenerAccessStatus Status { get; private set; } = UserNotificationListenerAccessStatus.Unspecified;
    public bool Allowed => Status == UserNotificationListenerAccessStatus.Allowed;
    /// <summary>Recent notifications, newest first (kept in memory only).</summary>
    public List<NotifItem> Recent { get; } = [];
    /// <summary>App names seen this run, for the mute list in Settings.</summary>
    public SortedSet<string> SeenApps { get; } = new(StringComparer.OrdinalIgnoreCase);

    public event Action<NotifItem>? Arrived;
    public event Action? Changed;

    /// <summary>Must run on the UI thread (Windows shows its permission prompt the first time).</summary>
    public async Task InitAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            Status = _listener.GetAccessStatus();
            if (Status == UserNotificationListenerAccessStatus.Unspecified)
                Status = await _listener.RequestAccessAsync();
            Log.Write($"Notifications: access {Status}");
            if (!Allowed) { Changed?.Invoke(); return; }
            // what's already in Action Center is old news: don't pop it
            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id);
            _poll.Tick += async (_, _) => await PollAsync();
            _poll.Start();
        }
        catch (Exception ex) { Log.Error("Notifications init", ex); Status = UserNotificationListenerAccessStatus.Denied; }
        Changed?.Invoke();
    }

    /// <summary>Asks again (after the user turns access on in Windows Settings).</summary>
    public async Task RetryAsync()
    {
        _poll.Stop();
        await InitAsync();
    }

    public static void OpenPrivacySettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); } catch { }
    }

    private async Task PollAsync()
    {
        if (_busy || _listener == null) return;
        _busy = true;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var fresh = list.Where(n => !_seen.Contains(n.Id)).OrderBy(n => n.CreationTime).ToList();
            foreach (var n in list) _seen.Add(n.Id);
            foreach (var n in fresh)
            {
                var item = await ToItemAsync(n);
                if (item == null) continue;
                SeenApps.Add(item.App);
                Recent.Insert(0, item);
                if (Recent.Count > 30) Recent.RemoveAt(Recent.Count - 1);
                Arrived?.Invoke(item);
            }
            if (fresh.Count > 0) Changed?.Invoke();
        }
        catch (Exception ex) { Log.Error("Notifications poll", ex); }
        finally { _busy = false; }
    }

    private async Task<NotifItem?> ToItemAsync(UserNotification n)
    {
        try
        {
            var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                        .Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? [];
            string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "App";
            string appId = n.AppInfo?.AppUserModelId ?? "";
            if (string.Equals(app, "Notch", StringComparison.OrdinalIgnoreCase)) return null;
            string title = texts.Count > 0 ? texts[0] : app;
            string body = string.Join(" · ", texts.Skip(1));
            var (logo, tint) = await LogoAsync(n.AppInfo, app);
            return new NotifItem(n.Id, app, appId, title, body, n.CreationTime, logo, tint);
        }
        catch (Exception ex) { Log.Error("Notification read", ex); return null; }
    }

    private async Task<(ImageSource?, Color)> LogoAsync(Windows.ApplicationModel.AppInfo? info, string app)
    {
        if (_logos.TryGetValue(app, out var hit)) return hit;
        ImageSource? img = null;
        try
        {
            if (info?.DisplayInfo != null)
            {
                var stream = await info.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64)).OpenReadAsync();
                using var s = stream.AsStreamForRead();
                var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 92;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
        }
        catch { }
        var tint = img is BitmapSource b ? Vivid(b) : AppLauncher.FromHsl(AppLauncher.Hue(app), 0.6, 0.55);
        return _logos[app] = (img, tint);
    }

    /// <summary>The logo's most saturated colour, for the glow.</summary>
    private static Color Vivid(BitmapSource src)
    {
        try
        {
            var f = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = f.PixelWidth, h = f.PixelHeight;
            var px = new byte[w * h * 4];
            f.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, bl = 0, wt = 0;
            for (int i = 0; i < px.Length; i += 16)
            {
                double a = px[i + 3] / 255.0;
                if (a < 0.5) continue;
                double B = px[i], G = px[i + 1], R = px[i + 2];
                double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
                double sat = max == 0 ? 0 : (max - min) / max;
                double k = a * (0.05 + sat * sat) * (max / 255.0);
                r += R * k; g += G * k; bl += B * k; wt += k;
            }
            if (wt < 0.01) return Color.FromRgb(170, 185, 255);
            return Color.FromRgb((byte)(r / wt), (byte)(g / wt), (byte)(bl / wt));
        }
        catch { return Color.FromRgb(170, 185, 255); }
    }

    /// <summary>Bring the app that sent it to the front (works for apps registered with Windows).</summary>
    public static void OpenApp(NotifItem n)
    {
        if (string.IsNullOrEmpty(n.AppId)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{n.AppId}") { UseShellExecute = true }); } catch { }
    }

    public void Clear() { Recent.Clear(); Changed?.Invoke(); }
}
