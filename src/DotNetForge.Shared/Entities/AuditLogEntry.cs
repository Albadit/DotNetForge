namespace DotNetForge.Shared.Entities;

/// <summary>
/// An append-only audit record (audit_logs.md). Logs are never edited or deleted. A monotonic
/// <see cref="Id"/> breaks timestamp ties, and display snapshots keep entries interpretable after
/// the referenced user/entity is deleted or renamed. Sensitive data must be redacted from
/// <see cref="Details"/> before persistence.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    public Guid? UserId { get; set; }

    public string? UserDisplaySnapshot { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? EntityDisplaySnapshot { get; set; }

    public string IpAddress { get; set; } = "unknown";

    public string UserAgent { get; set; } = "unknown";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Structured JSON context (changed fields, reason, etc.), redacted of secrets.</summary>
    public string? Details { get; set; }

    public bool Success { get; set; } = true;

    public Guid? TenantId { get; set; }
}
