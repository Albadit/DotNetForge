import { subtree, topLevel } from "../core/doc.js";
import {
  duplicateNodes,
  gridItemsAt,
  insertSubtree,
  moveGridItem,
  moveNodes,
  removeNodes,
  setFlag,
  setPlacement,
  unwrapNode,
  wrapNodes
} from "../core/commands.js";
import { canContain } from "../core/schema.js";
import { freePlacementAt, layoutAt } from "../core/responsive.js";
import { normalizeDocument } from "../core/validate.js";
import { align, flip, flowOf, guessFlow, moveToEnd, setFlow } from "./layout-ops.js";
const CLIPBOARD_KEY = "pe-clipboard";
class Actions {
  constructor(session, canvas, inline) {
    this.session = session;
    this.canvas = canvas;
    this.inline = inline;
  }
  session;
  canvas;
  inline;
  get store() {
    return this.session.store;
  }
  get sel() {
    return this.store.selection;
  }
  get canLayout() {
    return this.session.caps.value.editLayout;
  }
  undo() {
    this.inline.commit();
    this.store.undo();
  }
  redo() {
    this.inline.commit();
    this.store.redo();
  }
  remove() {
    if (!this.canLayout || !this.sel.length) return;
    const locked = this.sel.filter((id) => this.store.doc.nodes[id]?.locked);
    if (locked.length) this.session.toast("Locked elements are not deleted. Unlock them first.", "info");
    removeNodes(this.store, this.sel);
  }
  duplicate() {
    if (this.canLayout && this.sel.length) duplicateNodes(this.store, this.sel);
  }
  copy() {
    const doc = this.store.doc;
    const roots = topLevel(doc, this.sel).filter((id) => id !== doc.root);
    if (!roots.length) return;
    const nodes = {};
    for (const r of roots) for (const id of subtree(doc, r)) nodes[id] = doc.nodes[id];
    const data = { peClipboard: 1, roots, nodes };
    try {
      localStorage.setItem(CLIPBOARD_KEY, JSON.stringify(data));
    } catch {
    }
    memoryClipboard = data;
    this.session.toast(roots.length === 1 ? "Copied." : `Copied ${roots.length} elements.`);
  }
  cut() {
    if (!this.canLayout) return;
    this.copy();
    removeNodes(this.store, this.sel);
  }
  paste() {
    if (!this.canLayout) return;
    let data = memoryClipboard;
    try {
      const raw = localStorage.getItem(CLIPBOARD_KEY);
      if (raw) data = JSON.parse(raw);
    } catch {
    }
    if (!data || data.peClipboard !== 1) {
      this.session.toast("Nothing to paste. Copy elements first (Ctrl+C).");
      return;
    }
    const doc = this.store.doc;
    const created = [];
    for (const root of data.roots) {
      const nodes = Object.values(this.subtreeOf(data, root));
      if (!nodes.length) continue;
      const valid = this.validateSubtree(nodes);
      if (!valid) continue;
      const target = this.pasteTarget(valid[0].type);
      if (!target) {
        this.session.toast(`A ${valid[0].type} cannot be pasted here.`, "error");
        continue;
      }
      const id = insertSubtree(this.store, valid, { ...target, bp: this.session.breakpoint.value }, "Paste");
      if (id) created.push(id);
    }
    if (created.length) this.store.select(created);
    void doc;
  }
  subtreeOf(data, root) {
    const out = {};
    const stack = [root];
    while (stack.length) {
      const id = stack.pop();
      const n = data.nodes[id];
      if (!n || out[id]) continue;
      out[id] = n;
      for (const c of n.children ?? []) stack.push(c);
    }
    const rootNode = out[root];
    delete out[root];
    return { [root]: rootNode, ...out };
  }
  validateSubtree(nodes) {
    const root = nodes[0];
    const wrapperType = root.type === "section" ? "page" : "container";
    try {
      const fake = { pagex: { id: "pagex", type: "page", parent: null, props: {}, children: wrapperType === "page" ? [root.id] : ["wrapx"] } };
      if (wrapperType === "container") fake.wrapx = { id: "wrapx", type: "section", parent: "pagex", props: {}, children: [root.id] };
      for (const n of nodes) fake[n.id] = { ...n, parent: n.id === root.id ? wrapperType === "page" ? "pagex" : "wrapx" : n.parent };
      const doc = normalizeDocument({ schema: 1, root: "pagex", nodes: fake, settings: this.store.doc.settings });
      return subtree(doc, root.id).map((id) => doc.nodes[id]);
    } catch {
      this.session.toast("The copied content could not be pasted here.", "error");
      return null;
    }
  }
  pasteTarget(type) {
    const doc = this.store.doc;
    const sel = this.sel[this.sel.length - 1];
    const node = sel ? doc.nodes[sel] : null;
    if (node?.children && canContain(node.type, type) && !node.locked) return { parent: node.id };
    if (node?.parent && canContain(doc.nodes[node.parent].type, type)) {
      return { parent: node.parent, index: doc.nodes[node.parent].children.indexOf(node.id) + 1 };
    }
    const page = doc.nodes[doc.root];
    if (canContain(page.type, type)) return { parent: page.id };
    const last = [...page.children ?? []].reverse().find((id) => canContain(doc.nodes[id].type, type));
    return last ? { parent: last } : null;
  }
  selectParent() {
    const id = this.sel[0];
    const parent = id ? this.store.doc.nodes[id]?.parent : null;
    if (parent) this.store.select([parent]);
    else this.store.select([]);
  }
  selectChild() {
    const id = this.sel[0];
    const kids = id ? this.store.doc.nodes[id]?.children : null;
    if (kids?.length) this.store.select([kids[0]]);
    else if (id && this.inline.canEdit(id)) this.inline.start(id);
  }
  selectSibling(delta) {
    const id = this.sel[0];
    const n = id ? this.store.doc.nodes[id] : null;
    if (!n?.parent) return;
    const sibs = this.store.doc.nodes[n.parent].children ?? [];
    const i = sibs.indexOf(id);
    const next = sibs[(i + delta + sibs.length) % sibs.length];
    if (next) {
      this.store.select([next]);
      this.canvas.scrollIntoView(next);
    }
  }
  selectAllSiblings() {
    const id = this.sel[0];
    const doc = this.store.doc;
    const parent = id ? doc.nodes[id]?.parent : doc.root;
    const kids = parent ? doc.nodes[parent]?.children : null;
    if (kids) this.store.select([...kids]);
  }
  wrap() {
    if (this.canLayout && this.sel.length && !wrapNodes(this.store, this.sel)) this.session.toast("These elements cannot be wrapped together (they must share a parent).", "error");
  }
  unwrap() {
    const id = this.sel[0];
    if (this.canLayout && id && !unwrapNode(this.store, id)) this.session.toast("Only containers can be removed this way.", "error");
  }
  toggleHidden() {
    if (!this.canLayout || !this.sel.length) return;
    const on = !this.sel.every((id) => this.store.doc.nodes[id]?.hidden);
    setFlag(this.store, this.sel, "hidden", on);
  }
  toggleLocked() {
    if (!this.canLayout || !this.sel.length) return;
    const on = !this.sel.every((id) => this.store.doc.nodes[id]?.locked);
    setFlag(this.store, this.sel, "locked", on);
  }
  /** Shift+A: auto layout on a container, or wraps the selection in a new auto layout container. */
  /** Shift+H / Shift+V: mirror the selection. */
  flip(horizontal) {
    if (this.canLayout && this.sel.length) flip(this.session, this.sel, horizontal);
  }
  addAutoLayout() {
    if (!this.canLayout || !this.sel.length) return;
    const doc = this.store.doc;
    const first = doc.nodes[this.sel[0]];
    if (this.sel.length === 1 && first?.children) {
      if (flowOf(this.session, first.id).startsWith("stack")) {
        this.session.toast("This container already has auto layout.");
        return;
      }
      setFlow(this.session, this.canvas, first.id, guessFlow(this.session, first.id));
      return;
    }
    this.wrap();
  }
  /** Alt+Shift+A: free positioning, every child staying where it is. */
  removeAutoLayout() {
    const id = this.sel[0];
    const n = id ? this.store.doc.nodes[id] : null;
    if (!this.canLayout || !n?.children || n.locked) return;
    setFlow(this.session, this.canvas, n.id, "free");
  }
  /** Ctrl+Shift+] / Ctrl+Shift+[ : bring to front / send to back. */
  moveToEnd(end) {
    if (this.canLayout && this.sel[0]) moveToEnd(this.session, this.sel[0], end);
  }
  /** Alt+A/D/W/S/H/V. */
  align(edge) {
    if (this.canLayout && this.sel.length) align(this.session, this.canvas, this.sel, edge);
  }
  /** Ctrl+R: rename the selected layer in the layers panel. */
  rename() {
    const id = this.sel[0];
    if (!this.canLayout || !id || id === this.store.doc.root) return;
    this.session.leftTab.set("layers");
    this.session.renaming.set(id);
  }
  /** Moves the selection one place earlier/later among its siblings. */
  reorder(delta) {
    const id = this.sel[0];
    const doc = this.store.doc;
    const n = id ? doc.nodes[id] : null;
    if (!this.canLayout || !n?.parent || n.locked) return;
    const sibs = doc.nodes[n.parent].children;
    const i = sibs.indexOf(id);
    const j = Math.min(sibs.length - 1, Math.max(0, i + delta));
    if (i === j) return;
    moveNodes(this.store, [id], { parent: n.parent, index: delta > 0 ? j + 1 : j, bp: this.session.breakpoint.value });
  }
  /** Arrow keys: one cell in a grid, 1 px (10 with Shift) in a free container, reorder in a stack. */
  nudge(dx, dy, big) {
    const id = this.sel[0];
    const doc = this.store.doc;
    const n = id ? doc.nodes[id] : null;
    if (!this.canLayout || !n?.parent || n.locked) return;
    const bp = this.session.breakpoint.value;
    const parent = doc.nodes[n.parent];
    const layout = layoutAt(parent, bp);
    if (layout.mode === "grid") {
      const it = gridItemsAt(doc, parent.id, bp).find((i) => i.id === id);
      if (!it) return;
      const step = big ? 2 : 1;
      moveGridItem(this.store, id, bp, { col: Math.max(1, it.col + dx * step), row: Math.max(1, it.row + dy * step), colSpan: it.colSpan, rowSpan: it.rowSpan });
    } else if (layout.mode === "free") {
      const f = freePlacementAt(n, bp);
      const step = big ? 10 : 1;
      setPlacement(this.store, id, bp, { x: f.x + dx * step, y: f.y + dy * step }, "nudge");
    } else {
      const vertical = layout.direction !== "row";
      const d = vertical ? dy : dx;
      if (d) this.reorder(d);
    }
  }
  /** Arrow keys with Ctrl: grow/shrink a grid item by one cell. */
  resizeBy(dw, dh) {
    const id = this.sel[0];
    const doc = this.store.doc;
    const n = id ? doc.nodes[id] : null;
    if (!this.canLayout || !n?.parent || n.locked) return;
    const bp = this.session.breakpoint.value;
    const parent = doc.nodes[n.parent];
    if (layoutAt(parent, bp).mode !== "grid") return;
    const it = gridItemsAt(doc, parent.id, bp).find((i) => i.id === id);
    if (!it) return;
    moveGridItem(this.store, id, bp, { col: it.col, row: it.row, colSpan: Math.max(1, it.colSpan + dw), rowSpan: Math.max(1, it.rowSpan + dh) });
  }
  zoom(delta) {
    const s = this.session;
    if (delta === "fit") {
      s.zoomMode.set("fit");
      return;
    }
    s.zoomMode.set("manual");
    if (delta === 1) s.zoom.set(1);
    else s.zoom.set(Math.min(4, Math.max(0.1, Math.round(s.zoom.value * (delta > 0 ? 1.25 : 0.8) * 100) / 100)));
  }
  preview() {
    void this.session.save().then(() => window.open(this.session.config.previewUrl, "_blank", "noopener"));
  }
  escape() {
    if (this.canvas.cancelOperation()) return;
    if (this.inline.active) {
      this.inline.commit();
      return;
    }
    if (this.session.dialog.value) {
      this.session.dialog.set(null);
      return;
    }
    if (this.sel.length) this.selectParent();
  }
}
let memoryClipboard = null;
export {
  Actions
};
