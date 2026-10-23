namespace DotNetForge.Abstractions.Authorization;

/// <summary>
/// RBAC check used on every admin and API action. A grant is an <c>(area, action)</c> pair; effective permissions of
/// a user are the union over all assigned roles (.docs/features/authorization.md).
/// </summary>
public interface IPermissionService
{
    /// <summary>Whether the built-in <paramref name="role"/> grants the action by default (the reference matrix).</summary>
    bool Has(string role, string area, string action);

    /// <summary>Whether any of the supplied roles grants the action by default (union of the reference matrix).</summary>
    bool HasAny(IEnumerable<string> roles, string area, string action);

    /// <summary>
    /// Whether any of the supplied roles of <paramref name="tenantId"/> grants the action, evaluated from the roles'
    /// stored grants - what custom roles and edited built-in roles need. <c>Super Admin</c> always has every grant.
    /// </summary>
    bool HasAny(Guid tenantId, IEnumerable<string> roles, string area, string action);
}
