namespace DotNetForge.Web.Middleware;

/// <summary>
/// Adds browser security headers to every response (.docs/features/security.md). The Content-Security-Policy allows
/// scripts and styles only from this origin - views carry no inline script or style - and images/media also from
/// HTTPS origins, because media downloads redirect to presigned object-storage URLs; frames also from the two video
/// hosts the page builder embeds (YouTube no-cookie, Vimeo). In Development the policy is
/// report-only, so the developer exception page (which uses inline styles) still renders.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; " +
        "media-src 'self' https:; font-src 'self'; connect-src 'self'; frame-src 'self' https://www.youtube-nocookie.com https://player.vimeo.com; frame-ancestors 'self'; " +
        "form-action 'self'; base-uri 'self'; object-src 'none'";

    private readonly RequestDelegate _next;
    private readonly string _cspHeaderName;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _cspHeaderName = environment.IsDevelopment()
            ? "Content-Security-Policy-Report-Only"
            : "Content-Security-Policy";
    }

    /// <summary>
    /// The policy of the Scalar API reference page (.docs/features/api-docs.md): its one inline script runs only with the
    /// per-request nonce Scalar puts on it, and it injects its own styles at runtime, so styles may be inline there.
    /// </summary>
    public static string ApiReferencePolicy(string nonce) =>
        ContentSecurityPolicy
            .Replace("script-src 'self';", $"script-src 'self' 'nonce-{nonce}';", StringComparison.Ordinal)
            .Replace("style-src 'self';", "style-src 'self' 'unsafe-inline';", StringComparison.Ordinal);

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[_cspHeaderName] = ContentSecurityPolicy;
        context.Response.OnStarting(() =>
        {
            if (context.Items[Scalar.AspNetCore.ScalarOptions.NonceHttpContextItemKey] is string nonce)
            {
                context.Response.Headers[_cspHeaderName] = ApiReferencePolicy(nonce);
            }

            return Task.CompletedTask;
        });
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "SAMEORIGIN"; // the admin embeds admin extensions in a same-origin iframe
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return _next(context);
    }
}
