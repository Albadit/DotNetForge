namespace DotNetForge.Web.Middleware;

/// <summary>
/// Adds browser security headers to every response (.docs/features/security.md). The Content-Security-Policy allows
/// scripts and styles only from this origin - views carry no inline script or style - and images/media also from
/// HTTPS origins, because media downloads redirect to presigned object-storage URLs. In Development the policy is
/// report-only, so the developer exception page (which uses inline styles) still renders.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; " +
        "media-src 'self' https:; font-src 'self'; connect-src 'self'; frame-src 'self'; frame-ancestors 'self'; " +
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

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers[_cspHeaderName] = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "SAMEORIGIN"; // the admin embeds admin extensions in a same-origin iframe
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        return _next(context);
    }
}
