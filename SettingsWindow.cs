using System.Windows;
using System.Windows.Controls;
using static NotchUser.Strings;

namespace NotchUser;

/// <summary>Language, add-ons and behavior. Opened from the pill's right-click menu.</summary>
public sealed class SettingsWindow : GlassWindow
{
    private static SettingsWindow? _open;

    public static void ShowOrFocus()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private SettingsWindow() : base(440, 520) => Build();

    private void Build()
    {
        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        var icon = Ui.AppIcon(56);
        stack.Children.Add(icon);
        var title = Ui.Text(L("settingsTitle"), 20, FontWeights.Bold, align: TextAlignment.Center);
        title.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(title);

        // Changing the language rebuilds the window in the new language.
        stack.Children.Add(Ui.Section(L("language"), Ui.LanguagePicker(Build)));
        stack.Children.Add(Ui.Section(L("addons"), Ui.AddOns()));

        var behavior = new StackPanel();
        behavior.Children.Add(Ui.Switch(L("peekOnTrack"), Settings.Current.PeekOnTrackChange, on =>
        {
            Settings.Current.PeekOnTrackChange = on;
            Settings.Current.Save();
        }));
        behavior.Children.Add(Ui.Switch(L("launchAtLogin"), Settings.LaunchAtLogin, on => Settings.LaunchAtLogin = on));
        stack.Children.Add(Ui.Section(L("behavior"), behavior));

        var done = Ui.Primary(L("done"), Close);
        done.Margin = new Thickness(0, 10, 0, 0);
        done.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(done);
        Card.Child = stack;
    }
}
