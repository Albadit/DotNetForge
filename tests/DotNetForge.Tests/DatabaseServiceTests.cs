using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// The <see cref="IDatabaseService"/> pipeline with a mock provider: routing, result normalization, timeouts,
/// cancellation, error translation, validation and logging (.docs/database/query-routing.md).
/// </summary>
public sealed class DatabaseServiceTests
{
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly CapturingLoggerProvider _logs = new();
    private readonly IDatabaseService _service;

    public DatabaseServiceTests()
    {
        var services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Debug))
            .AddDatabaseProvider<FakeDatabaseProvider>("fake");
        services.AddDotNetForgeDatabases(
            new DatabaseSettings { Provider = "fake", ConnectionString = MainConnection },
            new[] { new DatabaseSettings { Name = "reports", Provider = "fake", ConnectionString = ReportsConnection } },
            new DatabaseHostContext(IsDevelopment: true, DevelopmentDataRoot: Path.GetTempPath()));
        _service = services.BuildServiceProvider().GetRequiredService<IDatabaseService>();
    }

    private string MainConnection => $"fake://main/{_id}?password=hunter2";

    private string ReportsConnection => $"fake://reports/{_id}";

    private FakeExecutor Main => FakeDatabaseProvider.Executors.GetOrAdd(MainConnection, _ => new FakeExecutor());

    private FakeExecutor Reports => FakeDatabaseProvider.Executors.GetOrAdd(ReportsConnection, _ => new FakeExecutor());

    private static DatabaseRecord Record(string email) => new(new Dictionary<string, object?> { ["Email"] = email });

    [Fact]
    public async Task Routes_each_command_to_its_database()
    {
        await _service.ExecuteAsync(DatabaseCommand.Count("Users"));
        await _service.ExecuteAsync(DatabaseCommand.Count("Sales").On("reports"));

        Assert.Equal("Users", Assert.Single(Main.Commands).Collection);
        Assert.Equal("Sales", Assert.Single(Reports.Commands).Collection);
        Assert.Equal(new[] { "main", "reports" }, _service.Databases.Select(d => d.Name));
    }

    [Fact]
    public async Task Results_have_the_same_shape_for_every_provider()
    {
        Main.Records = new[] { Record("a@example.com"), Record("b@example.com") };

        var result = await _service.QueryAsync(DatabaseCommand.Find("Users"));

        Assert.True(result.Success);
        Assert.Equal(2, result.AffectedRows);
        Assert.Equal("Fake", result.Provider);
        Assert.Equal("main", result.Database);
        Assert.True(result.ExecutionTime > TimeSpan.Zero);
        Assert.Equal("b@example.com", result.Data![1].Get<string>("email"));
    }

    [Fact]
    public async Task FindOne_asks_for_one_record_and_commands_get_the_default_timeout()
    {
        await _service.QueryAsync(DatabaseCommand.FindOne("Users", DatabaseFilter.Eq("Email", "a@example.com")));

        var sent = Assert.Single(Main.Commands);
        Assert.Equal(1, sent.Take);
        Assert.Equal(DatabaseService.DefaultTimeout, sent.Timeout);
    }

    [Fact]
    public async Task A_slow_command_fails_with_a_timeout_error()
    {
        Main.Before = (_, token) => Task.Delay(TimeSpan.FromSeconds(10), token);

        var ex = await Assert.ThrowsAsync<DatabaseTimeoutException>(() =>
            _service.ExecuteAsync(DatabaseCommand.Count("Users").WithTimeout(TimeSpan.FromMilliseconds(50))));

        Assert.Equal("main", ex.Database);
        Assert.Equal("Fake", ex.Provider);
        Assert.Equal(DatabaseOperation.Count, ex.Operation);
        Assert.Equal("Users", ex.Collection);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_timeout()
    {
        Main.Before = (_, token) => Task.Delay(TimeSpan.FromSeconds(10), token);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ExecuteAsync(DatabaseCommand.Count("Users"), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task Driver_errors_become_database_exceptions_with_the_original_inside()
    {
        Main.Before = (_, _) => throw new FakeDriverException("duplicate value 'a@example.com'");

        var ex = await Assert.ThrowsAsync<DatabaseQueryException>(() =>
            _service.ExecuteAsync(DatabaseCommand.InsertOne("Users", new Dictionary<string, object?> { ["Email"] = "a@example.com" })));

        Assert.IsType<FakeDriverException>(ex.InnerException);
        Assert.Equal(DatabaseOperation.InsertOne, ex.Operation);
        Assert.DoesNotContain("a@example.com", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bugs_are_not_disguised_as_database_errors()
    {
        Main.Before = (_, _) => throw new InvalidOperationException("bug");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExecuteAsync(DatabaseCommand.Count("Users")));
    }

    [Fact]
    public async Task Try_methods_return_failures_instead_of_throwing()
    {
        Main.Before = (_, _) => throw new FakeDriverException("no");

        var result = await _service.TryExecuteAsync(DatabaseCommand.Count("Users"));

        Assert.False(result.Success);
        Assert.IsType<DatabaseQueryException>(result.Error);
        Assert.Throws<DatabaseQueryException>(() => result.EnsureSuccess());

        var unknown = await _service.TryQueryAsync(DatabaseCommand.Find("Users").On("nope"));
        Assert.IsType<DatabaseNotFoundException>(unknown.Error);
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task Invalid_commands_never_reach_the_provider(DatabaseCommand command, string expected)
    {
        var ex = await Assert.ThrowsAsync<DatabaseQueryException>(() => command.Operation is DatabaseOperation.Find or DatabaseOperation.FindOne or DatabaseOperation.Aggregate
            ? _service.QueryAsync(command)
            : _service.ExecuteAsync(command));

        Assert.Contains(expected, ex.Message);
        Assert.Empty(Main.Commands);
    }

    public static TheoryData<DatabaseCommand, string> InvalidCommands() => new()
    {
        { DatabaseCommand.DeleteMany("Users", filter: null), "needs a filter" },
        { DatabaseCommand.UpdateMany("Users", filter: null, new Dictionary<string, object?> { ["Status"] = 1 }), "needs a filter" },
        { DatabaseCommand.UpdateOne("Users", DatabaseFilter.Eq("Email", "x"), new Dictionary<string, object?>()), "at least one field" },
        { DatabaseCommand.InsertMany("Users", Array.Empty<IReadOnlyDictionary<string, object?>>()), "at least one document" },
        { DatabaseCommand.Find("Users") with { Take = -1 }, "negative" },
        { DatabaseCommand.Find("") , "collection" },
        { DatabaseCommand.Aggregate("Users", new DatabaseAggregation(new[] { "Status" }, Array.Empty<DatabaseAccumulator>())), "accumulator" },
    };

    [Fact]
    public async Task Queries_and_writes_use_their_own_methods()
    {
        await Assert.ThrowsAsync<DatabaseQueryException>(() => _service.ExecuteAsync(DatabaseCommand.Find("Users")));
        await Assert.ThrowsAsync<DatabaseQueryException>(() => _service.QueryAsync(DatabaseCommand.Count("Users")));
    }

    [Fact]
    public async Task Deleting_everything_needs_an_explicit_match_all_filter()
    {
        var result = await _service.ExecuteAsync(DatabaseCommand.DeleteMany("Users", DatabaseFilter.And()));

        Assert.Equal(1, result.Data);
    }

    [Fact]
    public async Task A_transaction_only_works_on_its_own_database()
    {
        await using var transaction = await _service.BeginTransactionAsync("reports");

        await Assert.ThrowsAsync<DatabaseQueryException>(() => _service.ExecuteAsync(DatabaseCommand.Count("Users"), transaction));
        Assert.Equal(1, (await _service.ExecuteAsync(DatabaseCommand.Count("Sales").On("reports"), transaction)).Data);
    }

    [Fact]
    public async Task Streaming_yields_records_as_they_arrive()
    {
        Main.Records = Enumerable.Range(1, 5).Select(i => Record($"u{i}@example.com")).ToList();

        var emails = new List<string?>();
        await foreach (var record in _service.StreamAsync(DatabaseCommand.Find("Users")))
        {
            emails.Add(record.Get<string>("Email"));
        }

        Assert.Equal(5, emails.Count);
    }

    [Fact]
    public async Task Connection_tests_report_failures_without_throwing()
    {
        Main.Connected = false;

        var failed = await _service.TestConnectionAsync();
        Assert.False(failed.Success);
        Assert.IsType<DatabaseQueryException>(failed.Error);

        var ok = await _service.TestConnectionAsync("reports");
        Assert.True(ok.Success);
        Assert.Equal("Fake server", ok.Data!.Server);
    }

    [Fact]
    public async Task Logs_describe_the_command_but_never_values_or_secrets()
    {
        Main.Records = new[] { Record("a@example.com") };
        await _service.QueryAsync(DatabaseCommand.Find("Users", DatabaseFilter.Eq("Email", "secret-filter-value")));
        Main.Before = (_, _) => throw new FakeDriverException("driver says: secret-filter-value");
        await _service.TryQueryAsync(DatabaseCommand.Find("Users", DatabaseFilter.Eq("Email", "secret-filter-value")));
        Main.Connected = false;
        await _service.TestConnectionAsync();

        var text = string.Join("\n", _logs.Entries.Select(e => e.Message + e.Exception));
        Assert.Contains("Database main (Fake) Find Users: 1 records", text);
        Assert.Contains("failed after", text);
        Assert.DoesNotContain("secret-filter-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", text, StringComparison.Ordinal);
    }
}
