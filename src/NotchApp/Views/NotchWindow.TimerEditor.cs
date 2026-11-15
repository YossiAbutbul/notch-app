using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NotchApp.Controls;
using NotchApp.Services;
using Switch = NotchApp.Controls.Switch;

namespace NotchApp.Views;

/// <summary>
/// The timer editor: tap the time (or the edit button) and the timer card turns into an editor in place.
/// Left, iOS picker wheels for the focus, break and long-break lengths; right, sessions, auto-start and Done.
/// Changing the length of the phase that's running restarts it at the new length.
/// </summary>
public partial class NotchWindow
{
    private bool _editing;
    private DispatcherTimer? _saveSoon;
    private readonly Dictionary<string, TextBlock> _stepValues = [];
    private Switch? _autoSwitch;

    /// <summary>A timer setting was changed from the editor (the Settings window redraws its timer page).</summary>
    public event Action? TimerEdited;

    private void InitTimerEditor()
    {
        // each wheel's caption wears its phase colour
        CapFocus.Foreground = new SolidColorBrush(FocusTimer.ColorOf(Phase.Focus));
        CapBreak.Foreground = new SolidColorBrush(FocusTimer.ColorOf(Phase.Break));
        CapLong.Foreground = new SolidColorBrush(FocusTimer.ColorOf(Phase.LongBreak));
        foreach (var (w, key) in new[] { (FocusWheel, "focus"), (BreakWheel, "brk"), (LongWheel, "long") })
        {
            w.Params = _every;
            w.GlideTo(Current(key), animate: false);
            w.Committed += v => SetLength(key, v);
        }
        // Done wears the timer's blue the way an "on" Devices tile does: a tint with the colour in the label
        var accent = FocusTimer.ColorOf(Phase.Focus);
        EditDone.Background = new SolidColorBrush(FocusTimer.Tint(accent));
        EditDone.Foreground = new SolidColorBrush(FocusTimer.Light(accent));
        EditDone.Click += (_, _) => CloseTimerEditor();
        TimerEditBtn.Click += (_, _) => OpenTimerEditor();

        EditRows.Children.Add(EditRow("Sessions", Stepper("sessions", 1), false));
        _autoSwitch = new Switch { IsOn = _cfg.Timer.AutoStart };
        _autoSwitch.Toggled += on => { _cfg.Timer.AutoStart = on; SaveSoon(); TimerEdited?.Invoke(); };
        EditRows.Children.Add(EditRow("Auto-start", _autoSwitch, true));
        RefreshEditor();
    }

    private static FrameworkElement EditRow(string label, FrameworkElement control, bool last)
    {
        var g = new Grid { Height = 36 };
        g.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(control);
        return new Border
        {
            Child = g,
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)),
        };
    }

    /// <summary>A capsule stepper: − value +.</summary>
    private FrameworkElement Stepper(string key, int step)
    {
        Button Btn(string glyph, int d)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Press"), Width = 26, Height = 24, FontSize = 14, Content = glyph,
                Foreground = (Brush)FindResource("Dim"), ToolTip = d < 0 ? "Less" : "More",
            };
            b.Click += (_, _) => SetLength(key, Current(key) + d);
            return b;
        }
        var value = new TextBlock { MinWidth = 30, TextAlignment = TextAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _stepValues[key] = value;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Btn("−", -step));
        row.Children.Add(value);
        row.Children.Add(Btn("+", step));
        return new Border { CornerRadius = new CornerRadius(12), Height = 24, Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)), Child = row };
    }

    private int Current(string key) => key switch
    {
        "focus" => _cfg.Timer.Focus,
        "brk" => _cfg.Timer.ShortBreak,
        "long" => _cfg.Timer.LongBreak,
        _ => _cfg.Timer.Sessions,
    };

    private static (int Lo, int Hi) Limits(string key) => key switch
    {
        "focus" => (1, 180),
        "brk" => (1, 30),
        "long" => (5, 60),
        _ => (1, 8),
    };

    private void SetLength(string key, int v)
    {
        var (lo, hi) = Limits(key);
        v = Math.Clamp(v, lo, hi);
        if (v == Current(key)) { RefreshEditor(); return; }
        var t = _cfg.Timer;
        switch (key)
        {
            case "focus": t.Focus = v; _timer.LengthChanged(Phase.Focus); break;
            case "brk": t.ShortBreak = v; _timer.LengthChanged(Phase.Break); break;
            case "long": t.LongBreak = v; _timer.LengthChanged(Phase.LongBreak); break;
            default: t.Sessions = v; _timer.SettingsChanged(); break;
        }
        SaveSoon();
        RefreshEditor();
        TimerEdited?.Invoke();
    }

    private void RefreshEditor()
    {
        var t = _cfg.Timer;
        FocusWheel.GlideTo(t.Focus);
        BreakWheel.GlideTo(t.ShortBreak);
        LongWheel.GlideTo(t.LongBreak);
        if (_stepValues.TryGetValue("sessions", out var ses)) ses.Text = t.Sessions.ToString();
        if (_autoSwitch != null) _autoSwitch.IsOn = t.AutoStart;
    }

    /// <summary>Settings are written once the scrubbing stops, not on every step.</summary>
    private void SaveSoon()
    {
        if (_saveSoon == null)
        {
            _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); _cfg.Save(); };
        }
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    private void OpenTimerEditor()
    {
        if (!_hub) OpenHub("sticky", "timer");
        else { _openMode = "sticky"; if (_tab != "timer") SetTab("timer"); }
        foreach (var w in new[] { FocusWheel, BreakWheel, LongWheel }) w.Params = _every;
        RefreshEditor();
        if (_editing) return;
        _editing = true;
        SwapEditor(true, animate: true);
        Apply();
    }

    private void CloseTimerEditor(bool animate = true)
    {
        if (!_editing) return;
        _editing = false;
        SwapEditor(false, animate);
        Apply();
    }

    /// <summary>The editor grows out of the timer card's corner; the row behind it steps back.</summary>
    private void SwapEditor(bool open, bool animate)
    {
        TimerEditor.IsHitTestVisible = open;
        TimerRow.IsHitTestVisible = !open;
        animate &= SystemParameters.ClientAreaAnimation;
        void To(UIElement el, DependencyProperty p, double v, int ms, int delay = 0)
        {
            if (!animate) { el.BeginAnimation(p, null); el.SetValue(p, v); return; }
            el.BeginAnimation(p, new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms))
            { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }
        void Scale(ScaleTransform s, double v, int ms)
        {
            if (!animate) { s.BeginAnimation(ScaleTransform.ScaleXProperty, null); s.BeginAnimation(ScaleTransform.ScaleYProperty, null); s.ScaleX = s.ScaleY = v; return; }
            var a = new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        To(TimerEditor, OpacityProperty, open ? 1 : 0, open ? 240 : 160, open ? 40 : 0);
        Scale(TimerEditorScale, open ? 1 : 0.96, 340);
        To(TimerRow, OpacityProperty, open ? 0 : 1, open ? 160 : 240, open ? 0 : 40);
        Scale(TimerRowScale, open ? 0.98 : 1, 340);
    }
}
