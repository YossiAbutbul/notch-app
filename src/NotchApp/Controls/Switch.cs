using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchApp.Controls;

/// <summary>An iOS-style switch: the knob travels with a little overshoot and the track fills green.</summary>
public sealed class Switch : Border
{
    private readonly Border _knob;
    private readonly TranslateTransform _x = new();
    private bool _on, _down;
    public event Action<bool>? Toggled;
    private static readonly Color OnColor = Color.FromRgb(0x30, 0xD1, 0x58), OffColor = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);

    public Switch()
    {
        Width = 38; Height = 22; CornerRadius = new CornerRadius(11); Padding = new Thickness(2);
        Background = new SolidColorBrush(OffColor);
        Cursor = Cursors.Hand;
        _knob = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _x,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.4 },
        };
        Child = _knob;
    }

    public bool IsOn
    {
        get => _on;
        set { if (_on == value) return; _on = value; Show(IsLoaded); }
    }

    private void Show(bool animate)
    {
        double to = _on ? 16 : 0;
        var c = _on ? OnColor : OffColor;
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            _x.BeginAnimation(TranslateTransform.XProperty, null); _x.X = to;
            Background = new SolidColorBrush(c);
            return;
        }
        _x.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } });
        var b = new SolidColorBrush(((SolidColorBrush)Background).Color);
        b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(200)));
        Background = b;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true; _down = true; CaptureMouse(); if (Focusable) Focus();
        Stretch(true);
    }

    /// <summary>
    /// Like iOS: the knob widens the moment it's pressed (growing away from the side it rests on) and narrows again
    /// as it travels, so the press is felt before the release.
    /// </summary>
    private void Stretch(bool down)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        const double rest = 18, pressed = 23;
        double w = down ? pressed : rest;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _knob.BeginAnimation(WidthProperty, new DoubleAnimation(w, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        if (_on) _x.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16 - (w - rest), TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    /// <summary>When the host makes it focusable (the Settings window does), Space or Enter flips it.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) { base.OnKeyDown(e); return; }
        e.Handled = true;
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        bool inside = new Rect(RenderSize).Contains(e.GetPosition(this));
        ReleaseMouseCapture();
        if (!_down) return;
        _down = false;
        Stretch(false);
        if (!inside) return;   // dragged off: cancelled
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
    }
}
