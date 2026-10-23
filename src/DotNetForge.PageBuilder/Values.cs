using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>
    /// Value checks mirrored one-to-one from the editor's values.ts. The editor
    /// runs them for immediate feedback; the server runs these and is the
    /// authority. Each function returns the normalized value, or null when the
    /// value is not acceptable.
    ///
    /// Every regular expression is a literal port of its JavaScript twin, with
    /// the differences between the two engines spelled out: JavaScript's <c>\s</c>
    /// and <c>trim()</c> use the ECMAScript whitespace set, <c>\d</c> is ASCII
    /// only, <c>$</c> matches only at the very end (.NET also matches before a
    /// final newline, so <c>\z</c> is used) and the <c>i</c> flag is expanded into
    /// explicit <c>[xX]</c> classes so no culture or Unicode case folding can
    /// widen a match.
    /// </summary>
    public static class Values
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;

        private static readonly string Ws = "[" + Js.WhitespaceChars + "]";
        private static readonly string NumPattern = @"[-+]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)%?";

        private static readonly Regex Hex = new Regex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z", Options);

        private static readonly Regex FuncColor = new Regex(
            @"^(rgb|rgba|hsl|hsla)\(" + Ws + "*" + NumPattern + "(?:deg)?" + Ws + "*(?:," + Ws + "*" + NumPattern + Ws + @"*){2,3}\)\z" +
            @"|^(rgb|rgba|hsl|hsla)\(" + Ws + "*" + NumPattern + "(?:deg)?(?:" + Ws + "+" + NumPattern + "){2}(?:" + Ws + "*/" + Ws + "*" + NumPattern + ")?" + Ws + @"*\)\z",
            Options);

        private static readonly Regex WsRun = new Regex(Ws + "+", Options);
        private static readonly Regex VarRef = new Regex(@"^var\(--[A-Za-z][A-Za-z0-9_-]{0,63}\)\z", Options);
        private static readonly HashSet<string> ColorKeywords = new HashSet<string>(StringComparer.Ordinal) { "transparent", "currentColor", "inherit", "white", "black" };

        /// <summary>No whitespace, control characters, quotes, backslashes or angle brackets anywhere in a URL.</summary>
        private static readonly Regex UrlForbidden = new Regex("[" + Js.WhitespaceChars + @"\u0000-\u001F\u007F""'<>\\`]", Options);

        private static readonly Regex HttpUrl = new Regex("^" + Js.Ci("http") + Js.Ci("s") + "?://[^/?#]+", Options);
        private static readonly Regex SiteRelative = new Regex(@"^/(?![/\\])", Options);
        private static readonly Regex AnchorOrQuery = new Regex("^[#?]", Options);
        private static readonly Regex Mailto = new Regex("^" + Js.Ci("mailto") + ":[^/]", Options);
        private static readonly Regex Tel = new Regex("^" + Js.Ci("tel") + @":[+0-9()\-.]+\z", Options);

        private static readonly Regex YouTube = new Regex(
            "^" + Js.Ci("http") + Js.Ci("s") + "?://(?:" + Js.Ci("www") + @"\.|" + Js.Ci("m") + @"\.)?" + Js.Ci("youtube") +
            "(?:-" + Js.Ci("nocookie") + @")?\." + Js.Ci("com") + "/(?:" + Js.Ci("watch") + @"\?(?:[^#]*&)?" + Js.Ci("v") + "=|" +
            Js.Ci("embed") + "/|" + Js.Ci("shorts") + "/|" + Js.Ci("live") + "/)([A-Za-z0-9_-]{11})(?:[?&#]" + Js.AnyChar + @"*)?\z",
            Options);

        private static readonly Regex YouTuBe = new Regex(
            "^" + Js.Ci("http") + Js.Ci("s") + "?://" + Js.Ci("youtu") + @"\." + Js.Ci("be") + "/([A-Za-z0-9_-]{11})(?:[?#]" + Js.AnyChar + @"*)?\z",
            Options);

        private static readonly Regex Vimeo = new Regex(
            "^" + Js.Ci("http") + Js.Ci("s") + "?://(?:" + Js.Ci("www") + @"\.|" + Js.Ci("player") + @"\.)?" + Js.Ci("vimeo") + @"\." +
            Js.Ci("com") + "/(?:" + Js.Ci("video") + "/)?([0-9]{6,12})(?:[/?#]" + Js.AnyChar + @"*)?\z",
            Options);

        private static readonly Regex SlugPattern = new Regex(@"^[a-z][a-z0-9_-]*\z", Options);
        private static readonly Regex ClassName = new Regex(@"^-?[_a-zA-Z][_a-zA-Z0-9-]*\z", Options);
        private static readonly Regex VarNamePattern = new Regex(@"^--[A-Za-z][A-Za-z0-9_-]{0,40}\z", Options);
        private static readonly Regex VarValueForbidden = new Regex(@"[;{}<>\\""'`@\u0000-\u001F\u007F]", Options);
        private static readonly Regex VarValueFunctions = new Regex(
            Js.Ci("url") + Ws + @"*\(|" + Js.Ci("expression") + Ws + @"*\(|" + Js.Ci("image-set") + Ws + @"*\(|-" + Js.Ci("moz-binding"),
            Options);

        private static readonly Regex ControlChars = new Regex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", Options);
        private static readonly Regex NodeIdPattern = new Regex(@"^[a-z0-9]{1,16}\z", Options);

        /// <summary>A CSS color: hex, rgb()/hsl() (whitespace collapsed), var(--x) or one of a few keywords.</summary>
        public static string Color(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = Js.Trim(raw);
            if (v.Length == 0 || v.Length > 100) return null;
            if (Hex.IsMatch(v) || VarRef.IsMatch(v) || ColorKeywords.Contains(v)) return v;
            if (FuncColor.IsMatch(v)) return WsRun.Replace(v, " ");
            return null;
        }

        /// <summary>Trims and encodes inner spaces (file names often have them); other whitespace stays invalid.</summary>
        private static string PrepareUrl(string value)
        {
            return Js.Trim(value).Replace(" ", "%20");
        }

        /// <summary>
        /// A link target: http(s), site-relative (/path, not //host), #anchor, ?query,
        /// mailto: or tel:. Everything else (javascript:, data:, vbscript:, relative
        /// paths that could be read as a scheme) is refused. Empty stays empty.
        /// </summary>
        public static string LinkUrl(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = PrepareUrl(raw);
            if (v.Length == 0) return string.Empty;
            if (v.Length > 2000 || UrlForbidden.IsMatch(v)) return null;
            if (HttpUrl.IsMatch(v)) return v;
            if (SiteRelative.IsMatch(v) || v == "/") return v;
            if (AnchorOrQuery.IsMatch(v)) return v;
            if (Mailto.IsMatch(v) || Tel.IsMatch(v)) return v;
            return null;
        }

        /// <summary>An image source: http(s) or site-relative only. Empty stays empty.</summary>
        public static string ImageUrl(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = PrepareUrl(raw);
            if (v.Length == 0) return string.Empty;
            if (v.Length > 2000 || UrlForbidden.IsMatch(v)) return null;
            if (HttpUrl.IsMatch(v)) return v;
            if (SiteRelative.IsMatch(v)) return v;
            return null;
        }

        /// <summary>
        /// YouTube and Vimeo links, in any of their usual forms, turned into a
        /// privacy-friendly embed address. Null for anything else.
        /// </summary>
        public static string VideoEmbed(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = Js.Trim(raw);
            if (v.Length == 0 || v.Length > 500 || UrlForbidden.IsMatch(v)) return null;
            var m = YouTube.Match(v);
            if (!m.Success) m = YouTuBe.Match(v);
            if (m.Success) return "https://www.youtube-nocookie.com/embed/" + m.Groups[1].Value;
            m = Vimeo.Match(v);
            if (m.Success) return "https://player.vimeo.com/video/" + m.Groups[1].Value + "?dnt=1";
            return null;
        }

        /// <summary>An anchor/id fragment: lower-case letters, digits, '-' and '_', starting with a letter.</summary>
        public static string Slug(JToken value, int max = 60)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = Js.Trim(raw);
            if (v.Length == 0) return string.Empty;
            if (v.Length > max) return null;
            return SlugPattern.IsMatch(v) ? v : null;
        }

        /// <summary>Space separated CSS class names; the editor's own "pe-" prefix is reserved.</summary>
        public static string ClassNames(JToken value, int max = 200)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var parts = WsRun.Split(Js.Trim(raw)).Where(p => p.Length > 0).ToList();
            if (parts.Count == 0) return string.Empty;
            var joined = string.Join(" ", parts);
            if (joined.Length > max) return null;
            return parts.All(p => ClassName.IsMatch(p) && !p.StartsWith("pe-", StringComparison.Ordinal)) ? joined : null;
        }

        /// <summary>A custom property name (--name). Not trimmed: the name is used as is.</summary>
        public static string VarName(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            return VarNamePattern.IsMatch(raw) ? raw : null;
        }

        /// <summary>
        /// A custom property value. Deliberately narrow: no characters that could end
        /// the declaration or rule, no url()/expression()/escapes, no imports.
        /// </summary>
        public static string VarValue(JToken value)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = Js.Trim(raw);
            if (v.Length == 0 || v.Length > 200) return null;
            if (VarValueForbidden.IsMatch(v)) return null;
            if (VarValueFunctions.IsMatch(v)) return null;
            // A comment or an unbalanced parenthesis would swallow the rules that follow in the stylesheet.
            if (v.Contains("/*") || v.Contains("*/")) return null;
            var depth = 0;
            foreach (var c in v)
            {
                if (c == '(') depth++;
                else if (c == ')' && --depth < 0) return null;
            }
            return depth == 0 ? v : null;
        }

        /// <summary>A finite number rounded to 2 decimals (as the editor rounds it), within [min, max].</summary>
        public static double? Num(JToken value, double min, double max)
        {
            if (!Js.TryNumber(value, out var d) || double.IsNaN(d) || double.IsInfinity(d)) return null;
            var rounded = Js.Round(d * 100) / 100;
            if (rounded < min || rounded > max) return null;
            return rounded == 0 ? 0 : rounded;
        }

        private static readonly Regex TrackPx = new Regex(@"^([0-9]{1,4})px$", RegexOptions.CultureInvariant);
        private static readonly Regex TrackFr = new Regex(@"^([0-9]{1,3}(?:\.[0-9]{1,6})?)fr$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A grid track size as Figma sets it: "auto" (Hug), "&lt;n&gt;px" (Fixed, 0-4000, whole pixels) or
        /// "&lt;n&gt;fr" (Fill, 0.01-100, two decimals), in canonical form; null when invalid.
        /// Mirrors track() in core/values.ts.
        /// </summary>
        public static string Track(JToken value)
        {
            if (value == null || value.Type != JTokenType.String) return null;
            var s = (string)value;
            if (s == "auto") return "auto";
            var px = TrackPx.Match(s);
            if (px.Success)
            {
                var n = int.Parse(px.Groups[1].Value, CultureInfo.InvariantCulture);
                return n <= 4000 ? n.ToString(CultureInfo.InvariantCulture) + "px" : null;
            }
            var fr = TrackFr.Match(s);
            if (!fr.Success) return null;
            var f = Math.Round(double.Parse(fr.Groups[1].Value, CultureInfo.InvariantCulture) * 100, MidpointRounding.AwayFromZero) / 100;
            return f >= 0.01 && f <= 100 ? f.ToString("0.##", CultureInfo.InvariantCulture) + "fr" : null;
        }

        /// <summary>A JSON number that is a whole number (1.0 included, as in JavaScript), within [min, max].</summary>
        public static long? Int(JToken value, double min, double max)
        {
            if (!Js.TryNumber(value, out var d) || !Js.IsInteger(d)) return null;
            if (d < min || d > max) return null;
            return (long)d;
        }

        public static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>Plain text: control characters (except newline, carriage return and tab) removed, length capped.</summary>
        public static string Text(JToken value, int max)
        {
            if (!Js.TryString(value, out var raw)) return null;
            var v = ControlChars.Replace(raw, string.Empty);
            return v.Length > max ? null : v;
        }

        /// <summary>A page icon name from schema/icons.json, or an empty string for none.</summary>
        public static string IconName(JToken value)
        {
            if (!Js.TryString(value, out var name)) return null;
            if (name.Length == 0) return string.Empty;
            return Schema.PageIcons.ContainsKey(name) ? name : null;
        }

        /// <summary>Node ids: short, lower-case alphanumerics; they become CSS class names.</summary>
        public static string NodeId(JToken value)
        {
            return Js.TryString(value, out var raw) && NodeIdPattern.IsMatch(raw) ? raw : null;
        }
    }

    /// <summary>
    /// The few JavaScript semantics both renderers depend on, reproduced exactly:
    /// what counts as a number or a string in parsed JSON, Math.round, String(n),
    /// trim(), and the order in which an object's keys are enumerated.
    /// </summary>
    internal static class Js
    {
        /// <summary>
        /// The ECMAScript WhiteSpace and LineTerminator code points (what \s and
        /// trim() mean in JavaScript). Kept as numbers so no editor or tool can turn
        /// them into invisible literal characters.
        /// </summary>
        private static readonly HashSet<int> WhitespaceCodes = new HashSet<int>
        {
            0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020, 0x00A0, 0x1680,
            0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
            0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF,
        };

        /// <summary>The whitespace set written as regex escapes, for use inside a character class.</summary>
        internal static readonly string WhitespaceChars = string.Concat(WhitespaceCodes.OrderBy(c => c).Select(RegexEscape));

        /// <summary>JavaScript's <c>.</c> outside the s flag: anything but a line terminator.</summary>
        internal static readonly string AnyChar = "[^" + RegexEscape(0x0A) + RegexEscape(0x0D) + RegexEscape(0x2028) + RegexEscape(0x2029) + "]";

        internal static bool IsWhitespace(char c)
        {
            return WhitespaceCodes.Contains(c);
        }

        private static string RegexEscape(int codePoint)
        {
            return "\\u" + codePoint.ToString("X4", CultureInfo.InvariantCulture);
        }

        /// <summary>String.prototype.trim(): .NET's Trim() uses a different whitespace set (it trims U+0085, but not U+FEFF).</summary>
        internal static string Trim(string value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && IsWhitespace(value[start])) start++;
            while (end > start && IsWhitespace(value[end - 1])) end--;
            return start == 0 && end == value.Length ? value : value.Substring(start, end - start);
        }

        /// <summary>A word matched without case, written out as [xX] classes (the regex i flag, ASCII only).</summary>
        internal static string Ci(string word)
        {
            var sb = new StringBuilder(word.Length * 4);
            foreach (var c in word)
            {
                if (c >= 'a' && c <= 'z') sb.Append('[').Append(c).Append(char.ToUpperInvariant(c)).Append(']');
                else if (c == '-' || c == '.') sb.Append('\\').Append(c);
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary><c>typeof value === 'string'</c>.</summary>
        internal static bool TryString(JToken value, out string text)
        {
            if (value != null && value.Type == JTokenType.String)
            {
                text = (string)((JValue)value).Value;
                return text != null;
            }
            text = null;
            return false;
        }

        internal static bool IsString(JToken value)
        {
            return TryString(value, out _);
        }

        /// <summary><c>typeof value === 'number'</c>: Newtonsoft's Integer and Float alike (it reads "1.0" as a Float).</summary>
        internal static bool TryNumber(JToken value, out double number)
        {
            number = 0;
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return false;
            var raw = ((JValue)value).Value;
            switch (raw)
            {
                case double d: number = d; return true;
                case long l: number = l; return true;
                case int i: number = i; return true;
                case BigInteger b: number = (double)b; return true;
                case decimal m: number = (double)m; return true;
                case float f: number = f; return true;
                case null: return false;
                default: number = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return true;
            }
        }

        internal static bool IsNumber(JToken value)
        {
            return TryNumber(value, out _);
        }

        /// <summary><c>value === true</c>.</summary>
        internal static bool IsTrue(JToken value)
        {
            return value != null && value.Type == JTokenType.Boolean && (bool)((JValue)value).Value;
        }

        internal static bool IsBool(JToken value)
        {
            return value != null && value.Type == JTokenType.Boolean;
        }

        /// <summary><c>Number.isInteger</c>.</summary>
        internal static bool IsInteger(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d;
        }

        /// <summary><c>Math.round</c>: halves go towards +infinity (Math.Round would go to even or away from zero).</summary>
        internal static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            var f = Math.Floor(x);
            if (x - f >= 0.5) f += 1;
            return f;
        }

        /// <summary>
        /// A number as JSON.stringify writes it: integers without a fraction, so
        /// the normalized document serializes the way the editor's does (Newtonsoft
        /// would write 12.0 for a double).
        /// </summary>
        internal static JValue Number(double d)
        {
            if (IsInteger(d) && Math.Abs(d) < 9.2e18) return new JValue((long)d);
            return new JValue(d);
        }

        /// <summary>
        /// <c>String(n)</c>: the shortest digit string that reads back as the same
        /// double, laid out with JavaScript's rules for when to use an exponent.
        /// </summary>
        internal static string NumberToString(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0) return "0";

            var abs = Math.Abs(d);
            string digits = null;
            var exponent = 0;
            for (var precision = 1; precision <= 17; precision++)
            {
                var s = abs.ToString("E" + (precision - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                // Rounded up past double.MaxValue, a short form does not parse (.NET Framework) - try more digits.
                if (precision < 17 && (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var back) || back != abs)) continue;
                var e = s.IndexOf('E');
                digits = s.Substring(0, e).Replace(".", string.Empty);
                exponent = int.Parse(s.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }

            digits = digits.TrimEnd('0');
            var k = digits.Length;
            var n = exponent + 1;
            var sb = new StringBuilder(32);
            if (d < 0) sb.Append('-');
            if (k <= n && n <= 21)
            {
                sb.Append(digits).Append('0', n - k);
            }
            else if (0 < n && n <= 21)
            {
                sb.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
            }
            else if (-6 < n && n <= 0)
            {
                sb.Append("0.").Append('0', -n).Append(digits);
            }
            else
            {
                sb.Append(digits[0]);
                if (k > 1) sb.Append('.').Append(digits, 1, k - 1);
                sb.Append('e').Append(n - 1 >= 0 ? '+' : '-').Append(Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary><c>String(value)</c> for a JSON value.</summary>
        internal static string ToJsString(JToken value)
        {
            if (value == null) return "undefined";
            if (TryString(value, out var s)) return s;
            if (TryNumber(value, out var d)) return NumberToString(d);
            if (value.Type == JTokenType.Boolean) return (bool)((JValue)value).Value ? "true" : "false";
            if (value.Type == JTokenType.Null) return "null";
            return value.ToString();
        }

        /// <summary>
        /// <c>Object.entries</c> order: keys that are array indices come first in
        /// ascending numeric order, then the other keys in insertion order. Node ids
        /// such as "12" are array indices, so this decides the order of the nodes.
        /// </summary>
        internal static IReadOnlyList<JProperty> Entries(JObject value)
        {
            List<KeyValuePair<uint, JProperty>> indices = null;
            var rest = new List<JProperty>();
            foreach (var p in value.Properties())
            {
                if (IsArrayIndex(p.Name, out var index))
                {
                    (indices ??= new List<KeyValuePair<uint, JProperty>>()).Add(new KeyValuePair<uint, JProperty>(index, p));
                }
                else
                {
                    rest.Add(p);
                }
            }
            if (indices == null) return rest;
            return indices.OrderBy(i => i.Key).Select(i => i.Value).Concat(rest).ToList();
        }

        private static bool IsArrayIndex(string key, out uint index)
        {
            index = 0;
            if (key.Length == 0 || key.Length > 10) return false;
            if (key.Length > 1 && key[0] == '0') return false;
            ulong value = 0;
            foreach (var c in key)
            {
                if (c < '0' || c > '9') return false;
                value = value * 10 + (ulong)(c - '0');
            }
            if (value > 4294967294UL) return false;
            index = (uint)value;
            return true;
        }
    }
}
