using WinScreen = System.Windows.Forms.Screen;

namespace NotchApp.Services;

public sealed record DisplayInfo(string Id, string Name, bool Primary, System.Drawing.Rectangle Bounds);

/// <summary>Connected monitors, in left-to-right order, and which one the notch should live on.</summary>
public static class Displays
{
    public static IReadOnlyList<DisplayInfo> All()
    {
        var screens = WinScreen.AllScreens.OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();
        return screens.Select((s, i) => new DisplayInfo(s.DeviceName, $"Display {i + 1}{(s.Primary ? " (main)" : "")}", s.Primary, s.Bounds)).ToList();
    }

    public static DisplayInfo Primary => All().First(d => d.Primary);

    public static DisplayInfo Under(System.Drawing.Point p)
    {
        var s = WinScreen.FromPoint(p);
        return All().FirstOrDefault(d => d.Id == s.DeviceName) ?? Primary;
    }

    /// <summary>"main", "cursor", or a device name; a monitor that was unplugged falls back to the main display.</summary>
    public static DisplayInfo Resolve(string setting, System.Drawing.Point cursor) => setting switch
    {
        "main" => Primary,
        "cursor" => Under(cursor),
        _ => All().FirstOrDefault(d => d.Id == setting) ?? Primary,
    };
}
