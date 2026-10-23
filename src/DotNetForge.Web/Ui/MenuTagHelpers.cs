using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Razor.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.Web.Ui;

/// <summary>
/// <c>&lt;action-menu label="Actions for X"&gt;…items…&lt;/action-menu&gt;</c> - the "⋯" row action menu
/// (.docs/architecture/design-system.md#menus). Renders an icon button with <c>popovertarget</c> and a
/// <c>.menu-list[popover]</c>: the list opens in the browser's top layer, so no table scroll area or neighbouring card
/// can clip or cover it, and the browser closes it on Escape and outside clicks; <c>ui.js</c> places it next to the
/// button (<see cref="Placement"/>). Items: <c>&lt;menu-link&gt;</c>, <c>&lt;menu-button&gt;</c>,
/// <c>&lt;menu-post&gt;</c>, <c>&lt;menu-separator /&gt;</c>, <c>&lt;menu-note&gt;</c>.
/// </summary>
[HtmlTargetElement("action-menu")]
public sealed class ActionMenuTagHelper : UiTagHelper
{
    public ActionMenuTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Accessible name of the button, e.g. "Actions for report.pdf".</summary>
    public string Label { get; set; } = "Actions";

    public string Icon { get; set; } = "more";

    /// <summary><c>ghost</c> (default, in tables) or <c>secondary</c> (on cards, over images).</summary>
    public string Variant { get; set; } = "ghost";

    /// <summary>Where the list opens: <c>bottom-end</c> (default), <c>bottom-start</c>, <c>top-end</c>, <c>top-start</c>.</summary>
    public string Placement { get; set; } = "bottom-end";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var id = $"menu-{Guid.NewGuid():N}"[..17];
        var items = (await output.GetChildContentAsync()).GetContent();
        var variant = Variant == "secondary" ? "secondary" : "ghost";

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, "menu");
        output.Content.SetHtmlContent(
            $"<button type=\"button\" class=\"btn btn-{variant} btn-icon btn-sm\" popovertarget=\"{id}\" aria-haspopup=\"menu\" " +
            $"aria-label=\"{Encode(Label)}\">{IconHtml(Icon, "icon icon-sm")}</button>" +
            $"<div class=\"menu-list\" id=\"{id}\" popover role=\"menu\" data-placement=\"{Encode(Placement)}\">{items}</div>");
    }
}

/// <summary><c>&lt;menu-link href="/admin/x" icon="pencil" new-tab="true"&gt;Edit&lt;/menu-link&gt;</c> - a link item.</summary>
[HtmlTargetElement("menu-link")]
public sealed class MenuLinkTagHelper : UiTagHelper
{
    public MenuLinkTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public string? Icon { get; set; }

    public bool NewTab { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, "menu-item");
        output.Attributes.SetAttribute("role", "menuitem");
        if (NewTab)
        {
            output.Attributes.SetAttribute("target", "_blank");
            output.Attributes.SetAttribute("rel", "noopener");
        }

        if (Icon is not null)
        {
            output.PreContent.SetHtmlContent(IconHtml(Icon));
        }
    }
}

/// <summary>
/// <c>&lt;menu-button icon="link" data-copy-text="…"&gt;Copy link&lt;/menu-button&gt;</c> - a button item for client-side
/// actions; other attributes (including <c>role</c>, e.g. <c>menuitemradio</c>) pass through.
/// </summary>
[HtmlTargetElement("menu-button")]
public sealed class MenuButtonTagHelper : UiTagHelper
{
    public MenuButtonTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public string? Icon { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", "button");
        AppendClass(output, "menu-item");
        if (!output.Attributes.ContainsName("role"))
        {
            output.Attributes.SetAttribute("role", "menuitem");
        }

        if (Icon is not null)
        {
            output.PreContent.SetHtmlContent(IconHtml(Icon));
        }
    }
}

/// <summary>
/// <c>&lt;menu-post action="/admin/x/1/delete" icon="trash" danger="true" confirm="Delete x?" field-returnTo="list"&gt;Delete&lt;/menu-post&gt;</c>
/// - an item that POSTs: a form with the antiforgery token, a hidden input per <c>field-*</c> attribute and a submit
/// button; <c>confirm*</c> attributes become the form's <c>data-confirm*</c> (the confirmation dialog).
/// </summary>
[HtmlTargetElement("menu-post")]
public sealed class MenuPostTagHelper : UiTagHelper
{
    private readonly IHtmlGenerator _html;

    public MenuPostTagHelper(IFileVersionProvider versions, IHtmlGenerator html) : base(versions)
    {
        _html = html;
    }

    public string Action { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public bool Danger { get; set; }

    public string? Confirm { get; set; }

    public string? ConfirmTitle { get; set; }

    public string? ConfirmAction { get; set; }

    /// <summary><c>accent</c> for a non-destructive confirmation; danger otherwise.</summary>
    public string? ConfirmTone { get; set; }

    [HtmlAttributeName(DictionaryAttributePrefix = "field-")]
    public IDictionary<string, string?> Fields { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var label = (await output.GetChildContentAsync()).GetContent();

        output.TagName = "form";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("method", "post");
        output.Attributes.SetAttribute("action", Action);
        if (Confirm is not null)
        {
            output.Attributes.SetAttribute("data-confirm", Confirm);
            SetIfPresent(output, "data-confirm-title", ConfirmTitle);
            SetIfPresent(output, "data-confirm-action", ConfirmAction);
            SetIfPresent(output, "data-confirm-tone", ConfirmTone);
        }

        var html = new StringBuilder();
        using (var writer = new StringWriter(html))
        {
            _html.GenerateAntiforgery(ViewContext)?.WriteTo(writer, HtmlEncoder.Default);
        }

        foreach (var (name, value) in Fields)
        {
            html.Append($"<input type=\"hidden\" name=\"{Encode(name)}\" value=\"{Encode(value ?? string.Empty)}\" />");
        }

        html.Append($"<button type=\"submit\" class=\"menu-item{(Danger ? " danger" : string.Empty)}\" role=\"menuitem\">");
        if (Icon is not null)
        {
            html.Append(IconHtml(Icon));
        }

        html.Append(label).Append("</button>");
        output.Content.SetHtmlContent(html.ToString());
    }

    private static void SetIfPresent(TagHelperOutput output, string name, string? value)
    {
        if (value is not null)
        {
            output.Attributes.SetAttribute(name, value);
        }
    }
}

/// <summary><c>&lt;menu-separator /&gt;</c> - a line between groups of items.</summary>
[HtmlTargetElement("menu-separator", TagStructure = TagStructure.WithoutEndTag)]
public sealed class MenuSeparatorTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "menu-separator");
        output.Attributes.SetAttribute("role", "separator");
    }
}

/// <summary><c>&lt;menu-note&gt;Why an action is missing&lt;/menu-note&gt;</c> - a short explanation inside a menu.</summary>
[HtmlTargetElement("menu-note")]
public sealed class MenuNoteTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "menu-note");
    }
}
