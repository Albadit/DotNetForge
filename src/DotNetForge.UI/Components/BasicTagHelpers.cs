using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.UI.Components;

/// <summary>
/// <c>&lt;dnf-icon name="pages" size="Sm" label="Pages" /&gt;</c> - an icon from the sprite
/// (.docs/ui/components/icon.md). Decorative (hidden from assistive technology) unless <see cref="Label"/> is set.
/// </summary>
[HtmlTargetElement("dnf-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfIconTagHelper : DnfTagHelper
{
    public DnfIconTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>The symbol id in the sprite, e.g. <c>pages</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Sm</c> 14px, <c>Md</c> 18px (default), <c>Lg</c> 22px, <c>Xl</c> 28px.</summary>
    public DnfSize Size { get; set; } = DnfSize.Md;

    /// <summary>Accessible name; set it only when the icon is the only content that names something.</summary>
    public string? Label { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Size == DnfSize.Md ? "dnf-icon" : $"dnf-icon dnf-icon--{Kebab(Size)}");
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

/// <summary>
/// <c>&lt;dnf-button variant="Primary" size="Sm" icon="plus" pending="false"&gt;Add&lt;/dnf-button&gt;</c> - a button,
/// or a link drawn as one when <see cref="Href"/> is set (.docs/ui/components/button.md). Other attributes (type,
/// name, value, form, data-*, aria-*) pass through.
/// </summary>
[HtmlTargetElement("dnf-button")]
public sealed class DnfButtonTagHelper : DnfTagHelper
{
    public DnfButtonTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Visual weight: <c>Primary</c> for the one main action, <c>Secondary</c> (default) for the rest, <c>Tertiary</c>, <c>Outline</c>, <c>Ghost</c>, <c>Danger</c>, <c>DangerSoft</c>.</summary>
    public DnfButtonVariant Variant { get; set; } = DnfButtonVariant.Secondary;

    /// <summary><c>Sm</c>, <c>Md</c> (default) or <c>Lg</c>.</summary>
    public DnfSize Size { get; set; } = DnfSize.Md;

    /// <summary>Leading icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Trailing icon (e.g. <c>external-link</c>, <c>chevron-down</c>).</summary>
    public string? IconEnd { get; set; }

    /// <summary>Only the icon shows; <see cref="Label"/> becomes the accessible name (and the tooltip).</summary>
    public bool IconOnly { get; set; }

    /// <summary>Accessible name (and tooltip) of an icon-only button; defaults to the content's text.</summary>
    public string? Label { get; set; }

    /// <summary>A spinner replaces the leading icon and the button ignores clicks (<c>data-pending</c>, <c>aria-busy</c>).</summary>
    public bool Pending { get; set; }

    /// <summary>Stretches the button to the width of its container.</summary>
    public bool FullWidth { get; set; }

    /// <summary>A <c>&lt;button disabled&gt;</c>; on a link <c>aria-disabled="true"</c> and <c>tabindex="-1"</c>.</summary>
    public bool Disabled { get; set; }

    /// <summary>Renders an <c>&lt;a&gt;</c> instead of a <c>&lt;button&gt;</c>.</summary>
    public string? Href { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var label = (await output.GetChildContentAsync()).GetContent();
        var size = Size == DnfSize.Xl ? DnfSize.Lg : Size;
        var classes = $"dnf-button dnf-button--{Kebab(Variant)}";
        if (size != DnfSize.Md)
        {
            classes += $" dnf-button--{Kebab(size)}";
        }

        if (IconOnly)
        {
            classes += " dnf-button--icon-only";
        }

        if (FullWidth)
        {
            classes += " dnf-button--full-width";
        }

        output.TagMode = TagMode.StartTagAndEndTag;
        if (Href is not null)
        {
            output.TagName = "a";
            output.Attributes.SetAttribute("href", Href);
            if (Disabled)
            {
                output.Attributes.SetAttribute("aria-disabled", "true");
                output.Attributes.SetAttribute("tabindex", "-1");
            }
        }
        else
        {
            output.TagName = "button";
            if (!output.Attributes.ContainsName("type"))
            {
                output.Attributes.SetAttribute("type", "button");
            }

            if (Disabled)
            {
                output.Attributes.SetAttribute("disabled", "disabled");
            }
        }

        SetClasses(output, classes);
        if (Pending)
        {
            output.Attributes.SetAttribute("data-pending", "true");
            output.Attributes.SetAttribute("aria-busy", "true");
        }

        // An icon-only button is named by Label, else by its content's text (never left without a name).
        var name = Label ?? (IconOnly ? System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(label, "<[^>]*>", string.Empty)).Trim() : null);
        if (IconOnly && !string.IsNullOrEmpty(name) && !output.Attributes.ContainsName("aria-label"))
        {
            output.Attributes.SetAttribute("aria-label", name);
            if (!output.Attributes.ContainsName("data-tooltip"))
            {
                output.Attributes.SetAttribute("data-tooltip", name);
            }
        }

        var iconSize = size == DnfSize.Sm ? "dnf-icon dnf-icon--sm dnf-button__icon" : "dnf-icon dnf-button__icon";
        var html = "<span class=\"dnf-button__spinner\" aria-hidden=\"true\"></span>";
        if (Icon is not null)
        {
            html += IconHtml(Icon, iconSize);
        }

        if (!IconOnly && !string.IsNullOrWhiteSpace(label))
        {
            html += $"<span class=\"dnf-button__label\">{label}</span>";
        }

        if (IconEnd is not null)
        {
            html += IconHtml(IconEnd, iconSize);
        }

        output.Content.SetHtmlContent(html);
    }
}

/// <summary><c>&lt;dnf-badge tone="Success" dot="true"&gt;Enabled&lt;/dnf-badge&gt;</c> - a small status label.</summary>
[HtmlTargetElement("dnf-badge")]
public sealed class DnfBadgeTagHelper : DnfTagHelper
{
    public DnfBadgeTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Colour: <c>Neutral</c> (default), <c>Success</c>, <c>Warning</c>, <c>Danger</c>, <c>Info</c> or <c>Primary</c>.</summary>
    public DnfTone Tone { get; set; } = DnfTone.Neutral;

    /// <summary>Leading coloured dot (for statuses).</summary>
    public bool Dot { get; set; }

    /// <summary>Transparent with a border.</summary>
    public bool Outline { get; set; }

    /// <summary>Monospaced, slightly spaced text - HTTP methods, codes, versions.</summary>
    public bool Mono { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var classes = Tone == DnfTone.Neutral ? "dnf-badge" : $"dnf-badge dnf-badge--{Kebab(Tone)}";
        if (Dot)
        {
            classes += " dnf-badge--dot";
        }

        if (Outline)
        {
            classes += " dnf-badge--outline";
        }

        if (Mono)
        {
            classes += " dnf-badge--mono";
        }

        SetClasses(output, classes);
    }
}

/// <summary><c>&lt;dnf-status-dot tone="Success" label="Published" /&gt;</c> - a coloured dot with an accessible name.</summary>
[HtmlTargetElement("dnf-status-dot", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfStatusDotTagHelper : DnfTagHelper
{
    public DnfStatusDotTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>Colour of the dot; <c>Neutral</c> is grey.</summary>
    public DnfTone Tone { get; set; } = DnfTone.Neutral;

    /// <summary>Accessible name, also shown as the tooltip - the dot has no text. Without it the dot is hidden from assistive technology.</summary>
    public string Label { get; set; } = string.Empty;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Tone == DnfTone.Neutral ? "dnf-status-dot" : $"dnf-status-dot dnf-status-dot--{Kebab(Tone)}");
        if (string.IsNullOrWhiteSpace(Label))
        {
            // Without a label the dot is decoration: the status must then be in nearby text.
            output.Attributes.SetAttribute("aria-hidden", "true");
            return;
        }

        output.Attributes.SetAttribute("role", "img");
        output.Attributes.SetAttribute("aria-label", Label);
        output.Attributes.SetAttribute("data-tooltip", Label);
    }
}

/// <summary>
/// <c>&lt;dnf-alert tone="Danger" title="Could not save"&gt;details&lt;/dnf-alert&gt;</c> - an inline message with an
/// icon (.docs/ui/components/alert.md). Danger and warning alerts are announced (<c>role="alert"</c>); the others
/// are polite (<c>role="status"</c>).
/// </summary>
[HtmlTargetElement("dnf-alert")]
public sealed class DnfAlertTagHelper : DnfTagHelper
{
    public DnfAlertTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary><c>Info</c> (default), <c>Success</c>, <c>Warning</c> or <c>Danger</c> - also picks the icon and whether it is announced; <c>Neutral</c> is the plain grey alert, <c>Primary</c> renders as <c>Info</c>.</summary>
    public DnfTone Tone { get; set; } = DnfTone.Info;

    /// <summary>Bold first line.</summary>
    public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Tone == DnfTone.Neutral ? "dnf-alert" : $"dnf-alert dnf-alert--{Kebab(Tone == DnfTone.Primary ? DnfTone.Info : Tone)}");
        output.Attributes.SetAttribute("role", Tone is DnfTone.Danger or DnfTone.Warning ? "alert" : "status");

        var icon = Tone switch
        {
            DnfTone.Success => "circle-check",
            DnfTone.Warning => "triangle-alert",
            DnfTone.Danger => "circle-x",
            _ => "info",
        };
        var title = Title is null ? string.Empty : $"<div class=\"dnf-alert__title\">{Encode(Title)}</div>";
        output.Content.SetHtmlContent($"{IconHtml(icon, "dnf-icon dnf-alert__icon")}<div class=\"dnf-alert__body\">{title}<div>{body}</div></div>");
    }
}

