using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Centralizes how the EF Core context is bound to the provider detected from the connection string
/// (SQLite default / PostgreSQL). Used by both the web host and the design-time factories so provider
/// selection lives in exactly one place (.docs/architecture/database.md).
/// </summary>
public static class DbProviderConfigurator
{
    public static void Configure(DbContextOptionsBuilder options, AppEnvironment env)
    {
        var connectionString = env.ResolveConnectionString();
        var migrationsAssembly = typeof(DotNetForgeDbContext).Assembly.FullName;

        switch (env.Provider)
        {
            case DatabaseProvider.Sqlite:
                EnsureSqliteDirectory(connectionString);
                options.UseSqlite(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
                break;

            case DatabaseProvider.PostgreSql:
                options.UseNpgsql(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider '{env.Provider}'.");
        }
    }

    /// <summary>
    /// Ensures the directory of a file-based SQLite database exists. The path is configured explicitly outside
    /// Development (a writable volume), so this never writes into the deployment directory there.
    /// </summary>
    private static void EnsureSqliteDirectory(string connectionString)
    {
        var dataSource = SqliteConnectionStrings.GetDataSource(connectionString);
        if (string.IsNullOrEmpty(dataSource) || SqliteConnectionStrings.IsInMemory(dataSource))
        {
            return;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
