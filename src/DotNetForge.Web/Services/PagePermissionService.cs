using System.Security.Claims;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>A user's entry on a page: per permission <c>true</c> = allow, <c>false</c> = block, absent = not set.</summary>
public sealed record PageUserGrant(Guid UserId, bool? View, bool? Edit);

/// <summary>The grants of one page: roles (allow only) and users (allow or block).</summary>
public sealed record PageGrants(IReadOnlySet<Guid> View, IReadOnlySet<Guid> Edit, IReadOnlyList<PageUserGrant> Users)
{
    public static readonly PageGrants None = new(new HashSet<Guid>(), new HashSet<Guid>(), Array.Empty<PageUserGrant>());

    /// <summary>A page is restricted when anyone is explicitly allowed to view it (role or user).</summary>
    public bool Restricted => View.Count > 0 || Users.Any(u => u.View == true);
}

/// <summary>
/// Per-page permissions (.docs/features/content-pages-and-routing.md#page-permissions):
/// <list type="bullet">
/// <item><b>Roles</b> (<see cref="PagePermission"/>): <c>view</c> - when a page has view grants (of roles or users),
/// only those may see it; <c>Public</c> = everyone, <c>Authenticated</c> = any signed-in user. <c>edit</c> - the roles may
/// edit this page in addition to the <c>Collection types</c> grants.</item>
/// <item><b>Users</b> (<see cref="PageUserPermission"/>): <i>allow</i> view or edit for one user, or <i>block</i> it.
/// A block wins over everything the user's roles grant, including <c>Collection types</c> rights - only a Super Admin
/// can't be blocked, and nobody can block themselves.</item>
/// </list>
/// Super Admins and (unless blocked) users who may edit the page always see it. Restricted pages are left out of menus
/// and sitemaps. Changing grants needs <c>Collection types</c> / <c>update</c> (checked by the controller) and is
/// audited as <c>permission.changed</c>.
/// </summary>
public sealed class PagePermissionService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;

    public PagePermissionService(DotNetForgeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PageGrants> GetAsync(Guid pageId, CancellationToken ct = default)
    {
        var roles = await _db.PagePermissions.AsNoTracking()
            .Where(p => p.PageId == pageId)
            .Select(p => new { p.RoleId, p.Permission })
            .ToListAsync(ct);
        var users = await _db.PageUserPermissions.AsNoTracking()
            .Where(p => p.PageId == pageId)
            .Select(p => new { p.UserId, p.Permission, p.Allow })
            .ToListAsync(ct);
        return new PageGrants(
            roles.Where(r => r.Permission == PagePermission.View).Select(r => r.RoleId).ToHashSet(),
            roles.Where(r => r.Permission == PagePermission.Edit).Select(r => r.RoleId).ToHashSet(),
            users.GroupBy(u => u.UserId)
                .Select(g => new PageUserGrant(g.Key,
                    g.FirstOrDefault(u => u.Permission == PagePermission.View)?.Allow,
                    g.FirstOrDefault(u => u.Permission == PagePermission.Edit)?.Allow))
                .ToList());
    }

    /// <summary>
    /// Replaces the page's grants; null when saved, else the error. <paramref name="users"/> null leaves the user
    /// entries as they are; an entry with neither view nor edit set is removed.
    /// </summary>
    public async Task<string?> SaveAsync(Page page, IEnumerable<Guid> viewRoles, IEnumerable<Guid> editRoles,
        IEnumerable<PageUserGrant>? users, Guid? actorId, CancellationToken ct = default)
    {
        var view = viewRoles.ToHashSet();
        var edit = editRoles.ToHashSet();
        var tenantRoles = await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == page.TenantId)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);
        var known = tenantRoles.Select(r => r.Id).ToHashSet();
        if (!view.All(known.Contains) || !edit.All(known.Contains))
        {
            return "One of the chosen roles no longer exists. Reload the page and try again.";
        }

        // Super Admin always has full access: a grant to it would mean nothing (and a view grant would restrict the page).
        var superAdmin = tenantRoles.Where(r => r.Name == Roles.SuperAdmin).Select(r => r.Id).ToList();
        view.ExceptWith(superAdmin);
        edit.ExceptWith(superAdmin);

        // Editing in the admin needs an admin role; site-only roles can only be given view access.
        var siteOnly = tenantRoles.Where(r => Roles.SiteOnly.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).Select(r => r.Id);
        if (edit.Overlaps(siteOnly))
        {
            return "Public and Authenticated can't edit pages; choose admin roles for editing.";
        }

        var userList = users?.Where(u => u.View is not null || u.Edit is not null)
            .GroupBy(u => u.UserId).Select(g => g.Last()).ToList();
        string? userError = userList is null ? null : await ValidateUsersAsync(page.TenantId, userList, actorId, tenantRoles.ToDictionary(r => r.Id, r => r.Name), ct);
        if (userError is not null)
        {
            return userError;
        }

        var changed = await ReplaceRolesAsync(page, view, edit, ct);
        if (userList is not null)
        {
            changed |= await ReplaceUsersAsync(page, userList, ct);
        }

        if (!changed)
        {
            return null;
        }

        await _db.SaveChangesAsync(ct);
        var current = await GetAsync(page.Id, ct);
        await _audit.LogAsync(AuditActions.PermissionChanged, "Page", page.Id.ToString(), page.Title,
            details: await DescribeAsync(current, tenantRoles.ToDictionary(r => r.Id, r => r.Name), ct), cancellationToken: ct);
        return null;
    }

    private async Task<string?> ValidateUsersAsync(Guid tenantId, IReadOnlyList<PageUserGrant> users, Guid? actorId,
        IReadOnlyDictionary<Guid, string> roleNames, CancellationToken ct)
    {
        var ids = users.Select(u => u.UserId).ToList();
        var found = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        if (found.Count != ids.Count)
        {
            return "One of the chosen users no longer exists. Reload the page and try again.";
        }

        var blocked = users.Where(u => u.View == false || u.Edit == false).Select(u => u.UserId).ToList();
        if (actorId is { } self && blocked.Contains(self))
        {
            return "You can't block your own access to a page.";
        }

        if (blocked.Count > 0 && roleNames.FirstOrDefault(r => r.Value == Roles.SuperAdmin).Key is { } superAdminRole && superAdminRole != Guid.Empty)
        {
            var superAdmins = await _db.UserRoles.AsNoTracking()
                .Where(ur => ur.RoleId == superAdminRole && blocked.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync(ct);
            if (superAdmins.Count > 0)
            {
                return $"{found.First(u => u.Id == superAdmins[0]).Email} is a Super Admin and always has access; remove the block.";
            }
        }

        return null;
    }

    private async Task<bool> ReplaceRolesAsync(Page page, HashSet<Guid> view, HashSet<Guid> edit, CancellationToken ct)
    {
        var current = await _db.PagePermissions.Where(p => p.PageId == page.Id).ToListAsync(ct);
        var wanted = view.Select(r => (r, PagePermission.View)).Concat(edit.Select(r => (r, PagePermission.Edit))).ToHashSet();
        var have = current.Select(p => (p.RoleId, p.Permission)).ToHashSet();
        if (have.SetEquals(wanted))
        {
            return false;
        }

        _db.PagePermissions.RemoveRange(current.Where(p => !wanted.Contains((p.RoleId, p.Permission))));
        foreach (var (roleId, permission) in wanted.Where(w => !have.Contains(w)))
        {
            _db.PagePermissions.Add(new PagePermission { TenantId = page.TenantId, PageId = page.Id, RoleId = roleId, Permission = permission });
        }

        return true;
    }

    private async Task<bool> ReplaceUsersAsync(Page page, IReadOnlyList<PageUserGrant> users, CancellationToken ct)
    {
        var current = await _db.PageUserPermissions.Where(p => p.PageId == page.Id).ToListAsync(ct);
        var wanted = users
            .SelectMany(u => new[] { (u.UserId, PagePermission.View, u.View), (u.UserId, PagePermission.Edit, u.Edit) })
            .Where(x => x.Item3 is not null)
            .Select(x => (x.UserId, x.Item2, Allow: x.Item3!.Value))
            .ToHashSet();
        var have = current.Select(p => (p.UserId, p.Permission, p.Allow)).ToHashSet();
        if (have.SetEquals(wanted))
        {
            return false;
        }

        _db.PageUserPermissions.RemoveRange(current);
        foreach (var (userId, permission, allow) in wanted)
        {
            _db.PageUserPermissions.Add(new PageUserPermission { TenantId = page.TenantId, PageId = page.Id, UserId = userId, Permission = permission, Allow = allow });
        }

        return true;
    }

    private async Task<string> DescribeAsync(PageGrants grants, IReadOnlyDictionary<Guid, string> roleNames, CancellationToken ct)
    {
        string Roles(IReadOnlySet<Guid> ids) => ids.Count == 0 ? "default" : string.Join(", ", ids.Where(roleNames.ContainsKey).Select(id => roleNames[id]).Order());
        var ids = grants.Users.Select(u => u.UserId).ToList();
        var emails = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        string State(bool? value) => value switch { true => "allow", false => "block", _ => "-" };
        var users = grants.Users.Count == 0
            ? string.Empty
            : "; users: " + string.Join(", ", grants.Users.Select(u => $"{emails.GetValueOrDefault(u.UserId, "?")} (view {State(u.View)}, edit {State(u.Edit)})"));
        return $"View: {Roles(grants.View)}; edit: {Roles(grants.Edit)}{users}";
    }

    /// <summary>Removes the page's grants (when the page is deleted; MongoDB has no cascading deletes).</summary>
    public async Task RemoveAllAsync(Guid pageId, CancellationToken ct = default)
    {
        _db.PagePermissions.RemoveRange(await _db.PagePermissions.Where(p => p.PageId == pageId).ToListAsync(ct));
        _db.PageUserPermissions.RemoveRange(await _db.PageUserPermissions.Where(p => p.PageId == pageId).ToListAsync(ct));
    }

    /// <summary>Removes a user's entries on every page (when the user is deleted).</summary>
    public async Task RemoveUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.PageUserPermissions.RemoveRange(await _db.PageUserPermissions.Where(p => p.UserId == userId).ToListAsync(ct));

    /// <summary>
    /// Whether <paramref name="user"/> may see the page on the site (see the class remarks). <paramref name="canEdit"/>:
    /// whether their roles let them edit pages, which also lets them see restricted ones.
    /// </summary>
    public async Task<bool> CanViewAsync(Guid pageId, Guid tenantId, ClaimsPrincipal user, Func<bool> canEdit, CancellationToken ct = default)
    {
        if (user.IsInRole(Roles.SuperAdmin))
        {
            return true;
        }

        var grants = await GetAsync(pageId, ct);
        var own = UserId(user) is { } id ? grants.Users.FirstOrDefault(u => u.UserId == id) : null;
        if (own?.View == false)
        {
            return false;
        }

        if (own?.View == true || !grants.Restricted)
        {
            return true;
        }

        var names = await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && grants.View.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(ct);
        if (names.Contains(Roles.Public, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return names.Contains(Roles.Authenticated, StringComparer.OrdinalIgnoreCase)
            || user.FindAll(ClaimTypes.Role).Any(c => names.Contains(c.Value, StringComparer.OrdinalIgnoreCase))
            || own?.Edit == true
            || canEdit();
    }

    /// <summary>
    /// The page's effect on whether <paramref name="user"/> may edit it: <c>false</c> = blocked (whatever their roles
    /// grant), <c>true</c> = allowed by a user or role edit grant, <c>null</c> = no page-level rule (the
    /// <c>Collection types</c> grants decide). Super Admins are never blocked.
    /// </summary>
    public async Task<bool?> EditRuleAsync(Guid pageId, Guid tenantId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (user.IsInRole(Roles.SuperAdmin))
        {
            return null;
        }

        var grants = await GetAsync(pageId, ct);
        var own = UserId(user) is { } id ? grants.Users.FirstOrDefault(u => u.UserId == id) : null;
        if (own?.Edit is { } explicitRule)
        {
            return explicitRule;
        }

        if (grants.Edit.Count == 0)
        {
            return null;
        }

        var names = await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && grants.Edit.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(ct);
        return user.FindAll(ClaimTypes.Role).Any(c => names.Contains(c.Value, StringComparer.OrdinalIgnoreCase)) ? true : null;
    }

    private static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
