using DotNetForge.Extensions;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Serves an admin extension's MVC-style views from its <c>extensions/admin/&lt;name&gt;/Views/</c> folder:
/// <c>Index.cshtml</c> (the page), <c>Shared/_Layout.cshtml</c> (the document shell), and
/// <c>Resources/css|js/</c> (static assets). The view is compiled on demand via runtime Razor compilation
/// and rendered server-side as a standalone document at <c>/admin/ext/{id}/raw</c>, which the admin shell
/// embeds in an iframe (see the Admin-area <c>ExtensionsController</c>). Only enabled extensions answer; the id is
/// matched against discovered manifests (no path traversal).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.AdminArea)]
[Route("admin/ext")]
public sealed class ExtensionViewController : Controller
{
    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionViewRenderer _renderer;

    public ExtensionViewController(ExtensionCatalog catalog, ExtensionViewRenderer renderer)
    {
        _catalog = catalog;
        _renderer = renderer;
    }

    [HttpGet("{id}/raw")]
    public async Task<IActionResult> Render(string id)
    {
        var extension = await _catalog.FindActiveAsync(id, "admin", HttpContext.RequestAborted);
        var indexView = extension?.FindView("Index.cshtml");
        if (extension is null || indexView is null)
        {
            return NotFound();
        }

        ExtensionViewRenderer.AddExtensionViewData(ViewData, extension, admin: true);
        return View(_renderer.ToViewPath(indexView));
    }

    /// <summary>
    /// Serves a static asset from an enabled extension's <c>Views/Resources/</c> folder (css, js, ...) inside the admin
    /// area - admin tabs and dashboard widgets link their files here.
    /// </summary>
    [HttpGet("{id}/resources/{**path}")]
    public async Task<IActionResult> Resource(string id, string path)
    {
        var extension = await _catalog.FindActiveAsync(id, cancellationToken: HttpContext.RequestAborted);
        return extension is null ? NotFound() : ExtensionResources.Serve(this, extension, path);
    }
}

/// <summary>
/// The public counterpart of <see cref="ExtensionViewController.Resource"/>: <c>/ext/{id}/resources/{**path}</c> for
/// enabled <c>theme</c> and <c>module</c> extensions, whose views are part of the public site.
/// </summary>
[Route("ext")]
public sealed class ExtensionResourcesController : Controller
{
    private static readonly string[] PublicTypes = { "theme", "module" };

    private readonly ExtensionCatalog _catalog;

    public ExtensionResourcesController(ExtensionCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("{id}/resources/{**path}")]
    public async Task<IActionResult> Resource(string id, string path)
    {
        var extension = await _catalog.FindActiveAsync(id, cancellationToken: HttpContext.RequestAborted);
        return extension is null || !PublicTypes.Contains(extension.Context.Type)
            ? NotFound()
            : ExtensionResources.Serve(this, extension, path);
    }
}

/// <summary>Serves files from an extension's <c>Views/Resources/</c> folder with a path-traversal guard.</summary>
internal static class ExtensionResources
{
    public static IActionResult Serve(ControllerBase controller, ActiveExtension extension, string path)
    {
        var resourcesRoot = Path.GetFullPath(Path.Combine(extension.Folder, "Views", "Resources"));
        var requested = Path.GetFullPath(Path.Combine(resourcesRoot, path ?? string.Empty));

        // Guard against path traversal: the resolved file must stay under the Resources folder.
        if (!requested.StartsWith(resourcesRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !System.IO.File.Exists(requested))
        {
            return controller.NotFound();
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
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
        return controller.PhysicalFile(requested, contentType);
    }
}
