using DotNetForge.Abstractions.Security;
using DotNetForge.Core.Validation;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>What the Users screen posts when a user is created or edited.</summary>
public sealed class UserInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }

    /// <summary>Required when creating; when editing, empty keeps the current password.</summary>
    public string? Password { get; set; }

    public List<Guid> RoleIds { get; set; } = new();

    // No status here: a new user is enabled, and SetStatusAsync (Disable / Enable user) is the only way to change it.
}

/// <summary>Who performs a change: their user id and whether they hold <c>Super Admin</c>.</summary>
public sealed record ManagementActor(Guid UserId, bool IsSuperAdmin);

/// <summary>
/// Creating, editing, enabling/disabling and deleting admin users (.docs/pages/users.md). The rules live here, not in
/// the controller:
/// <list type="bullet">
/// <item>email valid and unique (case-insensitive); password per <see cref="PasswordPolicy"/>, stored only
/// as a hash; at least one existing role;</item>
/// <item>only a Super Admin may assign the <c>Super Admin</c> role or change a user who holds it;</item>
/// <item>there is always at least one enabled Super Admin;</item>
/// <item>nobody can disable or delete their own account.</item>
/// </list>
/// Every change is audited (<c>user.created/updated/deleted</c>, plus <c>role.changed</c> when roles change). Role
/// changes reach a signed-in user at their next sign-in; disabling ends their session within 30 seconds.
/// </summary>
public sealed class UserManagementService
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 256;

    private readonly DotNetForgeDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;

    public UserManagementService(DotNetForgeDbContext db, IPasswordHasher hasher, IAuditService audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    public async Task<(Guid? Id, string? Error)> CreateAsync(UserInput input, ManagementActor actor, CancellationToken ct = default)
    {
        var error = await ValidateAsync(input, actor, existing: null, ct) ?? PasswordPolicy.Validate(input.Password);
        if (error is not null)
        {
            return (null, error);
        }

        var user = new User
        {
            FirstName = Clean(input.FirstName),
            LastName = Clean(input.LastName),
            Email = input.Email!.Trim(),
            PasswordHash = _hasher.Hash(input.Password!),
            // A new user is always enabled; they are disabled later from the edit screen.
            Status = UserStatus.Enabled,
            // An administrator created it: no email confirmation needed (.docs/features/authentication.md).
            EmailConfirmed = true,
            CreatedDate = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        foreach (var roleId in input.RoleIds.Distinct())
        {
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserCreated, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
        return (user.Id, null);
    }

    public async Task<string?> UpdateAsync(Guid id, UserInput input, ManagementActor actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return "This user no longer exists.";
        }

        var error = await ValidateAsync(input, actor, user, ct);
        if (error is null && !string.IsNullOrEmpty(input.Password))
        {
            error = PasswordPolicy.Validate(input.Password);
        }

        if (error is not null)
        {
            return error;
        }

        user.FirstName = Clean(input.FirstName);
        user.LastName = Clean(input.LastName);
        user.Email = input.Email!.Trim();
        if (!string.IsNullOrEmpty(input.Password))
        {
            user.PasswordHash = _hasher.Hash(input.Password);
        }

        var current = await _db.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync(ct);
        var wanted = input.RoleIds.ToHashSet();
        var rolesChanged = !current.Select(ur => ur.RoleId).ToHashSet().SetEquals(wanted);
        _db.UserRoles.RemoveRange(current.Where(ur => !wanted.Contains(ur.RoleId)));
        foreach (var roleId in wanted.Where(r => current.All(ur => ur.RoleId != r)))
        {
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserUpdated, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
        if (rolesChanged)
        {
            await _audit.LogAsync(AuditActions.RoleChanged, "User", user.Id.ToString(), user.Email,
                details: "Role assignment changed", cancellationToken: ct);
        }

        return null;
    }

    public async Task<string?> SetStatusAsync(Guid id, bool enabled, ManagementActor actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return "This user no longer exists.";
        }

        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == id).Select(ur => ur.RoleId).ToListAsync(ct);
        var error = user.Id == actor.UserId && !enabled
            ? "You can't disable your own account."
            : await GuardSuperAdminAsync(user, roleIds, enabled, actor, ct);
        if (error is not null)
        {
            return error;
        }

        user.Status = enabled ? UserStatus.Enabled : UserStatus.Disabled;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserUpdated, "User", user.Id.ToString(), user.Email,
            details: enabled ? "Enabled" : "Disabled", cancellationToken: ct);
        return null;
    }

    public async Task<string?> DeleteAsync(Guid id, ManagementActor actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return "This user no longer exists.";
        }

        if (user.Id == actor.UserId)
        {
            return "You can't delete your own account.";
        }

        // Deleting is checked like disabling: the user stops counting as an enabled Super Admin.
        var error = await GuardSuperAdminAsync(user, Array.Empty<Guid>(), enabled: false, actor, ct, deleting: true);
        if (error is not null)
        {
            return error;
        }

        // Role links and page entries first (MongoDB has no cascading deletes); audit entries keep the user's name snapshot.
        _db.UserRoles.RemoveRange(await _db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(ct));
        _db.PageUserPermissions.RemoveRange(await _db.PageUserPermissions.Where(p => p.UserId == id).ToListAsync(ct));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserDeleted, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
        return null;
    }

    private async Task<string?> ValidateAsync(UserInput input, ManagementActor actor, User? existing, CancellationToken ct)
    {
        var email = input.Email?.Trim();
        if (!EmailValidator.IsValid(email) || email!.Length > MaxEmailLength)
        {
            return $"Enter a valid email address (at most {MaxEmailLength} characters).";
        }

        if (Clean(input.FirstName)?.Length > MaxNameLength || Clean(input.LastName)?.Length > MaxNameLength)
        {
            return $"First and last name are limited to {MaxNameLength} characters each.";
        }

        // Case-insensitive, compared in memory so the check is the same on every database provider.
        var excludeId = existing?.Id ?? Guid.Empty;
        var emails = await _db.Users.AsNoTracking()
            .Where(u => u.Id != excludeId)
            .Select(u => u.Email)
            .ToListAsync(ct);
        if (emails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return $"Another user already uses {email}.";
        }

        var roleIds = input.RoleIds.Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return "Choose at least one role.";
        }

        var chosen = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(ct);
        if (chosen.Count != roleIds.Count)
        {
            return "One of the chosen roles no longer exists. Reload the page and try again.";
        }

        if (!actor.IsSuperAdmin && chosen.Contains(Roles.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return "Only a Super Admin can give the Super Admin role.";
        }

        return existing is null ? null : await GuardSuperAdminAsync(existing, roleIds, existing.Status == UserStatus.Enabled, actor, ct);
    }

    /// <summary>
    /// Changes to a Super Admin need a Super Admin, and at least one enabled Super Admin must remain after
    /// <paramref name="user"/> ends up with <paramref name="roleIds"/> and <paramref name="enabled"/>.
    /// </summary>
    private async Task<string?> GuardSuperAdminAsync(User user, IReadOnlyCollection<Guid> roleIds, bool enabled,
        ManagementActor actor, CancellationToken ct, bool deleting = false)
    {
        var superAdminRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == Roles.SuperAdmin)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (superAdminRoleId is not { } saRole)
        {
            return null;
        }

        var holders = await _db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == saRole).Select(ur => ur.UserId).ToListAsync(ct);
        var isSuperAdmin = holders.Contains(user.Id);
        if (isSuperAdmin && !actor.IsSuperAdmin)
        {
            return "Only a Super Admin can change or delete a Super Admin.";
        }

        var staysSuperAdmin = !deleting && enabled && (roleIds.Count == 0 ? isSuperAdmin : roleIds.Contains(saRole));
        if (!isSuperAdmin || staysSuperAdmin)
        {
            return null;
        }

        var otherEnabled = await _db.Users.AsNoTracking()
            .Where(u => u.Id != user.Id && holders.Contains(u.Id) && u.Status == UserStatus.Enabled)
            .AnyAsync(ct);
        return otherEnabled ? null : "This is the last enabled Super Admin. Give another user the Super Admin role first.";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
