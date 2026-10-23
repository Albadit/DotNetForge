/**
 * The layers panel: the page as a tree. Rows subscribe to their own node, so
 * editing one element re-renders one row. Rows can be selected (Shift for a
 * range, Ctrl/Cmd to add), renamed (double-click), hidden, locked and dragged
 * to reorder or nest.
 *
 * As in Figma, the order means two things: in a stack or grid (auto layout)
 * the top row is the first item of the flow; in a free container the top row
 * is the frontmost layer, so its children are listed last-first.
 */
import { jsx, Fragment, useEffect, useMemo, useRef, useState } from "./vdom.js";
import { canContain } from "../core/schema.js";
import { ancestors, displayName, isAncestor, walk } from "../core/doc.js";
import { moveNodes, renameNode, setFlag } from "../core/commands.js";
import { signal } from "../core/signal.js";
import { layoutAt } from "../core/responsive.js";
import { Icon } from "./icons.js";
import { cls, useDerived, useEditor, useNode, useSignal, useStructure } from "./hooks.js";
import { openContextMenu } from "./context-menu.js";
const expanded = signal(new Set());
/** Sections (depth 1) start open; these were closed by the user. */
const collapsed = signal(new Set());
const dropSpot = signal(null);
const dragging = signal(null);
/** Free and grid containers list their children last-first, as Figma does (frames: frontmost on top). */
/**
 * A free frame lists its layers front-most first (the last child on top, as in Figma). Stacks
 * and grids list them in reading order, the order they appear on the page.
 */
function isStacking(node, bp) {
    if (!node?.children)
        return false;
    return layoutAt(node, bp).mode === 'free';
}
const SIDE = { start: 'left', center: 'center', end: 'right' };
const EDGE = { start: 'top', center: 'center', end: 'bottom' };
const TYPE_ICON = {
    page: 'layerPage', heading: 'layerText', text: 'layerText', image: 'layerImage', button: 'layerInstance', links: 'layerLink', video: 'layerVideo',
    spacer: 'layerRect', divider: 'layerLine', module: 'layerWidget', icon: 'layerStar', post: 'layerSticky',
};
/**
 * Figma's layer icons: an auto layout frame shows its direction and how it
 * aligns (vertical/horizontal: the cross axis; wrap: the main axis), a grid
 * the grid icon, a frame without auto layout the frame icon.
 */
