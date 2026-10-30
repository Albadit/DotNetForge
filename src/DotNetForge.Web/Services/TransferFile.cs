using System.Data;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DotNetForge.Abstractions.Database;
using DotNetForge.Core.Transfer;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DotNetForge.Web.Services;

/// <summary>
/// The export file (.docs/features/transfer-and-updates.md#export-file): a .zip with <c>tables/&lt;Entity&gt;.json</c>
/// per table and a <see cref="TransferManifest"/>. Driven by the EF model, never by a database's own types (exports from
/// before the CMS ran on PostgreSQL only - e.g. from SQLite - import as well). Not exported: this site's Data Protection keys (secrets), its
/// <see cref="SystemState"/> and its transfer history.
/// </summary>
public static partial class TransferFile
{
    private const int BatchSize = 500;

    private static readonly HashSet<Type> NotTransferred = [typeof(DataProtectionKey), typeof(UserSession), typeof(SystemState), typeof(TransferRecord)];

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly MethodInfo WriteRowsMethod =
        typeof(TransferFile).GetMethod(nameof(WriteRowsAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo DeleteAllMethod =
        typeof(TransferFile).GetMethod(nameof(DeleteAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The application version written into exports (the assembly's, without the commit suffix).</summary>
    public static string AppVersion { get; } =
        (typeof(TransferFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
        .Split('+')[0];

    /// <summary>The transferred tables, principals before their dependents (the insert order).</summary>
    public static IReadOnlyList<IEntityType> Tables(DotNetForgeDbContext db)
    {
        var types = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && !NotTransferred.Contains(t.ClrType))
            .OrderBy(t => t.ClrType.Name, StringComparer.Ordinal)
            .ToList();
        var ordered = new List<IEntityType>();
        var visiting = new HashSet<IEntityType>();
        void Visit(IEntityType type)
        {
            if (ordered.Contains(type) || !visiting.Add(type))
            {
                return;
            }

            foreach (var principal in type.GetForeignKeys().Select(f => f.PrincipalEntityType).Where(p => p != type && types.Contains(p)))
            {
                Visit(principal);
            }

            ordered.Add(type);
        }

        types.ForEach(Visit);
        return ordered;
    }

    /// <summary>This site's tables and columns, as an export describes them (rows and checksums empty).</summary>
    public static List<TransferTable> Schema(DotNetForgeDbContext db) => Tables(db).Select(Describe).ToList();

    /// <summary>Writes every table and the manifest as a .zip to <paramref name="output"/> (left open); returns the manifest.</summary>
    public static async Task<TransferManifest> WriteAsync(DotNetForgeDbContext db, Stream output, string provider, CancellationToken ct)
    {
        var manifest = new TransferManifest
        {
            AppVersion = AppVersion,
            Provider = provider,
            SeedVersion = await SeedVersionAsync(db, ct),
            CreatedAt = DateTime.UtcNow,
        };

        // One read transaction (a snapshot on PostgreSQL), so the tables agree with each other while the site keeps
        // being edited.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var type in Tables(db))
        {
            var table = Describe(type);
            await using (var entry = zip.CreateEntry(table.EntryName, CompressionLevel.Optimal).Open())
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var writer = new Utf8JsonWriter(new HashingStream(entry, hash));
                table.Rows = await (Task<int>)WriteRowsMethod.MakeGenericMethod(type.ClrType).Invoke(null, [db, type, writer, ct])!;
                await writer.FlushAsync(ct);
                table.Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            manifest.Tables.Add(table);
        }

        await using (var entry = zip.CreateEntry(TransferManifest.FileName, CompressionLevel.Optimal).Open())
        {
            await JsonSerializer.SerializeAsync(entry, manifest, ManifestJson, ct);
        }

        return manifest;
    }

    /// <summary>The manifest format of an additional database's export (not importable).</summary>
    public const string DatabaseFormatName = "dotnetforge-database-export";

    /// <summary>
    /// Writes every table or collection of the additional database <paramref name="database"/> as a .zip to
    /// <paramref name="output"/> (left open): <c>tables/&lt;name&gt;.json</c> (all rows, as the database returns them)
    /// and a manifest with row counts and SHA-256 per table. Collections whose names aren't plain identifiers can't be
    /// read through <see cref="IDatabaseService"/> and are listed as skipped. Returns the number of rows.
    /// </summary>
    public static async Task<int> WriteDatabaseAsync(
        IDatabaseService databases, string database, string provider, Stream output, CancellationToken ct)
    {
        var tables = new List<object>();
        var skipped = new List<string>();
        var total = 0;
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var collection in await databases.ListCollectionsAsync(database, ct))
        {
            if (!PlainName().IsMatch(collection))
            {
                skipped.Add(collection);
                continue;
            }

            int rows;
            string sha256;
            await using (var entry = zip.CreateEntry($"tables/{collection}.json", CompressionLevel.Optimal).Open())
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using var writer = new Utf8JsonWriter(new HashingStream(entry, hash));
                rows = 0;
                writer.WriteStartArray();
                var find = DatabaseCommand.Find(collection).On(database).WithTimeout(TimeSpan.FromMinutes(10));
                await foreach (var record in databases.StreamAsync(find, cancellationToken: ct))
                {
                    writer.WriteStartObject();
                    foreach (var (field, value) in record)
                    {
                        writer.WritePropertyName(field);
                        WriteValue(writer, value);
                    }

                    writer.WriteEndObject();
                    rows++;
                    if (writer.BytesPending > 64 * 1024)
                    {
                        await writer.FlushAsync(ct);
                    }
                }

                writer.WriteEndArray();
                await writer.FlushAsync(ct);
                sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            tables.Add(new { name = collection, rows, sha256 });
            total += rows;
        }

        await using (var entry = zip.CreateEntry(TransferManifest.FileName, CompressionLevel.Optimal).Open())
        {
            await JsonSerializer.SerializeAsync(entry, new
            {
                format = DatabaseFormatName,
                formatVersion = 1,
                appVersion = AppVersion,
                database,
                provider,
                createdAt = DateTime.UtcNow,
                tables,
                skipped,
            }, ManifestJson, ct);
        }

        return total;
    }

    /// <summary>
    /// Reads and checks a whole export (<paramref name="file"/> must be seekable): format, compatibility with this
    /// site, every table's checksum and row count, every value, and that it has an enabled Super Admin. Nothing is
    /// changed; the rows are returned only when there are no errors.
    /// </summary>
    public static async Task<TransferContent> ReadAsync(DotNetForgeDbContext db, Stream file, CancellationToken ct)
    {
        var errors = new List<string>();
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return TransferContent.Failed(null, "The file is not a .zip export, or it is damaged or incomplete.");
        }

        using (zip)
        {
            TransferManifest? manifest;
            try
            {
                var manifestEntry = zip.GetEntry(TransferManifest.FileName);
                if (manifestEntry is null)
                {
                    return TransferContent.Failed(null, "The file has no manifest.json - it is not a DotNetForge export.");
                }

                await using var stream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<TransferManifest>(stream, ManifestJson, ct);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                manifest = null;
            }

            if (manifest is null)
            {
                return TransferContent.Failed(null, "The manifest of the file is damaged.");
            }

            errors.AddRange(TransferRules.Check(manifest, Schema(db), await SeedVersionAsync(db, ct)));
            if (errors.Count > 0)
            {
                return new TransferContent(manifest, errors, new());
            }

            var types = Tables(db).ToDictionary(t => t.ClrType.Name, StringComparer.Ordinal);
            var rows = new Dictionary<IEntityType, List<object>>();
            foreach (var table in manifest.Tables)
            {
                var error = await ReadTableAsync(zip, table, types[table.Name], rows, ct);
                if (error is not null)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count == 0 && !HasSuperAdmin(rows))
            {
                errors.Add("The export has no enabled Super Admin - nobody could sign in after the import.");
            }

            return new TransferContent(manifest, errors, errors.Count == 0 ? rows : new());
        }
    }

    /// <summary>
    /// Replaces every transferred table with <paramref name="content"/> in one transaction: all or nothing. Integer
    /// keys the database generates (the audit log's) get new values, in the original order.
    /// </summary>
    public static async Task ReplaceAsync(DotNetForgeDbContext db, TransferContent content, CancellationToken ct)
    {
        var tables = Tables(db);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var type in tables.Reverse())
            {
                await (Task)DeleteAllMethod.MakeGenericMethod(type.ClrType).Invoke(null, [db, ct])!;
            }

            foreach (var type in tables)
            {
                var rows = content.Rows.GetValueOrDefault(type) ?? new List<object>();
                if (GeneratedIntegerKey(type) is { } key)
                {
                    var getter = key.PropertyInfo!;
                    rows = rows.OrderBy(r => Convert.ToInt64(getter.GetValue(r))).ToList();
                    rows.ForEach(r => getter.SetValue(r, Activator.CreateInstance(key.ClrType)));
                }

                foreach (var batch in rows.Chunk(BatchSize))
                {
                    db.AddRange(batch);
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }

            await transaction.CommitAsync(ct);
        });
    }

    private static async Task<string?> ReadTableAsync(
        ZipArchive zip, TransferTable table, IEntityType type, Dictionary<IEntityType, List<object>> rows, CancellationToken ct)
    {
        var entry = zip.GetEntry(table.EntryName);
        if (entry is null)
        {
            return $"The file has no {table.EntryName} - the export is incomplete.";
        }

        byte[] bytes;
        try
        {
            await using var stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return $"{table.EntryName} is damaged.";
        }

        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != table.Sha256)
        {
            return $"The data of {table.Name} doesn't match its checksum - the file is damaged.";
        }

        var columns = Columns(type).ToDictionary(p => p.Name, StringComparer.Ordinal);
        var list = new List<object>();
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != table.Rows)
            {
                return $"{table.Name} doesn't have the {table.Rows} rows its manifest lists.";
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var row = Activator.CreateInstance(type.ClrType)!;
                foreach (var value in element.EnumerateObject())
                {
                    if (TransferRules.RetiredColumns.Contains(table.Name + "." + value.Name))
                    {
                        continue;
                    }

                    if (!columns.TryGetValue(value.Name, out var property))
                    {
                        return $"{table.Name} row {list.Count + 1} has an unknown column {value.Name}.";
                    }

                    property.PropertyInfo!.SetValue(row, Normalize(value.Value.Deserialize(property.ClrType)));
                }

                list.Add(row);
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return $"{table.Name} row {list.Count + 1} has an invalid value.";
        }

        rows[type] = list;
        return null;
    }

