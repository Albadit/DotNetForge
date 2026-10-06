using System.Collections;
using System.Globalization;

namespace DotNetForge.Abstractions.Database;

/// <summary>
/// The outcome of a command, the same shape for every provider. Returned by <see cref="IDatabaseService"/>: the
/// throwing methods only return successful results; the <c>Try</c> methods return failures with <see cref="Error"/>.
/// </summary>
public sealed record DatabaseResult<T>
{
    public T? Data { get; init; }

    public bool Success => Error is null;

    /// <summary>Rows/documents read, written or counted.</summary>
    public long AffectedRows { get; init; }

    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Display name of the provider that ran the command, e.g. <c>MongoDB</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Configured database name, e.g. <c>main</c>.</summary>
    public required string Database { get; init; }

    public DatabaseException? Error { get; init; }

    /// <summary>The data of a successful result; throws the error of a failed one.</summary>
    public T? EnsureSuccess() => Error is null ? Data : throw Error;
}

/// <summary>
/// One row or document with normalized .NET values (<see cref="Guid"/>, <see cref="DateTime"/> in UTC, <see cref="long"/>,
/// <see cref="string"/>, ...). Field names are case-insensitive and use the model's property names, so the same code
/// reads a record from SQL or MongoDB.
/// </summary>
public sealed class DatabaseRecord : IReadOnlyDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _values;

    public DatabaseRecord(IEnumerable<KeyValuePair<string, object?>> values)
    {
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The value converted to <typeparamref name="T"/> (numeric widening/narrowing, enums, Guid from text).</summary>
    public T? Get<T>(string field)
    {
        if (!_values.TryGetValue(field, out var value) || value is null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (target.IsEnum)
        {
            return (T)Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (target == typeof(Guid))
        {
            return (T)(object)(value is byte[] bytes ? new Guid(bytes) : Guid.Parse(value.ToString()!));
        }

        return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    public object? this[string key] => _values[key];

    public IEnumerable<string> Keys => _values.Keys;

    public IEnumerable<object?> Values => _values.Values;

    public int Count => _values.Count;

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Result of a connection test. <see cref="Server"/> never contains credentials.</summary>
public sealed record DatabaseConnectionInfo(string Provider, string Database, string Server, TimeSpan Latency);

/// <summary>A configured database as diagnostics may show it (no connection string).</summary>
public sealed record DatabaseDescriptor(string Name, string Provider, string Server, bool IsMain);
