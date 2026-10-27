using DotNetForge.Abstractions.Messaging;
using DotNetForge.Core.Email;
using DotNetForge.Core.Validation;
using DotNetForge.Data;
using DotNetForge.Infrastructure.Messaging;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Email → Configuration (.docs/pages/email-configuration.md): the SMTP settings from <c>.env</c> (read-only - they are
/// changed in <c>.env</c>), a connection test, test sends (a plain message or an email template with sample data), the
/// email log and, in Development, the local Mailpit inbox. Reading needs Settings read; tests need Settings update.
/// </summary>
[Route("admin/email/configuration")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed partial class EmailConfigurationController : AdminControllerBase
{
    private const int LogSize = 50;

    private readonly EmailConfigurationService _config;
    private readonly EmailTemplateService _templates;
    private readonly IEmailSender _sender;
    private readonly IAuditService _audit;
    private readonly DotNetForgeDbContext _db;
    private readonly AppEnvironment _env;
    private readonly ILogger<EmailConfigurationController> _logger;

    public EmailConfigurationController(EmailConfigurationService config, EmailTemplateService templates, IEmailSender sender,
        IAuditService audit, DotNetForgeDbContext db, AppEnvironment env, ILogger<EmailConfigurationController> logger)
    {
        _config = config;
        _templates = templates;
        _sender = sender;
        _audit = audit;
        _db = db;
        _env = env;
        _logger = logger;
    }

    /// <summary>The screen: the settings from <c>.env</c> (never the password), the tools and the email log.</summary>
    public sealed class EmailConfigurationViewModel
    {
        public required EmailSettings Settings { get; init; }
        public required string AppName { get; init; }
        public bool CanTest { get; init; }
        public string? TestRecipient { get; init; }
        public string? TestTemplate { get; init; }
        public string? MailpitInboxUrl { get; init; }
        public SmtpProbeResult? Probe { get; init; }
        public required List<EmailLogEntry> Log { get; init; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await BuildAsync(probe: null));

    /// <summary>Connects, starts TLS and signs in without sending anything; the steps are shown on the screen.</summary>
    [HttpPost("connection")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> TestConnection()
    {
        if (!_config.Settings.IsConfigured)
        {
            TempData["Error"] = "Email is not configured: set SMTP_HOST and SMTP_FROM in .env.";
            return RedirectToAction(nameof(Index));
        }

        var probe = await SmtpProbe.RunAsync(_config.Settings, HttpContext.RequestAborted);
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Email configuration",
            details: probe.Succeeded ? "Connection test passed" : "Connection test failed", success: probe.Succeeded);
        return View(nameof(Index), await BuildAsync(probe));
    }

    /// <summary>Sends a plain test message, or <paramref name="template"/> rendered with its sample data.</summary>
    [HttpPost("test")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Test(string? recipient, string? template)
    {
        var to = recipient?.Trim() ?? string.Empty;
        var definition = string.IsNullOrEmpty(template) ? null : EmailTemplateCatalog.Find(template);
        TempData["TestRecipient"] = to;
        TempData["TestTemplate"] = definition?.Key;
        if (!EmailValidator.IsValid(to))
        {
            TempData["Error"] = "Enter a valid email address to send the test to.";
            return RedirectToAction(nameof(Index));
        }

        if (!_config.Settings.IsConfigured)
        {
            TempData["Error"] = "Email is not configured: set SMTP_HOST and SMTP_FROM in .env.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(template) && definition is null)
        {
            return NotFound();
        }

        var what = definition is null ? "Test email" : $"Test of '{definition.Name}'";
        try
        {
            await _sender.SendAsync(await TestMessageAsync(to, definition), HttpContext.RequestAborted);
            TempData["Success"] = $"{what} sent to {to}.";
            await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Email configuration", details: what + " sent");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The admin sees the kind of failure; the log gets the type only (SMTP errors can echo addresses).
            LogTestFailed(ex.GetType().Name);
            TempData["Error"] = EmailConfigurationService.DescribeFailure(ex, _config.Settings);
            await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Email configuration",
                details: what + " failed", success: false);
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<EmailMessage> TestMessageAsync(string to, EmailTemplateDefinition? definition)
    {
        if (definition is null)
        {
            return new EmailMessage
            {
                To = to,
                Subject = $"Test email from {_env.AppName}",
                Body = $"<p>This is a test email from {System.Net.WebUtility.HtmlEncode(_env.AppName)}. Your email settings work.</p>",
                IsHtml = true,
            };
        }

        // The template as it is sent (customized or default), with its sample data.
        var (subject, body) = await _templates.RenderAsync(definition.Key, definition.SampleValues, HttpContext.RequestAborted);
        return new EmailMessage { To = to, Subject = "[Test] " + subject, Body = body, IsHtml = true, Template = definition.Key };
    }

    private async Task<EmailConfigurationViewModel> BuildAsync(SmtpProbeResult? probe) => new()
    {
        Settings = _config.Settings,
        AppName = _env.AppName,
        CanTest = Can(PermissionAreas.Settings, PermissionActions.Update),
        TestRecipient = TempData["TestRecipient"] as string ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
        TestTemplate = TempData["TestTemplate"] as string,
        MailpitInboxUrl = _config.MailpitInboxUrl,
        Probe = probe,
        Log = await _db.EmailLog.AsNoTracking().OrderByDescending(e => e.SentAt).Take(LogSize).ToListAsync(HttpContext.RequestAborted),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "A test email failed with {ErrorType}.")]
    private partial void LogTestFailed(string errorType);
}
