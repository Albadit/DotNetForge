using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Each provider as an additional database next to the PostgreSQL CMS database (.docs/database/providers.md#testing):
/// <see cref="IDatabaseService"/> reads and writes it and lists its tables, a custom API endpoint runs its SQL there,
/// and Data Management exports it. SQLite and PostgreSQL always run; SQL Server, MySQL and MongoDB when their
/// <c>DNF_TEST_*</c> server is set (the CI's additional-database jobs).
/// </summary>
public sealed class AdditionalDatabaseProviderTests
{
    [Fact]
    public Task Sqlite() => RunAsync("sqlite");

    [Fact]
    public Task PostgreSql() => RunAsync("postgresql");

    [AdditionalDatabaseFact("sqlserver", "DNF_TEST_SQLSERVER")]
    public Task SqlServer() => RunAsync("sqlserver");

    [AdditionalDatabaseFact("mysql", "DNF_TEST_MYSQL")]
    public Task MySql() => RunAsync("mysql");

    [AdditionalDatabaseFact("mongodb", "DNF_TEST_MONGODB")]
    public Task MongoDb() => RunAsync("mongodb");

    [Fact]
    public async Task Endpoints_refuse_unknown_databases_and_databases_without_sql()
    {
        Environment.SetEnvironmentVariable("DATABASES_EVENTS_PROVIDER", "mongodb");
        Environment.SetEnvironmentVariable("DATABASES_EVENTS_CONNECTION_STRING", "mongodb://127.0.0.1:1/events");
        try
        {
            using var factory = new DotNetForgeWebFactory();
            await factory.InstallAsync();
            var admin = await factory.SignInAsync();

            // MongoDB is configured, but not offered: it has no SQL.
            var editor = await admin.GetStringAsync("/admin/api-endpoints/new");
            Assert.DoesNotContain("events (MongoDB)", editor);

            var mongo = await CreateEndpointAsync(admin, "events", "SELECT 1");
            Assert.Contains("which has no SQL", WebUtility.HtmlDecode(await mongo.Content.ReadAsStringAsync()));
            var unknown = await CreateEndpointAsync(admin, "missing", "SELECT 1");
            Assert.Contains("There is no database 'missing'", WebUtility.HtmlDecode(await unknown.Content.ReadAsStringAsync()));
            await factory.WithDbAsync(async db => Assert.False(await db.CustomEndpoints.AnyAsync()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASES_EVENTS_PROVIDER", null);
            Environment.SetEnvironmentVariable("DATABASES_EVENTS_CONNECTION_STRING", null);
        }
    }

    private static async Task RunAsync(string provider)
    {
        var name = $"dnf_extra_{Guid.NewGuid():N}"[..24];
        var folder = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(folder);
        var connectionString = TestDatabaseServer.AdditionalDatabase(provider, name, folder)!;
        Environment.SetEnvironmentVariable("DATABASES_EXTRA_PROVIDER", provider);
        Environment.SetEnvironmentVariable("DATABASES_EXTRA_CONNECTION_STRING", connectionString);
        try
        {
            using var factory = new DotNetForgeWebFactory();
            var catalog = factory.Services.GetRequiredService<DatabaseCatalog>();
            await CreateDatabaseAsync(catalog.Get("extra"), name);
            var db = factory.Services.GetRequiredService<IDatabaseService>();

            await db.ExecuteAsync(DatabaseCommand.InsertMany("events", new[]
            {
                new Dictionary<string, object?> { ["id"] = 1, ["kind"] = "sale", ["amount"] = 10 },
                new Dictionary<string, object?> { ["id"] = 2, ["kind"] = "sale", ["amount"] = 5 },
                new Dictionary<string, object?> { ["id"] = 3, ["kind"] = "refund", ["amount"] = 3 },
            }).On("extra"));
            Assert.Contains("events", await db.ListCollectionsAsync("extra"));
            Assert.Equal(3, (await db.QueryAsync(DatabaseCommand.Find("events").On("extra"))).Data!.Count);

            await factory.InstallAsync();
            var admin = await factory.SignInAsync();

            if (provider != "mongodb")
            {
                // A custom endpoint whose SQL runs on the additional database.
                Assert.Contains($"extra ({catalog.Get("extra").Provider.DisplayName})", await admin.GetStringAsync("/admin/api-endpoints/new"));
                var created = await CreateEndpointAsync(admin, "extra", "SELECT kind, amount FROM events WHERE kind = @kind ORDER BY amount");
                Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
                await factory.WithDbAsync(async cms => Assert.Equal("extra", (await cms.CustomEndpoints.SingleAsync()).Database));

                var response = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/custom/sales?kind=sale");
                Assert.Equal(new[] { 5, 10 }, response.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("amount").GetInt32()));
            }

            // Data Management exports it table by table.
            var export = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/data", "/admin/data/export",
                new Dictionary<string, string> { ["database"] = "extra" });
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var zip = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
            using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
            Assert.Equal("dotnetforge-database-export", manifest.RootElement.GetProperty("format").GetString());
            Assert.Equal("extra", manifest.RootElement.GetProperty("database").GetString());
            var events = manifest.RootElement.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "events");
            Assert.Equal(3, events.GetProperty("rows").GetInt32());
            using var table = JsonDocument.Parse(zip.GetEntry("tables/events.json")!.Open());
            Assert.Equal(3, table.RootElement.GetArrayLength());
            await factory.WithDbAsync(async cms => Assert.True(await cms.TransferRecords.AnyAsync(r => r.Note == "Database extra")));

            await DropDatabaseAsync(catalog.Get("extra"), name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASES_EXTRA_PROVIDER", null);
            Environment.SetEnvironmentVariable("DATABASES_EXTRA_CONNECTION_STRING", null);
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static Task<HttpResponseMessage> CreateEndpointAsync(HttpClient admin, string database, string sql) =>
        DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/new", new Dictionary<string, string>
        {
            ["Name"] = "Sales",
            ["Path"] = "sales",
            ["Access"] = "Public",
            ["Enabled"] = "true",
            ["Database"] = database,
            ["OperationsJson"] = JsonSerializer.Serialize(new[]
            {
                new
                {
                    method = "GET",
                    path = "",
                    sql,
                    parameters = JsonDocument.Parse("""[{ "name": "kind", "source": "query", "required": true }]""").RootElement,
                    result = "Rows",
                    timeoutSeconds = 30,
                    maxRows = 100,
                },
            }),
        });

    /// <summary>The server databases are created here (and the table, for the SQL ones); MongoDB creates both on insert.</summary>
    private static async Task CreateDatabaseAsync(ResolvedDatabase extra, string name)
    {
        if (extra.ProviderName is "postgresql" or "sqlserver" or "mysql")
        {
            await ExecuteOnServerAsync(extra, $"CREATE DATABASE {name}");
        }

        if (extra.ProviderName != "mongodb")
        {
            await using var connection = extra.Provider.CreateConnection(extra.Settings)!;
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE events (id INTEGER PRIMARY KEY, kind VARCHAR(20) NOT NULL, amount INTEGER NOT NULL)";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropDatabaseAsync(ResolvedDatabase extra, string name)
    {
        try
        {
            if (extra.ProviderName == "mongodb")
            {
                await new MongoDB.Driver.MongoClient(extra.Settings.ConnectionString).DropDatabaseAsync(name);
            }
            else if (extra.ProviderName != "sqlite")
            {
                Npgsql.NpgsqlConnection.ClearAllPools();
                Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                global::MySql.Data.MySqlClient.MySqlConnection.ClearAllPools();
                await ExecuteOnServerAsync(extra, $"DROP DATABASE {name}");
            }
        }
        catch
        {
            // Best effort: a leftover test database is harmless.
        }
    }

    /// <summary>Runs <paramref name="sql"/> on the server's default database (the connection string without ours).</summary>
    private static async Task ExecuteOnServerAsync(ResolvedDatabase extra, string sql)
    {
        var server = extra.ProviderName switch
        {
            "postgresql" => extra.Settings with { ConnectionString = TestDatabaseServer.Postgres + ";Database=postgres" },
            "sqlserver" => extra.Settings with { ConnectionString = Environment.GetEnvironmentVariable("DNF_TEST_SQLSERVER") },
            _ => extra.Settings with { ConnectionString = Environment.GetEnvironmentVariable("DNF_TEST_MYSQL") },
        };
        await using var connection = extra.Provider.CreateConnection(server)!;
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A fact that runs only when the provider's test server is configured (like <see cref="DatabaseServerFactAttribute"/>).</summary>
public sealed class AdditionalDatabaseFactAttribute : FactAttribute
{
    public AdditionalDatabaseFactAttribute(string provider, string variable)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Set {variable} to test {provider} as an additional database (.docs/guides/testing.md#databases).";
        }
    }
}
