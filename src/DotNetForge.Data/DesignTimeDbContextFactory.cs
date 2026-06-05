using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotNetForge.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> create the context without booting the full web host.
/// Defaults to SQLite; set <c>DATABASE_PROVIDER</c>/<c>DATABASE_CONNECTION_STRING</c> in the
/// environment to generate migrations against PostgreSQL instead.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DotNetForgeDbContext>
{
    public DotNetForgeDbContext CreateDbContext(string[] args)
    {
        var providerRaw = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") ?? "sqlite";
        var provider = providerRaw.Trim().ToLowerInvariant() is "postgresql" or "postgres"
            ? DatabaseProvider.PostgreSql
            : DatabaseProvider.Sqlite;

        var env = new AppEnvironment
        {
            Provider = provider,
            RawProvider = providerRaw,
            ConnectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING"),
        };

        var options = new DbContextOptionsBuilder<DotNetForgeDbContext>();
        DbProviderConfigurator.Configure(options, env);
        return new DotNetForgeDbContext(options.Options);
    }
}
