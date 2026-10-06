using DotNetForge.Abstractions.Database;

namespace DotNetForge.Data.Database;

/// <summary>
/// Shape checks before a command reaches any provider. Writes that could touch every record need an explicit filter:
/// pass <c>DatabaseFilter.And()</c> (an empty AND, which matches everything) to really mean "all".
/// </summary>
public static class DatabaseCommandValidator
{
    public static void Validate(DatabaseCommand command, bool query)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Collection))
        {
            Fail("A collection or table is required.");
        }

        var isQuery = command.Operation is DatabaseOperation.Find or DatabaseOperation.FindOne or DatabaseOperation.Aggregate;
        if (isQuery != query)
        {
            Fail(query
                ? $"{command.Operation} is not a query: use ExecuteAsync."
                : $"{command.Operation} returns records: use QueryAsync or StreamAsync.");
        }

        if (command.Skip is < 0 || command.Take is < 0)
        {
            Fail("Skip and Take can't be negative.");
        }

        if (command.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            Fail("The timeout must be positive.");
        }

        if (command.Fields is { Count: 0 })
        {
            Fail("Fields can't be empty; pass null for all fields.");
        }

        switch (command.Operation)
        {
            case DatabaseOperation.InsertOne when command.Documents.Count != 1:
                Fail("InsertOne needs exactly one document.");
                break;
            case DatabaseOperation.InsertMany when command.Documents.Count == 0:
                Fail("InsertMany needs at least one document.");
                break;
            case DatabaseOperation.InsertOne or DatabaseOperation.InsertMany when command.Documents.Any(d => d.Count == 0):
                Fail("Documents to insert can't be empty.");
                break;
            case DatabaseOperation.UpdateOne or DatabaseOperation.UpdateMany when command.Set is null || command.Set.Count == 0:
                Fail($"{command.Operation} needs at least one field to set.");
                break;
            case DatabaseOperation.UpdateOne or DatabaseOperation.UpdateMany or DatabaseOperation.DeleteOne or DatabaseOperation.DeleteMany
                when command.Filter is null:
                Fail($"{command.Operation} needs a filter; use DatabaseFilter.And() to target every record on purpose.");
                break;
            case DatabaseOperation.Aggregate when command.Aggregation is null || command.Aggregation.Accumulators.Count == 0:
                Fail("Aggregate needs at least one accumulator.");
                break;
        }
    }

    private static void Fail(string message) => throw new DatabaseQueryException(message);
}
