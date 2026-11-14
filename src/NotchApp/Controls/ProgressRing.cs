using System.Windows;
using System.Windows.Media;

namespace NotchApp.Controls;

/// <summary>
/// The iOS timer ring: a dim track and a bright arc for the time remaining, drawn counter-clockwise from 12 o'clock
/// with round caps. Dims while paused; the arc breathes in the last ten seconds.
/// </summary>
public sealed class ProgressRing : FrameworkElement
{
    public double Fraction { get; set; } = 1;   // time remaining / total
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public bool Paused { get; set; }
    public double Urgency { get; set; }         // 0..1
    public double Thickness { get; set; } = 4;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double r = (size - Thickness) / 2 - 1, c = size / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x38, Color.R, Color.G, Color.B)), Thickness), center, r, r);
        double frac = Math.Clamp(Fraction, 0, 1);
        if (frac <= 0.001) return;
        double a = (Paused ? 0.55 : 1) * (1 - 0.35 * Urgency);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(a * 255), Color.R, Color.G, Color.B)), Thickness)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (frac >= 0.999) { dc.DrawEllipse(null, pen, center, r, r); return; }
        // the remaining time is the arc from 12 o'clock clockwise to the head, which sweeps back toward 12 as time runs out
        double end = -Math.PI / 2 + frac * Math.PI * 2;
        var start = new Point(center.X, center.Y - r);
        var head = new Point(center.X + Math.Cos(end) * r, center.Y + Math.Sin(end) * r);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(head, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
        _ = c;
    }
}
