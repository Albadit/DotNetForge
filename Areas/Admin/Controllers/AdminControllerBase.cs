using System.Security.Claims;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for the server-rendered Razor admin served under /admin (admin_area.md). Requires an admin-capable
/// role and exposes the active tenant and current user id from the cookie principal.
/// </summary>
[Area("Admin")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
public abstract class AdminControllerBase : Controller
{
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var id) ? id : Guid.Empty;

    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
}
