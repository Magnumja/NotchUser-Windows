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
            // Arrives on a WMI thread, where an error would end the app: read it carefully, act on the UI thread.
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    if (e.NewEvent["Brightness"] is byte level)
                        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke(level));
                }
                catch (Exception ex) { App.Log(ex); }
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
