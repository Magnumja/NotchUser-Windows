using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static NotchUser.MacColors;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>A dark, rounded, translucent window like the Mac's HUD windows (walkthrough, Settings).</summary>
public class GlassWindow : Window
{
    protected readonly Border Card;
    private readonly Grid _content = new();

    public GlassWindow(double width, double height)
    {
        Title = "NotchUser";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Width = width + 40;
        Height = height + 40;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico")); } catch { }

        Card = new Border();
        var frame = new Border
        {
            Margin = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Background = Frozen(new LinearGradientBrush(Color.FromArgb(0xF7, 0x2A, 0x2A, 0x2D), Color.FromArgb(0xF7, 0x1E, 0x1E, 0x21), 90)),
            BorderBrush = White(0.12),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5 },
            ClipToBounds = true,
        };
        _content.Children.Add(Card);

        // Close, top right as on Windows (Esc works too).
        var close = new Button
        {
            Width = 28, Height = 28, Margin = new Thickness(0, 8, 8, 0), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, Focusable = false, ToolTip = L("close"),
            Template = CloseTemplate(),
        };
        close.Click += (_, _) => Close();
        _content.Children.Add(close);
        frame.Child = _content;
        Content = frame;

        // Drag the window by its background.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => App.TrimMemory();
    }

    private static ControlTemplate CloseTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bg = new FrameworkElementFactory(typeof(Border), "Bg");
        bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var x = new FrameworkElementFactory(typeof(TextBlock));
        x.SetValue(TextBlock.TextProperty, "\uE711");
        x.SetValue(TextBlock.FontFamilyProperty, Icons.Glyphs);
        x.SetValue(TextBlock.FontSizeProperty, 10.0);
        x.SetValue(TextBlock.ForegroundProperty, White(0.6));
        x.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        x.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bg.AppendChild(x);
        t.VisualTree = bg;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, White(0.1), "Bg"));
        t.Triggers.Add(hover);
        return t;
    }
}

/// <summary>Small building blocks shared by the walkthrough and Settings, drawn like their Mac counterparts.</summary>
public static class Ui
{
    public static readonly Brush Secondary = White(0.55);

    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, double opacity = 1,
                                 TextAlignment align = TextAlignment.Left) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = White(opacity),
        TextAlignment = align,
        TextWrapping = TextWrapping.Wrap,
    };

    public static Image AppIcon(double size) => new()
    {
        Source = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico")),
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.45, BlurRadius = size / 3, ShadowDepth = size / 15, Direction = 270 },
    };

    /// <summary>Frosted group with a small caption, like the panes in System Settings (the Mac's glassSection).</summary>
    public static FrameworkElement Section(string title, UIElement content)
    {
        var stack = new StackPanel();
        if (title.Length > 0)
        {
            var caption = Text(title.ToUpperInvariant(), 10.5, FontWeights.SemiBold, 0.55);
            caption.Margin = new Thickness(4, 0, 0, 7);
            stack.Children.Add(caption);
        }
        stack.Children.Add(new Border
        {
            Background = White(0.06),
            BorderBrush = Frozen(new LinearGradientBrush(Color.FromArgb(0x29, 255, 255, 255), Color.FromArgb(0x0A, 255, 255, 255), 90)),
            BorderThickness = new Thickness(0.7),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = content,
        });
        return stack;
    }

    public static CheckBox Switch(string label, bool value, Action<bool> onChange)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Style = (Style)Application.Current.Resources["Switch"],
        };
        box.Click += (_, _) => onChange(box.IsChecked == true);
        return box;
    }

    public static Button Primary(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources["Primary"] };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Plain(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.Resources["Plain"] };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>A macOS segmented picker: options in one rounded track, the chosen one raised.</summary>
    public static FrameworkElement Segmented<T>(string group, IEnumerable<(T Value, string Label)> options, T current, Action<T> onPick)
    {
        var row = new UniformGrid { Rows = 1 };
        var name = group + Guid.NewGuid();
        foreach (var (value, label) in options)
        {
            var option = new RadioButton
            {
                Content = label,
                GroupName = name,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
                Style = (Style)Application.Current.Resources["Segment"],
            };
            option.Checked += (_, _) => onPick(value);
            row.Children.Add(option);
        }
        return new Border { Background = White(0.1), CornerRadius = new CornerRadius(7), Padding = new Thickness(1), Child = row };
    }

    /// <summary>System / English / Português / Español.</summary>
    public static FrameworkElement LanguagePicker(Action onChange) =>
        Segmented("language", Strings.Languages.Select(c => (c, LanguageName(c))), Settings.Current.Language, code =>
        {
            if (Settings.Current.Language == code) return;
            Settings.Current.Language = code;
            Settings.Current.Save();
            onChange();
        });

    /// <summary>Auto, 100%…150%: the whole notch drawn bigger.</summary>
    public static FrameworkElement SizePicker() =>
        Segmented("size", new[] { 0, 1.0, 1.15, 1.3, 1.5 }.Select(s => (s, s == 0 ? L("sizeAuto") : $"{s * 100:0}%")), Settings.Current.Size, size =>
        {
            if (Settings.Current.Size == size) return;
            Settings.Current.Size = size;
            Settings.Current.Save();
        });


    /// <summary>The optional extras, all off until turned on.</summary>
    public static FrameworkElement AddOns()
    {
        var stack = new StackPanel();
        stack.Children.Add(Switch(L("addonOCR"), Settings.Current.OcrEnabled, on =>
        {
            Settings.Current.OcrEnabled = on;
            Settings.Current.Save();
        }));
        stack.Children.Add(Switch(L("addonLyrics"), Settings.Current.LyricsEnabled, on =>
        {
            Settings.Current.LyricsEnabled = on;
            Settings.Current.Save();
        }));
        var hint = Text(L("lyricsHint"), 10.5, opacity: 0.55);
        hint.Margin = new Thickness(0, 0, 44, 2);
        stack.Children.Add(hint);
        return stack;
    }

    /// <summary>A rounded square in a color gradient with a white symbol (SwiftUI's color.gradient badges).</summary>
    public static Border Badge(string glyph, Color color, double size, double radius, double glyphSize)
    {
        var top = Color.FromRgb((byte)Math.Min(255, color.R + 28), (byte)Math.Min(255, color.G + 28), (byte)Math.Min(255, color.B + 28));
        var badge = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = Frozen(new LinearGradientBrush(top, color, 90)),
            Child = Icons.Glyph(glyph, glyphSize, Brushes.White),
        };
        ((TextBlock)badge.Child).FontWeight = FontWeights.SemiBold;
        return badge;
    }

    public static FrameworkElement Tip(string glyph, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var icon = Icons.Glyph(glyph, 13, White(0.75));
        icon.Width = 18;
        icon.Margin = new Thickness(0, 1, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(Text(text, 12.5, opacity: 0.75));
        return row;
    }
}
