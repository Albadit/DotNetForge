// The inside of a content list (.docs/features/structured-content.md#content-lists): its item template once per entry,
// or a table, plus the pager. The entries come from ctx.data (keyed by node id: { items: [{ id, fields }], total, page,
// pageCount }), fetched by the editor or resolved by the server; the renderers never query anything. Mirrors
// Rendering/Collections.cs.
import { componentDef } from "../core/schema.js";
import * as V from "../core/values.js";
import { escapeHtml } from "./format.js";

const isObj = (v) => !!v && typeof v === "object" && !Array.isArray(v);

function choice(node, prop) {
  const def = componentDef(node.type).props[prop];
  const value = node.props[prop];
  return typeof value === "string" && def.options?.includes(value) ? value : String(def.default);
}

function str(node, prop) {
  const v = node.props[prop];
  return typeof v === "string" ? v : "";
}

function emptyHtml(node) {
  const text = str(node, "emptyText");
  return text ? '<p class="pe-collection-empty">' + escapeHtml(text) + "</p>" : "";
}

function cellOf(fields, path) {
  if (!isObj(fields) || !Object.prototype.hasOwnProperty.call(fields, path)) return null;
  return isObj(fields[path]) ? fields[path] : null;
}

function tableHtml(node, items, ctx) {
  if (items.length === 0 && ctx.mode !== "canvas") return emptyHtml(node);
  const raw = Array.isArray(node.props.columns) ? node.props.columns.filter(isObj) : [];
  const columns = raw.map((c) => ({ field: typeof c.field === "string" ? c.field : "", label: typeof c.label === "string" ? c.label : "" })).filter((c) => c.field);
  if (columns.length === 0) columns.push({ field: "$title", label: "Title" });
  let html = '<div class="pe-table-wrap"><table class="pe-table"><thead><tr>';
  for (const c of columns) html += '<th scope="col">' + escapeHtml(c.label || c.field) + "</th>";
  html += "</tr></thead><tbody>";
  for (const item of items) {
    const fields = isObj(item.fields) ? item.fields : {};
    const url = V.linkUrl(cellOf(fields, "$url")?.url);
    html += "<tr>";
    columns.forEach((c, i) => {
      const cell = cellOf(fields, c.field);
      const text = escapeHtml(cell && typeof cell.text === "string" ? cell.text : "");
      html += "<td>" + (i === 0 && url ? '<a href="' + escapeHtml(url) + '">' + text + "</a>" : text) + "</td>";
    });
    html += "</tr>";
  }
  return html + "</tbody></table></div>";
}

function pagerHtml(node, data) {
  if (node.props.paginate !== true || !isObj(data)) return "";
  const count = V.int(data.pageCount, 1, 1e5) ?? 1;
  const page = V.int(data.page, 1, 1e5) ?? 1;
  if (count <= 1) return "";
  const key = "pg-" + node.id;
  const prev = page > 1 ? '<a class="pe-pager-prev" href="?' + key + "=" + (page - 1) + '" rel="prev">' + escapeHtml(str(node, "prevLabel")) + "</a>" : '<span class="pe-pager-prev"></span>';
  const next = page < count ? '<a class="pe-pager-next" href="?' + key + "=" + (page + 1) + '" rel="next">' + escapeHtml(str(node, "nextLabel")) + "</a>" : '<span class="pe-pager-next"></span>';
  return '<nav class="pe-pager" aria-label="Pages">' + prev + '<span class="pe-pager-status">' + page + " / " + count + "</span>" + next + "</nav>";
}

/** The data the host resolved for a content list node, or undefined. */
function collectionData(ctx, node) {
  const data = isObj(ctx.data) && Object.prototype.hasOwnProperty.call(ctx.data, node.id) ? ctx.data[node.id] : void 0;
  return isObj(data) ? data : void 0;
}

/**
 * The list's markup. `itemHtml(item, index)` renders the template for one entry (item null when there is no data and
 * the editor shows the bare template).
 */
function collectionInner(node, ctx, itemHtml) {
  const data = collectionData(ctx, node);
  const items = Array.isArray(data?.items) ? data.items.filter(isObj) : [];
  if (choice(node, "view") === "table") return tableHtml(node, items, ctx) + pagerHtml(node, data);
  let html = "";
  if (items.length === 0) {
    if (ctx.mode !== "canvas") return emptyHtml(node);
    html = itemHtml(null, 0);
  } else {
    items.forEach((item, i) => {
      html += itemHtml(item, i);
    });
  }
  return '<div class="pe-in">' + html + "</div>" + pagerHtml(node, data);
}

export {
  collectionData,
  collectionInner
};
