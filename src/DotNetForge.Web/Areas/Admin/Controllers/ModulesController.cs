using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services.Localization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining planned-but-unbuilt sidebar items to a placeholder that names the document holding their
/// planned behaviour, so the navigation has no dead links (.docs/pages/module-placeholders.md). The built screens
/// (Content, Media, Settings, Advanced Settings, Internationalization, Providers, Two-factor authentication, Webhooks, Email Configuration, Email Templates, Users, Roles, Audit Logs, Plugins, API Tokens) have their own controllers.
/// </summary>
public sealed class ModulesController : AdminControllerBase
{
    [HttpGet("/admin/marketplace")]
    public IActionResult Marketplace() => Placeholder(AdminText.Key("Marketplace"),
        ".docs/features/extensions.md", AdminText.Key("Browse and install extensions from a trusted marketplace source."));

    [HttpGet("/admin/content-history")]
    public IActionResult ContentHistory() => Placeholder(AdminText.Key("Content History"),
        ".docs/features/content-pages-and-routing.md", AdminText.Key("Versioned content snapshots with compare and restore."));


    private IActionResult Placeholder(string title, string docPath, string description) =>
        View("Placeholder", new ModulePlaceholderViewModel
        {
            Title = title,
            DocPath = docPath,
            Description = description,
        });
}
