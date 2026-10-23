import "../components/builtins.js";
import { getComponent } from "../components/registry.js";
import { classNames } from "../core/values.js";
import { escapeHtml } from "./format.js";
const DEVICE_CLASS = { desktop: "pe-hide-d", tablet: "pe-hide-t", mobile: "pe-hide-m" };
const DEVICES = ["desktop", "tablet", "mobile"];
function hiddenOnDevices(node) {
  const shown = node.visibility?.devices;
  if (!Array.isArray(shown)) return [];
  return DEVICES.filter((d) => !shown.includes(d));
}
function isSkipped(node, ctx) {
  if (node.hidden) return true;
  if (ctx.mode === "view") {
    if (hiddenOnDevices(node).length === 3) return true;
    if (ctx.isVisible && !ctx.isVisible(node)) return true;
    if (node.type === "module" && ctx.hasModule) {
      const id = typeof node.props.moduleId === "number" ? node.props.moduleId : 0;
      if (!ctx.hasModule(id)) return true;
    }
  }
  return false;
}
function classesOf(doc, node) {
  const comp = getComponent(node.type);
  const parts = [];
  if (node.id === doc.root) parts.push("pe-root");
  parts.push("pe-node", "pe-t-" + node.type, "pe-n-" + node.id);
  if (comp?.classes) parts.push(...comp.classes(node));
  for (const d of hiddenOnDevices(node)) parts.push(DEVICE_CLASS[d]);
  const user = classNames(node.className ?? "");
  if (user) parts.push(user);
  return parts.join(" ");
}
function renderNode(doc, id, ctx) {
  const node = doc.nodes[id];
  if (!node || isSkipped(node, ctx)) return "";
  const comp = getComponent(node.type);
  if (!comp) return "";
  let children = "";
  if (node.children) for (const child of node.children) children += renderNode(doc, child, ctx);
  const tag = comp.tag(node);
  let open = "<" + tag + ' class="' + escapeHtml(classesOf(doc, node)) + '"';
  if (comp.attrs) for (const [name, value] of comp.attrs(node, ctx)) open += " " + name + '="' + escapeHtml(value) + '"';
  if (ctx.mode === "canvas") open += ' data-pe-id="' + node.id + '"';
  open += ">";
  if (comp.voidElement) return open;
  const content = comp.content ? comp.content(node, ctx, children) : children;
  return open + content + "</" + tag + ">";
}
function renderDocument(doc, ctx) {
  return renderNode(doc, doc.root, ctx);
}
export {
  classesOf,
  hiddenOnDevices,
  isSkipped,
  renderDocument,
  renderNode
};
