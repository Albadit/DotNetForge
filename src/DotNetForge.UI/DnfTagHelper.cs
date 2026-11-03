using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.UI;

/// <summary>Size of a component (.docs/ui/README.md#component-api). Components that have fewer sizes use the nearest (Xl → Lg).</summary>
public enum DnfSize
{
    Md,
    Sm,
    Lg,
    Xl,
}

/// <summary>Semantic colour of a status component (badge, alert, toast, status dot) - maps to the --dnf-* tokens.</summary>
public enum DnfTone
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
    Primary,
}

/// <summary>Visual weight of a button (.docs/ui/components/button.md#variants).</summary>
public enum DnfButtonVariant
{
    Secondary,
    Primary,
    Tertiary,
    Outline,
    Ghost,
    Danger,
    DangerSoft,
}

/// <summary>The library's static files, served from the Razor class library under <c>/_content/DotNetForge.UI/</c>.</summary>
public static class DnfAssets
{
    public const string BasePath = "/_content/DotNetForge.UI/";

    /// <summary>Tokens, base styles, every component and the Tailwind utilities (.docs/ui/styling.md).</summary>
    public const string Stylesheet = BasePath + "dnf.css";

    /// <summary>The behaviours (<c>window.dnfUI</c>); load with <c>defer</c>.</summary>
    public const string Script = BasePath + "dnf.js";

    /// <summary>Applies the saved theme before the first paint; load in <c>&lt;head&gt;</c> without <c>defer</c>.</summary>
    public const string ThemeScript = BasePath + "dnf-theme.js";

    /// <summary>The icon sprite (<c>&lt;symbol id="name"&gt;</c> per icon).</summary>
    public const string Sprite = BasePath + "icons.svg";
}

/// <summary>
/// Shared plumbing of the Dnf tag helpers (.docs/ui/README.md#tag-helpers): BEM class names with the author's own
/// classes appended last (so Tailwind utilities and custom classes extend the component, Level 2), and the
/// cache-busted sprite URL for icons.
/// </summary>
public abstract class DnfTagHelper : TagHelper
{
    private readonly IFileVersionProvider _versions;

    protected DnfTagHelper(IFileVersionProvider versions)
    {
        _versions = versions;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary><c>/_content/DotNetForge.UI/icons.svg?v=hash</c>, below the app's path base.</summary>
    protected string Sprite => SpriteUrl(_versions, ViewContext);

    public static string SpriteUrl(IFileVersionProvider versions, ViewContext viewContext)
    {
        // The provider adds ?v=hash but returns the path as given: the app's path base goes in front, as "~/" would.
        var pathBase = viewContext.HttpContext.Request.PathBase;
        var versioned = versions.AddFileVersionToPath(pathBase, DnfAssets.Sprite);
        return pathBase.HasValue ? pathBase.Value + versioned : versioned;
    }

    /// <summary>A decorative (aria-hidden) sprite icon with the given classes.</summary>
    protected string IconHtml(string name, string classes = "dnf-icon") =>
        $"<svg class=\"{classes}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"{Sprite}#{Encode(name)}\"></use></svg>";

    protected static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>The modifier suffix of a size or tone: <c>Sm</c> → <c>sm</c>, <c>DangerSoft</c> → <c>danger-soft</c>.</summary>
    protected static string Kebab(Enum value)
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Sets the component's classes (safe names built from enums), followed by any class the author wrote on the
    /// element - a literal or a Razor expression, kept as encoded HTML.
    /// </summary>
    protected internal static void SetClasses(TagHelperOutput output, string classes)
    {
        var own = output.Attributes.TryGetAttribute("class", out var existing) ? Html(existing.Value) : string.Empty;
        output.Attributes.SetAttribute("class", new HtmlString(own.Length == 0 ? classes : $"{classes} {own}"));
    }

    private static string Html(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case IHtmlContent content:
                using (var writer = new StringWriter())
                {
                    content.WriteTo(writer, HtmlEncoder.Default);
                    return writer.ToString().Trim();
                }
            default:
                return HtmlEncoder.Default.Encode(value.ToString() ?? string.Empty).Trim();
        }
    }

    protected static void SetIfPresent(TagHelperOutput output, string name, string? value)
    {
        if (value is not null)
        {
            output.Attributes.SetAttribute(name, value);
        }
    }
}
