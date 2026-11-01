using System.Runtime.InteropServices;

namespace NotchUser;

/// <summary>The battery, for the charging and low-battery notices.</summary>
public static class Power
{
    public readonly record struct State(bool HasBattery, bool Charging, int Percent);

    public static State? Read()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        // 128 = no battery (a desktop), 255 = unknown.
        var hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255 && s.BatteryLifePercent <= 100;
        return new State(hasBattery, s.ACLineStatus == 1, hasBattery ? s.BatteryLifePercent : 100);
    }

    /// <summary>
    /// While a game or a video has the screen: Windows' efficiency mode (EcoQoS, the cores and
    /// clocks that save power) and below-normal priority, so the notch never competes with it.
    /// </summary>
    public static void Quiet(bool on)
    {
        try
        {
            var state = new ThrottlingState { Version = 1, ControlMask = 1 /* execution speed */, StateMask = on ? 1u : 0u };
            SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state, Marshal.SizeOf<ThrottlingState>());
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                on ? System.Diagnostics.ProcessPriorityClass.BelowNormal : System.Diagnostics.ProcessPriorityClass.Normal;
        }
        catch
        {
            // Older Windows: no efficiency mode, nothing else changes.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThrottlingState { public uint Version, ControlMask, StateMask; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref ThrottlingState info, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
