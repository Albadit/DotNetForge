using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Base for the legacy Razor admin (kept as a fallback under /admin-legacy; the primary admin is the
/// React SPA at /admin). Requires an admin-capable role and exposes the active tenant.
/// </summary>
[Area("Admin")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
public abstract class AdminControllerBase : Controller
{
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var id) ? id : Guid.Empty;
}
