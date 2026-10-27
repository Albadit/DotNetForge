using System.Data.Common;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database.Providers.Sqlite;

/// <summary>
/// SQLite for additional databases (<c>DATABASES_&lt;NAME&gt;_*</c>; the CMS itself runs on PostgreSQL). An empty
/// connection string means <c>storage/&lt;name&gt;.db</c> in Development; elsewhere the file must be an absolute path on
/// a writable volume (the deployment directory is read-only).
/// Self-contained: no code is shared with the other SQL providers, so a change here never affects them.
/// </summary>
public sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    private static readonly string[] ServerKeys = { "Server", "Host", "Initial Catalog", "Integrated Security", "Trusted_Connection", "User ID", "Uid" };

    public string DisplayName => "SQLite";

    /// <summary>Empty, or a file data source without any server-style key (those belong to SQL Server).</summary>
    public bool CanHandle(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        var keys = Keys(connectionString);
        return (keys.Contains("Data Source") || keys.Contains("DataSource") || keys.Contains("Filename")) && !ServerKeys.Any(keys.Contains);
    }

    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
    {
        var key = settings.IsMain ? "DATABASE_CONNECTION_STRING" : $"DATABASES_{settings.Name.ToUpperInvariant()}_CONNECTION_STRING";
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            if (!host.IsDevelopment)
            {
                throw new DatabaseConfigurationException(
                    $"{key} is required outside Development: the deployment directory is read-only, so point it at a " +
                    "database server (PostgreSQL, SQL Server, MySQL, MongoDB) or at a SQLite file on a writable volume " +
                    "('Data Source=/data/dotnetforge.db').");
            }

            // Development default: storage/ at the repository root (or the content root outside a checkout).
            var file = Path.Combine(host.DevelopmentDataRoot, "storage", settings.IsMain ? "dotnetforge.db" : $"{settings.Name}.db");
            return settings with { ConnectionString = $"Data Source={file}" };
        }

        var dataSource = SqliteConnectionStrings.GetDataSource(settings.ConnectionString);
        if (!host.IsDevelopment && dataSource is not null && !SqliteConnectionStrings.IsInMemory(dataSource) && !Path.IsPathRooted(dataSource))
        {
            throw new DatabaseConfigurationException(
                $"The SQLite data source must be an absolute path outside Development (got '{dataSource}').");
        }

        return settings;
    }

    public string Describe(DatabaseSettings settings) =>
        $"SQLite {SqliteConnectionStrings.GetDataSource(settings.ConnectionString ?? string.Empty)}";

    /// <summary>The CMS runs on PostgreSQL only; SQLite serves additional databases (<c>DATABASES_&lt;NAME&gt;_*</c>).</summary>
    public void AddDbContext(IServiceCollection services, DatabaseSettings settings) =>
        throw new DatabaseConfigurationException(
            "The CMS database must be PostgreSQL; SQLite can only be an additional database (DATABASES_<NAME>_PROVIDER=sqlite).");

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        throw new NotSupportedException("SQLite does not host the CMS model.");

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory)
    {
        EnsureDirectory(settings.ConnectionString!);
        return new SqliteExecutor(settings, model is null ? DatabaseSchema.Unmapped : Schema(model));
    }

    public System.Data.Common.DbConnection? CreateConnection(DatabaseSettings settings)
    {
        var connection = Microsoft.Data.Sqlite.SqliteFactory.Instance.CreateConnection()
            ?? throw new DatabaseProviderException("The database driver could not create a connection.");
        connection.ConnectionString = settings.ConnectionString;
        return connection;
    }

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        DatabaseException translated => translated,
        SqliteException { SqliteErrorCode: 19 } e when e.Message.Contains("UNIQUE", StringComparison.Ordinal) || e.Message.Contains("PRIMARY KEY", StringComparison.Ordinal)
            => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        SqliteException { SqliteErrorCode: 14 } => new DatabaseConnectionException("The SQLite database file could not be opened.", exception),
        SqliteException { SqliteErrorCode: 5 or 6 } => new DatabaseTimeoutException("The SQLite database is locked by another writer.", exception),
        SqliteException e when e.Message.Contains("no such table", StringComparison.Ordinal) || e.Message.Contains("no such column", StringComparison.Ordinal)
            => new DatabaseNotFoundException("The table or column does not exist.", exception),
        TimeoutException => new DatabaseTimeoutException("The database command timed out.", exception),
        DbException => new DatabaseQueryException("The database rejected the command.", exception),
        _ => null,
    };

    /// <summary>Model names → table and column names, as EF Core's relational mapping defines them.</summary>
    public static DatabaseSchema Schema(IModel model) => DatabaseSchema.FromModel(
        model,
        entityType => (entityType.GetTableName()!, entityType.GetSchema()),
        (entityType, property) => property.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()))!);

    /// <summary>The keys present in a connection string (case-insensitive); empty for null or unparsable strings.</summary>
    private static ISet<string> Keys(string connectionString)
    {
        try
        {
            return new DbConnectionStringBuilder { ConnectionString = connectionString }.Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Creates the folder of a file database. Outside Development the path is an explicitly configured volume, so this
    /// never writes into the deployment directory.
    /// </summary>
    private static void EnsureDirectory(string connectionString)
    {
        var dataSource = SqliteConnectionStrings.GetDataSource(connectionString);
        if (string.IsNullOrEmpty(dataSource) || SqliteConnectionStrings.IsInMemory(dataSource))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
