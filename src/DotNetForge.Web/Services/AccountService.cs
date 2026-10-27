using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotNetForge.Abstractions.Messaging;
using DotNetForge.Abstractions.Security;
using DotNetForge.Core.Email;
using DotNetForge.Core.Validation;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>What the registration form posts.</summary>
public sealed class RegisterInput
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }
}

/// <summary>The outcome of a registration: the new user, whether they must confirm their email first, or an error.</summary>
public sealed record RegisterResult(User? User, bool MustConfirm, string? Error);

/// <summary>
/// Self-service accounts (.docs/features/authentication.md#self-service-accounts): registration (when sign-ups are on),
/// password reset and email confirmation. Links carry ASP.NET Data Protection tokens - time-limited and bound to the
/// account, so they need no table: a reset link stops working once the password changes (single use), a confirmation
/// link once the email changes. Links are built from <c>APP_URL</c>, never from the request's Host header.
/// </summary>
public sealed partial class AccountService
{
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan ConfirmationLinkLifetime = TimeSpan.FromDays(2);

    private readonly DotNetForgeDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;
    private readonly AppEnvironment _env;
    private readonly UserAccountSettingsService _settings;
    private readonly IAuditService _audit;
    private readonly ILogger<AccountService> _logger;
    private readonly ITimeLimitedDataProtector _tokens;
    private readonly IEmailSettingsSource _emailSettings;
    private readonly EmailTemplateService _templates;

    public AccountService(DotNetForgeDbContext db, IPasswordHasher hasher, IEmailSender email, AppEnvironment env,
        UserAccountSettingsService settings, IAuditService audit, IDataProtectionProvider dataProtection, ILogger<AccountService> logger,
        IEmailSettingsSource emailSettings, EmailTemplateService templates)
    {
        _emailSettings = emailSettings;
        _templates = templates;
        _db = db;
        _hasher = hasher;
        _email = email;
        _env = env;
        _settings = settings;
        _audit = audit;
        _logger = logger;
        _tokens = dataProtection.CreateProtector("DotNetForge.Account.Links.v1").ToTimeLimitedDataProtector();
    }

    /// <summary>Whether email can be sent (SMTP configured): "Forgot password?" and confirmation depend on it.</summary>
    public async Task<bool> EmailEnabledAsync(CancellationToken ct = default) => (await _emailSettings.GetAsync(ct)).IsConfigured;

