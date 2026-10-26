import { createNode } from "../core/doc.js";
function build(spec) {
  const out = [];
  let n = 0;
  const make = (s, parent) => {
    const id = "tmp" + (n++).toString(36);
    const node = createNode(s[0], id, parent);
    const patch = s[1];
    if (patch.props) node.props = { ...node.props, ...patch.props };
    Object.assign(node, { ...patch, props: node.props, id, parent });
    out.push(node);
    if (s[2]) node.children = s[2].map((c) => make(c, id));
    return id;
  };
  make(spec, null);
  return out;
}
const text = (t) => [{ type: "p", runs: [{ text: t }] }];
/**
 * Content lists (.docs/features/structured-content.md#content-lists): a list and its item template, designed like
 * any layout. The props are bound to fields when a content type is chosen (ui/data-panel.js autoBind).
 */
const card = { padding: [0, 0, 20, 0], background: "var(--dnn-color-surface, #ffffff)", radius: [14, 14, 14, 14], shadow: "md", overflow: "hidden" };
// The list sits in its own section (a full-width band with a stack layout); inserting it selects the list itself.
const listOf = (name, props, layout, item) => build(["section", { name: name + " section", layout: { base: { mode: "stack", direction: "column", gap: 24 } } }, [
  ["collection", { name, props: { limit: 6, ...props }, layout }, [item]]
]]);
const CONTENT_PRESETS = [
  {
    id: "content-cards",
    group: "Content lists",
    label: "Card grid",
    description: "Entries as cards in a responsive grid: picture, title, text and a link. 3 columns, 2 on tablets, 1 on phones.",
    icon: "cards",
    build: () => listOf("Card grid", {}, { base: { mode: "grid", columns: 3, colGap: 24, rowGap: 24, rowMode: "auto" }, tablet: { columns: 2 }, mobile: { columns: 1 } },
      ["item", { name: "Card", layout: { base: { mode: "stack", direction: "column", gap: 12 } }, style: { base: card } }, [
        ["image", { style: { base: { height: { mode: "fixed", value: 200 } } } }],
        ["heading", { props: { text: [{ text: "Title" }], level: "3" }, style: { base: { padding: [0, 20, 0, 20], fontSize: 20 } } }],
        ["text", { props: { content: text("A short description.") }, style: { base: { padding: [0, 20, 0, 20] } } }],
        ["button", { props: { label: "Read more", href: "#", variant: "link" }, style: { base: { margin: [0, 20, 0, 20] } } }]
      ]])
  },
  {
    id: "content-list",
    group: "Content lists",
    label: "List",
    description: "Entries one below the other, a picture beside the text. Stacks on phones.",
    icon: "rows",
    build: () => listOf("List", {}, { base: { mode: "stack", direction: "column", gap: 20 } },
      ["item", { name: "Row", layout: { base: { mode: "stack", direction: "row", gap: 20, align: "center" }, mobile: { direction: "column", align: "stretch" } } }, [
        ["image", { style: { base: { width: { mode: "fixed", value: 200 }, height: { mode: "fixed", value: 140 }, radius: [10, 10, 10, 10] }, mobile: { width: { mode: "fill" } } } }],
        ["container", { name: "Text", layout: { base: { mode: "stack", direction: "column", gap: 8 } }, style: { base: { width: { mode: "fill" }, padding: [0, 0, 0, 0] } } }, [
          ["heading", { props: { text: [{ text: "Title" }], level: "3" }, style: { base: { fontSize: 20 } } }],
          ["text", { props: { content: text("A short description.") } }],
          ["button", { props: { label: "Read more", href: "#", variant: "link" } }]
        ]]
      ]])
  },
  {
    id: "content-carousel",
    group: "Content lists",
    label: "Carousel",
    description: "Cards in one row that scrolls sideways; swipe on phones.",
    icon: "carousel",
    build: () => listOf("Carousel", { carousel: true }, { base: { mode: "stack", direction: "row", gap: 20, align: "stretch" } },
      ["item", { name: "Slide", layout: { base: { mode: "stack", direction: "column", gap: 12 } }, style: { base: { ...card, width: { mode: "fixed", value: 300 } }, mobile: { width: { mode: "fixed", value: 260 } } } }, [
        ["image", { style: { base: { height: { mode: "fixed", value: 180 } } } }],
        ["heading", { props: { text: [{ text: "Title" }], level: "3" }, style: { base: { padding: [0, 20, 0, 20], fontSize: 18 } } }],
        ["button", { props: { label: "View", href: "#", variant: "link" }, style: { base: { margin: [0, 20, 0, 20] } } }]
      ]])
  },
  {
    id: "content-table",
    group: "Content lists",
    label: "Table",
    description: "Entries as table rows; choose the columns. The first column links to the entry.",
    icon: "table",
    build: () => listOf("Table", { view: "table", limit: 20, paginate: true }, { base: { mode: "stack", direction: "column", gap: 16 } },
      ["item", { name: "Item", layout: { base: { mode: "stack", direction: "column", gap: 8 } } }, [
        ["heading", { props: { text: [{ text: "Title" }], level: "3" } }]
      ]])
  },
  {
    id: "content-detail",
    group: "Content lists",
    label: "Detail",
    description: "One entry in full - for a page like /products/[slug] that shows the entry its URL names.",
    icon: "detail",
    build: () => listOf("Detail", { mode: "single", limit: 1 }, { base: { mode: "stack", direction: "column", gap: 0 } },
      ["item", { name: "Entry", layout: { base: { mode: "stack", direction: "column", gap: 20 } } }, [
        ["heading", { props: { text: [{ text: "Title" }], level: "1" }, style: { base: { fontSize: 44 }, mobile: { fontSize: 32 } } }],
        ["image", { style: { base: { height: { mode: "fixed", value: 420 }, radius: [16, 16, 16, 16] }, mobile: { height: { mode: "fixed", value: 240 } } } }],
        ["text", { props: { content: text("The entry's text.") }, style: { base: { fontSize: 18, maxWidth: 760 } } }]
      ]])
  }
];
const PRESETS = [
  ...CONTENT_PRESETS,
  {
    id: "hero",
    label: "Hero",
    description: "A big title, a short text and two buttons, centered on a colored band.",
    icon: "section",
    build: () => build(["section", {
      name: "Hero",
      props: { tag: "section", contentWidth: 880, anchor: "" },
      layout: { base: { mode: "stack", direction: "column", gap: 20, align: "center", justify: "center" } },
      style: { base: { padding: [96, 24, 96, 24], background: "var(--dnn-color-primary)", color: "var(--dnn-color-primary-contrast)", textAlign: "center" }, mobile: { padding: [56, 16, 56, 16] } }
    }, [
      ["heading", { props: { text: [{ text: "A clear promise in a few words" }], level: "1" }, style: { base: { fontSize: 52, fontWeight: "800", lineHeight: 1.1 }, mobile: { fontSize: 34 } } }],
      ["text", { props: { content: text("Explain in one or two sentences what visitors find here and why it matters to them.") }, style: { base: { fontSize: 19, maxWidth: 640, opacity: 0.9 } } }],
      ["container", { name: "Buttons", layout: { base: { mode: "stack", direction: "row", gap: 12, justify: "center", wrap: true } }, style: { base: { padding: [8, 0, 0, 0] } } }, [
        ["button", { props: { label: "Get started", href: "#", variant: "secondary" } }],
        ["button", { props: { label: "Learn more", href: "#", variant: "outline" }, style: { base: { width: { mode: "hug" }, color: "inherit" } } }]
      ]]
    ]])
  },
  {
    id: "two-columns",
    label: "Text and image",
    description: "Two columns: text on the left, a picture on the right. Stacks on phones.",
    icon: "stackH",
    build: () => build(["section", {
      name: "Text and image",
      layout: { base: { mode: "grid", columns: 12, colGap: 48, rowGap: 24, rowHeight: 40, rowMode: "auto", compact: true }, mobile: { columns: 1 } }
    }, [
      ["container", { name: "Text", place: { base: { col: 1, row: 1, colSpan: 6, rowSpan: 6 } }, layout: { base: { mode: "stack", direction: "column", gap: 16, justify: "center" } }, style: { base: { padding: [0, 0, 0, 0] } } }, [
        ["heading", { props: { text: [{ text: "Tell your story" }], level: "2" } }],
        ["text", { props: { content: text("Write a paragraph that supports the picture. Keep it short; people scan before they read.") } }],
        ["button", { props: { label: "Read more", href: "#", variant: "link" } }]
      ]],
      ["image", { name: "Picture", place: { base: { col: 7, row: 1, colSpan: 6, rowSpan: 6 } }, style: { base: { radius: [16, 16, 16, 16], minHeight: 280 } } }]
    ]])
  },
  {
    id: "cards",
    label: "Three cards",
    description: "Three equal cards with a picture, a title and a link; 2 on tablets, 1 on phones.",
    icon: "grid",
    build: () => {
      const card = (n) => ["container", {
        name: "Card " + n,
        layout: { base: { mode: "stack", direction: "column", gap: 12 } },
        style: { base: { padding: [0, 0, 20, 0], background: "var(--dnn-color-surface, #ffffff)", radius: [14, 14, 14, 14], shadow: "md", overflow: "hidden" } },
        place: { base: { colSpan: 1, rowSpan: 1 } }
      }, [
        ["image", { style: { base: { height: { mode: "fixed", value: 180 } } } }],
        ["heading", { props: { text: [{ text: "Card title" }], level: "3" }, style: { base: { padding: [0, 20, 0, 20], fontSize: 20 } } }],
        ["text", { props: { content: text("A short description of what this card links to.") }, style: { base: { padding: [0, 20, 0, 20] } } }],
        ["button", { props: { label: "Open", href: "#", variant: "link" }, style: { base: { margin: [0, 20, 0, 20] } } }]
      ]];
      return build(["section", {
        name: "Cards",
        layout: { base: { mode: "grid", columns: 3, colGap: 24, rowGap: 24, rowHeight: 40, rowMode: "auto" }, tablet: { columns: 2 }, mobile: { columns: 1 } }
      }, [card(1), card(2), card(3)]]);
    }
  },
  {
    id: "dashboard",
    label: "Dashboard grid",
    description: "A 12-column grid with a banner (8×2), a side panel (4×4) and wide blocks, like a community dashboard.",
    icon: "dashboard",
    build: () => build(["section", {
      name: "Dashboard",
      layout: { base: { mode: "grid", columns: 12, rowHeight: 110, rowMode: "fixed", colGap: 16, rowGap: 16, compact: true }, tablet: { columns: 6 }, mobile: { columns: 1, rowMode: "auto" } },
      style: { base: { padding: [24, 24, 24, 24] } }
    }, [
      ["image", { name: "Banner", place: { base: { col: 1, row: 1, colSpan: 8, rowSpan: 2 } }, style: { base: { radius: [12, 12, 12, 12] } } }],
      ["container", { name: "Recent activity", place: { base: { col: 9, row: 1, colSpan: 4, rowSpan: 4 } }, style: { base: { background: "var(--dnn-color-surface, #ffffff)", radius: [12, 12, 12, 12], shadow: "sm", overflow: "auto" } } }, [
        ["heading", { props: { text: [{ text: "Recent activity" }], level: "3" }, style: { base: { fontSize: 18 } } }],
        ["text", { props: { content: [{ type: "ul", items: [[{ text: "Placeholder item" }], [{ text: "Another item" }]] }] } }]
      ]],
      ["container", { name: "Discover", place: { base: { col: 1, row: 3, colSpan: 8, rowSpan: 2 } }, style: { base: { background: "var(--dnn-color-surface, #ffffff)", radius: [12, 12, 12, 12], shadow: "sm" } } }, [
        ["heading", { props: { text: [{ text: "Discover" }], level: "3" }, style: { base: { fontSize: 18 } } }],
        ["links", {}]
      ]],
      ["container", { name: "Groups", place: { base: { col: 1, row: 5, colSpan: 12, rowSpan: 2 } }, style: { base: { background: "var(--dnn-color-surface, #ffffff)", radius: [12, 12, 12, 12], shadow: "sm" } } }, [
        ["heading", { props: { text: [{ text: "Groups" }], level: "3" }, style: { base: { fontSize: 18 } } }]
      ]]
    ]])
  },
  {
    id: "cta",
    label: "Call to action",
    description: "A line of text and a button side by side; stacked on phones.",
    icon: "button",
    build: () => build(["section", {
      name: "Call to action",
      props: { tag: "section", contentWidth: 1100, anchor: "" },
      layout: { base: { mode: "stack", direction: "row", gap: 24, align: "center", justify: "between", wrap: false }, mobile: { direction: "column", align: "start" } },
      style: { base: { padding: [40, 24, 40, 24], background: "var(--dnn-color-secondary, #eef1f6)" } }
    }, [
      ["heading", { props: { text: [{ text: "Ready to take the next step?" }], level: "2" }, style: { base: { fontSize: 28 } } }],
      ["button", { props: { label: "Contact us", href: "#", variant: "primary" } }]
    ]])
  },
  {
    id: "columns-3",
    label: "Three columns",
    description: "An empty 3-column grid to fill yourself.",
    icon: "frame",
    build: () => build(["section", {
      name: "Columns",
      layout: { base: { mode: "grid", columns: 3, colGap: 24, rowGap: 24, rowHeight: 40 }, mobile: { columns: 1 } }
    }, [
      ["container", { name: "Column 1", place: { base: { colSpan: 1, rowSpan: 4 } } }],
      ["container", { name: "Column 2", place: { base: { colSpan: 1, rowSpan: 4 } } }],
      ["container", { name: "Column 3", place: { base: { colSpan: 1, rowSpan: 4 } } }]
    ]])
  }
];
export {
  CONTENT_PRESETS,
  PRESETS,
  build
};
