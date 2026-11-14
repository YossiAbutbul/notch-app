using System.IO;

namespace NotchApp.Services;

/// <summary>A tiny diagnostics log at %AppData%\Notch\log.txt, capped so it never grows large.</summary>
public static class Log
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch", "log.txt");
    private static readonly object Gate = new();

    public static void Error(string where, Exception ex) => Write($"{where}: {ex.GetType().Name}: {ex.Message}");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 256 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
