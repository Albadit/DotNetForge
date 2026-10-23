const overlaps = (a, b) => a.col < b.col + b.colSpan && b.col < a.col + a.colSpan && a.row < b.row + b.rowSpan && b.row < a.row + a.rowSpan;
class Occupancy {
  constructor(cols) {
    this.cols = cols;
  }
  cols;
  rows = [];
  row(r) {
    while (this.rows.length <= r) this.rows.push(new Uint8Array(this.cols + 1));
    return this.rows[r];
  }
  fits(col, row, colSpan, rowSpan) {
    if (col < 1 || col + colSpan - 1 > this.cols) return false;
    for (let r = row; r < row + rowSpan; r++) {
      const line = this.row(r);
      for (let c = col; c < col + colSpan; c++) if (line[c]) return false;
    }
    return true;
  }
  mark(col, row, colSpan, rowSpan) {
    for (let r = row; r < row + rowSpan; r++) {
      const line = this.row(r);
      for (let c = col; c < col + colSpan && c <= this.cols; c++) line[c] = 1;
    }
  }
}
function autoFlow(items, cols) {
  const c = Math.max(1, cols);
  const occ = new Occupancy(c);
  const out = /* @__PURE__ */ new Map();
  for (const it of items) {
    if (typeof it.col === "number" && typeof it.row === "number") {
      const col = Math.min(Math.max(1, it.col), c);
      const colSpan = Math.min(Math.max(1, it.colSpan), c - col + 1);
      const g = { id: it.id, col, row: Math.max(1, it.row), colSpan, rowSpan: Math.max(1, it.rowSpan) };
      occ.mark(g.col, g.row, g.colSpan, g.rowSpan);
      out.set(it.id, g);
    }
  }
  let curRow = 1;
  let curCol = 1;
  for (const it of items) {
    if (out.has(it.id)) continue;
    const colSpan = Math.min(Math.max(1, it.colSpan), c);
    const rowSpan = Math.max(1, it.rowSpan);
    for (; ; ) {
      if (curCol + colSpan - 1 > c) {
        curRow++;
        curCol = 1;
        continue;
      }
      if (occ.fits(curCol, curRow, colSpan, rowSpan)) break;
      curCol++;
    }
    const g = { id: it.id, col: curCol, row: curRow, colSpan, rowSpan };
    occ.mark(g.col, g.row, g.colSpan, g.rowSpan);
    out.set(it.id, g);
    curCol += colSpan;
  }
  return items.map((it) => out.get(it.id));
}
function sortByPosition(list) {
  return [...list].sort((a, b) => a.row - b.row || a.col - b.col);
}
function compact(items, pinned) {
  const cols = items.reduce((m, i) => Math.max(m, i.col + i.colSpan - 1), 1);
  const occ = new Occupancy(cols);
  const result = /* @__PURE__ */ new Map();
  const pin = pinned ? items.find((i) => i.id === pinned) : void 0;
  if (pin) {
    occ.mark(pin.col, pin.row, pin.colSpan, pin.rowSpan);
    result.set(pin.id, { ...pin });
  }
  for (const it of sortByPosition(items)) {
    if (it.id === pinned) continue;
    const g = { ...it };
    while (g.row > 1 && occ.fits(g.col, g.row - 1, g.colSpan, g.rowSpan)) g.row--;
    let guard = 0;
    while (!occ.fits(g.col, g.row, g.colSpan, g.rowSpan) && guard++ < 1e5) g.row++;
    occ.mark(g.col, g.row, g.colSpan, g.rowSpan);
    result.set(g.id, g);
  }
  return items.map((i) => result.get(i.id));
}
function placeItem(items, id, target, cols, options = {}) {
  const c = Math.max(1, cols);
  const colSpan = Math.min(Math.max(1, target.colSpan), c);
  const moved = {
    id,
    colSpan,
    rowSpan: Math.max(1, target.rowSpan),
    col: Math.min(Math.max(1, target.col), c - colSpan + 1),
    row: Math.max(1, target.row)
  };
  const others = items.filter((i) => i.id !== id).map((i) => ({ ...i }));
  const fixed = [moved];
  for (const it of sortByPosition(others)) {
    let guard = 0;
    while (fixed.some((f) => overlaps(f, it)) && guard++ < 1e4) {
      const blocker = fixed.filter((f) => overlaps(f, it)).reduce((m, f) => Math.max(m, f.row + f.rowSpan), 0);
      it.row = Math.max(it.row + 1, blocker);
    }
    fixed.push(it);
  }
  const byId = new Map(fixed.map((f) => [f.id, f]));
  let result = items.some((i) => i.id === id) ? items.map((i) => byId.get(i.id)) : [...items.map((i) => byId.get(i.id)), moved];
  if (options.float) result = compact(result, options.pin ? id : void 0);
  return result;
}
function firstFree(items, colSpan, rowSpan, cols) {
  const flow = autoFlow([...items, { id: "\0new", colSpan, rowSpan }], cols);
  const g = flow[flow.length - 1];
  return { col: g.col, row: g.row };
}
function rowsUsed(items) {
  return items.reduce((m, i) => Math.max(m, i.row + i.rowSpan - 1), 0);
}
function anyOverlap(items) {
  for (let i = 0; i < items.length; i++) for (let j = i + 1; j < items.length; j++) if (overlaps(items[i], items[j])) return true;
  return false;
}
export {
  anyOverlap,
  autoFlow,
  compact,
  firstFree,
  placeItem,
  rowsUsed
};
