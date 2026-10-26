import { Store } from "../core/store.js";
import { signal } from "../core/signal.js";
import { createDocument, removedModuleIds } from "../core/doc.js";
import { normalizeDocument } from "../core/validate.js";
import { insertComponent, moveGridItem, renameNode } from "../core/commands.js";
import { gridPlacementAt, layoutAt } from "../core/responsive.js";
import { Api, ApiError, antiForgeryToken } from "./api.js";
import { ContentSource } from "./content.js";
// The editor's light/dark theme is the admin's (the Light / Dark / System menu of the admin screens,
// wwwroot/js/forge/ui.js): one setting in localStorage "dnf-theme" - "light" or "dark", absent for System.
const THEME_KEY = "dnf-theme";
function storedTheme() {
  try {
    // Earlier versions kept a separate editor theme; drop it so dnf-theme is the only setting.
    localStorage.removeItem("pe.theme");
    const v = localStorage.getItem(THEME_KEY);
    return v === "light" || v === "dark" ? v : "system";
  } catch {
    return "system";
  }
}
const DEVICE_WIDTHS = { base: 1280, tablet: 768, mobile: 390 };
const AUTOSAVE_DELAY = 1500;
const AUTOSAVE_MAX_WAIT = 1e4;
const HEARTBEAT_MS = 3e4;
class Session {
  constructor(config) {
    this.config = config;
    this.api = new Api({ base: config.api, tabId: config.tabId, canUpload: config.canUpload, token: () => antiForgeryToken() });
    this.content = new ContentSource(this);
  }
  config;
  api;
  /** Content types and the entries content lists show (editor/content.js). */
  content;
  store = new Store(createDocument());
  status = signal("loading");
  /** The editor's theme, remembered per browser. */
  theme = signal(storedTheme());
  /** The layer whose name is being edited in the layers panel (Ctrl+R, double-click). */
  renaming = signal(null);
  /** The grid whose track settings replace the design panel (Figma's "Open grid settings"). */
  gridSettings = signal(null);
  loadError = signal("");
  saveState = signal("saved");
  saveError = signal("");
  savedOn = signal(null);
  page = signal(null);
  caps = signal({ view: true, editContent: false, editLayout: false, publish: false, manage: false, admin: false });
  modules = signal([]);
  roles = signal([]);
  others = signal([]);
  problems = signal([]);
  pendingChanges = signal(false);
  breakpoint = signal("base");
  zoom = signal(1);
  zoomMode = signal("fit");
  deviceWidths = signal({ ...DEVICE_WIDTHS });
  leftTab = signal("layers");
  hover = signal(null);
  editingText = signal(null);
  dialog = signal(null);
  toasts = signal([]);
  showGrid = signal(true);
  canvasRevision = signal(0);
  /** Bumped whenever the document is replaced wholesale (load/restore), so views can reset. */
  docRevision = signal(0);
  version = null;
  savedVersion = 0;
  saveTimer;
  firstDirtyAt = 0;
  saving = null;
  heartbeatTimer;
  toastId = 0;
  unsubscribeStore = null;
  get readOnly() {
    const c = this.caps.value;
    return !c.editContent && !c.editLayout;
  }
  async load() {
    this.status.set("loading");
    let state;
    try {
      state = await this.api.state();
    } catch (e) {
      this.loadError.set(e instanceof Error ? e.message : String(e));
      this.status.set("error");
      return;
    }
    this.applyState(state);
    this.content.start();
    this.status.set("ready");
    if (!state.draft.document && !this.readOnly) this.dialog.set({ kind: "onboarding" });
    this.startHeartbeat();
  }
  applyState(state) {
    this.page.set(state.page);
    this.caps.set(state.capabilities);
    this.modules.set(state.modules);
    this.roles.set(state.roles);
    this.others.set(state.others);
    this.problems.set(state.problems ?? []);
    this.pendingChanges.set(state.draft.pendingChanges);
    this.version = state.draft.version;
    this.savedOn.set(state.draft.savedOn);
    let doc;
    try {
      doc = state.draft.document ? normalizeDocument(state.draft.document) : createDocument();
    } catch {
      doc = createDocument();
      this.problems.update((p) => [...p, "The saved draft could not be read and was replaced by an empty page."]);
    }
    this.replaceDocument(doc);
    this.saveState.set("saved");
    if (state.draft.document && state.capabilities.editLayout) this.adoptUnplacedModules(state.modules, state.page.hostPane);
  }
  /**
   * Modules added to the host pane in DNN after the layout was made render
   * after the layout (SkinHook), so they are on screen but were not layers.
   * They become layers: after the placed module of the same pane that comes
   * before them in DNN's order, or else at the end of the page. Modules
   * deleted from the layout in the editor stay out (settings.removedModules).
   */
  adoptUnplacedModules(modules, hostPane) {
    const doc = this.store.doc;
    const placed = /* @__PURE__ */ new Map();
    for (const n of Object.values(doc.nodes)) if (n.type === "module" && typeof n.props.moduleId === "number") placed.set(n.props.moduleId, n.id);
    const host = modules.filter((m) => m.paneName.toLowerCase() === hostPane.toLowerCase()).sort((a, b) => a.order - b.order);
    const removed = removedModuleIds(doc);
    const missing = host.filter((m) => !placed.has(m.moduleId) && !removed.has(m.moduleId));
    if (!missing.length) return;
    for (const m of missing) {
      const before = [...host].reverse().find((o) => o.order < m.order && placed.has(o.moduleId)) ?? host.find((o) => placed.has(o.moduleId));
      const anchor = before ? this.store.doc.nodes[placed.get(before.moduleId)] : null;
      const parent = anchor?.parent ? this.store.doc.nodes[anchor.parent] : this.store.doc.nodes[this.store.doc.root];
      const at = anchor && before.order < m.order ? parent.children.indexOf(anchor.id) + 1 : anchor ? parent.children.indexOf(anchor.id) : parent.children.length;
      const id = insertComponent(this.store, "module", { parent: parent.id, index: at, bp: "base" }, { moduleId: m.moduleId });
      if (!id) continue;
      renameNode(this.store, id, m.title || m.moduleName);
      if (anchor && layoutAt(parent, "base").mode === "grid") {
        const g = gridPlacementAt(anchor, parent, "base");
        if (!g.auto) moveGridItem(this.store, id, "base", { col: g.col, row: before.order < m.order ? g.row + g.rowSpan : g.row, colSpan: g.colSpan, rowSpan: g.rowSpan });
      }
      placed.set(m.moduleId, id);
    }
    this.replaceDocument(this.store.doc, true);
    const names = missing.map((m) => '"' + (m.title || m.moduleName) + '"').join(", ");
    this.toast((missing.length === 1 ? "Module " + names + " was" : "Modules " + names + " were") + " added to this page and now show in the layers.", "info");
  }
  /** Swaps in a whole new document (load, restore, onboarding) and resets history. */
  replaceDocument(doc, markDirty = false) {
    this.unsubscribeStore?.();
    this.store = new Store(doc);
    this.savedVersion = markDirty ? -1 : this.store.version;
    this.unsubscribeStore = this.store.subscribe(() => this.onChange());
    this.docRevision.update((n) => n + 1);
    if (markDirty) this.onChange();
  }
  onChange() {
    if (this.readOnly) return;
    const dirty = this.store.version !== this.savedVersion;
    if (!dirty) {
      if (this.saveState.value === "dirty") this.saveState.set("saved");
      return;
    }
    if (this.saveState.value === "conflict") return;
    if (this.saveState.value !== "saving") this.saveState.set("dirty");
    this.pendingChanges.set(true);
    const now = Date.now();
    if (!this.firstDirtyAt) this.firstDirtyAt = now;
    window.clearTimeout(this.saveTimer);
    const wait = Math.max(0, Math.min(AUTOSAVE_DELAY, this.firstDirtyAt + AUTOSAVE_MAX_WAIT - now));
    this.saveTimer = window.setTimeout(() => void this.save(), wait);
  }
  get isDirty() {
    return this.store.version !== this.savedVersion || this.saveState.value === "saving";
  }
  /** Saves the draft now (also used by Ctrl+S). Resolves when no save is pending. */
  async save(force = false) {
    window.clearTimeout(this.saveTimer);
    if (this.readOnly) return;
    if (this.saving) {
      await this.saving;
      if (this.store.version === this.savedVersion) return;
    }
    if (!force && this.store.version === this.savedVersion) return;
    if (this.saveState.value === "conflict" && !force) return;
    const doc = this.store.doc;
    const versionAtSave = this.store.version;
    this.saveState.set("saving");
    this.saving = (async () => {
      try {
        const res = await this.api.save(doc, this.version, force);
        this.version = res.version;
        this.savedOn.set(res.savedOn);
        this.savedVersion = versionAtSave;
        this.firstDirtyAt = 0;
        this.saveError.set("");
        this.saveState.set(this.store.version === this.savedVersion ? "saved" : "dirty");
        if (this.store.version !== this.savedVersion) this.onChange();
      } catch (e) {
        if (e instanceof ApiError && e.status === 409 && e.body && typeof e.body === "object" && "conflict" in e.body) {
          this.saveState.set("conflict");
          this.dialog.set({ kind: "conflict", conflict: e.body.conflict });
        } else if (e instanceof ApiError && e.status === 0) {
          this.saveState.set("offline");
          this.saveError.set(e.message);
          this.retryLater();
        } else {
          this.saveState.set("error");
          this.saveError.set(e instanceof Error ? e.message : String(e));
          if (!(e instanceof ApiError) || e.status >= 500) this.retryLater();
        }
      } finally {
        this.saving = null;
      }
    })();
    await this.saving;
  }
  retryDelay = 2e3;
  retryLater() {
    window.clearTimeout(this.saveTimer);
    this.saveTimer = window.setTimeout(() => void this.save(), this.retryDelay);
    this.retryDelay = Math.min(this.retryDelay * 2, 6e4);
  }
  /** Conflict: keep my version and overwrite the newer one on the server. */
  async overwrite() {
    this.dialog.set(null);
    this.saveState.set("dirty");
    await this.save(true);
  }
  /** Conflict: drop my changes and load the newer version. */
  async reloadFromServer() {
    this.dialog.set(null);
    const state = await this.api.state();
    this.applyState(state);
    this.canvasRevision.update((n) => n + 1);
  }
  async publish(note) {
    await this.save();
    if (this.saveState.value !== "saved") {
      this.toast("Save the page before publishing: " + (this.saveError.value || "there are unsaved changes."), "error");
      return;
    }
    try {
      const res = await this.api.publish(note);
      this.page.update((p) => p ? { ...p, published: true, enabled: res.enabled, publishedVersionId: res.versionId, publishedOn: res.publishedOn } : p);
      this.pendingChanges.set(false);
      this.toast("Published. Visitors now see this version.", "success");
    } catch (e) {
      this.toast(e instanceof Error ? e.message : String(e), "error");
    }
  }
  async unpublish() {
    try {
      await this.api.unpublish();
      this.page.update((p) => p ? { ...p, published: false, publishedVersionId: null } : p);
      this.pendingChanges.set(true);
      this.toast("Unpublished. Visitors see the page's default view again; your draft is kept.", "success");
    } catch (e) {
      this.toast(e instanceof Error ? e.message : String(e), "error");
    }
  }
  async setEnabled(enabled) {
    try {
      const res = await this.api.setEnabled(enabled);
      this.page.update((p) => p ? { ...p, enabled: res.enabled } : p);
      this.toast(enabled ? "The custom layout is switched on for this page." : "The page uses DNN's own layout again. Nothing was deleted.", "success");
    } catch (e) {
      this.toast(e instanceof Error ? e.message : String(e), "error");
    }
  }
  async restore(versionId) {
    await this.save();
    try {
      const res = await this.api.restore(versionId);
      this.version = res.version;
      this.savedOn.set(res.savedOn);
      this.replaceDocument(normalizeDocument(res.document));
      this.saveState.set("saved");
      this.pendingChanges.set(true);
      this.canvasRevision.update((n) => n + 1);
      this.toast("Version restored into the draft. Publish to make it live.", "success");
    } catch (e) {
      this.toast(e instanceof Error ? e.message : String(e), "error");
    }
  }
  async discardDraft() {
    try {
      const res = await this.api.discard();
      this.version = res.version;
      this.replaceDocument(res.document ? normalizeDocument(res.document) : createDocument());
      this.saveState.set("saved");
      this.pendingChanges.set(false);
      this.canvasRevision.update((n) => n + 1);
      this.toast("Unpublished changes discarded.", "success");
    } catch (e) {
      this.toast(e instanceof Error ? e.message : String(e), "error");
    }
  }
  startHeartbeat() {
    const beat = async () => {
      try {
        const res = await this.api.heartbeat();
        this.others.set(res.others);
      } catch {
      }
    };
    window.clearInterval(this.heartbeatTimer);
    void beat();
    this.heartbeatTimer = window.setInterval(beat, HEARTBEAT_MS);
  }
  setTheme(theme) {
    this.theme.set(theme);
    try {
      if (theme === "light" || theme === "dark") localStorage.setItem(THEME_KEY, theme);
      else localStorage.removeItem(THEME_KEY);
    } catch {
    }
  }
  /** Follows the admin's theme when it is changed in another tab (the storage event fires there only). */
  followAdminTheme() {
    const onStorage = (e) => {
      if (e.key === null || e.key === THEME_KEY) this.theme.set(storedTheme());
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }
  toast(text, kind = "info", action) {
    const id = ++this.toastId;
    this.toasts.update((list) => [...list.slice(-3), { id, text, kind, action }]);
    window.setTimeout(() => this.toasts.update((list) => list.filter((t) => t.id !== id)), kind === "error" ? 8e3 : 4e3);
  }
  dispose() {
    window.clearTimeout(this.saveTimer);
    window.clearInterval(this.heartbeatTimer);
    this.unsubscribeStore?.();
    this.api.leave();
  }
}
function useText(session) {
  return (key, fallback) => session.config.text?.[key] ?? fallback;
}
export {
  DEVICE_WIDTHS,
  Session,
  useText
};
