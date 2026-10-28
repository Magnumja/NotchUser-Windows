using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NotchUser;

/// <summary>Saved in %AppData%\NotchUser\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "system";
    public bool PeekOnTrackChange { get; set; } = true;
    /// <summary>The welcome walkthrough has been seen.</summary>
    public bool Onboarded { get; set; }
    /// <summary>Add-on: copy text from the screen with Ctrl+Shift+2 (off until turned on).</summary>
    public bool OcrEnabled { get; set; }

    public static Settings Current { get; } = Load();

    /// <summary>Raised after any setting changes, so every window can follow.</summary>
    public static event Action? Changed;

    public static string Folder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchUser");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    private static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        Changed?.Invoke();
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
