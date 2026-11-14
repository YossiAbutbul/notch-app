using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchApp.Settings;

namespace NotchApp.Services;

/// <summary>Launcher items: what the Apps tab starts with, their icons, and how each kind opens.</summary>
public static class AppLauncher
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A sensible first set, using only things that exist on this PC.</summary>
    public static List<AppEntry> Defaults()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var list = new List<AppEntry>
        {
            new() { Name = "File Explorer", Target = Path.Combine(win, "explorer.exe"), Kind = "app" },
            new() { Name = "Downloads", Target = downloads, Kind = "folder" },
            new() { Name = "Terminal", Target = Path.Combine(apps, "wt.exe"), Kind = "app" },
            new() { Name = "Settings", Target = "ms-settings:", Kind = "web" },
            new() { Name = "Calculator", Target = Path.Combine(sys, "calc.exe"), Kind = "app" },
            new() { Name = "Notepad", Target = File.Exists(Path.Combine(sys, "notepad.exe")) ? Path.Combine(sys, "notepad.exe") : Path.Combine(apps, "notepad.exe"), Kind = "app" },
        };
        return list.Where(a => a.Kind == "web" || File.Exists(a.Target) || Directory.Exists(a.Target)).ToList();
    }

    /// <summary>Turn a dropped or picked path (exe, shortcut, any file, folder) into a launcher item.</summary>
    public static AppEntry FromPath(string path)
    {
        bool dir = Directory.Exists(path);
        string name = dir ? Path.GetFileName(path.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = path;
        return new AppEntry { Name = name, Target = path, Kind = dir ? "folder" : "app" };
    }

    public static AppEntry Website(string name, string url)
    {
        url = url.Trim();
        if (!url.Contains("://") && !url.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (string.IsNullOrWhiteSpace(name))
            name = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : url;
        return new AppEntry { Name = name.Trim(), Target = url, Kind = "web" };
    }

    public static bool Launch(AppEntry a)
    {
        try
        {
            Process.Start(new ProcessStartInfo(a.Target) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>The real Windows icon for apps and files; null for folders and websites (drawn as tiles instead).</summary>
    public static ImageSource? IconFor(AppEntry a)
    {
        if (a.Kind != "app") return null;
        // Store-app aliases (…\WindowsApps\x.exe) only expose a blank generic icon: draw a tile instead
        if (a.Target.Contains(@"\Microsoft\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return null;
        if (IconCache.TryGetValue(a.Target, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            if (File.Exists(a.Target))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(a.Target);
                if (ico != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    img = src;
                }
            }
        }
        catch { }
        IconCache[a.Target] = img;
        return img;
    }

    /// <summary>A stable colour per name for tiles without an icon.</summary>
    public static double Hue(string name)
    {
        uint h = 2166136261;
        foreach (var ch in name) h = (h ^ ch) * 16777619;
        return h % 360;
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
