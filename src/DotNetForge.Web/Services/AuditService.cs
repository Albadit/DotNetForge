using System.Security.Claims;
using DotNetForge.Api.Authentication;
using DotNetForge.Data;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Entities;

namespace DotNetForge.Web.Services;

/// <summary>
/// Writes append-only audit entries (.docs/features/audit-logging.md), capturing the acting user or API token, the
/// client IP and user agent from the current request. Display snapshots keep entries meaningful after a
/// user/entity is later deleted or renamed. Saved entries are then queued for extension handlers
/// (<see cref="AuditEventDispatcher"/>).
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly AuditEventDispatcher _dispatcher;

    public AuditService(DotNetForgeDbContext db, IHttpContextAccessor http, AuditEventDispatcher dispatcher)
    {
        _db = db;
        _http = http;
        _dispatcher = dispatcher;
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
            Timestamp = DateTime.UtcNow,
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        _dispatcher.Publish(new AuditEvent(entry.Action, entry.EntityType, entry.EntityId, entry.EntityDisplaySnapshot,
            entry.Details, entry.Success, entry.UserDisplaySnapshot, entry.IpAddress, entry.Timestamp));
    }

    /// <summary>
    /// Snapshot values can come from request input (the email typed into a failed login, the user agent); cap them
    /// at the column lengths so an oversized value can never fail the request on providers that enforce lengths.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
