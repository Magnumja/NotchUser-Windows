using System.Windows;

namespace NotchUser;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // One NotchUser at a time.
        _single = new Mutex(true, "NotchUser.SingleInstance", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
