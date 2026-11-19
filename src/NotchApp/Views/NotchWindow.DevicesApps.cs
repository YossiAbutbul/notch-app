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
    private static readonly Color Blue = Color.FromRgb(0x0A, 0x84, 0xFF), Green = Color.FromRgb(0x30, 0xD1, 0x58);
    private const double AppTile = 64;

    private bool _dndShown;

    private void WireDevicesApps()
    {
        _dev.Changed += () => { if (_hub && _tab == "dev") RenderDevices(); };
        _dev.DeviceConnected += d =>
        {
            string value = d.Battery is { } b ? $"{b}%" : "";
            ShowToast(new("device", 300, Color.FromRgb(110, 165, 255), d.Kind, Blue, Colors.White, $"{d.Name} connected", value, Green, false, 2600));
        };
        _dev.BatteryLow += d =>
        {
            var red = Color.FromRgb(0xFF, 0x45, 0x3A);
            ShowToast(new("lowbat", 300, red, "battery", Color.FromArgb(0x40, 0xFF, 0x45, 0x3A), red, $"{d.Name} battery low", $"{d.Battery}%", red, false, 4000));
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
        DevTiles.Children.Add(DevTile("plane", "Airplane mode", "Open settings", false, Orange, () => { DevicesService.OpenSettings("network-airplanemode"); return Task.CompletedTask; }, opens: true));
        _dndShown = QuietMode.On;
        DevTiles.Children.Add(DevTile("moon", "Do not disturb", _dndShown ? "On" : "Off", _dndShown, Purple, () => { DevicesService.OpenSettings("notifications"); return Task.CompletedTask; }, opens: true));

        DevList.Children.Clear();
        if (!_dev.BtOn)
            DevList.Children.Add(new TextBlock { Text = _dev.BtAvailable ? "Turn on Bluetooth to see your devices." : "This PC has no Bluetooth.", Foreground = (Brush)FindResource("Dim"), FontSize = 12, Margin = new Thickness(4, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        else if (_dev.Devices.Count == 0)
            DevList.Children.Add(new TextBlock { Text = "No paired devices yet.", Foreground = (Brush)FindResource("Dim"), FontSize = 12, Margin = new Thickness(4, 10, 0, 0) });
        else
            foreach (var d in _dev.Devices) DevList.Children.Add(DevRow(d));
    }

    /// <param name="opens">The tile opens a Settings page instead of switching something here: it wears a small arrow.</param>
    private Button DevTile(string icon, string label, string sub, bool on, Color accent, Func<Task> act, bool opens = false)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        // "on" is a tint of the accent with the icon in the accent, not a solid block: these are set-and-forget
        // switches, so they shouldn't be the loudest thing in the hub
        var iconColor = on ? Mix(accent, Colors.White, 0.25) : Colors.White;
        // 72 px tile − 18 px padding = 54: icon 16 + 6 + label 17 + sub 15
        sp.Children.Add(new IconEl { Kind = icon, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Foreground = new SolidColorBrush(iconColor) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, LineHeight = 17, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = on ? new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)) : (Brush)FindResource("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        var b = new Button
        {
            Style = (Style)FindResource("Press"), Margin = new Thickness(0, 0, 8, 8),
            Background = on ? new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B)) : (Brush)FindResource("Card"),
            Content = new Grid { Width = 96, Children = { sp } },
        };
        if (opens)
            ((Grid)b.Content).Children.Add(new IconEl { Kind = "out", Width = 13, Height = 13, StrokeWidth = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, -2, 0), Foreground = (Brush)FindResource("Dim") });
        b.Template = TileTemplate();
        b.Click += async (_, _) => await act();
        return b;
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static ControlTemplate? _tile, _tileCentered;

    /// <summary>
    /// Tiles get a 20 px corner (concentric with the 32 px hub at a 12 px inset) instead of the pill shape, and the same
    /// feel as every other button: a hover sheen that fades, and the press dip from PressFeedback.
    /// </summary>
    private ControlTemplate TileTemplate(bool centered = false)
    {
        ref var cached = ref centered ? ref _tileCentered : ref _tile;
        if (cached != null) return cached;
        string r = centered ? "14" : "20", pad = centered ? "2" : "12,9,12,9", ha = centered ? "Center" : "Left", va = centered ? "Center" : "Top";
        var xaml = $@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Grid x:Name='Root' RenderTransformOrigin='0.5,0.5'>
    <Grid.RenderTransform><ScaleTransform x:Name='S'/></Grid.RenderTransform>
    <Border CornerRadius='{r}' Background='{{TemplateBinding Background}}' Padding='{pad}'>
      <ContentPresenter HorizontalAlignment='{ha}' VerticalAlignment='{va}'/>
    </Border>
    <Border x:Name='Hover' CornerRadius='{r}' Background='#14FFFFFF' Opacity='0' IsHitTestVisible='False'/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
      <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Hover' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.24'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
        return cached = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
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
