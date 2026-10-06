using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database;
using DotNetForge.Data.Database.MongoDb;
using DotNetForge.Data.Database.Relational;
using DotNetForge.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Provider registration and resolution (.docs/database/providers.md): explicit names, aliases, detection from the
/// connection string, per-provider validation, multiple databases and registering an extra provider.
/// </summary>
public sealed class DatabaseProviderTests
{
    private static readonly DatabaseHostContext Development = new(IsDevelopment: true, DevelopmentDataRoot: Path.Combine(Path.GetTempPath(), "dnf-root"));
    private static readonly DatabaseHostContext Production = Development with { IsDevelopment = false };

    private static DatabaseProviderRegistry DefaultRegistry() => new(Registrations());

    private static IEnumerable<KeyValuePair<string, IDatabaseProvider>> Registrations()
    {
        var postgres = new PostgreSqlDatabaseProvider();
        return new Dictionary<string, IDatabaseProvider>
        {
            ["sqlite"] = new SqliteDatabaseProvider(),
            ["postgresql"] = postgres,
            ["postgres"] = postgres,
            ["sqlserver"] = new SqlServerDatabaseProvider(),
            ["mysql"] = new MySqlDatabaseProvider(),
            ["mongodb"] = new MongoDbDatabaseProvider(),
        };
    }

    private static ResolvedDatabase Resolve(string? provider, string? connectionString, DatabaseHostContext? host = null, string? databaseName = null) =>
        DefaultRegistry().Resolve(new DatabaseSettings { Provider = provider, ConnectionString = connectionString, DatabaseName = databaseName }, host ?? Development);

    [Theory]
    [InlineData("sqlite", "Data Source=/data/cms.db", "SQLite")]
    [InlineData("postgresql", "Host=db;Database=cms", "PostgreSQL")]
    [InlineData("POSTGRES", "Host=db;Database=cms", "PostgreSQL")]
    [InlineData("sqlserver", "Server=db;Database=cms;User Id=sa;Password=x", "SQL Server")]
    [InlineData("mysql", "Server=db;Database=cms;Uid=root;Pwd=x", "MySQL")]
    [InlineData("mongodb", "mongodb://db:27017/cms", "MongoDB")]
    public void Explicit_provider_names_and_aliases_resolve(string name, string connectionString, string expected)
    {
        var database = Resolve(name, connectionString);

        Assert.Equal(expected, database.Provider.DisplayName);
        Assert.Equal(name.ToLowerInvariant(), database.ProviderName);
    }

    [Fact]
    public void Explicit_provider_wins_over_detection()
    {
        // "Host=" alone would be detected as PostgreSQL; MySQL's connector accepts it too.
        Assert.Equal("MySQL", Resolve("mysql", "Host=db;Database=cms;Uid=root").Provider.DisplayName);
    }

    [Fact]
    public void Unknown_provider_lists_the_registered_ones()
    {
        var ex = Assert.Throws<DatabaseConfigurationException>(() => Resolve("oracle", "Data Source=x"));

        Assert.Contains("oracle", ex.Message);
        Assert.Contains("mongodb", ex.Message);
        Assert.Contains("sqlserver", ex.Message);
    }

    [Theory]
    [InlineData(null, "SQLite")]
    [InlineData("", "SQLite")]
    [InlineData("Data Source=/data/cms.db", "SQLite")]
    [InlineData("Filename=:memory:", "SQLite")]
    [InlineData("Host=db;Port=5432;Database=cms;Username=u;Password=p", "PostgreSQL")]
    [InlineData("Server=db;Initial Catalog=cms;TrustServerCertificate=true", "SQL Server")]
    [InlineData("Data Source=db.example;Initial Catalog=cms;User ID=sa;Password=p", "SQL Server")]
    [InlineData("mongodb://db:27017/cms?replicaSet=rs0", "MongoDB")]
    [InlineData("mongodb+srv://cluster.example.net/cms", "MongoDB")]
    public void Without_a_provider_the_connection_string_decides(string? connectionString, string expected)
    {
        Assert.Equal(expected, Resolve(null, connectionString).Provider.DisplayName);
    }

    [Theory]
    [InlineData("Server=db;Database=cms;Uid=root;Pwd=x")] // MySQL style: never guessed
    [InlineData("whatever")]
    public void Undetectable_connection_strings_ask_for_DATABASE_PROVIDER(string connectionString)
    {
        var ex = Assert.Throws<DatabaseConfigurationException>(() => Resolve(null, connectionString));
        Assert.Contains("DATABASE_PROVIDER", ex.Message);
    }

    [Fact]
    public void Sqlite_defaults_to_a_development_file_and_is_required_elsewhere()
    {
        var development = Resolve(null, null);
        Assert.Equal($"Data Source={Path.Combine(Development.DevelopmentDataRoot, "storage", "dotnetforge.db")}", development.Settings.ConnectionString);

        var ex = Assert.Throws<DatabaseConfigurationException>(() => Resolve(null, null, Production));
        Assert.Contains("DATABASE_CONNECTION_STRING is required outside Development", ex.Message);
    }

