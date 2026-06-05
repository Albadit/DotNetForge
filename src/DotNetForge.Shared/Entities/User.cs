using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A user account. Email is the login identifier and is unique within the tenant. The password is
/// stored only as a secure hash (security.md). Effective permissions are the union of all roles.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>Salted, adaptive password hash. Never returned by the API, never logged.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserStatus Status { get; set; } = UserStatus.Enabled;

    public bool EmailConfirmed { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginDate { get; set; }

    public Guid TenantId { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
            is { Length: > 0 } name
            ? name
            : Email;
}
