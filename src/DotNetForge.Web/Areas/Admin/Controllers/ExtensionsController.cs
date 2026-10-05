using DotNetForge.Core.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Hosts an <c>admin</c>-type extension's page inside the admin shell (.docs/features/extensions.md). The extension
/// ships its own complete HTML document, so it is embedded in an iframe pointing at the standalone render
/// (<c>/admin/ext/{id}/raw</c>, served by <c>ExtensionViewController</c>); the surrounding chrome (sidebar, topbar)
/// stays the admin's. Sidebar entries are contributed by the <c>AdminExtensionsNav</c> view component.
/// </summary>
[Route("admin/ext")]
public sealed class ExtensionsController : AdminControllerBase
{
    private readonly IExtensionLoader _loader;

    public ExtensionsController(IExtensionLoader loader)
    {
        _loader = loader;
    }

    [HttpGet("{id}")]
    public IActionResult Host(string id)
    {
        var match = _loader.FindAdminExtension(id);

        if (match?.Manifest is null)
        {
            return NotFound();
        }

        ViewData["Title"] = match.Manifest.Name;
        ViewData["ExtensionId"] = match.Manifest.Id;
        return View();
    }
}
