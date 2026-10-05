using DotNetForge.Api.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotNetForge.Api.Authorization;

/// <summary>
/// Enforces that the authenticated API token carries a specific granular permission. Returns 401
/// when unauthenticated and 403 when the permission is missing - matching the .docs/features/headless-api.md contract
/// (every API action is gated by a permission check).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireApiPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequireApiPermissionAttribute(string permission)
    {
        _permission = permission;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var hasPermission = user.Claims.Any(c =>
            c.Type == ApiTokenDefaults.PermissionClaimType &&
            string.Equals(c.Value, _permission, StringComparison.OrdinalIgnoreCase));

        if (!hasPermission)
        {
            context.Result = new ObjectResult(new { error = $"Missing required permission '{_permission}'." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }
}
