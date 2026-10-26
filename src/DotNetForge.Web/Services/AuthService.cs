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
/// .docs/features/authentication.md) and builds the cookie principal (user and role claims) used by the admin
/// area.
/// </summary>
public sealed class AuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

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
        string email, string password, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

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
        return await CompleteSignInAsync(user, cancellationToken);
    }

    /// <summary>
    /// Signs in the existing user whose email an external provider (an authentication extension) vouched for
    /// (.docs/features/authentication.md#external-sign-in-extensions). No account is created; disabled and locked-out
    /// accounts are refused exactly as for a password sign-in.
    /// </summary>
    public async Task<SignInResult> ValidateExternalAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim();
        var user = normalized.Length == 0
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

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

        return await CompleteSignInAsync(user, cancellationToken);
    }

    /// <summary>
    /// Whether a signed-in user may keep their session: the account still exists and is enabled. Called for every
    /// authenticated cookie (see <c>DependencyRegistration</c>), so disabling or deleting a user ends their session
    /// instead of leaving it valid until the cookie expires.
    /// </summary>
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Enabled, cancellationToken);

    /// <summary>
    /// A fresh cookie principal for an already signed-in user (after they change their own name or email), so the
    /// shell shows the new values without signing in again. Null when the account is gone or disabled.
    /// </summary>
    public async Task<ClaimsPrincipal?> RefreshPrincipalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Enabled, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(cancellationToken);
        return BuildPrincipal(user, roles);
    }

    private async Task<SignInResult> CompleteSignInAsync(User user, CancellationToken cancellationToken)
    {
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

    private static ClaimsPrincipal BuildPrincipal(User user, IReadOnlyList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
