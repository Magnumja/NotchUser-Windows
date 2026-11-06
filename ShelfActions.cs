using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchUser;

/// <summary>What the shelf can do to files: compress them, or convert and shrink pictures. Plain file work, no windows.</summary>
public static class ShelfActions
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic" };

    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path);

    /// <summary>A name that is free: "photo.zip", then "photo (2).zip", "photo (3).zip"...</summary>
    public static string FreeName(string folder, string name, string extension)
    {
        var target = Path.Combine(folder, name + extension);
        for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{name} ({n}){extension}");
        return target;
    }

    /// <summary>Puts files and folders in one ZIP beside the first of them. Returns the new file.</summary>
    public static string Zip(IReadOnlyList<string> paths)
    {
        var first = paths[0].TrimEnd('\\', '/');
        var folder = Path.GetDirectoryName(first) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var name = paths.Count == 1 ? Path.GetFileNameWithoutExtension(first) : "NotchUser";
        var zip = FreeName(folder, name, ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var root = path.TrimEnd('\\', '/');
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    archive.CreateEntryFromFile(file, Path.GetFileName(root) + "/" + Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
            else archive.CreateEntryFromFile(path, Path.GetFileName(path));
        }
        return zip;
    }

    /// <summary>
    /// Converts pictures to "png" or "jpg" (null keeps the type: JPEG stays JPEG, the rest become PNG),
    /// optionally scaled (0.5 = half the size). The results sit beside the originals; their paths come back.
    /// </summary>
    public static List<string> Convert(IEnumerable<string> sources, string? format, double scale)
    {
        var results = new List<string>();
        foreach (var path in sources.Where(IsImage))
        {
            BitmapSource image = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (scale != 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            var extension = format ?? (Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" ? "jpg" : "png");
            BitmapEncoder encoder = extension == "jpg" ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            var target = FreeName(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + (scale != 1 ? " (50%)" : ""), "." + extension);
            using (var stream = File.Create(target)) encoder.Save(stream);
            results.Add(target);
        }
        return results;
    }
}
