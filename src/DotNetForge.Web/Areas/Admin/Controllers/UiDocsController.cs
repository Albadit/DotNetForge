using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.UiDocs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// The UI docs (.docs/pages/ui-docs.md): the documentation of the DotNetForge UI library inside the admin - guides and
/// one page per component with live examples (preview, Razor, HTML), styling reference and generated API tables. Its
/// own full-width layout, outside the admin shell. Read-only; Super Admins and Developers, like the API docs.
/// </summary>
[Route(UiDocsCatalog.Root)]
[Authorize(Policy = AuthorizationPolicies.ApiDocs)]
public sealed class UiDocsController : AdminControllerBase
{
    [HttpGet("")]
    public IActionResult Index() => Guide("introduction");

    [HttpGet("components")]
    public IActionResult Components() =>
        View("~/Areas/Admin/Views/UiDocs/Components/Index.cshtml", new UiDocsViewModel(
            new UiDocsPage("components", "Components", "Every component of the library, by category.", "Components", IsComponent: false),
            null, null));

    [HttpGet("components/{slug}")]
    public IActionResult Component(string slug) => Show(UiDocsCatalog.Find(slug, component: true));

    [HttpGet("{slug}")]
    public IActionResult Guide(string slug) => Show(UiDocsCatalog.Find(slug, component: false));

    private IActionResult Show(UiDocsPage? page) =>
        page is null ? NotFound() : View(page.ViewPath, UiDocsCatalog.ViewModel(page));
}
