import { canContain } from "../../core/schema.js";
import { layoutAt } from "../../core/responsive.js";
import { subtree } from "../../core/doc.js";
import { layoutElement } from "./frame.js";
function rectOf(el) {
  const r = el.getBoundingClientRect();
  return { x: r.left, y: r.top, w: r.width, h: r.height };
}
function parseTracks(value) {
  return value.split(/\s+/).map((v) => parseFloat(v)).filter((n) => Number.isFinite(n));
}
function gridMetrics(el, fallbackColumns, fallbackRowHeight) {
  const cs = el.ownerDocument.defaultView.getComputedStyle(el);
  const r = el.getBoundingClientRect();
  const padL = parseFloat(cs.paddingLeft) + parseFloat(cs.borderLeftWidth);
  const padT = parseFloat(cs.paddingTop) + parseFloat(cs.borderTopWidth);
  const padR = parseFloat(cs.paddingRight) + parseFloat(cs.borderRightWidth);
  const box = { x: r.left + padL, y: r.top + padT, w: r.width - padL - padR, h: r.height - padT - (parseFloat(cs.paddingBottom) + parseFloat(cs.borderBottomWidth)) };
  const colGap = parseFloat(cs.columnGap) || 0;
  const rowGap = parseFloat(cs.rowGap) || 0;
  let colWidths = parseTracks(cs.gridTemplateColumns);
  if (colWidths.length === 0) {
    const n = Math.max(1, fallbackColumns);
    const w = (box.w - colGap * (n - 1)) / n;
    colWidths = Array.from({ length: n }, () => w);
  }
  const rowHeights = parseTracks(cs.gridTemplateRows);
  const cols = [];
  let x = box.x;
  for (const w of colWidths) {
    cols.push(x);
    x += w + colGap;
  }
  const rows = [];
  let y = box.y;
  for (const h of rowHeights) {
    rows.push(y);
    y += h + rowGap;
  }
  return { box, cols, colWidths, rows, rowHeights, colGap, rowGap, rowHeight: fallbackRowHeight };
}
function heldMetrics(held, live) {
  const dx = live.box.x - held.box.x;
  const dy = live.box.y - held.box.y;
  if (!dx && !dy) return held;
  return { ...held, box: { ...held.box, x: held.box.x + dx, y: held.box.y + dy }, cols: held.cols.map((x) => x + dx), rows: held.rows.map((y) => y + dy) };
}
function rowStart(m, row) {
  const i = row - 1;
  if (i < m.rows.length) return m.rows[i];
  const lastEnd = m.rows.length ? m.rows[m.rows.length - 1] + m.rowHeights[m.rowHeights.length - 1] + m.rowGap : m.box.y;
  return lastEnd + (i - m.rows.length) * (m.rowHeight + m.rowGap);
}
function rowHeightAt(m, row) {
  return row - 1 < m.rowHeights.length ? m.rowHeights[row - 1] : m.rowHeight;
}
function cellAt(m, x, y) {
  let col = 1;
  for (let i = 0; i < m.cols.length; i++) {
    if (x >= m.cols[i] - m.colGap / 2) col = i + 1;
  }
  let row = 1;
  for (let r = 1; r < 1e3; r++) {
    const start = rowStart(m, r);
    if (y >= start - m.rowGap / 2) row = r;
    else break;
  }
  return { col, row };
}
function cellRect(m, col, row, colSpan, rowSpan) {
  const c0 = Math.min(Math.max(1, col), m.cols.length || 1) - 1;
  const c1 = Math.min(c0 + colSpan, m.cols.length || 1) - 1;
  const x = m.cols[c0] ?? m.box.x;
  const right = (m.cols[c1] ?? m.box.x) + (m.colWidths[c1] ?? 0);
  const y = rowStart(m, row);
  const bottom = rowStart(m, row + rowSpan - 1) + rowHeightAt(m, row + rowSpan - 1);
  return { x, y, w: right - x, h: bottom - y };
}
function spansFor(m, col, row, width, height) {
  const startX = m.cols[col - 1] ?? m.box.x;
  let colSpan = 1;
  for (let c = col; c <= m.cols.length; c++) {
    const right = m.cols[c - 1] + m.colWidths[c - 1];
    if (startX + width >= right - m.colWidths[c - 1] / 2) colSpan = c - col + 1;
  }
  const startY = rowStart(m, row);
  let rowSpan = 1;
  for (let r = row; r < row + 400; r++) {
    const bottom = rowStart(m, r) + rowHeightAt(m, r);
    if (startY + height >= bottom - rowHeightAt(m, r) / 2) rowSpan = r - row + 1;
    else break;
  }
  return { colSpan: Math.max(1, colSpan), rowSpan: Math.max(1, rowSpan) };
}
function findDropTarget(frame, doc, bp, q) {
  const excluded = /* @__PURE__ */ new Set();
  for (const id2 of q.moving) for (const s of subtree(doc, id2)) excluded.add(s);
  let id = frame.nodeAt(q.x, q.y);
  if (id && excluded.has(id)) {
    id = doc.nodes[q.moving[0]]?.parent ?? null;
  }
  if (!id) id = doc.root;
  while (id) {
    const node = doc.nodes[id];
    if (!node) break;
    if (!excluded.has(id) && !node.locked && node.children && canContain(node.type, q.type)) {
      const t = targetIn(frame, doc, bp, id, q);
      if (t) return t;
    }
    id = node.parent;
  }
  return null;
}
function targetIn(frame, doc, bp, parentId, q) {
  const parent = doc.nodes[parentId];
  const el = frame.elements.get(parentId);
  if (!el) return null;
  const target = layoutElement(el);
  const container = rectOf(target);
  const layout = layoutAt(parent, bp);
  if (layout.mode === "grid") {
    const live = gridMetrics(target, layout.columns ?? 12, layout.rowHeight ?? 40);
    const m = q.metrics?.(parentId, live) ?? live;
    const span = q.span ?? { colSpan: 4, rowSpan: 2 };
    const colSpan = Math.min(span.colSpan, m.cols.length || 1);
    const px = q.x - (q.grab?.dx ?? 0);
    const py = q.y - (q.grab?.dy ?? 0);
    const cell = cellAt(m, px + Math.min(m.colWidths[0] ?? 0, 40) / 2, py + 8);
    const col = Math.min(cell.col, Math.max(1, m.cols.length - colSpan + 1));
    return {
      parent: parentId,
      at: { col, row: cell.row },
      indicator: { kind: "cell", rect: cellRect(m, col, cell.row, colSpan, span.rowSpan) },
      container
    };
  }
  if (layout.mode === "free") {
    const x = Math.round(q.x - container.x - (q.grab?.dx ?? 0));
    const y = Math.round(q.y - container.y - (q.grab?.dy ?? 0));
    const size = q.size ?? { w: 240, h: 120 };
    return {
      parent: parentId,
      at: { x, y },
      indicator: { kind: "box", rect: { x: container.x + x, y: container.y + y, w: size.w, h: size.h } },
      container
    };
  }
  const row = layout.direction === "row";
  const kids = (parent.children ?? []).filter((c) => !q.moving.includes(c));
  let index = kids.length;
  let line = null;
  for (let i = 0; i < kids.length; i++) {
    const kel = frame.elements.get(kids[i]);
    if (!kel) continue;
    const r = rectOf(kel);
    const mid = row ? r.x + r.w / 2 : r.y + r.h / 2;
    if ((row ? q.x : q.y) < mid) {
      index = i;
      line = row ? { kind: "line", x: r.x - Math.min(4, (layout.gap ?? 0) / 2), y: r.y, length: r.h, vertical: true } : { kind: "line", x: r.x, y: r.y - Math.min(4, (layout.gap ?? 0) / 2), length: r.w, vertical: false };
      break;
    }
  }
  if (!line) {
    const lastEl = kids.length ? frame.elements.get(kids[kids.length - 1]) : null;
    if (lastEl) {
      const r = rectOf(lastEl);
      line = row ? { kind: "line", x: r.x + r.w + Math.min(4, (layout.gap ?? 0) / 2), y: r.y, length: r.h, vertical: true } : { kind: "line", x: r.x, y: r.y + r.h + Math.min(4, (layout.gap ?? 0) / 2), length: r.w, vertical: false };
    } else {
      line = { kind: "box", rect: container };
    }
  }
  const all = parent.children ?? [];
  const realIndex = index >= kids.length ? all.length : all.indexOf(kids[index]);
  return { parent: parentId, index: realIndex, indicator: line, container };
}
export {
  cellAt,
  cellRect,
  findDropTarget,
  gridMetrics,
  heldMetrics,
  parseTracks,
  rectOf,
  rowHeightAt,
  rowStart,
  spansFor
};
