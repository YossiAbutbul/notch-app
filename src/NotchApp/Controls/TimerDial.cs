using System.Windows;
using System.Windows.Media;

namespace NotchApp.Controls;

/// <summary>
/// The watch-style dial: 60 ticks lit for the time remaining, an arc that fades from a soft tail to a bright head,
/// and a glowing knob that pulses in the last ten seconds.
/// </summary>
public sealed class TimerDial : FrameworkElement
{
    public double Fraction { get; set; } = 1;   // time remaining / total
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xB5, 0x47);
    public bool Paused { get; set; }
    public double Urgency { get; set; }         // 0..1, the last-seconds pulse

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), c = size / 2;
        double R = size * 128 / 288, lw = size * 12 / 288;
        double dim = Paused ? 0.45 : 1;
        double frac = Math.Clamp(Fraction, 0, 1);
        var center = new Point(c, c);
        Color A(double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), Color.R, Color.G, Color.B);
        Point On(double ang, double r) => new(c + Math.Cos(ang) * r, c + Math.Sin(ang) * r);
        const double top = -Math.PI / 2;

        // ticks
        for (int i = 0; i < 60; i++)
        {
            double a = top + i / 60.0 * Math.PI * 2;
            bool major = i % 5 == 0, lit = i / 60.0 < frac;
            double r1 = R - lw / 2 - size * 9 / 288, r2 = r1 - size * (major ? 13 : 7) / 288;
            var col = lit ? A((major ? 0.95 : 0.55) * dim) : Color.FromArgb((byte)(major ? 51 : 23), 255, 255, 255);
            var pen = new Pen(new SolidColorBrush(col), size * (major ? 4 : 2.5) / 288) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, On(a, r1), On(a, r2));
        }

        // track
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), lw), center, R, R);

        if (frac <= 0.002) return;

        // arc: short segments brightening from tail to head
        int segs = Math.Max(2, (int)(frac * 90));
        double end = top + frac * Math.PI * 2;
        for (int i = 0; i < segs; i++)
        {
            double a0 = top + (end - top) * i / segs, a1 = top + (end - top) * (i + 1) / segs + 0.01;
            double t = (i + 1.0) / segs;
            var pen = new Pen(new SolidColorBrush(A((0.28 + 0.72 * t) * dim)), lw) { StartLineCap = PenLineCap.Flat, EndLineCap = i == segs - 1 ? PenLineCap.Round : PenLineCap.Flat };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(On(a0, R), false, false);
                ctx.ArcTo(On(a1, R), new Size(R, R), 0, false, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, pen, g);
        }
        // start cap
        dc.DrawEllipse(new SolidColorBrush(A(0.28 * dim)), null, On(top, R), lw / 2, lw / 2);

        // knob with glow
        var head = On(end, R);
        double glowR = lw * (1.6 + Urgency * 1.2);
        var glow = new RadialGradientBrush(A(0.75 * dim), A(0)) { RadiusX = 0.5, RadiusY = 0.5 };
        dc.DrawEllipse(glow, null, head, glowR, glowR);
        dc.DrawEllipse(new SolidColorBrush(Paused ? Color.FromArgb(153, 255, 255, 255) : Colors.White), null, head, lw / 2 + size * 3 / 288, lw / 2 + size * 3 / 288);
        dc.DrawEllipse(new SolidColorBrush(A(dim)), null, head, Math.Max(1, lw / 2 - size * 2.5 / 288), Math.Max(1, lw / 2 - size * 2.5 / 288));
    }
}

/// <summary>The compact pill's timer mark: a pie of time remaining inside a thin outline.</summary>
public sealed class PieGauge : FrameworkElement
{
    public double Fraction { get; set; } = 1;
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xB5, 0x47);

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
