using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NotchApp.Services;
using NotchApp.Settings;
using IconEl = NotchApp.Controls.Icon;

namespace NotchApp.Views;

/// <summary>Devices (radios, network, Bluetooth) and Apps (launcher) tabs.</summary>
public partial class NotchWindow
{
    private static readonly Color Blue = Color.FromRgb(0x3D, 0x8B, 0xFF), Green = Color.FromRgb(0x34, 0xC7, 0x6F);
    private const double AppTile = 68;

    private bool _dndShown;

    private void WireDevicesApps()
    {
        _dev.Changed += () => { if (_hub && _tab == "dev") RenderDevices(); };
        _dev.DeviceConnected += d =>
        {
            string value = d.Battery is { } b ? $"{b}%" : "";
            ShowToast(new("device", 300, Color.FromRgb(110, 165, 255), d.Kind, Blue, Colors.White, $"{d.Name} connected", value, Green, false, 2600));
        };
        BtManage.Click += (_, _) => DevicesService.OpenSettings("bluetooth");
    }

    // ================================================================ devices

    private void RenderDevices()
    {
        DevTiles.Children.Clear();
        string wifiSub = !_dev.WifiAvailable ? (_dev.Wired ? "Ethernet" : "No Wi-Fi") : !_dev.WifiOn ? "Off" : _dev.Network != null && !_dev.Wired ? _dev.Network : "Not connected";
        int connected = _dev.Devices.Count(d => d.Connected);
        string btSub = !_dev.BtAvailable ? "Not available" : !_dev.BtOn ? "Off" : connected > 0 ? $"{connected} connected" : "On";
        DevTiles.Children.Add(DevTile("wifi", "Wi-Fi", wifiSub, _dev.WifiOn, Blue, async () => await _dev.SetWifiAsync(!_dev.WifiOn)));
        DevTiles.Children.Add(DevTile("bt", "Bluetooth", btSub, _dev.BtOn, Blue, async () => await _dev.SetBluetoothAsync(!_dev.BtOn)));
        // Windows has no public switch for these two, so they open the right Settings page
        DevTiles.Children.Add(DevTile("plane", "Airplane mode", "Open settings", false, Orange, () => { DevicesService.OpenSettings("network-airplanemode"); return Task.CompletedTask; }));
        _dndShown = QuietMode.On;
        DevTiles.Children.Add(DevTile("moon", "Do not disturb", _dndShown ? "On" : "Off", _dndShown, Purple, () => { DevicesService.OpenSettings("notifications"); return Task.CompletedTask; }));

        DevList.Children.Clear();
        if (!_dev.BtOn)
            DevList.Children.Add(new TextBlock { Text = _dev.BtAvailable ? "Turn on Bluetooth to see your devices." : "This PC has no Bluetooth.", Foreground = (Brush)FindResource("Dim"), FontSize = 12, Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        else if (_dev.Devices.Count == 0)
            DevList.Children.Add(new TextBlock { Text = "No paired devices yet.", Foreground = (Brush)FindResource("Dim"), FontSize = 12, Margin = new Thickness(4, 10, 0, 0) });
        else
            foreach (var d in _dev.Devices) DevList.Children.Add(DevRow(d));
    }

    private Button DevTile(string icon, string label, string sub, bool on, Color accent, Func<Task> act)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        // 72 px tile − 18 px padding = 54: icon 16 + 6 + label 17 + sub 15
        sp.Children.Add(new IconEl { Kind = icon, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, LineHeight = 17, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = on ? new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)) : (Brush)FindResource("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        var b = new Button
        {
            Style = (Style)FindResource("Press"), Margin = new Thickness(0, 0, 8, 8),
            Background = on ? new SolidColorBrush(accent) : (Brush)FindResource("Card"),
            Content = new Grid { Width = 96, Children = { sp } },
        };
        b.Template = TileTemplate();
        b.Click += async (_, _) => await act();
        return b;
    }

    /// <summary>Tiles get a 20 px corner (concentric with the 32 px hub at a 12 px inset) instead of the pill shape.</summary>
    private ControlTemplate TileTemplate(bool centered = false)
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(centered ? 14 : 20));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetValue(Border.PaddingProperty, centered ? new Thickness(2) : new Thickness(12, 9, 12, 9));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, centered ? HorizontalAlignment.Center : HorizontalAlignment.Left);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, centered ? VerticalAlignment.Center : VerticalAlignment.Top);
        bd.AppendChild(cp);
        bd.Name = "Bd";
        t.VisualTree = bd;
        // same feel as every other button: lifts on hover, presses in on mouse-down
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        if (centered) hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(17, 255, 255, 255)), "Bd"));
        else hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.86, "Bd"));
        var press = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        press.Setters.Add(new Setter(UIElement.RenderTransformProperty, new ScaleTransform(0.95, 0.95)));
        t.Triggers.Add(hover);
        t.Triggers.Add(press);
        return t;
    }

    private FrameworkElement DevRow(BtDevice d)
    {
        var dim = (Brush)FindResource("Dim");
        var g = new Grid { Height = 40, Opacity = d.Connected ? 1 : 0.55, Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = (Brush)FindResource("Faint"), Child = new IconEl { Kind = d.Kind, Width = 15, Height = 15 } };
        g.Children.Add(ic);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = d.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = d.Connected ? "Connected" : "Paired", FontSize = 11, Foreground = dim });
        Grid.SetColumn(text, 2);
        g.Children.Add(text);
        if (d.Battery is { } pct)
        {
            var bat = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
            bat.Children.Add(new TextBlock { Text = $"{pct}%", FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            bat.Children.Add(new Controls.PieGauge { Width = 16, Height = 16, Fraction = pct / 100.0, Color = pct <= 20 ? Color.FromRgb(0xFF, 0x5D, 0x5D) : Green });
            Grid.SetColumn(bat, 3);
            g.Children.Add(bat);
        }
        return g;
    }

    // ================================================================ apps

    private int AppRows => (int)Math.Ceiling((_cfg.Apps.Count + 1) / 6.0);
    private double AppsHeight() => Math.Min(3, AppRows) * AppTile + (Math.Min(3, AppRows) - 1) * 8;

    private void RenderApps()
    {
        AppsGrid.Children.Clear();
        foreach (var a in _cfg.Apps) AppsGrid.Children.Add(AppButton(a));
        // add: opens Settings → Apps, where typing works (the notch never takes keyboard focus)
        var add = AppButton(new AppEntry { Name = "Add", Kind = "add" });
        add.ToolTip = "Add an app, folder or website. You can also drop apps and folders here.";
        AppsGrid.Children.Add(add);
        AppsScroll.Height = AppsHeight();
        PaneApps.Height = AppsHeight();
        if (_hub) Apply();
    }

    private Button AppButton(AppEntry a)
    {
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12) };
        var img = AppLauncher.IconFor(a);
        if (a.Kind == "add")
        {
            tile.Background = (Brush)FindResource("Faint");
            tile.Child = new IconEl { Kind = "plus", Width = 18, Height = 18 };
        }
        else if (img != null)
        {
            tile.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            tile.Child = new Image { Source = img, Width = 28, Height = 28 };
        }
        else
        {
            double h = a.Target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? 215 : a.Kind == "folder" ? 38 : AppLauncher.Hue(a.Name);
            tile.Background = new LinearGradientBrush(AppLauncher.FromHsl(h, 0.7, 0.58), AppLauncher.FromHsl((h + 18) % 360, 0.65, 0.40), 45);
            string kind = a.Target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? "gear"
                        : a.Kind == "folder" ? "folder"
                        : a.Target.EndsWith("wt.exe", StringComparison.OrdinalIgnoreCase) ? "terminal"
                        : a.Kind == "app" ? "apps" : "globe";
            tile.Child = new IconEl { Kind = kind, Width = 19, Height = 19, Foreground = Brushes.White };
        }
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(tile);
        sp.Children.Add(new TextBlock { Text = a.Name, FontSize = 11, Foreground = (Brush)FindResource("Dim"), TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 84, Margin = new Thickness(0, 5, 0, 0) });
        var b = new Button { Style = (Style)FindResource("Press"), Height = AppTile, Margin = new Thickness(0, 0, 8, 8), Content = sp, ToolTip = a.Kind == "add" ? null : a.Target, Background = Brushes.Transparent };
        b.Template = TileTemplate(centered: true);
        b.Click += (_, _) =>
        {
            if (a.Kind == "add") { SettingsRequested?.Invoke("apps"); return; }
            if (!AppLauncher.Launch(a)) return;
            CloseHub();
            var c = img != null ? Color.FromRgb(200, 205, 255) : AppLauncher.FromHsl(AppLauncher.Hue(a.Name), 0.7, 0.6);
            ShowToast(new("launch", 250, c, a.Kind == "folder" ? "folder" : a.Kind == "web" ? "globe" : "apps", Color.FromArgb(40, 255, 255, 255), Colors.White, $"Opening {a.Name}", "", Colors.White, false, 1500));
        };
        return b;
    }

    private void AddApps(IEnumerable<string> paths)
    {
        int n = 0;
        foreach (var p in paths)
        {
            if (_cfg.Apps.Any(x => string.Equals(x.Target, p, StringComparison.OrdinalIgnoreCase))) continue;
            _cfg.Apps.Add(AppLauncher.FromPath(p));
            n++;
        }
        if (n == 0) return;
        _cfg.Save();
        RenderApps();
        Kick();
    }
}
