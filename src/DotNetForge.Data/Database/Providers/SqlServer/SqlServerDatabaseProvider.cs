using System.Data.Common;
using System.Globalization;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database.Providers.SqlServer;

/// <summary>
/// SQL Server through Microsoft.Data.SqlClient. Detected from <c>Initial Catalog</c>, <c>Trusted_Connection</c>,
/// <c>Integrated Security</c> or <c>TrustServerCertificate</c>. Context: <see cref="SqlServerDbContext"/>; migrations in
/// <c>Migrations/</c> next to this file.
/// Self-contained: no code is shared with the other SQL providers, so a change here never affects them.
/// </summary>
public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    private static readonly string MigrationsAssembly = typeof(SqlServerDatabaseProvider).Assembly.FullName!;
    private static readonly string[] Markers = { "Initial Catalog", "Trusted_Connection", "Integrated Security", "TrustServerCertificate" };

    public string DisplayName => "SQL Server";

    public bool CanHandle(string? connectionString) => Markers.Any(Keys(connectionString).Contains);

    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host) => RequireConnectionString(settings);

    public string Describe(DatabaseSettings settings) => DescribeConnection(settings.ConnectionString);

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings) =>
        services.AddDbContext<DotNetForgeDbContext, SqlServerDbContext>(options => Configure(options, settings.ConnectionString!));

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory) =>
        new SqlServerExecutor(settings, model is null ? DatabaseSchema.Unmapped : Schema(model));

    /// <summary>
    /// Also used by <c>DesignTimeDbContextFactory</c>. No retrying execution strategy: it can't be combined with the
    /// explicit transactions the CMS uses.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        DatabaseException translated => translated,
        SqlException { Number: 18456 } => new DatabaseAuthenticationException("SQL Server rejected the credentials.", exception),
        SqlException { Number: 4060 } => new DatabaseNotFoundException("The SQL Server database does not exist or can't be opened.", exception),
        SqlException { Number: 208 or 207 } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        SqlException { Number: 2627 or 2601 } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        SqlException { Number: -2 } => new DatabaseTimeoutException("The SQL Server command timed out.", exception),
        // Connection-level failures come with severity 20+ and many numbers (53, 258, 10061, 40613, ...).
        SqlException { Number: 53 or -1 or 2 or 258 or 10060 or 10061 or 11001 or 40613 } or SqlException { Class: >= 20 }
            => new DatabaseConnectionException("SQL Server could not be reached.", exception),
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

    /// <summary><c>SQL Server server:port/database</c>; credentials are never included.</summary>
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

        var server = Find("Server", "Data Source", "DataSource", "Address", "Addr", "Network Address");
        var port = Find("Port");
        var database = Find("Database", "Initial Catalog");
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
