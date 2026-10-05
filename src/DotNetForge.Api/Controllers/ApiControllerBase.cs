using DotNetForge.Api.Authentication;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Api.Controllers;

/// <summary>
/// Base for all headless API controllers. Requires an authenticated API token and exposes the
/// token's tenant scope so every query stays tenant-isolated (.docs/features/multi-tenancy.md).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiTokenDefaults.Scheme)]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The tenant the presented token is scoped to.</summary>
    protected Guid TenantId =>
        Guid.TryParse(User.FindFirst(ApiTokenDefaults.TenantClaimType)?.Value, out var id)
            ? id
            : Guid.Empty;
}
