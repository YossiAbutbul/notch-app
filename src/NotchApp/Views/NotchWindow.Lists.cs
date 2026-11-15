using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NotchApp.Controls;
using NotchApp.Services;
using IconEl = NotchApp.Controls.Icon;

namespace NotchApp.Views;

/// <summary>Clipboard and Shelf: lists in the hub, the drop zone, and drag in / drag out.</summary>
public partial class NotchWindow
{
    private const double Row = 44, RowGap = 4, Tile = 56, TileGap = 8;
    /// <summary>Clipboard and Notifications rows sit inside one card with a 4 px inset all round.</summary>
    private const double ListCardPad = 8;
    /// <summary>A row's hover fill inside the list card: concentric with the card's 20 px corner at a 4 px inset.</summary>
    private static readonly CornerRadius RowCorner = new(16);
    private static readonly Color Purple = Color.FromRgb(0x8E, 0x7B, 0xFF), Orange = Color.FromRgb(0xFF, 0x9F, 0x43), Amber = Color.FromRgb(0xFF, 0x9F, 0x0A);

    private double ClipListHeight()
    {
        int n = _clip.Items.Count;
        return n == 0 ? 96 : n > 3 ? 3 * (Row + RowGap) + Row / 2 : n * (Row + RowGap) - RowGap;   // a half row hints that it scrolls
    }

    private int ShelfRows => Math.Min(2, (int)Math.Ceiling(_shelf.Items.Count / 4.0));
    private double ShelfGridHeight() => _shelf.Items.Count == 0 ? 96 : ShelfRows * Tile + (ShelfRows - 1) * TileGap;

    /// <summary>
    /// Scroll edge effect: content fades out over the last 20 px on whichever side has more to scroll to,
    /// instead of being cut off by the edge.
    /// </summary>
    private static void ScrollEdges(ScrollViewer sv)
    {
        void Update()
        {
            double h = sv.ActualHeight;
            bool top = sv.VerticalOffset > 1, bottom = sv.VerticalOffset < sv.ScrollableHeight - 1;
            if (h <= 0 || (!top && !bottom)) { sv.OpacityMask = null; return; }
            double f = Math.Min(0.4, 20 / h);
            var m = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            m.GradientStops.Add(new GradientStop(top ? Colors.Transparent : Colors.Black, 0));
            m.GradientStops.Add(new GradientStop(Colors.Black, f));
            m.GradientStops.Add(new GradientStop(Colors.Black, 1 - f));
            m.GradientStops.Add(new GradientStop(bottom ? Colors.Transparent : Colors.Black, 1));
            m.Freeze();
            sv.OpacityMask = m;
        }
        sv.ScrollChanged += (_, _) => Update();
        sv.SizeChanged += (_, _) => Update();
    }

