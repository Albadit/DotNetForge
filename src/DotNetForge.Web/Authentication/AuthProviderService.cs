using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Authentication;

/// <summary>What the edit form of a provider posts. Empty secret fields keep the saved secret.</summary>
public sealed class ProviderInput
{
    public bool Enabled { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TeamId { get; set; }
    public string? KeyId { get; set; }
    public string? PrivateKey { get; set; }
    public string? ServerUrl { get; set; }
    public string? Tenant { get; set; }
}

/// <summary>
/// Reads and saves the built-in providers' settings for Users &amp; Permissions Plugin → Providers
/// (.docs/pages/providers.md). Secrets are write-only: stored protected, never shown again, kept when the field is
/// left empty. A save writes the <c>provider.updated</c> audit entry and refreshes the sign-in schemes at once.
/// </summary>
public sealed partial class AuthProviderService
{
    public const int MaxClientIdLength = 500;
    public const int MaxSecretLength = 2000;
    public const int MaxPrivateKeyLength = 4000;

    private readonly DotNetForgeDbContext _db;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly OAuthProviderRegistry _registry;
    private readonly IAuditService _audit;

    public AuthProviderService(DotNetForgeDbContext db, IDataProtectionProvider dataProtection, OAuthProviderRegistry registry, IAuditService audit)
    {
        _db = db;
        _dataProtection = dataProtection;
        _registry = registry;
        _audit = audit;
    }

    /// <summary>Every provider row with its readable settings, keyed by provider key.</summary>
    public async Task<Dictionary<string, (AuthProvider Row, ProviderSettings Settings, bool Unreadable)>> AllAsync(CancellationToken ct = default)
    {
        var rows = await _db.AuthProviders.AsNoTracking().ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r =>
        {
            var settings = ProviderSettings.Read(r.SettingsJson, _dataProtection, out var unreadable);
            return (r, settings, unreadable);
        }, StringComparer.Ordinal);
    }

    /// <summary>Validates and saves; returns the errors (nothing is saved when there are any).</summary>
    public async Task<List<string>> SaveAsync(OAuthProviderDefinition provider, ProviderInput input, CancellationToken ct = default)
    {
        var row = await _db.AuthProviders.FirstOrDefaultAsync(p => p.Key == provider.Key, ct);
        if (row is null)
        {
            return new List<string> { $"{provider.Name} is not set up in this database yet; restart the app." };
        }

        var saved = ProviderSettings.Read(row.SettingsJson, _dataProtection, out _);
        var errors = new List<string>();
        string? Text(string? value, int max, string label)
        {
            var trimmed = value?.Trim();
            if (trimmed?.Length > max)
            {
                errors.Add($"{label} can have at most {max} characters.");
            }

            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        bool Uses(ProviderField field) => provider.Fields.Contains(field);
        var next = new ProviderSettings(
            Uses(ProviderField.ClientId) ? Text(input.ClientId, MaxClientIdLength, provider.SignedClientSecret ? "Services ID" : "Client ID") : null,
            Uses(ProviderField.ClientSecret) ? Text(input.ClientSecret, MaxSecretLength, "Client secret") ?? saved.ClientSecret : null,
            Uses(ProviderField.TeamId) ? Text(input.TeamId, 20, "Team ID") : null,
            Uses(ProviderField.KeyId) ? Text(input.KeyId, 20, "Key ID") : null,
            Uses(ProviderField.PrivateKey) ? Text(input.PrivateKey, MaxPrivateKeyLength, "Private key") ?? saved.PrivateKey : null,
            Uses(ProviderField.ServerUrl) ? Text(input.ServerUrl, 200, "Server")?.TrimEnd('/') : null,
            Uses(ProviderField.Tenant) ? Text(input.Tenant, 100, "Tenant") : null);

        foreach (var (value, label) in new[] { (next.TeamId, "Team ID"), (next.KeyId, "Key ID") })
        {
            if (value is not null && !AppleIdPattern().IsMatch(value))
            {
                errors.Add($"{label} is the 10-character code from your Apple developer account, e.g. ABCDE12345.");
            }
        }

        if (!string.IsNullOrEmpty(input.PrivateKey) && !IsSigningKey(input.PrivateKey))
        {
            errors.Add("The private key must be the contents of the .p8 file Apple gave you (-----BEGIN PRIVATE KEY----- ...).");
        }

        if (next.ServerUrl is { } server && (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Query.Length > 0))
        {
            errors.Add("Server must be an https address, e.g. https://gitlab.example.com.");
        }

        if (next.Tenant is { } tenant && !TenantPattern().IsMatch(tenant))
        {
            errors.Add("Tenant must be common, consumers, organizations, a tenant ID or a domain.");
        }

        if (input.Enabled && errors.Count == 0 && !next.IsComplete(provider))
        {
            errors.Add($"Fill in every field before turning {provider.Name} on.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var changes = new List<string>();
        if (row.Enabled != input.Enabled)
        {
            changes.Add(input.Enabled ? "Enabled" : "Disabled");
        }

        var changedFields = provider.Fields.Where(f => saved[f] != next[f]).Select(f => f.ToString()).ToList();
        if (changedFields.Count > 0)
        {
            // Field names only - never values.
            changes.Add("Changed: " + string.Join(", ", changedFields));
        }

        if (changes.Count == 0)
        {
            return errors;
        }

        row.Enabled = input.Enabled;
        row.SettingsJson = next.Write(_dataProtection);
        row.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _registry.Invalidate();
        await _audit.LogAsync(AuditActions.ProviderUpdated, nameof(AuthProvider), row.Id.ToString(), provider.Name,
            details: string.Join("; ", changes), cancellationToken: ct);
        return errors;
    }

    private static bool IsSigningKey(string pem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem.Trim());
            return key.KeySize == 256;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9]{10}$")]
    private static partial Regex AppleIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,99}$")]
    private static partial Regex TenantPattern();
}
