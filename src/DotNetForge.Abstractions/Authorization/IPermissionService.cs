namespace DotNetForge.Abstractions.Authorization;

/// <summary>
/// RBAC check used on every admin and API action. <see cref="Has"/> returns true only when the
/// role is granted the action in that permission area (least-privilege). Effective permissions of
/// a user are the union over all assigned roles (.docs/features/authorization.md).
/// </summary>
public interface IPermissionService
{
    /// <summary>Whether <paramref name="role"/> grants <paramref name="action"/> in <paramref name="area"/>.</summary>
    bool Has(string role, string area, string action);

    /// <summary>Whether any of the supplied roles grants the action (union of roles).</summary>
    bool HasAny(IEnumerable<string> roles, string area, string action);
}
