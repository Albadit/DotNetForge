using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>
    /// The inside of a content list (.docs/features/structured-content.md#content-lists): its item template once per
    /// entry, or a table, plus the pager. The entries come from <see cref="RenderContext.Data"/> (keyed by node id,
    /// <c>{ items: [{ id, fields }], total, page, pageCount }</c>), resolved by the host before rendering; the
    /// renderers never query anything. Mirrors renderer/collection.js.
    /// </summary>
    public static class Collections
    {
        /// <summary>
        /// The list's markup. <paramref name="itemHtml"/> renders the template for one entry (null when there is no data
        /// and the editor shows the bare template) and its position.
        /// </summary>
        public static string Inner(PageNode node, RenderContext ctx, Func<JObject, int, string> itemHtml)
        {
            var data = ctx.Data?.Property(node.Id, StringComparison.Ordinal)?.Value as JObject;
            var items = (data?["items"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            if (Choice(node, "view") == "table") return TableHtml(node, items, ctx) + PagerHtml(node, data);
            var sb = new StringBuilder();
            if (items.Count == 0)
            {
                if (ctx.Mode != RenderMode.Canvas) return EmptyHtml(node);
                sb.Append(itemHtml(null, 0));
            }
            else
            {
                for (var i = 0; i < items.Count; i++) sb.Append(itemHtml(items[i], i));
            }
            return "<div class=\"pe-in\">" + sb + "</div>" + PagerHtml(node, data);
        }

        private static string EmptyHtml(PageNode node)
        {
            var text = Str(node, "emptyText");
            return text.Length > 0 ? "<p class=\"pe-collection-empty\">" + Format.EscapeHtml(text) + "</p>" : string.Empty;
        }

        private static string TableHtml(PageNode node, IReadOnlyList<JObject> items, RenderContext ctx)
        {
            if (items.Count == 0 && ctx.Mode != RenderMode.Canvas) return EmptyHtml(node);
            var columns = (node.Props["columns"] as JArray)?.OfType<JObject>()
                .Select(c => new KeyValuePair<string, string>(Js.TryString(c["field"], out var f) ? f : string.Empty, Js.TryString(c["label"], out var l) ? l : string.Empty))
                .Where(c => c.Key.Length > 0)
                .ToList() ?? new List<KeyValuePair<string, string>>();
            if (columns.Count == 0) columns.Add(new KeyValuePair<string, string>("$title", "Title"));
            var sb = new StringBuilder("<div class=\"pe-table-wrap\"><table class=\"pe-table\"><thead><tr>");
            foreach (var c in columns) sb.Append("<th scope=\"col\">").Append(Format.EscapeHtml(c.Value.Length > 0 ? c.Value : c.Key)).Append("</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var item in items)
            {
                var fields = item["fields"] as JObject ?? new JObject();
                var url = Values.LinkUrl((fields.Property("$url", StringComparison.Ordinal)?.Value as JObject)?["url"]);
                sb.Append("<tr>");
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = fields.Property(columns[i].Key, StringComparison.Ordinal)?.Value as JObject;
                    var text = Format.EscapeHtml(Js.TryString(cell?["text"], out var t) ? t : string.Empty);
                    sb.Append("<td>");
                    if (i == 0 && !string.IsNullOrEmpty(url)) sb.Append("<a href=\"").Append(Format.EscapeHtml(url)).Append("\">").Append(text).Append("</a>");
                    else sb.Append(text);
                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }
            return sb.Append("</tbody></table></div>").ToString();
        }

        private static string PagerHtml(PageNode node, JObject data)
        {
            if (!Js.IsTrue(node.Props["paginate"]) || data == null) return string.Empty;
            var count = Values.Int(data["pageCount"], 1, 100000) ?? 1;
            var page = Values.Int(data["page"], 1, 100000) ?? 1;
            if (count <= 1) return string.Empty;
            var key = "pg-" + node.Id;
            var prev = page > 1
                ? "<a class=\"pe-pager-prev\" href=\"?" + key + "=" + Format.Int(page - 1) + "\" rel=\"prev\">" + Format.EscapeHtml(Str(node, "prevLabel")) + "</a>"
                : "<span class=\"pe-pager-prev\"></span>";
            var next = page < count
                ? "<a class=\"pe-pager-next\" href=\"?" + key + "=" + Format.Int(page + 1) + "\" rel=\"next\">" + Format.EscapeHtml(Str(node, "nextLabel")) + "</a>"
                : "<span class=\"pe-pager-next\"></span>";
            return "<nav class=\"pe-pager\" aria-label=\"Pages\">" + prev + "<span class=\"pe-pager-status\">" + Format.Int(page) + " / " + Format.Int(count) + "</span>" + next + "</nav>";
        }

        private static string Choice(PageNode node, string prop)
        {
            var def = Schema.Component(node.Type).Prop(prop);
            return Js.TryString(node.Props[prop], out var value) && def.Options != null && def.Options.Contains(value) ? value : def.DefaultString;
        }

        private static string Str(PageNode node, string prop)
        {
            return Js.TryString(node.Props[prop], out var value) ? value : string.Empty;
        }
    }
}
