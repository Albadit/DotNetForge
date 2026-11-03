using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetForge.Web.Areas.Admin.UiDocs;

/// <summary>
/// The source text of the UI docs views (embedded into the assembly by DotNetForge.Web.csproj, since a published app
/// has no .cshtml files): an example's code tab shows exactly the Razor that renders its preview.
/// </summary>
public static partial class UiDocsSource
{
    private const string ViewRoot = "/Areas/Admin/Views/UiDocs/";
    private static readonly ConcurrentDictionary<string, string?> Files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The inner source of <c>&lt;ui-doc-example id="…"&gt;</c> (or another docs <paramref name="element"/>) in the view at
    /// <paramref name="viewPath"/>, dedented.
    /// </summary>
    public static string? Inner(string viewPath, string id, string element = "ui-doc-example")
    {
        var text = Files.GetOrAdd(viewPath, Read);
        if (text is null)
        {
            return null;
        }

        foreach (Match start in ElementStart().Matches(text))
        {
            if (start.Groups["name"].Value == element && start.Groups["attrs"].Value.Contains($"id=\"{id}\"", StringComparison.Ordinal))
            {
                var bodyStart = start.Index + start.Length;
                var end = text.IndexOf($"</{element}>", bodyStart, StringComparison.Ordinal);
                return end < 0 ? null : Dedent(text[bodyStart..end]);
            }
        }

        return null;
    }

    /// <summary>Removes the leading and trailing blank lines and the indentation all lines share.</summary>
    public static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var indent = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.Trim()));
    }

    /// <summary>
    /// Rendered HTML laid out one element per line, indented by nesting - an element with only text (or nothing) inside
    /// stays on one line, and so does an <c>&lt;svg&gt;</c> icon.
    /// </summary>
    public static string FormatHtml(string html)
    {
        var lines = new List<string>();
        var tokens = HtmlToken().Matches(html).Select(m => m.Value).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var pad = new string(' ', depth * 2);
            if (token.StartsWith("</", StringComparison.Ordinal))
            {
                depth = Math.Max(0, depth - 1);
                lines.Add(new string(' ', depth * 2) + token.Trim());
                continue;
            }

            if (!token.StartsWith('<') || token.StartsWith("<!", StringComparison.Ordinal))
            {
                lines.Add(pad + string.Join(' ', token.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
                continue;
            }

            var name = TagName().Match(token).Groups[1].Value.ToLowerInvariant();
            if (token.EndsWith("/>", StringComparison.Ordinal) || VoidElements.Contains(name))
            {
                lines.Add(pad + token);
                continue;
            }

            // <svg …>…</svg> and <x>text</x> / <x></x> on one line.
            var close = $"</{name}>";
            if (name == "svg")
            {
                var line = new StringBuilder(token);
                while (++i < tokens.Count)
                {
                    line.Append(tokens[i].Trim());
                    if (string.Equals(tokens[i].Trim(), close, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                lines.Add(pad + line);
                continue;
            }

            if (i + 1 < tokens.Count && string.Equals(tokens[i + 1].Trim(), close, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(pad + token + close);
                i++;
                continue;
            }

            if (i + 2 < tokens.Count && !tokens[i + 1].StartsWith('<') && string.Equals(tokens[i + 2].Trim(), close, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(pad + token + tokens[i + 1].Trim() + close);
                i += 2;
                continue;
            }

            lines.Add(pad + token);
            depth++;
        }

        return string.Join('\n', lines);
    }

    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    /// <summary>
    /// Markup (Razor or HTML) as highlighted HTML: comments, tag names, attribute names, quoted values and Razor
    /// expressions get <c>dnf-docs-token--*</c> spans; everything is HTML-encoded.
    /// </summary>
    public static string Highlight(string code)
    {
        var html = new StringBuilder(code.Length * 2);
        var at = 0;
        foreach (Match token in Token().Matches(code))
        {
            html.Append(WebUtility.HtmlEncode(code[at..token.Index]));
            var kind = token.Groups["comment"].Success ? "comment"
                : token.Groups["tag"].Success ? "tag"
                : token.Groups["attr"].Success ? "attr"
                : token.Groups["string"].Success ? "string"
                : "razor";
            if (kind == "attr")
            {
                var name = token.Groups["attr"].Value;
                html.Append(WebUtility.HtmlEncode(token.Value[..^name.Length]));
                html.Append($"<span class=\"dnf-docs-token dnf-docs-token--attr\">{WebUtility.HtmlEncode(name)}</span>");
            }
            else
            {
                html.Append($"<span class=\"dnf-docs-token dnf-docs-token--{kind}\">{WebUtility.HtmlEncode(token.Value)}</span>");
            }

            at = token.Index + token.Length;
        }

        html.Append(WebUtility.HtmlEncode(code[at..]));
        return html.ToString();
    }

    private static string? Read(string viewPath)
    {
        if (!viewPath.StartsWith(ViewRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var wanted = "UiDocs/" + viewPath[ViewRoot.Length..];
        var assembly = typeof(UiDocsSource).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => string.Equals(n.Replace('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->|<(?:[^>""']|""[^""]*""|'[^']*')+>|[^<]+")]
    private static partial Regex HtmlToken();

    [GeneratedRegex(@"^<\s*([A-Za-z][\w:-]*)")]
    private static partial Regex TagName();

    [GeneratedRegex(@"<(?<name>ui-doc-example|ui-doc-code)(?<attrs>(?:[^>""]|""[^""]*"")*)>")]
    private static partial Regex ElementStart();

    [GeneratedRegex(@"(?<comment>@\*[\s\S]*?\*@|<!--[\s\S]*?-->)|(?<tag></?[A-Za-z][\w:.-]*)|\s(?<attr>[@A-Za-z_:][\w:.\-]*)(?==)|(?<string>""[^""\n]*"")|(?<razor>@\(?[A-Za-z_][\w.]*)")]
    private static partial Regex Token();
}
