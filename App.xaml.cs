using System.Runtime.InteropServices;
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
        if (!Settings.Current.Onboarded) new OnboardingWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Hand unused memory back to Windows, e.g. after a big window (Settings, the text
    /// selection overlay) closes. The notch itself is small and stays.
    /// </summary>
    public static void TrimMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
