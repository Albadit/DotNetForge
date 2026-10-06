using System.Data.Common;
using System.Globalization;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;

namespace DotNetForge.Data.Database.Providers.MySql;

/// <summary>
/// MySQL 8 through Oracle's MySql.EntityFrameworkCore (the Pomelo provider has no EF Core 10 release). Never
/// auto-detected - <c>Server=</c> strings are ambiguous - so set <c>DATABASE_PROVIDER=mysql</c>. Context:
/// <see cref="MySqlDbContext"/>; migrations in <c>Migrations/</c> next to this file.
/// Self-contained: no code is shared with the other SQL providers, so a change here never affects them.
/// </summary>
public sealed class MySqlDatabaseProvider : IDatabaseProvider
{
    private static readonly string MigrationsAssembly = typeof(MySqlDatabaseProvider).Assembly.FullName!;

    public string DisplayName => "MySQL";

    public bool CanHandle(string? connectionString) => false;

    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host) => RequireConnectionString(settings);

    public string Describe(DatabaseSettings settings) => DescribeConnection(settings.ConnectionString);

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings) =>
        services.AddDbContext<DotNetForgeDbContext, MySqlDbContext>(options => Configure(options, settings.ConnectionString!));

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory) =>
        new MySqlExecutor(settings, model is null ? DatabaseSchema.Unmapped : Schema(model));

    /// <summary>Also used by <c>DesignTimeDbContextFactory</c>.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseMySQL(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        DatabaseException translated => translated,
        MySqlException { Number: 1045 } => new DatabaseAuthenticationException("MySQL rejected the credentials.", exception),
        MySqlException { Number: 1049 } => new DatabaseNotFoundException("The MySQL database does not exist.", exception),
        MySqlException { Number: 1146 or 1054 } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        MySqlException { Number: 1062 } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        MySqlException { Number: 3024 or 1317 } => new DatabaseTimeoutException("MySQL interrupted the command (timeout).", exception),
        MySqlException { Number: 1042 or 1040 or 2003 or 2013 } => new DatabaseConnectionException("MySQL could not be reached.", exception),
        MySqlException { InnerException: TimeoutException } => new DatabaseTimeoutException("The MySQL command timed out.", exception),
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

    /// <summary><c>MySQL server:port/database</c>; credentials are never included.</summary>
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

        var server = Find("Server", "Host", "Data Source", "DataSource", "Address", "Addr", "Network Address");
        var port = Find("Port");
        var database = Find("Database", "Initial Catalog", "Db");
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
