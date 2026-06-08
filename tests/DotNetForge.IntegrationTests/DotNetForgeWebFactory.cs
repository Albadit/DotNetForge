using DotNetForge.Abstractions.Security;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Boots the real DotNetForge.Web host against an isolated, throwaway SQLite database so each test
/// gets a fresh, fully-migrated CMS. The provider/connection are supplied via environment variables
/// (read by the host's .env loader) before the host builds.
/// </summary>
public sealed class DotNetForgeWebFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public DotNetForgeWebFactory()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dnf-it-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("DATABASE_PROVIDER", "sqlite");
        Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("APP_URL", "http://localhost");
    }

    /// <summary>Programmatically completes installation (creates the first Super Admin).</summary>
    public async Task InstallAsync()
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInstallationStore>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var cache = scope.ServiceProvider.GetRequiredService<InstallationStatusCache>();

        var admin = new User
        {
            Email = "admin@example.com",
            PasswordHash = hasher.Hash("Sup3rSecret"),
            EmailConfirmed = true,
        };

        await store.InstallFirstAdminAsync(admin);
        cache.MarkInstalled();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { File.Delete(_dbPath); } catch { /* best effort cleanup */ }
        }
    }
}
