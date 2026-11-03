using System.Text;
using System.Text.RegularExpressions;

namespace DotNetForge.Core.Html;

/// <summary>
/// Shrinks rendered HTML without changing the page the browser builds from it
/// (.docs/guides/development.md#development-and-production-builds):
/// <list type="bullet">
/// <item>whitespace next to a block-level tag (<c>div</c>, <c>li</c>, <c>td</c>, <c>meta</c>, ...) goes - browsers don't
/// render it; between inline content it becomes one space, or one line break inside text (text on both sides) when it
/// holds one, so a multi-line text keeps its lines for <c>white-space: pre-line</c>;</item>
/// <item>comments go, except the page builder's <c>&lt;!--pe:...--&gt;</c> markers and conditional comments;</item>
/// <item>closing tags the HTML spec lets out go where it does (<c>&lt;/li&gt;</c> before the next <c>li</c> or the end of
/// its list, <c>&lt;/td&gt;</c>, <c>&lt;/tr&gt;</c>, <c>&lt;/option&gt;</c>, <c>&lt;/head&gt;</c>, <c>&lt;/body&gt;</c>, ...);</item>
/// <item>in a tag, whitespace between attributes becomes one space and an empty <c>class</c> or <c>style</c> goes.</item>
/// </list>
/// Exactly as written: attribute values and the content of <c>pre</c>, <c>textarea</c>, <c>script</c> and <c>style</c>.
/// Only HTML whitespace counts (a no-break space stays). A block element that CSS makes inline (inline-block) loses the
/// space next to it; give such an element a parent that is flex or grid, as the UI library does.
/// </summary>
public static partial class HtmlMinifier
{
    private enum Kind { Text, Space, Tag, Raw, Comment, Other }

    private readonly record struct Token(Kind Kind, string Value, string Name = "", bool Closing = false);

    public static string Minify(string html)
    {
        List<Token> tokens;
        try
        {
            tokens = Tokenize(html);
        }
        catch (RegexMatchTimeoutException)
        {
            return html;
        }

        var output = new StringBuilder(html.Length);
        Token? previous = null;
        var pending = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == Kind.Space || (token.Kind == Kind.Comment && !IsKeptComment(token.Value)))
            {
                pending.Append(token.Kind == Kind.Space ? token.Value : string.Empty);
                continue;
            }

            if (pending.Length > 0)
            {
                output.Append(Whitespace(previous, token, pending.ToString()));
                pending.Clear();
            }

            if (!IsOmitted(token, NextSignificant(tokens, i)))
            {
                output.Append(token.Kind == Kind.Tag ? InsideTag().Replace(token.Value, CollapseAttribute) : token.Value);
            }

