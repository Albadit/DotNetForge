using System.Collections.Concurrent;
using System.Reflection;
using DotNetForge.Extensions;
using DotNetForge.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace DotNetForge.Web.Areas.Admin.Navigation;

/// <summary>One sidebar link. <see cref="Controller"/> is the screen's controller; its <c>[Authorize]</c> rules decide
/// who sees the link (.docs/architecture/pages.md#navigation).</summary>
public sealed record NavLink(string Label, string Href, string Icon, Type Controller, bool Planned = false);

/// <summary>A collapsible sub-group of the Settings section.</summary>
public sealed record NavGroup(string Label, IReadOnlyList<NavLink> Links);

/// <summary>A top-level sidebar section: direct links and/or collapsible groups.</summary>
public sealed record NavSection(string Label, IReadOnlyList<NavLink> Links, IReadOnlyList<NavGroup> Groups);

/// <summary>A link as rendered for the current user and request.</summary>
public sealed record NavLinkView(string Label, string Href, string Icon, bool Planned, bool Active);

public sealed record NavGroupView(string Label, IReadOnlyList<NavLinkView> Links, bool ContainsActive);

public sealed record NavSectionView(string Label, IReadOnlyList<NavLinkView> Links, IReadOnlyList<NavGroupView> Groups);

/// <summary>The sidebar and the breadcrumb trail of the current admin request.</summary>
public sealed record AdminNavModel(IReadOnlyList<NavSectionView> Sections, IReadOnlyList<(string Label, string? Href)> Trail)
{
    /// <summary>Whether the user's sidebar contains <paramref name="href"/> - i.e. they may open that screen.</summary>
    public bool Allows(string href) =>
        Sections.Any(s => s.Links.Concat(s.Groups.SelectMany(g => g.Links)).Any(l => l.Href == href));
}

/// <summary>
/// The admin sidebar tree - the canonical spec tree (.docs/architecture/pages.md#canonical-sidebar-tree-vs-the-sidebar):
/// <b>Main</b> and one <b>Settings</b> section with four sub-groups, in spec order, plus <b>Extensions</b> for enabled
/// admin extensions. The only place navigation is defined; the sidebar and the breadcrumbs are built from it.
/// </summary>
public static class AdminNavigation
{
    public static readonly IReadOnlyList<NavSection> Tree = new[]
    {
        new NavSection("Main", new[]
        {
            new NavLink("Dashboard", "/admin", "dashboard", typeof(DashboardController)),
            // The pages, the structured content they present, and the types that shape it (.docs/features/structured-content.md).
            new NavLink("Pages", "/admin/pages", "pages", typeof(PagesController)),
            new NavLink("Content Manager", "/admin/content", "database", typeof(ContentController)),
            new NavLink("Content types", "/admin/content-types", "layers", typeof(ContentTypesController)),
            new NavLink("Media", "/admin/media", "image", typeof(MediaController)),
            new NavLink("Marketplace", "/admin/marketplace", "store", typeof(ModulesController), Planned: true),
        }, Array.Empty<NavGroup>()),
        new NavSection("Settings", Array.Empty<NavLink>(), new[]
        {
            new NavGroup("Global Settings", new[]
            {
                new NavLink("Settings", "/admin/settings", "settings", typeof(SettingsController)),
                new NavLink("API Tokens", "/admin/api-tokens", "key", typeof(ApiTokensController)),
                new NavLink("API Endpoints", "/admin/api-endpoints", "code", typeof(ApiEndpointsController)),
                new NavLink("API Docs", "/admin/api-docs", "book", typeof(ApiDocsController)),
                new NavLink("Content History", "/admin/content-history", "history", typeof(ModulesController), Planned: true),
                new NavLink("Internationalization", "/admin/internationalization", "globe", typeof(ModulesController), Planned: true),
                new NavLink("Plugins", "/admin/plugins", "puzzle", typeof(PluginsController)),
                new NavLink("Transfer", "/admin/transfer", "transfer", typeof(ModulesController), Planned: true),
                new NavLink("Webhooks", "/admin/webhooks", "webhook", typeof(ModulesController), Planned: true),
            }),
            new NavGroup("Administration Panel", new[]
            {
                new NavLink("Roles", "/admin/roles", "shield", typeof(RolesController)),
                new NavLink("Users", "/admin/users", "users", typeof(UsersController)),
                new NavLink("Audit Logs", "/admin/audit-logs", "activity", typeof(AuditLogsController)),
            }),
            new NavGroup("Email", new[]
            {
                new NavLink("Configuration", "/admin/email/configuration", "mail", typeof(ModulesController), Planned: true),
                new NavLink("Templates", "/admin/email/templates", "file-code", typeof(ModulesController), Planned: true),
            }),
            // The spec tree lists Roles here too; it is shown once, under Administration Panel
            // (.docs/architecture/pages.md#canonical-sidebar-tree-vs-the-sidebar).
            new NavGroup("Users & Permissions Plugin", new[]
            {
                new NavLink("Providers", "/admin/providers", "lock", typeof(ModulesController), Planned: true),
                new NavLink("Advanced Settings", "/admin/advanced-settings", "sliders", typeof(ModulesController), Planned: true),
            }),
        }),
    };
}

