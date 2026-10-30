using System.Security.Claims;
using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services.Mfa;
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

    /// <summary>The password is right, but email confirmation is on and this account hasn't confirmed its email yet.</summary>
    EmailNotConfirmed,

    /// <summary>The first step is right and the account has a second factor (.docs/features/mfa.md#sign-in): ask for it.</summary>
    SecondFactorRequired,
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
    private readonly UserAccountSettingsService _accountSettings;
    private readonly AccountService _accounts;
    private readonly MfaService _mfa;

    public AuthService(DotNetForgeDbContext db, IPasswordHasher passwordHasher, IDateTimeProvider clock, UserAccountSettingsService accountSettings,
        AccountService accounts, MfaService mfa)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _accountSettings = accountSettings;
        _accounts = accounts;
        _mfa = mfa;
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
            await RecordFailureAsync(user, cancellationToken);
            return new SignInResult(SignInStatus.InvalidCredentials, null, null);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        if (!user.EmailConfirmed && (await _accountSettings.GetAsync(cancellationToken)).EmailConfirmation)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new SignInResult(SignInStatus.EmailNotConfirmed, null, user);
        }

        return await SecondFactorOrSignInAsync(user, cancellationToken);
    }

    /// <summary>
    /// Finishes a sign-in after its second factor was checked (.docs/features/mfa.md#sign-in): the account must still
    /// be enabled and not locked - it may have changed during the code step.
    /// </summary>
    public async Task<SignInResult> CompleteSecondFactorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
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

        user.FailedLoginCount = 0;
        return await CompleteSignInAsync(user, cancellationToken);
    }

    /// <summary>
    /// A wrong second-factor code counts like a wrong password: <see cref="MaxFailedAttempts"/> lock the account, so
    /// a 6-digit code can't be guessed. Returns whether the account is locked now.
    /// </summary>
    public async Task<bool> RecordFailedSecondFactorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is not null && await RecordFailureAsync(user, cancellationToken);
    }

    /// <summary>The enabled, not locked-out user a pending sign-in belongs to (the code page shows their methods), or null.</summary>
    public async Task<User?> FindPendingUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Enabled, cancellationToken);
        return user?.LockoutEndUtc is { } until && until > _clock.UtcNow ? null : user;
    }

    /// <summary>
    /// Signs in the existing user whose verified email a provider vouched for
    /// (.docs/features/authentication.md#external-sign-in): the account with exactly that email, else the only one
    /// with it in another case (providers don't keep the case people typed here). Disabled and locked-out accounts are
    /// refused exactly as for a password sign-in. No account: <see cref="SignInStatus.InvalidCredentials"/> - the
    /// caller may create one (sign-up through a provider).
    /// </summary>
    public async Task<SignInResult> ValidateExternalAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim();
        var lower = normalized.ToLowerInvariant();
        var user = normalized.Length == 0
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
              ?? (await _db.Users.Where(u => u.Email.ToLower() == lower).Take(2).ToListAsync(cancellationToken)) switch
              {
                  [var only] => only,
                  _ => null,
              };

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

        return await SecondFactorOrSignInAsync(user, cancellationToken);
    }

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

    private async Task<SignInResult> SecondFactorOrSignInAsync(User user, CancellationToken cancellationToken)
    {
        if (await _mfa.RequiresSecondFactorAsync(user.Id, cancellationToken))
        {
            await _db.SaveChangesAsync(cancellationToken); // the reset failure counter
            return new SignInResult(SignInStatus.SecondFactorRequired, null, user);
        }

        return await CompleteSignInAsync(user, cancellationToken);
    }

    /// <summary>Counts a failed attempt and locks the account at <see cref="MaxFailedAttempts"/>; true when it locked now.</summary>
    private async Task<bool> RecordFailureAsync(User user, CancellationToken cancellationToken)
    {
        user.FailedLoginCount++;
        var lockedNow = user.FailedLoginCount >= MaxFailedAttempts;
        if (lockedNow)
        {
            user.LockoutEndUtc = _clock.UtcNow.Add(LockoutWindow);
            user.FailedLoginCount = 0;
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (lockedNow)
        {
            // The "Account locked" email (Email → Templates); nothing without SMTP, a failed send is only logged.
            await _accounts.SendAccountLockedAsync(user, LockoutWindow, cancellationToken);
        }

        return lockedNow;
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
