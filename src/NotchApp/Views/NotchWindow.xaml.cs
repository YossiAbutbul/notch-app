using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NotchApp.Interop;
using NotchApp.Motion;
using NotchApp.Services;
using NotchApp.Settings;

namespace NotchApp.Views;

/// <summary>
/// The notch: one transparent, click-through, always-on-top window per app. Every size, position and offset
/// is a spring, stepped once per display frame; the frame loop stops itself when nothing is moving.
/// </summary>
public partial class NotchWindow : Window
{
    private const double WinW = 760, WinH = 380, EdgeInset = 28, HubChrome = 68;   // 12 inset + 32 tabs + 12 gap + 12 inset

    private readonly AppSettings _cfg;
    private readonly MediaService _media;
    private readonly FocusTimer _timer;
    private readonly SystemStats _stats;
    private readonly SystemVolume _vol = new();
    private readonly ClipboardHistory _clip;
    private readonly ShelfStore _shelf;
    private readonly DevicesService _dev;
    private readonly NotificationsService _notifs;
    private bool _dropHover;
    private int _dropCount;
    private readonly DispatcherTimer _dropLeave;

    /// <summary>Settings section to open (null = the first one).</summary>
    public event Action<string?>? SettingsRequested;

    private SpringParams _every, _mom;
    private readonly Spring W, H, R, LeanX, Press, Off, TabX, TabW, Pane, VolFill, Swipe;

    private string _key = "", _tab = "now";
    private bool _hub;
    private string? _openMode;                    // "hover" or "sticky"
    private ToastSpec? _toast;
    private bool _hoverNudge, _mouseOver, _hidden;
    private DragState? _drag;
    private DateTime _pausedAt = DateTime.MinValue;
    private bool _wasPlaying;

    // Line style: music or the timer starting shows its notch for a few seconds before it settles into the line
    private string? _announce;
    private readonly DispatcherTimer _announceTimer = new() { Interval = TimeSpan.FromSeconds(3.2) };
    private bool _timerWasRunning;
    private string _lastSong = "";
    /// <summary>How much of the line is drawn (0–1): it draws out from the centre as the notch collapses into it.</summary>
    private readonly Spring LineDraw = new(1, new SpringParams(0.72, 0.55), 0.002);

    private Color _glowCur = Color.FromRgb(255, 98, 120), _glowTarget = Color.FromRgb(255, 98, 120);
    private double _glowA, _glowCurA, _clock;

    private IntPtr _hwnd;
    private DisplayInfo? _display;
    private string _displaySetting;
    private bool _moving;

    private bool _rendering;
    private TimeSpan _lastRender;
    private DateTime _lastTick = DateTime.UtcNow;

    private readonly DispatcherTimer _toastTimer, _hoverIntent, _leaveGrace, _poll, _tick, _fullscreen;
    private readonly Rectangle[] _barsS, _barsL;
    private readonly Dictionary<string, FrameworkElement> _layers;
    private readonly Dictionary<FrameworkElement, ScaleTransform> _layerScale = new();
    /// <summary>Per layer: scales content with the shape's live width, so it grows inside the notch instead of being cropped.</summary>
    private readonly Dictionary<FrameworkElement, ScaleTransform> _layerFit = new();
    private SpringParams _grow;
    private int _tabSwap;
    private readonly TranslateTransform _musicSwipe = new();

    private sealed record ToastSpec(string Type, double Width, Color Glow, string Icon, Color IconBg, Color IconFg, string Text, string Value, Color ValueColor, bool Bar, int Ms);

    private sealed class DragState(Point start, bool fromHub, string keyAtStart)
    {
        public Point Start = start;
        public bool FromHub = fromHub;
        public string KeyAtStart = keyAtStart;
        public char Axis;          // '\0' undecided, 'y' pull, 'x' swipe
        public bool Moved;
        public (double W, double H) Compact, Expanded;
        public readonly List<(Point P, long T)> Samples = [];
    }

