using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotchUser;

/// <summary>
/// Pictures from the Windows shell, the same ones File Explorer shows: thumbnails of files
/// (photos, PDFs, videos, documents) and the icons of the apps that play music.
/// </summary>
public static class ShellImage
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new();

    /// <summary>A file's thumbnail (or its icon when it has none), about <paramref name="size"/> px.</summary>
    public static BitmapSource? File(string path, int size) => Cached("f|" + path, () => Get(path, size, iconOnly: false));

    /// <summary>The icon of the app behind a media session (Spotify, Chrome, Media Player…).</summary>
    public static BitmapSource? App(string sourceId) => Cached("a|" + sourceId, () =>
        // Apps with an id (Store apps, Chrome, Firefox, Edge) are in the Start menu's Apps folder;
        // the others report their .exe, whose icon we take from the running process.
        Get(@"shell:AppsFolder\" + sourceId, 64, iconOnly: true)
        ?? (ExePath(sourceId) is { } exe ? Get(exe, 64, iconOnly: true) : null));

    public static void Forget(string path) => Cache.Remove("f|" + path);

    private static BitmapSource? Cached(string key, Func<BitmapSource?> make)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var image = make();
        if (Cache.Count > 200) Cache.Clear();
        Cache[key] = image;
        return image;
    }

    /// <summary>"Spotify.exe" → the path of the running Spotify.exe.</summary>
    public static string? ExePath(string sourceId)
    {
        var name = Path.GetFileNameWithoutExtension(sourceId.Split('!')[0]);
        foreach (var p in ProcessesFor(name))
        {
            try { if (p.MainModule?.FileName is { } file) return file; }
            catch { }  // elevated or protected process
        }
        return null;
    }

    public static IEnumerable<Process> ProcessesFor(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch { return Array.Empty<Process>(); }
    }

    private static BitmapSource? Get(string path, int size, bool iconOnly)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
            var flags = iconOnly ? Siigbf.IconOnly | Siigbf.BiggerSizeOk : Siigbf.BiggerSizeOk;
            if (factory.GetImage(new NativeSize { Width = size, Height = size }, flags, out bitmap) != 0) return null;
            return ToBitmapSource(bitmap);
        }
        catch { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    /// <summary>
    /// The shell hands back a 32-bit bitmap with transparency, which WPF's own conversion
    /// would lose (icons on a black square); copy the pixels ourselves, top row first.
    /// </summary>
    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var info) == 0) return null;
        int w = info.Width, h = Math.Abs(info.Height);
        var header = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        // Some icons come without any alpha at all: then everything is opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        source.Freeze();
        return source;
    }

    [Flags]
    private enum Siigbf { ResizeToFit = 0, BiggerSizeOk = 1, MemoryOnly = 2, IconOnly = 4, ThumbnailOnly = 8 }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, Siigbf flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
