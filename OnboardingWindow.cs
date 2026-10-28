using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>
/// Shown once, the first time the app opens: welcome and language, what it does with the
/// optional add-ons, and a few last options. (Windows needs no permission prompts for this.)
/// </summary>
public sealed class OnboardingWindow : GlassWindow
{
    private const int Pages = 3;
    private int _page;

    public OnboardingWindow() : base(480, 560)
    {
        ShowInTaskbar = true;
        Build();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(28, 30, 28, 20), LastChildFill = true };
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var page = _page switch { 0 => Welcome(), 1 => Features(), _ => Ready() };
        root.Children.Add(page);
        Card.Child = root;

        // Slide the new page in.
        page.RenderTransform = new TranslateTransform(30, 0);
        page.Opacity = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(slide, 60);
        page.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
    }

    private FrameworkElement Welcome()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.AppIcon(96));
        var title = Ui.Text(L("welcomeTitle"), 24, FontWeights.Bold, align: TextAlignment.Center);
        title.Margin = new Thickness(0, 14, 0, 6);
        stack.Children.Add(title);
        stack.Children.Add(Ui.Text(L("welcomeSub"), 14, alpha: 0x99, align: TextAlignment.Center));
        var lang = Ui.Section(L("language"), Ui.LanguagePicker(Build));
        lang.Margin = new Thickness(0, 30, 0, 0);
        stack.Children.Add(lang);
        return stack;
    }

    private FrameworkElement Features()
    {
        var stack = new StackPanel();
        var title = Ui.Text(L("featuresTitle"), 22, FontWeights.Bold, align: TextAlignment.Center);
        title.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(title);
        stack.Children.Add(Feature("", Color.FromRgb(0xE8, 0x4A, 0x8A), L("featMusic"), L("featMusicDesc")));
        stack.Children.Add(Feature("", Color.FromRgb(0x3A, 0x7B, 0xF0), L("featShelf"), L("featShelfDesc")));
        var addons = Ui.Section(L("addons"), Ui.AddOns());
        addons.Margin = new Thickness(0, 14, 0, 4);
        stack.Children.Add(addons);
        var hint = Ui.Text(L("addonsSub"), 11, alpha: 0x80);
        hint.Margin = new Thickness(4, 0, 0, 0);
        stack.Children.Add(hint);
        return stack;
    }

    private static FrameworkElement Feature(string glyph, Color color, string title, string detail)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        var badge = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(11),
            Background = new LinearGradientBrush(color, Color.Multiply(color, 0.75f), 90),
            Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var texts = new StackPanel();
        texts.Children.Add(Ui.Text(title, 14, FontWeights.SemiBold));
        texts.Children.Add(Ui.Text(detail, 12, alpha: 0x99));
        row.Children.Add(texts);
        return row;
    }

    private FrameworkElement Ready()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 40,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)), HorizontalAlignment = HorizontalAlignment.Center,
        });
        var title = Ui.Text(L("readyTitle"), 24, FontWeights.Bold, align: TextAlignment.Center);
        title.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(title);
        stack.Children.Add(Ui.Section(L("behavior"),
            Ui.Switch(L("launchAtLogin"), Settings.LaunchAtLogin, on => Settings.LaunchAtLogin = on)));
        var tips = new StackPanel { Margin = new Thickness(4, 10, 0, 0) };
        tips.Children.Add(Ui.Tip("", L("tipHover")));
        if (Settings.Current.OcrEnabled) tips.Children.Add(Ui.Tip("", L("tipOCR")));
        tips.Children.Add(Ui.Tip("", L("tipMenu")));
        stack.Children.Add(tips);
        return stack;
    }

    private FrameworkElement Footer()
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var back = Ui.Plain(L("back"), () => { _page--; Build(); });
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Visibility = _page > 0 ? Visibility.Visible : Visibility.Hidden;
        grid.Children.Add(back);

        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < Pages; i++)
        {
            dots.Children.Add(new Rectangle
            {
                Width = i == _page ? 18 : 7, Height = 7, RadiusX = 3.5, RadiusY = 3.5, Margin = new Thickness(3, 0, 3, 0),
                Fill = new SolidColorBrush(Color.FromArgb(i == _page ? (byte)0xE6 : (byte)0x40, 0xFF, 0xFF, 0xFF)),
            });
        }
        Grid.SetColumn(dots, 1);
        grid.Children.Add(dots);

        var last = _page == Pages - 1;
        var next = Ui.Primary(last ? L("getStarted") : L("next"), () =>
        {
            if (last)
            {
                Settings.Current.Onboarded = true;
                Settings.Current.Save();
                Close();
            }
            else
            {
                _page++;
                Build();
            }
        });
        next.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(next, 2);
        grid.Children.Add(next);
        return grid;
    }
}
