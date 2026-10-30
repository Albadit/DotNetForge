using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.Web.Areas.Admin.UiDocs;

/// <summary>
/// <c>&lt;ui-doc-example id="variants" title="Variants" description="…"&gt;…Razor…&lt;/ui-doc-example&gt;</c> - one example of
/// the UI docs: its content renders as the live preview, and the Preview / Razor / HTML tabs show the same content's
/// source (read from the view itself, <see cref="UiDocsSource"/>) and its rendered HTML. The <see cref="Id"/> is the
/// heading's anchor (and the "On this page" entry) and must be unique on the page.
/// </summary>
[HtmlTargetElement("ui-doc-example")]
public sealed class UiDocExampleTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public string Id { get; set; } = string.Empty;

    /// <summary>The example's heading; none for the first example under "Usage".</summary>
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>How the preview lays out its content: <c>row</c> (wrapping, centred), <c>stack</c> or <c>block</c> (full width).</summary>
    public string Layout { get; set; } = "row";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var preview = (await output.GetChildContentAsync()).GetContent();
        var razor = UiDocsSource.Inner(ViewContext.ExecutingFilePath ?? ViewContext.View.Path, Id)
            ?? throw new InvalidOperationException($"No source for <ui-doc-example id=\"{Id}\"> in {ViewContext.View.Path}.");
        var html = UiDocsSource.FormatHtml(preview);
        var key = $"{Id}-{Guid.NewGuid():N}"[..(Id.Length + 9)];

        output.TagName = "section";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "dnf-docs-example");
        output.Attributes.SetAttribute("data-doc-example", Id);

        var body = new StringBuilder();
        if (Title is not null)
        {
            body.Append($"<h3 class=\"dnf-docs-example__title\" id=\"{Encode(Id)}\">{Encode(Title)}</h3>");
        }

        if (Description is not null)
        {
            body.Append($"<p class=\"dnf-docs-example__description\">{Encode(Description)}</p>");
        }

        var label = Encode(Title ?? "Usage");
        body.Append($"<div class=\"dnf-docs-example__frame\" data-tabs data-tab-hash=\"false\" data-initial-tab=\"preview\">");
        body.Append($"<div class=\"dnf-tabs dnf-docs-example__tabs\" role=\"tablist\" aria-label=\"{label} example\">");
        foreach (var (tab, text) in new[] { ("preview", "Preview"), ("razor", "Razor"), ("html", "HTML") })
        {
            // Preview is selected from the first paint (dnf.js keeps it so), so the tab underline doesn't jump.
            var selected = tab == "preview";
            body.Append($"<button type=\"button\" class=\"dnf-tabs__tab\" role=\"tab\" id=\"{key}-{tab}-tab\" aria-controls=\"{key}-{tab}\" " +
                $"aria-selected=\"{(selected ? "true" : "false")}\" tabindex=\"{(selected ? "0" : "-1")}\" data-tab=\"{tab}\">{text}</button>");
        }

        body.Append("</div>");
        body.Append($"<div class=\"dnf-docs-example__preview dnf-docs-example__preview--{Encode(Layout)}\" id=\"{key}-preview\" role=\"tabpanel\" aria-labelledby=\"{key}-preview-tab\" data-tab-panel=\"preview\" data-doc-preview>{preview}</div>");
        body.Append(Code(key, "razor", razor));
        body.Append(Code(key, "html", html));
        body.Append("</div>");
        output.Content.SetHtmlContent(body.ToString());
    }

    internal static string Code(string key, string tab, string code) =>
        $"<div class=\"dnf-docs-example__code\" id=\"{key}-{tab}\" role=\"tabpanel\" aria-labelledby=\"{key}-{tab}-tab\" data-tab-panel=\"{tab}\">" +
        $"<div class=\"dnf-docs-code\" data-lang=\"{(tab == "razor" ? "cshtml" : tab)}\">" +
        $"<pre class=\"dnf-code-block dnf-docs-code__pre\" tabindex=\"0\" aria-label=\"{(tab == "razor" ? "Razor" : "HTML")} code\"><code id=\"{key}-{tab}-code\">{UiDocsSource.Highlight(code)}</code></pre>" +
        CopyButton($"{key}-{tab}-code") + "</div></div>";

    /// <summary>
    /// The Copy button sits outside the scrolling &lt;pre&gt;, so it stays in its corner while the code scrolls; the
    /// &lt;pre&gt; itself takes focus (tabindex, a name), so a keyboard can scroll it.
    /// </summary>
    internal static string CopyButton(string codeId) =>
        $"<button type=\"button\" class=\"dnf-button dnf-button--secondary dnf-button--sm dnf-docs-code__copy\" data-copy-target=\"#{codeId}\">Copy</button>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>
