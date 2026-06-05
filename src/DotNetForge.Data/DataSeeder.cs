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
        await SeedAuthProvidersAsync(db, cancellationToken);
        await EnsureSystemStateAsync(db, cancellationToken);
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
