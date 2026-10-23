// The shared page builder schema (.docs/features/page-builder.md#schema): src/DotNetForge.Web/wwwroot/builder/schema/*.json,
// loaded once when the editor starts. The server embeds the same files (DotNetForge.PageBuilder), so the editor and
// the renderer validate and render identically. (In the reference editor these were bundled JSON imports.)
const base = new URL("../../../builder/schema/", import.meta.url);

async function load(name) {
  const response = await fetch(new URL(name, base), { credentials: "same-origin" });
  if (!response.ok) {
    throw new Error(`Page builder schema ${name}: HTTP ${response.status}`);
  }
  return response.json();
}

export const [styleSchema, componentSchema, iconSchema] = await Promise.all([
  load("style.json"),
  load("components.json"),
  load("icons.json"),
]);
