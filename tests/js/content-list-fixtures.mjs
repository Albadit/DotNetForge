// Writes the content list golden fixtures (tests/fixtures/page-builder/render/content-*.json) from the browser modules
// in src/DotNetForge.Web/wwwroot/js/builder (.docs/features/structured-content.md#content-lists). The C# renderer must
// then produce the same normalized document, HTML and CSS (GoldenFixtureTests), and page-builder-fixtures.mjs checks
// the JavaScript side keeps producing them. Run after changing a case below, and review the diff:
//
//   node tests/js/content-list-fixtures.mjs        (Node 20+; no packages needed)
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const builder = path.join(root, "src/DotNetForge.Web/wwwroot/js/builder");
globalThis.fetch = async (url) => {
  const text = await readFile(fileURLToPath(url), "utf8");
  return { ok: true, status: 200, json: async () => JSON.parse(text), text: async () => text };
};
const load = (p) => import(pathToFileURL(path.join(builder, p)).href);
await load("components/builtins.js");
const { normalizeDocument } = await load("core/validate.js");
const { renderDocument } = await load("renderer/html.js");
const { documentCss } = await load("renderer/css.js");

const TYPE = "6f1d2c3b-4a5e-4f60-8a7b-9c0d1e2f3a4b";
const node = (id, type, parent, extra = {}) => ({ id, type, parent, props: {}, ...extra });

/** A card grid: picture, name, a price line, the description and a link, each bound to a field. */
function cards(extraProps = {}, children = ["i"]) {
  return {
    schema: 1, root: "r", settings: {},
    nodes: {
      r: node("r", "page", null, { children: ["s"] }),
      s: node("s", "section", "r", { children: ["c"] }),
      c: node("c", "collection", "s", {
        children,
        props: { source: TYPE.toUpperCase(), limit: 3, paginate: true, itemUrl: "/products/{slug}", ...extraProps },
        layout: { base: { mode: "grid", columns: 3, colGap: 24, rowGap: 24 }, mobile: { columns: 1 } }
      }),
      i: node("i", "item", "c", {
        children: ["img", "h", "p", "t", "b"],
        place: { base: { col: 2, row: 3, colSpan: 1, rowSpan: 1 } },
        layout: { base: { mode: "stack", direction: "column", gap: 8 } },
        style: { base: { padding: [16, 16, 16, 16], radius: [12, 12, 12, 12], shadow: "md" } }
      }),
      img: node("img", "image", "i", { props: { alt: "Product" }, bind: { src: "{photo}", alt: "{name}", caption: "no braces", fit: "{name}" } }),
      h: node("h", "heading", "i", { props: { level: "3" }, bind: { text: "{name}" } }),
      p: node("p", "text", "i", { bind: { content: "From {price} USD" } }),
      t: node("t", "text", "i", { bind: { content: "{description}" } }),
      b: node("b", "button", "i", { props: { label: "View" }, bind: { href: "{$url}", label: "View {name}" } })
    }
  };
}

const item = (id, fields) => ({ id, fields });
const items = [
  item("a1", {
    $url: { text: "/products/chair", url: "/products/chair" },
    name: { text: "Chair & <Table> \"set\" 'one'" },
    slug: { text: "chair" },
    price: { text: "1,249.50", value: 1249.5 },
    photo: { text: "chair.jpg", url: "/media/1/chair.jpg" },
    description: { text: "Comfy", rich: [{ type: "p", runs: [{ text: "Comfy ", b: true }, { text: "and ", i: true }, { text: "strong", href: "https://example.com" }] }, { type: "ul", items: [[{ text: "oak" }], [{ text: "walnut" }]] }] }
  }),
  item("a2", {
    $url: { text: "/products/caf%C3%A9", url: "/products/caf%C3%A9" },
    name: { text: "Café ü (special)!" },
    price: { text: "15", value: 15 },
    photo: { text: "bad", url: "javascript:alert(1)" },
    description: { text: "Line one\nline two" }
  }),
  item("a3", { name: { text: "No picture" } })
];
const data = (list, page = 2, pageCount = 3) => ({ c: { items: list, total: list.length, page, pageCount } });

const cases = [
  { name: "content-list-cards", input: cards(), context: { mode: "view", data: data(items) } },
  { name: "content-list-canvas", input: cards(), context: { mode: "canvas", data: data(items, 1, 1) } },
  { name: "content-list-empty", input: cards({ emptyText: "No products <yet>" }), context: { mode: "view", data: data([], 1, 1) } },
  { name: "content-list-template", input: cards(), context: { mode: "canvas" } },
  { name: "content-list-table", input: cards({ view: "table", columns: [{ field: "name", label: "Product" }, { field: "price", label: "" }, { field: "missing", label: "Gone" }, { field: "bad path!", label: "x" }] }), context: { mode: "view", data: data(items, 3, 3) } },
  { name: "content-list-carousel", input: cards({ carousel: true, paginate: false, nextLabel: "More" }), context: { mode: "view", data: data(items.slice(0, 2), 1, 2) } },
  {
    name: "content-list-props",
    input: cards({
      source: "not-a-guid", mode: "single", entry: "ABCDEF01-2345-6789-ABCD-EF0123456789", routeParam: "Bad Param", routeField: "$title",
      filters: [{ field: "category.slug", op: "eq", value: "chairs" }, { field: "price", op: "between", value: 5 }, { field: "", op: "eq" }, "nope", { field: "inStock", op: "notEmpty" }],
      sortField: "price", sortDir: "sideways", limit: 500, columns: "x"
    }),
    context: { mode: "view" }
  }
];

for (const c of cases) {
  const normalized = normalizeDocument(JSON.parse(JSON.stringify(c.input)));
  const ctx = { mode: c.context.mode, data: c.context.data, isVisible: () => true, hasModule: () => true };
  const fixture = {
    name: c.name,
    input: c.input,
    normalized,
    error: false,
    context: c.context,
    html: renderDocument(normalized, ctx),
    css: documentCss(normalized)
  };
  await writeFile(path.join(root, "tests/fixtures/page-builder/render", c.name + ".json"), JSON.stringify(fixture, null, 2) + "\n");
  console.log("wrote", c.name);
}
