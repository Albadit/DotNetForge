using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The same contract on every provider (.docs/database/providers.md#contract): run on SQLite by default and on each
/// database server in CI (<see cref="TestDatabaseServer"/>). Each test uses its own keys, so they share one host.
/// </summary>
public sealed class DatabaseServiceContractTests : IClassFixture<DatabaseServiceContractTests.Host>
{
    private readonly DotNetForgeWebFactory _factory;
    private readonly IDatabaseService _db;
    private readonly string _prefix = $"t{Guid.NewGuid():N}"[..12];

    public DatabaseServiceContractTests(Host host)
    {
        _factory = host.Factory;
        _db = _factory.Services.GetRequiredService<IDatabaseService>();
    }

    /// <summary>One host for the class (xUnit fixtures need a single parameterless constructor).</summary>
    public sealed class Host : IDisposable
    {
        public DotNetForgeWebFactory Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    private static Dictionary<string, object?> Setting(string key, string? value, Guid? tenantId = null) =>
        new() { ["Key"] = key, ["Value"] = value, ["TenantId"] = tenantId };

    private DatabaseFilter Mine => DatabaseFilter.StartsWith("Key", _prefix);

    [Fact]
    public async Task Crud_round_trip()
    {
        Assert.Equal(1, (await _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-a", "1")))).Data);
        Assert.Equal(3, (await _db.ExecuteAsync(DatabaseCommand.InsertMany("Settings", new[]
        {
            Setting($"{_prefix}-b", "2"), Setting($"{_prefix}-c", "2"), Setting($"{_prefix}-d", "3"),
        }))).Data);

        var page = await _db.QueryAsync(DatabaseCommand.Find("Settings", Mine).OrderBy("Key").Page(1, 2).Select("Key", "Value"));
        Assert.Equal(new[] { $"{_prefix}-b", $"{_prefix}-c" }, page.Data!.Select(r => r.Get<string>("Key")));
        Assert.Equal(2, page.Data![0].Count); // only the selected fields

        var one = await _db.QueryAsync(DatabaseCommand.FindOne("Settings", Mine & DatabaseFilter.Eq("Value", "3")));
        Assert.Equal($"{_prefix}-d", Assert.Single(one.Data!).Get<string>("Key"));
        Assert.Equal(4, (await _db.ExecuteAsync(DatabaseCommand.Count("Settings", Mine))).Data);

        // UpdateOne/DeleteOne take the first match in the command's sort order.
        Assert.Equal(1, (await _db.ExecuteAsync(DatabaseCommand.UpdateOne("Settings", Mine, new Dictionary<string, object?> { ["Value"] = "first" })
            .OrderByDescending("Key"))).Data);
        Assert.Equal("first", (await FindValueAsync($"{_prefix}-d")));
        Assert.Equal(2, (await _db.ExecuteAsync(DatabaseCommand.UpdateMany("Settings", Mine & DatabaseFilter.Eq("Value", "2"),
            new Dictionary<string, object?> { ["Value"] = null }))).Data);
        Assert.Equal(2, (await _db.ExecuteAsync(DatabaseCommand.Count("Settings", Mine & DatabaseFilter.Eq("Value", null)))).Data);

        Assert.Equal(1, (await _db.ExecuteAsync(DatabaseCommand.DeleteOne("Settings", Mine).OrderBy("Key"))).Data);
        Assert.Null(await FindValueAsync($"{_prefix}-a"));
        Assert.Equal(3, (await _db.ExecuteAsync(DatabaseCommand.DeleteMany("Settings", Mine))).Data);
        Assert.Equal(0, (await _db.ExecuteAsync(DatabaseCommand.Count("Settings", Mine))).Data);
    }

    private async Task<string?> FindValueAsync(string key) =>
        (await _db.QueryAsync(DatabaseCommand.FindOne("Settings", DatabaseFilter.Eq("Key", key)))).Data!.SingleOrDefault()?.Get<string>("Value");

    [Fact]
    public async Task Ef_core_and_the_service_read_each_others_data()
    {
        var tenant = Guid.NewGuid();
        var efId = Guid.NewGuid();
        await _factory.WithDbAsync(async db =>
        {
            db.Settings.Add(new Setting { Id = efId, TenantId = tenant, Key = $"{_prefix}-ef", Value = "from EF" });
            await db.SaveChangesAsync();
        });

        await _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-svc", "from service", tenant)));

        var found = await _db.QueryAsync(DatabaseCommand.Find("Settings", DatabaseFilter.Eq("TenantId", tenant)).OrderBy("Key"));
        Assert.Equal(new[] { $"{_prefix}-ef", $"{_prefix}-svc" }, found.Data!.Select(r => r.Get<string>("Key")));
        Assert.Equal(efId, found.Data![0].Get<Guid>("Id"));
        Assert.Equal(tenant, found.Data![1].Get<Guid?>("TenantId"));

        await _factory.WithDbAsync(async db =>
        {
            var viaEf = await db.Settings.AsNoTracking().Where(s => s.TenantId == tenant).OrderBy(s => s.Key).ToListAsync();
            Assert.Equal(new[] { "from EF", "from service" }, viaEf.Select(s => s.Value));
            Assert.NotEqual(Guid.Empty, viaEf[1].Id); // generated by the service, readable by EF
        });
    }

    [Fact]
    public async Task Database_generated_keys_are_created_on_insert()
    {
        var action = $"{_prefix}.created";
        await _db.ExecuteAsync(DatabaseCommand.InsertMany("AuditLogs", new[]
        {
            new Dictionary<string, object?> { ["Action"] = action, ["IpAddress"] = "127.0.0.1", ["UserAgent"] = "test", ["Timestamp"] = DateTime.UtcNow, ["Success"] = true },
            new Dictionary<string, object?> { ["Action"] = action, ["IpAddress"] = "127.0.0.1", ["UserAgent"] = "test", ["Timestamp"] = DateTime.UtcNow, ["Success"] = false },
        }));

        await _factory.WithDbAsync(async db =>
        {
            var ids = await db.AuditLogs.AsNoTracking().Where(a => a.Action == action).Select(a => a.Id).ToListAsync();
            Assert.Equal(2, ids.Distinct().Count());
            Assert.All(ids, id => Assert.True(id > 0));
        });
    }

    [Fact]
    public async Task Text_filters_match_literally()
    {
        await _db.ExecuteAsync(DatabaseCommand.InsertMany("Settings", new[]
        {
            Setting($"{_prefix}-50%_off", "x"), Setting($"{_prefix}-50xyoff", "x"), Setting($"{_prefix}-a.*b", "x"), Setting($"{_prefix}-ABC", "x"),
        }));

        async Task<string?[]> Keys(DatabaseFilter filter) =>
            (await _db.QueryAsync(DatabaseCommand.Find("Settings", Mine & filter).OrderBy("Key"))).Data!.Select(r => r.Get<string>("Key")).ToArray();

        Assert.Equal(new[] { $"{_prefix}-50%_off" }, await Keys(DatabaseFilter.Contains("Key", "50%_")));
        Assert.Equal(new[] { $"{_prefix}-a.*b" }, await Keys(DatabaseFilter.Contains("Key", ".*")));
        Assert.Equal(new[] { $"{_prefix}-ABC" }, await Keys(DatabaseFilter.Contains("Key", "abc", ignoreCase: true)));
    }

    [Fact]
    public async Task Aggregates_group_and_count()
    {
        await _db.ExecuteAsync(DatabaseCommand.InsertMany("Settings", new[]
        {
            Setting($"{_prefix}-1", "red"), Setting($"{_prefix}-2", "red"), Setting($"{_prefix}-3", "blue"),
        }));

        var groups = await _db.QueryAsync(DatabaseCommand.Aggregate("Settings",
                new DatabaseAggregation(new[] { "Value" }, new[] { new DatabaseAccumulator("Total", AggregateFunction.Count) }), Mine)
            .OrderByDescending("Total"));

        Assert.Equal(new[] { ("red", 2L), ("blue", 1L) }, groups.Data!.Select(r => (r.Get<string>("Value")!, r.Get<long>("Total"))));
    }

    [Fact]
    public async Task Unique_violations_are_conflicts()
    {
        var tenant = Guid.NewGuid();
        await _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-dup", "1", tenant)));

        var ex = await Assert.ThrowsAsync<DatabaseConflictException>(() =>
            _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-dup", "2", tenant))));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task Transactions_commit_or_roll_back()
    {
        await using (var rolledBack = await _db.BeginTransactionAsync())
        {
            await _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-rb", "x")), rolledBack);
            await rolledBack.RollbackAsync();
        }

        await using (var committed = await _db.BeginTransactionAsync())
        {
            await _db.ExecuteAsync(DatabaseCommand.InsertOne("Settings", Setting($"{_prefix}-ok", "x")), committed);
            await committed.CommitAsync();
        }

        var keys = (await _db.QueryAsync(DatabaseCommand.Find("Settings", Mine))).Data!.Select(r => r.Get<string>("Key"));
        Assert.Equal(new[] { $"{_prefix}-ok" }, keys);
    }

    [Fact]
    public async Task Large_results_can_be_streamed()
    {
        await _db.ExecuteAsync(DatabaseCommand.InsertMany("Settings",
            Enumerable.Range(0, 25).Select(i => (IReadOnlyDictionary<string, object?>)Setting($"{_prefix}-{i:00}", "s")).ToList()));

        var count = 0;
        await foreach (var _ in _db.StreamAsync(DatabaseCommand.Find("Settings", Mine)))
        {
            count++;
        }

        Assert.Equal(25, count);
    }

    [Fact]
    public async Task Seeded_cms_data_is_queryable_by_entity_name()
    {
        var roles = await _db.QueryAsync(DatabaseCommand.Find("Roles", DatabaseFilter.Eq("Name", Shared.Constants.Roles.SuperAdmin)).Select("Name", "IsBuiltIn"));

        Assert.True(Assert.Single(roles.Data!).Get<bool>("IsBuiltIn"));
        Assert.True((await _db.ExecuteAsync(DatabaseCommand.Count("Role"))).Data >= 4);
    }

    [Fact]
    public async Task Composite_keys_are_read_and_filtered_like_other_fields()
    {
        await _factory.InstallAsync();
        Guid adminId = default, superAdminRoleId = default;
        await _factory.WithDbAsync(async db =>
        {
            adminId = await db.Users.Where(u => u.Email == DotNetForgeWebFactory.AdminEmail).Select(u => u.Id).SingleAsync();
            superAdminRoleId = await db.Roles.Where(r => r.Name == Shared.Constants.Roles.SuperAdmin).Select(r => r.Id).SingleAsync();
        });

        // UserRoles has the key (UserId, RoleId); MongoDB stores it as the _id document.
        var links = await _db.QueryAsync(DatabaseCommand.Find("UserRoles", DatabaseFilter.Eq("UserId", adminId)));

        var link = Assert.Single(links.Data!);
        Assert.Equal(adminId, link.Get<Guid>("UserId"));
        Assert.Equal(superAdminRoleId, link.Get<Guid>("RoleId"));
    }

    [Fact]
    public async Task Unknown_names_and_connection_status_are_reported()
    {
        await Assert.ThrowsAsync<DatabaseNotFoundException>(() => _db.QueryAsync(DatabaseCommand.Find("Customers")));
        await Assert.ThrowsAsync<DatabaseNotFoundException>(() => _db.QueryAsync(DatabaseCommand.Find("Settings", DatabaseFilter.Eq("Secret", 1))));

        var connection = await _db.TestConnectionAsync();
        Assert.True(connection.Success, connection.Error?.ToString());
        Assert.Equal(_factory.Provider, _db.Databases.Single(d => d.IsMain).Provider switch
        {
            "SQLite" => "sqlite",
            "PostgreSQL" => "postgresql",
            "SQL Server" => "sqlserver",
            "MySQL" => "mysql",
            "MongoDB" => "mongodb",
            var other => other,
        });
    }
}

/// <summary>
/// A second, model-less database next to the CMS one (<c>DATABASES_REPORTS_*</c>): names are used as written, every
/// column is returned, and operations that need a key are refused.
/// </summary>
public sealed class AdditionalDatabaseTests
{
    [Fact]
    public async Task Commands_run_against_a_named_database_without_a_model()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"dnf-reports-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var connectionString = $"Data Source={Path.Combine(folder, "reports.db")}";
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE events (id TEXT PRIMARY KEY, kind TEXT NOT NULL, amount INTEGER NOT NULL)";
            await create.ExecuteNonQueryAsync();
        }

        Environment.SetEnvironmentVariable("DATABASES_REPORTS_PROVIDER", "sqlite");
        Environment.SetEnvironmentVariable("DATABASES_REPORTS_CONNECTION_STRING", connectionString);
        try
        {
            using var factory = new DotNetForgeWebFactory();
            var db = factory.Services.GetRequiredService<IDatabaseService>();

            await db.ExecuteAsync(DatabaseCommand.InsertMany("events", new[]
            {
                new Dictionary<string, object?> { ["id"] = "1", ["kind"] = "sale", ["amount"] = 10 },
                new Dictionary<string, object?> { ["id"] = "2", ["kind"] = "sale", ["amount"] = 5 },
                new Dictionary<string, object?> { ["id"] = "3", ["kind"] = "refund", ["amount"] = 3 },
            }).On("reports"));

            var all = await db.QueryAsync(DatabaseCommand.Find("events").On("reports").OrderBy("id"));
            Assert.Equal(new[] { "id", "kind", "amount" }, all.Data![0].Keys);
            Assert.Equal("reports", all.Database);

            var totals = await db.QueryAsync(DatabaseCommand.Aggregate("events",
                new DatabaseAggregation(new[] { "kind" }, new[] { new DatabaseAccumulator("total", AggregateFunction.Sum, "amount") })).On("reports").OrderBy("kind"));
            Assert.Equal(new[] { ("refund", 3L), ("sale", 15L) }, totals.Data!.Select(r => (r.Get<string>("kind")!, r.Get<long>("total"))));

            await Assert.ThrowsAsync<DatabaseProviderException>(() => db.ExecuteAsync(
                DatabaseCommand.DeleteOne("events", DatabaseFilter.Eq("kind", "sale")).On("reports")));
            await Assert.ThrowsAsync<DatabaseQueryException>(() => db.QueryAsync(DatabaseCommand.Find("events; DROP TABLE events").On("reports")));
            Assert.Equal(2, (await db.ExecuteAsync(DatabaseCommand.DeleteMany("events", DatabaseFilter.Eq("kind", "sale")).On("reports"))).Data);

            // The CMS database is untouched and still the default target.
            Assert.Equal("main", (await db.ExecuteAsync(DatabaseCommand.Count("Settings"))).Database);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DATABASES_REPORTS_PROVIDER", null);
            Environment.SetEnvironmentVariable("DATABASES_REPORTS_CONNECTION_STRING", null);
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }
}
