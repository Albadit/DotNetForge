using DotNetForge.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Api.Controllers;

/// <summary>
/// Base for all headless API controllers. Requires an authenticated API token and exposes the
/// token's tenant scope so every query stays tenant-isolated (multi_tenancy.md).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiTokenDefaults.Scheme)]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The tenant the presented token is scoped to.</summary>
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(ApiTokenDefaults.TenantClaimType)?.Value, out var id)
            ? id
            : Guid.Empty;
}
