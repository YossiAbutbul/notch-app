using System.Windows;
using System.Windows.Media;

namespace NotchApp.Controls;

/// <summary>Line icons on a 24-unit grid, drawn with the element's Foreground. Same set as the motion spec.</summary>
public sealed class Icon : FrameworkElement
{
    private static readonly Dictionary<string, (string Data, bool Filled)> Paths = new()
    {
        ["now"] = ("M9 18V5l11-2v13 M3 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M14 16a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", false),
        ["system"] = ("M8 6h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2z M9 2v4 M15 2v4 M9 18v4 M15 18v4 M2 9h4 M2 15h4 M18 9h4 M18 15h4", false),
        // gear: Feather "settings" (MIT)
        ["gear"] = ("M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 -2.83 2.83l-.06-.06a1.65 1.65 0 0 0 -1.82-.33 1.65 1.65 0 0 0 -1 1.51V21a2 2 0 0 1 -4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0 -1.82.33l-.06.06a2 2 0 0 1 -2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0 -1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0 -.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0 -1.51 1z", false),
        ["play"] = ("M7 4.5v15l12-7.5z", true),
        ["pause"] = ("M6 4.5h4v15h-4z M14 4.5h4v15h-4z", true),
        ["prev"] = ("M6 5v14 M19 5v14l-11-7z", false),
        ["next"] = ("M18 5v14 M5 5v14l11-7z", false),
        ["plus"] = ("M12 5v14 M5 12h14", false),
        ["reset"] = ("M4 12a8 8 0 1 0 2.4 -5.7 M4 4v4.5h4.5", false),
        ["bolt"] = ("M13 2L4 14h7l-1 8 9-12h-7z", false),
        ["speaker"] = ("M4 9h4l5-4v14l-5-4h-4z M16.5 8.5a5 5 0 0 1 0 7 M19 6a8.5 8.5 0 0 1 0 12", false),
        ["mute"] = ("M4 9h4l5-4v14l-5-4h-4z M17 9l5 6 M22 9l-5 6", false),
        ["spark"] = ("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", false),
        ["bell"] = ("M18 8a6 6 0 0 0 -12 0c0 7 -3 9 -3 9h18s-3 -2 -3 -9 M13.7 21a2 2 0 0 1 -3.4 0", false),
        ["check"] = ("M5 12.5l4.5 4.5L19 7", false),
        ["clock"] = ("M4 13a8 8 0 1 0 16 0a8 8 0 1 0 -16 0 M12 9v4l2.5 2.5 M9 2.5h6", false),
        ["clip"] = ("M9 3h6a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1v-2a1 1 0 0 1 1 -1z M16 5h2a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2h2", false),
        ["shelf"] = ("M3 13h5l2 3h4l2-3h5 M5 5h14l2 8v6a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1v-6z", false),
        ["link"] = ("M10 14a4 4 0 0 0 6 0l3-3a4 4 0 0 0 -6-6l-1 1 M14 10a4 4 0 0 0 -6 0l-3 3a4 4 0 0 0 6 6l1-1", false),
        ["code"] = ("M8 8l-5 4 5 4 M16 8l5 4-5 4", false),
        ["text"] = ("M4 6h16 M4 12h16 M4 18h10", false),
        ["image"] = ("M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2z M7 10a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M21 16l-5-5-9 9", false),
        ["copy"] = ("M11 9h7a2 2 0 0 1 2 2v7a2 2 0 0 1 -2 2h-7a2 2 0 0 1 -2 -2v-7a2 2 0 0 1 2 -2z M5 15v-9a2 2 0 0 1 2 -2h8", false),
        ["pin"] = ("M12 17v5 M9 3h6l-1 6 4 4v2h-12v-2l4-4z", false),
        ["open"] = ("M14 4h6v6 M20 4l-9 9 M18 14v5a1 1 0 0 1 -1 1h-12a1 1 0 0 1 -1 -1v-12a1 1 0 0 1 1 -1h5", false),
        ["x"] = ("M7 7l10 10 M17 7l-10 10", false),
        ["folder"] = ("M3 7a2 2 0 0 1 2 -2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z", false),
        ["file"] = ("M14 3h-7a2 2 0 0 0 -2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-11z M14 3v5h5", false),
        ["wifi"] = ("M2 9a15 15 0 0 1 20 0 M5 12.5a10 10 0 0 1 14 0 M8.5 16a5 5 0 0 1 7 0 M11 19.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0", false),
        ["bt"] = ("M7 7l10 10-5 5v-20l5 5-10 10", false),
        ["plane"] = ("M10 3.5a2 2 0 0 1 4 0v5.5l7 4v2l-7-2v5l2 2v1l-4-1-4 1v-1l2-2v-5l-7 2v-2l7-4z", false),
        ["moon"] = ("M20 14.5a8 8 0 1 1 -10.5 -10.5a6.5 6.5 0 0 0 10.5 10.5z", false),
        ["apps"] = ("M5 3h3a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-3a2 2 0 0 1 -2 -2v-3a2 2 0 0 1 2 -2z M16 3h3a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-3a2 2 0 0 1 -2 -2v-3a2 2 0 0 1 2 -2z M5 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-3a2 2 0 0 1 -2 -2v-3a2 2 0 0 1 2 -2z M16 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-3a2 2 0 0 1 -2 -2v-3a2 2 0 0 1 2 -2z", false),
        ["buds"] = ("M7 4a3 3 0 0 0 -3 3v1a3 3 0 0 0 3 3v8a1.5 1.5 0 0 0 3 0v-12a3 3 0 0 0 -3 -3z M17 4a3 3 0 0 1 3 3v1a3 3 0 0 1 -3 3v8a1.5 1.5 0 0 1 -3 0v-12a3 3 0 0 1 3 -3z", false),
        ["headphones"] = ("M3 18v-6a9 9 0 0 1 18 0v6 M21 19a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2v-3a2 2 0 0 1 2 -2h3z M3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2 -2v-3a2 2 0 0 0 -2 -2h-3z", false),
        ["keyboard"] = ("M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M7 14h10", false),
        ["mouse"] = ("M12 3a6 6 0 0 1 6 6v6a6 6 0 0 1 -12 0v-6a6 6 0 0 1 6 -6z M12 7v4", false),
        ["pad"] = ("M6 8h12a4 4 0 0 1 4 4v1a3 3 0 0 1 -5.4 1.8l-1.6-1.8h-6l-1.6 1.8a3 3 0 0 1 -5.4 -1.8v-1a4 4 0 0 1 4 -4z M7 11v2 M6 12h2", false),
        ["phone"] = ("M8 2h8a2 2 0 0 1 2 2v16a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2v-16a2 2 0 0 1 2 -2z M11 18h2", false),
        ["globe"] = ("M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18", false),
        ["terminal"] = ("M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2z M7 9l3 3-3 3 M12 15h5", false),
        ["battery"] = ("M3 8h15a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-15a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1z M22 11v2", false),
    };
    private static readonly Dictionary<string, Geometry> Cache = new();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    // shares the inherited text Foreground, so an icon takes its button's colour automatically
    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public Icon() { IsHitTestVisible = false; Width = 16; Height = 16; }

    protected override void OnRender(DrawingContext dc)
    {
        if (!Paths.TryGetValue(Kind, out var p)) return;
        if (!Cache.TryGetValue(Kind, out var geo))
        {
            geo = Geometry.Parse(p.Data);
            geo.Freeze();
            Cache[Kind] = geo;
        }
        double s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        var pen = new Pen(Foreground, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(p.Filled ? Foreground : null, p.Filled ? null : pen, geo);
        dc.Pop(); dc.Pop();
    }
}
