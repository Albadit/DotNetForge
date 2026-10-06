using System.Text;

namespace DotNetForge.Data.Database.Relational;

/// <summary>
/// The few things that differ between SQL databases for the statements <see cref="SqlCommandBuilder"/> writes:
/// identifier quoting, paging and <c>LIKE</c> escaping. Values never pass through a dialect - they are parameters.
/// </summary>
public abstract class SqlDialect
{
    /// <summary>Escape character for <c>LIKE</c> patterns; <c>!</c> needs no escaping inside SQL string literals anywhere.</summary>
    public const char LikeEscape = '!';

    public abstract string QuoteIdentifier(string name);

    public virtual string ParameterName(int index) => $"@p{index}";

    /// <summary>Appends paging; <paramref name="hasOrderBy"/> tells whether an ORDER BY was written.</summary>
    public abstract void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy);

    /// <summary>Escapes <c>LIKE</c> wildcards so the text matches literally.</summary>
    public virtual string EscapeLike(string text) =>
        text.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);

    /// <summary>Aggregate expression; overridden where a database needs a cast (integer average on SQL Server).</summary>
    public virtual string Aggregate(string function, string expression) => $"{function}({expression})";

    protected static string Quote(string name, char open, char close) =>
        open + name.Replace(close.ToString(), new string(close, 2), StringComparison.Ordinal) + close;
}

/// <summary>SQLite: <c>"name"</c>, <c>LIMIT/OFFSET</c> (LIMIT is required with OFFSET; -1 = no limit).</summary>
public sealed class SqliteDialect : SqlDialect
{
    public override string QuoteIdentifier(string name) => Quote(name, '"', '"');

    public override void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null)
        {
            return;
        }

        sql.Append(" LIMIT ").Append(take ?? -1);
        if (skip is > 0)
        {
            sql.Append(" OFFSET ").Append(skip.Value);
        }
    }
}

/// <summary>PostgreSQL: <c>"name"</c>, <c>LIMIT/OFFSET</c>.</summary>
public sealed class PostgreSqlDialect : SqlDialect
{
    public override string QuoteIdentifier(string name) => Quote(name, '"', '"');

    public override void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (take is not null)
        {
            sql.Append(" LIMIT ").Append(take.Value);
        }

        if (skip is > 0)
        {
            sql.Append(" OFFSET ").Append(skip.Value);
        }
    }
}

/// <summary>MySQL: <c>`name`</c>, <c>LIMIT/OFFSET</c> (LIMIT is required with OFFSET).</summary>
public sealed class MySqlDialect : SqlDialect
{
    public override string QuoteIdentifier(string name) => Quote(name, '`', '`');

    public override void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null)
        {
            return;
        }

        sql.Append(" LIMIT ").Append(take is null ? "18446744073709551615" : take.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (skip is > 0)
        {
            sql.Append(" OFFSET ").Append(skip.Value);
        }
    }
}

/// <summary>SQL Server: <c>[name]</c>, <c>OFFSET … FETCH</c> (needs an ORDER BY), <c>[</c> is a LIKE wildcard too.</summary>
public sealed class SqlServerDialect : SqlDialect
{
    public override string QuoteIdentifier(string name) => Quote(name, '[', ']');

    public override void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null)
        {
            return;
        }

        if (!hasOrderBy)
        {
            sql.Append(" ORDER BY (SELECT NULL)");
        }

        sql.Append(" OFFSET ").Append(skip ?? 0).Append(" ROWS");
        if (take is not null)
        {
            sql.Append(" FETCH NEXT ").Append(take.Value).Append(" ROWS ONLY");
        }
    }

    public override string EscapeLike(string text) => base.EscapeLike(text).Replace("[", "![", StringComparison.Ordinal);

    /// <summary>AVG of an integer column is an integer on SQL Server; average as floating point like the others.</summary>
    public override string Aggregate(string function, string expression) =>
        function == "AVG" ? $"AVG(CAST({expression} AS float))" : base.Aggregate(function, expression);
}