    public NotchWindow(AppSettings cfg, MediaService media, FocusTimer timer, SystemStats stats, ClipboardHistory clip, ShelfStore shelf, DevicesService dev, NotificationsService notifs)
    {
        _cfg = cfg; _media = media; _timer = timer; _stats = stats; _clip = clip; _shelf = shelf; _dev = dev; _notifs = notifs;
        _displaySetting = cfg.Display;
        InitializeComponent();

        (_every, _mom) = Feels.Get(cfg.Feel);
        _grow = GrowOf(_every);
        W = new Spring(cfg.IdleWidth, _every); H = new Spring(34, _every); R = new Spring(17, _every);
        LeanX = new Spring(0, _every); Off = new Spring(0, _every); Press = new Spring(1, SpringParams.Press, 0.0005);
        TabX = new Spring(0, _every); TabW = new Spring(80, _every); Pane = new Spring(0, _every);
        VolFill = new Spring(_vol.Level, _every, 0.002); Swipe = new Spring(0, _mom);

        _layers = new() { ["music"] = LMusic, ["timer"] = LTimer, ["toast"] = LToast, ["hub"] = LHub, ["drop"] = LDrop, ["notif"] = LNotif };
        foreach (var el in _layers.Values)
        {
            var sc = new ScaleTransform(0.94, 0.94);
            _layerScale[el] = sc;
            el.RenderTransformOrigin = new Point(0.5, 0);
            var fit = new ScaleTransform(1, 1);
            _layerFit[el] = fit;
            var g = new TransformGroup();
            g.Children.Add(sc);
            g.Children.Add(fit);
            if (el == LMusic) g.Children.Add(_musicSwipe);
            el.RenderTransform = g;
        }

        _barsS = MakeBars(BarsSmall, 4);
        _barsL = MakeBars(BarsLarge, 5);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2300) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast = null; Apply(); };
        _hoverIntent = new DispatcherTimer();
        _hoverIntent.Tick += (_, _) => { _hoverIntent.Stop(); if (_mouseOver && !_hub && _drag == null && _notif == null) OpenHub("hover"); };
        _leaveGrace = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _leaveGrace.Tick += (_, _) => { _leaveGrace.Stop(); if (!_mouseOver && _hub && _openMode == "hover") CloseHub(); };
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _poll.Tick += (_, _) => Poll();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) => Tick();
        _fullscreen = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _fullscreen.Tick += (_, _) => CheckFullscreen();
        _dropLeave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _dropLeave.Tick += (_, _) => { _dropLeave.Stop(); _dropHover = false; Apply(); };
        _announceTimer.Tick += (_, _) => EndAnnounce();

        PressFeedback();
        WireInput();
        WireLists();
        WireClipViewer();
        WireDevicesApps();
        WireNotifs();
        _media.Changed += OnMediaChanged;
        _timer.Changed += UpdateTimerUI;
        _timer.PhaseEnded += OnPhaseEnded;
        _stats.ChargerChanged += plugged => Dispatcher.InvokeAsync(() => OnCharger(plugged));
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(() => PlaceOn(Displays.Resolve(_cfg.Display, CursorPx())));

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
            HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == Native.WM_CLIPBOARDUPDATE) _clip.OnClipboardUpdate();
                return IntPtr.Zero;
            });
            _clip.Attach(_hwnd);
        };
        Loaded += (_, _) =>
        {
            PlaceOn(Displays.Resolve(_cfg.Display, CursorPx()));
            LayoutHub();
            OnMediaChanged();
            UpdateTimerUI();
            UpdateSystemUI();
            Apply();
            _poll.Start(); _tick.Start(); _fullscreen.Start();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render, Align);
    }

    // ================================================================ placement & displays

    private static System.Drawing.Point CursorPx()
    {
        Native.GetCursorPos(out var p);
        return new System.Drawing.Point(p.X, p.Y);
    }

    /// <summary>Move onto a monitor first (Windows rescales us for its DPI), then align once the size is final.</summary>
    private void PlaceOn(DisplayInfo d)
    {
        if (_hwnd == IntPtr.Zero) return;
        _display = d;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, d.Bounds.Left, d.Bounds.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Align);
    }

    private void Align()
    {
        if (_display is not { } d || _hwnd == IntPtr.Zero) return;
        Native.GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left;
        int x = _cfg.Position switch
        {
            "left" => d.Bounds.Left,
            "right" => d.Bounds.Right - w,
            _ => d.Bounds.Left + (d.Bounds.Width - w) / 2,
        };
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, d.Bounds.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    /// <summary>The notch leaves toward the other display and arrives from the matching side.</summary>
    private void MoveTo(DisplayInfo target)
    {
        if (_display?.Id == target.Id || _moving) { Align(); return; }
        var all = Displays.All();
        int from = all.ToList().FindIndex(x => x.Id == _display?.Id), to = all.ToList().FindIndex(x => x.Id == target.Id);
        int dir = to >= from ? 1 : -1;
        CloseHub();
        _moving = true;
        Off.Params = _every;
        Off.Target = dir * WinW;
        EnsureFrames();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            PlaceOn(target);
            Off.Snap(-dir * WinW);
            Off.Target = 0;
            Off.Kick(_mom, _every);
            _moving = false;
            EnsureFrames();
        };
        t.Start();
    }

    /// <summary>Notch centre within the window: left and right placements stay anchored to their edge as it grows.</summary>
    private double NX(double w) => _cfg.Position switch
    {
        "left" => EdgeInset + w / 2,
        "right" => WinW - EdgeInset - w / 2,
        _ => WinW / 2,
    };

    private void CheckFullscreen()
    {
        bool hide = false;
        if (_cfg.HideInFullscreen && Native.SHQueryUserNotificationState(out var st) == 0) hide = st is 2 or 3 or 4;
        if (hide == _hidden) return;
        _hidden = hide;
        Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        if (!hide) Align();
    }

    // ================================================================ state

    private string Activity()
    {
        if (_timer.Active && _cfg.IsLive("timer")) return "timer";
        if (_media.HasSession && _cfg.IsLive("music") && (_media.IsPlaying || (DateTime.UtcNow - _pausedAt).TotalSeconds < 30)) return "music";
        return "idle";
    }

    private string Key() => _dropHover ? "drop" : _hub ? "hub" : _notif != null ? "notif" : _toast != null ? "toast" : Activity();

    private (double W, double H) SizeOf(string k) => k switch
    {
        "drop" => (420, 118),
        "notif" => NotifSize,
        "hub" => (_cfg.HubWidth, HubHeight(_tab)),
        "toast" => (_toast?.Width ?? 260, 40),
        // Line style: closed, everything is a thin bar. The shape is a strip a little bigger than the bar, so it's easy
        // to land on; only the bar is drawn. Something live (music, timer) gets a longer bar than idle.
        "music" or "timer" when LineStyle && _announce != k => (_cfg.IdleWidth + LinePad, 12),
        "music" => (236, 38),
        "timer" => (206, 38),
        _ => LineStyle ? (Math.Round(_cfg.IdleWidth * 0.7) + LinePad, 12) : (_cfg.IdleWidth, 34),
    };

    private bool LineStyle => _cfg.IdleStyle != "pill";
    private const double LinePad = 24;

    /// <summary>Every pane is exactly as tall as its content, so the hub never shows dead space.</summary>
    private double PaneHeight(string t) => t switch
    {
        "system" => 144,
        "clip" => _viewing != null ? ViewerHeight : 34 + ListCardPad + ClipListHeight(),
        "shelf" => 34 + ShelfGridHeight(),
        "dev" => 152,
        "notif" => 34 + ListCardPad + NotifListHeight(),
        "apps" => AppsHeight(),
        "now" => _media.HasSession ? 140 : 64,   // nothing playing: a single row, not an empty player
        _ => 140,
    };
    private double HubHeight(string t) => HubChrome + PaneHeight(t);
    private double PaneW => _cfg.HubWidth - 24;

    /// <summary>Retarget the springs for the current state. Never animates from the target: always from where things are.</summary>
    private void Apply()
    {
        if (_drag?.Axis == 'y') return;
        var k = Key();
        var (w, h) = SizeOf(k);
        if (_hoverNudge && k != "hub")
        {
            // resting on a line grows it into its notch (a small pill when idle, the music or timer pill when live):
            // a peek at what it holds, and a hint that it opens
            if (LineStyle && k is "idle" or "music" or "timer")
                (w, h) = k == "idle" ? (_cfg.IdleWidth, 34) : k == "music" ? (236, 38) : (206, 38);
            else if (k != "notif") { w += 14; h += 3; }   // a notification opens to its whole message instead
        }
        bool changed = Math.Abs(w - W.Target) > 0.5 || Math.Abs(h - H.Target) > 0.5;
        bool growing = w > W.Target + 0.5 || h > H.Target + 0.5;
        if (changed && _drag == null)
        {
            // like the Dynamic Island: things arriving overshoot a touch, things leaving settle without a bounce
            if (growing && SystemParameters.ClientAreaAnimation) { W.Kick(_grow, _every); H.Kick(_grow, _every); R.Kick(_grow, _every); }
            else if (W.RestoreTo == null) { W.Params = H.Params = R.Params = _every; }
        }
        // collapsing into the line from anything bigger (the hub closing, a pop-up ending): the line draws itself out
        // from the centre while the shape dissolves into it, instead of appearing at full length
        bool toLine = LineStyle && h <= 12 && !_hoverNudge;
        if (toLine && k != _key && _key != "" && H.Value > 14 && SystemParameters.ClientAreaAnimation)
        {
            LineDraw.Snap(0.15);
            LineDraw.Target = 1;
        }
        // settling into the line is a calmer move than the everyday spring: a third slower, so the dissolve can be seen
        if (toLine && changed && _drag == null)
        {
            var calm = new SpringParams(1.0, _every.Response * 1.35);
            W.Params = H.Params = R.Params = calm;
            W.RestoreTo = H.RestoreTo = R.RestoreTo = _every;
        }
        W.Target = w; H.Target = h;
        R.Target = h <= 46 ? h / 2 : Math.Min(_cfg.CornerRadius, h / 2);
        if (k != _key) { SwitchLayers(k); _key = k; }
        GlowTarget(k);
        EnsureFrames();
    }

    private void SwitchLayers(string to)
    {
        foreach (var (k, el) in _layers) Fade(el, k == to);
    }

    /// <summary>Content materialises a beat after the shape starts moving, and leaves quickly.</summary>
    /// <summary>
    /// Content materialises a beat after the shape starts moving (a short blur resolving to sharp) and leaves a little
    /// faster than it came, blurring as it goes. Scale is handled per frame by the fit transform.
    /// </summary>
    private void Fade(FrameworkElement el, bool on)
    {
        el.IsHitTestVisible = on;
        bool motion = SystemParameters.ClientAreaAnimation;
        // what leaves clears out before what arrives is readable, so two layers never overlap
        var dur = TimeSpan.FromMilliseconds(on ? 260 : 120);
        var begin = on ? TimeSpan.FromMilliseconds(70) : TimeSpan.Zero;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation { To = on ? 1 : 0, Duration = dur, BeginTime = begin, EasingFunction = new CubicEase { EasingMode = on ? EasingMode.EaseOut : EasingMode.EaseIn } });
        var sc = _layerScale[el];
        double s = on || !motion ? 1 : 0.96;
        var a = new DoubleAnimation { To = s, Duration = TimeSpan.FromMilliseconds(on ? 380 : 120), BeginTime = begin, EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        if (!motion)
        {
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, null); sc.BeginAnimation(ScaleTransform.ScaleYProperty, null); sc.ScaleX = sc.ScaleY = 1;
            el.Effect = null;
            return;
        }
        // blur: arrives soft and resolves, leaves by softening; removed when done so text renders crisp
        var blur = el.Effect as System.Windows.Media.Effects.BlurEffect ?? new System.Windows.Media.Effects.BlurEffect { Radius = on ? 10 : 0, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        el.Effect = blur;
        var b = new DoubleAnimation { To = on ? 0 : 8, Duration = TimeSpan.FromMilliseconds(on ? 300 : 120), BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        b.Completed += (_, _) => { if (el.Effect == blur && (on ? el.Opacity > 0.99 : true)) { blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, null); el.Effect = null; } };
        blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, b);
    }

    /// <summary>The "arrival" spring: a touch of overshoot on top of the chosen feel.</summary>
    private static SpringParams GrowOf(SpringParams every) => new(Math.Min(every.DampingRatio, 1) * 0.84, every.Response * 1.08);

    private void Kick()
    {
        W.Kick(_mom, _every); H.Kick(_mom, _every); R.Kick(_mom, _every);
    }

    private void GlowTarget(string k)
    {
        string act = Activity();
        (Color c, double a) = k switch
        {
            "toast" => (_toast!.Glow, _cfg.Glow * 0.8),
            "hub" => (_tab == "timer" && _editing ? FocusTimer.ColorOf(Phase.Focus)
                      : _tab == "timer" ? FocusTimer.ColorOf(_timer.Phase)
                      : _tab == "now" && _media.HasSession ? _media.ArtColor
                      : Color.FromRgb(170, 185, 255), _cfg.Glow * 0.45),
            "music" => (_media.ArtColor, _cfg.Glow * (_media.IsPlaying ? 1 : 0.35)),
            "timer" => (FocusTimer.ColorOf(_timer.Phase), _cfg.Glow * 0.6),
            "notif" => (_notif!.Tint, _cfg.Glow),
            "drop" => (Color.FromRgb(255, 175, 80), Math.Min(1, _cfg.Glow * 1.2 + 0.2)),
            _ => (_glowTarget, 0.0),
        };
        _glowTarget = c; _glowA = a;
    }

    // ================================================================ hub

    private List<string> VisibleTabs() => _cfg.Tabs.Where(t => t.On).Select(t => t.Id).ToList();

    private string DefaultTab()
    {
        var v = VisibleTabs();
        if (v.Contains(_cfg.DefaultTab)) return _cfg.DefaultTab;   // a specific tab picked in Settings
        if (_cfg.DefaultTab == "playing")
        {
            // open where the action is: the timer while it runs, the player while something plays
            var act = Activity();
            if (act == "timer" && v.Contains("timer")) return "timer";
            if (act == "music" && v.Contains("now")) return "now";
        }
        // the default: back where you were last time; the tab on the left before you've used any
        if (_cfg.DefaultTab == "auto" && _lastTab != null && v.Contains(_lastTab)) return _lastTab;
        return v[0];
    }

    private Button TabButton(string id) => id switch { "system" => TabSystem, "timer" => TabTimer, "notif" => TabNotif, "clip" => TabClip, "shelf" => TabShelf, "dev" => TabDev, "apps" => TabApps, _ => TabNow };
    private FrameworkElement PaneOf(string id) => id switch { "system" => PaneSystem, "timer" => PaneTimer, "notif" => PaneNotif, "clip" => PaneClip, "shelf" => PaneShelf, "dev" => PaneDev, "apps" => PaneApps, _ => PaneNow };
    private IEnumerable<Button> AllTabButtons => [TabNow, TabTimer, TabNotif, TabClip, TabShelf, TabDev, TabApps, TabSystem];

    /// <summary>Order and visibility of tabs and panes, from Settings → Hub & activities.</summary>
    private void LayoutHub()
    {
        LHub.Width = _cfg.HubWidth;
        Viewport.Width = PaneW;
        PaneTimer.Width = PaneNotif.Width = PaneNow.Width = PaneSystem.Width = PaneClip.Width = PaneShelf.Width = PaneDev.Width = PaneApps.Width = PaneW;
        foreach (var b in AllTabButtons) b.Visibility = Visibility.Collapsed;

        // tabs in the order Settings gives them
        TabButtons.Children.Clear();
        Strip.Children.Clear();
        foreach (var t in _cfg.Tabs)
        {
            var b = TabButton(t.Id);
            TabButtons.Children.Add(b);
            b.Visibility = t.On ? Visibility.Visible : Visibility.Collapsed;
            if (t.On) Strip.Children.Add(PaneOf(t.Id));
        }
        LHub.UpdateLayout();
        SetTab(VisibleTabs().Contains(_tab) ? _tab : VisibleTabs()[0], instant: true);
    }

    /// <summary>The tab the hub was last on, so it reopens there.</summary>
    private string? _lastTab;

    private void SetTab(string t, bool instant = false)
    {
        var vis = VisibleTabs();
        if (!vis.Contains(t)) t = vis[0];
        if (_hub) _lastTab = t;   // only tabs you actually opened, not layout refreshes
        if (t != "timer") CloseTimerEditor(animate: false);
        if (t != "clip") CloseViewer(animate: false);
        int from = vis.IndexOf(_tab), to = vis.IndexOf(t);
        _tab = t;
        double target = -to * PaneW;
        ShowTabTitle(t, from < 0 ? 0 : Math.Sign(to - from), instant || !_hub);
        var b = TabButton(t);
        LHub.UpdateLayout();
        TabX.Target = b.TranslatePoint(new Point(0, 0), TrackInner).X;
        TabW.Target = b.ActualWidth;
        if (instant || !_hub || !SystemParameters.ClientAreaAnimation || from < 0 || from == to)
        {
            Pane.Snap(target);
            ShowOnlyPane();
            Strip.BeginAnimation(OpacityProperty, null); Strip.Opacity = 1;
            if (instant) { TabX.Snap(TabX.Target); TabW.Snap(TabW.Target); }
        }
        else SwapPane(target, Math.Sign(to - from));
        foreach (var tb in AllTabButtons) TintTab(tb, (string)tb.Tag == t, instant);
        if (t == "system") { _stats.Sample(); UpdateSystemUI(); }
        if (t == "clip") RenderClip();
        if (t == "notif") RenderNotifs();
        if (t == "shelf") RenderShelf();
        if (t == "dev") RenderDevices();
        if (t == "apps") RenderApps();
        Apply();
    }

    /// <summary>
    /// Tab change: the current pane fades out quickly, the new one fades in already in place, drifting the last 24 px
    /// from the side you're moving toward. Never scrolls past the panes in between.
    /// </summary>
    private void SwapPane(double target, int dir)
    {
        int token = ++_tabSwap;
        var outA = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        outA.Completed += (_, _) =>
        {
            if (token != _tabSwap) return;   // a newer switch took over
            ShowOnlyPane();   // the neighbours must not peek in while it drifts
            Pane.Snap(target + dir * 24);
            Pane.Target = target;
            Pane.Params = _every;
            Strip.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            EnsureFrames();
        };
        Strip.BeginAnimation(OpacityProperty, outA);
    }

    private static string TabName(Button tb) =>
        tb.Content is StackPanel { Children.Count: > 1 } sp && sp.Children[1] is Border { Child: TextBlock label } ? label.Text : "";

    /// <summary>
    /// Tab icons keep a fixed slot, so the one you're about to click never moves. The open tab's name is the title on
    /// the left: it slides in from the side you moved toward.
    /// </summary>
    private void ShowTabTitle(string t, int dir, bool instant)
    {
        var name = TabName(TabButton(t));
        foreach (var tb in AllTabButtons) tb.ToolTip = TabName(tb);
        if (TabTitle.Text == name && !instant) return;
        TabTitle.Text = name;
        if (instant || dir == 0 || !SystemParameters.ClientAreaAnimation)
        {
            TabTitle.BeginAnimation(OpacityProperty, null); TabTitle.Opacity = 1;
            TabTitleX.BeginAnimation(TranslateTransform.XProperty, null); TabTitleX.X = 0;
            return;
        }
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        TabTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        TabTitleX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void ShowOnlyPane()
    {
        var cur = PaneOf(_tab);
        foreach (UIElement p in Strip.Children) p.Opacity = p == cur ? 1 : 0;
    }

    private void TintTab(Button tb, bool selected, bool instant)
    {
        var to = selected ? Color.FromRgb(0xF5, 0xF5, 0xF5) : Color.FromArgb(0xA8, 0xF4, 0xF5, 0xF8);
        var from = tb.Foreground is SolidColorBrush cur ? cur.Color : to;
        if (instant || from == to) { tb.Foreground = new SolidColorBrush(to); return; }
        var brush = new SolidColorBrush(from);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(180)));
        tb.Foreground = brush;
    }

    private void OpenHub(string mode, string? tab = null)
    {
        _hoverIntent.Stop();
        _hoverNudge = false;
        if (!_hub)
        {
            _hub = true;
            _openMode = mode;
            _notif = null; _notifOpen = false; _notifTimer?.Stop();
            SetTab(tab ?? DefaultTab(), instant: true);
            UpdateNowUI();
        }
        else
        {
            if (mode == "sticky") _openMode = "sticky";
            if (tab != null) SetTab(tab);
        }
        Apply();
    }

    private void CloseHub()
    {
        if (!_hub) return;
        _hub = false; _openMode = null;
        CloseTimerEditor(animate: false);
        CloseViewer(animate: false);
        _leaveGrace.Stop();
        Apply();
    }

    // ================================================================ pop-ups

    private void ShowToast(ToastSpec t)
    {
        if (_hub || !_cfg.IsLive(t.Type)) return;
        bool first = _toast?.Type != t.Type;
        _toast = t;
        LToast.Width = t.Width;
        ToastIcon.Kind = t.Icon;
        ToastIcon.Foreground = new SolidColorBrush(t.IconFg);
        ToastIconBox.Background = new SolidColorBrush(t.IconBg);
        ToastText.Text = t.Text;
        ToastValue.Text = t.Value;
        ToastValue.Foreground = new SolidColorBrush(t.ValueColor);
        ToastBar.Visibility = t.Bar ? Visibility.Visible : Visibility.Collapsed;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(t.Ms);
        _toastTimer.Start();
        Apply();
        if (first) { Kick(); if (_cfg.ImpactBounce) H.Velocity += 160; }
    }

    private void OnCharger(bool plugged)
    {
        if (!plugged || _stats.BatteryPercent is not { } pct) return;
        var green = Color.FromRgb(0x30, 0xD1, 0x58);
        ShowToast(new("charge", 236, green, "bolt", Colors.Transparent, green, "Charging", $"{pct}%", green, false, 2300));
    }

    private void OnPhaseEnded(Phase was, Phase now)
    {
        var c = FocusTimer.ColorOf(now);
        ShowToast(new("timerDone", 300, c, "check", c, Colors.White, $"{FocusTimer.Label(was)} done",
            $"{FocusTimer.Label(now)} · {_timer.Minutes(now)} min", c, false, 4000));
    }

    // ================================================================ input

    private void WireInput()
    {
        // the notch and the strip above a floating pill are one hover area: entering either counts, and leaving is
        // checked a beat later so crossing from one to the other (or the pill dropping away under a still cursor) isn't a leave
        Notch.MouseEnter += (_, _) => HoverChanged();
        HoverGap.MouseEnter += (_, _) => HoverChanged();
        Notch.MouseLeave += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, HoverChanged);
        HoverGap.MouseLeave += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, HoverChanged);

        HoverGap.MouseLeftButtonDown += OnDown;
        HoverGap.MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke(null); };
        HoverGap.MouseWheel += OnWheel;
        Notch.MouseLeftButtonDown += OnDown;
        Notch.MouseMove += OnMove;
        Notch.MouseLeftButtonUp += (_, _) => EndDrag(false);
        Notch.LostMouseCapture += (_, _) => { if (_drag != null) EndDrag(true); };
        Notch.MouseRightButtonUp += (_, e) => { e.Handled = true; SettingsRequested?.Invoke(null); };
        Notch.MouseWheel += OnWheel;

        foreach (var b in AllTabButtons) b.Click += (_, _) => SetTab((string)b.Tag);
        GearBtn.Click += (_, _) => SettingsRequested?.Invoke(null);
        PlayBtn.Click += (_, _) => _media.PlayPause();
        NextBtn.Click += (_, _) => _media.Next();
        PrevBtn.Click += (_, _) => _media.Previous();
        TimerPlay.Click += (_, _) => _timer.Toggle();
        TimerAdd.Click += (_, _) => _timer.AddMinutes(5);
        TimerReset.Click += (_, _) => _timer.RestartPhase();
        FreeMemBtn.Click += async (_, _) => await FreeMemory();
        InitTimerEditor();
    }

    private bool OverNotch => Notch.IsMouseOver || HoverGap.IsMouseOver;

    private void HoverChanged()
    {
        bool over = OverNotch;
        if (over == _mouseOver) return;
        _mouseOver = over;
        if (over)
        {
            _leaveGrace.Stop();
            if (!_hub && _drag == null && _notif != null) { _hoverNudge = true; SetNotifOpen(true); return; }
            if (!_hub && _drag == null)
            {
                _hoverNudge = true;   // grows slightly toward you: a hint of what a click or pull will do
                Apply();
                if (_cfg.OpenOnHover) { _hoverIntent.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _cfg.HoverDelayMs)); _hoverIntent.Start(); }
            }
        }
        else
        {
            _hoverIntent.Stop();
            if (_hoverNudge) { _hoverNudge = false; Apply(); }
            SetNotifOpen(false);
            if (_hub && _openMode == "hover" && _drag == null) _leaveGrace.Start();
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Root);
        _drag = new DragState(p, _hub, Key());
        _drag.Samples.Add((p, Environment.TickCount64));
        Press.Target = _hub ? 0.995 : 0.965;   // feedback on press, not on release
        Notch.CaptureMouse();
        EnsureFrames();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var d = _drag;
        if (d == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(Root);
        d.Samples.Add((p, Environment.TickCount64));
        if (d.Samples.Count > 6) d.Samples.RemoveAt(0);
        double dx = p.X - d.Start.X, dy = p.Y - d.Start.Y;

        if (d.Axis == '\0')
        {
            bool compactStart = d.KeyAtStart is "idle" or "music" or "timer" or "toast" or "notif";
            if (_cfg.PullToOpen && Math.Abs(dy) > 8 && Math.Abs(dy) >= Math.Abs(dx) && (d.FromHub || compactStart))
            {
                d.Axis = 'y';
                Press.Target = 1;
                _hoverIntent.Stop(); _leaveGrace.Stop(); _hoverNudge = false;
                if (!d.FromHub) SetTab(DefaultTab(), instant: true);
                var ck = _notif != null ? "notif" : _toast != null ? "toast" : Activity();
                d.Compact = SizeOf(ck);
                d.Expanded = (_cfg.HubWidth, HubHeight(_tab));
                foreach (var el in _layers.Values) { el.Effect = null; el.BeginAnimation(OpacityProperty, null); _layerScale[el].BeginAnimation(ScaleTransform.ScaleXProperty, null); _layerScale[el].BeginAnimation(ScaleTransform.ScaleYProperty, null); }
                foreach (var (k, el) in _layers) el.Opacity = k == d.KeyAtStart ? 1 : 0;
            }
            else if (_cfg.SwipeToSkip && Math.Abs(dx) > 8 && Math.Abs(dx) > Math.Abs(dy) && d.KeyAtStart == "music")
            {
                d.Axis = 'x';
                Press.Target = 1;
            }
            else if (Math.Abs(dx) > 8 || Math.Abs(dy) > 8) { Press.Target = 1; d.Moved = true; }
            if (d.Axis == '\0') return;
        }

        if (d.Axis == 'y')
        {
            var (cw, ch) = d.Compact;
            var (ew, eh) = d.Expanded;
            double range = eh - ch;
            double raw = (d.FromHub ? range : 0) + dy;   // tracks 1:1 from where it was grabbed
            double h = raw < 0 ? ch + Gesture.RubberBand(raw, 80)
                     : raw > range ? eh + Gesture.RubberBand(raw - range, 240)
                     : ch + raw;
            double pr = Math.Clamp((h - ch) / range, 0, 1);
            H.Snap(h);
            W.Snap(cw + (ew - cw) * pr + Math.Max(0, h - eh) * 0.25);
            R.Snap(ch / 2 + (Math.Min(_cfg.CornerRadius, eh / 2) - ch / 2) * pr);
            // the hub hints at what's coming as you pull
            LHub.Opacity = Math.Clamp(pr * 1.6 - 0.25, 0, 1);
            var hs = _layerScale[LHub]; hs.ScaleX = hs.ScaleY = 0.94 + 0.06 * pr;
            if (_layers.TryGetValue(d.KeyAtStart, out var cur) && cur != LHub) cur.Opacity = Math.Clamp(1 - pr * 2.5, 0, 1);
        }
        else
        {
            Swipe.Snap(Gesture.RubberBand(dx, 60));
        }
        EnsureFrames();
    }

    private (double X, double Y) Velocity(DragState d)
    {
        var a = d.Samples[0]; var b = d.Samples[^1];
        double dt = (b.T - a.T) / 1000.0;
        if (dt < 0.005 || Environment.TickCount64 - b.T > 80) return (0, 0);
        return ((b.P.X - a.P.X) / dt, (b.P.Y - a.P.Y) / dt);
    }

    private void EndDrag(bool cancelled)
    {
        var d = _drag;
        if (d == null) return;
        _drag = null;
        if (Notch.IsMouseCaptured) Notch.ReleaseMouseCapture();
        Press.Target = 1;
        EnsureFrames();

        if (d.Axis == '\0')
        {
            if (cancelled || d.Moved) return;
            if (d.KeyAtStart == "notif") { OpenNotif(); return; }   // a click on a notification opens its app
            if (d.KeyAtStart == "hub") _openMode = "sticky";   // a click pins a hover-opened hub
            else OpenHub("sticky");
            return;
        }

        var (vx, vy) = Velocity(d);
        if (d.Axis == 'y')
        {
            double range = d.Expanded.H - d.Compact.H, cur = H.Value - d.Compact.H;
            double projected = cur + Gesture.Project(vy);        // where the flick is heading, not where it let go
            bool open = cancelled ? d.FromHub : projected > range / 2;
            if (open) { _hub = true; _openMode = "sticky"; _notif = null; _notifOpen = false; _notifTimer?.Stop(); UpdateNowUI(); }
            else { _hub = false; _openMode = null; }
            _key = "";                                           // re-run the content hand-off from the current opacities
            Apply();
            H.Velocity = vy;                                     // velocity hand-off: no seam between drag and spring
            W.Velocity = vy * (d.Expanded.W - d.Compact.W) / Math.Max(1, range);
            if (Math.Abs(vy) > 300) Kick();
        }
        else
        {
            double projected = Swipe.Value + Gesture.Project(vx, 0.99);
            if (projected < -45) { _media.Next(); Swipe.Snap(46); }          // next comes in from the right
            else if (projected > 45) { _media.Previous(); Swipe.Snap(-46); } // previous from the left
            Swipe.Target = 0;
            Swipe.Velocity = vx * 0.3;
        }
        EnsureFrames();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_hub || !_cfg.ScrollForVolume || !_vol.Available) return;
        e.Handled = true;
        double level = Math.Round((_vol.Level + Math.Sign(e.Delta) * 0.04) * 50) / 50;
        _vol.Muted = false;
        _vol.Level = level;
        VolFill.Target = Math.Clamp(level, 0, 1);
        int pct = (int)Math.Round(level * 100);
        ShowToast(new("volume", 250, Color.FromRgb(220, 225, 255), pct == 0 ? "mute" : "speaker", Colors.Transparent, Colors.White,
            "", pct.ToString(), Color.FromRgb(0xF4, 0xF5, 0xF8), true, 1300));
    }

    /// <summary>20 times a second: cursor lean, click-outside to close, follow-cursor display.</summary>
    private int _pollN;
    private void Poll()
    {
        if (_hidden || _hwnd == IntPtr.Zero) return;
        Native.GetCursorPos(out var cp);
        var p = Root.PointFromScreen(new Point(cp.X, cp.Y));
        var k = Key();

        double lean = 0;
        if (_cfg.LeanTowardCursor && k != "hub" && _drag == null)
        {
            double dx = p.X - NX(W.Value), dy = p.Y;
            if (dy > -20 && dy < 260 && Math.Abs(dx) < 380)
            {
                double fall = (1 - Math.Max(0, dy) / 260) * (1 - Math.Abs(dx) / 380);
                lean = Math.Clamp(dx / 180, -1, 1) * 12 * fall;
            }
        }
        if (Math.Abs(lean - LeanX.Target) > 0.05) { LeanX.Target = lean; EnsureFrames(); }

        Peek(p, k);

        // a click anywhere else closes a pinned hub
        if (_hub && _openMode == "sticky" && (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0 && !OverNotch && _drag == null)
            CloseHub();

        if (_cfg.Display == "cursor" && ++_pollN % 10 == 0 && !_hub && _drag == null)
        {
            var under = Displays.Under(new System.Drawing.Point(cp.X, cp.Y));
            if (under.Id != _display?.Id) MoveTo(under);
        }
    }

    // ================================================================ the line

    /// <summary>
    /// Something just started (a song, the timer): in Line style it shows its notch for a few seconds, so you see what
    /// started, then collapses into the line.
    /// </summary>
    private void Announce(string k)
    {
        if (!LineStyle || _hub) return;
        _announce = k;
        _announceTimer.Stop();
        _announceTimer.Start();
        LineDraw.Snap(1);
        Apply();
    }

    /// <summary>
    /// The notch collapses up into the edge while the line draws itself out from the centre (with a little overshoot)
    /// and glows in its colour for a moment, as if the notch had been poured into it.
    /// </summary>
    private void EndAnnounce()
    {
        _announceTimer.Stop();
        if (_announce == null) return;
        _announce = null;
        if (SystemParameters.ClientAreaAnimation && LineStyle)
        {
            LineDraw.Snap(0);
            LineDraw.Target = 1;
            var c = Key() == "timer" ? FocusTimer.ColorOf(_timer.Phase) : _media.ArtColor;
            var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 14, ShadowDepth = 0, Opacity = 0 };
            LineBar.Effect = glow;
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1600) };
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)), new CubicEase { EasingMode = EasingMode.EaseIn }));
            a.Completed += (_, _) => { if (LineBar.Effect == glow) LineBar.Effect = null; };
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, a);
        }
        Apply();
        EnsureFrames();
    }

    /// <summary>
    /// What the line shows: grey when idle; the timer's time left in its phase colour (dimmed while paused); the song's
    /// progress in the album colour (dimmed while paused, full for a live stream).
    /// </summary>
    private void UpdateLine()
    {
        if (!LineStyle) return;
        string k = Key();
        double lw = LineBar.Width is double d && !double.IsNaN(d) ? d : 0;
        if (k == "timer")
        {
            var c = FocusTimer.ColorOf(_timer.Phase);
            SetLine(Color.FromArgb(0x55, c.R, c.G, c.B), c, _timer.Total > 0 ? _timer.Left / _timer.Total : 0, _timer.Running ? 1 : 0.55, lw);
        }
        else if (k == "music")
        {
            var c = _media.ArtColor;
            double len = _media.Duration.TotalSeconds;
            SetLine(Color.FromArgb(0x55, c.R, c.G, c.B), c, len > 0 ? Math.Clamp(_media.Position.TotalSeconds / len, 0, 1) : 1, _media.IsPlaying ? 1 : 0.55, lw);
        }
        else SetLine(Color.FromRgb(0x6A, 0x6A, 0x74), Colors.Transparent, 0, 1, lw);
    }

    private void SetLine(Color track, Color fill, double fraction, double strength, double width)
    {
        if (LineTrack.Background is not SolidColorBrush tb || tb.IsFrozen) LineTrack.Background = tb = new SolidColorBrush(track);
        if (tb.Color != track) tb.Color = track;
        if (LineFill.Background is not SolidColorBrush fb || fb.IsFrozen) LineFill.Background = fb = new SolidColorBrush(fill);
        if (fb.Color != fill) fb.Color = fill;
        LineFill.Width = Math.Round(width * Math.Clamp(fraction, 0, 1), 1);
        LineBar.Children[0].Opacity = LineFill.Opacity = strength;
    }

    // ================================================================ stepping aside

    private bool _peeking, _lmbWasDown;
    private DateTime _peekUntil;

    /// <summary>
    /// A compact pill (music, timer, or the idle pill) covers whatever is under it. Clicking just below or beside it
    /// means you're working there, so it fades to a ghost and lets every click through for a few seconds. It comes
    /// back once that time is up and the cursor has moved away.
    /// </summary>
    private void Peek(Point p, string k)
    {
        bool lmb = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0;
        bool compact = !LineStyle && k is "music" or "timer" or "idle";   // a line covers nothing worth stepping away from
        double left = Canvas.GetLeft(Notch), top = Canvas.GetTop(Notch), w = Notch.Width, h = Notch.Height;
        bool onPill = p.X >= left && p.X <= left + w && p.Y >= 0 && p.Y <= top + h;   // the strip above a floating pill is the pill
        bool near = p.X >= left - 60 && p.X <= left + w + 60 && p.Y >= 0 && p.Y <= top + h + 90;

        if (lmb && !_lmbWasDown && compact && !_peeking && near && !onPill && _drag == null)
        {
            _peeking = true;
            ClickThrough(true);
            FadeNotch(0.15, 160);
        }
        if (_peeking && lmb && near) _peekUntil = DateTime.UtcNow.AddSeconds(3);   // still working there
        _lmbWasDown = lmb;

        if (_peeking && (!compact || (DateTime.UtcNow > _peekUntil && !near)))
        {
            _peeking = false;
            ClickThrough(false);
            FadeNotch(1, 260);
        }
    }

    private void FadeNotch(double to, int ms)
    {
        if (!SystemParameters.ClientAreaAnimation) { Notch.BeginAnimation(OpacityProperty, null); Notch.Opacity = to; return; }
        Notch.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        long ex = (long)Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
    }

    // ================================================================ frame loop

    private void EnsureFrames()
    {
        if (_rendering) return;
        _rendering = true;
        _lastRender = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (t == _lastRender) return;
        double dt = _lastRender == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (t - _lastRender).TotalSeconds);
        _lastRender = t;
        _clock += dt;
        _frameDt = dt;

        foreach (var s in new[] { W, H, R, LeanX, Press, Off, TabX, TabW, Pane, VolFill, Swipe, LineDraw }) s.Step(dt);
        Render();

        if (!NeedsFrames())
        {
            CompositionTarget.Rendering -= OnFrame;
            _rendering = false;
        }
    }

    private bool NeedsFrames()
    {
        bool springs = !(W.IsSettled && H.IsSettled && R.IsSettled && LeanX.IsSettled && Press.IsSettled && Off.IsSettled
                       && TabX.IsSettled && TabW.IsSettled && Pane.IsSettled && VolFill.IsSettled && Swipe.IsSettled && LineDraw.IsSettled);
        bool glow = Math.Abs(_glowCurA - _glowA) > 0.004 || Dist(_glowCur, _glowTarget) > 3;
        bool live = (_key == "music" && _media.IsPlaying)
                 || (_key == "hub" && ((_tab == "now" && _media.IsPlaying) || (_tab == "timer" && _timer.Running)))
                 || (_key == "timer" && _timer.Running && _timer.Left < 10);
        return springs || glow || live || _drag != null;
    }

    private static double Dist(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private void Render()
    {
        double w = Math.Max(20, W.Value + Math.Abs(LeanX.Value) * 0.9), h = Math.Max(4, H.Value);
        double r = Math.Clamp(R.Value, 0, Math.Min(h / 2, w / 2));
        bool attached = _cfg.Style == "attached";

        // "ink": how much of a black shape there is. The line strip (12 px or less) is fully see-through, only a
        // 1/255 alpha so it still catches the cursor; growing past it, the black and its contents fill in and the
        // line fades out.
        // the dissolve runs over the last 22 px of height (pill → strip), so closing into the line is gradual, not a pop
        double ink = Math.Clamp((h - 12) / 22, 0, 1);
        var fill = Color.FromArgb((byte)Math.Max(1, ink * 255), 0, 0, 0);
        if (Notch.Background is SolidColorBrush nb && !nb.IsFrozen) { if (nb.Color != fill) nb.Color = fill; }
        else Notch.Background = new SolidColorBrush(fill);
        Inner.Opacity = ink;
        LineBar.Opacity = LineStyle ? 1 - ink : 0;

        Notch.Width = w; Notch.Height = h;
        Notch.CornerRadius = attached ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
        Inner.Width = w; Inner.Height = h;
        Inner.Clip = Rounded(w, h, attached ? 0 : r, r);
        Canvas.SetLeft(Notch, NX(w) - w / 2 + LeanX.Value + Off.Value);
        // the line strip always touches the top edge, so flinging the cursor up finds it; a floating pill drops away from it
        double top = attached ? 0 : 10 * ink;
        Canvas.SetTop(Notch, top);
        // ...and the strip above a floating pill still counts as the notch, so the top edge keeps it open
        Canvas.SetLeft(HoverGap, Canvas.GetLeft(Notch));
        Canvas.SetTop(HoverGap, 0);
        HoverGap.Width = w;
        HoverGap.Height = top;
        if (LineBar.Opacity > 0)
        {
            double lw = Math.Max(0, w - LinePad);
            LineBar.Width = lw;
            if (LineBar.RenderTransform is not ScaleTransform ls) LineBar.RenderTransform = ls = new ScaleTransform();
            LineBar.RenderTransformOrigin = new Point(0.5, 0);
            ls.ScaleX = Math.Max(0, LineDraw.Value);
            Canvas.SetLeft(LineBar, Canvas.GetLeft(Notch) + LinePad / 2);
            Canvas.SetTop(LineBar, 0);
            UpdateLine();
        }
        PressScale.ScaleX = PressScale.ScaleY = Press.Value;
        foreach (var el in _layers.Values)
        {
            Canvas.SetLeft(el, (w - el.Width) / 2);
            if (el.Opacity <= 0.001) continue;
            // content lives inside the shape: while the shape is narrower than the content, the content is scaled to it
            double f = Math.Clamp(w / el.Width, 0.5, 1);
            var fit = _layerFit[el];
            if (Math.Abs(fit.ScaleX - f) > 0.0005) fit.ScaleX = fit.ScaleY = f;
        }

        if (_key == "hub" && LHub.Opacity > 0)
        {
            // the pill chases the selected tab while the row reflows around its widening label
            var sel = TabButton(_tab);
            double tx = sel.TranslatePoint(new Point(0, 0), TrackInner).X, tw = sel.ActualWidth;
            if (Math.Abs(tx - TabX.Target) > 0.25) TabX.Target = tx;
            if (Math.Abs(tw - TabW.Target) > 0.25) TabW.Target = tw;
        }
        TabIndicatorX.X = TabX.Value;
        TabIndicator.Width = Math.Max(0, TabW.Value);
        StripX.X = Pane.Value;
        _musicSwipe.X = Swipe.Value;
        ToastBarFill.Width = 86 * Math.Clamp(VolFill.Value, 0, 1);

        // glow: eases toward the current colour; music pulses with a beat, the timer's last seconds breathe
        // by time, not by frame, so it eases at the same pace on a 60 Hz and a 144 Hz screen (about 0.2 s)
        double gk = 1 - Math.Exp(-_frameDt / 0.2);
        _glowCur = Color.FromRgb(Lerp(_glowCur.R, _glowTarget.R, gk), Lerp(_glowCur.G, _glowTarget.G, gk), Lerp(_glowCur.B, _glowTarget.B, gk));
        _glowCurA += (_glowA - _glowCurA) * gk;
        double pulse = 1;
        if (_cfg.BeatPulse && _media.IsPlaying && (_key == "music" || (_key == "hub" && _tab == "now" && Activity() == "music")))
            pulse = 0.7 + 0.6 * Math.Pow(Math.Max(0, Math.Sin(_clock * Math.PI * 4)), 6);
        if (_timer.Running && _timer.Left < 10 && (_key == "timer" || _key == "hub"))
            pulse = 0.8 + 0.9 * Math.Pow(Math.Max(0, Math.Sin(_clock * Math.PI * 2)), 3);
        GlowFx.Color = _glowCur;
        GlowFx.Opacity = Math.Min(1, _glowCurA * pulse);
        GlowFx.BlurRadius = 26 + h * 0.22;
        GlowFx.ShadowDepth = attached ? 3 : 6;

        // beat bars
        if (_key == "music" || (_key == "hub" && _tab == "now"))
        {
            var bars = _key == "music" ? _barsS : _barsL;
            for (int i = 0; i < bars.Length; i++)
                bars[i].Height = _media.IsPlaying
                    ? 4 + 13 * Math.Abs(Math.Sin(_clock * (5.1 + i * 1.7) + i * 1.3) * Math.Sin(_clock * (2.3 + i * 0.9) + (_key == "music" ? 0 : 2)))
                    : 4;
        }

        if (_key == "hub" && _tab == "timer")
        {
            Dial.Fraction = _timer.Total > 0 ? _timer.Left / _timer.Total : 0;
            Dial.Urgency = _timer.Running && _timer.Left < 10 ? 0.5 + 0.5 * Math.Sin(_clock * Math.PI * 4) : 0;
            Dial.InvalidateVisual();
        }
    }

    private double _frameDt = 1 / 60.0;
    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);

    // ================================================================ press feedback

    private readonly HashSet<Button> _pressWired = [];

    /// <summary>
    /// Every button dips the instant the mouse goes down and comes back when it's let go, or when the cursor drags off
    /// it (which cancels the click). Wide buttons dip less than small ones, so the movement looks the same size, and
    /// they come back without an overshoot: a click has no momentum to carry.
    /// </summary>
    private void PressFeedback()
    {
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d != null && d != this; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is not Button b) continue;
                if (_pressWired.Add(b))
                    System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, typeof(Button))
                        .AddValueChanged(b, (_, _) => Dip(b));
                break;
            }
        }), true);
    }

    private static void Dip(Button b)
    {
        if (b.Template?.FindName("S", b) is not ScaleTransform s || s.IsFrozen) return;
        if (!SystemParameters.ClientAreaAnimation) { s.BeginAnimation(ScaleTransform.ScaleXProperty, null); s.BeginAnimation(ScaleTransform.ScaleYProperty, null); return; }
        double to = !b.IsPressed ? 1 : b.ActualWidth > 64 ? 0.965 : 0.93;
        var a = b.IsPressed
            ? new DoubleAnimation(to, TimeSpan.FromMilliseconds(90)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }
            : new DoubleAnimation(to, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }


    private static Geometry Rounded(double w, double h, double rt, double rb)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(rt, 0), true, true);
            c.LineTo(new Point(w - rt, 0), false, false);
            if (rt > 0) c.ArcTo(new Point(w, rt), new Size(rt, rt), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(w, h - rb), false, false);
            if (rb > 0) c.ArcTo(new Point(w - rb, h), new Size(rb, rb), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(rb, h), false, false);
            if (rb > 0) c.ArcTo(new Point(0, h - rb), new Size(rb, rb), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(0, rt), false, false);
            if (rt > 0) c.ArcTo(new Point(rt, 0), new Size(rt, rt), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    // ================================================================ content

    private static Rectangle[] MakeBars(Panel host, int n)
    {
        var list = new Rectangle[n];
        for (int i = 0; i < n; i++)
        {
            list[i] = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(1.25, 0, 1.25, 0), VerticalAlignment = VerticalAlignment.Center, Fill = Brushes.White };
            host.Children.Add(list[i]);
        }
        return list;
    }

    private void OnMediaChanged()
    {
        if (_wasPlaying && !_media.IsPlaying) _pausedAt = DateTime.UtcNow;
        // a song starting (or a new one coming on) shows its notch for a moment in Line style
        bool started = _media.IsPlaying && (!_wasPlaying || _media.Title != _lastSong);
        _wasPlaying = _media.IsPlaying;
        if (_media.IsPlaying) _lastSong = _media.Title;
        if (started && _cfg.IsLive("music")) Dispatcher.BeginInvoke(() => Announce("music"));

        Brush art = _media.Art != null
            ? new ImageBrush(_media.Art) { Stretch = Stretch.UniformToFill }
            : new LinearGradientBrush(Color.FromRgb(0x2A, 0x2D, 0x3A), Color.FromRgb(0x15, 0x16, 0x1C), 45);
        ArtSmall.Background = art;
        ArtLarge.Background = art;
        var barBrush = new SolidColorBrush(_media.ArtColor);
        foreach (var b in _barsS.Concat(_barsL)) b.Fill = barBrush;
        SongTitle.Text = _media.Title;
        SongArtist.Text = _media.Artist;
        PlayIcon.Kind = _media.IsPlaying ? "pause" : "play";
        MusicCard.Visibility = _media.HasSession ? Visibility.Visible : Visibility.Collapsed;
        NoMusic.Visibility = _media.HasSession ? Visibility.Collapsed : Visibility.Visible;
        PaneNow.Height = PaneHeight("now");
        UpdateNowUI();
        Apply();
    }

    private void UpdateNowUI()
    {
        var pos = _media.Position;
        var len = _media.Duration;
        if (LineBar.Opacity > 0) UpdateLine();
        SongPos.Text = Fmt(pos);
        // time left, like iOS; a live stream has no length
        SongLen.Text = len.TotalSeconds < 1 ? "Live" : "-" + Fmt(len - pos > TimeSpan.Zero ? len - pos : TimeSpan.Zero);
        if (SongBar.Parent is FrameworkElement track && len.TotalSeconds > 0)
            SongBar.Width = Math.Clamp(pos.TotalSeconds / len.TotalSeconds, 0, 1) * track.ActualWidth;
    }

    private void UpdateTimerUI()
    {
        // starting the timer shows its notch for a moment in Line style
        if (_timer.Running && !_timerWasRunning && _cfg.IsLive("timer")) Dispatcher.BeginInvoke(() => Announce("timer"));
        _timerWasRunning = _timer.Running;
        var c = FocusTimer.ColorOf(_timer.Phase);
        var brush = new SolidColorBrush(c);
        Pie.Color = c; Dial.Color = c;
        Pie.Fraction = Dial.Fraction = _timer.Total > 0 ? _timer.Left / _timer.Total : 0;
        Dial.Paused = !_timer.Running;
        TimerSmall.Foreground = brush;
        foreach (var el in LTimer.Children.OfType<FrameworkElement>()) el.Opacity = _timer.Running ? 1 : 0.5;
        PhaseDot.Fill = brush;
        PhaseDot.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
        PhaseText.Foreground = brush;
        PhaseText.Text = FocusTimer.Label(_timer.Phase);
        PhaseCount.Text = _timer.Phase switch
        {
            Phase.Focus => $"  ·  {_timer.Session} of {_timer.Sessions}",
            Phase.Break => $"  ·  then {Math.Min(_timer.Session + 1, _timer.Sessions)} of {_timer.Sessions}",
            _ => "  ·  round done",
        };
        // like the Clock app: white digits, dimmed while paused; colour only where it means something
        DialTime.Foreground = Brushes.WhiteSmoke;
        DialTime.Opacity = _timer.Running ? 1 : 0.5;
        // tinted like an "on" Devices tile; the ring around it carries the solid colour
        TimerPlay.Background = new SolidColorBrush(FocusTimer.Tint(c));
        TimerPlayIcon.Foreground = new SolidColorBrush(FocusTimer.Light(c));
        TimerPlayIcon.Kind = _timer.Running ? "pause" : "play";
        TimerPlay.ToolTip = _timer.Running ? "Pause" : _timer.Active ? "Resume" : "Start";
        UpdateTimerText();
        Apply();
    }

    private void UpdateTimerText()
    {
        var s = FmtTimer(_timer.Left);
        if (LineBar.Opacity > 0) UpdateLine();
        TimerSmall.Text = s;
        DialTime.Text = s;
        Pie.Fraction = _timer.Total > 0 ? _timer.Left / _timer.Total : 0;
        Pie.InvalidateVisual();
        EndsAt.Inlines.Clear();
        if (_timer.Running)
        {
            EndsAt.Inlines.Add(new System.Windows.Documents.Run("Ends at "));
            EndsAt.Inlines.Add(new System.Windows.Documents.Run(_timer.EndsAt.ToString("HH:mm")) { Foreground = Brushes.WhiteSmoke, FontWeight = FontWeights.SemiBold });
        }
        else if (_timer.Active) EndsAt.Inlines.Add(new System.Windows.Documents.Run("Paused"));
        else EndsAt.Inlines.Add(new System.Windows.Documents.Run($"{_timer.Minutes(_timer.Phase)} min {FocusTimer.Label(_timer.Phase).ToLowerInvariant()}"));
    }

    private void UpdateSystemUI()
    {
        CpuValue.Text = $"{_stats.Cpu:0}%";
        CpuCores.Text = $"{Environment.ProcessorCount} threads";
        double cw = CpuSpark.ActualWidth > 0 ? CpuSpark.ActualWidth : 140, chh = 26;
        var pts = new PointCollection();
        for (int i = 0; i < _stats.CpuHistory.Length; i++)
            pts.Add(new Point(i * cw / (_stats.CpuHistory.Length - 1), chh - 2 - _stats.CpuHistory[i] / 100 * (chh - 6)));
        CpuLine.Points = pts;

        MemValue.Inlines.Clear();
        MemValue.Inlines.Add(new System.Windows.Documents.Run($"{_stats.MemUsedGb:0.0} "));
        MemValue.Inlines.Add(new System.Windows.Documents.Run($"/ {_stats.MemTotalGb:0} GB") { FontSize = 12, Foreground = (Brush)FindResource("Dim") });
        double frac = _stats.MemTotalGb > 0 ? _stats.MemUsedGb / _stats.MemTotalGb : 0;
        MemPct.Text = $"{frac * 100:0}%";
        if (MemBar.Parent is FrameworkElement mt) MemBar.Width = frac * mt.ActualWidth;

        // a PC without a battery gets no Battery card: CPU and Memory share the row
        bool hasBattery = _stats.BatteryPercent is not null;
        BattCard.Visibility = hasBattery ? Visibility.Visible : Visibility.Collapsed;
        BattGapCol.Width = new GridLength(hasBattery ? 8 : 0);
        BattCol.Width = hasBattery ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (_stats.BatteryPercent is { } b)
        {
            BattValue.Text = $"{b}%";
            BattState.Text = _stats.Charging ? "Charging" : "On battery";
            BattBolt.Visibility = _stats.Charging ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task FreeMemory()
    {
        FreeMemBtn.IsEnabled = false;
        FreeMemBtn.Content = "Freeing…";
        double gb = await SystemStats.FreeMemoryAsync();
        _stats.Sample();
        UpdateSystemUI();
        FreeMemBtn.Content = gb >= 0.05 ? $"Freed {gb:0.0} GB" : "Already lean";
        SysFoot.Text = "Idle apps gave back memory they weren't using.";
        var green = Color.FromRgb(0x30, 0xD1, 0x58);
        ShowToast(new("memory", 300, green, "spark", green, Colors.White, $"Freed {gb:0.0} GB from idle apps", "", Colors.White, false, 2300));
        await Task.Delay(2500);
        FreeMemBtn.Content = "Free up memory";
        FreeMemBtn.IsEnabled = true;
    }

    /// <summary>Four times a second: timer, song position, stats, and whether the compact view should change.</summary>
    private int _tickN;
    private void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _timer.Tick(dt);
        if (_timer.Active) UpdateTimerText();
        if (_key == "timer" && _timer.Running) EnsureFrames();
        if (_hub && _tab == "now") UpdateNowUI();
        if (_hub && _tab == "dev" && _tickN % 4 == 0 && QuietMode.On != _dndShown) RenderDevices();   // Do not disturb switched elsewhere
        if (++_tickN % 4 == 0)
        {
            if (_hub && _tab == "system") { _stats.Sample(); UpdateSystemUI(); }
            Apply();   // music paused for 30 s drops back to the idle pill
        }
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    private static string FmtTimer(double s)
    {
        int v = (int)Math.Ceiling(Math.Max(0, s));
        return $"{v / 60:00}:{v % 60:00}";
    }

    // ================================================================ settings

    /// <summary>Called by the Settings window after any change.</summary>
    public void ApplySettings()
    {
        (_every, _mom) = Feels.Get(_cfg.Feel);
        _grow = GrowOf(_every);
        foreach (var s in new[] { W, H, R, LeanX, Off, TabX, TabW, Pane, VolFill }) { s.Params = _every; s.RestoreTo = null; }
        _clip.SkipSecrets = _cfg.ClipSkipSecrets;
        if (_clip.Max != _cfg.ClipMax) _clip.ApplyMax(_cfg.ClipMax);
        if (_shelf.Keep != _cfg.ShelfKeep) { _shelf.Keep = _cfg.ShelfKeep; _shelf.Prune(); }
        Swipe.Params = _mom;
        LayoutHub();
        if (_cfg.Display != _displaySetting)
        {
            _displaySetting = _cfg.Display;
            if (_cfg.Display != "cursor") MoveTo(Displays.Resolve(_cfg.Display, CursorPx()));
        }
        else Align();
        _timer.SettingsChanged();
        foreach (var w in new[] { FocusWheel, BreakWheel, LongWheel }) w.Params = _every;
        RefreshEditor();
        if (_hub && _tab == "apps") RenderApps();
        if (!_cfg.HideInFullscreen && _hidden) { _hidden = false; Visibility = Visibility.Visible; }
        _key = "";
        Apply();
    }

    /// <summary>Launch options for checking states without touching the mouse: --hub[=system], --timer.</summary>
    public void ApplyLaunchArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (a == "--timer") _timer.Start();
            if (a == "--demo") RunDemo();
            if (a == "--notif")
            {
                // an example notification a second after start (it doesn't go through Windows)
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    var c = Color.FromRgb(0x5B, 0x5F, 0xE8);
                    OnNotification(new NotifItem(2, "Teams", "MSTeams_8wekyb3d8bbwe!MSTeams", "Dana Levi",
                        "Can you look over the design review before the 3pm sync? I left comments on the notch line and the timer page.\nAlso: the build is green.",
                        DateTimeOffset.Now, null, c), example: true);
                };
                t.Start();
            }
            if (a == "--edit-timer") Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, OpenTimerEditor);
            if (a == "--sample")
            {
                _clip.AddSamples();
                _clip.AddSampleImage(SamplePhoto(1600, 900, Color.FromRgb(0xFF, 0x8A, 0x4C), Color.FromRgb(0x5B, 0x3F, 0xD9)), "Snipping Tool", 4);
                _clip.AddSampleImage(SamplePhoto(800, 1100, Color.FromRgb(0x2E, 0xC4, 0xB6), Color.FromRgb(0x0A, 0x3D, 0x62)), "Photos", 20);
                var root = AppContext.BaseDirectory;
                var src = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "..", "..", "..", ".."));
                _shelf.Add(System.IO.Directory.GetFiles(src).Take(3).Append(System.IO.Path.Combine(src, "NotchApp")).Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)));
            }
            // --view: the Clipboard tab with its newest image open big (use with --sample); --view=tour then steps to
            // the next image and goes back to the list, for checking the motion
            if (a.StartsWith("--view")) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                OpenHub("sticky", "clip");
                var steps = new List<(int Ms, Action Do)> { (900, () => { if (ClipImages().FirstOrDefault() is { } c) OpenViewer(c); }) };
                if (a == "--view=tour") { steps.Add((2600, () => StepViewer(1))); steps.Add((4600, () => CloseViewer(animate: true))); }
                foreach (var (ms, run) in steps)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    t.Tick += (_, _) => { t.Stop(); run(); };
                    t.Start();
                }
            });
            if (a.StartsWith("--hub")) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => OpenHub("sticky", a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : null));
        }
    }

    /// <summary>A stand-in photo for --sample: a sky-like gradient with a sun and two hills.</summary>
    private static System.Windows.Media.Imaging.BitmapSource SamplePhoto(int w, int h, Color top, Color bottom)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(top, bottom, 90), null, new Rect(0, 0, w, h));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xE0, 255, 240, 200)), null, new Point(w * 0.68, h * 0.38), h * 0.09, h * 0.09);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x12, 0x20)), null, new Point(w * 0.25, h * 1.05), w * 0.55, h * 0.35);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xE6, 0x08, 0x09, 0x10)), null, new Point(w * 0.85, h * 1.1), w * 0.5, h * 0.33);
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        return bmp;
    }

    /// <summary>--demo: plays the main moments on a fixed schedule, for reviewing motion frame by frame.</summary>
    private void RunDemo()
    {
        var steps = new (double At, Action Do)[]
        {
            (1.5, () => OpenHub("sticky", "now")),
            (3.0, () => SetTab("clip")),
            (4.0, () => SetTab("dev")),
            (5.0, () => SetTab("timer")),
            (6.0, CloseHub),
            (7.5, () => OnNotification(new NotifItem(1, "Messages", "", "Maya", "Are we still on for 7?", DateTimeOffset.Now, null, Color.FromRgb(0x30, 0xD1, 0x58)))),
            (11.5, () => ShowToast(new("volume", 260, Colors.White, "speaker", Color.FromArgb(0x30, 255, 255, 255), Colors.White, "Volume", "48", Colors.White, true, 1800))),
        };
        foreach (var (at, act) in steps)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(at) };
            t.Tick += (_, _) => { t.Stop(); act(); };
            t.Start();
        }
    }

    public void ShowOnDisplay(string id)
    {
        _cfg.Display = id;
        _cfg.Save();
        ApplySettings();
    }
}
