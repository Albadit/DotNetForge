using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DotNetForge.Abstractions.Database;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DotNetForge.Data.Database.MongoDb;

/// <summary>
/// Runs <see cref="DatabaseCommand"/>s with MongoDB's native API: <c>Find</c>, <c>CountDocuments</c>,
/// <c>InsertOne/Many</c>, <c>UpdateOne/Many</c> with <c>$set</c>, <c>DeleteOne/Many</c> and aggregation pipelines.
/// Filters are built with the driver's typed builders from validated field names and typed BSON values, so command
/// values can never become operators (<c>{"$ne": null}</c> as a value is just a string) and text matching is regex-escaped.
/// </summary>
public sealed class MongoExecutor : IDatabaseExecutor
{
    private static readonly FilterDefinitionBuilder<BsonDocument> Filter = Builders<BsonDocument>.Filter;

    private readonly IMongoDatabase _database;
    private readonly DatabaseSchema _schema;

    public MongoExecutor(IMongoDatabase database, DatabaseSchema schema)
    {
        _database = database;
        _schema = schema;
    }

    public async IAsyncEnumerable<DatabaseRecord> QueryAsync(
        DatabaseCommand command, IDatabaseTransaction? transaction, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var map = _schema.Resolve(command.Collection);
        var collection = _database.GetCollection<BsonDocument>(map.Native);
        var session = Session(transaction);

        if (command.Operation == DatabaseOperation.Aggregate)
        {
            var (pipeline, outputs) = BuildPipeline(map, command);
            var options = new AggregateOptions { MaxTime = command.Timeout };
            using var cursor = session is null
                ? await collection.AggregateAsync<BsonDocument>(pipeline, options, cancellationToken)
                : await collection.AggregateAsync<BsonDocument>(session, pipeline, options, cancellationToken);
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var document in cursor.Current)
                {
                    yield return new DatabaseRecord(outputs.Select(o => KeyValuePair.Create(o.Name, MongoValues.FromBson(document.GetValue(o.Name, BsonNull.Value), o.Type))));
                }
            }

