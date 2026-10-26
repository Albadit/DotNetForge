using System.Security.Claims;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for the server-rendered Razor admin served under /admin (.docs/architecture/pages.md). Requires an
/// admin-capable role and exposes the current user id and permission checks against the role
/// permission matrix (.docs/features/authorization.md).
/// </summary>
[Area("Admin")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
public abstract class AdminControllerBase : Controller
{
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;

    /// <summary>
    /// Whether any of the signed-in user's roles grants <paramref name="action"/> in <paramref name="area"/> - from the
    /// roles' stored grants, so custom roles count.
    /// </summary>
    protected bool Can(string area, string action) =>
        HttpContext.RequestServices.GetRequiredService<IPermissionService>()
            .IsGranted(User.FindAll(ClaimTypes.Role).Select(c => c.Value), area, action);

    /// <summary>
    /// Whether the user may perform <paramref name="anyAction"/> on any record, or <paramref name="ownAction"/> on a
    /// record they created (e.g. <c>update</c> / <c>update.own</c>).
    /// </summary>
    protected bool CanModify(string area, string anyAction, string ownAction, Guid? createdById) =>
        Can(area, anyAction) || (createdById is not null && createdById == CurrentUserId && Can(area, ownAction));

    /// <summary>
    /// Whether the user may edit <paramref name="page"/> (.docs/features/content-pages-and-routing.md#page-permissions):
    /// a page-level rule decides first - blocked for this user, or allowed by a user or role edit grant - otherwise
    /// <c>Collection types</c> update (update.own on pages they created).
    /// </summary>
    protected async Task<bool> CanEditPageAsync(DotNetForge.Shared.Entities.Page page) =>
        await PageEditRuleAsync(page) ?? CanModify(DotNetForge.Shared.Constants.PermissionAreas.CollectionTypes,
            DotNetForge.Shared.Constants.PermissionActions.Update, DotNetForge.Shared.Constants.PermissionActions.UpdateOwn, page.CreatedById);

    /// <summary>The page's own rule for this user: false = blocked, true = allowed, null = none.</summary>
    protected Task<bool?> PageEditRuleAsync(DotNetForge.Shared.Entities.Page page) =>
        HttpContext.RequestServices.GetRequiredService<PagePermissionService>()
            .EditRuleAsync(page.Id, User, HttpContext.RequestAborted);
}