/// <summary>
/// <c>&lt;dnf-empty-state icon="pages" title="No pages yet" description="Create the first one."&gt;actions&lt;/dnf-empty-state&gt;</c>
/// - what a list shows when it has nothing to show; the child content holds the call-to-action buttons.
/// </summary>
[HtmlTargetElement("dnf-empty-state")]
public sealed class DnfEmptyStateTagHelper : DnfTagHelper
{
    public DnfEmptyStateTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>The icon in the tile above the title.</summary>
    public string Icon { get; set; } = "inbox";

    /// <summary>What is empty, e.g. "No pages yet".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>One sentence on what to do next.</summary>
    public string? Description { get; set; }

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
        SetClasses(output, Compact ? "dnf-empty-state dnf-empty-state--compact" : "dnf-empty-state");
        var description = Description is null ? string.Empty : $"<p class=\"dnf-empty-state__description\">{Encode(Description)}</p>";
        var actionBlock = string.IsNullOrWhiteSpace(actions) ? string.Empty : $"<div class=\"dnf-empty-state__actions\">{actions}</div>";
        output.Content.SetHtmlContent(
            $"<div class=\"dnf-empty-state__icon\">{IconHtml(Icon, "dnf-icon dnf-icon--lg")}</div>" +
            $"<{titleTag} class=\"dnf-empty-state__title\">{Encode(Title)}</{titleTag}>{description}{actionBlock}");
    }
}

