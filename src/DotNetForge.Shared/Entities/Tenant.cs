using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A tenant (site). The tenant is resolved first in the request pipeline (by domain, subdomain,
/// or path prefix) and every scoped entity is filtered by <see cref="Id"/> (multi_tenancy.md).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>URL-safe identifier used for path-prefix / [tenantSlug] resolution. Globally unique.</summary>
    public string Slug { get; set; } = string.Empty;

    public string? PrimaryDomain { get; set; }

    /// <summary>Additional subdomains mapped to this tenant, stored comma-separated.</summary>
    public string? Subdomains { get; set; }

    public string? PathPrefix { get; set; }

    public string DefaultLocale { get; set; } = "en";

    public string DefaultTheme { get; set; } = "dotnetforge.theme.default";

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}
