using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace NotchUser;

/// <summary>Saved in %AppData%\NotchUser\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "system";
    /// <summary>
    /// Open the notch for a moment when the song changes. Off by default, as on the Mac: opening
    /// at every song gets in the way (the closed notch still gives a small hop).
    /// </summary>
    public bool PeekOnTrackChange { get; set; }
    /// <summary>
    /// The look: "basic" (vivid black), "elevated" (depth, a lit edge and a glow in the album's color)
    /// or "glass" (a see-through panel with a sheen). All three are drawn without blurs or live shadows.
    /// (A separate name from the old "Style", which every saved file has set to "basic".)
    /// </summary>
    public string Look { get; set; } = "elevated";
    [System.Text.Json.Serialization.JsonIgnore] public string Style => Look is "basic" or "glass" ? Look : "elevated";
    /// <summary>The welcome walkthrough has been seen.</summary>
    public bool Onboarded { get; set; }
    /// <summary>Add-on: copy text from the screen with Ctrl+Shift+2 (off until turned on).</summary>
    public bool OcrEnabled { get; set; }
    /// <summary>Add-on: synced lyrics from LRCLIB (off until turned on: it sends the song's name there).</summary>
    public bool LyricsEnabled { get; set; }
    /// <summary>Add-on: Ctrl+Alt+N opens and closes the notch from the keyboard.</summary>
    public bool ToggleHotkey { get; set; }
    /// <summary>Add-on: Ctrl+Alt+M mutes and unmutes the microphone, with a notice in the notch.</summary>
    public bool MicHotkey { get; set; }
    /// <summary>Show the volume in the notch when it changes.</summary>
    public bool VolumeHud { get; set; } = true;
    /// <summary>Battery and audio device notices in the notch.</summary>
    public bool Notices { get; set; } = true;
    /// <summary>Notch size: 0 = automatic, else a scale like 1.15.</summary>
    public double Size { get; set; }
    /// <summary>How big the closed pill is next to the overall Size (1 = as designed): 0.5 to 2.</summary>
    public double ClosedSize { get; set; } = 1;
    /// <summary>The same for the open panel.</summary>
    public double OpenSize { get; set; } = 1;
    public static readonly double[] SizeSteps = { 0.7, 0.8, 0.9, 1.0, 1.15, 1.3, 1.5 };

    private static double Clamp(double v) => v <= 0 ? 1 : Math.Clamp(v, 0.5, 2);

    /// <summary>While the menu is open the layout is kept at this size (0 = off), so dragging a size slider never moves the menu.</summary>
    public static double MaxMulOverride { get; set; }

    private double MaxMul => MaxMulOverride > 0 ? MaxMulOverride : Math.Max(1, Math.Max(Clamp(ClosedSize), Clamp(OpenSize)));

    /// <summary>
    /// The window and layout are drawn at the biggest of the two sizes; the notch itself is then
    /// drawn smaller by <see cref="StateFactor"/> (and springs between the two as it opens and closes).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double LayoutScale => Scale * MaxMul;

    public double StateFactor(bool open) => Clamp(open ? OpenSize : ClosedSize) / MaxMul;

    /// <summary>Tells every window about a change that is not saved yet (a slider being dragged).</summary>
    public void Preview() => Changed?.Invoke();

    /// <summary>What shows the music is playing: "bars" (the Mac's Basic) or "wave" (the Mac's Glass wave).</summary>
    public string Indicator { get; set; } = "bars";
    /// <summary>With nothing playing and an empty shelf, the closed pill shows the time (off: just the pill, as on the Mac).</summary>
    public bool ClockInPill { get; set; }
    /// <summary>Apps whose media never shows in the notch (e.g. a browser playing videos), by app id.</summary>
    public List<string> HiddenApps { get; set; } = new();

    /// <summary>Which screen: "primary", "follow" (the one the mouse reaches the top of) or a screen's id.</summary>
    public string Display { get; set; } = "primary";
    /// <summary>How lively the animations are: "calm", "standard" or "lively".</summary>
    public string Motion { get; set; } = "standard";
    /// <summary>What opens the notch: "hover" or "click" (for those who open it by accident).</summary>
    public string OpenOn { get; set; } = "hover";
    /// <summary>With nothing playing and nothing on the shelf, the pill disappears until the mouse reaches the top.</summary>
    public bool HideWhenIdle { get; set; }
    /// <summary>Once a day, look for a newer release on GitHub (off until turned on).</summary>
    public bool CheckUpdates { get; set; }
    public string LastUpdateCheck { get; set; } = "";

    /// <summary>"island": a floating pill a little below the edge (a Dynamic Island). "notch": hangs from the edge like the Mac's.</summary>
    public string Shape { get; set; } = "island";
    public bool Island => Shape != "notch";

    /// <summary>Where the notch sits: "top", "bottom", "left" or "right" of the screen.</summary>
    public string Dock { get; set; } = "top";
    /// <summary>How far along that edge, 0 to 1 (0.5 = the middle).</summary>
    public double DockOffset { get; set; } = 0.5;

    public bool Lifted => Style != "basic";
    public bool Glass => Style == "glass";
    public bool Wave => Indicator == "wave";

    /// <summary>The scale actually used: the chosen one, or one that suits the screen.</summary>
    public double Scale => Size > 0 ? Size : SystemParameters.PrimaryScreenWidth >= 2400 ? 1.15 : 1.0;

    public static Settings Current { get; } = Load();

    /// <summary>Raised after any setting changes, so every window can follow.</summary>
    public static event Action? Changed;

    public static string Folder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchUser");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    private static Settings Load() => ReadJson<Settings>(FilePath) ?? new Settings();

    public void Save()
    {
        WriteJson(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
    }

    /// <summary>
    /// Reads a saved file. A damaged one is kept aside as .bak (instead of being overwritten
    /// on the next save) and the app starts from the defaults.
    /// </summary>
    public static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch
        {
            try { File.Copy(path, path + ".bak", true); } catch { }
            return null;
        }
    }

    /// <summary>Writes to a temporary file first, so a crash or power cut never leaves half a file.</summary>
    public static void WriteJson(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
        }
        catch
        {
            // Disk full or locked by an antivirus: keep running with what's in memory.
        }
    }

    // "Start with Windows" lives in the registry, not in the settings file.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("NotchUser") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is string exe)
                key.SetValue("NotchUser", $"\"{exe}\"");
            else
                key.DeleteValue("NotchUser", false);
            Changed?.Invoke();
        }
    }
}
