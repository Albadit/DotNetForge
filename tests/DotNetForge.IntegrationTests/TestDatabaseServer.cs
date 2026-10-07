using MongoDB.Driver;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Which database the integration tests run on (.docs/guides/testing.md#databases). Each variable holds a server
/// connection string without a database; the first one that is set wins, otherwise SQLite in a temp folder.
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
    public static (string Provider, string ConnectionString) For(string workDir, string database)
    {
        if (Get("DNF_TEST_POSTGRES") is { } postgres)
        {
            return ("postgresql", $"{postgres.TrimEnd(';')};Database={database}");
        }

        if (Get("DNF_TEST_SQLSERVER") is { } sqlServer)
        {
            return ("sqlserver", $"{sqlServer.TrimEnd(';')};Initial Catalog={database}");
        }

        if (Get("DNF_TEST_MYSQL") is { } mySql)
        {
            return ("mysql", $"{mySql.TrimEnd(';')};Database={database}");
        }

        if (Get("DNF_TEST_MONGODB") is { } mongo)
        {
            return ("mongodb", new MongoUrlBuilder(mongo) { DatabaseName = database }.ToString());
        }

        return ("sqlite", $"Data Source={Path.Combine(workDir, "cms.db")}");
    }

    private static string? Get(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
