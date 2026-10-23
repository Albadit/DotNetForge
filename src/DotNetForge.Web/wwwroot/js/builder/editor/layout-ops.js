import { moveNodes, setPlacement, updateLayout, updateStyle, updateStyleEach } from "../core/commands.js";
import { freePlacementAt, layoutAt, merged } from "../core/responsive.js";
function flowOf(session, id) {
  const l = layoutAt(session.store.doc.nodes[id], session.breakpoint.value);
  return l.mode === "stack" ? l.direction === "row" ? "stack-row" : "stack-col" : l.mode ?? "free";
}
function setFlow(session, canvas, id, flow) {
  const store = session.store;
  const bp = session.breakpoint.value;
  const node = store.doc.nodes[id];
  if (!node?.children) return;
  const l = layoutAt(node, bp);
  if (flow === "free") {
    if (l.mode === "free") return;
    const placements = {};
    const box = canvas.frame?.layoutTargetOf(id)?.getBoundingClientRect();
    let height = 0;
    for (const c of node.children) {
      const r = canvas.rectOfNode(c);
      if (r && box) {
        placements[c] = { x: Math.round(r.x - box.left), y: Math.round(r.y - box.top), w: Math.round(r.w), h: Math.round(r.h) };
        height = Math.max(height, Math.round(r.y - box.top + r.h));
      }
    }
    updateLayout(store, id, bp, { mode: "free", height: Math.max(height + 24, 200) }, void 0, placements);
  } else if (flow === "grid") {
    updateLayout(store, id, bp, { mode: "grid" });
  } else {
    updateLayout(store, id, bp, { mode: "stack", direction: flow === "stack-row" ? "row" : "column" });
  }
}
function resizeToFit(session, id) {
  const store = session.store;
  const bp = session.breakpoint.value;
  const node = store.doc.nodes[id];
  if (!node?.children || layoutAt(node, bp).mode !== "free") return;
  let bottom = 0;
  for (const c of node.children) {
    const f = freePlacementAt(store.doc.nodes[c], bp);
    bottom = Math.max(bottom, f.y + f.h);
  }
  updateLayout(store, id, bp, { height: Math.max(bottom, 40) });
}
function align(session, canvas, ids, edge) {
  const store = session.store;
  const bp = session.breakpoint.value;
  const doc = store.doc;
  const nodes = ids.map((id) => doc.nodes[id]).filter((n) => n && n.parent && !n.locked);
  if (!nodes.length) return false;
  const horizontal = edge === "left" || edge === "hcenter" || edge === "right";
  const key = "align-" + Date.now();
  const pos = edge === "left" || edge === "top" ? "start" : edge === "right" || edge === "bottom" ? "end" : "center";
  const parentId = nodes[0].parent;
  const sameParent = nodes.every((n) => n.parent === parentId);
  const pl = layoutAt(doc.nodes[parentId], bp);
  if (nodes.length > 1 && sameParent && pl.mode === "free") {
    const boxes = nodes.map((n) => ({ id: n.id, ...freePlacementAt(n, bp) }));
    const lo = Math.min(...boxes.map((b) => horizontal ? b.x : b.y));
    const hi = Math.max(...boxes.map((b) => horizontal ? b.x + b.w : b.y + b.h));
    for (const b of boxes) {
      const size = horizontal ? b.w : b.h;
      const v = pos === "start" ? lo : pos === "end" ? hi - size : Math.round((lo + hi - size) / 2);
      setPlacement(store, b.id, bp, horizontal ? { x: v } : { y: v }, key);
    }
    return true;
  }
  let done = false;
  for (const n of nodes) {
    const parent = doc.nodes[n.parent];
    const l = layoutAt(parent, bp);
    if (l.mode === "grid") {
      updateStyle(store, [n.id], bp, horizontal ? { justifySelf: pos } : { alignSelf: pos }, key);
      done = true;
    } else if (l.mode === "stack") {
      const cross = l.direction === "row" ? !horizontal : horizontal;
      if (cross) {
        updateStyle(store, [n.id], bp, { alignSelf: pos }, key);
        done = true;
      }
    } else {
      const box = canvas.frame?.layoutTargetOf(parent.id)?.getBoundingClientRect();
      const f = freePlacementAt(n, bp);
      const total = horizontal ? box ? Math.round(box.width) : 0 : l.height ?? (box ? Math.round(box.height) : 0);
      if (!total) continue;
      const size = horizontal ? f.w : f.h;
      const v = pos === "start" ? 0 : pos === "end" ? total - size : Math.round((total - size) / 2);
      setPlacement(store, n.id, bp, horizontal ? { x: v } : { y: v }, key);
      done = true;
    }
  }
  if (!done) session.toast("In a stack, children line up along the stack. Use the alignment box of the stack instead.", "info");
  return done;
}
function moveToEnd(session, id, end) {
  const store = session.store;
  const n = store.doc.nodes[id];
  if (!n?.parent || n.locked) return;
  const sibs = store.doc.nodes[n.parent].children ?? [];
  const index = end === "front" ? sibs.length : 0;
  if (end === "front" && sibs[sibs.length - 1] === id || end === "back" && sibs[0] === id) return;
  moveNodes(store, [id], { parent: n.parent, index, bp: session.breakpoint.value });
}
function flipStyle(cur, angle, horizontal) {
  const [sx, sy] = (cur ?? "1 1").split(" ").map(Number);
  const x = horizontal ? -sx : sx;
  const y = horizontal ? sy : -sy;
  const flip2 = x === 1 && y === 1 ? void 0 : `${x} ${y}`;
  return angle ? { flip: flip2, rotation: -angle } : { flip: flip2 };
}
function flip(session, ids, horizontal) {
  const store = session.store;
  const bp = session.breakpoint.value;
  const patches = {};
  for (const id of ids) {
    const n = store.doc.nodes[id];
    if (!n?.parent || n.locked) continue;
    const style = merged(n.style, bp);
    patches[id] = flipStyle(style.flip, style.rotation ?? 0, horizontal);
  }
  if (Object.keys(patches).length) updateStyleEach(store, bp, patches);
}
function guessFlow(session, id) {
  const bp = session.breakpoint.value;
  const node = session.store.doc.nodes[id];
  const boxes = (node?.children ?? []).map((c) => freePlacementAt(session.store.doc.nodes[c], bp));
  if (boxes.length < 2) return "stack-col";
  const spread = (v) => Math.max(...v) - Math.min(...v);
  return spread(boxes.map((b) => b.x + b.w / 2)) > spread(boxes.map((b) => b.y + b.h / 2)) ? "stack-row" : "stack-col";
}
export {
  align,
  flip,
  flipStyle,
  flowOf,
  guessFlow,
  moveToEnd,
  resizeToFit,
  setFlow
};
