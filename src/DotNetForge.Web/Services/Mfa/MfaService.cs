using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QRCoder;

namespace DotNetForge.Web.Services.Mfa;

/// <summary>An authenticator app being set up: the secret (also shown as text), its QR code and the signed setup token.</summary>
public sealed record AuthenticatorSetup(string Secret, string QrCodeDataUri, string Token);

/// <summary>A started passkey ceremony: the options for <c>navigator.credentials</c> and the signed token that carries them back.</summary>
public sealed record PasskeyCeremony(string OptionsJson, string Token);

/// <summary>
/// Two-factor authentication (.docs/features/mfa.md): setting up, checking and removing a user's second factors -
/// authenticator app (TOTP), email codes, SMS codes and passkeys (WebAuthn) - and their recovery codes. Codes and
/// ceremonies travel in Data Protection tokens bound to the user and a short lifetime, so nothing pending is stored.
/// Setting up and removing write <c>user.mfa.*</c> audit entries.
/// </summary>
public sealed partial class MfaService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SetupLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PasskeyLifetime = TimeSpan.FromMinutes(5);
    public const int RecoveryCodeCount = 10;
    public const int MaxNameLength = 100;

    /// <summary>The order methods are offered at sign-in (the first one the user has is the default).</summary>
    public static readonly MfaMethodType[] Preference = { MfaMethodType.AuthenticatorApp, MfaMethodType.Passkey, MfaMethodType.Sms, MfaMethodType.Email };

    private static readonly TimeSpan EnrolledCacheLifetime = TimeSpan.FromSeconds(30);
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I

    private readonly DotNetForgeDbContext _db;
    private readonly MfaSettingsService _settings;
    private readonly ITimeLimitedDataProtector _tokens;
    private readonly IDataProtector _secrets;
    private readonly AccountService _accounts;
    private readonly ISmsSender _sms;
    private readonly AppEnvironment _env;
    private readonly IAuditService _audit;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MfaService> _logger;

    public MfaService(DotNetForgeDbContext db, MfaSettingsService settings, IDataProtectionProvider dataProtection, AccountService accounts,
        ISmsSender sms, AppEnvironment env, IAuditService audit, IMemoryCache cache, ILogger<MfaService> logger)
    {
        _db = db;
        _settings = settings;
        _tokens = dataProtection.CreateProtector("DotNetForge.Mfa.Tokens.v1").ToTimeLimitedDataProtector();
        _secrets = dataProtection.CreateProtector("DotNetForge.Mfa.Secrets.v1");
        _accounts = accounts;
        _sms = sms;
        _env = env;
        _audit = audit;
        _cache = cache;
        _logger = logger;
    }

    // ---- What a user has --------------------------------------------------------------------------------------------

    public Task<List<UserMfaMethod>> MethodsAsync(Guid userId, CancellationToken ct = default) =>
        _db.UserMfaMethods.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.CreatedDate).ToListAsync(ct);

    /// <summary>The user's methods of the types that are on (Two-factor authentication), in <see cref="Preference"/> order.</summary>
    public async Task<List<UserMfaMethod>> UsableMethodsAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        return (await MethodsAsync(userId, ct))
            .Where(m => settings.Allows(m.Type))
            .OrderBy(m => Array.IndexOf(Preference, m.Type))
            .ThenBy(m => m.CreatedDate)
            .ToList();
    }

    /// <summary>Whether sign-in needs a second factor: the user has at least one usable method.</summary>
    public async Task<bool> RequiresSecondFactorAsync(Guid userId, CancellationToken ct = default) =>
        (await UsableMethodsAsync(userId, ct)).Count > 0;

    /// <summary><see cref="RequiresSecondFactorAsync"/>, remembered for 30 s (checked on every admin request when it is required).</summary>
    public async Task<bool> IsEnrolledAsync(Guid userId, CancellationToken ct = default)
    {
        var key = EnrolledKey(userId);
        if (_cache.TryGetValue(key, out bool enrolled))
        {
            return enrolled;
        }

        enrolled = await RequiresSecondFactorAsync(userId, ct);
        _cache.Set(key, enrolled, EnrolledCacheLifetime);
        return enrolled;
    }

    public Task<int> RecoveryCodesLeftAsync(Guid userId, CancellationToken ct = default) =>
        _db.UserRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedDate == null, ct);

    // ---- Authenticator app ------------------------------------------------------------------------------------------

    public AuthenticatorSetup BeginAuthenticator(User user)
    {
        var secret = Totp.NewSecret();
        return Setup(user, secret, _tokens.Protect($"totp|{user.Id}|{secret}", SetupLifetime));
    }

    /// <summary>The setup of <paramref name="token"/> again (after a wrong code), so the app already scanned keeps working; null when expired.</summary>
    public AuthenticatorSetup? RebuildAuthenticator(User user, string? token) =>
        Read(token, "totp", user.Id) is [var secret] ? Setup(user, secret, token!) : null;

    private AuthenticatorSetup Setup(User user, string secret, string token)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(Totp.Uri(secret, _env.AppName, user.Email), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return new AuthenticatorSetup(secret, "data:image/png;base64," + Convert.ToBase64String(png), token);
    }

    /// <summary>Adds the authenticator app when <paramref name="code"/> matches the secret in <paramref name="token"/>; returns an error or null.</summary>
    public async Task<string?> CompleteAuthenticatorAsync(User user, string? token, string? code, CancellationToken ct = default)
    {
        if (Read(token, "totp", user.Id) is not [var secret])
        {
            return "The setup expired. Start again.";
        }

        if (Totp.Verify(secret, code, DateTimeOffset.UtcNow, lastStep: 0) is not { } step)
        {
            return "That code is not right. Check the time on your phone and enter the current code.";
        }

        await AddAsync(new UserMfaMethod
        {
            UserId = user.Id,
            Type = MfaMethodType.AuthenticatorApp,
            Name = "Authenticator app",
            Secret = _secrets.Protect(secret),
            Counter = step,
        }, ct);
        return null;
    }

    // ---- Email and SMS codes ----------------------------------------------------------------------------------------

    /// <summary>
    /// Sends a setup code to the account's email (<paramref name="type"/> Email) or to <paramref name="phone"/> (Sms);
    /// returns the token that checks it, or an error.
    /// </summary>
    public async Task<(string? Token, string? Error)> SendSetupCodeAsync(User user, MfaMethodType type, string? phone, CancellationToken ct = default)
    {
        string target;
        if (type == MfaMethodType.Sms)
        {
            if (NormalizePhone(phone) is not { } number)
            {
                return (null, "Enter the phone number with its country code, e.g. +41 79 123 45 67.");
            }

            target = number;
        }
        else
        {
            target = user.Email;
        }

        var code = NewCode();
        var sent = await SendCodeAsync(user, type, target, code, ct);
        return sent
            ? (_tokens.Protect($"setup|{user.Id}|{(int)type}|{target}|{Hash(code)}", CodeLifetime), null)
            : (null, type == MfaMethodType.Sms ? "The text message couldn't be sent. Check the number, or try again later." : "The email couldn't be sent. Try again later.");
    }

    /// <summary>Adds the email or SMS method once the code from <see cref="SendSetupCodeAsync"/> is entered.</summary>
    public async Task<string?> CompleteSetupCodeAsync(User user, string? token, string? code, CancellationToken ct = default)
    {
        if (Read(token, "setup", user.Id) is not [var typeText, var target, var hash] || !int.TryParse(typeText, out var typeValue))
        {
            return "The code expired. Send a new one.";
        }

        if (!FixedEquals(Hash(Totp.Normalize(code)), hash))
        {
            return "That code is not right.";
        }

        var type = (MfaMethodType)typeValue;
        var exists = await _db.UserMfaMethods.AnyAsync(m => m.UserId == user.Id && m.Type == type && (type != MfaMethodType.Sms || m.PhoneNumber == target), ct);
        if (!exists)
        {
            await AddAsync(new UserMfaMethod
            {
                UserId = user.Id,
                Type = type,
                Name = type == MfaMethodType.Sms ? MaskPhone(target) : "Email codes",
                PhoneNumber = type == MfaMethodType.Sms ? target : null,
            }, ct);
        }

        return null;
    }

    /// <summary>Sends a sign-in code for <paramref name="method"/> (email or SMS); the token goes into the pending sign-in.</summary>
    public async Task<string?> SendSignInCodeAsync(User user, UserMfaMethod method, CancellationToken ct = default)
    {
        var code = NewCode();
        var target = method.Type == MfaMethodType.Sms ? method.PhoneNumber! : user.Email;
        return await SendCodeAsync(user, method.Type, target, code, ct)
            ? _tokens.Protect($"signin|{user.Id}|{method.Id}|{Hash(code)}", CodeLifetime)
            : null;
    }

    public async Task<bool> VerifySignInCodeAsync(User user, UserMfaMethod method, string? token, string? code, CancellationToken ct = default)
    {
        if (Read(token, "signin", user.Id) is not [var methodId, var hash] || methodId != method.Id.ToString()
            || !FixedEquals(Hash(Totp.Normalize(code)), hash))
        {
            return false;
        }

        await MarkUsedAsync(method.Id, counter: null, ct);
        return true;
    }

    /// <summary>Checks a code against the user's authenticator apps; each code works once.</summary>
    public async Task<bool> VerifyAuthenticatorAsync(User user, string? code, CancellationToken ct = default)
    {
        var apps = await _db.UserMfaMethods.Where(m => m.UserId == user.Id && m.Type == MfaMethodType.AuthenticatorApp).ToListAsync(ct);
        foreach (var app in apps)
        {
            string secret;
            try
            {
                secret = _secrets.Unprotect(app.Secret!);
            }
            catch (CryptographicException)
            {
                continue; // came from another site's export: that app can't be checked here
            }

            if (Totp.Verify(secret, code, DateTimeOffset.UtcNow, app.Counter) is { } step)
            {
                await MarkUsedAsync(app.Id, step, ct);
                return true;
            }
        }

        return false;
    }

    // ---- Passkeys ---------------------------------------------------------------------------------------------------

    public async Task<PasskeyCeremony> BeginPasskeyRegistrationAsync(User user, CancellationToken ct = default)
    {
        var existing = await _db.UserMfaMethods.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Type == MfaMethodType.Passkey && m.CredentialId != null)
            .Select(m => m.CredentialId!).ToListAsync(ct);
        var options = Fido().RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = user.Id.ToByteArray(), Name = user.Email, DisplayName = user.DisplayName },
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Preferred,
                UserVerification = UserVerificationRequirement.Preferred,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = options.ToJson();
        return new PasskeyCeremony(json, _tokens.Protect($"passkey-new|{user.Id}|{json}", PasskeyLifetime));
    }

    /// <summary>Verifies the browser's new credential and stores it; returns an error or null.</summary>
    public async Task<string?> CompletePasskeyRegistrationAsync(User user, string? token, string? credentialJson, string? name, CancellationToken ct = default)
    {
        if (Read(token, "passkey-new", user.Id) is not [var optionsJson])
        {
            return "The passkey request expired. Try again.";
        }

        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(credentialJson ?? string.Empty)
                ?? throw new JsonException("Empty credential.");
            var credential = await Fido().MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = CredentialCreateOptions.FromJson(optionsJson),
                IsCredentialIdUniqueToUserCallback = async (p, c) => !await _db.UserMfaMethods.AnyAsync(m => m.CredentialId == p.CredentialId, c),
            }, ct);
            await AddAsync(new UserMfaMethod
            {
                UserId = user.Id,
                Type = MfaMethodType.Passkey,
                Name = string.IsNullOrWhiteSpace(name) ? "Passkey" : name.Trim()[..Math.Min(name.Trim().Length, MaxNameLength)],
                CredentialId = credential.Id,
                PublicKey = credential.PublicKey,
                Counter = credential.SignCount,
            }, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the browser (or someone else) sent didn't verify - never a server error.
            _logger.LogWarning("A passkey registration was refused: {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            return "The passkey couldn't be verified. Try again; the site must be opened at " + Origin + ".";
        }
    }

    public async Task<PasskeyCeremony?> BeginPasskeySignInAsync(User user, CancellationToken ct = default)
    {
        var credentials = await _db.UserMfaMethods.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Type == MfaMethodType.Passkey && m.CredentialId != null)
            .Select(m => m.CredentialId!).ToListAsync(ct);
        if (credentials.Count == 0)
        {
            return null;
        }

        var options = Fido().GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentials.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Preferred,
        });
        var json = options.ToJson();
        return new PasskeyCeremony(json, _tokens.Protect($"passkey|{user.Id}|{json}", PasskeyLifetime));
    }

    public async Task<bool> VerifyPasskeyAsync(User user, string? token, string? assertionJson, CancellationToken ct = default)
    {
        if (Read(token, "passkey", user.Id) is not [var optionsJson])
        {
            return false;
        }

        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson ?? string.Empty)
                ?? throw new JsonException("Empty assertion.");
            var method = await _db.UserMfaMethods.FirstOrDefaultAsync(
                m => m.UserId == user.Id && m.Type == MfaMethodType.Passkey && m.CredentialId == response.RawId, ct);
            if (method is null)
            {
                return false;
            }

            var result = await Fido().MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = AssertionOptions.FromJson(optionsJson),
                StoredPublicKey = method.PublicKey!,
                StoredSignatureCounter = (uint)method.Counter,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) =>
                    Task.FromResult(p.UserHandle is not { Length: 16 } handle || new Guid(handle) == user.Id),
            }, ct);
            await MarkUsedAsync(method.Id, result.SignCount, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("A passkey sign-in was refused: {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    // ---- Recovery codes ---------------------------------------------------------------------------------------------

    /// <summary>New recovery codes (the old ones stop working); shown once, stored as hashes.</summary>
    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(User user, CancellationToken ct = default)
    {
        await _db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        _db.UserRecoveryCodes.AddRange(codes.Select(c => new UserRecoveryCode { UserId = user.Id, CodeHash = Hash(NormalizeRecovery(c)) }));
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RecoveryCodesGenerated, "User", user.Id.ToString(), user.Email, cancellationToken: ct);
        return codes;
    }

    /// <summary>Uses up a recovery code; false when it is wrong or already used.</summary>
    public async Task<bool> UseRecoveryCodeAsync(Guid userId, string? code, CancellationToken ct = default)
    {
        var hash = Hash(NormalizeRecovery(code));
        var used = await _db.UserRecoveryCodes
            .Where(c => c.UserId == userId && c.CodeHash == hash && c.UsedDate == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedDate, DateTime.UtcNow), ct);
        return used == 1;
    }

    // ---- Removing ---------------------------------------------------------------------------------------------------

    /// <summary>Removes one of the user's methods; with the last one the recovery codes go too.</summary>
    public async Task<bool> RemoveAsync(User user, Guid methodId, CancellationToken ct = default)
    {
        var method = await _db.UserMfaMethods.FirstOrDefaultAsync(m => m.Id == methodId && m.UserId == user.Id, ct);
        if (method is null)
        {
            return false;
        }

        _db.UserMfaMethods.Remove(method);
        await _db.SaveChangesAsync(ct);
        if (!await _db.UserMfaMethods.AnyAsync(m => m.UserId == user.Id, ct))
        {
            await _db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        }

        _cache.Remove(EnrolledKey(user.Id));
        await _audit.LogAsync(AuditActions.MfaMethodRemoved, "User", user.Id.ToString(), user.Email, details: $"{Label(method.Type)}: {method.Name}", cancellationToken: ct);
        return true;
    }

    /// <summary>An administrator removes every second factor of a user (lost phone); returns how many there were.</summary>
    public async Task<int> ResetAsync(User user, CancellationToken ct = default)
    {
        var removed = await _db.UserMfaMethods.Where(m => m.UserId == user.Id).ExecuteDeleteAsync(ct);
        await _db.UserRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
        _cache.Remove(EnrolledKey(user.Id));
        await _audit.LogAsync(AuditActions.MfaReset, "User", user.Id.ToString(), user.Email, details: $"{removed} method(s) removed", cancellationToken: ct);
        return removed;
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------------

    public static string Label(MfaMethodType type) => type switch
    {
        MfaMethodType.AuthenticatorApp => "Authenticator app",
        MfaMethodType.Passkey => "Passkey",
        MfaMethodType.Email => "Email code",
        MfaMethodType.Sms => "SMS code",
        _ => type.ToString(),
    };

    /// <summary>"+41791234567" from "+41 79 123 45 67" / "0041 79 ..."; null when it isn't an international number.</summary>
    public static string? NormalizePhone(string? phone)
    {
        var text = (phone ?? string.Empty).Trim();
        if (text.StartsWith("00", StringComparison.Ordinal))
        {
            text = "+" + text[2..];
        }

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return text.StartsWith('+') && PhonePattern().IsMatch(digits) ? "+" + digits : null;
    }

    /// <summary>"+41 *** *** 67" - what lists show of a number.</summary>
    public static string MaskPhone(string phone) => phone.Length <= 6 ? phone : phone[..3] + " *** *** " + phone[^2..];

    private async Task AddAsync(UserMfaMethod method, CancellationToken ct)
    {
        _db.UserMfaMethods.Add(method);
        await _db.SaveChangesAsync(ct);
        _cache.Remove(EnrolledKey(method.UserId));
        var user = await _db.Users.AsNoTracking().Where(u => u.Id == method.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        await _audit.LogAsync(AuditActions.MfaMethodAdded, "User", method.UserId.ToString(), user, details: $"{Label(method.Type)}: {method.Name}", cancellationToken: ct);
    }

    private async Task MarkUsedAsync(Guid methodId, long? counter, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await (counter is { } value
            ? _db.UserMfaMethods.Where(m => m.Id == methodId).ExecuteUpdateAsync(s => s.SetProperty(m => m.LastUsedDate, now).SetProperty(m => m.Counter, value), ct)
            : _db.UserMfaMethods.Where(m => m.Id == methodId).ExecuteUpdateAsync(s => s.SetProperty(m => m.LastUsedDate, now), ct));
    }

    private async Task<bool> SendCodeAsync(User user, MfaMethodType type, string target, string code, CancellationToken ct)
    {
        if (type == MfaMethodType.Email)
        {
            return await _accounts.SendSignInCodeAsync(user, code, CodeLifetime, ct);
        }

        try
        {
            await _sms.SendAsync(target, $"{code} is your {_env.AppName} sign-in code. It works for {CodeLifetime.TotalMinutes:0} minutes.", ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The error type only: the number is personal data.
            _logger.LogError("Sending an SMS code failed with {ErrorType}: {Message}", ex.GetType().Name, ex.Message);
            return false;
        }
    }

    /// <summary>WebAuthn for this site: the relying party is APP_URL's host, and only APP_URL's origin is accepted.</summary>
    private Fido2 Fido() => new(new Fido2Configuration
    {
        RPID = new Uri(_env.AppUrl).Host,
        RPName = _env.AppName,
        Origins = new HashSet<string> { Origin },
    }, null!);

    private string Origin => new Uri(_env.AppUrl).GetLeftPart(UriPartial.Authority);

    private static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    private static string NewRecoveryCode()
    {
        var chars = Enumerable.Range(0, 10).Select(_ => RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]).ToArray();
        return new string(chars, 0, 5) + "-" + new string(chars, 5, 5);
    }

    private static string NormalizeRecovery(string? code) =>
        new string((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>The parts after purpose and user of a valid, unexpired token for <paramref name="userId"/>, or null.</summary>
    private string[]? Read(string? token, string purpose, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var text = _tokens.Unprotect(token);
            var prefix = $"{purpose}|{userId}|";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            var rest = text[prefix.Length..];
            // Passkey options are JSON (may contain '|'): one part. Codes: split.
            return purpose.StartsWith("passkey", StringComparison.Ordinal) ? new[] { rest } : rest.Split('|');
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string EnrolledKey(Guid userId) => "dnf:mfa:enrolled:" + userId;

    [GeneratedRegex("^[1-9][0-9]{6,14}$")]
    private static partial Regex PhonePattern();
}
