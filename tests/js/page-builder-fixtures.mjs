// Page builder conformance test for the JavaScript editor core (.docs/guides/testing.md#page-builder):
// runs every golden fixture in tests/fixtures/page-builder/render through the browser modules in
// src/DotNetForge.Web/wwwroot/js/builder and requires the same normalized document (or rejection), HTML and CSS the
// C# renderer produces (tests/DotNetForge.Tests/PageBuilder/GoldenFixtureTests.cs checks the C# side).
//
//   node tests/js/page-builder-fixtures.mjs        (Node 20+; no packages needed)
import { readFile, readdir } from "node:fs/promises";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const builder = path.join(root, "src/DotNetForge.Web/wwwroot/js/builder");

// The editor loads the schema with fetch() relative to its own URL; in Node those are file: URLs.
globalThis.fetch = async (url) => {
  const text = await readFile(fileURLToPath(url), "utf8");
  return { ok: true, status: 200, json: async () => JSON.parse(text), text: async () => text };
};

const load = (p) => import(pathToFileURL(path.join(builder, p)).href);
await load("components/builtins.js");
const { normalizeDocument, DocumentError } = await load("core/validate.js");
const { renderDocument } = await load("renderer/html.js");
const { documentCss } = await load("renderer/css.js");

const dir = path.join(root, "tests/fixtures/page-builder/render");
const files = (await readdir(dir)).filter((f) => f.endsWith(".json")).sort();
let failed = 0;
for (const file of files) {
  const fixture = JSON.parse(await readFile(path.join(dir, file), "utf8"));
  const problems = [];
  let normalized = null;
  let error = false;
  try {
    normalized = normalizeDocument(JSON.parse(JSON.stringify(fixture.input)));
  } catch (e) {
    if (DocumentError && !(e instanceof DocumentError)) throw e;
    error = true;
  }
  if (error !== !!fixture.error) problems.push(`rejected: expected ${!!fixture.error}, got ${error}`);
  if (!error) {
    if (JSON.stringify(normalized) !== JSON.stringify(fixture.normalized)) problems.push("normalized document differs");
    const ctx = fixture.context ?? {};
    const hidden = new Set(ctx.hidden ?? []);
    const modules = ctx.modules ? new Set(ctx.modules) : null;
    const html = renderDocument(normalized, { mode: ctx.mode, isVisible: (n) => !hidden.has(n.id), hasModule: (id) => (modules ? modules.has(id) : true) });
    if (html !== fixture.html) problems.push("HTML differs");
    if (documentCss(normalized) !== fixture.css) problems.push("CSS differs");
  }
  if (problems.length) {
    failed++;
    console.error(`FAIL ${file}: ${problems.join("; ")}`);
  }
}
console.log(`${files.length - failed}/${files.length} page builder fixtures match`);
process.exit(failed ? 1 : 0);
