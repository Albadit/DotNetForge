import { LAYOUT, LAYOUT_DEFAULTS } from "./schema.js";
function merged(value, bp) {
  if (!value) return {};
  if (bp === "base") return { ...value.base ?? {} };
  if (bp === "tablet") return { ...value.base ?? {}, ...value.tablet ?? {} };
  return { ...value.base ?? {}, ...value.tablet ?? {}, ...value.mobile ?? {} };
}
function layoutAt(node, bp) {
  const m = merged(node.layout, bp);
  const mode = m.mode === "grid" || m.mode === "free" ? m.mode : "stack";
  const out = { ...LAYOUT_DEFAULTS[mode], mode };
  for (const key of Object.keys(m)) {
    const def = LAYOUT[key];
    if (key !== "mode" && def?.modes?.includes(mode) && m[key] !== void 0) {
      out[key] = m[key];
    }
  }
  return out;
}
function styleAt(node, bp) {
  return merged(node.style, bp);
}
function columnsAt(parent, bp) {
  const l = layoutAt(parent, bp);
  return l.mode === "grid" ? l.columns ?? 12 : 0;
}
function fromPartial(p) {
  const explicit = typeof p.col === "number" && typeof p.row === "number";
  return {
    auto: !explicit,
    col: explicit ? p.col : 0,
    row: explicit ? p.row : 0,
    colSpan: p.colSpan ?? 1,
    rowSpan: p.rowSpan ?? 1
  };
}
function toPartial(g) {
  return g.auto ? { colSpan: g.colSpan, rowSpan: g.rowSpan } : { col: g.col, row: g.row, colSpan: g.colSpan, rowSpan: g.rowSpan };
}
function clampTo(g, cols) {
  const c = Math.max(1, cols);
  if (g.auto) return { ...g, colSpan: Math.min(Math.max(1, g.colSpan), c), rowSpan: Math.max(1, g.rowSpan) };
  const col = Math.min(Math.max(1, g.col), c);
  return {
    auto: false,
    col,
    row: Math.max(1, g.row),
    colSpan: Math.min(Math.max(1, g.colSpan), c - col + 1),
    rowSpan: Math.max(1, g.rowSpan)
  };
}
function gridPlacementAt(node, parent, bp) {
  const place = node.place ?? {};
  const colsBase = columnsAt(parent, "base");
  const base = fromPartial(place.base ?? {});
  const effBase = colsBase > 0 ? clampTo(base, colsBase) : { ...base, auto: true };
  if (bp === "base") return effBase;
  const colsTablet = columnsAt(parent, "tablet");
  let effTablet;
  if (place.tablet) effTablet = fromPartial({ ...toPartial(effBase), ...place.tablet });
  else if (colsTablet === colsBase) effTablet = effBase;
  else effTablet = { ...effBase, auto: true, col: 0, row: 0 };
  effTablet = clampTo(effTablet, colsTablet);
  if (bp === "tablet") return effTablet;
  const colsMobile = columnsAt(parent, "mobile");
  let effMobile;
  if (place.mobile) effMobile = fromPartial({ ...toPartial(effTablet), ...place.mobile });
  else if (colsMobile === colsTablet) effMobile = effTablet;
  else effMobile = { ...effTablet, auto: true, col: 0, row: 0 };
  return clampTo(effMobile, colsMobile);
}
function freePlacementAt(node, bp) {
  const m = merged(node.place, bp);
  return { x: m.x ?? 0, y: m.y ?? 0, w: m.w ?? 240, h: m.h ?? 120 };
}
export {
  freePlacementAt,
  gridPlacementAt,
  layoutAt,
  merged,
  styleAt
};
