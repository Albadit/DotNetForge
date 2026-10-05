namespace DotNetForge.Shared.Entities;

/// <summary>
/// An RBAC role. Built-in roles cannot be deleted or renamed; Super Admin always has full access
/// and Public is always read-only public content (.docs/features/authorization.md).
/// </summary>
public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsBuiltIn { get; set; }

    public Guid TenantId { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

/// <summary>A single (area, action) grant attached to a role.</summary>
public class RolePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public string Area { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
}

/// <summary>Join entity assigning a user to a role.</summary>
public class UserRole
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }
}
