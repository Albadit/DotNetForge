using System.Net;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DotNetForge.Web.Components;

/// <summary>
/// The page builder layout of a content page (.docs/features/page-builder.md#rendering): its stylesheets and HTML,
/// rendered from the published layout for the current visitor (elements limited to signed-in or anonymous visitors are
/// left out on the server). Renders nothing when the page has no published layout. Called by the built-in page view
/// and by themes: <c>@await Component.InvokeAsync("PageLayout", new { page = Model })</c>.
/// </summary>
/// <remarks>
/// Pass <c>draft: true</c> (the editor's preview) to render the unpublished draft; its CSS then comes from the
/// admin-only draft stylesheet.
/// </remarks>
public sealed class PageLayoutViewComponent : ViewComponent
{
    private readonly IFileVersionProvider _versions;

    public PageLayoutViewComponent(IFileVersionProvider versions)
    {
        _versions = versions;
    }

    public IViewComponentResult Invoke(Page page, bool draft = false)
    {
        var colors = _versions.AddFileVersionToPath(HttpContext.Request.PathBase, "/builder/site-colors.css");
        var baseCss = _versions.AddFileVersionToPath(HttpContext.Request.PathBase, "/builder/base.css");
        var html = new HtmlContentBuilder();
        html.AppendHtml($"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(colors)}\" />");
        html.AppendHtml($"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(baseCss)}\" />");

        // The page builder canvas: the editor renders the layout into this mount itself.
        if (ViewData["BuilderCanvas"] as bool? == true)
        {
            var canvasCss = _versions.AddFileVersionToPath(HttpContext.Request.PathBase, "/builder/canvas.css");
            html.AppendHtml($"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(canvasCss)}\" />");
            html.AppendHtml("<div id=\"pe-canvas-mount\"></div>");
            return new HtmlContentViewComponentResult(html);
        }

        draft = draft || ViewData["DraftPreview"] as bool? == true;
        var json = draft ? PageBuilderService.EditableJson(page) : page.LayoutJson;
        var rendered = PageBuilderService.Render(json, HttpContext.User.Identity?.IsAuthenticated == true);
        if (rendered is null)
        {
            return new HtmlContentViewComponentResult(HtmlString.Empty);
        }

        var pageCss = draft
            ? $"/admin/content/{page.Id}/builder/draft.css?v={rendered.CssHash}"
            : $"/page-css/{page.Id}.css?v={rendered.CssHash}";
        html.AppendHtml($"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(pageCss)}\" />");
        html.AppendHtml(rendered.Html);
        return new HtmlContentViewComponentResult(html);
    }
}
