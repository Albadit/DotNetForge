using DotNetForge.Data.Database.Relational;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DotNetForge.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> create each SQL provider's context without booting the web host
/// (.docs/database/adding-a-provider.md). Generating migrations never opens a connection, so the defaults are
/// placeholders; <c>DATABASE_CONNECTION_STRING</c> overrides them for <c>dotnet ef database update</c>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DotNetForgeDbContext>
{
    public DotNetForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DotNetForgeDbContext>();
        SqliteDatabaseProvider.Configure(options, DesignTime.ConnectionString("Data Source=:memory:"));
        return new DotNetForgeDbContext(options.Options);
    }
}

/// <summary>Design-time factory for <c>--context PostgreSqlDbContext</c> (<c>Migrations/PostgreSql/</c>).</summary>
public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgreSqlDbContext>
{
    public PostgreSqlDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgreSqlDbContext>();
        PostgreSqlDatabaseProvider.Configure(options, DesignTime.ConnectionString("Host=localhost;Database=dotnetforge_design"));
        return new PostgreSqlDbContext(options.Options);
    }
}

/// <summary>Design-time factory for <c>--context SqlServerDbContext</c> (<c>Migrations/SqlServer/</c>).</summary>
public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqlServerDbContext>();
        SqlServerDatabaseProvider.Configure(options, DesignTime.ConnectionString("Server=localhost;Initial Catalog=dotnetforge_design;TrustServerCertificate=true"));
        return new SqlServerDbContext(options.Options);
    }
}

/// <summary>Design-time factory for <c>--context MySqlDbContext</c> (<c>Migrations/MySql/</c>).</summary>
public sealed class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MySqlDbContext>
{
    public MySqlDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MySqlDbContext>();
        MySqlDatabaseProvider.Configure(options, DesignTime.ConnectionString("Server=localhost;Database=dotnetforge_design;Uid=root"));
        return new MySqlDbContext(options.Options);
    }
}

internal static class DesignTime
{
    public static string ConnectionString(string placeholder) =>
        Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") is { Length: > 0 } value ? value : placeholder;
}
