using System.Globalization;
using DotNetForge.Abstractions.Database;

namespace DotNetForge.Data.Database;

/// <summary>
/// Converts command values to a field's model type and database values back to normalized .NET values, so the same
/// command means the same thing on every provider: <c>Eq("Status", 1)</c> works for an enum field, a Guid may be
/// passed as text, and dates come back as UTC <see cref="DateTime"/>s.
/// </summary>
public static class ValueCoercion
{
    /// <summary>Converts <paramref name="value"/> to <paramref name="target"/> (no conversion for <see cref="object"/>).</summary>
    public static object? ToClr(object? value, Type target)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(target) ?? target;
        if (type == typeof(object) || type.IsInstanceOfType(value))
        {
            return Normalize(value);
        }

        try
        {
            if (type.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(type, text, ignoreCase: true)
                    : Enum.ToObject(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }

            if (type == typeof(Guid))
            {
                return value switch
                {
                    byte[] bytes when bytes.Length == 16 => new Guid(bytes),
                    _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
                };
            }

            if (type == typeof(DateTime))
            {
                var date = value switch
                {
                    string text => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                    DateTimeOffset offset => offset.UtcDateTime,
                    _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
                };
                return AsUtc(date);
            }

            if (type == typeof(DateTimeOffset))
            {
                return value switch
                {
                    string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
                    DateTime date => new DateTimeOffset(AsUtc(date)),
                    _ => throw new InvalidCastException(),
                };
            }

            if (type == typeof(bool) && value is string boolText)
            {
                return bool.Parse(boolText);
            }

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new DatabaseQueryException($"A value can't be converted to {type.Name}.", ex);
        }
    }

    /// <summary>Normalizes a value read without a model type (dates to UTC, leaves the rest).</summary>
    public static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        DateTime date => AsUtc(date),
        _ => value,
    };

    /// <summary>The CMS stores UTC; providers return <see cref="DateTimeKind.Unspecified"/>, which is marked as UTC here.</summary>
    public static DateTime AsUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Utc => date,
        DateTimeKind.Local => date.ToUniversalTime(),
        _ => DateTime.SpecifyKind(date, DateTimeKind.Utc),
    };
}
