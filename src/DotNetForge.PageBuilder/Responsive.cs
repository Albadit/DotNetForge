using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder
{
    /// <summary>Where a node sits in its grid parent at one breakpoint.</summary>
    public sealed class GridPlacement
    {
        /// <summary>True when the node flows automatically (only its spans are known).</summary>
        public bool Auto { get; internal set; }

        public double Col { get; internal set; }

        public double Row { get; internal set; }

        public double ColSpan { get; internal set; }

        public double RowSpan { get; internal set; }

        internal GridPlacement Copy()
        {
            return (GridPlacement)MemberwiseClone();
        }
    }

    /// <summary>Position and size of a node in a free (absolute) container, in pixels.</summary>
    public sealed class FreePlacement
    {
        public double X { get; internal set; }

        public double Y { get; internal set; }

        public double W { get; internal set; }

        public double H { get; internal set; }
    }

    /// <summary>
    /// Resolving per-breakpoint values. Desktop-first: base everywhere, tablet
    /// overrides it below the tablet width, mobile overrides tablet below the
    /// mobile width. Mirrors core/responsive.ts.
    /// </summary>
    public static class Responsive
    {
        internal static string Key(Breakpoint bp)
        {
            return bp == Breakpoint.Base ? "base" : bp == Breakpoint.Tablet ? "tablet" : "mobile";
        }

        /// <summary>The values that apply at a breakpoint: base, then tablet, then mobile spread over each other.</summary>
        public static JObject Merged(JObject value, Breakpoint bp)
        {
            var output = new JObject();
            if (value == null) return output;
            Spread(output, value["base"]);
            if (bp != Breakpoint.Base) Spread(output, value["tablet"]);
            if (bp == Breakpoint.Mobile) Spread(output, value["mobile"]);
            return output;
        }

        /// <summary>The full layout of a container at a breakpoint, including its mode's defaults; always has "mode".</summary>
        public static JObject LayoutAt(PageNode node, Breakpoint bp)
        {
            var m = Merged(node.Layout, bp);
            Js.TryString(m["mode"], out var requested);
            var mode = requested == "grid" || requested == "free" ? requested : "stack";
            var output = (JObject)Schema.LayoutDefaults(mode).DeepClone();
            output["mode"] = mode;
            foreach (var p in m.Properties())
            {
                var def = Schema.Layout(p.Name);
                if (p.Name != "mode" && def?.Modes != null && def.Modes.Contains(mode)) output[p.Name] = p.Value.DeepClone();
            }
            return output;
        }

        public static JObject StyleAt(PageNode node, Breakpoint bp)
        {
            return Merged(node.Style, bp);
        }

        /// <summary>
        /// Where a node sits in its grid parent at a breakpoint.
        ///
        /// A breakpoint without its own placement inherits the larger one's when the
        /// grid has the same number of columns there; otherwise the node flows
        /// automatically, keeping its spans (capped at the column count). That gives
        /// every page a sensible tablet and mobile layout before anyone touches them.
        /// </summary>
        public static GridPlacement GridPlacementAt(PageNode node, PageNode parent, Breakpoint bp)
        {
            var place = node.Place ?? new JObject();
            var colsBase = ColumnsAt(parent, Breakpoint.Base);
            var placeBase = place["base"] as JObject ?? new JObject();
            var b = FromPartial(Get(placeBase, "col"), Get(placeBase, "row"), Get(placeBase, "colSpan"), Get(placeBase, "rowSpan"));
            GridPlacement effBase;
            if (colsBase > 0)
            {
                effBase = ClampTo(b, colsBase);
            }
            else
            {
                effBase = b.Copy();
                effBase.Auto = true;
            }
            if (bp == Breakpoint.Base) return effBase;

            var colsTablet = ColumnsAt(parent, Breakpoint.Tablet);
            var effTablet = Inherit(effBase, place["tablet"] as JObject, colsTablet == colsBase);
            effTablet = ClampTo(effTablet, colsTablet);
            if (bp == Breakpoint.Tablet) return effTablet;

            var colsMobile = ColumnsAt(parent, Breakpoint.Mobile);
            var effMobile = Inherit(effTablet, place["mobile"] as JObject, colsMobile == colsTablet);
            return ClampTo(effMobile, colsMobile);
        }

        public static FreePlacement FreePlacementAt(PageNode node, Breakpoint bp)
        {
            var m = Merged(node.Place, bp);
            return new FreePlacement
            {
                X = Get(m, "x") ?? 0,
                Y = Get(m, "y") ?? 0,
                W = Get(m, "w") ?? 240,
                H = Get(m, "h") ?? 120,
            };
        }

        /// <summary>
        /// The placement at a smaller breakpoint: its own values over the larger
        /// one's, the larger one's as is when the column count is unchanged, or
        /// automatic flow with the same spans.
        /// </summary>
        private static GridPlacement Inherit(GridPlacement larger, JObject own, bool sameColumns)
        {
            if (own != null)
            {
                // { ...toPartial(larger), ...own }: an automatic placement contributes only its spans.
                return FromPartial(
                    Get(own, "col") ?? (larger.Auto ? (double?)null : larger.Col),
                    Get(own, "row") ?? (larger.Auto ? (double?)null : larger.Row),
                    Get(own, "colSpan") ?? larger.ColSpan,
                    Get(own, "rowSpan") ?? larger.RowSpan);
            }
            if (sameColumns) return larger;
            var flow = larger.Copy();
            flow.Auto = true;
            flow.Col = 0;
            flow.Row = 0;
            return flow;
        }

        private static double ColumnsAt(PageNode parent, Breakpoint bp)
        {
            var l = LayoutAt(parent, bp);
            return (string)l["mode"] == "grid" ? Get(l, "columns") ?? 12 : 0;
        }

        private static GridPlacement FromPartial(double? col, double? row, double? colSpan, double? rowSpan)
        {
            var explicitCell = col.HasValue && row.HasValue;
            return new GridPlacement
            {
                Auto = !explicitCell,
                Col = explicitCell ? col.Value : 0,
                Row = explicitCell ? row.Value : 0,
                ColSpan = colSpan ?? 1,
                RowSpan = rowSpan ?? 1,
            };
        }

        private static GridPlacement ClampTo(GridPlacement g, double cols)
        {
            var c = Math.Max(1, cols);
            if (g.Auto)
            {
                var auto = g.Copy();
                auto.ColSpan = Math.Min(Math.Max(1, g.ColSpan), c);
                auto.RowSpan = Math.Max(1, g.RowSpan);
                return auto;
            }
            var col = Math.Min(Math.Max(1, g.Col), c);
            return new GridPlacement
            {
                Auto = false,
                Col = col,
                Row = Math.Max(1, g.Row),
                ColSpan = Math.Min(Math.Max(1, g.ColSpan), c - col + 1),
                RowSpan = Math.Max(1, g.RowSpan),
            };
        }

        /// <summary>A numeric value of a JSON object, or null when missing or not a number (<c>typeof === 'number'</c>).</summary>
        internal static double? Get(JObject o, string key)
        {
            return o != null && Js.TryNumber(o[key], out var d) ? d : (double?)null;
        }

        private static void Spread(JObject target, JToken source)
        {
            if (!(source is JObject o)) return;
            foreach (var p in o.Properties()) target[p.Name] = p.Value.DeepClone();
        }
    }
}
