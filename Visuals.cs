using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NotchUser;

/// <summary>The colors macOS uses in dark mode, so both versions look the same.</summary>
public static class MacColors
{
    public static readonly Color Accent = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Pink = Color.FromRgb(0xFF, 0x37, 0x5F);
    public static readonly Color Purple = Color.FromRgb(0xBF, 0x5A, 0xF2);
    public static readonly Color Gray = Color.FromRgb(0x8E, 0x8E, 0x93);

    public static SolidColorBrush White(double opacity) => Frozen(Color.FromArgb((byte)Math.Round(opacity * 255), 255, 255, 255));
    public static SolidColorBrush Black(double opacity) => Frozen(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
    public static SolidColorBrush Brush(Color c, double opacity = 1) => Frozen(Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B));

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static SolidColorBrush Frozen(Color c) => Frozen(new SolidColorBrush(c));
}

/// <summary>
/// The notch outline, the same curve as the Mac's NotchShape: the top corners flare outward
/// (so it melts into the top edge of the screen) and the bottom corners are round.
/// </summary>
public static class NotchShape
{
    public static Geometry Create(double w, double h, double topR, double botR)
    {
        w = Math.Max(w, 2 * topR + 2 * botR);
        h = Math.Max(h, topR + botR);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.QuadraticBezierTo(new Point(topR, 0), new Point(topR, topR), true, true);
            c.LineTo(new Point(topR, h - botR), true, true);
            c.QuadraticBezierTo(new Point(topR, h), new Point(topR + botR, h), true, true);
            c.LineTo(new Point(w - topR - botR, h), true, true);
            c.QuadraticBezierTo(new Point(w - topR, h), new Point(w - topR, h - botR), true, true);
            c.LineTo(new Point(w - topR, topR), true, true);
            c.QuadraticBezierTo(new Point(w - topR, 0), new Point(w, 0), true, true);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Shapes drawn like SF Symbols (rounded, filled), for the playback buttons.</summary>
public static class Icons
{
    public static readonly Geometry Play = MacColors.Frozen(Geometry.Parse(
        "M 1,1.6 C 1,0.4 2.3,-0.3 3.3,0.3 L 16.4,8.2 C 17.4,8.8 17.4,10.2 16.4,10.8 L 3.3,18.7 C 2.3,19.3 1,18.6 1,17.4 Z"));
    public static readonly Geometry Pause = MacColors.Frozen(Geometry.Parse(
        "M 1.5,0 H 4.7 A 1.5,1.5 0 0 1 6.2,1.5 V 17.5 A 1.5,1.5 0 0 1 4.7,19 H 1.5 A 1.5,1.5 0 0 1 0,17.5 V 1.5 A 1.5,1.5 0 0 1 1.5,0 Z " +
        "M 10.3,0 H 13.5 A 1.5,1.5 0 0 1 15,1.5 V 17.5 A 1.5,1.5 0 0 1 13.5,19 H 10.3 A 1.5,1.5 0 0 1 8.8,17.5 V 1.5 A 1.5,1.5 0 0 1 10.3,0 Z"));
    public static readonly Geometry Forward = MacColors.Frozen(Geometry.Parse(
        "M 0,1.4 C 0,0.3 1.2,-0.3 2.1,0.3 L 11.2,6.2 C 12,6.7 12,7.8 11.2,8.3 L 2.1,14.2 C 1.2,14.8 0,14.2 0,13.1 Z " +
        "M 11.6,1.4 C 11.6,0.3 12.8,-0.3 13.7,0.3 L 22.8,6.2 C 23.6,6.7 23.6,7.8 22.8,8.3 L 13.7,14.2 C 12.8,14.8 11.6,14.2 11.6,13.1 Z"));
    public static readonly Geometry Backward = MacColors.Frozen(new PathGeometry(PathGeometry.CreateFromGeometry(Forward).Figures,
        FillRule.Nonzero, new MatrixTransform(-1, 0, 0, 1, 23.6, 0)));

    /// <summary>A filled shape sized to <paramref name="height"/>, keeping its proportions.</summary>
    public static Path Shape(Geometry g, double height, Brush fill) => new()
    {
        Data = g, Fill = fill, Height = height, Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    public static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Glyph(string glyph, double size, Brush color) => new()
    {
        Text = glyph, FontFamily = Glyphs, FontSize = size, Foreground = color,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>Shared animation helpers (60 fps, SwiftUI-like springs).</summary>
public static class Motion
{
    public static DoubleAnimation Ease(double to, double seconds, double delay = 0, double? from = null, IEasingFunction? ease = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        if (from is double f) a.From = f;
        Timeline.SetDesiredFrameRate(a, 60);
        return a;
    }

    public static void Spring(UIElement e, DependencyProperty p, double to, double response, double damping) =>
        e.BeginAnimation(p, SpringEase.To(to, response, damping));

    public static void Spring(Animatable a, DependencyProperty p, double to, double response, double damping) =>
        a.BeginAnimation(p, SpringEase.To(to, response, damping));

    public static void Fade(UIElement e, double to, double seconds, double delay = 0) => e.BeginAnimation(UIElement.OpacityProperty, Ease(to, seconds, delay));

    /// <summary>A transform group with a scale and a translation, created once per element.</summary>
    public static (ScaleTransform Scale, TranslateTransform Move) Transforms(UIElement e, Point? origin = null)
    {
        if (e.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] }) return (s, t);
        var scale = new ScaleTransform();
        var move = new TranslateTransform();
        e.RenderTransform = new TransformGroup { Children = { scale, move } };
        e.RenderTransformOrigin = origin ?? new Point(0.5, 0.5);
        return (scale, move);
    }

    /// <summary>Buttons sink a little while pressed and spring back (Mac PressStyle: 0.86, response 0.25).</summary>
    public static void AddPress(UIElement e)
    {
        var (scale, _) = Transforms(e);
        void To(double v) { Spring(scale, ScaleTransform.ScaleXProperty, v, 0.25, 0.6); Spring(scale, ScaleTransform.ScaleYProperty, v, 0.25, 0.6); }
        e.PreviewMouseLeftButtonDown += (_, _) => To(0.86);
        e.PreviewMouseLeftButtonUp += (_, _) => To(1);
        e.MouseLeave += (_, _) => To(1);
    }

    /// <summary>A loop sampled from <paramref name="f"/>, so it repeats without a seam (like the Mac's Core Animation loops).</summary>
    public static DoubleAnimationUsingKeyFrames Loop(double duration, int steps, Func<double, double> f)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(duration) };
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps * duration;
            a.KeyFrames.Add(new LinearDoubleKeyFrame(f(t), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        Timeline.SetDesiredFrameRate(a, 30);
        return a;
    }
}

/// <summary>Four bars dancing to the music (the Mac's BarsLayerView).</summary>
public sealed class BarsView : Canvas
{
    private readonly Rectangle[] _bars = new Rectangle[4];
    private bool _playing;

    public BarsView()
    {
        Width = 18;
        Height = 16;
        IsHitTestVisible = false;
        const double w = 2.5, gap = 2;
        var x0 = (Width - (w * 4 + gap * 3)) / 2;
        for (var i = 0; i < 4; i++)
        {
            var bar = new Rectangle { Width = w, Height = 16, RadiusX = 1.25, RadiusY = 1.25, RenderTransformOrigin = new Point(0.5, 0.5) };
            bar.RenderTransform = new ScaleTransform(1, 0.2);
            SetLeft(bar, x0 + i * (w + gap));
            Children.Add(bar);
            _bars[i] = bar;
        }
    }

    public Brush Color { set { foreach (var b in _bars) b.Fill = value; } }

    /// <summary>Elevated style: the bars glow in the album's color.</summary>
    public void SetGlow(bool on, Color color)
    {
        if (!on) { Effect = null; return; }
        if (Effect is not DropShadowEffect glow) Effect = glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 6, Opacity = 0.7 };
        glow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(color, TimeSpan.FromSeconds(0.5)));
    }

    public bool Playing
    {
        get => _playing;
        set
        {
            if (_playing == value && _started) return;
            _playing = value;
            _started = true;
            for (var i = 0; i < 4; i++)
            {
                var scale = (ScaleTransform)_bars[i].RenderTransform;
                if (value)
                {
                    // |sin| at a different speed per bar, over whole cycles so it loops seamlessly.
                    var speed = 3.1 + i * 1.7;
                    var cycles = Math.Max(1, Math.Round(1.6 * speed / Math.PI));
                    var phase = i;
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                        Motion.Loop(cycles * Math.PI / speed, (int)cycles * 10, t => 0.35 + 0.65 * Math.Abs(Math.Sin(t * speed + phase))));
                }
                else
                {
                    // Settles low, like the Mac's implicit 0.25 s fade.
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Ease(0.2, 0.25));
                }
            }
        }
    }
    private bool _started;
}

