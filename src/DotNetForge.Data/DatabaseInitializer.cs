using DotNetForge.Data.Database;

namespace DotNetForge.Data;

/// <summary>
/// Brings the main database up to date at startup and seeds baseline data. The provider decides how: SQL providers
/// apply their committed migration set (<c>Migrations/</c>, <c>Migrations/PostgreSql/</c>, <c>Migrations/SqlServer/</c>,
/// <c>Migrations/MySql/</c>); MongoDB creates missing collections and indexes (.docs/architecture/database.md).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(DotNetForgeDbContext db, IDatabaseProvider provider, CancellationToken cancellationToken = default)
    {
        await provider.InitializeSchemaAsync(db, cancellationToken);
        await DataSeeder.SeedAsync(db, cancellationToken);
    }
}
