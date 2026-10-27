using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>
/// The notch: a black pill hanging from the top of the screen. Hover it for music controls
/// and the shelf; drop files, links or text on it to keep them.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MediaService _media = new();
    private readonly Shelf _shelf = Shelf.Load();
    private readonly HashSet<ShelfEntry> _selected = new();

    private readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _leaveDelay = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private readonly DispatcherTimer _progressTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _barsTick = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private bool _expanded;
    private bool _dragging;      // something is being dragged over the notch
    private bool _draggingOut;   // ...or out of our own shelf (ignored as a drop)
    private bool _menuOpen;
    private ShelfEntry? _pressed;
    private Point _pressPoint;
    private readonly List<Rectangle> _bars = new();
    private readonly Random _random = new();
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public MainWindow()
    {
        InitializeComponent();

        // Four little bars, twice: in the pill and in the panel header.
        foreach (var panel in new[] { PillBars, BigBars })
        {
            for (var i = 0; i < 4; i++)
            {
                var bar = new Rectangle
                {
                    Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(1, 0, 1, 0),
                    Fill = Brushes.White, RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 0.35),
                };
                panel.Children.Add(bar);
                _bars.Add(bar);
            }
        }

        Notch.MouseEnter += (_, _) => { _leaveDelay.Stop(); if (!_expanded) _hoverDelay.Start(); };
        Notch.MouseLeave += (_, _) => { _hoverDelay.Stop(); _leaveDelay.Start(); };
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); if (Notch.IsMouseOver) SetExpanded(true); };
        _leaveDelay.Tick += (_, _) =>
        {
            _leaveDelay.Stop();
            if (!Notch.IsMouseOver && !_dragging && !_menuOpen) SetExpanded(false);
        };
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (!Notch.IsMouseOver && !_dragging && !_menuOpen) SetExpanded(false);
        };
        _progressTick.Tick += (_, _) => UpdateProgress();
        _barsTick.Tick += (_, _) => AnimateBars();

        Notch.DragEnter += OnDragEnter;
        Notch.DragOver += (_, e) => { e.Effects = _draggingOut ? DragDropEffects.None : DragDropEffects.Copy; e.Handled = true; };
        Notch.DragLeave += OnDragLeave;
        Notch.Drop += OnDrop;

        PrevButton.Click += (_, _) => _media.Previous();
        NextButton.Click += (_, _) => _media.Next();
        PlayButton.Click += (_, _) => _media.PlayPause();
        TrashButton.Click += (_, _) =>
        {
            if (_selected.Count > 0) _shelf.Remove(_selected); else _shelf.Clear();
            _selected.Clear();
            Refresh();
        };
        Track.MouseLeftButtonDown += (_, e) =>
        {
            if (_media.Duration <= 0) return;
            _media.Seek(e.GetPosition(Track).X / Track.ActualWidth * _media.Duration);
            UpdateProgress();
        };
        BigCover.MouseLeftButtonUp += (_, _) => _media.PlayPause();

        Notch.ContextMenu = new ContextMenu();
        Notch.ContextMenuOpening += (_, _) => BuildMenu();
        Notch.ContextMenu.Opened += (_, _) => _menuOpen = true;
        Notch.ContextMenu.Closed += (_, _) => { _menuOpen = false; _leaveDelay.Start(); };

        _media.Changed += Refresh;
        _media.TrackChanged += OnTrackChanged;

        Loaded += async (_, _) =>
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
            ApplyLanguage();
            Refresh();
            await _media.StartAsync();
        };
    }

    // Never take focus away from the app being used, and stay out of Alt+Tab.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    // MARK: - Layout

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (expanded) _progressTick.Start(); else _progressTick.Stop();
        Refresh();
    }

    private void Refresh()
    {
        var hasMedia = _media.HasMedia;
        var hasShelf = _shelf.Items.Count > 0;
        var showShelf = hasShelf || _dragging;
        var showMedia = hasMedia && !_dragging;

        Collapsed.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        Expanded.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;

        // Pill
        PillCover.Visibility = PillTitle.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        PillBars.Visibility = hasMedia && _media.Playing ? Visibility.Visible : Visibility.Collapsed;
        PillShelf.Visibility = !hasMedia && hasShelf ? Visibility.Visible : Visibility.Collapsed;
        PillShelfCount.Text = _shelf.Items.Count.ToString();
        PillTitle.Text = _media.Title;

        // Panel
        MediaSection.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        ShelfSection.Visibility = showShelf ? Visibility.Visible : Visibility.Collapsed;
        Hairline.Visibility = showMedia && showShelf ? Visibility.Visible : Visibility.Collapsed;
        EmptySection.Visibility = !showMedia && !showShelf ? Visibility.Visible : Visibility.Collapsed;
        DropZone.Visibility = _dragging ? Visibility.Visible : Visibility.Collapsed;
        ShelfWell.Visibility = _dragging ? Visibility.Collapsed : Visibility.Visible;

        TitleText.Text = _media.Title;
        ArtistText.Text = _media.Artist;
        ArtistText.Visibility = _media.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Content = _media.Playing ? "" : "";
        SetCover(_media.Cover);
        RebuildSources();
        RebuildShelf();
        UpdateProgress();

        if (_media.Playing && hasMedia) _barsTick.Start(); else StopBars();

        double width, height;
        if (_expanded)
        {
            width = 460;
            height = 26 + (showMedia ? 150 : 0) + (showShelf ? 84 : 0) + (showMedia && showShelf ? 21 : 0)
                     + (!showMedia && !showShelf ? 58 : 0);
        }
        else if (hasMedia) (width, height) = (300, 32);
        else if (hasShelf) (width, height) = (170, 28);
        else (width, height) = (150, 20);
        AnimateSize(width, height);
    }

    private void AnimateSize(double width, double height)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        Notch.CornerRadius = _expanded ? new CornerRadius(0, 0, 26, 26) : new CornerRadius(0, 0, 14, 14);
    }

    private void UpdateProgress()
    {
        var duration = _media.Duration;
        if (duration <= 0 || !_expanded) return;
        var position = _media.LivePosition;
        TrackFill.Width = Math.Max(0, Track.ActualWidth * position / duration);
        ElapsedText.Text = Clock(position);
        RemainingText.Text = "-" + Clock(duration - position);
    }

    private static string Clock(double seconds)
    {
        var t = (int)Math.Max(0, seconds);
        return $"{t / 60}:{t % 60:00}";
    }

    // MARK: - Cover and album color

    private void SetCover(BitmapSource? cover)
    {
        Brush brush = cover == null
            ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
            : new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
        PillCover.Background = brush;
        BigCover.Background = brush;

        var accent = cover == null ? Colors.White : AlbumColor(cover);
        var accentBrush = new SolidColorBrush(accent);
        foreach (var bar in _bars) bar.Fill = accentBrush;
        // The panel picks up a tint of the cover, fading into black.
        Notch.Background = _expanded && cover != null
            ? new LinearGradientBrush(Color.FromArgb(0xF5, (byte)(accent.R * 0.3), (byte)(accent.G * 0.3), (byte)(accent.B * 0.3)),
                                      Color.FromArgb(0xF5, 0x0B, 0x0B, 0x0F), 90)
            : new SolidColorBrush(Color.FromArgb(0xF5, 0x0B, 0x0B, 0x0F));
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

    // MARK: - Bars and bounce

    private void AnimateBars()
    {
        foreach (var bar in _bars)
        {
            if (bar.RenderTransform is not ScaleTransform scale) continue;
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3 + _random.NextDouble() * 0.7,
                TimeSpan.FromMilliseconds(240)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
    }

    private void StopBars()
    {
        _barsTick.Stop();
        foreach (var bar in _bars)
            if (bar.RenderTransform is ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleY = 0.35; }
    }

    /// <summary>Quick squash-and-stretch, like the island reacting to something new.</summary>
    private void Bump()
    {
        var ease = new ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = EasingMode.EaseOut };
        NotchScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        NotchScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.16, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
    }

    private void OnTrackChanged()
    {
        Bump();
        if (!Settings.Current.PeekOnTrackChange || _expanded) return;
        SetExpanded(true);
        _peekTimer.Start();
    }

    // MARK: - Players

    private void RebuildSources()
    {
        Sources.Children.Clear();
        var sessions = _media.Sessions;
        if (sessions.Count < 2) return;
        foreach (var s in sessions)
        {
            var id = s.SourceAppUserModelId;
            var selected = id == _media.SourceId;
            var button = new Button
            {
                Content = MediaService.AppName(id),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(selected ? (byte)0xFF : (byte)0x8C, 0xFF, 0xFF, 0xFF)),
                Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)0x2E : (byte)0x0F, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(3, 0, 0, 0),
                Cursor = Cursors.Hand,
                Focusable = false,
            };
            button.Click += (_, _) => _media.Pin(id);
            Sources.Children.Add(button);
        }
    }

    // MARK: - Shelf

    private void RebuildShelf()
    {
        ShelfItems.Children.Clear();
        foreach (var entry in _shelf.Items) ShelfItems.Children.Add(ShelfItemView(entry));
        TrashButton.ToolTip = _selected.Count > 0 ? L("removeSelected") : L("clearShelf");
    }

    private FrameworkElement ShelfItemView(ShelfEntry entry)
    {
        var selected = _selected.Contains(entry);
        var preview = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center };
        var image = entry.Kind == "file" ? Thumbnail(entry.Value) : null;
        if (image != null)
        {
            preview.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        else
        {
            preview.Background = new SolidColorBrush(entry.Kind == "link" ? Color.FromRgb(0x3A, 0x7B, 0xF0) : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
            preview.Child = new TextBlock
            {
                FontFamily = IconFont,
                Text = entry.Kind switch { "link" => "", "text" => "", _ => "" },
                FontSize = 18, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(new TextBlock
        {
            Text = entry.Title, FontSize = 9.5, Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
        });

        var box = new Border
        {
            Width = 66, Height = 70, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4),
            CornerRadius = new CornerRadius(10), Child = stack, Cursor = Cursors.Hand,
            ToolTip = entry.Value,
            Background = new SolidColorBrush(selected ? Color.FromArgb(0x59, 0x35, 0x84, 0xE4) : Colors.Transparent),
            BorderBrush = new SolidColorBrush(selected ? Color.FromArgb(0xE6, 0x35, 0x84, 0xE4) : Colors.Transparent),
            BorderThickness = new Thickness(1.5),
        };

        // Click selects, double-click opens, dragging carries the item (or the whole selection) out.
        box.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Open(entry); _pressed = null; return; }
            _pressed = entry;
            _pressPoint = e.GetPosition(this);
        };
        box.MouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != entry) return;
            _pressed = null;
            if (!_selected.Remove(entry)) _selected.Add(entry);
            RebuildShelf();
        };
        box.MouseMove += (_, e) =>
        {
            if (_pressed != entry || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _pressPoint.X) + Math.Abs(p.Y - _pressPoint.Y) < 5) return;
            _pressed = null;
            DragOut(entry, box);
        };

        var menu = new ContextMenu();
        menu.Items.Add(Item(entry.Kind == "text" ? L("copyText") : L("open"), () => Open(entry)));
        if (entry.Kind == "file")
            menu.Items.Add(Item(L("showInExplorer"), () => Process.Start("explorer.exe", $"/select,\"{entry.Value}\"")));
        menu.Items.Add(Item(L("copy"), () => CopyToClipboard(Group(entry))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L("removeFromShelf"), () =>
        {
            var group = Group(entry);
            _shelf.Remove(group);
            _selected.ExceptWith(group);
            Refresh();
        }));
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; _leaveDelay.Start(); };
        box.ContextMenu = menu;
        return box;
    }

    private static BitmapSource? Thumbnail(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff")) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 80;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
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

    private static void CopyToClipboard(IReadOnlyCollection<ShelfEntry> entries) => Clipboard.SetDataObject(DataFor(entries), true);

    private static void Open(ShelfEntry entry)
    {
        if (entry.Kind == "text") { Clipboard.SetText(entry.Value); return; }
        try { Process.Start(new ProcessStartInfo(entry.Value) { UseShellExecute = true }); } catch { }
    }

    // MARK: - Dropping things in

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (_draggingOut) return;
        _dragging = true;
        DropText.Text = L("releaseNow");
        DropRect.Stroke = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        _leaveDelay.Stop();
        SetExpanded(true);
        Refresh();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only react when really outside.
        var p = e.GetPosition(Notch);
        if (p.X >= 0 && p.Y >= 0 && p.X <= Notch.ActualWidth && p.Y <= Notch.ActualHeight) return;
        _dragging = false;
        Refresh();
        _leaveDelay.Start();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragging = false;
        if (_draggingOut) { Refresh(); return; }
        var added = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths) added |= _shelf.Add(new ShelfEntry("file", path));
        }
        else if (e.Data.GetDataPresent(DataFormats.UnicodeText) && e.Data.GetData(DataFormats.UnicodeText) is string text
                 && text.Trim().Length > 0)
        {
            var t = text.Trim();
            var isLink = Uri.TryCreate(t, UriKind.Absolute, out var uri)
                         && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !t.Contains(' ');
            added = _shelf.Add(isLink ? new ShelfEntry("link", t) : new ShelfEntry("text", text));
        }
        if (added) Bump();
        Refresh();
        _leaveDelay.Start();
    }

    // MARK: - Menu and language

    private void ApplyLanguage()
    {
        EmptyTitle.Text = L("nothingPlaying");
        EmptyHint.Text = L("dragHint");
        DropText.Text = L("dropHere");
    }

    private static MenuItem Item(string header, Action action, bool? isChecked = null)
    {
        var item = new MenuItem { Header = header };
        if (isChecked is bool c) { item.IsCheckable = true; item.IsChecked = c; }
        item.Click += (_, _) => action();
        return item;
    }

    private void BuildMenu()
    {
        var menu = Notch.ContextMenu!;
        menu.Items.Clear();

        var language = new MenuItem { Header = L("language") };
        foreach (var code in Strings.Languages)
        {
            language.Items.Add(Item(LanguageName(code), () =>
            {
                Settings.Current.Language = code;
                Settings.Current.Save();
                ApplyLanguage();
                Refresh();
            }, Settings.Current.Language == code));
        }
        menu.Items.Add(language);
        menu.Items.Add(Item(L("peekOnTrack"), () =>
        {
            Settings.Current.PeekOnTrackChange = !Settings.Current.PeekOnTrackChange;
            Settings.Current.Save();
        }, Settings.Current.PeekOnTrackChange));
        menu.Items.Add(Item(L("launchAtLogin"), () => Settings.LaunchAtLogin = !Settings.LaunchAtLogin, Settings.LaunchAtLogin));
        if (_shelf.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(L("clearShelf"), () => { _shelf.Clear(); _selected.Clear(); Refresh(); }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L("quit"), () => Application.Current.Shutdown()));
    }
}
