using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// Centralizes how the EF Core context is bound to the active provider selected from <c>.env</c>
/// (SQLite default / PostgreSQL). Used by both the web host and the design-time factory so provider
/// selection lives in exactly one place (architecture.md).
/// </summary>
public static class DbProviderConfigurator
{
    public static void Configure(DbContextOptionsBuilder options, AppEnvironment env)
    {
        var connectionString = env.ResolveConnectionString();

        switch (env.Provider)
        {
            case DatabaseProvider.Sqlite:
                EnsureSqliteDirectory(connectionString);
                options.UseSqlite(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(DotNetForgeDbContext).Assembly.FullName));
                break;

            case DatabaseProvider.PostgreSql:
                options.UseNpgsql(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(DotNetForgeDbContext).Assembly.FullName));
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider '{env.Provider}'.");
        }
    }

    /// <summary>Ensures the directory for a SQLite <c>Data Source</c> file exists.</summary>
    private static void EnsureSqliteDirectory(string connectionString)
    {
        const string marker = "data source=";
        var idx = connectionString.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return;
        }

        var rest = connectionString[(idx + marker.Length)..];
        var end = rest.IndexOf(';');
        var path = (end >= 0 ? rest[..end] : rest).Trim();

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
