using Newtonsoft.Json.Linq;

namespace DotNetForge.Core.Content;

/// <summary>
/// The pure rules of translated entries (.docs/features/internationalization.md#entries): which fields get a value per
/// locale - the top-level text, textarea and rich text fields of a type with <c>Localized</c> on, except unique ones (an
/// SKU or code is the same in every language, and uniqueness is checked on the entry's own values); every other field is
/// shared by all locales - and how a translation shows: its non-empty values over the entry's own, the rest falls back.
/// </summary>
public static class EntryTranslations
{
    /// <summary>The field types a translation can hold.</summary>
    public static readonly IReadOnlySet<string> TranslatableTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        FieldTypes.Text, FieldTypes.Textarea, FieldTypes.RichText,
    };

    /// <summary>The fields of a type that are translated (top level, text-like, not unique), in their order.</summary>
    public static IReadOnlyList<FieldDefinition> Fields(IReadOnlyList<FieldDefinition> fields) =>
        fields.Where(f => TranslatableTypes.Contains(f.Type) && !f.Unique).ToList();

    /// <summary>The translated values of <paramref name="data"/>: only the translatable fields, only non-empty values.</summary>
    public static JObject Keep(IReadOnlyList<FieldDefinition> fields, JObject data)
    {
        var kept = new JObject();
        foreach (var f in Fields(fields))
        {
            if (data[f.Key] is { } value && !IsEmpty(value))
            {
                kept[f.Key] = value.DeepClone();
            }
        }

        return kept;
    }

    /// <summary>The entry as a locale shows it: a copy of <paramref name="main"/> with the translation's non-empty translatable values.</summary>
    public static JObject Merge(IReadOnlyList<FieldDefinition> fields, JObject main, JObject? translation)
    {
        var merged = (JObject)main.DeepClone();
        if (translation is null)
        {
            return merged;
        }

        foreach (var f in Fields(fields))
        {
            if (translation[f.Key] is { } value && !IsEmpty(value))
            {
                merged[f.Key] = value.DeepClone();
            }
        }

        return merged;
    }

    /// <summary>
    /// The default locale changes: the new main values (the translation over the old main) and the old main values of the
    /// translated fields, which become the old default's translation.
    /// </summary>
    public static (JObject Main, JObject OldTranslation) Swap(IReadOnlyList<FieldDefinition> fields, JObject main, JObject translation)
    {
        var old = new JObject();
        foreach (var f in Fields(fields))
        {
            if (translation[f.Key] is { } value && !IsEmpty(value) && main[f.Key] is { } previous && !IsEmpty(previous))
            {
                old[f.Key] = previous.DeepClone();
            }
        }

        return (Merge(fields, main, translation), old);
    }

    /// <summary>No value: null, a blank string, an empty list or object (an empty rich text).</summary>
    public static bool IsEmpty(JToken? value) => value switch
    {
        null => true,
        { Type: JTokenType.Null or JTokenType.Undefined } => true,
        JValue { Type: JTokenType.String } s => string.IsNullOrWhiteSpace((string?)s),
        JArray a => a.Count == 0,
        JObject o => !o.HasValues,
        _ => false,
    };
}
