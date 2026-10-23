using DotNetForge.Data;
using DotNetForge.Extensions;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Enables and disables extensions (.docs/features/extensions.md#enable-and-disable): the rules (valid manifest, code
/// loaded, dependencies enabled, no enabled dependents), the <c>InstalledExtensions</c> record and the audit entry.
/// The Plugins screen only maps the returned error to the UI.
/// </summary>
public sealed class ExtensionLifecycleService
{
    /// <summary>Serializes changes on this instance so two clicks can't interleave their dependency checks.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly DotNetForgeDbContext _db;
    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionState _state;
    private readonly IAuditService _audit;

    public ExtensionLifecycleService(DotNetForgeDbContext db, ExtensionCatalog catalog, ExtensionState state, IAuditService audit)
    {
        _db = db;
        _catalog = catalog;
        _state = state;
        _audit = audit;
    }

    /// <summary>Enables the extension; returns an error message, or <c>null</c> on success.</summary>
    public async Task<string?> EnableAsync(string id, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var extension = _catalog.Installed()
                .FirstOrDefault(d => string.Equals(d.Manifest!.Id, id, StringComparison.OrdinalIgnoreCase));
            if (extension is null)
            {
                return "No extension with a valid manifest has this id.";
            }

            var manifest = extension.Manifest!;
            if (_catalog.Problem(extension) is { } problem)
            {
                return $"{manifest.Name} can't be enabled: {problem}";
            }

            var enabled = await _state.GetEnabledAsync(cancellationToken);
            var missing = manifest.Dependencies.Where(d => !enabled.Contains(d)).ToList();
            if (missing.Count > 0)
            {
                return $"{manifest.Name} needs {string.Join(", ", missing)} - enable {(missing.Count == 1 ? "it" : "them")} first.";
            }

            var row = await _db.InstalledExtensions.FirstOrDefaultAsync(x => x.Id == manifest.Id, cancellationToken);
            if (row is null)
            {
                row = new InstalledExtension { Id = manifest.Id!, InstalledDate = DateTime.UtcNow };
                _db.InstalledExtensions.Add(row);
            }

            row.Name = Truncate(manifest.Name!, 200);
            row.Description = Truncate(manifest.Description!, 1000);
            row.Version = Truncate(manifest.Version!, 50);
            row.Type = Enum.TryParse<ExtensionType>(manifest.Type, ignoreCase: true, out var type) ? type : ExtensionType.Plugin;
            row.Author = Truncate(manifest.Author!, 200);
            row.PermissionsCsv = Truncate(string.Join(',', manifest.Permissions ?? Array.Empty<string>()), 2000);
            row.Website = manifest.Website;
            row.License = manifest.License;
            row.Status = ExtensionStatus.Enabled;
            await _db.SaveChangesAsync(cancellationToken);

            _state.Invalidate();
            await _audit.LogAsync(AuditActions.PluginEnabled, "Extension", manifest.Id, manifest.Name,
                cancellationToken: cancellationToken);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Disables the extension; returns an error message, or <c>null</c> on success.</summary>
    public async Task<string?> DisableAsync(string id, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var row = await _db.InstalledExtensions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (row is null || row.Status != ExtensionStatus.Enabled)
            {
                return "This extension is not enabled.";
            }

            var enabled = await _state.GetEnabledAsync(cancellationToken);
            var dependents = _catalog.Installed()
                .Where(d => enabled.Contains(d.Manifest!.Id!) &&
                            d.Manifest.Dependencies.Contains(row.Id, StringComparer.OrdinalIgnoreCase))
                .Select(d => d.Manifest!.Name)
                .ToList();
            if (dependents.Count > 0)
            {
                return $"{string.Join(", ", dependents)} {(dependents.Count == 1 ? "depends" : "depend")} on {row.Name} - disable {(dependents.Count == 1 ? "it" : "them")} first.";
            }

            row.Status = ExtensionStatus.Disabled;
            await _db.SaveChangesAsync(cancellationToken);

            _state.Invalidate();
            await _audit.LogAsync(AuditActions.PluginDisabled, "Extension", row.Id, row.Name,
                cancellationToken: cancellationToken);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
