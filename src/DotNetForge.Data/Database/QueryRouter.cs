using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Data.Database;

/// <summary>
/// Sends a command to the right executor (.docs/database/query-routing.md): database name → configured database
/// (<see cref="DatabaseCatalog"/>) → its provider → one executor per database, created on first use and reused, so
/// clients and pools are created once. No knowledge of concrete providers.
/// </summary>
public sealed class QueryRouter : IAsyncDisposable
{
    private readonly DatabaseCatalog _catalog;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, Lazy<IDatabaseExecutor>> _executors = new(StringComparer.OrdinalIgnoreCase);

    public QueryRouter(DatabaseCatalog catalog, IServiceProvider services, ILoggerFactory loggerFactory)
    {
        _catalog = catalog;
        _services = services;
        _loggerFactory = loggerFactory;
    }

    /// <summary>The configured database called <paramref name="database"/> (main for <c>null</c>) and its executor.</summary>
    public (ResolvedDatabase Database, IDatabaseExecutor Executor) Route(string? database)
    {
        var target = _catalog.Get(database);
        var executor = _executors.GetOrAdd(target.Name, _ => new Lazy<IDatabaseExecutor>(
            () => target.Provider.CreateExecutor(target.Settings, target.Settings.IsMain ? MainModel() : null, _loggerFactory)));
        return (target, executor.Value);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var executor in _executors.Values.Where(e => e.IsValueCreated))
        {
            await executor.Value.DisposeAsync();
        }
    }

    /// <summary>The CMS model, so main-database commands are mapped and checked against real tables and fields.</summary>
    private IModel? MainModel()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetService<DotNetForgeDbContext>()?.Model;
    }
}
