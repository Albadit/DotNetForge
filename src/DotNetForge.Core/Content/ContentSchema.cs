using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Core.Content;

/// <summary>The field types a content type can use (.docs/features/structured-content.md#field-types).</summary>
public static class FieldTypes
{
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string RichText = "richText";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Select = "select";
    public const string Email = "email";
    public const string Url = "url";
    public const string Slug = "slug";
    public const string Image = "image";
    public const string File = "file";
    public const string Relationship = "relationship";
    public const string Array = "array";
    public const string Group = "group";
    public const string Repeater = "repeater";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Text, Textarea, RichText, Number, Boolean, Date, Select, Email, Url, Slug,
        Image, File, Relationship, Array, Group, Repeater,
    };

    /// <summary>Types that hold other fields.</summary>
    public static bool HasFields(string type) => type is Group or Repeater;

    /// <summary>Types whose value is one line of text.</summary>
    public static bool IsShortText(string type) => type is Text or Email or Url or Slug;

    /// <summary>Types that may be marked unique (single top-level values compared as text or number).</summary>
    public static bool CanBeUnique(string type) => type is Text or Email or Url or Slug or Number;

    /// <summary>Types that can hold several values when <see cref="FieldDefinition.Multiple"/> is set.</summary>
    public static bool CanBeMultiple(string type) => type is Select or Image or File or Relationship;
}

/// <summary>One option of a select field.</summary>
public sealed record FieldChoice(string Value, string Label);

/// <summary>
/// One field of a content type. Created only by <see cref="ContentSchema"/>, so every instance has been checked.
/// <see cref="Id"/> never changes once the field exists; <see cref="Key"/> (the API name) may be renamed, and stored
/// values follow the rename (<see cref="EntryData.Migrate"/>).
/// </summary>
public sealed class FieldDefinition
{
    public string Id { get; init; } = string.Empty;

    /// <summary>The API name: a letter, then letters, digits or underscores.</summary>
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Type { get; init; } = FieldTypes.Text;

    public bool Required { get; init; }

    /// <summary>No two entries of the type may have the same value (top-level text, email, URL, slug and number).</summary>
    public bool Unique { get; init; }

    public string? Help { get; init; }

    public string? Placeholder { get; init; }

