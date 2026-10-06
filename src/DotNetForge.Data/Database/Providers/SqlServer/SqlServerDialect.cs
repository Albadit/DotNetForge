using System.Text;

namespace DotNetForge.Data.Database.Providers.SqlServer;

/// <summary>
/// How SQL Server writes the statements <see cref="SqlServerSqlBuilder"/> builds: identifier quoting (<c>[name]</c>),
/// paging and <c>LIKE</c> escaping. Values never pass through here - they are parameters.
/// </summary>
public static class SqlServerDialect
{
    /// <summary>Escape character for <c>LIKE</c> patterns; <c>!</c> needs no escaping inside SQL string literals.</summary>
    public const char LikeEscape = '!';

    public static string QuoteIdentifier(string name) =>
        "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public static string ParameterName(int index) => $"@p{index}";

    /// <summary>Appends paging; <paramref name="hasOrderBy"/> tells whether an ORDER BY was written.</summary>
    public static void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null)
        {
            return;
        }

        // OFFSET ... FETCH needs an ORDER BY.
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

    /// <summary>Escapes <c>LIKE</c> wildcards (<c>%</c>, <c>_</c>, the escape character, and <c>[</c>) so the text matches literally.</summary>
    public static string EscapeLike(string text) =>
        text.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal)
            .Replace("[", "![", StringComparison.Ordinal);

    /// <summary>AVG of an integer column is an integer on SQL Server; average as floating point like the others.</summary>
    public static string Aggregate(string function, string expression) =>
        function == "AVG" ? $"AVG(CAST({expression} AS float))" : $"{function}({expression})";
}
