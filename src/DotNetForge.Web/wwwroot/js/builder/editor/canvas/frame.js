import "../../components/builtins.js";
import { getComponent } from "../../components/registry.js";
import { removedModuleIds } from "../../core/doc.js";
import { nodeCss, visibilityCss } from "../../renderer/css.js";
import { classesOf, isSkipped, itemContext, renderNode } from "../../renderer/html.js";
import { bindNode } from "../../renderer/binding.js";
import { collectionInner } from "../../renderer/collection.js";
import { escapeHtml } from "../../renderer/format.js";
const CTX = { mode: "canvas" };
const KIDS_MARKER = "<template data-pe-kids></template>";
const MODULE_CLASS = /\bDnnModule-(\d+)\b/;
// Editor-only canvas styles (placeholders for empty things) and the shared base styles are stylesheets the canvas
// document links (/builder/base.css, /builder/canvas.css). Node CSS is written into constructable stylesheets
// (document.adoptedStyleSheets): no <style> elements, so the Content-Security-Policy needs no 'unsafe-inline'.
class CanvasFrame {
  constructor(win, getDoc, getData = () => void 0) {
    this.win = win;
    this.getDoc = getDoc;
    this.getData = getData;
  }
  win;
  getDoc;
  /** The entries of the content lists (ctx.data), from editor/content.js. */
  getData;
  elements = /* @__PURE__ */ new Map();
  modules = /* @__PURE__ */ new Map();
  origins = /* @__PURE__ */ new Map();
  styles = /* @__PURE__ */ new Map();
  visibilityStyle = null;
  mount = null;
  /** For each module id, the node that shows it (the first rendered one in document order). */
  primary = /* @__PURE__ */ new Map();
  /** Nodes whose elements were created during the current patch (their CSS must be written). */
  fresh = /* @__PURE__ */ new Set();
  stash = null;
  get document() {
    return this.win.document;
  }
  /** Prepares the iframe document. Returns an error text when the page has no mount point. */
  init() {
    const d = this.document;
    this.mount = d.getElementById("pe-canvas-mount");
    if (!this.mount) return "The page did not load in canvas mode. Is the Page Editor installed and are you still signed in?";
    this.stash = d.createElement("div");
    this.stash.id = "pe-module-stash";
    this.stash.hidden = true;
    d.body.appendChild(this.stash);
    for (const el of Array.from(d.querySelectorAll(".DnnModule"))) {
      const m = MODULE_CLASS.exec(el.className);
      if (!m) continue;
      const id = Number(m[1]);
      if (this.modules.has(id)) continue;
      const marker = d.createComment("pe-origin:" + id);
      el.before(marker);
      this.modules.set(id, el);
      this.origins.set(id, marker);
    }
    this.renderAll();
    return null;
  }
  hasModule(moduleId) {
    return this.modules.has(moduleId);
  }
  /** Full render: used on load, document replacement and breakpoint settings changes. */
  renderAll() {
    if (!this.mount) return;
    const doc = this.getDoc();
    this.releaseAllModules();
    this.elements.clear();
    this.primary = this.primarySlots();
    this.mount.innerHTML = renderNode(doc, doc.root, this.ctx());
    this.index(this.mount);
    this.attachModules(this.mount);
    this.styles.clear();
    this.adopt();
    for (const id of Object.keys(doc.nodes)) this.writeCss(id);
    this.writeVisibilityCss();
  }
  /** Applies a change set: only the touched nodes are rebuilt. */
  patch(change) {
    if (!this.mount) return;
    if (change.source === "load" || change.settings) {
      this.renderAll();
      return;
    }
    const doc = this.getDoc();
    const before = this.primary;
    this.primary = this.primarySlots();
    const nodes = new Set(change.nodes);
    // A change inside a content list's item template also rebuilds the list: its repeated copies show the template.
    for (const id of change.nodes) {
      const list = listAncestor(doc, id);
      if (list) nodes.add(list);
    }
    for (const [mid, id] of this.primary) {
      const old = before.get(mid);
      if (old !== id) {
        nodes.add(id);
        if (old && doc.nodes[old]) nodes.add(old);
      }
    }
    const touched = /* @__PURE__ */ new Set();
    this.fresh.clear();
    const ids = [...nodes].sort((a, b) => depthOf(doc, a) - depthOf(doc, b));
    const done = /* @__PURE__ */ new Set();
    for (const id of ids) {
      if (done.has(id)) continue;
      const node = doc.nodes[id];
      if (!node) {
        this.drop(id);
        continue;
      }
      this.rebuild(node, done);
      touched.add(id);
      for (const c of node.children ?? []) touched.add(c);
    }
    for (const id of change.nodes) if (!doc.nodes[id]) this.removeCss(id);
    for (const id of this.fresh) touched.add(id);
    for (const id of touched) if (doc.nodes[id]) this.writeCss(id);
  }
  rebuild(node, done) {
    const doc = this.getDoc();
    const old = this.elements.get(node.id);
    if (isSkipped(node, CTX) || !this.isRenderedParent(node)) {
      if (old) this.detach(node.id, old);
      done.add(node.id);
      return;
    }
    const el = this.createShallow(doc, node, done);
    if (old) {
      this.releaseModulesIn(old, el);
      old.replaceWith(el);
    } else {
      this.insertIntoParent(node, el);
    }
    this.elements.set(node.id, el);
    done.add(node.id);
  }
  isRenderedParent(node) {
    if (!node.parent) return true;
    return this.elements.has(node.parent);
  }
  /** The render context of the canvas: the content lists' entries, and inside an item template its first entry. */
  ctx() {
    return { mode: "canvas", data: this.getData() };
  }
  ctxFor(id) {
    const base = this.ctx();
    const doc = this.getDoc();
    const list = listAncestor(doc, id);
    if (!list) return base;
    const data = base.data?.[list];
    return itemContext(base, Array.isArray(data?.items) && data.items.length ? data.items[0] : null, false);
  }
  /** The node's element with its existing child elements moved in (or rendered if new). */
  createShallow(doc, raw, done) {
    const ctx = this.ctxFor(raw.id);
    const node = bindNode(raw, ctx.item);
    const comp = getComponent(node.type);
    const tag = comp.tag(node);
    let open = "<" + tag + ' class="' + escapeHtml(classesOf(doc, node)) + '"';
    if (comp.attrs) for (const [name, value] of comp.attrs(node, ctx)) open += " " + name + '="' + escapeHtml(value) + '"';
    open += ' data-pe-id="' + node.id + '">';
    // A content list: the editable template takes the first entry's place; the other entries are copies.
    const kids = node.type === "collection"
      ? collectionInner(node, ctx, (item, index) => {
        if (index === 0) return KIDS_MARKER;
        const copy = itemContext(ctx, item, true);
        return (node.children ?? []).map((cid) => renderNode(doc, cid, copy)).join("");
      })
      : null;
    const html = comp.voidElement ? open : open + (kids ?? (comp.content ? comp.content(node, ctx, node.children ? KIDS_MARKER : "") : node.children ? KIDS_MARKER : "")) + "</" + tag + ">";
    const tpl = this.document.createElement("template");
    tpl.innerHTML = html;
    const el = tpl.content.firstElementChild;
    const marker = el.querySelector("template[data-pe-kids]");
    if (!marker && node.children) {
      // Nothing shows the children (a content list as a table): their old elements leave.
      for (const cid of node.children) {
        const stale = this.elements.get(cid);
        if (stale) this.detach(cid, stale);
      }
    }
    if (marker && node.children) {
      const frag = this.document.createDocumentFragment();
      for (const cid of node.children) {
        const child = doc.nodes[cid];
        if (!child || isSkipped(child, CTX)) {
          const stale = this.elements.get(cid);
          if (stale) this.detach(cid, stale);
          continue;
        }
        let childEl = this.elements.get(cid);
        if (!childEl) {
          childEl = this.fromHtml(renderNode(doc, cid, this.ctxFor(cid)));
          this.index(childEl);
          this.attachModules(childEl);
          done.add(cid);
        }
        frag.appendChild(childEl);
      }
      marker.replaceWith(frag);
    }
    if (node.type === "module") this.attachModules(el);
    return el;
  }
  fromHtml(html) {
    const tpl = this.document.createElement("template");
    tpl.innerHTML = html;
    return tpl.content.firstElementChild;
  }
  insertIntoParent(node, el) {
    const doc = this.getDoc();
    const parent = node.parent ? doc.nodes[node.parent] : null;
    const parentEl = node.parent ? this.elements.get(node.parent) : null;
    if (!parent || !parentEl) {
      if (node.id === doc.root && this.mount) {
        this.mount.replaceChildren(el);
        this.index(el);
      }
      return;
    }
    const container = layoutElement(parentEl);
    const siblings = parent.children ?? [];
    const at = siblings.indexOf(node.id);
    let before = null;
    for (let i = at + 1; i < siblings.length; i++) {
      const s = this.elements.get(siblings[i]);
      if (s && s.parentElement === container) {
        before = s;
        break;
      }
    }
    container.insertBefore(el, before);
    this.index(el);
    this.attachModules(el);
  }
  detach(id, el) {
    this.releaseModulesIn(el, null);
    el.remove();
    for (const sub of [el, ...Array.from(el.querySelectorAll("[data-pe-id]"))]) {
      const sid = sub.getAttribute("data-pe-id");
      if (sid && this.elements.get(sid) === sub) {
        this.elements.delete(sid);
        this.removeCss(sid);
      }
    }
    this.elements.delete(id);
  }
  drop(id) {
    const el = this.elements.get(id);
    if (el) this.detach(id, el);
  }
  index(root) {
    const all = [root, ...Array.from(root.querySelectorAll("[data-pe-id]"))];
    for (const el of all) {
      const id = el.getAttribute("data-pe-id");
      if (id) {
        this.elements.set(id, el);
        this.fresh.add(id);
      }
    }
  }
  /** Puts live module elements into the module slots below `root`. */
  attachModules(root) {
    const slots = root.matches("[data-pe-module]") ? [root] : Array.from(root.querySelectorAll("[data-pe-module]"));
    for (const slot of slots) {
      const id = Number(slot.getAttribute("data-pe-module"));
      const owner = slot.getAttribute("data-pe-id");
      const shown = this.primary.get(id);
      const mod = shown && owner && shown !== owner ? void 0 : this.modules.get(id);
      const marker = Array.from(slot.childNodes).find((n) => n.nodeType === 8 && n.data.startsWith("pe:module:"));
      if (mod) {
        if (marker) marker.replaceWith(mod);
        else if (mod.parentElement !== slot) slot.appendChild(mod);
        slot.classList.remove("pe-module-missing");
      } else {
        slot.classList.add("pe-module-missing");
        slot.setAttribute("data-pe-missing", !id ? "Choose a module" : this.modules.has(id) ? "Module " + id + " is already placed elsewhere" : "Module " + id + " is not on this page");
      }
    }
  }
  /**
   * Before `old` leaves the DOM: modules inside it that are not inside
   * `replacement` go back to where DNN rendered them.
   */
  releaseModulesIn(old, replacement) {
    for (const mod of Array.from(old.querySelectorAll(".DnnModule"))) {
      if (replacement && replacement.contains(mod)) continue;
      const m = MODULE_CLASS.exec(mod.className);
      if (!m) continue;
      this.park(Number(m[1]), mod);
    }
  }
  primarySlots() {
    const doc = this.getDoc();
    const out = /* @__PURE__ */ new Map();
    const stack = [doc.root];
    while (stack.length) {
      const id = stack.pop();
      const n = doc.nodes[id];
      if (!n || isSkipped(n, CTX)) continue;
      if (n.type === "module" && typeof n.props.moduleId === "number" && !out.has(n.props.moduleId)) out.set(n.props.moduleId, id);
      if (n.children) for (let i = n.children.length - 1; i >= 0; i--) stack.push(n.children[i]);
    }
    return out;
  }
  releaseAllModules() {
    for (const [id, mod] of this.modules) this.park(id, mod);
  }
  /**
   * A module the layout does not place goes back where DNN rendered it (the page shows it
   * after the layout); one removed from the layout in the editor waits hidden in the stash.
   */
  park(id, mod) {
    if (removedModuleIds(this.getDoc()).has(id)) this.stash?.appendChild(mod);
    else this.origins.get(id)?.after(mod);
  }
  writeCss(id) {
    const doc = this.getDoc();
    const node = doc.nodes[id];
    if (!node || !this.elements.has(id)) {
      this.removeCss(id);
      return;
    }
    const css = nodeCss(doc, id);
    let sheet = this.styles.get(id);
    if (!css) {
      if (sheet) sheet.css = "", sheet.sheet.replaceSync("");
      return;
    }
    if (!sheet) {
      sheet = { sheet: new this.win.CSSStyleSheet(), css: "" };
      this.styles.set(id, sheet);
      this.adopt();
    }
    if (sheet.css !== css) {
      sheet.css = css;
      sheet.sheet.replaceSync(css);
    }
  }
  removeCss(id) {
    if (this.styles.delete(id)) this.adopt();
  }
  writeVisibilityCss() {
    if (!this.visibilityStyle) this.visibilityStyle = new this.win.CSSStyleSheet();
    this.visibilityStyle.replaceSync(visibilityCss(this.getDoc()));
    this.adopt();
  }
  /** The document's adopted sheets: one per node, in document order of creation, then the device visibility rules. */
  adopt() {
    const sheets = [...this.styles.values()].map((s) => s.sheet);
    if (this.visibilityStyle) sheets.push(this.visibilityStyle);
    this.document.adoptedStyleSheets = sheets;
  }
  /** The element whose layout holds the node's children (a section's inner wrapper). */
  layoutTargetOf(id) {
    const el = this.elements.get(id);
    return el ? layoutElement(el) : null;
  }
  /** The node id of the element at a point of the canvas viewport, ignoring `except` subtrees. */
  nodeAt(x, y) {
    const stack = this.document.elementsFromPoint(x, y);
    for (const el of stack) {
      const hit = el.closest?.("[data-pe-id]");
      if (hit && this.mount?.contains(hit)) return hit.getAttribute("data-pe-id");
    }
    return null;
  }
  moduleIdsOnPage() {
    return [...this.modules.keys()];
  }
  dispose() {
    this.releaseAllModules();
    this.elements.clear();
  }
}
function layoutElement(el) {
  const inner = el.firstElementChild;
  return inner && inner.classList.contains("pe-in") ? inner : el;
}
/** The id of the content list whose item template holds the node, or undefined. */
function listAncestor(doc, id) {
  let cur = doc.nodes[id]?.parent;
  for (let d = 0; cur && d < 64; d++) {
    const n = doc.nodes[cur];
    if (!n) return void 0;
    if (n.type === "collection") return n.id;
    cur = n.parent;
  }
  return void 0;
}
function depthOf(doc, id) {
  let d = 0;
  let cur = doc.nodes[id]?.parent;
  while (cur && d < 64) {
    d++;
    cur = doc.nodes[cur]?.parent;
  }
  return d;
}
export {
  CanvasFrame,
  layoutElement
};
