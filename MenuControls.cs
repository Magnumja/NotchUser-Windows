using System.Windows.Documents;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static NotchUser.MacColors;

namespace NotchUser;

/// <summary>The right-click menu's controls: a caption with a hint, a labeled row, and a slider (bar and dot) like macOS menus have.</summary>
public static class MenuUi
{
    public const double RowWidth = 300;

    /// <summary>
    /// A bar with a dot, 60% to 160% in steps of 5. It applies as the dot moves (<paramref name="live"/>),
    /// a double click goes back to 100%, and <paramref name="done"/> runs when the dot is let go.
    /// </summary>
    public static FrameworkElement Slider(string label, Func<double> get, Action<double> live, Action? start, Action done, double rowWidth = RowWidth, double barWidth = 168)
    {
        const double min = 0.6, max = 1.6;
        var width = barWidth;
        var name = new TextBlock { Text = label, FontSize = 12.5, Foreground = White(0.85), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var value = new TextBlock { FontSize = 12, FontWeight = FontWeights.Medium, Foreground = White(0.7), Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);

        var track = new Grid { Width = width + 16, Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        var bar = new Grid { Width = width, Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = White(0.2) });
        var fill = new Border { CornerRadius = new CornerRadius(2), Background = White(0.85), HorizontalAlignment = HorizontalAlignment.Left };
        bar.Children.Add(fill);
        // A mark at 100%, so "as designed" is easy to find.
        var mark = new Border { Width = 1.5, Height = 10, Background = White(0.4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8 + (1 - min) / (max - min) * width - 0.75, 0, 0, 0), IsHitTestVisible = false };
        var pop = new ScaleTransform(1, 1);
        var thumb = new Ellipse
        {
            Width = 16, Height = 16, Fill = Brushes.White, Stroke = Black(0.3), StrokeThickness = 0.6,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = pop, IsHitTestVisible = false,
        };
        track.Children.Add(mark);
        track.Children.Add(bar);
        track.Children.Add(thumb);

        double shown = double.NaN;
        void Show(double v)
        {
            shown = v;
            var f = Math.Clamp((v - min) / (max - min), 0, 1);
            fill.Width = Math.Max(0, f * width);
            thumb.Margin = new Thickness(8 + f * width - 8, 0, 0, 0);
            value.Text = $"{v * 100:0}%";
        }
        void Set(double x)
        {
            var f = Math.Clamp((x - 8) / width, 0, 1);
            var v = Math.Round((min + f * (max - min)) * 20) / 20;
            if (Math.Abs(v - 1) < 0.03) v = 1;
            if (Math.Abs(v - shown) < 1e-6) return;
            Show(v);
            live(v);
        }
        bool dragging = false;
        track.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { Show(1); live(1); done(); return; }
            dragging = track.CaptureMouse();
            start?.Invoke();
            Motion.Spring(pop, ScaleTransform.ScaleXProperty, 1.25, 0.2, 0.6);
            Motion.Spring(pop, ScaleTransform.ScaleYProperty, 1.25, 0.2, 0.6);
            Set(e.GetPosition(track).X);
            e.Handled = true;
        };
        track.MouseMove += (_, e) => { if (dragging) Set(e.GetPosition(track).X); };
        void Release()
        {
            if (!dragging) return;
            dragging = false;
            track.ReleaseMouseCapture();
            Motion.Spring(pop, ScaleTransform.ScaleXProperty, 1, 0.3, 0.6);
            Motion.Spring(pop, ScaleTransform.ScaleYProperty, 1, 0.3, 0.6);
            done();
        }
        track.MouseLeftButtonUp += (_, _) => Release();
        track.LostMouseCapture += (_, _) => Release();
        Show(Math.Clamp(get(), min, max));

        var grid = new Grid { Width = rowWidth, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(name);
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }
}
