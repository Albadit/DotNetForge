using System.Text.Encodings.Web;
using DotNetForge.UI;
using DotNetForge.UI.Components;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace DotNetForge.Tests.UI;

/// <summary>
/// The Dnf tag helpers (.docs/ui/README.md#tag-helpers): BEM classes from their properties, the author's classes
/// appended last (Level 2 customization), and the accessibility attributes each component promises.
/// </summary>
public sealed class DnfTagHelperTests
{
    [Fact]
    public async Task Button_renders_variant_size_and_icon_classes_with_the_author_class_last()
    {
        var html = await RenderAsync(new DnfButtonTagHelper(new Versions()) { Variant = DnfButtonVariant.DangerSoft, Size = DnfSize.Sm, Icon = "trash" },
            "dnf-button", "Delete", ("class", "w-full"));

        Assert.StartsWith("<button", html);
        Assert.Contains("class=\"dnf-button dnf-button--danger-soft dnf-button--sm w-full\"", html);
        Assert.Contains("type=\"button\"", html);
        Assert.Contains("<span class=\"dnf-button__label\">Delete</span>", html);
        Assert.Contains("dnf-icon dnf-icon--sm dnf-button__icon", html);
        Assert.Contains("/_content/DotNetForge.UI/icons.svg?v=test#trash", html);
    }

    [Fact]
    public async Task Button_with_href_is_a_link_and_icon_only_buttons_are_named()
    {
        var link = await RenderAsync(new DnfButtonTagHelper(new Versions()) { Href = "/admin", Variant = DnfButtonVariant.Primary }, "dnf-button", "Go");
        Assert.StartsWith("<a", link);
        Assert.Contains("href=\"/admin\"", link);
        Assert.DoesNotContain("type=", link);

        var icon = await RenderAsync(new DnfButtonTagHelper(new Versions()) { Icon = "pencil", IconOnly = true, Label = "Edit page" }, "dnf-button", "ignored");
        Assert.Contains("dnf-button--icon-only", icon);
        Assert.Contains("aria-label=\"Edit page\"", icon);
        Assert.DoesNotContain("dnf-button__label", icon);
    }

    [Fact]
    public async Task Pending_buttons_are_busy()
    {
        var html = await RenderAsync(new DnfButtonTagHelper(new Versions()) { Pending = true }, "dnf-button", "Save");
        Assert.Contains("data-pending=\"true\"", html);
        Assert.Contains("aria-busy=\"true\"", html);
    }

    [Fact]
    public async Task Author_classes_from_razor_expressions_are_kept_and_encoded_once()
    {
        var html = await RenderAsync(new DnfBadgeTagHelper(new Versions()) { Tone = DnfTone.Success, Dot = true }, "dnf-badge", "Live",
            ("class", new HtmlString("ml-2 \"x\"")), ("id", "b"));
        Assert.Contains("class=\"dnf-badge dnf-badge--success dnf-badge--dot ml-2 \"x\"\"", html);

        var raw = await RenderAsync(new DnfBadgeTagHelper(new Versions()), "dnf-badge", "Plain", ("class", "a<b"));
        Assert.Contains("class=\"dnf-badge a&lt;b\"", raw);
    }

    [Fact]
    public async Task Alert_is_announced_for_danger_and_polite_otherwise()
    {
        var danger = await RenderAsync(new DnfAlertTagHelper(new Versions()) { Tone = DnfTone.Danger, Title = "Could not save" }, "dnf-alert", "Details");
        Assert.Contains("role=\"alert\"", danger);
        Assert.Contains("dnf-alert dnf-alert--danger", danger);
        Assert.Contains("<div class=\"dnf-alert__title\">Could not save</div>", danger);

        var info = await RenderAsync(new DnfAlertTagHelper(new Versions()), "dnf-alert", "Note");
        Assert.Contains("role=\"status\"", info);
    }

    [Fact]
    public async Task Icon_is_decorative_unless_labelled()
    {
        var decorative = await RenderAsync(new DnfIconTagHelper(new Versions()) { Name = "pages" }, "dnf-icon", "");
        Assert.Contains("aria-hidden=\"true\"", decorative);
        Assert.Contains("class=\"dnf-icon\"", decorative);

        var named = await RenderAsync(new DnfIconTagHelper(new Versions()) { Name = "pages", Size = DnfSize.Lg, Label = "Pages" }, "dnf-icon", "");
        Assert.Contains("role=\"img\"", named);
        Assert.Contains("aria-label=\"Pages\"", named);
        Assert.Contains("dnf-icon dnf-icon--lg", named);
    }

