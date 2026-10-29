using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A webhook subscription (.docs/features/webhooks.md). Fires a signed POST to <see cref="Url"/> for each
/// subscribed event. The signing <see cref="Secret"/> is system-generated, stored protected with Data Protection, and
/// shown only once - after creation and after each rotation.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute <c>https://</c> endpoint that receives the POST.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Custom headers attached to every request: a JSON array of <c>{ "key", "value" }</c> (an older object still reads).</summary>
    public string HeadersJson { get; set; } = "{}";

    /// <summary>Subscribed event keys, comma-separated (see <c>WebhookEvents</c>).</summary>
    public string EventsCsv { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>The signing secret, protected with Data Protection (<c>WebhookService</c> unprotects it to sign).</summary>
    public string Secret { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastDelivery { get; set; }

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();

    public IReadOnlyList<string> Events =>
        string.IsNullOrWhiteSpace(EventsCsv)
            ? Array.Empty<string>()
            : EventsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// One delivery attempt of an event to a webhook (.docs/features/webhooks.md#delivery). A <see cref="WebhookOutcome.Pending"/>
/// row is queued work, sent once <see cref="NextAttemptDate"/> has passed; a failed attempt adds the next one as a new
/// row, so every attempt stays in the delivery log.
/// </summary>
public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WebhookId { get; set; }

    /// <summary>The event this attempt delivers - the same for every retry, so receivers can deduplicate.</summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>When a pending attempt is due (also the lease while it is being sent); null once it is done.</summary>
    public DateTime? NextAttemptDate { get; set; }

    /// <summary>The exact JSON body sent (the signature is over these bytes).</summary>
    public string? RequestBody { get; set; }

    /// <summary>The start of the receiver's answer, for the delivery log.</summary>
    public string? ResponseBody { get; set; }

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
