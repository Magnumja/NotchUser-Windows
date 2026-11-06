using System.IO.Compression;
using Directory = System.IO.Directory;
using File = System.IO.File;
using SearchOption = System.IO.SearchOption;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using static NotchUser.MacColors;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>
/// The notch: a black pill hanging from the top of the screen, built to look and move like the
/// Mac version (same sizes, springs and styles). Hover it for music controls and the shelf;
/// drop files, links or text on it to keep them.
/// </summary>
public partial class MainWindow : Window
{
    // The Mac's pill on displays without a notch, and its expanded layout.
    private const double NotchW = 130, NotchH = 28, Side = NotchH + 12;
    private const double PanelW = 470, Chrome = 14, Separator = 13;
    private const double MediaH = 124, ShelfH = 78, EmptyH = 84, ToastH = 52;
    private const double ItemW = 64, ItemGap = 4;
    private string Dock => Settings.Current.Dock is "bottom" or "left" or "right" ? Settings.Current.Dock : "top";
    private bool Vertical => Dock is "left" or "right";
    /// <summary>The flared notch outline only makes sense hanging from the top edge.</summary>
    private bool IslandShape => Settings.Current.Island || Dock != "top";
    // Island: floats this far below the screen's edge, and its content starts this much higher (no bezel to clear).
    private const double IslandGap = 8, IslandInset = 18;
    // The window while the notch is closed: room for the widest closed pill (a lyric line, 380) as it bumps (x1.07).
    private const double CompactW = 420, CompactH = 52;

    private readonly MediaService _media = new();
    private readonly Volume _volume = new();
    private readonly Brightness _brightness = new();
    private readonly Shelf _shelf = Shelf.Load();
    private readonly HashSet<ShelfEntry> _selected = new();

    private readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leaveDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _progressTick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private readonly DispatcherTimer _lyricsTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _fullScreenCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _batteryCheck = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _dragPoll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _bumpBack = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer _compactDelay = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _privacyCheck = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _clockTick = new() { Interval = TimeSpan.FromSeconds(10) };

    // What the notch is doing.
    private bool _expanded;
    private bool _inside;            // the mouse is on the notch
    private bool _hoverOpen;         // ...long enough to open it
    private bool _peeking;           // open for a moment after a song change
    private bool _lift;              // the closed notch rises under the mouse (Elevated and Glass)
    private bool _dragging;          // something is being carried toward the notch
    private bool _dropTargeted;      // ...and it is over the notch
    private bool _draggingOut;       // ...or out of our own shelf (ignored as a drop)
    private bool _menuOpen;
    private ContextMenu? _openMenu;
    private bool _hud;
    private bool _seeking, _settingVolume;
    private bool _hiddenForFullScreen;
    private bool _locked;            // the session is locked (Win+L): nothing to animate for
    private bool _compact;           // the window is cut down to the closed notch
    private DateTime _trimmedAt;
    private string _hudKind = "volume";  // what the closed pill's level shows: "volume", "brightness" or "text"
    private bool? _capsLock, _numLock;
    private Toast? _toast;
    private Power.State? _power;
    private ShelfEntry? _pressed;
    private Point _pressPoint;

    /// <summary>A short message (e.g. "Text copied") shown on its own in the open notch.</summary>
    private sealed record Toast(string Glyph, string Title, string Detail, bool Ok);

    // Synced lyrics of the current song (empty when off, not found or still loading).
    private IReadOnlyList<LyricLine> _lyrics = Array.Empty<LyricLine>();
    private string _lyricsKey = "";
    private int _lyricIndex = -1;
    private string _lyricLine = "";

    // What's on screen now, so Refresh only redoes what changed.
    private BitmapSource? _shownCover;
    private Color _accent = Colors.White;
    private readonly SolidColorBrush _accentBrush = new(Colors.White);
    private string? _sourcesKey, _shelfKey, _trackKey;
    private bool? _shownPlaying;
    private Size _target;
    private bool _targetExpanded;
    private readonly HashSet<ShelfEntry> _shownEntries = new();

