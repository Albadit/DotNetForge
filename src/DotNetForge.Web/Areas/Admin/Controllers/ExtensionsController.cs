using DotNetForge.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Hosts an enabled <c>admin</c>-type extension's page inside the admin shell (.docs/features/extensions.md). The
/// extension ships its own complete HTML document, so it is embedded in an iframe pointing at the standalone render
/// (<c>/admin/ext/{id}/raw</c>, served by <c>ExtensionViewController</c>); the surrounding chrome (sidebar, topbar)
/// stays the admin's. Sidebar entries come from <c>AdminNavigationService</c> (the Extensions section).
/// </summary>
[Route("admin/ext")]
public sealed class ExtensionsController : AdminControllerBase
{
    private readonly ExtensionCatalog _catalog;

    public ExtensionsController(ExtensionCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Host(string id)
    {
        var extension = await _catalog.FindActiveAsync(id, "admin", HttpContext.RequestAborted);
        if (extension is null)
        {
            return NotFound();
        }

        ViewData["Title"] = extension.Context.Name;
        ViewData["ExtensionId"] = extension.Id;
        return View();
    }
}
