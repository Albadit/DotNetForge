using SkiaSharp;

namespace DotNetForge.Web.Services;

/// <summary>
/// Shrinks uploaded images to save storage (.docs/features/media-storage.md#optimization), with SkiaSharp: JPEG, PNG
/// and still WebP are turned upright (EXIF orientation), scaled down to the configured box, and re-encoded at the
/// configured quality - as WebP when <see cref="MediaSettings.ConvertToWebP"/> is on. Metadata (EXIF, GPS) is not
/// carried over. GIF and AVIF, animations and images that can't be decoded are left alone, and so is any result
/// that is not smaller than the upload.
/// </summary>
public static class ImageOptimizer
{
    /// <summary>Larger images are stored as uploaded: decoding them would need too much memory (4 bytes per pixel).</summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>The optimized image: bytes, the extension and content type to store it under, and its size in pixels.</summary>
    public sealed record Result(byte[] Data, string Extension, string ContentType, int Width, int Height);

    /// <summary>The image types this optimizer handles (by extension, as <see cref="MediaService.AllowedTypes"/> maps them).</summary>
    public static bool Handles(string extension) =>
        extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    /// <summary>The smaller version of <paramref name="input"/>, or null when there is none (see the class summary).</summary>
    public static Result? Optimize(byte[] input, string extension, MediaSettings settings)
    {
        if (!Handles(extension))
        {
            return null;
        }

        using var data = SKData.CreateCopy(input);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.FrameCount > 1 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var bitmap = upright ?? decoded;

        var scale = Math.Min(1d, Math.Min((double)settings.ImageMaxWidth / bitmap.Width, (double)settings.ImageMaxHeight / bitmap.Height));
        using var resized = scale < 1
            ? bitmap.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)),
                bitmap.ColorType, bitmap.AlphaType, bitmap.ColorSpace), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : null;
        var output = resized ?? bitmap;

        var format = settings.ConvertToWebP ? SKEncodedImageFormat.Webp
            : codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Png => SKEncodedImageFormat.Png,
                SKEncodedImageFormat.Webp => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Jpeg,
            };
        using var encoded = output.Encode(format, settings.ImageQuality);
        if (encoded is null || encoded.Size >= input.Length)
        {
            return null;
        }

        var (ext, type) = format switch
        {
            SKEncodedImageFormat.Webp => (".webp", "image/webp"),
            SKEncodedImageFormat.Png => (".png", "image/png"),
            _ => (extension.ToLowerInvariant() == ".jpeg" ? ".jpeg" : ".jpg", "image/jpeg"),
        };
        return new Result(encoded.ToArray(), ext, type, output.Width, output.Height);
    }

    /// <summary>A copy turned upright for the EXIF orientation, or null when it already is.</summary>
    private static SKBitmap? Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return null;
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (source.Height, source.Width) : (source.Width, source.Height);
        var target = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType, source.ColorSpace));
        using var canvas = new SKCanvas(target);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored horizontally
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // rotated 180°
                canvas.Translate(w, h);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // mirrored vertically
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // mirrored along the top-left diagonal
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // rotated 90° clockwise
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // mirrored along the top-right diagonal
                canvas.Translate(w, h);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom: // rotated 90° counter-clockwise
                canvas.Translate(0, h);
                canvas.RotateDegrees(270);
                break;
        }

        // Quarter turns and mirrors map pixels one to one: no resampling needed.
        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        return target;
    }
}
