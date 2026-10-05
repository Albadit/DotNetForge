using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
/// (.docs/features/headless-api.md, .docs/features/security.md). Expired or revoked tokens yield 401.
/// </summary>
public sealed class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>How long a successful hash verification is remembered (revocation/expiry are still checked every request).</summary>
    private static readonly TimeSpan VerificationCacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary><see cref="ApiToken.LastUsedDate"/> is written at most this often, not on every request.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    private readonly DotNetForgeDbContext _db;
    private readonly IApiTokenFactory _tokenFactory;
    private readonly IDateTimeProvider _clock;
    private readonly IMemoryCache _cache;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DotNetForgeDbContext db,
        IApiTokenFactory tokenFactory,
        IDateTimeProvider clock,
        IMemoryCache cache)
        : base(options, logger, encoder)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _clock = clock;
        _cache = cache;
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

        var token = await FindTokenAsync(presented);
        if (token is null)
        {
            return AuthenticateResult.Fail("Unknown token.");
        }

        if (token.Revoked)
        {
            return AuthenticateResult.Fail("Token has been revoked.");
        }

        var now = _clock.UtcNow;
        if (token.IsExpired(now))
        {
            return AuthenticateResult.Fail("Token has expired.");
        }

        await TouchLastUsedAsync(token, now);

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

    /// <summary>
    /// Resolves the token row. PBKDF2 verification is deliberately expensive, so a successful match is remembered
    /// in memory under a SHA-256 of the presented secret (never the secret itself); the row - and with it the
    /// revoked/expiry state - is always re-read from the database.
    /// </summary>
    private async Task<ApiToken?> FindTokenAsync(string presented)
    {
        var cacheKey = "dnf:apitoken:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
        if (_cache.TryGetValue(cacheKey, out Guid cachedId))
        {
            return await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == cachedId, Context.RequestAborted);
        }

        var prefix = _tokenFactory.ExtractPrefix(presented);
        var candidates = await _db.ApiTokens
            .Where(t => t.TokenPrefix == prefix)
            .ToListAsync(Context.RequestAborted);

        var token = candidates.FirstOrDefault(t => _tokenFactory.Verify(presented, t.TokenHash));
        if (token is not null)
        {
            _cache.Set(cacheKey, token.Id, VerificationCacheLifetime);
        }

        return token;
    }

    /// <summary>Records usage without turning every API read into a database write, and never fails the request.</summary>
    private async Task TouchLastUsedAsync(ApiToken token, DateTime now)
    {
        if (token.LastUsedDate is { } last && now - last < LastUsedResolution)
        {
            return;
        }

        try
        {
            token.LastUsedDate = now;
            await _db.SaveChangesAsync(Context.RequestAborted);
        }
        catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException)
        {
            Logger.LogWarning(ex, "Could not record last use of API token {TokenId}.", token.Id);
        }
    }
}
