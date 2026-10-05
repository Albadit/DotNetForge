namespace DotNetForge.Shared.Configuration;

/// <summary>
/// Minimal SQLite connection-string inspection shared by configuration validation and the provider configurator,
/// without taking a dependency on Microsoft.Data.Sqlite in the shared kernel.
/// </summary>
public static class SqliteConnectionStrings
{
    private static readonly string[] DataSourceKeys = { "data source", "datasource", "filename" };

    /// <summary>The <c>Data Source</c> value, or <c>null</c> when the string has none.</summary>
    public static string? GetDataSource(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (DataSourceKeys.Contains(part[..eq].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim().Trim('"', '\'');
            }
        }

        return null;
    }

    /// <summary>Whether the data source is an in-memory database (nothing is written to disk).</summary>
    public static bool IsInMemory(string dataSource) =>
        dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
        dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase);
}
