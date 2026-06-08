using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A scoped API token (api_tokens.md). The plaintext secret is shown exactly once at creation;
/// only the salted <see cref="TokenHash"/> is persisted. <see cref="TokenPrefix"/> is a short,
/// non-secret identifier used in list views.
/// </summary>
public class ApiToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TokenDuration Duration { get; set; } = TokenDuration.ThirtyDays;

    /// <summary>Null only when <see cref="Duration"/> is <see cref="TokenDuration.Unlimited"/>.</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Granted permission keys, stored comma-separated (e.g. <c>content.read,media.read</c>).</summary>
    public string PermissionsCsv { get; set; } = string.Empty;

    public Guid CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedDate { get; set; }

    public bool Revoked { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string TokenPrefix { get; set; } = string.Empty;

    public IReadOnlyList<string> Permissions =>
        string.IsNullOrWhiteSpace(PermissionsCsv)
            ? Array.Empty<string>()
            : PermissionsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsExpired(DateTime utcNow) => ExpirationDate is { } exp && exp <= utcNow;
}
