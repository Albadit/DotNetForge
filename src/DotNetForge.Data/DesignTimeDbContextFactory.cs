using DotNetForge.Data.Database.Providers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotNetForge.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add --context PostgreSqlDbContext</c> create the CMS context without booting the web host
/// (migrations in <c>Database/Providers/PostgreSql/Migrations/</c>). Generating migrations never opens a connection, so
/// the default is a placeholder; <c>DATABASE_CONNECTION_STRING</c> overrides it for <c>dotnet ef database update</c>.
/// </summary>
public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgreSqlDbContext>
{
    public PostgreSqlDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgreSqlDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") is { Length: > 0 } value
            ? value
            : "Host=localhost;Database=dotnetforge_design";
        PostgreSqlDatabaseProvider.Configure(options, connectionString);
        return new PostgreSqlDbContext(options.Options);
    }
}
