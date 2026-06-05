using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Configuration;

/// <summary>
/// Strongly-typed view of the <c>.env</c> configuration contract (owned by installation_setup.md).
/// Built and validated at startup; invalid configuration must abort startup with a clear error.
/// </summary>
public sealed class AppEnvironment
{
    public const string DefaultAppName = "DotNetForge CMS";
    public const string DefaultAppUrl = "http://localhost:5000";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Raw provider value as written in <c>.env</c> (for diagnostics).</summary>
    public string RawProvider { get; set; } = "sqlite";

    public string? ConnectionString { get; set; }

    public string AppName { get; set; } = DefaultAppName;

    public string AppUrl { get; set; } = DefaultAppUrl;

    /// <summary>The effective connection string, applying the SQLite local-file default when empty.</summary>
    public string ResolveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString!;
        }

        return Provider switch
        {
            DatabaseProvider.Sqlite => "Data Source=storage/dotnetforge.db",
            _ => throw new InvalidOperationException(
                "DATABASE_CONNECTION_STRING is required when DATABASE_PROVIDER=postgresql."),
        };
    }
}
