/**
 * Content lists in the inspector (.docs/features/structured-content.md#content-lists): the Data panel of a list (which
 * content type, one entry or many, filters, sort, how many, how it is displayed, where an entry links to) and the
 * binding of an element's props to fields of the entry it shows ("Image ← Product image"). Everything here edits the
 * document; the entries themselves are fetched by editor/content.js and previewed on the canvas.
 */
import { jsx, useEffect, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { NumberField, Row, Section, Segmented, SelectField, TextField, Toggle } from "./fields.js";
import { cls, useEditor, useSignal } from "./hooks.js";
import { setBindings, updateLayout, updateProps } from "../core/commands.js";
import { layoutAt } from "../core/responsive.js";
import { BINDABLE_TYPES, FILTER_OPS } from "../core/validate.js";

const OP_LABELS = { eq: "is", neq: "is not", contains: "contains", gt: "is more than", gte: "is at least", lt: "is less than", lte: "is at most", empty: "is empty", notEmpty: "is not empty" };

/** Which field kinds suit a prop type, best first: the binding menu lists those first. */
const KINDS = {
  image: ["image"],
  url: ["url", "file", "text"],
  rich: ["rich", "text", "number", "date"],
  inline: ["text", "number", "date", "boolean", "rich"],
  text: ["text", "number", "date", "boolean", "rich", "url"],
  video: ["url", "text"],
};

/* ---------------------------------------------------------------- the list */

export function DataPanel({ node }) {
  const { session } = useEditor();
  const content = session.content;
  const store = session.store;
  const caps = useSignal(session.caps);
  const types = useSignal(content.types);
  const data = useSignal(content.data);
  const loading = useSignal(content.loading);
  const error = useSignal(content.error);
  const targets = useSignal(content.linkTargets);
  useEffect(() => { void content.loadTypes(); }, []);
  const disabled = !caps.editLayout || !!node.locked;
  const p = node.props;
  const type = types?.find((t) => t.id === p.source);
  const set = (patch) => updateProps(store, node.id, patch);
  const shown = data?.[node.id];

  if (types === null) {
    return jsx("div", { class: "pe-hint pe-data-loading" }, "Loading content types…");
  }
  if (types.length === 0) {
    return jsx(Section, { title: "Content" },
      jsx("div", { class: "pe-note" }, jsx(Icon, { name: "database", size: 16 }), " There are no content types yet."),
      jsx("div", { class: "pe-hint" }, "A content list shows entries of a content type - products, blog posts, events. Create one, add entries, then come back."),
      jsx("a", { class: "pe-btn", href: "/admin/content-types/new", target: "_blank", rel: "noopener" }, jsx(Icon, { name: "plus", size: 16 }), " New content type"));
  }

  // On a detail page (its URL has a [param] segment) one entry means the one the URL names.
  const urlParam = /\[([a-z][a-z0-9_-]*)\]/.exec(session.page.value?.url ?? "")?.[1];
  const chooseType = (id) => {
    const next = types.find((t) => t.id === id);
    const route = p.mode === "single" && urlParam && !p.routeParam ? { routeParam: urlParam } : {};
    set({ source: id ?? "", filters: [], sortField: "", entry: "", columns: [], ...route });
    if (next) autoBind(session, node, next);
  };

  return jsx("div", { class: "pe-data" },
    jsx(Section, { title: "Content" },
      jsx(Row, { label: "Content type", wide: true },
        jsx(SelectField, { value: p.source || undefined, placeholder: "Choose what to show", disabled, options: types.map((t) => ({ value: t.id, label: t.name })), onChange: chooseType })),
      type && jsx(StatusLine, { type, shown, loading, error, mode: p.mode })),
    type && jsx(Section, { title: "Entries" },
      jsx(Row, { label: "Show", wide: true },
        jsx(Segmented, { value: p.mode, disabled, options: [{ value: "list", label: "Many" }, { value: "single", label: "One" }],
          onChange: (v) => set({ mode: v, limit: v === "single" ? 1 : Math.max(p.limit, 6), ...(v === "single" && urlParam && !p.routeParam && !p.entry ? { routeParam: urlParam } : {}) }) })),
      p.mode === "single"
        ? jsx(SingleEntry, { node, type, disabled, set, targets })
        : [
          jsx(Filters, { key: "f", filters: p.filters ?? [], type, disabled, onChange: (filters) => set({ filters }), latest: () => session.store.doc.nodes[node.id]?.props.filters ?? [] }),
          jsx(Row, { key: "s", label: "Sort by", label2: "Order" },
            jsx(SelectField, { value: p.sortField ?? "", disabled, options: type.sorts.map((s) => ({ value: s.key, label: s.label })), onChange: (v) => set({ sortField: v ?? "" }) }),
            jsx(Segmented, { value: p.sortDir, disabled, options: [{ value: "asc", label: "A→Z", title: "Ascending" }, { value: "desc", label: "Z→A", title: "Descending" }], onChange: (v) => set({ sortDir: v }) })),
          jsx(Row, { key: "l", label: "Items" },
            jsx(NumberField, { value: p.limit, min: 1, max: 100, decimals: 0, disabled, title: "How many entries (per page with pagination)", onChange: (v, m) => v && updateProps(store, node.id, { limit: v }, m) })),
          jsx(Row, { key: "pg", label: "" },
            jsx(Toggle, { checked: p.paginate === true, disabled, label: "Pages (Previous / Next links)", onChange: (v) => set({ paginate: v }) }))
        ]),
    type && jsx(Display, { node, type, disabled }),
    type && jsx(ItemLink, { node, type, disabled, targets, set }),
    type && jsx(Section, { title: "Texts" },
      jsx(Row, { label: "When nothing matches", wide: true },
        jsx(TextField, { value: p.emptyText, maxLength: 300, disabled: !caps.editContent, onChange: (v) => set({ emptyText: v }) })),
      p.paginate && jsx(Row, { label: "Previous", label2: "Next" },
        jsx(TextField, { value: p.prevLabel, maxLength: 60, disabled: !caps.editContent, onChange: (v) => set({ prevLabel: v }) }),
        jsx(TextField, { value: p.nextLabel, maxLength: 60, disabled: !caps.editContent, onChange: (v) => set({ nextLabel: v }) }))),
    type && jsx(Section, { title: "Fields" },
      jsx("div", { class: "pe-hint" }, "Select an element inside the item (the first copy on the canvas) to choose which field it shows. The others are copies of it, one per entry."),
      jsx("button", { type: "button", class: "pe-btn", disabled, onClick: () => autoBind(session, node, type, true) }, jsx(Icon, { name: "bind", size: 16 }), " Map fields automatically")));
}

function StatusLine({ type, shown, loading, error, mode }) {
  const count = shown?.total ?? 0;
  return jsx("div", { class: "pe-data-status" },
    error ? jsx("div", { class: "pe-note" }, jsx(Icon, { name: "alert", size: 16 }), " ", error)
      : loading ? jsx("div", { class: "pe-hint" }, "Loading entries…")
        : jsx("div", { class: "pe-hint" }, type.published === 0
          ? "No published entries yet: the canvas shows the bare design. "
          : mode === "single" ? "Showing one of " + type.published + " published entries. " : count + " of " + type.published + " published entries match. ",
          jsx("a", { href: "/admin/content/" + type.slug, target: "_blank", rel: "noopener" }, "Manage " + type.name)));
}

function SingleEntry({ node, type, disabled, set, targets }) {
  const { session } = useEditor();
  const p = node.props;
  const source = p.routeParam ? "url" : p.entry ? "fixed" : "first";
  const [entries, setEntries] = useState(null);
  useEffect(() => {
    if (source !== "fixed") return;
    let live = true;
    session.api.contentEntries(type.id, "").then((list) => live && setEntries(list)).catch(() => live && setEntries([]));
    return () => { live = false; };
  }, [type.id, source]);
  const params = [...new Set(targets.map((t) => t.param))];
  return [
    jsx(Row, { key: "w", label: "Which entry", wide: true },
      jsx(SelectField, { value: source, disabled, options: [
        { value: "url", label: "The one named in the page URL" },
        { value: "fixed", label: "A specific entry" },
        { value: "first", label: "The first that matches" },
      ], onChange: (v) => set(v === "url" ? { routeParam: params[0] ?? "slug", entry: "" } : v === "fixed" ? { routeParam: "", entry: "" } : { routeParam: "", entry: "" }) })),
    source === "url" && [
      jsx(Row, { key: "rp", label: "URL part", label2: "Matches field" },
        jsx(TextField, { value: p.routeParam, mono: true, disabled, placeholder: "slug", validate: (v) => (/^[a-z][a-z0-9_-]*$/.test(v) ? null : "Lower-case letters, digits, - and _."), onChange: (v) => set({ routeParam: v }) }),
        jsx(SelectField, { value: p.routeField || "slug", disabled, options: [{ value: "$id", label: "Entry id" }, ...type.filters.filter((f) => f.key !== "$title").map((f) => ({ value: f.key, label: f.label }))], onChange: (v) => set({ routeField: v ?? "slug" }) })),
      jsx("div", { key: "rh", class: "pe-hint" }, "Put this list on a page whose slug is ", jsx("code", { class: "pe-code" }, "[" + (p.routeParam || "slug") + "]"),
        " (e.g. /products/[" + (p.routeParam || "slug") + "]). Visitors to /products/red-chair see the entry whose field is red-chair; an unknown one is a 404. The editor previews the first entry.")
    ],
    source === "fixed" && jsx(Row, { key: "e", label: "Entry", wide: true },
      entries === null ? jsx("div", { class: "pe-hint" }, "Loading…")
        : jsx(SelectField, { value: p.entry || undefined, placeholder: "Choose an entry", disabled, options: entries.map((e) => ({ value: e.id, label: e.title + (e.published ? "" : " (draft)") })), onChange: (v) => set({ entry: v ?? "" }) })),
    source === "first" && jsx(Filters, { key: "f", filters: p.filters ?? [], type, disabled, onChange: (filters) => set({ filters }) })
  ];
}

function Filters({ filters, type, disabled, onChange, latest }) {
  const latestFilters = () => latest?.() ?? filters;
  const update = (i, patch) => onChange(filters.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  return jsx(Row, { label: "Filters", wide: true },
    jsx("div", { class: "pe-filters" },
      filters.length === 0 && jsx("div", { class: "pe-hint" }, "Every published entry. Add a filter to narrow it down."),
      filters.map((f, i) => {
        const field = type.filters.find((x) => x.key === f.field);
        const noValue = f.op === "empty" || f.op === "notEmpty";
        return jsx("div", { key: i, class: "pe-filter" },
          jsx(SelectField, { value: f.field, disabled, title: "Field", options: type.filters.map((x) => ({ value: x.key, label: x.label })), onChange: (v) => update(i, { field: v, value: "" }) }),
          jsx(SelectField, { value: f.op, disabled, title: "Condition", options: FILTER_OPS.map((op) => ({ value: op, label: OP_LABELS[op] })), onChange: (v) => update(i, { op: v }) }),
          !noValue && (field?.choices?.length
            ? jsx(SelectField, { value: f.value || undefined, disabled, placeholder: "Choose", title: "Value", options: field.choices.map((c) => ({ value: c.value, label: c.label })), onChange: (v) => update(i, { value: v ?? "" }) })
            : field?.type === "boolean"
              ? jsx(SelectField, { value: f.value || undefined, disabled, placeholder: "Choose", title: "Value", options: [{ value: "true", label: "Yes" }, { value: "false", label: "No" }], onChange: (v) => update(i, { value: v ?? "" }) })
              : jsx(TextField, { value: f.value, disabled, maxLength: 200, ariaLabel: "Value", placeholder: field?.type === "date" ? "YYYY-MM-DD" : field?.type === "relationship" ? "Entry id" : "Value", onChange: (v) => update(i, { value: v }) })),
          jsx("button", { type: "button", class: "pe-icon-btn", title: "Remove filter", disabled, onClick: () => onChange(filters.filter((_, j) => j !== i)) }, jsx(Icon, { name: "trash", size: 16 })));
      }),
      jsx("button", { type: "button", class: "pe-btn", disabled: disabled || filters.length >= 10, onClick: () => onChange([...latestFilters(), { field: type.filters[0]?.key ?? "$title", op: "eq", value: "" }]) },
        jsx(Icon, { name: "plus", size: 16 }), " Add filter")));
}

/** Grid, list, carousel or table: sets the list's layout (the item copies are its children) and view. */
function Display({ node, type, disabled }) {
  const { session } = useEditor();
  const store = session.store;
  const bp = useSignal(session.breakpoint);
  const p = node.props;
  const layout = layoutAt(node, bp);
  const current = p.view === "table" ? "table" : p.carousel ? "carousel" : layout.mode === "grid" ? "grid" : "list";
  const choose = (v) => {
    if (v === "table") return updateProps(store, node.id, { view: "table", carousel: false });
    updateProps(store, node.id, { view: "items", carousel: v === "carousel" });
    if (v === "grid") updateLayout(store, node.id, "base", { mode: "grid", columns: layoutAt(node, "base").columns ?? 3, colGap: 24, rowGap: 24, rowMode: "auto" });
    if (v === "list") updateLayout(store, node.id, "base", { mode: "stack", direction: "column", gap: 20 });
    if (v === "carousel") updateLayout(store, node.id, "base", { mode: "stack", direction: "row", gap: 20, wrap: false });
  };
  const columns = p.columns ?? [];
  // Read the stored list at click time: two quick clicks must not both start from the same render.
  const latest = () => session.store.doc.nodes[node.id]?.props.columns ?? [];
  const setColumns = (next) => updateProps(store, node.id, { columns: next });
  return jsx(Section, { title: "Display" },
    jsx(Row, { label: "Look", wide: true },
      jsx(Segmented, { value: current, disabled, onChange: choose, options: [
        { value: "grid", icon: "cards", title: "Grid" },
        { value: "list", icon: "rows", title: "List" },
        { value: "carousel", icon: "carousel", title: "Carousel" },
        { value: "table", icon: "table", title: "Table" },
      ] })),
    current === "grid" && jsx(Row, { label: "Columns", label2: bp === "base" ? "Desktop" : bp === "tablet" ? "Tablet" : "Mobile" },
      jsx(NumberField, { value: layout.columns ?? 3, min: 1, max: 12, decimals: 0, disabled, title: "Columns on this device", onChange: (v, m) => v && updateLayout(store, node.id, bp, { columns: v }, m) })),
    current !== "table" && jsx("div", { class: "pe-hint" }, "Design one item - its picture, title, price - in the layers below the list; every entry repeats it. Gaps and columns per device are in the Design tab."),
    current === "table" && jsx(Row, { label: "Columns", wide: true },
      jsx("div", { class: "pe-filters" },
        columns.length === 0 && jsx("div", { class: "pe-hint" }, "Shows the title. Add the columns you want; the first one links to the entry."),
        columns.map((c, i) => jsx("div", { key: i, class: "pe-filter" },
          jsx(SelectField, { value: c.field, disabled, title: "Field", options: type.fields.filter((f) => f.kind !== "rich").map((f) => ({ value: f.path, label: f.label })), onChange: (v) => setColumns(columns.map((x, j) => (j === i ? { field: v, label: x.label || labelOf(type, v) } : x))) }),
          jsx(TextField, { value: c.label, disabled, maxLength: 100, ariaLabel: "Column heading", placeholder: "Heading", onChange: (v) => setColumns(columns.map((x, j) => (j === i ? { ...x, label: v } : x))) }),
          jsx("button", { type: "button", class: "pe-icon-btn", title: "Remove column", disabled, onClick: () => setColumns(columns.filter((_, j) => j !== i)) }, jsx(Icon, { name: "trash", size: 16 })))),
        jsx("button", { type: "button", class: "pe-btn", disabled: disabled || columns.length >= 12, onClick: () => {
          const current = latest();
          const used = new Set(current.map((c) => c.field));
          const next = type.fields.find((f) => !used.has(f.path) && f.kind !== "rich" && f.path !== "$url") ?? type.fields[0];
          if (current.length < 12) setColumns([...current, { field: next.path, label: next.label }]);
        } }, jsx(Icon, { name: "plus", size: 16 }), " Add column"))));
}

function ItemLink({ node, type, disabled, targets, set }) {
  const p = node.props;
  const fieldKeys = new Set(type.fields.map((f) => f.path));
  return jsx(Section, { title: "Item link" },
    targets.length > 0 && jsx(Row, { label: "Detail page", wide: true },
      jsx(SelectField, { value: targets.some((t) => t.pattern === p.itemUrl) ? p.itemUrl : undefined, placeholder: "Choose a page…", disabled,
        options: targets.map((t) => ({ value: t.pattern, label: t.label })), onChange: (v) => set({ itemUrl: v ?? "" }) })),
    jsx(Row, { label: "Link pattern", wide: true },
      jsx(TextField, { value: p.itemUrl, mono: true, disabled, maxLength: 300, placeholder: "/products/{slug}", onChange: (v) => set({ itemUrl: v.trim() }) })),
    jsx("div", { class: cls("pe-hint", unknownPlaceholder(p.itemUrl, fieldKeys) && "is-warning") },
      unknownPlaceholder(p.itemUrl, fieldKeys)
        ? "\"{" + unknownPlaceholder(p.itemUrl, fieldKeys) + "}\" is not a field of " + type.name + "."
        : "Each entry's link: {field} is replaced by the entry's value. Bind a button or image link to \"Item link\" to use it."));
}

function unknownPlaceholder(pattern, keys) {
  for (const m of String(pattern || "").matchAll(/\{([^{}]+)\}/g)) if (!keys.has(m[1])) return m[1];
  return null;
}

function labelOf(type, path) {
  return type.fields.find((f) => f.path === path)?.label ?? path;
}

/* ---------------------------------------------------------------- binding one prop */

/**
 * "Shows: Product name" for a prop of an element inside a content list item. A binding is a template: "{name}" shows
 * the field, "From {price} USD" mixes text and fields, "/products/{slug}" builds a link.
 */
export function BindingRow({ node, prop, def, children }) {
  const { session } = useEditor();
  const content = session.content;
  const types = useSignal(content.types);
  const caps = useSignal(session.caps);
  useEffect(() => { void content.loadTypes(); }, []);
  const list = content.listOf(node.id);
  const type = list && types?.find((t) => t.id === list.props.source);
  if (!list || !BINDABLE_TYPES.has(def.type)) return children;
  const template = node.bind?.[prop] ?? "";
  const exact = /^\{([^{}]+)\}$/.exec(template);
  const disabled = !caps.editLayout || !!node.locked;
  const set = (value) => setBindings(session.store, { [node.id]: { [prop]: value } }, value ? "Bind " + def.label : "Unbind " + def.label);
  const fields = type ? orderFields(type.fields, def.type) : [];
  const custom = template && !exact;
  const options = [
    { value: "", label: "Fixed value (not from content)" },
    ...fields.map((f) => ({ value: "{" + f.path + "}", label: f.label })),
    { value: "custom", label: "Text with fields…" },
  ];
  return jsx("div", { class: cls("pe-binding", template && "is-bound") },
    jsx(Row, { label: jsx("span", { class: "pe-binding-label" }, jsx(Icon, { name: "bind", size: 14 }), " ", def.label, template ? " · from content" : ""), wide: true },
      jsx(SelectField, { value: custom ? "custom" : template, disabled: disabled || !type, title: def.label + " comes from", options,
        onChange: (v) => set(v === "custom" ? (template || "{" + (fields[0]?.path ?? "$title") + "}") : v ?? "") }),
      !type && jsx("div", { class: "pe-hint" }, "Choose the list's content type first."),
      (custom || (template && def.type !== "image")) && jsx(TextField, { value: template, mono: true, disabled, maxLength: 300, placeholder: "From {price} USD",
        validate: (v) => (v.includes("{") ? null : "Put a field in braces, e.g. {name}."), onChange: set }),
      template && fields.length > 0 && jsx("div", { class: "pe-hint" }, "Fields: ", fields.slice(0, 8).map((f) => "{" + f.path + "}").join(" "))),
    !template && children);
}

function orderFields(fields, propType) {
  const kinds = KINDS[propType] ?? ["text"];
  const rank = (f) => {
    const i = kinds.indexOf(f.kind);
    return i < 0 ? kinds.length : i;
  };
  return fields.filter((f) => rank(f) < kinds.length || propType === "text" || propType === "inline").sort((a, b) => rank(a) - rank(b));
}

/* ---------------------------------------------------------------- automatic mapping */

/**
 * Binds the item template's elements to the type's fields by what they are: the first image to the first image field,
 * the first heading to the title, a text to the description, a price-like line to a number, buttons to the item link.
 * Elements that are already bound keep their binding unless `force`.
 */
function autoBind(session, list, type, force = false) {
  const doc = session.store.doc;
  const by = (kind) => type.fields.filter((f) => f.kind === kind && !f.path.includes(".$"));
  const images = by("image");
  const rich = by("rich");
  const texts = type.fields.filter((f) => f.kind === "text" && f.path !== "$title" && !f.path.includes("."));
  const numbers = by("number");
  const patches = {};
  let headings = 0;
  let paragraphs = 0;
  const walk = (id) => {
    const n = doc.nodes[id];
    if (!n) return;
    const unbound = (prop) => force || !n.bind?.[prop];
    const add = (prop, template) => {
      if (template && unbound(prop)) (patches[n.id] ??= {})[prop] = template;
    };
    if (n.type === "image" && images[0]) {
      add("src", "{" + images[0].path + "}");
      add("alt", "{$title}");
      if (list.props.itemUrl) add("href", "{$url}");
    } else if (n.type === "heading") {
      add("text", headings === 0 ? "{$title}" : numbers[headings - 1] ? "{" + numbers[headings - 1].path + "}" : null);
      headings++;
    } else if (n.type === "text") {
      const source = paragraphs === 0 ? rich[0] ?? texts[0] : numbers[paragraphs - 1] ?? texts[paragraphs];
      if (source) add("content", "{" + source.path + "}");
      paragraphs++;
    } else if (n.type === "button") {
      add("href", "{$url}");
    }
    for (const c of n.children ?? []) walk(c);
  };
  for (const c of list.children ?? []) walk(c);
  if (Object.keys(patches).length) setBindings(session.store, patches, "Map fields");
}
