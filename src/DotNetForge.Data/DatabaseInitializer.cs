using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Brings the database up to date at startup and seeds baseline data. SQLite (the default provider)
/// applies the committed EF Core migration set; PostgreSQL creates the schema from the model. Either
/// way the schema is ready before traffic is served (architecture.md deployment strategy).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        DotNetForgeDbContext db, AppEnvironment env, CancellationToken cancellationToken = default)
    {
        if (env.Provider == DatabaseProvider.Sqlite)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            // PostgreSQL: build the schema from the model. A PostgreSQL-specific migration set can
            // be generated as a follow-up (see docs/developer-guide.md).
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        await DataSeeder.SeedAsync(db, cancellationToken);
    }
}
