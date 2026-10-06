using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Data.Database;

/// <summary>
/// Provider lookup by name (.docs/database/query-routing.md). Built from <c>AddDatabaseProvider&lt;T&gt;(name)</c>
/// registrations; the registry has no knowledge of any concrete provider, so new providers never change it.
/// </summary>
public sealed class DatabaseProviderRegistry
{
    private readonly Dictionary<string, IDatabaseProvider> _byName;

    public DatabaseProviderRegistry(IEnumerable<KeyValuePair<string, IDatabaseProvider>> registrations)
    {
        _byName = new Dictionary<string, IDatabaseProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, provider) in registrations)
        {
            _byName[name] = provider; // a later registration replaces an earlier one (lets apps swap a provider)
        }
    }

    /// <summary>Registered names, including aliases.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    public bool TryGet(string name, out IDatabaseProvider provider) => _byName.TryGetValue(name, out provider!);

    /// <summary>
    /// Binds settings to a provider: the configured provider name, otherwise the single provider that recognizes the
    /// connection string. The provider then validates and normalizes the settings.
    /// </summary>
    public ResolvedDatabase Resolve(DatabaseSettings settings, DatabaseHostContext host)
    {
        var (name, provider) = string.IsNullOrWhiteSpace(settings.Provider)
            ? Detect(settings)
            : (settings.Provider!, Get(settings));

        var normalized = provider.Normalize(settings with { Provider = name.ToLowerInvariant() }, host);
        return new ResolvedDatabase(normalized, name.ToLowerInvariant(), provider);
    }

    private IDatabaseProvider Get(DatabaseSettings settings) =>
        _byName.TryGetValue(settings.Provider!, out var provider)
            ? provider
            : throw new DatabaseConfigurationException(
                $"Unknown database provider '{settings.Provider}' for database '{settings.Name}'. " +
                $"Registered providers: {string.Join(", ", _byName.Keys.Order())}.");

    private (string Name, IDatabaseProvider Provider) Detect(DatabaseSettings settings)
    {
        // One candidate per provider instance: aliases (postgres/postgresql) must not count twice.
        var matches = _byName
            .GroupBy(kv => kv.Value)
            .Where(group => group.Key.CanHandle(settings.ConnectionString))
            .Select(group => (Name: group.First().Key, Provider: group.Key))
            .ToList();

        var setting = settings.IsMain ? "DATABASE_PROVIDER" : $"DATABASES_{settings.Name.ToUpperInvariant()}_PROVIDER";
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new DatabaseConfigurationException(
                $"Cannot tell which database the connection string of '{settings.Name}' is for. Set {setting} to one " +
                $"of: {string.Join(", ", _byName.Keys.Order())}."),
            _ => throw new DatabaseConfigurationException(
                $"The connection string of '{settings.Name}' fits several databases " +
                $"({string.Join(", ", matches.Select(m => m.Provider.DisplayName))}). Set {setting}."),
        };
    }
}

/// <summary>The configured databases, resolved once at startup. The main database is the one the CMS runs on.</summary>
public sealed class DatabaseCatalog
{
    private readonly Dictionary<string, ResolvedDatabase> _byName;

    public DatabaseCatalog(ResolvedDatabase main, IEnumerable<ResolvedDatabase> additional)
    {
        Main = main;
        _byName = new Dictionary<string, ResolvedDatabase>(StringComparer.OrdinalIgnoreCase) { [main.Name] = main };
        foreach (var database in additional)
        {
            _byName.Add(database.Name, database);
        }
    }

    public ResolvedDatabase Main { get; }

    /// <summary>Main first, then the others by name.</summary>
    public IReadOnlyList<ResolvedDatabase> All =>
        _byName.Values.OrderBy(d => d.Settings.IsMain ? 0 : 1).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();

    /// <summary>The named database, or the main one for <c>null</c>.</summary>
    public ResolvedDatabase Get(string? name) =>
        _byName.TryGetValue(name ?? DatabaseSettings.MainName, out var database)
            ? database
            : throw new DatabaseNotFoundException(
                $"No database named '{name}' is configured. Configured: {string.Join(", ", _byName.Keys.Order())}.");
}
