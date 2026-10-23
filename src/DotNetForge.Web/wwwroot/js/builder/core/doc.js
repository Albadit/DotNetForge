import { componentDef, DEFAULT_BREAKPOINTS } from "./schema.js";
const ID_CHARS = "abcdefghijklmnopqrstuvwxyz0123456789";
function newId(taken) {
  const has = (id) => taken === void 0 ? false : typeof taken.has === "function" ? taken.has(id) : Object.prototype.hasOwnProperty.call(taken, id);
  const bytes = new Uint8Array(8);
  for (; ; ) {
    crypto.getRandomValues(bytes);
    let id = "";
    id += ID_CHARS[bytes[0] % 26];
    for (let i = 1; i < 8; i++) id += ID_CHARS[bytes[i] % 36];
    if (!has(id)) return id;
  }
}
function deepClone(value) {
  return value === void 0 ? value : JSON.parse(JSON.stringify(value));
}
function createNode(type, id, parent) {
  const def = componentDef(type);
  if (!def) throw new Error("Unknown component type: " + type);
  const props = {};
  for (const [key, prop] of Object.entries(def.props)) props[key] = deepClone(prop.default);
  const node = { id, type, parent, props };
  if (def.container) node.children = [];
  if (def.layout) node.layout = deepClone(def.layout);
  if (def.style) node.style = deepClone(def.style);
  if (def.place) node.place = { base: { colSpan: def.place.colSpan, rowSpan: def.place.rowSpan } };
  return node;
}
function createDocument() {
  const nodes = {};
  const rootId = newId(nodes);
  nodes[rootId] = createNode("page", rootId, null);
  const sectionId = newId(nodes);
  nodes[sectionId] = createNode("section", sectionId, rootId);
  nodes[rootId].children = [sectionId];
  return { schema: 1, root: rootId, nodes, settings: { breakpoints: { ...DEFAULT_BREAKPOINTS } } };
}
function ancestors(doc, id) {
  const out = [];
  let cur = doc.nodes[id]?.parent ?? null;
  let guard = 0;
  while (cur && guard++ < 1e3) {
    out.push(cur);
    cur = doc.nodes[cur]?.parent ?? null;
  }
  return out;
}
function isAncestor(doc, ancestor, id) {
  return ancestors(doc, id).includes(ancestor);
}
function depth(doc, id) {
  return ancestors(doc, id).length;
}
function subtree(doc, id) {
  const out = [];
  const stack = [id];
  while (stack.length) {
    const cur = stack.pop();
    const node = doc.nodes[cur];
    if (!node) continue;
    out.push(cur);
    const kids = node.children;
    if (kids) for (let i = kids.length - 1; i >= 0; i--) stack.push(kids[i]);
  }
  return out;
}
function walk(doc) {
  return subtree(doc, doc.root);
}
function indexInParent(doc, id) {
  const node = doc.nodes[id];
  if (!node?.parent) return -1;
  return doc.nodes[node.parent]?.children?.indexOf(id) ?? -1;
}
function topLevel(doc, ids) {
  const set = new Set(ids);
  return ids.filter((id) => doc.nodes[id] && !ancestors(doc, id).some((a) => set.has(a)));
}
function cloneSubtree(source, id, parent, taken) {
  const map = /* @__PURE__ */ new Map();
  const ids = subtree(source, id);
  for (const old of ids) {
    const fresh = newId(taken);
    taken.add(fresh);
    map.set(old, fresh);
  }
  const nodes = ids.map((old) => {
    const copy = deepClone(source.nodes[old]);
    copy.id = map.get(old);
    copy.parent = old === id ? parent : map.get(copy.parent);
    if (copy.children) copy.children = copy.children.map((c) => map.get(c)).filter(Boolean);
    return copy;
  });
  return { root: map.get(id), nodes };
}
function placedModuleIds(doc) {
  const out = /* @__PURE__ */ new Set();
  for (const n of Object.values(doc.nodes)) if (n?.type === "module" && typeof n.props.moduleId === "number" && n.props.moduleId > 0) out.add(n.props.moduleId);
  return out;
}
function removedModuleIds(doc) {
  const placed = placedModuleIds(doc);
  return new Set((doc.settings.removedModules ?? []).filter((id) => !placed.has(id)));
}
function displayName(node) {
  if (node.name) return node.name;
  const def = componentDef(node.type);
  if (node.type === "heading" || node.type === "button") {
    const t = node.type === "heading" ? Array.isArray(node.props.text) ? node.props.text.map((r) => r.text).join("") : "" : String(node.props.label ?? "");
    if (t.trim()) return t.trim().slice(0, 40);
  }
  return def?.label ?? node.type;
}
export {
  ancestors,
  cloneSubtree,
  createDocument,
  createNode,
  deepClone,
  depth,
  displayName,
  indexInParent,
  isAncestor,
  newId,
  placedModuleIds,
  removedModuleIds,
  subtree,
  topLevel,
  walk
};
