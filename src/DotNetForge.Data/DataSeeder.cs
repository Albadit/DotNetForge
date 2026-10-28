using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Seeds the baseline data every fresh database needs: the seven built-in roles
/// with their default permission grants, the default authentication providers, and the single
/// system-state row. Seeding is idempotent and runs after migrations, before install detection.
/// </summary>
public static class DataSeeder
{
    private static readonly (string Name, string Description)[] RoleDefinitions =
    {
        (Roles.SuperAdmin, "Full, unrestricted access to every permission area and system function."),
        (Roles.Admin, "Manages content, media, users, roles, settings, API tokens, and webhooks."),
        (Roles.Developer, "Builds against the API: the API docs, API tokens and custom API endpoints."),
        (Roles.Editor, "Creates, edits, and publishes all content and media."),
        (Roles.Author, "Can manage the content they have created."),
        (Roles.Authenticated, "Default role for any signed-in end user."),
        (Roles.Public, "The implicit role for unauthenticated visitors. Read-only public content."),
    };

    public static async Task SeedAsync(DotNetForgeDbContext db, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(db, cancellationToken);
        await SeedPagesAsync(db, cancellationToken);
        await SeedAuthProvidersAsync(db, cancellationToken);
        await EnsureSystemStateAsync(db, cancellationToken);
        await UpgradeAsync(db, cancellationToken);
    }

    /// <summary>The newest one-time data update (<see cref="SystemState.SeedVersion"/>).</summary>
    public const int CurrentSeedVersion = 2;

    /// <summary>
    /// One-time updates of data that existing databases were seeded without, each applied once (recorded in
    /// <see cref="SystemState.SeedVersion"/>), so a grant an administrator later removes is not put back.
    /// </summary>
    private static async Task UpgradeAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        var state = await db.SystemState.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (state is null || state.SeedVersion >= CurrentSeedVersion)
        {
            return;
        }

        if (state.SeedVersion < 1)
        {
            // 1: structured content - the default grants of the new areas for the built-in roles.
            await AddDefaultGrantsAsync(db, new[] { PermissionAreas.ContentTypes, PermissionAreas.Entries }, ct);
        }

        if (state.SeedVersion < 2)
        {
            // 2: email confirmation - accounts that existed before it are trusted as confirmed, so turning it on
            // never locks them out.
            foreach (var user in await db.Users.Where(u => !u.EmailConfirmed).ToListAsync(ct))
            {
                user.EmailConfirmed = true;
            }
        }

        state.SeedVersion = CurrentSeedVersion;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Gives every built-in role that has no grant in an area the matrix defaults of that area.</summary>
    private static async Task AddDefaultGrantsAsync(DotNetForgeDbContext db, IReadOnlyCollection<string> areas, CancellationToken ct)
    {
        var roles = await db.Roles.Where(r => r.IsBuiltIn).Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        var roleIds = roles.Select(r => r.Id).ToList();
        var existing = (await db.RolePermissions
                .Where(p => roleIds.Contains(p.RoleId) && areas.Contains(p.Area))
                .Select(p => new { p.RoleId, p.Area })
                .ToListAsync(ct))
            .Select(p => (p.RoleId, p.Area.ToLowerInvariant()))
            .ToHashSet();
        foreach (var role in roles)
        {
            foreach (var (area, action) in PermissionMatrix.GrantsFor(role.Name).Where(g => areas.Contains(g.Area)))
            {
                if (!existing.Contains((role.Id, area.ToLowerInvariant())))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Area = area, Action = action });
                }
            }
        }
    }

    /// <summary>Seeds a small starter page tree so the Content Manager has something to show on a fresh
    /// install.</summary>
    private static async Task SeedPagesAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        if (await db.Pages.AnyAsync(ct))
        {
            return;
        }

        var home = new Page { Slug = "/", Title = "Home", Published = true, DisplayInMenu = true, SortOrder = 0 };
        var about = new Page { Slug = "about", Title = "About", Published = true, DisplayInMenu = true, SortOrder = 1 };
        var news = new Page { Slug = "news", Title = "News", Published = true, DisplayInMenu = true, SortOrder = 2 };
        var contact = new Page { Slug = "contact", Title = "Contact", DisplayInMenu = true, SortOrder = 3 };
        db.Pages.AddRange(home, about, news, contact);

        // Children of About.
        db.Pages.AddRange(
            new Page { Slug = "team", Title = "Team", Published = true, SortOrder = 0, ParentPageId = about.Id },
            new Page { Slug = "history", Title = "History", SortOrder = 1, ParentPageId = about.Id });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        var existing = await db.Roles
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
            };

            foreach (var (area, action) in PermissionMatrix.GrantsFor(name))
            {
                role.Permissions.Add(new RolePermission { Area = area, Action = action });
            }

            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One <see cref="AuthProvider"/> row per sign-in provider (.docs/features/authentication.md#providers): Email
    /// (built-in, enabled) and every <see cref="AuthProviderKeys.OAuth"/> provider (disabled until configured). Runs on
    /// every start: adds new catalog providers, keeps names current and removes rows of providers that left the catalog
    /// - only while they were never configured, so no settings are lost.
    /// </summary>
    private static async Task SeedAuthProvidersAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        var rows = await db.AuthProviders.ToListAsync(ct);
        var wanted = AuthProviderKeys.OAuth.Prepend((Key: AuthProviderKeys.Email, Name: "Email")).ToList();
        foreach (var row in rows.Where(r => wanted.All(w => w.Key != r.Key) && !r.Enabled && r.SettingsJson is "" or "{}"))
        {
            db.AuthProviders.Remove(row);
        }

        foreach (var (key, name) in wanted)
        {
            var row = rows.FirstOrDefault(r => r.Key == key);
            if (row is null)
            {
                db.AuthProviders.Add(new AuthProvider
                {
                    Key = key,
                    Name = name,
                    Enabled = key == AuthProviderKeys.Email,
                    IsBuiltIn = key == AuthProviderKeys.Email,
                });
            }
            else if (row.Name != name)
            {
                row.Name = name;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
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
