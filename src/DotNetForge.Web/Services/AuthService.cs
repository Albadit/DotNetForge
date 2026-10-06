using System.Security.Claims;
using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>The result of a sign-in attempt.</summary>
public enum SignInStatus
{
    Success,
    InvalidCredentials,
    Disabled,
    LockedOut,
}

public sealed record SignInResult(SignInStatus Status, ClaimsPrincipal? Principal, User? User);

/// <summary>
/// Validates email/password credentials with account lockout (.docs/features/security.md,
/// .docs/features/authentication.md) and builds the cookie principal (roles + tenant claims) used by the admin
/// area.
/// </summary>
public sealed class AuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public const string TenantClaimType = "dnf:tenant";

    private readonly DotNetForgeDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _clock;

    public AuthService(DotNetForgeDbContext db, IPasswordHasher passwordHasher, IDateTimeProvider clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    public async Task<SignInResult> ValidateAsync(
        string email, string password, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == normalized, cancellationToken);

        // Neutral response to avoid account enumeration.
        if (user is null)
        {
            return new SignInResult(SignInStatus.InvalidCredentials, null, null);
        }

        if (user.Status == UserStatus.Disabled)
        {
            return new SignInResult(SignInStatus.Disabled, null, null);
        }

        if (user.LockoutEndUtc is { } until && until > _clock.UtcNow)
        {
            return new SignInResult(SignInStatus.LockedOut, null, null);
        }

        if (!_passwordHasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = _clock.UtcNow.Add(LockoutWindow);
                user.FailedLoginCount = 0;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new SignInResult(SignInStatus.InvalidCredentials, null, null);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.LastLoginDate = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Two single-table queries rather than a join, so sign-in works on every provider (including MongoDB).
        var roleIds = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);
        var roles = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        return new SignInResult(SignInStatus.Success, BuildPrincipal(user, roles), user);
    }

    /// <summary>
    /// The tenant used for sign-in. There is no tenant resolution yet (.docs/features/multi-tenancy.md), so this is
    /// the oldest tenant - the seeded default.
    /// </summary>
    public Task<Guid> GetDefaultTenantIdAsync(CancellationToken cancellationToken = default) =>
        _db.Tenants.OrderBy(t => t.CreatedDate).Select(t => t.Id).FirstAsync(cancellationToken);

    /// <summary>
    /// Whether a signed-in user may keep their session: the account still exists and is enabled. Called for every
    /// authenticated cookie (see <c>DependencyRegistration</c>), so disabling or deleting a user ends their session
    /// instead of leaving it valid until the cookie expires.
    /// </summary>
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Enabled, cancellationToken);

    private static ClaimsPrincipal BuildPrincipal(User user, IReadOnlyList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(TenantClaimType, user.TenantId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
