using System.Text;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>The stylesheet every custom page shares (schema/base.css, embedded). The editor loads the same file.</summary>
    public static class BaseCss
    {
        public static string Text => Schema.BaseCssText;
    }

    /// <summary>
    /// Document to CSS. One block per node: its base rules, then the declarations
    /// that differ at tablet, then at mobile. The editor replaces exactly the
    /// blocks of the nodes an edit touched; the server joins the same blocks for
    /// visitors. Mirrors renderer/css.ts and is checked byte for byte by the
    /// golden fixtures.
    /// </summary>
    public static class CssBuilder
    {
        private static readonly Dictionary<string, string> Align = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["start"] = "flex-start",
            ["center"] = "center",
            ["end"] = "flex-end",
            ["stretch"] = "stretch",
            ["baseline"] = "baseline",
        };

        private static readonly Dictionary<string, string> Justify = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["start"] = "flex-start",
            ["center"] = "center",
            ["end"] = "flex-end",
            ["between"] = "space-between",
            ["around"] = "space-around",
            ["evenly"] = "space-evenly",
        };

        /// <summary>
        /// The selector of a node's rules. A node rendered as a link (&lt;a&gt;) has the element in
        /// its selector, so its own text color beats link colors (base.css, skins).
        /// </summary>
        public static string SelectorOf(PageDocument doc, string id)
        {
            if (id == doc.Root) return ".pe-root.pe-n-" + id;
            var node = doc.Node(id);
            var renderer = node != null ? ComponentRegistry.Get(node.Type) : null;
            return renderer != null && renderer.Tag(node) == "a" ? ".pe-root a.pe-n-" + id : ".pe-root .pe-n-" + id;
        }

        /// <summary>The CSS of one node (empty when it has none).</summary>
        public static string NodeCss(PageDocument doc, string id)
        {
            var node = doc.Node(id);
            if (node == null) return string.Empty;
            var sel = SelectorOf(doc, id);
            var innerSel = sel + ">.pe-in";
            var b = DeclsAt(doc, node, Breakpoint.Base);
            var t = DeclsAt(doc, node, Breakpoint.Tablet);
            var m = DeclsAt(doc, node, Breakpoint.Mobile);
            var lines = new List<string>(3);
            var baseRules = Rule(sel, b.Self.List) + Rule(innerSel, b.Inner.List);
            if (baseRules.Length > 0) lines.Add(baseRules);
            var tabletRules = Rule(sel, Diff(b.Self.List, t.Self.List)) + Rule(innerSel, Diff(b.Inner.List, t.Inner.List));
            if (tabletRules.Length > 0) lines.Add(TabletQuery(doc) + "{" + tabletRules + "}");
            var mobileRules = Rule(sel, Diff(t.Self.List, m.Self.List)) + Rule(innerSel, Diff(t.Inner.List, m.Inner.List));
            if (mobileRules.Length > 0) lines.Add(MobileQuery(doc) + "{" + mobileRules + "}");
            return string.Join("\n", lines);
        }

        /// <summary>Rules that hide nodes per device; nodes carry pe-hide-d / -t / -m classes.</summary>
        public static string VisibilityCss(PageDocument doc)
        {
            return string.Join("\n",
                "@media (min-width:" + Format.Fmt(doc.TabletWidth) + "px){.pe-root .pe-hide-d{display:none!important}}",
                "@media (min-width:" + Format.Fmt(doc.MobileWidth) + "px) and (max-width:" + Format.Fmt(doc.TabletWidth - 0.02) + "px){.pe-root .pe-hide-t{display:none!important}}",
                "@media (max-width:" + Format.Fmt(doc.MobileWidth - 0.02) + "px){.pe-root .pe-hide-m{display:none!important}}");
        }

        /// <summary>The whole document's CSS: every rendered node in document order, then the visibility rules.</summary>
        public static string DocumentCss(PageDocument doc)
        {
            var parts = new List<string>();
            foreach (var id in WalkRendered(doc))
            {
                var css = NodeCss(doc, id);
                if (css.Length > 0) parts.Add(css);
            }
            parts.Add(VisibilityCss(doc));
            return string.Join("\n", parts);
        }

        /// <summary>Document order, skipping hidden nodes and everything below them.</summary>
        public static IReadOnlyList<string> WalkRendered(PageDocument doc)
        {
            var output = new List<string>();
            var stack = new Stack<string>();
            stack.Push(doc.Root);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                var node = doc.Node(id);
                if (node == null || node.Hidden) continue;
                output.Add(id);
                var kids = node.Children;
                if (kids != null) for (var i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
            }
            return output;
        }

        internal static string TabletQuery(PageDocument doc)
        {
            return "@media (max-width:" + Format.Fmt(doc.TabletWidth - 0.02) + "px)";
        }

        internal static string MobileQuery(PageDocument doc)
        {
            return "@media (max-width:" + Format.Fmt(doc.MobileWidth - 0.02) + "px)";
        }

        private static NodeDecls DeclsAt(PageDocument doc, PageNode node, Breakpoint bp)
        {
            var result = new NodeDecls();
            var comp = ComponentRegistry.Get(node.Type);
            var parent = node.Parent != null ? doc.Node(node.Parent) : null;
            var parentLayout = parent != null ? Responsive.LayoutAt(parent, bp) : null;
            var container = comp != null && Schema.Component(node.Type)?.Container == true;
            var targetsInner = comp != null && comp.LayoutTarget == LayoutTarget.Inner;

            if (container) LayoutDecls(targetsInner ? result.Inner : result.Self, Responsive.LayoutAt(node, bp));
            PlacementDecls(result.Self, node, parent, parentLayout, bp);
            StyleDecls(result.Self, Responsive.StyleAt(node, bp), parentLayout);
            var hooks = comp?.Css(node);
            if (hooks != null)
            {
                foreach (var d in hooks.Self) result.Self.Set(d.Key, d.Value);
                foreach (var d in hooks.Inner) result.Inner.Set(d.Key, d.Value);
            }
            foreach (var v in node.Vars)
            {
                var name = Values.VarName(v.Key);
                var value = Values.VarValue(v.Value);
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value)) result.Self.Set(name, value);
            }
            return result;
        }

        /// <summary>The track list of a grid axis; mirrors trackCss in renderer/css.ts.</summary>
        private static string Tracks(int count, JArray sizes, string missing, string hug)
        {
            var parts = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var size = sizes != null && i < sizes.Count && sizes[i].Type == JTokenType.String ? (string)sizes[i] : missing;
                parts.Add(size.EndsWith("fr", StringComparison.Ordinal) ? "minmax(0," + size + ")" : size == "auto" ? hug : size);
            }
            return string.Join(" ", parts);
        }

        private static void LayoutDecls(Decls output, JObject l)
        {
            var mode = Str(l, "mode");
            if (mode == "grid")
            {
                output.Set("display", "grid");
                // Equal columns unless sizes are set (Figma's Fill / Fixed / Hug per track).
                var cols = (int)(Responsive.Get(l, "columns") ?? 12);
                var colSizes = l["colSizes"] as JArray;
                output.Set("grid-template-columns", colSizes != null ? Tracks(cols, colSizes, "1fr", "auto") : "repeat(" + Js.NumberToString(cols) + ",minmax(0,1fr))");
                var rowHeight = Format.Px(Responsive.Get(l, "rowHeight") ?? 40);
                var autoRow = Str(l, "rowMode") == "fixed" ? rowHeight : "minmax(" + rowHeight + ",auto)";
                // A row count makes the rows explicit; more rows than that (and Auto) use the automatic row size.
                var rows = (int)(Responsive.Get(l, "rows") ?? 0);
                if (rows > 0) output.Set("grid-template-rows", Tracks(rows, l["rowSizes"] as JArray, "auto", autoRow));
                output.Set("grid-auto-rows", autoRow);
                output.Set("column-gap", Format.Px(Responsive.Get(l, "colGap") ?? 0));
                output.Set("row-gap", Format.Px(Responsive.Get(l, "rowGap") ?? 0));
            }
            else if (mode == "free")
            {
                output.Set("display", "block");
                output.Set("position", "relative");
                output.Set("height", Format.Px(Responsive.Get(l, "height") ?? 400));
            }
            else
            {
                output.Set("display", "flex");
                output.Set("flex-direction", Str(l, "direction") == "row" ? "row" : "column");
                output.Set("gap", Format.Px(Responsive.Get(l, "gap") ?? 0));
                output.Set("align-items", Align.TryGetValue(Str(l, "align") ?? "stretch", out var align) ? align : "stretch");
                output.Set("justify-content", Justify.TryGetValue(Str(l, "justify") ?? "start", out var justify) ? justify : "flex-start");
                output.Set("flex-wrap", Truthy(l["wrap"]) ? "wrap" : "nowrap");
            }
        }

        private static void SizeDecls(Decls output, string axis, JToken size, JObject parent)
        {
            if (!(size is JObject s)) return;
            var mode = Str(s, "mode");
            var value = Responsive.Get(s, "value");
            if (mode == "fixed" && value.HasValue && value >= 0 && value <= 10000) output.Set(axis, Format.Px(value.Value));
            else if (mode == "percent" && value.HasValue && value >= 0 && value <= 100) output.Set(axis, Format.Fmt(value.Value) + "%");
            else if (mode == "hug") output.Set(axis, "fit-content");

            if (parent != null && Str(parent, "mode") == "stack")
            {
                var main = Str(parent, "direction") == "row" ? "width" : "height";
                if (axis == main)
                {
                    if (mode == "fill") output.Set("flex", "1 1 0px");
                    else if (mode == "hug" || mode == "fixed") output.Set("flex", "0 0 auto");
                    else if (mode == "percent") output.Set("flex", "0 1 auto");
                }
                else if (mode == "fill")
                {
                    output.Set("align-self", "stretch");
                }
            }
        }

        private static string ShadowValue(JToken value)
        {
            if (Js.TryString(value, out var preset)) return Schema.Shadows.TryGetValue(preset, out var css) ? css : null;
            if (!(value is JObject s)) return null;
            var x = Values.Num(s["x"], -200, 200);
            var y = Values.Num(s["y"], -200, 200);
            var blur = Values.Num(s["blur"], 0, 400);
            var spread = Values.Num(s["spread"], -200, 200);
            var color = Values.Color(s["color"]);
            if (x == null || y == null || blur == null || spread == null || string.IsNullOrEmpty(color)) return null;
            return (Js.IsTrue(s["inset"]) ? "inset " : string.Empty) + Format.Px(x.Value) + " " + Format.Px(y.Value) + " " + Format.Px(blur.Value) + " " + Format.Px(spread.Value) + " " + color;
        }

        private static string BoxValue(JToken value, double min, double max)
        {
            if (!(value is JArray list) || list.Count != 4) return null;
            var parts = new List<string>(4);
            foreach (var v in list)
            {
                var n = Values.Num(v, min, max);
                if (n == null) return null;
                parts.Add(Format.Px(n.Value));
            }
            return string.Join(" ", parts);
        }

        /// <summary>Paint settings the renderer applies itself (they have no CSS property of their own).</summary>
        private static readonly HashSet<string> PaintFlags = new HashSet<string>(StringComparer.Ordinal) { "borderSides", "strokeAlign", "strokeHidden", "fillHidden" };
        private static readonly HashSet<string> FillKeys = new HashSet<string>(StringComparer.Ordinal) { "background", "bgImage" };
        private static readonly HashSet<string> StrokeKeys = new HashSet<string>(StringComparer.Ordinal) { "borderWidth", "borderStyle", "borderColor" };

        /// <summary>True when the layer fills its stack parent's cross axis (width in a column, height in a row).</summary>
        private static bool FillsCrossAxis(JObject style, JObject parent)
        {
            if (parent == null || Str(parent, "mode") != "stack") return false;
            return style[Str(parent, "direction") == "row" ? "height" : "width"] is JObject size && Str(size, "mode") == "fill";
        }

        private static void StyleDecls(Decls output, JObject style, JObject parent)
        {
            var freeChild = parent != null && Str(parent, "mode") == "free";
            // Figma's paints: a hidden fill or stroke writes nothing; a stroke outside or centered on the
            // edge is an outline (it follows the corners and takes no room); per-side weights are borders.
            var fillHidden = Str(style, "fillHidden") == "hidden";
            var strokeHidden = Str(style, "strokeHidden") == "hidden";
            var sidesDef = Schema.Style("borderSides");
            var sidesToken = style["borderSides"];
            var sides = sidesToken == null || sidesToken.Type == JTokenType.Null ? null : BoxValue(sidesToken, sidesDef.Min ?? 0, sidesDef.Max ?? 40);
            var align = Str(style, "strokeAlign");
            var outline = sides == null && (align == "outside" || align == "center");
            foreach (var def in Schema.StyleProperties)
            {
                var value = style[def.Key];
                if (value == null || value.Type == JTokenType.Null) continue;
                if (PaintFlags.Contains(def.Key) || (fillHidden && FillKeys.Contains(def.Key)) || (strokeHidden && StrokeKeys.Contains(def.Key))) continue;
                var css = outline && StrokeKeys.Contains(def.Key) ? ReplaceFirst(def.Css ?? string.Empty, "border-", "outline-") : def.Css ?? string.Empty;
                switch (def.Type)
                {
                    case "size":
                        if (!freeChild) SizeDecls(output, def.Key, value, parent);
                        break;
                    case "length":
                        {
                            var n = Values.Num(value, def.Min ?? -10000, def.Max ?? 10000);
                            if (n.HasValue)
                            {
                                output.Set(css, Format.Px(n.Value));
                                if (def.Key == "borderWidth" && style["borderStyle"] == null) output.Set(outline ? "outline-style" : "border-style", "solid");
                                if (def.Key == "borderWidth" && outline && align == "center") output.Set("outline-offset", Format.Px(-n.Value / 2));
                            }
                            break;
                        }
                    case "number":
                        {
                            var n = Values.Num(value, def.Min ?? -10000, def.Max ?? 10000);
                            if (n.HasValue) output.Set(css, Format.Fmt(n.Value));
                            break;
                        }
                    case "int":
                        {
                            var n = Values.Int(value, def.Min ?? -10000, def.Max ?? 10000);
                            if (n.HasValue) output.Set(css, Format.Int(n.Value));
                            break;
                        }
                    case "angle":
                        {
                            var n = Values.Num(value, def.Min ?? -360, def.Max ?? 360);
                            if (n.HasValue) output.Set(css, Format.Fmt(n.Value) + "deg");
                            break;
                        }
                    case "enum":
                        // Fill on a stack's cross axis is align-self: stretch (SizeDecls); an alignment must not undo it.
                        if (def.Key == "alignSelf" && FillsCrossAxis(style, parent)) break;
                        if (Js.TryString(value, out var option) && def.Options != null && def.Options.Contains(option)) output.Set(css, option);
                        break;
                    case "box":
                    case "corners":
                        {
                            var box = BoxValue(value, def.Min ?? 0, def.Max ?? 1000);
                            if (!string.IsNullOrEmpty(box)) output.Set(css, box);
                            break;
                        }
                    case "color":
                        {
                            var color = Values.Color(value);
                            if (!string.IsNullOrEmpty(color)) output.Set(css, color);
                            break;
                        }
                    case "image":
                        {
                            var url = Values.ImageUrl(value);
                            if (!string.IsNullOrEmpty(url)) output.Set(css, Format.CssUrl(url));
                            break;
                        }
                    case "shadow":
                        {
                            var shadow = ShadowValue(value);
                            if (!string.IsNullOrEmpty(shadow)) output.Set(css, shadow);
                            break;
                        }
                    case "font":
                        if (Js.TryString(value, out var font) && Schema.Fonts.TryGetValue(font, out var stack)) output.Set(css, stack);
                        break;
                }
            }
            if (sides != null && !strokeHidden)
            {
                output.Set("border-width", sides);
                if (style["borderStyle"] == null) output.Set("border-style", "solid");
            }
        }

        private static string ReplaceFirst(string text, string find, string with)
        {
            var i = text.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? text : text.Substring(0, i) + with + text.Substring(i + find.Length);
        }

        private static void PlacementDecls(Decls output, PageNode node, PageNode parent, JObject parentLayout, Breakpoint bp)
        {
            if (parent == null || parentLayout == null) return;
            var mode = Str(parentLayout, "mode");
            if (mode == "grid")
            {
                var g = Responsive.GridPlacementAt(node, parent, bp);
                if (g.Auto)
                {
                    output.Set("grid-column", "span " + Js.NumberToString(g.ColSpan));
                    output.Set("grid-row", "span " + Js.NumberToString(g.RowSpan));
                }
                else
                {
                    output.Set("grid-column", Js.NumberToString(g.Col) + " / span " + Js.NumberToString(g.ColSpan));
                    output.Set("grid-row", Js.NumberToString(g.Row) + " / span " + Js.NumberToString(g.RowSpan));
                }
            }
            else if (mode == "free")
            {
                var f = Responsive.FreePlacementAt(node, bp);
                output.Set("position", "absolute");
                output.Set("left", Format.Px(f.X));
                output.Set("top", Format.Px(f.Y));
                output.Set("width", Format.Px(f.W));
                output.Set("height", Format.Px(f.H));
            }
        }

        /// <summary>
        /// The declarations to write at a smaller breakpoint: those whose value
        /// changed, then a reset for those it no longer has.
        /// </summary>
        private static List<KeyValuePair<string, string>> Diff(List<KeyValuePair<string, string>> previous, List<KeyValuePair<string, string>> current)
        {
            var before = previous.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            var now = new HashSet<string>(current.Select(d => d.Key), StringComparer.Ordinal);
            var output = new List<KeyValuePair<string, string>>();
            foreach (var d in current)
            {
                if (!before.TryGetValue(d.Key, out var old) || old != d.Value) output.Add(d);
            }
            foreach (var d in previous)
            {
                if (!now.Contains(d.Key)) output.Add(new KeyValuePair<string, string>(d.Key, ResetValue(d.Key)));
            }
            return output;
        }

        /// <summary>What a declaration falls back to when a smaller breakpoint drops it.</summary>
        private static string ResetValue(string property)
        {
            return property == "min-width" ? "0" : "unset";
        }

        private static string Rule(string selector, List<KeyValuePair<string, string>> decls)
        {
            if (decls.Count == 0) return string.Empty;
            var sb = new StringBuilder(selector).Append('{');
            for (var i = 0; i < decls.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(decls[i].Key).Append(':').Append(decls[i].Value);
            }
            return sb.Append('}').ToString();
        }

        private static string Str(JObject o, string key)
        {
            return Js.TryString(o[key], out var s) ? s : null;
        }

        /// <summary>JavaScript truthiness of a JSON value.</summary>
        private static bool Truthy(JToken value)
        {
            if (value == null) return false;
            switch (value.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return false;
                case JTokenType.Boolean:
                    return (bool)value;
                case JTokenType.String:
                    return ((string)value).Length > 0;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Js.TryNumber(value, out var d) && d != 0 && !double.IsNaN(d);
                default:
                    return true;
            }
        }

        /// <summary>An ordered declaration list where setting an existing property replaces its value in place.</summary>
        private sealed class Decls
        {
            public List<KeyValuePair<string, string>> List { get; } = new List<KeyValuePair<string, string>>();

            public void Set(string property, string value)
            {
                for (var i = 0; i < List.Count; i++)
                {
                    if (List[i].Key == property)
                    {
                        List[i] = new KeyValuePair<string, string>(property, value);
                        return;
                    }
                }
                List.Add(new KeyValuePair<string, string>(property, value));
            }
        }

        private sealed class NodeDecls
        {
            public Decls Self { get; } = new Decls();

            public Decls Inner { get; } = new Decls();
        }
    }
}
