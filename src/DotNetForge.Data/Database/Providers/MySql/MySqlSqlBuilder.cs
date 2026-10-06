using System.Text;
using DotNetForge.Abstractions.Database;

namespace DotNetForge.Data.Database.Providers.MySql;

/// <summary>A SQL statement and its parameters. The text only ever contains quoted identifiers and parameter names.</summary>
public sealed record MySqlSqlStatement(string Text, IReadOnlyList<MySqlSqlParameter> Parameters);

/// <summary>A parameter value plus the field it is compared with or written to (for the field's type mapping).</summary>
public sealed record MySqlSqlParameter(string Name, object? Value, FieldMap? Field);

/// <summary>
/// Translates a <see cref="DatabaseCommand"/> into parameterized MySQL SQL for one table (.docs/database/query-routing.md).
/// This is MySQL's own copy (no SQL code is shared between databases). Identifiers come from the
/// <see cref="CollectionMap"/> (validated) and are quoted by <see cref="MySqlDialect"/>; every value
/// becomes a parameter, including LIKE patterns, so there is no path for SQL injection through command values.
/// </summary>
public sealed class MySqlSqlBuilder
{
    private readonly CollectionMap _map;
    private readonly List<MySqlSqlParameter> _parameters = new();

    public MySqlSqlBuilder(CollectionMap map)
    {
        _map = map;
    }

    private string Table => _map.Schema is null
        ? MySqlDialect.QuoteIdentifier(_map.Native)
        : $"{MySqlDialect.QuoteIdentifier(_map.Schema)}.{MySqlDialect.QuoteIdentifier(_map.Native)}";

    /// <summary>SELECT of <paramref name="fields"/> (in order) with filter, sort and paging.</summary>
    public MySqlSqlStatement Select(IReadOnlyList<FieldMap> fields, DatabaseFilter? filter, IReadOnlyList<DatabaseSort> sort, int? skip, int? take)
    {
        // No fields: every column (databases without a model, where columns aren't known up front).
        var sql = new StringBuilder("SELECT ")
            .Append(fields.Count == 0 ? "*" : string.Join(", ", fields.Select(f => MySqlDialect.QuoteIdentifier(f.Native))))
            .Append(" FROM ").Append(Table);
        AppendWhere(sql, filter);
        var hasOrder = AppendOrderBy(sql, sort.Select(s => (Column(s.Field), s.Descending)));
        MySqlDialect.AppendPaging(sql, skip, take, hasOrder);
        return Build(sql);
    }

    public MySqlSqlStatement Count(DatabaseFilter? filter)
    {
        var sql = new StringBuilder("SELECT COUNT(*) FROM ").Append(Table);
        AppendWhere(sql, filter);
        return Build(sql);
    }

