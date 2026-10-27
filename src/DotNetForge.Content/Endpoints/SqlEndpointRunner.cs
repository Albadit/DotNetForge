using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using DotNetForge.Core.Endpoints;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DotNetForge.Content.Endpoints;

/// <summary>What one run of an endpoint's SQL produced.</summary>
/// <param name="Data">Rows (a list of column → value maps), one row, a single value, or null for <see cref="EndpointResultShape.AffectedRows"/>.</param>
/// <param name="Affected">Rows the statement changed (-1 when the database doesn't say, e.g. for a SELECT).</param>
/// <param name="Truncated">More rows existed than <see cref="CustomEndpointOperation.MaxRows"/>.</param>
public sealed record EndpointRunResult(object? Data, long Affected, bool Truncated, TimeSpan Duration);

/// <summary>The SQL failed or timed out. <see cref="Exception.Message"/> is the database's message (for admins and token callers).</summary>
public sealed class EndpointSqlException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A database custom endpoints can run on: <see cref="Name"/> null = the CMS database.</summary>
public sealed record EndpointDatabase(string? Name, string Label);

/// <summary>
/// Runs a custom endpoint operation's SQL (.docs/features/custom-endpoints.md#how-a-call-runs) on the CMS database or
/// on the endpoint's additional SQL database (<c>DATABASES_&lt;NAME&gt;_*</c>): one transaction, the request values as
/// typed ADO.NET parameters (<c>@name</c>) - never pasted into the SQL - with the operation's timeout and row limit.
/// MongoDB databases have no SQL and can't be chosen.
/// </summary>
public sealed class SqlEndpointRunner
{
    private readonly DotNetForgeDbContext _db;
    private readonly DatabaseCatalog _catalog;

    public SqlEndpointRunner(DotNetForgeDbContext db, DatabaseCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    /// <summary>The CMS database first, then every additional database that runs SQL.</summary>
    public IReadOnlyList<EndpointDatabase> Databases =>
        _catalog.All
            .Where(d => d.Settings.IsMain || HasSql(d))
            .Select(d => d.Settings.IsMain
                ? new EndpointDatabase(null, $"CMS database ({d.Provider.DisplayName})")
                : new EndpointDatabase(d.Name, $"{d.Name} ({d.Provider.DisplayName})"))
            .ToList();

    /// <summary>"reports" for " Reports ", null for empty (the CMS database).</summary>
    public static string? Normalize(string? database) =>
        string.IsNullOrWhiteSpace(database) ? null : database.Trim().ToLowerInvariant();

    /// <summary>Why <paramref name="database"/> can't run endpoint SQL, or null when it can.</summary>
    public string? Problem(string? database)
    {
        if (Normalize(database) is not { } name)
        {
            return null;
        }

        var target = _catalog.All.FirstOrDefault(d => !d.Settings.IsMain && d.Name == name);
        if (target is null)
        {
            return $"There is no database '{name}': add DATABASES_{name.ToUpperInvariant()}_PROVIDER and _CONNECTION_STRING to the configuration.";
        }

        return HasSql(target) ? null : $"The database '{name}' is {target.Provider.DisplayName}, which has no SQL.";
    }

    /// <param name="database">The endpoint's database (<see cref="CustomEndpoint.Database"/>); null = the CMS database.</param>
    /// <param name="rollback">Undo every change when done (the editor's dry run).</param>
    public async Task<EndpointRunResult> RunAsync(
        CustomEndpointOperation operation, string? database, IReadOnlyDictionary<string, object?> values, bool rollback, CancellationToken ct)
    {
        if (Problem(database) is { } problem)
        {
            throw new EndpointSqlException(problem);
        }

        var timeout = Math.Clamp(operation.TimeoutSeconds, 1, 120);
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var session = await OpenAsync(Normalize(database), linked.Token);
            await using var command = session.Connection.CreateCommand();
            command.Transaction = session.Transaction;
            command.CommandText = operation.Sql;
            command.CommandTimeout = timeout;
            AddParameters(command, operation.Sql, values);

            (object? Data, long Affected, bool Truncated) result;
            try
            {
                result = await ExecuteAsync(command, operation, linked.Token);
            }
            catch (InvalidOperationException ex)
            {
                // Drivers report some SQL problems this way, e.g. SQLite's "Must add values for the following parameters".
                throw new EndpointSqlException(ex.Message, ex);
            }

            var (data, affected, truncated) = result;
            if (rollback)
            {
                await session.Transaction.RollbackAsync(CancellationToken.None);
            }
            else
            {
                await session.Transaction.CommitAsync(linked.Token);
            }

            return new EndpointRunResult(data, affected, truncated, stopwatch.Elapsed);
        }
        catch (OperationCanceledException ex) when (timer.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new EndpointSqlException($"The SQL did not finish within {timeout} seconds.", ex);
        }
        catch (DbException ex)
        {
            throw new EndpointSqlException(ex.Message, ex);
        }
    }

