using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NotchApp.Settings;

public sealed class TabSetting
{
    public string Id { get; set; } = "";
    public bool On { get; set; } = true;
}

public sealed class AppEntry
{
    public string Name { get; set; } = "";
    /// <summary>Path to an app, file or folder, or a URL (including ms-settings: links).</summary>
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "app";   // app | folder | web
}

public sealed class TimerSettings
{
    public int Focus { get; set; } = 25;
    public int ShortBreak { get; set; } = 5;
    public int LongBreak { get; set; } = 15;
    public int Sessions { get; set; } = 4;
    public bool AutoStart { get; set; } = true;
}

/// <summary>Everything in Settings, saved as JSON in %AppData%\Notch\settings.json.</summary>
public sealed class AppSettings
{
    // Display
    /// <summary>"main", "cursor", or a monitor device name such as \\.\DISPLAY2.</summary>
    public string Display { get; set; } = "main";
    public string Position { get; set; } = "center";      // left | center | right
    public string Style { get; set; } = "floating";       // floating | attached
    public double IdleWidth { get; set; } = 124;
    public double HubWidth { get; set; } = 600;
    public double CornerRadius { get; set; } = 32;

    // General
    public bool OpenOnHover { get; set; } = true;
    public int HoverDelayMs { get; set; } = 120;
    public bool PullToOpen { get; set; } = true;
    public bool ScrollForVolume { get; set; } = true;
    public bool SwipeToSkip { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool HideInFullscreen { get; set; } = true;
    public string DefaultTab { get; set; } = "auto";

    // Appearance & motion
    public string Feel { get; set; } = "fluid";
    public bool ImpactBounce { get; set; } = true;
    public bool LeanTowardCursor { get; set; } = true;
    public double Glow { get; set; } = 0.5;
    public bool BeatPulse { get; set; } = true;

    // Hub & activities
    public List<TabSetting> Tabs { get; set; } = DefaultTabs();
    public Dictionary<string, bool> Live { get; set; } = DefaultLive();

    public TimerSettings Timer { get; set; } = new();

    // Clipboard & shelf
    public int ClipMax { get; set; } = 20;
    public bool ClipSkipSecrets { get; set; } = true;
    public string ShelfKeep { get; set; } = "forever";   // forever | day | week

    // Notifications
    /// <summary>Show the message itself; off shows only which app it's from (for screen sharing).</summary>
    public bool NotifShowText { get; set; } = true;
    public int NotifSeconds { get; set; } = 5;
    /// <summary>Keep notifications out of the notch while Windows Do not disturb is on.</summary>
    public bool NotifQuietInDnd { get; set; } = true;
    /// <summary>App names that never drop into the notch (they still appear in the Alerts list).</summary>
    public List<string> NotifMuted { get; set; } = [];

    // Apps
    public List<AppEntry> Apps { get; set; } = NotchApp.Services.AppLauncher.Defaults();

    public static List<TabSetting> DefaultTabs() =>
        [new() { Id = "now" }, new() { Id = "notif" }, new() { Id = "clip" }, new() { Id = "shelf" }, new() { Id = "dev" }, new() { Id = "apps" }, new() { Id = "system" }];

    public static Dictionary<string, bool> DefaultLive() => new()
    {
        ["music"] = true, ["timer"] = true, ["notif"] = true, ["charge"] = true, ["volume"] = true, ["memory"] = true, ["timerDone"] = true,
        ["copy"] = true, ["files"] = true, ["device"] = true, ["launch"] = true,
    };

    public bool IsLive(string key) => !Live.TryGetValue(key, out var on) || on;

    // ---------- persistence ----------
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                s.Normalize();
                return s;
            }
        }
        catch { /* a damaged file falls back to defaults */ }
        return new AppSettings();
    }

    /// <summary>Older or hand-edited files pick up anything new and lose anything unknown.</summary>
    private void Normalize()
    {
        var known = DefaultTabs();
        Tabs = Tabs.Where(t => known.Any(k => k.Id == t.Id)).GroupBy(t => t.Id).Select(g => g.First()).ToList();
        // a tab added in a newer version goes in its default place, not at the end
        for (int i = 0; i < known.Count; i++)
            if (Tabs.All(t => t.Id != known[i].Id)) Tabs.Insert(Math.Min(i, Tabs.Count), known[i]);
        Apps ??= NotchApp.Services.AppLauncher.Defaults();
        if (!Tabs.Any(t => t.On)) Tabs[0].On = true;
        foreach (var (k, v) in DefaultLive()) Live.TryAdd(k, v);
        Timer ??= new TimerSettings();
        NotifMuted ??= [];
        NotifSeconds = Math.Clamp(NotifSeconds, 2, 15);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings stay in memory for this run */ }
        ApplyStartWithWindows();
    }

    private void ApplyStartWithWindows()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run == null) return;
            if (StartWithWindows && Environment.ProcessPath is { } exe) run.SetValue("Notch", $"\"{exe}\"");
            else run.DeleteValue("Notch", throwOnMissingValue: false);
        }
        catch { }
    }
}
