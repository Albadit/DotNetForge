using System.Data.Common;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using MySql.EntityFrameworkCore.Extensions;
using Npgsql;

namespace DotNetForge.Data.Database.Relational;

/// <summary>
/// SQLite: the zero-setup default. An empty connection string means <c>storage/dotnetforge.db</c> in Development;
/// elsewhere the file must be an absolute path on a writable volume (the deployment directory is read-only).
/// Migrations: <c>Migrations/</c> (<see cref="DotNetForgeDbContext"/>).
/// </summary>
public sealed class SqliteDatabaseProvider : RelationalDatabaseProvider<DotNetForgeDbContext>
{
    private static readonly string[] ServerKeys = { "Server", "Host", "Initial Catalog", "Integrated Security", "Trusted_Connection", "User ID", "Uid" };

    public override string DisplayName => "SQLite";

    protected override DbProviderFactory Factory => SqliteFactory.Instance;

    protected override SqlDialect Dialect { get; } = new SqliteDialect();

    /// <summary>Empty, or a file data source without any server-style key (those belong to SQL Server).</summary>
    public override bool CanHandle(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        var keys = ConnectionStrings.Keys(connectionString);
        return (keys.Contains("Data Source") || keys.Contains("DataSource") || keys.Contains("Filename")) && !ServerKeys.Any(keys.Contains);
    }

    public override DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
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

    public override string Describe(DatabaseSettings settings) =>
        $"SQLite {SqliteConnectionStrings.GetDataSource(settings.ConnectionString ?? string.Empty)}";

    protected override void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString) => Configure(options, connectionString);

    /// <summary>Also used by <c>DesignTimeDbContextFactory</c>.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        EnsureDirectory(connectionString);
        options.UseSqlite(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));
    }

    protected override DatabaseException? TranslateDriverException(Exception exception) => exception switch
    {
        SqliteException { SqliteErrorCode: 19 } e when e.Message.Contains("UNIQUE", StringComparison.Ordinal) || e.Message.Contains("PRIMARY KEY", StringComparison.Ordinal)
            => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        SqliteException { SqliteErrorCode: 14 } => new DatabaseConnectionException("The SQLite database file could not be opened.", exception),
        SqliteException { SqliteErrorCode: 5 or 6 } => new DatabaseTimeoutException("The SQLite database is locked by another writer.", exception),
        SqliteException e when e.Message.Contains("no such table", StringComparison.Ordinal) || e.Message.Contains("no such column", StringComparison.Ordinal)
            => new DatabaseNotFoundException("The table or column does not exist.", exception),
        _ => null,
    };

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

/// <summary>PostgreSQL through Npgsql. Detected from a <c>Host=</c> connection string. Migrations: <c>Migrations/PostgreSql/</c>.</summary>
public sealed class PostgreSqlDatabaseProvider : RelationalDatabaseProvider<PostgreSqlDbContext>
{
    public override string DisplayName => "PostgreSQL";

    protected override DbProviderFactory Factory => NpgsqlFactory.Instance;

    protected override SqlDialect Dialect { get; } = new PostgreSqlDialect();

    public override bool CanHandle(string? connectionString)
    {
        var keys = ConnectionStrings.Keys(connectionString);
        return keys.Contains("Host") && !keys.Contains("Initial Catalog");
    }

    public override DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
    {
        var value = settings.ConnectionString?.Trim() ?? string.Empty;
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseConfigurationException(
                "PostgreSQL connection strings must use the key=value form, not a URL: " +
                "'Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>'.");
        }

        return base.Normalize(settings, host);
    }

    protected override void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString) => Configure(options, connectionString);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    protected override DatabaseException? TranslateDriverException(Exception exception) => exception switch
    {
        PostgresException { SqlState: "28P01" or "28000" } => new DatabaseAuthenticationException("PostgreSQL rejected the credentials.", exception),
        PostgresException { SqlState: "3D000" } => new DatabaseNotFoundException("The PostgreSQL database does not exist.", exception),
        PostgresException { SqlState: "42P01" or "42703" } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        PostgresException { SqlState: "23505" } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        PostgresException { SqlState: "57014" } => new DatabaseTimeoutException("PostgreSQL canceled the command (timeout).", exception),
        PostgresException { SqlState: var state } when state.StartsWith("08", StringComparison.Ordinal) || state == "53300"
            => new DatabaseConnectionException("The connection to PostgreSQL failed.", exception),
        NpgsqlException { InnerException: TimeoutException } => new DatabaseTimeoutException("The PostgreSQL command timed out.", exception),
        NpgsqlException and not PostgresException => new DatabaseConnectionException("PostgreSQL could not be reached.", exception),
        _ => null,
    };
}