    /// <summary>GROUP BY the aggregation's fields; result columns: group fields, then accumulators, in that order.</summary>
    public MySqlSqlStatement Aggregate(DatabaseAggregation aggregation, DatabaseFilter? filter, IReadOnlyList<DatabaseSort> sort, int? skip, int? take)
    {
        var groups = aggregation.GroupBy.Select(_map.Field).ToList();
        var columns = groups.Select(g => $"{MySqlDialect.QuoteIdentifier(g.Native)} AS {MySqlDialect.QuoteIdentifier(g.Name)}")
            .Concat(aggregation.Accumulators.Select(a => $"{AccumulatorExpression(a)} AS {MySqlDialect.QuoteIdentifier(a.Name)}"));

        var sql = new StringBuilder("SELECT ").AppendJoin(", ", columns).Append(" FROM ").Append(Table);
        AppendWhere(sql, filter);
        if (groups.Count > 0)
        {
            sql.Append(" GROUP BY ").AppendJoin(", ", groups.Select(g => MySqlDialect.QuoteIdentifier(g.Native)));
        }

        // Sort by an output column: a group field or an accumulator name.
        var outputNames = groups.Select(g => g.Name).Concat(aggregation.Accumulators.Select(a => a.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasOrder = AppendOrderBy(sql, sort.Select(s => outputNames.Contains(s.Field)
            ? (MySqlDialect.QuoteIdentifier(outputNames.First(n => n.Equals(s.Field, StringComparison.OrdinalIgnoreCase))), s.Descending)
            : throw new DatabaseQueryException($"Aggregate results can only be sorted by a group field or an accumulator, not '{s.Field}'.")));
        MySqlDialect.AppendPaging(sql, skip, take, hasOrder);
        return Build(sql);
    }

    public MySqlSqlStatement Insert(IReadOnlyDictionary<string, object?> document)
    {
        var values = document.Select(kv => (Field: _map.Field(kv.Key), kv.Value)).ToList();
        var sql = new StringBuilder("INSERT INTO ").Append(Table).Append(" (")
            .AppendJoin(", ", values.Select(v => MySqlDialect.QuoteIdentifier(v.Field.Native)))
            .Append(") VALUES (")
            .AppendJoin(", ", values.Select(v => Parameter(v.Value, v.Field)))
            .Append(')');
        return Build(sql);
    }

    public MySqlSqlStatement Update(IReadOnlyDictionary<string, object?> set, DatabaseFilter? filter)
    {
        var assignments = set.Select(kv =>
        {
            var field = _map.Field(kv.Key);
            if (field.IsKey)
            {
                throw new DatabaseQueryException($"Key field '{field.Name}' can't be updated.");
            }

            return $"{MySqlDialect.QuoteIdentifier(field.Native)} = {Parameter(kv.Value, field)}";
        }).ToList();

        var sql = new StringBuilder("UPDATE ").Append(Table).Append(" SET ").AppendJoin(", ", assignments);
        AppendWhere(sql, filter);
        return Build(sql);
    }

    public MySqlSqlStatement Delete(DatabaseFilter? filter)
    {
        var sql = new StringBuilder("DELETE FROM ").Append(Table);
        AppendWhere(sql, filter);
        return Build(sql);
    }

    private string AccumulatorExpression(DatabaseAccumulator accumulator)
    {
        DatabaseSchema.EnsureIdentifier(accumulator.Name, "accumulator");
        if (accumulator.Function == AggregateFunction.Count)
        {
            return "COUNT(*)";
        }

        var field = _map.Field(accumulator.Field
            ?? throw new DatabaseQueryException($"Accumulator '{accumulator.Name}' needs a field."));
        var function = accumulator.Function switch
        {
            AggregateFunction.Sum => "SUM",
            AggregateFunction.Min => "MIN",
            AggregateFunction.Max => "MAX",
            AggregateFunction.Average => "AVG",
            _ => throw new DatabaseQueryException($"Unsupported aggregate function {accumulator.Function}."),
        };
        return MySqlDialect.Aggregate(function, MySqlDialect.QuoteIdentifier(field.Native));
    }

    private string Column(string field) => MySqlDialect.QuoteIdentifier(_map.Field(field).Native);

    private void AppendWhere(StringBuilder sql, DatabaseFilter? filter)
    {
        if (filter is not null)
        {
            sql.Append(" WHERE ").Append(Condition(filter));
        }
    }

    private bool AppendOrderBy(StringBuilder sql, IEnumerable<(string Column, bool Descending)> sort)
    {
        var parts = sort.Select(s => s.Column + (s.Descending ? " DESC" : " ASC")).ToList();
        if (parts.Count == 0)
        {
            return false;
        }

        sql.Append(" ORDER BY ").AppendJoin(", ", parts);
        return true;
    }

    private string Condition(DatabaseFilter filter) => filter switch
    {
        ComparisonFilter c => $"{Column(c.Field)} {Operator(c.Operator)} {Parameter(c.Value, _map.Field(c.Field))}",
        NullFilter n => $"{Column(n.Field)} IS {(n.IsNull ? "" : "NOT ")}NULL",
        InFilter i when i.Values.Count == 0 => "1=0",
        InFilter i => $"{Column(i.Field)} IN ({string.Join(", ", i.Values.Select(v => Parameter(v, _map.Field(i.Field))))})",
        TextFilter t => TextCondition(t),
        CompositeFilter { Filters.Count: 0 } c => c.Operator == LogicalOperator.And ? "1=1" : "1=0",
        CompositeFilter c => "(" + string.Join(c.Operator == LogicalOperator.And ? " AND " : " OR ", c.Filters.Select(Condition)) + ")",
        NotFilter n => $"NOT ({Condition(n.Filter)})",
        _ => throw new DatabaseQueryException($"Unsupported filter {filter.GetType().Name}."),
    };

    private string TextCondition(TextFilter filter)
    {
        var field = _map.Field(filter.Field);
        if (_map.IsMapped && field.ClrType != typeof(string))
        {
            throw new DatabaseQueryException($"Text matching needs a text field; '{field.Name}' is {field.ClrType.Name}.");
        }

        var pattern = filter.Match == TextMatch.Contains
            ? $"%{MySqlDialect.EscapeLike(filter.Value)}%"
            : $"{MySqlDialect.EscapeLike(filter.Value)}%";
        var column = MySqlDialect.QuoteIdentifier(field.Native);
        var parameter = Parameter(pattern, field);
        return filter.IgnoreCase
            ? $"LOWER({column}) LIKE LOWER({parameter}) ESCAPE '{MySqlDialect.LikeEscape}'"
            : $"{column} LIKE {parameter} ESCAPE '{MySqlDialect.LikeEscape}'";
    }

    private static string Operator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "<>",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        _ => throw new DatabaseQueryException($"Unsupported operator {op}."),
    };

    private string Parameter(object? value, FieldMap? field)
    {
        var name = MySqlDialect.ParameterName(_parameters.Count);
        var converted = field is null ? ValueCoercion.Normalize(value) : ValueCoercion.ToClr(value, field.ClrType);
        _parameters.Add(new MySqlSqlParameter(name, converted, field));
        return name;
    }

    private MySqlSqlStatement Build(StringBuilder sql) => new(sql.ToString(), _parameters.ToList());
}
