namespace DotNetForge.Shared.Auditing;

/// <summary>
/// Appends audit entries for the current request (.docs/features/audit-logging.md). The acting principal (user or
/// API token), tenant, IP address and user agent are captured by the implementation; callers pass a constant from
/// <c>AuditActions</c> and never secrets.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        string? entityDisplay = null,
        string? details = null,
        bool success = true,
        CancellationToken cancellationToken = default);
}
