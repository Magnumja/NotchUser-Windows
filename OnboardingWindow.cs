using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static NotchUser.MacColors;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>
/// Shown once, the first time the app opens: welcome with language and style, what it does
/// with the optional add-ons, and a few last options. (Windows needs no permission prompts.)
/// </summary>
public sealed class OnboardingWindow : GlassWindow
{
    private const int Pages = 3;
    private int _page;
    private readonly Grid _pageHost = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private Button _back = null!, _next = null!;

    public OnboardingWindow() : base(500, 610)
    {
        ShowInTaskbar = true;
        // Closing it early counts as seen too, as on the Mac.
        Closed += (_, _) =>
        {
            if (Settings.Current.Onboarded) return;
            Settings.Current.Onboarded = true;
            Settings.Current.Save();
        };
        Build();
    }

    private void Build()
    {
        var root = new DockPanel { Margin = new Thickness(28, 36, 28, 22), LastChildFill = true };
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_pageHost);
        Card.Child = root;
        _pageHost.Children.Clear();
        ShowPage(0);
    }

    /// <summary>The new page slides in from the side it comes from while the old one leaves (Mac: 40 pt, spring 0.42/0.86).</summary>
    private void ShowPage(int direction)
    {
        var page = _page switch { 0 => Welcome(), 1 => Features(), _ => Ready() };
        page.VerticalAlignment = VerticalAlignment.Top;
        foreach (UIElement old in _pageHost.Children.Cast<UIElement>().ToList())
        {
            var leave = new TranslateTransform();
            old.RenderTransform = leave;
            old.IsHitTestVisible = false;
            leave.BeginAnimation(TranslateTransform.XProperty, SpringEase.To(-40 * direction, 0.42, 0.86));
            var fade = Motion.Ease(0, 0.2);
            fade.Completed += (_, _) => _pageHost.Children.Remove(old);
            old.BeginAnimation(OpacityProperty, fade);
        }
        _pageHost.Children.Add(page);
        if (direction != 0)
        {
            var enter = new TranslateTransform(40 * direction, 0);
            page.RenderTransform = enter;
            page.Opacity = 0;
            enter.BeginAnimation(TranslateTransform.XProperty, SpringEase.To(0, 0.42, 0.86));
            page.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.25));
        }
        UpdateFooter();
    }

    private static FrameworkElement Gap(FrameworkElement e, double top)
    {
        e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }

    private FrameworkElement Welcome()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.AppIcon(88));
        stack.Children.Add(Gap(Ui.Text(L("welcomeTitle"), 26, FontWeights.Bold, align: TextAlignment.Center), 14));
        stack.Children.Add(Gap(Ui.Text(L("welcomeSub"), 14, opacity: 0.55, align: TextAlignment.Center), 14));
        // Changing the language redraws the walkthrough in the new language.
        stack.Children.Add(Gap(Ui.Section(L("language"), Ui.LanguagePicker(Build)), 22));
        stack.Children.Add(Gap(Ui.Section(L("appearance"), Ui.StylePicker()), 14));
        return stack;
    }

    private FrameworkElement Features()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(L("featuresTitle"), 22, FontWeights.Bold, align: TextAlignment.Center));
        stack.Children.Add(Gap(Feature("\uEC4F", Pink, L("featMusic"), L("featMusicDesc")), 22));
        stack.Children.Add(Gap(Feature("\uE7B8", Accent, L("featShelf"), L("featShelfDesc")), 18));
        stack.Children.Add(Gap(Ui.Section(L("addons"), Ui.AddOns()), 24));
        var hint = Ui.Text(L("addonsSub"), 11, opacity: 0.55);
        hint.Margin = new Thickness(4, 6, 4, 0);
        stack.Children.Add(hint);
        return stack;
    }

    private static FrameworkElement Feature(string glyph, Color color, string title, string detail)
    {
        var row = new DockPanel();
        var badge = Ui.Badge(glyph, color, 42, 11, 18);
        badge.Margin = new Thickness(0, 0, 14, 0);
        badge.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        var texts = new StackPanel();
        texts.Children.Add(Ui.Text(title, 14, FontWeights.SemiBold));
        texts.Children.Add(Gap(Ui.Text(detail, 12, opacity: 0.55), 3));
        row.Children.Add(texts);
        return row;
    }

    private FrameworkElement Ready()
    {
        var stack = new StackPanel();
        stack.Children.Add(Icons.Glyph("\uEC61", 46, Brush(Green)));
        stack.Children.Add(Gap(Ui.Text(L("readyTitle"), 24, FontWeights.Bold, align: TextAlignment.Center), 14));
        stack.Children.Add(Gap(Ui.Section(L("behavior"),
            Ui.Switch(L("launchAtLogin"), Settings.LaunchAtLogin, on => Settings.LaunchAtLogin = on)), 14));
        var tips = new StackPanel { Margin = new Thickness(4, 14, 4, 0) };
        tips.Children.Add(Ui.Tip("\uE7C9", L("tipHover")));
        if (Settings.Current.OcrEnabled) tips.Children.Add(Ui.Tip("\uE8C8", L("tipOCR")));
        tips.Children.Add(Ui.Tip("\uE700", L("tipMenu")));
        stack.Children.Add(tips);
        return stack;
    }

    private FrameworkElement Footer()
    {
        var grid = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        _back = Ui.Plain(L("back"), () => { _page--; ShowPage(-1); });
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(_back);

        _dots.HorizontalAlignment = HorizontalAlignment.Center;
        _dots.Children.Clear();
        for (var i = 0; i < Pages; i++)
            _dots.Children.Add(new Rectangle { Width = 7, Height = 7, RadiusX = 3.5, RadiusY = 3.5, Margin = new Thickness(3.5, 0, 3.5, 0) });
        Grid.SetColumn(_dots, 1);
        grid.Children.Add(_dots);

        _next = Ui.Primary("", () =>
        {
            if (_page == Pages - 1)
            {
                Settings.Current.Onboarded = true;
                Settings.Current.Save();
                Close();
                return;
            }
            _page++;
            ShowPage(1);
        });
        _next.HorizontalAlignment = HorizontalAlignment.Right;
        _next.MinWidth = 100;
        _next.IsDefault = true;
        Grid.SetColumn(_next, 2);
        grid.Children.Add(_next);
        return grid;
    }

    private void UpdateFooter()
    {
        _back.Visibility = _page > 0 ? Visibility.Visible : Visibility.Hidden;
        _next.Content = _page == Pages - 1 ? L("getStarted") : L("next");
        // The current page's dot stretches into a capsule.
        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var dot = (Rectangle)_dots.Children[i];
            dot.Fill = White(i == _page ? 0.9 : 0.25);
            Motion.Spring(dot, WidthProperty, i == _page ? 18 : 7, 0.42, 0.86);
        }
    }
}
