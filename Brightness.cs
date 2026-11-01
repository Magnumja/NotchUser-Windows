using System.Management;
using System.Windows;

namespace NotchUser;

/// <summary>
/// The laptop screen's brightness, for the brightness level in the notch (like the volume).
/// Windows reports each change, from the keys or from Settings; desktops with no built-in
/// screen simply never report anything.
/// </summary>
public sealed class Brightness
{
    private ManagementEventWatcher? _watcher;

    /// <summary>The new level, 0 to 100. Raised on the UI thread.</summary>
    public event Action<int>? Changed;

    public void Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                if (e.NewEvent["Brightness"] is byte level)
                    Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke(level));
            };
            _watcher.Start();
        }
        catch
        {
            // No built-in screen, or WMI unavailable: no brightness in the notch.
            _watcher = null;
        }
    }
}