/// <summary>
/// <c>&lt;dnf-stat-card icon="pages" label="Pages" value="12" href="/admin/pages" /&gt;</c> - a KPI tile; a link when
/// <see cref="Href"/> is set. <see cref="Bare"/> drops the card frame (several tiles inside one card).
/// </summary>
[HtmlTargetElement("dnf-stat-card", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfStatCardTagHelper : DnfTagHelper
{
    public DnfStatCardTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>The icon in the tile.</summary>
    public string Icon { get; set; } = "activity";

    /// <summary>What the figure counts.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The figure, as shown.</summary>
    public string Value { get; set; } = "0";

    /// <summary>Makes the whole tile a link.</summary>
    public string? Href { get; set; }

    /// <summary>Without the card frame - several tiles inside one card.</summary>
    public bool Bare { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = Href is null ? "div" : "a";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Bare ? "dnf-stat-card" : "dnf-card dnf-stat-card");
        if (Href is not null)
        {
            output.Attributes.SetAttribute("href", Href);
        }

        output.Content.SetHtmlContent(
            $"<span class=\"dnf-stat-card__icon\">{IconHtml(Icon)}</span>" +
            $"<span class=\"dnf-stat-card__text\"><span class=\"dnf-stat-card__value\">{Encode(Value)}</span>" +
            $"<span class=\"dnf-stat-card__label\">{Encode(Label)}</span></span>");
    }
}

/// <summary>
/// <c>&lt;dnf-spinner size="Sm" label="Loading" /&gt;</c> - an indeterminate progress indicator, announced politely.
/// </summary>
[HtmlTargetElement("dnf-spinner", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfSpinnerTagHelper : DnfTagHelper
{
    public DnfSpinnerTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary><c>Sm</c>, <c>Md</c> (default) or <c>Lg</c>; <c>Xl</c> renders as <c>Lg</c>.</summary>
    public DnfSize Size { get; set; } = DnfSize.Md;

    /// <summary>Announced to screen readers while it spins.</summary>
    public string Label { get; set; } = "Loading";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Size switch { DnfSize.Sm => "dnf-spinner dnf-spinner--sm", DnfSize.Lg or DnfSize.Xl => "dnf-spinner dnf-spinner--lg", _ => "dnf-spinner" });
        output.Attributes.SetAttribute("role", "status");
        output.Attributes.SetAttribute("aria-label", Label);
    }
}

/// <summary>
/// <c>&lt;dnf-avatar name="Ada Lovelace" size="Lg" /&gt;</c> - the initials of a person (or an image with
/// <see cref="Src"/>); decorative, since the name is always shown next to it.
/// </summary>
[HtmlTargetElement("dnf-avatar", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfAvatarTagHelper : DnfTagHelper
{
    public DnfAvatarTagHelper(IFileVersionProvider versions) : base(versions)
    {
    }

    /// <summary>The person's name or email; its initials are shown.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A picture instead of the initials.</summary>
    public string? Src { get; set; }

    /// <summary><c>Sm</c>, <c>Md</c> (default) or <c>Lg</c>; <c>Xl</c> renders as <c>Lg</c>.</summary>
    public DnfSize Size { get; set; } = DnfSize.Md;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        SetClasses(output, Size switch { DnfSize.Sm => "dnf-avatar dnf-avatar--sm", DnfSize.Lg or DnfSize.Xl => "dnf-avatar dnf-avatar--lg", _ => "dnf-avatar" });
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Content.SetHtmlContent(Src is null ? Encode(Initials(Name)) : $"<img src=\"{Encode(Src)}\" alt=\"\" />");
    }

    /// <summary>The first letters of the first two words: "Ada Lovelace" → "AL", "ada@example.com" → "AE", "x" → "X", "" → "?".</summary>
    public static string Initials(string name)
    {
        var words = name.Split([' ', '.', '_', '-', '@'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0])),
        };
    }
}

