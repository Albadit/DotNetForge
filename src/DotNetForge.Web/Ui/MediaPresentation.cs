using System.Globalization;

namespace DotNetForge.Web.Ui;

/// <summary>How media files read in the UI: a filterable kind with its icon, and a human-readable size.</summary>
public static class MediaPresentation
{
    /// <summary>The library filter value (<c>image</c>, <c>video</c>, <c>audio</c>, <c>archive</c>, <c>document</c>) and icon.</summary>
    public static (string Kind, string Icon) Classify(string contentType) => contentType switch
    {
        _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => ("image", "image"),
        _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => ("video", "film"),
        _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => ("audio", "music"),
        _ when contentType.Contains("zip", StringComparison.OrdinalIgnoreCase) => ("archive", "archive"),
        _ => ("document", "file"),
    };

    /// <summary>"512 B", "3.4 KB", "12.5 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    };
}
