using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Tests;

/// <summary>
/// A test provider registered exactly like a real one (<c>AddDatabaseProvider&lt;FakeDatabaseProvider&gt;("fake")</c>):
/// it proves new providers plug in without core changes and lets tests script executor behavior. Executors are keyed
/// by connection string so parallel tests don't share state.
/// </summary>
public sealed class FakeDatabaseProvider : IDatabaseProvider
{
    public static readonly ConcurrentDictionary<string, FakeExecutor> Executors = new(StringComparer.Ordinal);

    public string DisplayName => "Fake";

    public bool CanHandle(string? connectionString) => connectionString?.StartsWith("fake://", StringComparison.Ordinal) == true;

    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host) => settings;

    public string Describe(DatabaseSettings settings) => "Fake server";

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings)
    {
    }

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) => Task.CompletedTask;

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory) =>
        Executors.GetOrAdd(settings.ConnectionString!, _ => new FakeExecutor());

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        FakeDriverException => new DatabaseQueryException("Fake rejected the command.", exception),
        _ => null,
    };
}

/// <summary>What a driver would throw; its message may contain data, which must never reach logs.</summary>
public sealed class FakeDriverException(string message) : Exception(message);

public sealed class FakeExecutor : IDatabaseExecutor
{
    public ConcurrentQueue<DatabaseCommand> Commands { get; } = new();

    /// <summary>Records to return from queries.</summary>
    public IReadOnlyList<DatabaseRecord> Records { get; set; } = Array.Empty<DatabaseRecord>();

    /// <summary>Runs before every operation; throw or delay here to simulate failures and slow databases.</summary>
    public Func<DatabaseCommand, CancellationToken, Task>? Before { get; set; }

    public long Affected { get; set; } = 1;

    public bool Connected { get; set; } = true;

    public async IAsyncEnumerable<DatabaseRecord> QueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Commands.Enqueue(command);
        if (Before is not null)
        {
            await Before(command, cancellationToken);
        }

        foreach (var record in Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    public async Task<long> ExecuteAsync(DatabaseCommand command, IDatabaseTransaction? transaction, CancellationToken cancellationToken)
    {
        Commands.Enqueue(command);
        if (Before is not null)
        {
            await Before(command, cancellationToken);
        }

        return Affected;
    }

    public Task<IDatabaseTransaction> BeginTransactionAsync(string database, CancellationToken cancellationToken) =>
        Task.FromResult<IDatabaseTransaction>(new FakeTransaction(database));

    public Task TestConnectionAsync(CancellationToken cancellationToken) =>
        Connected ? Task.CompletedTask : throw new FakeDriverException("connection refused by fake://secret-host");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeTransaction(string database) : IDatabaseTransaction
{
    public string Database { get; } = database;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Collects formatted log messages so tests can check what is (not) logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
