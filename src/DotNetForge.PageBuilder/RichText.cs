using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>A run of text with marks. Links are a mark too, so text is never HTML.</summary>
    public sealed class Run
    {
        public string Text { get; internal set; }

        public bool Bold { get; internal set; }

        public bool Italic { get; internal set; }

        public bool Underline { get; internal set; }

        public bool Strike { get; internal set; }

        public bool Code { get; internal set; }

        /// <summary>A validated link target, or null.</summary>
        public string Href { get; internal set; }

        public bool NewTab { get; internal set; }

        /// <summary>The stored shape: only the marks that are set (b, i, u, s, code, href, newTab).</summary>
        public JObject ToJson()
        {
            var o = new JObject { ["text"] = Text };
            if (Bold) o["b"] = true;
            if (Italic) o["i"] = true;
            if (Underline) o["u"] = true;
            if (Strike) o["s"] = true;
            if (Code) o["code"] = true;
            if (Href != null) o["href"] = Href;
            if (NewTab) o["newTab"] = true;
            return o;
        }

        internal bool SameMarks(Run other)
        {
            return Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Strike == other.Strike && Code == other.Code
                && (Href ?? string.Empty) == (other.Href ?? string.Empty) && NewTab == other.NewTab;
        }
    }

    /// <summary>A paragraph (p), heading (h3, h4) or quote holding runs, or a list (ul, ol) holding items of runs.</summary>
    public sealed class Block
    {
        internal Block(string type, IReadOnlyList<Run> runs, IReadOnlyList<IReadOnlyList<Run>> items)
        {
            Type = type;
            Runs = runs;
            Items = items;
        }

        public string Type { get; }

        /// <summary>The runs of p/h3/h4/quote; null for lists.</summary>
        public IReadOnlyList<Run> Runs { get; }

        /// <summary>The items of ul/ol; null for the other blocks.</summary>
        public IReadOnlyList<IReadOnlyList<Run>> Items { get; }

        public bool IsList => Items != null;

        public JObject ToJson()
        {
            var o = new JObject { ["type"] = Type };
            if (IsList) o["items"] = new JArray(Items.Select(i => RichText.ToJson(i)));
            else o["runs"] = RichText.ToJson(Runs);
            return o;
        }
    }

    /// <summary>
    /// Rich text is stored as data, never as HTML: blocks of runs, each run a
    /// string with marks. The renderers build the markup and escape every
    /// character of text, so editors cannot inject markup and the server needs no
    /// HTML sanitizer. Mirrors core/richtext.ts.
    /// </summary>
    public static class RichText
    {
        /// <summary>Cleans runs: unknown keys dropped, empty runs removed, neighbours with equal marks merged.</summary>
        public static IReadOnlyList<Run> NormalizeRuns(JToken input, int maxRuns = 2000)
        {
            var output = new List<Run>();
            if (!(input is JArray array)) return output;
            // Merged text is collected in a builder: appending strings one run at a time
            // copies the text again for every run (quadratic for hostile input).
            System.Text.StringBuilder merged = null;
            foreach (var raw in array.Take(maxRuns))
            {
                if (!(raw is JObject r)) continue;
                if (!Js.TryString(r["text"], out var text) || text.Length == 0) continue;
                var run = new Run
                {
                    Text = text.Replace("\r\n", "\n").Replace('\r', '\n'),
                    Bold = Js.IsTrue(r["b"]),
                    Italic = Js.IsTrue(r["i"]),
                    Underline = Js.IsTrue(r["u"]),
                    Strike = Js.IsTrue(r["s"]),
                    Code = Js.IsTrue(r["code"]),
                };
                if (Js.IsString(r["href"]))
                {
                    var href = Values.LinkUrl(r["href"]);
                    if (!string.IsNullOrEmpty(href))
                    {
                        run.Href = href;
                        if (Js.IsTrue(r["newTab"])) run.NewTab = true;
                    }
                }
                var last = output.Count > 0 ? output[output.Count - 1] : null;
                if (last != null && last.SameMarks(run))
                {
                    if (merged == null) merged = new System.Text.StringBuilder(last.Text);
                    merged.Append(run.Text);
                }
                else
                {
                    if (merged != null) last.Text = merged.ToString();
                    merged = null;
                    output.Add(run);
                }
            }
            if (merged != null) output[output.Count - 1].Text = merged.ToString();
            return output;
        }

        public static IReadOnlyList<Block> NormalizeBlocks(JToken input, int maxBlocks = 500)
        {
            var output = new List<Block>();
            if (!(input is JArray array)) return output;
            foreach (var raw in array.Take(maxBlocks))
            {
                if (!(raw is JObject b)) continue;
                Js.TryString(b["type"], out var type);
                if (type == "ul" || type == "ol")
                {
                    var items = b["items"] is JArray list ? list.Take(200).Select(i => NormalizeRuns(i)).ToList() : new List<IReadOnlyList<Run>>();
                    if (items.Count > 0) output.Add(new Block(type, null, items));
                }
                else if (type == "p" || type == "h3" || type == "h4" || type == "quote")
                {
                    output.Add(new Block(type, NormalizeRuns(b["runs"]), null));
                }
            }
            return output;
        }

        public static string RunsText(IEnumerable<Run> runs)
        {
            return runs == null ? string.Empty : string.Concat(runs.Select(r => r.Text));
        }

        /// <summary>The number of characters (UTF-16 units, as JavaScript counts) of all text.</summary>
        public static int TextLength(IEnumerable<Block> blocks)
        {
            var n = 0;
            foreach (var b in blocks)
            {
                if (b.IsList)
                {
                    foreach (var item in b.Items) n += RunsText(item).Length;
                }
                else
                {
                    n += RunsText(b.Runs).Length;
                }
            }
            return n;
        }

        public static JArray ToJson(IEnumerable<Run> runs)
        {
            return new JArray(runs.Select(r => r.ToJson()));
        }

        public static JArray ToJson(IEnumerable<Block> blocks)
        {
            return new JArray(blocks.Select(b => b.ToJson()));
        }
    }
}
