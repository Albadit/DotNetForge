namespace DotNetForge.Abstractions.Database;

/// <summary>
/// The single entry point for structured database operations against any configured database and provider
/// (.docs/database/architecture.md). The service validates the command, routes it to the provider of the target
/// database, applies the timeout, translates errors into <see cref="DatabaseException"/> subclasses and logs the call
/// without values or credentials.
/// </summary>
/// <remarks>
/// The CMS's own data access keeps using EF Core LINQ against <c>DotNetForgeDbContext</c>, which runs on the same
/// providers. Use this service for dynamic operations: extensions, tooling, additional databases.
/// </remarks>
public interface IDatabaseService
{
    /// <summary>The configured databases (main first).</summary>
    IReadOnlyList<DatabaseDescriptor> Databases { get; }

    /// <summary>Runs Find, FindOne (at most one record) or Aggregate. Throws <see cref="DatabaseException"/> on failure.</summary>
    Task<DatabaseResult<IReadOnlyList<DatabaseRecord>>> QueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs Count, Insert*, Update* or Delete*; <see cref="DatabaseResult{T}.Data"/> is the count or the number of
    /// affected records. Throws <see cref="DatabaseException"/> on failure.
    /// </summary>
    Task<DatabaseResult<long>> ExecuteAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>Streams Find/Aggregate results without buffering them (server cursor / data reader).</summary>
    IAsyncEnumerable<DatabaseRecord> StreamAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="QueryAsync"/>, but returns a failed result instead of throwing a <see cref="DatabaseException"/>.</summary>
    Task<DatabaseResult<IReadOnlyList<DatabaseRecord>>> TryQueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="ExecuteAsync"/>, but returns a failed result instead of throwing a <see cref="DatabaseException"/>.</summary>
    Task<DatabaseResult<long>> TryExecuteAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction = null, CancellationToken cancellationToken = default);

    /// <summary>Starts a transaction on a database; pass it to the commands that must commit together.</summary>
    Task<IDatabaseTransaction> BeginTransactionAsync(string? database = null, CancellationToken cancellationToken = default);

    /// <summary>Opens a connection and runs a trivial command. Never throws for database errors.</summary>
    Task<DatabaseResult<DatabaseConnectionInfo>> TestConnectionAsync(
        string? database = null, CancellationToken cancellationToken = default);
}

/// <summary>A transaction on one configured database. Disposing without committing rolls back.</summary>
public interface IDatabaseTransaction : IAsyncDisposable
{
    /// <summary>The configured database the transaction belongs to.</summary>
    string Database { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