/// <summary>Glass style: a small wave flowing through the pill, Siri-like (the Mac's WaveLayerView).</summary>
public sealed class MiniWaveView : Canvas
{
    private readonly Path[] _lines = { new(), new() };
    private bool _playing, _started;

    public MiniWaveView()
    {
        Width = 26;
        Height = 16;
        IsHitTestVisible = false;
        for (var i = 0; i < 2; i++)
        {
            _lines[i].StrokeThickness = i == 0 ? 1.8 : 1.2;
            _lines[i].StrokeStartLineCap = _lines[i].StrokeEndLineCap = PenLineCap.Round;
            _lines[i].StrokeLineJoin = PenLineJoin.Round;
            _lines[i].RenderTransformOrigin = new Point(0.5, 0.5);
            _lines[i].RenderTransform = new ScaleTransform(1, 0.14);
            _lines[i].Width = Width;
            _lines[i].Height = Height;
            Children.Add(_lines[i]);
        }
        Color = Colors.White;
    }

    public Color Color
    {
        set
        {
            _lines[0].Stroke = MacColors.Brush(value);
            _lines[1].Stroke = MacColors.Brush(value, 0.45);
        }
    }

    private Geometry Wave(double phase, double frequency)
    {
        var g = new StreamGeometry();
        double w = Width, mid = Height / 2;
        using (var c = g.Open())
        {
            for (var x = 0.0; x <= w; x += 1)
            {
                var u = x / w;
                // Both ends meet in the middle.
                var y = mid - Math.Sin(u * 2 * Math.PI * frequency - phase) * Math.Sin(Math.PI * u) * mid * 0.85;
                if (x == 0) c.BeginFigure(new Point(x, y), false, false); else c.LineTo(new Point(x, y), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    public bool Playing
    {
        get => _playing;
        set
        {
            if (_playing == value && _started) return;
            _playing = value;
            _started = true;
            for (var i = 0; i < 2; i++)
            {
                var line = _lines[i];
                var scale = (ScaleTransform)line.RenderTransform;
                double k = i, frequency = 1.6 + k * 0.5;
                if (value)
                {
                    var speed = 5 + k * 2;
                    var duration = 2 * Math.PI / speed;
                    var flow = new ObjectAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(duration) };
                    const int steps = 30;
                    for (var s = 0; s < steps; s++)
                    {
                        var t = (double)s / steps * duration;
                        flow.KeyFrames.Add(new DiscreteObjectKeyFrame(Wave(t * speed, frequency), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
                    }
                    line.BeginAnimation(Path.DataProperty, flow);
                    // Louder and quieter moments, so it feels alive rather than a loop.
                    var pulse = 1.3 + k * 0.7;
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Loop(Math.PI / pulse, 12, t => 0.55 + 0.45 * Math.Abs(Math.Sin(t * pulse + k))));
                }
                else
                {
                    line.BeginAnimation(Path.DataProperty, null);
                    line.Data = Wave(0, frequency);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Ease(0.14, 0.25));
                }
            }
        }
    }
}

/// <summary>Glass style: soft waves in the album's color drifting along the bottom of the panel (the Mac's FlowLayerView).</summary>
public sealed class FlowingWavesView : Canvas
{
    private readonly Path[] _fills = { new(), new(), new() };
    private readonly Storyboard _story = new();
    private bool _playing = true, _built;

    public FlowingWavesView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        foreach (var f in _fills) Children.Add(f);
        Loaded += (_, _) => Build();
        Unloaded += (_, _) => { _story.Stop(this); _built = false; };
    }

    private Color _color = Colors.White;

    public Color Color
    {
        set
        {
            _color = value;
            for (var i = 0; i < _fills.Length; i++)
            {
                if (_fills[i].Fill is LinearGradientBrush g && g.GradientStops.Count == 2)
                {
                    var top = Color.FromArgb((byte)((0.32 - 0.07 * i) * 255), value.R, value.G, value.B);
                    g.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(top, TimeSpan.FromSeconds(0.5)));
                    g.GradientStops[1].Color = Color.FromArgb(0, value.R, value.G, value.B);
                }
            }
        }
    }

    public bool Playing
    {
        set
        {
            if (_playing == value) return;
            _playing = value;
            if (!_built) return;
            if (value) _story.Resume(this); else _story.Pause(this);
            BeginAnimation(OpacityProperty, Motion.Ease(value ? 1 : 0.4, 0.4));
        }
    }

    private void Build()
    {
        if (_built || Width <= 0 || Height <= 0) return;
        _built = true;
        _story.Children.Clear();
        double w = Width, h = Height;
        for (var i = 0; i < _fills.Length; i++)
        {
            var k = (double)i;
            var amp = h * (0.13 + 0.05 * k);
            var crest = h * (0.42 + 0.14 * k);  // the wave's middle line, from the top
            var length = w / (1 + 0.4 * k);
            var width = w + length;

            // One wave a wavelength wider than the view, slid by exactly one wavelength: a seamless loop.
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                for (var x = 0.0; x <= width; x += 4) c.LineTo(new Point(x, crest - amp * Math.Sin(x / length * 2 * Math.PI)), true, true);
                c.LineTo(new Point(width, h), true, true);
            }
            g.Freeze();
            var path = _fills[i];
            path.Data = g;
            path.Fill ??= new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, crest - amp),
                EndPoint = new Point(0, h),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)((0.32 - 0.07 * k) * 255), _color.R, _color.G, _color.B), 0),
                    new GradientStop(Color.FromArgb(0, _color.R, _color.G, _color.B), 1),
                },
            };

            var move = new TranslateTransform();
            var breathe = new ScaleTransform(1, 1, 0, crest);  // breathing keeps the middle line in place
            path.RenderTransform = new TransformGroup { Children = { breathe, move } };
            var direction = i % 2 == 0 ? 1 : -1;
            var from = direction > 0 ? 0 : -length;
            var speed = 0.9 + 0.45 * k;

            var slide = new DoubleAnimation(from, from - direction * length, TimeSpan.FromSeconds(2 * Math.PI / speed)) { RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(slide, path);
            Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.Children[1].X"));
            var swell = new DoubleAnimation(0.4, 1.0, TimeSpan.FromSeconds(Math.PI / (0.8 + 0.3 * k)))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(swell, path);
            Storyboard.SetTargetProperty(swell, new PropertyPath("RenderTransform.Children[0].ScaleY"));
            _story.Children.Add(slide);
            _story.Children.Add(swell);
        }
        Timeline.SetDesiredFrameRate(_story, 30);
        _story.Begin(this, true);
        if (!_playing) { _story.Pause(this); Opacity = 0.4; }
    }
}

