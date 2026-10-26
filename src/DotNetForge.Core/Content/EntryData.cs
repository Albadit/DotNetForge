using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotNetForge.PageBuilder;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Core.Content;

/// <summary>A problem with one value; <see cref="Path"/> is the dotted field path (<c>variants.2.name</c>).</summary>
public sealed record FieldError(string Path, string Message);

/// <summary>
/// Answers whether referenced records exist: media files (images only, or any file) and entries
/// of a given content type. The service looks them up before validating, so validation stays pure.
/// </summary>
public sealed class EntryReferences
{
    public static readonly EntryReferences None = new((_, _) => false, (_, _) => false);

    private readonly Func<Guid, bool, bool> _media;
    private readonly Func<Guid, Guid, bool> _entry;

    /// <param name="media">(file id, images only) → exists.</param>
    /// <param name="entry">(entry id, content type id) → exists.</param>
    public EntryReferences(Func<Guid, bool, bool> media, Func<Guid, Guid, bool> entry)
    {
        _media = media;
        _entry = entry;
    }

    public bool MediaExists(Guid id, bool imagesOnly) => _media(id, imagesOnly);

    public bool EntryExists(Guid id, Guid typeId) => _entry(id, typeId);
}

/// <summary>An entry's values after validation, with every problem found.</summary>
public sealed record EntryValidation(JObject Data, IReadOnlyList<FieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>One indexed value of an entry (see <c>ContentEntryValue</c>).</summary>
public sealed record IndexValue(string Field, string? Text, double? Number, DateTime? Date);

/// <summary>
/// Entry values against their content type's fields (.docs/features/structured-content.md#validation): normalizes
/// untrusted input (unknown keys dropped, empty values removed, types converted from form strings), reports every
/// problem with its path, and derives what is stored next to the JSON (title, search text, index values).
/// Saving a draft checks formats and limits; publishing also requires required fields, minimum lengths and row
/// counts.
/// </summary>
public static partial class EntryData
{
    public const int DefaultTextMax = 500;
    public const int DefaultRichTextMax = 100_000;
    public const int MaxTitle = 300;
    public const int MaxSearchText = 1000;
    public const int MaxIndexText = 200;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotSlug();

    /// <summary>Checks and normalizes an entry's values.</summary>
    public static EntryValidation Validate(IReadOnlyList<FieldDefinition> fields, JToken? input, bool publish, EntryReferences references)
    {
        var errors = new List<FieldError>();
        var data = NormalizeObject(fields, input as JObject ?? new JObject(), "", errors, publish, references);
        return new EntryValidation(data, errors);
    }

    /// <summary>
    /// A rich text value as blocks, whatever form it is in: stored blocks, the editor's JSON as posted, or plain text
    /// (blank lines separate paragraphs).
    /// </summary>
    public static IReadOnlyList<Block> RichBlocks(JToken? value)
    {
        if (value is JValue { Type: JTokenType.String } s)
        {
            var raw = ((string)s!).Trim();
            try
            {
                value = raw.StartsWith('[') ? JToken.Parse(raw) : Paragraphs(raw);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                value = Paragraphs(raw);
            }
        }

        return RichText.NormalizeBlocks(value, 2000);
    }

    /// <summary>The values a new entry starts with: every field's default.</summary>
    public static JObject Defaults(IReadOnlyList<FieldDefinition> fields)
    {
        var o = new JObject();
        foreach (var f in fields)
        {
            if (f.Default is not null)
            {
                o[f.Key] = f.Default.DeepClone();
            }
            else if (f.Type == FieldTypes.Boolean)
            {
                o[f.Key] = false;
            }
        }

        return o;
    }

    private static JObject NormalizeObject(IReadOnlyList<FieldDefinition> fields, JObject input, string prefix, List<FieldError> errors, bool publish, EntryReferences references)
    {
        var output = new JObject();
        foreach (var f in fields)
        {
            var path = prefix + f.Key;
            var raw = input[f.Key];
            // An empty slug is made from the text field it is generated from (as the form does while typing).
            if (f.Type == FieldTypes.Slug && f.SlugFrom is not null && Strings(raw).All(s => s.Trim().Length == 0)
                && input[f.SlugFrom] is JValue { Type: JTokenType.String } source)
            {
                raw = new JValue((string)source!);
            }

            var value = NormalizeValue(f, raw, path, errors, publish, references);
            if (value is not null)
            {
                output[f.Key] = value;
            }
            else if (publish && f.Required)
            {
                errors.Add(new FieldError(path, $"{f.Label} is required."));
            }
        }

        return output;
    }

    /// <summary>One value cleaned for its field, or null when it is empty or invalid (an error is added for invalid values).</summary>
    public static JToken? NormalizeValue(FieldDefinition f, JToken? value, string path, List<FieldError> errors, bool publish, EntryReferences references)
    {
        if (value is null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
        {
            return f.Type == FieldTypes.Boolean ? new JValue(false) : null;
        }

        void Error(string message) => errors.Add(new FieldError(path, $"{f.Label}: {message}"));

        switch (f.Type)
        {
            case FieldTypes.Text:
            case FieldTypes.Textarea:
            case FieldTypes.Email:
            case FieldTypes.Url:
            case FieldTypes.Slug:
                {
                    if (!TryText(value, out var text))
                    {
                        Error("must be text.");
                        return null;
                    }

                    text = f.Type == FieldTypes.Textarea ? text.Replace("\r\n", "\n").Trim() : OneLine(text);
                    if (f.Type == FieldTypes.Slug)
                    {
                        text = Slugify(text);
                    }

                    if (text.Length == 0)
                    {
                        return null;
                    }

                    var max = f.MaxLength ?? (f.Type == FieldTypes.Textarea ? ContentSchema.MaxText : f.Type == FieldTypes.Url ? 2000 : DefaultTextMax);
                    if (text.Length > max)
                    {
                        Error($"at most {max} characters.");
                        return null;
                    }

                    if (publish && f.MinLength is { } min && text.Length < min)
                    {
                        Error($"at least {min} characters.");
                    }

                    if (f.Type == FieldTypes.Email && !EmailPattern().IsMatch(text))
                    {
                        Error("not a valid email address.");
                        return null;
                    }

                    if (f.Type == FieldTypes.Url)
                    {
                        var url = Values.LinkUrl(text);
                        if (string.IsNullOrEmpty(url))
                        {
                            Error("use https://…, a site path like /about, mailto: or tel:.");
                            return null;
                        }

                        text = url;
                    }

                    if (f.Pattern is not null && !Matches(f.Pattern, text))
                    {
                        Error("does not have the expected format.");
                        return null;
                    }

                    return new JValue(text);
                }

            case FieldTypes.RichText:
                {
                    var blocks = value;
                    if (value.Type == JTokenType.String)
                    {
                        // A form posts the editor's JSON; the API may send plain text (blank lines separate paragraphs).
                        var raw = ((string)value!).Trim();
                        try
                        {
                            blocks = raw.StartsWith('[') ? JToken.Parse(raw) : Paragraphs(raw);
                        }
                        catch (Newtonsoft.Json.JsonException)
                        {
                            blocks = Paragraphs(raw);
                        }
                    }

                    var normalized = RichText.NormalizeBlocks(blocks, 2000);
                    var length = RichText.TextLength(normalized);
                    if (length == 0)
                    {
                        return null;
                    }

                    var max = f.MaxLength ?? DefaultRichTextMax;
                    if (length > max)
                    {
                        Error($"at most {max} characters.");
                        return null;
                    }

                    if (publish && f.MinLength is { } min && length < min)
                    {
                        Error($"at least {min} characters.");
                    }

                    return RichText.ToJson(normalized);
                }

            case FieldTypes.Number:
                {
                    if (!TryNumber(value, out var n))
                    {
                        if (value.Type == JTokenType.String && ((string)value!).Trim().Length == 0)
                        {
                            return null;
                        }

                        Error("must be a number.");
                        return null;
                    }

                    return CheckNumber(f, n, Error);
                }

            case FieldTypes.Boolean:
                return new JValue(value.Type switch
                {
                    JTokenType.Boolean => (bool)value,
                    JTokenType.String => ((string)value!).Trim().ToLowerInvariant() is "true" or "on" or "1" or "yes",
                    JTokenType.Integer => (long)value != 0,
                    _ => false,
                });

            case FieldTypes.Date:
                {
                    if (!TryText(value, out var text) || (text = text.Trim()).Length == 0)
                    {
                        if (value.Type != JTokenType.String)
                        {
                            Error("must be a date.");
                        }

                        return null;
                    }

                    if (!TryDate(text, out var date))
                    {
                        Error("must be a date (YYYY-MM-DD).");
                        return null;
                    }

                    return new JValue(FormatDate(date, f.WithTime));
                }

            case FieldTypes.Select:
                {
                    var values = Strings(value).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                    var known = values.Where(v => f.Choices.Any(c => c.Value == v)).ToList();
                    if (known.Count < values.Count)
                    {
                        Error("choose one of the listed options.");
                    }

                    return MultiOrSingle(f, known.Select(v => (JToken)new JValue(v)).ToList(), publish, Error);
                }

            case FieldTypes.Image:
            case FieldTypes.File:
            case FieldTypes.Relationship:
                {
                    var ids = new List<JToken>();
                    foreach (var s in Strings(value))
                    {
                        if (s.Trim().Length == 0)
                        {
                            continue;
                        }

                        if (!Guid.TryParse(s, out var id))
                        {
                            Error("not a valid reference.");
                            continue;
                        }

                        var exists = f.Type == FieldTypes.Relationship
                            ? f.RelationTo is { } typeId && references.EntryExists(id, typeId)
                            : references.MediaExists(id, f.Type == FieldTypes.Image);
                        if (!exists)
                        {
                            Error(f.Type == FieldTypes.Relationship ? "the linked entry does not exist." : f.Type == FieldTypes.Image ? "the image does not exist (or is not an image)." : "the file does not exist.");
                            continue;
                        }

                        var text = id.ToString();
                        if (!ids.Any(i => (string?)i == text))
                        {
                            ids.Add(new JValue(text));
                        }
                    }

                    return MultiOrSingle(f, ids, publish, Error);
                }

            case FieldTypes.Array:
                {
                    // A form posts the list as lines of a textarea.
                    var items = value.Type == JTokenType.String
                        ? ((string)value!).Split('\n').Select(l => (JToken)new JValue(l.Trim()))
                        : value as JArray ?? new JArray(value);
                    var output = new JArray();
                    foreach (var item in items)
                    {
                        if (f.ItemType == FieldTypes.Number)
                        {
                            if (item.Type == JTokenType.String && ((string)item!).Length == 0)
                            {
                                continue;
                            }

                            if (!TryNumber(item, out var n))
                            {
                                Error("every item must be a number.");
                                continue;
                            }

                            if (CheckNumber(f, n, Error) is { } checkedNumber)
                            {
                                output.Add(checkedNumber);
                            }
                        }
                        else
                        {
                            if (!TryText(item, out var text) || (text = OneLine(text)).Length == 0)
                            {
                                continue;
                            }

                            var max = f.MaxLength ?? DefaultTextMax;
                            if (text.Length > max)
                            {
                                Error($"every item must be at most {max} characters.");
                                continue;
                            }

                            output.Add(text);
                        }
                    }

                    return Rows(f, output, publish, Error);
                }

            case FieldTypes.Group:
                {
                    if (value is not JObject o)
                    {
                        Error("must be an object.");
                        return null;
                    }

                    var group = NormalizeObject(f.Fields, o, path + ".", errors, publish, references);
                    return group.Properties().Any(p => p.Value.Type != JTokenType.Boolean || (bool)p.Value) ? group : null;
                }

            case FieldTypes.Repeater:
                {
                    if (value is not JArray list)
                    {
                        Error("must be a list of rows.");
                        return null;
                    }

                    var rows = new JArray();
                    var index = 0;
                    foreach (var row in list)
                    {
                        if (row is not JObject ro)
                        {
                            continue;
                        }

                        // Paths use the row's position after empty rows are dropped, as the form shows them.
                        var before = errors.Count;
                        var normalized = NormalizeObject(f.Fields, ro, $"{path}.{index}.", errors, publish, references);
                        if (!normalized.Properties().Any(p => p.Value.Type != JTokenType.Boolean || (bool)p.Value))
                        {
                            errors.RemoveRange(before, errors.Count - before);
                            continue;
                        }

                        rows.Add(normalized);
                        index++;
                    }

                    return Rows(f, rows, publish, Error);
                }

            default:
                return null;
        }
    }

    private static JToken? CheckNumber(FieldDefinition f, double n, Action<string> error)
    {
        if (f.Integer && n != Math.Floor(n))
        {
            error("must be a whole number.");
            return null;
        }

        if (f.Min is { } min && n < min)
        {
            error($"must be at least {FormatNumber(min, null)}.");
            return null;
        }

        if (f.Max is { } max && n > max)
        {
            error($"must be at most {FormatNumber(max, null)}.");
            return null;
        }

        return n == Math.Floor(n) && Math.Abs(n) < 1e15 ? new JValue((long)n) : new JValue(n);
    }

    private static JToken? MultiOrSingle(FieldDefinition f, List<JToken> values, bool publish, Action<string> error)
    {
        if (!f.Multiple)
        {
            if (values.Count > 1)
            {
                error("only one value is allowed.");
            }

            return values.Count > 0 ? values[0] : null;
        }

        return Rows(f, new JArray(values), publish, error);
    }

    private static JArray? Rows(FieldDefinition f, JArray rows, bool publish, Action<string> error)
    {
        if (f.MaxRows is { } max && rows.Count > max)
        {
            error($"at most {max} item{(max == 1 ? "" : "s")}.");
            while (rows.Count > max)
            {
                rows.RemoveAt(rows.Count - 1);
            }
        }

        if (publish && f.MinRows is { } min && rows.Count > 0 && rows.Count < min)
        {
            error($"at least {min} item{(min == 1 ? "" : "s")}.");
        }

        return rows.Count > 0 ? rows : null;
    }

    /// <summary>
    /// Values stored under the old field definitions, carried over to new ones: a field keeps its value when its id is
    /// the same, also after its API name changed (renames move the value). Values of removed fields are dropped.
    /// </summary>
    public static JObject Migrate(IReadOnlyList<FieldDefinition> before, IReadOnlyList<FieldDefinition> after, JObject data)
    {
        var output = new JObject();
        foreach (var f in after)
        {
            var old = before.FirstOrDefault(b => b.Id == f.Id) ?? before.FirstOrDefault(b => b.Key == f.Key && b.Type == f.Type);
            if (old is null || data[old.Key] is not { } value)
            {
                continue;
            }

            output[f.Key] = (f.Type, value) switch
            {
                (FieldTypes.Group, JObject o) when old.Type == FieldTypes.Group => Migrate(old.Fields, f.Fields, o),
                (FieldTypes.Repeater, JArray rows) when old.Type == FieldTypes.Repeater =>
                    new JArray(rows.OfType<JObject>().Select(r => Migrate(old.Fields, f.Fields, r))),
                _ => value.DeepClone(),
            };
        }

        return output;
    }

    /// <summary>Every media id and every (entry id, content type id) the input refers to, for one lookup before validation.</summary>
    public static (HashSet<Guid> Media, HashSet<(Guid Entry, Guid Type)> Entries) References(IReadOnlyList<FieldDefinition> fields, JToken? input)
    {
        var media = new HashSet<Guid>();
        var entries = new HashSet<(Guid, Guid)>();
        void Walk(IReadOnlyList<FieldDefinition> level, JObject? o)
        {
            if (o is null)
            {
                return;
            }

            foreach (var f in level)
            {
                var v = o[f.Key];
                switch (f.Type)
                {
                    case FieldTypes.Image or FieldTypes.File:
                        foreach (var s in Strings(v))
                        {
                            if (Guid.TryParse(s, out var id))
                            {
                                media.Add(id);
                            }
                        }

                        break;
                    case FieldTypes.Relationship when f.RelationTo is { } typeId:
                        foreach (var s in Strings(v))
                        {
                            if (Guid.TryParse(s, out var id))
                            {
                                entries.Add((id, typeId));
                            }
                        }

                        break;
                    case FieldTypes.Group:
                        Walk(f.Fields, v as JObject);
                        break;
                    case FieldTypes.Repeater:
                        foreach (var row in (v as JArray ?? new JArray()).OfType<JObject>())
                        {
                            Walk(f.Fields, row);
                        }

                        break;
                }
            }
        }

        Walk(fields, input as JObject);
        return (media, entries);
    }

    /// <summary>The entry's title: the title field's text, cut to <see cref="MaxTitle"/>.</summary>
    public static string Title(IReadOnlyList<FieldDefinition> fields, string? titleKey, JObject data)
    {
        var key = ContentSchema.TitleKey(fields, titleKey);
        var text = key is null ? null : (string?)(data[key] as JValue);
        text = text?.Trim() ?? string.Empty;
        return text.Length > MaxTitle ? text[..MaxTitle] : text;
    }

    /// <summary>Lower-cased text the admin search looks in: the title, then short text and choice values.</summary>
    public static string SearchText(IReadOnlyList<FieldDefinition> fields, string title, JObject data)
    {
        var sb = new StringBuilder(title);
        foreach (var f in fields.Where(f => FieldTypes.IsShortText(f.Type) || f.Type is FieldTypes.Select or FieldTypes.Textarea))
        {
            foreach (var s in Strings(data[f.Key]))
            {
                if (sb.Length >= MaxSearchText)
                {
                    break;
                }

                sb.Append(' ').Append(s);
            }
        }

        var text = sb.ToString().ToLowerInvariant();
        return text.Length > MaxSearchText ? text[..MaxSearchText] : text;
    }

    /// <summary>The indexed values of an entry: one per top-level scalar value (a multi-value field gives one per value).</summary>
    public static IReadOnlyList<IndexValue> IndexValues(IReadOnlyList<FieldDefinition> fields, JObject data)
    {
        var output = new List<IndexValue>();
        foreach (var f in fields)
        {
            var v = data[f.Key];
            if (v is null)
            {
                continue;
            }

            switch (f.Type)
            {
                case FieldTypes.Number:
                    if (TryNumber(v, out var n))
                    {
                        output.Add(new IndexValue(f.Key, IndexText(FormatNumber(n, null)), n, null));
                    }

                    break;
                case FieldTypes.Boolean:
                    var b = v.Type == JTokenType.Boolean && (bool)v;
                    output.Add(new IndexValue(f.Key, b ? "true" : "false", b ? 1 : 0, null));
                    break;
                case FieldTypes.Date:
                    if (TryText(v, out var d) && TryDate(d, out var date))
                    {
                        output.Add(new IndexValue(f.Key, IndexText(d), null, date));
                    }

                    break;
                case FieldTypes.Array when f.ItemType == FieldTypes.Number:
                    foreach (var item in v as JArray ?? new JArray())
                    {
                        if (TryNumber(item, out var x))
                        {
                            output.Add(new IndexValue(f.Key, IndexText(FormatNumber(x, null)), x, null));
                        }
                    }

                    break;
                case FieldTypes.Text or FieldTypes.Textarea or FieldTypes.Email or FieldTypes.Url or FieldTypes.Slug
                    or FieldTypes.Select or FieldTypes.Relationship or FieldTypes.Image or FieldTypes.File or FieldTypes.Array:
                    foreach (var s in Strings(v))
                    {
                        output.Add(new IndexValue(f.Key, IndexText(s), null, null));
                    }

                    break;
            }
        }

        return output;
    }

    /// <summary>Text as stored in the index: lower-cased, cut to <see cref="MaxIndexText"/>.</summary>
    public static string IndexText(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text.Length > MaxIndexText ? text[..MaxIndexText] : text;
    }

    /// <summary>Lower-case letters and digits joined by single hyphens: "Hello, World!" → "hello-world".</summary>
    public static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                ascii.Append(c);
            }
        }

        return NotSlug().Replace(ascii.ToString().ToLowerInvariant(), "-").Trim('-');
    }

    /// <summary>A number for display: invariant culture, the given decimals or as few as needed.</summary>
    public static string FormatNumber(double n, int? decimals) =>
        decimals is { } d ? n.ToString("N" + d, CultureInfo.InvariantCulture) : n.ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>A stored date: <c>yyyy-MM-dd</c>, or <c>yyyy-MM-ddTHH:mm:ssZ</c> with a time.</summary>
    public static string FormatDate(DateTime utc, bool withTime) =>
        withTime ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Reads a date as UTC: <c>yyyy-MM-dd</c>, a <c>datetime-local</c> value (taken as UTC) or ISO 8601 with an offset.</summary>
    public static bool TryDate(string text, out DateTime utc)
    {
        text = text.Trim();
        if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }

        utc = default;
        return false;
    }

    internal static bool TryNumber(JToken? value, out double n)
    {
        switch (value?.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float:
                n = (double)value;
                return double.IsFinite(n);
            case JTokenType.String:
                return double.TryParse(((string)value!).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && double.IsFinite(n);
            default:
                n = 0;
                return false;
        }
    }

    private static bool TryText(JToken? value, out string text)
    {
        if (value is { Type: JTokenType.String })
        {
            text = (string)value!;
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>The strings in a value that is one string or a list of strings.</summary>
    internal static IEnumerable<string> Strings(JToken? value) => value switch
    {
        JArray a => a.Where(t => t.Type == JTokenType.String).Select(t => (string)t!),
        JValue { Type: JTokenType.String } s => new[] { (string)s! },
        _ => Enumerable.Empty<string>(),
    };

    private static string OneLine(string text) => Regex.Replace(text, @"\s*[\r\n]+\s*", " ").Trim();

    private static JArray Paragraphs(string text) => new(text.Replace("\r\n", "\n").Split("\n\n")
        .Select(p => p.Trim()).Where(p => p.Length > 0)
        .Select(p => new JObject { ["type"] = "p", ["runs"] = new JArray(new JObject { ["text"] = p }) }));

    private static bool Matches(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
