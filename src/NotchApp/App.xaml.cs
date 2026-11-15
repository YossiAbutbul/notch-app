using System.Windows;
using NotchApp.Services;
using NotchApp.Settings;
using NotchApp.Views;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace NotchApp;

public partial class App : Application
{
    private Mutex? _single;
    private Forms.NotifyIcon? _tray;
    private NotchWindow? _notch;
    private SettingsWindow? _settings;
    private AppSettings _cfg = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!IsSupportedWindows())
        {
            MessageBox.Show(
                "Notch needs Windows 10 version 2004 or later.\n\nUpdate Windows from Settings > Windows Update, then run Notch again.",
                "Notch", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
#if PACKAGED
        // the published Notch.exe is its own installer and uninstaller
        if (e.Args.Contains("--uninstall"))
        {
            var u = new SetupWindow(SetupMode.Uninstall);
            u.Closed += (_, _) => Shutdown();
            u.Show();
            return;
        }
        if (!Installer.IsRunningInstalled && !e.Args.Contains("--portable"))
        {
            var w = new SetupWindow(Installer.IsInstalled ? SetupMode.Update : SetupMode.Install);
            bool portable = false;
            w.RunPortable += () => portable = true;
            w.Closed += (_, _) => { if (portable) StartApp(e.Args); else Shutdown(); };
            w.Show();
            return;
        }
#endif
        StartApp(e.Args);
    }

    /// <summary>Media sessions, notifications and the other WinRT APIs Notch uses need Windows 10 2004 (build 19041).</summary>
    private static bool IsSupportedWindows() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    private async void StartApp(string[] args)
    {
        _single = new Mutex(true, "Notch.SingleInstance.v1", out bool first);
        if (!first) { Shutdown(); return; }   // already running: the tray icon is the way in
        ListenForQuit();

        _cfg = AppSettings.Load();
        var media = new MediaService();
        var timer = new FocusTimer(_cfg.Timer);
        var stats = new SystemStats();
        var clip = new ClipboardHistory { Max = _cfg.ClipMax, SkipSecrets = _cfg.ClipSkipSecrets };
        var shelf = new ShelfStore { Keep = _cfg.ShelfKeep };
        shelf.Load();

        var devices = new DevicesService();
        var notifs = new NotificationsService();

        _notch = new NotchWindow(_cfg, media, timer, stats, clip, shelf, devices, notifs);
        _notch.SettingsRequested += OpenSettings;
        _notch.TimerEdited += () => _settings?.Refresh("timer");
        notifs.Changed += () => _settings?.Refresh("notif");
        _notch.Show();
        _notch.ApplyLaunchArgs(args);

        CreateTray();
        await media.InitAsync();
        await devices.InitAsync();
        await notifs.InitAsync();
    }

    /// <summary>An installer or uninstaller asks the running copy to step aside through a named event.</summary>
    private void ListenForQuit()
    {
        try
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.QuitEventName);
            var t = new Thread(() => { ev.WaitOne(); Dispatcher.Invoke(Quit); }) { IsBackground = true, Name = "Notch quit listener" };
            t.Start();
        }
        catch { }
    }

    private void OpenSettings(string? section)
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(_cfg, _notch!);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.ShowSection(section);
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => BuildMenu(menu);
        _tray = new Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Notch",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) OpenSettings(null); };
        BuildMenu(menu);
    }

    private void BuildMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings(null));
        var show = new Forms.ToolStripMenuItem("Show on");
        void Add(string id, string label)
        {
            var it = new Forms.ToolStripMenuItem(label) { Checked = _cfg.Display == id };
            it.Click += (_, _) => _notch?.ShowOnDisplay(id);
            show.DropDownItems.Add(it);
        }
        Add("main", "Main display");
        foreach (var d in Displays.All().Where(d => !d.Primary)) Add(d.Id, d.Name);
        Add("cursor", "Follow my cursor");
        menu.Items.Add(show);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Notch", null, (_, _) => Quit());
    }

    /// <summary>The app icon, at the tray's size for this display.</summary>
    private static Drawing.Icon MakeIcon()
    {
        var info = GetResourceStream(new Uri("pack://application:,,,/Assets/Notch.ico"));
        int size = Forms.SystemInformation.SmallIconSize.Width;
        return new Drawing.Icon(info.Stream, size, size);
    }

    private void Quit()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
