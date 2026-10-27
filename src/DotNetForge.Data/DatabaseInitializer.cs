using DotNetForge.Data.Database;

namespace DotNetForge.Data;

/// <summary>
/// Brings the CMS database (PostgreSQL) up to date at startup - its committed migrations in
/// <c>Database/Providers/PostgreSql/Migrations/</c> - and seeds baseline data (.docs/architecture/database.md).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(DotNetForgeDbContext db, IDatabaseProvider provider, CancellationToken cancellationToken = default)
    {
        await provider.InitializeSchemaAsync(db, cancellationToken);
        await DataSeeder.SeedAsync(db, cancellationToken);
    }
}
