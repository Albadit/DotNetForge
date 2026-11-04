using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Infrastructure.Configuration;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// The admin translations (.docs/features/internationalization.md#admin-language): every text the code shows through
/// <c>T["..."]</c> (views, controllers), <c>AdminText.Key("...")</c>, the sidebar tree, the UI components'
/// <c>t("...")</c>, the scripts' <c>dnfText("...")</c> / <c>t("...")</c> and the page builder editor's <c>t("...")</c>
/// is in every shipped catalog, with the same
/// <c>{0}</c> placeholders - so every admin screen is complete in every offered language.
/// </summary>
public sealed partial class AdminTextCatalogTests
{
    private static readonly string Root = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory)!;
    private static readonly string Web = Path.Combine(Root, "src", "DotNetForge.Web");
    private static readonly string CatalogFolder = Path.Combine(Web, "Localization", "Admin");

    [Fact]
    public void Keys_are_string_literals()
    {
        var found = new List<string>();
        foreach (var file in ServerFiles())
        {
            foreach (Match m in InterpolatedKey().Matches(File.ReadAllText(file)))
            {
                found.Add($"{Relative(file)}: {m.Value}");
            }
        }

        Assert.True(found.Count == 0, "Use T[\"... {0} ...\", value], not an interpolated or verbatim key:\n" + string.Join("\n", found));
    }

    [Fact]
    public void Every_catalog_has_every_text_with_the_same_placeholders()
    {
        var server = ServerKeys();
        var script = ScriptKeys();
        var builder = BuilderKeys();
        Assert.NotEmpty(server);
        var catalogs = Directory.GetFiles(CatalogFolder, "*.json");
        Assert.NotEmpty(catalogs);

        var problems = new List<string>();
        foreach (var file in catalogs)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Check(code, "strings", json.RootElement, server, problems);
            Check(code, "script", json.RootElement, script, problems);
            Check(code, "builder", json.RootElement, builder, problems);
        }

        Assert.True(problems.Count == 0, $"{problems.Count} catalog problem(s):\n" + string.Join("\n", problems.Take(200)));
    }

    private static void Check(string code, string section, JsonElement root, IReadOnlySet<string> keys, List<string> problems)
    {
        var values = root.TryGetProperty(section, out var s) && s.ValueKind == JsonValueKind.Object
            ? s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{code}.{section}: missing \"{key}\"");
            }
            else if (!Placeholders(key).SetEquals(Placeholders(value)))
            {
                problems.Add($"{code}.{section}: placeholders differ in \"{key}\" → \"{value}\"");
            }
        }

        foreach (var stale in values.Keys.Where(k => !keys.Contains(k)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{code}.{section}: unused \"{stale}\"");
        }
    }

    /// <summary>The texts of views, controllers, services, the sidebar tree and the UI components.</summary>
    public static IReadOnlySet<string> ServerKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ServerFiles())
        {
            var text = File.ReadAllText(file);
            Collect(ServerKey().Matches(text), keys);
            if (file.EndsWith("AdminNavigation.cs", StringComparison.Ordinal))
            {
                Collect(NavKey().Matches(text), keys);
            }
        }

        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src", "DotNetForge.UI"), "*.cs", SearchOption.AllDirectories).Where(Included))
        {
            Collect(ComponentKey().Matches(File.ReadAllText(file)), keys);
        }

        return keys;
    }

    /// <summary>The texts of the admin scripts: dnf.js and the admin screen scripts.</summary>
    public static IReadOnlySet<string> ScriptKeys() =>
        ScriptKeysOf(new[] { Path.Combine(Root, "src", "DotNetForge.UI", "wwwroot", "dnf.js") }
            .Concat(Directory.GetFiles(Path.Combine(Web, "wwwroot", "js", "admin"), "*.js", SearchOption.AllDirectories)));

    /// <summary>The texts of the page builder editor (its renderer and core show none).</summary>
    public static IReadOnlySet<string> BuilderKeys() =>
        ScriptKeysOf(Directory.GetFiles(Path.Combine(Web, "wwwroot", "js", "builder"), "*.js", SearchOption.AllDirectories));

    private static HashSet<string> ScriptKeysOf(IEnumerable<string> files)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            Collect(ScriptKey().Matches(File.ReadAllText(file)), keys);
        }

        return keys;
    }

    private static IEnumerable<string> ServerFiles() =>
        Directory.GetFiles(Web, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Web, "*.cshtml", SearchOption.AllDirectories))
            .Where(Included)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}UiDocs{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool Included(string file) =>
        !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static void Collect(MatchCollection matches, HashSet<string> keys)
    {
        foreach (Match m in matches)
        {
            keys.Add(Unescape(m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value));
        }
    }

    private static string Unescape(string literal) =>
        literal.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\'", "'", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    private static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(m => m.Value).ToHashSet();

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary><c>T["text"</c>, <c>_t["text"</c> and <c>AdminText.Key("text"</c>.</summary>
    [GeneratedRegex("""(?:\bT|\b_t)\[\s*"((?:[^"\\]|\\.)*)"|\bAdminText\.Key\(\s*"((?:[^"\\]|\\.)*)"(?=\s*\))""")]
    private static partial Regex ServerKey();

    [GeneratedRegex("""\bT\[\s*[$@]""")]
    private static partial Regex InterpolatedKey();

    /// <summary>The labels of <c>new NavSection("..."</c>, <c>new NavGroup("..."</c>, <c>new NavLink("..."</c>.</summary>
    [GeneratedRegex(@"new Nav(?:Section|Group|Link)\(\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex NavKey();

    /// <summary>The components' <c>t("text")</c> (<c>DnfText.For</c>).</summary>
    [GeneratedRegex("""\bt\(\s*"((?:[^"\\]|\\.)*)"\s*\)""")]
    private static partial Regex ComponentKey();

    /// <summary><c>dnfText("text"</c> / <c>t("text"</c> with double or single quotes.</summary>
    [GeneratedRegex("""\b(?:dnfText|t)\(\s*(?:"((?:[^"\\]|\\.)*)"|'((?:[^'\\]|\\.)*)')""")]
    private static partial Regex ScriptKey();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
