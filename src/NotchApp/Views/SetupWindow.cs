using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotchApp.Services;

namespace NotchApp.Views;

public enum SetupMode { Install, Update, Uninstall }

/// <summary>
/// The small window the published Notch.exe shows when it isn't installed yet (Install / Run without installing),
/// when it's newer than the installed copy (Update), and for --uninstall.
/// </summary>
public sealed class SetupWindow : Window
{
    private readonly SetupMode _mode;
    private readonly StackPanel _body = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _status = new() { Opacity = 0.7, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar _bar = new() { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };

    /// <summary>The user chose to run this copy without installing.</summary>
    public event Action? RunPortable;

    public SetupWindow(SetupMode mode)
    {
        _mode = mode;
        Title = mode == SetupMode.Uninstall ? "Uninstall Notch" : "Notch setup";
        Width = 460; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Notch.ico")); } catch { }

        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 24) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        head.Children.Add(new Image { Source = LargeIcon(), Width = 64, Height = 64, Margin = new Thickness(-4, -4, 14, -4) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Notch", FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        titles.Children.Add(new TextBlock { Text = $"A fluid, Apple-style notch for Windows · {Installer.Current.ToString(3)}", Opacity = 0.65, FontSize = 13 });
        head.Children.Add(titles);
        root.Children.Add(head);
        root.Children.Add(_body);
        root.Children.Add(_bar);
        var foot = new Grid();
        foot.Children.Add(_status);
        foot.Children.Add(_buttons);
        root.Children.Add(foot);
        Content = root;

        if (mode == SetupMode.Uninstall) BuildUninstall(); else BuildInstall();
    }

    private static ImageSource? LargeIcon()
    {
        try
        {
            var dec = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/Notch.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return dec.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch { return null; }
    }

    private static Button Btn(string text, bool accent)
    {
        var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        // the Fluent theme's accent style lives in the window's theme dictionary, available once loaded
        if (accent) b.Loaded += (_, _) => { if (b.TryFindResource("AccentButtonStyle") is Style s) b.Style = s; };
        return b;
    }

    private void Busy(bool on, string text = "")
    {
        _bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _body.IsEnabled = _buttons.IsEnabled = !on;
        _status.Text = text;
    }

    // ------------------------------------------------------------ install / update

    private void BuildInstall()
    {
        var installed = Installer.InstalledVersion;
        string lead = _mode == SetupMode.Update && installed != null
            ? installed < Installer.Current ? $"Notch {installed.ToString(3)} is installed. Update it to {Installer.Current.ToString(3)}?"
              : "Notch is already installed. Install this copy over it?"
            : "Install Notch for your account? It needs no administrator rights and you can remove it any time from Settings → Apps.";
        _body.Children.Add(new TextBlock { Text = lead, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var startup = new CheckBox { Content = "Start Notch when I sign in", IsChecked = _mode != SetupMode.Update || Settings.AppSettings.Load().StartWithWindows, Margin = new Thickness(0, 0, 0, 6) };
        var desktop = new CheckBox { Content = "Add a desktop shortcut", IsChecked = _mode != SetupMode.Update || Installer.HasDesktopShortcut, Margin = new Thickness(0, 0, 0, 20) };
        _body.Children.Add(startup);
        _body.Children.Add(desktop);

        var portable = Btn("Run without installing", false);
        portable.Click += (_, _) => { RunPortable?.Invoke(); Close(); };
        var go = Btn(_mode == SetupMode.Update ? "Update" : "Install", true);
        go.IsDefault = true;
        go.Click += async (_, _) =>
        {
            Busy(true, "Installing…");
            try
            {
                await Installer.InstallAsync(desktop.IsChecked == true, startup.IsChecked == true, new Progress<string>(s => _status.Text = s));
                Busy(false, "");
                _body.Children.Clear();
                _body.Children.Add(new TextBlock { Text = "Notch is installed. Find it in the Start menu; it lives in the tray while it runs.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
                _buttons.Children.Clear();
                var open = Btn("Open Notch", true);
                open.IsDefault = true;
                open.Click += (_, _) => { Installer.LaunchInstalled(); Close(); };
                _buttons.Children.Add(open);
            }
            catch (Exception ex)
            {
                Log.Error("Install", ex);
                Busy(false, "Couldn't install: " + ex.Message);
            }
        };
        _buttons.Children.Add(portable);
        _buttons.Children.Add(go);
    }

    // ------------------------------------------------------------ uninstall

    private void BuildUninstall()
    {
        _body.Children.Add(new TextBlock { Text = "Remove Notch, its shortcuts and its start-up entry?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var data = new CheckBox { Content = "Also delete my settings, pinned clips and shelf list", Margin = new Thickness(0, 0, 0, 20) };
        _body.Children.Add(data);
        var cancel = Btn("Cancel", false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        var go = Btn("Uninstall", true);
        go.Click += async (_, _) =>
        {
            Busy(true, "Removing…");
            try
            {
                await Installer.UninstallAsync(data.IsChecked == true);
                Busy(false, "");
                _body.Children.Clear();
                _body.Children.Add(new TextBlock { Text = "Notch was removed. Thanks for trying it.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
                _buttons.Children.Clear();
                var close = Btn("Close", true);
                close.IsDefault = true;
                close.Click += (_, _) => Close();
                _buttons.Children.Add(close);
            }
            catch (Exception ex)
            {
                Log.Error("Uninstall", ex);
                Busy(false, "Couldn't finish: " + ex.Message);
            }
        };
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(go);
    }
}