/// <summary>
/// <c>&lt;dnf-timestamp value="@date" /&gt;</c> - a UTC date shown relative to now ("5 min ago", "3 Oct 2026") inside
/// a <c>&lt;time&gt;</c> element whose tooltip and <c>datetime</c> carry the exact UTC time. <c>null</c> renders
/// <see cref="Empty"/>.
/// </summary>
[HtmlTargetElement("dnf-timestamp", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfTimestampTagHelper : TagHelper
{
    /// <summary>The UTC date; local dates are converted.</summary>
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
            DnfTagHelper.SetClasses(output, "text-subtle");
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
/// <c>&lt;dnf-validation-summary /&gt;</c> - every model-state error of the request as one danger block, or nothing
/// when there are none. Replaces <c>asp-validation-summary</c>, whose empty state renders an inline style the
/// Content-Security-Policy forbids.
/// </summary>
[HtmlTargetElement("dnf-validation-summary", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DnfValidationSummaryTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public Microsoft.AspNetCore.Mvc.Rendering.ViewContext ViewContext { get; set; } = default!;

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
        DnfTagHelper.SetClasses(output, "dnf-validation-summary");
        output.Attributes.SetAttribute("role", "alert");
        output.Content.SetHtmlContent("<ul>" + string.Concat(errors.Select(e => $"<li>{WebUtility.HtmlEncode(e)}</li>")) + "</ul>");
    }
}
