using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A webhook subscription (.docs/features/webhooks.md). Fires a signed POST to <see cref="Url"/> for each
/// subscribed event. The signing <see cref="Secret"/> is system-managed and never displayed
/// after creation/rotation.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute <c>https://</c> endpoint that receives the POST.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Custom headers attached to every request, stored as a JSON object.</summary>
    public string HeadersJson { get; set; } = "{}";

    /// <summary>Subscribed event keys, comma-separated (see <c>WebhookEvents</c>).</summary>
    public string EventsCsv { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Secret { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastDelivery { get; set; }

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();

    public IReadOnlyList<string> Events =>
        string.IsNullOrWhiteSpace(EventsCsv)
            ? Array.Empty<string>()
            : EventsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>A single webhook delivery attempt, logged for troubleshooting (.docs/features/webhooks.md).</summary>
public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WebhookId { get; set; }

    public Webhook? Webhook { get; set; }

    public string TargetUrl { get; set; } = string.Empty;

    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

    public string Event { get; set; } = string.Empty;

    public int? ResponseStatusCode { get; set; }

    public long? ResponseTimeMs { get; set; }

    public int AttemptNumber { get; set; } = 1;

    public WebhookOutcome Outcome { get; set; } = WebhookOutcome.Pending;

    public string? Detail { get; set; }
}