    public MainWindow()
    {
        InitializeComponent();
        BuildCollapsed();
        BuildExpanded();
        _lyric.Foreground = _accentBrush;
        PillRim.Stroke = _accentBrush;
        // The "drop": stretches along the way it moves while being carried (rotate, stretch, rotate back), and lifts a little.
        if (NotchHost.RenderTransform is TransformGroup group)
        {
            group.Children.Add(_dropIn);
            group.Children.Add(_dropStretch);
            group.Children.Add(_dropOut);
            group.Children.Add(_dropLift);
            group.Children.Add(_sizeScale);
        }

        NotchHost.SizeChanged += (_, _) => UpdateShape();
        NotchHost.MouseEnter += (_, _) => OnEnter();
        NotchHost.MouseLeave += (_, _) => OnLeave();
        // Small delays so passing the cursor along the top doesn't pop it open, and a brief exit doesn't snap it shut.
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); if (_inside && Settings.Current.OpenOn != "click") { _hoverOpen = true; UpdateExpanded(); } };
        _leaveDelay.Tick += (_, _) =>
        {
            _leaveDelay.Stop();
            if (_inside) return;
            if (DateTime.UtcNow < _keyHold) { _leaveDelay.Start(); return; }  // opened from the keyboard: it stays a few seconds
            // Open, it keeps a little slack below and beside it: reaching for its bottom edge
            // (or overshooting a button) isn't leaving.
            if (_expanded && NearNotch()) { _leaveDelay.Start(); return; }
            _hoverOpen = false;
            UpdateExpanded();
        };
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); _peeking = false; UpdateExpanded(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast = null; UpdateExpanded(); Refresh(); };
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); if (_moving) return; _hud = false; Refresh(); };
        _bumpBack.Tick += (_, _) =>
        {
            _bumpBack.Stop();
            Motion.Spring(BumpScale, ScaleTransform.ScaleXProperty, 1, 0.22, 0.32);
            Motion.Spring(BumpScale, ScaleTransform.ScaleYProperty, 1, 0.22, 0.32);
        };
        _progressTick.Tick += (_, _) => UpdateProgress();
        _lyricsTick.Tick += (_, _) => UpdateLyric(animate: true);
        _fullScreenCheck.Tick += (_, _) => { CheckFullScreen(); CheckUpdate(); if (++_topTicks % 3 == 0) KeepOnTop(); };
        _batteryCheck.Tick += (_, _) => CheckPower();
        _dragPoll.Tick += (_, _) =>
        {
            PollMove();
            PollDrag();
            CheckHover();
            CheckKeyLocks();
            PollMenu();
        };
        _compactDelay.Tick += (_, _) => { _compactDelay.Stop(); if (!NeedsFullWindow()) SetCompact(true); };
        _privacyCheck.Tick += (_, _) => UpdatePrivacy();
        _clockTick.Tick += (_, _) => PillLyric.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
        _brightness.Changed += level =>
        {
            if (_expanded || !Settings.Current.VolumeHud) return;
            _hudIcon.Text = "";
            _hudFill.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(level / 100.0, 0.25, 0.85));
            ShowHud("brightness");
        };
        // Locked (Win+L): the music bars and lyrics stop until the user is back.
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)) return;
            Dispatcher.BeginInvoke(() => { _locked = e.Reason == SessionSwitchReason.SessionLock; KeepOnTop(); Refresh(); });
        };

        // Drops: the catcher only opens the notch; the notch itself takes the drop.
        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += OnDragLeave;
        Root.Drop += OnDrop;

        // Touchpad, touch or pen have no hover to wait for: a click opens the closed notch,
        // and a middle click plays or pauses (the Mac's gestures, for a mouse).
        NotchHost.MouseLeftButtonUp += (_, e) =>
        {
            if (_expanded || _hiddenForFullScreen || _moving || e.ChangedButton != MouseButton.Left) return;
            _hoverDelay.Stop();
            _hoverOpen = true;
            UpdateExpanded();
        };
        NotchHost.MouseDown += (_, e) =>
        {
            if (_expanded || e.ChangedButton != MouseButton.Middle || !_media.HasMedia) return;
            _media.PlayPause();
            e.Handled = true;
        };

        NotchHost.MouseWheel += (_, e) =>
        {
            if (Native.KeyDown(0x11 /* VK_CONTROL */)) { StepSize(e.Delta > 0 ? 1 : -1); e.Handled = true; return; }
            if (!_volume.Available) return;
            _volume.Set(_volume.Level + e.Delta / 120f * 0.02f);
            UpdateVolume();
            if (!_expanded) ShowHud();
            e.Handled = true;
        };

        // Menus stay open until a click outside, Esc or a choice (see PollMenu): left to WPF, they
        // closed the moment another app (a game launcher) took the mouse for an instant.
        NotchHost.ContextMenu = new ContextMenu { StaysOpen = true };
        NotchHost.ContextMenuOpening += (_, e) =>
        {
            if (_openMenu != null) { e.Handled = true; _openMenu.IsOpen = false; return; }
            PrepareMenu();
        };
        NotchHost.ContextMenu.Opened += (_, _) => { _menuOpen = true; _openMenu = NotchHost.ContextMenu; App.Trace("menu opened"); };
        NotchHost.ContextMenu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _openMenu = null;
            _leaveDelay.Start();
            App.Trace("menu closed");
            if (Settings.MaxMulOverride > 0) { Settings.MaxMulOverride = 0; ApplyScaleNow(); }
        };

        _media.Changed += Refresh;
        _media.TrackChanged += OnTrackChanged;
        _media.PositionChanged += () => { UpdateProgress(); UpdateLyric(animate: true); };
        _volume.Changed += fromUs =>
        {
            UpdateVolume();
            // The keyboard, the taskbar or another app changed it: show it in the notch.
            if (!fromUs && !_expanded && Settings.Current.VolumeHud) ShowHud();
        };
        _volume.DeviceChanged += name =>
        {
            if (Settings.Current.Notices && name.Length > 0) ShowToast("\uE7F6", L("audioOutput"), name, ok: true);
        };
        Settings.Changed += () =>
        {
            // A toggle in the menu must not re-lay-out, re-spring or re-pop the notch: only real layout changes do.
            var layout = $"{Dock}|{Settings.Current.DockOffset}|{IslandShape}|{Settings.Current.Display}|{Settings.Current.Size}";
            if (layout != _layoutKey) { _layoutKey = layout; ApplyDock(); }
            var sizes = $"{Settings.Current.ClosedSize}|{Settings.Current.OpenSize}";
            if (sizes != _sizeKey) { _sizeKey = sizes; ApplyScale(); }
            ApplyMotionMode();
            ApplyStyle();
            ApplyLanguage();
            UpdateHotKey();
            UpdateLyricsSource();
            Refresh();
        };

        // Screens plugged in or out, resolution or scaling changed: back to the middle.
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PlaceWindow);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceWindow);
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.StatusChange) Dispatcher.BeginInvoke(CheckPower);
        };

        Loaded += async (_, _) =>
        {
            ApplyMotionMode();
            ApplyDock();
            _layoutKey = $"{Dock}|{Settings.Current.DockOffset}|{IslandShape}|{Settings.Current.Display}|{Settings.Current.Size}";
            ApplyScale();
            ApplyStyle();
            ApplyLanguage();
            UpdateShape();
            Refresh();
            _volume.Start();
            UpdateVolume();
            CheckPower();
            if (_power?.HasBattery == true) _batteryCheck.Start();
            _fullScreenCheck.Start();
            _dragPoll.Start();
            _privacyCheck.Start();
            UpdatePrivacy();
            _brightness.Start();
            await _media.StartAsync();
            // For screenshots and checks: "NotchUser.exe --show-open" starts with the panel open.
            if (Environment.GetCommandLineArgs().Contains("--show-open")) { await Task.Delay(1500); _hoverOpen = true; UpdateExpanded(); }
            foreach (var arg in Environment.GetCommandLineArgs().Where(a => a.StartsWith("--settings")))
                SettingsWindow.ShowPane(arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "General");
            if (Environment.GetCommandLineArgs().Contains("--show-menu")) { await Task.Delay(1500); PrepareMenu(); NotchHost.ContextMenu!.IsOpen = true; }
        };
    }

    // MARK: - Notch outline (animatable corner radii, like the Mac's NotchShape)

    public static readonly DependencyProperty TopRadiusProperty = DependencyProperty.Register(
        nameof(TopRadius), typeof(double), typeof(MainWindow), new PropertyMetadata(6.0, (d, _) => ((MainWindow)d).UpdateShape()));
    public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(
        nameof(BottomRadius), typeof(double), typeof(MainWindow), new PropertyMetadata(10.0, (d, _) => ((MainWindow)d).UpdateShape()));

    public double TopRadius { get => (double)GetValue(TopRadiusProperty); set => SetValue(TopRadiusProperty, value); }
    public double BottomRadius { get => (double)GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }

    private bool _shapeQueued;

    /// <summary>
    /// Size and both corner radii animate together, and each change asks for a new outline:
    /// they're merged into one redraw per frame, just before it's drawn.
    /// </summary>
    private void UpdateShape()
    {
        if (_shapeQueued) return;
        _shapeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { _shapeQueued = false; DrawShape(); });
    }

    /// <summary>Redraws the outline at the notch's current (animated) size: the clip and the rim share it.</summary>
    private void DrawShape()
    {
        double w = NotchHost.ActualWidth, h = NotchHost.ActualHeight;
        if (w <= 0 || h <= 0) { w = NotchHost.Width; h = NotchHost.Height; }
        Geometry shape;
        if (IslandShape)
        {
            var r = Math.Min((TopRadius + BottomRadius) / 2, Math.Min(w, h) / 2);
            shape = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }
        else shape = NotchShape.Create(w, h, TopRadius, BottomRadius);
        Notch.Clip = shape;
        RimLine.Data = shape;
        PillRim.Data = shape;
    }

    /// <summary>The cursor is on the open notch or just outside it (8 px to the sides, 16 below).</summary>
    private bool NearNotch()
    {
        var (x, y) = Native.Cursor;
        if (x == int.MinValue || PresentationSource.FromVisual(NotchHost) == null) return false;
        var p = NotchHost.PointFromScreen(new Point(x, y));
        return p.X >= -8 && p.X <= NotchHost.ActualWidth + 8 && p.Y >= -8 && p.Y <= NotchHost.ActualHeight + 16;
    }

    // MARK: - Window

    // Never take focus away from the app being used, and stay out of Alt+Tab.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        UpdateHotKey();
    }

    /// <summary>Top middle of the main screen (above it, off view, while something is full screen).</summary>
    private void PlaceWindow()
    {
        if (_anim) return;  // being carried or settling: the animation owns the position
        var handle = new WindowInteropHelper(this).Handle;
        if (DockedPos(handle) is { } pos)
            SetWindowPos(handle, IntPtr.Zero, pos.X, pos.Y, 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE | NOZORDER | NOACTIVATE */);
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = _hiddenForFullScreen ? -Height - 100 : 0;
        }
    }

    /// <summary>Where the window sits (screen pixels) for the chosen screen, edge and place along it.</summary>
    private (int X, int Y)? DockedPos(IntPtr handle, double? wDip = null, double? hDip = null)
    {
        var screen = Displays.Chosen(Settings.Current.Display, _screen);
        _screen = screen;
        if (handle == IntPtr.Zero || screen.Width == 0) return null;
        // In real pixels, on whichever screen was chosen (each can have its own scaling).
        var dpi = GetDpiForWindow(handle) / 96.0;
        double wPx = (wDip ?? Width) * dpi, hPx = (hDip ?? Height) * dpi;
        var scale = Settings.Current.LayoutScale * dpi;
        var off = Math.Clamp(Settings.Current.DockOffset, 0, 1);
        int x, y;
        if (!Vertical)
        {
            // The open panel must fit, so the notch stays that far from the corners.
            double half = Root.Width / 2 * scale, lo = screen.Left + half, hi = screen.Right - half;
            var center = hi < lo ? (screen.Left + screen.Right) / 2.0 : Math.Clamp(screen.Left + off * screen.Width, lo, hi);
            x = (int)(center - wPx / 2);
            // Docked against the usable area: the taskbar stays clear.
            y = Dock == "bottom" ? screen.WBottom - (int)hPx : screen.WTop;
            if (_hiddenForFullScreen) y = Dock == "bottom" ? screen.Bottom + 100 : screen.Top - (int)hPx - 100;
        }
        else
        {
            double half = Root.Height / 2 * scale, lo = screen.Top + half, hi = screen.Bottom - half;
            var center = hi < lo ? (screen.Top + screen.Bottom) / 2.0 : Math.Clamp(screen.Top + off * screen.Height, lo, hi);
            y = (int)(center - hPx / 2);
            x = Dock == "right" ? screen.WRight - (int)wPx : screen.WLeft;
            if (_hiddenForFullScreen) x = Dock == "right" ? screen.Right + 100 : screen.Left - (int)wPx - 100;
        }
        return (x, y);
    }

    private Point DockOrigin() => Dock switch
    {
        "bottom" => new Point(0.5, 1), "left" => new Point(0, 0.5), "right" => new Point(1, 0.5), _ => new Point(0.5, 0),
    };

    /// <summary>
    /// Lays the notch out for its edge: on top or bottom a horizontal pill that opens down or up,
    /// on the sides a vertical one that opens sideways. Everything else follows from these few settings.
    /// </summary>
    private void ApplyDock()
    {
        var d = Dock;
        var island = IslandShape;
        var vertical = Vertical;
        NotchHost.HorizontalAlignment = d switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        NotchHost.VerticalAlignment = d switch { "bottom" => VerticalAlignment.Bottom, "left" or "right" => VerticalAlignment.Center, _ => VerticalAlignment.Top };
        var g = island ? IslandGap : 0;
        NotchHost.Margin = d switch { "bottom" => new Thickness(0, 0, 0, g), "left" => new Thickness(g, 0, 0, 0), "right" => new Thickness(0, 0, g, 0), _ => new Thickness(0, g, 0, 0) };
        NotchHost.RenderTransformOrigin = _anim ? new Point(0.5, 0.5) : DockOrigin();
        DragCatcher.HorizontalAlignment = NotchHost.HorizontalAlignment;
        DragCatcher.VerticalAlignment = NotchHost.VerticalAlignment;
        DragCatcher.Width = vertical ? 220 : 480;
        DragCatcher.Height = vertical ? 230 : 220;
        Expanded.Margin = new Thickness(0, island ? 32 - IslandInset : 32, 0, 10);
        _hudBar.LayoutTransform = vertical ? new RotateTransform(-90) : Transform.Identity;

        // The closed pill's content: left | lyric | right, or top / bottom when standing up.
        Collapsed.ColumnDefinitions.Clear();
        Collapsed.RowDefinitions.Clear();
        if (!vertical)
        {
            Collapsed.Width = double.NaN; Collapsed.Height = NotchH;
            Collapsed.Margin = new Thickness(13, 0, 13, 0);
            Collapsed.HorizontalAlignment = HorizontalAlignment.Stretch; Collapsed.VerticalAlignment = VerticalAlignment.Top;
            Collapsed.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Collapsed.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Collapsed.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(PillLeft, 0); Grid.SetColumn(PillLeft, 0);
            Grid.SetRow(PillLyric, 0); Grid.SetColumn(PillLyric, 1);
            Grid.SetRow(PillRight, 0); Grid.SetColumn(PillRight, 2);
            PillLeft.HorizontalAlignment = PillRight.HorizontalAlignment = HorizontalAlignment.Stretch;
            PrivacyDot.Margin = new Thickness(0, 11, 7, 0);
        }
        else
        {
            Collapsed.Height = double.NaN; Collapsed.Width = NotchH;
            Collapsed.Margin = new Thickness(0, 13, 0, 13);
            Collapsed.HorizontalAlignment = HorizontalAlignment.Left; Collapsed.VerticalAlignment = VerticalAlignment.Stretch;
            Collapsed.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Collapsed.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Collapsed.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(PillLeft, 0); Grid.SetRow(PillLeft, 0);
            Grid.SetColumn(PillLyric, 0); Grid.SetRow(PillLyric, 1);
            Grid.SetColumn(PillRight, 0); Grid.SetRow(PillRight, 2);
            PillLeft.HorizontalAlignment = PillRight.HorizontalAlignment = HorizontalAlignment.Center;
            PrivacyDot.Margin = new Thickness(0, 5, 5, 0);
        }
        PrivacyDot.VerticalAlignment = VerticalAlignment.Top;
        PillLyric.Visibility = Visibility.Collapsed;

        _target = default;
        ApplyScale();
    }

    // MARK: - Moving the notch

    private bool _moving, _moveGrab, _anim, _settling, _commitOnSettle, _frames;
    private DateTime _moveStarted;
    private (string Dock, double Offset, string Display) _moveBackup;
    // The window as a body with a position, a velocity and a spring toward where it wants to be (screen pixels).
    private double _px, _py, _vx, _vy, _tx, _ty, _gx, _gy;
    private double _stretch, _angle;
    private TimeSpan _lastFrame;
    private readonly RotateTransform _dropIn = new(), _dropOut = new();
    private readonly ScaleTransform _dropStretch = new(), _dropLift = new();

    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);

    public void BeginMoveLater() => Dispatcher.BeginInvoke(DispatcherPriority.Background, BeginMove);

    /// <summary>
    /// "Move the notch": the pill says so, and once it's grabbed it follows the mouse like a drop
    /// of water: it stretches the way it moves, lags a hair behind, and on release falls into the
    /// nearest edge with a bounce. Release to keep it, Esc to put it back.
    /// </summary>
    public void BeginMove()
    {
        if (_moving || _hiddenForFullScreen) return;
        _moveBackup = (Settings.Current.Dock, Settings.Current.DockOffset, Settings.Current.Display);
        _hoverOpen = _peeking = false;
        UpdateExpanded();
        _moving = true;
        _moveGrab = _settling = false;
        _moveStarted = DateTime.UtcNow;
        _hudIcon.Text = "\uE7C2";
        _hudText.Text = L("moveHint");
        _hudKind = "move";
        _hud = true;
        _hudTimer.Stop();
        _dragPoll.Interval = TimeSpan.FromMilliseconds(33);
        Refresh();
    }

    /// <summary>The slow part: waiting for Esc, a timeout, or the press that grabs the pill. Once grabbed, the frame loop takes over.</summary>
    private void PollMove()
    {
        if (!_moving || _settling) return;
        if (Native.KeyDown(0x1B /* VK_ESCAPE */) || (!_moveGrab && DateTime.UtcNow - _moveStarted > TimeSpan.FromSeconds(25)))
        {
            EndMove(commit: false);
            return;
        }
        if (_moveGrab || !Native.LeftButtonDown || !CursorOnNotch()) return;
        StartDrag();
    }

    private void StartDrag()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var r)) return;
        var (cx, cy) = Native.Cursor;
        _px = r.Left; _py = r.Top; _vx = _vy = 0;
        _tx = _px; _ty = _py;
        _gx = cx - _px; _gy = cy - _py;   // where on the window it was grabbed
        _stretch = 0; _angle = 0;
        _moveGrab = _anim = true;
        // Everything below turns around the pill's own middle.
        NotchHost.RenderTransformOrigin = new Point(0.5, 0.5);
        _dropLift.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1.1, 0.25, 0.5));
        _dropLift.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1.1, 0.25, 0.5));
        if (!_frames)
        {
            _frames = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    /// <summary>One step per screen refresh: follow the mouse or fall toward the edge, then shape the drop by its speed.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        if (now == _lastFrame) return;
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        if (_moveGrab)
        {
            var (cx, cy) = Native.Cursor;
            if (Native.LeftButtonDown)
            {
                if (UpdateEdge(cx, cy))
                {
                    // Another edge: the pill turns (flat or upright) and keeps hanging from the mouse by its middle.
                    ApplyDock();
                    Refresh();
                    var dpi = GetDpiForWindow(handle) / 96.0;
                    _gx = Width * dpi / 2;
                    _gy = Height * dpi / 2;
                }
                _tx = cx - _gx;
                _ty = cy - _gy;
            }
            else
            {
                if (DockedPos(handle) is { } to) { _tx = to.X; _ty = to.Y; }
                _commitOnSettle = true;
                BeginSettle();
            }
        }

        // A spring toward the target: stiff while carried (a slight lag), loose when falling into place (it overshoots and wobbles).
        var (response, zeta) = _settling ? (0.5, 0.42) : (0.085, 0.8);
        var w0 = 2 * Math.PI / response;
        var steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        var h = dt / steps;
        for (var n = 0; n < steps; n++)
        {
            _vx += (w0 * w0 * (_tx - _px) - 2 * zeta * w0 * _vx) * h;
            _vy += (w0 * w0 * (_ty - _py) - 2 * zeta * w0 * _vy) * h;
            _px += _vx * h;
            _py += _vy * h;
        }
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(_px), (int)Math.Round(_py), 0, 0, 0x0001 | 0x0004 | 0x0010);

        // The drop: longer along its way, thinner across, easing back when it slows.
        var speed = Math.Sqrt(_vx * _vx + _vy * _vy);
        _stretch += (Math.Clamp(speed / 3200, 0, 0.32) - _stretch) * Math.Min(1, dt * 14);
        if (speed > 120)
        {
            var heading = Math.Atan2(_vy, _vx) * 180 / Math.PI;
            _angle += (((heading - _angle + 540) % 360) - 180) * Math.Min(1, dt * 20);
        }
        _dropIn.Angle = -_angle;
        _dropOut.Angle = _angle;
        _dropStretch.ScaleX = 1 + _stretch;
        _dropStretch.ScaleY = 1 / Math.Sqrt(1 + _stretch);

        if (_settling && speed < 8 && Math.Abs(_tx - _px) < 0.6 && Math.Abs(_ty - _py) < 0.6) FinishMove();
    }

    /// <summary>Let go: the drop falls toward its place and the lift relaxes.</summary>
    private void BeginSettle()
    {
        _moveGrab = false;
        _settling = true;
        _dropLift.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.35, 0.5));
        _dropLift.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.35, 0.5));
    }

    private void FinishMove()
    {
        if (_frames) { _frames = false; CompositionTarget.Rendering -= OnFrame; }
        _anim = _settling = _moving = _moveGrab = false;
        _hud = false;
        _dropIn.Angle = _dropOut.Angle = 0;
        _dropStretch.ScaleX = _dropStretch.ScaleY = 1;
        _dropLift.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _dropLift.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _dropLift.ScaleX = _dropLift.ScaleY = 1;
        _stretch = 0;
        NotchHost.RenderTransformOrigin = DockOrigin();
        if (_commitOnSettle) Settings.Current.Save();   // the save lays it out and places it for good
        else PlaceWindow();
        Bump();
        Refresh();
    }

    /// <summary>The edge nearest the mouse (with a margin of habit so it doesn't flicker at a corner) and how far along it.</summary>
    private bool UpdateEdge(int x, int y)
    {
        if (Displays.At(x, y) is not { } s) return false;
        var distance = new Dictionary<string, double>
        {
            ["top"] = y - s.Top, ["bottom"] = s.Bottom - 1 - y, ["left"] = x - s.Left, ["right"] = s.Right - 1 - x,
        };
        var nearest = distance.MinBy(k => k.Value).Key;
        var sameScreen = _screen?.Id == s.Id;
        var edge = !sameScreen || nearest == Dock || distance[nearest] < distance[Dock] - 40 ? nearest : Dock;
        var changed = edge != Dock || !sameScreen;
        var along = edge is "top" or "bottom" ? (x - s.Left) / (double)s.Width : (y - s.Top) / (double)s.Height;
        var current = Settings.Current;
        current.Dock = edge;
        current.DockOffset = Math.Clamp(along, 0, 1);
        current.Display = s.Primary ? "primary" : s.Id;
        _screen = s;
        return changed;
    }

    /// <summary>Esc (or a timeout): everything goes back to where it was, falling there if it was already carried.</summary>
    private void EndMove(bool commit)
    {
        (Settings.Current.Dock, Settings.Current.DockOffset, Settings.Current.Display) = _moveBackup;
        _screen = null;
        _commitOnSettle = commit;
        ApplyDock();
        if (_anim)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (DockedPos(handle) is { } to) { _tx = to.X; _ty = to.Y; }
            BeginSettle();
        }
        else FinishMove();
    }

    public void ResetPosition()
    {
        Settings.Current.Dock = "top";
        Settings.Current.DockOffset = 0.5;
        Settings.Current.Display = "primary";
        _screen = null;
        Settings.Current.Save();
    }

    private Displays.Screen? _screen;
    private DateTime _snoozeUntil = DateTime.MinValue;
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>Opened again from the Start menu or a shortcut: come back from "hide for…".</summary>
    public void Wake()
    {
        _snoozeUntil = DateTime.MinValue;
        CheckFullScreen();
    }

    private void Snooze(TimeSpan span)
    {
        _snoozeUntil = DateTime.UtcNow + span;
        CheckFullScreen();
    }

    /// <summary>With nothing to show the pill can fade out until the mouse comes to the top.</summary>
    private void UpdateIdle()
    {
        var idle = Settings.Current.HideWhenIdle && !Settings.Current.ClockInPill && !_expanded && !_inside && !_hud
                   && _toast == null && !_dragging && !_media.HasMedia && _shelf.Items.Count == 0
                   && PrivacyDot.Visibility != Visibility.Visible;
        if (NotchHost.Tag as bool? == idle) return;
        NotchHost.Tag = idle;
        Motion.Fade(NotchHost, idle ? 0 : 1, idle ? 0.5 : 0.15);
    }

    /// <summary>
    /// The Size setting: the whole notch, drawn bigger. The window is the full open size only while
    /// it's needed; closed, it's cut down to the pill (the layout stays put, shifted so the notch
    /// stays in the middle). A transparent window is copied whole on every frame, so the small one
    /// makes the music bars about eight times cheaper.
    /// </summary>
    private void ApplyScale()
    {
        var scale = Settings.Current.LayoutScale;
        if (Root.LayoutTransform is not ScaleTransform current || current.ScaleX != scale)
            Root.LayoutTransform = new ScaleTransform(scale, scale);
        UpdateSizeScale(animate: true);
        var (w, h) = _compact ? (Vertical ? (CompactH, CompactW) : (CompactW, CompactH)) : (Root.Width, Root.Height);
        Root.HorizontalAlignment = HorizontalAlignment.Left;
        Root.VerticalAlignment = VerticalAlignment.Top;
        // The layout is always the full-size one; the (smaller) window shows the part around the notch.
        var mx = Dock switch { "left" => 0, "right" => -(Root.Width - w), _ => -(Root.Width - w) / 2 };
        var my = Dock switch { "bottom" => -(Root.Height - h), "left" or "right" => -(Root.Height - h) / 2, _ => 0 };
        Root.Margin = new Thickness(mx * scale, my * scale, 0, 0);
        if (_anim) { Width = w * scale; Height = h * scale; return; }
        ApplyRect(w * scale, h * scale);
    }

    /// <summary>New size and place in a single step, so no frame is drawn with one and not the other.</summary>
    private void ApplyRect(double wDip, double hDip)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && DockedPos(handle, wDip, hDip) is { } pos)
        {
            var dpi = GetDpiForWindow(handle) / 96.0;
            SetWindowPos(handle, IntPtr.Zero, pos.X, pos.Y, (int)Math.Round(wDip * dpi), (int)Math.Round(hDip * dpi), 0x0004 | 0x0010 /* NOZORDER | NOACTIVATE */);
            return;
        }
        Width = wDip;
        Height = hDip;
        PlaceWindow();
    }

    /// <summary>Open, carrying something in, or about to: the window needs its full size.</summary>
    private bool NeedsFullWindow() => _expanded || _dragging || DragCatcher.Visibility == Visibility.Visible;

    /// <summary>Full size right away when needed; back to compact once the closing spring has settled.</summary>
    private void UpdateCompact()
    {
        // A menu is anchored to this window: it must not shrink, grow or collect garbage under it.
        if (_menuOpen) { _compactDelay.Stop(); return; }
        if (NeedsFullWindow())
        {
            _compactDelay.Stop();
            SetCompact(false);
        }
        else if (!_compact && !_compactDelay.IsEnabled) _compactDelay.Start();
    }

    private void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        ApplyScale();
        // Closed for a while: hand back the memory the open panel used (at most once a minute).
        if (compact && DateTime.UtcNow - _trimmedAt > TimeSpan.FromMinutes(1))
        {
            _trimmedAt = DateTime.UtcNow;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, App.TrimMemory);
        }
    }

    private int _topTicks;

    /// <summary>
    /// Another always-on-top window (Start, a launcher, a recorder) can end up above the notch
    /// and hide it for good: take the top of that group back, without taking focus.
    /// </summary>
    private void KeepOnTop()
    {
        if (_hiddenForFullScreen || _locked || _menuOpen || _moving) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010 /* NOMOVE | NOSIZE | NOACTIVATE */);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    // MARK: - Collapsed pill

    private readonly Border _pillCover = new() { Width = NotchH - 12, Height = NotchH - 12, CornerRadius = new CornerRadius((NotchH - 12) * 0.22) };
    private readonly BarsView _pillBars = new();
    private readonly MiniWaveView _pillWave = new();
    private readonly TextBlock _pillShelfIcon = Icons.Glyph("\uE7B8", 12, White(0.8));
    private readonly TextBlock _pillCount = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = White(0.8), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hudIcon = Icons.Glyph("\uE995", 13, White(0.85));
    private readonly ScaleTransform _hudFill = new(0, 1);
    private readonly FrameworkElement _pillEmptyLeft = new Border(), _pillEmptyRight = new Border();
    private readonly TextBlock _hudText = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = White(0.85), VerticalAlignment = VerticalAlignment.Center };

    private void BuildCollapsed()
    {
        Typography.SetNumeralAlignment(_pillCount, FontNumeralAlignment.Tabular);
        _pillBars.Color = _accentBrush;
        var hudBar = new Grid { Width = 46, Height = 4, VerticalAlignment = VerticalAlignment.Center };
        hudBar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = White(0.22) });
        hudBar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = White(0.9), RenderTransform = _hudFill });
        _hudBar = hudBar;
    }
    private Grid _hudBar = null!;

    /// <summary>Left: what's playing (else the shelf). Right: the music indicator (else the shelf count).</summary>
    private void UpdateCollapsed(bool hasMedia, bool hud)
    {
        UIElement left, right;
        if (hud)
        {
            left = _hudIcon;
            right = _hudKind is "text" or "move" ? (Vertical && _hudKind == "move" ? _pillEmptyRight : _hudText) : _hudBar;
        }
        else if (hasMedia)
        {
            left = _pillCover;
            right = Settings.Current.Wave ? _pillWave : _pillBars;
        }
        else if (_shelf.Items.Count == 0 && Settings.Current.ClockInPill)
        {
            // Nothing playing, nothing kept: just the time in the middle (PillLyric holds it).
            left = _pillEmptyLeft;
            right = _pillEmptyRight;
        }
        else
        {
            left = _pillShelfIcon;
            right = _pillCount;
        }
        if (PillLeft.Children.Count != 1 || PillLeft.Children[0] != left) { PillLeft.Children.Clear(); PillLeft.Children.Add(left); }
        if (PillRight.Children.Count != 1 || PillRight.Children[0] != right) { PillRight.Children.Clear(); PillRight.Children.Add(right); }
        _pillCount.Text = _shelf.Items.Count.ToString(CultureInfo.InvariantCulture);
    }

    // MARK: - Expanded sections

    private readonly StackPanel _toastSection = new() { Orientation = Orientation.Horizontal, Height = ToastH };
    private readonly TextBlock _toastIcon = Icons.Glyph("", 20, White(1));
    private readonly TextBlock _toastTitle = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _toastDetail = new() { FontSize = 11.5, Foreground = White(0.6), TextWrapping = TextWrapping.Wrap, MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly Grid _mediaSection = new() { Height = MediaH };
    private readonly Border _cover = new() { Width = 56, Height = 56, CornerRadius = new CornerRadius(56 * 0.22), Cursor = Cursors.Hand, Background = White(0.1) };
    private readonly Border _coverGlint = new() { CornerRadius = new CornerRadius(56 * 0.22), IsHitTestVisible = false, Opacity = 0 };
    private readonly Grid _coverBox = new() { Width = 56, Height = 56 };
    private readonly Marquee _title = new(15, FontWeights.SemiBold, Brushes.White, 19);
    private readonly Marquee _artist = new(13, FontWeights.Medium, White(0.65), 17);
    private readonly TextBlock _lyric = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Height = 16, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _texts = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
    private readonly BarsView _bigBars = new();
    private readonly MiniWaveView _bigWave = new();
    private readonly Grid _bigIndicator = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _elapsed = new() { Width = 34, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = White(0.5), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _remaining = new() { Width = 38, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = White(0.5), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _track = new() { Height = 14, Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(10, 0, 10, 0) };
    private readonly Border _trackBg = new() { Height = 4, CornerRadius = new CornerRadius(3.5), Background = White(0.18), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _trackFill = new() { Height = 4, CornerRadius = new CornerRadius(3.5), Background = White(0.7), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Border _controls = new() { CornerRadius = new CornerRadius(21), HorizontalAlignment = HorizontalAlignment.Center };
    private ControlButton _prev = null!, _play = null!, _next = null!, _volumeButton = null!, _trash = null!;
    private readonly Grid _volumeTrack = new() { Width = 52, Height = 14, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
    private readonly Border _volumeFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = White(0.7), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly StackPanel _volumeRow = new() { Orientation = Orientation.Horizontal, Width = 96, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _sources = new() { Orientation = Orientation.Horizontal, Width = 96, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.RightToLeft, VerticalAlignment = VerticalAlignment.Center };

    private readonly Rectangle _hairline = new()
    {
        Height = 1, Margin = new Thickness(0, (Separator - 1) / 2, 0, (Separator - 1) / 2),
        Fill = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(0x24, 255, 255, 255), 0.5), new(Color.FromArgb(0, 255, 255, 255), 1),
        }, new Point(0, 0.5), new Point(1, 0.5))),
    };

    private readonly Grid _shelfSection = new() { Height = ShelfH };
    private readonly Border _shelfWell = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(6) };
    private readonly ScrollViewer _shelfScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly StackPanel _shelfItems = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _dropZone = new() { Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly Border _dropFill = new() { CornerRadius = new CornerRadius(16) };
    private readonly Rectangle _dropStroke = new() { RadiusX = 16, RadiusY = 16, StrokeThickness = 1.4, StrokeDashArray = new DoubleCollection { 6 / 1.4, 5 / 1.4 } };
    private readonly TextBlock _dropIcon = Icons.Glyph("\uE896", 20, Brushes.White);
    private readonly TextBlock _dropTitle = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _dropCount = new() { FontSize = 11, Opacity = 0.6 };
    private readonly StackPanel _dropContent = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel _emptySection = new() { Height = EmptyH, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _emptyTitle = new() { FontSize = 13, FontWeight = FontWeights.Medium, Foreground = White(0.55), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _emptyHint = new() { FontSize = 11, Foreground = White(0.35), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

    private UIElement[] _mediaRows = Array.Empty<UIElement>();

    /// <summary>The panel's rows rise into place one after another, after the panel itself has started to open.</summary>
    private void StaggerContent()
    {
        var rows = _mediaRows.Concat(new UIElement[] { _hairline, _shelfSection }).Where(r => r.Visibility == Visibility.Visible).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var move = new TranslateTransform(0, 9);
            rows[i].RenderTransform = move;
            var delay = 0.06 + i * 0.045;
            move.BeginAnimation(TranslateTransform.YProperty, SpringEase.To(0, 0.38, 0.78, delay, 9));
            rows[i].BeginAnimation(OpacityProperty, Motion.Ease(1, 0.22, delay, 0));
        }
    }

    private void BuildExpanded()
    {
        // Toast
        _toastSection.Children.Add(_toastIcon);
        _toastIcon.FontWeight = FontWeights.SemiBold;
        _toastIcon.Margin = new Thickness(0, 0, 12, 0);
        var toastTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380 };
        toastTexts.Children.Add(_toastTitle);
        _toastDetail.Margin = new Thickness(0, 2, 0, 0);
        toastTexts.Children.Add(_toastDetail);
        _toastSection.Children.Add(toastTexts);

        // Music: cover, title and artist, the indicator
        foreach (var t in new[] { _elapsed, _remaining }) Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var top = new Grid { Height = 56 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _cover.BorderBrush = White(0.1);
        _cover.BorderThickness = new Thickness(0.5);
        _cover.Child = Icons.Glyph("\uEC4F", 18, White(0.5));
        // A soft shadow under the cover, made of translucent rings (a blur would redraw with every animation).
        foreach (var (margin, alpha) in new[] { (new Thickness(-5, -2, -5, -9), (byte)0x14), (new Thickness(-3, -1, -3, -6), (byte)0x1E), (new Thickness(-1.5, 0, -1.5, -3), (byte)0x2A) })
            _coverShadow.Children.Add(new Border { Margin = margin, CornerRadius = new CornerRadius(56 * 0.22 + 4), Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)) });
        _coverBox.Children.Add(_coverShadow);
        _coverBox.Children.Add(_cover);
        _coverBox.Children.Add(_coverGlint);
        _coverBox.RenderTransformOrigin = new Point(0.5, 0.5);
        _coverBox.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new SkewTransform(), new ScaleTransform() } };
        _coverBox.MouseLeftButtonUp += (_, _) => _media.OpenPlayer();
        _coverBox.MouseMove += (_, e) => Tilt(e.GetPosition(_coverBox));
        _coverBox.MouseLeave += (_, _) => Tilt(null);
        top.Children.Add(_coverBox);
        _texts.Children.Add(_title);
        _texts.Children.Add(_artist);
        _artist.Margin = new Thickness(0, 2, 0, 0);
        _texts.Children.Add(_lyric);
        Grid.SetColumn(_texts, 1);
        top.Children.Add(_texts);
        _bigBars.Color = _accentBrush;
        Grid.SetColumn(_bigIndicator, 2);
        top.Children.Add(_bigIndicator);

        // Progress
        var progress = new Grid { Height = 14, Margin = new Thickness(0, 6, 0, 0) };
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progress.Children.Add(_elapsed);
        _track.Children.Add(_trackBg);
        _track.Children.Add(_trackFill);
        Grid.SetColumn(_track, 1);
        progress.Children.Add(_track);
        Grid.SetColumn(_remaining, 2);
        progress.Children.Add(_remaining);
        // Thicker while hovered or dragged (Mac: 4 → 7 pt on a quick spring).
        _track.MouseEnter += (_, _) => ThickTrack(true);
        _track.MouseLeave += (_, _) => { if (!_seeking) ThickTrack(false); };
        _track.MouseLeftButtonDown += (_, e) =>
        {
            if (_media.Duration <= 0) return;
            _seeking = _track.CaptureMouse();
            ThickTrack(true);
            PreviewSeek(e.GetPosition(_track).X);
        };
        _track.MouseMove += (_, e) => { if (_seeking) PreviewSeek(e.GetPosition(_track).X); };
        _track.MouseLeftButtonUp += (_, e) =>
        {
            if (!_seeking) return;
            _seeking = false;
            _track.ReleaseMouseCapture();
            _media.Seek(Fraction(_track, e.GetPosition(_track).X) * _media.Duration);
            ThickTrack(_track.IsMouseOver);
            UpdateProgress();
            UpdateLyric(animate: false);
        };
        _track.LostMouseCapture += (_, _) => _seeking = false;

        // Controls: volume on the left, playback in the middle, players on the right.
        var controlsRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        _volumeButton = new ControlButton(Icons.Glyph("\uE995", 15, White(0.7)), 30);
        _volumeButton.Click += (_, _) => _volume.ToggleMute();
        _volumeTrack.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = White(0.18), VerticalAlignment = VerticalAlignment.Center });
        _volumeTrack.Children.Add(_volumeFill);
        _volumeTrack.MouseLeftButtonDown += (_, e) =>
        {
            _settingVolume = _volumeTrack.CaptureMouse();
            _volume.Set((float)Fraction(_volumeTrack, e.GetPosition(_volumeTrack).X));
            UpdateVolume();
        };
        _volumeTrack.MouseMove += (_, e) =>
        {
            if (!_settingVolume) return;
            _volume.Set((float)Fraction(_volumeTrack, e.GetPosition(_volumeTrack).X));
            UpdateVolume();
        };
        _volumeTrack.MouseLeftButtonUp += (_, _) => { _settingVolume = false; _volumeTrack.ReleaseMouseCapture(); };
        _volumeTrack.LostMouseCapture += (_, _) => _settingVolume = false;
        _volumeRow.Children.Add(_volumeButton);
        _volumeRow.Children.Add(_volumeTrack);
        controlsRow.Children.Add(_volumeRow);

        _prev = new ControlButton(Icons.Shape(Icons.Backward, 13, Brushes.White));
        _play = new ControlButton(Icons.Shape(Icons.Play, 20, Brushes.White)) { Margin = new Thickness(10, 0, 10, 0) };
        _next = new ControlButton(Icons.Shape(Icons.Forward, 13, Brushes.White));
        _prev.Click += (_, _) => _media.Previous();
        _play.Click += (_, _) => _media.PlayPause();
        _next.Click += (_, _) => _media.Next();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_prev);
        buttons.Children.Add(_play);
        buttons.Children.Add(_next);
        _controls.Child = buttons;
        controlsRow.Children.Add(_controls);
        controlsRow.Children.Add(_sources);

        _mediaRows = new UIElement[] { top, progress, controlsRow };
        var media = new StackPanel();
        media.Children.Add(top);
        media.Children.Add(progress);
        media.Children.Add(controlsRow);
        _mediaSection.Children.Add(media);

        // Shelf: a wide, short strip; files sit side by side and scroll sideways.
        var well = new Grid();
        well.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        well.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _shelfScroll.Content = _shelfItems;
        _shelfScroll.PreviewMouseWheel += (_, e) =>
        {
            if (_shelfScroll.ScrollableWidth <= 0) return;
            _shelfScroll.ScrollToHorizontalOffset(_shelfScroll.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };
        well.Children.Add(_shelfScroll);
        _trash = new ControlButton(Icons.Glyph("\uE74D", 12, White(0.45)), 30) { Margin = new Thickness(4, 0, 0, 0) };
        // With a selection the trash removes just that, otherwise everything.
        _trash.Click += (_, _) => RemoveFromShelf(_selected.Count > 0 ? _shelf.Items.Where(_selected.Contains).ToList() : _shelf.Items.ToList());
        Grid.SetColumn(_trash, 1);
        well.Children.Add(_trash);
        _shelfWell.Child = well;
        _shelfSection.Children.Add(_shelfWell);

        _dropIcon.FontWeight = FontWeights.SemiBold;
        _dropIcon.Margin = new Thickness(0, 0, 10, 0);
        _dropIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        _dropIcon.RenderTransform = new ScaleTransform();
        _dropContent.Children.Add(_dropIcon);
        var dropTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dropTexts.Children.Add(_dropTitle);
        dropTexts.Children.Add(_dropCount);
        _dropContent.Children.Add(dropTexts);
        _dropZone.Children.Add(_dropFill);
        _dropZone.Children.Add(_dropStroke);
        _dropZone.Children.Add(_dropContent);
        _dropZone.RenderTransform = new ScaleTransform();
        _shelfSection.Children.Add(_dropZone);

        // Nothing playing and an empty shelf
        var emptyRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var note = Icons.Glyph("\uEC4F", 13, White(0.55));
        note.Margin = new Thickness(0, 1, 6, 0);
        emptyRow.Children.Add(note);
        emptyRow.Children.Add(_emptyTitle);
        _emptySection.Children.Add(emptyRow);
        _emptySection.Children.Add(_emptyHint);

        foreach (var s in new UIElement[] { _toastSection, _mediaSection, _hairline, _shelfSection, _emptySection }) Expanded.Children.Add(s);
    }

    // MARK: - State → screen

    private void Refresh()
    {
        var hasMedia = _media.HasMedia;
        var hasShelf = _shelf.Items.Count > 0;
        var extended = hasMedia || hasShelf;
        // While something is carried in, the notch shows only the big drop zone.
        var showShelf = hasShelf || _dragging;
        var showMedia = hasMedia && !_dragging;
        var toast = _toast != null;
        var hud = _hud && !_expanded && !toast;
        var lyric = LyricShown() && !_expanded && !hud && !Vertical;
        var clock = Settings.Current.ClockInPill && !extended && !hud && !_expanded && !Vertical;

        // Collapsed
        UpdateCollapsed(hasMedia, hud);
        PillLyric.Text = clock ? DateTime.Now.ToString("t", CultureInfo.CurrentCulture) : _lyricLine;
        PillLyric.Foreground = White(clock ? 0.85 : 0.8);
        PillLyric.Visibility = lyric || clock ? Visibility.Visible : Visibility.Collapsed;
        if (clock && !_hiddenForFullScreen && !_locked) _clockTick.Start(); else _clockTick.Stop();
        var showCollapsed = !_expanded && (extended || hud || clock);
        if (Collapsed.Tag as bool? != showCollapsed)
        {
            Collapsed.Tag = showCollapsed;
            Motion.Fade(Collapsed, showCollapsed ? 1 : 0, showCollapsed ? 0.3 : 0.12);
        }

        // Expanded sections
        _toastSection.Visibility = toast ? Visibility.Visible : Visibility.Collapsed;
        _mediaSection.Visibility = showMedia && !toast ? Visibility.Visible : Visibility.Collapsed;
        _hairline.Visibility = showMedia && showShelf && !toast ? Visibility.Visible : Visibility.Collapsed;
        _shelfSection.Visibility = showShelf && !toast ? Visibility.Visible : Visibility.Collapsed;
        _emptySection.Visibility = !showMedia && !showShelf && !toast ? Visibility.Visible : Visibility.Collapsed;
        _shelfWell.Visibility = _dragging ? Visibility.Collapsed : Visibility.Visible;
        _dropZone.Visibility = _dragging ? Visibility.Visible : Visibility.Collapsed;
        if (_toast is { } t)
        {
            _toastIcon.Text = t.Glyph;
            _toastIcon.Foreground = Brush(t.Ok ? Green : Orange);
            _toastTitle.Text = t.Title;
            _toastDetail.Text = t.Detail;
            _toastDetail.Visibility = t.Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Music
        var key = _media.Title + "\n" + _media.Artist;
        if (key != _trackKey)
        {
            var first = _trackKey == null;
            _trackKey = key;
            _title.Text = _media.Title;
            _artist.Text = _media.Artist;
            _artist.Visibility = _media.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // The texts change places: the new ones rise into view (Mac: opacity + move from the bottom, 0.3 s).
            if (!first) SlideIn(_texts, 10, 0.3);
        }
        _lyric.Text = _lyricLine;
        _lyric.Visibility = LyricShown() ? Visibility.Visible : Visibility.Collapsed;
        SetCover(_media.Cover);
        if (_shownPlaying != _media.Playing) SetPlaying(_media.Playing);
        var sourcesKey = _media.SourcesKey;
        if (sourcesKey != _sourcesKey) RebuildSources(sourcesKey);
        if (ShelfKey() != _shelfKey) RebuildShelf();
        UpdateDropZone();
        _emptyTitle.Text = L("nothingPlaying");
        _emptyHint.Text = L("dragHint");
        UpdateProgress();
        UpdateShadows(showMedia);

        // Only the indicator that can be seen moves: the pill's while closed, the panel's while open.
        var running = !_hiddenForFullScreen && !_locked;
        var glassStyle = Settings.Current.Wave;
        var pillOn = _media.Playing && running && !_expanded;
        var bigOn = _media.Playing && running && _expanded;
        _pillBars.Playing = pillOn && !glassStyle;
        _pillWave.Playing = pillOn && glassStyle;
        _bigBars.Playing = bigOn && !glassStyle;
        _bigWave.Playing = bigOn && glassStyle;
        UpdatePillRim(running);
        UpdateLyricsSource();
        if (_lyrics.Count > 0 && _media.Playing && running) _lyricsTick.Start(); else _lyricsTick.Stop();
        if (_expanded && _media.Playing && running) _progressTick.Start(); else _progressTick.Stop();

        // Size: one spring per kind of change, as on the Mac.
        Size size;
        if (_expanded)
        {
            double sections;
            if (toast) sections = ToastH;
            else
            {
                var list = new List<double>();
                if (showMedia) list.Add(MediaH);
                if (showShelf) list.Add(ShelfH);
                if (list.Count == 0) list.Add(EmptyH);
                sections = list.Sum() + Separator * (list.Count - 1);
            }
            size = new Size(PanelW, NotchH + Chrome + sections - (IslandShape ? IslandInset : 0));
        }
        else if (hud) size = Vertical ? new Size(NotchH, NotchW + Side * 2 + 34) : new Size(NotchW + Side * 2 + 34, NotchH);
        else if (lyric) size = new Size(380, NotchH);
        else
        {
            var length = NotchW + (extended ? Side * 2 : 0);
            size = Vertical ? new Size(NotchH, length) : new Size(length, NotchH);
        }

        if (size != _target)
        {
            var toggling = _expanded != _targetExpanded;
            var (response, damping) = toggling ? (0.42, 0.76)   // opening or closing
                : _expanded ? (0.38, 0.8)                        // sections come and go
                : (0.4, 0.8);                                    // the pill grows its sides
            _target = size;
            _targetExpanded = _expanded;
            if (toggling)
            {
                // Like the Mac: opening, it spreads out of the edge first and then drops down;
                // closing, it pulls up first and slips back in last.
                var (lead, trail) = _expanded ? (WidthProperty, HeightProperty) : (HeightProperty, WidthProperty);
                var leadTo = lead == WidthProperty ? size.Width : size.Height;
                var trailTo = trail == WidthProperty ? size.Width : size.Height;
                NotchHost.BeginAnimation(lead, SpringEase.To(leadTo, response * 0.8, damping));
                NotchHost.BeginAnimation(trail, SpringEase.To(trailTo, response * 1.1, damping, delay: 0.035));
            }
            else
            {
                Motion.Spring(NotchHost, WidthProperty, size.Width, response, damping);
                Motion.Spring(NotchHost, HeightProperty, size.Height, response, damping);
            }
        }
        UpdateCompact();
        UpdateIdle();
    }

    /// <summary>Opens or closes from everything that wants it open (Mac: NotchController.tick).</summary>
    private void UpdateExpanded()
    {
        if (_moving && !_expanded) return;
        if (_menuOpen) { App.Trace("UpdateExpanded skipped: menu open"); return; }  // the mouse is on the menu, not the notch: hold still
        var want = _hoverOpen || _dragging || _toast != null || (_peeking && _media.HasMedia);
        if (_hiddenForFullScreen) want = false;
        SetExpanded(want);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        App.Trace($"SetExpanded {expanded} (hover={_hoverOpen} drag={_dragging} toast={_toast != null} peek={_peeking})");
        _expanded = expanded;
        UpdatePillSheen();
        UpdateSizeScale(animate: true);
        if (expanded)
        {
            _hud = false;
            if (_shelf.Prune()) _shelfKey = null;
        }
        UpdateLift();

        // The outline: corners flare and round more when open (Mac: 6/10 → 12/32).
        var island = IslandShape;
        BeginAnimation(TopRadiusProperty, SpringEase.To(island ? (expanded ? 26 : 14) : expanded ? 12 : 6, 0.42, 0.76));
        BeginAnimation(BottomRadiusProperty, SpringEase.To(island ? (expanded ? 26 : 14) : expanded ? 32 : 10, 0.42, 0.76));
        Motion.Fade(Backdrop, expanded ? 1 : 0, expanded ? 0.3 : 0.25);
        // CollapsedBlack (now below the backdrop) stays fully opaque, so the pill never goes see-through
        // mid-fade (that was the blink). Only Glass lets it go, late on the way in and early on the way out.
        if (Settings.Current.Glass) Motion.Fade(CollapsedBlack, expanded ? 0 : 1, expanded ? 0.2 : 0.12, expanded ? 0.15 : 0);
        else { CollapsedBlack.BeginAnimation(OpacityProperty, null); CollapsedBlack.Opacity = 1; }
        Motion.Fade(RimLine, expanded ? 1 : 0, 0.3);

        if (expanded) ShowExpandedContent(); else HideExpandedContent();
        UpdatePrivacy();
        Refresh();
        if (expanded) StaggerContent();
    }

    /// <summary>
    /// The open panel's content arrives: Basic grows from 90% while fading in; Elevated and Glass
    /// "materialize" out of a blur, slightly smaller and higher (the Mac's Materialize).
    /// </summary>
    private void ShowExpandedContent()
    {
        Expanded.Visibility = Visibility.Visible;
        var (scale, move) = Motion.Transforms(Expanded, new Point(0.5, 0));
        Expanded.BeginAnimation(OpacityProperty, null);
        Expanded.Opacity = 0;
        if (Settings.Current.Lifted)
        {
            const double r = 0.45, d = 0.82, delay = 0.04;
            scale.ScaleX = scale.ScaleY = 0.92;
            move.Y = -8;
            // No blur (it is drawn by the processor, every frame): the panel is drawn once into a texture while it settles.
            Expanded.Effect = null;
            Expanded.CacheMode = new BitmapCache(VisualTreeHelper.GetDpi(this).DpiScaleX * Settings.Current.LayoutScale * Settings.Current.StateFactor(true));
            var appearLifted = SpringEase.To(1, r, d, delay);
            appearLifted.Completed += (_, _) => Expanded.CacheMode = null;
            Expanded.BeginAnimation(OpacityProperty, appearLifted);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, r, d, delay));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, r, d, delay));
            move.BeginAnimation(TranslateTransform.YProperty, SpringEase.To(0, r, d, delay));
        }
        else
        {
            Expanded.Effect = null;
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.Y = 0;
            scale.ScaleX = scale.ScaleY = 0.9;
            // While it grows in, the content is drawn once into a texture and only scaled, instead
            // of laying out and drawing every text again on each frame.
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX * Settings.Current.LayoutScale * Settings.Current.StateFactor(true);
            Expanded.CacheMode = new BitmapCache(dpi);
            var appear = SpringEase.To(1, 0.4, 0.8, 0.05);
            appear.Completed += (_, _) => Expanded.CacheMode = null;
            Expanded.BeginAnimation(OpacityProperty, appear);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.4, 0.8, 0.05));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.4, 0.8, 0.05));
        }
    }

    /// <summary>Leaves quickly (Mac: opacity, ease-out 0.12 s) while the notch shrinks around it.</summary>
    private void HideExpandedContent()
    {
        var fade = Motion.Ease(0, 0.12, ease: new QuadraticEase { EasingMode = EasingMode.EaseOut });
        fade.Completed += (_, _) => { if (!_expanded) Expanded.Visibility = Visibility.Collapsed; };
        Expanded.BeginAnimation(OpacityProperty, fade);
    }

    private string _rimKey = "";

    /// <summary>
    /// The closed pill gets a thin rim in the album's color that breathes while music plays (a
    /// faint white one otherwise), so it reads on dark wallpapers too. Slow, 30 fps, only while visible.
    /// </summary>
    private void UpdatePillRim(bool running)
    {
        var state = _expanded || _hiddenForFullScreen ? "off" : _media.HasMedia ? (_media.Playing && running ? "breathe" : "steady") : "faint";
        if (state == _rimKey) return;
        _rimKey = state;
        switch (state)
        {
            case "breathe":
                var arrive = Motion.Ease(0.25, 0.5, delay: 0.25);
                arrive.Completed += (_, _) =>
                {
                    if (_rimKey != "breathe") return;
                    var a = new DoubleAnimation(0.25, 0.8, TimeSpan.FromSeconds(1.6))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(a, 30);
                    PillRim.BeginAnimation(OpacityProperty, a);
                };
                PillRim.BeginAnimation(OpacityProperty, arrive);
                break;
            default:
                PillRim.BeginAnimation(OpacityProperty, Motion.Ease(state == "off" ? 0 : state == "steady" ? 0.5 : 0.22, 0.35));
                break;
        }
    }

    /// <summary>Elevated and Glass: the closed notch rises a little under the mouse before it opens.</summary>
    private void UpdateLift()
    {
        var lift = _inside && !_expanded && Settings.Current.Lifted;
        if (lift == _lift) return;
        _lift = lift;
        Motion.Spring(LiftScale, ScaleTransform.ScaleXProperty, lift ? 1.04 : 1, 0.3, 0.6);
        Motion.Spring(LiftScale, ScaleTransform.ScaleYProperty, lift ? 1.12 : 1, 0.3, 0.6);
        UpdateShadows(_media.HasMedia && !_dragging);
    }

    /// <summary>The album's color rises from the bottom of the open notch while there's music (none without).</summary>
    private void UpdateShadows(bool showMedia)
    {
        var on = showMedia ? _glowLevel : 0;
        if (AccentGlow.Tag as double? == on) return;
        AccentGlow.Tag = on;
        AccentGlow.BeginAnimation(OpacityProperty, Motion.Ease(on, 0.4));
    }

    /// <summary>The look of each style (Settings → Appearance, or right-click the notch).</summary>
    private string _layoutKey = "", _styleKey = "", _langKey = "", _sizeKey = "";
    // The closed and the open size: the notch is drawn at the factor for its state, springing between the two.
    private readonly ScaleTransform _sizeScale = new(1, 1);

    /// <summary>Everything that happens the moment a menu is asked for.</summary>
    private void PrepareMenu()
    {
        App.Trace("menu opening");
        BuildMenu();
        LockForMenu();
        PlaceMenu(NotchHost.ContextMenu!);
    }

    private bool _instantSize;

    /// <summary>Re-lays out at once (no spring): for changes that must not be seen, like pinning the layout size for the menu.</summary>
    private void ApplyScaleNow()
    {
        _instantSize = true;
        ApplyScale();
        _instantSize = false;
        Root.UpdateLayout();
    }

    private void UpdateSizeScale(bool animate)
    {
        animate &= !_instantSize;
        var f = Settings.Current.StateFactor(_expanded);
        if (Math.Abs(_sizeScale.ScaleX - f) < 0.0005 && !_sizeScale.HasAnimatedProperties) return;
        if (!animate)
        {
            _sizeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _sizeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _sizeScale.ScaleX = _sizeScale.ScaleY = f;
            return;
        }
        _sizeScale.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(f, 0.42, 0.78));
        _sizeScale.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(f, 0.42, 0.78));
    }

    /// <summary>Ctrl + wheel: one step along the preset sizes, for the closed or the open notch, whichever is showing.</summary>
    private void StepSize(int direction)
    {
        var s = Settings.Current;
        var current = _expanded ? s.OpenSize : s.ClosedSize;
        var steps = Settings.SizeSteps;
        var index = Array.IndexOf(steps, steps.OrderBy(v => Math.Abs(v - current)).First());
        var next = steps[Math.Clamp(index + direction, 0, steps.Length - 1)];
        if (_expanded) s.OpenSize = next; else s.ClosedSize = next;
        s.Save();
    }
    private double _glowLevel = 0.55;

    private void UpdatePillSheen()
    {
        var look = Settings.Current.Style;
        var on = !_expanded && look != "basic" ? (look == "glass" ? 0.9 : 0.6) : 0;
        Motion.Fade(PillSheen, on, on > 0 ? 0.3 : 0.15);
    }
    private readonly Grid _coverShadow = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    /// <summary>Calm waits a little longer before opening and closing, Lively hardly waits at all.</summary>
    private void ApplyMotionMode()
    {
        var (open, leave) = Settings.Current.Motion switch { "calm" => (150, 300), "lively" => (85, 200), _ => (120, 250) };
        _hoverDelay.Interval = TimeSpan.FromMilliseconds(open);
        _leaveDelay.Interval = TimeSpan.FromMilliseconds(leave);
    }

    private void ApplyStyle()
    {
        var styleKey = $"{Settings.Current.Indicator}|{IslandShape}|{Settings.Current.Style}";
        if (styleKey == _styleKey) return;
        _styleKey = styleKey;
        var island = IslandShape;
        BeginAnimation(TopRadiusProperty, SpringEase.To(island ? (_expanded ? 26 : 14) : _expanded ? 12 : 6, 0.3, 0.9));
        BeginAnimation(BottomRadiusProperty, SpringEase.To(island ? (_expanded ? 26 : 14) : _expanded ? 32 : 10, 0.3, 0.9));
        _target = default;
        var style = Settings.Current.Style;
        var glass = style == "glass";
        var elevated = style == "elevated";
        // No real backdrop blur on a layered window: Glass is a more see-through surface instead.
        RimMid.Color = Color.FromArgb((byte)((glass ? 0.20 : elevated ? 0.16 : 0.10) * 255), 255, 255, 255);
        RimEnd.Color = Color.FromArgb((byte)((glass ? 0.50 : elevated ? 0.36 : 0.28) * 255), 255, 255, 255);
        // Basic is pure black; Elevated adds a reflection and a stronger glow; Glass lets the desktop show a little through.
        BackdropBlack.Opacity = glass ? 0.87 : 1;
        Sheen.Opacity = glass ? 1 : elevated ? 0.55 : 0;
        _glowLevel = elevated ? 0.85 : glass ? 0.6 : 0.55;
        CollapsedBlack.BeginAnimation(OpacityProperty, null);
        CollapsedBlack.Opacity = glass && _expanded ? 0 : 1;
        _coverShadow.Visibility = style == "basic" ? Visibility.Collapsed : Visibility.Visible;
        UpdatePillSheen();

        _bigIndicator.Children.Clear();
        _bigIndicator.Children.Add(Settings.Current.Wave ? _bigWave : _bigBars);
        _pillBars.SetGlow(elevated, _accent);
        _bigBars.SetGlow(elevated, _accent);

        // Surfaces inside the panel: plain, a soft lit pod (Elevated) or a pane of glass.
        Surface(_controls, style, fallback: 0, elevated: 0.05);
        Surface(_shelfWell, style, fallback: 0.05, elevated: 0.05);
        _controls.Padding = new Thickness(style == "basic" ? 0 : 6, 0, style == "basic" ? 0 : 6, 0);
        _texts.Effect = null;
        _shownPlaying = null;
        AccentGlow.Tag = null;
        UpdateCollapsed(_media.HasMedia, _hud && !_expanded);
    }

    private static void Surface(Border b, string style, double fallback, double elevated)
    {
        switch (style)
        {
            case "glass":
                b.Background = White(0.08);
                b.BorderBrush = White(0.12);
                b.BorderThickness = new Thickness(0.6);
                b.Effect = null;
                break;
            case "elevated":
                b.Background = Frozen(new LinearGradientBrush(Color.FromArgb((byte)(elevated * 1.6 * 255), 255, 255, 255),
                    Color.FromArgb((byte)(elevated * 0.6 * 255), 255, 255, 255), 90));
                b.BorderBrush = Frozen(new LinearGradientBrush(Color.FromArgb(0x2E, 255, 255, 255), Color.FromArgb(0x08, 255, 255, 255), 90));
                b.BorderThickness = new Thickness(0.6);
                b.Effect = null;
                break;
            default:
                b.Background = White(fallback);
                b.BorderBrush = null;
                b.BorderThickness = new Thickness(0);
                b.Effect = null;
                break;
        }
    }

    private void ApplyLanguage()
    {
        if (_langKey == Resolved) return;
        _langKey = Resolved;
        _shelfKey = null;
        _sourcesKey = null;
        _trash.ToolTip = L("clearShelf");
        // Icon-only buttons get names, so Narrator can read them.
        foreach (var (e, key) in new (UIElement, string)[]
        {
            (_prev, "previousTrack"), (_play, "playPause"), (_next, "nextTrack"), (_volumeButton, "muteVolume"),
            (_volumeTrack, "volume"), (_track, "progress"), (_coverBox, "openPlayer"), (_trash, "clearShelf"),
        })
            System.Windows.Automation.AutomationProperties.SetName(e, L(key));
    }

    // MARK: - Cover and album color

    private string _shownFallback = "";

    private void SetCover(BitmapSource? cover)
    {
        // No thumbnail (yet, or ever): the player's own icon stands in, never an empty square.
        var fallbackId = cover == null ? _media.SourceId : "";
        if (ReferenceEquals(cover, _shownCover) && fallbackId == _shownFallback) return;
        _shownCover = cover;
        _shownFallback = fallbackId;
        var image = cover ?? (fallbackId.Length > 0 ? ShellImage.App(fallbackId) : null);
        var accent = cover == null ? Colors.White : AlbumColor(cover);
        if (accent != _accent)
        {
            _accent = accent;
            // The album's color fades over (Mac: ease-in-out 0.5 s).
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromSeconds(0.5)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _pillWave.Color = _bigWave.Color = accent;
            _lyric.Foreground = _accentBrush;
            GlowBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(Deep(accent), TimeSpan.FromSeconds(0.5)));
            _pillBars.SetGlow(Settings.Current.Style == "elevated", accent);
            _bigBars.SetGlow(Settings.Current.Style == "elevated", accent);
        }

        Brush brush = image == null ? White(0.1) : Frozen(new ImageBrush(image) { Stretch = Stretch.UniformToFill });
        _pillCover.Background = brush;
        _cover.Background = brush;
        _cover.Child = image == null ? Icons.Glyph("\uEC4F", 18, White(0.5)) : null;
        // The new cover fades in over the old one (Mac: 0.3 s).
        foreach (var e in new UIElement[] { _cover, _pillCover }) e.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.3, from: 0.3));
    }

    /// <summary>The album's color made deep and saturated, for the glow at the bottom (like a blue night sky).</summary>
    private static Color Deep(Color c)
    {
        if (c == Colors.White) return Color.FromRgb(0x30, 0x30, 0x36);
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        double k = max == 0 ? 1 : 220.0 / max;
        return Color.FromRgb((byte)Math.Min(255, c.R * k * 0.85), (byte)Math.Min(255, c.G * k * 0.85), (byte)Math.Min(255, c.B * k * 0.85));
    }

    /// <summary>Average color of the cover, brightened so it shows on black (same rule as the Mac version).</summary>
    private static Color AlbumColor(BitmapSource cover)
    {
        try
        {
            var small = new TransformedBitmap(new FormatConvertedBitmap(cover, PixelFormats.Bgra32, null, 0),
                new ScaleTransform(1.0 / cover.PixelWidth, 1.0 / cover.PixelHeight));
            var px = new byte[4];
            small.CopyPixels(px, 4, 0);
            var c = System.Drawing.Color.FromArgb(px[2], px[1], px[0]);
            var h = c.GetHue();
            var s = Math.Min(c.GetSaturation() * 1.3, 1);
            var v = Math.Max(Math.Max(px[0], Math.Max(px[1], px[2])) / 255.0, 0.8);
            return FromHsv(h, s, v);
        }
        catch { return Colors.White; }
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>Play/pause changed: the icon swaps with a little pop, and the cover sits back while paused.</summary>
    private void SetPlaying(bool playing)
    {
        _shownPlaying = playing;
        var icon = Icons.Shape(playing ? Icons.Pause : Icons.Play, playing ? 19 : 20, Brushes.White);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        var pop = new ScaleTransform(0.5, 0.5);
        icon.RenderTransform = pop;
        icon.Opacity = 0;
        _play.Icon = icon;
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.3, 0.65));
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.3, 0.65));
        icon.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.15));

        // Richer styles: the cover sits back a little while paused, like Apple Music.
        var lifted = Settings.Current.Lifted;
        var scale = (ScaleTransform)((TransformGroup)_coverBox.RenderTransform).Children[0];
        var s = lifted && !playing ? 0.9 : 1;
        Motion.Spring(scale, ScaleTransform.ScaleXProperty, s, 0.4, 0.65);
        Motion.Spring(scale, ScaleTransform.ScaleYProperty, s, 0.4, 0.65);
    }

    /// <summary>
    /// Elevated and Glass: the cover leans toward the mouse with a glint that follows it.
    /// WPF has no 3D rotation for flat elements, so the lean is a light skew and squeeze.
    /// </summary>
    private void Tilt(Point? at)
    {
        var group = (TransformGroup)_coverBox.RenderTransform;
        var skew = (SkewTransform)group.Children[1];
        var grow = (ScaleTransform)group.Children[2];
        if (!Settings.Current.Lifted) at = null;
        double x = 0, y = 0;
        if (at is Point p)
        {
            x = Math.Clamp(p.X / 56 * 2 - 1, -1, 1);
            y = Math.Clamp(p.Y / 56 * 2 - 1, -1, 1);
            _coverGlint.Background = new RadialGradientBrush(Color.FromArgb(0x52, 255, 255, 255), Colors.Transparent)
            {
                Center = new Point(0.5 + x / 2, 0.5 + y / 2), GradientOrigin = new Point(0.5 + x / 2, 0.5 + y / 2),
                RadiusX = 0.75, RadiusY = 0.75,
            };
        }
        _coverGlint.BeginAnimation(OpacityProperty, Motion.Ease(at == null ? 0 : 1, 0.2));
        Motion.Spring(skew, SkewTransform.AngleYProperty, x * 5, 0.3, 0.7);
        Motion.Spring(skew, SkewTransform.AngleXProperty, -y * 5, 0.3, 0.7);
        Motion.Spring(grow, ScaleTransform.ScaleXProperty, at == null ? 1 : 1.07 - Math.Abs(x) * 0.03, 0.3, 0.7);
        Motion.Spring(grow, ScaleTransform.ScaleYProperty, at == null ? 1 : 1.07 - Math.Abs(y) * 0.03, 0.3, 0.7);
    }

    private static void SlideIn(UIElement e, double from, double seconds)
    {
        var (_, move) = Motion.Transforms(e);
        move.BeginAnimation(TranslateTransform.YProperty, Motion.Ease(0, seconds, from: from));
        e.BeginAnimation(OpacityProperty, Motion.Ease(1, seconds, from: 0));
    }

    // MARK: - Progress

    private void ThickTrack(bool thick)
    {
        foreach (var b in new[] { _trackBg, _trackFill }) Motion.Spring(b, HeightProperty, thick ? 7 : 4, 0.25, 0.8);
        _trackFill.Background = thick ? _accentBrush : White(0.7);
    }

    private void PreviewSeek(double x)
    {
        var position = Fraction(_track, x) * _media.Duration;
        _trackFill.Width = _track.ActualWidth * position / _media.Duration;
        _elapsed.Text = Clock(position);
        _remaining.Text = "-" + Clock(_media.Duration - position);
    }

    private void UpdateProgress()
    {
        var duration = _media.Duration;
        if (!_expanded || _seeking) return;
        var position = duration > 0 ? _media.LivePosition : 0;
        // A new width means a new layout pass: only when the bar visibly moves (half a pixel), not on every tick.
        var width = duration > 0 ? Math.Max(0, _track.ActualWidth * position / duration) : 0;
        if (double.IsNaN(_trackFill.Width) || Math.Abs(_trackFill.Width - width) >= 0.5) _trackFill.Width = width;
        _elapsed.Text = Clock(position);
        _remaining.Text = "-" + Clock(Math.Max(duration - position, 0));
    }

    private static string Clock(double seconds)
    {
        var t = (int)Math.Max(0, seconds);
        return $"{t / 60}:{t % 60:00}";
    }

    /// <summary>Where along <paramref name="bar"/> the mouse is, from 0 to 1.</summary>
    private static double Fraction(FrameworkElement bar, double x) => Math.Clamp(x / Math.Max(bar.ActualWidth, 1), 0, 1);

    // MARK: - Players

    /// <summary>App icons of every source; tap one to show it, tap the shown one to open the app (Mac: sourceSwitcher).</summary>
    private void RebuildSources(string key)
    {
        _sourcesKey = key;
        _sources.Children.Clear();
        foreach (var s in _media.Sessions.Reverse())
        {
            var id = s.SourceAppUserModelId;
            var selected = id == _media.SourceId;
            var name = MediaService.AppName(id);
            UIElement icon = ShellImage.App(id) is { } image
                ? new Image { Source = image, Width = 18, Height = 18 }
                : Icons.Glyph("\uEC4F", 13, Brushes.White);
            var cell = new Grid { Width = 24, Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand, FlowDirection = FlowDirection.LeftToRight };
            var holder = new Grid { Width = 18, Height = 18, Opacity = selected ? 1 : 0.4 };
            holder.Children.Add(icon);
            if (MediaService.IsPlaying(s) && !selected)
            {
                holder.Children.Add(new Ellipse
                {
                    Width = 5, Height = 5, Fill = Brush(Green), HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -1, -1),
                });
            }
            cell.Children.Add(holder);
            cell.ToolTip = string.Format(L(selected ? "openApp" : "showApp"), name);
            Motion.AddPress(cell);
            cell.MouseLeftButtonUp += (_, _) => { if (selected) _media.OpenPlayer(id); else _media.Pin(id); };
            _sources.Children.Add(cell);
        }
    }

    // MARK: - Volume

    /// <summary>A level (volume, brightness) or a short state ("Caps Lock on") in the closed pill.</summary>
    private void ShowHud(string kind = "volume")
    {
        if (_toast != null || _hiddenForFullScreen || _moving || (Vertical && kind == "text")) return;
        _hudKind = kind;
        _hud = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        if (kind == "volume") UpdateVolume();
        Refresh();
    }

    private void UpdateVolume()
    {
        var level = _volume.Muted ? 0 : _volume.Level;
        _volumeRow.Visibility = _volume.Available ? Visibility.Visible : Visibility.Hidden;
        _volumeFill.Width = _volumeTrack.Width * level;
        var glyph = _volume.Muted || level <= 0 ? "\uE74F" : level < 0.34 ? "\uE993" : level < 0.67 ? "\uE994" : "\uE995";
        _volumeButton.Icon = Icons.Glyph(glyph, 15, White(0.7));
        // The pill shows another level right now (brightness, Caps Lock): leave it be.
        if (_hud && _hudKind != "volume") return;
        _hudFill.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(level, 0.25, 0.85));
        _hudIcon.Text = glyph;
    }

    /// <summary>
    /// Caps Lock and Num Lock, read on the same light poll as the mouse (a keyboard hook would sit
    /// in front of every key pressed in every app, games included).
    /// </summary>
    private void CheckKeyLocks()
    {
        var caps = Native.Toggled(0x14 /* VK_CAPITAL */);
        var num = Native.Toggled(0x90 /* VK_NUMLOCK */);
        var (wasCaps, wasNum) = (_capsLock, _numLock);
        (_capsLock, _numLock) = (caps, num);
        if (!Settings.Current.Notices || _expanded) return;
        string? name = wasCaps is bool c && c != caps ? "Caps Lock" : wasNum is bool n && n != num ? "Num Lock" : null;
        if (name == null) return;
        var on = name == "Caps Lock" ? caps : num;
        _hudIcon.Text = "\uE765";
        _hudText.Text = string.Format(L(on ? "lockOn" : "lockOff"), name);
        ShowHud("text");
    }

    /// <summary>The privacy dot: camera (green) wins over microphone (orange); hidden while open.</summary>
    private void UpdatePrivacy()
    {
        var (mic, camera) = Privacy.InUse();
        var on = (mic || camera) && !_expanded;
        PrivacyDot.Fill = Brush(camera ? Green : Orange);
        if ((PrivacyDot.Visibility == Visibility.Visible) == on) return;
        PrivacyDot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) PrivacyDot.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.3, from: 0));
    }

    // MARK: - Lyrics

    private bool LyricShown() =>
        Settings.Current.LyricsEnabled && _media.HasMedia && _lyrics.Count > 0 && _lyricIndex >= 0 && _lyricLine.Length > 0;

    /// <summary>Fetch the lyrics when the song changes (or the add-on is turned on).</summary>
    private async void UpdateLyricsSource()
    {
        var key = Settings.Current.LyricsEnabled && _media.HasMedia ? $"{_media.Title}\n{_media.Artist}" : "";
        if (key == _lyricsKey) return;
        _lyricsKey = key;
        _lyrics = Array.Empty<LyricLine>();
        _lyricIndex = -1;
        _lyricLine = "";
        if (key.Length == 0) return;
        var lines = await Lyrics.FindAsync(_media.Title, _media.Artist, _media.Album, _media.Duration);
        if (key != _lyricsKey) return;  // the song changed meanwhile
        _lyrics = lines;
        UpdateLyric(animate: false);
        Refresh();
    }

    private void UpdateLyric(bool animate)
    {
        // A little ahead, so the line shows up as it's sung and not just after.
        var index = Lyrics.IndexAt(_lyrics, _media.LivePosition + 0.25);
        if (index == _lyricIndex) return;
        var wasShown = LyricShown();
        _lyricIndex = index;
        _lyricLine = index >= 0 ? (_lyrics[index].Text.Length > 0 ? _lyrics[index].Text : "♪") : "";
        if (LyricShown() != wasShown) { Refresh(); return; }
        _lyric.Text = PillLyric.Text = _lyricLine;
        if (!animate || !LyricShown()) return;
        SlideIn(_lyric, 6, 0.26);
        SlideIn(PillLyric, 6, 0.26);
    }

    // MARK: - Bounce, peek and messages

    /// <summary>Quick squash-and-stretch, like the island reacting to something new (Mac: 1.07 × 1.16, response 0.22, damping 0.32).</summary>
    private void Bump()
    {
        var (bx, by) = Settings.Current.Motion switch { "calm" => (1.04, 1.09), "lively" => (1.1, 1.23), _ => (1.07, 1.16) };
        Motion.Spring(BumpScale, ScaleTransform.ScaleXProperty, bx, 0.22, 0.32);
        Motion.Spring(BumpScale, ScaleTransform.ScaleYProperty, by, 0.22, 0.32);
        _bumpBack.Stop();
        _bumpBack.Start();
    }

    private void OnTrackChanged()
    {
        Bump();
        // Off by default, as on the Mac: the closed notch just hops.
        if (!Settings.Current.PeekOnTrackChange || _expanded) return;
        _peeking = true;
        _peekTimer.Stop();
        _peekTimer.Start();
        UpdateExpanded();
    }

    private void ShowToast(string glyph, string title, string detail, bool ok, double seconds = 2.8)
    {
        _toast = new Toast(glyph, title, detail, ok);
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
        _toastTimer.Start();
        _hud = false;
        Bump();
        UpdateExpanded();
        Refresh();
        // The icon pops in with a bounce.
        _toastIcon.RenderTransformOrigin = new Point(0.5, 0.5);
        var pop = _toastIcon.RenderTransform as ScaleTransform ?? new ScaleTransform();
        _toastIcon.RenderTransform = pop;
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.4, 0.4, 0.08, 0.3));
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.4, 0.4, 0.08, 0.3));
    }

    // MARK: - Copy text from the screen (Ctrl+Shift+2)

    private const int WM_HOTKEY = 0x0312, HotKeyId = 1;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_2 = 0x32;
    private readonly bool[] _keyOn = new bool[4], _keyTried = new bool[4];
    private DateTime _keyHold = DateTime.MinValue;
    private const uint MOD_ALT = 0x1;

    private void UpdateHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var s = Settings.Current;
        void Sync(int id, bool want, uint modifiers, uint key, string label)
        {
            if (!want) { if (_keyOn[id]) UnregisterHotKey(handle, id); _keyOn[id] = _keyTried[id] = false; return; }
            if (_keyTried[id]) return;  // tried once: a taken shortcut is reported once, not at every setting
            _keyTried[id] = true;
            _keyOn[id] = RegisterHotKey(handle, id, modifiers | MOD_NOREPEAT, key);
            if (!_keyOn[id]) ShowToast("\uE7BA", string.Format(L("shortcutTakenN"), label), "", ok: false);
        }
        Sync(HotKeyId, s.OcrEnabled, MOD_CONTROL | MOD_SHIFT, VK_2, "Ctrl+Shift+2");
        Sync(2, s.ToggleHotkey, MOD_CONTROL | MOD_ALT, 0x4E /* N */, "Ctrl+Alt+N");
        Sync(3, s.MicHotkey, MOD_CONTROL | MOD_ALT, 0x4D /* M */, "Ctrl+Alt+M");
    }

    /// <summary>Ctrl+Alt+N: opens the notch for a few seconds (or until the mouse has been on it and left), or closes it.</summary>
    private void ToggleByKey()
    {
        if (_hiddenForFullScreen || _moving) return;
        if (_expanded)
        {
            _keyHold = DateTime.MinValue;
            _hoverOpen = _peeking = false;
            UpdateExpanded();
        }
        else
        {
            _keyHold = DateTime.UtcNow.AddSeconds(5);
            _hoverOpen = true;
            UpdateExpanded();
            _leaveDelay.Start();
        }
    }

    private void ToggleMicByKey()
    {
        var muted = Volume.ToggleMicrophone();
        if (muted == null) ShowToast("\uF781", L("micNone"), "", ok: false);
        else ShowToast(muted.Value ? "\uF781" : "\uE720", L(muted.Value ? "micMuted" : "micOn"), "", ok: !muted.Value);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == 2) { Dispatcher.BeginInvoke(ToggleByKey); handled = true; }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == 3) { Dispatcher.BeginInvoke(ToggleMicByKey); handled = true; }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            // Leave the window procedure before opening the selection overlay.
            Dispatcher.BeginInvoke(CopyTextFromScreen);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void CopyTextFromScreen()
    {
        if (!Settings.Current.OcrEnabled) return;
        // Don't freeze the open panel into the snapshot.
        if (_expanded)
        {
            _hoverOpen = _peeking = false;
            SetExpanded(false);
            await Task.Delay(300);
        }
        var text = await ScreenText.CaptureAsync();
        if (text == null) return;
        if (text.Length == 0)
        {
            ShowToast("\uE8C8", L("noTextFound"), L("noTextHint"), ok: false);
            return;
        }
        try { Clipboard.SetText(text); } catch { }
        var preview = text.ReplaceLineEndings(" ");
        ShowToast("\uE8C8", L("textCopied"), preview.Length > 90 ? preview[..90] : preview, ok: true);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // MARK: - Battery

    private void CheckPower()
    {
        var now = Power.Read();
        var before = _power;
        _power = now;
        if (before == null || now == null || !now.Value.HasBattery || !Settings.Current.Notices) return;
        var (was, state) = (before.Value, now.Value);
        if (state.Charging && !was.Charging)
            ShowToast("\uEA93", L("charging"), $"{state.Percent}%", ok: true);
        else if (!state.Charging && was.Charging)
            ShowToast("\uE83F", L("onBattery"), $"{state.Percent}%", ok: true);
        else if (!state.Charging && (state.Percent <= 20 && was.Percent > 20 || state.Percent <= 10 && was.Percent > 10))
            ShowToast("\uE850", L("batteryLow"), $"{state.Percent}%", ok: false);
    }

    // MARK: - Full screen

    /// <summary>
    /// Step aside while a game, a video or a presentation fills the screen. The whole window goes,
    /// not just the notch: any window left on top of a borderless game, even an empty transparent
    /// one, keeps Windows from handing the game the screen directly (more lag, fewer frames).
    /// It moves off the screen rather than hiding: a transparent WPF window shown again after
    /// Hide() can come back opaque black. Meanwhile the app goes quiet: no polling, efficiency
    /// mode, lower priority.
    /// </summary>
    private void CheckFullScreen()
    {
        var hide = (FullScreen.IsActive(new WindowInteropHelper(this).Handle) && !_dragging) || DateTime.UtcNow < _snoozeUntil;
        if (hide == _hiddenForFullScreen) return;
        _hiddenForFullScreen = hide;
        if (hide)
        {
            _hoverOpen = _peeking = _hud = false;
            SetExpanded(false);
            NotchHost.Visibility = Visibility.Hidden;
            _dragPoll.Stop();
            _privacyCheck.Stop();
            PlaceWindow();
        }
        else
        {
            PlaceWindow();
            _dragPoll.Start();
            _privacyCheck.Start();
            NotchHost.Visibility = Visibility.Visible;
            NotchHost.Tag = null;
            NotchHost.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.25, from: 0));
        }
        Power.Quiet(hide);
        Refresh();
    }

    // MARK: - Shelf

    /// <summary>Changes whenever the shelf would look different (items, selection, language).</summary>
    private string ShelfKey() =>
        Resolved + "\n" + string.Join("\n", _shelf.Items.Select(e => (_selected.Contains(e) ? "*" : "") + e.Kind + ":" + e.Value));

    private readonly Dictionary<ShelfEntry, FrameworkElement> _itemViews = new();

    private void RebuildShelf()
    {
        _shelfKey = ShelfKey();
        _shelfItems.Children.Clear();
        _itemViews.Clear();
        var first = _shownEntries.Count == 0;
        foreach (var entry in _shelf.Items)
        {
            var view = ShelfItemView(entry);
            if (_shelfItems.Children.Count > 0) view.Margin = new Thickness(ItemGap, 0, 0, 0);
            _shelfItems.Children.Add(view);
            _itemViews[entry] = view;
            if (first || _shownEntries.Contains(entry)) continue;
            // Something new pops in (Mac: scale from 60% with a fade, on a spring).
            var (scale, _) = Motion.Transforms(view);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.35, 0.8, 0, 0.6));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.35, 0.8, 0, 0.6));
            view.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.25, from: 0));
        }
        _shownEntries.Clear();
        _shownEntries.UnionWith(_shelf.Items);
        _trash.ToolTip = _selected.Count > 0 ? string.Format(L("removeSelected"), _selected.Count) : L("clearShelf");
    }

    /// <summary>Removed entries shrink away first, then the others close the gap.</summary>
    private void RemoveFromShelf(IReadOnlyCollection<ShelfEntry> entries)
    {
        if (entries.Count == 0) return;
        foreach (var e in entries)
        {
            if (!_itemViews.TryGetValue(e, out var view)) continue;
            var (scale, _) = Motion.Transforms(view);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Ease(0.6, 0.18));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Ease(0.6, 0.18));
            view.BeginAnimation(OpacityProperty, Motion.Ease(0, 0.18));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _shelf.Remove(entries);
            _selected.ExceptWith(entries);
            Refresh();
        };
        timer.Start();
    }

    private FrameworkElement ShelfItemView(ShelfEntry entry)
    {
        var selected = _selected.Contains(entry);
        var root = new Grid { Width = ItemW, Height = 66, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        root.ToolTip = entry.Kind == "text" ? entry.Value : entry.Title;
        var bg = new Border
        {
            CornerRadius = new CornerRadius(10), Background = White(selected ? 0.18 : 0),
            BorderBrush = Brush(Accent, selected ? 0.9 : 0), BorderThickness = new Thickness(1.5),
        };
        root.Children.Add(bg);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var preview = Preview(entry);
        preview.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 6, ShadowDepth = 1, Direction = 270 };
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = MiddleTrim(entry.Title, 9.5, ItemW - 6), FontSize = 9.5, FontWeight = FontWeights.Medium,
            Foreground = White(0.75), TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 3, 2, 0),
        });
        root.Children.Add(stack);

        if (selected)
        {
            var check = new Grid { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, -2, 0, 0), IsHitTestVisible = false };
            check.Children.Add(new Ellipse { Fill = Brush(Accent), Stroke = Brushes.White, StrokeThickness = 1 });
            check.Children.Add(Icons.Glyph("\uE73E", 7, Brushes.White));
            root.Children.Add(check);
        }

        // Hovered: a small × to take just this one off.
        var close = new Grid
        {
            Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, -2, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand, ToolTip = L("removeFromShelf"),
        };
        close.Children.Add(new Ellipse { Fill = Black(0.65) });
        close.Children.Add(Icons.Glyph("\uE711", 7, Brushes.White));
        close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveFromShelf(new[] { entry }); };
        root.Children.Add(close);

        root.MouseEnter += (_, _) =>
        {
            close.Visibility = Visibility.Visible;
            if (!selected) bg.Background = White(0.1);
            var (lift, _) = Motion.Transforms(root);
            Motion.Spring(lift, ScaleTransform.ScaleXProperty, 1.07, 0.28, 0.6);
            Motion.Spring(lift, ScaleTransform.ScaleYProperty, 1.07, 0.28, 0.6);
        };
        root.MouseLeave += (_, _) =>
        {
            close.Visibility = Visibility.Collapsed;
            if (!selected) bg.Background = White(0);
            var (lift, _) = Motion.Transforms(root);
            Motion.Spring(lift, ScaleTransform.ScaleXProperty, 1, 0.3, 0.75);
            Motion.Spring(lift, ScaleTransform.ScaleYProperty, 1, 0.3, 0.75);
        };

        // Click selects, double-click opens, dragging carries the item (or the whole selection) out.
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { foreach (var g in Group(entry)) Open(g); _pressed = null; return; }
            _pressed = entry;
            _pressPoint = e.GetPosition(this);
        };
        root.MouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != entry) return;
            _pressed = null;
            if (!_selected.Remove(entry)) _selected.Add(entry);
            Refresh();
        };
        root.MouseMove += (_, e) =>
        {
            if (_pressed != entry || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _pressPoint.X) + Math.Abs(p.Y - _pressPoint.Y) < 5) return;
            _pressed = null;
            DragOut(entry, root);
        };

        var menu = new ContextMenu { StaysOpen = true };
        menu.Opened += (_, _) => { _menuOpen = true; _openMenu = menu; };
        menu.Closed += (_, _) => { _menuOpen = false; _openMenu = null; _leaveDelay.Start(); };
        root.ContextMenu = menu;
        root.ContextMenuOpening += (_, _) => { FillShelfMenu(menu, entry); LockForMenu(); PlaceMenu(menu); };
        return root;
    }

    /// <summary>The moment a menu is asked for, the notch holds still: no opening or closing under it.</summary>
    private void LockForMenu()
    {
        _hoverDelay.Stop();
        _leaveDelay.Stop();
        _peekTimer.Stop();
        _menuOpen = true;
    }

    /// <summary>
    /// Menus drop from just under the notch (or its open panel), centered, never at the mouse:
    /// at the mouse they sat on top of the controls and moved with the panel's animation.
    /// </summary>
    private void PlaceMenu(ContextMenu menu)
    {
        // Anchored to the layout (fixed size), with the notch's *final* size: if it were anchored to the
        // pill itself, the menu would slide along while the panel was still opening.
        var L = Settings.Current.LayoutScale;                          // the layout
        var k = L * Settings.Current.StateFactor(_expanded);           // the notch itself, at its size for this state
        var fw = _target.Width > 0 ? _target.Width : NotchHost.ActualWidth;
        var fh = _target.Height > 0 ? _target.Height : NotchHost.ActualHeight;
        var gap = IslandShape ? IslandGap * L : 0;
        double rw = Root.Width * L, rh = Root.Height * L;
        menu.PlacementTarget = Root;
        switch (Dock)
        {
            case "bottom":
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.HorizontalOffset = rw / 2 - 126;
                menu.VerticalOffset = rh - gap - fh * k - 2;
                break;
            case "left":
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
                menu.HorizontalOffset = gap + fw * k + 2 - rw;
                menu.VerticalOffset = (rh - fh * k) / 2;
                break;
            case "right":
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                menu.HorizontalOffset = rw - gap - fw * k - 2;
                menu.VerticalOffset = (rh - fh * k) / 2;
                break;
            default:
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.HorizontalOffset = rw / 2 - 126;
                menu.VerticalOffset = gap + fh * k + 2 - rh;
                break;
        }
    }

    private void FillShelfMenu(ContextMenu menu, ShelfEntry entry)
    {
        {
            menu.Items.Clear();
            var group = Group(entry);
            var many = group.Count > 1;
            menu.Items.Add(Item(many ? string.Format(L("openN"), group.Count) : entry.Kind == "text" ? L("copyText") : L("open"), "\uE8A7",
                () => group.ForEach(Open)));
            if (entry.Kind == "file" && !many)
                menu.Items.Add(Item(L("showInExplorer"), "\uE838", () => Process.Start("explorer.exe", $"/select,\"{entry.Value}\"")));
            menu.Items.Add(Item(many ? string.Format(L("copyN"), group.Count) : L("copy"), "\uE8C8", () => CopyToClipboard(group)));
            if (group.Any(e => e.Kind == "file"))
                menu.Items.Add(Item(L("zip"), "\uE8B7", () => Zip(group)));
            if (group.Any(IsImage))
            {
                menu.Items.Add(Item(L("convertPng"), "\uEB9F", () => ConvertImages(group, "png", 1)));
                menu.Items.Add(Item(L("convertJpeg"), "\uEB9F", () => ConvertImages(group, "jpg", 1)));
                menu.Items.Add(Item(L("halfSize"), "\uE9A6", () => ConvertImages(group, null, 0.5)));
            }
            menu.Items.Add(new Separator());
            if (_selected.Count < _shelf.Items.Count)
                menu.Items.Add(Item(L("selectAll"), "\uE8B3", () => { _selected.UnionWith(_shelf.Items); Refresh(); }));
            if (_selected.Count > 0)
                menu.Items.Add(Item(L("deselect"), "\uE8E6", () => { _selected.Clear(); Refresh(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(many ? string.Format(L("removeN"), group.Count) : L("removeFromShelf"), "\uE74D", () => RemoveFromShelf(group)));
        }
    }

    private static FrameworkElement Preview(ShelfEntry entry)
    {
        var box = new Grid { Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center };
        switch (entry.Kind)
        {
            case "file":
                if (ShellImage.File(entry.Value, 96) is { } image)
                    box.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform });
                else
                    box.Children.Add(Icons.Glyph("\uE8A5", 26, White(0.85)));
                break;
            case "link":
                var link = new Border
                {
                    Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
                    Background = Frozen(new LinearGradientBrush(Color.FromRgb(0x40, 0x8C, 0xFF), Color.FromRgb(0x26, 0x59, 0xE6), 90)),
                    Child = Icons.Glyph("\uE71B", 16, Brushes.White),
                };
                ((TextBlock)link.Child).FontWeight = FontWeights.Bold;
                box.Children.Add(link);
                break;
            default:
                box.Children.Add(new Border
                {
                    Width = 32, Height = 38, CornerRadius = new CornerRadius(6), Background = White(0.92), ClipToBounds = true,
                    Child = new TextBlock
                    {
                        Text = entry.Value, FontSize = 5.5, Foreground = Black(0.75), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4),
                    },
                });
                break;
        }
        return box;
    }

    /// <summary>"a very long file name.pdf" → "a very…name.pdf", so the end (and the extension) stays visible.</summary>
    private string MiddleTrim(string text, double size, double max)
    {
        var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        double Width(string s) => new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.White, dip).Width;
        if (Width(text) <= max) return text;
        for (var keep = text.Length - 1; keep > 2; keep--)
        {
            var head = (keep + 1) / 2;
            var candidate = text[..head] + "…" + text[^(keep - head)..];
            if (Width(candidate) <= max) return candidate;
        }
        return text[..1] + "…";
    }

    /// <summary>Acting on a selected item acts on the whole selection.</summary>
    private List<ShelfEntry> Group(ShelfEntry entry) =>
        _selected.Contains(entry) ? _shelf.Items.Where(_selected.Contains).ToList() : new List<ShelfEntry> { entry };

    private static DataObject DataFor(IReadOnlyCollection<ShelfEntry> entries)
    {
        var data = new DataObject();
        var files = new StringCollection();
        files.AddRange(entries.Where(e => e.Kind == "file").Select(e => e.Value).ToArray());
        if (files.Count > 0) data.SetFileDropList(files);
        // Plain text too, for apps that only take text (chat boxes, editors).
        data.SetText(string.Join(Environment.NewLine, entries.Select(e => e.Value)));
        return data;
    }

    private void DragOut(ShelfEntry entry, DependencyObject source)
    {
        _draggingOut = true;
        try { DragDrop.DoDragDrop(source, DataFor(Group(entry)), DragDropEffects.Copy); }
        finally { _draggingOut = false; }
    }

    private static void CopyToClipboard(IReadOnlyCollection<ShelfEntry> entries)
    {
        try { Clipboard.SetDataObject(DataFor(entries), true); } catch { }
    }

    private static void Open(ShelfEntry entry)
    {
        if (entry.Kind == "text") { try { Clipboard.SetText(entry.Value); } catch { } return; }
        try { Process.Start(new ProcessStartInfo(entry.Value) { UseShellExecute = true }); } catch { }
    }

    // MARK: - Dropping things in

    /// <summary>
    /// Windows doesn't say when a drag starts elsewhere, so while the button is held near the
    /// top the catcher appears under the cursor: a real drag then reaches us before the edge
    /// (Mac: a 640 × 220 zone below the notch opens it).
    /// </summary>
    private void PollDrag()
    {
        var down = Native.LeftButtonDown;
        var (x, y) = Native.Cursor;
        var cursor = new Point(x, y);
        bool In((double left, double top, double right, double bottom) z)
        {
            var (left, top, right, bottom) = z;
            if (PresentationSource.FromVisual(Root) == null) return false;
            var a = Root.PointToScreen(new Point(left, top));
            var b = Root.PointToScreen(new Point(right, bottom));
            return cursor.X >= a.X && cursor.X <= b.X && cursor.Y >= a.Y && cursor.Y <= b.Y;
        }
        var hot = _expanded || _menuOpen || down || _moving || In(Zone(260, -80));
        _dragPoll.Interval = TimeSpan.FromMilliseconds(hot ? 33 : 250);

        // Only for a real drag: held a moment and moved. A click or double-click near the top
        // (a tab, a title bar) never meets the catcher.
        if (down && _pressStart == null) _pressStart = (DateTime.UtcNow, cursor);
        if (!down) _pressStart = null;
        var dragged = _pressStart is var (at, from)
                      && (DateTime.UtcNow - at).TotalMilliseconds > 150
                      && Math.Abs(cursor.X - from.X) + Math.Abs(cursor.Y - from.Y) > 12;
        var catcher = dragged && !_draggingOut && !_moving && !_hiddenForFullScreen && In(Zone(220, 20));
        DragCatcher.Visibility = catcher || _dragging ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompact();
        if (!down && _dragging) EndDrag();
    }

    private (DateTime At, Point From)? _pressStart;

    /// <summary>A band along the notch's edge of the layout, <paramref name="depth"/> deep, trimmed (or grown) by <paramref name="inset"/> at both ends.</summary>
    private (double Left, double Top, double Right, double Bottom) Zone(double depth, double inset) => Dock switch
    {
        "bottom" => (inset, Root.Height - depth, Root.Width - inset, Root.Height),
        "left" => (0, inset, depth, Root.Height - inset),
        "right" => (Root.Width - depth, inset, Root.Width, Root.Height - inset),
        _ => (inset, 0, Root.Width - inset, depth),
    };

    /// <summary>
    /// The cursor's real position settles hovering, as on the Mac (it polls the mouse too). Windows
    /// doesn't always say the mouse left: another app holding the mouse (a game, its launcher) or a
    /// quick flick swallows it, and the notch would stay open for good.
    /// </summary>
    private void CheckHover()
    {
        if (_menuOpen || _seeking || _settingVolume || _dragging || _moving)
        {
            if (DateTime.UtcNow - _blockedTraceAt > TimeSpan.FromSeconds(1))
            {
                _blockedTraceAt = DateTime.UtcNow;
                App.Trace($"hover check blocked: menu={_menuOpen} seek={_seeking} vol={_settingVolume} drag={_dragging} openMenu={_openMenu?.IsOpen}");
            }
            return;
        }
        FollowMouse();
        var over = CursorOnNotch();
        // Entering only with no button held: moving a window or selecting text along the top isn't hovering.
        if (over && !_inside && !Native.LeftButtonDown && !Native.RightButtonDown) OnEnter();
        else if (!over && _inside) OnLeave();
    }

    private DateTime _blockedTraceAt;

    /// <summary>"Follow the mouse": touching the top edge of another screen brings the notch there.</summary>
    private void FollowMouse()
    {
        if (Settings.Current.Display != "follow" || _expanded || _inside || _dragging || _hiddenForFullScreen) return;
        var (x, y) = Native.Cursor;
        if (x == int.MinValue || Displays.At(x, y) is not { } s || s.Id == _screen?.Id || y - s.Top > 6) return;
        _screen = s;
        PlaceWindow();
    }

    // MARK: - Updates (once a day, only if turned on)

    private bool _updating;

    private async void CheckUpdate()
    {
        var s = Settings.Current;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!s.CheckUpdates || s.LastUpdateCheck == today || _updating || _hiddenForFullScreen) return;
        _updating = true;
        try
        {
            var found = await Updater.CheckAsync();
            s.LastUpdateCheck = today;
            s.Save();
            if (found != null) ShowToast("\uE896", L("updateAvailable"), "v" + found, ok: true, seconds: 4);
        }
        finally { _updating = false; }
    }

    // MARK: - Images

    private static bool IsImage(ShelfEntry e) => e.Kind == "file" && ShelfActions.IsImage(e.Value);

    /// <summary>Quick actions on pictures: to PNG or JPEG, or half the size. The result sits beside the original and goes on the shelf.</summary>
    private async void ConvertImages(List<ShelfEntry> group, string? format, double scale)
    {
        var sources = group.Where(IsImage).Select(e => e.Value).ToList();
        if (sources.Count == 0) return;
        try
        {
            var made = await Task.Run(() => ShelfActions.Convert(sources, format, scale));
            foreach (var path in made) _shelf.Add(new ShelfEntry("file", path));
            Refresh();
            ShowToast("\uEB9F", L("converted"), made.Count == 1 ? System.IO.Path.GetFileName(made[0]) : $"{made.Count}", ok: true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("\uE7BA", L("convertFailed"), "", ok: false);
        }
    }

    // MARK: - ZIP

    private async void Zip(List<ShelfEntry> group)
    {
        var paths = group.Where(e => e.Kind == "file" && (File.Exists(e.Value) || Directory.Exists(e.Value))).Select(e => e.Value).ToList();
        if (paths.Count == 0) return;
        try
        {
            var zip = await Task.Run(() => ShelfActions.Zip(paths));
            _shelf.Add(new ShelfEntry("file", zip));
            Refresh();
            ShowToast("\uE8B7", L("zipCreated"), System.IO.Path.GetFileName(zip), ok: true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowToast("\uE7BA", L("zipFailed"), "", ok: false);
        }
    }

    private bool CursorOnNotch()
    {
        var (x, y) = Native.Cursor;
        if (x == int.MinValue || PresentationSource.FromVisual(NotchHost) == null) return false;
        var p = NotchHost.PointFromScreen(new Point(x, y));
        // Flicking the mouse to the very edge must reach a floating island too.
        var gap = IslandShape ? IslandGap + 2 : 0;
        var d = Dock;
        return p.X >= (d == "left" ? -gap : 0) && p.X <= NotchHost.ActualWidth + (d == "right" ? gap : 0)
               && p.Y >= (d == "top" ? -gap : 0) && p.Y <= NotchHost.ActualHeight + (d == "bottom" ? gap : 0);
    }

    private void OnEnter()
    {
        if (_inside || _moving) return;
        App.Trace("enter");
        _inside = true;
        _leaveDelay.Stop();
        _peeking = false;
        if (!_expanded) _hoverDelay.Start();
        UpdateLift();
        UpdateIdle();
    }

    private void OnLeave()
    {
        if (!_inside) return;
        App.Trace("leave");
        _inside = false;
        _hoverDelay.Stop();
        if (!_leaveDelay.IsEnabled) _leaveDelay.Start();
        UpdateLift();
        UpdateIdle();
    }

    /// <summary>
    /// Closes an open menu the way a normal one would: a click anywhere that isn't the menu, its
    /// submenu or the notch, or Esc. The notch never takes the keyboard (the app in use keeps it),
    /// so Esc is read directly.
    /// </summary>
    private void PollMenu()
    {
        if (_openMenu == null) return;
        var (x, y) = Native.Cursor;
        var clickedAway = (Native.LeftButtonDown || Native.RightButtonDown) && !Native.OurWindowAt(x, y) && !CursorOnNotch();
        if (clickedAway || Native.KeyDown(0x1B /* VK_ESCAPE */))
        {
            App.Trace(clickedAway ? "menu: clicked away" : "menu: Esc");
            _openMenu.IsOpen = false;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggingOut) { e.Effects = DragDropEffects.None; return; }
        var p = e.GetPosition(NotchHost);
        var targeted = p.X >= 0 && p.Y >= 0 && p.X <= NotchHost.ActualWidth && p.Y <= NotchHost.ActualHeight;
        e.Effects = targeted ? DragDropEffects.Copy : DragDropEffects.None;
        if (!_dragging)
        {
            _dragging = true;
            _leaveDelay.Stop();
            UpdateExpanded();
            Refresh();
        }
        if (targeted != _dropTargeted)
        {
            _dropTargeted = targeted;
            UpdateDropZone();
            if (targeted)
            {
                // The tray icon bounces when the file is over the notch (Mac: symbolEffect .bounce).
                var bounce = (ScaleTransform)_dropIcon.RenderTransform;
                bounce.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(1, 0.3, 0.45, 0, 1.25));
                bounce.BeginAnimation(ScaleTransform.ScaleYProperty, SpringEase.To(1, 0.3, 0.45, 0, 1.25));
            }
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only react when really outside.
        var p = e.GetPosition(Root);
        var z = Zone(220, 20);
        var inCatcher = DragCatcher.IsVisible && p.X >= z.Left && p.X <= z.Right && p.Y >= z.Top && p.Y <= z.Bottom;
        var n = e.GetPosition(NotchHost);
        var inNotch = n.X >= 0 && n.Y >= 0 && n.X <= NotchHost.ActualWidth && n.Y <= NotchHost.ActualHeight;
        if (!inCatcher && !inNotch) EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging && !_dropTargeted) return;
        _dragging = _dropTargeted = false;
        DragCatcher.Visibility = Visibility.Collapsed;
        UpdateExpanded();
        Refresh();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var take = _dropTargeted && !_draggingOut;
        var added = false;
        if (take && e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths) added |= _shelf.Add(new ShelfEntry("file", path));
        }
        else if (take && e.Data.GetDataPresent(DataFormats.UnicodeText) && e.Data.GetData(DataFormats.UnicodeText) is string text
                 && text.Trim().Length > 0)
        {
            var t = text.Trim();
            var isLink = Uri.TryCreate(t, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !t.Contains(' ');
            added = _shelf.Add(isLink ? new ShelfEntry("link", t) : new ShelfEntry("text", text));
        }
        e.Handled = true;
        EndDrag();
        if (added) Bump();
        _leaveDelay.Start();
    }

    /// <summary>Big and obvious while carrying something in; brighter once it's over the notch.</summary>
    private void UpdateDropZone()
    {
        var on = _dropTargeted;
        _dropIcon.Text = on ? "\uE896" : "\uE896";
        _dropIcon.Foreground = _dropTitle.Foreground = _dropCount.Foreground = White(on ? 1 : 0.65);
        _dropTitle.Text = on ? L("releaseNow") : L("dropHere");
        _dropCount.Text = string.Format(L("alreadyOnShelf"), _shelf.Items.Count);
        _dropCount.Visibility = _shelf.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _dropFill.Background = White(on ? 0.16 : 0.05);
        _dropStroke.Stroke = White(on ? 0.7 : 0.3);
        var scale = (ScaleTransform)_dropZone.RenderTransform;
        if (_dropZone.Tag as bool? == on) return;
        _dropZone.Tag = on;
        Motion.Spring(scale, ScaleTransform.ScaleXProperty, on ? 1.02 : 1, 0.3, 0.7);
        Motion.Spring(scale, ScaleTransform.ScaleYProperty, on ? 1.02 : 1, 0.3, 0.7);
    }

    // MARK: - Menu

    /// <summary>A menu row with an icon (and a check when it's a setting), like the Mac's native menu.</summary>
    private static MenuItem Item(string header, string? glyph, Action action, bool? isChecked = null, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        if (glyph != null) item.Icon = Icons.Glyph(glyph, 14, Brushes.White);
        if (isChecked is bool c) { item.IsCheckable = true; item.IsChecked = c; }
        item.Click += (_, _) =>
        {
            // The menus stay open on their own (StaysOpen), so a choice closes them here.
            DependencyObject? owner = item;
            while (owner != null && owner is not System.Windows.Controls.ContextMenu) owner = LogicalTreeHelper.GetParent(owner);
            if (owner is System.Windows.Controls.ContextMenu menu) menu.IsOpen = false;
            action();
        };
        return item;
    }

    private static MenuItem Header(string text) => new()
    {
        Header = text, IsEnabled = false, Style = (Style)Application.Current.Resources["MenuHeader"],
    };

    /// <summary>
    /// A size dot is being dragged in Settings: the open one keeps the panel open (and lets it close after),
    /// the closed one closes it, so each can be seen changing.
    /// </summary>
    public void PreviewSize(bool open, bool start)
    {
        if (open && start) { _hoverOpen = true; UpdateExpanded(); }
        else if (open) { _hoverOpen = false; _leaveDelay.Start(); }
        else if (start) { _hoverOpen = false; UpdateExpanded(); }
    }

    /// <summary>The notch's right-click menu, built when it opens (same sections as the Mac's).</summary>
    private void BuildMenu()
    {
        var menu = NotchHost.ContextMenu!;
        menu.Items.Clear();
        if (Settings.Current.OcrEnabled)
        {
            menu.Items.Add(Item(L("copyTextFromScreen"), "\uE8C8", () => Dispatcher.BeginInvoke(CopyTextFromScreen), gesture: "Ctrl+Shift+2"));
            menu.Items.Add(new Separator());
        }
        if (_shelf.Items.Count > 0)
            menu.Items.Add(Item(L("clearShelf"), "\uE74D", () => RemoveFromShelf(_shelf.Items.ToList())));
        menu.Items.Add(Item(L("movePosition"), "\uE7C2", BeginMoveLater));
        var hide = new MenuItem { Header = L("hideFor"), Icon = Icons.Glyph("\uED1A", 14, Brushes.White) };
        hide.Items.Add(Item(L("min15"), null, () => Snooze(TimeSpan.FromMinutes(15))));
        hide.Items.Add(Item(L("hour1"), null, () => Snooze(TimeSpan.FromHours(1))));
        hide.Items.Add(Item(L("untilReopen"), null, () => Snooze(TimeSpan.FromDays(3650))));
        menu.Items.Add(hide);
        if (Updater.NewVersion is { } v)
            menu.Items.Add(Item(string.Format(L("downloadUpdate"), v), "\uE896", () => Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true })));
        menu.Items.Add(new Separator());
        // Everything else lives in Settings.
        menu.Items.Add(Item(L("settings"), "\uE713", SettingsWindow.ShowOrFocus));
        menu.Items.Add(Item(L("quit"), "\uE7E8", () => Application.Current.Shutdown()));
    }
}
