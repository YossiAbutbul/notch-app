using System.Windows;
using System.Windows.Controls;

namespace NotchApp.Controls;

/// <summary>
/// A border whose corners are always half its shorter side: a circle when square, a capsule when wide.
/// (WPF scales an oversized CornerRadius into an ellipse instead of clamping it like CSS does.)
/// </summary>
public sealed class Pill : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        CornerRadius = new CornerRadius(Math.Min(info.NewSize.Width, info.NewSize.Height) / 2);
    }
}
