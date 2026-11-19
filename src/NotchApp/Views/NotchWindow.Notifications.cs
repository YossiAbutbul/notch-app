using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotchApp.Services;

namespace NotchApp.Views;

/// <summary>
/// Notifications from other apps: each new one drops into the notch for a few seconds (click it to open the app),
/// and the Notifications tab keeps the recent ones.
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

    private void OnNotification(NotifItem n) => OnNotification(n, false);

    private void OnNotification(NotifItem n, bool example)
    {
        if (_hub || _hidden || !_cfg.IsLive("notif") || Muted(n)) return;   // the Notifications list still has it
        if (!example && _cfg.NotifQuietInDnd && QuietMode.On) return;                    // Do not disturb: same, it waits in the Notifications tab
        bool first = _notif == null;
        _notif = n;
        FillNotif(n);
        _toast = null;   // a notification outranks a small pop-up
        _notifTimer!.Stop();
        _notifTimer.Interval = TimeSpan.FromSeconds(_cfg.NotifSeconds);
        if (!_notifOpen) _notifTimer.Start();   // one you're reading stays until you move away
        if (_key == "notif" && !first) { _key = ""; }   // a second one arriving re-runs the hand-off
        Apply();
        // it lands with momentum: taller first, a touch narrow, then settles. One replacing another gets a smaller
        // nudge down, so a new message is felt as new even though the shape barely changes.
        Kick();
        if (_cfg.ImpactBounce) { if (first) { H.Velocity += 460; W.Velocity -= 320; } else H.Velocity += 220; }
    }

    private void FillNotif(NotifItem n)
    {
        var t = n.Tint;
        NotifApp.Text = n.App;
        NotifApp.Foreground = new SolidColorBrush(FocusTimer.Light(t));   // the app's own colour names it
        NotifHint.Foreground = new SolidColorBrush(FocusTimer.Light(t));
        NotifWash.Background = new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(0x38, t.R, t.G, t.B), 0), new GradientStop(Color.FromArgb(0x10, t.R, t.G, t.B), 0.45), new GradientStop(Color.FromArgb(0, t.R, t.G, t.B), 1)],
            new Point(0, 0), new Point(1, 0.6));
        NotifWhen.Text = "now";
        ApplyLogo(NotifIconBox, NotifLogo, NotifLetter, n, 18);
        LayoutNotif();
    }

    // ---------------------------------------------------------------- open on hover

    /// <summary>Hovering a notification opens it in place to the whole message, instead of opening the hub over it.</summary>
    private bool _notifOpen;
    private double _notifOpenH = 86;
    private const double NotifW = 372, NotifOpenW = 420;

    private (double W, double H) NotifSize => _notifOpen ? (NotifOpenW, _notifOpenH) : (NotifW, 86);

    private void LayoutNotif()
    {
        var n = _notif;
        if (n == null) return;
        bool open = _notifOpen, text = _cfg.NotifShowText;
        var wrap = open ? TextWrapping.Wrap : TextWrapping.NoWrap;
        NotifTitle.TextWrapping = wrap;
        NotifTitle.MaxHeight = open ? 42 : double.PositiveInfinity;       // two lines
        NotifTitle.Text = text ? (open ? n.Title.Trim() : OneLine(n.Title)) : n.App;
        NotifBody.TextWrapping = wrap;
        NotifBody.MaxHeight = open ? 17 * 6 : double.PositiveInfinity;   // up to six lines, then an ellipsis
        NotifBody.Text = !text ? "New notification" : open ? n.Body.Trim() : OneLine(Flat(n.Body));
        NotifBody.Visibility = NotifBody.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifBody.Margin = new Thickness(0, open ? 3 : 0, 0, 0);
        NotifHint.Text = $"Click to open {n.App}";
        NotifHint.Visibility = open && n.AppId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var top = open ? VerticalAlignment.Top : VerticalAlignment.Center;
        NotifBox.VerticalAlignment = NotifIconBox.VerticalAlignment = NotifText.VerticalAlignment = top;
        NotifBox.Margin = open ? new Thickness(20, 18, 22, 18) : new Thickness(20, 0, 22, 0);
        double w = open ? NotifOpenW : NotifW;
        LNotif.Width = w;
        if (open)
        {
            NotifBox.Measure(new Size(w, double.PositiveInfinity));
            _notifOpenH = Math.Max(86, Math.Ceiling(NotifBox.DesiredSize.Height));
        }
        LNotif.Height = open ? _notifOpenH : 86;
    }

    private void SetNotifOpen(bool open)
    {
        if (_notif == null || _notifOpen == open) return;
        _notifOpen = open;
        LayoutNotif();
        _notifTimer!.Stop();
        if (!open)
        {
            // read it, moved away: it stays a moment longer, then goes
            _notifTimer.Interval = TimeSpan.FromSeconds(Math.Min(3, _cfg.NotifSeconds));
            _notifTimer.Start();
        }
        // no momentum kick: opening to read is a calm reveal (Apply picks the reveal spring while hovered).
        // The extra text materialises once the shape has started making room for it, instead of popping in.
        if (open && SystemParameters.ClientAreaAnimation)
            foreach (var el in new FrameworkElement[] { NotifBody, NotifHint })
            {
                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                {
                    BeginTime = TimeSpan.FromMilliseconds(el == NotifHint ? 140 : 90),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                });
            }
        else
            foreach (var el in new FrameworkElement[] { NotifBody, NotifHint }) { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; }
        Apply();
    }

    /// <summary>The text parts of a notification, on one line.</summary>
    private static string Flat(string body) => string.Join(" · ", body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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
        _notifOpen = false;
        Apply();
    }

    /// <summary>A click on the pop-up opens the app that sent it.</summary>
    private void OpenNotif()
    {
        var n = _notif;
        DismissNotif();
        if (n != null) NotificationsService.OpenApp(n);
    }

    // ================================================================ Notifications tab

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
        else if (items.Count == 0) NotifList.Children.Add(Empty("Notifications from your apps show up here while Notch is running.", dropTarget: false));
        else foreach (var n in items) NotifList.Children.Add(NotifRow(n));
        PaneNotif.Height = PaneHeight("notif");
        if (_hub) Apply();
    }

    private FrameworkElement NoAccess()
    {
        var g = (Grid)Empty("", dropTarget: false);
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
        bool text = _cfg.NotifShowText;
        var row = new Border { Height = Row, CornerRadius = RowCorner, Padding = new Thickness(6, 0, 10, 0), Margin = new Thickness(0, 0, 0, RowGap), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = $"Open {n.App}" };
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
        string second = text && n.Body.Length > 0 ? $"{n.App} · {OneLine(Flat(n.Body))}" : n.App;
        sp.Children.Add(new TextBlock { Text = second, FontSize = 11, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(sp, 2);
        g.Children.Add(sp);

        var when = new TextBlock { Text = ClipboardHistory.Ago(n.At.LocalDateTime), FontSize = 11, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(when, 3);
        g.Children.Add(when);

        row.Child = g;
        HoverFill(row);
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseHub(); NotificationsService.OpenApp(n); };
        return row;
    }
}
