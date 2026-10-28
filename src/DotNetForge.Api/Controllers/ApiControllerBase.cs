using DotNetForge.Api.Authentication;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Api.Controllers;

/// <summary>
/// Base for all headless API controllers. Requires an authenticated API token - or, for same-origin calls from the
/// admin (the API docs), a signed-in Super Admin - and applies the API rate limit (.docs/features/headless-api.md).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiTokenDefaults.RequestScheme)]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
public abstract class ApiControllerBase : ControllerBase
{
}
