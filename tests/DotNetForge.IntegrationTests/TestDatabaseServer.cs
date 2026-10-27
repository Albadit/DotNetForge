using DotNetForge.Data.Database.Providers.PostgreSql;
using MongoDB.Driver;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The database servers of the integration tests (.docs/guides/testing.md#databases). The CMS runs on PostgreSQL:
/// <c>DNF_TEST_POSTGRES</c> holds a server connection string without a database (default: the server of
/// <c>docker/databases/postgres.yml</c> on localhost:5432), and every factory creates and drops its own database there.
/// The other variables are optional servers for additional-database tests (<see cref="AdditionalDatabase"/>).
/// </summary>
/// <example>
/// <code>
/// DNF_TEST_POSTGRES=Host=localhost;Port=5432;Username=postgres;Password=postgres;GSS Encryption Mode=Disable
/// DNF_TEST_SQLSERVER=Server=localhost,1433;User Id=sa;Password=DotNetForge!2026;TrustServerCertificate=true
/// DNF_TEST_MYSQL=Server=localhost;Port=3306;Uid=root;Pwd=dotnetforge
/// DNF_TEST_MONGODB=mongodb://localhost:27017/?replicaSet=rs0&amp;directConnection=true
/// </code>
/// </example>
public static class TestDatabaseServer
{
    /// <summary>The PostgreSQL server of the CMS test databases.</summary>
    public static string Postgres =>
        (Get("DNF_TEST_POSTGRES") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;GSS Encryption Mode=Disable").TrimEnd(';');

    /// <summary>A connection string for a new CMS database named <paramref name="database"/>.</summary>
    public static string CmsDatabase(string database) => $"{Postgres};Database={database}";

    /// <summary>
    /// A connection string for an additional database of <paramref name="provider"/> named <paramref name="database"/>,
    /// or null when that provider's server isn't configured. SQLite needs no server (a file in <paramref name="folder"/>).
    /// </summary>
    public static string? AdditionalDatabase(string provider, string database, string folder) => provider switch
    {
        "sqlite" => $"Data Source={Path.Combine(folder, database + ".db")}",
        "postgresql" => $"{Postgres};Database={database}",
        "sqlserver" => Get("DNF_TEST_SQLSERVER") is { } sqlServer ? $"{sqlServer.TrimEnd(';')};Initial Catalog={database}" : null,
        "mysql" => Get("DNF_TEST_MYSQL") is { } mySql ? $"{mySql.TrimEnd(';')};Database={database}" : null,
        "mongodb" => Get("DNF_TEST_MONGODB") is { } mongo ? new MongoUrlBuilder(mongo) { DatabaseName = database }.ToString() : null,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    /// <summary>The provider name the tests' CMS database uses.</summary>
    public const string CmsProvider = "postgresql";

    /// <summary>For tests that build the CMS context directly (migrations of <see cref="PostgreSqlDatabaseProvider"/>).</summary>
    public static void Configure(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options, string connectionString) =>
        PostgreSqlDatabaseProvider.Configure(options, connectionString);

    private static string? Get(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
