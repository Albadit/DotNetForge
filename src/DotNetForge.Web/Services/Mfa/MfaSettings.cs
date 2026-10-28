using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetForge.Web.Services.Mfa;

/// <summary>
/// The Two-factor authentication settings (.docs/pages/mfa.md): which second factors users may set up and who must
/// have one. Email codes count as on only while SMTP is configured, SMS codes only while Twilio is
/// (<see cref="MfaSettingsService.GetAsync"/>).
/// </summary>
public sealed record MfaSettings(bool AuthenticatorApp, bool Passkeys, bool Email, bool Sms, MfaRequirement Requirement)
{
    public static readonly MfaSettings Default = new(true, true, false, false, MfaRequirement.Optional);

    public bool Allows(MfaMethodType type) => type switch
    {
        MfaMethodType.AuthenticatorApp => AuthenticatorApp,
        MfaMethodType.Passkey => Passkeys,
        MfaMethodType.Email => Email,
        MfaMethodType.Sms => Sms,
        _ => false,
    };

    public bool AnyAllowed => AuthenticatorApp || Passkeys || Email || Sms;
}

/// <summary>
/// Reads and saves <see cref="MfaSettings"/> as <c>Settings</c> rows (keys <c>mfa.*</c>; the Settings screen refuses
/// them, so their checks can't be skipped).
/// </summary>
public sealed class MfaSettingsService
{
    public const string KeyPrefix = "mfa.";
    private const string AuthenticatorAppKey = "mfa.authenticatorApp";
    private const string PasskeysKey = "mfa.passkeys";
    private const string EmailKey = "mfa.email";
    private const string SmsKey = "mfa.sms";
    private const string RequirementKey = "mfa.requirement";

    private static readonly string[] Keys = { AuthenticatorAppKey, PasskeysKey, EmailKey, SmsKey, RequirementKey };
    private const string RequiredCacheKey = "dnf:mfa:required";
    private static readonly TimeSpan RequiredCacheLifetime = TimeSpan.FromSeconds(30);

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IEmailSettingsSource _email;
    private readonly AppEnvironment _env;
    private readonly IMemoryCache _cache;

    public MfaSettingsService(DotNetForgeDbContext db, IAuditService audit, IEmailSettingsSource email, AppEnvironment env, IMemoryCache cache)
    {
        _db = db;
        _audit = audit;
        _email = email;
        _env = env;
        _cache = cache;
    }

    /// <summary>
    /// Whether admin users must have a second factor - and can set one up (a method is on). Checked on every admin
    /// request, so remembered for 30 s (cleared on save).
    /// </summary>
    public async Task<bool> IsRequiredAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(RequiredCacheKey, out bool required))
        {
            return required;
        }

        var settings = await GetAsync(ct);
        required = settings.Requirement == MfaRequirement.AdminUsers && settings.AnyAllowed;
        _cache.Set(RequiredCacheKey, required, RequiredCacheLifetime);
        return required;
    }

    /// <summary>Whether email codes can be sent (SMTP configured).</summary>
    public async Task<bool> EmailAvailableAsync(CancellationToken ct = default) => (await _email.GetAsync(ct)).IsConfigured;

    /// <summary>Whether SMS codes can be sent (<c>TWILIO_*</c> configured).</summary>
    public bool SmsAvailable => _env.Sms.IsConfigured;

    /// <summary>The settings as they apply: a method whose service is missing counts as off.</summary>
    public async Task<MfaSettings> GetAsync(CancellationToken ct = default)
    {
        var stored = await GetStoredAsync(ct);
        return stored with
        {
            Email = stored.Email && await EmailAvailableAsync(ct),
            Sms = stored.Sms && SmsAvailable,
        };
    }

    /// <summary>The settings as saved (the screen shows these, so a choice survives while SMTP or Twilio is missing).</summary>
    public async Task<MfaSettings> GetStoredAsync(CancellationToken ct = default)
    {
        var keys = Keys.ToList();
        var values = await _db.Settings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        bool Flag(string key, bool fallback) => values.TryGetValue(key, out var v) && v is "true" or "false" ? v == "true" : fallback;
        var d = MfaSettings.Default;
        return new MfaSettings(
            Flag(AuthenticatorAppKey, d.AuthenticatorApp),
            Flag(PasskeysKey, d.Passkeys),
            Flag(EmailKey, d.Email),
            Flag(SmsKey, d.Sms),
            values.TryGetValue(RequirementKey, out var r) && Enum.TryParse<MfaRequirement>(r, out var requirement) && Enum.IsDefined(requirement)
                ? requirement
                : d.Requirement);
    }

    /// <summary>Validates and saves; returns the errors (nothing is saved when there are any).</summary>
    public async Task<List<string>> SaveAsync(MfaSettings input, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var stored = await GetStoredAsync(ct);
        if (input.Email && !stored.Email && !await EmailAvailableAsync(ct))
        {
            errors.Add("Email codes need email: set SMTP_HOST and SMTP_FROM in .env first.");
        }

        if (input.Sms && !stored.Sms && !SmsAvailable)
        {
            errors.Add("SMS codes need Twilio: set TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN and TWILIO_FROM in .env first.");
        }

        if (input.Requirement != MfaRequirement.Optional && !input.AuthenticatorApp && !input.Passkeys && !input.Email && !input.Sms)
        {
            errors.Add("Turn on at least one method before requiring two-factor authentication.");
        }

        if (!Enum.IsDefined(input.Requirement))
        {
            errors.Add("Choose who must use two-factor authentication.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var values = new Dictionary<string, string>
        {
            [AuthenticatorAppKey] = input.AuthenticatorApp ? "true" : "false",
            [PasskeysKey] = input.Passkeys ? "true" : "false",
            [EmailKey] = input.Email ? "true" : "false",
            [SmsKey] = input.Sms ? "true" : "false",
            [RequirementKey] = input.Requirement.ToString(),
        };
        var keys = Keys.ToList();
        var rows = await _db.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        foreach (var (key, value) in values)
        {
            if (rows.FirstOrDefault(r => r.Key == key) is { } row)
            {
                row.Value = value;
            }
            else
            {
                _db.Settings.Add(new Setting { Key = key, Value = value });
            }
        }

        await _db.SaveChangesAsync(ct);
        _cache.Remove(RequiredCacheKey);
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Two-factor authentication", cancellationToken: ct);
        return errors;
    }
}
