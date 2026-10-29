using System.Runtime.InteropServices;
using System.Text;

namespace NotchUser;

/// <summary>Whether the app in front fills the whole screen the notch is on (a game, a video, slides).</summary>
public static class FullScreen
{
    public static bool IsActive(IntPtr notch)
    {
        var front = GetForegroundWindow();
        if (front == IntPtr.Zero || front == GetShellWindow() || front == GetDesktopWindow()) return false;
        // Our own windows (like the text selection overlay, which covers the screen) don't count.
        GetWindowThreadProcessId(front, out var process);
        if (process == Environment.ProcessId) return false;
        // Clicking the desktop makes it the front window, and it does cover the screen.
        var name = new StringBuilder(32);
        GetClassName(front, name, name.Capacity);
        if (name.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd") return false;

        if (!GetWindowRect(front, out var window)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(notch, 2 /* nearest */), ref info)) return false;
        var screen = info.Monitor;
        // A maximized window stops at the taskbar; only a full-screen one covers it too.
        return window.Left <= screen.Left && window.Top <= screen.Top
               && window.Right >= screen.Right && window.Bottom >= screen.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int process);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
