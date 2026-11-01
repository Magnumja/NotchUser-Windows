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

        // A mistake in one button or menu must not close the whole notch: it's written to
        // %AppData%\NotchUser\errors.log and the app keeps running.
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
            // The drawing thread itself failed (UCEERR_RENDERTHREADFAILURE): nothing would be drawn
            // anymore, so start over instead of staying frozen.
            if (args.Exception is COMException { HResult: unchecked((int)0x88980406) }) Restart();
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Log(ex); };

        // Drawn by the processor, not the graphics card. A transparent window drawn on the GPU has
        // to be copied back from it on every frame, and on laptops with two GPUs (Intel + NVIDIA)
        // that copy can stall for good: the notch freezes on its last frame (stuck open, or black).
        // The notch is small, so the processor draws it easily, and it never competes with a game.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Motion.UseScreenRate();
        Motion.AnimateMenus();
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

    private void Restart()
    {
        // Let go of the one-instance lock first, or the new copy would see this one and quit.
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();
        _single = null;
        if (Environment.ProcessPath is string exe) System.Diagnostics.Process.Start(exe);
        Shutdown();
    }

    /// <summary>
    /// Step-by-step trace of the notch's state, for tracking down a misbehavior: written to
    /// trace.log only while a file named "trace" exists in %AppData%\NotchUser.
    /// </summary>
    public static void Trace(string what)
    {
        if (!_traceOn) return;
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Folder, "trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {what}\n"); }
        catch { }
    }

    private static readonly bool _traceOn = System.IO.File.Exists(System.IO.Path.Combine(Settings.Folder, "trace"));

    /// <summary>Appends an error to errors.log (kept under 200 KB: the older half goes first).</summary>
    public static void Log(Exception ex)
    {
        try
        {
            var path = System.IO.Path.Combine(Settings.Folder, "errors.log");
            System.IO.Directory.CreateDirectory(Settings.Folder);
            if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 200_000)
            {
                var text = System.IO.File.ReadAllText(path);
                System.IO.File.WriteAllText(path, text[(text.Length / 2)..]);
            }
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{version}\n{ex}\n\n");
        }
        catch
        {
            // Nowhere to write: nothing else to do.
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
