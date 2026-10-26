namespace DotNetForge.Shared.Media;

/// <summary>
/// The public URL path of a media file (.docs/features/media-storage.md#download-flow): <c>/media/{id}/{file name}</c>.
/// Authorization is applied when it is requested. One definition for the admin, the page renderer and the API.
/// </summary>
public static class MediaUrls
{
    public static string For(Guid id, string fileName) => $"/media/{id}/{Uri.EscapeDataString(fileName)}";
}
