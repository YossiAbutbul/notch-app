using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NotchApp.Services;
using IconEl = NotchApp.Controls.Icon;

namespace NotchApp.Views;

/// <summary>
/// Clipboard photo viewer: an image in the history opens big inside the Clipboard tab. It grows out of its thumbnail
/// and shrinks back into it, the hub resizes to the photo's shape, and the arrows step through the other images.
/// </summary>
public partial class NotchWindow
{
    private ClipItem? _viewing;
    /// <summary>Each image row's thumbnail, so the viewer can grow out of it and go back into it.</summary>
    private readonly Dictionary<ClipItem, FrameworkElement> _clipThumbs = [];
    private const double ViewBarH = 34, ViewPad = 16, ViewMaxImgH = 270 - 34 - 16, ViewMinImgH = 96;
    private int _viewToken;

    private void WireClipViewer()
    {
        ViewBack.Click += (_, _) => CloseViewer(animate: true);
        ViewPrev.Click += (_, _) => StepViewer(-1);
        ViewNext.Click += (_, _) => StepViewer(1);
        ViewCopy.Click += async (_, _) =>
        {
            if (_viewing == null || !_clip.CopyBack(_viewing)) return;
            ViewCopy.Content = "Copied";
            await Task.Delay(1100);
            ViewCopy.Content = "Copy";
        };
        ViewSave.Click += async (_, _) =>
        {
            if (_viewing?.Image is not { } img) return;
            var path = SaveImage(img, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            if (path == null) return;
            ViewSave.Content = "Saved";
            ViewSave.ToolTip = path;
            await Task.Delay(1400);
            ViewSave.Content = "Save";
        };
        ViewOpen.Click += (_, _) =>
        {
            if (_viewing?.Image is not { } img) return;
            var path = SaveImage(img, Path.Combine(Path.GetTempPath(), "Notch"));
            if (path == null) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { Log.Error("Open image", ex); }
        };
    }

    private List<ClipItem> ClipImages() => _clip.Items.Where(i => i.Kind == ClipKind.Image && i.Image != null).ToList();

    /// <summary>The photo's height in the viewer: the full width when it fits, never bigger than its own pixels.</summary>
    private double ViewImgHeight(ClipItem c, out double w)
    {
        var img = c.Image!;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double nw = img.PixelWidth / dpi, nh = img.PixelHeight / dpi;
        double maxW = PaneW - ViewPad;
        double s = Math.Min(1, Math.Min(maxW / nw, ViewMaxImgH / nh));
        w = Math.Max(1, nw * s);
        return Math.Max(1, nh * s);
    }

    private double ViewerHeight => _viewing == null ? 0 : ViewBarH + ViewPad + Math.Max(ViewMinImgH, ViewImgHeight(_viewing, out _));

    private void OpenViewer(ClipItem c)
    {
        if (c.Image == null) return;
        int token = ++_viewToken;
        _viewing = c;
        FillViewer(c);
        bool motion = SystemParameters.ClientAreaAnimation;
        ClipViewer.Visibility = Visibility.Visible;
        PaneClip.Height = PaneHeight("clip");
        Apply();
        if (!motion)
        {
            ClipListView.Visibility = Visibility.Collapsed;
            ClipViewer.Opacity = 1;
            return;
        }
        // the photo grows out of its thumbnail; the list and the bar trade places with a quick cross-fade
        PaneClip.UpdateLayout();
        var from = _clipThumbs.TryGetValue(c, out var th) && th.IsVisible ? th : null;
        ClipListView.BeginAnimation(OpacityProperty, Anim(0, 120));
        ViewBar.Opacity = 0;
        ViewBar.BeginAnimation(OpacityProperty, Anim(1, 240, 90));
        ClipViewer.Opacity = 1;
        ViewCard.Background = Brushes.Transparent;
        ViewCard.BeginAnimation(OpacityProperty, null);
        Morph(from, toThumb: false, () =>
        {
            if (token != _viewToken) return;
            ClipListView.Visibility = Visibility.Collapsed;
        });
        // the card behind it fills in once the photo has mostly arrived
        var card = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        ViewCard.Background = card;
        card.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x11, 255, 255, 255), TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(160) });
    }

    private void FillViewer(ClipItem c)
    {
        double h = ViewImgHeight(c, out double w);
        ViewImg.Width = w; ViewImg.Height = h;
        ViewImg.Background = new ImageBrush(c.Image) { Stretch = Stretch.Fill };
        ViewImg.CornerRadius = new CornerRadius(Math.Min(12, Math.Min(w, h) / 4));
        ViewCard.Height = Math.Max(ViewMinImgH, h) + ViewPad;
        var all = ClipImages();
        int i = all.IndexOf(c);
        bool many = all.Count > 1;
        ViewPrev.Visibility = ViewNext.Visibility = ViewCount.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        ViewPrev.IsEnabled = i > 0;
        ViewNext.IsEnabled = i < all.Count - 1;
        ViewCount.Text = $"{i + 1} of {all.Count}";
        ViewInfo.Text = string.Join(" · ", new[] { $"{c.Image!.PixelWidth} × {c.Image.PixelHeight}", c.Source, ClipboardHistory.Ago(c.At) }.Where(s => !string.IsNullOrEmpty(s)));
        ViewSave.ToolTip = "Save to Downloads";
    }

    /// <summary>Newer (−1) or older (+1) image: the new one slides in from the side it comes from.</summary>
    private void StepViewer(int dir)
    {
        if (_viewing == null) return;
        var all = ClipImages();
        int i = all.IndexOf(_viewing) + dir;
        if (i < 0 || i >= all.Count) return;
        _viewing = all[i];
        FillViewer(_viewing);
        PaneClip.Height = PaneHeight("clip");
        Apply();
        if (!SystemParameters.ClientAreaAnimation) return;
        var t = new TranslateTransform(dir * 28, 0);
        ViewImg.RenderTransform = t;
        ViewImg.Opacity = 0;
        ViewImg.BeginAnimation(OpacityProperty, Anim(1, 220));
        t.BeginAnimation(TranslateTransform.XProperty, Anim(0, 340));
    }

    /// <summary>Back to the list: the photo shrinks into its thumbnail while the list fades in around it.</summary>
    private void CloseViewer(bool animate)
    {
        if (_viewing == null) return;
        var c = _viewing;
        int token = ++_viewToken;
        _viewing = null;
        ClipListView.Visibility = Visibility.Visible;
        if (!animate || !SystemParameters.ClientAreaAnimation || !_hub)
        {
            ClipListView.BeginAnimation(OpacityProperty, null); ClipListView.Opacity = 1;
            ClipViewer.Visibility = Visibility.Collapsed;
            ViewImg.RenderTransform = null;
            if (_hub && _tab == "clip") { PaneClip.Height = PaneHeight("clip"); Apply(); }
            return;
        }
        PaneClip.Height = PaneHeight("clip");
        Apply();
        PaneClip.UpdateLayout();
        // bring the photo's row into view so it has somewhere to land
        if (_clipThumbs.TryGetValue(c, out var th)) th.BringIntoView();
        PaneClip.UpdateLayout();
        ClipListView.BeginAnimation(OpacityProperty, Anim(1, 240, 60));
        ViewBar.BeginAnimation(OpacityProperty, Anim(0, 100));
        if (ViewCard.Background is SolidColorBrush { IsFrozen: false } card)
            card.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, 255, 255, 255), TimeSpan.FromMilliseconds(140)));
        Morph(th is { IsVisible: true } ? th : null, toThumb: true, () =>
        {
            if (token != _viewToken) return;
            ClipViewer.Visibility = Visibility.Collapsed;
            ViewImg.RenderTransform = null;
            ViewImg.Opacity = 1;
        });
    }

    /// <summary>
    /// Moves the photo between its full place and the thumbnail's place. Without a thumbnail to go to (scrolled away),
    /// it grows or shrinks a little from its own centre and fades instead.
    /// </summary>
    private void Morph(FrameworkElement? thumb, bool toThumb, Action done)
    {
        ViewImg.UpdateLayout();
        double w = ViewImg.ActualWidth, h = ViewImg.ActualHeight;
        var sc = new ScaleTransform(1, 1);
        var tr = new TranslateTransform();
        var g = new TransformGroup { Children = { sc, tr } };
        ViewImg.RenderTransformOrigin = new Point(0, 0);
        ViewImg.RenderTransform = g;

        double s, dx, dy;
        if (thumb != null && w > 0 && h > 0)
        {
            var p = thumb.TranslatePoint(new Point(0, 0), ViewImg);
            // the photo fits inside the thumbnail's square, centred on it
            s = Math.Min(thumb.ActualWidth / w, thumb.ActualHeight / h);
            dx = p.X + (thumb.ActualWidth - w * s) / 2;
            dy = p.Y + (thumb.ActualHeight - h * s) / 2;
        }
        else
        {
            s = 0.9; dx = w * 0.05; dy = h * 0.05;
        }
        var ms = toThumb ? 300 : 380;
        void Run(DependencyObject o, DependencyProperty prop, double a, double b, Action? end = null)
        {
            var an = new DoubleAnimation(toThumb ? a : b, toThumb ? b : a, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            if (end != null) an.Completed += (_, _) => end();
            ((IAnimatable)o).BeginAnimation(prop, an);
        }
        Run(sc, ScaleTransform.ScaleXProperty, 1, s);
        Run(sc, ScaleTransform.ScaleYProperty, 1, s);
        Run(tr, TranslateTransform.XProperty, 0, dx);
        Run(tr, TranslateTransform.YProperty, 0, dy, done);
        // with no thumbnail to land on, it fades so it doesn't just stop
        if (thumb == null) ViewImg.BeginAnimation(OpacityProperty, new DoubleAnimation(toThumb ? 1 : 0, toThumb ? 0 : 1, TimeSpan.FromMilliseconds(toThumb ? 160 : 240)));
        else { ViewImg.BeginAnimation(OpacityProperty, null); ViewImg.Opacity = 1; }
    }

    private static DoubleAnimation Anim(double to, int ms, int delay = 0) =>
        new(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    /// <summary>Writes the image as a PNG named by when it's saved; returns the path, or null when it can't.</summary>
    private static string? SaveImage(BitmapSource img, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Clipboard image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var f = File.Create(path);
            enc.Save(f);
            return path;
        }
        catch (Exception ex) { Log.Error("Save image", ex); return null; }
    }
}
