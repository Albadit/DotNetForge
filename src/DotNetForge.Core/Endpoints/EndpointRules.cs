using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetForge.Core.Endpoints;

/// <summary>One parameter of a custom endpoint: where its value comes from, its type, and whether it may be missing.</summary>
/// <param name="Source"><c>route</c> (a <c>{name}</c> path segment), <c>query</c> (<c>?name=</c>) or <c>body</c> (a JSON property).</param>
/// <param name="Default">Used when the request has no value (text, converted like a request value).</param>
public sealed record EndpointParameter(string Name, string Type, string Source, bool Required, string? Default);

/// <summary>A value that could not be used, with the parameter it belongs to.</summary>
public sealed record EndpointValueError(string Parameter, string Message);

/// <summary>
/// The rules of custom API endpoints (.docs/features/custom-endpoints.md): methods, path templates, parameter
/// definitions, and turning request values into typed SQL parameter values. Pure: no database, no HTTP.
/// </summary>
public static partial class EndpointRules
{
    public const int MaxParameters = 50;
    public const int MaxSql = 50_000;

    public static readonly IReadOnlyList<string> Methods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };

    public static readonly IReadOnlyList<string> Types = new[] { "string", "int", "number", "bool", "date", "guid", "json" };

    public static readonly IReadOnlyList<string> Sources = new[] { "route", "query", "body" };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"^(?:[a-z0-9][a-z0-9_.-]{0,59}|\{[A-Za-z_][A-Za-z0-9_]{0,63}\})$", RegexOptions.CultureInvariant)]
    private static partial Regex PathSegment();

    // @name, but not @@name (SQL Server globals) or name@domain inside text.
    [GeneratedRegex(@"(?<![@\w])@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SqlParameter();

    /// <summary>A path as stored: trimmed of slashes, literal segments lower-cased. Null when it is not a valid template.</summary>
    public static string? NormalizePath(string? path)
    {
        var segments = (path ?? string.Empty).Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith('{') ? s : s.ToLowerInvariant()).ToList();
        if (segments.Count == 0 || segments.Count > 10 || segments.Any(s => !PathSegment().IsMatch(s)))
        {
            return null;
        }

        var names = segments.Where(s => s.StartsWith('{')).Select(s => s.Trim('{', '}').ToLowerInvariant()).ToList();
        return names.Count == names.Distinct().Count() ? string.Join('/', segments) : null;
    }

    /// <summary>An operation's sub-path as stored: empty for the endpoint's own path, else like <see cref="NormalizePath"/>. Null when invalid.</summary>
    public static string? NormalizeSubPath(string? path) =>
        (path ?? string.Empty).Trim().Trim('/').Length == 0 ? string.Empty : NormalizePath(path);

    /// <summary>
    /// The full template an operation answers: the endpoint's path plus the operation's sub-path. Null when together they
    /// are not a valid template (more than 10 segments, or the same <c>{name}</c> twice).
    /// </summary>
    public static string? Combine(string endpointPath, string subPath) =>
        subPath.Length == 0 ? NormalizePath(endpointPath) : NormalizePath(endpointPath + "/" + subPath);

    /// <summary>The <c>{name}</c> segments of a path.</summary>
    public static IReadOnlyList<string> RouteNames(string path) =>
        path.Split('/').Where(s => s.StartsWith('{')).Select(s => s.Trim('{', '}')).ToList();

    /// <summary>The route values when <paramref name="requestPath"/> fits <paramref name="template"/>, else null. Literal segments ignore case.</summary>
    public static IReadOnlyDictionary<string, string>? Match(string template, string requestPath)
    {
        var expected = template.Split('/');
        var actual = requestPath.Trim('/').Split('/');
        if (expected.Length != actual.Length)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i].StartsWith('{'))
            {
                var value = Uri.UnescapeDataString(actual[i]);
                if (value.Length == 0)
                {
                    return null;
                }

                values[expected[i].Trim('{', '}')] = value;
            }
            else if (!string.Equals(expected[i], actual[i], StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return values;
    }

    /// <summary>
    /// Reads and checks parameter definitions (the editor's JSON). Every <c>{name}</c> in the path must have a route
    /// parameter; route parameters are always required.
    /// </summary>
    public static (IReadOnlyList<EndpointParameter> Parameters, IReadOnlyList<string> Errors) ParseParameters(string? json, string? path)
    {
        var errors = new List<string>();
        var output = new List<EndpointParameter>();
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return (output, new[] { "The parameters are not valid JSON." });
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return (output, new[] { "The parameters must be a list." });
        }

        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add("A parameter is not an object.");
                continue;
            }

            var name = Str(item, "name")?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue; // an empty row of the editor
            }

            if (!ParameterName().IsMatch(name))
            {
                errors.Add($"Parameter \"{name}\": use letters, digits and _, starting with a letter or _.");
                continue;
            }

            if (output.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Parameter \"{name}\" is listed twice.");
                continue;
            }

            var type = Str(item, "type") is { } t && Types.Contains(t) ? t : "string";
            var source = Str(item, "source") is { } s && Sources.Contains(s) ? s : "query";
            var required = item.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
            var def = Str(item, "default");
            if (def is { Length: > 0 } && Convert(new EndpointParameter(name, type, source, false, null), def) is { Error: { } error })
            {
                errors.Add($"Parameter \"{name}\": the default {error}");
            }

            output.Add(new EndpointParameter(name, type, source, required || source == "route", string.IsNullOrEmpty(def) ? null : def));
        }

        if (output.Count > MaxParameters)
        {
            errors.Add($"An endpoint can have at most {MaxParameters} parameters.");
        }

        foreach (var routeName in path is null ? Array.Empty<string>() : RouteNames(path))
        {
            var p = output.FirstOrDefault(x => string.Equals(x.Name, routeName, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                errors.Add($"The path has {{{routeName}}}: add a parameter \"{routeName}\" from the route.");
            }
            else if (p.Source != "route")
            {
                errors.Add($"Parameter \"{p.Name}\" is in the path, so its source must be route.");
            }
        }

        foreach (var p in output.Where(p => p.Source == "route" && (path is null || !RouteNames(path).Any(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase)))))
        {
            errors.Add($"Parameter \"{p.Name}\" comes from the route, but the path has no {{{p.Name}}}.");
        }

        return (output, errors);
    }

    /// <summary>The <c>@name</c> placeholders the SQL uses (for hints; the database reports a missing one when it runs).</summary>
    public static IReadOnlyList<string> SqlParameterNames(string sql) =>
        SqlParameter().Matches(sql).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// A request value (text from the route or query, or a JSON value from the body) as the parameter's type. Null with
    /// no error means "no value".
    /// </summary>
    public static (object? Value, string? Error) Convert(EndpointParameter p, object? raw)
    {
        if (raw is JsonElement json)
        {
            if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return (null, null);
            }

            if (p.Type == "json")
            {
                return (json.GetRawText(), null);
            }

            raw = json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText();
        }

        var text = raw as string;
        if (text is null)
        {
            return (null, null);
        }

        switch (p.Type)
        {
            case "int":
                return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? (l, null) : (null, "must be a whole number.");
            case "number":
                return decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? (d, null) : (null, "must be a number.");
            case "bool":
                return text.Trim().ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "on" => (true, null),
                    "false" or "0" or "no" or "off" => (false, null),
                    _ => (null, "must be true or false."),
                };
            case "date":
                return DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? (date.UtcDateTime, null) : (null, "must be a date (ISO 8601, e.g. 2026-10-26).");
            case "guid":
                return Guid.TryParse(text.Trim(), out var g) ? (g, null) : (null, "must be a GUID.");
            case "json":
                try
                {
                    using var _ = JsonDocument.Parse(text);
                    return (text, null);
                }
                catch (JsonException)
                {
                    return (null, "must be JSON.");
                }

            default:
                return (text, null);
        }
    }

    /// <summary>
    /// Every parameter's value from the request: route values, the query string and the JSON body, converted and with
    /// defaults applied. Missing required values and values of the wrong type are errors.
    /// </summary>
    public static (IReadOnlyDictionary<string, object?> Values, IReadOnlyList<EndpointValueError> Errors) Bind(
        IReadOnlyList<EndpointParameter> parameters, IReadOnlyDictionary<string, string> route,
        Func<string, string?> query, JsonElement? body)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<EndpointValueError>();
        foreach (var p in parameters)
        {
            object? raw = p.Source switch
            {
                "route" => route.TryGetValue(p.Name, out var r) ? r : null,
                "query" => query(p.Name),
                _ => body is { ValueKind: JsonValueKind.Object } b && TryProperty(b, p.Name, out var prop) ? prop : null,
            };
            var (value, error) = Convert(p, raw);
            if (error is null && value is null && p.Default is not null)
            {
                (value, error) = Convert(p, p.Default);
            }

            if (error is not null)
            {
                errors.Add(new EndpointValueError(p.Name, $"{p.Name} {error}"));
            }
            else if (value is null && p.Required)
            {
                errors.Add(new EndpointValueError(p.Name, $"{p.Name} is required ({p.Source})."));
            }

            values[p.Name] = value;
        }

        return (values, errors);
    }

    private static bool TryProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