/// <c>&lt;ui-doc-api type="typeof(DnfButtonTagHelper)" /&gt;</c> - the API table of a tag helper (attribute, type,
/// default, description), generated from the type and its doc comments (<see cref="TagHelperReference"/>).
/// </summary>
[HtmlTargetElement("ui-doc-api", TagStructure = TagStructure.WithoutEndTag)]
public sealed class UiDocApiTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public Type Type { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var rows = TagHelperReference.Attributes(Type, ViewContext.HttpContext.RequestServices);
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "dnf-table dnf-table--stack dnf-docs-api");

        var html = new StringBuilder();
        html.Append("<table class=\"dnf-table__table\"><thead><tr><th>Attribute</th><th>Type</th><th>Default</th><th>Description</th></tr></thead><tbody>");
        if (rows.Count == 0)
        {
            html.Append("<tr class=\"dnf-table__empty\"><td colspan=\"4\">No attributes - the element takes only content and pass-through attributes.</td></tr>");
        }

        foreach (var row in rows)
        {
            html.Append("<tr>")
                .Append($"<td class=\"dnf-table__cell--lead\"><code>{WebUtility.HtmlEncode(row.Name)}</code></td>")
                .Append($"<td data-label=\"Type\"><code class=\"dnf-docs-api__type\">{WebUtility.HtmlEncode(row.Type)}</code></td>")
                .Append($"<td data-label=\"Default\">{(row.Default is "" or "-" ? "-" : $"<code>{WebUtility.HtmlEncode(row.Default)}</code>")}</td>")
                .Append($"<td data-label=\"Description\">{row.DescriptionHtml}</td>")
                .Append("</tr>");
        }

        html.Append("</tbody></table>");
        output.Content.SetHtmlContent(html.ToString());
    }
}

/// <summary>
/// <c>&lt;ui-doc-code id="imports" lang="razor"&gt;…&lt;/ui-doc-code&gt;</c> - a static code snippet with a copy button. The
/// text is the element's source in the view (so tags need no escaping; write <c>@@</c> for <c>@</c>); the content is
/// never rendered. <c>lang</c>: <c>razor</c> / <c>html</c> (highlighted) or anything else (plain).
/// </summary>
[HtmlTargetElement("ui-doc-code")]
public sealed class UiDocCodeTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public string Id { get; set; } = string.Empty;

    public string Lang { get; set; } = "razor";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var code = UiDocsSource.Inner(ViewContext.ExecutingFilePath ?? ViewContext.View.Path, Id, "ui-doc-code")
            ?? throw new InvalidOperationException($"No source for <ui-doc-code id=\"{Id}\"> in {ViewContext.View.Path}.");
        code = code.Replace("@@", "@", StringComparison.Ordinal);
        var body = Lang is "razor" or "html" or "cshtml" ? UiDocsSource.Highlight(code) : WebUtility.HtmlEncode(code);

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "dnf-docs-code");
        output.Attributes.SetAttribute("data-lang", Lang);
        output.Content.SetHtmlContent(
            $"<pre class=\"dnf-code-block dnf-docs-code__pre\" tabindex=\"0\" aria-label=\"{WebUtility.HtmlEncode(Lang.ToUpperInvariant())} code\"><code id=\"code-{WebUtility.HtmlEncode(Id)}\">{body}</code></pre>" +
            UiDocExampleTagHelper.CopyButton($"code-{WebUtility.HtmlEncode(Id)}"));
    }
}
