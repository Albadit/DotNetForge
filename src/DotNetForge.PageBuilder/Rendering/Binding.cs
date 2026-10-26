using System;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>
    /// Bound props of a node inside a content list item (.docs/features/structured-content.md#data-binding): each
    /// template (<c>{name}</c>, <c>From {price} USD</c>, <c>/products/{slug}</c>) is filled from the item's field map
    /// and the result is cleaned by the prop's own validator, so a bound value is held to the same rules as a typed
    /// one. Mirrors renderer/binding.js.
    /// </summary>
    public static class Binding
    {
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant);
        private static readonly Regex Exact = new Regex(@"^\{([^{}]+)\}\z", RegexOptions.CultureInvariant);
        private static readonly Regex LoneSurrogate = new Regex(@"[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]", RegexOptions.CultureInvariant);

        /// <summary>The node with its bound props filled from <paramref name="item"/>; the node itself when nothing is bound.</summary>
        public static PageNode Apply(PageNode node, JObject item)
        {
            if (item == null || node.Bind.Count == 0) return node;
            var def = Schema.Component(node.Type);
            if (def == null) return node;
            var fields = item["fields"] as JObject ?? new JObject();
            var props = (JObject)node.Props.DeepClone();
            foreach (var b in node.Bind)
            {
                var pdef = def.Prop(b.Key);
                if (pdef == null) continue;
                var value = Value(pdef.Type, b.Value, fields, MaxLength(pdef));
                if (value != null) props[b.Key] = DocumentValidator.NormalizeProp(pdef, value);
            }
            return new PageNode(node, props);
        }

        /// <summary>How much filled-in text a prop takes; longer text is cut instead of being refused.</summary>
        private static int MaxLength(PropDef def)
        {
            var fallback = def.Type == "rich" ? 20000 : Schema.MaxText;
            return def.Max.HasValue ? (int)Math.Min(def.Max.Value, int.MaxValue) : fallback;
        }

        /// <summary>A template's value for a prop type; null for types that can't be bound.</summary>
        public static JToken Value(string type, string template, JObject fields, int max)
        {
            var exact = Exact.Match(template);
            var cell = exact.Success ? Cell(fields, exact.Groups[1].Value) : null;
            switch (type)
            {
                case "text":
                case "video":
                    return new JValue(Cut(Fill(template, fields, false), max));
                case "inline":
                    {
                        var text = Cut(Fill(template, fields, false), max);
                        return text.Length > 0 ? new JArray(new JObject { ["text"] = text }) : new JArray();
                    }
                case "rich":
                    {
                        if (cell?["rich"] is JArray rich) return rich.DeepClone();
                        var text = Cut(Fill(template, fields, false), max);
                        return text.Length > 0
                            ? new JArray(new JObject { ["type"] = "p", ["runs"] = new JArray(new JObject { ["text"] = text }) })
                            : new JArray();
                    }
                case "url":
                case "image":
                    if (exact.Success)
                    {
                        if (Js.TryString(cell?["url"], out var url)) return new JValue(url);
                        return new JValue(Js.TryString(cell?["text"], out var text) ? text : string.Empty);
                    }
                    return new JValue(Fill(template, fields, true));
                default:
                    return null;
            }
        }

        /// <summary>The template with every <c>{path}</c> replaced by that field's text (URL-encoded inside links).</summary>
        private static string Fill(string template, JObject fields, bool encode)
        {
            return Placeholder.Replace(template, m =>
            {
                var text = Js.TryString(Cell(fields, m.Groups[1].Value)?["text"], out var t) ? t : string.Empty;
                return encode ? EncodeComponent(text) : text;
            });
        }

        private static string Cut(string text, int max)
        {
            return text.Length > max ? text.Substring(0, max) : text;
        }

        private static JObject Cell(JObject fields, string path)
        {
            return fields.Property(path, StringComparison.Ordinal)?.Value as JObject;
        }

        /// <summary>
        /// <c>encodeURIComponent</c> with <c>! ' ( ) *</c> escaped too: everything but letters, digits and <c>- _ . ~</c>
        /// as UTF-8 %XX (what Uri.EscapeDataString does). Lone surrogates are dropped (encodeURIComponent would throw).
        /// </summary>
        internal static string EncodeComponent(string value)
        {
            var clean = LoneSurrogate.Replace(value, string.Empty);
            var sb = new StringBuilder(clean.Length);
            foreach (var b in Encoding.UTF8.GetBytes(clean))
            {
                var c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
