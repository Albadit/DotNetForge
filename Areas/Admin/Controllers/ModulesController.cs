using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining sidebar items to a documented placeholder so the navigation has no dead
/// links (admin_area.md acceptance). These modules are part of the specification and their data
/// models exist; their full admin UI is layered on top of this foundation.
/// </summary>
public sealed class ModulesController : AdminControllerBase
{
    [HttpGet("/admin/content")]
    public IActionResult Content() => Placeholder("Content Manager", "content_manager.md",
        "Page tree, collection/single types, the page builder, drafts, publishing, and content history.");

    [HttpGet("/admin/file-manager")]
    public IActionResult FileManager() => Placeholder("File Manager", "file_manager.md",
        "Folders, uploads, public/private access, search, and image processing.");

    [HttpGet("/admin/marketplace")]
    public IActionResult Marketplace() => Placeholder("Marketplace", "extensions.md",
        "Browse and install extensions from a trusted marketplace source.");

    [HttpGet("/admin/overview")]
    public IActionResult Overview() => Placeholder("Overview", "settings.md",
        "Global settings hub, logo uploads, and the update workflow.");

    [HttpGet("/admin/content-history")]
    public IActionResult ContentHistory() => Placeholder("Content History", "content_manager.md",
        "Versioned content snapshots with compare and restore.");

    [HttpGet("/admin/internationalization")]
    public IActionResult Internationalization() => Placeholder("Internationalization", "internationalization.md",
        "Locales, default-locale rules, and content localization.");

    [HttpGet("/admin/transfer")]
    public IActionResult Transfer() => Placeholder("Transfer", "transfer_updates.md",
        "Provider-aware database import/export, plus core updates, rollback, and backups.");

    [HttpGet("/admin/webhooks")]
    public IActionResult Webhooks() => Placeholder("Webhooks", "webhooks.md",
        "Event subscriptions, custom headers, HMAC signing, retries, and delivery logging.");

    [HttpGet("/admin/email/configuration")]
    public IActionResult EmailConfiguration() => Placeholder("Email Configuration", "email.md",
        "SMTP configuration and test email.");

    [HttpGet("/admin/email/templates")]
    public IActionResult EmailTemplates() => Placeholder("Email Templates", "email.md",
        "Default email templates (confirmation, password reset, account locked).");

    [HttpGet("/admin/providers")]
    public IActionResult Providers() => Placeholder("Authentication Providers", "authentication.md",
        "Email and social/OAuth providers with per-provider settings.");

    [HttpGet("/admin/advanced-settings")]
    public IActionResult AdvancedSettings() => Placeholder("Advanced User Settings", "authentication.md",
        "Default role, one-account-per-email, sign-up enablement, and email confirmation.");

    private IActionResult Placeholder(string title, string specFile, string description) =>
        View("Placeholder", new ModulePlaceholderViewModel
        {
            Title = title,
            SpecFile = specFile,
            Description = description,
        });
}
