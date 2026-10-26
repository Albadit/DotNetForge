// Structured content in the editor (.docs/features/structured-content.md#content-lists): the content types
// (fields a node can be bound to, fields a list can filter and sort by) and the entries each content list shows, so the
// canvas previews real content. Entries are fetched from the server whenever a list's query changes (source, filters,
// sort, limit...) - the same query visitors get - and handed to the renderer as ctx.data.
import { signal } from "../core/signal.js";

/** The props that decide which entries a content list shows; changing any of them refetches. */
const QUERY_PROPS = ["source", "mode", "entry", "routeParam", "routeField", "filters", "sortField", "sortDir", "limit", "paginate", "itemUrl"];
const FETCH_DELAY = 300;

class ContentSource {
  constructor(session) {
    this.session = session;
  }
  session;
  /** The content types with their bindable fields, or null until loaded. */
  types = signal(null);
  /** Pages with a [param] segment, as item link patterns: [{ pattern, label }]. */
  linkTargets = signal([]);
  /** Entries per content list node: { [nodeId]: { items, total, page, pageCount } }. */
  data = signal({});
  loading = signal(false);
  error = signal("");
  keys = new Map();
  timer = 0;
  offStore = null;
  started = false;

  /** Starts following the document; types load on first use (an editor without content lists never asks). */
  start() {
    if (this.started) return;
    this.started = true;
    const follow = () => {
      this.offStore?.();
      this.offStore = this.session.store.subscribe(() => this.schedule());
      this.keys.clear();
      this.schedule();
    };
    this.session.docRevision.subscribe(follow);
    follow();
  }

  /** The content list nodes of the document. */
  lists() {
    return Object.values(this.session.store.doc.nodes).filter((n) => n.type === "collection");
  }

  schedule() {
    const lists = this.lists();
    if (lists.length && this.types.value === null) void this.loadTypes();
    const changed = lists.some((n) => this.keys.get(n.id) !== keyOf(n)) || [...this.keys.keys()].some((id) => !lists.some((n) => n.id === id));
    if (!changed) return;
    window.clearTimeout(this.timer);
    this.timer = window.setTimeout(() => void this.fetch(), FETCH_DELAY);
  }

  async fetch() {
    const lists = this.lists();
    const keys = new Map(lists.map((n) => [n.id, keyOf(n)]));
    if (lists.every((n) => !n.props.source)) {
      this.keys = keys;
      this.data.set({});
      return;
    }
    this.loading.set(true);
    try {
      const data = await this.session.api.contentPreview(lists.filter((n) => n.props.source).map((n) => ({ id: n.id, props: pick(n.props) })));
      this.keys = keys;
      this.error.set("");
      this.data.set(data && typeof data === "object" ? data : {});
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : String(e));
    } finally {
      this.loading.set(false);
    }
  }

  async loadTypes(force = false) {
    if (this.types.value !== null && !force) return this.types.value;
    try {
      const [types, targets] = await Promise.all([this.session.api.contentTypes(), this.session.api.linkTargets()]);
      this.types.set(Array.isArray(types) ? types : []);
      this.linkTargets.set(Array.isArray(targets) ? targets : []);
    } catch (e) {
      this.types.set([]);
      this.error.set(e instanceof Error ? e.message : String(e));
    }
    return this.types.value;
  }

  typeById(id) {
    return (this.types.value ?? []).find((t) => t.id === id);
  }

  /** The content list a node is inside (its item template's list), or undefined. */
  listOf(id) {
    const doc = this.session.store.doc;
    let cur = doc.nodes[id]?.parent;
    while (cur) {
      const n = doc.nodes[cur];
      if (!n) return void 0;
      if (n.type === "collection") return n;
      cur = n.parent;
    }
    return void 0;
  }
}

function pick(props) {
  const out = {};
  for (const k of QUERY_PROPS) out[k] = props[k];
  return out;
}
function keyOf(node) {
  return JSON.stringify(QUERY_PROPS.map((k) => node.props[k]));
}

export {
  ContentSource
};
