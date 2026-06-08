using System.Security.Claims;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Http;

namespace DotNetForge.Web.Services;

/// <summary>
/// Writes append-only audit entries (audit_logs.md), capturing the acting user, client IP, and user
/// agent from the current request. Display snapshots keep entries meaningful after a user/entity is
/// later deleted or renamed.
/// </summary>
public sealed class AuditService
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

        Guid? userId = Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        Guid? tenantId = Guid.TryParse(user?.FindFirst(AuthService.TenantClaimType)?.Value, out var t) ? t : null;

        var entry = new AuditLogEntry
        {
            UserId = userId,
            UserDisplaySnapshot = user?.Identity?.Name ?? user?.FindFirst(ClaimTypes.Email)?.Value,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityDisplaySnapshot = entityDisplay,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = ctx?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : "unknown",
            Details = details,
            Success = success,
            TenantId = tenantId,
            Timestamp = DateTime.UtcNow,
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
