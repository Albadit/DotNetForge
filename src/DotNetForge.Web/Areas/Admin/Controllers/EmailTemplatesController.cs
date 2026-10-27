using System.Net;
using System.Text;
using DotNetForge.Core.Email;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Middleware;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Email → Templates (.docs/pages/email-templates.md): the emails the CMS sends, each with a customizable subject and
/// HTML body, a preview with sample data and "restore default". Reading needs Settings read; saving and restoring
/// Settings update. Rules: <see cref="EmailTemplateRenderer"/> (variables) and <see cref="EmailTemplateService"/>
/// (storage, concurrency, audit).
/// </summary>
[Route("admin/email/templates")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class EmailTemplatesController : AdminControllerBase
{
    private readonly EmailTemplateService _templates;

    public EmailTemplatesController(EmailTemplateService templates)
    {
        _templates = templates;
    }

    /// <summary>The editor: the template as stored (or as posted, after a failed save).</summary>
    public sealed class EmailTemplateEditViewModel
    {
        public required EmailTemplateState State { get; init; }
        public required string Subject { get; init; }
        public required string Body { get; init; }
        public long Version { get; init; }
        public bool CanEdit { get; init; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _templates.ListAsync(HttpContext.RequestAborted));

    [HttpGet("{key}")]
    public async Task<IActionResult> Edit(string key)
    {
        if (await _templates.GetAsync(key, HttpContext.RequestAborted) is not { } state)
        {
            return NotFound();
        }

        HttpContext.Items[SecurityHeadersMiddleware.PagePolicyItemKey] = SecurityHeadersMiddleware.CodeEditorPolicy;
        return View(new EmailTemplateEditViewModel
        {
            State = state,
            Subject = state.Subject,
            Body = state.Body,
            Version = state.Version,
            CanEdit = Can(PermissionAreas.Settings, PermissionActions.Update),
        });
    }

    [HttpPost("{key}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(string key, string? subject, string? body, long version)
    {
        if (await _templates.GetAsync(key, HttpContext.RequestAborted) is not { } state)
        {
            return NotFound();
        }

        // SaveAsync writes the SettingsChanged audit entry.
        var errors = await _templates.SaveAsync(key, subject, body, version, CurrentUserId, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            HttpContext.Items[SecurityHeadersMiddleware.PagePolicyItemKey] = SecurityHeadersMiddleware.CodeEditorPolicy;
            return View(nameof(Edit), new EmailTemplateEditViewModel
            {
                State = state,
                Subject = subject ?? string.Empty,
                Body = body ?? string.Empty,
                Version = version,
                CanEdit = true,
            });
        }

        TempData["Success"] = $"Saved '{state.Definition.Name}'.";
        return RedirectToAction(nameof(Edit), new { key });
    }

    [HttpPost("{key}/restore")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Restore(string key)
    {
        if (EmailTemplateCatalog.Find(key) is not { } definition)
        {
            return NotFound();
        }

        // RestoreAsync audits only when there was something to restore.
        TempData["Success"] = await _templates.RestoreAsync(key, HttpContext.RequestAborted)
            ? $"'{definition.Name}' uses the default again."
            : $"'{definition.Name}' already uses the default.";
        return RedirectToAction(nameof(Edit), new { key });
    }

    /// <summary>The saved template with sample data - the editor's preview frame on load.</summary>
    [HttpGet("{key}/preview")]
    public async Task<IActionResult> Preview(string key)
    {
        if (await _templates.GetAsync(key, HttpContext.RequestAborted) is not { } state)
        {
            return NotFound();
        }

        return PreviewDocument(state.Definition, state.Subject, state.Body);
    }

    /// <summary>
    /// The editor's unsaved text with sample data (email-templates.js posts it into the preview frame while you type).
    /// Changes nothing, so nothing is audited; the antiforgery check still applies.
    /// </summary>
    [HttpPost("{key}/preview")]
    [ValidateAntiForgeryToken]
    public IActionResult PreviewChanges(string key, string? subject, string? body)
    {
        if (EmailTemplateCatalog.Find(key) is not { } definition)
        {
            return NotFound();
        }

        return PreviewDocument(definition, subject?.Trim(), EmailTemplateService.Normalize(body));
    }

    /// <summary>
    /// The email as it is sent - the rendered body in <see cref="EmailLayout"/> - with the subject on top, served with
    /// <see cref="SecurityHeadersMiddleware.EmailPreviewPolicy"/> (no script, sandboxed) because the body is
    /// administrator-written HTML. Invalid text shows the validation errors instead of a broken render.
    /// </summary>
    private ContentResult PreviewDocument(EmailTemplateDefinition definition, string? subject, string? body)
    {
        HttpContext.Items[SecurityHeadersMiddleware.PolicyOverrideItemKey] = SecurityHeadersMiddleware.EmailPreviewPolicy;
        var errors = EmailTemplateRenderer.Validate(definition, subject, body);
        if (errors.Count == 0)
        {
            var values = definition.SampleValues;
            var banner = "<div style=\"padding:10px 16px;border-bottom:1px solid " + EmailLayout.Border + ";background:#ffffff;font:13px/1.5 "
                + EmailLayout.Font + ";color:" + EmailLayout.Text + ";\"><b style=\"color:" + EmailLayout.Muted + ";margin-right:6px;\">Subject</b>"
                + WebUtility.HtmlEncode(EmailTemplateRenderer.RenderSubject(subject!, values)) + "</div>";
            return Content(EmailLayout.Wrap(EmailTemplateRenderer.RenderBody(body!, values), values["appName"], values["appUrl"], banner),
                "text/html; charset=utf-8");
        }

        var html = new StringBuilder("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Preview</title>")
            .Append("<style>body{margin:0;font:15px/1.5 system-ui,sans-serif;color:#1f2328;background:#fff}")
            .Append(".errors{padding:16px;color:#b42318}</style></head><body><div class=\"errors\">")
            .Append("<p><b>This template can't be rendered:</b></p><ul>");
        foreach (var error in errors)
        {
            html.Append("<li>").Append(WebUtility.HtmlEncode(error)).Append("</li>");
        }

        return Content(html.Append("</ul></div></body></html>").ToString(), "text/html; charset=utf-8");
    }
}
