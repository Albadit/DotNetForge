namespace DotNetForge.Web.Areas.Admin.UiDocs;

/// <summary>One page of the UI docs: a guide (<c>/admin/docs/ui/{slug}</c>) or a component
/// (<c>/admin/docs/ui/components/{slug}</c>).</summary>
public sealed record UiDocsPage(string Slug, string Title, string Description, string Category, bool IsComponent)
{
    public string Href => IsComponent ? $"{UiDocsCatalog.Root}/components/{Slug}" : $"{UiDocsCatalog.Root}/{Slug}";

    /// <summary>The view: <c>Guides/Theming.cshtml</c>, <c>Components/InputGroup.cshtml</c>.</summary>
    public string ViewPath =>
        $"~/Areas/Admin/Views/UiDocs/{(IsComponent ? "Components" : "Guides")}/{string.Concat(Slug.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]))}.cshtml";
}

/// <summary>A group of the docs navigation.</summary>
public sealed record UiDocsCategory(string Name, IReadOnlyList<UiDocsPage> Pages);

/// <summary>What a docs view renders: its page, the navigation and the pages before and after it.</summary>
public sealed record UiDocsViewModel(UiDocsPage Page, UiDocsPage? Previous, UiDocsPage? Next)
{
    public IReadOnlyList<UiDocsCategory> Categories => UiDocsCatalog.Categories;
}

/// <summary>
/// The pages of the UI docs (.docs/pages/ui-docs.md), in navigation order - the only place they are listed. Each page
/// is a Razor view in <c>Areas/Admin/Views/UiDocs/</c>; its examples are <c>&lt;ui-doc-example&gt;</c> blocks.
/// </summary>
public static class UiDocsCatalog
{
    public const string Root = "/admin/docs/ui";

    public static readonly IReadOnlyList<UiDocsCategory> Categories = new UiDocsCategory[]
    {
        Guides("Getting started",
            ("introduction", "Introduction", "What DotNetForge UI is, and how the components, classes, tokens and behaviours fit together."),
            ("installation", "Using the library", "The view imports, the three assets and the icon sprite - in an admin screen or an extension."),
            ("customization", "Customization", "Three levels: component properties, Tailwind utilities, and your own CSS and tokens - no !important."),
            ("theming", "Theming", "The design tokens, light and dark, and re-theming a part of a page."),
            ("behaviours", "Behaviours", "dnf.js: the data-* hooks for menus, dialogs, tabs, collections and more, and the dnfUI API."),
            ("extensions", "Extensions", "Building an extension's screens with the library: scoping, assets, overlays.")),
        Components("Buttons",
            ("button", "Button", "A clickable button or link, in seven variants and three sizes, with icons and a pending state.")),
        Components("Forms",
            ("form", "Form", "The form, its grid of fields and the action row."),
            ("field", "Field", "A label, a control, a description and an error message."),
            ("input", "Input", "A text control in three sizes, with invalid, disabled and read-only states."),
            ("input-group", "Input group", "An input with a leading icon, a text prefix or suffix, or a trailing action."),
            ("select", "Select", "A choice from a list, with the theme's chevron and a styled option list."),
            ("textarea", "Textarea", "Multi-line text, also for code."),
            ("checkbox", "Checkbox", "A checkbox with its label and description, alone or in a list."),
            ("radio", "Radio", "One choice of several."),
            ("switch", "Switch", "An on/off setting, alone or as a settings row."),
            ("choice-card", "Choice card", "Radio or checkbox options drawn as selectable cards."),
            ("file-upload", "File upload", "A drop area around a real file input."),
            ("fieldset", "Fieldset", "Groups related controls under a legend, framed or not.")),
        Components("Data display",
            ("table", "Table", "Rows with sortable headers, selection and a stacked layout on phones."),
            ("toolbar", "Toolbar", "Search and filters above a collection, and the bulk bar for selected items."),
            ("pagination", "Pagination", "Page buttons and rows per page, rendered by dnf.js."),
            ("card", "Card", "A surface with a header, body and footer."),
            ("stat-card", "Stat card", "A key figure with its icon and label."),
            ("badge", "Badge", "A small status label, and the status dot."),
            ("chip", "Chip", "A removable value."),
            ("avatar", "Avatar", "A person's initials or picture."),
            ("description-list", "Description list", "Label and value pairs."),
            ("list", "List", "Rows of a card: activity, members, settings."),
            ("code-block", "Code block", "Code with a copy button."),
            ("timestamp", "Timestamp", "A UTC date shown relative to now."),
            ("icon", "Icon", "The icon sprite and its sizes.")),
        Components("Feedback",
            ("alert", "Alert", "An inline message with an icon, and the validation summary."),
            ("toast", "Toast", "A short notice in the corner of the screen."),
            ("progress", "Progress", "How far a task has come."),
            ("spinner", "Spinner", "Something is loading."),
            ("skeleton", "Skeleton", "A placeholder while content loads."),
            ("empty-state", "Empty state", "What a list shows when there is nothing in it.")),
        Components("Navigation",
            ("page-header", "Page header", "A screen's title, description and actions."),
            ("breadcrumbs", "Breadcrumbs", "Where the current screen sits."),
            ("tabs", "Tabs", "Panels of one screen, one at a time."),
            ("segmented", "Segmented", "A small group of exclusive options."),
            ("accordion", "Accordion", "Sections that open and close.")),
        Components("Overlays",
            ("dialog", "Dialog", "A modal window, the confirmation and the drawer."),
            ("dropdown", "Dropdown", "A button that opens a menu of actions."),
            ("popover", "Popover", "A non-modal panel next to its button."),
            ("tooltip", "Tooltip", "A short explanation of a control.")),
        Components("Layout",
            ("layout", "Layout", "Stack, cluster, spread, grid, split, container, divider and note.")),
    };

    public static IEnumerable<UiDocsPage> Pages => Categories.SelectMany(c => c.Pages);

    public static UiDocsPage? Find(string slug, bool component) =>
        Pages.FirstOrDefault(p => p.IsComponent == component && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static UiDocsViewModel ViewModel(UiDocsPage page)
    {
        var all = Pages.ToList();
        var index = all.IndexOf(page);
        return new UiDocsViewModel(page, index > 0 ? all[index - 1] : null, index < all.Count - 1 ? all[index + 1] : null);
    }

    private static UiDocsCategory Guides(string name, params (string Slug, string Title, string Description)[] pages) =>
        new(name, pages.Select(p => new UiDocsPage(p.Slug, p.Title, p.Description, name, IsComponent: false)).ToList());

    private static UiDocsCategory Components(string name, params (string Slug, string Title, string Description)[] pages) =>
        new(name, pages.Select(p => new UiDocsPage(p.Slug, p.Title, p.Description, name, IsComponent: true)).ToList());
}
