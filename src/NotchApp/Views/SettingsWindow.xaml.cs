using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotchApp.Services;
using NotchApp.Settings;
using Icon = NotchApp.Controls.Icon;

namespace NotchApp.Views;

/// <summary>
/// Every setting, grouped like Windows 11 Settings. Each control writes straight into AppSettings,
/// saves, and the notch applies the change live.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _cfg;
    private readonly NotchWindow _notch;
    private string _section = "general";

    private static readonly (string Id, string Label, string Icon)[] Sections =
    [
        ("general", "General", "gear"),
        ("display", "Display", "system"),
        ("hub", "Hub & activities", "now"),
        ("look", "Appearance & motion", "spark"),
        ("timer", "Focus timer", "clock"),
        ("notif", "Notifications", "bell"),
        ("clip", "Clipboard & shelf", "clip"),
        ("apps", "Apps", "apps"),
        ("about", "About", "battery"),
    ];

    private static readonly Dictionary<string, string> TabNames = new()
    {
        ["now"] = "Now playing", ["timer"] = "Focus timer", ["notif"] = "Alerts (notifications)", ["clip"] = "Clipboard", ["shelf"] = "Shelf", ["dev"] = "Devices", ["apps"] = "Apps", ["system"] = "System",
    };
    private static readonly (string Key, string Label, string Desc)[] LiveItems =
    [
        ("music", "Now playing", "Album art and beat bars while music plays"),
        ("timer", "Focus timer", "Countdown while a session runs"),
        ("notif", "Notifications", "Messages and alerts from your apps drop into the notch"),
        ("timerDone", "Session finished", "When a focus session or break ends"),
        ("volume", "Volume", "While you scroll on the notch"),
        ("charge", "Charging", "When you plug in a charger"),
        ("memory", "Memory freed", "After Free up memory"),
        ("copy", "Copied to clipboard", "A short preview of what you copied"),
        ("files", "Files added to Shelf", "After you drop files on the notch"),
        ("device", "Devices connecting", "Earbuds, headphones, keyboards and controllers, with battery"),
        ("launch", "App launching", "When you open something from the Apps tab"),
    ];

    public SettingsWindow(AppSettings cfg, NotchWindow notch)
    {
        _cfg = cfg;
        _notch = notch;
        InitializeComponent();
        foreach (var (id, label, icon) in Sections)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
            sp.Children.Add(new Icon { Kind = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 14, 0) });
            sp.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Nav.Items.Add(new ListBoxItem { Content = sp, Tag = id });
        }
        Nav.SelectionChanged += (_, _) =>
        {
            if (Nav.SelectedItem is ListBoxItem it) { _section = (string)it.Tag; Build(); Scroller.ScrollToTop(); }
        };
        Nav.SelectedIndex = 0;
    }

    /// <summary>Something changed elsewhere (the notch's timer editor): redraw that page if it's the one showing.</summary>
    public void Refresh(string section)
    {
        if (_section != section) return;
        double y = Scroller.VerticalOffset;
        Build();
        Scroller.ScrollToVerticalOffset(y);
    }

    public void ShowSection(string? id)
    {
        int i = Array.FindIndex(Sections, s => s.Id == id);
        if (i >= 0) Nav.SelectedIndex = i;
    }

    private void Changed(bool rebuild = false)
    {
        _cfg.Save();
        _notch.ApplySettings();
        if (rebuild) Build();
    }

    // ---------------------------------------------------------------- page

    private void Build()
    {
        Page.Children.Clear();
        Page.Children.Add(new TextBlock
        {
            Text = Sections.First(s => s.Id == _section).Label,
            FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 20),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        });
        switch (_section)
        {
            case "general": General(); break;
            case "display": Display(); break;
            case "hub": Hub(); break;
            case "look": Look(); break;
            case "timer": Timer(); break;
            case "clip": ClipAndShelf(); break;
            case "notif": Notifications(); break;
            case "apps": AppsPage(); break;
            default: About(); break;
        }
    }

    private void General()
    {
        Group("Opening",
            Row("Open on hover", "Expands when the cursor rests on the notch", Toggle(() => _cfg.OpenOnHover, v => { _cfg.OpenOnHover = v; Changed(true); })),
            Row("Hover delay", "How long the cursor rests before it opens", Slide(0, 400, 10, () => _cfg.HoverDelayMs, v => { _cfg.HoverDelayMs = (int)v; Changed(); }, v => $"{v:0} ms"), enabled: _cfg.OpenOnHover),
            Row("Pull down to open", "Drag the notch down to open it, flick it up to close", Toggle(() => _cfg.PullToOpen, v => { _cfg.PullToOpen = v; Changed(); })),
            Row("When the hub opens, show", null, Combo(
                new[] { ("auto", "What's playing, else last tab") }.Concat(_cfg.Tabs.Where(t => t.On).Select(t => (t.Id, TabNames[t.Id]))).ToArray(),
                () => _cfg.DefaultTab, v => { _cfg.DefaultTab = v; Changed(); })));
        Group("Gestures",
            Row("Scroll on the notch to change volume", null, Toggle(() => _cfg.ScrollForVolume, v => { _cfg.ScrollForVolume = v; Changed(); })),
            Row("Swipe the music pill to skip tracks", null, Toggle(() => _cfg.SwipeToSkip, v => { _cfg.SwipeToSkip = v; Changed(); })));
        Group("System",
            Row("Start with Windows", null, Toggle(() => _cfg.StartWithWindows, v => { _cfg.StartWithWindows = v; Changed(); })),
            Row("Hide when an app is full screen", "Games, videos and presentations stay uninterrupted", Toggle(() => _cfg.HideInFullscreen, v => { _cfg.HideInFullscreen = v; Changed(); })));
    }

    private void Display()
    {
        var displays = Displays.All();
        // arrangement preview, like Windows Settings → Display
        var arr = new Canvas { Height = 150, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
        int minX = displays.Min(d => d.Bounds.Left), minY = displays.Min(d => d.Bounds.Top);
        int maxX = displays.Max(d => d.Bounds.Right), maxY = displays.Max(d => d.Bounds.Bottom);
        double scale = Math.Min(460.0 / (maxX - minX), 130.0 / (maxY - minY));
        arr.Width = (maxX - minX) * scale;
        var selected = Displays.Resolve(_cfg.Display, System.Windows.Forms.Cursor.Position);
        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            bool sel = _cfg.Display != "cursor" && d.Id == selected.Id;
            var mon = new Border
            {
                Width = d.Bounds.Width * scale - 4, Height = d.Bounds.Height * scale - 4, CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(sel ? Color.FromRgb(0x3D, 0x8B, 0xFF) : Color.FromArgb(60, 255, 255, 255)),
                Background = new SolidColorBrush(sel ? Color.FromArgb(40, 0x3D, 0x8B, 0xFF) : Color.FromArgb(14, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = d.Name,
            };
            var g = new Grid();
            g.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new TextBlock { Text = $"{d.Bounds.Width} × {d.Bounds.Height}", FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
            if (sel || _cfg.Display == "cursor")
                g.Children.Add(new Border
                {
                    Width = 26, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White, Opacity = sel ? 1 : 0.45, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 5, 8, 0),
                    HorizontalAlignment = _cfg.Position switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
                });
            mon.Child = g;
            var id = d.Id;
            mon.MouseLeftButtonUp += (_, _) => { _cfg.Display = d.Primary ? "main" : id; Changed(true); };
            Canvas.SetLeft(mon, (d.Bounds.Left - minX) * scale + 2);
            Canvas.SetTop(mon, (d.Bounds.Top - minY) * scale + 2);
            arr.Children.Add(mon);
        }
        var opts = new List<(string, string)> { ("main", "Main display") };
        opts.AddRange(displays.Where(d => !d.Primary).Select(d => (d.Id, d.Name)));
        opts.Add(("cursor", "Follow my cursor"));
        Group("Show the notch on",
            Card(arr),
            Row("Display", displays.Count > 1 ? "Follow my cursor moves it to whichever display you're using" : "Connect another display to choose it here",
                Combo(opts.ToArray(), () => _cfg.Display, v => { _cfg.Display = v; Changed(true); })));
        Group("Placement",
            Row("Position", "Along the top edge of the screen", Seg([("left", "Left"), ("center", "Center"), ("right", "Right")], () => _cfg.Position, v => { _cfg.Position = v; Changed(true); })),
            Row("Style", null, Seg([("floating", "Floating"), ("attached", "Attached to edge")], () => _cfg.Style, v => { _cfg.Style = v; Changed(); })));
        Group("Size",
            Row("Idle width", null, Slide(80, 200, 2, () => _cfg.IdleWidth, v => { _cfg.IdleWidth = v; Changed(); }, v => $"{v:0} px")),
            Row("Expanded width", null, Slide(540, 680, 4, () => _cfg.HubWidth, v => { _cfg.HubWidth = v; Changed(); }, v => $"{v:0} px")),
            Row("Corner radius", null, Slide(14, 44, 1, () => _cfg.CornerRadius, v => { _cfg.CornerRadius = v; Changed(); }, v => $"{v:0} px")));
    }

    private void Hub()
    {
        var rows = new List<UIElement>();
        int on = _cfg.Tabs.Count(t => t.On);
        for (int i = 0; i < _cfg.Tabs.Count; i++)
        {
            var t = _cfg.Tabs[i];
            int j = i;
            var ctl = new StackPanel { Orientation = Orientation.Horizontal };
            var up = new Button { Content = "↑", Width = 36, Margin = new Thickness(0, 0, 4, 0), IsEnabled = i > 0, ToolTip = "Move up" };
            var down = new Button { Content = "↓", Width = 36, Margin = new Thickness(0, 0, 12, 0), IsEnabled = i < _cfg.Tabs.Count - 1, ToolTip = "Move down" };
            up.Click += (_, _) => { (_cfg.Tabs[j - 1], _cfg.Tabs[j]) = (_cfg.Tabs[j], _cfg.Tabs[j - 1]); Changed(true); };
            down.Click += (_, _) => { (_cfg.Tabs[j + 1], _cfg.Tabs[j]) = (_cfg.Tabs[j], _cfg.Tabs[j + 1]); Changed(true); };
            ctl.Children.Add(up);
            ctl.Children.Add(down);
            var tg = Toggle(() => t.On, v => { t.On = v; Changed(true); });
            tg.IsEnabled = !(t.On && on == 1);   // the last visible tab stays
            ctl.Children.Add(tg);
            rows.Add(Row(TabNames[t.Id], null, ctl));
        }
        Group("Hub tabs", rows.ToArray());
        Group("Live activities",
            LiveItems.Select(l => Row(l.Label, l.Desc, Toggle(() => _cfg.IsLive(l.Key), v => { _cfg.Live[l.Key] = v; Changed(); }))).ToArray());
    }

    private void Look()
    {
        Group("Motion",
            Row("Feel", "How springy everything is", Seg([("fluid", "Fluid"), ("snappy", "Snappy"), ("playful", "Playful"), ("calm", "Calm")], () => _cfg.Feel, v => { _cfg.Feel = v; Changed(); })),
            Row("Bounce when things arrive", "Pop-ups and finished sessions", Toggle(() => _cfg.ImpactBounce, v => { _cfg.ImpactBounce = v; Changed(); })),
            Row("Lean toward the cursor", null, Toggle(() => _cfg.LeanTowardCursor, v => { _cfg.LeanTowardCursor = v; Changed(); })));
        Group("Glow",
            Row("Glow strength", null, Slide(0, 1, 0.05, () => _cfg.Glow, v => { _cfg.Glow = v; Changed(); }, v => $"{v * 100:0}%")),
            Row("Pulse with the music", null, Toggle(() => _cfg.BeatPulse, v => { _cfg.BeatPulse = v; Changed(); })));
    }

    private void Timer()
    {
        var t = _cfg.Timer;
        Group("Lengths",
            Row("Focus", null, Step(() => t.Focus, v => { t.Focus = v; Changed(true); }, 1, 1, 180, "min")),
            Row("Short break", null, Step(() => t.ShortBreak, v => { t.ShortBreak = v; Changed(true); }, 1, 1, 30, "min")),
            Row("Long break", null, Step(() => t.LongBreak, v => { t.LongBreak = v; Changed(true); }, 5, 5, 60, "min")),
            Row("Sessions before a long break", null, Step(() => t.Sessions, v => { t.Sessions = v; Changed(true); }, 1, 1, 8, "")),
            Row("Start the next session automatically", null, Toggle(() => t.AutoStart, v => { t.AutoStart = v; Changed(); })));
        Page.Children.Add(Note("Changing the length of the session that's running restarts it at the new length."));
    }

    private void Notifications()
    {
        var svc = _notch.Notifications;
        UIElement access;
        if (svc.Allowed) access = new TextBlock { Text = "Allowed", Opacity = 0.8 };
        else
        {
            var open = new Button { Content = "Open Windows settings" };
            open.Click += (_, _) => NotificationsService.OpenPrivacySettings();
            var retry = new Button { Content = "Try again", Margin = new Thickness(8, 0, 0, 0) };
            retry.Click += async (_, _) => { await svc.RetryAsync(); Build(); };
            access = new StackPanel { Orientation = Orientation.Horizontal, Children = { open, retry } };
        }
        Group("Access",
            Row("Read notifications from other apps", svc.Allowed ? "Windows lets Notch see the notifications your apps post" : "Turn on notification access for Notch in Windows Settings, then try again", access));
        Group("In the notch",
            Row("Show notifications in the notch", "They drop in for a few seconds; click one to open its app", Toggle(() => _cfg.IsLive("notif"), v => { _cfg.Live["notif"] = v; Changed(true); })),
            Row("Show the message", "Off shows only which app it's from, handy when sharing your screen", Toggle(() => _cfg.NotifShowText, v => { _cfg.NotifShowText = v; Changed(); }), enabled: _cfg.IsLive("notif")),
            Row("Stay quiet during Do not disturb", "While Windows Do not disturb or Focus is on, notifications wait in the Alerts tab instead", Toggle(() => _cfg.NotifQuietInDnd, v => { _cfg.NotifQuietInDnd = v; Changed(); }), enabled: _cfg.IsLive("notif")),
            Row("Stay for", null, Step(() => _cfg.NotifSeconds, v => { _cfg.NotifSeconds = v; Changed(); }, 1, 2, 15, "s"), enabled: _cfg.IsLive("notif")));
        Page.Children.Add(Note("Windows still shows its own banner too. Notch can read notifications but can't hide another app's. The Alerts list lives in memory and is gone when Notch quits."));

        var apps = svc.SeenApps.Union(_cfg.NotifMuted, StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (apps.Count == 0)
            Group("Apps", Row("No apps yet", "Apps show up here after they send a notification, so you can keep any of them out of the notch", new TextBlock()));
        else
            Group("Apps", apps.Select(a => Row(a, null, Toggle(() => !_cfg.NotifMuted.Contains(a, StringComparer.OrdinalIgnoreCase), v =>
            {
                _cfg.NotifMuted.RemoveAll(m => string.Equals(m, a, StringComparison.OrdinalIgnoreCase));
                if (!v) _cfg.NotifMuted.Add(a);
                Changed();
            }))).ToArray());
    }

    private void ClipAndShelf()
    {
        var openShelf = new Button { Content = "Open folder" };
        openShelf.Click += (_, _) => { Directory.CreateDirectory(ShelfStore.SaveFolder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ShelfStore.SaveFolder}\"") { UseShellExecute = true }); };
        Group("Clipboard",
            Row("Keep history of", "Pinned items are kept on top of this", Step(() => _cfg.ClipMax, v => { _cfg.ClipMax = v; Changed(); }, 10, 10, 100, "items")),
            Row("Skip copies from password managers", "Copies marked private by the app that made them never enter the history", Toggle(() => _cfg.ClipSkipSecrets, v => { _cfg.ClipSkipSecrets = v; Changed(); })));
        Page.Children.Add(Note("History stays in memory and is gone when Notch quits. Only pinned text is saved to disk."));
        Group("Shelf",
            Row("Keep files on the shelf", "Files themselves are never moved; the shelf only remembers them", Combo([("forever", "Until I remove them"), ("day", "For 1 day"), ("week", "For 1 week")], () => _cfg.ShelfKeep, v => { _cfg.ShelfKeep = v; Changed(); })),
            Row("Save all puts copies in", ShelfStore.SaveFolder, openShelf));
    }

    private void AppsPage()
    {
        var rows = new List<UIElement>();
        var apps = _cfg.Apps;
        for (int i = 0; i < apps.Count; i++)
        {
            int j = i;
            var a = apps[i];
            var ctl = new StackPanel { Orientation = Orientation.Horizontal };
            var up = new Button { Content = "↑", Width = 36, Margin = new Thickness(0, 0, 4, 0), IsEnabled = i > 0, ToolTip = "Move earlier" };
            var down = new Button { Content = "↓", Width = 36, Margin = new Thickness(0, 0, 4, 0), IsEnabled = i < apps.Count - 1, ToolTip = "Move later" };
            var rm = new Button { Content = "Remove" };
            up.Click += (_, _) => { (apps[j - 1], apps[j]) = (apps[j], apps[j - 1]); Changed(true); };
            down.Click += (_, _) => { (apps[j + 1], apps[j]) = (apps[j], apps[j + 1]); Changed(true); };
            rm.Click += (_, _) => { apps.RemoveAt(j); Changed(true); };
            ctl.Children.Add(up); ctl.Children.Add(down); ctl.Children.Add(rm);
            rows.Add(Row(a.Name, a.Target, ctl));
        }
        if (rows.Count == 0) rows.Add(Row("Nothing here yet", "Add apps, folders or websites below", new TextBlock()));
        Group("In the Apps tab", rows.ToArray());

        var pickApp = new Button { Content = "Choose…" };
        pickApp.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Add to Apps", Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.appref-ms|All files|*.*" };
            if (dlg.ShowDialog(this) == true) { foreach (var f in dlg.FileNames) apps.Add(AppLauncher.FromPath(f)); Changed(true); }
        };
        var pickFolder = new Button { Content = "Choose…" };
        pickFolder.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add a folder to Apps", Multiselect = true };
            if (dlg.ShowDialog(this) == true) { foreach (var f in dlg.FolderNames) apps.Add(AppLauncher.FromPath(f)); Changed(true); }
        };
        var name = new TextBox { Width = 130, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Name (optional)" };
        var url = new TextBox { Width = 200, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Address, for example github.com" };
        var addWeb = new Button { Content = "Add" };
        void AddWeb()
        {
            if (string.IsNullOrWhiteSpace(url.Text)) { url.Focus(); return; }
            apps.Add(AppLauncher.Website(name.Text, url.Text));
            Changed(true);
        }
        addWeb.Click += (_, _) => AddWeb();
        url.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) AddWeb(); };
        var web = new StackPanel { Orientation = Orientation.Horizontal };
        var nameCol = new StackPanel(); nameCol.Children.Add(new TextBlock { Text = "Name", FontSize = 11, Opacity = 0.6 }); nameCol.Children.Add(name);
        var urlCol = new StackPanel(); urlCol.Children.Add(new TextBlock { Text = "Address", FontSize = 11, Opacity = 0.6 }); urlCol.Children.Add(url);
        web.Children.Add(nameCol); web.Children.Add(urlCol);
        addWeb.VerticalAlignment = VerticalAlignment.Bottom;
        web.Children.Add(addWeb);
        Group("Add",
            Row("App or file", "Programs, shortcuts or any document", pickApp),
            Row("Folder", null, pickFolder),
            Row("Website", null, web));
        Page.Children.Add(Note("You can also drop apps, shortcuts and folders straight onto the Apps tab in the notch."));
    }

    private void About()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notch");
        var open = new Button { Content = "Open folder" };
        open.Click += (_, _) => { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true }); };
        var reset = new Button { Content = "Reset" };
        reset.Click += (_, _) =>
        {
            var d = new AppSettings();
            foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) p.SetValue(_cfg, p.GetValue(d));
            Changed(true);
        };
        Group("Notch",
            Row("Version", null, new TextBlock { Text = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", Opacity = 0.7 }),
            Row("Settings file", folder, open),
            Row("Reset all settings", "Back to the defaults, timer included", reset));
    }

    // ---------------------------------------------------------------- building blocks

    private void Group(string title, params UIElement[] rows)
    {
        Page.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) });
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        foreach (var r in rows) sp.Children.Add(r);
        Page.Children.Add(sp);
    }

    private static Border Card(UIElement child) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 3), Child = child,
    };

    private static UIElement Row(string label, string? desc, UIElement control, bool enabled = true)
    {
        var g = new Grid { MinHeight = 44, Opacity = enabled ? 1 : 0.45, IsEnabled = enabled };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (desc != null) text.Children.Add(new TextBlock { Text = desc, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(text);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return Card(g);
    }

    private static TextBlock Note(string text) => new() { Text = text, Opacity = 0.6, FontSize = 12.5, Margin = new Thickness(2, -12, 0, 24), TextWrapping = TextWrapping.Wrap };

    private static CheckBox Toggle(Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { IsChecked = get(), Content = get() ? "On" : "Off", MinWidth = 0 };
        cb.Click += (_, _) => { bool v = cb.IsChecked == true; cb.Content = v ? "On" : "Off"; set(v); };
        return cb;
    }

    private static UIElement Slide(double min, double max, double step, Func<double> get, Action<double> set, Func<double, string> fmt)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = fmt(get()), Width = 64, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Opacity = 0.8 };
        var s = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5, TickFrequency = step, IsSnapToTickEnabled = true, Value = get(), Width = 200, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => { label.Text = fmt(e.NewValue); set(e.NewValue); };
        sp.Children.Add(label);
        sp.Children.Add(s);
        return sp;
    }

    private static ComboBox Combo((string Value, string Label)[] options, Func<string> get, Action<string> set)
    {
        var cb = new ComboBox { MinWidth = 220 };
        foreach (var (v, l) in options) cb.Items.Add(new ComboBoxItem { Content = l, Tag = v });
        cb.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Value == get()));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem it) set((string)it.Tag); };
        return cb;
    }

    private static UIElement Seg((string Value, string Label)[] options, Func<string> get, Action<string> set)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        string group = Guid.NewGuid().ToString("N");
        foreach (var (v, l) in options)
        {
            var rb = new RadioButton { Content = l, GroupName = group, IsChecked = get() == v, Margin = new Thickness(12, 0, 0, 0), MinWidth = 0 };
            rb.Checked += (_, _) => { if (get() != v) set(v); };
            sp.Children.Add(rb);
        }
        return sp;
    }

    private static UIElement Step(Func<int> get, Action<int> set, int step, int min, int max, string unit)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var val = new TextBlock { Width = 72, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = $"{get()} {unit}".Trim() };
        var minus = new Button { Content = "−", Width = 36 };
        var plus = new Button { Content = "+", Width = 36 };
        minus.Click += (_, _) => { int v = Math.Clamp(get() - step, min, max); set(v); val.Text = $"{v} {unit}".Trim(); };
        plus.Click += (_, _) => { int v = Math.Clamp(get() + step, min, max); set(v); val.Text = $"{v} {unit}".Trim(); };
        sp.Children.Add(minus); sp.Children.Add(val); sp.Children.Add(plus);
        return sp;
    }
}
