using System.Security.Claims;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for the server-rendered Razor admin served under /admin (.docs/architecture/pages.md). Requires an
/// admin-capable role and exposes the active tenant, the current user id, and permission checks against the role
/// permission matrix (.docs/features/authorization.md).
/// </summary>
[Area("Admin")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
public abstract class AdminControllerBase : Controller
{
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var id) ? id : Guid.Empty;

    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;

    /// <summary>Whether any of the signed-in user's roles grants <paramref name="action"/> in <paramref name="area"/>.</summary>
    protected bool Can(string area, string action) =>
        HttpContext.RequestServices.GetRequiredService<IPermissionService>()
            .HasAny(User.FindAll(ClaimTypes.Role).Select(c => c.Value), area, action);

    /// <summary>
    /// Whether the user may perform <paramref name="anyAction"/> on any record, or <paramref name="ownAction"/> on a
    /// record they created (e.g. <c>update</c> / <c>update.own</c>).
    /// </summary>
    protected bool CanModify(string area, string anyAction, string ownAction, Guid? createdById) =>
        Can(area, anyAction) || (createdById is not null && createdById == CurrentUserId && Can(area, ownAction));
}
