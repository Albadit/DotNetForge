namespace DotNetForge.Abstractions.Database;

/// <summary>What a <see cref="DatabaseCommand"/> does. One set of verbs for every provider (.docs/database/query-routing.md).</summary>
public enum DatabaseOperation
{
    /// <summary>Records matching the filter (with sorting, paging and field selection).</summary>
    Find,

    /// <summary>The first record matching the filter, or none.</summary>
    FindOne,

    /// <summary>The number of records matching the filter.</summary>
    Count,

    InsertOne,
    InsertMany,

    /// <summary>Applies <see cref="DatabaseCommand.Set"/> to the first matching record.</summary>
    UpdateOne,

    /// <summary>Applies <see cref="DatabaseCommand.Set"/> to every matching record.</summary>
    UpdateMany,

    DeleteOne,
    DeleteMany,

    /// <summary>Groups matching records and computes <see cref="DatabaseCommand.Aggregation"/>.</summary>
    Aggregate,
}

/// <summary>
/// A structured, provider-neutral database operation. Providers translate it into their native form (parameterized
/// SQL, MongoDB filter definitions); callers never build query strings, so values are always sent as parameters and
/// names are validated before they reach a database (.docs/database/security.md).
/// </summary>
/// <remarks>
/// <see cref="Collection"/> and field names refer to the application's entity model when the target is the main
/// database (<c>"Users"</c>, <c>"Email"</c>) and are mapped to table/collection and column/element names by the
/// provider. Named additional databases have no model: names are used as written and must be plain identifiers.
/// </remarks>
public sealed record DatabaseCommand
{
    public required DatabaseOperation Operation { get; init; }

    /// <summary>Entity set, table or collection, e.g. <c>"Users"</c>.</summary>
    public required string Collection { get; init; }

    /// <summary>Configured database name; <c>null</c> targets the main database (.docs/database/configuration.md).</summary>
    public string? Database { get; init; }

    public DatabaseFilter? Filter { get; init; }

    /// <summary>Fields to return (Find/FindOne); <c>null</c> returns all fields.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<DatabaseSort> Sort { get; init; } = Array.Empty<DatabaseSort>();

    public int? Skip { get; init; }

    public int? Take { get; init; }

    /// <summary>Records to insert (InsertOne: exactly one; InsertMany: one or more).</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Documents { get; init; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();

    /// <summary>Field values to set (UpdateOne/UpdateMany).</summary>
    public IReadOnlyDictionary<string, object?>? Set { get; init; }

    public DatabaseAggregation? Aggregation { get; init; }

    /// <summary>Overrides the default command timeout for this command.</summary>
    public TimeSpan? Timeout { get; init; }

    public static DatabaseCommand Find(string collection, DatabaseFilter? filter = null) =>
        new() { Operation = DatabaseOperation.Find, Collection = collection, Filter = filter };

    public static DatabaseCommand FindOne(string collection, DatabaseFilter? filter = null) =>
        new() { Operation = DatabaseOperation.FindOne, Collection = collection, Filter = filter };

    public static DatabaseCommand Count(string collection, DatabaseFilter? filter = null) =>
        new() { Operation = DatabaseOperation.Count, Collection = collection, Filter = filter };

    public static DatabaseCommand InsertOne(string collection, IReadOnlyDictionary<string, object?> document) =>
        new() { Operation = DatabaseOperation.InsertOne, Collection = collection, Documents = new[] { document } };

    public static DatabaseCommand InsertMany(string collection, IReadOnlyList<IReadOnlyDictionary<string, object?>> documents) =>
        new() { Operation = DatabaseOperation.InsertMany, Collection = collection, Documents = documents };

    public static DatabaseCommand UpdateOne(string collection, DatabaseFilter filter, IReadOnlyDictionary<string, object?> set) =>
        new() { Operation = DatabaseOperation.UpdateOne, Collection = collection, Filter = filter, Set = set };

    public static DatabaseCommand UpdateMany(string collection, DatabaseFilter? filter, IReadOnlyDictionary<string, object?> set) =>
        new() { Operation = DatabaseOperation.UpdateMany, Collection = collection, Filter = filter, Set = set };

    public static DatabaseCommand DeleteOne(string collection, DatabaseFilter filter) =>
        new() { Operation = DatabaseOperation.DeleteOne, Collection = collection, Filter = filter };

    public static DatabaseCommand DeleteMany(string collection, DatabaseFilter? filter) =>
        new() { Operation = DatabaseOperation.DeleteMany, Collection = collection, Filter = filter };

    public static DatabaseCommand Aggregate(string collection, DatabaseAggregation aggregation, DatabaseFilter? filter = null) =>
        new() { Operation = DatabaseOperation.Aggregate, Collection = collection, Filter = filter, Aggregation = aggregation };

    /// <summary>Targets a named database instead of the main one.</summary>
    public DatabaseCommand On(string database) => this with { Database = database };

    public DatabaseCommand OrderBy(string field) => this with { Sort = Sort.Append(new DatabaseSort(field)).ToArray() };

    public DatabaseCommand OrderByDescending(string field) =>
        this with { Sort = Sort.Append(new DatabaseSort(field, Descending: true)).ToArray() };

    public DatabaseCommand Page(int skip, int take) => this with { Skip = skip, Take = take };

    public DatabaseCommand Select(params string[] fields) => this with { Fields = fields };

    public DatabaseCommand WithTimeout(TimeSpan timeout) => this with { Timeout = timeout };

    /// <summary>The first matching record for UpdateOne/DeleteOne/FindOne is the first in this order.</summary>
    public bool IsSingle => Operation is DatabaseOperation.FindOne or DatabaseOperation.UpdateOne or DatabaseOperation.DeleteOne;
}

public sealed record DatabaseSort(string Field, bool Descending = false);

public enum AggregateFunction
{
    Count,
    Sum,
    Min,
    Max,
    Average,
}

/// <summary>One computed output column/field: <c>Count</c> needs no field, the others aggregate <see cref="Field"/>.</summary>
public sealed record DatabaseAccumulator(string Name, AggregateFunction Function, string? Field = null);

/// <summary>
/// Portable grouping: each result record holds the <see cref="GroupBy"/> fields and one value per accumulator.
/// Translated to <c>GROUP BY</c> in SQL and <c>$group</c> in MongoDB.
/// </summary>
public sealed record DatabaseAggregation(IReadOnlyList<string> GroupBy, IReadOnlyList<DatabaseAccumulator> Accumulators);
