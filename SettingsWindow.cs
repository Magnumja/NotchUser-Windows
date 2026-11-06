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

    private enum Pane { General, Appearance, Layout, AddOns, About }
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

    /// <summary>Opens Settings on a named section (used by checks and screenshots).</summary>
    public static void ShowPane(string name)
    {
        ShowOrFocus();
        if (_open != null && Enum.TryParse<Pane>(name, true, out var pane)) _open.Select(pane, animate: false);
    }

    private SettingsWindow() : base(860, 620) => Build();

    private static (string Title, string Glyph, Color Color) Info(Pane pane) => pane switch
    {
        Pane.General => (L("general"), "\uE713", Gray),
        Pane.Appearance => (L("appearance"), "\uE790", Purple),
        Pane.Layout => (L("sizePosition"), "\uE7F4", Accent),
        Pane.About => (L("about"), "\uE946", Green),
        _ => (L("addons"), "\uEA86", Orange),
    };

    private void Build()
    {
        _rows.Clear();
        // Built again when the language changes: the parts kept across builds leave the old layout
        // first (WPF refuses an element that still has a parent, and the app closed).
        (_highlight.Parent as Panel)?.Children.Remove(_highlight);
        (_contentHost.Parent as Panel)?.Children.Remove(_contentHost);
        _contentHost.Children.Clear();
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
        // Taken here, or the press would reach the window and start dragging it, and the drag
        // swallows the release: clicking a section did nothing.
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
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
        FrameworkElement Hint(string text)
        {
            var t = Ui.Text(text, 10.5, opacity: 0.55);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 8, 0, 0);
            return t;
        }
        FrameworkElement Choice(string key, (string, string)[] options, Func<string> get, Action<string> set, string? hint = null)
        {
            var box = new StackPanel();
            box.Children.Add(Ui.Segmented(key, options, get(), v =>
            {
                if (get() == v) return;
                set(v);
                Settings.Current.Save();
            }));
            if (hint != null) box.Children.Add(Hint(hint));
            return box;
        }
        var s = Settings.Current;
        Add(Ui.Text(Info(pane).Title, 22, FontWeights.Bold));
        switch (pane)
        {
            case Pane.General:
                // Changing the language redraws the window in the new language.
                Add(Ui.Section(L("language"), Ui.LanguagePicker(Build)));
                Add(Ui.Section(L("openOn"), Choice("openOn", new[] { ("hover", L("onHover")), ("click", L("onClick")) }, () => s.OpenOn, v => s.OpenOn = v)));
                var startup = new StackPanel();
                startup.Children.Add(Ui.Switch(L("launchAtLogin"), Settings.LaunchAtLogin, on => Settings.LaunchAtLogin = on));
                startup.Children.Add(Toggle(L("hideWhenIdle"), s.HideWhenIdle, on => s.HideWhenIdle = on));
                startup.Children.Add(Toggle(L("peekOnTrack"), s.PeekOnTrackChange, on => s.PeekOnTrackChange = on));
                startup.Children.Add(Toggle(L("checkUpdates"), s.CheckUpdates, on => { s.CheckUpdates = on; s.LastUpdateCheck = ""; }));
                Add(Ui.Section(L("startupTitle"), startup));
                var shows = new StackPanel();
                shows.Children.Add(Toggle(L("volumeHud"), s.VolumeHud, on => s.VolumeHud = on));
                shows.Children.Add(Toggle(L("notices"), s.Notices, on => s.Notices = on));
                Add(Ui.Section(L("noticesTitle"), shows));
                Add(Ui.Section(L("appsInNotch"), AppsList()));
                break;

            case Pane.Appearance:
                Add(Ui.Section(L("lookTitle"), Choice("look",
                    new[] { ("basic", L("style.basic")), ("elevated", L("style.elevated")), ("glass", L("style.glass")) },
                    () => s.Style, v => s.Look = v, L("lookHint"))));
                Add(Ui.Section(L("shape"), Choice("shape",
                    new[] { ("island", L("shapeIsland")), ("notch", L("shapeNotch")) }, () => s.Shape, v => s.Shape = v)));
                Add(Ui.Section(L("motion"), Choice("motion",
                    new[] { ("calm", L("motionCalm")), ("standard", L("motionStandard")), ("lively", L("motionLively")) },
                    () => s.Motion, v => s.Motion = v, L("motionHint"))));
                Add(Ui.Section(L("indicator"), Choice("indicator",
                    new[] { ("bars", L("indicatorBars")), ("wave", L("indicatorWave")) }, () => s.Indicator, v => s.Indicator = v)));
                var pill = new StackPanel();
                pill.Children.Add(Toggle(L("clockInPill"), s.ClockInPill, on => s.ClockInPill = on));
                Add(Ui.Section("", pill), 10);
                break;

            case Pane.Layout:
                Add(Ui.Section(L("position"), PlacementPreview()));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(Ui.Primary(L("movePosition"), () => (Application.Current.MainWindow as MainWindow)?.BeginMoveLater()));
                var reset = Ui.Plain(L("resetPosition"), () => { (Application.Current.MainWindow as MainWindow)?.ResetPosition(); Application.Current.Dispatcher.BeginInvoke(Build); });
                reset.Margin = new Thickness(8, 0, 0, 0);
                buttons.Children.Add(reset);
                Add(buttons, 10);
                var screens = Displays.All();
                if (screens.Count > 1)
                {
                    var options = new List<(string, string)> { ("primary", L("mainScreen")), ("follow", L("followMouse")) };
                    for (var n = 0; n < screens.Count; n++) options.Add((screens[n].Id, string.Format(L("screenN"), n + 1, screens[n].Width, screens[n].Height)));
                    Add(Ui.Section(L("display"), Choice("display", options.ToArray(), () => s.Display, v => s.Display = v)));
                }
                Add(Ui.Section(L("sizeOverall"), Ui.SizePicker()));
                var sliders = new StackPanel();
                sliders.Children.Add(MenuUi.Slider(L("closedShort"), () => s.ClosedSize,
                    v => { s.ClosedSize = v; s.Preview(); },
                    () => (Application.Current.MainWindow as MainWindow)?.PreviewSize(open: false, start: true),
                    () => s.Save(), rowWidth: double.NaN, barWidth: 300));
                sliders.Children.Add(MenuUi.Slider(L("openShort"), () => s.OpenSize,
                    v => { s.OpenSize = v; s.Preview(); },
                    () => (Application.Current.MainWindow as MainWindow)?.PreviewSize(open: true, start: true),
                    () => { s.Save(); (Application.Current.MainWindow as MainWindow)?.PreviewSize(open: true, start: false); },
                    rowWidth: double.NaN, barWidth: 300));
                sliders.Children.Add(Hint(L("sizeLive")));
                Add(Ui.Section(L("size"), sliders));
                break;

            case Pane.About:
                var about = new StackPanel();
                var version = Assembly.GetEntryAssembly()?.GetName().Version;
                about.Children.Add(Ui.Text("NotchUser", 18, FontWeights.Bold));
                about.Children.Add(Ui.Text(version == null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}", 12, opacity: 0.55));
                var blurb = Ui.Text(L("aboutText"), 12, opacity: 0.75);
                blurb.TextWrapping = TextWrapping.Wrap;
                blurb.Margin = new Thickness(0, 10, 0, 0);
                about.Children.Add(blurb);
                Add(Ui.Section("", about), 10);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                Button? check = null;
                check = Ui.Primary(L("checkNow"), async () =>
                {
                    check!.IsEnabled = false;
                    var found = await Updater.CheckAsync();
                    if (found != null) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true }); check.Content = "v" + found; }
                    else check.Content = L("upToDate");
                    check.IsEnabled = true;
                });
                actions.Children.Add(check);
                var source = Ui.Plain(L("sourceCode"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Magnumja/NotchUser-Windows") { UseShellExecute = true }));
                source.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(source);
                var logs = Ui.Plain(L("openLogs"), () => System.Diagnostics.Process.Start("explorer.exe", Settings.Folder));
                logs.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(logs);
                Add(actions, 12);
                break;

            default:
                Add(Ui.Section("", Ui.AddOns()));
                Add(Hint(L("addonsSub")), 6);
                break;
        }
        return stack;
    }

    /// <summary>
    /// The screen in miniature, with the notch on it. Click or drag anywhere on it: the real notch
    /// follows, to the nearest edge and the place along it.
    /// </summary>
    private static FrameworkElement PlacementPreview()
    {
        const double w = 320, h = 176, thick = 12, length = 64, gap = 8;
        var s = Settings.Current;
        var canvas = new Canvas { Width = w, Height = h, Background = Brushes.Transparent, Cursor = Cursors.Hand, ClipToBounds = true };
        // A window on the desktop, so it reads as a screen.
        var window = new Border { Width = 170, Height = 96, CornerRadius = new CornerRadius(7), Background = White(0.05), BorderBrush = White(0.12), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        Canvas.SetLeft(window, (w - 170) / 2);
        Canvas.SetTop(window, (h - 96) / 2 + 6);
        var bar = new Border { Height = 8, CornerRadius = new CornerRadius(7, 7, 0, 0), Background = White(0.08), VerticalAlignment = VerticalAlignment.Top };
        window.Child = bar;
        canvas.Children.Add(window);
        var move = new TranslateTransform();
        var pill = new Border { CornerRadius = new CornerRadius(thick / 2), Background = Brush(Accent), RenderTransform = move, IsHitTestVisible = false };
        canvas.Children.Add(pill);

        void Lay(bool animate)
        {
            var dock = s.Dock is "bottom" or "left" or "right" ? s.Dock : "top";
            var vertical = dock is "left" or "right";
            pill.Width = vertical ? thick : length;
            pill.Height = vertical ? length : thick;
            var off = Math.Clamp(s.DockOffset, 0, 1);
            double x = dock switch { "left" => gap, "right" => w - gap - thick, _ => Math.Clamp(off * w - length / 2, gap, w - gap - length) };
            double y = dock switch { "top" => gap, "bottom" => h - gap - thick, _ => Math.Clamp(off * h - length / 2, gap, h - gap - length) };
            if (!animate) { move.BeginAnimation(TranslateTransform.XProperty, null); move.BeginAnimation(TranslateTransform.YProperty, null); move.X = x; move.Y = y; return; }
            move.BeginAnimation(TranslateTransform.XProperty, SpringEase.To(x, 0.3, 0.7));
            move.BeginAnimation(TranslateTransform.YProperty, SpringEase.To(y, 0.3, 0.7));
        }
        void Aim(Point p)
        {
            var d = new Dictionary<string, double> { ["top"] = p.Y, ["bottom"] = h - p.Y, ["left"] = p.X, ["right"] = w - p.X };
            var edge = d.MinBy(k => k.Value).Key;
            s.Dock = edge;
            s.DockOffset = Math.Clamp(edge is "top" or "bottom" ? p.X / w : p.Y / h, 0, 1);
            Lay(true);
            s.Preview();
        }
        var dragging = false;
        canvas.MouseLeftButtonDown += (_, e) => { dragging = canvas.CaptureMouse(); Aim(e.GetPosition(canvas)); e.Handled = true; };
        canvas.MouseMove += (_, e) => { if (dragging) Aim(e.GetPosition(canvas)); };
        void Release() { if (!dragging) return; dragging = false; canvas.ReleaseMouseCapture(); s.Save(); }
        canvas.MouseLeftButtonUp += (_, _) => Release();
        canvas.LostMouseCapture += (_, _) => Release();
        Lay(false);

        return new Border
        {
            Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(10),
            Background = Black(0.35), BorderBrush = White(0.18), BorderThickness = new Thickness(1), Child = canvas, ClipToBounds = true,
            ToolTip = L("positionHint"),
        };
    }

    /// <summary>
    /// One switch per app that plays media (the ones playing now and the ones already turned off):
    /// off keeps that app out of the notch.
    /// </summary>
    private static FrameworkElement AppsList()
    {
        var stack = new StackPanel();
        var hidden = Settings.Current.HiddenApps;
        var ids = (MediaService.Current?.AllAppIds ?? new List<string>())
            .Concat(hidden).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(MediaService.AppName, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var id in ids)
        {
            stack.Children.Add(Toggle(MediaService.AppName(id), !hidden.Contains(id, StringComparer.OrdinalIgnoreCase), on =>
            {
                hidden.RemoveAll(h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase));
                if (!on) hidden.Add(id);
            }));
        }
        var hint = Ui.Text(L("appsHint"), 10.5, opacity: 0.55);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, ids.Count > 0 ? 4 : 0, 0, 2);
        stack.Children.Add(hint);
        return stack;
    }

    private static CheckBox Toggle(string label, bool value, Action<bool> set) => Ui.Switch(label, value, on =>
    {
        set(on);
        Settings.Current.Save();
    });
}
