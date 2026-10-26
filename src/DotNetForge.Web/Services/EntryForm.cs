using DotNetForge.Core.Content;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Web.Services;

/// <summary>
/// The Content Manager's entry form back to entry values (.docs/pages/content-manager.md#entry-form). Every input is
/// named <c>f.</c> + its field path (<c>f.title</c>, <c>f.specs.width</c>, <c>f.variants.3.name</c>); the type's
/// fields say how to read each one. The result is untrusted input for <see cref="EntryData.Validate"/>.
/// </summary>
public static class EntryForm
{
    public const string Prefix = "f.";

    /// <summary>A hidden input every repeater writes, so a repeater whose rows were all removed reads as empty.</summary>
    public const string RowsMarker = "__rows";

    public static JObject Read(IReadOnlyList<FieldDefinition> fields, IFormCollection form) => ReadObject(fields, form, Prefix);

    private static JObject ReadObject(IReadOnlyList<FieldDefinition> fields, IFormCollection form, string prefix)
    {
        var output = new JObject();
        foreach (var f in fields)
        {
            var name = prefix + f.Key;
            switch (f.Type)
            {
                case FieldTypes.Group:
                    output[f.Key] = ReadObject(f.Fields, form, name + ".");
                    break;
                case FieldTypes.Repeater:
                    {
                        if (!form.ContainsKey(name + "." + RowsMarker) && !form.Keys.Any(k => k.StartsWith(name + ".", StringComparison.Ordinal)))
                        {
                            break;
                        }

                        // Rows in the order the form posted them (the script reorders the DOM; indexes only keep rows apart).
                        var rows = new JArray();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var key in form.Keys)
                        {
                            if (!key.StartsWith(name + ".", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var rest = key[(name.Length + 1)..];
                            var dot = rest.IndexOf('.');
                            if (dot <= 0 || !int.TryParse(rest[..dot], out _) || !seen.Add(rest[..dot]))
                            {
                                continue;
                            }

                            rows.Add(ReadObject(f.Fields, form, name + "." + rest[..dot] + "."));
                        }

                        output[f.Key] = rows;
                        break;
                    }

                case FieldTypes.Boolean:
                    // A checkbox posts "true" next to its hidden "false".
                    if (form.TryGetValue(name, out var flags))
                    {
                        output[f.Key] = flags.Any(v => v == "true");
                    }

                    break;
                case FieldTypes.Select or FieldTypes.Image or FieldTypes.File or FieldTypes.Relationship when f.Multiple:
                    if (form.TryGetValue(name, out var many) || form.ContainsKey(name + "." + RowsMarker))
                    {
                        output[f.Key] = new JArray(many.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => (object)v!).ToArray());
                    }

                    break;
                default:
                    if (form.TryGetValue(name, out var value))
                    {
                        output[f.Key] = value.ToString();
                    }

                    break;
            }
        }

        return output;
    }
}
