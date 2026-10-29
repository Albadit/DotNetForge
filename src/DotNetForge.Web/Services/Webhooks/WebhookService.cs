using System.Security.Cryptography;
using System.Text.Json;
using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services.Webhooks;

/// <summary>One custom header of a webhook (the editor's rows).</summary>
public sealed class WebhookHeader
{
    public string? Key { get; set; }
    public string? Value { get; set; }
}

/// <summary>What the webhook editor posts.</summary>
public sealed class WebhookInput
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Events { get; set; } = new();
    public List<WebhookHeader> Headers { get; set; } = new();
}

/// <summary>
/// Webhooks (.docs/features/webhooks.md): creating, editing, enabling, disabling and deleting them, their signing
/// secrets (generated here, stored protected, shown once) and test deliveries. Every change writes a
/// <c>webhook.*</c> audit entry. Delivering events is <see cref="WebhookDispatcher"/>'s job.
/// </summary>
public sealed partial class WebhookService
{
    public const int MaxNameLength = 200;
    public const int MaxUrlLength = 2000;
    public const int MaxHeaders = 20;

    /// <summary>The stored headers (<c>Webhook.HeadersJson</c>, max 4000 characters) must fit.</summary>
    public const int MaxHeadersLength = 4000;

    /// <summary>Headers the CMS sets itself, or that HTTP owns; refused as custom headers.</summary>
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Expect", "User-Agent",
    };

    private const string SecretPurpose = "DotNetForge.Webhooks.Secret.v1";

    private readonly DotNetForgeDbContext _db;
    private readonly IWebhookSigner _signer;
    private readonly IDataProtector _secrets;
    private readonly IAuditService _audit;
    private readonly WebhookSender _sender;
    private readonly IHostEnvironment _environment;

    public WebhookService(DotNetForgeDbContext db, IWebhookSigner signer, IDataProtectionProvider dataProtection, IAuditService audit,
        WebhookSender sender, IHostEnvironment environment)
    {
        _db = db;
        _signer = signer;
        _secrets = dataProtection.CreateProtector(SecretPurpose);
        _audit = audit;
        _sender = sender;
        _environment = environment;
    }

    public static IReadOnlyList<WebhookHeader> ReadHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<WebhookHeader>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(h => new WebhookHeader
                {
                    Key = h.TryGetProperty("key", out var k) ? k.GetString() : null,
                    Value = h.TryGetProperty("value", out var v) ? v.GetString() : null,
                }).ToList()
                // An object (the original format): one header per property.
                : doc.RootElement.EnumerateObject().Select(p => new WebhookHeader { Key = p.Name, Value = p.Value.GetString() }).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<WebhookHeader>();
        }
    }

    /// <summary>The webhook's secret in clear, for signing; null when it can't be read (saved on another site).</summary>
    public string? Secret(Webhook webhook) => UnprotectSecret(_secrets, webhook.Secret);

    internal static string? UnprotectSecret(IDataProtector protector, string stored)
    {
        try
        {
            return protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The protector <see cref="WebhookDispatcher"/> uses (it has no scope of its own for this service).</summary>
    public static IDataProtector SecretProtector(IDataProtectionProvider provider) => provider.CreateProtector(SecretPurpose);

    /// <summary>Validates and creates; returns the webhook and its new secret (shown once), or the errors.</summary>
    public async Task<(Webhook? Webhook, string? Secret, List<string> Errors)> CreateAsync(WebhookInput input, CancellationToken ct = default)
    {
        var errors = await ValidateAsync(input, null, ct);
        if (errors.Count > 0)
        {
            return (null, null, errors);
        }

        var secret = _signer.GenerateSecret();
        var webhook = new Webhook { Secret = _secrets.Protect(secret), CreatedDate = DateTime.UtcNow };
        Apply(webhook, input);
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.WebhookCreated, nameof(Webhook), webhook.Id.ToString(), webhook.Name,
            details: $"{webhook.Url}; events: {webhook.EventsCsv}", cancellationToken: ct);
        return (webhook, secret, errors);
    }

    public async Task<List<string>> UpdateAsync(Guid id, WebhookInput input, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return new List<string> { "The webhook no longer exists." };
        }

        var errors = await ValidateAsync(input, id, ct);
        if (errors.Count > 0)
        {
            return errors;
        }

        var wasEnabled = webhook.Enabled;
        Apply(webhook, input);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.WebhookUpdated, nameof(Webhook), webhook.Id.ToString(), webhook.Name,
            details: $"{webhook.Url}; events: {webhook.EventsCsv}", cancellationToken: ct);
        if (wasEnabled != webhook.Enabled)
        {
            await StatusChangedAsync(webhook, ct);
        }

        return errors;
    }

    /// <summary>Turns a webhook on or off; off cancels its pending retries.</summary>
    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return false;
        }

        if (webhook.Enabled != enabled)
        {
            webhook.Enabled = enabled;
            await _db.SaveChangesAsync(ct);
            await StatusChangedAsync(webhook, ct);
        }

        return true;
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return null;
        }

        _db.Webhooks.Remove(webhook); // the delivery log goes with it (cascade)
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.WebhookDeleted, nameof(Webhook), webhook.Id.ToString(), webhook.Name, cancellationToken: ct);
        return webhook.Name;
    }

    /// <summary>A new secret (returned once); deliveries from now on are signed with it.</summary>
    public async Task<string?> RotateSecretAsync(Guid id, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return null;
        }

        var secret = _signer.GenerateSecret();
        webhook.Secret = _secrets.Protect(secret);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.WebhookSecretRotated, nameof(Webhook), webhook.Id.ToString(), webhook.Name, cancellationToken: ct);
        return secret;
    }

    /// <summary>
    /// Sends a signed sample event now (not retried, real subscriptions untouched) and logs it in the delivery log
    /// as event <c>webhook.test</c>; returns the logged attempt.
    /// </summary>
    public async Task<WebhookDelivery?> SendTestAsync(Guid id, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null)
        {
            return null;
        }

        var delivery = new WebhookDelivery
        {
            WebhookId = webhook.Id,
            Event = WebhookPayload.TestEvent,
            TargetUrl = webhook.Url,
        };
        delivery.RequestBody = WebhookPayload.Test(delivery.EventId, webhook);
        await _sender.SendAsync(webhook, Secret(webhook), delivery, ct);
        webhook.LastDelivery = delivery.RequestTimestamp;
        _db.WebhookDeliveries.Add(delivery);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.WebhookTested, nameof(Webhook), webhook.Id.ToString(), webhook.Name,
            details: delivery.Outcome == WebhookOutcome.Success ? $"Succeeded ({delivery.ResponseStatusCode})" : "Failed: " + delivery.Detail,
            success: delivery.Outcome == WebhookOutcome.Success, cancellationToken: ct);
        return delivery;
    }

    private async Task StatusChangedAsync(Webhook webhook, CancellationToken ct)
    {
        if (!webhook.Enabled)
        {
            // Disabled webhooks neither fire nor retry: what is still queued is dropped, not kept for later.
            await _db.WebhookDeliveries
                .Where(d => d.WebhookId == webhook.Id && d.Outcome == WebhookOutcome.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Outcome, WebhookOutcome.Failure)
                    .SetProperty(d => d.RequestTimestamp, DateTime.UtcNow)
                    .SetProperty(d => d.NextAttemptDate, (DateTime?)null)
                    .SetProperty(d => d.Detail, "Cancelled: the webhook was disabled."), ct);
        }

        await _audit.LogAsync(webhook.Enabled ? AuditActions.WebhookEnabled : AuditActions.WebhookDisabled, nameof(Webhook),
            webhook.Id.ToString(), webhook.Name, cancellationToken: ct);
    }

    private static void Apply(Webhook webhook, WebhookInput input)
    {
        webhook.Name = input.Name!.Trim();
        webhook.Url = input.Url!.Trim();
        webhook.Enabled = input.Enabled;
        webhook.EventsCsv = string.Join(',', WebhookEvents.All.Where(input.Events.Contains));
        webhook.HeadersJson = JsonSerializer.Serialize(Rows(input)
            .Select(h => new { key = h.Key!.Trim(), value = h.Value ?? string.Empty }));
    }

    /// <summary>The header rows the user filled in (completely empty rows are ignored).</summary>
    private static IEnumerable<WebhookHeader> Rows(WebhookInput input) =>
        input.Headers.Where(h => !string.IsNullOrWhiteSpace(h.Key) || !string.IsNullOrEmpty(h.Value));

    private async Task<List<string>> ValidateAsync(WebhookInput input, Guid? id, CancellationToken ct)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add("Enter a name.");
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add($"The name can have at most {MaxNameLength} characters.");
        }
        else
        {
            var lower = name.ToLowerInvariant();
            if (await _db.Webhooks.AnyAsync(w => w.Name.ToLower() == lower && w.Id != id, ct))
            {
                errors.Add($"Another webhook is already called '{name}'.");
            }
        }

        var url = input.Url?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || url.Length > MaxUrlLength || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add("Enter the full address of the receiver, e.g. https://example.com/hooks/cms.");
        }
        else if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && _environment.IsDevelopment()))
        {
            errors.Add("The URL must start with https:// (plain http only for localhost, in Development).");
        }

        if (input.Events.Count == 0 || !input.Events.All(WebhookEvents.IsValid))
        {
            errors.Add("Choose at least one event.");
        }

        var rows = Rows(input).ToList();
        if (rows.Count > MaxHeaders)
        {
            errors.Add($"At most {MaxHeaders} headers.");
        }

        if (rows.Any(h => string.IsNullOrWhiteSpace(h.Key)))
        {
            errors.Add("Every header needs a name (its value may be empty).");
        }

        foreach (var key in rows.Select(h => h.Key?.Trim()).OfType<string>().Where(k => k.Length > 0))
        {
            if (!HeaderNamePattern().IsMatch(key))
            {
                errors.Add($"'{key}' is not a valid header name (letters, digits and - only).");
            }
            else if (ReservedHeaders.Contains(key) || key.StartsWith("X-DotNetForge-", StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"'{key}' is set by the CMS and can't be a custom header.");
            }
        }

        if (rows.Any(h => h.Value?.IndexOfAny(new[] { '\r', '\n' }) >= 0))
        {
            errors.Add("Header values can't contain line breaks.");
        }

        if (rows.Select(h => h.Key?.Trim()).Where(k => !string.IsNullOrEmpty(k)).GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            errors.Add("Each header name can be used once.");
        }

        if (JsonSerializer.Serialize(rows.Select(h => new { key = h.Key?.Trim(), value = h.Value ?? string.Empty })).Length > MaxHeadersLength)
        {
            errors.Add($"The headers are too long together (at most about {MaxHeadersLength} characters).");
        }

        return errors;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9-]{1,100}$")]
    private static partial System.Text.RegularExpressions.Regex HeaderNamePattern();
}
