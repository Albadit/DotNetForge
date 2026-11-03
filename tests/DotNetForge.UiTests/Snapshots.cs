using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace DotNetForge.UiTests;

/// <summary>
/// Screenshot baselines (.docs/guides/testing.md#ui-tests): <c>Baselines/&lt;os&gt;/&lt;name&gt;.png</c> next to this
/// file - one set per operating system, because fonts render differently; git-ignored, so each machine records its
/// own. A missing baseline is recorded and the check passes; <c>DNF_UI_UPDATE_SNAPSHOTS=1</c> re-records every one.
/// Otherwise the screenshot must match: at most <see cref="MaxDifferentPixels"/> of the pixels may differ by more than
/// <see cref="ChannelTolerance"/> in any colour channel (anti-aliasing noise). On a mismatch the actual image and a
/// diff are written to TestResults/ui-snapshots/.
/// </summary>
internal static class Snapshots
{
    public const double MaxDifferentPixels = 0.00002;

    /// <summary>Never fewer than this many pixels may differ: a few anti-aliasing pixels move with sub-pixel layout.</summary>
    public const int MinAllowedPixels = 12;
    public const int ChannelTolerance = 24;

    private static readonly string BaselineDir = Path.Combine(ProjectDir(), "Baselines", Os());

    private static string Os() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
        : "linux";

    private static string ProjectDir([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    private static bool Update => Environment.GetEnvironmentVariable("DNF_UI_UPDATE_SNAPSHOTS") == "1";

    /// <summary>Null when the screenshot matches (or a baseline was recorded); otherwise why it does not.</summary>
    public static string? Compare(string name, byte[] png)
    {
        var baselinePath = Path.Combine(BaselineDir, name + ".png");
        if (Update || !File.Exists(baselinePath))
        {
            Directory.CreateDirectory(BaselineDir);
            File.WriteAllBytes(baselinePath, png);
            return null;
        }

        using var expected = SKBitmap.Decode(baselinePath);
        using var actual = SKBitmap.Decode(png);
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return Fail(name, png, null, $"size {actual.Width}x{actual.Height}, baseline {expected.Width}x{expected.Height}");
        }

        // The outermost pixel row and column are left out: an element at a fractional position shares them with its
        // neighbours, so they change with content elsewhere on the page.
        using var diff = new SKBitmap(actual.Width, actual.Height);
        var different = 0;
        for (var y = 1; y < actual.Height - 1; y++)
        {
            for (var x = 1; x < actual.Width - 1; x++)
            {
                var a = actual.GetPixel(x, y);
                var e = expected.GetPixel(x, y);
                var off = Math.Abs(a.Red - e.Red) > ChannelTolerance || Math.Abs(a.Green - e.Green) > ChannelTolerance ||
                          Math.Abs(a.Blue - e.Blue) > ChannelTolerance || Math.Abs(a.Alpha - e.Alpha) > ChannelTolerance;
                if (off)
                {
                    different++;
                }

                diff.SetPixel(x, y, off ? SKColors.Red : e.WithAlpha(40));
            }
        }

        var ratio = (double)different / (actual.Width * actual.Height);
        return different <= Math.Max(MinAllowedPixels, MaxDifferentPixels * actual.Width * actual.Height) ? null : Fail(name, png, diff, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{different} pixels ({ratio:P3}) differ"));
    }

    private static string Fail(string name, byte[] png, SKBitmap? diff, string reason)
    {
        var dir = Path.Combine(ProjectDir(), "TestResults", "ui-snapshots");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name + ".actual.png"), png);
        if (diff is not null)
        {
            using var image = SKImage.FromBitmap(diff);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, name + ".diff.png"), data.ToArray());
        }

        return $"{name}: {reason} (see {dir}).";
    }
}
