using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchApp.Interop;

namespace NotchApp.Services;

public enum ClipKind { Text, Link, Color, Code, Image }

public sealed class ClipItem
{
    public int Id { get; set; }
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public BitmapSource? Image { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public BitmapSource? Thumb { get; set; }
}

/// <summary>
/// Clipboard history. Listens for clipboard changes on the notch's window, keeps the latest items in memory,
/// and saves only pinned text to disk. Copies that password managers mark as private are never recorded.
/// </summary>
public sealed partial class ClipboardHistory
{
    private static readonly string PinsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch", "clipboard-pins.json");
    private int _nextId = 1;
    private DateTime _suppressUntil = DateTime.MinValue;

    public List<ClipItem> Items { get; } = [];
    public int Max { get; set; } = 20;
    public bool SkipSecrets { get; set; } = true;

    public event Action? Changed;
    /// <summary>A new copy was recorded (for the pop-up).</summary>
    public event Action<ClipItem>? Captured;

    public ClipboardHistory()
    {
        try
        {
            if (File.Exists(PinsFile))
                foreach (var p in JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(PinsFile)) ?? [])
                {
                    p.Id = _nextId++;
                    p.Pinned = true;
                    Items.Add(p);
                }
        }
        catch { }
    }

    public void Attach(IntPtr hwnd) => Native.AddClipboardFormatListener(hwnd);

