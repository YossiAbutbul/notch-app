using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchApp.Services;

public sealed class ShelfItem
{
    public string Path { get; set; } = "";
    public DateTime Added { get; set; } = DateTime.Now;

    [JsonIgnore] public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } n ? n : Path;
    [JsonIgnore] public bool IsFolder => Directory.Exists(Path);
    [JsonIgnore] public string Ext => IsFolder ? "" : System.IO.Path.GetExtension(Path).TrimStart('.').ToLowerInvariant();
    [JsonIgnore] public bool IsImage => Ext is "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "tif" or "tiff" or "ico";
    [JsonIgnore] public long Size { get; set; }
    [JsonIgnore] public ImageSource? Preview { get; set; }
}

/// <summary>
/// Files held on the Shelf. The shelf keeps a reference to each file (nothing is moved), remembers them across
/// restarts, and "Save all" copies them into Downloads\Shelf.
/// </summary>
public sealed class ShelfStore
{
    private static readonly string FilePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch", "shelf.json");
    public static readonly string SaveFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Shelf");

    public List<ShelfItem> Items { get; } = [];
    public event Action? Changed;

    /// <summary>"forever", "day" or "week".</summary>
    public string Keep { get; set; } = "forever";

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                foreach (var i in JsonSerializer.Deserialize<List<ShelfItem>>(File.ReadAllText(FilePath)) ?? []) Items.Add(i);
        }
        catch { }
        Prune(save: false);
        foreach (var i in Items) Describe(i);
    }

    /// <summary>Drop files that were deleted or moved, and anything older than the keep setting.</summary>
    public void Prune(bool save = true)
    {
        var limit = Keep switch { "day" => TimeSpan.FromDays(1), "week" => TimeSpan.FromDays(7), _ => TimeSpan.MaxValue };
        int before = Items.Count;
        Items.RemoveAll(i => !(File.Exists(i.Path) || Directory.Exists(i.Path)) || (limit != TimeSpan.MaxValue && DateTime.Now - i.Added > limit));
        if (save && Items.Count != before) { Save(); Changed?.Invoke(); }
    }

    public int Add(IEnumerable<string> paths)
    {
        int n = 0;
        foreach (var p in paths)
        {
            if (!(File.Exists(p) || Directory.Exists(p))) continue;
            if (Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;   // already here
            var item = new ShelfItem { Path = p };
            Describe(item);
            Items.Insert(0, item);
            n++;
        }
        if (n > 0) { Save(); Changed?.Invoke(); }
        return n;
    }

    public void Remove(ShelfItem item)
    {
        Items.Remove(item);
        Save();
        Changed?.Invoke();
    }

    public void Clear()
    {
        Items.Clear();
        Save();
        Changed?.Invoke();
    }

    public long TotalSize => Items.Sum(i => i.Size);

    /// <summary>Copies everything into Downloads\Shelf, never overwriting: a clash gets " (2)" and so on.</summary>
    public async Task<int> SaveAllAsync()
    {
        var items = Items.ToList();
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(SaveFolder);
            int n = 0;
            foreach (var i in items)
            {
                try
                {
                    var dest = Unique(System.IO.Path.Combine(SaveFolder, i.Name));
                    if (i.IsFolder) CopyDir(i.Path, dest); else File.Copy(i.Path, dest);
                    n++;
                }
                catch { }
            }
            return n;
        });
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = System.IO.Path.GetDirectoryName(path)!, name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
        for (int k = 2; ; k++)
        {
            var p = System.IO.Path.Combine(dir, $"{name} ({k}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(d)));
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Items));
        }
        catch { }
    }

    /// <summary>Size, plus a small preview: the picture itself for images, the Windows file icon otherwise.</summary>
    private static void Describe(ShelfItem i)
    {
        try { i.Size = i.IsFolder ? 0 : new FileInfo(i.Path).Length; } catch { }
        try
        {
            if (i.IsImage)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 72;
                bmp.UriSource = new Uri(i.Path);
                bmp.EndInit();
                bmp.Freeze();
                i.Preview = bmp;
            }
            else if (!i.IsFolder)
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(i.Path);
                if (ico != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    i.Preview = src;
                }
            }
        }
        catch { }
    }

    public static string Bytes(long b) => b >= 1_000_000_000 ? $"{b / 1e9:0.0} GB" : b >= 1_000_000 ? $"{b / 1e6:0.0} MB" : $"{Math.Max(1, b / 1000)} KB";
}
