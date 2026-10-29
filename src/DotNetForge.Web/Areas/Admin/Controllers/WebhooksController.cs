using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services.Webhooks;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Settings → API &amp; Webhooks → Webhooks (.docs/pages/webhooks.md): list, create, edit, enable/disable, delete,
/// rotate the secret, send a test delivery and read the delivery log. Webhooks permission area: read to view,
/// create, update (edit, enable/disable, rotate, test) and delete. The rules live in <see cref="WebhookService"/>.
/// </summary>
[Route("admin/webhooks")]
[RequirePermission(PermissionAreas.Webhooks, PermissionActions.Read)]
public sealed class WebhooksController : AdminControllerBase
{
    private const string SecretKey = "dnf.webhook-secret";
    private const int LogPageSize = 50;

    private readonly WebhookService _webhooks;
    private readonly DotNetForgeDbContext _db;

    public WebhooksController(WebhookService webhooks, DotNetForgeDbContext db)
    {
        _webhooks = webhooks;
        _db = db;
    }

    public sealed record WebhookRow(Webhook Webhook, WebhookDelivery? Last, int Failures24h);

    public sealed record WebhooksViewModel(IReadOnlyList<WebhookRow> Rows, bool CanCreate, bool CanUpdate, bool CanDelete);

    /// <summary>The editor; <see cref="Id"/> is null when creating.</summary>
    public sealed record WebhookEditViewModel(Guid? Id, WebhookInput Input, DateTime? Created, DateTime? LastDelivery, bool CanSave, bool CanDelete);

    public sealed record SecretViewModel(Guid Id, string Name, string Secret, bool Created);

