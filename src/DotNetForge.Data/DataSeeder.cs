using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Seeds the baseline data every fresh database needs: the default tenant, the six built-in roles
/// with their default permission grants, the default authentication providers, and the single
/// system-state row. Seeding is idempotent and runs after migrations, before install detection.
/// </summary>
public static class DataSeeder
{
    public const string DefaultTenantSlug = "default";

    private static readonly (string Name, string Description)[] RoleDefinitions =
    {
        (Roles.SuperAdmin, "Full, unrestricted access to every permission area, tenant, and system function."),
        (Roles.Admin, "Manages content, media, users, roles, settings, API tokens, and webhooks within their tenant."),
        (Roles.Editor, "Creates, edits, and publishes all content and media within their tenant."),
        (Roles.Author, "Can manage the content they have created."),
        (Roles.Authenticated, "Default role for any signed-in end user."),
        (Roles.Public, "The implicit role for unauthenticated visitors. Read-only public content."),
    };

    // The default authentication providers (authentication.md). Email is built-in and enabled.
    private static readonly string[] OAuthProviders =
    {
        "Auth0", "CAS", "Cognito", "Discord", "Facebook", "GitHub", "Google", "Instagram",
        "Keycloak", "LinkedIn", "Microsoft", "Patreon", "Reddit", "Twitch", "Twitter/X", "VK",
    };

    public static async Task SeedAsync(DotNetForgeDbContext db, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == DefaultTenantSlug, cancellationToken);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = "Default",
                Slug = DefaultTenantSlug,
                DefaultLocale = "en",
                DefaultTheme = "dotnetforge.theme.default",
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        await SeedRolesAsync(db, tenant.Id, cancellationToken);
        await SeedPagesAsync(db, tenant.Id, cancellationToken);
        await SeedAuthProvidersAsync(db, cancellationToken);
        await EnsureSystemStateAsync(db, cancellationToken);
    }

    /// <summary>Seeds a small starter page tree so the Content Manager has something to show on a fresh install.</summary>
    private static async Task SeedPagesAsync(DotNetForgeDbContext db, Guid tenantId, CancellationToken ct)
    {
        if (await db.Pages.AnyAsync(p => p.TenantId == tenantId, ct))
        {
            return;
        }

        var home = new Page { TenantId = tenantId, Slug = "/", Title = "Home", Published = true, DisplayInMenu = true, SortOrder = 0 };
        var about = new Page { TenantId = tenantId, Slug = "about", Title = "About", Published = true, DisplayInMenu = true, SortOrder = 1 };
        var news = new Page { TenantId = tenantId, Slug = "news", Title = "News", Published = true, DisplayInMenu = true, SortOrder = 2 };
        var contact = new Page { TenantId = tenantId, Slug = "contact", Title = "Contact", DisplayInMenu = true, SortOrder = 3 };
        db.Pages.AddRange(home, about, news, contact);

        // Children of About.
        db.Pages.AddRange(
            new Page { TenantId = tenantId, Slug = "team", Title = "Team", Published = true, SortOrder = 0, ParentPageId = about.Id },
            new Page { TenantId = tenantId, Slug = "history", Title = "History", SortOrder = 1, ParentPageId = about.Id });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(DotNetForgeDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Roles
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.Name)
            .ToListAsync(ct);

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, description) in RoleDefinitions)
        {
            if (existingSet.Contains(name))
            {
                continue;
            }

            var role = new Role
            {
                Name = name,
                Description = description,
                IsBuiltIn = true,
                TenantId = tenantId,
            };

            foreach (var (area, action) in PermissionMatrix.GrantsFor(name))
            {
                role.Permissions.Add(new RolePermission { Area = area, Action = action });
            }

            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAuthProvidersAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        if (await db.AuthProviders.AnyAsync(ct))
        {
            return;
        }

        db.AuthProviders.Add(new AuthProvider { Name = "Email", Enabled = true, IsBuiltIn = true });
        foreach (var name in OAuthProviders)
        {
            db.AuthProviders.Add(new AuthProvider { Name = name, Enabled = false, IsBuiltIn = false });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureSystemStateAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        if (!await db.SystemState.AnyAsync(ct))
        {
            db.SystemState.Add(new SystemState { Id = 1, Installed = false });
            await db.SaveChangesAsync(ct);
        }
    }
}
