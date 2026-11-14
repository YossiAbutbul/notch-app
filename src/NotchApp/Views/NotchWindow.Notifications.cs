using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotchApp.Services;

namespace NotchApp.Views;

/// <summary>
/// Notifications from other apps: each new one drops into the notch for a few seconds (click it to open the app),
/// and the Alerts tab keeps the recent ones.
/// </summary>
public partial class NotchWindow
{
    private NotifItem? _notif;
    public NotificationsService Notifications => _notifs;
    private DispatcherTimer? _notifTimer;

    private void WireNotifs()
    {
        _notifTimer = new DispatcherTimer();
        _notifTimer.Tick += (_, _) => DismissNotif();
        _notifs.Arrived += OnNotification;
        _notifs.Changed += () => { if (_hub && _tab == "notif") RenderNotifs(); };
        NotifClear.Click += (_, _) => _notifs.Clear();
        LNotif.Cursor = Cursors.Hand;
    }

    private bool Muted(NotifItem n) => _cfg.NotifMuted.Contains(n.App, StringComparer.OrdinalIgnoreCase);

    private void OnNotification(NotifItem n)
    {
        if (_hub || _hidden || !_cfg.IsLive("notif") || Muted(n)) return;   // the Alerts list still has it
        if (_cfg.NotifQuietInDnd && QuietMode.On) return;                    // Do not disturb: same, it waits in Alerts
        bool first = _notif == null;
        _notif = n;
        FillNotif(n);
        _toast = null;   // a notification outranks a small pop-up
        _notifTimer!.Stop();
        _notifTimer.Interval = TimeSpan.FromSeconds(_cfg.NotifSeconds);
        _notifTimer.Start();
        if (_key == "notif" && !first) { _key = ""; }   // a second one arriving re-runs the hand-off
        Apply();
        // it lands with momentum: taller first, a touch narrow, then settles
        Kick();
        if (_cfg.ImpactBounce && first) { H.Velocity += 460; W.Velocity -= 320; }
    }

    private void FillNotif(NotifItem n)
    {
        bool text = _cfg.NotifShowText;
        NotifApp.Text = n.App;
        NotifTitle.Text = text ? OneLine(n.Title) : n.App;
        NotifBody.Text = text ? OneLine(n.Body) : "New notification";
        NotifBody.Visibility = NotifBody.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifWhen.Text = "now";
        ApplyLogo(NotifIconBox, NotifLogo, NotifLetter, n, 18);
    }

    /// <summary>The app's own logo on a soft tile of its colour, or its initial when Windows has no logo.</summary>
    private static void ApplyLogo(Border box, Image img, TextBlock letter, NotifItem n, double letterSize)
    {
        var t = n.Tint;
        if (n.Logo != null)
        {
            img.Source = n.Logo; img.Visibility = Visibility.Visible; letter.Visibility = Visibility.Collapsed;
            box.Background = new SolidColorBrush(Color.FromArgb(0x2E, t.R, t.G, t.B));
        }
        else
        {
            img.Visibility = Visibility.Collapsed; letter.Visibility = Visibility.Visible;
            letter.Text = n.App.Length > 0 ? n.App[..1].ToUpperInvariant() : "•";
            letter.FontSize = letterSize;
            letter.Foreground = Brushes.White;
            var g = new LinearGradientBrush(t, Color.FromRgb((byte)(t.R * 0.6), (byte)(t.G * 0.6), (byte)(t.B * 0.6)), 45);
            box.Background = g;
        }
    }

    private void DismissNotif()
    {
        _notifTimer?.Stop();
        if (_notif == null) return;
        _notif = null;
        Apply();
    }

    /// <summary>A click on the pop-up opens the app that sent it.</summary>
    private void OpenNotif()
    {
        var n = _notif;
        DismissNotif();
        if (n != null) NotificationsService.OpenApp(n);
    }

    // ================================================================ Alerts tab

    private double NotifListHeight()
    {
        if (!_notifs.Allowed) return 96;
        int n = _notifs.Recent.Count;
        return n == 0 ? 96 : n > 3 ? 3 * (Row + RowGap) + Row / 2 : n * (Row + RowGap) - RowGap;
    }

    private void RenderNotifs()
    {
        var items = _notifs.Recent;
        NotifCount.Text = (items.Count == 0 ? "Recent" : $"Recent · {items.Count}")
                        + (_cfg.NotifQuietInDnd && QuietMode.On ? " · Do not disturb is on" : "");
        NotifClear.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifScroll.Height = NotifListHeight();
        NotifList.Children.Clear();
        if (!_notifs.Allowed) NotifList.Children.Add(NoAccess());
        else if (items.Count == 0) NotifList.Children.Add(Empty("Notifications from your apps show up here while Notch is running."));
        else foreach (var n in items) NotifList.Children.Add(NotifRow(n));
        PaneNotif.Height = PaneHeight("notif");
        if (_hub) Apply();
    }

    private FrameworkElement NoAccess()
    {
        var g = (Grid)Empty("");
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "Notch needs permission to read notifications", Foreground = (Brush)FindResource("Dim"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button
        {
            Style = (Style)FindResource("Press"), Height = 26, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 10, 0, 0),
            Background = (Brush)FindResource("Faint"), FontWeight = FontWeights.SemiBold, FontSize = 11.5, Content = "Open Windows settings", HorizontalAlignment = HorizontalAlignment.Center,
        };
        b.Click += (_, _) => NotificationsService.OpenPrivacySettings();
        sp.Children.Add(b);
        g.Children.Add(sp);
        return g;
    }

    private FrameworkElement NotifRow(NotifItem n)
    {
        var dim = (Brush)FindResource("Dim");
        var card = (Brush)FindResource("Card");
        bool text = _cfg.NotifShowText;
        var row = new Border { Height = Row, CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 0, 10, 0), Margin = new Thickness(0, 0, 0, RowGap), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"Open {n.App}" };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9) };
        var img = new Image { Width = 22, Height = 22 };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var letter = new TextBlock { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.Child = new Grid { Children = { img, letter } };
        ApplyLogo(box, img, letter, n, 13);
        g.Children.Add(box);

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = text ? OneLine(n.Title) : n.App, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        string second = text && n.Body.Length > 0 ? $"{n.App} · {OneLine(n.Body)}" : n.App;
        sp.Children.Add(new TextBlock { Text = second, FontSize = 11, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(sp, 2);
        g.Children.Add(sp);

        var when = new TextBlock { Text = ClipboardHistory.Ago(n.At.LocalDateTime), FontSize = 11, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(when, 3);
        g.Children.Add(when);

        row.Child = g;
        row.MouseEnter += (_, _) => row.Background = card;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseHub(); NotificationsService.OpenApp(n); };
        return row;
    }
}
