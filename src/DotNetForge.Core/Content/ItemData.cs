using System.Globalization;
using DotNetForge.PageBuilder;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Core.Content;

/// <summary>A media file as content blocks show it.</summary>
public sealed record MediaInfo(string Url, string Name, bool IsImage);

/// <summary>A published entry another entry links to (relationship fields).</summary>
public sealed record RelatedEntry(Guid Id, string Title, IReadOnlyList<FieldDefinition> Fields, JObject Data);

/// <summary>A value a layout element can be bound to: its path, a label for the editor and what kind of value it is.</summary>
public sealed record BindableField(string Path, string Label, string Kind);

/// <summary>
/// Turns an entry into the item data the page builder renders (.docs/features/structured-content.md#data-binding): a
/// flat map from field path to <c>{ text, url?, rich?, value? }</c>. Both renderers (C# and the editor's JavaScript)
/// only look values up in this map, so they bind identically; every formatting decision (numbers, dates, choice
/// labels, media URLs, linked entries) is made here, once.
/// </summary>
/// <remarks>
/// Paths: top-level keys (<c>price</c>), group members (<c>specs.width</c>), fields of a linked entry
/// (<c>category.name</c>, one level deep) and the built-ins <c>$id</c>, <c>$title</c>, <c>$url</c>.
/// </remarks>
public static class ItemData
{
    public const string IdPath = "$id";
    public const string TitlePath = "$title";
    public const string UrlPath = "$url";

    /// <summary>The item of one entry.</summary>
    /// <param name="url">The entry's link (from the content block's item link pattern), or empty.</param>
    public static JObject Build(Guid id, string title, string url, IReadOnlyList<FieldDefinition> fields, JObject data,
        Func<Guid, MediaInfo?> media, Func<Guid, RelatedEntry?> related)
    {
        var map = new JObject
        {
            [IdPath] = new JObject { ["text"] = id.ToString() },
            [TitlePath] = new JObject { ["text"] = title },
            [UrlPath] = new JObject { ["text"] = url, ["url"] = url },
        };
        AddFields(map, "", fields, data, media, related, depth: 0);
        return new JObject { ["id"] = id.ToString(), ["fields"] = map };
    }

    private static void AddFields(JObject map, string prefix, IReadOnlyList<FieldDefinition> fields, JObject data,
        Func<Guid, MediaInfo?> media, Func<Guid, RelatedEntry?> related, int depth)
    {
        foreach (var f in fields)
        {
            var path = prefix + f.Key;
            var value = data[f.Key];
            if (f.Type == FieldTypes.Group)
            {
                AddFields(map, path + ".", f.Fields, value as JObject ?? new JObject(), media, related, depth);
                continue;
            }

            var cell = Cell(f, value, media, related);
            if (cell is not null)
            {
                map[path] = cell;
            }

            // The first linked entry's own values, one level deep (category.name, author.photo...).
            if (f.Type == FieldTypes.Relationship && depth == 0)
            {
                var first = EntryData.Strings(value).Select(s => Guid.TryParse(s, out var g) ? related(g) : null).FirstOrDefault(r => r is not null);
                if (first is not null)
                {
                    map[path + "." + TitlePath] = new JObject { ["text"] = first.Title };
                    AddFields(map, path + ".", first.Fields, first.Data, media, related, depth + 1);
                }
            }
        }
    }

