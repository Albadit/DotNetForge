import { walk } from "../core/doc.js";
import { freePlacementAt, gridPlacementAt, layoutAt, styleAt } from "../core/responsive.js";
import { FONTS, SHADOWS, STYLE, STYLE_KEYS } from "../core/schema.js";
import * as V from "../core/values.js";
import { getComponent } from "../components/registry.js";
import { cssUrl, fmt, px } from "./format.js";
class Decls {
  list = [];
  set(prop, value) {
    for (const d of this.list) {
      if (d[0] === prop) {
        d[1] = value;
        return;
      }
    }
    this.list.push([prop, value]);
  }
}
const ALIGN = { start: "flex-start", center: "center", end: "flex-end", stretch: "stretch", baseline: "baseline" };
const JUSTIFY = {
  start: "flex-start",
  center: "center",
  end: "flex-end",
  between: "space-between",
  around: "space-around",
  evenly: "space-evenly"
};
const RESET = { "min-width": "0" };
const resetValue = (prop) => RESET[prop] ?? "unset";
function selectorOf(doc, id) {
  if (id === doc.root) return `.pe-root.pe-n-${id}`;
  const node = doc.nodes[id];
  const comp = node ? getComponent(node.type) : void 0;
  return comp && comp.tag(node) === "a" ? `.pe-root a.pe-n-${id}` : `.pe-root .pe-n-${id}`;
}
function trackCss(size, hug) {
  return size.endsWith("fr") ? `minmax(0,${size})` : size === "auto" ? hug : size;
}
function layoutDecls(out, l) {
  if (l.mode === "grid") {
    out.set("display", "grid");
    const cols = l.columns ?? 12;
    out.set("grid-template-columns", l.colSizes ? Array.from({ length: cols }, (_, i) => trackCss(l.colSizes[i] ?? "1fr", "auto")).join(" ") : `repeat(${cols},minmax(0,1fr))`);
    const autoRow = l.rowMode === "fixed" ? px(l.rowHeight ?? 40) : `minmax(${px(l.rowHeight ?? 40)},auto)`;
    if (l.rows) out.set("grid-template-rows", Array.from({ length: l.rows }, (_, i) => trackCss(l.rowSizes?.[i] ?? "auto", autoRow)).join(" "));
    out.set("grid-auto-rows", autoRow);
    out.set("column-gap", px(l.colGap ?? 0));
    out.set("row-gap", px(l.rowGap ?? 0));
  } else if (l.mode === "free") {
    out.set("display", "block");
    out.set("position", "relative");
    out.set("height", px(l.height ?? 400));
  } else {
    out.set("display", "flex");
    out.set("flex-direction", l.direction === "row" ? "row" : "column");
    out.set("gap", px(l.gap ?? 0));
    out.set("align-items", ALIGN[l.align ?? "stretch"] ?? "stretch");
    out.set("justify-content", JUSTIFY[l.justify ?? "start"] ?? "flex-start");
    out.set("flex-wrap", l.wrap ? "wrap" : "nowrap");
  }
}
function sizeDecls(out, axis, size, parent) {
  if (!size || typeof size !== "object") return;
  const mode = size.mode;
  const value = typeof size.value === "number" ? size.value : null;
  if (mode === "fixed" && value !== null && value >= 0 && value <= 1e4) out.set(axis, px(value));
  else if (mode === "percent" && value !== null && value >= 0 && value <= 100) out.set(axis, fmt(value) + "%");
  else if (mode === "hug") out.set(axis, "fit-content");
  if (parent?.mode === "stack") {
    const main = parent.direction === "row" ? "width" : "height";
    if (axis === main) {
      if (mode === "fill") out.set("flex", "1 1 0px");
      else if (mode === "hug" || mode === "fixed") out.set("flex", "0 0 auto");
      else if (mode === "percent") out.set("flex", "0 1 auto");
    } else if (mode === "fill") {
      out.set("align-self", "stretch");
    }
  }
}
function shadowValue(value) {
  if (typeof value === "string") return Object.prototype.hasOwnProperty.call(SHADOWS, value) ? SHADOWS[value] : null;
  if (!value || typeof value !== "object") return null;
  const s = value;
  const n = (v, min, max) => V.num(v, min, max);
  const x = n(s.x, -200, 200), y = n(s.y, -200, 200), blur = n(s.blur, 0, 400), spread = n(s.spread, -200, 200);
  const c = V.color(s.color);
  if (x === null || y === null || blur === null || spread === null || !c) return null;
  return `${s.inset === true ? "inset " : ""}${px(x)} ${px(y)} ${px(blur)} ${px(spread)} ${c}`;
}
function boxValue(value, min, max) {
  if (!Array.isArray(value) || value.length !== 4) return null;
  const parts = [];
  for (const v of value) {
    const n = V.num(v, min, max);
    if (n === null) return null;
    parts.push(px(n));
  }
  return parts.join(" ");
}
const PAINT_FLAGS = /* @__PURE__ */ new Set(["borderSides", "strokeAlign", "strokeHidden", "fillHidden"]);
const FILL_KEYS = /* @__PURE__ */ new Set(["background", "bgImage"]);
const STROKE_KEYS = /* @__PURE__ */ new Set(["borderWidth", "borderStyle", "borderColor"]);
/** True when the layer fills its stack parent's cross axis (width in a column, height in a row). */
function fillsCrossAxis(style, parent) {
  if (parent?.mode !== "stack") return false;
  const size = style[parent.direction === "row" ? "height" : "width"];
  return !!size && typeof size === "object" && size.mode === "fill";
}
function styleDecls(out, style, parent) {
  const freeChild = parent?.mode === "free";
  const fillHidden = style.fillHidden === "hidden";
  const strokeHidden = style.strokeHidden === "hidden";
  const sides = style.borderSides === void 0 ? null : boxValue(style.borderSides, STYLE.borderSides.min ?? 0, STYLE.borderSides.max ?? 40);
  const outline = !sides && (style.strokeAlign === "outside" || style.strokeAlign === "center");
  for (const key of STYLE_KEYS) {
    const value = style[key];
    if (value === void 0 || value === null) continue;
    if (PAINT_FLAGS.has(key) || fillHidden && FILL_KEYS.has(key) || strokeHidden && STROKE_KEYS.has(key)) continue;
    const def = STYLE[key];
    const css = outline && STROKE_KEYS.has(key) ? (def.css ?? "").replace("border-", "outline-") : def.css ?? "";
    switch (def.type) {
      case "size":
        if (!freeChild) sizeDecls(out, key, value, parent);
        break;
      case "length": {
        const n = V.num(value, def.min ?? -1e4, def.max ?? 1e4);
        if (n !== null) {
          out.set(css, px(n));
          if (key === "borderWidth" && style.borderStyle === void 0) out.set(outline ? "outline-style" : "border-style", "solid");
          if (key === "borderWidth" && outline && style.strokeAlign === "center") out.set("outline-offset", px(-n / 2));
        }
        break;
      }
      case "number": {
        const n = V.num(value, def.min ?? -1e4, def.max ?? 1e4);
        if (n !== null) out.set(css, fmt(n));
        break;
      }
      case "int": {
        const n = V.int(value, def.min ?? -1e4, def.max ?? 1e4);
        if (n !== null) out.set(css, String(n));
        break;
      }
      case "angle": {
        const n = V.num(value, def.min ?? -360, def.max ?? 360);
        if (n !== null) out.set(css, fmt(n) + "deg");
        break;
      }
      case "enum":
        // Fill on a stack's cross axis is align-self: stretch (sizeDecls); an alignment must not undo it.
        if (key === "alignSelf" && fillsCrossAxis(style, parent)) break;
        if (typeof value === "string" && def.options?.includes(value)) out.set(css, value);
        break;
      case "box":
      case "corners": {
        const v = boxValue(value, def.min ?? 0, def.max ?? 1e3);
        if (v) out.set(css, v);
        break;
      }
      case "color": {
        const c = V.color(value);
        if (c) out.set(css, c);
        break;
      }
      case "image": {
        const u = V.imageUrl(value);
        if (u) out.set(css, cssUrl(u));
        break;
      }
      case "shadow": {
        const s = shadowValue(value);
        if (s) out.set(css, s);
        break;
      }
      case "font":
        if (typeof value === "string" && Object.prototype.hasOwnProperty.call(FONTS, value)) out.set(css, FONTS[value]);
        break;
      default:
        break;
    }
  }
  if (sides && !strokeHidden) {
    out.set("border-width", sides);
    if (style.borderStyle === void 0) out.set("border-style", "solid");
  }
}
function placementDecls(out, node, parent, parentLayout, bp) {
  if (!parent || !parentLayout) return;
  if (parentLayout.mode === "grid") {
    const g = gridPlacementAt(node, parent, bp);
    if (g.auto) {
      out.set("grid-column", `span ${g.colSpan}`);
      out.set("grid-row", `span ${g.rowSpan}`);
    } else {
      out.set("grid-column", `${g.col} / span ${g.colSpan}`);
      out.set("grid-row", `${g.row} / span ${g.rowSpan}`);
    }
  } else if (parentLayout.mode === "free") {
    const f = freePlacementAt(node, bp);
    out.set("position", "absolute");
    out.set("left", px(f.x));
    out.set("top", px(f.y));
    out.set("width", px(f.w));
    out.set("height", px(f.h));
  }
}
function declsAt(doc, node, bp) {
  const self = new Decls();
  const inner = new Decls();
  const comp = getComponent(node.type);
  const parent = node.parent ? doc.nodes[node.parent] ?? null : null;
  const parentLayout = parent ? layoutAt(parent, bp) : null;
  const container = comp?.schema.container === true;
  const targetsInner = comp?.layoutTarget === "inner";
  if (container) layoutDecls(targetsInner ? inner : self, layoutAt(node, bp));
  placementDecls(self, node, parent, parentLayout, bp);
  styleDecls(self, styleAt(node, bp), parentLayout);
  const hooks = comp?.css?.(node);
  if (hooks?.self) for (const [p, v] of hooks.self) self.set(p, v);
  if (hooks?.inner) for (const [p, v] of hooks.inner) inner.set(p, v);
  if (node.vars) {
    for (const [name, value] of Object.entries(node.vars)) {
      const n = V.varName(name);
      const v = V.varValue(value);
      if (n && v) self.set(n, v);
    }
  }
  return { self: self.list, inner: inner.list };
}
function diff(prev, cur) {
  const before = new Map(prev);
  const now = new Map(cur);
  const out = [];
  for (const [p, v] of cur) if (before.get(p) !== v) out.push([p, v]);
  for (const [p] of prev) if (!now.has(p)) out.push([p, resetValue(p)]);
  return out;
}
function rule(selector, decls) {
  return decls.length === 0 ? "" : selector + "{" + decls.map(([p, v]) => p + ":" + v).join(";") + "}";
}
function mediaQueries(doc) {
  const bp = doc.settings?.breakpoints ?? { tablet: 1024, mobile: 640 };
  return {
    tablet: `@media (max-width:${fmt(bp.tablet - 0.02)}px)`,
    mobile: `@media (max-width:${fmt(bp.mobile - 0.02)}px)`
  };
}
function nodeCss(doc, id) {
  const node = doc.nodes[id];
  if (!node) return "";
  const sel = selectorOf(doc, id);
  const innerSel = sel + ">.pe-in";
  const base = declsAt(doc, node, "base");
  const tablet = declsAt(doc, node, "tablet");
  const mobile = declsAt(doc, node, "mobile");
  const mq = mediaQueries(doc);
  const lines = [];
  const b = rule(sel, base.self) + rule(innerSel, base.inner);
  if (b) lines.push(b);
  const t = rule(sel, diff(base.self, tablet.self)) + rule(innerSel, diff(base.inner, tablet.inner));
  if (t) lines.push(mq.tablet + "{" + t + "}");
  const m = rule(sel, diff(tablet.self, mobile.self)) + rule(innerSel, diff(tablet.inner, mobile.inner));
  if (m) lines.push(mq.mobile + "{" + m + "}");
  return lines.join("\n");
}
function visibilityCss(doc) {
  const bp = doc.settings?.breakpoints ?? { tablet: 1024, mobile: 640 };
  return [
    `@media (min-width:${fmt(bp.tablet)}px){.pe-root .pe-hide-d{display:none!important}}`,
    `@media (min-width:${fmt(bp.mobile)}px) and (max-width:${fmt(bp.tablet - 0.02)}px){.pe-root .pe-hide-t{display:none!important}}`,
    `@media (max-width:${fmt(bp.mobile - 0.02)}px){.pe-root .pe-hide-m{display:none!important}}`
  ].join("\n");
}
function documentCss(doc) {
  const parts = [];
  for (const id of walkRendered(doc)) {
    const css = nodeCss(doc, id);
    if (css) parts.push(css);
  }
  parts.push(visibilityCss(doc));
  return parts.join("\n");
}
function walkRendered(doc) {
  const out = [];
  const stack = [doc.root];
  while (stack.length) {
    const id = stack.pop();
    const node = doc.nodes[id];
    if (!node || node.hidden) continue;
    out.push(id);
    const kids = node.children;
    if (kids) for (let i = kids.length - 1; i >= 0; i--) stack.push(kids[i]);
  }
  return out;
}
export {
  documentCss,
  mediaQueries,
  nodeCss,
  selectorOf,
  visibilityCss,
  walk,
  walkRendered
};
