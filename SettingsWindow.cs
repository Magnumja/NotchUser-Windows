using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static NotchUser.MacColors;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>
/// Settings in landscape, like the Mac's (and System Settings): sections on the left,
/// their options on the right. Opened from the notch's right-click menu.
/// </summary>
public sealed class SettingsWindow : GlassWindow
{
    private static SettingsWindow? _open;
    private const double RowHeight = 36, RowGap = 3;

    private enum Pane { General, Appearance, AddOns }
    private Pane _pane = Pane.General;
    private readonly Border _highlight = new() { Height = RowHeight, CornerRadius = new CornerRadius(9), Background = White(0.12), VerticalAlignment = VerticalAlignment.Top };
    private readonly TranslateTransform _highlightMove = new();
    private readonly List<(Pane Pane, Border Badge, TextBlock Title)> _rows = new();
    private readonly Grid _contentHost = new();

    public static void ShowOrFocus()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private SettingsWindow() : base(780, 500) => Build();

    private static (string Title, string Glyph, Color Color) Info(Pane pane) => pane switch
    {
        Pane.General => (L("general"), "\uE713", Gray),
        Pane.Appearance => (L("appearance"), "\uE790", Purple),
        _ => (L("addons"), "\uEA86", Orange),
    };

    private void Build()
    {
        _rows.Clear();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar
        var sidebar = new DockPanel { Background = Black(0.18), LastChildFill = false };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 28, 20, 16) };
        header.Children.Add(Ui.AppIcon(38));
        var names = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text("NotchUser", 15, FontWeights.Bold));
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        names.Children.Add(Ui.Text(version == null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}", 11, opacity: 0.55));
        header.Children.Add(names);
        DockPanel.SetDock(header, Dock.Top);
        sidebar.Children.Add(header);

        var list = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        _highlight.RenderTransform = _highlightMove;
        list.Children.Add(_highlight);
        var rows = new StackPanel();
        foreach (var pane in Enum.GetValues<Pane>()) rows.Children.Add(Row(pane));
        list.Children.Add(rows);
        DockPanel.SetDock(list, Dock.Top);
        sidebar.Children.Add(list);

        var tip = Ui.Text(L("tip"), 10.5, opacity: 0.55);
        tip.Margin = new Thickness(20, 0, 20, 16);
        DockPanel.SetDock(tip, Dock.Bottom);
        sidebar.Children.Add(tip);
        grid.Children.Add(sidebar);

        var divider = new Border { Background = White(0.08) };
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);
        Grid.SetColumn(_contentHost, 2);
        grid.Children.Add(_contentHost);
        Card.Child = grid;

        Select(_pane, animate: false);
    }

    private FrameworkElement Row(Pane pane)
    {
        var (title, glyph, color) = Info(pane);
        var badge = Ui.Badge(glyph, color, 24, 6, 11.5);
        badge.RenderTransformOrigin = new Point(0.5, 0.5);
        badge.RenderTransform = new ScaleTransform(1, 1);
        var text = Ui.Text(title, 13);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(10, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = RowHeight, Margin = new Thickness(8, 0, 8, RowGap), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.Children.Add(badge);
        row.Children.Add(text);
        row.MouseLeftButtonUp += (_, _) => Select(pane, animate: true);
        _rows.Add((pane, badge, text));
        return row;
    }

    private void Select(Pane pane, bool animate)
    {
        var changed = pane != _pane || _contentHost.Children.Count == 0;
        _pane = pane;
        var index = _rows.FindIndex(r => r.Pane == pane);
        // The highlight glides to the chosen row (Mac: matchedGeometryEffect on a spring).
        if (animate) Motion.Spring(_highlightMove, TranslateTransform.YProperty, index * (RowHeight + RowGap), 0.34, 0.86);
        else _highlightMove.Y = index * (RowHeight + RowGap);
        foreach (var (p, badge, title) in _rows)
        {
            var on = p == pane;
            title.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            var s = (ScaleTransform)badge.RenderTransform;
            Motion.Spring(s, ScaleTransform.ScaleXProperty, on ? 1.06 : 1, 0.34, 0.86);
            Motion.Spring(s, ScaleTransform.ScaleYProperty, on ? 1.06 : 1, 0.34, 0.86);
        }
        if (!changed) return;

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = PaneContent(pane), Focusable = false };
        _contentHost.Children.Clear();
        _contentHost.Children.Add(scroll);
        if (!animate) return;
        // The new pane rises in (Mac: opacity + 10 pt offset on a spring).
        var move = new TranslateTransform(0, 10);
        scroll.RenderTransform = move;
        scroll.Opacity = 0;
        move.BeginAnimation(TranslateTransform.YProperty, SpringEase.To(0, 0.34, 0.86));
        scroll.BeginAnimation(OpacityProperty, Motion.Ease(1, 0.2));
    }

    private FrameworkElement PaneContent(Pane pane)
    {
        var stack = new StackPanel { Margin = new Thickness(28, 30, 28, 24) };
        void Add(UIElement e, double gap = 16)
        {
            if (stack.Children.Count > 0 && e is FrameworkElement f) f.Margin = new Thickness(0, gap, 0, 0);
            stack.Children.Add(e);
        }
        Add(Ui.Text(Info(pane).Title, 22, FontWeights.Bold));
        switch (pane)
        {
            case Pane.General:
                // Changing the language redraws the window in the new language.
                Add(Ui.Section(L("language"), Ui.LanguagePicker(Build)));
                var behavior = new StackPanel();
                behavior.Children.Add(Toggle(L("peekOnTrack"), Settings.Current.PeekOnTrackChange, on => Settings.Current.PeekOnTrackChange = on));
                behavior.Children.Add(Toggle(L("volumeHud"), Settings.Current.VolumeHud, on => Settings.Current.VolumeHud = on));
                behavior.Children.Add(Toggle(L("notices"), Settings.Current.Notices, on => Settings.Current.Notices = on));
                behavior.Children.Add(Ui.Switch(L("launchAtLogin"), Settings.LaunchAtLogin, on => Settings.LaunchAtLogin = on));
                Add(Ui.Section(L("behavior"), behavior));
                break;
            case Pane.Appearance:
                Add(Ui.Section("", Ui.StylePicker()));
                Add(Ui.Section(L("size"), Ui.SizePicker()));
                break;
            default:
                Add(Ui.Section("", Ui.AddOns()));
                var hint = Ui.Text(L("addonsSub"), 11, opacity: 0.55);
                hint.Margin = new Thickness(4, 0, 4, 0);
                Add(hint, 6);
                break;
        }
        return stack;
    }

    private static CheckBox Toggle(string label, bool value, Action<bool> set) => Ui.Switch(label, value, on =>
    {
        set(on);
        Settings.Current.Save();
    });
}