    private static JObject? Cell(FieldDefinition f, JToken? value, Func<Guid, MediaInfo?> media, Func<Guid, RelatedEntry?> related)
    {
        if (value is null || value.Type == JTokenType.Null)
        {
            return null;
        }

        switch (f.Type)
        {
            case FieldTypes.RichText:
                {
                    var blocks = RichText.NormalizeBlocks(value);
                    var text = string.Join("\n\n", blocks.Select(b => b.IsList
                        ? string.Join("\n", b.Items.Select(RichText.RunsText))
                        : RichText.RunsText(b.Runs)));
                    return new JObject { ["text"] = text, ["rich"] = RichText.ToJson(blocks) };
                }

            case FieldTypes.Number:
                return EntryData.TryNumber(value, out var n)
                    ? new JObject { ["text"] = EntryData.FormatNumber(n, f.Decimals), ["value"] = value.DeepClone() }
                    : null;

            case FieldTypes.Boolean:
                {
                    var on = value.Type == JTokenType.Boolean && (bool)value;
                    return new JObject { ["text"] = on ? "Yes" : "No", ["value"] = on };
                }

            case FieldTypes.Date:
                {
                    var raw = (string?)value ?? string.Empty;
                    if (!EntryData.TryDate(raw, out var date))
                    {
                        return null;
                    }

                    var text = date.ToString(f.WithTime ? "MMM d, yyyy HH:mm" : "MMM d, yyyy", CultureInfo.InvariantCulture);
                    return new JObject { ["text"] = text, ["value"] = raw };
                }

            case FieldTypes.Select:
                {
                    var values = EntryData.Strings(value).ToList();
                    var labels = values.Select(v => f.Choices.FirstOrDefault(c => c.Value == v)?.Label ?? v);
                    return new JObject { ["text"] = string.Join(", ", labels), ["value"] = string.Join(", ", values) };
                }

            case FieldTypes.Image:
            case FieldTypes.File:
                {
                    var files = EntryData.Strings(value).Select(s => Guid.TryParse(s, out var g) ? media(g) : null).Where(m => m is not null).ToList();
                    if (files.Count == 0)
                    {
                        return null;
                    }

                    return new JObject { ["text"] = string.Join(", ", files.Select(m => m!.Name)), ["url"] = files[0]!.Url };
                }

            case FieldTypes.Relationship:
                {
                    var titles = EntryData.Strings(value).Select(s => Guid.TryParse(s, out var g) ? related(g)?.Title : null).Where(t => t is not null).ToList();
                    return titles.Count == 0 ? null : new JObject { ["text"] = string.Join(", ", titles) };
                }

            case FieldTypes.Array:
                return new JObject { ["text"] = string.Join(", ", (value as JArray ?? new JArray()).Select(v => v.Type == JTokenType.String ? (string)v! : v.ToString())) };

            case FieldTypes.Repeater:
                {
                    var count = (value as JArray)?.Count ?? 0;
                    return new JObject { ["text"] = count == 1 ? "1 item" : count + " items", ["value"] = count };
                }

            case FieldTypes.Url:
                return new JObject { ["text"] = (string?)value ?? string.Empty, ["url"] = (string?)value ?? string.Empty };

            default:
                return value.Type == JTokenType.String ? new JObject { ["text"] = (string)value! } : null;
        }
    }

    /// <summary>
    /// Every path a layout can bind to for a content type, with labels for the editor. <paramref name="relatedFields"/>
    /// returns a linked type's name and fields (or null when it is gone).
    /// </summary>
    public static IReadOnlyList<BindableField> Paths(IReadOnlyList<FieldDefinition> fields,
        Func<Guid, (string Name, IReadOnlyList<FieldDefinition> Fields)?> relatedFields)
    {
        var output = new List<BindableField>
        {
            new(TitlePath, "Title", "text"),
            new(UrlPath, "Item link", "url"),
        };
        AddPaths(output, "", "", fields, relatedFields, depth: 0);
        return output;
    }

    private static void AddPaths(List<BindableField> output, string prefix, string labelPrefix, IReadOnlyList<FieldDefinition> fields,
        Func<Guid, (string Name, IReadOnlyList<FieldDefinition> Fields)?> relatedFields, int depth)
    {
        foreach (var f in fields)
        {
            var path = prefix + f.Key;
            var label = labelPrefix + f.Label;
            if (f.Type == FieldTypes.Group)
            {
                AddPaths(output, path + ".", label + " › ", f.Fields, relatedFields, depth);
                continue;
            }

            output.Add(new BindableField(path, label, Kind(f.Type)));
            if (f.Type == FieldTypes.Relationship && depth == 0 && f.RelationTo is { } target && relatedFields(target) is { } rel)
            {
                output.Add(new BindableField(path + "." + TitlePath, label + " › Title", "text"));
                AddPaths(output, path + ".", label + " › ", rel.Fields, relatedFields, depth + 1);
            }
        }
    }

    /// <summary>What a field's item value offers: <c>image</c> (url of a picture), <c>file</c>, <c>url</c>, <c>rich</c> or <c>text</c>.</summary>
    public static string Kind(string type) => type switch
    {
        FieldTypes.Image => "image",
        FieldTypes.File => "file",
        FieldTypes.Url => "url",
        FieldTypes.RichText => "rich",
        FieldTypes.Number => "number",
        FieldTypes.Boolean => "boolean",
        FieldTypes.Date => "date",
        _ => "text",
    };
}