            yield break;
        }

        var fields = command.Fields?.Select(map.Field).ToList();
        var find = session is null
            ? collection.Find(BuildFilter(map, command.Filter), new FindOptions { MaxTime = command.Timeout })
            : collection.Find(session, BuildFilter(map, command.Filter), new FindOptions { MaxTime = command.Timeout });
        if (command.Sort.Count > 0)
        {
            find = find.Sort(BuildSort(map, command.Sort));
        }

        if (fields is not null)
        {
            find = find.Project<BsonDocument>(Builders<BsonDocument>.Projection.Combine(fields.Select(f => Builders<BsonDocument>.Projection.Include(f.Native))));
        }

        using var findCursor = await find.Skip(command.Skip).Limit(command.Take).ToCursorAsync(cancellationToken);
        while (await findCursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in findCursor.Current)
            {
                yield return ToRecord(map, fields, document);
            }
        }
    }

    public async Task<long> ExecuteAsync(DatabaseCommand command, IDatabaseTransaction? transaction, CancellationToken cancellationToken)
    {
        var map = _schema.Resolve(command.Collection);
        var collection = _database.GetCollection<BsonDocument>(map.Native);
        var session = Session(transaction);
        var filter = BuildFilter(map, command.Filter);

        switch (command.Operation)
        {
            case DatabaseOperation.Count:
                var countOptions = new CountOptions { MaxTime = command.Timeout };
                return session is null
                    ? await collection.CountDocumentsAsync(filter, countOptions, cancellationToken)
                    : await collection.CountDocumentsAsync(session, filter, countOptions, cancellationToken);

            case DatabaseOperation.InsertOne:
                var single = ToDocument(map, command.Documents[0]);
                if (session is null)
                {
                    await collection.InsertOneAsync(single, cancellationToken: cancellationToken);
                }
                else
                {
                    await collection.InsertOneAsync(session, single, cancellationToken: cancellationToken);
                }

                return 1;

            case DatabaseOperation.InsertMany:
                var documents = command.Documents.Select(d => ToDocument(map, d)).ToList();
                if (session is null)
                {
                    await collection.InsertManyAsync(documents, cancellationToken: cancellationToken);
                }
                else
                {
                    await collection.InsertManyAsync(session, documents, cancellationToken: cancellationToken);
                }

                return documents.Count;

            case DatabaseOperation.UpdateOne:
            case DatabaseOperation.UpdateMany:
                var update = BuildUpdate(map, command.Set!);
                if (command.Operation == DatabaseOperation.UpdateOne)
                {
                    filter = await FirstIdFilterAsync(collection, session, filter, map, command, cancellationToken);
                    var one = session is null
                        ? await collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)
                        : await collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken);
                    return one.ModifiedCount;
                }

                var many = session is null
                    ? await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken)
                    : await collection.UpdateManyAsync(session, filter, update, cancellationToken: cancellationToken);
                return many.ModifiedCount;

            case DatabaseOperation.DeleteOne:
                filter = await FirstIdFilterAsync(collection, session, filter, map, command, cancellationToken);
                var deletedOne = session is null
                    ? await collection.DeleteOneAsync(filter, cancellationToken)
                    : await collection.DeleteOneAsync(session, filter, cancellationToken: cancellationToken);
                return deletedOne.DeletedCount;

            case DatabaseOperation.DeleteMany:
                var deleted = session is null
                    ? await collection.DeleteManyAsync(filter, cancellationToken)
                    : await collection.DeleteManyAsync(session, filter, cancellationToken: cancellationToken);
                return deleted.DeletedCount;

            default:
                throw new DatabaseQueryException($"{command.Operation} is not an execute operation.");
        }
    }

    public async Task<IDatabaseTransaction> BeginTransactionAsync(string database, CancellationToken cancellationToken)
    {
        var session = await _database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();
        return new MongoTransaction(database, session);
    }

    public Task TestConnectionAsync(CancellationToken cancellationToken) =>
        _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

    /// <summary>The client is owned by the provider and shared with EF Core; nothing to release here.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IClientSessionHandle? Session(IDatabaseTransaction? transaction) => transaction switch
    {
        null => null,
        MongoTransaction t => t.Session,
        _ => throw new DatabaseQueryException("The transaction was started on a different database."),
    };

    /// <summary>
    /// UpdateOne/DeleteOne honour the command's sort: with a sort, the first match's <c>_id</c> is looked up first and
    /// the write targets it; without one, MongoDB's own "first match" is used.
    /// </summary>
    private static async Task<FilterDefinition<BsonDocument>> FirstIdFilterAsync(
        IMongoCollection<BsonDocument> collection, IClientSessionHandle? session, FilterDefinition<BsonDocument> filter,
        CollectionMap map, DatabaseCommand command, CancellationToken cancellationToken)
    {
        if (command.Sort.Count == 0)
        {
            return filter;
        }

        var find = session is null ? collection.Find(filter) : collection.Find(session, filter);
        var first = await find.Sort(BuildSort(map, command.Sort))
            .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include("_id"))
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);
        return first is null ? Filter.In("_id", Array.Empty<BsonValue>()) : Filter.Eq("_id", first["_id"]);
    }

    /// <summary>Translates a filter tree into a typed MongoDB filter (public for tests and diagnostics).</summary>
    public static FilterDefinition<BsonDocument> BuildFilter(CollectionMap map, DatabaseFilter? filter) => filter switch
    {
        null => Filter.Empty,
        ComparisonFilter c => Compare(map.Field(c.Field), c.Operator, c.Value),
        NullFilter { IsNull: true } n => Filter.Eq(map.Field(n.Field).Native, BsonNull.Value),
        NullFilter n => Filter.Ne(map.Field(n.Field).Native, BsonNull.Value),
        InFilter i => Filter.In(map.Field(i.Field).Native, i.Values.Select(v => MongoValues.ToBson(v, map.Field(i.Field)))),
        TextFilter t => Text(map, t),
        CompositeFilter { Filters.Count: 0 } c => c.Operator == LogicalOperator.And ? Filter.Empty : Filter.In("_id", Array.Empty<BsonValue>()),
        CompositeFilter { Operator: LogicalOperator.And } c => Filter.And(c.Filters.Select(f => BuildFilter(map, f))),
        CompositeFilter c => Filter.Or(c.Filters.Select(f => BuildFilter(map, f))),
        NotFilter n => Filter.Not(BuildFilter(map, n.Filter)),
        _ => throw new DatabaseQueryException($"Unsupported filter {filter.GetType().Name}."),
    };

    private static FilterDefinition<BsonDocument> Compare(FieldMap field, ComparisonOperator op, object value)
    {
        var bson = MongoValues.ToBson(value, field);
        return op switch
        {
            ComparisonOperator.Equal => Filter.Eq(field.Native, bson),
            ComparisonOperator.NotEqual => Filter.Ne(field.Native, bson),
            ComparisonOperator.GreaterThan => Filter.Gt(field.Native, bson),
            ComparisonOperator.GreaterThanOrEqual => Filter.Gte(field.Native, bson),
            ComparisonOperator.LessThan => Filter.Lt(field.Native, bson),
            ComparisonOperator.LessThanOrEqual => Filter.Lte(field.Native, bson),
            _ => throw new DatabaseQueryException($"Unsupported operator {op}."),
        };
    }

    /// <summary>The text is escaped, so it matches literally: <c>.*</c> in a search term finds ".*", not everything.</summary>
    private static FilterDefinition<BsonDocument> Text(CollectionMap map, TextFilter filter)
    {
        var field = map.Field(filter.Field);
        if (map.IsMapped && field.ClrType != typeof(string))
        {
            throw new DatabaseQueryException($"Text matching needs a text field; '{field.Name}' is {field.ClrType.Name}.");
        }

        var pattern = (filter.Match == TextMatch.StartsWith ? "^" : string.Empty) + Regex.Escape(filter.Value);
        return Filter.Regex(field.Native, new BsonRegularExpression(pattern, filter.IgnoreCase ? "i" : string.Empty));
    }

    private static SortDefinition<BsonDocument> BuildSort(CollectionMap map, IReadOnlyList<DatabaseSort> sort) =>
        Builders<BsonDocument>.Sort.Combine(sort.Select(s => s.Descending
            ? Builders<BsonDocument>.Sort.Descending(map.Field(s.Field).Native)
            : Builders<BsonDocument>.Sort.Ascending(map.Field(s.Field).Native)));

    private static UpdateDefinition<BsonDocument> BuildUpdate(CollectionMap map, IReadOnlyDictionary<string, object?> set) =>
        Builders<BsonDocument>.Update.Combine(set.Select(kv =>
        {
            var field = map.Field(kv.Key);
            if (field.IsKey)
            {
                throw new DatabaseQueryException($"Key field '{field.Name}' can't be updated.");
            }

            return Builders<BsonDocument>.Update.Set(field.Native, MongoValues.ToBson(kv.Value, field));
        }));

    /// <summary>
    /// <c>$match</c> → <c>$group</c> (group fields under <c>_id</c>) → <c>$project</c> (flatten to output names) →
    /// <c>$sort</c>/<c>$skip</c>/<c>$limit</c>. Output names are validated identifiers.
    /// </summary>
    private static (PipelineDefinition<BsonDocument, BsonDocument> Pipeline, IReadOnlyList<(string Name, Type Type)> Outputs) BuildPipeline(
        CollectionMap map, DatabaseCommand command)
    {
        var aggregation = command.Aggregation!;
        var groups = aggregation.GroupBy.Select(map.Field).ToList();
        var groupId = new BsonDocument(groups.Select(g => new BsonElement(g.Name, "$" + g.Native)));
        var group = new BsonDocument("_id", groups.Count == 0 ? BsonNull.Value : groupId);
        var project = new BsonDocument("_id", 0);
        foreach (var g in groups)
        {
            project.Add(g.Name, "$_id." + g.Name);
        }

        foreach (var accumulator in aggregation.Accumulators)
        {
            DatabaseSchema.EnsureIdentifier(accumulator.Name, "accumulator");
            group.Add(accumulator.Name, accumulator.Function switch
            {
                AggregateFunction.Count => new BsonDocument("$sum", 1),
                AggregateFunction.Sum => new BsonDocument("$sum", "$" + FieldOf(map, accumulator).Native),
                AggregateFunction.Min => new BsonDocument("$min", "$" + FieldOf(map, accumulator).Native),
                AggregateFunction.Max => new BsonDocument("$max", "$" + FieldOf(map, accumulator).Native),
                AggregateFunction.Average => new BsonDocument("$avg", "$" + FieldOf(map, accumulator).Native),
                _ => throw new DatabaseQueryException($"Unsupported aggregate function {accumulator.Function}."),
            });
            project.Add(accumulator.Name, 1);
        }

        var stages = new List<BsonDocument>
        {
            new("$match", BuildFilter(map, command.Filter).Render(new RenderArgs<BsonDocument>(
                MongoDB.Bson.Serialization.BsonSerializer.LookupSerializer<BsonDocument>(), MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry))),
            new("$group", group),
            new("$project", project),
        };

        var outputs = groups.Select(g => (g.Name, g.ClrType)).Concat(aggregation.Accumulators.Select(a => (a.Name, typeof(object)))).ToList();
        if (command.Sort.Count > 0)
        {
            var names = outputs.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            stages.Add(new BsonDocument("$sort", new BsonDocument(command.Sort.Select(s => names.Contains(s.Field)
                ? new BsonElement(outputs.First(o => o.Name.Equals(s.Field, StringComparison.OrdinalIgnoreCase)).Name, s.Descending ? -1 : 1)
                : throw new DatabaseQueryException($"Aggregate results can only be sorted by a group field or an accumulator, not '{s.Field}'.")))));
        }

        if (command.Skip is > 0)
        {
            stages.Add(new BsonDocument("$skip", command.Skip.Value));
        }

        if (command.Take is not null)
        {
            stages.Add(new BsonDocument("$limit", command.Take.Value));
        }

        return (PipelineDefinition<BsonDocument, BsonDocument>.Create(stages), outputs);
    }

    private static FieldMap FieldOf(CollectionMap map, DatabaseAccumulator accumulator) =>
        map.Field(accumulator.Field ?? throw new DatabaseQueryException($"Accumulator '{accumulator.Name}' needs a field."));

    /// <summary>Builds the document as the EF provider stores it (keys under <c>_id</c>), generating a missing key.</summary>
    private static BsonDocument ToDocument(CollectionMap map, IReadOnlyDictionary<string, object?> values)
    {
        var document = new BsonDocument();
        foreach (var (name, value) in values)
        {
            var field = map.Field(name);
            MongoValues.SetPath(document, field.Native, MongoValues.ToBson(value, field));
        }

        if (map.Key is [var key] && !document.Contains("_id"))
        {
            object? generated = key.ClrType == typeof(Guid) ? Guid.NewGuid()
                : key.ClrType == typeof(long) ? MongoKeys.NextLong()
                : key.ClrType == typeof(int) ? MongoKeys.NextInt()
                : null;
            if (generated is not null)
            {
                document.InsertAt(0, new BsonElement("_id", MongoValues.ToBson(generated, key)));
            }
        }

        return document;
    }

    private static DatabaseRecord ToRecord(CollectionMap map, IReadOnlyList<FieldMap>? fields, BsonDocument document)
    {
        if (!map.IsMapped)
        {
            return new DatabaseRecord(document.Elements
                .Where(e => fields is null || e.Name == "_id" || fields.Any(f => f.Native == e.Name))
                .Select(e => KeyValuePair.Create(e.Name, MongoValues.FromBson(e.Value, typeof(object)))));
        }

        return new DatabaseRecord((fields ?? map.Fields).Select(f =>
            KeyValuePair.Create(f.Name, MongoValues.FromBson(MongoValues.GetPath(document, f.Native), f.ClrType))));
    }
}

