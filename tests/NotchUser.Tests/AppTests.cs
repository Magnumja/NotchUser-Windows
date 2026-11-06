using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NotchUser;
using Xunit;

namespace NotchUser.Tests;

public class StringsTests
{
    private static Dictionary<string, Dictionary<string, string>> All() =>
        (Dictionary<string, Dictionary<string, string>>)typeof(Strings)
            .GetField("All", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void EveryTextExistsInEnglishPortugueseAndSpanish()
    {
        foreach (var (key, texts) in All())
            foreach (var language in new[] { "en", "pt", "es" })
                Assert.True(texts.TryGetValue(language, out var text) && text.Trim().Length > 0, $"'{key}' has no {language} text");
    }

    [Fact]
    public void PlaceholdersMatchAcrossLanguages()
    {
        foreach (var (key, texts) in All())
        {
            string[] Holes(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v).ToArray();
            var english = Holes(texts["en"]);
            Assert.True(english.SequenceEqual(Holes(texts["pt"])), $"'{key}': pt placeholders differ");
            Assert.True(english.SequenceEqual(Holes(texts["es"])), $"'{key}': es placeholders differ");
        }
    }

    [Fact]
    public void AnUnknownKeyComesBackAsItself() => Assert.Equal("nope.nothing", Strings.L("nope.nothing"));
}

public class LyricsTests
{
    [Fact]
    public void ParsesTimedLinesInOrder()
    {
        var lines = Lyrics.Parse("[00:12.50] second\n[00:05.00] first\nno tag here\n[01:00.00]");
        Assert.Equal(3, lines.Count);
        Assert.Equal("first", lines[0].Text);
        Assert.Equal(12.5, lines[1].Time, 3);
        Assert.Equal(60, lines[2].Time, 3);
    }

    [Fact]
    public void FindsTheLineBeingSung()
    {
        var lines = Lyrics.Parse("[00:05.00] a\n[00:10.00] b\n[00:20.00] c");
        Assert.Equal(-1, Lyrics.IndexAt(lines, 1));
        Assert.Equal(0, Lyrics.IndexAt(lines, 5));
        Assert.Equal(1, Lyrics.IndexAt(lines, 19.9));
        Assert.Equal(2, Lyrics.IndexAt(lines, 999));
    }

    [Fact]
    public void EmptyInputGivesNoLines() => Assert.Empty(Lyrics.Parse(null));
}

public class SettingsTests
{
    [Fact]
    public void TheDefaultsAreWhatTheReadmePromises()
    {
        var s = new Settings();
        Assert.Equal("elevated", s.Style);
        Assert.Equal("top", s.Dock);
        Assert.Equal(0.5, s.DockOffset);
        Assert.Equal(1, s.ClosedSize);
        Assert.Equal(1, s.OpenSize);
        Assert.False(s.CheckUpdates);   // nothing leaves the PC unless asked
        Assert.False(s.LyricsEnabled);
        Assert.False(s.OcrEnabled);
    }

    [Fact]
    public void AnOldSettingsFileStillLoadsAndGetsTheNewLook()
    {
        var old = JsonSerializer.Deserialize<Settings>("{\"Style\":\"basic\",\"Language\":\"pt\",\"Onboarded\":true}")!;
        Assert.Equal("pt", old.Language);
        Assert.Equal("elevated", old.Style);
    }

    [Fact]
    public void SettingsSurviveARoundTrip()
    {
        var s = new Settings { Look = "glass", Dock = "left", DockOffset = 0.3, ClosedSize = 0.8, OpenSize = 1.3, Motion = "lively" };
        var back = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.Equal("glass", back.Style);
        Assert.Equal("left", back.Dock);
        Assert.Equal(0.3, back.DockOffset);
        Assert.Equal(0.8, back.ClosedSize);
        Assert.Equal(1.3, back.OpenSize);
        Assert.Equal("lively", back.Motion);
    }

    [Fact]
    public void TheBiggerOfTheTwoSizesSetsTheLayoutAndTheOtherIsDrawnSmaller()
    {
        Settings.MaxMulOverride = 0;
        var s = new Settings { ClosedSize = 0.7, OpenSize = 1.3 };
        Assert.Equal(s.Scale * 1.3, s.LayoutScale, 6);
        Assert.Equal(1.0, s.StateFactor(open: true), 6);
        Assert.Equal(0.7 / 1.3, s.StateFactor(open: false), 6);
        // Both smaller than designed: the layout stays at 100% and each is drawn at its own factor.
        var small = new Settings { ClosedSize = 0.6, OpenSize = 0.8 };
        Assert.Equal(small.Scale, small.LayoutScale, 6);
        Assert.Equal(0.8, small.StateFactor(open: true), 6);
    }

