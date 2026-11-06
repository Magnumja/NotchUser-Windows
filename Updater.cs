using System.Net.Http;
using System.Text.Json;

namespace NotchUser;

/// <summary>
/// Once a day (only if turned on), asks GitHub whether a newer release exists. Nothing but the
/// request itself leaves the PC, and nothing is downloaded or installed on its own.
/// </summary>
public static class Updater
{
    public const string ReleasesPage = "https://github.com/Magnumja/NotchUser-Windows/releases/latest";
    public static string? NewVersion { get; private set; }

    public static async Task<string?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NotchUser-Windows");
            var json = await http.GetStringAsync("https://api.github.com/repos/Magnumja/NotchUser-Windows/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            var mine = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (tag != null && Version.TryParse(tag, out var theirs) && mine != null && theirs > mine)
                NewVersion = tag;
        }
        catch { }
        return NewVersion;
    }
}
