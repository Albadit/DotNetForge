using DotNetForge.Data;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Users (.docs/pages/users.md): list, create, edit (details, password, roles, status), enable/disable and delete
/// admin users, and show each user's effective permissions. The rules live in <see cref="UserManagementService"/>.
/// </summary>
[Route("admin/users")]
[RequirePermission(PermissionAreas.Users, PermissionActions.Read)]
public sealed class UsersController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly UserManagementService _users;
    private readonly IRoleGrantStore _grants;
    private readonly DotNetForge.Web.Services.Mfa.MfaService _mfa;

    public UsersController(DotNetForgeDbContext db, UserManagementService users, IRoleGrantStore grants, DotNetForge.Web.Services.Mfa.MfaService mfa)
    {
        _db = db;
        _users = users;
        _grants = grants;
        _mfa = mfa;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Single-table queries combined in memory.
        var users = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Status, u.LastLoginDate })
            .ToListAsync();
        var links = await RoleLinksAsync(users.Select(u => u.Id).ToList());
        var roleNames = await RoleNamesAsync();

        ViewData["CanCreate"] = Can(PermissionAreas.Users, PermissionActions.Create);
        ViewData["CanUpdate"] = Can(PermissionAreas.Users, PermissionActions.Update);
        ViewData["CanDelete"] = Can(PermissionAreas.Users, PermissionActions.Delete);
        return View(users.Select(u => new UserListItem(
            u.Id,
            u.Email,
            $"{u.FirstName} {u.LastName}".Trim(),
            u.Status.ToString(),
            u.LastLoginDate,
            links.Where(l => l.UserId == u.Id && roleNames.ContainsKey(l.RoleId)).Select(l => roleNames[l.RoleId]).Order().ToList(),
            u.Id == CurrentUserId)).ToList());
    }

    [HttpGet("create")]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Create)]
    public async Task<IActionResult> Create() =>
        View("Edit", await BuildFormAsync(null, new UserInput()));

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Create)]
    public async Task<IActionResult> Create(UserInput input)
    {
        var (id, error) = await _users.CreateAsync(input, Actor);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            input.Password = null;
            return View("Edit", await BuildFormAsync(null, input));
        }

        TempData["Success"] = $"Created {input.Email!.Trim()}. They can sign in now.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        var input = new UserInput
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RoleIds = (await RoleLinksAsync(new List<Guid> { id })).Select(l => l.RoleId).ToList(),
        };
        return View(await BuildFormAsync(id, input));
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Update)]
    public async Task<IActionResult> Edit(Guid id, UserInput input)
    {
        var error = await _users.UpdateAsync(id, input, Actor);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            input.Password = null;
            return View(await BuildFormAsync(id, input));
        }

        TempData["Success"] = id == CurrentUserId
            ? "Saved. Role changes apply the next time you sign in."
            : "Saved. Role changes apply the next time the user signs in.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Update)]
    public Task<IActionResult> Enable(Guid id) => SetStatusAsync(id, enabled: true);

    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Update)]
    public Task<IActionResult> Disable(Guid id) => SetStatusAsync(id, enabled: false);

    /// <summary>
    /// Removes every second factor and recovery code of a user who lost them (.docs/features/mfa.md#lost-access); they
    /// sign in with their password alone and set up a new method. Not for one's own account (use Sign-in security).
    /// </summary>
    [HttpPost("{id:guid}/mfa/reset")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Update)]
    public async Task<IActionResult> ResetMfa(Guid id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        if (id == CurrentUserId)
        {
            TempData["Error"] = "Change your own methods on Profile › Sign-in security.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        // ResetAsync writes the user.mfa.reset audit entry.
        var removed = await _mfa.ResetAsync(user, HttpContext.RequestAborted);
        TempData["Success"] = removed == 0
            ? $"{user.Email} has no two-step methods."
            : $"Two-step verification is off for {user.Email}. They sign in with their password and can set it up again.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Users, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var email = await EmailOfAsync(id);
        var error = await _users.DeleteAsync(id, Actor);
        if (error is not null)
        {
            TempData["Error"] = error;
            return FromList ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Edit), new { id });
        }

        TempData["Success"] = $"Deleted {email}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SetStatusAsync(Guid id, bool enabled)
    {
        var error = await _users.SetStatusAsync(id, enabled, Actor);
        TempData[error is null ? "Success" : "Error"] = error ?? $"{(enabled ? "Enabled" : "Disabled")} {await EmailOfAsync(id)}.";
        return FromList ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>The list's row menu posts <c>returnTo=list</c> so the user lands back on the list.</summary>
    private bool FromList => Request.Form["returnTo"] == "list";

    private ManagementActor Actor => new(CurrentUserId ?? Guid.Empty, User.IsInRole(Roles.SuperAdmin));

    private async Task<UserFormModel> BuildFormAsync(Guid? id, UserInput input)
    {
        // The aside's Two-step verification card.
        ViewData["MfaMethods"] = id is { } editedId ? await _mfa.MethodsAsync(editedId, HttpContext.RequestAborted) : null;
        var roles = await _db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description, r.IsBuiltIn })
            .ToListAsync();
        var actorIsSuperAdmin = User.IsInRole(Roles.SuperAdmin);
        var options = roles
            .Select(r => new RoleOption(r.Id, r.Name, r.Description, r.IsBuiltIn,
                Locked: !actorIsSuperAdmin && string.Equals(r.Name, Roles.SuperAdmin, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        UserFormModel.Facts? facts = null;
        if (id is { } userId)
        {
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
            var held = (await RoleLinksAsync(new List<Guid> { userId })).Select(l => l.RoleId).ToHashSet();
            var heldNames = roles.Where(r => held.Contains(r.Id)).Select(r => r.Name).ToList();
            facts = new UserFormModel.Facts(user.Email, user.CreatedDate, user.LastLoginDate, user.Status == UserStatus.Enabled,
                heldNames, EffectivePermissions(heldNames));
        }

        var editsSuperAdmin = facts?.Roles.Contains(Roles.SuperAdmin, StringComparer.OrdinalIgnoreCase) == true;
        return new UserFormModel(
            id,
            input,
            options,
            facts,
            IsSelf: id is not null && id == CurrentUserId,
            CanSave: id is null || (Can(PermissionAreas.Users, PermissionActions.Update) && (!editsSuperAdmin || actorIsSuperAdmin)),
            CanDelete: id is not null && id != CurrentUserId && Can(PermissionAreas.Users, PermissionActions.Delete) &&
                       (!editsSuperAdmin || actorIsSuperAdmin));
    }

    /// <summary>What the roles grant together - the union, like <c>PermissionService</c> evaluates it.</summary>
    private IReadOnlyList<(PermissionAreaDefinition Area, IReadOnlyList<string> Actions)> EffectivePermissions(IReadOnlyList<string> roleNames)
    {
        var all = roleNames.Contains(Roles.SuperAdmin, StringComparer.OrdinalIgnoreCase);
        var grants = _grants.GetGrants();
        var keys = roleNames
            .SelectMany(r => grants.TryGetValue(r, out var g) ? g : (IEnumerable<string>)Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PermissionCatalog.Areas
            .Select(a => (a, (IReadOnlyList<string>)a.Actions.Where(action => all || keys.Contains(PermissionCatalog.Key(a.Area, action))).ToList()))
            .ToList();
    }

    private async Task<List<(Guid UserId, Guid RoleId)>> RoleLinksAsync(List<Guid> userIds) =>
        (await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Select(ur => new { ur.UserId, ur.RoleId })
            .ToListAsync())
        .Select(l => (l.UserId, l.RoleId))
        .ToList();

    private async Task<Dictionary<Guid, string>> RoleNamesAsync() =>
        await _db.Roles.AsNoTracking()
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name);

    private async Task<string> EmailOfAsync(Guid id) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync() ?? "the user";

    public sealed record UserListItem(Guid Id, string Email, string Name, string Status, DateTime? LastLogin, List<string> Roles, bool IsSelf);

    /// <summary>A role checkbox; <see cref="Locked"/> when the signed-in user may not assign it (Super Admin).</summary>
    public sealed record RoleOption(Guid Id, string Name, string? Description, bool IsBuiltIn, bool Locked);

    /// <summary>The create/edit form. <see cref="Saved"/> is null when creating.</summary>
    public sealed record UserFormModel(Guid? Id, UserInput Input, IReadOnlyList<RoleOption> Roles, UserFormModel.Facts? Saved,
        bool IsSelf, bool CanSave, bool CanDelete)
    {
        /// <summary>The stored user: what the header, status card and effective permissions show.</summary>
        public sealed record Facts(string Email, DateTime Created, DateTime? LastLogin, bool Enabled, IReadOnlyList<string> Roles,
            IReadOnlyList<(PermissionAreaDefinition Area, IReadOnlyList<string> Actions)> Permissions);
    }
}
