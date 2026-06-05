using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for admin-area controllers. Requires an admin-capable role and exposes the active tenant.
/// The admin area always uses the built-in admin layout, never a public frontend theme (admin_area.md).
/// </summary>
[Area("Admin")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
public abstract class AdminControllerBase : Controller
{
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var id) ? id : Guid.Empty;
}
