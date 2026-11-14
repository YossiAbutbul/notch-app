using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NotchApp.Motion;

namespace NotchApp.Controls;

/// <summary>
/// An iOS-style picker wheel: numbers sit on a cylinder (they shrink and fade as they turn away), a static unit
/// label rides on the selection band, and the wheel tracks the mouse 1:1, rubber-bands past its ends, projects a
/// flick forward and hands the release velocity to a spring that settles on a whole value. The wheel steps one row.
/// </summary>
public sealed class WheelPicker : FrameworkElement
{
    public const double RowH = 26;
    private const double Radius = 58;   // cylinder radius: how quickly rows turn away from you

    public int Min { get; set; } = 1;
    public int Max { get; set; } = 60;
    public int Step { get; set; } = 1;
    public string Unit { get; set; } = "min";

    /// <summary>The value under the band while it turns (for live previews).</summary>
    public event Action<int>? Scrubbed;
    /// <summary>A new value was chosen (released, wheel step, row click).</summary>
    public event Action<int>? Committed;

    private readonly Spring _s = new(0, new SpringParams(1.0, 0.34), 0.002);   // position in rows (index)
    private bool _loop;
    private TimeSpan _last;
    private int _shown = -1;
    private sealed class Drag { public double Y0, P0; public bool Moved; public List<(double Y, long T)> Samples = []; }
    private Drag? _drag;
    private static readonly Typeface Num = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface UnitFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public WheelPicker()
    {
        Cursor = Cursors.SizeNS;
        Focusable = false;
        Unloaded += (_, _) => StopLoop();
    }

    public SpringParams Params { get => _s.Params; set => _s.Params = value; }
    private int Count => (Max - Min) / Step + 1;
    private int ValueAt(int index) => Min + Math.Clamp(index, 0, Count - 1) * Step;
    private int IndexOf(int value) => Math.Clamp((int)Math.Round((value - Min) / (double)Step), 0, Count - 1);
    public int Value => ValueAt((int)Math.Round(_s.Target));

    /// <summary>Set from outside: turns there unless the user is holding the wheel.</summary>
    public void GlideTo(int value, bool animate = true)
    {
        if (_drag != null) return;
        int i = IndexOf(value);
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation) { _s.Target = i; StartLoop(); }
        else { _s.Snap(i); _shown = value; InvalidateVisual(); }
    }

    // ------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;   // the notch underneath must not start a pull
        CaptureMouse();
        double y = e.GetPosition(this).Y;
        _s.Snap(_s.Value);  // grab it mid-spin: it stops where it is
        _drag = new Drag { Y0 = y, P0 = _s.Value };
        _drag.Samples.Add((y, Environment.TickCount64));
        StartLoop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } d || !IsMouseCaptured) return;
        double y = e.GetPosition(this).Y;
        d.Samples.Add((y, Environment.TickCount64));
        if (d.Samples.Count > 6) d.Samples.RemoveAt(0);
        if (Math.Abs(y - d.Y0) > 3) d.Moved = true;
        double p = d.P0 - (y - d.Y0) / RowH;
        double hi = Count - 1;
        if (p < 0) p = -Gesture.RubberBand(-p, 3);
        else if (p > hi) p = hi + Gesture.RubberBand(p - hi, 3);
        _s.Snap(p);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_drag is { Moved: false } d)
        {
            // a click on a row turns the wheel to it
            double rows = (e.GetPosition(this).Y - ActualHeight / 2) / RowH;
            _drag = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            int target = Math.Clamp((int)Math.Round(d.P0 + rows), 0, Count - 1);
            _s.Target = target;
            StartLoop();
            Committed?.Invoke(ValueAt(target));
            return;
        }
        if (IsMouseCaptured) ReleaseMouseCapture(); else End();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => End();

    private void End()
    {
        if (_drag is not { } d) return;
        _drag = null;
        var (a, b) = (d.Samples[0], d.Samples[^1]);
        double dt = (b.T - a.T) / 1000.0;
        double v = dt > 0.005 && Environment.TickCount64 - b.T < 80 ? (b.Y - a.Y) / dt : 0;   // px/s
        v = Math.Clamp(v, -4000, 4000);
        // a flick spins on to where it's heading, then settles on a whole row, keeping its speed on the way
        int target = (int)Math.Clamp(Math.Round(_s.Value - Gesture.Project(v, 0.995) / RowH), 0, Count - 1);
        _s.Target = target;
        _s.Velocity = -v / RowH;
        StartLoop();
        Committed?.Invoke(ValueAt(target));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;   // not the volume
        if (_drag != null) return;
        int i = Math.Clamp((int)Math.Round(_s.Target) - Math.Sign(e.Delta), 0, Count - 1);
        if (i == (int)Math.Round(_s.Target)) return;
        _s.Target = i;
        StartLoop();
        Committed?.Invoke(ValueAt(i));
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
        int v = ValueAt((int)Math.Round(_s.Value));
        if (v != _shown) { _shown = v; Scrubbed?.Invoke(v); }
        InvalidateVisual();
        if (_drag == null && _s.IsSettled) StopLoop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, cy = h / 2, p = _s.Value;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // the whole wheel is grabbable
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // numbers end just left of centre; the unit sits just right of it, on the band, and doesn't move
        double numRight = w / 2 + 6;
        int first = Math.Max(0, (int)Math.Floor(p) - 4), last = Math.Min(Count - 1, (int)Math.Ceiling(p) + 4);
        for (int i = first; i <= last; i++)
        {
            double ang = (i - p) * RowH / Radius;                  // angle on the cylinder
            if (Math.Abs(ang) >= Math.PI / 2 - 0.05) continue;
            double y = cy + Math.Sin(ang) * Radius;
            double squash = Math.Cos(ang);
            double near = Math.Max(0, 1 - Math.Abs(i - p));       // 1 on the band
            double alpha = Math.Clamp(squash * squash * (0.55 + 0.45 * near), 0, 1);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 0xF4, 0xF5, 0xF8));
            var ft = new FormattedText(ValueAt(i).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Num, 21, brush, dpi);
            dc.PushTransform(new TranslateTransform(numRight - ft.Width, y));
            dc.PushTransform(new ScaleTransform(1, squash, 0, 0));
            dc.DrawText(ft, new Point(0, -ft.Height / 2));
            dc.Pop(); dc.Pop();
        }
        var unit = new FormattedText(Unit, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UnitFace, 13, new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF8)), dpi);
        dc.DrawText(unit, new Point(numRight + 6, cy - unit.Height / 2 + 1));
    }
}