    private void WireLists()
    {
        foreach (var sv in new[] { ClipScroll, ShelfScroll, NotifScroll, AppsScroll, DevScroll }) ScrollEdges(sv);
        _clip.Changed += () => { if (_hub && _tab == "clip") RenderClip(); };
        _clip.Captured += OnCaptured;
        _shelf.Changed += () => { if (_hub && _tab == "shelf") RenderShelf(); };

        ClipClear.Click += (_, _) => _clip.ClearUnpinned();
        ShelfAdd.Click += (_, _) => AddFilesDialog();
        ShelfClear.Click += (_, _) => _shelf.Clear();
        ShelfSave.Click += async (_, _) =>
        {
            if (_shelf.Items.Count == 0) return;
            ShelfSave.IsEnabled = false;
            int n = await _shelf.SaveAllAsync();
            ShelfSave.Content = $"Saved {n}";
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ShelfStore.SaveFolder}\"") { UseShellExecute = true }); } catch { }
            await Task.Delay(1800);
            ShelfSave.Content = "Save all";
            ShelfSave.IsEnabled = true;
        };

        // files dragged from Explorer onto the notch
        Notch.DragEnter += OnDragOverNotch;
        Notch.DragOver += OnDragOverNotch;
        Notch.DragLeave += (_, _) => _dropLeave.Start();   // short grace: DragLeave also fires between child elements
        Notch.Drop += OnDrop;
    }

    // ================================================================ clipboard

    private void OnCaptured(ClipItem c)
    {
        if (_hub) return;   // the list updates in place instead
        string preview = c.Kind == ClipKind.Image ? "Image copied" : $"“{OneLine(c.Text)}”";
        ShowToast(new("copy", 320, Color.FromRgb(150, 175, 255), "clip", Purple, Colors.White, preview, "Saved", Color.FromArgb(0x94, 0xF4, 0xF5, 0xF8), false, 2000));
    }

    private static string OneLine(string s) => s.ReplaceLineEndings(" ").Trim() is var t && t.Length > 80 ? t[..80] + "…" : s.ReplaceLineEndings(" ").Trim();

    private void RenderClip()
    {
        var items = _clip.Items;
        ClipCount.Text = items.Count == 0 ? "Recent" : $"Recent · {items.Count} item{(items.Count == 1 ? "" : "s")}";
        ClipClear.Visibility = items.Any(i => !i.Pinned) ? Visibility.Visible : Visibility.Collapsed;
        ClipScroll.Height = ClipListHeight();
        ClipList.Children.Clear();
        if (items.Count == 0)
        {
            ClipList.Children.Add(Empty("Anything you copy shows up here. Pin the things you reuse.", dropTarget: false));
        }
        else
        {
            foreach (var c in items) ClipList.Children.Add(ClipRow(c));
        }
        PaneClip.Height = PaneHeight("clip");
        if (_hub) Apply();
    }

    private FrameworkElement ClipRow(ClipItem c)
    {
        var dim = (Brush)FindResource("Dim");
        var row = new Border { Height = Row, CornerRadius = RowCorner, Padding = new Thickness(6, 0, 4, 0), Margin = new Thickness(0, 0, 0, RowGap), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to copy" };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // leading visual: colour swatch, image thumbnail, or a kind icon
        // the glyph tile is one step lighter than the card it now sits on
        var box = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)) };
        if (c.Kind == ClipKind.Color && ClipboardHistory.ParseColor(c.Text) is { } col) box.Background = new SolidColorBrush(col);
        else if (c.Kind == ClipKind.Image && c.Thumb != null) box.Background = new ImageBrush(c.Thumb) { Stretch = Stretch.UniformToFill };
        else box.Child = new IconEl { Kind = c.Kind switch { ClipKind.Link => "link", ClipKind.Code => "code", ClipKind.Image => "image", _ => "text" }, Width = 15, Height = 15, Foreground = dim };
        g.Children.Add(box);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var main = new TextBlock { Text = OneLine(c.Text), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        if (c.Kind == ClipKind.Code) { main.FontFamily = new FontFamily("Cascadia Mono, Consolas"); main.FontSize = 12; }
        string meta = string.Join(" · ", new[] { c.Source, ClipboardHistory.Ago(c.At), c.Kind == ClipKind.Color && ClipboardHistory.ParseColor(c.Text) is { } k ? $"rgb {k.R} {k.G} {k.B}" : "" }.Where(s => s.Length > 0));
        text.Children.Add(main);
        text.Children.Add(new TextBlock { Text = meta, FontSize = 11, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 2);
        g.Children.Add(text);

        var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        if (c.Kind == ClipKind.Link)
            acts.Children.Add(Act("open", "Open link", dim, () => { try { Process.Start(new ProcessStartInfo(c.Text) { UseShellExecute = true }); } catch { } }));
        var copyBtn = Act("copy", "Copy", dim, () => { });
        copyBtn.Click += (_, _) => CopyWithFeedback(c, copyBtn);
        acts.Children.Add(copyBtn);
        acts.Children.Add(Act("pin", c.Pinned ? "Unpin" : "Pin", c.Pinned ? new SolidColorBrush(Amber) : dim, () => _clip.TogglePin(c)));
        acts.Children.Add(Act("x", "Remove", dim, () => _clip.Remove(c)));
        Grid.SetColumn(acts, 3);
        g.Children.Add(acts);

        row.Child = g;
        HoverFill(row);
        // actions appear with the hover; a pinned item keeps its pin showing
        foreach (UIElement a in acts.Children) a.Opacity = 0;
        if (c.Pinned) acts.Children[acts.Children.Count - 2].Opacity = 1;
        row.MouseEnter += (_, _) => { foreach (UIElement a in acts.Children) Ease(a, 1, 120); };
        row.MouseLeave += (_, _) => { for (int i = 0; i < acts.Children.Count; i++) Ease(acts.Children[i], c.Pinned && i == acts.Children.Count - 2 ? 1 : 0, 220); };
        row.MouseLeftButtonDown += (_, e) => { e.Handled = true; };                 // a click on a row copies; it doesn't start a pull
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; CopyWithFeedback(c, copyBtn); };
        return row;
    }

    /// <summary>A row's highlight fades in quickly and out a little slower, like a list cell.</summary>
    private static void HoverFill(Border row)
    {
        var brush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        row.Background = brush;
        row.MouseEnter += (_, _) => brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x11, 255, 255, 255), TimeSpan.FromMilliseconds(120)));
        row.MouseLeave += (_, _) => brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0, 255, 255, 255), TimeSpan.FromMilliseconds(240)));
    }

    private static void Ease(UIElement el, double to, int ms) =>
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    private async void CopyWithFeedback(ClipItem c, Button btn)
    {
        if (!_clip.CopyBack(c)) return;
        if (btn.Content is IconEl ic)
        {
            ic.Kind = "check";
            ic.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
            await Task.Delay(1100);
            ic.Kind = "copy";
            ic.Foreground = (Brush)FindResource("Dim");
        }
    }

    private Button Act(string icon, string tip, Brush fg, Action onClick)
    {
        var b = new Button { Style = (Style)FindResource("Press"), Width = 28, Height = 28, ToolTip = tip, Content = new IconEl { Kind = icon, Width = 15, Height = 15, Foreground = fg } };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>
    /// An empty pane. Dashes mean "drop here" (the Shelf), so only drop targets get them; a list that's simply empty
    /// shows its message on the plain card it lives in.
    /// </summary>
    private FrameworkElement Empty(string text, bool dropTarget = true)
    {
        var g = new Grid { Height = 96 };
        if (dropTarget) g.Children.Add(new Rectangle { RadiusX = 20, RadiusY = 20, Stroke = (Brush)FindResource("Faint"), StrokeThickness = 1.5, StrokeDashArray = [4, 3] });
        g.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Dim"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 24, 0) });
        return g;
    }

    // ================================================================ shelf

    private void RenderShelf()
    {
        var items = _shelf.Items;
        ShelfCount.Text = items.Count == 0 ? "Shelf" : $"Shelf · {items.Count} item{(items.Count == 1 ? "" : "s")} · {ShelfStore.Bytes(_shelf.TotalSize)}";
        ShelfSave.Visibility = ShelfClear.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfGrid.Children.Clear();
        if (items.Count == 0)
        {
            ShelfGrid.Columns = 1;
            ShelfGrid.Margin = new Thickness(0);
            ShelfGrid.Children.Add(Empty("Drag files onto the notch to keep them here, then drag them out into any app."));
        }
        else
        {
            ShelfGrid.Columns = 4;
            ShelfGrid.Margin = new Thickness(0, 0, -TileGap, -TileGap);   // the last row's gap isn't content
            foreach (var i in items) ShelfGrid.Children.Add(ShelfTile(i));
        }
        ShelfScroll.Height = ShelfGridHeight() + (items.Count > 0 ? 0 : 0);
        PaneShelf.Height = PaneHeight("shelf");
        if (_hub) Apply();
    }

    private FrameworkElement ShelfTile(ShelfItem it)
    {
        var dim = (Brush)FindResource("Dim");
        var tile = new Border { Height = Tile, Margin = new Thickness(0, 0, TileGap, TileGap), CornerRadius = new CornerRadius(14), Background = (Brush)FindResource("Card"), Padding = new Thickness(11, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = it.Path };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        g.ColumnDefinitions.Add(new ColumnDefinition());

        var ic = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        if (it.IsImage && it.Preview != null) ic.Background = new ImageBrush(it.Preview) { Stretch = Stretch.UniformToFill };
        else if (it.Preview != null) ic.Child = new Image { Source = it.Preview, Width = 26, Height = 26 };
        else { ic.Background = new SolidColorBrush(Orange); ic.Child = new IconEl { Kind = it.IsFolder ? "folder" : "file", Width = 16, Height = 16, Foreground = Brushes.White }; }
        g.Children.Add(ic);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = it.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = it.IsFolder ? "Folder" : ShelfStore.Bytes(it.Size), FontSize = 10.5, Foreground = dim });
        Grid.SetColumn(text, 2);
        g.Children.Add(text);

        var rm = new Button { Style = (Style)FindResource("Press"), Width = 20, Height = 20, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x33)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, -6, 0), Opacity = 0, ToolTip = "Remove from shelf", Content = new IconEl { Kind = "x", Width = 11, Height = 11, StrokeWidth = 2.4 } };
        rm.Click += (_, _) => _shelf.Remove(it);
        Grid.SetColumnSpan(rm, 3);
        g.Children.Add(rm);
        tile.Child = g;
        tile.MouseEnter += (_, _) => Ease(rm, 1, 120);
        tile.MouseLeave += (_, _) => Ease(rm, 0, 220);

        // drag a file back out into any app; double-click opens it
        Point? down = null;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); } catch { } return; }
            down = e.GetPosition(tile);
        };
        tile.MouseLeftButtonUp += (_, _) => down = null;
        tile.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(tile);
            if (Math.Abs(p.X - d.X) < 6 && Math.Abs(p.Y - d.Y) < 6) return;
            down = null;
            _dragOut = true;
            try { DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { it.Path }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch { }
            finally { _dragOut = false; }
        };
        return tile;
    }

    private bool _dragOut;

    private void AddFilesDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Add to Shelf" };
        if (dlg.ShowDialog() == true) _shelf.Add(dlg.FileNames);
    }

    // ================================================================ drop zone

    private void OnDragOverNotch(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_dragOut || !e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        if (_hub && _tab == "apps") { e.Effects = DragDropEffects.Link; return; }   // dropping on Apps adds launcher items
        e.Effects = DragDropEffects.Copy;
        _dropLeave.Stop();
        if (_dropHover) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        _dropCount = files.Length;
        DropTitle.Text = _dropCount > 1 ? $"Drop {_dropCount} files to keep on Shelf" : "Drop to keep on Shelf";
        _dropHover = true;
        _hoverIntent.Stop();
        Apply();
        Kick();   // the notch reaches out to catch it
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _dropLeave.Stop();
        _dropHover = false;
        if (_dragOut || e.Data.GetData(DataFormats.FileDrop) is not string[] files) { Apply(); return; }
        if (_hub && _tab == "apps") { AddApps(files); return; }
        int n = _shelf.Add(files);
        Kick();
        if (_cfg.ImpactBounce) H.Velocity -= 520;   // swallows the drop
        if (_hub) SetTab("shelf");
        else if (n > 0) ShowToast(new("files", 290, Color.FromRgb(255, 180, 80), "shelf", Orange, Colors.White, $"{n} item{(n == 1 ? "" : "s")} on Shelf", "", Colors.White, false, 2000));
        Apply();
    }
}
