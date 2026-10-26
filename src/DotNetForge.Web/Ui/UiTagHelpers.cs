using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Razor.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.Web.Ui;

/// <summary>Semantic colour of a status component (badge, alert, toast) - maps to the design tokens.</summary>
public enum Tone
{
    Neutral,
    Success,
    Warning,
    Danger,
    Info,
    Accent,
}

/// <summary>
/// Shared plumbing of the UI tag helpers (.docs/architecture/design-system.md#tag-helpers): the cache-busted sprite URL
/// (<c>icons.svg?v=hash</c>) and the markup of a decorative icon, so every component draws icons the same way.
/// </summary>
public abstract class UiTagHelper : TagHelper
{
    public const string SpritePath = "/img/icons.svg";

    private readonly IFileVersionProvider _versions;

    protected UiTagHelper(IFileVersionProvider versions)
    {
        _versions = versions;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    protected string Sprite => _versions.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, SpritePath);

    /// <summary>A decorative (aria-hidden) icon.</summary>
    protected string IconHtml(string name, string classes = "icon") =>
        $"<svg class=\"{classes}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"{Sprite}#{Encode(name)}\"></use></svg>";

    protected static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>Puts <paramref name="classes"/> before any class the author wrote on the element.</summary>
    protected static void AppendClass(TagHelperOutput output, string classes)
    {
        if (output.Attributes.TryGetAttribute("class", out var existing))
        {
            classes = $"{classes} {existing.Value}";
        }

        output.Attributes.SetAttribute("class", classes);
    }
}

/// <summary>
/// <c>&lt;icon name="pages" size="sm" label="Pages" /&gt;</c> - an icon from <c>wwwroot/img/icons.svg</c>
/// (.docs/architecture/design-system.md#icons). Decorative (hidden from assistive technology) unless
/// <see cref="Label"/> is set.
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper : UiTagHelper
{
    public IconTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>The symbol id in the sprite, e.g. <c>pages</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>sm</c> (14px), default (18px), <c>lg</c> (22px) or <c>xl</c> (28px).</summary>
    public string? Size { get; set; }

    /// <summary>Accessible name; set it only when the icon is the only content of its control.</summary>
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, Size is null ? "icon" : $"icon icon-{Size}");
        output.Attributes.SetAttribute("focusable", "false");
        if (Label is null)
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label);
        }

        output.Content.SetHtmlContent($"<use href=\"{Sprite}#{Encode(Name)}\"></use>");
    }
}

/// <summary><c>&lt;badge tone="Success" dot="true"&gt;Enabled&lt;/badge&gt;</c> - a small status label.</summary>
[HtmlTargetElement("badge")]
public sealed class BadgeTagHelper : UiTagHelper
{
    public BadgeTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public Tone Tone { get; set; } = Tone.Neutral;

    /// <summary>Leading coloured dot (for statuses).</summary>
    public bool Dot { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var classes = Tone == Tone.Neutral ? "badge" : $"badge badge-{Tone.ToString().ToLowerInvariant()}";
        AppendClass(output, Dot ? classes + " badge-dot" : classes);
    }
}

/// <summary>
/// <c>&lt;alert tone="Danger" title="Could not save"&gt;details&lt;/alert&gt;</c> - an inline message with an icon.
/// Danger and warning alerts are announced (<c>role="alert"</c>); the others are polite (<c>role="status"</c>).
/// </summary>
[HtmlTargetElement("alert")]
public sealed class AlertTagHelper : UiTagHelper
{
    public AlertTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public Tone Tone { get; set; } = Tone.Info;

    public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, $"alert alert-{Tone.ToString().ToLowerInvariant()}");
        output.Attributes.SetAttribute("role", Tone is Tone.Danger or Tone.Warning ? "alert" : "status");

        var icon = Tone switch
        {
            Tone.Success => "circle-check",
            Tone.Warning => "triangle-alert",
            Tone.Danger => "circle-x",
            _ => "info",
        };
        var title = Title is null ? string.Empty : $"<div class=\"alert-title\">{Encode(Title)}</div>";
        output.Content.SetHtmlContent($"{IconHtml(icon)}<div class=\"alert-body\">{title}<div>{body}</div></div>");
    }
}

/// <summary>
/// <c>&lt;empty-state icon="pages" title="No pages yet" text="Create the first one."&gt;actions&lt;/empty-state&gt;</c> -
/// what a list shows when it has nothing to show; the child content holds the call-to-action buttons.
/// </summary>
[HtmlTargetElement("empty-state")]
public sealed class EmptyStateTagHelper : UiTagHelper
{
    public EmptyStateTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public string Icon { get; set; } = "inbox";

    public string Title { get; set; } = string.Empty;

    public string? Text { get; set; }

    /// <summary>Less vertical padding (inside cards and side panes).</summary>
    public bool Compact { get; set; }

