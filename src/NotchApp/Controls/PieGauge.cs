using System.Windows;
using System.Windows.Media;

namespace NotchApp.Controls;

/// <summary>The compact pill's timer mark: a pie of time remaining inside a thin outline.</summary>
public sealed class PieGauge : FrameworkElement
{
    public double Fraction { get; set; } = 1;
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0x9F, 0x0A);

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight), c = s / 2;
        var brush = new SolidColorBrush(Color);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(115, Color.R, Color.G, Color.B)), 1.5), new Point(c, c), c - 1, c - 1);
        double f = Math.Clamp(Fraction, 0, 1), r = c - 4;
        if (f >= 0.999) { dc.DrawEllipse(brush, null, new Point(c, c), r, r); return; }
        if (f <= 0.001) return;
        double a0 = -Math.PI / 2, a1 = a0 + f * Math.PI * 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c, c), true, true);
            ctx.LineTo(new Point(c + Math.Cos(a0) * r, c + Math.Sin(a0) * r), false, false);
            ctx.ArcTo(new Point(c + Math.Cos(a1) * r, c + Math.Sin(a1) * r), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, false, false);
        }
        dc.DrawGeometry(brush, null, g);
    }
}
