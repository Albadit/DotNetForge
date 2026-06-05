using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining legacy-admin sidebar items to a documented placeholder so the navigation has
/// no dead links. Served under /admin-legacy (the primary admin is the React SPA at /admin).
/// </summary>
public sealed class ModulesController : AdminControllerBase
{
    [HttpGet("/admin-legacy/content")]
    public IActionResult Content() => Placeholder("Content Manager", "content_manager.md",
        "Page tree, collection/single types, the page builder, drafts, publishing, and content history.");

    [HttpGet("/admin-legacy/file-manager")]
    public IActionResult FileManager() => Placeholder("File Manager", "file_manager.md",
        "Folders, uploads, public/private access, search, and image processing.");

    [HttpGet("/admin-legacy/marketplace")]
    public IActionResult Marketplace() => Placeholder("Marketplace", "extensions.md",
        "Browse and install extensions from a trusted marketplace source.");

    [HttpGet("/admin-legacy/overview")]
    public IActionResult Overview() => Placeholder("Overview", "settings.md",
        "Global settings hub, logo uploads, and the update workflow.");

    [HttpGet("/admin-legacy/content-history")]
    public IActionResult ContentHistory() => Placeholder("Content History", "content_manager.md",
        "Versioned content snapshots with compare and restore.");

    [HttpGet("/admin-legacy/internationalization")]
    public IActionResult Internationalization() => Placeholder("Internationalization", "internationalization.md",
        "Locales, default-locale rules, and content localization.");

    [HttpGet("/admin-legacy/transfer")]
    public IActionResult Transfer() => Placeholder("Transfer", "transfer_updates.md",
        "Provider-aware database import/export, plus core updates, rollback, and backups.");

    [HttpGet("/admin-legacy/webhooks")]
    public IActionResult Webhooks() => Placeholder("Webhooks", "webhooks.md",
        "Event subscriptions, custom headers, HMAC signing, retries, and delivery logging.");

    [HttpGet("/admin-legacy/email/configuration")]
    public IActionResult EmailConfiguration() => Placeholder("Email Configuration", "email.md",
        "SMTP configuration and test email.");

    [HttpGet("/admin-legacy/email/templates")]
    public IActionResult EmailTemplates() => Placeholder("Email Templates", "email.md",
        "Default email templates (confirmation, password reset, account locked).");

    [HttpGet("/admin-legacy/providers")]
    public IActionResult Providers() => Placeholder("Authentication Providers", "authentication.md",
        "Email and social/OAuth providers with per-provider settings.");

    [HttpGet("/admin-legacy/advanced-settings")]
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
