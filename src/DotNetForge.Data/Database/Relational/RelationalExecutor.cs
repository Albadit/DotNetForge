using System.Data.Common;
using System.Runtime.CompilerServices;
using DotNetForge.Abstractions.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data.Database.Relational;

/// <summary>
/// Runs <see cref="DatabaseCommand"/>s on a SQL database through ADO.NET. Connections come from the driver's pool (one
/// short-lived connection per command, or the transaction's connection), so the executor itself holds no connection.
/// Values are sent through the EF Core type mapping of the target field, so they are stored and compared exactly as
/// EF Core does (e.g. Guids as text on SQLite).
/// </summary>
public sealed class RelationalExecutor : IDatabaseExecutor
{
    private readonly DbProviderFactory _factory;
    private readonly SqlDialect _dialect;
    private readonly DatabaseSchema _schema;
    private readonly string _connectionString;

    public RelationalExecutor(DbProviderFactory factory, SqlDialect dialect, DatabaseSettings settings, DatabaseSchema schema)
    {
        _factory = factory;
        _dialect = dialect;
        _schema = schema;
        _connectionString = settings.ConnectionString
            ?? throw new DatabaseConfigurationException($"Database '{settings.Name}' has no connection string.");
    }

    public async IAsyncEnumerable<DatabaseRecord> QueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var map = _schema.Resolve(command.Collection);
        var builder = new SqlCommandBuilder(_dialect, map);
        IReadOnlyList<(string Name, Type Type, FieldMap? Field)> columns;
        SqlStatement statement;

        if (command.Operation == DatabaseOperation.Aggregate)
        {
            var aggregation = command.Aggregation!;
            statement = builder.Aggregate(aggregation, command.Filter, command.Sort, command.Skip, command.Take);
            columns = aggregation.GroupBy.Select(map.Field).Select(f => (f.Name, f.ClrType, (FieldMap?)f))
                .Concat(aggregation.Accumulators.Select(a => (a.Name, typeof(object), (FieldMap?)null)))
                .ToList();
        }
        else
        {
            var fields = command.Fields?.Select(map.Field).ToList() ?? map.Fields.ToList();
            statement = builder.Select(fields, command.Filter, command.Sort, command.Skip, command.Take);
            columns = fields.Select(f => (f.Name, f.ClrType, (FieldMap?)f)).ToList();
        }

