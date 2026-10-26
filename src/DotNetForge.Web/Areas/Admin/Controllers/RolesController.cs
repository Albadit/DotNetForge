using DotNetForge.Data;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Roles (.docs/pages/roles.md): list, create, edit (name, description, permission grid), duplicate and delete roles.
/// The rules live in <see cref="RoleManagementService"/>.
/// </summary>
[Route("admin/roles")]
[RequirePermission(PermissionAreas.Roles, PermissionActions.Read)]
public sealed class RolesController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly RoleManagementService _roles;

    public RolesController(DotNetForgeDbContext db, RoleManagementService roles)
    {
        _db = db;
        _roles = roles;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Separate single-table queries, combined in memory: they run on every provider, including MongoDB, which
        // can't count across collections in one query (.docs/database/mongodb.md#query-rules).
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description, r.IsBuiltIn })
            .ToListAsync();
        var roleIds = roles.Select(r => r.Id).ToList();
        var users = (await _db.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.RoleId).ToListAsync())
            .CountBy(id => id).ToDictionary();
        var permissions = (await _db.RolePermissions.AsNoTracking().Where(p => roleIds.Contains(p.RoleId)).Select(p => new { p.RoleId, p.Area, p.Action }).ToListAsync())
            .Where(p => PermissionCatalog.IsKnown(p.Area, p.Action))
            .CountBy(p => p.RoleId).ToDictionary();

        ViewData["CanCreate"] = Can(PermissionAreas.Roles, PermissionActions.Create);
        ViewData["CanDelete"] = Can(PermissionAreas.Roles, PermissionActions.Delete);
        return View(roles.Select(r => new RoleListItem(
            r.Id, r.Name, r.Description, r.IsBuiltIn, users.GetValueOrDefault(r.Id),
            IsSuperAdmin(r.Name, r.IsBuiltIn) ? CatalogSize : permissions.GetValueOrDefault(r.Id))).ToList());
    }

    [HttpGet("create")]
    [RequirePermission(PermissionAreas.Roles, PermissionActions.Create)]
    public IActionResult Create() => View("Edit", NewForm(new RoleInput()));

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Roles, PermissionActions.Create)]
    public async Task<IActionResult> Create(RoleInput input)
    {
        var (id, error) = await _roles.CreateAsync(input, Actor);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View("Edit", NewForm(input));
        }

        TempData["Success"] = $"Created the role '{input.Name!.Trim()}'. Assign it to users on the Users screen.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var form = await BuildFormAsync(id, input: null);
        return form is null ? NotFound() : View(form);
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Roles, PermissionActions.Update)]
    public async Task<IActionResult> Edit(Guid id, RoleInput input)
    {
        var error = await _roles.UpdateAsync(id, input, Actor);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            var form = await BuildFormAsync(id, input);
            return form is null ? NotFound() : View(form);
        }

        TempData["Success"] = "Saved. Permission changes apply immediately.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/duplicate")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Roles, PermissionActions.Create)]
    public async Task<IActionResult> Duplicate(Guid id)
    {
        var (copyId, error) = await _roles.DuplicateAsync(id, Actor);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "Duplicated the role. Rename it and adjust its permissions.";
        return RedirectToAction(nameof(Edit), new { id = copyId });
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Roles, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var name = await _db.Roles.AsNoTracking().Where(r => r.Id == id).Select(r => r.Name).FirstOrDefaultAsync();
        var error = await _roles.DeleteAsync(id, Actor);
        if (error is not null)
        {
            TempData["Error"] = error;
            return Request.Form["returnTo"] == "list" ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Edit), new { id });
        }

        TempData["Success"] = $"Deleted the role '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    private static int CatalogSize => PermissionCatalog.Areas.Sum(a => a.Actions.Count);

    private ManagementActor Actor => new(CurrentUserId ?? Guid.Empty, User.IsInRole(Roles.SuperAdmin));

    private static bool IsSuperAdmin(string name, bool builtIn) =>
        builtIn && string.Equals(name, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

    private RoleFormModel NewForm(RoleInput input) => new(
        null, input, IsBuiltIn: false, NameEditable: true,
        Grants: input.Grants.ToHashSet(StringComparer.OrdinalIgnoreCase),
        GrantsReadOnlyReason: null, Members: Array.Empty<RoleMember>(),
        CanSave: true, CanDelete: false, CanDuplicate: false);

    private async Task<RoleFormModel?> BuildFormAsync(Guid id, RoleInput? input)
    {
        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return null;
        }

        var memberIds = await _db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == id).Select(ur => ur.UserId).ToListAsync();
        var members = (await _db.Users.AsNoTracking()
                .Where(u => memberIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName })
                .ToListAsync())
            .Select(u => new RoleMember(u.Id, u.Email, $"{u.FirstName} {u.LastName}".Trim()))
            .OrderBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var superAdmin = IsSuperAdmin(role.Name, role.IsBuiltIn);
        var actorIsSuperAdmin = User.IsInRole(Roles.SuperAdmin);
        var canUpdate = Can(PermissionAreas.Roles, PermissionActions.Update) && (!superAdmin || actorIsSuperAdmin);
        string? readOnly = superAdmin
            ? "Super Admin always has every permission; it can't be limited."
            : !RoleManagementService.HasEditableGrants(role.Name)
                ? "Public is what site visitors may see; its permissions are fixed."
                : !canUpdate
                    ? "You can view this role but not change it."
                    : !actorIsSuperAdmin && User.IsInRole(role.Name)
                        ? "You hold this role, so you can't change its permissions. Ask a Super Admin."
                        : null;

        HashSet<string> grants;
        if (superAdmin)
        {
            grants = PermissionCatalog.Areas.SelectMany(a => a.Actions.Select(action => PermissionCatalog.Key(a.Area, action)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else if (input is not null && readOnly is null)
        {
            grants = input.Grants.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            grants = (await _db.RolePermissions.AsNoTracking().Where(p => p.RoleId == id).Select(p => new { p.Area, p.Action }).ToListAsync())
                .Where(p => PermissionCatalog.IsKnown(p.Area, p.Action))
                .Select(p => PermissionCatalog.Key(p.Area, p.Action))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        input ??= new RoleInput { Name = role.Name, Description = role.Description };
        if (role.IsBuiltIn)
        {
            input.Name = role.Name;
        }

        return new RoleFormModel(
            id, input, role.IsBuiltIn, NameEditable: !role.IsBuiltIn && canUpdate, grants, readOnly, members,
            CanSave: canUpdate,
            CanDelete: !role.IsBuiltIn && Can(PermissionAreas.Roles, PermissionActions.Delete),
            CanDuplicate: Can(PermissionAreas.Roles, PermissionActions.Create));
    }

    public sealed record RoleListItem(Guid Id, string Name, string? Description, bool IsBuiltIn, int Users, int Permissions);

    public sealed record RoleMember(Guid Id, string Email, string Name);

    /// <summary>The create/edit form. <see cref="Id"/> is null when creating.</summary>
    public sealed record RoleFormModel(Guid? Id, RoleInput Input, bool IsBuiltIn, bool NameEditable, IReadOnlySet<string> Grants,
        string? GrantsReadOnlyReason, IReadOnlyList<RoleMember> Members, bool CanSave, bool CanDelete, bool CanDuplicate);
}
