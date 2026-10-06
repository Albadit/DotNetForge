using System.Text;

namespace DotNetForge.Data.Database.Providers.Sqlite;

/// <summary>
/// How SQLite writes the statements <see cref="SqliteSqlBuilder"/> builds: identifier quoting (<c>"name"</c>),
/// paging and <c>LIKE</c> escaping. Values never pass through here - they are parameters.
/// </summary>
public static class SqliteDialect
{
    /// <summary>Escape character for <c>LIKE</c> patterns; <c>!</c> needs no escaping inside SQL string literals.</summary>
    public const char LikeEscape = '!';

    public static string QuoteIdentifier(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string ParameterName(int index) => $"@p{index}";

    /// <summary>Appends paging; <paramref name="hasOrderBy"/> tells whether an ORDER BY was written.</summary>
    public static void AppendPaging(StringBuilder sql, int? skip, int? take, bool hasOrderBy)
    {
        if (skip is null && take is null)
        {
            return;
        }

        // LIMIT is required with OFFSET; -1 means no limit.
        sql.Append(" LIMIT ").Append(take ?? -1);
        if (skip is > 0)
        {
            sql.Append(" OFFSET ").Append(skip.Value);
        }
    }

    /// <summary>Escapes <c>LIKE</c> wildcards (<c>%</c>, <c>_</c>, the escape character) so the text matches literally.</summary>
    public static string EscapeLike(string text) =>
        text.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);

    /// <summary>Aggregate expression, e.g. <c>SUM("Amount")</c>.</summary>
    public static string Aggregate(string function, string expression) =>
        $"{function}({expression})";
}