        await using var lease = await LeaseAsync(transaction, cancellationToken);
        await using var dbCommand = Create(lease, statement, command.Timeout);
        await using var reader = await dbCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return ReadRecord(reader, columns);
        }
    }

    public async Task<long> ExecuteAsync(DatabaseCommand command, IDatabaseTransaction? transaction, CancellationToken cancellationToken)
    {
        var map = _schema.Resolve(command.Collection);
        switch (command.Operation)
        {
            case DatabaseOperation.Count:
                {
                    await using var lease = await LeaseAsync(transaction, cancellationToken);
                    await using var dbCommand = Create(lease, new SqlCommandBuilder(_dialect, map).Count(command.Filter), command.Timeout);
                    return Convert.ToInt64(await dbCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
                }

            case DatabaseOperation.InsertOne or DatabaseOperation.InsertMany:
                return await InTransactionAsync(transaction, command.Documents.Count > 1, cancellationToken, async lease =>
                {
                    long inserted = 0;
                    foreach (var document in command.Documents)
                    {
                        var statement = new SqlCommandBuilder(_dialect, map).Insert(WithGeneratedKey(map, document));
                        await using var dbCommand = Create(lease, statement, command.Timeout);
                        inserted += await dbCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    return inserted;
                });

            case DatabaseOperation.UpdateMany or DatabaseOperation.DeleteMany:
                {
                    var builder = new SqlCommandBuilder(_dialect, map);
                    var statement = command.Operation == DatabaseOperation.UpdateMany
                        ? builder.Update(command.Set!, command.Filter)
                        : builder.Delete(command.Filter);
                    await using var lease = await LeaseAsync(transaction, cancellationToken);
                    await using var dbCommand = Create(lease, statement, command.Timeout);
                    return await dbCommand.ExecuteNonQueryAsync(cancellationToken);
                }

            case DatabaseOperation.UpdateOne or DatabaseOperation.DeleteOne:
                return await InTransactionAsync(transaction, startLocal: true, cancellationToken, lease => SingleAsync(map, command, lease, cancellationToken));

            default:
                throw new DatabaseQueryException($"{command.Operation} is not an execute operation.");
        }
    }

    public async Task<IDatabaseTransaction> BeginTransactionAsync(string database, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        try
        {
            return new RelationalTransaction(database, connection, await connection.BeginTransactionAsync(cancellationToken));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// UpdateOne/DeleteOne: SQL has no portable "first matching row" write, so the key of the first match (in the
    /// command's sort order) is read first and the write targets that key. Needs a key, so only for model tables.
    /// </summary>
    private async Task<long> SingleAsync(CollectionMap map, DatabaseCommand command, Lease lease, CancellationToken cancellationToken)
    {
        if (map.Key.Count == 0)
        {
            throw new DatabaseProviderException(
                $"{command.Operation} needs a table with a known key; use {(command.Operation == DatabaseOperation.UpdateOne ? "UpdateMany" : "DeleteMany")} with a filter that matches one row.");
        }

        var select = new SqlCommandBuilder(_dialect, map).Select(map.Key, command.Filter, command.Sort, skip: null, take: 1);
        object?[]? key = null;
        await using (var dbCommand = Create(lease, select, command.Timeout))
        await using (var reader = await dbCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                key = map.Key.Select((field, i) => ReadValue(reader, i, field.ClrType, field)).ToArray();
            }
        }

        if (key is null)
        {
            return 0;
        }

        var keyFilter = DatabaseFilter.And(map.Key.Select((field, i) => DatabaseFilter.Eq(field.Name, key[i])).ToArray());
        var builder = new SqlCommandBuilder(_dialect, map);
        var write = command.Operation == DatabaseOperation.UpdateOne ? builder.Update(command.Set!, keyFilter) : builder.Delete(keyFilter);
        await using var writeCommand = Create(lease, write, command.Timeout);
        return await writeCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Generates a missing single Guid key, as EF Core would on the client. Database-generated keys stay omitted.</summary>
    private static IReadOnlyDictionary<string, object?> WithGeneratedKey(CollectionMap map, IReadOnlyDictionary<string, object?> document)
    {
        if (map.Key is [{ ClrType: var type, IsGeneratedKey: true } key] && type == typeof(Guid) &&
            !document.Keys.Any(k => k.Equals(key.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return new Dictionary<string, object?>(document, StringComparer.OrdinalIgnoreCase) { [key.Name] = Guid.NewGuid() };
        }

        return document;
    }

    private async Task<long> InTransactionAsync(
        IDatabaseTransaction? transaction, bool startLocal, CancellationToken cancellationToken, Func<Lease, Task<long>> work)
    {
        if (transaction is not null || !startLocal)
        {
            await using var lease = await LeaseAsync(transaction, cancellationToken);
            return await work(lease);
        }

        await using var local = (RelationalTransaction)await BeginTransactionAsync(string.Empty, cancellationToken);
        var result = await work(new Lease(local.Connection, local.Transaction, owned: false));
        await local.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Lease> LeaseAsync(IDatabaseTransaction? transaction, CancellationToken cancellationToken) =>
        transaction switch
        {
            null => new Lease(await OpenAsync(cancellationToken), null, owned: true),
            RelationalTransaction t => new Lease(t.Connection, t.Transaction, owned: false),
            _ => throw new DatabaseQueryException("The transaction was started on a different database."),
        };

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = _factory.CreateConnection()
            ?? throw new DatabaseProviderException("The database driver could not create a connection.");
        connection.ConnectionString = _connectionString;
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static DbCommand Create(Lease lease, SqlStatement statement, TimeSpan? timeout)
    {
        var command = lease.Connection.CreateCommand();
        command.CommandText = statement.Text;
        command.Transaction = lease.Transaction;
        if (timeout is { } t)
        {
            // Backstop only: the database service cancels at the exact timeout.
            command.CommandTimeout = (int)Math.Ceiling(t.TotalSeconds) + 1;
        }

        foreach (var parameter in statement.Parameters)
        {
            command.Parameters.Add(CreateParameter(command, parameter));
        }

        return command;
    }

    /// <summary>Uses the field's EF Core type mapping (applies its value converter and DbType); plain parameters otherwise.</summary>
    private static DbParameter CreateParameter(DbCommand command, SqlParameterValue value)
    {
        if (value.Field?.Property?.FindRelationalTypeMapping() is { } mapping)
        {
            return mapping.CreateParameter(command, value.Name, value.Value, nullable: true);
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = value.Name;
        parameter.Value = value.Value ?? DBNull.Value;
        return parameter;
    }

    private static DatabaseRecord ReadRecord(DbDataReader reader, IReadOnlyList<(string Name, Type Type, FieldMap? Field)> columns) =>
        columns.Count == 0
            ? new(Enumerable.Range(0, reader.FieldCount).Select(i => KeyValuePair.Create(reader.GetName(i), ReadValue(reader, i, typeof(object), null))))
            : new(columns.Select((column, i) => KeyValuePair.Create(column.Name, ReadValue(reader, i, column.Type, column.Field))));

    private static object? ReadValue(DbDataReader reader, int ordinal, Type type, FieldMap? field)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var raw = reader.GetValue(ordinal);
        if (field?.Property?.FindRelationalTypeMapping()?.Converter is { } converter && !type.IsInstanceOfType(raw))
        {
            raw = converter.ConvertFromProvider(raw);
        }

        return ValueCoercion.ToClr(raw, type);
    }

    /// <summary>A connection (and transaction) for one operation; disposes the connection only when it owns it.</summary>
    private sealed class Lease(DbConnection connection, DbTransaction? transaction, bool owned) : IAsyncDisposable
    {
        public DbConnection Connection { get; } = connection;

        public DbTransaction? Transaction { get; } = transaction;

        public ValueTask DisposeAsync() => owned ? Connection.DisposeAsync() : ValueTask.CompletedTask;
    }
}

/// <summary>A transaction on its own connection; rolled back if disposed before commit.</summary>
public sealed class RelationalTransaction : IDatabaseTransaction
{
    private bool _completed;

    public RelationalTransaction(string database, DbConnection connection, DbTransaction transaction)
    {
        Database = database;
        Connection = connection;
        Transaction = transaction;
    }

    public string Database { get; }

    public DbConnection Connection { get; }

    public DbTransaction Transaction { get; }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await Transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await Transaction.RollbackAsync();
            }
            catch (DbException)
            {
                // The connection may already be broken; disposing it releases the transaction.
            }
        }

        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