    /// <summary>The CMS database through EF Core's connection; an additional one through its own connection.</summary>
    private async Task<Session> OpenAsync(string? database, CancellationToken ct)
    {
        if (database is null)
        {
            var efTransaction = await _db.Database.BeginTransactionAsync(ct);
            return new Session(_db.Database.GetDbConnection(), efTransaction.GetDbTransaction(), efTransaction, ownedConnection: null);
        }

        var target = _catalog.Get(database);
        var connection = target.Provider.CreateConnection(target.Settings)!;
        try
        {
            await connection.OpenAsync(ct);
            var transaction = await connection.BeginTransactionAsync(ct);
            return new Session(connection, transaction, transaction, connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static bool HasSql(ResolvedDatabase database)
    {
        using var connection = database.Provider.CreateConnection(database.Settings);
        return connection is not null;
    }

    private static void AddParameters(DbCommand command, string sql, IReadOnlyDictionary<string, object?> values)
    {
        // Only placeholders the SQL uses: some drivers reject parameters the statement doesn't reference.
        var used = new HashSet<string>(EndpointRules.SqlParameterNames(sql), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values)
        {
            if (!used.Contains(name))
            {
                continue;
            }

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value ?? DBNull.Value;
            if (value is not null && DbTypeOf(value) is { } dbType)
            {
                parameter.DbType = dbType;
            }

            command.Parameters.Add(parameter);
        }
    }

    /// <summary>An open connection with its transaction; disposing ends both (the connection only when it is ours).</summary>
    private sealed class Session(DbConnection connection, DbTransaction transaction, IAsyncDisposable owner, DbConnection? ownedConnection)
        : IAsyncDisposable
    {
        public DbConnection Connection { get; } = connection;
        public DbTransaction Transaction { get; } = transaction;

        public async ValueTask DisposeAsync()
        {
            await owner.DisposeAsync();
            if (ownedConnection is not null)
            {
                await ownedConnection.DisposeAsync();
            }
        }
    }

    private static DbType? DbTypeOf(object value) => value switch
    {
        long => DbType.Int64,
        decimal => DbType.Decimal,
        bool => DbType.Boolean,
        DateTime => DbType.DateTime,
        Guid => DbType.Guid,
        string => DbType.String,
        _ => null,
    };

    private static async Task<(object? Data, long Affected, bool Truncated)> ExecuteAsync(
        DbCommand command, CustomEndpointOperation operation, CancellationToken ct)
    {
        switch (operation.Result)
        {
            case EndpointResultShape.AffectedRows:
                return (null, await command.ExecuteNonQueryAsync(ct), false);
            case EndpointResultShape.Value:
                var scalar = await command.ExecuteScalarAsync(ct);
                return (JsonValue(scalar), -1, false);
        }

        var limit = operation.Result == EndpointResultShape.FirstRow ? 1 : Math.Clamp(operation.MaxRows, 1, 10_000);
        var rows = new List<Dictionary<string, object?>>();
        var truncated = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            var names = ColumnNames(reader);
            while (await reader.ReadAsync(ct))
            {
                if (rows.Count == limit)
                {
                    truncated = operation.Result == EndpointResultShape.Rows;
                    break;
                }

                var row = new Dictionary<string, object?>(names.Count, StringComparer.Ordinal);
                for (var i = 0; i < names.Count; i++)
                {
                    row[names[i]] = JsonValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
                }

                rows.Add(row);
            }

            var affected = reader.RecordsAffected;
            return operation.Result == EndpointResultShape.FirstRow
                ? (rows.Count > 0 ? rows[0] : null, affected, false)
                : (rows, affected, truncated);
        }
    }

    /// <summary>Column names, made unique (<c>id</c>, <c>id_2</c>) so a join's duplicate names don't overwrite each other.</summary>
    private static List<string> ColumnNames(DbDataReader reader)
    {
        var names = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = string.IsNullOrEmpty(reader.GetName(i)) ? $"column{i + 1}" : reader.GetName(i);
            var unique = name;
            for (var n = 2; names.Contains(unique, StringComparer.Ordinal); n++)
            {
                unique = $"{name}_{n}";
            }

            names.Add(unique);
        }

        return names;
    }

    /// <summary>A database value as JSON can show it: Guids and dates as text (ISO 8601), bytes as base64.</summary>
    public static object? JsonValue(object? value) => value switch
    {
        null or DBNull => null,
        string or bool or long or int or short or byte or decimal or double or float => value,
        Guid g => g.ToString(),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        ulong or uint or ushort or sbyte => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
