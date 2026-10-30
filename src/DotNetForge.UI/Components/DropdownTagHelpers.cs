using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.UI.Components;

/// <summary>
/// <c>&lt;dnf-dropdown label="Actions for X"&gt;…items…&lt;/dnf-dropdown&gt;</c> - a button that opens a menu
/// (.docs/ui/components/dropdown.md). Renders an icon button with <c>popovertarget</c> and a
/// <c>.dnf-dropdown__menu[popover]</c>: the menu opens in the browser's top layer, so no scroll area or neighbouring
/// card can clip or cover it, and the browser closes it on Escape and outside clicks; dnf.js places it next to the
/// button (<see cref="Placement"/>), moves focus in and handles the arrow keys. With <see cref="Text"/> the button
/// shows text and a chevron instead of the "⋯" icon. Items: <c>&lt;dnf-dropdown-link&gt;</c>,
/// <c>&lt;dnf-dropdown-button&gt;</c>, <c>&lt;dnf-dropdown-post&gt;</c>, <c>&lt;dnf-dropdown-label&gt;</c>,
/// <c>&lt;dnf-dropdown-separator /&gt;</c>, <c>&lt;dnf-dropdown-note&gt;</c>.
/// </summary>
[HtmlTargetElement("dnf-dropdown")]
public sealed class DnfDropdownTagHelper : DnfTagHelper
{
    public DnfDropdownTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Accessible name of the button, e.g. "Actions for report.pdf".</summary>
    public string Label { get; set; } = "Actions";

    /// <summary>Visible button text; without it the button is icon-only.</summary>
    public string? Text { get; set; }

    /// <summary>The icon of an icon-only button (default <c>more</c>, the "⋯").</summary>
    public string Icon { get; set; } = "more";

    /// <summary><c>Ghost</c> (default, in tables) or any other button variant (<c>Secondary</c> on cards and images).</summary>
    public DnfButtonVariant Variant { get; set; } = DnfButtonVariant.Ghost;

    /// <summary>Button size: <c>Sm</c> (default), <c>Md</c> or <c>Lg</c>; <c>Xl</c> renders as <c>Lg</c>.</summary>
    public DnfSize Size { get; set; } = DnfSize.Sm;

    /// <summary>Where the menu opens: <c>bottom-end</c> (default), <c>bottom-start</c>, <c>top-end</c>, <c>top-start</c>.</summary>
    public string Placement { get; set; } = "bottom-end";

    /// <summary>Extra classes for the menu panel (e.g. a width utility).</summary>
    public string? MenuClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var id = $"dnf-menu-{Guid.NewGuid():N}"[..21];
        var items = (await output.GetChildContentAsync()).GetContent();
        var size = Size == DnfSize.Md ? string.Empty : $" dnf-button--{Kebab(Size == DnfSize.Xl ? DnfSize.Lg : Size)}";
        var iconClass = Size == DnfSize.Sm ? "dnf-icon dnf-icon--sm dnf-button__icon" : "dnf-icon dnf-button__icon";
        var button = Text is null
            ? $"<button type=\"button\" class=\"dnf-button dnf-button--{Kebab(Variant)}{size} dnf-button--icon-only\" " +
              $"popovertarget=\"{id}\" aria-haspopup=\"menu\" aria-expanded=\"false\" aria-label=\"{Encode(Label)}\">{IconHtml(Icon, iconClass)}</button>"
            : $"<button type=\"button\" class=\"dnf-button dnf-button--{Kebab(Variant)}{size}\" popovertarget=\"{id}\" " +
              $"aria-haspopup=\"menu\" aria-expanded=\"false\"><span class=\"dnf-button__label\">{Encode(Text)}</span>{IconHtml("chevron-down", iconClass + " dnf-dropdown__chevron")}</button>";
        var menuClass = MenuClass is null ? "dnf-dropdown__menu" : $"dnf-dropdown__menu {Encode(MenuClass)}";

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, "dnf-dropdown");
        output.Content.SetHtmlContent(
            button + $"<div class=\"{menuClass}\" id=\"{id}\" popover role=\"menu\" aria-label=\"{Encode(Label)}\" data-placement=\"{Encode(Placement)}\">{items}</div>");
    }
}