/// <summary>
/// SQL Server through Microsoft.Data.SqlClient. Detected from <c>Initial Catalog</c>, <c>Trusted_Connection</c>,
/// <c>Integrated Security</c> or <c>TrustServerCertificate</c>. Migrations: <c>Migrations/SqlServer/</c>.
/// </summary>
public sealed class SqlServerDatabaseProvider : RelationalDatabaseProvider<SqlServerDbContext>
{
    private static readonly string[] Markers = { "Initial Catalog", "Trusted_Connection", "Integrated Security", "TrustServerCertificate" };

    public override string DisplayName => "SQL Server";

    protected override DbProviderFactory Factory => SqlClientFactory.Instance;

    protected override SqlDialect Dialect { get; } = new SqlServerDialect();

    public override bool CanHandle(string? connectionString) => Markers.Any(ConnectionStrings.Keys(connectionString).Contains);

    protected override void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString) => Configure(options, connectionString);

    /// <summary>No retrying execution strategy: it can't be combined with the explicit transactions the CMS uses.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    protected override DatabaseException? TranslateDriverException(Exception exception) => exception switch
    {
        SqlException { Number: 18456 } => new DatabaseAuthenticationException("SQL Server rejected the credentials.", exception),
        SqlException { Number: 4060 } => new DatabaseNotFoundException("The SQL Server database does not exist or can't be opened.", exception),
        SqlException { Number: 208 or 207 } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        SqlException { Number: 2627 or 2601 } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        SqlException { Number: -2 } => new DatabaseTimeoutException("The SQL Server command timed out.", exception),
        // Connection-level failures come with severity 20+ and many numbers (53, 258, 10061, 40613, ...).
        SqlException { Number: 53 or -1 or 2 or 258 or 10060 or 10061 or 11001 or 40613 } or SqlException { Class: >= 20 }
            => new DatabaseConnectionException("SQL Server could not be reached.", exception),
        _ => null,
    };
}

/// <summary>
/// MySQL 8 through Oracle's MySql.EntityFrameworkCore (the Pomelo provider has no EF Core 10 release). Never
/// auto-detected - <c>Server=</c> strings are ambiguous - so set <c>DATABASE_PROVIDER=mysql</c>. Migrations:
/// <c>Migrations/MySql/</c>.
/// </summary>
public sealed class MySqlDatabaseProvider : RelationalDatabaseProvider<MySqlDbContext>
{
    public override string DisplayName => "MySQL";

    protected override DbProviderFactory Factory => MySqlClientFactory.Instance;

    protected override SqlDialect Dialect { get; } = new MySqlDialect();

    public override bool CanHandle(string? connectionString) => false;

    protected override void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString) => Configure(options, connectionString);

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseMySQL(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    protected override DatabaseException? TranslateDriverException(Exception exception) => exception switch
    {
        MySqlException { Number: 1045 } => new DatabaseAuthenticationException("MySQL rejected the credentials.", exception),
        MySqlException { Number: 1049 } => new DatabaseNotFoundException("The MySQL database does not exist.", exception),
        MySqlException { Number: 1146 or 1054 } => new DatabaseNotFoundException("The table or column does not exist.", exception),
        MySqlException { Number: 1062 } => new DatabaseConflictException("A record with the same unique value already exists.", exception),
        MySqlException { Number: 3024 or 1317 } => new DatabaseTimeoutException("MySQL interrupted the command (timeout).", exception),
        MySqlException { Number: 1042 or 1040 or 2003 or 2013 } => new DatabaseConnectionException("MySQL could not be reached.", exception),
        MySqlException { InnerException: TimeoutException } => new DatabaseTimeoutException("The MySQL command timed out.", exception),
        _ => null,
    };
}
