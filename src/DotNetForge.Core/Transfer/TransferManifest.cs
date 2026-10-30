namespace DotNetForge.Core.Transfer;

/// <summary>
/// <c>manifest.json</c> of an export file (.docs/features/transfer-and-updates.md#export-file): what made the file and,
/// per table, its columns, row count and SHA-256, so an import can check the file before it changes anything.
/// Tables and columns are named after the entity model (provider-neutral), never after a database's own types.
/// </summary>
public sealed class TransferManifest
{
    public const string FormatName = "dotnetforge-export";
    public const int CurrentFormatVersion = 1;
    public const string FileName = "manifest.json";

    public string Format { get; set; } = FormatName;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>The application (assembly) version that wrote the file.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>The database it was exported from, e.g. <c>PostgreSQL</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The data version (one-time data updates applied, <c>SystemState.SeedVersion</c>).</summary>
    public int SeedVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<TransferTable> Tables { get; set; } = new();

    public int RecordCount => Tables.Sum(t => t.Rows);
}

/// <summary>One table: <c>tables/&lt;Name&gt;.json</c>, a JSON array of row objects.</summary>
public sealed class TransferTable
{
    public string Name { get; set; } = string.Empty;

    public int Rows { get; set; }

    /// <summary>SHA-256 (lowercase hex) of the table file's bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public List<TransferColumn> Columns { get; set; } = new();

    public string EntryName => "tables/" + Name + ".json";
}

/// <summary>A column: the model property, its .NET type (without nullability) and whether it may be null.</summary>
public sealed record TransferColumn(string Name, string Type, bool Nullable);
