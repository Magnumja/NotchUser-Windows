using Microsoft.Win32;

namespace NotchUser;

/// <summary>
/// Whether an app is using the microphone or the camera right now, for the privacy dot (the Mac's
/// orange and green dots). Windows records each app's use in the registry: an app that started
/// and hasn't stopped yet is using it.
/// </summary>
public static class Privacy
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static (bool Microphone, bool Camera) InUse() => (Any("microphone"), Any("webcam"));

    private static bool Any(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Store + capability);
            return key != null && Any(key);
        }
        catch
        {
            return false;
        }
    }

    private static bool Any(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var app = key.OpenSubKey(name);
            if (app == null) continue;
            // Store apps sit right here; other apps one level down, one key per program.
            if (name == "NonPackaged") { if (Any(app)) return true; continue; }
            if (app.GetValue("LastUsedTimeStart") is long start && start > 0 && app.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                return true;
        }
        return false;
    }
}
