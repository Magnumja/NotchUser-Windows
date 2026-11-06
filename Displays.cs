using System.Runtime.InteropServices;

namespace NotchUser;

/// <summary>The connected screens, in real pixels (no WinForms needed), and where the notch sits on each.</summary>
public static class Displays
{
    public readonly record struct Screen(string Id, int Left, int Top, int Right, int Bottom, bool Primary, int WLeft, int WTop, int WRight, int WBottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static List<Screen> All()
    {
        var list = new List<Screen>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref Rect r, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
                list.Add(new Screen(info.Device, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom, (info.Flags & 1) != 0,
                    info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(s => s.Primary).ThenBy(s => s.Left).ToList();
    }

    public static Screen Primary() => All().FirstOrDefault(s => s.Primary);

    /// <summary>The screen the setting names; the main one if it is not connected anymore.</summary>
    public static Screen Chosen(string setting, Screen? current)
    {
        var all = All();
        if (all.Count == 0) return default;
        if (setting == "follow" && current is { } c && all.Any(s => s.Id == c.Id)) return all.First(s => s.Id == c.Id);
        return all.FirstOrDefault(s => s.Id == setting, all.First(s => s.Primary));
    }

    public static Screen? At(int x, int y) =>
        All().Cast<Screen?>().FirstOrDefault(s => x >= s!.Value.Left && x < s.Value.Right && y >= s.Value.Top && y < s.Value.Bottom);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size; public Rect Monitor, Work; public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
