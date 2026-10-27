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

    /// <summary>
    /// <c>HttpContext.Items</c> key: a screen that needs a different policy puts it here; it replaces the default under
    /// the usual header (report-only in Development).
    /// </summary>
    public const string PagePolicyItemKey = "dnf:csp-page";

    /// <summary>
    /// The policy of screens with the Monaco code editor (the email template editor): Monaco injects its styles at
    /// runtime and has no nonce support, so styles may be inline there. Scripts stay <c>'self'</c> - Monaco is
    /// self-hosted under <c>/lib/monaco/</c> and its workers load from the same origin.
    /// </summary>
    public static readonly string CodeEditorPolicy =
        ContentSecurityPolicy.Replace("style-src 'self';", "style-src 'self' 'unsafe-inline';", StringComparison.Ordinal);

    /// <summary>
    /// <c>HttpContext.Items</c> key: an action that serves untrusted HTML (the email template preview) puts its own
    /// policy here; it is sent as an <b>enforced</b> <c>Content-Security-Policy</c> even in Development.
    /// </summary>
    public const string PolicyOverrideItemKey = "dnf:csp-override";

    /// <summary>
    /// The policy of an email template preview (.docs/pages/email-templates.md): administrator-written HTML in a
    /// sandbox - no script, no form, no plugin; only inline styles and images, as emails use them.
    /// </summary>
    public const string EmailPreviewPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; img-src https: data:; frame-ancestors 'self'; " +
        "form-action 'none'; base-uri 'none'; sandbox";

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
            else if (context.Items[PolicyOverrideItemKey] is string policy)
            {
                context.Response.Headers.Remove("Content-Security-Policy-Report-Only");
                context.Response.Headers["Content-Security-Policy"] = policy;
            }
            else if (context.Items[PagePolicyItemKey] is string pagePolicy)
            {
                context.Response.Headers[_cspHeaderName] = pagePolicy;
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
