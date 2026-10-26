// Bound props of a node inside a content list item (.docs/features/structured-content.md#data-binding): each template
// ({name}, "From {price} USD", "/products/{slug}") is filled from the item's field map and the result is cleaned by the
// prop's own validator, so a bound value is held to the same rules as a typed one. Mirrors Rendering/Binding.cs.
import { componentDef, PROP_LIMITS } from "../core/schema.js";
import { normalizeProp } from "../core/validate.js";

const PLACEHOLDER = /\{([^{}]+)\}/g;
const EXACT = /^\{([^{}]+)\}$/;
const LONE_SURROGATE = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/g;

const isObj = (v) => !!v && typeof v === "object" && !Array.isArray(v);

function cell(fields, path) {
  if (!isObj(fields) || !Object.prototype.hasOwnProperty.call(fields, path)) return null;
  const c = fields[path];
  return isObj(c) ? c : null;
}

/** encodeURIComponent with ! ' ( ) * escaped too (Uri.EscapeDataString); lone surrogates dropped. */
function encodeComponent(value) {
  return encodeURIComponent(value.replace(LONE_SURROGATE, "")).replace(/[!'()*]/g, (c) => "%" + c.charCodeAt(0).toString(16).toUpperCase());
}

function fill(template, fields, encode) {
  return template.replace(PLACEHOLDER, (_m, path) => {
    const c = cell(fields, path);
    const text = c && typeof c.text === "string" ? c.text : "";
    return encode ? encodeComponent(text) : text;
  });
}

const cut = (text, max) => (text.length > max ? text.substring(0, max) : text);

/** A template's value for a prop type; undefined for types that can't be bound. */
function bindValue(type, template, fields, max) {
  const exact = EXACT.exec(template);
  const c = exact ? cell(fields, exact[1]) : null;
  switch (type) {
    case "text":
    case "video":
      return cut(fill(template, fields, false), max);
    case "inline": {
      const text = cut(fill(template, fields, false), max);
      return text ? [{ text }] : [];
    }
    case "rich": {
      if (c && Array.isArray(c.rich)) return JSON.parse(JSON.stringify(c.rich));
      const text = cut(fill(template, fields, false), max);
      return text ? [{ type: "p", runs: [{ text }] }] : [];
    }
    case "url":
    case "image":
      if (exact) {
        if (c && typeof c.url === "string") return c.url;
        return c && typeof c.text === "string" ? c.text : "";
      }
      return fill(template, fields, true);
    default:
      return void 0;
  }
}

/** The node with its bound props filled from `item`; the node itself when nothing is bound. */
function bindNode(node, item) {
  if (!item || !isObj(node.bind)) return node;
  const def = componentDef(node.type);
  if (!def) return node;
  const fields = isObj(item.fields) ? item.fields : {};
  const props = JSON.parse(JSON.stringify(node.props));
  for (const [prop, template] of Object.entries(node.bind)) {
    const pdef = Object.prototype.hasOwnProperty.call(def.props, prop) ? def.props[prop] : void 0;
    if (!pdef || typeof template !== "string") continue;
    const max = pdef.max ?? (pdef.type === "rich" ? 2e4 : PROP_LIMITS.text);
    const value = bindValue(pdef.type, template, fields, max);
    if (value !== void 0) props[prop] = normalizeProp(pdef, value);
  }
  return { ...node, props };
}

export {
  bindNode,
  bindValue,
  encodeComponent
};
