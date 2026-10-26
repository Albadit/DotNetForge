using System.Security.Claims;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DotNetForge.Web.Startup;

/// <summary>
/// <c>[RequirePermission(PermissionAreas.Users, PermissionActions.Read)]</c> - the signed-in user's roles must grant
/// the pair (.docs/features/authorization.md#screen-access). Evaluated from the roles' stored grants,
/// so custom roles work; combine with the admin area policy that <c>AdminControllerBase</c> already applies.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string area, string action)
    {
        Policy = PermissionPolicyProvider.PolicyName(area, action);
    }
}

/// <summary>The requirement behind <see cref="RequirePermissionAttribute"/>.</summary>
public sealed record PermissionRequirement(string Area, string Action) : IAuthorizationRequirement;

/// <summary>Turns <c>permission:{area}:{action}</c> policy names into a <see cref="PermissionRequirement"/>.</summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private const string Prefix = "permission:";

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
    {
    }

    public static string PolicyName(string area, string action) => $"{Prefix}{area}:{action}";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        // Areas contain spaces but never ':' (PermissionAreas); actions never contain ':' either.
        var parts = policyName[Prefix.Length..].Split(':');
        return parts.Length != 2
            ? null
            : new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(parts[0], parts[1])).Build();
    }
}

/// <summary>Grants <see cref="PermissionRequirement"/> when any of the user's roles has the pair.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionAuthorizationHandler(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        if (_permissions.IsGranted(roles, requirement.Area, requirement.Action))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