            previous = token;
        }

        return output.ToString();
    }

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        foreach (Match match in Tokens().Matches(html))
        {
            tokens.Add(match switch
            {
                _ when match.Groups["space"].Success => new Token(Kind.Space, match.Value),
                _ when match.Groups["comment"].Success => new Token(Kind.Comment, match.Value),
                _ when match.Groups["raw"].Success => new Token(Kind.Raw, match.Value, match.Groups["rawname"].Value.ToLowerInvariant()),
                _ when match.Groups["tag"].Success => new Token(Kind.Tag, match.Value, match.Groups["name"].Value.ToLowerInvariant(), match.Value[1] == '/'),
                _ when match.Groups["other"].Success => new Token(Kind.Other, match.Value),
                _ => new Token(Kind.Text, match.Value),
            });
        }

        return tokens;
    }

    /// <summary>What a whitespace run between two tokens becomes (nothing at the start or end of the document).</summary>
    private static string Whitespace(Token? previous, Token next, string run)
    {
        if (previous is not { } before || IsBlock(before) || IsBlock(next))
        {
            return string.Empty;
        }

        return before.Kind == Kind.Text && next.Kind == Kind.Text && run.Contains('\n') ? "\n" : " ";
    }

    private static Token? NextSignificant(List<Token> tokens, int index)
    {
        for (var i = index + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != Kind.Space && !(tokens[i].Kind == Kind.Comment && !IsKeptComment(tokens[i].Value)))
            {
                return tokens[i];
            }
        }

        return null;
    }

    private static bool IsKeptComment(string comment) =>
        comment.StartsWith("<!--pe:", StringComparison.Ordinal) || comment.StartsWith("<!--[if", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlock(Token token) =>
        token.Kind is Kind.Other || (token.Kind is Kind.Tag or Kind.Raw && !Inline.Contains(token.Name) && !token.Name.Contains('-'));

    /// <summary>A closing tag the spec allows to leave out before <paramref name="next"/> (null: the end of the document).</summary>
    private static bool IsOmitted(Token token, Token? next)
    {
        if (token.Kind != Kind.Tag || !token.Closing || !OptionalEnd.TryGetValue(token.Name, out var followers))
        {
            return false;
        }

        if (next is not { } after)
        {
            return followers.Contains("");
        }

        return after.Kind == Kind.Tag && followers.Contains(after.Closing ? "/" + after.Name : after.Name);
    }

    // In a tag only the whitespace between attributes collapses and an empty class/style goes; quoted values stay whole.
    private static string CollapseAttribute(Match match) =>
        match.Groups["empty"].Success ? string.Empty : match.Groups["space"].Success ? " " : match.Value;

    /// <summary>Elements laid out inline by default: whitespace next to them can show. Custom elements are inline too.</summary>
    private static readonly HashSet<string> Inline = new(StringComparer.Ordinal)
    {
        "a", "abbr", "audio", "b", "bdi", "bdo", "br", "button", "canvas", "cite", "code", "data", "datalist", "del",
        "dfn", "em", "embed", "i", "iframe", "img", "input", "ins", "kbd", "label", "map", "mark", "math", "meter",
        "noscript", "object", "output", "picture", "progress", "q", "rp", "rt", "ruby", "s", "samp", "script", "select",
        "slot", "small", "span", "strong", "style", "sub", "sup", "svg", "template", "text", "textarea", "time",
        "tspan", "u", "var", "video", "wbr",
    };

    /// <summary>Closing tags that may be left out, and what may follow (a name: its start tag; "/name": its end tag; "": the end).</summary>
    private static readonly Dictionary<string, HashSet<string>> OptionalEnd = new(StringComparer.Ordinal)
    {
        ["li"] = ["li", "/ul", "/ol", "/menu"],
        ["option"] = ["option", "optgroup", "/select", "/optgroup", "/datalist"],
        ["td"] = ["td", "th", "/tr"],
        ["th"] = ["td", "th", "/tr"],
        ["tr"] = ["tr", "/tbody", "/thead", "/tfoot", "/table"],
        ["thead"] = ["tbody", "tfoot"],
        ["tbody"] = ["tbody", "tfoot", "/table"],
        ["tfoot"] = ["/table"],
        ["head"] = ["body"],
        ["body"] = ["/html", ""],
        ["html"] = [""],
    };

    [GeneratedRegex("""
        (?<comment><!--[\s\S]*?-->)
        |(?<raw><(?<rawname>pre|textarea|script|style)\b(?:[^>"']|"[^"]*"|'[^']*')*>[\s\S]*?</\k<rawname>[ \t\r\n\f]*>)
        |(?<tag></?(?<name>[a-zA-Z][^ \t\r\n\f/>]*)(?:[^>"']|"[^"]*"|'[^']*')*>)
        |(?<other><[!?](?:[^>"']|"[^"]*"|'[^']*')*>)
        |(?<space>[ \t\r\n\f]+)
        |[^< \t\r\n\f]+|<
        """, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Tokens();

    [GeneratedRegex("""(?<empty>[ \t\r\n\f]+(?:class|style)=(?:""|''))|(?<space>[ \t\r\n\f]+)|"[^"]*"|'[^']*'""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex InsideTag();
}
