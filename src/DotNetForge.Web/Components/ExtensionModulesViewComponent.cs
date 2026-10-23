using DotNetForge.Extensions;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace DotNetForge.Web.Components;

/// <summary>
/// The content-page module slot (.docs/features/extensions.md#slots): every enabled <c>module</c> extension with a
/// <c>Views/Module.cshtml</c> whose <c>pages</c> setting lists the current URL path (<c>"contact"</c>,
/// <c>"about/team"</c>, <c>"/"</c> for the root page, <c>"*"</c> for every page), in name order. Called by the
/// built-in page view and by themes: <c>@await Component.InvokeAsync("ExtensionModules", new { page = Model })</c>.
/// </summary>
public sealed class ExtensionModulesViewComponent : ViewComponent
{
    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionViewRenderer _renderer;

    public ExtensionModulesViewComponent(ExtensionCatalog catalog, ExtensionViewRenderer renderer)
    {
        _catalog = catalog;
        _renderer = renderer;
    }

    public async Task<IViewComponentResult> InvokeAsync(Page page)
    {
        var path = Normalize(HttpContext.Request.Path.Value);
        var html = new HtmlContentBuilder();
        foreach (var module in await _catalog.GetActiveAsync("module", HttpContext.RequestAborted))
        {
            var placed = module.Context.GetStringList("pages").Any(p => p == "*" || Normalize(p) == path);
            if (placed && module.FindView("Module.cshtml") is { } view)
            {
                html.AppendHtml(await _renderer.RenderAsync(HttpContext, module, view, page, admin: false));
            }
        }

        return new HtmlContentViewComponentResult(html);
    }

    /// <summary><c>/About/Team/</c> → <c>about/team</c>; the root (<c>/</c>, empty) → <c>""</c>.</summary>
    private static string Normalize(string? path) => (path ?? string.Empty).Trim().Trim('/').ToLowerInvariant();
}
