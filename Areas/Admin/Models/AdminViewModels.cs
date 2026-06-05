using DotNetForge.Shared.Entities;

namespace DotNetForge.Web.Areas.Admin.Models;

/// <summary>Aggregated counts shown on the admin dashboard (dashboard.md).</summary>
public sealed class DashboardViewModel
{
    public string AppName { get; init; } = string.Empty;
    public int Users { get; init; }
    public int Roles { get; init; }
    public int Pages { get; init; }
    public int Media { get; init; }
    public int ApiTokens { get; init; }
    public int Webhooks { get; init; }
    public int Extensions { get; init; }
    public int AuditEntries { get; init; }
    public DateTime? InstalledAtUtc { get; init; }
    public string CmsVersion { get; init; } = "1.0.0";
}

/// <summary>A row on the Plugins page combining the DB record and on-disk discovery (extensions.md).</summary>
public sealed class PluginRowViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public bool ValidManifest { get; init; } = true;
    public string? Source { get; init; }
}

/// <summary>Shown once after a token is created - the plaintext is never retrievable again.</summary>
public sealed class CreatedTokenViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Plaintext { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
}

/// <summary>Placeholder content for foundation modules whose full UI is future work.</summary>
public sealed class ModulePlaceholderViewModel
{
    public string Title { get; init; } = string.Empty;
    public string SpecFile { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<ApiToken> Tokens { get; init; } = Array.Empty<ApiToken>();
}
