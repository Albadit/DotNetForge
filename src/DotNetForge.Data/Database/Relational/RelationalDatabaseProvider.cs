using System.Data.Common;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database.Relational;

/// <summary>
/// Shared behavior of SQL providers: EF Core with a per-provider migration set (one context type each, so
/// <typeparamref name="TContext"/> carries its own <c>Migrations/&lt;Provider&gt;/</c>), schema upgrades through
/// migrations, and command execution through <see cref="RelationalExecutor"/>. A concrete provider supplies the
/// driver, the dialect, connection-string rules and error codes.
/// </summary>
public abstract class RelationalDatabaseProvider<TContext> : IDatabaseProvider
    where TContext : DotNetForgeDbContext
{
    /// <summary>All migration sets live in the Data assembly.</summary>
    protected static readonly string MigrationsAssembly = typeof(DotNetForgeDbContext).Assembly.FullName!;

    public abstract string DisplayName { get; }

    /// <summary>The driver's ADO.NET factory (connections are pooled by the driver).</summary>
    protected abstract DbProviderFactory Factory { get; }

    protected abstract SqlDialect Dialect { get; }

    public abstract bool CanHandle(string? connectionString);

    /// <summary>Points EF Core at this database (also used by the design-time factory for <c>dotnet ef</c>).</summary>
    protected abstract void ConfigureEfCore(DbContextOptionsBuilder options, string connectionString);

    /// <summary>Provider-specific error mapping; return <c>null</c> to fall back to the generic mapping.</summary>
    protected abstract DatabaseException? TranslateDriverException(Exception exception);

    /// <summary>Requires a connection string; providers with defaults (SQLite) override.</summary>
    public virtual DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new DatabaseConfigurationException(
                $"{DisplayName} needs a connection string for database '{settings.Name}' " +
                $"({(settings.IsMain ? "DATABASE_CONNECTION_STRING" : $"DATABASES_{settings.Name.ToUpperInvariant()}_CONNECTION_STRING")}).");
        }

        return settings;
    }

    public virtual string Describe(DatabaseSettings settings) =>
        ConnectionStrings.Describe(settings.ConnectionString, DisplayName);

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings) =>
        services.AddDbContext<DotNetForgeDbContext, TContext>(options => ConfigureEfCore(options, settings.ConnectionString!));

    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory) =>
        new RelationalExecutor(Factory, Dialect, settings, model is null ? DatabaseSchema.Unmapped : RelationalSchema.For(model));

    public DatabaseException? TranslateException(Exception exception) =>
        TranslateDriverException(exception) ?? exception switch
        {
            DatabaseException translated => translated,
            TimeoutException => new DatabaseTimeoutException("The database command timed out.", exception),
            DbException => new DatabaseQueryException("The database rejected the command.", exception),
            _ => null,
        };
}

/// <summary>Model names → table and column names, as EF Core's relational mapping defines them.</summary>
public static class RelationalSchema
{
    public static DatabaseSchema For(IModel model) => DatabaseSchema.FromModel(
        model,
        entityType => (entityType.GetTableName()!, entityType.GetSchema()),
        (entityType, property) => property.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()))!);
}

/// <summary>Helpers for key=value connection strings that never expose secrets.</summary>
public static class ConnectionStrings
{
    private static readonly string[] ServerKeys = { "Host", "Server", "Data Source", "DataSource", "Address", "Addr", "Network Address", "Filename" };
    private static readonly string[] DatabaseKeys = { "Database", "Initial Catalog", "Db" };

    /// <summary>The keys present in a connection string (case-insensitive); empty for null or unparsable strings.</summary>
    public static ISet<string> Keys(string? connectionString)
    {
        var builder = TryParse(connectionString);
        return builder is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : builder.Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary><c>PostgreSQL db.internal:5432/cms</c>-style description; credentials are never included.</summary>
    public static string Describe(string? connectionString, string provider)
    {
        var builder = TryParse(connectionString);
        if (builder is null)
        {
            return provider;
        }

        string? Find(IEnumerable<string> keys) =>
            keys.Select(k => builder.TryGetValue(k, out var v) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var server = Find(ServerKeys);
        var port = Find(new[] { "Port" });
        var database = Find(DatabaseKeys);
        return $"{provider} {server}{(port is null ? "" : ":" + port)}{(database is null ? "" : "/" + database)}".TrimEnd();
    }

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