    /// <summary>The value a new entry starts with (already normalized for the type), or null.</summary>
    public JToken? Default { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>Numbers: whole numbers only.</summary>
    public bool Integer { get; init; }

    /// <summary>Numbers: digits shown after the decimal point when the value is displayed (null = as entered).</summary>
    public int? Decimals { get; init; }

    /// <summary>Text: a regular expression the value must match.</summary>
    public string? Pattern { get; init; }

    public IReadOnlyList<FieldChoice> Choices { get; init; } = System.Array.Empty<FieldChoice>();

    /// <summary>Select, image, file, relationship: several values.</summary>
    public bool Multiple { get; init; }

    /// <summary>Relationship: the related content type.</summary>
    public Guid? RelationTo { get; init; }

    /// <summary>Array and repeater: row limits.</summary>
    public int? MinRows { get; init; }

    public int? MaxRows { get; init; }

    /// <summary>Array: the item type, <c>text</c> or <c>number</c>.</summary>
    public string ItemType { get; init; } = FieldTypes.Text;

    /// <summary>Date: with a time of day (UTC).</summary>
    public bool WithTime { get; init; }

    /// <summary>Slug: the text field it is generated from while empty.</summary>
    public string? SlugFrom { get; init; }

    /// <summary>Form layout: <c>full</c> or <c>half</c> width.</summary>
    public string Width { get; init; } = "full";

    /// <summary>Shown as a column in the entry list.</summary>
    public bool ShowInList { get; init; }

    /// <summary>Group and repeater: the nested fields.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; init; } = System.Array.Empty<FieldDefinition>();

    public JObject ToJson()
    {
        var o = new JObject { ["id"] = Id, ["key"] = Key, ["label"] = Label, ["type"] = Type };
        if (Required) o["required"] = true;
        if (Unique) o["unique"] = true;
        if (Help is not null) o["help"] = Help;
        if (Placeholder is not null) o["placeholder"] = Placeholder;
        if (Default is not null) o["default"] = Default.DeepClone();
        if (MinLength is not null) o["minLength"] = MinLength;
        if (MaxLength is not null) o["maxLength"] = MaxLength;
        if (Min is not null) o["min"] = Min;
        if (Max is not null) o["max"] = Max;
        if (Integer) o["integer"] = true;
        if (Decimals is not null) o["decimals"] = Decimals;
        if (Pattern is not null) o["pattern"] = Pattern;
        if (Choices.Count > 0) o["choices"] = new JArray(Choices.Select(c => new JObject { ["value"] = c.Value, ["label"] = c.Label }));
        if (Multiple) o["multiple"] = true;
        if (RelationTo is not null) o["relationTo"] = RelationTo.Value.ToString();
        if (MinRows is not null) o["minRows"] = MinRows;
        if (MaxRows is not null) o["maxRows"] = MaxRows;
        if (Type == FieldTypes.Array) o["itemType"] = ItemType;
        if (WithTime) o["withTime"] = true;
        if (SlugFrom is not null) o["slugFrom"] = SlugFrom;
        if (Width != "full") o["width"] = Width;
        if (ShowInList) o["showInList"] = true;
        if (Fields.Count > 0) o["fields"] = new JArray(Fields.Select(f => f.ToJson()));
        return o;
    }
}

/// <summary>A checked field list, or the problems that kept it from being one.</summary>
public sealed record SchemaResult(IReadOnlyList<FieldDefinition> Fields, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Reads and checks the field definitions of a content type (.docs/features/structured-content.md#field-types). The
/// builder's JSON is untrusted: <see cref="Validate"/> refuses what is wrong with a message per problem and normalizes
/// the rest; <see cref="Parse"/> reads stored definitions (already validated) without failing.
/// </summary>
public static partial class ContentSchema
{
    public const int MaxFields = 150;
    public const int MaxDepth = 3;
    public const int MaxChoices = 200;
    public const int MaxLabel = 100;
    public const int MaxHelp = 500;
    public const int MaxText = 10_000;
    public const int MaxPattern = 200;
    public const int MaxRowsLimit = 500;

    [GeneratedRegex("^[a-z][A-Za-z0-9_]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _.-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChoicePattern();

    [GeneratedRegex("^[a-z0-9]{4,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>Stored definitions to fields. Never throws: unreadable input is an empty list.</summary>
    public static IReadOnlyList<FieldDefinition> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return System.Array.Empty<FieldDefinition>();
        }

        try
        {
            return Validate(JToken.Parse(json), _ => true).Fields;
        }
        catch (JsonException)
        {
            return System.Array.Empty<FieldDefinition>();
        }
    }

    /// <summary>The fields as the JSON stored in <c>ContentType.FieldsJson</c>.</summary>
    public static string Serialize(IEnumerable<FieldDefinition> fields) =>
        new JArray(fields.Select(f => f.ToJson())).ToString(Formatting.None);

    /// <summary>
    /// Checks and normalizes untrusted field definitions. <paramref name="typeExists"/> answers whether a relationship
    /// target is an existing content type.
    /// </summary>
    public static SchemaResult Validate(JToken? input, Func<Guid, bool> typeExists)
    {
        var errors = new List<string>();
        if (input is not JArray list)
        {
            return new SchemaResult(System.Array.Empty<FieldDefinition>(), new[] { "The field list is missing." });
        }

        var count = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var fields = ReadFields(list, 1, "", errors, typeExists, ref count, ids, topLevel: true);
        if (count > MaxFields)
        {
            errors.Add($"A content type can have at most {MaxFields} fields.");
        }

        // A slug generated from another field needs that field to be a text field on the same level.
        foreach (var f in fields.Where(f => f.Type == FieldTypes.Slug && f.SlugFrom is not null))
        {
            if (!fields.Any(o => o.Key == f.SlugFrom && o.Type == FieldTypes.Text))
            {
                errors.Add($"{f.Label}: the slug is generated from \"{f.SlugFrom}\", which is not a text field of this type.");
            }
        }

        return new SchemaResult(fields, errors);
    }

    private static List<FieldDefinition> ReadFields(JArray list, int depth, string prefix, List<string> errors,
        Func<Guid, bool> typeExists, ref int count, HashSet<string> ids, bool topLevel)
    {
        var output = new List<FieldDefinition>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in list)
        {
            count++;
            if (raw is not JObject o)
            {
                errors.Add("A field definition is not an object.");
                continue;
            }

            var field = ReadField(o, depth, prefix, errors, typeExists, ref count, ids, topLevel);
            if (field is null)
            {
                continue;
            }

            if (!keys.Add(field.Key))
            {
                errors.Add($"Two fields are named \"{prefix}{field.Key}\"; API names must be unique.");
                continue;
            }

            output.Add(field);
        }

        return output;
    }

    private static FieldDefinition? ReadField(JObject o, int depth, string prefix, List<string> errors,
        Func<Guid, bool> typeExists, ref int count, HashSet<string> ids, bool topLevel)
    {
        var key = Str(o["key"]) ?? string.Empty;
        var label = (Str(o["label"]) ?? string.Empty).Trim();
        var type = Str(o["type"]) ?? string.Empty;
        var name = label.Length > 0 ? label : key.Length > 0 ? key : "A field";

        if (!KeyPattern().IsMatch(key))
        {
            errors.Add($"{name}: the API name must start with a lower-case letter and use only letters, digits and _ (at most 50).");
            return null;
        }

        if (!FieldTypes.All.Contains(type))
        {
            errors.Add($"{name}: unknown field type \"{type}\".");
            return null;
        }

        if (label.Length == 0)
        {
            label = Humanize(key);
        }

        if (label.Length > MaxLabel)
        {
            errors.Add($"{name}: the label must be at most {MaxLabel} characters.");
            return null;
        }

        // Ids are kept so values follow a renamed key; new or duplicated ones get a fresh id.
        var id = Str(o["id"]);
        if (id is null || !IdPattern().IsMatch(id) || !ids.Add(id))
        {
            id = NewId();
            ids.Add(id);
        }

        var path = prefix + key;
        var help = Trimmed(o["help"], MaxHelp, name, "help text", errors);
        var placeholder = Trimmed(o["placeholder"], 200, name, "placeholder", errors);
        var minLength = Int(o["minLength"], 0, MaxText);
        var maxLength = Int(o["maxLength"], 1, MaxText);
        if (minLength is not null && maxLength is not null && minLength > maxLength)
        {
            errors.Add($"{name}: the minimum length is larger than the maximum length.");
        }

        var min = Num(o["min"]);
        var max = Num(o["max"]);
        if (min is not null && max is not null && min > max)
        {
            errors.Add($"{name}: the minimum is larger than the maximum.");
        }

        var minRows = Int(o["minRows"], 0, MaxRowsLimit);
        var maxRows = Int(o["maxRows"], 1, MaxRowsLimit);
        if (minRows is not null && maxRows is not null && minRows > maxRows)
        {
            errors.Add($"{name}: the minimum number of rows is larger than the maximum.");
        }

        string? pattern = null;
        if (type is FieldTypes.Text or FieldTypes.Textarea && Str(o["pattern"]) is { Length: > 0 } p)
        {
            if (p.Length > MaxPattern || !IsValidRegex(p))
            {
                errors.Add($"{name}: the pattern is not a valid regular expression (at most {MaxPattern} characters).");
            }
            else
            {
                pattern = p;
            }
        }

        var choices = new List<FieldChoice>();
        if (type == FieldTypes.Select)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in (o["choices"] as JArray ?? new JArray()).Take(MaxChoices + 1))
            {
                var value = (c is JObject co ? Str(co["value"]) : Str(c))?.Trim() ?? string.Empty;
                var text = (c is JObject cl ? Str(cl["label"]) : null)?.Trim();
                if (value.Length == 0 && string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (value.Length == 0)
                {
                    value = text!;
                }

                if (!ChoicePattern().IsMatch(value))
                {
                    errors.Add($"{name}: the option value \"{value}\" may use letters, digits, spaces, '.', '-' and '_' (at most 100).");
                    continue;
                }

                if (seen.Add(value))
                {
                    choices.Add(new FieldChoice(value, string.IsNullOrEmpty(text) ? value : text.Length > MaxLabel ? text[..MaxLabel] : text));
                }
            }

            if (choices.Count == 0)
            {
                errors.Add($"{name}: a select field needs at least one option.");
            }
            else if (choices.Count > MaxChoices)
            {
                errors.Add($"{name}: a select field can have at most {MaxChoices} options.");
            }
        }

        Guid? relationTo = null;
        if (type == FieldTypes.Relationship)
        {
            if (Guid.TryParse(Str(o["relationTo"]), out var target) && typeExists(target))
            {
                relationTo = target;
            }
            else
            {
                errors.Add($"{name}: choose the content type this field links to.");
            }
        }

        var nested = System.Array.Empty<FieldDefinition>() as IReadOnlyList<FieldDefinition>;
        if (FieldTypes.HasFields(type))
        {
            if (depth >= MaxDepth)
            {
                errors.Add($"{name}: groups and repeaters can be nested at most {MaxDepth - 1} levels deep.");
            }
            else if (o["fields"] is not JArray children || children.Count == 0)
            {
                errors.Add($"{name}: add at least one field inside the {(type == FieldTypes.Group ? "group" : "repeater")}.");
            }
            else
            {
                nested = ReadFields(children, depth + 1, path + ".", errors, typeExists, ref count, ids, topLevel: false);
            }
        }

        var unique = Bool(o["unique"]);
        if (unique && (!FieldTypes.CanBeUnique(type) || !topLevel))
        {
            errors.Add($"{name}: only top-level text, email, URL, slug and number fields can be unique.");
            unique = false;
        }

        var multiple = Bool(o["multiple"]) && FieldTypes.CanBeMultiple(type);
        var itemType = Str(o["itemType"]) == FieldTypes.Number ? FieldTypes.Number : FieldTypes.Text;
        var slugFrom = type == FieldTypes.Slug && Str(o["slugFrom"]) is { Length: > 0 } from && KeyPattern().IsMatch(from) ? from : null;
        var width = Str(o["width"]) == "half" ? "half" : "full";

        var field = new FieldDefinition
        {
            Id = id,
            Key = key,
            Label = label,
            Type = type,
            Required = Bool(o["required"]) && type != FieldTypes.Boolean,
            Unique = unique,
            Help = help,
            Placeholder = placeholder,
            MinLength = HasLength(type) ? minLength : null,
            MaxLength = HasLength(type) ? maxLength : null,
            Min = type == FieldTypes.Number || (type == FieldTypes.Array && itemType == FieldTypes.Number) ? min : null,
            Max = type == FieldTypes.Number || (type == FieldTypes.Array && itemType == FieldTypes.Number) ? max : null,
            Integer = type == FieldTypes.Number && Bool(o["integer"]),
            Decimals = type == FieldTypes.Number ? Int(o["decimals"], 0, 6) : null,
            Pattern = pattern,
            Choices = choices,
            Multiple = multiple,
            RelationTo = relationTo,
            MinRows = type is FieldTypes.Array or FieldTypes.Repeater || multiple ? minRows : null,
            MaxRows = type is FieldTypes.Array or FieldTypes.Repeater || multiple ? maxRows : null,
            ItemType = itemType,
            WithTime = type == FieldTypes.Date && Bool(o["withTime"]),
            SlugFrom = slugFrom,
            Width = width,
            ShowInList = topLevel && Bool(o["showInList"]) && !FieldTypes.HasFields(type) && type != FieldTypes.RichText,
            Fields = nested,
        };

        // The default is checked like any value, without reference lookups (they are not known yet).
        if (o["default"] is { Type: not JTokenType.Null } def && type is not (FieldTypes.Image or FieldTypes.File or FieldTypes.Relationship or FieldTypes.Group or FieldTypes.Repeater))
        {
            var problems = new List<FieldError>();
            var value = EntryData.NormalizeValue(field, def, path, problems, publish: false, EntryReferences.None);
            if (problems.Count > 0)
            {
                errors.Add($"{name}: the default value is not valid ({problems[0].Message}).");
            }
            else if (value is not null)
            {
                field = Copy(field, value);
            }
        }

        return field;
    }

    private static FieldDefinition Copy(FieldDefinition f, JToken def) => new()
    {
        Id = f.Id,
        Key = f.Key,
        Label = f.Label,
        Type = f.Type,
        Required = f.Required,
        Unique = f.Unique,
        Help = f.Help,
        Placeholder = f.Placeholder,
        Default = def,
        MinLength = f.MinLength,
        MaxLength = f.MaxLength,
        Min = f.Min,
        Max = f.Max,
        Integer = f.Integer,
        Decimals = f.Decimals,
        Pattern = f.Pattern,
        Choices = f.Choices,
        Multiple = f.Multiple,
        RelationTo = f.RelationTo,
        MinRows = f.MinRows,
        MaxRows = f.MaxRows,
        ItemType = f.ItemType,
        WithTime = f.WithTime,
        SlugFrom = f.SlugFrom,
        Width = f.Width,
        ShowInList = f.ShowInList,
        Fields = f.Fields,
    };

    private static bool HasLength(string type) =>
        type is FieldTypes.Text or FieldTypes.Textarea or FieldTypes.Email or FieldTypes.Url or FieldTypes.Slug or FieldTypes.RichText;

    /// <summary>The key of the field that titles an entry: the configured one when it is a top-level text field, else the first such field.</summary>
    public static string? TitleKey(IReadOnlyList<FieldDefinition> fields, string? configured)
    {
        var candidates = fields.Where(f => f.Type is FieldTypes.Text or FieldTypes.Slug or FieldTypes.Email).ToList();
        return candidates.FirstOrDefault(f => f.Key == configured)?.Key ?? candidates.FirstOrDefault()?.Key;
    }

    /// <summary>"featuredImage" → "Featured image".</summary>
    public static string Humanize(string key)
    {
        var words = Regex.Replace(key.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ").Trim().ToLowerInvariant();
        return words.Length == 0 ? key : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>A new stable field id (12 lower-case hex characters).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    internal static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? Str(JToken? t) => t is { Type: JTokenType.String } ? (string?)t : null;

    private static bool Bool(JToken? t) => t is { Type: JTokenType.Boolean } && (bool)t;

    private static string? Trimmed(JToken? t, int max, string name, string what, List<string> errors)
    {
        var s = Str(t)?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (s.Length > max)
        {
            errors.Add($"{name}: the {what} must be at most {max} characters.");
            return null;
        }

        return s;
    }

    private static int? Int(JToken? t, int min, int max)
    {
        var d = Num(t);
        return d is not null && d == Math.Floor(d.Value) && d >= min && d <= max ? (int)d.Value : null;
    }

    private static double? Num(JToken? t) => t?.Type switch
    {
        JTokenType.Integer or JTokenType.Float => (double)t,
        JTokenType.String when double.TryParse((string?)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) => d,
        _ => null,
    };
}