    public sealed record DeliveriesViewModel(Webhook Webhook, IReadOnlyList<WebhookDelivery> Deliveries, int Total, bool CanUpdate);

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var ct = HttpContext.RequestAborted;
        var webhooks = await _db.Webhooks.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);
        var ids = webhooks.Select(w => w.Id).ToList();
        var since = DateTime.UtcNow.AddDays(-1);
        // The latest finished attempt per webhook, and failures in the last day (two single-table queries).
        var recent = await _db.WebhookDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.WebhookId) && d.Outcome != WebhookOutcome.Pending && d.RequestTimestamp > since)
            .Select(d => new { d.WebhookId, d.Outcome, d.RequestTimestamp, d.ResponseStatusCode, d.Event, d.Id })
            .ToListAsync(ct);
        var latest = await _db.WebhookDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.WebhookId) && d.Outcome != WebhookOutcome.Pending)
            .GroupBy(d => d.WebhookId)
            .Select(g => g.OrderByDescending(d => d.RequestTimestamp).First())
            .ToListAsync(ct);
        var rows = webhooks.Select(w => new WebhookRow(w, latest.FirstOrDefault(d => d.WebhookId == w.Id),
            recent.Count(d => d.WebhookId == w.Id && d.Outcome == WebhookOutcome.Failure))).ToList();
        return View(new WebhooksViewModel(rows, Can(PermissionAreas.Webhooks, PermissionActions.Create),
            Can(PermissionAreas.Webhooks, PermissionActions.Update), Can(PermissionAreas.Webhooks, PermissionActions.Delete)));
    }

    [HttpGet("create")]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Create)]
    public IActionResult Create() =>
        View("Edit", new WebhookEditViewModel(null, new WebhookInput { Enabled = true }, null, null, true, false));

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Create)]
    public async Task<IActionResult> Create(WebhookInput input)
    {
        // CreateAsync writes the webhook.created audit entry.
        var (webhook, secret, errors) = await _webhooks.CreateAsync(input, HttpContext.RequestAborted);
        if (webhook is null)
        {
            AddErrors(errors);
            return View("Edit", new WebhookEditViewModel(null, input, null, null, true, false));
        }

        return ShowSecret(webhook, secret!, created: true);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (webhook is null)
        {
            return NotFound();
        }

        return View(Editor(webhook, new WebhookInput
        {
            Name = webhook.Name,
            Url = webhook.Url,
            Enabled = webhook.Enabled,
            Events = webhook.Events.ToList(),
            Headers = WebhookService.ReadHeaders(webhook.HeadersJson).ToList(),
        }));
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Update)]
    public async Task<IActionResult> Edit(Guid id, WebhookInput input)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (webhook is null)
        {
            return NotFound();
        }

        // UpdateAsync writes webhook.updated (and webhook.enabled/disabled when that changed).
        var errors = await _webhooks.UpdateAsync(id, input, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            AddErrors(errors);
            return View(Editor(webhook, input));
        }

        TempData["Success"] = $"Saved '{input.Name?.Trim()}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Update)]
    public Task<IActionResult> Enable(Guid id, string? returnTo) => SetEnabledAsync(id, true, returnTo);

    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Update)]
    public Task<IActionResult> Disable(Guid id, string? returnTo) => SetEnabledAsync(id, false, returnTo);

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        // DeleteAsync writes the webhook.deleted audit entry.
        var name = await _webhooks.DeleteAsync(id, HttpContext.RequestAborted);
        TempData[name is null ? "Error" : "Success"] = name is null ? "The webhook no longer exists." : $"Deleted '{name}' and its delivery log.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/rotate")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Update)]
    public async Task<IActionResult> Rotate(Guid id)
    {
        // RotateSecretAsync writes the webhook.secret.rotated audit entry.
        var secret = await _webhooks.RotateSecretAsync(id, HttpContext.RequestAborted);
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        return secret is null || webhook is null ? NotFound() : ShowSecret(webhook, secret, created: false);
    }

    /// <summary>The secret panel after create/rotate - reached through a redirect, so a reload doesn't show it again.</summary>
    [HttpGet("{id:guid}/secret")]
    public async Task<IActionResult> Secret(Guid id)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (webhook is null || TempData[SecretKey] is not string value || !value.StartsWith(id + "|", StringComparison.Ordinal))
        {
            return RedirectToAction(nameof(Edit), new { id });
        }

        var parts = value.Split('|', 3);
        return View(new SecretViewModel(id, webhook.Name, parts[2], parts[1] == "created"));
    }

    [HttpPost("{id:guid}/test")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Webhooks, PermissionActions.Update)]
    public async Task<IActionResult> Test(Guid id, string? returnTo)
    {
        // SendTestAsync logs the attempt and writes the webhook.tested audit entry.
        var delivery = await _webhooks.SendTestAsync(id, HttpContext.RequestAborted);
        if (delivery is null)
        {
            return NotFound();
        }

        if (delivery.Outcome == WebhookOutcome.Success)
        {
            TempData["Success"] = $"Test delivered: {delivery.ResponseStatusCode} in {delivery.ResponseTimeMs} ms.";
        }
        else
        {
            TempData["Error"] = "Test failed: " + delivery.Detail;
        }

        return Back(id, returnTo);
    }

    [HttpGet("{id:guid}/deliveries")]
    public async Task<IActionResult> Deliveries(Guid id, int page = 1)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (webhook is null)
        {
            return NotFound();
        }

        var query = _db.WebhookDeliveries.AsNoTracking().Where(d => d.WebhookId == id);
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(d => d.RequestTimestamp).ThenByDescending(d => d.AttemptNumber)
            .Skip((Math.Max(page, 1) - 1) * LogPageSize).Take(LogPageSize).ToListAsync();
        ViewData["Page"] = Math.Max(page, 1);
        ViewData["Pages"] = Math.Max(1, (total + LogPageSize - 1) / LogPageSize);
        return View(new DeliveriesViewModel(webhook, rows, total, Can(PermissionAreas.Webhooks, PermissionActions.Update)));
    }

    private async Task<IActionResult> SetEnabledAsync(Guid id, bool enabled, string? returnTo)
    {
        // SetEnabledAsync writes webhook.enabled / webhook.disabled; disabling cancels pending retries.
        if (!await _webhooks.SetEnabledAsync(id, enabled, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        TempData["Success"] = enabled ? "Enabled: it fires again for its events." : "Disabled: it no longer fires, and pending retries are cancelled.";
        return Back(id, returnTo);
    }

    private IActionResult ShowSecret(Webhook webhook, string secret, bool created)
    {
        TempData[SecretKey] = $"{webhook.Id}|{(created ? "created" : "rotated")}|{secret}";
        return RedirectToAction(nameof(Secret), new { id = webhook.Id });
    }

    private IActionResult Back(Guid id, string? returnTo) => returnTo switch
    {
        "edit" => RedirectToAction(nameof(Edit), new { id }),
        "deliveries" => RedirectToAction(nameof(Deliveries), new { id }),
        _ => RedirectToAction(nameof(Index)),
    };

    private WebhookEditViewModel Editor(Webhook webhook, WebhookInput input) =>
        new(webhook.Id, input, webhook.CreatedDate, webhook.LastDelivery,
            Can(PermissionAreas.Webhooks, PermissionActions.Update), Can(PermissionAreas.Webhooks, PermissionActions.Delete));

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }
}
