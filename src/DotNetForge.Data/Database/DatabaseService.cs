using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNetForge.Abstractions.Database;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database;

/// <summary>
/// <see cref="IDatabaseService"/>: validate → route → execute with timeout → normalize → log (.docs/database/architecture.md).
/// Logs carry the database name, provider, operation, collection, duration and record count - never values,
/// connection strings or driver messages (which can echo data).
/// </summary>
public sealed partial class DatabaseService : IDatabaseService
{
    /// <summary>Applies when a command sets no <see cref="DatabaseCommand.Timeout"/>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Commands slower than this are logged at Information level (otherwise Debug).</summary>
    public static readonly TimeSpan SlowCommandThreshold = TimeSpan.FromMilliseconds(500);

    private readonly QueryRouter _router;
    private readonly DatabaseCatalog _catalog;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(QueryRouter router, DatabaseCatalog catalog, ILogger<DatabaseService> logger)
    {
        _router = router;
        _catalog = catalog;
        _logger = logger;
    }

    public IReadOnlyList<DatabaseDescriptor> Databases => _catalog.All.Select(d => d.ToDescriptor()).ToList();

    public Task<DatabaseResult<IReadOnlyList<DatabaseRecord>>> QueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        RunQueryAsync(command, transaction, throwOnError: true, cancellationToken);

    public Task<DatabaseResult<IReadOnlyList<DatabaseRecord>>> TryQueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        RunQueryAsync(command, transaction, throwOnError: false, cancellationToken);

    public Task<DatabaseResult<long>> ExecuteAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        RunExecuteAsync(command, transaction, throwOnError: true, cancellationToken);

    public Task<DatabaseResult<long>> TryExecuteAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        RunExecuteAsync(command, transaction, throwOnError: false, cancellationToken);

    public async IAsyncEnumerable<DatabaseRecord> StreamAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (database, executor, effective) = Prepare(command, transaction, query: true);
        using var timeout = new CancellationTokenSource(effective.Timeout!.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stopwatch = Stopwatch.StartNew();
        long count = 0;

        await using var enumerator = executor.QueryAsync(effective, transaction, linked.Token).GetAsyncEnumerator(linked.Token);
        while (true)
        {
            DatabaseRecord record;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                record = enumerator.Current;
            }
            catch (Exception ex) when (Translate(ex, database, effective, timeout, cancellationToken) is { } error)
            {
                LogFailed(database, effective, stopwatch.Elapsed, error);
                throw error;
            }

            count++;
            yield return record;
        }

