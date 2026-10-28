using System.Security.Claims;
using System.Text.Encodings.Web;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetForge.Api.Authentication;

/// <summary>
/// Lets a Super Admin who is signed in to the admin call the headless API from the admin itself - the API docs'
/// "Send" - without pasting a token (.docs/features/headless-api.md#authentication). Only used when the request has no
/// <c>Authorization</c> header (<see cref="ApiTokenDefaults.RequestScheme"/> picks the scheme), and only for a
/// same-origin browser request (<c>Sec-Fetch-Site: same-origin</c>, which another site can't set), so the session
/// cookie can't be used cross-site. The session gets every <see cref="PermissionKeys"/> key, like a Super Admin token;
/// audit entries name the user.
/// </summary>
public sealed class AdminSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AdminSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Request.Headers["Sec-Fetch-Site"].ToString(), "same-origin", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        // The admin cookie, validated as on every admin screen (a deleted or disabled user is signed out).
        var session = await Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var user = session.Principal;
        if (!session.Succeeded || user is null || !user.IsInRole(Roles.SuperAdmin))
        {
            return AuthenticateResult.NoResult();
        }

        var claims = user.Claims.ToList();
        claims.AddRange(PermissionKeys.All.Select(p => new Claim(ApiTokenDefaults.PermissionClaimType, p)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiTokenDefaults.SessionScheme, ClaimTypes.Name, ClaimTypes.Role));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiTokenDefaults.SessionScheme));
    }
}