/// <summary>One line of text that slides sideways while hovered when it doesn't fit (the Mac's Marquee).</summary>
public sealed class Marquee : Border
{
    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TranslateTransform _move = new();
    private bool _hover;

    public Marquee(double size, FontWeight weight, Brush color, double height)
    {
        Height = height;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        _text.FontSize = size;
        _text.FontWeight = weight;
        _text.Foreground = color;
        _text.RenderTransform = _move;
        Child = _text;
        MouseEnter += (_, _) => { _hover = true; Roll(); };
        MouseLeave += (_, _) => { _hover = false; Roll(); };
    }

    public string Text
    {
        get => _text.Text;
        set { if (_text.Text == value) return; _text.Text = value; Roll(); }
    }

    public Brush Foreground { set => _text.Foreground = value; }
    public TextBlock Inner => _text;

    private void Roll()
    {
        _move.BeginAnimation(TranslateTransform.XProperty, null);
        _move.X = 0;
        _text.Width = double.NaN;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;
        if (!_hover || ActualWidth <= 0) return;
        var full = new FormattedText(_text.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_text.FontFamily, _text.FontStyle, _text.FontWeight, _text.FontStretch), _text.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var overflow = full - ActualWidth;
        if (overflow <= 0) return;
        _text.TextTrimming = TextTrimming.None;
        _text.Width = full + 1;
        // To the end and back, at a calm reading speed.
        var roll = Motion.Ease(-overflow, overflow / 28 + 0.6, 0.5, 0);
        roll.AutoReverse = true;
        roll.RepeatBehavior = RepeatBehavior.Forever;
        _move.BeginAnimation(TranslateTransform.XProperty, roll);
    }
}

/// <summary>A round icon button with the Mac's hover circle and press spring (ControlButton).</summary>
public sealed class ControlButton : Button
{
    private readonly Border _circle = new() { CornerRadius = new CornerRadius(999), Background = Brushes.Transparent };
    private readonly ContentPresenter _icon = new();

    public ControlButton(UIElement icon, double box = 42)
    {
        Width = Height = box;
        Focusable = false;
        Cursor = Cursors.Hand;
        _icon.Content = icon;
        var grid = new Grid();
        grid.Children.Add(_circle);
        grid.Children.Add(_icon);
        Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        Content = grid;
        Motion.AddPress(this);
        MouseEnter += (_, _) => Circle(0.1);
        MouseLeave += (_, _) => Circle(0);
        PreviewMouseLeftButtonDown += (_, _) => Circle(0.2);
        PreviewMouseLeftButtonUp += (_, _) => Circle(IsMouseOver ? 0.1 : 0);
    }

    public UIElement Icon
    {
        set => _icon.Content = value;
    }

    private void Circle(double opacity) =>
        _circle.Background = MacColors.White(opacity);
}
