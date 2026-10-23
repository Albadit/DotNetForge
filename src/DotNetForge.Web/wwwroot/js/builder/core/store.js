class Tx {
  constructor(store) {
    this.store = store;
  }
  store;
  changes = /* @__PURE__ */ new Map();
  settingsChange;
  structure = false;
  selection;
  get doc() {
    return this.store.doc;
  }
  get(id) {
    const c = this.changes.get(id);
    return c ? c[1] : this.store.doc.nodes[id];
  }
  has(id) {
    return this.get(id) !== void 0;
  }
  /** Replaces a node (or adds it). */
  put(node) {
    const prev = this.changes.get(node.id);
    const before = prev ? prev[0] : this.store.doc.nodes[node.id];
    if (!before || before.parent !== node.parent || before.children !== node.children) this.structure = true;
    this.changes.set(node.id, [before, node]);
  }
  update(id, fn) {
    const cur = this.get(id);
    if (!cur) return;
    const next = fn(cur);
    if (next !== cur) this.put(next);
  }
  remove(id) {
    const prev = this.changes.get(id);
    const before = prev ? prev[0] : this.store.doc.nodes[id];
    if (prev && !prev[0]) {
      this.changes.delete(id);
    } else {
      this.changes.set(id, [before, void 0]);
    }
    this.structure = true;
  }
  settings(next) {
    const before = this.settingsChange ? this.settingsChange[0] : this.store.doc.settings;
    this.settingsChange = [before, next];
  }
  select(ids) {
    this.selection = ids;
  }
}
const HISTORY_LIMIT = 300;
const MERGE_WINDOW_MS = 1200;
class Store {
  doc;
  selection = [];
  /** Increases on every change; the saver compares it with the last saved one. */
  version = 0;
  undoStack = [];
  redoStack = [];
  listeners = /* @__PURE__ */ new Set();
  selectionListeners = /* @__PURE__ */ new Set();
  nodeListeners = /* @__PURE__ */ new Map();
  mergeBarrier = false;
  constructor(doc) {
    this.doc = doc;
  }
  subscribe(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }
  subscribeSelection(listener) {
    this.selectionListeners.add(listener);
    return () => this.selectionListeners.delete(listener);
  }
  /** Called when this one node changes (replaced, removed). */
  subscribeNode(id, listener) {
    let set = this.nodeListeners.get(id);
    if (!set) this.nodeListeners.set(id, set = /* @__PURE__ */ new Set());
    set.add(listener);
    return () => {
      set.delete(listener);
      if (set.size === 0) this.nodeListeners.delete(id);
    };
  }
  get canUndo() {
    return this.undoStack.length > 0;
  }
  get canRedo() {
    return this.redoStack.length > 0;
  }
  get undoLabel() {
    return this.undoStack[this.undoStack.length - 1]?.label;
  }
  get redoLabel() {
    return this.redoStack[this.redoStack.length - 1]?.label;
  }
  /**
   * Runs `fn` as one undoable step. With a `merge` key, consecutive
   * transactions with the same key (typing, dragging a slider) within a
   * short time become one step.
   */
  transact(label, fn, options = {}) {
    const tx = new Tx(this);
    fn(tx);
    if (tx.changes.size === 0 && !tx.settingsChange) {
      if (tx.selection) this.select(tx.selection);
      return false;
    }
    const selectionBefore = this.selection;
    this.apply(tx.changes, 1, tx.settingsChange?.[1]);
    const selectionAfter = tx.selection ? this.cleanSelection(tx.selection) : this.cleanSelection(this.selection);
    this.selection = selectionAfter;
    const last = this.undoStack[this.undoStack.length - 1];
    const now = Date.now();
    if (options.merge && !this.mergeBarrier && last && last.mergeKey === options.merge && now - last.time < MERGE_WINDOW_MS) {
      for (const [id, [before, after]] of tx.changes) {
        const existing = last.changes.get(id);
        last.changes.set(id, [existing ? existing[0] : before, after]);
      }
      if (tx.settingsChange) last.settings = [last.settings ? last.settings[0] : tx.settingsChange[0], tx.settingsChange[1]];
      last.selectionAfter = selectionAfter;
      last.time = now;
    } else {
      this.undoStack.push({
        label,
        changes: tx.changes,
        settings: tx.settingsChange,
        selectionBefore,
        selectionAfter,
        mergeKey: options.merge,
        time: now
      });
      if (this.undoStack.length > HISTORY_LIMIT) this.undoStack.shift();
    }
    this.mergeBarrier = false;
    this.redoStack = [];
    this.emit(tx.changes.keys(), tx.structure, !!tx.settingsChange, "edit");
    if (selectionBefore !== selectionAfter) this.emitSelection();
    return true;
  }
  /** Ends merging: the next transaction starts a new undo step even with the same key. */
  breakMerge() {
    this.mergeBarrier = true;
  }
  undo() {
    const entry = this.undoStack.pop();
    if (!entry) return;
    this.applyEntry(entry, 0);
    this.redoStack.push(entry);
    this.selection = this.cleanSelection(entry.selectionBefore);
    this.emit(entry.changes.keys(), true, !!entry.settings, "undo");
    this.emitSelection();
  }
  redo() {
    const entry = this.redoStack.pop();
    if (!entry) return;
    this.applyEntry(entry, 1);
    this.undoStack.push(entry);
    this.selection = this.cleanSelection(entry.selectionAfter);
    this.emit(entry.changes.keys(), true, !!entry.settings, "redo");
    this.emitSelection();
  }
  /** Replaces the whole document (load, restore, conflict resolution). Clears history. */
  load(doc, keepHistory = false) {
    const ids = /* @__PURE__ */ new Set([...Object.keys(this.doc.nodes), ...Object.keys(doc.nodes)]);
    this.doc = doc;
    if (!keepHistory) {
      this.undoStack = [];
      this.redoStack = [];
    }
    this.selection = this.cleanSelection(this.selection);
    this.version++;
    this.emit(ids.values(), true, true, "load");
    this.emitSelection();
  }
  select(ids) {
    const next = this.cleanSelection(ids);
    if (next.length === this.selection.length && next.every((id, i) => id === this.selection[i])) return;
    this.selection = next;
    this.emitSelection();
  }
  cleanSelection(ids) {
    const seen = /* @__PURE__ */ new Set();
    return ids.filter((id) => this.doc.nodes[id] && !seen.has(id) && (seen.add(id), true));
  }
  applyEntry(entry, side) {
    this.apply(entry.changes, side, entry.settings?.[side]);
  }
  apply(changes, side, settings) {
    const nodes = { ...this.doc.nodes };
    for (const [id, pair] of changes) {
      const value = pair[side];
      if (value) nodes[id] = value;
      else delete nodes[id];
    }
    this.doc = { ...this.doc, nodes, settings: settings ?? this.doc.settings };
    this.version++;
  }
  emit(ids, structure, settings, source) {
    const set = new Set(ids);
    const change = { nodes: set, structure, settings, source };
    for (const id of set) {
      const ls = this.nodeListeners.get(id);
      if (ls) for (const l of [...ls]) l();
    }
    for (const l of [...this.listeners]) l(change);
  }
  emitSelection() {
    for (const l of [...this.selectionListeners]) l(this.selection);
  }
}
export {
  Store,
  Tx
};
