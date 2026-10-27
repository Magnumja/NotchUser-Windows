using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NotchUser;

/// <summary>Saved in %AppData%\NotchUser\settings.json.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "system";
    public bool PeekOnTrackChange { get; set; } = true;

    public static Settings Current { get; } = Load();

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
        }
    }
}
