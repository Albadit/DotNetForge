using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// Gates the entire application until first-run setup completes (.docs/features/installation.md): while the
/// CMS is not installed, every non-setup request is redirected to the setup wizard; once installed,
/// the wizard is permanently blocked and redirects to the admin area.
/// </summary>
public sealed class InstallationMiddleware
{
    private static readonly string[] StaticPrefixes =
    {
        "/css", "/js", "/lib", "/images", "/img", "/fonts", "/favicon", "/health",
    };

    private readonly RequestDelegate _next;

    public InstallationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, InstallationStatusCache status, IInstallationStore store)
    {
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

        // Always let static assets through so the setup/login pages can style themselves.
        if (IsStaticAsset(path))
        {
            await _next(context);
            return;
        }

        var installed = await status.IsInstalledAsync(store, context.RequestAborted);
        var isSetupRoute = path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase);

        if (!installed)
        {
            if (isSetupRoute)
            {
                await _next(context);
                return;
            }

            context.Response.Redirect("/setup");
            return;
        }

        // Installed: the wizard is permanently blocked.
        if (isSetupRoute)
        {
            context.Response.Redirect("/admin");
            return;
        }

        await _next(context);
    }

    private static bool IsStaticAsset(string path)
    {
        foreach (var prefix in StaticPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return Path.HasExtension(path);
    }
}