function layerIcon(node, bp) {
    if (node.children && node.type !== 'page') {
        const l = layoutAt(node, bp);
        const cross = l.align === 'center' || l.align === 'end' ? l.align : 'start';
        const main = l.justify === 'center' || l.justify === 'end' ? l.justify : 'start';
        if (l.mode === 'stack')
            return l.direction === 'row' ? (l.wrap ? 'alW' + SIDE[main] : 'alH' + EDGE[cross]) : 'alV' + SIDE[cross];
        if (l.mode === 'grid')
            return 'grid';
        return 'layerFrame';
    }
    return TYPE_ICON[node.type] ?? 'layerFrame';
}
export function LayersPanel() {
    const { session } = useEditor();
    useStructure();
    const rev = useSignal(session.docRevision);
    const [query, setQuery] = useState('');
    const doc = session.store.doc;
    const root = doc.nodes[doc.root];
    // Reveal the selection (expand its ancestors) without re-rendering the whole tree.
    useEffect(() => session.store.subscribeSelection(selection => {
        if (!selection.length)
            return;
        const next = new Set(expanded.value);
        let changed = false;
        for (const id of selection)
            for (const a of ancestors(session.store.doc, id))
                if (!next.has(a)) {
                    next.add(a);
                    changed = true;
                }
        if (changed)
            expanded.set(next);
        requestAnimationFrame(() => document.querySelector(`.pe-layer-row[data-id="${selection[0]}"]`)?.scrollIntoView({ block: 'nearest' }));
    }), [rev]);
    const matches = useMemo(() => {
        const q = query.trim().toLowerCase();
        if (!q)
            return null;
        return walk(doc).filter(id => {
            const n = doc.nodes[id];
            return id !== doc.root && (displayName(n).toLowerCase().includes(q) || n.type.includes(q) || id === q);
        });
    }, [query, doc]);
    if (!root)
        return null;
    return (jsx("div", { class: "pe-layers", role: "tree", "aria-label": "Layers" },
        jsx("div", { class: "pe-layers-bar" },
            jsx("div", { class: "pe-search" },
                jsx(Icon, { name: "search", size: 16 }),
                jsx("input", { type: "search", placeholder: "Search layers", value: query, "aria-label": "Search layers", onInput: e => setQuery(e.target.value) })),
            jsx("button", { type: "button", class: "pe-icon-btn", title: "Collapse layers", "aria-label": "Collapse layers", onClick: () => {
                    expanded.set(new Set());
                    collapsed.set(new Set(root.children ?? []));
                } },
                jsx(Icon, { name: "collapseLayers", size: 16 }))),
        jsx("div", { class: "pe-layer-list" }, matches ? (matches.length ? matches.map(id => jsx(LayerRow, { key: id, id: id, depth: 0, flat: true })) : jsx("div", { class: "pe-empty-state" },
            "No layer matches \u201C",
            query,
            "\u201D.")) : (
        // As in Figma, the list starts at the page's layers; the page itself has no row.
        (isStacking(root, session.breakpoint.value) ? [...(root.children ?? [])].reverse() : root.children ?? []).map(id => jsx(LayerRow, { key: id, id: id, depth: 0 }))))));
}
function LayerRow({ id, depth, flat }) {
    const { session, canvas } = useEditor();
    const node = useNode(id);
    const isOpen = useDerived(cb => expanded.subscribe(cb), () => expanded.value.has(id), [id]);
    const isClosed = useDerived(cb => collapsed.subscribe(cb), () => collapsed.value.has(id), [id]);
    // Top-level layers start open; deeper ones open when asked (or to reveal the selection).
    const open = isOpen || (depth === 0 && !flat && !isClosed);
    // Each row follows only its own state, so selecting or hovering re-renders two rows, not all.
    const rev = useSignal(session.docRevision);
    const selected = useDerived(cb => session.store.subscribeSelection(cb), () => session.store.selection.includes(id), [id, rev]);
    const hover = useDerived(cb => session.hover.subscribe(cb), () => session.hover.value === id, [id]);
    const spotWhere = useDerived(cb => dropSpot.subscribe(cb), () => (dropSpot.value?.rowId === id ? dropSpot.value.where : null), [id]);
    const isDragged = useDerived(cb => dragging.subscribe(cb), () => !!dragging.value?.includes(id), [id]);
    const caps = useSignal(session.caps);
    const renamingRequested = useDerived(cb => session.renaming.subscribe(cb), () => session.renaming.value === id, [id]);
    const [renamingLocal, setRenamingLocal] = useState(false);
    const renaming = renamingLocal || renamingRequested;
    const setRenaming = (on) => {
        setRenamingLocal(on);
        if (!on && session.renaming.value === id)
            session.renaming.set(null);
    };
    const bp = useSignal(session.breakpoint);
    const rowRef = useRef(null);
    if (!node)
        return null;
    const doc = session.store.doc;
    const isRoot = id === doc.root;
    const kids = node.children ?? [];
    const listed = isStacking(node, bp) ? [...kids].reverse() : kids;
    const toggle = (e) => {
        e.stopPropagation();
        const next = new Set(expanded.value);
        const closed = new Set(collapsed.value);
        if (open) {
            next.delete(id);
            closed.add(id);
        }
        else {
            next.add(id);
            closed.delete(id);
        }
        expanded.set(next);
        collapsed.set(closed);
    };
    const select = (e) => {
        const store = session.store;
        if (e.shiftKey && store.selection.length) {
            const order = walk(store.doc);
            const a = order.indexOf(store.selection[store.selection.length - 1]);
            const b = order.indexOf(id);
            const [from, to] = a < b ? [a, b] : [b, a];
            const parent = node.parent;
            store.select(order.slice(from, to + 1).filter(x => store.doc.nodes[x]?.parent === parent));
        }
        else if (e.ctrlKey || e.metaKey) {
            store.select(selected ? store.selection.filter(s => s !== id) : [...store.selection, id]);
        }
        else {
            store.select([id]);
        }
        canvas.scrollIntoView(id);
    };
    const onPointerDown = (e) => {
        if (e.button !== 0 || isRoot || renaming || !caps.editLayout || node.locked)
            return;
        const startY = e.clientY;
        let started = false;
        const ids = selected ? session.store.selection : [id];
        const move = (ev) => {
            if (!started && Math.abs(ev.clientY - startY) < 4)
                return;
            started = true;
            dragging.set(ids);
            dropSpot.set(computeSpot(ev.clientX, ev.clientY, ids));
        };
        const up = () => {
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            const s = dropSpot.value;
            dragging.set(null);
            dropSpot.set(null);
            if (started && s)
                moveNodes(session.store, ids, { parent: s.parent, index: s.index, bp: session.breakpoint.value });
        };
        window.addEventListener('pointermove', move);
        window.addEventListener('pointerup', up);
    };
    const computeSpot = (x, y, ids) => {
        const el = document.elementFromPoint(x, y)?.closest('.pe-layer-row');
        const targetId = el?.dataset.id;
        const d = session.store.doc;
        if (!el || !targetId || ids.includes(targetId) || ids.some(m => isAncestor(d, m, targetId)))
            return null;
        const target = d.nodes[targetId];
        const r = el.getBoundingClientRect();
        const rel = (y - r.top) / r.height;
        const type = d.nodes[ids[0]].type;
        const canInside = !!target.children && canContain(target.type, type) && !target.locked;
        const bpNow = session.breakpoint.value;
        // Rows above/below map to earlier/later children, or the reverse in a free container (listed frontmost first).
        const indexFor = (parentId, where) => {
            const parent = d.nodes[parentId];
            const i = parent.children.indexOf(targetId);
            return (where === 'before') !== isStacking(parent, bpNow) ? i : i + 1;
        };
        if (target.parent && (rel < 0.3 || (!canInside && rel < 0.5))) {
            const parent = d.nodes[target.parent];
            if (canContain(parent.type, type))
                return { parent: parent.id, index: indexFor(parent.id, 'before'), rowId: targetId, where: 'before' };
        }
        // Dropped onto a container: the top of its list (the front in a free container, the first item in a stack).
        if (canInside && (rel <= 0.7 || !target.parent))
            return { parent: targetId, index: isStacking(target, bpNow) ? target.children.length : 0, rowId: targetId, where: 'inside' };
        if (target.parent) {
            const parent = d.nodes[target.parent];
            if (canContain(parent.type, type))
                return { parent: parent.id, index: indexFor(parent.id, 'after'), rowId: targetId, where: 'after' };
        }
        return null;
    };
    return (jsx("div", { role: "treeitem", "aria-selected": selected, "aria-expanded": kids.length ? open : undefined },
        jsx("div", { ref: rowRef, "data-id": id, class: cls('pe-layer-row', selected && 'is-selected', hover && 'is-hover', node.hidden && 'is-hidden', node.locked && 'is-locked', isDragged && 'is-dragging', spotWhere && 'drop-' + spotWhere), style: { paddingLeft: (flat ? 8 : 8 + depth * 14) + 'px' }, onClick: select, onDblClick: () => !isRoot && caps.editLayout && setRenaming(true), onPointerDown: onPointerDown, onPointerEnter: () => session.hover.set(id), onPointerLeave: () => session.hover.value === id && session.hover.set(null), onContextMenu: e => {
                e.preventDefault();
                if (!selected)
                    session.store.select([id]);
                openContextMenu(e.clientX, e.clientY);
            } },
            !flat && (jsx("button", { type: "button", class: cls('pe-layer-caret', !kids.length && 'is-empty'), tabIndex: -1, onClick: toggle, "aria-label": open ? 'Collapse' : 'Expand' }, kids.length > 0 && jsx(Icon, { name: open ? 'chevronDown16' : 'chevronRight16', size: 16 }))),
            jsx(Icon, { name: layerIcon(node, bp), size: 16, class: "pe-layer-icon" }),
            renaming ? (jsx("input", { class: "pe-layer-rename", autoFocus: true, defaultValue: node.name ?? displayName(node), onClick: e => e.stopPropagation(), onBlur: e => {
                    renameNode(session.store, id, e.target.value);
                    setRenaming(false);
                }, onKeyDown: e => {
                    if (e.key === 'Enter')
                        e.target.blur();
                    if (e.key === 'Escape')
                        setRenaming(false);
                    e.stopPropagation();
                } })) : (jsx("span", { class: "pe-layer-name" },
                displayName(node),
                flat && node.parent && jsx("span", { class: "pe-layer-path" },
                    " \u00B7 ",
                    displayName(doc.nodes[node.parent])))),
            jsx(LayerBadges, { id: id }),
            !isRoot && caps.editLayout && (jsx("span", { class: "pe-layer-tools" },
                jsx("button", { type: "button", class: cls('pe-layer-tool', node.locked && 'is-on'), title: node.locked ? 'Unlock' : 'Lock', "aria-pressed": !!node.locked, onClick: e => {
                        e.stopPropagation();
                        setFlag(session.store, [id], 'locked', !node.locked);
                    } },
                    jsx(Icon, { name: node.locked ? 'lock16' : 'unlock16', size: 16 })),
                jsx("button", { type: "button", class: cls('pe-layer-tool', node.hidden && 'is-on'), title: node.hidden ? 'Show' : 'Hide', "aria-pressed": !!node.hidden, onClick: e => {
                        e.stopPropagation();
                        setFlag(session.store, [id], 'hidden', !node.hidden);
                    } },
                    jsx(Icon, { name: node.hidden ? 'eyeOff16' : 'eye16', size: 16 }))))),
        !flat && open && kids.length > 0 && (jsx("div", { role: "group" }, listed.map(k => jsx(LayerRow, { key: k, id: k, depth: depth + 1 }))))));
}
function LayerBadges({ id }) {
    const node = useNode(id);
    if (!node)
        return null;
    const v = node.visibility;
    const out = [];
    if (v?.roles?.length || v?.audience)
        out.push(jsx("span", { key: "aud", class: "pe-badge", title: "Shown to some visitors only" },
            jsx(Icon, { name: "users", size: 14 })));
    if (v?.devices && v.devices.length < 3)
        out.push(jsx("span", { key: "dev", class: "pe-badge", title: 'Shown on: ' + (v.devices.join(', ') || 'no device') },
            jsx(Icon, { name: "mobile", size: 14 })));
    if (node.permissions?.editRoles?.length)
        out.push(jsx("span", { key: "perm", class: "pe-badge", title: "Editing restricted to some roles" },
            jsx(Icon, { name: "lock", size: 14 })));
    if (node.type === 'module')
        out.push(jsx("span", { key: "mod", class: "pe-badge pe-badge-text" },
            "#",
            String(node.props.moduleId ?? '')));
    return jsx(Fragment, null, out);
}
