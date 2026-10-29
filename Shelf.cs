using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotchUser;

/// <summary>A file path, a web link or a piece of text parked on the shelf.</summary>
public sealed record ShelfEntry(string Kind, string Value)
{
    [JsonIgnore]
    public string Title => Kind switch
    {
        "file" => System.IO.Path.GetFileName(Value),
        "link" => Uri.TryCreate(Value, UriKind.Absolute, out var u) ? u.Host : Value,
        _ => Value.Trim().Split('\n')[0] is { Length: > 0 } first ? first.Trim() : Strings.L("text"),
    };
}

/// <summary>The shelf's items, saved in %AppData%\NotchUser\shelf.json.</summary>
public sealed class Shelf
{
    public List<ShelfEntry> Items { get; private set; } = new();

    private static string FilePath => System.IO.Path.Combine(Settings.Folder, "shelf.json");

    public static Shelf Load()
    {
        var items = Settings.ReadJson<List<ShelfEntry>>(FilePath) ?? new();
        return new Shelf { Items = items.Where(e => e.Kind != "file" || Exists(e.Value)).ToList() };
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Files moved or deleted elsewhere drop off the shelf. True if anything went.</summary>
    public bool Prune()
    {
        var gone = Items.RemoveAll(e => e.Kind == "file" && !Exists(e.Value));
        if (gone > 0) Save();
        return gone > 0;
    }

    public bool Add(ShelfEntry e)
    {
        if (Items.Contains(e)) return false;
        Items.Add(e);
        Save();
        return true;
    }

    public void Remove(IEnumerable<ShelfEntry> entries)
    {
        var gone = entries.ToHashSet();
        Items.RemoveAll(gone.Contains);
        Save();
    }

    public void Clear()
    {
        Items.Clear();
        Save();
    }

    private void Save() => Settings.WriteJson(FilePath, JsonSerializer.Serialize(Items));
}
