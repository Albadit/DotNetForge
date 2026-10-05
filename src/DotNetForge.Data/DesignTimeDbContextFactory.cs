using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotNetForge.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> create the SQLite context (<see cref="DotNetForgeDbContext"/>) without booting
/// the web host. Generating migrations never connects to the database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DotNetForgeDbContext>
{
    public DotNetForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DotNetForgeDbContext>();
        DbProviderConfigurator.Configure(options, new AppEnvironment
        {
            Provider = DatabaseProvider.Sqlite,
            // Generating migrations never opens the connection; an in-memory default avoids creating folders.
            ConnectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ?? "Data Source=:memory:",
        });
        return new DotNetForgeDbContext(options.Options);
    }
}

/// <summary>
/// Design-time factory for the PostgreSQL migration set (<c>dotnet ef migrations add ... --context
/// PostgreSqlDbContext</c>). The placeholder connection string is never opened while generating migrations.
/// </summary>
public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgreSqlDbContext>
{
    public PostgreSqlDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgreSqlDbContext>();
        DbProviderConfigurator.Configure(options, new AppEnvironment
        {
            Provider = DatabaseProvider.PostgreSql,
            ConnectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
                ?? "Host=localhost;Database=dotnetforge_design",
        });
        return new PostgreSqlDbContext(options.Options);
    }
}
