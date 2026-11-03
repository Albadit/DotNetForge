using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// Serves the Brotli and gzip copies that a Release publish writes next to each text file (build/DotNetForge.Minify,
/// strongest levels) instead of compressing the file on every request - like Nuxt's compressPublicAssets
/// (.docs/guides/development.md#development-and-production-builds). Release builds only (Program.cs): a Debug build's
/// static web assets map the SDK's own copies, which are older than the files being edited. A file without copies, or a
/// browser that accepts neither encoding, gets the file itself (compressed on the fly by response compression).
/// </summary>
public sealed class PrecompressedStaticFilesMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private static readonly (string Coding, string Suffix)[] Codings = [("br", ".br"), ("gzip", ".gz")];

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;
        if (path is null || !PrecompressedStaticFiles.IsText(path) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            return next(context);
        }

        foreach (var (coding, suffix) in Codings)
        {
            if (PrecompressedStaticFiles.Accepts(request, coding) && environment.WebRootFileProvider.GetFileInfo(path + suffix).Exists)
            {
                request.Path = path + suffix;
                break;
            }
        }

        return next(context);
    }
}

/// <summary>The static file options that make a <c>.br</c>/<c>.gz</c> copy answer as the file it compresses.</summary>
public static class PrecompressedStaticFiles
{
    private static readonly FileExtensionContentTypeProvider Types = new();

    public static bool IsText(string path) => Path.GetExtension(path) is ".css" or ".js" or ".mjs" or ".svg" or ".json";

    /// <summary>The content type of the original file for a copy (<c>dnf.css.br</c> is <c>text/css</c>), else the file's own.</summary>
    public static IContentTypeProvider ContentTypes { get; } = new CopyContentTypes();

    /// <summary>Marks a served copy as compressed, so the browser unpacks it and response compression leaves it alone.</summary>
    public static void Prepare(StaticFileResponseContext file)
    {
        if (Coding(file.File.Name) is { } coding)
        {
            file.Context.Response.Headers.ContentEncoding = coding;
            file.Context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
        }
    }

    internal static bool Accepts(HttpRequest request, string coding) =>
        StringWithQualityHeaderValue.TryParseList(request.Headers.AcceptEncoding, out var accepted)
        && accepted.Any(value => value.Value.Equals(coding, StringComparison.OrdinalIgnoreCase) && (value.Quality ?? 1) > 0);

    private static string? Coding(string name) =>
        !IsText(Path.GetFileNameWithoutExtension(name)) ? null
        : name.EndsWith(".br", StringComparison.OrdinalIgnoreCase) ? "br"
        : name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? "gzip"
        : null;

    private sealed class CopyContentTypes : IContentTypeProvider
    {
        public bool TryGetContentType(string subpath, out string contentType) =>
            Types.TryGetContentType(Coding(subpath) is null ? subpath : subpath[..subpath.LastIndexOf('.')], out contentType!);
    }
}
