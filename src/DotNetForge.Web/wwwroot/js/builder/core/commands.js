import { canContain, componentDef } from "./schema.js";
import { cloneSubtree, createNode, deepClone, newId, placedModuleIds, subtree, topLevel } from "./doc.js";
import { autoFlow, compact, firstFree, placeItem } from "./grid.js";
import { freePlacementAt, gridPlacementAt, layoutAt } from "./responsive.js";
function txDoc(tx) {
  const base = tx.doc;
  return {
    root: base.root,
    nodes: new Proxy(base.nodes, {
      get: (_t, key) => tx.get(key),
      has: (_t, key) => tx.has(key)
    })
  };
}
function gridItemsAt(doc, parentId, bp) {
  const parent = doc.nodes[parentId];
  if (!parent?.children) return [];
  const layout = layoutAt(parent, bp);
  const cols = layout.columns ?? 12;
  const kids = parent.children.map((id) => doc.nodes[id]).filter((n) => !!n && !n.hidden);
  const flow = kids.map((k) => {
    const g = gridPlacementAt(k, parent, bp);
    return g.auto ? { id: k.id, colSpan: g.colSpan, rowSpan: g.rowSpan } : { id: k.id, col: g.col, row: g.row, colSpan: g.colSpan, rowSpan: g.rowSpan };
  });
  return autoFlow(flow, cols);
}
function setResponsive(value, bp, patch) {
  const next = { ...value ?? {} };
  const cur = { ...next[bp] ?? {} };
  for (const [k, v] of Object.entries(patch)) {
    if (v === void 0) delete cur[k];
    else cur[k] = v;
  }
  if (Object.keys(cur).length) next[bp] = cur;
  else delete next[bp];
  return Object.keys(next).length ? next : void 0;
}
function withField(node, key, value) {
  const next = { ...node };
  if (value === void 0) delete next[key];
  else next[key] = value;
  return next;
}
function writeGridItems(tx, items, bp) {
  for (const it of items) {
    tx.update(it.id, (n) => {
      const cur = n.place?.[bp] ?? {};
      if (cur.col === it.col && cur.row === it.row && cur.colSpan === it.colSpan && cur.rowSpan === it.rowSpan) return n;
      return withField(n, "place", setResponsive(n.place, bp, { col: it.col, row: it.row, colSpan: it.colSpan, rowSpan: it.rowSpan }));
    });
  }
}
function breakpointsWithExplicitPlacement(doc, parentId) {
  const parent = doc.nodes[parentId];
  const out = /* @__PURE__ */ new Set(["base"]);
  for (const id of parent?.children ?? []) {
    const p = doc.nodes[id]?.place;
    if (p?.tablet) out.add("tablet");
    if (p?.mobile) out.add("mobile");
  }
  return ["base", "tablet", "mobile"].filter((b) => out.has(b));
}
function compactGrid(tx, parentId) {
  const doc = txDoc(tx);
  const parent = doc.nodes[parentId];
  if (!parent) return;
  for (const bp of breakpointsWithExplicitPlacement(doc, parentId)) {
    const layout = layoutAt(parent, bp);
    if (layout.mode !== "grid" || layout.compact === false) continue;
    const explicit = (parent.children ?? []).some((id) => {
      const n = doc.nodes[id];
      return n && !gridPlacementAt(n, parent, bp).auto;
    });
    if (explicit) writeGridItems(tx, compact(gridItemsAt(doc, parentId, bp)), bp);
  }
}
function reflowGrid(tx, parentId) {
  const doc = txDoc(tx);
  const parent = doc.nodes[parentId];
  if (!parent) return;
  for (const bp of breakpointsWithExplicitPlacement(doc, parentId)) {
    const layout = layoutAt(parent, bp);
    if (layout.mode !== "grid") continue;
    const kids = (parent.children ?? []).map((id) => doc.nodes[id]).filter((n) => !!n && !n.hidden);
    if (!kids.some((k) => !gridPlacementAt(k, parent, bp).auto)) continue;
    const flow = kids.map((k) => {
      const g = gridPlacementAt(k, parent, bp);
      return { id: k.id, colSpan: g.colSpan, rowSpan: g.rowSpan };
    });
    let items = autoFlow(flow, layout.columns ?? 12);
    if (layout.compact !== false) items = compact(items);
    writeGridItems(tx, items, bp);
  }
}
function placeNew(tx, id, target) {
  const doc = txDoc(tx);
  const parent = doc.nodes[target.parent];
  const node = doc.nodes[id];
  if (!parent || !node) return;
  const layout = layoutAt(parent, target.bp);
  if (layout.mode === "grid") {
    const def = componentDef(node.type);
    const g = gridPlacementAt(node, parent, target.bp);
    const colSpan = g.colSpan || def?.place?.colSpan || 4;
    const rowSpan = g.rowSpan || def?.place?.rowSpan || 2;
    const others = gridItemsAt(doc, target.parent, target.bp).filter((i) => i.id !== id);
    const pos = target.at?.col && target.at?.row ? { col: target.at.col, row: target.at.row } : firstFree(others, colSpan, rowSpan, layout.columns ?? 12);
    const items = placeItem(others, id, { ...pos, colSpan, rowSpan }, layout.columns ?? 12, { float: layout.compact !== false });
    writeGridItems(tx, items, target.bp);
    if (target.bp !== "base" && layoutAt(parent, "base").mode === "grid") {
      const baseCols = layoutAt(parent, "base").columns ?? 12;
      const baseOthers = gridItemsAt(txDoc(tx), target.parent, "base").filter((i) => i.id !== id);
      const p = firstFree(baseOthers, Math.min(colSpan, baseCols), rowSpan, baseCols);
      tx.update(id, (n) => withField(n, "place", setResponsive(n.place, "base", { ...p, colSpan: Math.min(colSpan, baseCols), rowSpan })));
    }
  } else if (layout.mode === "free") {
    const x = Math.round(target.at?.x ?? 24), y = Math.round(target.at?.y ?? 24);
    const w = node.type === "heading" || node.type === "text" ? 320 : 240;
    const h = node.type === "heading" ? 60 : node.type === "button" ? 48 : 160;
    tx.update(id, (n) => withField(n, "place", setResponsive(n.place, target.bp, { x, y, w, h })));
  }
}
function insertChild(tx, parentId, childId, index) {
  tx.update(parentId, (p) => {
    const kids = (p.children ?? []).filter((c) => c !== childId);
    const i = index === void 0 || index < 0 || index > kids.length ? kids.length : index;
    kids.splice(i, 0, childId);
    return { ...p, children: kids };
  });
}
function detach(tx, id) {
  const node = tx.get(id);
  if (!node?.parent) return -1;
  let index = -1;
  tx.update(node.parent, (p) => {
    index = (p.children ?? []).indexOf(id);
    return { ...p, children: (p.children ?? []).filter((c) => c !== id) };
  });
  return index;
}
function placedModules(doc) {
  const out = /* @__PURE__ */ new Map();
  const stack = [doc.root];
  while (stack.length) {
    const id = stack.pop();
    const n = doc.nodes[id];
    if (!n) continue;
    if (n.type === "module" && typeof n.props.moduleId === "number" && n.props.moduleId > 0 && !out.has(n.props.moduleId)) out.set(n.props.moduleId, id);
    if (n.children) for (let i = n.children.length - 1; i >= 0; i--) stack.push(n.children[i]);
  }
  return out;
}
function releaseUsedModules(doc, nodes) {
  const used = placedModules(doc);
  return nodes.map((n) => n.type === "module" && typeof n.props.moduleId === "number" && used.has(n.props.moduleId) ? { ...n, props: { ...n.props, moduleId: 0 } } : n);
}
function insertComponent(store, type, target, props) {
  const parent = store.doc.nodes[target.parent];
  if (!parent || !canContain(parent.type, type)) return null;
  const id = newId(store.doc.nodes);
  store.transact("Insert " + (componentDef(type)?.label ?? type), (tx) => {
    const node = createNode(type, id, target.parent);
    if (props) node.props = { ...node.props, ...props };
    tx.put(node);
    insertChild(tx, target.parent, id, target.index);
    placeNew(tx, id, target);
    tx.select([id]);
  });
  return id;
}
function insertSubtree(store, nodes, target, label = "Paste") {
  if (nodes.length === 0) return null;
  const parent = store.doc.nodes[target.parent];
  if (!parent || !canContain(parent.type, nodes[0].type)) return null;
  const taken = new Set(Object.keys(store.doc.nodes));
  const source = { nodes: Object.fromEntries(nodes.map((n) => [n.id, n])) };
  const copy = cloneSubtree(source, nodes[0].id, target.parent, taken);
  store.transact(label, (tx) => {
    for (const n of releaseUsedModules(txDoc(tx), copy.nodes)) tx.put(n);
    insertChild(tx, target.parent, copy.root, target.index);
    placeNew(tx, copy.root, target);
    tx.select([copy.root]);
  });
  return copy.root;
}
function moveNodes(store, ids, target) {
  const doc = store.doc;
  const parent = doc.nodes[target.parent];
  if (!parent) return false;
  const moving = topLevel(doc, ids).filter((id) => id !== doc.root);
  if (moving.length === 0) return false;
  for (const id of moving) {
    if (id === target.parent || subtree(doc, id).includes(target.parent)) return false;
    if (!canContain(parent.type, doc.nodes[id].type)) return false;
  }
  return store.transact(moving.length > 1 ? "Move elements" : "Move", (tx) => {
    let index = target.index;
    const oldParents = /* @__PURE__ */ new Set();
    for (const id of moving) {
      const node = tx.get(id);
      const sameParent = node.parent === target.parent;
      if (node.parent) oldParents.add(node.parent);
      const oldIndex = detach(tx, id);
      if (sameParent && index !== void 0 && oldIndex >= 0 && oldIndex < index) index--;
      tx.update(id, (n) => ({ ...n, parent: target.parent }));
      insertChild(tx, target.parent, id, index);
      if (index !== void 0) index++;
      if (!sameParent) placeNew(tx, id, target);
    }
    if (!target.at) reflowGrid(tx, target.parent);
    for (const p of oldParents) if (p !== target.parent) compactGrid(tx, p);
    tx.select(moving);
  });
}
function replaceContent(store, next, label = "Replace the layout") {
  const doc = store.doc;
  const nextRoot = next.nodes[next.root];
  store.transact(label, (tx) => {
    for (const id of Object.keys(doc.nodes)) if (id !== doc.root) tx.remove(id);
    for (const n of Object.values(next.nodes)) {
      if (n.id !== next.root) tx.put(n.parent === next.root ? { ...n, parent: doc.root } : n);
    }
    tx.update(doc.root, (r) => withField(withField({ ...r, children: [...nextRoot.children ?? []] }, "style", nextRoot.style), "layout", nextRoot.layout));
    tx.settings(deepClone(next.settings));
    tx.select([]);
  });
}
function removeNodes(store, ids) {
  const doc = store.doc;
  const targets = topLevel(doc, ids).filter((id) => id !== doc.root && !doc.nodes[id]?.locked);
  if (targets.length === 0) return false;
  return store.transact(targets.length > 1 ? "Delete elements" : "Delete", (tx) => {
    const parents = /* @__PURE__ */ new Set();
    const modules = /* @__PURE__ */ new Set();
    for (const id of targets) {
      const node = tx.get(id);
      if (!node) continue;
      if (node.parent) parents.add(node.parent);
      detach(tx, id);
      for (const d of subtree(txDoc(tx), id)) {
        const n = tx.get(d);
        if (n?.type === "module" && typeof n.props.moduleId === "number" && n.props.moduleId > 0) modules.add(n.props.moduleId);
        tx.remove(d);
      }
    }
    for (const p of parents) if (tx.has(p)) compactGrid(tx, p);
    const placed = placedModuleIds(txDoc(tx));
    const gone = [...modules].filter((m) => !placed.has(m));
    if (gone.length) {
      const settings = store.doc.settings;
      tx.settings({ ...settings, removedModules: [.../* @__PURE__ */ new Set([...settings.removedModules ?? [], ...gone])].sort((x, y) => x - y) });
    }
    tx.select([]);
  });
}
function duplicateNodes(store, ids) {
  const doc = store.doc;
  const targets = topLevel(doc, ids).filter((id) => id !== doc.root);
  const created = [];
  if (targets.length === 0) return created;
  store.transact("Duplicate", (tx) => {
    const taken = new Set(Object.keys(doc.nodes));
    for (const id of targets) {
      const node = tx.get(id);
      const parentId = node.parent;
      const copy = cloneSubtree(txDoc(tx), id, parentId, taken);
      for (const n of releaseUsedModules(txDoc(tx), copy.nodes)) tx.put(n);
      const index = (tx.get(parentId).children ?? []).indexOf(id) + 1;
      insertChild(tx, parentId, copy.root, index);
      const parent = tx.get(parentId);
      for (const bp of ["base", "tablet", "mobile"]) {
        if (!node.place?.[bp]) continue;
        const l = layoutAt(parent, bp);
        if (l.mode === "grid") {
          const g = gridPlacementAt(node, parent, bp);
          const others = gridItemsAt(txDoc(tx), parentId, bp).filter((i) => i.id !== copy.root);
          const p = firstFree(others, g.colSpan, g.rowSpan, l.columns ?? 12);
          tx.update(copy.root, (n) => withField(n, "place", setResponsive(n.place, bp, { ...p, colSpan: g.colSpan, rowSpan: g.rowSpan })));
        } else if (l.mode === "free") {
          const f = freePlacementAt(node, bp);
          tx.update(copy.root, (n) => withField(n, "place", setResponsive(n.place, bp, { x: f.x + 16, y: f.y + 16 })));
        }
      }
      created.push(copy.root);
    }
    tx.select(created);
  });
  return created;
}
function updateProps(store, id, patch, merge) {
  store.transact("Edit content", (tx) => {
    tx.update(id, (n) => ({ ...n, props: { ...n.props, ...patch } }));
  }, { merge: merge ? id + ":props:" + merge : void 0 });
}
function updateStyleEach(store, bp, patches) {
  store.transact("Change style", (tx) => {
    for (const id of Object.keys(patches)) tx.update(id, (n) => withField(n, "style", setResponsive(n.style, bp, patches[id])));
  });
}
function updateStyle(store, ids, bp, patch, merge) {
  store.transact("Change style", (tx) => {
    for (const id of ids) tx.update(id, (n) => withField(n, "style", setResponsive(n.style, bp, patch)));
  }, { merge: merge ? ids.join(",") + ":style:" + bp + ":" + merge : void 0 });
}
function updateLayout(store, id, bp, patch, merge, childPlacements) {
  store.transact("Change layout", (tx) => {
    tx.update(id, (n) => withField(n, "layout", setResponsive(n.layout, bp, patch)));
    if (childPlacements) {
      for (const [cid, p] of Object.entries(childPlacements)) {
        tx.update(cid, (n) => withField(n, "place", setResponsive(n.place, bp, p)));
      }
    }
  }, { merge: merge ? id + ":layout:" + bp + ":" + merge : void 0 });
}
function setPlacement(store, id, bp, patch, merge) {
  store.transact("Move", (tx) => {
    tx.update(id, (n) => withField(n, "place", setResponsive(n.place, bp, patch)));
  }, { merge: merge ? id + ":place:" + bp + ":" + merge : void 0 });
}
function moveGridItem(store, id, bp, target) {
  const node = store.doc.nodes[id];
  if (!node?.parent) return;
  const parent = store.doc.nodes[node.parent];
  const l = layoutAt(parent, bp);
  if (l.mode !== "grid") return;
  const items = placeItem(gridItemsAt(store.doc, parent.id, bp), id, target, l.columns ?? 12, { float: l.compact !== false });
  store.transact("Move", (tx) => writeGridItems(tx, items, bp));
}
function resetBreakpoint(store, ids, bp, fields) {
  if (bp === "base") return;
  store.transact("Reset " + bp, (tx) => {
    for (const id of ids) {
      tx.update(id, (n) => {
        let next = n;
        for (const f of fields) {
          const v = next[f];
          if (v?.[bp]) {
            const copy = { ...v };
            delete copy[bp];
            next = withField(next, f, Object.keys(copy).length ? copy : void 0);
          }
        }
        return next;
      });
    }
  });
}
function renameNode(store, id, name) {
  store.transact("Rename", (tx) => tx.update(id, (n) => withField(n, "name", name.trim() ? name.trim().slice(0, 120) : void 0)));
}
function setFlag(store, ids, flag, on) {
  store.transact(flag === "hidden" ? on ? "Hide" : "Show" : on ? "Lock" : "Unlock", (tx) => {
    for (const id of ids) if (id !== store.doc.root) tx.update(id, (n) => withField(n, flag, on ? true : void 0));
  });
}
function setVisibility(store, id, visibility) {
  store.transact("Change visibility", (tx) => tx.update(id, (n) => withField(n, "visibility", visibility && Object.keys(visibility).length ? visibility : void 0)));
}
function setAdvanced(store, id, patch) {
  store.transact("Change advanced settings", (tx) => tx.update(id, (n) => {
    let next = n;
    if ("className" in patch) next = withField(next, "className", patch.className || void 0);
    if ("vars" in patch) next = withField(next, "vars", patch.vars && Object.keys(patch.vars).length ? patch.vars : void 0);
    if ("permissions" in patch) next = withField(next, "permissions", patch.permissions?.editRoles?.length ? patch.permissions : void 0);
    return next;
  }));
}
function wrapNodes(store, ids, type = "container") {
  const doc = store.doc;
  const targets = topLevel(doc, ids).filter((id2) => id2 !== doc.root);
  if (targets.length === 0) return null;
  const parentId = doc.nodes[targets[0]].parent;
  if (targets.some((id2) => doc.nodes[id2].parent !== parentId)) return null;
  const parent = doc.nodes[parentId];
  if (!canContain(parent.type, type) || targets.some((id2) => !canContain(type, doc.nodes[id2].type))) return null;
  const id = newId(doc.nodes);
  const ordered = [...targets].sort((a, b) => parent.children.indexOf(a) - parent.children.indexOf(b));
  const first = doc.nodes[ordered[0]];
  store.transact("Wrap in container", (tx) => {
    const wrapper = createNode(type, id, parentId);
    wrapper.children = [...ordered];
    if (first.place) wrapper.place = deepClone(first.place);
    tx.put(wrapper);
    const index = parent.children.indexOf(ordered[0]);
    for (const c of ordered) {
      detach(tx, c);
      tx.update(c, (n) => withField({ ...n, parent: id }, "place", void 0));
    }
    insertChild(tx, parentId, id, index);
    tx.select([id]);
  });
  return id;
}
function unwrapNode(store, id) {
  const doc = store.doc;
  const node = doc.nodes[id];
  if (!node?.parent || !node.children) return false;
  const parent = doc.nodes[node.parent];
  if (node.children.some((c) => !canContain(parent.type, doc.nodes[c].type))) return false;
  return store.transact("Remove container", (tx) => {
    const index = detach(tx, id);
    let i = index;
    for (const c of node.children) {
      tx.update(c, (n) => ({ ...n, parent: parent.id }));
      insertChild(tx, parent.id, c, i++);
    }
    tx.remove(id);
    compactGrid(tx, parent.id);
    tx.select([...node.children]);
  });
}
function setBreakpoints(store, tablet, mobile, merge) {
  store.transact("Change breakpoints", (tx) => tx.settings({ ...store.doc.settings, breakpoints: { tablet, mobile } }), { merge: merge ? "breakpoints:" + merge : void 0 });
}
export {
  duplicateNodes,
  gridItemsAt,
  insertComponent,
  insertSubtree,
  moveGridItem,
  moveNodes,
  placedModules,
  removeNodes,
  renameNode,
  replaceContent,
  resetBreakpoint,
  setAdvanced,
  setBreakpoints,
  setFlag,
  setPlacement,
  setVisibility,
  unwrapNode,
  updateLayout,
  updateProps,
  updateStyle,
  updateStyleEach,
  wrapNodes,
  writeGridItems
};