/// <summary>A MongoDB session with an open transaction; aborted if disposed before commit.</summary>
public sealed class MongoTransaction(string database, IClientSessionHandle session) : IDatabaseTransaction
{
    public string Database { get; } = database;

    public IClientSessionHandle Session { get; } = session;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Session.CommitTransactionAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Session.AbortTransactionAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Session.IsInTransaction)
        {
            try
            {
                await Session.AbortTransactionAsync();
            }
            catch (MongoException)
            {
                // Already aborted by the server; disposing ends the session.
            }
        }

        Session.Dispose();
    }
}

/// <summary>Conversions between model values and BSON, matching how the EF Core MongoDB provider stores them.</summary>
public static class MongoValues
{
    public static BsonValue ToBson(object? value, FieldMap field)
    {
        var clr = ValueCoercion.ToClr(value, field.ClrType);
        return clr switch
        {
            null => BsonNull.Value,
            Guid guid => new BsonBinaryData(guid, GuidRepresentation.Standard),
            Enum e => Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture) is var n && n is >= int.MinValue and <= int.MaxValue
                ? new BsonInt32((int)n)
                : new BsonInt64(n),
            DateTime date => new BsonDateTime(ValueCoercion.AsUtc(date)),
            DateTimeOffset offset => new BsonDateTime(offset.UtcDateTime),
            decimal d => new BsonDecimal128(d),
            _ => BsonValue.Create(clr),
        };
    }

    public static object? FromBson(BsonValue value, Type type)
    {
        object? raw = value switch
        {
            null or BsonNull or BsonUndefined => null,
            BsonBinaryData { SubType: BsonBinarySubType.UuidStandard } binary => binary.ToGuid(GuidRepresentation.Standard),
            BsonBinaryData { SubType: BsonBinarySubType.UuidLegacy } binary => binary.ToGuid(GuidRepresentation.CSharpLegacy),
            BsonDateTime date => date.ToUniversalTime(),
            BsonObjectId id => id.Value.ToString(),
            BsonDecimal128 d => d.Value.ToString() is var text ? decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : null,
            BsonDocument or BsonArray => value.ToJson(),
            _ => BsonTypeMapper.MapToDotNetValue(value),
        };
        return ValueCoercion.ToClr(raw, type);
    }

    /// <summary>Reads a dotted path (<c>_id.UserId</c>); missing parts read as null.</summary>
    public static BsonValue GetPath(BsonDocument document, string path)
    {
        BsonValue current = document;
        foreach (var part in path.Split('.'))
        {
            if (current is not BsonDocument doc || !doc.TryGetValue(part, out current))
            {
                return BsonNull.Value;
            }
        }

        return current;
    }

    public static void SetPath(BsonDocument document, string path, BsonValue value)
    {
        var parts = path.Split('.');
        var target = document;
        foreach (var part in parts[..^1])
        {
            if (!target.TryGetValue(part, out var next) || next is not BsonDocument nested)
            {
                nested = new BsonDocument();
                target[part] = nested;
            }

            target = nested;
        }

        target[parts[^1]] = value;
    }
}
