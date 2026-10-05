using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Brings the database up to date at startup and seeds baseline data. Both providers apply their own committed
/// migration set (SQLite: <c>Migrations/</c>, PostgreSQL: <c>Migrations/PostgreSql/</c>), so the schema is ready
/// before traffic is served and later releases upgrade existing databases (.docs/architecture/database.md).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(DotNetForgeDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await DataSeeder.SeedAsync(db, cancellationToken);
    }
}
