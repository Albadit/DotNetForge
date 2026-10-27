using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// The Advanced User Settings (.docs/features/authentication.md#advanced-user-settings), as stored.
/// </summary>
/// <param name="SignUps">Visitors may create an account at <c>/register</c>. Off by default.</param>
/// <param name="DefaultRoleId">The role new accounts get; null = <c>Authenticated</c>.</param>
/// <param name="EmailConfirmation">New accounts must confirm their email before they can sign in (needs SMTP).</param>
/// <param name="ResetPasswordUrl">Where reset links point (a headless front end's page); null = the built-in <c>/reset-password</c>.</param>
/// <param name="ConfirmationRedirectUrl">Where a confirmed user lands; null = the sign-in page.</param>
public sealed record UserAccountSettings(
    bool SignUps, Guid? DefaultRoleId, bool EmailConfirmation, string? ResetPasswordUrl, string? ConfirmationRedirectUrl);

/// <summary>
/// Reads and saves <see cref="UserAccountSettings"/> as rows of the <c>Settings</c> table (keys <c>users.*</c>), so
/// they need no table of their own and show on the Settings screen too.
/// </summary>
public sealed class UserAccountSettingsService
{
    public const string SignUpsKey = "users.signUps";
    public const string DefaultRoleKey = "users.defaultRole";
    public const string EmailConfirmationKey = "users.emailConfirmation";
    public const string ResetPasswordUrlKey = "users.resetPasswordUrl";
    public const string ConfirmationRedirectUrlKey = "users.confirmationRedirectUrl";

    private static readonly string[] Keys = { SignUpsKey, DefaultRoleKey, EmailConfirmationKey, ResetPasswordUrlKey, ConfirmationRedirectUrlKey };

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IEmailSettingsSource _email;

    public UserAccountSettingsService(DotNetForgeDbContext db, IAuditService audit, IEmailSettingsSource email)
    {
        _db = db;
        _audit = audit;
        _email = email;
    }

    public async Task<UserAccountSettings> GetAsync(CancellationToken ct = default)
    {
        var keys = Keys.ToList();
        var values = await _db.Settings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string? Value(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        var confirmation = Value(EmailConfirmationKey) == "true" && (await _email.GetAsync(ct)).IsConfigured;
        return new UserAccountSettings(
            Value(SignUpsKey) == "true",
            Guid.TryParse(Value(DefaultRoleKey), out var role) ? role : null,
            // Confirmation can only be on while email can be sent.
            confirmation,
            Value(ResetPasswordUrlKey),
            Value(ConfirmationRedirectUrlKey));
    }

    /// <summary>The role new accounts get: the configured one, else <c>Authenticated</c>; null when neither exists.</summary>
    public async Task<Role?> DefaultRoleAsync(UserAccountSettings settings, CancellationToken ct = default)
    {
        if (settings.DefaultRoleId is { } id && await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) is { } chosen)
        {
            return chosen;
        }

        return settings.DefaultRoleId is null
            ? await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == Roles.Authenticated, ct)
            : null; // the chosen role was deleted: registration stops until a valid one is set
    }

    /// <summary>
    /// Validates and saves; returns the errors (nothing is saved when there are any). Only a Super Admin
    /// (<paramref name="bySuperAdmin"/>) may give new accounts a role that opens the admin - otherwise anyone with the
    /// Settings permission could sign themselves up with more rights than they have (keeping one a Super Admin chose is fine).
    /// </summary>
    public async Task<List<string>> SaveAsync(UserAccountSettings input, bool bySuperAdmin, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (input.DefaultRoleId is { } roleId)
        {
            var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct);
            if (role is null)
            {
                errors.Add("Choose a default role that exists.");
            }
            else if (role.Name is Roles.SuperAdmin or Roles.Public)
            {
                errors.Add($"New accounts can't get the {role.Name} role.");
            }
            else if (!bySuperAdmin && Roles.CanEnterAdmin(new[] { role.Name }) && (await GetAsync(ct)).DefaultRoleId != roleId)
            {
                errors.Add($"Only a Super Admin can give new accounts a role that opens the admin ({role.Name}).");
            }
        }

        if (input.EmailConfirmation && !(await _email.GetAsync(ct)).IsConfigured)
        {
            errors.Add("Email confirmation needs email: set SMTP_HOST and SMTP_FROM in .env first.");
        }

        foreach (var (label, url) in new[] { ("Reset password page URL", input.ResetPasswordUrl), ("Email confirmation redirection URL", input.ConfirmationRedirectUrl) })
        {
            if (!string.IsNullOrWhiteSpace(url) && !IsHttpUrl(url))
            {
                errors.Add($"{label} must be an absolute http(s) address, or empty for the built-in page.");
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var values = new Dictionary<string, string>
        {
            [SignUpsKey] = input.SignUps ? "true" : "false",
            [DefaultRoleKey] = input.DefaultRoleId?.ToString() ?? string.Empty,
            [EmailConfirmationKey] = input.EmailConfirmation ? "true" : "false",
            [ResetPasswordUrlKey] = input.ResetPasswordUrl?.Trim() ?? string.Empty,
            [ConfirmationRedirectUrlKey] = input.ConfirmationRedirectUrl?.Trim() ?? string.Empty,
        };
        var keys = Keys.ToList();
        var rows = await _db.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            var row = rows.FirstOrDefault(r => r.Key == key);
            if (row is null)
            {
                _db.Settings.Add(new Setting { Key = key, Value = value });
            }
            else
            {
                row.Value = value;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Advanced User Settings",
            details: $"Sign-ups {(input.SignUps ? "on" : "off")}, email confirmation {(input.EmailConfirmation ? "on" : "off")}", cancellationToken: ct);
        return errors;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
