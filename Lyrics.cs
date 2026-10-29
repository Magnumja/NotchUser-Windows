using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotchUser;

/// <summary>One line of a song's lyrics and when it starts.</summary>
public readonly record struct LyricLine(double Time, string Text);

/// <summary>
/// Synced lyrics from LRCLIB (lrclib.net): free, no account, open data. Only the song's title,
/// artist, album and length are sent, and only while the add-on is on.
/// </summary>
public static partial class Lyrics
{
    private static readonly HttpClient Http = CreateClient();
    // A few recent songs, so going back and forth doesn't ask again.
    private static readonly Dictionary<string, IReadOnlyList<LyricLine>> Cache = new();
    private static readonly Queue<string> CacheOrder = new();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(8) };
        // LRCLIB asks apps to say who they are.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NotchUser-Windows (https://github.com/Magnumja/NotchUser-Windows)");
        return client;
    }

    /// <summary>The synced lines, or an empty list when the song has none (or it's offline).</summary>
    public static async Task<IReadOnlyList<LyricLine>> FindAsync(string title, string artist, string album, double duration)
    {
        var key = $"{title}\n{artist}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        IReadOnlyList<LyricLine> lines = Array.Empty<LyricLine>();
        try
        {
            // Exact match first (it also uses the length to tell versions apart), then a search.
            var query = $"track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            var exact = query + (album.Length > 0 ? $"&album_name={Uri.EscapeDataString(album)}" : "")
                        + (duration > 0 ? $"&duration={Math.Round(duration)}" : "");
            lines = Parse(await SyncedAsync("api/get?" + exact));
            if (lines.Count == 0) lines = Parse(await SyncedAsync("api/search?" + query));
        }
        catch
        {
            // Offline or LRCLIB is down: no lyrics this time, and don't remember the miss.
            return lines;
        }

        Cache[key] = lines;
        CacheOrder.Enqueue(key);
        if (CacheOrder.Count > 20) Cache.Remove(CacheOrder.Dequeue());
        return lines;
    }

    /// <summary>The "syncedLyrics" text of the answer (the first result, for a search).</summary>
    private static async Task<string?> SyncedAsync(string path)
    {
        using var response = await Http.GetAsync(path);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (Synced(item) is { } text) return text;
            return null;
        }
        return Synced(root);
    }

    private static string? Synced(JsonElement item) =>
        item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } text
            ? text : null;

    [GeneratedRegex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]")]
    private static partial Regex TimeTag();

    /// <summary>"[01:02.50] words" lines (a line can carry several times) into a sorted list.</summary>
    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrEmpty(lrc)) return lines;
        foreach (var raw in lrc.Split('\n'))
        {
            var tags = TimeTag().Matches(raw);
            if (tags.Count == 0) continue;
            var text = raw[(tags[^1].Index + tags[^1].Length)..].Trim();
            foreach (Match tag in tags)
            {
                var minutes = int.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(tag.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(minutes * 60 + seconds, text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>The line being sung at <paramref name="position"/>, or -1 before the first one.</summary>
    public static int IndexAt(IReadOnlyList<LyricLine> lines, double position)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
}