    private static bool HasSuperAdmin(Dictionary<IEntityType, List<object>> rows)
    {
        List<T> Of<T>() => rows.FirstOrDefault(r => r.Key.ClrType == typeof(T)).Value?.Cast<T>().ToList() ?? new();
        var superAdmin = Of<Role>().FirstOrDefault(r => r.Name == Roles.SuperAdmin)?.Id;
        var enabled = Of<User>().Where(u => u.Status == UserStatus.Enabled).Select(u => u.Id).ToHashSet();
        return superAdmin is not null && Of<UserRole>().Any(ur => ur.RoleId == superAdmin && enabled.Contains(ur.UserId));
    }

    private static TransferTable Describe(IEntityType type) => new()
    {
        Name = type.ClrType.Name,
        Columns = Columns(type)
            .Select(p => new TransferColumn(p.Name, (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).FullName!, p.IsNullable))
            .ToList(),
    };

    /// <summary>The model's stored properties (computed and ignored ones aren't in the model).</summary>
    private static IEnumerable<IProperty> Columns(IEntityType type) =>
        type.GetProperties().Where(p => p.PropertyInfo is not null).OrderBy(p => p.Name, StringComparer.Ordinal);

    private static IProperty? GeneratedIntegerKey(IEntityType type) =>
        type.FindPrimaryKey() is { Properties: [{ ValueGenerated: ValueGenerated.OnAdd } key] } &&
        (key.ClrType == typeof(int) || key.ClrType == typeof(long))
            ? key
            : null;

