using DotNetForge.Extensions;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Components;

/// <summary>
/// The dashboard's widget slot (.docs/pages/dashboard.md#widgets): every enabled <c>widget</c> extension with a
/// <c>Views/Widget.cshtml</c>, rendered in its own panel. A widget that throws shows a notice instead.
/// </summary>
public sealed class ExtensionWidgetsViewComponent : ViewComponent
{
    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionViewRenderer _renderer;

    public ExtensionWidgetsViewComponent(ExtensionCatalog catalog, ExtensionViewRenderer renderer)
    {
        _catalog = catalog;
        _renderer = renderer;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var widgets = new List<(string Name, IHtmlContent Html)>();
        foreach (var widget in await _catalog.GetActiveAsync("widget", HttpContext.RequestAborted))
        {
            if (widget.FindView("Widget.cshtml") is { } view)
            {
                widgets.Add((widget.Context.Name, await _renderer.RenderAsync(HttpContext, widget, view, model: null, admin: true)));
            }
        }

        return View(widgets);
    }
}