    /// <summary>Render the title as <c>&lt;h1&gt;</c>..<c>&lt;h6&gt;</c> (e.g. 1 on a page whose only content this is); 0 = a paragraph.</summary>
    public int HeadingLevel { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var actions = (await output.GetChildContentAsync()).GetContent();
        var titleTag = HeadingLevel is >= 1 and <= 6 ? $"h{HeadingLevel}" : "p";
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, Compact ? "empty-state compact" : "empty-state");
        var text = Text is null ? string.Empty : $"<p class=\"empty-state-text\">{Encode(Text)}</p>";
        var actionBlock = string.IsNullOrWhiteSpace(actions) ? string.Empty : $"<div class=\"empty-state-actions\">{actions}</div>";
        output.Content.SetHtmlContent(
            $"<div class=\"empty-state-icon\">{IconHtml(Icon, "icon icon-lg")}</div>" +
            $"<{titleTag} class=\"empty-state-title\">{Encode(Title)}</{titleTag}>{text}{actionBlock}");
    }
}

/// <summary>
/// <c>&lt;stat-card icon="pages" label="Pages" value="12" href="/admin/pages" /&gt;</c> - a KPI tile; a link when
/// <see cref="Href"/> is set.
/// </summary>
[HtmlTargetElement("stat-card", TagStructure = TagStructure.WithoutEndTag)]
public sealed class StatCardTagHelper : UiTagHelper
{
    public StatCardTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    public string Icon { get; set; } = "activity";

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = "0";

    public string? Href { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = Href is null ? "div" : "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        AppendClass(output, "card stat");
        if (Href is not null)
        {
            output.Attributes.SetAttribute("href", Href);
        }

        output.Content.SetHtmlContent(
            $"<span class=\"stat-icon\">{IconHtml(Icon)}</span>" +
            $"<span class=\"stat-text\"><span class=\"stat-value\">{Encode(Value)}</span><span class=\"stat-label\">{Encode(Label)}</span></span>");
    }
}

/// <summary>
/// <c>&lt;timestamp value="@date" /&gt;</c> - a UTC date shown relative to now ("5 min ago", "3 Oct 2026") inside a
/// <c>&lt;time&gt;</c> element whose tooltip and <c>datetime</c> carry the exact UTC time. <c>null</c> renders
/// <see cref="Empty"/>.
/// </summary>
[HtmlTargetElement("timestamp", TagStructure = TagStructure.WithoutEndTag)]
public sealed class TimestampTagHelper : TagHelper
{
    public DateTime? Value { get; set; }

    /// <summary>Text when <see cref="Value"/> is null.</summary>
    public string Empty { get; set; } = "Never";

    /// <summary>Show the full date and time instead of a relative time.</summary>
    public bool Absolute { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagMode = TagMode.StartTagAndEndTag;
        if (Value is not { } value)
        {
            output.TagName = "span";
            output.Attributes.SetAttribute("class", "text-subtle");
            output.Content.SetContent(Empty);
            return;
        }

        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        output.TagName = "time";
        output.Attributes.SetAttribute("datetime", utc.ToString("O", CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("title", utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
        output.Content.SetContent(Absolute ? Format(utc) : Relative(utc, DateTime.UtcNow));
    }

    /// <summary>"just now", "12 min ago", "3 h ago", "yesterday", "4 days ago", then "3 Oct 2026" (also future).</summary>
    public static string Relative(DateTime utc, DateTime nowUtc)
    {
        var delta = nowUtc - utc;
        var future = delta < TimeSpan.Zero;
        var span = future ? -delta : delta;
        string Ago(string amount) => future ? $"in {amount}" : $"{amount} ago";

        return span switch
        {
            { TotalSeconds: < 45 } => future ? "in a moment" : "just now",
            { TotalMinutes: < 60 } => Ago($"{Math.Max(1, (int)Math.Round(span.TotalMinutes))} min"),
            { TotalHours: < 24 } => Ago($"{(int)Math.Round(span.TotalHours)} h"),
            { TotalDays: < 2 } => future ? "tomorrow" : "yesterday",
            { TotalDays: < 7 } => Ago($"{(int)span.TotalDays} days"),
            _ => utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    public static string Format(DateTime utc) => utc.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
}

/// <summary>
/// <c>&lt;form-errors /&gt;</c> - every model-state error of the request as one danger alert, or nothing when there
/// are none. Replaces <c>asp-validation-summary</c>, whose empty state renders <c>&lt;li style="display:none"&gt;</c>,
/// an inline style the Content-Security-Policy forbids.
/// </summary>
[HtmlTargetElement("form-errors", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FormErrorsTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var errors = ViewContext.ViewData.ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToList();
        if (errors.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "validation-summary-errors");
        output.Attributes.SetAttribute("role", "alert");
        output.Content.SetHtmlContent("<ul>" + string.Concat(errors.Select(e => $"<li>{WebUtility.HtmlEncode(e)}</li>")) + "</ul>");
    }
}
