namespace DotNetForge.Infrastructure.Configuration;

/// <summary>
/// A tiny, dependency-free <c>.env</c> parser. Supports <c>KEY=VALUE</c> lines, <c>#</c> comments,
/// blank lines, optional <c>export</c> prefixes, and single/double-quoted values. Kept in-house to
/// honor the "avoid unnecessary dependencies" principle (.docs/architecture/codebase.md).
/// </summary>
public static class DotEnvParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(content))
        {
            return result;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
