namespace DotNetForge.Abstractions.Database;

/// <summary>
/// A provider-neutral filter tree. Values are data, never syntax: providers send them as SQL parameters or typed BSON
/// values, so a value such as <c>"x' OR 1=1"</c> or <c>{ "$ne": null }</c> is only ever compared as text.
/// Combine with <c>&amp;</c>, <c>|</c> and <c>!</c>: <c>Filter.Eq("Status", 1) &amp; Filter.Contains("Email", "@example.com")</c>.
/// </summary>
public abstract record DatabaseFilter
{
    public static DatabaseFilter operator &(DatabaseFilter left, DatabaseFilter right) => And(left, right);

    public static DatabaseFilter operator |(DatabaseFilter left, DatabaseFilter right) => Or(left, right);

    public static DatabaseFilter operator !(DatabaseFilter filter) => new NotFilter(filter);

    public static DatabaseFilter Eq(string field, object? value) =>
        value is null ? new NullFilter(field, IsNull: true) : new ComparisonFilter(field, ComparisonOperator.Equal, value);

    public static DatabaseFilter Ne(string field, object? value) =>
        value is null ? new NullFilter(field, IsNull: false) : new ComparisonFilter(field, ComparisonOperator.NotEqual, value);

    public static DatabaseFilter Gt(string field, object value) => new ComparisonFilter(field, ComparisonOperator.GreaterThan, value);

    public static DatabaseFilter Gte(string field, object value) =>
        new ComparisonFilter(field, ComparisonOperator.GreaterThanOrEqual, value);

    public static DatabaseFilter Lt(string field, object value) => new ComparisonFilter(field, ComparisonOperator.LessThan, value);

    public static DatabaseFilter Lte(string field, object value) =>
        new ComparisonFilter(field, ComparisonOperator.LessThanOrEqual, value);

    public static DatabaseFilter In(string field, IEnumerable<object?> values) => new InFilter(field, values.ToArray());

    public static DatabaseFilter Contains(string field, string text, bool ignoreCase = false) =>
        new TextFilter(field, TextMatch.Contains, text, ignoreCase);

    public static DatabaseFilter StartsWith(string field, string text, bool ignoreCase = false) =>
        new TextFilter(field, TextMatch.StartsWith, text, ignoreCase);

    public static DatabaseFilter And(params DatabaseFilter[] filters) => new CompositeFilter(LogicalOperator.And, filters);

    public static DatabaseFilter Or(params DatabaseFilter[] filters) => new CompositeFilter(LogicalOperator.Or, filters);
}

public enum ComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

public enum TextMatch
{
    Contains,
    StartsWith,
}

public enum LogicalOperator
{
    And,
    Or,
}

public sealed record ComparisonFilter(string Field, ComparisonOperator Operator, object Value) : DatabaseFilter;

/// <summary>Matches when the field equals any of <see cref="Values"/>; an empty list matches nothing.</summary>
public sealed record InFilter(string Field, IReadOnlyList<object?> Values) : DatabaseFilter;

/// <summary>Substring or prefix match. The text is matched literally (wildcards and regex characters are escaped).</summary>
public sealed record TextFilter(string Field, TextMatch Match, string Value, bool IgnoreCase) : DatabaseFilter;

public sealed record NullFilter(string Field, bool IsNull) : DatabaseFilter;

public sealed record CompositeFilter(LogicalOperator Operator, IReadOnlyList<DatabaseFilter> Filters) : DatabaseFilter;

public sealed record NotFilter(DatabaseFilter Filter) : DatabaseFilter;
