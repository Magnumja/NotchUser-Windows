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
    /// <summary>How the notch looks: "basic" (plain black), "elevated" (depth and glow) or "glass".</summary>
    public string Style { get; set; } = "basic";
    /// <summary>The welcome walkthrough has been seen.</summary>
    public bool Onboarded { get; set; }
    /// <summary>Add-on: copy text from the screen with Ctrl+Shift+2 (off until turned on).</summary>
    public bool OcrEnabled { get; set; }
    /// <summary>Add-on: synced lyrics from LRCLIB (off until turned on: it sends the song's name there).</summary>
    public bool LyricsEnabled { get; set; }
    /// <summary>Show the volume in the notch when it changes.</summary>
    public bool VolumeHud { get; set; } = true;
    /// <summary>Battery and audio device notices in the notch.</summary>
    public bool Notices { get; set; } = true;
    /// <summary>Notch size: 0 = automatic, else a scale like 1.15.</summary>
    public double Size { get; set; }

    public bool Lifted => Style != "basic";
    public bool Glass => Style == "glass";

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
