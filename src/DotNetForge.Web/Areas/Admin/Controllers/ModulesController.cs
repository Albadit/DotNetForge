using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining planned-but-unbuilt sidebar items to a placeholder that names the document holding their
/// planned behaviour, so the navigation has no dead links (.docs/pages/module-placeholders.md). The built screens
/// (Content, Media, Settings, Advanced Settings, Providers, Two-factor authentication, Email Configuration, Email Templates, Users, Roles, Audit Logs, Plugins, API Tokens) have their own controllers.
/// </summary>
public sealed class ModulesController : AdminControllerBase
{
    [HttpGet("/admin/marketplace")]
    public IActionResult Marketplace() => Placeholder("Marketplace",
        ".docs/features/extensions.md", "Browse and install extensions from a trusted marketplace source.");

    [HttpGet("/admin/content-history")]
    public IActionResult ContentHistory() => Placeholder("Content History",
        ".docs/features/content-pages-and-routing.md", "Versioned content snapshots with compare and restore.");

    [HttpGet("/admin/internationalization")]
    public IActionResult Internationalization() => Placeholder("Internationalization",
        ".docs/features/internationalization.md", "Locales, default-locale rules, and content localization.");

    [HttpGet("/admin/webhooks")]
    public IActionResult Webhooks() => Placeholder("Webhooks",
        ".docs/features/webhooks.md", "Event subscriptions, custom headers, HMAC signing, retries, and delivery logging.");

    private IActionResult Placeholder(string title, string docPath, string description) =>
        View("Placeholder", new ModulePlaceholderViewModel
        {
            Title = title,
            DocPath = docPath,
            Description = description,
        });
}
