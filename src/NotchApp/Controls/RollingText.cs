using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchApp.Controls;

/// <summary>
/// Text whose digits roll into place when they change: the new digit slides in from below when it grows and from above
/// when it shrinks, the old one slides out the other way. Characters that don't change stay still.
/// </summary>
public sealed class RollingText : StackPanel
{
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(RollingText));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(RollingText));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(RollingText));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(RollingText));

    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private string _text = "";

    public RollingText() { Orientation = Orientation.Horizontal; }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (value == _text) return;
            var old = _text;
            _text = value;
            bool motion = SystemParameters.ClientAreaAnimation && IsLoaded;
            if (old.Length != value.Length || !motion) { Rebuild(); return; }
            for (int i = 0; i < value.Length; i++)
                if (value[i] != old[i]) Roll((Grid)Children[i], old[i], value[i]);
        }
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var ch in _text) Children.Add(new Grid { ClipToBounds = true, Children = { Glyph(ch) } });
    }

    private static TextBlock Glyph(char ch) => new() { Text = ch.ToString(), RenderTransform = new TranslateTransform() };

    private void Roll(Grid slot, char from, char to)
    {
        // anything still leaving from a previous roll goes now
        while (slot.Children.Count > 1) slot.Children.RemoveAt(0);
        var outgoing = (TextBlock)slot.Children[0];
        var incoming = Glyph(to);
        slot.Children.Add(incoming);
        double h = FontSize * 1.1;
        double dir = char.IsDigit(from) && char.IsDigit(to) && to < from ? -1 : 1;   // counting down rolls downward

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var inT = TimeSpan.FromMilliseconds(420);
        ((TranslateTransform)incoming.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(dir * h * 0.75, 0, inT) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } });
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });

        var outT = TimeSpan.FromMilliseconds(300);
        var slide = new DoubleAnimation(0, -dir * h * 0.75, outT) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var fade = new DoubleAnimation(1, 0, outT);
        fade.Completed += (_, _) => slot.Children.Remove(outgoing);
        ((TranslateTransform)outgoing.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
        outgoing.BeginAnimation(OpacityProperty, fade);
    }
}
