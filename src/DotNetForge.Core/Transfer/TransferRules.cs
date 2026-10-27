using System.Text.RegularExpressions;

namespace DotNetForge.Core.Transfer;

/// <summary>
/// Whether an export file can be imported into this site (.docs/features/transfer-and-updates.md#rules). Pure logic:
/// the import runs only when <see cref="Check"/> finds nothing, before any data changes.
/// </summary>
public static partial class TransferRules
{
    /// <summary>
    /// Columns older exports may carry that the CMS no longer has ("Table.Column"): their values are ignored on import
    /// instead of refusing the file.
    /// </summary>
    public static readonly IReadOnlySet<string> RetiredColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        "MediaFile.FolderPath", // replaced by MediaFile.FolderId (media folders)
    };

    /// <summary>
    /// The problems of importing <paramref name="file"/> into a site whose model is <paramref name="current"/> (tables
    /// with their columns; rows and checksums unused) at data version <paramref name="seedVersion"/>; empty when it can be imported.
    /// An older file may lack a table or a nullable column (imported as empty / null); anything the site doesn't have,
    /// a changed type or a missing required column is refused.
    /// </summary>
    public static List<string> Check(TransferManifest file, IReadOnlyList<TransferTable> current, int seedVersion)
    {
        var errors = new List<string>();
        if (file.Format != TransferManifest.FormatName)
        {
            errors.Add("This is not a DotNetForge export file.");
            return errors;
        }

        if (file.FormatVersion != TransferManifest.CurrentFormatVersion)
        {
            errors.Add($"The export file format {file.FormatVersion} is not supported (this site reads format {TransferManifest.CurrentFormatVersion}).");
            return errors;
        }

        if (file.SeedVersion != seedVersion)
        {
            errors.Add($"The export's data version ({file.SeedVersion}) differs from this site's ({seedVersion}). " +
                "Export again from a site running the same CMS version.");
        }

        var tables = current.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in file.Tables)
        {
            if (!seen.Add(table.Name))
            {
                errors.Add($"The table {table.Name} appears twice.");
                continue;
            }

            if (!tables.TryGetValue(table.Name, out var target))
            {
                errors.Add($"Unknown table {table.Name} - the export is from a different CMS version.");
                continue;
            }

            if (table.Rows < 0 || !Sha256Hex().IsMatch(table.Sha256 ?? string.Empty))
            {
                errors.Add($"The manifest entry of {table.Name} is damaged.");
            }

            var columns = target.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var column in table.Columns.Where(c => !RetiredColumns.Contains(table.Name + "." + c.Name)))
            {
                if (!columns.TryGetValue(column.Name, out var expected))
                {
                    errors.Add($"Unknown column {table.Name}.{column.Name} - the export is from a different CMS version.");
                }
                else if (expected.Type != column.Type || (column.Nullable && !expected.Nullable))
                {
                    errors.Add($"The column {table.Name}.{column.Name} has a different type ({column.Type}) than on this site ({expected.Type}).");
                }
            }

            foreach (var missing in target.Columns.Where(c => !c.Nullable && table.Columns.All(f => f.Name != c.Name)))
            {
                errors.Add($"The export has no {table.Name}.{missing.Name}, which this site requires.");
            }
        }

        return errors;
    }

    /// <summary>The download/storage file name, e.g. <c>dotnetforge-sqlite-20261027-143005.zip</c>.</summary>
    public static string FileName(string provider, DateTime utc, string kind = "export") =>
        $"dotnetforge-{kind}-{Slug().Replace(provider.ToLowerInvariant(), string.Empty)}-{utc:yyyyMMdd-HHmmss}.zip";

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex Slug();
}
