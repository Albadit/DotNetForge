using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Routes the remaining planned-but-unbuilt sidebar items to a placeholder that names the document holding their
/// planned behaviour, so the navigation has no dead links (.docs/pages/module-placeholders.md). The built screens
/// (Content, Media, Settings, Users, Roles, Audit Logs, Plugins, API Tokens) have their own controllers.
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

    [HttpGet("/admin/transfer")]
    public IActionResult Transfer() => Placeholder("Transfer",
        ".docs/features/transfer-and-updates.md", "Provider-aware database import/export, plus core updates, rollback, and backups.");

    [HttpGet("/admin/webhooks")]
    public IActionResult Webhooks() => Placeholder("Webhooks",
        ".docs/features/webhooks.md", "Event subscriptions, custom headers, HMAC signing, retries, and delivery logging.");

    [HttpGet("/admin/email/configuration")]
    public IActionResult EmailConfiguration() => Placeholder("Email Configuration",
        ".docs/features/email.md", "SMTP configuration and test email.");

    [HttpGet("/admin/email/templates")]
    public IActionResult EmailTemplates() => Placeholder("Email Templates",
        ".docs/features/email.md", "Default email templates (confirmation, password reset, account locked).");

    [HttpGet("/admin/providers")]
    public IActionResult Providers() => Placeholder("Authentication Providers",
        ".docs/features/authentication.md", "Email and social/OAuth providers with per-provider settings.");

    [HttpGet("/admin/advanced-settings")]
    public IActionResult AdvancedSettings() => Placeholder("Advanced User Settings",
        ".docs/features/authentication.md", "Default role, one-account-per-email, sign-up enablement, and email confirmation.");

    private IActionResult Placeholder(string title, string docPath, string description) =>
        View("Placeholder", new ModulePlaceholderViewModel
        {
            Title = title,
            DocPath = docPath,
            Description = description,
        });
}
