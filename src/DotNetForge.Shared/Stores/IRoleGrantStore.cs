namespace DotNetForge.Shared.Stores;

/// <summary>
/// The stored permission grants of the roles (<c>RolePermission</c> rows), as read by authorization on every
/// check (.docs/features/authorization.md#how-permissions-are-evaluated). Implementations cache; callers that change
/// roles or grants call <see cref="Invalidate"/>.
/// </summary>
public interface IRoleGrantStore
{
    /// <summary>
    /// Role name (case-insensitive) → its grants as <c>PermissionCatalog.Key(area, action)</c> (case-insensitive). A role
    /// that does not exist is absent.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> GetGrants();

    /// <summary>Drops the cached grants; the next check reads the database.</summary>
    void Invalidate();
}
