/**
 * The canvas area (port of Canvas.tsx): the page in an iframe at the device width, scaled to fit,
 * with an interaction layer on top (so clicks select instead of following
 * links) and the overlay that draws selection, handles, grid and guides.
 */
import { jsx, useEffect, useLayoutEffect, useRef, useState } from "./vdom.js";
import { displayName } from "../core/doc.js";
import { gridPlacementAt, layoutAt } from "../core/responsive.js";
import { gridItemsAt } from "../core/commands.js";
import { Icon } from "./icons.js";
import { gridMetrics } from "../editor/canvas/geometry.js";
import { cls, useEditor, useSelection, useSignal } from "./hooks.js";
import { openContextMenu } from "./context-menu.js";
const HANDLES = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];
export function CanvasView() {
    const { session, canvas, inline } = useEditor();
    const area = useRef(null);
    const stage = useRef(null);
    const iframe = useRef(null);
    const [size, setSize] = useState({ w: 1000, h: 700 });
    const bp = useSignal(session.breakpoint);
    const widths = useSignal(session.deviceWidths);
    const zoomMode = useSignal(session.zoomMode);
    const zoom = useSignal(session.zoom);
    const ready = useSignal(canvas.ready);
    const error = useSignal(canvas.error);
    const editing = useSignal(session.editingText);
    const deviceWidth = widths[bp];
    useLayoutEffect(() => {
        const el = area.current;
        const ro = new ResizeObserver(() => setSize({ w: el.clientWidth, h: el.clientHeight }));
        ro.observe(el);
        setSize({ w: el.clientWidth, h: el.clientHeight });
        return () => ro.disconnect();
    }, []);
    useEffect(() => {
        if (zoomMode !== 'fit')
            return;
        const fit = Math.min(1, Math.max(0.1, (size.w - 48) / deviceWidth));
        session.zoom.set(Math.round(fit * 100) / 100);
    }, [zoomMode, size.w, deviceWidth]);
    useEffect(() => {
        canvas.attach(iframe.current, stage.current);
        return () => canvas.detach();
    }, []);
    useEffect(() => {
        canvas.invalidate();
    }, [deviceWidth, zoom, size.h]);
    // The canvas is the site's page view (PageBuilderController.Canvas, with the page's theme). It shows the page
    // light or dark as chosen in the ⋯ menu: data-theme on its <html> (the Forge tokens and the sample theme read it)
    // and color-scheme for the scrollbars. Through the CSSOM - the CSP allows no style attribute. (Not in the reference.)
    const canvasTheme = useSignal(useEditor().canvasTheme);
    useEffect(() => {
        const root = ready ? canvas.frame?.document?.documentElement : null;
        if (!root)
            return;
        root.setAttribute('data-theme', canvasTheme);
        root.style.colorScheme = canvasTheme;
    }, [ready, canvasTheme]);
    const stageH = Math.max(200, size.h - 24);
    const stageW = deviceWidth * zoom;
    const left = Math.max(24, (size.w - stageW) / 2);
    return (jsx("div", { class: cls('pe-canvas-area', 'bp-' + bp), ref: area },
        jsx("div", { class: "pe-stage", ref: stage, style: { width: stageW + 'px', height: stageH + 'px', left: left + 'px' } },
            jsx("iframe", { ref: iframe, class: "pe-frame", title: "Page being edited", style: { width: deviceWidth + 'px', height: stageH / zoom + 'px', transform: `scale(${zoom})` } }),
            jsx("div", { class: "pe-interaction", style: { pointerEvents: editing ? 'none' : 'auto', cursor: canvas.busy ? 'grabbing' : 'default' }, onPointerDown: canvas.onPointerDown, onPointerMove: canvas.onPointerMove, onPointerUp: canvas.onPointerUp, onPointerLeave: canvas.onPointerLeave, onWheel: canvas.onWheel, onDblClick: e => {
                    // Figma: each double-click goes one layer deeper; on the deepest one, text is edited.
                    const p = canvas.toCanvas(e.clientX, e.clientY);
                    const next = canvas.deeper(p.x, p.y);
                    if (next) {
                        session.store.select([next]);
                        return;
                    }
                    const id = session.store.selection[0];
                    if (id && inline.canEdit(id))
                        inline.start(id, p);
                }, onContextMenu: e => {
                    e.preventDefault();
                    openContextMenu(e.clientX, e.clientY);
                } }),
            jsx(Overlay, null)),
        !ready && !error && (jsx("div", { class: "pe-canvas-loading", role: "status" },
            jsx(Icon, { name: "loader", size: 16, class: "pe-spin" }),
            " Loading the page\u2026")),
        error && (jsx("div", { class: "pe-canvas-error", role: "alert" },
            jsx(Icon, { name: "alert", size: 16 }),
            jsx("div", null,
                jsx("strong", null, "The canvas could not be opened."),
                jsx("p", null, error),
                jsx("button", { type: "button", class: "pe-btn", onClick: () => canvas.load() }, "Try again")))),
        jsx("div", { class: "pe-device-label" },
            deviceWidth,
            " px \u00B7 ",
            Math.round(zoom * 100),
            "%")));
}
function Overlay() {
    const { session, canvas } = useEditor();
    useSignal(canvas.tick);
    const selection = useSelection();
    const hover = useSignal(session.hover);
    const drag = useSignal(canvas.drag);
    const editing = useSignal(session.editingText);
    const showGrid = useSignal(session.showGrid);
    const caps = useSignal(session.caps);
    const bp = session.breakpoint.value;
    if (!canvas.frame)
        return jsx("div", { class: "pe-overlay" });
    const doc = session.store.doc;
    const S = (r) => canvas.toStage(r);
    const box = (r) => {
        const s = S(r);
        return { left: s.x + 'px', top: s.y + 'px', width: s.w + 'px', height: s.h + 'px' };
    };
    const parts = [];
    // Grid of the selected container (or of the selected item's parent): columns and item outlines with spans.
    const primary = selection[0];
    const primaryNode = primary ? doc.nodes[primary] : undefined;
    let gridOwner = null;
    if (primaryNode && primaryNode.children && layoutAt(primaryNode, bp).mode === 'grid')
        gridOwner = primary;
    else if (primaryNode?.parent && layoutAt(doc.nodes[primaryNode.parent], bp).mode === 'grid')
        gridOwner = primaryNode.parent;
    if (drag?.grid)
        gridOwner = null;
    const metrics = drag?.grid ?? (gridOwner && showGrid ? (() => {
        const el = canvas.frame.layoutTargetOf(gridOwner);
        const l = layoutAt(doc.nodes[gridOwner], bp);
        return el ? gridMetrics(el, l.columns ?? 12, l.rowHeight ?? 40) : null;
    })() : null);
    if (metrics && showGrid) {
        const h = metrics.box.h;
        metrics.cols.forEach((x, i) => {
            parts.push(jsx("div", { key: 'col' + i, class: "pe-ov-col", style: box({ x, y: metrics.box.y, w: metrics.colWidths[i], h }) }));
        });
    }
    if (gridOwner && showGrid && !drag) {
        const owner = doc.nodes[gridOwner];
        for (const it of gridItemsAt(doc, gridOwner, bp)) {
            const r = canvas.rectOfNode(it.id);
            // The selected item shows its size in the selection badge instead.
            if (!r || selection.includes(it.id))
                continue;
            const auto = gridPlacementAt(doc.nodes[it.id], owner, bp).auto;
            parts.push(jsx("div", { key: 'gi' + it.id, class: cls('pe-ov-item', auto && 'is-auto'), style: box(r) },
                jsx("span", { class: "pe-ov-span" },
                    it.colSpan,
                    " \u00D7 ",
                    it.rowSpan)));
        }
    }
    // Hover outline.
    if (hover && !selection.includes(hover) && !drag && !editing) {
        const r = canvas.rectOfNode(hover);
        const n = doc.nodes[hover];
        if (r && n) {
            parts.push(jsx("div", { key: "hover", class: "pe-ov-hover", style: box(r) },
                jsx("span", { class: "pe-ov-tag" }, displayName(n))));
        }
    }
    // Selection.
    if (!drag || drag.kind === 'resize') {
        selection.forEach((id, i) => {
            const r = canvas.rectOfNode(id);
            const n = doc.nodes[id];
            if (!r || !n)
                return;
            const single = selection.length === 1;
            const canResize = single && caps.editLayout && !n.locked && id !== doc.root && !editing;
            parts.push(jsx("div", { key: 'sel' + id, class: cls('pe-ov-sel', n.locked && 'is-locked', editing === id && 'is-editing'), style: box(r) },
                i === 0 && jsx("span", { class: "pe-ov-tag is-sel" },
                    n.locked && jsx(Icon, { name: "lock", size: 12 }),
                    " ",
                    displayName(n)),
                canResize && HANDLES.map(h => (jsx("span", { key: h, class: 'pe-ov-handle h-' + h, onPointerDown: e => canvas.beginResize(h, e), onPointerMove: canvas.onPointerMove, onPointerUp: canvas.onPointerUp }))),
                single && !drag && jsx(SizeBadge, { id: id, rect: r })));
        });
    }
    // Drag feedback.
    if (drag) {
        if (drag.container)
            parts.push(jsx("div", { key: "dc", class: "pe-ov-container", style: box(drag.container) }));
        if (drag.indicator) {
            const ind = drag.indicator;
            if (ind.kind === 'cell')
                parts.push(jsx("div", { key: "di", class: "pe-ov-cell", style: box(ind.rect) }));
            else if (ind.kind === 'box')
                parts.push(jsx("div", { key: "di", class: "pe-ov-dropbox", style: box(ind.rect) }));
            else {
                const s = S({ x: ind.x, y: ind.y, w: ind.vertical ? 0 : ind.length, h: ind.vertical ? ind.length : 0 });
                parts.push(jsx("div", { key: "di", class: cls('pe-ov-line', ind.vertical && 'is-v'), style: { left: s.x + 'px', top: s.y + 'px', width: (ind.vertical ? 2 : s.w) + 'px', height: (ind.vertical ? s.h : 2) + 'px' } }));
            }
        }
        if (drag.ghost)
            parts.push(jsx("div", { key: "ghost", class: cls('pe-ov-ghost', drag.kind === 'insert' && 'is-insert'), style: box(drag.ghost) }, drag.kind === 'insert' ? drag.label : null));
        drag.guides.forEach((g, i) => {
            const a = S({ x: g.x1, y: g.y1, w: g.x2 - g.x1, h: g.y2 - g.y1 });
            parts.push(jsx("div", { key: 'g' + i, class: "pe-ov-guide", style: { left: a.x + 'px', top: a.y + 'px', width: Math.max(1, a.w) + 'px', height: Math.max(1, a.h) + 'px' } }));
        });
        if (drag.label && drag.labelAt && drag.kind !== 'insert') {
            const p = S({ x: drag.labelAt.x, y: drag.labelAt.y, w: 0, h: 0 });
            parts.push(jsx("span", { key: "dl", class: "pe-ov-badge is-live", style: { left: p.x + 'px', top: p.y + 'px' } }, drag.label));
        }
    }
    if (editing)
        parts.push(jsx(TextToolbar, { key: "tt", id: editing }));
    return jsx("div", { class: "pe-overlay" }, parts);
}
function SizeBadge({ id, rect }) {
    const { session } = useEditor();
    const doc = session.store.doc;
    const n = doc.nodes[id];
    const bp = session.breakpoint.value;
    let text = `${Math.round(rect.w)} × ${Math.round(rect.h)}`;
    if (n?.parent) {
        const parent = doc.nodes[n.parent];
        if (layoutAt(parent, bp).mode === 'grid') {
            const it = gridItemsAt(doc, parent.id, bp).find(i => i.id === id);
            if (it)
                text = `${it.colSpan} × ${it.rowSpan}`;
        }
    }
    return jsx("span", { class: "pe-ov-badge" }, text);
}
function TextToolbar({ id }) {
    const { session, canvas, inline } = useEditor();
    const r = canvas.rectOfNode(id);
    if (!r)
        return null;
    const s = canvas.toStage(r);
    const rich = session.store.doc.nodes[id]?.type === 'text';
    const btn = (icon, title, run) => (jsx("button", { type: "button", class: "pe-tt-btn", title: title, "aria-label": title, onPointerDown: e => {
            e.preventDefault();
            run();
        } },
        jsx(Icon, { name: icon, size: 16 })));
    const isText = session.store.doc.nodes[id]?.type === 'button';
    return (jsx("div", { class: "pe-text-toolbar", style: { left: s.x + 'px', top: Math.max(4, s.y - 40) + 'px' }, role: "toolbar", "aria-label": "Text formatting" },
        !isText && btn('bold', 'Bold (Ctrl+B)', () => inline.format('bold')),
        !isText && btn('italic', 'Italic (Ctrl+I)', () => inline.format('italic')),
        !isText && btn('underline', 'Underline (Ctrl+U)', () => inline.format('underline')),
        !isText && btn('strike', 'Strikethrough', () => inline.format('strikeThrough')),
        !isText && btn('link', 'Link (Ctrl+K)', () => inline.link()),
        rich && jsx("span", { class: "pe-tt-sep" }),
        rich && btn('list', 'Bulleted list', () => inline.format('insertUnorderedList')),
        rich && btn('listOrdered', 'Numbered list', () => inline.format('insertOrderedList')),
        rich && btn('heading', 'Subheading', () => inline.format('formatBlock', 'h3')),
        rich && btn('text', 'Paragraph', () => inline.format('formatBlock', 'p')),
        jsx("span", { class: "pe-tt-sep" }),
        jsx("button", { type: "button", class: "pe-tt-done", onPointerDown: e => {
                e.preventDefault();
                inline.commit();
            } }, "Done")));
}
