using System.Security.Cryptography;
using DotNetForge.Data;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>An environment variable as the list shows it; <see cref="Value"/> is null for a secret.</summary>
public sealed record EnvironmentVariableRow(
    Guid Id, string Key, string Environment, bool IsSecret, string? Value, string? Note, DateTime UpdatedDate,
    string? UpdatedBy, EnvironmentVariableService.ManagedScreen? ManagedBy);

/// <summary>The add/edit form of an environment variable.</summary>
public sealed class EnvironmentVariableInput
{
    public Guid? Id { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
    public bool IsSecret { get; set; }
    public string? Environment { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Settings → Environment Variables (.docs/pages/environment-variables.md): the rows of the <see cref="Setting"/> table
/// as typed variables - a secret (encrypted with Data Protection, never shown again) or config - for all environments or
/// one, with a note and who changed it last. Keys owned by another screen (sign-up, media, MFA, backups, admin language)
/// are listed but changed only there. <see cref="GetAsync"/> answers extensions (<see cref="IEnvironmentVariables"/>):
/// the running environment's value, else the all-environments one.
/// </summary>
public sealed class EnvironmentVariableService : IEnvironmentVariables
{
    public const int MaxKeyLength = 200;
    public const int MaxValueLength = 4000;
    public const int MaxNoteLength = 500;

    /// <summary>The environments a value can be for; the empty one is "all environments".</summary>
    public static readonly IReadOnlyList<string> Environments = new[] { string.Empty, AdminText.Key("Development"), AdminText.Key("Staging"), AdminText.Key("Production") };

    /// <summary>A screen that owns some keys: their rules live there.</summary>
    public sealed record ManagedScreen(string Prefix, string Name, string Href);

    /// <summary>The keys other screens own (by prefix); this screen lists them but sends edits there.</summary>
    public static readonly IReadOnlyList<ManagedScreen> ManagedScreens = new[]
    {
        new ManagedScreen("users.", AdminText.Key("Advanced Settings"), "/admin/advanced-settings"),
        new ManagedScreen(DatabaseTransferService.KeyPrefix, AdminText.Key("Data Management"), "/admin/data"),
        new ManagedScreen(MediaSettingsService.KeyPrefix, AdminText.Key("Media Library"), "/admin/media-settings"),
        new ManagedScreen(Mfa.MfaSettingsService.KeyPrefix, AdminText.Key("Two-factor authentication"), "/admin/mfa"),
        new ManagedScreen("i18n.", AdminText.Key("Internationalization"), "/admin/internationalization"),
    };

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDataProtector _protector;

    public EnvironmentVariableService(DotNetForgeDbContext db, IAuditService audit, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _audit = audit;
        _protector = dataProtection.CreateProtector("DotNetForge.EnvironmentVariables");
    }

    /// <summary>The screen that owns <paramref name="key"/>, or null.</summary>
    public static ManagedScreen? ManagedBy(string key) =>
        ManagedScreens.FirstOrDefault(m => key.StartsWith(m.Prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every variable of every environment, by key then environment (all environments first).</summary>
    public async Task<IReadOnlyList<EnvironmentVariableRow>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Settings.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(s => s.Key).ThenBy(s => s.Environment)
            .ToListAsync(ct);
        var editorIds = rows.Where(r => r.UpdatedById != null).Select(r => r.UpdatedById!.Value).Distinct().ToList();
        var editors = await _db.Users.AsNoTracking().Where(u => editorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(r => new EnvironmentVariableRow(r.Id, r.Key, r.Environment, r.IsSecret, r.IsSecret ? null : r.Value, r.Note,
            r.UpdatedDate, r.UpdatedById is { } id ? editors.GetValueOrDefault(id) : null, ManagedBy(r.Key))).ToList();
    }

    /// <summary>One variable for its edit form (a secret without its value), or null.</summary>
    public async Task<EnvironmentVariableInput?> FindAsync(Guid id, CancellationToken ct = default) =>
        await _db.Settings.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == id)
            .Select(s => new EnvironmentVariableInput
            {
                Id = s.Id,
                Key = s.Key,
                Value = s.IsSecret ? null : s.Value,
                IsSecret = s.IsSecret,
                Environment = s.Environment,
                Note = s.Note,
            })
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Adds a variable (<see cref="EnvironmentVariableInput.Id"/> null) or changes one - its key stays, its type,
    /// environment, value and note may change; an empty value keeps a secret's current one. Returns an error message,
    /// or null when saved (and audited, never with the value).
    /// </summary>
    public async Task<string?> SaveAsync(EnvironmentVariableInput input, Guid? userId, CancellationToken ct = default)
    {
        var environment = input.Environment?.Trim() ?? string.Empty;
        if (!Environments.Contains(environment, StringComparer.Ordinal))
        {
            return "Choose one of the environments.";
        }

        if (input.Value?.Length > MaxValueLength || input.Note?.Trim().Length > MaxNoteLength)
        {
            return $"Values are limited to {MaxValueLength} characters and notes to {MaxNoteLength}.";
        }

        Setting? row = null;
        if (input.Id is { } id)
        {
            row = await _db.Settings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row is null)
            {
                return "The environment variable no longer exists.";
            }
        }

        var key = row?.Key ?? input.Key?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            return "Key is required.";
        }

        if (key.Length > MaxKeyLength || key.Any(char.IsWhiteSpace))
        {
            return $"A key has at most {MaxKeyLength} characters and no spaces.";
        }

        if (ManagedBy(key) is { } managed)
        {
            return $"'{key}' is managed on {managed.Name}; change it there.";
        }

        if (await _db.Settings.IgnoreQueryFilters().AnyAsync(s => s.Key == key && s.Environment == environment && s.Id != input.Id, ct))
        {
            return environment.Length == 0
                ? $"'{key}' already has a value for all environments."
                : $"'{key}' already has a value for {environment}.";
        }

        var keepSecret = row is { IsSecret: true } && input.IsSecret && string.IsNullOrEmpty(input.Value);
        if (row is { IsSecret: true } && !input.IsSecret && string.IsNullOrEmpty(input.Value))
        {
            return "A secret's value can't be shown, so enter the value again to make it a config variable.";
        }

        var created = row is null;
        if (row is null)
        {
            row = new Setting { Key = key };
            _db.Settings.Add(row);
        }

        if (!keepSecret)
        {
            row.Value = input.IsSecret && !string.IsNullOrEmpty(input.Value) ? _protector.Protect(input.Value) : input.Value;
        }

        row.IsSecret = input.IsSecret;
        row.Environment = environment;
        row.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        row.UpdatedDate = DateTime.UtcNow;
        row.UpdatedById = userId;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return $"'{key}' was just added for this environment by someone else.";
        }

        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", key, key,
            details: $"{(created ? "Added" : "Updated")} - {(row.IsSecret ? "secret" : "config")}, {Describe(environment)}", cancellationToken: ct);
        return null;
    }

