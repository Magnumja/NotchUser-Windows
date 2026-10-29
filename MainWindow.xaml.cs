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

    private readonly MediaService _media = new();
    private readonly Volume _volume = new();
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
    private bool _hud;
    private bool _seeking, _settingVolume;
    private bool _hiddenForFullScreen;
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
        UnderGlow.Fill = _accentBrush;
        _lyric.Foreground = _accentBrush;

        NotchHost.SizeChanged += (_, _) => UpdateShape();
        NotchHost.MouseEnter += (_, _) =>
        {
            _inside = true;
            _leaveDelay.Stop();
            _peeking = false;
            if (!_expanded) _hoverDelay.Start();
            UpdateLift();
        };
        NotchHost.MouseLeave += (_, _) =>
        {
            _inside = false;
            _hoverDelay.Stop();
            _leaveDelay.Start();
            UpdateLift();
        };
        // Small delays so passing the cursor along the top doesn't pop it open, and a brief exit doesn't snap it shut.
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); if (_inside) { _hoverOpen = true; UpdateExpanded(); } };
        _leaveDelay.Tick += (_, _) => { _leaveDelay.Stop(); if (!_inside) { _hoverOpen = false; UpdateExpanded(); } };
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); _peeking = false; UpdateExpanded(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast = null; UpdateExpanded(); Refresh(); };
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); _hud = false; Refresh(); };
        _bumpBack.Tick += (_, _) =>
        {
            _bumpBack.Stop();
            Motion.Spring(BumpScale, ScaleTransform.ScaleXProperty, 1, 0.22, 0.32);
            Motion.Spring(BumpScale, ScaleTransform.ScaleYProperty, 1, 0.22, 0.32);
        };
        _progressTick.Tick += (_, _) => UpdateProgress();
        _lyricsTick.Tick += (_, _) => UpdateLyric(animate: true);
        _fullScreenCheck.Tick += (_, _) => CheckFullScreen();
        _batteryCheck.Tick += (_, _) => CheckPower();
        _dragPoll.Tick += (_, _) => PollDrag();

        // Drops: the catcher only opens the notch; the notch itself takes the drop.
        Root.DragEnter += OnDragOver;
        Root.DragOver += OnDragOver;
        Root.DragLeave += OnDragLeave;
        Root.Drop += OnDrop;

        NotchHost.MouseWheel += (_, e) =>
        {
            if (!_volume.Available) return;
            _volume.Set(_volume.Level + e.Delta / 120f * 0.02f);
            UpdateVolume();
            if (!_expanded) ShowHud();
            e.Handled = true;
        };

        NotchHost.ContextMenu = new ContextMenu();
        NotchHost.ContextMenuOpening += (_, _) => BuildMenu();
        NotchHost.ContextMenu.Opened += (_, _) => _menuOpen = true;
        NotchHost.ContextMenu.Closed += (_, _) => { _menuOpen = false; _leaveDelay.Start(); };

        _media.Changed += Refresh;
        _media.TrackChanged += OnTrackChanged;
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
            ApplyScale();
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
            await _media.StartAsync();
        };
    }

    // MARK: - Notch outline (animatable corner radii, like the Mac's NotchShape)

    public static readonly DependencyProperty TopRadiusProperty = DependencyProperty.Register(
        nameof(TopRadius), typeof(double), typeof(MainWindow), new PropertyMetadata(6.0, (d, _) => ((MainWindow)d).UpdateShape()));
    public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(
        nameof(BottomRadius), typeof(double), typeof(MainWindow), new PropertyMetadata(10.0, (d, _) => ((MainWindow)d).UpdateShape()));

    public double TopRadius { get => (double)GetValue(TopRadiusProperty); set => SetValue(TopRadiusProperty, value); }
    public double BottomRadius { get => (double)GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }

    /// <summary>Redraws the outline at the notch's current (animated) size: clip, rim and shadows share it.</summary>
    private void UpdateShape()
    {
        double w = NotchHost.ActualWidth, h = NotchHost.ActualHeight;
        if (w <= 0 || h <= 0) { w = NotchHost.Width; h = NotchHost.Height; }
        var shape = NotchShape.Create(w, h, TopRadius, BottomRadius);
        Notch.Clip = shape;
        RimGlow.Data = RimLine.Data = shape;
        AmbientShadow.Data = NearShadow.Data = Halo.Data = shape;
        ShadowLayer.Clip = Frozen(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-300, -300, w + 600, h + 600)), shape));
        UnderGlow.Width = w * 0.55;
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

    /// <summary>Top middle of the main screen.</summary>
    private void PlaceWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    /// <summary>The Size setting: the whole notch, drawn bigger.</summary>
    private void ApplyScale()
    {
        var scale = Settings.Current.Scale;
        if (Root.LayoutTransform is not ScaleTransform current || current.ScaleX != scale)
            Root.LayoutTransform = new ScaleTransform(scale, scale);
        Width = Root.Width * scale;
        Height = Root.Height * scale;
        PlaceWindow();
    }

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
            right = _hudBar;
        }
        else if (hasMedia)
        {
            left = _pillCover;
            right = Settings.Current.Glass ? _pillWave : _pillBars;
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
    private readonly DropShadowEffect _coverShadow = new() { Direction = 270, ShadowDepth = 3, BlurRadius = 20, Opacity = 0.35, Color = Colors.White };
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
        _coverBox.Children.Add(_cover);
        _coverBox.Children.Add(_coverGlint);
        _coverBox.Effect = _coverShadow;
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
        var lyric = LyricShown() && !_expanded && !hud;

        // Collapsed
        UpdateCollapsed(hasMedia, hud);
        PillLyric.Text = _lyricLine;
        PillLyric.Visibility = lyric ? Visibility.Visible : Visibility.Collapsed;
        var showCollapsed = !_expanded && (extended || hud);
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
        Waves.Visibility = Settings.Current.Glass && showMedia && !toast ? Visibility.Visible : Visibility.Collapsed;
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
        var sourcesKey = string.Join("|", _media.Sessions.Select(x => x.SourceAppUserModelId + (MediaService.IsPlaying(x) ? "+" : ""))) + "|" + _media.SourceId;
        if (sourcesKey != _sourcesKey) RebuildSources(sourcesKey);
        if (ShelfKey() != _shelfKey) RebuildShelf();
        UpdateDropZone();
        _emptyTitle.Text = L("nothingPlaying");
        _emptyHint.Text = L("dragHint");
        UpdateProgress();
        UpdateShadows(showMedia);

        // Only the indicator that can be seen moves: the pill's while closed, the panel's while open.
        var running = !_hiddenForFullScreen;
        var glassStyle = Settings.Current.Glass;
        var pillOn = _media.Playing && running && !_expanded;
        var bigOn = _media.Playing && running && _expanded;
        _pillBars.Playing = pillOn && !glassStyle;
        _pillWave.Playing = pillOn && glassStyle;
        _bigBars.Playing = bigOn && !glassStyle;
        _bigWave.Playing = bigOn && glassStyle;
        Waves.Playing = _media.Playing && _expanded;
        UpdateLyricsSource();
        if (_lyrics.Count > 0 && _media.Playing && running) _lyricsTick.Start(); else _lyricsTick.Stop();
        if (_expanded && _media.Playing && running) _progressTick.Start(); else _progressTick.Stop();
        var glow = Settings.Current.Lifted && _media.Playing && !_expanded && hasMedia;
        if (UnderGlow.Tag as bool? != glow)
        {
            UnderGlow.Tag = glow;
            Motion.Fade(UnderGlow, glow ? 0.75 : 0, 0.4);
        }

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
            size = new Size(PanelW, NotchH + Chrome + sections);
        }
        else if (hud) size = new Size(NotchW + Side * 2 + 34, NotchH);
        else if (lyric) size = new Size(380, NotchH);
        else size = new Size(NotchW + (extended ? Side * 2 : 0), NotchH);

        if (size != _target)
        {
            var (response, damping) = _expanded != _targetExpanded ? (0.42, 0.76)   // opening or closing
                : _expanded ? (0.38, 0.8)                                             // sections come and go
                : (0.4, 0.8);                                                         // the pill grows its sides
            _target = size;
            _targetExpanded = _expanded;
            Motion.Spring(NotchHost, WidthProperty, size.Width, response, damping);
            Motion.Spring(NotchHost, HeightProperty, size.Height, response, damping);
        }
    }

    /// <summary>Opens or closes from everything that wants it open (Mac: NotchController.tick).</summary>
    private void UpdateExpanded()
    {
        if (_menuOpen) return;  // the mouse is on the menu, not the notch: hold still
        var want = _hoverOpen || _dragging || _toast != null || (_peeking && _media.HasMedia);
        if (_hiddenForFullScreen) want = false;
        SetExpanded(want);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (expanded)
        {
            _hud = false;
            if (_shelf.Prune()) _shelfKey = null;
        }
        UpdateLift();

        // The outline: corners flare and round more when open (Mac: 6/10 → 12/32).
        BeginAnimation(TopRadiusProperty, SpringEase.To(expanded ? 12 : 6, 0.42, 0.76));
        BeginAnimation(BottomRadiusProperty, SpringEase.To(expanded ? 32 : 10, 0.42, 0.76));
        Motion.Fade(Backdrop, expanded ? 1 : 0, expanded ? 0.3 : 0.25);
        Motion.Fade(CollapsedBlack, expanded ? 0 : 1, expanded ? 0.3 : 0.25);
        Motion.Fade(RimGlow, expanded ? 1 : 0, 0.3);
        Motion.Fade(RimLine, expanded ? 1 : 0, 0.3);

        if (expanded) ShowExpandedContent(); else HideExpandedContent();
        Refresh();
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
            var blur = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
            Expanded.Effect = blur;
            var unblur = SpringEase.To(0, r, d, delay);
            unblur.Completed += (_, _) => { if (Expanded.Effect == blur) Expanded.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
            Expanded.BeginAnimation(OpacityProperty, SpringEase.To(1, r, d, delay));
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
            Expanded.BeginAnimation(OpacityProperty, SpringEase.To(1, 0.4, 0.8, 0.05));
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

    /// <summary>A halo in the album's color, then the drop shadow (two layers when elevated: close and ambient).</summary>
    private void UpdateShadows(bool showMedia)
    {
        var lifted = Settings.Current.Lifted;
        var glass = Settings.Current.Glass;
        var haloOpacity = _expanded ? (glass ? 0.3 : lifted ? 0.22 : 0.18) : 0;
        var near = _expanded || _lift;
        var ambient = lifted && _expanded;
        var state = (haloOpacity, near, ambient, _lift, showMedia, _accent);
        if (Halo.Tag is ValueTuple<double, bool, bool, bool, bool, Color> old && old.Equals(state)) return;
        Halo.Tag = state;

        HaloEffect.BlurRadius = lifted ? 64 : 48;
        HaloEffect.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(showMedia ? _accent : Colors.White, TimeSpan.FromSeconds(0.5)));
        Motion.Fade(Halo, haloOpacity, 0.35);
        NearEffect.BlurRadius = _expanded ? (lifted ? 20 : 36) : 12;
        NearEffect.ShadowDepth = _expanded ? (lifted ? 5 : 8) : 3;
        Motion.Fade(NearShadow, near ? 1 : 0, 0.3);
        Motion.Fade(AmbientShadow, ambient ? 1 : 0, 0.35);
    }

    /// <summary>The look of each style (Settings → Appearance, or right-click the notch).</summary>
    private void ApplyStyle()
    {
        var style = Settings.Current.Style;
        var glass = style == "glass";
        var elevated = style == "elevated";
        // No real backdrop blur on a layered window: Glass is a more see-through surface instead.
        BackdropFill.Fill = glass ? Brush(Color.FromRgb(0x1C, 0x1C, 0x22), 0.78) : Brush(Color.FromRgb(0x1C, 0x1C, 0x1F), 0.95);
        BackdropDim.Fill = Black(glass ? 0.22 : 0.28);
        BackdropArt.Opacity = glass ? 0.55 : 0.4;
        ElevatedSheen.Visibility = elevated ? Visibility.Visible : Visibility.Collapsed;
        GlassSheen.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        RimGlow.Stroke = White(glass ? 0.16 : 0.10);
        RimMid.Color = Color.FromArgb((byte)((glass ? 0.16 : 0.10) * 255), 255, 255, 255);
        RimEnd.Color = Color.FromArgb((byte)((glass ? 0.45 : 0.28) * 255), 255, 255, 255);

        _bigIndicator.Children.Clear();
        _bigIndicator.Children.Add(glass ? _bigWave : _bigBars);
        _pillBars.SetGlow(elevated, _accent);
        _bigBars.SetGlow(elevated, _accent);

        // Surfaces inside the panel: plain, a soft lit pod (Elevated) or a pane of glass.
        Surface(_controls, style, fallback: 0, elevated: 0.05);
        Surface(_shelfWell, style, fallback: 0.05, elevated: 0.05);
        _controls.Padding = new Thickness(style == "basic" ? 0 : 6, 0, style == "basic" ? 0 : 6, 0);
        _texts.Effect = glass ? new DropShadowEffect { Color = Colors.Black, Opacity = 0.45, BlurRadius = 4, ShadowDepth = 1, Direction = 270 } : null;
        _shownPlaying = null;
        Halo.Tag = null;
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
                b.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 12, ShadowDepth = 3, Direction = 270 };
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
        _shelfKey = null;
        _sourcesKey = null;
        _trash.ToolTip = L("clearShelf");
    }

    // MARK: - Cover and album color

    private void SetCover(BitmapSource? cover)
    {
        if (ReferenceEquals(cover, _shownCover)) return;
        _shownCover = cover;
        var accent = cover == null ? Colors.White : AlbumColor(cover);
        if (accent != _accent)
        {
            _accent = accent;
            // The album's color fades over (Mac: ease-in-out 0.5 s).
            _accentBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromSeconds(0.5)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _pillWave.Color = _bigWave.Color = accent;
            Waves.Color = accent;
            _lyric.Foreground = _accentBrush;
            _coverShadow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(accent, TimeSpan.FromSeconds(0.5)));
            UnderGlow.Fill = _accentBrush;
            _pillBars.SetGlow(Settings.Current.Style == "elevated", accent);
            _bigBars.SetGlow(Settings.Current.Style == "elevated", accent);
        }

        Brush brush = cover == null ? White(0.1) : Frozen(new ImageBrush(cover) { Stretch = Stretch.UniformToFill });
        _pillCover.Background = brush;
        _cover.Background = brush;
        _cover.Child = cover == null ? Icons.Glyph("\uEC4F", 18, White(0.5)) : null;
        BackdropArt.Source = cover;
        // The new cover fades in over the old one (Mac: 0.3 s).
        foreach (var e in new UIElement[] { _cover, _pillCover }) e.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.3, from: 0.3));
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
        _coverShadow.BeginAnimation(DropShadowEffect.OpacityProperty, Motion.Ease(lifted && playing ? 0.5 : 0.35, 0.4));
        _coverShadow.BlurRadius = lifted && playing ? 28 : 20;
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
        _trackFill.Background = White(thick ? 0.95 : 0.7);
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
        _trackFill.Width = duration > 0 ? Math.Max(0, _track.ActualWidth * position / duration) : 0;
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

    private void ShowHud()
    {
        if (_toast != null || _hiddenForFullScreen) return;
        _hud = true;
        _hudTimer.Stop();
        _hudTimer.Start();
        UpdateVolume();
        Refresh();
    }

    private void UpdateVolume()
    {
        var level = _volume.Muted ? 0 : _volume.Level;
        _volumeRow.Visibility = _volume.Available ? Visibility.Visible : Visibility.Hidden;
        _volumeFill.Width = _volumeTrack.Width * level;
        _hudFill.BeginAnimation(ScaleTransform.ScaleXProperty, SpringEase.To(level, 0.25, 0.85));
        var glyph = _volume.Muted || level <= 0 ? "\uE74F" : level < 0.34 ? "\uE993" : level < 0.67 ? "\uE994" : "\uE995";
        _volumeButton.Icon = Icons.Glyph(glyph, 15, White(0.7));
        _hudIcon.Text = glyph;
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
        Motion.Spring(BumpScale, ScaleTransform.ScaleXProperty, 1.07, 0.22, 0.32);
        Motion.Spring(BumpScale, ScaleTransform.ScaleYProperty, 1.16, 0.22, 0.32);
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
    }

    // MARK: - Copy text from the screen (Ctrl+Shift+2)

    private const int WM_HOTKEY = 0x0312, HotKeyId = 1;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_2 = 0x32;
    private bool _hotKeyOn;

    private void UpdateHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var want = Settings.Current.OcrEnabled;
        if (want == _hotKeyOn) return;
        if (want)
        {
            _hotKeyOn = RegisterHotKey(handle, HotKeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_2);
            if (!_hotKeyOn) ShowToast("\uE7BA", L("shortcutTaken"), "", ok: false);
        }
        else
        {
            UnregisterHotKey(handle, HotKeyId);
            _hotKeyOn = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
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

    /// <summary>Step aside while a game, a video or a presentation fills the screen.</summary>
    private void CheckFullScreen()
    {
        var hide = FullScreen.IsActive(new WindowInteropHelper(this).Handle) && !_dragging;
        if (hide == _hiddenForFullScreen) return;
        _hiddenForFullScreen = hide;
        if (hide)
        {
            _hoverOpen = _peeking = _hud = false;
            SetExpanded(false);
            NotchHost.Visibility = Visibility.Hidden;
        }
        else
        {
            NotchHost.Visibility = Visibility.Visible;
            NotchHost.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.25, from: 0));
        }
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

        root.MouseEnter += (_, _) => { close.Visibility = Visibility.Visible; if (!selected) bg.Background = White(0.1); };
        root.MouseLeave += (_, _) => { close.Visibility = Visibility.Collapsed; if (!selected) bg.Background = White(0); };

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

        var menu = new ContextMenu();
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; _leaveDelay.Start(); };
        root.ContextMenu = menu;
        root.ContextMenuOpening += (_, _) => FillShelfMenu(menu, entry);
        return root;
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
        bool In(double left, double top, double right, double bottom)
        {
            if (PresentationSource.FromVisual(Root) == null) return false;
            var a = Root.PointToScreen(new Point(left, top));
            var b = Root.PointToScreen(new Point(right, bottom));
            return cursor.X >= a.X && cursor.X <= b.X && cursor.Y >= a.Y && cursor.Y <= b.Y;
        }
        var hot = _expanded || down || In(-80, 0, Root.Width + 80, 260);
        _dragPoll.Interval = TimeSpan.FromMilliseconds(hot ? 33 : 250);

        // Only for a real drag: held a moment and moved. A click or double-click near the top
        // (a tab, a title bar) never meets the catcher.
        if (down && _pressStart == null) _pressStart = (DateTime.UtcNow, cursor);
        if (!down) _pressStart = null;
        var dragged = _pressStart is var (at, from)
                      && (DateTime.UtcNow - at).TotalMilliseconds > 150
                      && Math.Abs(cursor.X - from.X) + Math.Abs(cursor.Y - from.Y) > 12;
        var catcher = dragged && !_draggingOut && !_hiddenForFullScreen && In(20, 0, Root.Width - 20, 220);
        DragCatcher.Visibility = catcher || _dragging ? Visibility.Visible : Visibility.Collapsed;
        if (!down && _dragging) EndDrag();
    }

    private (DateTime At, Point From)? _pressStart;

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
        var inCatcher = DragCatcher.IsVisible && p.X >= 20 && p.X <= Root.Width - 20 && p.Y >= 0 && p.Y <= 220;
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
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem Header(string text) => new()
    {
        Header = text, IsEnabled = false, Style = (Style)Application.Current.Resources["MenuHeader"],
    };

    /// <summary>The notch's right-click menu, built when it opens (same sections as the Mac's).</summary>
    private void BuildMenu()
    {
        var s = Settings.Current;
        var menu = NotchHost.ContextMenu!;
        menu.Items.Clear();
        if (s.OcrEnabled)
        {
            menu.Items.Add(Item(L("copyTextFromScreen"), "\uE8C8", () => Dispatcher.BeginInvoke(CopyTextFromScreen), gesture: "Ctrl+Shift+2"));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Header(L("appearance")));
        foreach (var (style, glyph) in new[] { ("basic", "\uE91F"), ("elevated", "\uE81E"), ("glass", "\uEB42") })
            menu.Items.Add(Item(L("style." + style), glyph, () => { s.Style = style; s.Save(); }, s.Style == style));
        menu.Items.Add(new Separator());

        menu.Items.Add(Header(L("behavior")));
        menu.Items.Add(Item(L("peekOnTrack"), "\uEC4F", () => { s.PeekOnTrackChange = !s.PeekOnTrackChange; s.Save(); }, s.PeekOnTrackChange));
        menu.Items.Add(Item(L("volumeHud"), "\uE995", () => { s.VolumeHud = !s.VolumeHud; s.Save(); }, s.VolumeHud));
        menu.Items.Add(Item(L("launchAtLogin"), "\uE706", () => Settings.LaunchAtLogin = !Settings.LaunchAtLogin, Settings.LaunchAtLogin));
        var addons = new MenuItem { Header = L("addons"), Icon = Icons.Glyph("\uEA86", 14, Brushes.White) };
        addons.Items.Add(Item(L("addonOCR"), "\uE8C8", () => { s.OcrEnabled = !s.OcrEnabled; s.Save(); }, s.OcrEnabled));
        addons.Items.Add(Item(L("addonLyrics"), "\uE8D6", () => { s.LyricsEnabled = !s.LyricsEnabled; s.Save(); }, s.LyricsEnabled));
        menu.Items.Add(addons);
        menu.Items.Add(new Separator());

        if (_shelf.Items.Count > 0)
            menu.Items.Add(Item(L("clearShelf"), "\uE74D", () => RemoveFromShelf(_shelf.Items.ToList())));
        menu.Items.Add(Item(L("settings"), "\uE713", SettingsWindow.ShowOrFocus));
        menu.Items.Add(Item(L("quit"), "\uE7E8", () => Application.Current.Shutdown()));
    }
}
