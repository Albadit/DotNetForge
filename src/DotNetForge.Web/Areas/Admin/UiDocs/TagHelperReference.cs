using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DotNetForge.Web.Areas.Admin.UiDocs;

/// <summary>One attribute of a tag helper as the API table shows it.</summary>
public sealed record TagHelperAttributeDoc(string Name, string Type, string Default, string DescriptionHtml);

/// <summary>
/// The API table of a tag helper, read from the type itself so it cannot drift: every bound
/// property (its Razor attribute name, type, default value) and the description from the library's XML doc comments
/// (DotNetForge.UI.xml next to the assembly; without it the descriptions are empty).
/// </summary>
public static class TagHelperReference
{
    private static readonly Lazy<Dictionary<string, XElement>> Docs = new(LoadDocs);

    public static IReadOnlyList<TagHelperAttributeDoc> Attributes(Type tagHelper, IServiceProvider services)
    {
        object? instance = null;
        try
        {
            instance = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(services, tagHelper);
        }
        catch (InvalidOperationException)
        {
            // A tag helper the container cannot build: the table shows no defaults.
        }

        return tagHelper.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetCustomAttribute<HtmlAttributeNotBoundAttribute>() is null &&
                        p.GetCustomAttribute<ViewContextAttribute>() is null && p.PropertyType != typeof(ViewContext))
            .Select(p =>
            {
                var bound = p.GetCustomAttribute<HtmlAttributeNameAttribute>();
                var name = bound?.DictionaryAttributePrefix is { Length: > 0 } prefix ? prefix + "*" : bound?.Name ?? Kebab(p.Name);
                return new TagHelperAttributeDoc(name, TypeName(p.PropertyType), DefaultValue(p, instance), Describe($"P:{p.DeclaringType!.FullName}.{p.Name}"));
            })
            .ToList();
    }

    /// <summary>Razor's attribute name for a property: <c>IconOnly</c> → <c>icon-only</c>.</summary>
    public static string Kebab(string name)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    private static string TypeName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return TypeName(nullable) + "?";
        }

        if (type.IsEnum)
        {
            return $"{type.Name}: {string.Join(" | ", Enum.GetNames(type))}";
        }

        if (typeof(IDictionary).IsAssignableFrom(type) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
        {
            return "string";
        }

        return type == typeof(bool) ? "bool"
            : type == typeof(string) ? "string"
            : type == typeof(int) ? "int"
            : type.Name;
    }

    private static string DefaultValue(PropertyInfo property, object? instance)
    {
        if (instance is null)
        {
            return "";
        }

        return property.GetValue(instance) switch
        {
            null => "-",
            bool b => b ? "true" : "false",
            string { Length: 0 } => "\"\"",
            string s => s,
            IDictionary or IEnumerable<KeyValuePair<string, string?>> => "-",
            var value => value.ToString() ?? "-",
        };
    }

    /// <summary>A doc comment's summary as HTML: text encoded, <c>&lt;c&gt;</c> and <c>&lt;see cref&gt;</c> as code.</summary>
    private static string Describe(string member)
    {
        if (!Docs.Value.TryGetValue(member, out var element) || element.Element("summary") is not { } summary)
        {
            return string.Empty;
        }

        var html = new StringBuilder();
        foreach (var node in summary.Nodes())
        {
            switch (node)
            {
                case XText text:
                    html.Append(WebUtility.HtmlEncode(string.Join(' ', text.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } words
                        ? (char.IsWhiteSpace(text.Value[0]) ? " " : "") + words + (char.IsWhiteSpace(text.Value[^1]) ? " " : "")
                        : " "));
                    break;
                case XElement { Name.LocalName: "c" } code:
                    html.Append("<code>").Append(WebUtility.HtmlEncode(code.Value)).Append("</code>");
                    break;
                case XElement { Name.LocalName: "see" } see:
                    var target = (string?)see.Attribute("cref") ?? (string?)see.Attribute("langword") ?? "";
                    html.Append("<code>").Append(WebUtility.HtmlEncode(Kebab(target[(target.LastIndexOf('.') + 1)..]))).Append("</code>");
                    break;
                case XElement other:
                    html.Append(WebUtility.HtmlEncode(other.Value));
                    break;
            }
        }

        return html.ToString().Trim();
    }

    private static Dictionary<string, XElement> LoadDocs()
    {
        var path = Path.ChangeExtension(typeof(DotNetForge.UI.DnfTagHelper).Assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, XElement>();
        }

        return XDocument.Load(path).Descendants("member")
            .Where(m => m.Attribute("name") is not null)
            .GroupBy(m => (string)m.Attribute("name")!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }
}
