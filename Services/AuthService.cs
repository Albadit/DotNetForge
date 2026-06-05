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
/// Validates email/password credentials with account lockout (security.md, authentication.md) and
/// builds the cookie principal (roles + tenant claims) used by the admin area.
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

        var roles = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .ToListAsync(cancellationToken);

        return new SignInResult(SignInStatus.Success, BuildPrincipal(user, roles), user);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .ToListAsync(cancellationToken);

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