    /// <summary>Call on WM_CLIPBOARDUPDATE. Reads after a short pause so the copying app has finished writing.</summary>
    public async void OnClipboardUpdate()
    {
        if (DateTime.UtcNow < _suppressUntil) return;    // our own copy-back
        string source = ForegroundApp();
        await Task.Delay(60);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Read(source); return; }
            catch (COMException) { await Task.Delay(120); }  // another app has the clipboard open
            catch { return; }
        }
    }

    private void Read(string source)
    {
        var data = Clipboard.GetDataObject();
        if (data == null) return;
        if (SkipSecrets && IsPrivate(data)) return;

        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = (data.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
            if (text.Length == 0) return;
            Add(new ClipItem { Kind = Detect(text), Text = text, Source = source });
        }
        else if (Clipboard.ContainsImage())
        {
            var img = Clipboard.GetImage();
            if (img == null) return;
            img.Freeze();
            Add(new ClipItem { Kind = ClipKind.Image, Text = $"Image · {img.PixelWidth} × {img.PixelHeight}", Source = source, Image = img, Thumb = MakeThumb(img) });
        }
    }

    /// <summary>Formats Windows defines for "don't record this": password managers set them.</summary>
    private static bool IsPrivate(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return true;
        foreach (var f in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (!data.GetDataPresent(f)) continue;
            if (data.GetData(f) is MemoryStream ms && ms.Length >= 4)
            {
                var b = new byte[4];
                ms.Position = 0;
                ms.ReadExactly(b);
                if (BitConverter.ToInt32(b) == 0 && f == "CanIncludeInClipboardHistory") return true;
            }
        }
        return false;
    }

    private void Add(ClipItem item)
    {
        if (item.Kind != ClipKind.Image)
        {
            var dup = Items.FirstOrDefault(i => i.Kind != ClipKind.Image && i.Text == item.Text);
            if (dup != null)
            {
                Items.Remove(dup);
                dup.At = DateTime.Now;
                dup.Source = item.Source;
                item = dup;
            }
        }
        item.Id = item.Id == 0 ? _nextId++ : item.Id;
        Items.Insert(0, item);
        Trim();
        Changed?.Invoke();
        Captured?.Invoke(item);
    }

    private void Trim()
    {
        var keep = Items.Where(i => i.Pinned).Concat(Items.Where(i => !i.Pinned).Take(Max)).ToHashSet();
        Items.RemoveAll(i => !keep.Contains(i));
        // pinned first, newest first within each group
        var sorted = Items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.At).ToList();
        Items.Clear();
        Items.AddRange(sorted);
    }

    /// <summary>Example items for checking the layout (launch option --sample); the system clipboard is not touched.</summary>
    /// <summary>An example image (for --sample), as if it had been copied from <paramref name="source"/>.</summary>
    public void AddSampleImage(BitmapSource img, string source, int minsAgo)
    {
        img.Freeze();
        Items.Add(new ClipItem { Id = _nextId++, Kind = ClipKind.Image, Text = $"Image · {img.PixelWidth} × {img.PixelHeight}", Source = source, Image = img, Thumb = MakeThumb(img), At = DateTime.Now.AddMinutes(-minsAgo) });
        Items.Sort((a, b) => b.At.CompareTo(a.At));
        Changed?.Invoke();
    }

    public void AddSamples()
    {
        var now = DateTime.Now;
        foreach (var (text, src, mins) in new[]
        {
            ("Ship the notch build on Friday after QA signs off", "Notes", 2),
            ("dotnet build -c Release", "Terminal", 6),
            ("https://learn.microsoft.com/windows/apps/", "Microsoft Edge", 14),
            ("#FF6D45", "Figma", 31),
        })
            Items.Add(new ClipItem { Id = _nextId++, Kind = Detect(text), Text = text, Source = src, At = now.AddMinutes(-mins) });
        Trim();
        Changed?.Invoke();
    }

    /// <summary>Put an item back on the clipboard without recording it again.</summary>
    public bool CopyBack(ClipItem item)
    {
        _suppressUntil = DateTime.UtcNow.AddMilliseconds(600);
        try
        {
            if (item.Kind == ClipKind.Image && item.Image != null) Clipboard.SetImage(item.Image);
            else Clipboard.SetText(item.Text);
            return true;
        }
        catch { return false; }
    }

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        Trim();
        SavePins();
        Changed?.Invoke();
    }

    public void Remove(ClipItem item)
    {
        Items.Remove(item);
        if (item.Pinned) SavePins();
        Changed?.Invoke();
    }

    public void ClearUnpinned()
    {
        Items.RemoveAll(i => !i.Pinned);
        Changed?.Invoke();
    }

    public void ApplyMax(int max)
    {
        Max = max;
        Trim();
        Changed?.Invoke();
    }

    private void SavePins()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PinsFile)!);
            File.WriteAllText(PinsFile, JsonSerializer.Serialize(Items.Where(i => i.Pinned && i.Kind != ClipKind.Image).ToList()));
        }
        catch { }
    }

    // ---------------------------------------------------------------- helpers

    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)] private static partial Regex LinkRx();
    [GeneratedRegex(@"^#([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.IgnoreCase)] private static partial Regex ColorRx();
    [GeneratedRegex(@"^(\$ |npm |dotnet |git |cd |winget |pip |py |python )|[{};]\s*$|=>|\(\)|^\s*(public|private|const|let|var|function|def|class|import|using)\s", RegexOptions.Multiline)] private static partial Regex CodeRx();

    public static ClipKind Detect(string s)
    {
        if (LinkRx().IsMatch(s)) return ClipKind.Link;
        if (ColorRx().IsMatch(s)) return ClipKind.Color;
        if (CodeRx().IsMatch(s)) return ClipKind.Code;
        return ClipKind.Text;
    }

    public static Color? ParseColor(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); } catch { return null; }
    }

    private static BitmapSource MakeThumb(BitmapSource img)
    {
        double scale = 64.0 / Math.Max(img.PixelWidth, img.PixelHeight);
        var t = new TransformedBitmap(img, new ScaleTransform(Math.Min(1, scale), Math.Min(1, scale)));
        t.Freeze();
        return t;
    }

    /// <summary>The app that was in front when the copy happened, by its friendly name where Windows gives one.</summary>
    private static string ForegroundApp()
    {
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            using var p = Process.GetProcessById((int)pid);
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) return desc!;
            }
            catch { }
            return p.ProcessName;
        }
        catch { return ""; }
    }

    public static string Ago(DateTime t)
    {
        var m = (DateTime.Now - t).TotalMinutes;
        return m < 1 ? "just now" : m < 60 ? $"{(int)m} min ago" : m < 1440 ? $"{(int)(m / 60)} h ago" : t.ToString("d MMM");
    }
}