    [Fact]
    public void NonsenseSizesAreKeptInRange()
    {
        Settings.MaxMulOverride = 0;
        var s = new Settings { ClosedSize = 0, OpenSize = 99 };
        Assert.InRange(s.StateFactor(open: false), 0.01, 1);
        Assert.InRange(s.StateFactor(open: true), 0.01, 1);
        Assert.True(s.LayoutScale <= s.Scale * 2.0001);
    }

    [Fact]
    public void ThePresetSizesAreSortedAndIncludeOneHundredPercent()
    {
        Assert.Equal(Settings.SizeSteps.OrderBy(v => v), Settings.SizeSteps);
        Assert.Contains(1.0, Settings.SizeSteps);
    }

    [Fact]
    public void HidingAppsKeepsTheirIdsDistinctEnoughToMatch() =>
        Assert.Contains("spotify.exe", new List<string> { "Spotify.exe" }, StringComparer.OrdinalIgnoreCase);
}

public class DisplaysTests
{
    [Fact]
    public void ThereIsAPrimaryScreenAndTheChosenOneAlwaysExists()
    {
        var all = Displays.All();
        Assert.NotEmpty(all);
        Assert.Contains(all, s => s.Primary);
        // A screen that is no longer connected falls back to the main one.
        var chosen = Displays.Chosen(@"\.DISPLAY99", null);
        Assert.True(chosen.Primary);
        Assert.True(chosen.Width > 0 && chosen.Height > 0);
    }

    [Fact]
    public void AScreenIsFoundByAPointInsideIt()
    {
        var primary = Displays.Primary();
        var found = Displays.At(primary.Left + 10, primary.Top + 10);
        Assert.NotNull(found);
        Assert.Equal(primary.Id, found!.Value.Id);
        Assert.Null(Displays.At(-99999, -99999));
    }
}

public class ShelfActionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notchuser-test-" + Guid.NewGuid().ToString("N"));

    public ShelfActionsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string MakePng(string name, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 20; pixels[i + 3] = 255; }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var path = Path.Combine(_dir, name);
        using (var stream = File.Create(path)) encoder.Save(stream);
        return path;
    }

    private static (int W, int H) SizeOf(string path)
    {
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(path), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    [Fact]
    public void ConvertsAPngToJpegBesideTheOriginal()
    {
        var png = MakePng("photo.png", 40, 20);
        var made = ShelfActions.Convert(new[] { png }, "jpg", 1);
        Assert.Single(made);
        Assert.Equal(".jpg", Path.GetExtension(made[0]));
        Assert.Equal(_dir, Path.GetDirectoryName(made[0]));
        Assert.Equal((40, 20), SizeOf(made[0]));
        Assert.True(File.Exists(png));  // the original is untouched
    }

    [Fact]
    public void HalvesTheSizeAndNeverOverwritesAnExistingFile()
    {
        var png = MakePng("big.png", 100, 60);
        var first = ShelfActions.Convert(new[] { png }, null, 0.5);
        var second = ShelfActions.Convert(new[] { png }, null, 0.5);
        Assert.Equal((50, 30), SizeOf(first[0]));
        Assert.NotEqual(first[0], second[0]);
        Assert.True(File.Exists(first[0]) && File.Exists(second[0]));
    }

    [Fact]
    public void IgnoresFilesThatAreNotPictures()
    {
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "hello");
        Assert.Empty(ShelfActions.Convert(new[] { text, Path.Combine(_dir, "missing.png") }, "png", 1));
    }

    [Fact]
    public void ZipsFilesAndFoldersTogetherWithFreshNames()
    {
        var file = Path.Combine(_dir, "a.txt");
        File.WriteAllText(file, "A");
        var folder = Path.Combine(_dir, "stuff");
        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        File.WriteAllText(Path.Combine(folder, "inner", "b.txt"), "B");

        var zip = ShelfActions.Zip(new[] { file, folder });
        Assert.Equal("NotchUser.zip", Path.GetFileName(zip));
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "a.txt", "stuff/inner/b.txt" }, names);
        }
        var again = ShelfActions.Zip(new[] { file, folder });
        Assert.Equal("NotchUser (2).zip", Path.GetFileName(again));
        Assert.Equal("a.zip", Path.GetFileName(ShelfActions.Zip(new[] { file })));
    }
}
