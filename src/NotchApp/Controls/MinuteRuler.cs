using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NotchApp.Motion;

namespace NotchApp.Controls;

/// <summary>
/// A minute ruler you scrub like a watch crown: 1:1 tracking while dragging, rubber band past the ends,
/// momentum projection on release with the release velocity handed to a spring that settles on a whole minute.
/// The wheel steps one minute. Ticks swell as they pass the caret.
/// </summary>
public sealed class MinuteRuler : FrameworkElement
{
    private const double PX = 9;   // ruler pixels per minute
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 180;
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xB5, 0x47);

    /// <summary>The minute under the caret while it moves (for the live readout).</summary>
    public event Action<int>? Scrubbed;
    /// <summary>A new value was chosen (drag released, wheel step).</summary>
    public event Action<int>? Committed;

    private readonly Spring _s = new(25, new SpringParams(1.0, 0.38), 0.002);
    private bool _loop;
    private TimeSpan _last;
    private int _shown = -1;
    private (double X0, double V0, List<(double X, long T)> Samples)? _drag;
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public MinuteRuler()
    {
        Cursor = Cursors.SizeWE;
        Focusable = false;
        // the ruler fades out toward both ends, like a crown turning away from you
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.24));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 0.76));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        OpacityMask = mask;
        Unloaded += (_, _) => StopLoop();
    }

    public SpringParams Params { get => _s.Params; set => _s.Params = value; }

    /// <summary>Set from outside (preset, settings): glides there unless the user is holding the ruler.</summary>
    public void GlideTo(int v, bool animate = true)
    {
        if (_drag != null) return;
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation) { _s.Target = v; StartLoop(); }
        else { _s.Snap(v); InvalidateVisual(); }
    }

    private int Rounded => (int)Math.Clamp(Math.Round(_s.Value), Min, Max);

    // ------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;   // the notch underneath must not start a pull
        CaptureMouse();
        double x = e.GetPosition(this).X;
        _s.Snap(_s.Value);  // grab it mid-flight: it stops where it is
        _drag = (x, _s.Value, [(x, Environment.TickCount64)]);
        StartLoop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } d || !IsMouseCaptured) return;
        double x = e.GetPosition(this).X;
        d.Samples.Add((x, Environment.TickCount64));
        if (d.Samples.Count > 6) d.Samples.RemoveAt(0);
        double v = d.V0 - (x - d.X0) / PX;
        if (v < Min) v = Min - Gesture.RubberBand(Min - v, 10);
        else if (v > Max) v = Max + Gesture.RubberBand(v - Max, 10);
        _s.Snap(v);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (IsMouseCaptured) ReleaseMouseCapture(); else End();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => End();

    private void End()
    {
        if (_drag is not { } d) return;
        _drag = null;
        var (a, b) = (d.Samples[0], d.Samples[^1]);
        double dt = (b.T - a.T) / 1000.0;
        double vpx = dt > 0.005 && Environment.TickCount64 - b.T < 80 ? (b.X - a.X) / dt : 0;
        vpx = Math.Clamp(vpx, -2000, 2000);
        // land where the flick is heading, and keep its speed on the way there
        int target = (int)Math.Clamp(Math.Round(_s.Value - Gesture.Project(vpx, 0.99) / PX), Min, Max);
        _s.Target = target;
        _s.Velocity = -vpx / PX;
        StartLoop();
        Committed?.Invoke(target);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;   // not the volume
        if (_drag != null) return;
        int v = Math.Clamp((int)Math.Round(_s.Target) + Math.Sign(e.Delta), Min, Max);
        if (v == (int)Math.Round(_s.Target)) return;
        GlideTo(v);
        Committed?.Invoke(v);
    }

    // ------------------------------------------------------------ frames

    private void StartLoop()
    {
        if (_loop) return;
        _loop = true; _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopLoop()
    {
        if (!_loop) return;
        _loop = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (t == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((t - _last).TotalSeconds, 1 / 20.0);
        _last = t;
        if (_drag == null) _s.Step(dt);
        int r = Rounded;
        if (r != _shown) { _shown = r; Scrubbed?.Invoke(r); }
        InvalidateVisual();
        if (_drag == null && _s.IsSettled) StopLoop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, c = w / 2, v = _s.Value;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // whole strip is grabbable
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int span = (int)Math.Ceiling(c / PX) + 1;
        for (int m = Math.Max(Min, (int)Math.Floor(v) - span); m <= Math.Min(Max, (int)Math.Ceiling(v) + span); m++)
        {
            double x = c + (m - v) * PX;
            bool major = m % 5 == 0;
            double near = Math.Max(0, 1 - Math.Abs(m - v) / 3);
            double a = major ? 0.5 + near * 0.5 : 0.2 + near * 0.45;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(a * 255), 255, 255, 255)), major ? 2.2 : 1.5)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            double th = (major ? 13 : 7) + near * 3;
            dc.DrawLine(pen, new Point(x, 3), new Point(x, 3 + th));
            if (major)
            {
                var ft = new FormattedText(m.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10,
                    new SolidColorBrush(Color.FromArgb((byte)((0.35 + near * 0.65) * 255), 255, 255, 255)), dpi);
                dc.DrawText(ft, new Point(x - ft.Width / 2, 21));
            }
        }
        // caret, with a soft glow in the accent colour
        for (int i = 3; i >= 1; i--)
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(28 * (4 - i)), Color.R, Color.G, Color.B)), null,
                new Rect(c - 1.5 - i * 2, 1 - i, 3 + i * 4, 19 + i * 2), 2 + i * 2, 2 + i * 2);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color), null, new Rect(c - 1.5, 1, 3, 19), 1.5, 1.5);
    }
}