        LogCompleted(database, effective, stopwatch.Elapsed, count);
    }

    public async Task<IDatabaseTransaction> BeginTransactionAsync(string? database = null, CancellationToken cancellationToken = default)
    {
        var (target, executor) = _router.Route(database);
        try
        {
            return await executor.BeginTransactionAsync(target.Name, cancellationToken);
        }
        catch (Exception ex) when (target.Provider.TranslateException(ex) is { } error)
        {
            throw error.WithContext(target.Provider.DisplayName, target.Name, operation: null, collection: null);
        }
    }

    public async Task<DatabaseResult<DatabaseConnectionInfo>> TestConnectionAsync(string? database = null, CancellationToken cancellationToken = default)
    {
        ResolvedDatabase target;
        IDatabaseExecutor executor;
        try
        {
            (target, executor) = _router.Route(database);
        }
        catch (DatabaseException ex)
        {
            return new DatabaseResult<DatabaseConnectionInfo> { Provider = "unknown", Database = database ?? "main", Error = ex };
        }

        using var timeout = new CancellationTokenSource(DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await executor.TestConnectionAsync(linked.Token);
            var info = new DatabaseConnectionInfo(target.Provider.DisplayName, target.Name, target.Provider.Describe(target.Settings), stopwatch.Elapsed);
            return new DatabaseResult<DatabaseConnectionInfo> { Data = info, ExecutionTime = stopwatch.Elapsed, Provider = info.Provider, Database = target.Name };
        }
        catch (Exception ex) when (target.Provider.TranslateException(ex) is { } error ||
                                  (ex is OperationCanceledException && timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested))
        {
            var translated = (ex is OperationCanceledException
                    ? new DatabaseTimeoutException("The connection test timed out.", ex)
                    : target.Provider.TranslateException(ex)!)
                .WithContext(target.Provider.DisplayName, target.Name, operation: null, collection: null);
            LogConnectionFailed(target.Name, target.Provider.DisplayName, translated.GetType().Name);
            return new DatabaseResult<DatabaseConnectionInfo>
            {
                ExecutionTime = stopwatch.Elapsed,
                Provider = target.Provider.DisplayName,
                Database = target.Name,
                Error = translated,
            };
        }
    }

    private async Task<DatabaseResult<IReadOnlyList<DatabaseRecord>>> RunQueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction, bool throwOnError, CancellationToken cancellationToken)
    {
        var records = new List<DatabaseRecord>();
        return await RunAsync<IReadOnlyList<DatabaseRecord>>(command, transaction, throwOnError, query: true, cancellationToken,
            async (executor, effective, token) =>
            {
                await foreach (var record in executor.QueryAsync(effective, transaction, token).WithCancellation(token))
                {
                    records.Add(record);
                }

                return (records, records.Count);
            });
    }

    private Task<DatabaseResult<long>> RunExecuteAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction, bool throwOnError, CancellationToken cancellationToken) =>
        RunAsync(command, transaction, throwOnError, query: false, cancellationToken,
            async (executor, effective, token) =>
            {
                var affected = await executor.ExecuteAsync(effective, transaction, token);
                return (affected, affected);
            });

    private async Task<DatabaseResult<T>> RunAsync<T>(
        DatabaseCommand command, IDatabaseTransaction? transaction, bool throwOnError, bool query, CancellationToken cancellationToken,
        Func<IDatabaseExecutor, DatabaseCommand, CancellationToken, Task<(T Data, long Affected)>> run)
    {
        ResolvedDatabase database;
        IDatabaseExecutor executor;
        DatabaseCommand effective;
        try
        {
            (database, executor, effective) = Prepare(command, transaction, query);
        }
        catch (DatabaseException ex) when (!throwOnError)
        {
            return new DatabaseResult<T> { Provider = "unknown", Database = command.Database ?? "main", Error = ex };
        }

        using var timeout = new CancellationTokenSource(effective.Timeout!.Value);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (data, affected) = await run(executor, effective, linked.Token);
            LogCompleted(database, effective, stopwatch.Elapsed, affected);
            return new DatabaseResult<T>
            {
                Data = data,
                AffectedRows = affected,
                ExecutionTime = stopwatch.Elapsed,
                Provider = database.Provider.DisplayName,
                Database = database.Name,
            };
        }
        catch (Exception ex) when (Translate(ex, database, effective, timeout, cancellationToken) is { } error)
        {
            LogFailed(database, effective, stopwatch.Elapsed, error);
            if (throwOnError)
            {
                throw error;
            }

            return new DatabaseResult<T>
            {
                ExecutionTime = stopwatch.Elapsed,
                Provider = database.Provider.DisplayName,
                Database = database.Name,
                Error = error,
            };
        }
    }

    /// <summary>Validates the command, resolves the target and fills in the effective timeout and FindOne's limit.</summary>
    private (ResolvedDatabase Database, IDatabaseExecutor Executor, DatabaseCommand Effective) Prepare(
        DatabaseCommand command, IDatabaseTransaction? transaction, bool query)
    {
        DatabaseCommandValidator.Validate(command, query);
        var (database, executor) = _router.Route(command.Database);
        if (transaction is not null && !string.Equals(transaction.Database, database.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseQueryException($"The transaction belongs to database '{transaction.Database}', not '{database.Name}'.");
        }

        var effective = command with
        {
            Timeout = command.Timeout ?? DefaultTimeout,
            Take = command.Operation == DatabaseOperation.FindOne ? 1 : command.Take,
        };
        return (database, executor, effective);
    }

    /// <summary>
    /// Database errors become <see cref="DatabaseException"/>s with context; our own timeout becomes
    /// <see cref="DatabaseTimeoutException"/>; caller cancellation and non-database exceptions (bugs) propagate as they are.
    /// </summary>
    private static DatabaseException? Translate(
        Exception ex, ResolvedDatabase database, DatabaseCommand command, CancellationTokenSource timeout, CancellationToken callerToken)
    {
        if (ex is OperationCanceledException && callerToken.IsCancellationRequested)
        {
            return null;
        }

        var error = ex is OperationCanceledException && timeout.IsCancellationRequested
            ? new DatabaseTimeoutException($"The {command.Operation} on '{command.Collection}' exceeded its timeout of {command.Timeout!.Value.TotalSeconds:0.###} s.", ex)
            : database.Provider.TranslateException(ex);
        return error?.WithContext(database.Provider.DisplayName, database.Name, command.Operation, command.Collection);
    }

    private void LogCompleted(ResolvedDatabase database, DatabaseCommand command, TimeSpan elapsed, long records)
    {
        if (elapsed >= SlowCommandThreshold)
        {
            LogSlow(database.Name, database.Provider.DisplayName, command.Operation, command.Collection, elapsed.TotalMilliseconds, records);
        }
        else
        {
            LogExecuted(database.Name, database.Provider.DisplayName, command.Operation, command.Collection, elapsed.TotalMilliseconds, records);
        }
    }

    private void LogFailed(ResolvedDatabase database, DatabaseCommand command, TimeSpan elapsed, DatabaseException error) =>
        LogFailure(database.Name, database.Provider.DisplayName, command.Operation, command.Collection, elapsed.TotalMilliseconds,
            error.GetType().Name, error.Message);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Database {Database} ({Provider}) {Operation} {Collection}: {Records} records in {ElapsedMs:0.0} ms")]
    private partial void LogExecuted(string database, string provider, DatabaseOperation operation, string collection, double elapsedMs, long records);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Slow database command: {Database} ({Provider}) {Operation} {Collection}: {Records} records in {ElapsedMs:0.0} ms")]
    private partial void LogSlow(string database, string provider, DatabaseOperation operation, string collection, double elapsedMs, long records);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Database {Database} ({Provider}) {Operation} {Collection} failed after {ElapsedMs:0.0} ms: {ErrorType}: {ErrorMessage}")]
    private partial void LogFailure(string database, string provider, DatabaseOperation operation, string collection, double elapsedMs, string errorType, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection test for database {Database} ({Provider}) failed: {ErrorType}")]
    private partial void LogConnectionFailed(string database, string provider, string errorType);
}
