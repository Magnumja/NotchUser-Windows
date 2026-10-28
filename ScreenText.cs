using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Media.Ocr;
using static NotchUser.Strings;
using WinImaging = Windows.Graphics.Imaging;

namespace NotchUser;

/// <summary>
/// Copy text from the screen: freeze the screen, drag a box over the text, and Windows'
/// own OCR reads it. Nothing is saved; the snapshot only lives in memory until it's read.
/// </summary>
public static class ScreenText
{
    private static bool _busy;

    /// <returns>The text found, "" when there was none, or null when the user cancelled.</returns>
    public static async Task<string?> CaptureAsync()
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            var screen = CaptureVirtualScreen();
            var region = SelectionWindow.Pick(screen);
            if (region == null) return null;
            return await RecognizeAsync(region);
        }
        finally
        {
            _busy = false;
            App.TrimMemory();  // the full-screen snapshot was big
        }
    }

    // MARK: Capture (all monitors, in real pixels)

    private static BitmapSource CaptureVirtualScreen()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77), w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, w, h);
        var old = SelectObject(memDc, bitmap);
        BitBlt(memDc, 0, 0, w, h, screenDc, x, y, 0x00CC0020 | 0x40000000);  // SRCCOPY | CAPTUREBLT
        SelectObject(memDc, old);
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // MARK: Recognition

    private static async Task<string> RecognizeAsync(BitmapSource region)
    {
        // OCR does better on bigger text, but has a size limit.
        var scale = region.PixelHeight < 60 ? 2.0 : 1.0;
        var max = OcrEngine.MaxImageDimension;
        scale = Math.Min(scale, Math.Min((double)max / region.PixelWidth, (double)max / region.PixelHeight));
        BitmapSource image = Math.Abs(scale - 1) > 0.01 ? new TransformedBitmap(region, new ScaleTransform(scale, scale)) : region;

        using var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        png.Position = 0;

        var decoder = await WinImaging.BitmapDecoder.CreateAsync(png.AsRandomAccessStream());
        using var bitmap = await decoder.GetSoftwareBitmapAsync(WinImaging.BitmapPixelFormat.Bgra8, WinImaging.BitmapAlphaMode.Premultiplied);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (engine == null) return "";
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
    }

    // MARK: Win32

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}

/// <summary>
/// Full-screen overlay showing the frozen screen, dimmed; dragging cuts a clear box out of
/// the dim. Returns that part of the snapshot, or null on Esc.
/// </summary>
internal sealed class SelectionWindow : Window
{
    private readonly BitmapSource _screen;
    private readonly Canvas _canvas = new() { Cursor = Cursors.Cross, Background = Brushes.Transparent };
    private readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x73, 0, 0, 0)) };
    private readonly Rectangle _box = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed };
    private Point _start;
    private bool _selecting;
    private BitmapSource? _result;

    public static BitmapSource? Pick(BitmapSource screen)
    {
        var w = new SelectionWindow(screen);
        w.ShowDialog();
        return w._result;
    }

    private SelectionWindow(BitmapSource screen)
    {
        _screen = screen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;

        var root = new Grid();
        root.Children.Add(new Image { Source = screen, Stretch = Stretch.Fill });
        root.Children.Add(_dim);
        _canvas.Children.Add(_box);
        root.Children.Add(_canvas);
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x10, 0x10, 0x14)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 7, 14, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 60, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = L("selectText"), Foreground = Brushes.White, FontSize = 13 },
        };
        root.Children.Add(hint);
        Content = root;

        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _selecting = true;
            _canvas.CaptureMouse();
            hint.Visibility = Visibility.Collapsed;
        };
        _canvas.MouseMove += (_, e) => { if (_selecting) Update(e.GetPosition(_canvas)); };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (!_selecting) return;
            _selecting = false;
            _canvas.ReleaseMouseCapture();
            Finish(Rect(_start, e.GetPosition(_canvas)));
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => { Update(_start); Activate(); Focus(); };
    }

    // Cover every monitor, in real pixels.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, new IntPtr(-1), GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79), 0x0040);
    }

    private static Rect Rect(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void Update(Point current)
    {
        var whole = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (!_selecting) { _dim.Data = whole; return; }
        var r = Rect(_start, current);
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(r));
        _box.Visibility = Visibility.Visible;
        Canvas.SetLeft(_box, r.X);
        Canvas.SetTop(_box, r.Y);
        _box.Width = r.Width;
        _box.Height = r.Height;
    }

    private void Finish(Rect r)
    {
        // Map from window units to snapshot pixels (works whatever the display scaling).
        var sx = _screen.PixelWidth / ActualWidth;
        var sy = _screen.PixelHeight / ActualHeight;
        var px = new Int32Rect((int)(r.X * sx), (int)(r.Y * sy), (int)(r.Width * sx), (int)(r.Height * sy));
        px.Width = Math.Min(px.Width, _screen.PixelWidth - px.X);
        px.Height = Math.Min(px.Height, _screen.PixelHeight - px.Y);
        if (px.Width >= 4 && px.Height >= 4)
        {
            var crop = new CroppedBitmap(_screen, px);
            crop.Freeze();
            _result = crop;
        }
        Close();
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