/// <summary><c>&lt;dnf-dropdown-link href="/admin/x" icon="pencil" new-tab="true"&gt;Edit&lt;/dnf-dropdown-link&gt;</c> - a link item.</summary>
[HtmlTargetElement("dnf-dropdown-link")]
public sealed class DnfDropdownLinkTagHelper : DnfTagHelper
{
    public DnfDropdownLinkTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Leading icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Opens in a new tab (<c>target="_blank"</c>, <c>rel="noopener"</c>).</summary>
    public bool NewTab { get; set; }

    /// <summary>Red: a destructive action.</summary>
    public bool Danger { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Danger ? "dnf-dropdown__item dnf-dropdown__item--danger" : "dnf-dropdown__item");
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
/// <c>&lt;dnf-dropdown-button icon="link" data-copy-text="…"&gt;Copy link&lt;/dnf-dropdown-button&gt;</c> - a button item
/// for client-side actions; other attributes (including <c>role</c>, e.g. <c>menuitemradio</c>) pass through.
/// </summary>
[HtmlTargetElement("dnf-dropdown-button")]
public sealed class DnfDropdownButtonTagHelper : DnfTagHelper
{
    public DnfDropdownButtonTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Leading icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Red: a destructive action.</summary>
    public bool Danger { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        if (!output.Attributes.ContainsName("type"))
        {
            output.Attributes.SetAttribute("type", "button");
        }

        SetClasses(output, Danger ? "dnf-dropdown__item dnf-dropdown__item--danger" : "dnf-dropdown__item");
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
/// <c>&lt;dnf-dropdown-post action="/admin/x/1/delete" icon="trash" danger="true" confirm="Delete x?" field-returnTo="list"&gt;Delete&lt;/dnf-dropdown-post&gt;</c>
/// - an item that POSTs: a form with the antiforgery token, a hidden input per <c>field-*</c> attribute and a submit
/// button; <c>confirm*</c> attributes become the form's <c>data-confirm*</c> (the confirmation dialog).
/// </summary>
[HtmlTargetElement("dnf-dropdown-post")]
public sealed class DnfDropdownPostTagHelper : DnfTagHelper
{
    private readonly IHtmlGenerator _html;

    public DnfDropdownPostTagHelper(IFileVersionProvider versions, IHtmlGenerator html) : base(versions)
    {
        _html = html;
    }

    /// <summary>The URL the form posts to.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Leading icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Red: a destructive action.</summary>
    public bool Danger { get; set; }

    /// <summary>A question asked in the confirmation dialog before posting.</summary>
    public string? Confirm { get; set; }

    /// <summary>The confirmation dialog's title.</summary>
    public string? ConfirmTitle { get; set; }

    /// <summary>The confirming button's text, e.g. "Delete".</summary>
    public string? ConfirmAction { get; set; }

    /// <summary><c>primary</c> for a non-destructive confirmation; danger otherwise.</summary>
    public string? ConfirmTone { get; set; }

    /// <summary>A hidden input per attribute: <c>field-returnTo="list"</c> posts <c>returnTo=list</c>.</summary>
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

        html.Append($"<button type=\"submit\" class=\"dnf-dropdown__item{(Danger ? " dnf-dropdown__item--danger" : string.Empty)}\" role=\"menuitem\">");
        if (Icon is not null)
        {
            html.Append(IconHtml(Icon));
        }

        html.Append(label).Append("</button>");
        output.Content.SetHtmlContent(html.ToString());
    }
}

/// <summary><c>&lt;dnf-dropdown-label&gt;Theme&lt;/dnf-dropdown-label&gt;</c> - a heading over a group of items.</summary>
[HtmlTargetElement("dnf-dropdown-label")]
public sealed class DnfDropdownLabelTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        DnfTagHelper.SetClasses(output, "dnf-dropdown__label");
        output.Attributes.SetAttribute("role", "presentation");
    }
}

/// <summary><c>&lt;dnf-dropdown-separator /&gt;</c> - a line between groups of items.</summary>
[HtmlTargetElement("dnf-dropdown-separator", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfDropdownSeparatorTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        DnfTagHelper.SetClasses(output, "dnf-dropdown__separator");
        output.Attributes.SetAttribute("role", "separator");
    }
}

/// <summary><c>&lt;dnf-dropdown-note&gt;Why an action is missing&lt;/dnf-dropdown-note&gt;</c> - a short explanation inside a menu.</summary>
[HtmlTargetElement("dnf-dropdown-note")]
public sealed class DnfDropdownNoteTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        DnfTagHelper.SetClasses(output, "dnf-dropdown__note");
    }
}
