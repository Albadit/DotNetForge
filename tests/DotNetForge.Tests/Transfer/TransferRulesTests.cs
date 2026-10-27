using DotNetForge.Core.Transfer;
using Xunit;

namespace DotNetForge.Tests.Transfer;

/// <summary>Whether an export file fits this site (.docs/features/transfer-and-updates.md#rules).</summary>
public sealed class TransferRulesTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static List<TransferTable> Site() =>
    [
        Table("User", ("Id", "System.Guid", false), ("Email", "System.String", false), ("FirstName", "System.String", true)),
        Table("EmailTemplate", ("Id", "System.Guid", false)),
    ];

    private static TransferTable Table(string name, params (string Name, string Type, bool Nullable)[] columns) => new()
    {
        Name = name,
        Rows = 1,
        Sha256 = Hash,
        Columns = columns.Select(c => new TransferColumn(c.Name, c.Type, c.Nullable)).ToList(),
    };

    private static TransferManifest File(params TransferTable[] tables) => new() { SeedVersion = 2, Tables = tables.ToList() };

    [Fact]
    public void The_same_schema_passes_and_an_older_file_may_lack_a_table_or_a_nullable_column()
    {
        Assert.Empty(TransferRules.Check(File(Site().ToArray()), Site(), seedVersion: 2));
        Assert.Empty(TransferRules.Check(File(Table("User", ("Id", "System.Guid", false), ("Email", "System.String", false))), Site(), 2));
    }

    [Fact]
    public void Retired_columns_of_older_exports_are_ignored()
    {
        var media = new List<TransferTable> { Table("MediaFile", ("Id", "System.Guid", false), ("FolderId", "System.Guid", true)) };
        var file = File(Table("MediaFile", ("Id", "System.Guid", false), ("FolderPath", "System.String", false)));
        Assert.Empty(TransferRules.Check(file, media, 2));
    }

    [Fact]
    public void A_foreign_file_or_format_is_refused_before_anything_else()
    {
        Assert.Equal(["This is not a DotNetForge export file."],
            TransferRules.Check(new TransferManifest { Format = "other" }, Site(), 2));
        Assert.Contains("format 9", Assert.Single(TransferRules.Check(new TransferManifest { FormatVersion = 9 }, Site(), 2)));
    }

    [Fact]
    public void A_different_data_version_unknown_tables_and_columns_and_changed_types_are_named()
    {
        var errors = TransferRules.Check(File(
            Table("User", ("Id", "System.Int32", false), ("Email", "System.String", false), ("Nickname", "System.String", true)),
            Table("Widget", ("Id", "System.Guid", false))), Site(), seedVersion: 3);

        Assert.Contains(errors, e => e.Contains("data version (2)") && e.Contains("(3)"));
        Assert.Contains(errors, e => e.Contains("Unknown table Widget"));
        Assert.Contains(errors, e => e.Contains("Unknown column User.Nickname"));
        Assert.Contains(errors, e => e.Contains("User.Id") && e.Contains("System.Int32"));
    }

    [Fact]
    public void A_missing_required_column_a_nullable_value_for_a_required_one_and_a_damaged_entry_are_refused()
    {
        var damaged = Table("User", ("Id", "System.Guid", false), ("Email", "System.String", true));
        damaged.Sha256 = "nope";
        var errors = TransferRules.Check(File(damaged, Table("EmailTemplate")), Site(), 2);

        Assert.Contains(errors, e => e.Contains("User.Email") && e.Contains("different type"));
        Assert.Contains(errors, e => e.Contains("manifest entry of User is damaged"));
        Assert.Contains(errors, e => e.Contains("no EmailTemplate.Id"));
    }

    [Fact]
    public void File_names_carry_kind_database_and_time()
    {
        Assert.Equal("dotnetforge-backup-postgresql-20261027-143005.zip",
            TransferRules.FileName("PostgreSQL", new DateTime(2026, 10, 27, 14, 30, 5, DateTimeKind.Utc), "backup"));
        Assert.Equal("dotnetforge-export-sqlserver-20260101-000000.zip",
            TransferRules.FileName("SQL Server", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}
