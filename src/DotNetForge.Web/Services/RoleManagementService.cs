using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Stores;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>What the Roles screen posts: name, description and the granted <c>PermissionCatalog.Key</c>s.</summary>
public sealed class RoleInput
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string> Grants { get; set; } = new();
}

/// <summary>
/// Creating, editing, duplicating and deleting roles and their grants (.docs/pages/roles.md). Rules:
/// <list type="bullet">
/// <item>name required (at most 100), unique; description at most 500; grants only from
/// <see cref="PermissionCatalog"/>;</item>
/// <item>built-in roles keep their name and can't be deleted; <c>Super Admin</c> (always everything) and <c>Public</c>
/// (site visitors) have fixed grants; only a Super Admin may change the <c>Super Admin</c> role;</item>
/// <item>nobody but a Super Admin changes the grants of a role they hold themselves - the safe way to avoid locking
/// yourself out;</item>
/// <item>a role still held by users can't be deleted.</item>
/// </list>
/// Changes are audited (<c>role.changed</c>, and <c>permission.changed</c> for grants) and take effect at once
/// (<see cref="IRoleGrantStore.Invalidate"/>).
/// </summary>
public sealed class RoleManagementService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IRoleGrantStore _grants;

    public RoleManagementService(DotNetForgeDbContext db, IAuditService audit, IRoleGrantStore grants)
    {
        _db = db;
        _audit = audit;
        _grants = grants;
    }

    /// <summary>Whether the grants of <paramref name="roleName"/> can be edited at all (not Super Admin or Public).</summary>
    public static bool HasEditableGrants(string roleName) =>
        !string.Equals(roleName, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(roleName, Roles.Public, StringComparison.OrdinalIgnoreCase);

    public async Task<(Guid? Id, string? Error)> CreateAsync(RoleInput input, ManagementActor actor, CancellationToken ct = default)
    {
        var name = input.Name?.Trim();
        var error = await ValidateDetailsAsync(name, input.Description, actor, excludeId: Guid.Empty, ct) ?? ValidateGrants(input.Grants);
        if (error is not null)
        {
            return (null, error);
        }

        var role = new Role { Name = name!, Description = Clean(input.Description), IsBuiltIn = false };
        AddGrants(role.Id, input.Grants);
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        _grants.Invalidate();
        await _audit.LogAsync(AuditActions.RoleChanged, "Role", role.Id.ToString(), role.Name, details: "Created", cancellationToken: ct);
        return (role.Id, null);
    }

    public async Task<string?> UpdateAsync(Guid id, RoleInput input, ManagementActor actor, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return "This role no longer exists.";
        }

        if (IsSuperAdminRole(role) && !actor.IsSuperAdmin)
        {
            return "Only a Super Admin can change the Super Admin role.";
        }

        var name = role.IsBuiltIn ? role.Name : input.Name?.Trim();
        var error = await ValidateDetailsAsync(name, input.Description, actor, role.Id, ct);
        if (error is not null)
        {
            return error;
        }

        var grantsChanged = false;
        if (HasEditableGrants(role.Name))
        {
            error = ValidateGrants(input.Grants);
            if (error is not null)
            {
                return error;
            }

            // Only catalog grants are compared and replaced; other stored rows (e.g. seeded Users|publish) stay as they are.
            var current = (await _db.RolePermissions.Where(p => p.RoleId == role.Id).ToListAsync(ct))
                .Where(p => PermissionCatalog.IsKnown(p.Area, p.Action))
                .ToList();
            var wanted = input.Grants.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var have = current.Select(p => PermissionCatalog.Key(p.Area, p.Action)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            grantsChanged = !have.SetEquals(wanted);
            if (grantsChanged)
            {
                if (!actor.IsSuperAdmin && await ActorHoldsAsync(actor, role.Id, ct))
                {
                    return "You can't change the permissions of a role you hold. Ask a Super Admin.";
                }

                _db.RolePermissions.RemoveRange(current.Where(p => !wanted.Contains(PermissionCatalog.Key(p.Area, p.Action))));
                AddGrants(role.Id, wanted.Where(k => !have.Contains(k)));
            }
        }

        var detailsChanged = role.Name != name || role.Description != Clean(input.Description);
        role.Name = name!;
        role.Description = Clean(input.Description);
        await _db.SaveChangesAsync(ct);
        _grants.Invalidate();

        if (detailsChanged)
        {
            await _audit.LogAsync(AuditActions.RoleChanged, "Role", role.Id.ToString(), role.Name, details: "Updated", cancellationToken: ct);
        }

        if (grantsChanged)
        {
            await _audit.LogAsync(AuditActions.PermissionChanged, "Role", role.Id.ToString(), role.Name, cancellationToken: ct);
        }

        return null;
    }

    public async Task<(Guid? Id, string? Error)> DuplicateAsync(Guid id, ManagementActor actor, CancellationToken ct = default)
    {
        var source = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (source is null)
        {
            return (null, "This role no longer exists.");
        }

        var names = (await _db.Roles.AsNoTracking().Select(r => r.Name).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = $"{source.Name} (copy)";
        for (var n = 2; names.Contains(name); n++)
        {
            name = $"{source.Name} (copy {n})";
        }

        if (name.Length > MaxNameLength)
        {
            return (null, "The copy's name would be too long. Create the role instead.");
        }

        var copy = new Role { Name = name, Description = source.Description, IsBuiltIn = false };
        if (IsSuperAdminRole(source))
        {
            // The original's grants are implicit; the copy gets every grant the catalog offers.
            AddGrants(copy.Id, PermissionCatalog.Areas.SelectMany(a => a.Actions.Select(action => PermissionCatalog.Key(a.Area, action))));
        }
        else
        {
            // Every stored row, so the copy behaves exactly like the original.
            var rows = await _db.RolePermissions.AsNoTracking().Where(p => p.RoleId == source.Id).Select(p => new { p.Area, p.Action }).ToListAsync(ct);
            foreach (var row in rows)
            {
                _db.RolePermissions.Add(new RolePermission { RoleId = copy.Id, Area = row.Area, Action = row.Action });
            }
        }

        _db.Roles.Add(copy);
        await _db.SaveChangesAsync(ct);
        _grants.Invalidate();
        await _audit.LogAsync(AuditActions.RoleChanged, "Role", copy.Id.ToString(), copy.Name, details: $"Duplicated from {source.Name}", cancellationToken: ct);
        return (copy.Id, null);
    }

    public async Task<string?> DeleteAsync(Guid id, ManagementActor actor, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return "This role no longer exists.";
        }

        if (role.IsBuiltIn)
        {
            return "Built-in roles can't be deleted.";
        }

        var holders = await _db.UserRoles.AsNoTracking().CountAsync(ur => ur.RoleId == id, ct);
        if (holders > 0)
        {
            return $"{holders} {(holders == 1 ? "user still has" : "users still have")} this role. Give them another role first.";
        }

        // Grants first (MongoDB has no cascading deletes).
        _db.RolePermissions.RemoveRange(await _db.RolePermissions.Where(p => p.RoleId == id).ToListAsync(ct));
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        _grants.Invalidate();
        await _audit.LogAsync(AuditActions.RoleChanged, "Role", role.Id.ToString(), role.Name, details: "Deleted", cancellationToken: ct);
        return null;
    }

    private async Task<string?> ValidateDetailsAsync(string? name, string? description, ManagementActor actor, Guid excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            return $"Enter a role name (at most {MaxNameLength} characters).";
        }

        if (Clean(description)?.Length > MaxDescriptionLength)
        {
            return $"The description is limited to {MaxDescriptionLength} characters.";
        }

        var names = await _db.Roles.AsNoTracking()
            .Where(r => r.Id != excludeId)
            .Select(r => r.Name)
            .ToListAsync(ct);
        return names.Contains(name, StringComparer.OrdinalIgnoreCase) ? $"A role named '{name}' already exists." : null;
    }

    private static string? ValidateGrants(IEnumerable<string> grants)
    {
        foreach (var grant in grants)
        {
            var parts = grant.Split('|');
            if (parts.Length != 2 || !PermissionCatalog.IsKnown(parts[0], parts[1]))
            {
                return $"'{grant}' is not a known permission.";
            }
        }

        return null;
    }

    private void AddGrants(Guid roleId, IEnumerable<string> grants)
    {
        foreach (var key in grants.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parts = key.Split('|');
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, Area = parts[0], Action = parts[1] });
        }
    }

    /// <summary>The catalog's exact spelling of a grant key, so stored rows match the constants the code checks.</summary>
    private static string Normalize(string key)
    {
        var parts = key.Split('|');
        var area = PermissionCatalog.Areas.First(a => string.Equals(a.Area, parts[0], StringComparison.OrdinalIgnoreCase));
        return PermissionCatalog.Key(area.Area, area.Actions.First(a => string.Equals(a, parts[1], StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<bool> ActorHoldsAsync(ManagementActor actor, Guid roleId, CancellationToken ct) =>
        await _db.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == actor.UserId && ur.RoleId == roleId, ct);

    private static bool IsSuperAdminRole(Role role) =>
        role.IsBuiltIn && string.Equals(role.Name, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
