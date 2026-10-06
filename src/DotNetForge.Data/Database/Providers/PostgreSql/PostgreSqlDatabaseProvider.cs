using System.Data.Common;
using System.Globalization;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DotNetForge.Data.Database.Providers.PostgreSql;

/// <summary>
/// PostgreSQL through Npgsql. Detected from a <c>Host=</c> connection string. Context: <see cref="PostgreSqlDbContext"/>;
/// migrations in <c>Migrations/</c> next to this file.
/// Self-contained: no code is shared with the other SQL providers, so a change here never affects them.
/// </summary>
public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
{
    private static readonly string MigrationsAssembly = typeof(PostgreSqlDatabaseProvider).Assembly.FullName!;

    public string DisplayName => "PostgreSQL";

    public bool CanHandle(string? connectionString)
    {
        var keys = Keys(connectionString);
        return keys.Contains("Host") && !keys.Contains("Initial Catalog");
    }

    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
    {
        var value = settings.ConnectionString?.Trim() ?? string.Empty;
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseConfigurationException(
                "PostgreSQL connection strings must use the key=value form, not a URL: " +
                "'Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>'.");
        }

        return RequireConnectionString(settings);
    }

    public string Describe(DatabaseSettings settings) => DescribeConnection(settings.ConnectionString);

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings) =>
        services.AddDbContext<DotNetForgeDbContext, PostgreSqlDbContext>(options => Configure(options, settings.ConnectionString!));

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory) =>
        new PostgreSqlExecutor(settings, model is null ? DatabaseSchema.Unmapped : Schema(model));

    /// <summary>Also used by <c>DesignTimeDbContextFactory</c>.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        DatabaseException translated => translated,
        PostgresException { SqlState: "28P01" or "28000" } => new DatabaseAuthenticationException("PostgreSQL rejected the credentials.", exception),
        PostgresException { SqlState: "3D000" } => new DatabaseNotFoundException("The PostgreSQL database does not exist.", exception),
        PostgresException { SqlState: "42P01" or "42703" } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        PostgresException { SqlState: "23505" } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        PostgresException { SqlState: "57014" } => new DatabaseTimeoutException("PostgreSQL canceled the command (timeout).", exception),
        PostgresException { SqlState: var state } when state.StartsWith("08", StringComparison.Ordinal) || state == "53300"
            => new DatabaseConnectionException("The connection to PostgreSQL failed.", exception),
        NpgsqlException { InnerException: TimeoutException } => new DatabaseTimeoutException("The PostgreSQL command timed out.", exception),
        NpgsqlException and not PostgresException => new DatabaseConnectionException("PostgreSQL could not be reached.", exception),
        TimeoutException => new DatabaseTimeoutException("The database command timed out.", exception),
        DbException => new DatabaseQueryException("The database rejected the command.", exception),
        _ => null,
    };

    /// <summary>Model names → table and column names, as EF Core's relational mapping defines them.</summary>
    public static DatabaseSchema Schema(IModel model) => DatabaseSchema.FromModel(
        model,
        entityType => (entityType.GetTableName()!, entityType.GetSchema()),
        (entityType, property) => property.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()))!);

    /// <summary>Requires a connection string.</summary>
    private DatabaseSettings RequireConnectionString(DatabaseSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new DatabaseConfigurationException(
                $"{DisplayName} needs a connection string for database '{settings.Name}' " +
                $"({(settings.IsMain ? "DATABASE_CONNECTION_STRING" : $"DATABASES_{settings.Name.ToUpperInvariant()}_CONNECTION_STRING")}).");
        }

        return settings;
    }

    /// <summary><c>PostgreSQL server:port/database</c>; credentials are never included.</summary>
    private string DescribeConnection(string? connectionString)
    {
        var builder = TryParse(connectionString);
        if (builder is null)
        {
            return DisplayName;
        }

        string? Find(params string[] keys) =>
            keys.Select(k => builder.TryGetValue(k, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var server = Find("Host", "Server");
        var port = Find("Port");
        var database = Find("Database");
        return $"{DisplayName} {server}{(port is null ? "" : ":" + port)}{(database is null ? "" : "/" + database)}".TrimEnd();
    }

    /// <summary>The keys present in a connection string (case-insensitive); empty for null or unparsable strings.</summary>
    private static ISet<string> Keys(string? connectionString) =>
        TryParse(connectionString)?.Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static DbConnectionStringBuilder? TryParse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            return new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
