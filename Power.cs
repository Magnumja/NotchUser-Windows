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