    /// <summary>The CMS stores UTC; an older export may carry dates without a kind, which PostgreSQL would refuse.</summary>
    private static object? Normalize(object? value) =>
        value is DateTime { Kind: DateTimeKind.Unspecified } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : value;

    private static async Task<int> SeedVersionAsync(DotNetForgeDbContext db, CancellationToken ct) =>
        await db.SystemState.AsNoTracking().OrderBy(s => s.Id).Select(s => s.SeedVersion).FirstOrDefaultAsync(ct);

    /// <summary>A value of an additional database as JSON; types JSON can't express (driver-specific ones) as text.</summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            JsonSerializer.Serialize(writer, value, value.GetType());
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or JsonException)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex PlainName();

    private static async Task<int> WriteRowsAsync<T>(DotNetForgeDbContext db, IEntityType type, Utf8JsonWriter writer, CancellationToken ct)
        where T : class
    {
        var columns = Columns(type).ToList();
        var count = 0;
        writer.WriteStartArray();
        await foreach (var row in db.Set<T>().AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            writer.WriteStartObject();
            foreach (var column in columns)
            {
                writer.WritePropertyName(column.Name);
                JsonSerializer.Serialize(writer, column.PropertyInfo!.GetValue(row), column.ClrType);
            }

            writer.WriteEndObject();
            count++;
            if (writer.BytesPending > 64 * 1024)
            {
                await writer.FlushAsync(ct);
            }
        }

        writer.WriteEndArray();
        return count;
    }

    private static Task DeleteAllAsync<T>(DotNetForgeDbContext db, CancellationToken ct) where T : class =>
        db.Set<T>().ExecuteDeleteAsync(ct);

    /// <summary>Hashes what is written through it (the table files' checksums), without buffering.</summary>
    private sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            hash.AppendData(buffer, offset, count);
            inner.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            hash.AppendData(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>A read export: its manifest (null when unreadable), the problems found, and the rows when there are none.</summary>
public sealed record TransferContent(TransferManifest? Manifest, List<string> Errors, Dictionary<IEntityType, List<object>> Rows)
{
    public static TransferContent Failed(TransferManifest? manifest, string error) => new(manifest, [error], new());
}
