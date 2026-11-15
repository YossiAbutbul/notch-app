using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace NotchApp.Services;

/// <summary>
/// Notch installs itself: the published Notch.exe copies itself to %LocalAppData%\Programs\Notch (per user, no admin),
/// adds Start menu (and optionally desktop) shortcuts, registers in Settings → Apps for uninstalling, and can start
/// with Windows. Running a newer Notch.exe updates the installed copy, closing the running one first.
/// </summary>
public static class Installer
{
    public const string QuitEventName = "Notch.Quit.v1";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Notch";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Notch");
    public static string Exe => Path.Combine(Dir, "Notch.exe");
    private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Notch.lnk");
    private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Notch.lnk");

    public static Version Current => typeof(Installer).Assembly.GetName().Version ?? new Version(1, 0, 0);
    public static bool IsRunningInstalled =>
        Environment.ProcessPath is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);
    public static bool IsInstalled => File.Exists(Exe);
    public static bool HasDesktopShortcut => File.Exists(DesktopLink);

    public static Version? InstalledVersion
    {
        get
        {
            try { return IsInstalled && Version.TryParse(FileVersionInfo.GetVersionInfo(Exe).FileVersion, out var v) ? v : null; }
            catch { return null; }
        }
    }

    /// <summary>Where "start with Windows" should point: the installed copy when there is one.</summary>
    public static string? LaunchPath => IsInstalled ? Exe : Environment.ProcessPath;

    public static async Task InstallAsync(bool desktopShortcut, bool startWithWindows, IProgress<string> progress)
    {
        progress.Report("Closing Notch…");
        await StopOthersAsync();

        progress.Report("Copying Notch…");
        Directory.CreateDirectory(Dir);
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find this program's file.");
        await Task.Run(async () =>
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(self, Exe, overwrite: true); return; }
                catch (IOException) when (i < 20) { await Task.Delay(250); }   // the old copy may take a moment to let go
            }
        });

        progress.Report("Adding shortcuts…");
        CreateShortcut(StartMenuLink);
        if (desktopShortcut) CreateShortcut(DesktopLink);
        else TryDelete(DesktopLink);

        progress.Report("Finishing…");
        Register();
        SetStartWithWindows(startWithWindows);
        var cfg = Settings.AppSettings.Load();
        if (cfg.StartWithWindows != startWithWindows) { cfg.StartWithWindows = startWithWindows; cfg.Save(); }
    }

    public static void LaunchInstalled()
    {
        try { Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, WorkingDirectory = Dir }); } catch { }
    }

    /// <summary>Removes shortcuts, registry entries and (after this process exits) the program folder.</summary>
    public static async Task UninstallAsync(bool removeData)
    {
        await StopOthersAsync();
        TryDelete(StartMenuLink);
        TryDelete(DesktopLink);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        SetStartWithWindows(false);
        if (removeData)
        {
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch"), recursive: true); } catch { }
        }
        // the folder holds this very exe: a hidden helper waits for this process to exit, then removes it
        try
        {
            string script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; " +
                            $"Remove-Item -LiteralPath '{Dir.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    /// <summary>Asks any other running Notch to quit (it listens for a named event), then makes sure it's gone.</summary>
    public static async Task StopOthersAsync()
    {
        try { if (EventWaitHandle.TryOpenExisting(QuitEventName, out var ev)) using (ev) ev.Set(); } catch { }
        int me = Environment.ProcessId;
        for (int i = 0; i < 30; i++)
        {
            var others = Process.GetProcessesByName("Notch").Where(p => p.Id != me).ToList();
            if (others.Count == 0) return;
            if (i == 20) foreach (var p in others) { try { p.Kill(); } catch { } }
            await Task.Delay(150);
        }
    }

    public static void SetStartWithWindows(bool on)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run == null) return;
            if (on && LaunchPath is { } exe) run.SetValue("Notch", $"\"{exe}\"");
            else run.DeleteValue("Notch", throwOnMissingValue: false);
        }
        catch { }
    }

    private static void Register()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", "Notch");
        k.SetValue("DisplayVersion", Current.ToString(3));
        k.SetValue("Publisher", "Notch");
        k.SetValue("DisplayIcon", $"{Exe},0");
        k.SetValue("InstallLocation", Dir);
        k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        k.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try { k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord); } catch { }
    }

    private static void CreateShortcut(string path)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = Exe;
        link.WorkingDirectory = Dir;
        link.IconLocation = $"{Exe},0";
        link.Description = "A fluid, Apple-style notch for Windows";
        link.Save();
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
