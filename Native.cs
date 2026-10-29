using System.Runtime.InteropServices;

namespace NotchUser;

/// <summary>Small Win32 helpers shared by the notch.</summary>
public static class Native
{
    /// <summary>
    /// Restore and focus another app's window. The notch never takes focus itself, so Windows
    /// would refuse the switch; a tap of Alt (what Alt+Tab does) makes it allowed.
    /// </summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);       // Alt down
        keybd_event(0x12, 0, 2, UIntPtr.Zero);       // Alt up
        SetForegroundWindow(hwnd);
    }

    /// <summary>The left mouse button is held down right now (anywhere on screen).</summary>
    public static bool LeftButtonDown => (GetAsyncKeyState(GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01) & 0x8000) != 0;

    /// <summary>Cursor position in screen pixels.</summary>
    public static (int X, int Y) Cursor => GetCursorPos(out var p) ? (p.X, p.Y) : (int.MinValue, int.MinValue);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}
