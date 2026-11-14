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

    protected override async void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "Notch.SingleInstance.v1", out bool first);
        if (!first) { Shutdown(); return; }   // already running: the tray icon is the way in
        base.OnStartup(e);

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
        _notch.ApplyLaunchArgs(e.Args);

        CreateTray();
        await media.InitAsync();
        await devices.InitAsync();
        await notifs.InitAsync();
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

    /// <summary>A small black pill on a light outline, drawn at runtime so the app ships without image files.</summary>
    private static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var path = new Drawing.Drawing2D.GraphicsPath();
            var r = new Drawing.RectangleF(3, 10, 26, 12);
            path.AddArc(r.X, r.Y, 12, 12, 90, 180);
            path.AddArc(r.Right - 12, r.Y, 12, 12, 270, 180);
            path.CloseFigure();
            using var fill = new Drawing.SolidBrush(Drawing.Color.Black);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(230, 255, 255, 255), 2f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 255, 181, 71));
            g.FillEllipse(dot, 21, 14, 4, 4);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
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
