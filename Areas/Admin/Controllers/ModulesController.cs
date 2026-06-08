using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining specced-but-unbuilt sidebar items to a documented placeholder so the navigation
/// has no dead links (admin_area.md). The built screens (Content, Media, Settings, Users, Roles, Audit
/// Logs, Plugins, API Tokens) have their own controllers.
/// </summary>
public sealed class ModulesController : AdminControllerBase
{
    [HttpGet("/admin/marketplace")]
    public IActionResult Marketplace() => Placeholder("Marketplace", "extensions.md",
        "Browse and install extensions from a trusted marketplace source.");

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
