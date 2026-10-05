using System.Security.Claims;
using DotNetForge.Api.Authentication;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Entities;

namespace DotNetForge.Web.Services;

/// <summary>
/// Writes append-only audit entries (.docs/features/audit-logging.md), capturing the acting user or API token, the
/// tenant, client IP and user agent from the current request. Display snapshots keep entries meaningful after a
/// user/entity is later deleted or renamed.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(DotNetForgeDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        string? entityDisplay = null,
        string? details = null,
        bool success = true,
        CancellationToken cancellationToken = default)
    {
        var ctx = _http.HttpContext;
        var user = ctx?.User;

        // An API token's NameIdentifier is the token id, not a user id: record the token as the actor instead.
        var tokenId = user?.FindFirst(ApiTokenDefaults.TokenIdClaimType)?.Value;
        Guid? userId = tokenId is null && Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
        Guid? tenantId = Guid.TryParse(user?.FindFirst(AuthService.TenantClaimType)?.Value, out var t) ? t : null;
        var userAgent = ctx?.Request.Headers.UserAgent.ToString();

        var entry = new AuditLogEntry
        {
            UserId = userId,
            UserDisplaySnapshot = Truncate(tokenId is not null
                ? $"API token {tokenId}"
                : user?.Identity?.Name ?? user?.FindFirst(ClaimTypes.Email)?.Value, 256),
            Action = action,
            EntityType = entityType,
            EntityId = Truncate(entityId, 100),
            EntityDisplaySnapshot = Truncate(entityDisplay, 400),
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = string.IsNullOrEmpty(userAgent) ? "unknown" : Truncate(userAgent, 512)!,
            Details = details,
            Success = success,
            TenantId = tenantId,
            Timestamp = DateTime.UtcNow,
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Snapshot values can come from request input (the email typed into a failed login, the user agent); cap them
    /// at the column lengths so an oversized value can never fail the request on providers that enforce lengths.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
