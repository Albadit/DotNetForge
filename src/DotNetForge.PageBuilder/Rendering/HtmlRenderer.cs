using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetForge.PageBuilder.Rendering
{
    public enum RenderMode
    {
        /// <summary>What visitors get.</summary>
        View,

        /// <summary>The editor canvas: data-pe-id hooks, empty-state placeholders, nothing filtered out.</summary>
        Canvas,
    }

    public sealed class RenderContext
    {
        public RenderMode Mode { get; set; }

        /// <summary>Audience/role check for the current viewer (view mode); null = everything is visible.</summary>
        public Func<PageNode, bool> IsVisible { get; set; }

        /// <summary>Whether a DNN module is available on this page for this viewer (view mode); null = all are.</summary>
        public Func<int, bool> HasModule { get; set; }
    }

    /// <summary>
    /// Document to HTML. Mirrors renderer/html.ts.
    ///
    /// Every node becomes one element with the classes <c>pe-node pe-t-{type}
    /// pe-n-{id}</c>, device-hiding classes and the user's classes. Module nodes
    /// contain a <c>&lt;!--pe:module:{id}--&gt;</c> marker where the server writes
    /// the module's own output (see <see cref="Segments"/>).
    /// </summary>
    public static class HtmlRenderer
    {
        private static readonly string[] Devices = { "desktop", "tablet", "mobile" };
        private static readonly Dictionary<string, string> DeviceClass = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["desktop"] = "pe-hide-d",
            ["tablet"] = "pe-hide-t",
            ["mobile"] = "pe-hide-m",
        };

        public static string Render(PageDocument doc, RenderContext ctx)
        {
            return RenderNode(doc, doc.Root, ctx);
        }

        /// <summary>The element for one node, children included. Empty when the node is skipped.</summary>
        public static string RenderNode(PageDocument doc, string id, RenderContext ctx)
        {
            var sb = new StringBuilder(4096);
            AppendNode(sb, doc, id, ctx);
            return sb.ToString();
        }

        /// <summary>The devices a node is hidden on (those its visibility does not list).</summary>
        public static IReadOnlyList<string> HiddenOnDevices(PageNode node)
        {
            var shown = node.Visibility?.Devices;
            if (shown == null) return new string[0];
            return Devices.Where(d => !shown.Contains(d)).ToList();
        }

        /// <summary>Whether a node produces no output for this context (and so neither do its children).</summary>
        public static bool IsSkipped(PageNode node, RenderContext ctx)
        {
            if (node.Hidden) return true;
            if (ctx.Mode == RenderMode.View)
            {
                if (HiddenOnDevices(node).Count == 3) return true;
                if (ctx.IsVisible != null && !ctx.IsVisible(node)) return true;
                if (node.Type == "module" && ctx.HasModule != null)
                {
                    var moduleId = Js.TryNumber(node.Props["moduleId"], out var d) ? d : 0;
                    if (!ctx.HasModule(ToModuleId(moduleId))) return true;
                }
            }
            return false;
        }

        public static string ClassesOf(PageDocument doc, PageNode node)
        {
            var comp = ComponentRegistry.Get(node.Type);
            var parts = new List<string>();
            if (node.Id == doc.Root) parts.Add("pe-root");
            parts.Add("pe-node");
            parts.Add("pe-t-" + node.Type);
            parts.Add("pe-n-" + node.Id);
            if (comp != null) parts.AddRange(comp.Classes(node) ?? Enumerable.Empty<string>());
            foreach (var d in HiddenOnDevices(node)) parts.Add(DeviceClass[d]);
            var user = Values.ClassNames(node.ClassName ?? string.Empty);
            if (!string.IsNullOrEmpty(user)) parts.Add(user);
            return string.Join(" ", parts);
        }

        private static void AppendNode(StringBuilder sb, PageDocument doc, string id, RenderContext ctx)
        {
            var node = doc.Node(id);
            if (node == null || IsSkipped(node, ctx)) return;
            var comp = ComponentRegistry.Get(node.Type);
            if (comp == null) return;

            var tag = comp.Tag(node);
            sb.Append('<').Append(tag).Append(" class=\"").Append(Format.EscapeHtml(ClassesOf(doc, node))).Append('"');
            var attributes = comp.Attributes(node, ctx);
            if (attributes != null)
            {
                foreach (var a in attributes) sb.Append(' ').Append(a.Key).Append("=\"").Append(Format.EscapeHtml(a.Value)).Append('"');
            }
            if (ctx.Mode == RenderMode.Canvas) sb.Append(" data-pe-id=\"").Append(node.Id).Append('"');
            sb.Append('>');
            if (comp.VoidElement) return;

            var children = new StringBuilder();
            if (node.Children != null) foreach (var child in node.Children) AppendNode(children, doc, child, ctx);
            sb.Append(comp.Content(node, ctx, children.ToString())).Append("</").Append(tag).Append('>');
        }

        /// <summary>Normalized module ids are whole numbers in int range; anything else maps to 0 (no module).</summary>
        private static int ToModuleId(double value)
        {
            return Js.IsInteger(value) && value >= int.MinValue && value <= int.MaxValue ? (int)value : 0;
        }
    }
}
