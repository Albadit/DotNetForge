using DotNetForge.Abstractions.Authorization;
using DotNetForge.Shared.Authorization;

namespace DotNetForge.Core.Authorization;

/// <summary>
/// Default <see cref="IPermissionService"/> backed by the canonical <see cref="PermissionMatrix"/>.
/// A user's effective permissions are the union over all assigned roles (most-permissive wins).
/// </summary>
public sealed class PermissionService : IPermissionService
{
    public bool Has(string role, string area, string action)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        return PermissionMatrix.IsGranted(role, area, action);
    }

    public bool HasAny(IEnumerable<string> roles, string area, string action)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(role => Has(role, area, action));
    }
}
