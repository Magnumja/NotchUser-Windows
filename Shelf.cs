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
        var shelf = new Shelf();
        try
        {
            var items = JsonSerializer.Deserialize<List<ShelfEntry>>(File.ReadAllText(FilePath)) ?? new();
            // Files moved or deleted elsewhere drop off the shelf.
            shelf.Items = items.Where(e => e.Kind != "file" || File.Exists(e.Value) || Directory.Exists(e.Value)).ToList();
        }
        catch { }
        return shelf;
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

    private void Save()
    {
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Items));
    }
}
