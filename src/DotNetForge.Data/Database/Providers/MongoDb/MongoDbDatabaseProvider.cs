using System.Collections.Concurrent;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.EntityFrameworkCore.Extensions;

namespace DotNetForge.Data.Database.Providers.MongoDb;

/// <summary>
/// MongoDB through the official EF Core provider (the CMS model) and the native C# driver (<see cref="DatabaseCommand"/>s).
/// Both share one <see cref="IMongoClient"/> per connection string - the client is the connection pool and must be
/// reused (.docs/database/mongodb.md). Transactions, and therefore the CMS's multi-document saves, need a replica set
/// or sharded cluster (Atlas always is one).
/// </summary>
public sealed class MongoDbDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// One client per connection string for the whole process, as MongoDB recommends: the client owns the connection
    /// pool and is thread-safe. Sharing it also lets EF Core reuse its internal services across contexts.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IMongoClient> Clients = new(StringComparer.Ordinal);

    public string DisplayName => "MongoDB";

    public bool CanHandle(string? connectionString) =>
        connectionString is not null &&
        (connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
         connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Requires a MongoDB URL with the database name in its path.</summary>
    public DatabaseSettings Normalize(DatabaseSettings settings, DatabaseHostContext host)
    {
        var prefix = settings.IsMain ? "DATABASE" : $"DATABASES_{settings.Name.ToUpperInvariant()}";
        if (!CanHandle(settings.ConnectionString))
        {
            throw new DatabaseConfigurationException(
                $"MongoDB needs a connection string like 'mongodb://<host>:27017/<database>?replicaSet=rs0' in {prefix}_CONNECTION_STRING.");
        }

        MongoUrl url;
        try
        {
            url = MongoUrl.Create(settings.ConnectionString);
        }
        catch (MongoConfigurationException ex)
        {
            throw new DatabaseConfigurationException($"{prefix}_CONNECTION_STRING is not a valid MongoDB connection string.", ex);
        }

        if (string.IsNullOrWhiteSpace(url.DatabaseName))
        {
            throw new DatabaseConfigurationException(
                $"MongoDB needs a database name in the URL path of {prefix}_CONNECTION_STRING (mongodb://host:27017/<database>).");
        }

        return settings with { DatabaseName = url.DatabaseName };
    }

    public string Describe(DatabaseSettings settings)
    {
        try
        {
            var url = MongoUrl.Create(settings.ConnectionString);
            return $"MongoDB {string.Join(",", url.Servers.Select(s => s.ToString()))}/{settings.DatabaseName}";
        }
        catch (MongoConfigurationException)
        {
            return "MongoDB";
        }
    }

    public void AddDbContext(IServiceCollection services, DatabaseSettings settings)
    {
        var client = Client(settings);
        services.AddDbContext<DotNetForgeDbContext, MongoDbContext>(options => options
            .UseMongoDB(client, settings.DatabaseName!)
            // The MongoDB provider keys EF Core's internal services by database name, so a process that hosts many
            // MongoDB databases (the integration tests: one per host) passes EF's limit of 20. One database per
            // process - every deployment - never reaches it; log instead of throwing.
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
    }

    /// <summary>Creates missing collections and the model's indexes (unique ones included); safe on every start.</summary>
    public Task InitializeSchemaAsync(DotNetForgeDbContext db, CancellationToken cancellationToken) =>
        db.Database.EnsureCreatedAsync(cancellationToken);

    public IDatabaseExecutor CreateExecutor(DatabaseSettings settings, IModel? model, ILoggerFactory loggerFactory)
    {
        var schema = model is null ? DatabaseSchema.Unmapped : Schema(model);
        return new MongoExecutor(Client(settings).GetDatabase(settings.DatabaseName), schema);
    }

    /// <summary>Model names → collection names and document paths, as the EF Core MongoDB provider stores them.</summary>
    public static DatabaseSchema Schema(IModel model) =>
        DatabaseSchema.FromModel(model, entityType => (entityType.GetCollectionName(), null), ElementPath);

    public DatabaseException? TranslateException(Exception exception) => exception switch
    {
        DatabaseException translated => translated,
        MongoAuthenticationException => new DatabaseAuthenticationException("MongoDB rejected the credentials.", exception),
        MongoCommandException { Code: 13 or 18 } => new DatabaseAuthenticationException("MongoDB rejected the credentials or the operation is not authorized.", exception),
        MongoConnectionException => new DatabaseConnectionException("The connection to MongoDB failed.", exception),
        TimeoutException when exception.Message.Contains("selecting a server", StringComparison.OrdinalIgnoreCase)
            => new DatabaseConnectionException("MongoDB could not be reached (no server available).", exception),
        MongoExecutionTimeoutException or MongoCommandException { Code: 50 } => new DatabaseTimeoutException("MongoDB stopped the operation (timeout).", exception),
        MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } => Conflict(exception),
        MongoBulkWriteException bulk when bulk.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey) => Conflict(exception),
        MongoCommandException { Code: 11000 } => Conflict(exception),
        MongoCommandException { Code: 20 } or NotSupportedException when exception.Message.Contains("transaction", StringComparison.OrdinalIgnoreCase)
            => new DatabaseProviderException("MongoDB transactions need a replica set or sharded cluster.", exception),
        MongoCommandException { Code: 26 } => new DatabaseNotFoundException("The MongoDB collection does not exist.", exception),
        TimeoutException => new DatabaseTimeoutException("The MongoDB operation timed out.", exception),
        MongoException => new DatabaseQueryException("MongoDB rejected the operation.", exception),
        _ => null,
    };

    private static DatabaseConflictException Conflict(Exception exception) =>
        new("A record with the same unique value already exists.", exception);

    /// <summary>
    /// Where the EF provider stores a property: the key is <c>_id</c>; each part of a composite key is a field of the
    /// <c>_id</c> document.
    /// </summary>
    private static string ElementPath(IReadOnlyEntityType entityType, IReadOnlyProperty property)
    {
        var key = entityType.FindPrimaryKey();
        if (key is null || !key.Properties.Contains(property))
        {
            return property.GetElementName();
        }

        return key.Properties.Count == 1 ? "_id" : $"_id.{property.GetElementName()}";
    }

    private static IMongoClient Client(DatabaseSettings settings) =>
        Clients.GetOrAdd(settings.ConnectionString!, cs => new MongoClient(MongoClientSettings.FromConnectionString(cs)));
}
