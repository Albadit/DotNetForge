import { signal } from "../../core/signal.js";
import { layoutAt, gridPlacementAt, freePlacementAt, styleAt } from "../../core/responsive.js";
import { componentDef } from "../../core/schema.js";
import { autoFlow, placeItem } from "../../core/grid.js";
import { gridItemsAt, moveGridItem, moveNodes, setPlacement, updateStyle, insertComponent, insertSubtree, placedModules } from "../../core/commands.js";
import { CanvasFrame, layoutElement } from "./frame.js";
import { findDropTarget, gridMetrics, heldMetrics, rectOf, spansFor, cellAt } from "./geometry.js";
const DRAG_THRESHOLD = 4;
const SNAP = 8;
const GUIDE_DISTANCE = 6;
function insertSpec(session, spec, target) {
  const store = session.store;
  if (spec.moduleId) {
    const existing = placedModules(store.doc).get(spec.moduleId);
    if (existing) {
      moveNodes(store, [existing], target);
      session.toast("That module was already on the page; it has been moved here.");
      return existing;
    }
  }
  if (spec.nodes) return insertSubtree(store, spec.nodes, target, "Insert " + spec.label);
  return insertComponent(store, spec.type, target, spec.moduleId !== void 0 ? { moduleId: spec.moduleId } : void 0);
}
class CanvasController {
  constructor(session) {
    this.session = session;
  }
  session;
  frame = null;
  iframe = null;
  stage = null;
  ready = signal(false);
  error = signal("");
  /** Bumped whenever overlay geometry may have changed. */
  tick = signal(0);
  drag = signal(null);
  unsubscribe = [];
  rafPending = false;
  pointer = null;
  op = null;
  pressed = null;
  scrollSpeed = 0;
  /* ---------- lifecycle ---------- */
  attach(iframe, stage) {
    this.iframe = iframe;
    this.stage = stage;
    iframe.addEventListener("load", this.onFrameLoad);
    this.load();
    const s = this.session;
    this.unsubscribe.push(
      s.store.subscribe((c) => {
        this.frame?.patch(c);
        this.invalidate();
      }),
      s.store.subscribeSelection(() => this.invalidate()),
      s.hover.subscribe(() => this.invalidate()),
      s.zoom.subscribe(() => this.invalidate()),
      s.docRevision.subscribe(() => this.resubscribeStore()),
      s.canvasRevision.subscribe(() => this.load())
    );
  }
  resubscribeStore() {
    this.unsubscribe.shift()?.();
    const s = this.session;
    this.unsubscribe.unshift(s.store.subscribe((c) => {
      this.frame?.patch(c);
      this.invalidate();
    }));
    this.unsubscribe.push(s.store.subscribeSelection(() => this.invalidate()));
    this.frame?.renderAll();
    this.invalidate();
  }
  load() {
    if (!this.iframe) return;
    this.ready.set(false);
    this.error.set("");
    this.frame?.dispose();
    this.frame = null;
    const url = new URL(this.session.config.canvasUrl, location.href);
    url.searchParams.set("peCanvas", "1");
    url.searchParams.set("peT", String(Date.now()));
    this.iframe.src = url.toString();
  }
  onFrameLoad = () => {
    const win = this.iframe?.contentWindow;
    if (!win) return;
    try {
      this.frame = new CanvasFrame(win, () => this.session.store.doc);
      const err = this.frame.init();
      if (err) {
        this.error.set(err);
        return;
      }
    } catch (e) {
      this.error.set("The page could not be opened for editing: " + (e instanceof Error ? e.message : String(e)));
      return;
    }
    win.addEventListener("scroll", this.invalidate, { passive: true });
    win.addEventListener("resize", this.invalidate);
    win.document.addEventListener("submit", (e) => e.preventDefault(), true);
    win.document.addEventListener("keydown", (e) => this.forwardKey(e), true);
    const ro = new win.ResizeObserver(() => this.invalidate());
    ro.observe(win.document.body);
    this.ready.set(true);
    this.invalidate();
  };
  /** Keys pressed while focus is inside the canvas (inline editing) still reach the editor's shortcuts. */
  forwardKey(e) {
    if (this.session.editingText.value) return;
    window.dispatchEvent(new KeyboardEvent("keydown", e));
    if (e.ctrlKey || e.metaKey) e.preventDefault();
  }
  detach() {
    for (const u of this.unsubscribe) u();
    this.unsubscribe = [];
    this.iframe?.removeEventListener("load", this.onFrameLoad);
    this.frame?.dispose();
    this.frame = null;
  }
  invalidate = () => {
    if (this.rafPending) return;
    this.rafPending = true;
    requestAnimationFrame(() => {
      this.rafPending = false;
      this.step();
      this.tick.update((n) => n + 1);
    });
  };
  /* ---------- coordinates ---------- */
  get zoom() {
    return this.session.zoom.value;
  }
  /** Viewport (client) point -> canvas page point. */
  toCanvas(clientX, clientY) {
    const r = this.iframe.getBoundingClientRect();
    return { x: (clientX - r.left) / this.zoom, y: (clientY - r.top) / this.zoom };
  }
  /** Canvas rect -> stage (overlay) coordinates. */
  toStage(r) {
    const z = this.zoom;
    return { x: r.x * z, y: r.y * z, w: r.w * z, h: r.h * z };
  }
  rectOfNode(id) {
    const el = this.frame?.elements.get(id);
    if (!el || !el.isConnected) return null;
    const r = rectOf(el);
    return r.w === 0 && r.h === 0 && el.offsetParent === null ? null : r;
  }
  scrollIntoView(id) {
    const el = this.frame?.elements.get(id);
    const win = this.frame?.win;
    if (!el || !win) return;
    const r = el.getBoundingClientRect();
    if (r.top < 0 || r.bottom > win.innerHeight) win.scrollBy({ top: r.top - win.innerHeight / 3, behavior: "smooth" });
  }
  /* ---------- hit testing & selection ---------- */
  /** The selectable node at a canvas point: locked nodes pass the click to their nearest unlocked ancestor. */
  hit(x, y) {
    const doc = this.session.store.doc;
    let id = this.frame?.nodeAt(x, y) ?? null;
    while (id && doc.nodes[id]?.locked) id = doc.nodes[id].parent;
    return id;
  }
  /** The layers from the page down to the deepest selectable layer under a point (the page excluded). */
  pathAt(x, y) {
    const doc = this.session.store.doc;
    const path = [];
    for (let id = this.hit(x, y); id && id !== doc.root; id = doc.nodes[id]?.parent ?? null) path.unshift(id);
    return path;
  }
  /**
   * Figma's click: the layer at the level of the current selection (the selected layer or a
   * sibling, or a sibling of one of its ancestors), else the outermost layer under the pointer.
   * Ctrl/Cmd+click takes the deepest layer.
   */
  pick(x, y, deep = false) {
    const path = this.pathAt(x, y);
    if (!path.length) return null;
    if (deep) return path[path.length - 1];
    const doc = this.session.store.doc;
    const sel = this.session.store.selection[0];
    for (let a = sel ? doc.nodes[sel]?.parent ?? null : null; a && a !== doc.root; a = doc.nodes[a]?.parent ?? null) {
      const i = path.indexOf(a);
      if (i >= 0) return path[i + 1] ?? a;
    }
    return path[0];
  }
  /** Double-click: the layer one level below the selection under the pointer, if any. */
  deeper(x, y) {
    const path = this.pathAt(x, y);
    const sel = this.session.store.selection[0];
    const i = sel ? path.indexOf(sel) : -1;
    return i >= 0 && i < path.length - 1 ? path[i + 1] : i < 0 && path.length ? path[0] : null;
  }
  onPointerDown = (e) => {
    if (!this.frame || e.button === 1) return;
    const p = this.toCanvas(e.clientX, e.clientY);
    const store = this.session.store;
    const id = this.pick(p.x, p.y, e.ctrlKey || e.metaKey);
    if (e.button === 2) {
      if (id && !store.selection.includes(id)) store.select([id]);
      return;
    }
    if (!id) {
      store.select([]);
      return;
    }
    if (e.shiftKey) {
      store.select(store.selection.includes(id) ? store.selection.filter((s) => s !== id) : [...store.selection, id]);
      return;
    }
    if (!store.selection.includes(id)) store.select([id]);
    if (e.detail >= 2) return;
    if (this.session.caps.value.editLayout && id !== store.doc.root) {
      this.pressed = { id, startX: e.clientX, startY: e.clientY, pointerId: e.pointerId, shift: e.shiftKey };
      e.currentTarget.setPointerCapture?.(e.pointerId);
    }
  };
  onPointerMove = (e) => {
    if (!this.frame) return;
    this.pointer = { x: e.clientX, y: e.clientY, shift: e.shiftKey, alt: e.altKey };
    if (this.pressed && !this.op) {
      if (Math.hypot(e.clientX - this.pressed.startX, e.clientY - this.pressed.startY) >= DRAG_THRESHOLD) {
        this.beginMove(this.pressed.id, this.pressed.startX, this.pressed.startY);
      }
    }
    if (!this.op) {
      const p = this.toCanvas(e.clientX, e.clientY);
      const id = this.pick(p.x, p.y, e.ctrlKey || e.metaKey);
      if (this.session.hover.value !== id) this.session.hover.set(id);
    }
    this.invalidate();
  };
  onPointerUp = (e) => {
    this.pressed = null;
    if (this.op) {
      this.pointer = { x: e.clientX, y: e.clientY, shift: e.shiftKey, alt: e.altKey };
      this.step();
      const op = this.op;
      this.op = null;
      op.commit();
      this.drag.set(null);
      this.invalidate();
    }
  };
  onPointerLeave = () => {
    if (!this.op) this.session.hover.set(null);
  };
  cancelOperation() {
    if (!this.op) return false;
    this.op.cancel();
    this.op = null;
    this.pressed = null;
    this.drag.set(null);
    this.invalidate();
    return true;
  }
  get busy() {
    return this.op !== null;
  }
  onWheel = (e) => {
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      const factor = Math.exp(-e.deltaY / 400);
      this.session.zoomMode.set("manual");
      this.session.zoom.set(Math.min(4, Math.max(0.1, Math.round(this.zoom * factor * 100) / 100)));
      return;
    }
    this.frame?.win.scrollBy({ left: e.deltaX / this.zoom, top: e.deltaY / this.zoom });
  };
  /* ---------- per-frame work ---------- */
  step() {
    if (!this.op || !this.pointer) return;
    this.autoscroll();
    this.op.update(this.toCanvas(this.pointer.x, this.pointer.y), this.pointer.shift, this.pointer.alt);
    this.drag.set(this.op.view());
    if (this.scrollSpeed) this.invalidate();
  }
  autoscroll() {
    if (!this.pointer || !this.iframe || !this.frame) return;
    const r = this.iframe.getBoundingClientRect();
    const edge = 48;
    const y = this.pointer.y;
    this.scrollSpeed = y < r.top + edge ? -Math.ceil((r.top + edge - y) / 4) : y > r.bottom - edge ? Math.ceil((y - (r.bottom - edge)) / 4) : 0;
    if (this.scrollSpeed) this.frame.win.scrollBy(0, this.scrollSpeed / this.zoom);
  }
  /* ---------- operations ---------- */
  beginMove(id, clientX, clientY) {
    const store = this.session.store;
    const node = store.doc.nodes[id];
    const el = this.frame?.elements.get(id);
    if (!node || !node.parent || !el || node.locked) return;
    const start = this.toCanvas(clientX, clientY);
    this.op = new MoveOperation(this, id, start);
    this.session.hover.set(null);
  }
  beginResize(handle, e) {
    const id = this.session.store.selection[0];
    if (!id || !this.frame) return;
    e.stopPropagation();
    e.preventDefault();
    this.pointer = { x: e.clientX, y: e.clientY, shift: e.shiftKey, alt: e.altKey };
    this.op = new ResizeOperation(this, id, handle, this.toCanvas(e.clientX, e.clientY));
    e.target.setPointerCapture?.(e.pointerId);
    this.invalidate();
  }
  /** Starts dragging something new from the insert panel (pointer events on the parent document). */
  beginInsert(spec, e) {
    if (!this.frame) return;
    this.pointer = { x: e.clientX, y: e.clientY, shift: e.shiftKey, alt: e.altKey };
    this.op = new InsertOperation(this, spec);
    const move = (ev) => {
      this.pointer = { x: ev.clientX, y: ev.clientY, shift: ev.shiftKey, alt: ev.altKey };
      this.invalidate();
    };
    const up = (ev) => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      this.onPointerUp(ev);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
    this.invalidate();
  }
  /** Inserts at the selection (click instead of drag on the insert panel). */
  insertAtSelection(spec) {
    const s = this.session;
    const store = s.store;
    const doc = store.doc;
    const sel = store.selection[0];
    let parent = null;
    let index;
    const type = spec.nodes?.[0]?.type ?? spec.type;
    const accepts = (id2) => {
      const n = doc.nodes[id2];
      return !!n?.children && !n.locked && componentDef(type)?.parents.includes(n.type);
    };
    if (sel && accepts(sel)) parent = sel;
    else if (sel && doc.nodes[sel]?.parent && accepts(doc.nodes[sel].parent)) {
      parent = doc.nodes[sel].parent;
      index = doc.nodes[parent].children.indexOf(sel) + 1;
    } else {
      const sections = doc.nodes[doc.root].children ?? [];
      const candidates = [doc.root, ...sections].reverse();
      parent = candidates.find(accepts) ?? null;
    }
    if (!parent) {
      s.toast(`A ${componentDef(type)?.label ?? type} cannot be placed here.`, "error");
      return null;
    }
    const target = { parent, index, bp: s.breakpoint.value };
    const id = insertSpec(s, spec, target);
    if (id) requestAnimationFrame(() => this.scrollIntoView(id));
    return id;
  }
}
class Preview {
  // Per element, the inline properties a drag preview overrode and their previous values. Restored through the CSSOM
  // (never the style attribute, which the Content-Security-Policy treats as inline style).
  saved = /* @__PURE__ */ new Map();
  set(el, props) {
    let before = this.saved.get(el);
    if (!before) {
      before = /* @__PURE__ */ new Map();
      this.saved.set(el, before);
    }
    for (const [k, v] of Object.entries(props)) {
      if (!before.has(k)) before.set(k, [el.style.getPropertyValue(k), el.style.getPropertyPriority(k)]);
      el.style.setProperty(k, v, "important");
    }
  }
  restore() {
    for (const [el, before] of this.saved) {
      for (const [k, [value, priority]] of before) {
        if (value) el.style.setProperty(k, value, priority);
        else el.style.removeProperty(k);
      }
    }
    this.saved.clear();
  }
}
function emptyView(kind) {
  return { kind, indicator: null, ghost: null, guides: [], label: null, labelAt: null, container: null, grid: null };
}
function snapFree(box, others, container, snap, moveEdges) {
  const guides = [];
  let { x, y } = box;
  if (snap) {
    x = container.x + Math.round((x - container.x) / SNAP) * SNAP;
    y = container.y + Math.round((y - container.y) / SNAP) * SNAP;
  }
  const xs = [container.x, container.x + container.w / 2, container.x + container.w, ...others.flatMap((o) => [o.x, o.x + o.w / 2, o.x + o.w])];
  const ys = [container.y, container.y + container.h / 2, container.y + container.h, ...others.flatMap((o) => [o.y, o.y + o.h / 2, o.y + o.h])];
  if (moveEdges.x) {
    let best = null;
    for (const edge of [0, box.w / 2, box.w]) {
      for (const cand of xs) {
        const d = Math.abs(box.x + edge - cand);
        if (d <= GUIDE_DISTANCE && (!best || d < best.d)) best = { d, to: cand - edge, line: cand };
      }
    }
    if (best) {
      x = best.to;
      guides.push({ x1: best.line, y1: Math.min(container.y, box.y), x2: best.line, y2: Math.max(container.y + container.h, box.y + box.h) });
    }
  }
  if (moveEdges.y) {
    let best = null;
    for (const edge of [0, box.h / 2, box.h]) {
      for (const cand of ys) {
        const d = Math.abs(box.y + edge - cand);
        if (d <= GUIDE_DISTANCE && (!best || d < best.d)) best = { d, to: cand - edge, line: cand };
      }
    }
    if (best) {
      y = best.to;
      guides.push({ x1: Math.min(container.x, box.x), y1: best.line, x2: Math.max(container.x + container.w, box.x + box.w), y2: best.line });
    }
  }
  return { box: { ...box, x, y }, guides };
}
class MoveOperation {
  constructor(c, id, start) {
    this.c = c;
    this.id = id;
    const doc = c.session.store.doc;
    this.node = doc.nodes[id];
    this.el = c.frame.elements.get(id);
    this.startRect = rectOf(this.el);
    this.grab = { dx: start.x - this.startRect.x, dy: start.y - this.startRect.y };
    const parent = doc.nodes[this.node.parent];
    const bp = c.session.breakpoint.value;
    const g = layoutAt(parent, bp).mode === "grid" ? gridPlacementAt(this.node, parent, bp) : null;
    this.span = g ? { colSpan: g.colSpan, rowSpan: g.rowSpan } : { colSpan: componentDef(this.node.type)?.place?.colSpan ?? 4, rowSpan: componentDef(this.node.type)?.place?.rowSpan ?? 2 };
    this.preview.set(this.el, { "pointer-events": "none", opacity: "0.35", transition: "none" });
  }
  c;
  id;
  preview = new Preview();
  node;
  el;
  startRect;
  grab;
  span;
  target = null;
  ghost = null;
  guides = [];
  gridKey = "";
  gridItems = null;
  freePos = null;
  grid = null;
  /** Each grid's tracks as they were when the drag first reached it (see heldMetrics). */
  held = /* @__PURE__ */ new Map();
  update(p, shift, alt) {
    const c = this.c;
    const doc = c.session.store.doc;
    const bp = c.session.breakpoint.value;
    this.ghost = { x: p.x - this.grab.dx, y: p.y - this.grab.dy, w: this.startRect.w, h: this.startRect.h };
    this.guides = [];
    this.grid = null;
    this.target = findDropTarget(c.frame, doc, bp, {
      x: p.x,
      y: p.y,
      type: this.node.type,
      moving: [this.id],
      span: this.span,
      grab: this.grab,
      size: { w: this.startRect.w, h: this.startRect.h },
      metrics: (pid, live) => {
        const held = this.held.get(pid);
        if (!held) this.held.set(pid, live);
        return held ? heldMetrics(held, live) : live;
      }
    });
    const t = this.target;
    const sameParent = t?.parent === this.node.parent;
    const parent = t ? doc.nodes[t.parent] : null;
    const layout = parent ? layoutAt(parent, bp) : null;
    if (t && sameParent && layout?.mode === "grid" && t.at?.col && t.at.row) {
      const key = t.at.col + ":" + t.at.row;
      const parentEl = c.frame.layoutTargetOf(t.parent);
      this.grid = gridMetrics(parentEl, layout.columns ?? 12, layout.rowHeight ?? 40);
      if (key !== this.gridKey) {
        this.gridKey = key;
        const items = gridItemsAt(doc, t.parent, bp);
        const was = new Map(items.map((i) => [i.id, i.col + ":" + i.row]));
        this.gridItems = placeItem(items, this.id, { col: t.at.col, row: t.at.row, ...this.span }, layout.columns ?? 12, { float: layout.compact !== false, pin: true });
        for (const it of this.gridItems) {
          const el = c.frame.elements.get(it.id);
          if (!el) continue;
          const pushed = it.id !== this.id && was.get(it.id) !== it.col + ":" + it.row;
          this.preview.set(el, { "grid-column": `${it.col} / span ${it.colSpan}`, "grid-row": `${it.row} / span ${it.rowSpan}`, transition: "none", ...pushed ? { "pointer-events": "none" } : {} });
        }
      }
    } else if (this.gridItems) {
      this.preview.restore();
      this.preview.set(this.el, { "pointer-events": "none", opacity: "0.35", transition: "none" });
      this.gridItems = null;
      this.gridKey = "";
    }
    this.freePos = null;
    if (t && sameParent && layout?.mode === "free" && t.at && t.at.x !== void 0) {
      const container = t.container;
      const others = (parent.children ?? []).filter((id) => id !== this.id).map((id) => c.rectOfNode(id)).filter((r) => !!r);
      const snapped = snapFree({ x: container.x + t.at.x, y: container.y + t.at.y, w: this.startRect.w, h: this.startRect.h }, others, container, !alt, { x: true, y: true });
      this.guides = snapped.guides;
      this.freePos = { x: Math.round(snapped.box.x - container.x), y: Math.round(snapped.box.y - container.y) };
      this.ghost = snapped.box;
      this.preview.set(this.el, { left: this.freePos.x + "px", top: this.freePos.y + "px", opacity: "1" });
    }
    void shift;
  }
  view() {
    const t = this.target;
    const v = emptyView("move");
    v.ghost = this.freePos ? null : this.ghost;
    v.guides = this.guides;
    v.indicator = t ? t.indicator : null;
    v.container = t ? t.container : null;
    v.grid = this.grid;
    if (t?.at?.col && this.grid) {
      v.label = `${this.span.colSpan} × ${this.span.rowSpan}`;
      v.labelAt = t.indicator.kind === "cell" ? { x: t.indicator.rect.x + t.indicator.rect.w, y: t.indicator.rect.y + t.indicator.rect.h } : null;
    } else if (this.freePos) {
      v.label = `${this.freePos.x}, ${this.freePos.y}`;
      v.labelAt = this.ghost ? { x: this.ghost.x + this.ghost.w, y: this.ghost.y + this.ghost.h } : null;
    }
    return v;
  }
  commit() {
    this.preview.restore();
    const t = this.target;
    if (!t) return;
    const s = this.c.session;
    const store = s.store;
    const bp = s.breakpoint.value;
    const doc = store.doc;
    const parent = doc.nodes[t.parent];
    const layout = layoutAt(parent, bp);
    if (t.parent === this.node.parent) {
      if (layout.mode === "grid" && t.at?.col && t.at.row) {
        moveGridItem(store, this.id, bp, { col: t.at.col, row: t.at.row, ...this.span });
        return;
      }
      if (layout.mode === "free" && this.freePos) {
        setPlacement(store, this.id, bp, { x: this.freePos.x, y: this.freePos.y });
        return;
      }
    }
    moveNodes(store, [this.id], { parent: t.parent, index: t.index, bp, at: t.at });
  }
  cancel() {
    this.preview.restore();
  }
}
class ResizeOperation {
  constructor(c, id, handle, origin) {
    this.c = c;
    this.id = id;
    this.handle = handle;
    this.origin = origin;
    const doc = c.session.store.doc;
    this.node = doc.nodes[id];
    this.el = c.frame.elements.get(id);
    this.start = rectOf(this.el);
    this.box = { ...this.start };
    const parent = this.node.parent ? doc.nodes[this.node.parent] : null;
    const layout = parent ? layoutAt(parent, c.session.breakpoint.value) : null;
    this.mode = layout?.mode === "grid" ? "grid" : layout?.mode === "free" ? "free" : "size";
    this.preview.set(this.el, { transition: "none" });
  }
  c;
  id;
  handle;
  origin;
  preview = new Preview();
  el;
  node;
  start;
  mode;
  box;
  guides = [];
  grid = null;
  spans = null;
  gridKey = "";
  held = null;
  update(p, shift, alt) {
    const dx = p.x - this.origin.x;
    const dy = p.y - this.origin.y;
    const h = this.handle;
    let { x, y, w, h: ht } = this.start;
    if (h.includes("e")) w = Math.max(8, this.start.w + dx);
    if (h.includes("s")) ht = Math.max(8, this.start.h + dy);
    if (h.includes("w")) {
      w = Math.max(8, this.start.w - dx);
      x = this.start.x + this.start.w - w;
    }
    if (h.includes("n")) {
      ht = Math.max(8, this.start.h - dy);
      y = this.start.y + this.start.h - ht;
    }
    if (shift && this.mode === "free" && h.length === 2) {
      const ratio = this.start.w / this.start.h;
      if (w / ht > ratio) ht = w / ratio;
      else w = ht * ratio;
      if (h.includes("n")) y = this.start.y + this.start.h - ht;
      if (h.includes("w")) x = this.start.x + this.start.w - w;
    }
    this.box = { x, y, w, h: ht };
    this.guides = [];
    const c = this.c;
    const s = c.session;
    const bp = s.breakpoint.value;
    const doc = s.store.doc;
    const parent = doc.nodes[this.node.parent];
    if (this.mode === "grid") {
      const layout = layoutAt(parent, bp);
      const parentEl = c.frame.layoutTargetOf(parent.id);
      const live = gridMetrics(parentEl, layout.columns ?? 12, layout.rowHeight ?? 40);
      if (!this.held) this.held = live;
      const m = heldMetrics(this.held, live);
      this.grid = m;
      const g = gridPlacementAt(this.node, parent, bp);
      const cur = g.auto ? autoFlow(gridItemsAt(doc, parent.id, bp).map((i) => i), layout.columns ?? 12).find((i) => i.id === this.id) : { col: g.col, row: g.row, colSpan: g.colSpan, rowSpan: g.rowSpan };
      const startCell = cellAt(m, this.box.x + 2, this.box.y + 2);
      const col = h.includes("w") ? startCell.col : cur.col;
      const row = h.includes("n") ? startCell.row : cur.row;
      const sp = spansFor(m, col, row, this.box.w, this.box.h);
      const colSpan = h.includes("e") || h.includes("w") ? Math.min(sp.colSpan, (layout.columns ?? 12) - col + 1) : cur.colSpan;
      const rowSpan = h.includes("s") || h.includes("n") ? sp.rowSpan : cur.rowSpan;
      this.spans = { col, row, colSpan, rowSpan };
      const key = `${col}:${row}:${colSpan}:${rowSpan}`;
      if (key !== this.gridKey) {
        this.gridKey = key;
        const items = placeItem(gridItemsAt(doc, parent.id, bp), this.id, this.spans, layout.columns ?? 12, { float: layout.compact !== false, pin: true });
        for (const it of items) {
          const el = c.frame.elements.get(it.id);
          if (el) this.preview.set(el, { "grid-column": `${it.col} / span ${it.colSpan}`, "grid-row": `${it.row} / span ${it.rowSpan}`, transition: "none" });
        }
      }
    } else if (this.mode === "free") {
      const container = rectOf(c.frame.layoutTargetOf(parent.id));
      const others = (parent.children ?? []).filter((i) => i !== this.id).map((i) => c.rectOfNode(i)).filter((r) => !!r);
      if (!alt) {
        const snap = (v, base) => base + Math.round((v - base) / SNAP) * SNAP;
        if (h.includes("e")) this.box.w = snap(this.box.x + this.box.w, container.x) - this.box.x;
        if (h.includes("s")) this.box.h = snap(this.box.y + this.box.h, container.y) - this.box.y;
        if (h.includes("w")) {
          const nx = snap(this.box.x, container.x);
          this.box.w += this.box.x - nx;
          this.box.x = nx;
        }
        if (h.includes("n")) {
          const ny = snap(this.box.y, container.y);
          this.box.h += this.box.y - ny;
          this.box.y = ny;
        }
      }
      const snapped = snapFree(this.box, others, container, false, { x: false, y: false });
      this.guides = snapped.guides;
      const rel = { x: Math.round(this.box.x - container.x), y: Math.round(this.box.y - container.y), w: Math.round(this.box.w), h: Math.round(this.box.h) };
      this.preview.set(this.el, { left: rel.x + "px", top: rel.y + "px", width: rel.w + "px", height: rel.h + "px" });
    } else {
      const props = {};
      if (h.includes("e") || h.includes("w")) props.width = Math.round(this.box.w) + "px";
      if (h.includes("s") || h.includes("n")) props.height = Math.round(this.box.h) + "px";
      props.flex = "0 0 auto";
      this.preview.set(this.el, props);
    }
  }
  view() {
    const v = emptyView("resize");
    v.grid = this.grid;
    v.guides = this.guides;
    if (this.mode === "grid" && this.spans && this.grid) {
      v.label = `${this.spans.colSpan} × ${this.spans.rowSpan}`;
    } else {
      v.label = `${Math.round(this.box.w)} × ${Math.round(this.box.h)}`;
    }
    const r = this.c.rectOfNode(this.id) ?? this.box;
    v.labelAt = { x: r.x + r.w, y: r.y + r.h };
    return v;
  }
  commit() {
    this.preview.restore();
    const s = this.c.session;
    const store = s.store;
    const bp = s.breakpoint.value;
    if (this.mode === "grid" && this.spans) {
      moveGridItem(store, this.id, bp, this.spans);
    } else if (this.mode === "free") {
      const parentEl = this.c.frame.layoutTargetOf(this.node.parent);
      const container = rectOf(parentEl);
      const f = freePlacementAt(this.node, bp);
      const next = { x: Math.round(this.box.x - container.x), y: Math.round(this.box.y - container.y), w: Math.round(this.box.w), h: Math.round(this.box.h) };
      if (next.x !== f.x || next.y !== f.y || next.w !== f.w || next.h !== f.h) setPlacement(store, this.id, bp, next);
    } else {
      const patch = {};
      const h = this.handle;
      if (h.includes("e") || h.includes("w")) patch.width = { mode: "fixed", value: Math.round(this.box.w) };
      if (h.includes("s") || h.includes("n")) patch.height = { mode: "fixed", value: Math.round(this.box.h) };
      const cur = styleAt(this.node, bp);
      if (JSON.stringify({ width: cur.width, height: cur.height }) !== JSON.stringify({ ...{ width: cur.width, height: cur.height }, ...patch })) {
        updateStyle(store, [this.id], bp, patch);
      }
    }
  }
  cancel() {
    this.preview.restore();
  }
}
class InsertOperation {
  constructor(c, spec) {
    this.c = c;
    this.spec = spec;
  }
  c;
  spec;
  target = null;
  ghost = null;
  overCanvas = false;
  get type() {
    return this.spec.nodes?.[0]?.type ?? this.spec.type;
  }
  update(p) {
    const c = this.c;
    const r = c.iframe.getBoundingClientRect();
    const z = c.zoom;
    const inside = p.x >= 0 && p.y >= 0 && p.x * z <= r.width && p.y * z <= r.height;
    this.overCanvas = inside;
    if (!inside) {
      this.target = null;
      this.ghost = null;
      return;
    }
    const def = componentDef(this.type);
    const span = { colSpan: def?.place?.colSpan ?? 4, rowSpan: def?.place?.rowSpan ?? 2 };
    this.target = findDropTarget(c.frame, c.session.store.doc, c.session.breakpoint.value, { x: p.x, y: p.y, type: this.type, moving: [], span, size: { w: 240, h: 120 }, grab: { dx: 20, dy: 12 } });
    this.ghost = { x: p.x - 20, y: p.y - 12, w: 160, h: 32 };
  }
  view() {
    const v = emptyView("insert");
    v.indicator = this.target?.indicator ?? null;
    v.container = this.target?.container ?? null;
    v.ghost = this.overCanvas ? this.ghost : null;
    v.label = this.spec.label;
    v.labelAt = this.ghost ? { x: this.ghost.x + 8, y: this.ghost.y } : null;
    return v;
  }
  commit() {
    const t = this.target;
    if (!t) return;
    const s = this.c.session;
    insertSpec(s, this.spec, { parent: t.parent, index: t.index, bp: s.breakpoint.value, at: t.at });
  }
  cancel() {
  }
}
export {
  CanvasController,
  insertSpec,
  layoutElement
};
