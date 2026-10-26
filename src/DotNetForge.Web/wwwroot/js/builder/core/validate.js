import { BREAKPOINT_LIMITS, canContain, componentDef, DEFAULT_BREAKPOINTS, LAYOUT, LIMITS, PLACE, PROP_LIMITS, SIZE_MODES, STYLE } from "./schema.js";
import { normalizeBlocks, normalizeRuns, textLength } from "./richtext.js";
import * as V from "./values.js";
class DocumentError extends Error {
}
const BPS = ["base", "tablet", "mobile"];
const DEVICES = ["desktop", "tablet", "mobile"];
const isObj = (v) => !!v && typeof v === "object" && !Array.isArray(v);
const own = (table, key) => Object.prototype.hasOwnProperty.call(table, key) ? table[key] : void 0;
const cloneDefault = (v) => v === void 0 ? null : JSON.parse(JSON.stringify(v));
/** The conditions a content list filter may use (FilterOperators in DocumentValidator.cs). */
const FILTER_OPS = ["eq", "neq", "contains", "gt", "gte", "lt", "lte", "empty", "notEmpty"];
/** Prop types a node inside a content list can take from an entry (PropDef.BindableTypes). */
const BINDABLE_TYPES = /* @__PURE__ */ new Set(["text", "inline", "rich", "url", "image", "video"]);
/** Longest binding template of one prop (DocumentValidator.MaxBinding). */
const MAX_BINDING = 300;
function normalizeProp(def, value) {
  switch (def.type) {
    case "text": {
      const t = V.text(value, def.max ?? PROP_LIMITS.text);
      return t ?? def.default;
    }
    case "inline": {
      const runs = normalizeRuns(value, PROP_LIMITS.runs);
      return runs.reduce((n, r) => n + r.text.length, 0) > (def.max ?? PROP_LIMITS.text) ? def.default : runs;
    }
    case "rich": {
      const blocks = normalizeBlocks(value, PROP_LIMITS.blocks);
      return textLength(blocks) > (def.max ?? 2e4) ? def.default : blocks;
    }
    case "url":
      return V.linkUrl(value) ?? def.default;
    case "image":
      return V.imageUrl(value) ?? def.default;
    case "video":
      return typeof value === "string" && (value === "" || V.videoEmbed(value)) ? value.trim() : def.default;
    case "slug":
      return V.slug(value, def.max ?? 60) ?? def.default;
    case "enum":
      return typeof value === "string" && def.options?.includes(value) ? value : def.default;
    case "bool":
      return typeof value === "boolean" ? value : def.default;
    case "int":
      return V.int(value, def.min ?? 0, def.max ?? 2147483647) ?? def.default;
    case "module":
      return V.int(value, 0, 2147483647) ?? def.default;
    case "links": {
      if (!Array.isArray(value)) return def.default;
      return value.slice(0, def.maxItems ?? 50).filter(isObj).map((i) => {
        const item = {
          label: V.text(i.label, 200) ?? "",
          href: V.linkUrl(i.href) ?? "#",
          newTab: i.newTab === true
        };
        const icon = V.iconName(i.icon);
        if (icon) item.icon = icon;
        return item;
      });
    }
    case "icon":
      return V.iconName(value) ?? def.default;
    case "ref":
      return typeof value === "string" && (value === "" || V.reference(value)) ? value === "" ? "" : V.reference(value) : def.default;
    case "field":
      return V.fieldPath(value) ?? def.default;
    case "filters": {
      if (!Array.isArray(value)) return def.default;
      return value.slice(0, def.maxItems ?? 10).filter(isObj).map((f) => ({ field: V.fieldPath(f.field), op: FILTER_OPS.includes(f.op) ? f.op : "eq", value: V.text(f.value, 200) ?? "" })).filter((f) => f.field);
    }
    case "columns": {
      if (!Array.isArray(value)) return def.default;
      return value.slice(0, def.maxItems ?? 12).filter(isObj).map((c) => ({ field: V.fieldPath(c.field), label: V.text(c.label, 100) ?? "" })).filter((c) => c.field);
    }
    case "comments": {
      if (!Array.isArray(value)) return def.default;
      return value.slice(0, def.maxItems ?? 100).filter(isObj).map((c) => ({
        author: V.text(c.author, 120) ?? "",
        avatar: V.imageUrl(c.avatar) ?? "",
        meta: V.text(c.meta, 60) ?? "",
        text: V.text(c.text, 2e3) ?? ""
      }));
    }
    default:
      return def.default;
  }
}
function normalizeStyleValue(key, value) {
  const def = own(STYLE, key);
  if (!def) return void 0;
  const min = def.min ?? -1e4, max = def.max ?? 1e4;
  switch (def.type) {
    case "size": {
      if (!isObj(value) || typeof value.mode !== "string" || !SIZE_MODES.includes(value.mode)) return void 0;
      if (value.mode === "fixed" || value.mode === "percent") {
        const n = V.num(value.value, 0, value.mode === "percent" ? 100 : 1e4);
        return n === null ? void 0 : { mode: value.mode, value: n };
      }
      return { mode: value.mode };
    }
    case "length":
    case "number":
    case "angle":
      return V.num(value, min, max) ?? void 0;
    case "int":
      return V.int(value, min, max) ?? void 0;
    case "enum":
      return typeof value === "string" && def.options?.includes(value) ? value : void 0;
    case "box":
    case "corners": {
      if (!Array.isArray(value) || value.length !== 4) return void 0;
      const out = value.map((v) => V.num(v, min, max));
      return out.some((v) => v === null) ? void 0 : out;
    }
    case "color":
      return V.color(value) ?? void 0;
    case "image":
      return V.imageUrl(value) || void 0;
    case "font":
      return typeof value === "string" && ["inherit", "sans", "serif", "mono"].includes(value) ? value : void 0;
    case "shadow": {
      if (typeof value === "string") return ["none", "sm", "md", "lg", "xl"].includes(value) ? value : void 0;
      if (!isObj(value)) return void 0;
      const x = V.num(value.x, -200, 200), y = V.num(value.y, -200, 200);
      const blur = V.num(value.blur, 0, 400), spread = V.num(value.spread, -200, 200);
      const color = V.color(value.color);
      if (x === null || y === null || blur === null || spread === null || !color) return void 0;
      const s = { x, y, blur, spread, color };
      if (value.inset === true) s.inset = true;
      return s;
    }
    default:
      return void 0;
  }
}
function normalizeLayoutValue(key, value) {
  const def = own(LAYOUT, key);
  if (!def) return void 0;
  switch (def.type) {
    case "enum":
      return typeof value === "string" && def.options?.includes(value) ? value : void 0;
    case "bool":
      return typeof value === "boolean" ? value : void 0;
    case "int":
      return V.int(value, def.min ?? 0, def.max ?? 1e4) ?? void 0;
    case "length":
      return V.num(value, def.min ?? 0, def.max ?? 1e4) ?? void 0;
    case "tracks": {
      if (!Array.isArray(value) || value.length === 0) return void 0;
      return value.slice(0, def.max ?? 100).map((t) => V.track(t) ?? def.fallback ?? "auto");
    }
    default:
      return void 0;
  }
}
function normalizePlaceValue(key, value) {
  const def = own(PLACE, key);
  return def ? V.int(value, def.min, def.max) ?? void 0 : void 0;
}
/** A content list's item template is repeated, so it is never pinned to a cell or position. */
function normalizeItemPlaceValue(key, value) {
  return key === "colSpan" || key === "rowSpan" || key === "w" || key === "h" ? normalizePlaceValue(key, value) : void 0;
}
function normalizeResponsive(value, fn) {
  if (!isObj(value)) return void 0;
  const out = {};
  for (const bp of BPS) {
    const src = value[bp];
    if (!isObj(src)) continue;
    const clean = {};
    for (const [k, v] of Object.entries(src)) {
      const n = fn(k, v);
      if (n !== void 0) clean[k] = n;
    }
    if (Object.keys(clean).length) out[bp] = clean;
  }
  return Object.keys(out).length ? out : void 0;
}
function intList(value, max) {
  if (!Array.isArray(value)) return void 0;
  const out = [...new Set(value.filter((v) => Number.isInteger(v) && v >= -1 && v <= 2147483647))].slice(0, max);
  return out;
}
function normalizeNode(raw, id) {
  const type = raw.type;
  if (typeof type !== "string") throw new DocumentError(`Node ${id} has no type.`);
  const def = componentDef(type);
  if (!def) throw new DocumentError(`Node ${id} has an unknown type "${type}".`);
  const node = { id, type, parent: typeof raw.parent === "string" ? raw.parent : null, props: {} };
  if (def.container) {
    if (raw.children !== void 0 && !Array.isArray(raw.children)) throw new DocumentError(`Node ${id}: children must be a list.`);
    node.children = (raw.children ?? []).map((c) => {
      const cid = V.nodeId(c);
      if (!cid) throw new DocumentError(`Node ${id} has an invalid child id.`);
      return cid;
    });
  } else if (Array.isArray(raw.children) && raw.children.length) {
    throw new DocumentError(`Node ${id} (${type}) cannot have children.`);
  }
  const props = isObj(raw.props) ? raw.props : {};
  for (const [key, pdef] of Object.entries(def.props)) {
    node.props[key] = Object.prototype.hasOwnProperty.call(props, key) ? normalizeProp(pdef, props[key]) : cloneDefault(pdef.default);
  }
  if (typeof raw.name === "string") {
    const name = V.text(raw.name, LIMITS.name);
    if (name) node.name = name;
  }
  if (def.container) {
    const layout = normalizeResponsive(raw.layout, normalizeLayoutValue);
    if (layout) node.layout = layout;
  }
  const place = normalizeResponsive(raw.place, type === "item" ? normalizeItemPlaceValue : normalizePlaceValue);
  if (place) node.place = place;
  const style = normalizeResponsive(raw.style, normalizeStyleValue);
  if (style) node.style = style;
  if (raw.hidden === true) node.hidden = true;
  if (raw.locked === true) node.locked = true;
  if (isObj(raw.visibility)) {
    const vis = {};
    if (Array.isArray(raw.visibility.devices)) vis.devices = DEVICES.filter((d) => raw.visibility.devices.includes(d));
    const a = raw.visibility.audience;
    if (a === "anonymous" || a === "authenticated") vis.audience = a;
    const roles = intList(raw.visibility.roles, 50);
    if (roles && roles.length) vis.roles = roles;
    if (Object.keys(vis).length) node.visibility = vis;
  }
  if (typeof raw.className === "string") {
    const c = V.classNames(raw.className, LIMITS.className);
    if (c) node.className = c;
  }
  if (isObj(raw.vars)) {
    const vars = {};
    for (const [k, v] of Object.entries(raw.vars).slice(0, LIMITS.vars)) {
      const name = V.varName(k), value = V.varValue(v);
      if (name && value) vars[name] = value;
    }
    if (Object.keys(vars).length) node.vars = vars;
  }
  if (isObj(raw.bind)) {
    // In schema prop order, as DocumentValidator writes them.
    const bind = {};
    for (const [key, pdef] of Object.entries(def.props)) {
      if (!BINDABLE_TYPES.has(pdef.type) || !Object.prototype.hasOwnProperty.call(raw.bind, key)) continue;
      const template = V.text(raw.bind[key], MAX_BINDING);
      if (template && template.includes("{")) bind[key] = template;
    }
    if (Object.keys(bind).length) node.bind = bind;
  }
  if (isObj(raw.permissions)) {
    const roles = intList(raw.permissions.editRoles, 50);
    if (roles && roles.length) node.permissions = { editRoles: roles };
  }
  return node;
}
function normalizeDocument(input) {
  if (!isObj(input)) throw new DocumentError("The document is not an object.");
  if (input.schema !== 1) throw new DocumentError("Unsupported document schema.");
  const root = V.nodeId(input.root);
  if (!root) throw new DocumentError("The document has no valid root.");
  if (!isObj(input.nodes)) throw new DocumentError("The document has no nodes.");
  const rawNodes = input.nodes;
  const ids = Object.keys(rawNodes);
  if (ids.length > LIMITS.nodes) throw new DocumentError(`A page can have at most ${LIMITS.nodes} elements.`);
  const nodes = {};
  for (const key of ids) {
    const id = V.nodeId(key);
    const raw = rawNodes[key];
    if (!id || !isObj(raw) || raw.id !== key) throw new DocumentError(`Invalid node id "${key}".`);
    nodes[id] = normalizeNode(raw, id);
  }
  const rootNode = nodes[root];
  if (!rootNode || rootNode.type !== "page") throw new DocumentError("The root must be a page.");
  rootNode.parent = null;
  const seen = /* @__PURE__ */ new Set([root]);
  const stack = [{ id: root, depth: 0 }];
  while (stack.length) {
    const { id, depth } = stack.pop();
    if (depth > LIMITS.depth) throw new DocumentError("The page is nested too deeply.");
    const node = nodes[id];
    for (const child of node.children ?? []) {
      const c = nodes[child];
      if (!c) throw new DocumentError(`Node ${id} lists a missing child ${child}.`);
      if (seen.has(child)) throw new DocumentError(`Node ${child} appears more than once.`);
      if (c.parent !== id) throw new DocumentError(`Node ${child} does not point back to its parent.`);
      if (!canContain(node.type, c.type)) throw new DocumentError(`A ${c.type} cannot be placed in a ${node.type}.`);
      seen.add(child);
      stack.push({ id: child, depth: depth + 1 });
    }
  }
  for (const id of Object.keys(nodes)) if (!seen.has(id)) delete nodes[id];
  const bp = isObj(input.settings) && isObj(input.settings.breakpoints) ? input.settings.breakpoints : {};
  let tablet = V.int(bp.tablet, BREAKPOINT_LIMITS.min, BREAKPOINT_LIMITS.max) ?? DEFAULT_BREAKPOINTS.tablet;
  let mobile = V.int(bp.mobile, BREAKPOINT_LIMITS.min, BREAKPOINT_LIMITS.max) ?? DEFAULT_BREAKPOINTS.mobile;
  if (mobile >= tablet) {
    tablet = DEFAULT_BREAKPOINTS.tablet;
    mobile = DEFAULT_BREAKPOINTS.mobile;
  }
  const settings = { breakpoints: { tablet, mobile } };
  const removed = removedModulesOf(input.settings, nodes);
  if (removed.length) settings.removedModules = removed;
  return { schema: 1, root, nodes, settings };
}
const MAX_REMOVED_MODULES = 1e3;
function removedModulesOf(settings, nodes) {
  if (!isObj(settings) || !Array.isArray(settings.removedModules)) return [];
  const placed = /* @__PURE__ */ new Set();
  for (const n of Object.values(nodes)) if (n.type === "module" && typeof n.props.moduleId === "number") placed.add(n.props.moduleId);
  const ids = /* @__PURE__ */ new Set();
  for (const item of settings.removedModules) {
    const id = V.int(item, 1, 2147483647);
    if (id !== null && !placed.has(id)) ids.add(id);
    if (ids.size >= MAX_REMOVED_MODULES) break;
  }
  return [...ids].sort((a, b) => a - b);
}
export {
  BINDABLE_TYPES,
  DocumentError,
  FILTER_OPS,
  normalizeDocument,
  normalizeProp
};