    /// <summary>Deletes a variable (not one another screen owns); returns an error message, or null when deleted (and audited).</summary>
    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Settings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
        {
            return "The environment variable no longer exists.";
        }

        if (ManagedBy(row.Key) is { } managed)
        {
            return $"'{row.Key}' is managed on {managed.Name}; change it there.";
        }

        _db.Settings.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.SettingsDeleted, "Setting", row.Key, row.Key, details: Describe(row.Environment), cancellationToken: ct);
        return null;
    }

    /// <summary>The value of <paramref name="key"/> in the running environment (its own value, else all environments'), decrypted.</summary>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Settings.AsNoTracking().Where(s => s.Key == key).ToListAsync(cancellationToken);
        return Effective(rows).FirstOrDefault() is { } row ? Reveal(row) : null;
    }

    /// <summary>
    /// The running environment's variables, one per key (its own value over all environments'): config values as they
    /// are, secrets without their value - for the API.
    /// </summary>
    public async Task<IReadOnlyList<(string Key, string? Value, bool IsSecret)>> EffectiveAsync(CancellationToken ct = default)
    {
        var rows = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct);
        return Effective(rows).Select(r => (r.Key, r.IsSecret ? null : r.Value, r.IsSecret)).ToList();
    }

    /// <summary>"All environments" or the environment's name, for the audit log.</summary>
    public static string Describe(string environment) => environment.Length == 0 ? "All environments" : environment;

    /// <summary>Per key, the row for the running environment over the one for all environments (the query filter left only those).</summary>
    private static IEnumerable<Setting> Effective(IEnumerable<Setting> rows) =>
        rows.GroupBy(r => r.Key, StringComparer.Ordinal).Select(g => g.OrderByDescending(r => r.Environment.Length).First());

    private string? Reveal(Setting row)
    {
        if (!row.IsSecret || string.IsNullOrEmpty(row.Value))
        {
            return row.Value;
        }

        try
        {
            return _protector.Unprotect(row.Value);
        }
        catch (CryptographicException)
        {
            // Encrypted by another site (an imported backup): it has to be entered again here.
            return null;
        }
    }
}
