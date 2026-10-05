using System.Text.Json;
using DotNetForge.Core.Extensions;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Serves an admin extension's MVC-style views from its <c>extensions/admin/&lt;name&gt;/Views/</c> folder:
/// <c>Index.cshtml</c> (the page), <c>Shared/_Layout.cshtml</c> (the document shell), and
/// <c>Resources/css|js/</c> (static assets). The view is compiled on demand via runtime Razor compilation
/// and rendered server-side as a standalone document at <c>/admin/ext/{id}/raw</c>, which the admin shell
/// embeds in an iframe (see the Admin-area <c>ExtensionsController</c>). The id is matched against
/// discovered manifests (no path traversal).
/// </summary>
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
[Route("admin/ext")]
public sealed class ExtensionViewController : Controller
{
    private readonly IExtensionLoader _loader;
    private readonly string _viewRoot;

    public ExtensionViewController(IExtensionLoader loader, AppEnvironment env)
    {
        _loader = loader;
        _viewRoot = DependencyRegistration.ExtensionViewRoot(env);
    }

    [HttpGet("{id}/raw")]
    public IActionResult Render(string id)
    {
        var match = _loader.FindAdminExtension(id);
        var folder = match is null ? null : Path.GetDirectoryName(match.Path);
        var indexView = folder is null ? null : Path.Combine(folder, "Views", "Index.cshtml");
        if (match?.Manifest is null || folder is null || indexView is null || !System.IO.File.Exists(indexView))
        {
            return NotFound();
        }

        // Path the runtime Razor view engine resolves through the extension file provider (see DependencyRegistration).
        var relative = "~/" + Path.GetRelativePath(_viewRoot, indexView).Replace('\\', '/');
        ViewData["Settings"] = match.Manifest.Settings.ToDictionary(kv => kv.Key, kv => Normalize(kv.Value));
        ViewData["ResourceBase"] = $"/admin/ext/{id}/resources";

        return View(relative);
    }

    /// <summary>Serves a static asset from the extension's <c>Views/Resources/</c> folder (css, js, ...).</summary>
    [HttpGet("{id}/resources/{**path}")]
    public IActionResult Resource(string id, string path)
    {
        var match = _loader.FindAdminExtension(id);
        var folder = match is null ? null : Path.GetDirectoryName(match.Path);
        if (folder is null)
        {
            return NotFound();
        }

        var resourcesRoot = Path.GetFullPath(Path.Combine(folder, "Views", "Resources"));
        var requested = Path.GetFullPath(Path.Combine(resourcesRoot, path));

        // Guard against path traversal: the resolved file must stay under the Resources folder.
        if (!requested.StartsWith(resourcesRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !System.IO.File.Exists(requested))
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(requested).ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
        return PhysicalFile(requested, contentType);
    }

    /// <summary>JSON manifest values arrive as <see cref="JsonElement"/>; flatten to plain CLR types so
    /// the view can use them directly (e.g. `is bool`).</summary>
    private static object? Normalize(object? value) => value switch
    {
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => e.GetString(),
            _ => e.ToString(),
        },
        _ => value,
    };
}
