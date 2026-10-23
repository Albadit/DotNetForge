namespace DotNetForge.Shared.Stores;

/// <summary>
/// The stored permission grants of a tenant's roles (<c>RolePermission</c> rows), as read by authorization on every
/// check (.docs/features/authorization.md#how-permissions-are-evaluated). Implementations cache; callers that change
/// roles or grants call <see cref="Invalidate"/>.
/// </summary>
public interface IRoleGrantStore
{
    /// <summary>
    /// Role name (case-insensitive) → its grants as <c>PermissionCatalog.Key(area, action)</c> (case-insensitive) for
    /// <paramref name="tenantId"/>. A role that does not exist in the tenant is absent.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> GetGrants(Guid tenantId);

    /// <summary>Drops the cached grants of <paramref name="tenantId"/>; the next check reads the database.</summary>
    void Invalidate(Guid tenantId);
}
