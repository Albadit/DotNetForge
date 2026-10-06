using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database;

/// <summary>
/// Everything the system needs to know about one database technology (.docs/database/providers.md). A provider is a
/// stateless strategy object: it validates configuration, wires EF Core for the main database, prepares the schema,
/// executes <see cref="DatabaseCommand"/>s natively and translates its driver's errors. Nothing outside the provider
/// knows how its database works, and adding a database means adding a provider and registering it
/// (.docs/database/adding-a-provider.md) - no other code changes.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Human-readable name used in results, logs and errors, e.g. <c>PostgreSQL</c>.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Whether <paramref name="connectionString"/> unambiguously belongs to this database. Only used when no provider is
    /// configured; return <c>false</c> when unsure - several matches are reported as ambiguous.
    /// </summary>
    bool CanHandle(string? connectionString);

    /// <summary>
    /// Validates and normalizes settings (defaults, required parts). Throws <see cref="DatabaseConfigurationException"/>
    /// with an actionable message; startup stops on it.
    /// </summary>
    DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host);

    /// <summary>A description of the server/database for logs and diagnostics. Must never include credentials.</summary>
    string Describe(DatabaseSettings settings);

    /// <summary>
    /// Registers <see cref="DotNetForgeDbContext"/> for the main database. Providers that can't host the CMS model throw
    /// <see cref="DatabaseConfigurationException"/>.
    /// </summary>
    void AddDbContext(IServiceCollection services, DatabaseSettings settings);

    /// <summary>Creates or upgrades the schema of the main database (migrations, or collections and indexes).</summary>
    Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the executor that runs <see cref="DatabaseCommand"/>s natively. Called once per configured database and
    /// cached, so it may hold pooled clients. <paramref name="model"/> is the CMS model for the main database (names
    /// are mapped and validated against it) and <c>null</c> for additional databases.
    /// </summary>
    IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory);

    /// <summary>
    /// Translates a driver exception into a <see cref="DatabaseException"/>, or returns <c>null</c> when it isn't a
    /// database error (bugs propagate unchanged).
    /// </summary>
    DatabaseException? TranslateException(Exception exception);
}

/// <summary>Runs commands against one configured database. Implementations are thread-safe singletons.</summary>
public interface IDatabaseExecutor : IAsyncDisposable
{
    /// <summary>Find, FindOne, Aggregate: records are produced lazily (data reader / server cursor).</summary>
    IAsyncEnumerable<DatabaseRecord> QueryAsync(DatabaseCommand command, IDatabaseTransaction? transaction, CancellationToken cancellationToken);

    /// <summary>Count, Insert*, Update*, Delete*: returns the count or the number of affected records.</summary>
    Task<long> ExecuteAsync(DatabaseCommand command, IDatabaseTransaction? transaction, CancellationToken cancellationToken);

    Task<IDatabaseTransaction> BeginTransactionAsync(string database, CancellationToken cancellationToken);

    /// <summary>Opens a connection and runs a trivial command; throws the driver exception on failure.</summary>
    Task TestConnectionAsync(CancellationToken cancellationToken);
}

/// <summary>What providers may need to know about the host when normalizing settings.</summary>
/// <param name="IsDevelopment">Outside Development nothing may default into the read-only deployment directory.</param>
/// <param name="DevelopmentDataRoot">Folder for development defaults (<c>storage/</c> lives under it).</param>
public sealed record DatabaseHostContext(bool IsDevelopment, string DevelopmentDataRoot);

/// <summary>A configured database bound to its provider.</summary>
public sealed record ResolvedDatabase(DatabaseSettings Settings, string ProviderName, IDatabaseProvider Provider)
{
    public string Name => Settings.Name;

    public DatabaseDescriptor ToDescriptor() =>
        new(Settings.Name, Provider.DisplayName, Provider.Describe(Settings), Settings.IsMain);
}
