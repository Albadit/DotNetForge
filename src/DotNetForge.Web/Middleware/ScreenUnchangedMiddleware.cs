using System.Security.Cryptography;
using DotNetForge.Web.Areas.Admin.Navigation;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// A screen asked for again, unchanged (.docs/architecture/pages.md#navigation). navigation.js keeps the screens it
/// has shown in memory (never in the browser's disk cache: admin pages stay no-store) and sends their ETag. When the
/// screen renders the same as that copy, the answer is 304 without a body. The antiforgery tokens don't count: they are
/// encrypted anew for every request but stay valid as long as the antiforgery cookie does, like a page from the
/// back/forward cache. Only screen-only requests (X-Dnf-Screen) are looked at; every other response streams as before.
/// </summary>
public sealed class ScreenUnchangedMiddleware(RequestDelegate next)
{
    private static readonly byte[] TokenValue = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\""u8.ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || !AdminShell.IsScreenOnly(context.Request))
        {
            await next(context);
            return;
        }

        var body = context.Response.Body;
        using var screen = new MemoryStream();
        context.Response.Body = screen;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = body;
        }

        if (context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType ?? string.Empty).StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var etag = ETagOf(screen.GetBuffer().AsSpan(0, (int)screen.Length));
            context.Response.Headers.ETag = etag;
            if (context.Request.Headers.IfNoneMatch.ToString() == etag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
        }

        screen.Position = 0;
        await screen.CopyToAsync(body, context.RequestAborted);
    }

    /// <summary>A hash of the screen's bytes, the antiforgery token values left out.</summary>
    private static string ETagOf(ReadOnlySpan<byte> html)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            var field = html.IndexOf(TokenValue);
            if (field < 0)
            {
                hash.AppendData(html);
                break;
            }

            var value = field + TokenValue.Length;
            hash.AppendData(html[..value]);
            var end = html[value..].IndexOf((byte)'"');
            html = end < 0 ? ReadOnlySpan<byte>.Empty : html[(value + end)..];
        }

        return "W/\"" + Convert.ToHexString(hash.GetHashAndReset())[..20].ToLowerInvariant() + "\"";
    }
}