/// <summary>
/// Builds <see cref="AdminNavModel"/> for the current request: hides links whose controller the user may not open
/// (the same <c>[Authorize]</c> data the request pipeline enforces, evaluated through <see cref="IAuthorizationService"/>
/// - hiding is never the gate), marks the active link (the longest matching href, so child routes such as
/// <c>/admin/api-tokens/create</c> count; the first occurrence when a link appears twice) and appends the enabled admin
/// extensions. Built once per request.
/// </summary>
public sealed class AdminNavigationService
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IAuthorizeData>> AuthorizeData = new();

    private readonly IAuthorizationService _authorization;
    private readonly IAuthorizationPolicyProvider _policies;
    private readonly ExtensionCatalog _catalog;
    private AdminNavModel? _built;

    public AdminNavigationService(IAuthorizationService authorization, IAuthorizationPolicyProvider policies, ExtensionCatalog catalog)
    {
        _authorization = authorization;
        _policies = policies;
        _catalog = catalog;
    }

    public async Task<AdminNavModel> BuildAsync(HttpContext http, string? pageTitle)
    {
        if (_built is not null)
        {
            return _built;
        }

        var path = (http.Request.Path.Value ?? "/admin").TrimEnd('/').ToLowerInvariant();
        var allowed = new Dictionary<Type, bool>();
        async Task<bool> CanOpenAsync(Type controller)
        {
            if (!allowed.TryGetValue(controller, out var can))
            {
                var policy = await AuthorizationPolicy.CombineAsync(_policies, AuthorizeData.GetOrAdd(controller, Collect));
                can = policy is null || (await _authorization.AuthorizeAsync(http.User, policy)).Succeeded;
                allowed[controller] = can;
            }

            return can;
        }

        var extensions = (await _catalog.GetActiveAsync("admin", http.RequestAborted))
            .Select(e => new NavLink(e.Context.Name, $"/admin/ext/{e.Id}", "panel", typeof(ExtensionsController)))
            .ToList();
        var tree = AdminNavigation.Tree.ToList();
        if (extensions.Count > 0)
        {
            tree.Add(new NavSection("Extensions", extensions, Array.Empty<NavGroup>()));
        }

        // The active link: the longest href that is the path or a parent of it ("/admin" only matches itself).
        var activeHref = tree
            .SelectMany(s => s.Links.Concat(s.Groups.SelectMany(g => g.Links)))
            .Select(l => l.Href.ToLowerInvariant())
            .Where(h => path == h || (h != "/admin" && path.StartsWith(h + "/", StringComparison.Ordinal)))
            .OrderByDescending(h => h.Length)
            .FirstOrDefault();

        var trail = new List<(string, string?)>();
        var activeTaken = false;
        NavLinkView View(NavLink link, string section, string? group)
        {
            var active = !activeTaken && string.Equals(link.Href, activeHref, StringComparison.OrdinalIgnoreCase);
            if (active)
            {
                activeTaken = true;
                trail.Add((section, null));
                if (group is not null)
                {
                    trail.Add((group, null));
                }

                trail.Add((link.Label, link.Href));
            }

            return new NavLinkView(link.Label, link.Href, link.Icon, link.Planned, active);
        }

        var sections = new List<NavSectionView>();
        foreach (var section in tree)
        {
            var links = new List<NavLinkView>();
            foreach (var link in section.Links)
            {
                if (await CanOpenAsync(link.Controller))
                {
                    links.Add(View(link, section.Label, null));
                }
            }

            var groups = new List<NavGroupView>();
            foreach (var group in section.Groups)
            {
                var groupLinks = new List<NavLinkView>();
                foreach (var link in group.Links)
                {
                    if (await CanOpenAsync(link.Controller))
                    {
                        groupLinks.Add(View(link, section.Label, group.Label));
                    }
                }

                if (groupLinks.Count > 0)
                {
                    groups.Add(new NavGroupView(group.Label, groupLinks, groupLinks.Any(l => l.Active)));
                }
            }

            if (links.Count > 0 || groups.Count > 0)
            {
                sections.Add(new NavSectionView(section.Label, links, groups));
            }
        }

        // A sub-screen (e.g. "Create API token") adds its own title after the screen it belongs to.
        if (pageTitle is not null && (trail.Count == 0 || !string.Equals(trail[^1].Item1, pageTitle, StringComparison.OrdinalIgnoreCase)))
        {
            trail.Add((pageTitle, null));
        }

        return _built = new AdminNavModel(sections, trail);
    }

    /// <summary>The <c>[Authorize]</c> data of a controller and its base classes (what the pipeline combines too).</summary>
    private static IReadOnlyList<IAuthorizeData> Collect(Type controller) =>
        controller.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().ToList();
}