    [Fact]
    public void Sqlite_needs_an_absolute_path_outside_development()
    {
        var ex = Assert.Throws<DatabaseConfigurationException>(() => Resolve("sqlite", "Data Source=relative.db", Production));
        Assert.Contains("absolute path", ex.Message);

        var absolute = Path.Combine(Path.GetTempPath(), "cms.db");
        Assert.Equal($"Data Source={absolute}", Resolve("sqlite", $"Data Source={absolute}", Production).Settings.ConnectionString);
        Assert.NotNull(Resolve("sqlite", "Data Source=:memory:", Production));
    }

    [Theory]
    [InlineData("postgresql", "postgres://u:p@db/cms", "key=value form")]
    [InlineData("postgresql", "", "needs a connection string")]
    [InlineData("sqlserver", null, "needs a connection string")]
    [InlineData("mysql", "", "needs a connection string")]
    [InlineData("mongodb", "Host=db", "mongodb://")]
    [InlineData("mongodb", "mongodb://db:27017", "database name")]
    public void Providers_validate_their_connection_strings(string provider, string? connectionString, string expected)
    {
        var ex = Assert.Throws<DatabaseConfigurationException>(() => Resolve(provider, connectionString));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void MongoDb_takes_the_database_name_from_the_url_or_DATABASE_NAME()
    {
        Assert.Equal("cms", Resolve("mongodb", "mongodb://db:27017/cms").Settings.DatabaseName);
        Assert.Equal("other", Resolve("mongodb", "mongodb://db:27017/cms", databaseName: "other").Settings.DatabaseName);
        Assert.Equal("cms", Resolve("mongodb", "mongodb://db:27017", databaseName: "cms").Settings.DatabaseName);
    }

    [Theory]
    [InlineData("postgresql", "Host=db.internal;Port=5432;Database=cms;Username=admin;Password=s3cret", "PostgreSQL db.internal:5432/cms")]
    [InlineData("sqlserver", "Server=sql01;Initial Catalog=cms;User Id=sa;Password=s3cret", "SQL Server sql01/cms")]
    [InlineData("mysql", "Server=my01;Database=cms;Uid=root;Pwd=s3cret", "MySQL my01/cms")]
    [InlineData("mongodb", "mongodb://admin:s3cret@mongo1:27017,mongo2:27017/cms?replicaSet=rs0", "MongoDB mongo1:27017,mongo2:27017/cms")]
    public void Descriptions_never_contain_credentials(string provider, string connectionString, string expected)
    {
        var database = Resolve(provider, connectionString);
        var description = database.Provider.Describe(database.Settings);

        Assert.Equal(expected, description);
        Assert.DoesNotContain("s3cret", description, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", database.ToDescriptor().Server, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_databases_resolve_independently()
    {
        var registry = DefaultRegistry();
        var catalog = new DatabaseCatalog(
            registry.Resolve(new DatabaseSettings(), Development),
            new[]
            {
                registry.Resolve(new DatabaseSettings { Name = "reports", ConnectionString = "Host=r;Database=reports" }, Development),
                registry.Resolve(new DatabaseSettings { Name = "events", ConnectionString = "mongodb://e:27017/events" }, Development),
            });

        Assert.Equal("SQLite", catalog.Get(null).Provider.DisplayName);
        Assert.Equal("PostgreSQL", catalog.Get("reports").Provider.DisplayName);
        Assert.Equal("MongoDB", catalog.Get("EVENTS").Provider.DisplayName);
        Assert.Equal(new[] { "main", "events", "reports" }, catalog.All.Select(d => d.Name));
        Assert.Throws<DatabaseNotFoundException>(() => catalog.Get("missing"));
    }

    [Fact]
    public void A_named_sqlite_database_gets_its_own_development_file()
    {
        var database = DefaultRegistry().Resolve(new DatabaseSettings { Name = "reports", Provider = "sqlite" }, Development);
        Assert.EndsWith($"reports.db", database.Settings.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_a_provider_adds_it_without_touching_the_core()
    {
        var services = new ServiceCollection()
            .AddDefaultDatabaseProviders()
            .AddDatabaseProvider<FakeDatabaseProvider>("fake");
        services.AddDotNetForgeDatabases(new DatabaseSettings { Provider = "fake", ConnectionString = "fake://main" },
            Array.Empty<DatabaseSettings>(), Development);

        var catalog = services.BuildServiceProvider().GetRequiredService<DatabaseCatalog>();
        Assert.Equal("Fake", catalog.Main.Provider.DisplayName);
    }

    [Fact]
    public void A_later_registration_replaces_a_provider_name()
    {
        var services = new ServiceCollection()
            .AddDefaultDatabaseProviders()
            .AddDatabaseProvider<FakeDatabaseProvider>("sqlite");
        services.AddDotNetForgeDatabases(new DatabaseSettings { Provider = "sqlite", ConnectionString = "fake://main" },
            Array.Empty<DatabaseSettings>(), Development);

        Assert.Equal("Fake", services.BuildServiceProvider().GetRequiredService<DatabaseCatalog>().Main.Provider.DisplayName);
    }
}
