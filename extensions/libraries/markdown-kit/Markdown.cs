using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkdownKit;

/// <summary>
/// A small, safe Markdown subset: paragraphs, <c>#</c> headings, <c>-</c>/<c>*</c> and <c>1.</c> lists, fenced code
/// blocks, <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c> and <c>[links](https://...)</c>. Everything is
/// HTML-encoded first, so raw HTML in the input is shown as text, and links only accept http(s), mailto and
/// site-relative URLs - the output never contains markup the input did not ask for through Markdown.
/// </summary>
public static partial class Markdown
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var html = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        string? list = null; // "ul" or "ol" while inside a list

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void CloseList()
        {
            if (list is not null)
            {
                html.Append("</").Append(list).Append(">\n");
                list = null;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimEnd().StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Add(lines[i]);
                }

                html.Append("<pre><code>").Append(WebUtility.HtmlEncode(string.Join('\n', code))).Append("</code></pre>\n");
                continue;
            }

            if (line.Length == 0)
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            if (Heading().Match(line) is { Success: true } heading)
            {
                FlushParagraph();
                CloseList();
                var level = heading.Groups[1].Length;
                html.Append($"<h{level}>").Append(Inline(heading.Groups[2].Value)).Append($"</h{level}>\n");
                continue;
            }

            var bullet = Bullet().Match(line);
            var numbered = bullet.Success ? Match.Empty : Numbered().Match(line);
            if (bullet.Success || numbered.Success)
            {
                FlushParagraph();
                var kind = bullet.Success ? "ul" : "ol";
                if (list != kind)
                {
                    CloseList();
                    html.Append('<').Append(kind).Append(">\n");
                    list = kind;
                }

                html.Append("<li>").Append(Inline((bullet.Success ? bullet : numbered).Groups[1].Value)).Append("</li>\n");
                continue;
            }

            CloseList();
            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        CloseList();
        return html.ToString().TrimEnd('\n');
    }

    /// <summary>Inline formatting on one block of text; code spans are protected from further formatting.</summary>
    private static string Inline(string text)
    {
        var spans = new List<string>();
        var encoded = CodeSpan().Replace(text, m =>
        {
            spans.Add("<code>" + WebUtility.HtmlEncode(m.Groups[1].Value) + "</code>");
            return $"\u0000{spans.Count - 1}\u0000";
        });

        encoded = WebUtility.HtmlEncode(encoded);
        encoded = Link().Replace(encoded, m => IsSafeUrl(m.Groups[2].Value)
            ? $"<a href=\"{m.Groups[2].Value}\">{m.Groups[1].Value}</a>"
            : m.Groups[1].Value);
        encoded = Bold().Replace(encoded, "<strong>$1</strong>");
        encoded = Italic().Replace(encoded, "<em>$1</em>");
        return Placeholder().Replace(encoded, m => spans[int.Parse(m.Groups[1].Value)]);
    }

    // The URL is already HTML-encoded (quotes can't break out of the attribute); only the scheme needs checking.
    private static bool IsSafeUrl(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal));

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*[-*]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*\d+[.)]\s+(.+)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])[*_](?![\s*_])(.+?)(?<![\s*_])[*_](?![\w*])")]
    private static partial Regex Italic();

    [GeneratedRegex(@"\x00(\d+)\x00")]
    private static partial Regex Placeholder();
}
