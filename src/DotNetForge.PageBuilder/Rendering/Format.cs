using System.Globalization;
using System.Text;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>
    /// Output formatting shared by the CSS and HTML builders. Mirrors
    /// renderer/format.ts character for character: the golden fixtures compare
    /// both renderers byte by byte.
    /// </summary>
    public static class Format
    {
        /// <summary>
        /// Escapes &amp; &lt; &gt; " and ' (as &amp;#39;). WebUtility.HtmlEncode is not used:
        /// it encodes a different set of characters.
        /// </summary>
        public static string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder sb = null;
            for (var i = 0; i < value.Length; i++)
            {
                string replacement;
                switch (value[i])
                {
                    case '&': replacement = "&amp;"; break;
                    case '<': replacement = "&lt;"; break;
                    case '>': replacement = "&gt;"; break;
                    case '"': replacement = "&quot;"; break;
                    case '\'': replacement = "&#39;"; break;
                    default: replacement = null; break;
                }
                if (replacement == null)
                {
                    sb?.Append(value[i]);
                    continue;
                }
                if (sb == null) sb = new StringBuilder(value.Length + 16).Append(value, 0, i);
                sb.Append(replacement);
            }
            return sb == null ? value : sb.ToString();
        }

        /// <summary>A number with at most 2 decimals, invariant, no exponent, no trailing zeros, never "-0".</summary>
        public static string Fmt(double n)
        {
            var r = Js.Round(n * 100) / 100;
            return Js.NumberToString(r == 0 ? 0 : r);
        }

        public static string Px(double n)
        {
            return Fmt(n) + "px";
        }

        /// <summary>A URL inside url("..."): characters that could end the string or the function are percent-encoded.</summary>
        public static string CssUrl(string url)
        {
            var sb = new StringBuilder(url.Length + 8).Append("url(\"");
            foreach (var c in url)
            {
                if (c == '(' || c == ')' || c == '"' || c == '\'' || c == '\\' || Js.IsWhitespace(c))
                {
                    sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.Append("\")").ToString();
        }

        /// <summary><c>String(n)</c> of a whole number.</summary>
        internal static string Int(long n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