    public async Task<RegisterResult> RegisterAsync(RegisterInput input, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        if (!settings.SignUps)
        {
            return new RegisterResult(null, false, "Registration is turned off.");
        }

        var email = input.Email?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email) || email.Length > UserManagementService.MaxEmailLength)
        {
            return new RegisterResult(null, false, "Enter a valid email address.");
        }

        if (PasswordPolicy.Validate(input.Password) is { } weak)
        {
            return new RegisterResult(null, false, weak);
        }

        if (input.Password != input.ConfirmPassword)
        {
            return new RegisterResult(null, false, "The passwords don't match.");
        }

        if ((input.FirstName?.Trim().Length ?? 0) > UserManagementService.MaxNameLength || (input.LastName?.Trim().Length ?? 0) > UserManagementService.MaxNameLength)
        {
            return new RegisterResult(null, false, $"Names are limited to {UserManagementService.MaxNameLength} characters.");
        }

        // One account per email address (case-insensitive, like the Users screen).
        var emails = await _db.Users.AsNoTracking().Select(u => u.Email).ToListAsync(ct);
        if (emails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return new RegisterResult(null, false, "An account with this email already exists. Sign in, or reset your password.");
        }

        if (await _settings.DefaultRoleAsync(settings, ct) is not { } role)
        {
            _logger.LogWarning("Registration refused: the default role for new accounts no longer exists.");
            return new RegisterResult(null, false, "Registration is not available right now. Please try again later.");
        }

        var user = new User
        {
            FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim(),
            Email = email,
            PasswordHash = _hasher.Hash(input.Password!),
            Status = UserStatus.Enabled,
            EmailConfirmed = !settings.EmailConfirmation,
            CreatedDate = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserRegistered, "User", user.Id.ToString(), user.Email, details: $"Role: {role.Name}", cancellationToken: ct);

        if (settings.EmailConfirmation)
        {
            await SendConfirmationAsync(user, ct);
        }
        else if (await EmailEnabledAsync(ct))
        {
            await SendWelcomeAsync(user, ct);
        }

        return new RegisterResult(user, settings.EmailConfirmation, null);
    }

    /// <summary>
    /// Emails a reset link when the address belongs to an enabled account. Always completes the same way, so the form
    /// can answer neutrally and never reveals whether an account exists.
    /// </summary>
    public async Task RequestPasswordResetAsync(string? email, CancellationToken ct = default)
    {
        var address = email?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(address) || !await EmailEnabledAsync(ct))
        {
            return;
        }

        var user = (await _db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Enabled).ToListAsync(ct))
            .FirstOrDefault(u => string.Equals(u.Email, address, StringComparison.OrdinalIgnoreCase));
        await _audit.LogAsync(AuditActions.PasswordResetRequested, "User", user?.Id.ToString(), address, success: user is not null, cancellationToken: ct);
        if (user is null)
        {
            return;
        }

        var settings = await _settings.GetAsync(ct);
        var token = _tokens.Protect($"reset|{user.Id}|{Fingerprint(user.PasswordHash)}", ResetLinkLifetime);
        var link = Link(settings.ResetPasswordUrl, "/reset-password", token);
        await SendAsync(user, EmailTemplateCatalog.PasswordReset, new()
        {
            ["resetUrl"] = link,
            ["expiryMinutes"] = ResetLinkLifetime.TotalMinutes.ToString("0", CultureInfo.InvariantCulture),
        }, ct);
    }

    /// <summary>The user a reset link is for, or null when it is invalid, expired or already used.</summary>
    public async Task<User?> FindResetUserAsync(string? token, CancellationToken ct = default)
    {
        if (Read(token, "reset") is not [var userId, var fingerprint] || !Guid.TryParse(userId, out var id))
        {
            return null;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is { Status: UserStatus.Enabled } && Fingerprint(user.PasswordHash) == fingerprint ? user : null;
    }

    /// <summary>Sets a new password from a reset link; returns an error or null. The link stops working afterwards.</summary>
    public async Task<string?> ResetPasswordAsync(string? token, string? password, string? confirmPassword, CancellationToken ct = default)
    {
        var user = await FindResetUserAsync(token, ct);
        if (user is null)
        {
            return "This link is invalid or has expired. Ask for a new one.";
        }

        if (PasswordPolicy.Validate(password) is { } weak)
        {
            return weak;
        }

        if (password != confirmPassword)
        {
            return "The passwords don't match.";
        }

        user.PasswordHash = _hasher.Hash(password!);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        // The link reached the inbox, so the address is confirmed too.
        user.EmailConfirmed = true;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.PasswordReset, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
        return null;
    }

    /// <summary>Confirms the email of a confirmation link; returns the user, or null when the link is invalid or expired.</summary>
    public async Task<User?> ConfirmEmailAsync(string? token, CancellationToken ct = default)
    {
        if (Read(token, "confirm") is not [var userId, var email] || !Guid.TryParse(userId, out var id))
        {
            return null;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null || !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActions.EmailConfirmed, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
            await SendWelcomeAsync(user, ct);
        }

        return user;
    }

    /// <summary>Sends the confirmation link again to an unconfirmed account; neutral like the reset request.</summary>
    public async Task ResendConfirmationAsync(string? email, CancellationToken ct = default)
    {
        var address = email?.Trim() ?? string.Empty;
        var settings = await _settings.GetAsync(ct);
        if (!settings.EmailConfirmation || !EmailValidator.IsValid(address))
        {
            return;
        }

        var user = (await _db.Users.AsNoTracking().Where(u => !u.EmailConfirmed && u.Status == UserStatus.Enabled).ToListAsync(ct))
            .FirstOrDefault(u => string.Equals(u.Email, address, StringComparison.OrdinalIgnoreCase));
        if (user is not null)
        {
            await SendConfirmationAsync(user, ct);
        }
    }

    /// <summary>Where a confirmed user goes: the configured redirection URL, else the sign-in page.</summary>
    public async Task<string> ConfirmedRedirectAsync(CancellationToken ct = default) =>
        (await _settings.GetAsync(ct)).ConfirmationRedirectUrl ?? "/login";

    /// <summary>
    /// Tells the owner that repeated wrong passwords locked their account (<c>AuthService</c> calls it when the lockout
    /// starts); a password reset unlocks it right away. Nothing is sent without SMTP.
    /// </summary>
    public async Task SendAccountLockedAsync(User user, TimeSpan lockout, CancellationToken ct = default)
    {
        if (!await EmailEnabledAsync(ct))
        {
            return;
        }

        await SendAsync(user, EmailTemplateCatalog.AccountLocked, new()
        {
            ["unlockUrl"] = _env.AppUrl.TrimEnd('/') + "/forgot-password",
            ["lockoutMinutes"] = lockout.TotalMinutes.ToString("0", CultureInfo.InvariantCulture),
        }, ct);
    }

    private async Task SendConfirmationAsync(User user, CancellationToken ct)
    {
        var token = _tokens.Protect($"confirm|{user.Id}|{user.Email}", ConfirmationLinkLifetime);
        await SendAsync(user, EmailTemplateCatalog.EmailConfirmation, new()
        {
            ["confirmationUrl"] = Link(null, "/confirm-email", token),
            ["expiryDays"] = ConfirmationLinkLifetime.TotalDays.ToString("0", CultureInfo.InvariantCulture),
        }, ct);
    }

    private Task SendWelcomeAsync(User user, CancellationToken ct) =>
        SendAsync(user, EmailTemplateCatalog.WelcomeEmail, new() { ["loginUrl"] = _env.AppUrl.TrimEnd('/') + "/login" }, ct);

    /// <summary>Renders the template (customized on Email → Templates, else the default) and sends it.</summary>
    private async Task SendAsync(User user, string templateKey, Dictionary<string, string> values, CancellationToken ct)
    {
        var purpose = EmailTemplateCatalog.Find(templateKey)?.Name ?? templateKey;
        values["userName"] = user.DisplayName;
        values["appName"] = _env.AppName;
        values["appUrl"] = _env.AppUrl.TrimEnd('/');
        try
        {
            var (subject, html) = await _templates.RenderAsync(templateKey, values, ct);
            await _email.SendAsync(new EmailMessage { To = user.Email, Subject = subject, Body = html, IsHtml = true, Template = templateKey }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The error type only: SMTP errors can echo addresses. The form still answers neutrally.
            LogSendFailed(purpose, ex.GetType().Name);
        }
    }

    /// <summary><paramref name="customUrl"/> (a front end's page) or APP_URL + <paramref name="path"/>, with <c>?token=</c>.</summary>
    private string Link(string? customUrl, string path, string token)
    {
        var target = customUrl ?? _env.AppUrl.TrimEnd('/') + path;
        return target + (target.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(token);
    }

    /// <summary>The parts after the purpose of a valid, unexpired token, or null.</summary>
    private string[]? Read(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = _tokens.Unprotect(token).Split('|', 3);
            return parts.Length == 3 && parts[0] == purpose ? parts[1..] : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A short digest of the password hash: a changed password invalidates outstanding reset links.</summary>
    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending the {Purpose} email failed with {ErrorType}.")]
    private partial void LogSendFailed(string purpose, string errorType);
}
