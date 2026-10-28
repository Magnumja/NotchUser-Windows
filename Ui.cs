using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>A dark, rounded, frosted-looking window (walkthrough, Settings).</summary>
public class GlassWindow : Window
{
    protected readonly Border Card;

    public GlassWindow(double width, double height)
    {
        Title = "NotchUser";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = width + 40;
        Height = height + 40;
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico")); } catch { }

        Card = new Border
        {
            Margin = new Thickness(20),
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush(Color.FromArgb(0xF7, 0x22, 0x22, 0x29), Color.FromArgb(0xF7, 0x12, 0x12, 0x16), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.5 },
        };
        Content = Card;
        // Drag the window by its background.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Closed += (_, _) => App.TrimMemory();
    }
}

/// <summary>Small building blocks shared by the walkthrough and Settings.</summary>
public static class Ui
{
    public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, byte alpha = 0xFF,
                                 TextAlignment align = TextAlignment.Left) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)),
        TextAlignment = align,
        TextWrapping = TextWrapping.Wrap,
    };

    public static Image AppIcon(double size) => new()
    {
        Source = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico")),
        Width = size,
        Height = size,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A frosted group with a small caption, like the panes in Windows Settings.</summary>
    public static FrameworkElement Section(string title, UIElement content)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        if (title.Length > 0)
        {
            var caption = Text(title.ToUpperInvariant(), 10.5, FontWeights.SemiBold, 0x8C);
            caption.Margin = new Thickness(4, 0, 0, 6);
            stack.Children.Add(caption);
        }
        stack.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 14, 8),
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

    /// <summary>System / English / Português / Español, as one segmented row.</summary>
    public static FrameworkElement LanguagePicker(Action onChange)
    {
        var row = new UniformGrid { Rows = 1 };
        foreach (var code in Strings.Languages)
        {
            var option = new RadioButton
            {
                Content = LanguageName(code),
                GroupName = "language",
                IsChecked = Settings.Current.Language == code,
                Style = (Style)Application.Current.Resources["Segment"],
            };
            option.Checked += (_, _) =>
            {
                if (Settings.Current.Language == code) return;
                Settings.Current.Language = code;
                Settings.Current.Save();
                onChange();
            };
            row.Children.Add(option);
        }
        return row;
    }

    /// <summary>The optional extras (only text copy on Windows for now).</summary>
    public static FrameworkElement AddOns() => Switch(L("addonOCR"), Settings.Current.OcrEnabled, on =>
    {
        Settings.Current.OcrEnabled = on;
        Settings.Current.Save();
    });

    public static FrameworkElement Tip(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0xBF, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 1, 10, 0),
        });
        row.Children.Add(Text(text, 12.5, alpha: 0xBF));
        return row;
    }
}
