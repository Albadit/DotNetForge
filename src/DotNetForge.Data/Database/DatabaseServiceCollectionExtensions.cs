using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database.Providers.MongoDb;
using DotNetForge.Data.Database.Providers.MySql;
using DotNetForge.Data.Database.Providers.PostgreSql;
using DotNetForge.Data.Database.Providers.Sqlite;
using DotNetForge.Data.Database.Providers.SqlServer;
using DotNetForge.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetForge.Data.Database;

/// <summary>Registration of providers and of the database layer (.docs/database/providers.md#registration).</summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers a provider under <paramref name="name"/> (the value of <c>DATABASE_PROVIDER</c>). Register the same
    /// type again under another name for an alias. A later registration of a name replaces an earlier one.
    /// </summary>
    public static IServiceCollection AddDatabaseProvider<TProvider>(this IServiceCollection services, string name)
        where TProvider : IDatabaseProvider, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProviderList(services).Add(name, typeof(TProvider));
        return services;
    }

    /// <summary>The providers that ship with DotNetForge.</summary>
    public static IServiceCollection AddDefaultDatabaseProviders(this IServiceCollection services) =>
        services
            .AddDatabaseProvider<SqliteDatabaseProvider>("sqlite")
            .AddDatabaseProvider<PostgreSqlDatabaseProvider>("postgresql")
            .AddDatabaseProvider<PostgreSqlDatabaseProvider>("postgres")
            .AddDatabaseProvider<SqlServerDatabaseProvider>("sqlserver")
            .AddDatabaseProvider<MySqlDatabaseProvider>("mysql")
            .AddDatabaseProvider<MongoDbDatabaseProvider>("mongodb");

    /// <summary>
    /// Resolves every configured database against the registered providers (stopping startup with a
    /// <see cref="DatabaseConfigurationException"/> on bad configuration), registers EF Core for the main database
    /// and the <see cref="IDatabaseService"/> pipeline. Call after the <c>AddDatabaseProvider</c> registrations.
    /// </summary>
    public static IServiceCollection AddDotNetForgeDatabases(
        this IServiceCollection services, DatabaseSettings main, IEnumerable<DatabaseSettings> additional, DatabaseHostContext host)
    {
        var registry = ProviderList(services).Build();
        var catalog = new DatabaseCatalog(
            registry.Resolve(main, host),
            additional.Select(settings => registry.Resolve(settings, host)).ToList());

        catalog.Main.Provider.AddDbContext(services, catalog.Main.Settings);

        services.TryAddSingleton(registry);
        services.TryAddSingleton(catalog);
        services.TryAddSingleton<QueryRouter>();
        services.TryAddSingleton<IDatabaseService, DatabaseService>();
        return services;
    }

    private static ProviderRegistrations ProviderList(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(ProviderRegistrations))?.ImplementationInstance;
        if (existing is ProviderRegistrations list)
        {
            return list;
        }

        list = new ProviderRegistrations();
        services.AddSingleton(list);
        return list;
    }

    /// <summary>
    /// Registrations collected before the container exists: the main database's DbContext must be configured during
    /// registration, so provider instances are created here rather than resolved from DI.
    /// </summary>
    private sealed class ProviderRegistrations
    {
        private readonly List<(string Name, Type Type)> _items = new();

        public void Add(string name, Type type) => _items.Add((name, type));

        public DatabaseProviderRegistry Build()
        {
            var instances = new Dictionary<Type, IDatabaseProvider>();
            return new DatabaseProviderRegistry(_items.Select(item =>
            {
                if (!instances.TryGetValue(item.Type, out var provider))
                {
                    provider = (IDatabaseProvider)Activator.CreateInstance(item.Type)!;
                    instances[item.Type] = provider;
                }

                return KeyValuePair.Create(item.Name, provider);
            }));
        }
    }
}