    [Fact]
    public async Task Dropdown_wires_the_button_to_its_popover_menu()
    {
        var html = await RenderAsync(new DnfDropdownTagHelper(new Versions()) { Label = "Actions for report.pdf" }, "dnf-dropdown",
            "<a class=\"dnf-dropdown__item\">Edit</a>");

        var id = System.Text.RegularExpressions.Regex.Match(html, "popovertarget=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(id));
        Assert.Contains($"id=\"{id}\" popover role=\"menu\"", html);
        Assert.Contains("aria-haspopup=\"menu\"", html);
        Assert.Contains("aria-label=\"Actions for report.pdf\"", html);
        Assert.Contains("dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only", html);
    }

    [Fact]
    public async Task Empty_state_renders_its_parts()
    {
        var html = await RenderAsync(new DnfEmptyStateTagHelper(new Versions()) { Title = "No pages", Description = "Create one.", Compact = true, HeadingLevel = 2 },
            "dnf-empty-state", "<a class=\"dnf-button\">New</a>");
        Assert.Contains("dnf-empty-state dnf-empty-state--compact", html);
        Assert.Contains("<h2 class=\"dnf-empty-state__title\">No pages</h2>", html);
        Assert.Contains("<p class=\"dnf-empty-state__description\">Create one.</p>", html);
        Assert.Contains("<div class=\"dnf-empty-state__actions\"><a class=\"dnf-button\">New</a></div>", html);
    }

    [Fact]
    public void The_sprite_url_keeps_the_apps_path_base()
    {
        var context = NewViewContext();
        Assert.Equal("/_content/DotNetForge.UI/icons.svg?v=test", DnfTagHelper.SpriteUrl(new Versions(), context));

        context.HttpContext.Request.PathBase = "/cms";
        Assert.Equal("/cms/_content/DotNetForge.UI/icons.svg?v=test", DnfTagHelper.SpriteUrl(new Versions(), context));
    }

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("ada@example.com", "AE")]
    [InlineData("x", "X")]
    [InlineData("", "?")]
    public void Avatar_initials(string name, string expected) => Assert.Equal(expected, DnfAvatarTagHelper.Initials(name));

    [Fact]
    public void Timestamps_read_relative_then_absolute()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("just now", DnfTimestampTagHelper.Relative(now.AddSeconds(-10), now));
        Assert.Equal("12 min ago", DnfTimestampTagHelper.Relative(now.AddMinutes(-12), now));
        Assert.Equal("yesterday", DnfTimestampTagHelper.Relative(now.AddHours(-30), now));
        Assert.Equal("3 Oct 2026", DnfTimestampTagHelper.Relative(new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), now));
    }

    [Fact]
    public async Task Validation_summary_lists_errors_or_renders_nothing()
    {
        var helper = new DnfValidationSummaryTagHelper { ViewContext = NewViewContext() };
        Assert.Equal(string.Empty, await RenderAsync(helper, "dnf-validation-summary", ""));

        helper.ViewContext.ViewData.ModelState.AddModelError("Name", "The name is required.");
        var html = await RenderAsync(helper, "dnf-validation-summary", "");
        Assert.Contains("class=\"dnf-validation-summary\" role=\"alert\"", html);
        Assert.Contains("<li>The name is required.</li>", html);
    }

    // ---- Plumbing ------------------------------------------------------------------------------------------------

    private static async Task<string> RenderAsync(TagHelper helper, string tagName, string childContent, params (string Name, object Value)[] attributes)
    {
        if (helper is DnfTagHelper dnf)
        {
            dnf.ViewContext = NewViewContext();
        }

        var all = new TagHelperAttributeList(attributes.Select(a => new TagHelperAttribute(a.Name, a.Value)));
        var context = new TagHelperContext(tagName, all, new Dictionary<object, object>(), Guid.NewGuid().ToString("N"));
        var output = new TagHelperOutput(tagName, new TagHelperAttributeList(all), (_, _) =>
            Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetHtmlContent(childContent)));

        helper.Init(context);
        await helper.ProcessAsync(context, output);

        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static ViewContext NewViewContext() => new()
    {
        HttpContext = new DefaultHttpContext(),
        ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
        ActionDescriptor = new ActionDescriptor(),
        RouteData = new RouteData(),
    };

    /// <summary>A file version provider that appends a fixed version, so the sprite URL is predictable.</summary>
    private sealed class Versions : IFileVersionProvider
    {
        public string AddFileVersionToPath(PathString requestPathBase, string path) => path + "?v=test";
    }
}
