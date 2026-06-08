using System.Security.Claims;
using System.Text.Encodings.Web;
using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetForge.Api.Authentication;

/// <summary>Well-known names for the API-token authentication scheme and its claims.</summary>
public static class ApiTokenDefaults
{
    public const string Scheme = "ApiToken";
    public const string PermissionClaimType = "dnf:permission";
    public const string TenantClaimType = "dnf:tenant";
    public const string TokenIdClaimType = "dnf:token";
}

/// <summary>
/// Authenticates headless/Hybrid API requests presenting <c>Authorization: Bearer &lt;token&gt;</c>.
/// Tokens are matched by their non-secret prefix, then verified against the stored salted hash. The
/// token's granted permissions and tenant scope become claims for downstream authorization
/// (api_tokens.md, security.md). Expired or revoked tokens yield 401.
/// </summary>
public sealed class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly DotNetForgeDbContext _db;
    private readonly IApiTokenFactory _tokenFactory;
    private readonly IDateTimeProvider _clock;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DotNetForgeDbContext db,
        IApiTokenFactory tokenFactory,
        IDateTimeProvider clock)
        : base(options, logger, encoder)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _clock = clock;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var raw = authHeader.ToString();
        const string bearer = "Bearer ";
        if (!raw.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = raw[bearer.Length..].Trim();
        if (presented.Length == 0)
        {
            return AuthenticateResult.Fail("Empty token.");
        }

        var prefix = _tokenFactory.ExtractPrefix(presented);
        var candidates = await _db.ApiTokens
            .Where(t => t.TokenPrefix == prefix)
            .ToListAsync();

        var token = candidates.FirstOrDefault(t => _tokenFactory.Verify(presented, t.TokenHash));
        if (token is null)
        {
            return AuthenticateResult.Fail("Unknown token.");
        }

        if (token.Revoked)
        {
            return AuthenticateResult.Fail("Token has been revoked.");
        }

        if (token.IsExpired(_clock.UtcNow))
        {
            return AuthenticateResult.Fail("Token has expired.");
        }

        token.LastUsedDate = _clock.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.Id.ToString()),
            new(ApiTokenDefaults.TokenIdClaimType, token.Id.ToString()),
            new(ApiTokenDefaults.TenantClaimType, token.TenantId.ToString()),
        };
        claims.AddRange(token.Permissions.Select(p => new Claim(ApiTokenDefaults.PermissionClaimType, p)));

        var identity = new ClaimsIdentity(claims, ApiTokenDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiTokenDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }
}
