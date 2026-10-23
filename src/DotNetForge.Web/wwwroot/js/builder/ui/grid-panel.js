/**
 * Figma's grid auto layout controls (docs/FIGMA_PARITY.md):
 *
 * - The Grid box shows "columns × rows" (or "× Auto") over a sketch of the
 *   tracks; it opens a picker with the two counts, a 12 × 8 cell grid to pick
 *   both at once, and "Open grid settings".
 * - Grid settings replace the design panel: every column and row with its
 *   sizing (Fixed / Hug / Fill), its value (px, measured px, fr), add and
 *   remove.
 */
import { jsx, useRef, useState } from "./vdom.js";
import { updateLayout } from "../core/commands.js";
import { layoutAt } from "../core/responsive.js";
import { parseTracks } from "../editor/canvas/geometry.js";
import { DropMenu, IconButton, NumberField, useDismiss } from "./fields.js";
import { cls, useEditor, useSignal } from "./hooks.js";
const PICK_COLS = 12;
const PICK_ROWS = 8;
const FR_STEPS = [0.25, 0.5, 0.75, 1, 2, 3, 4, 5];
/** The sizes of an axis with `count` tracks, with only what differs from the default kept (undefined = all default). */
function trimmed(sizes, count, fallback) {
    const out = sizes.slice(0, count);
    while (out.length < count)
        out.push(fallback);
    return out.every(s => s === fallback) ? undefined : out;
}
/** The measured track sizes (px) of a grid on the canvas. */
function measured(id, axis) {
    const { canvas } = useEditor();
    const el = canvas.frame?.layoutTargetOf(id);
    if (!el)
        return [];
    const cs = el.ownerDocument.defaultView.getComputedStyle(el);
    return parseTracks(axis === 'cols' ? cs.gridTemplateColumns : cs.gridTemplateRows).map(Math.round);
}
export function GridBox({ node, l, set, disabled }) {
    const { session } = useEditor();
    const [open, setOpen] = useState(false);
    const [hover, setHover] = useState(null);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    const cols = l.columns ?? 12;
    const rows = l.rows;
    const shownRows = Math.min(rows ?? 2, 6);
    const rowCount = measured(node.id, 'rows').length || 1;
    const pick = (c, r) => {
        set({ columns: c, rows: r, colSizes: trimmed(l.colSizes ?? [], c, '1fr'), rowSizes: trimmed(l.rowSizes ?? [], r, 'auto') });
        setOpen(false);
    };
    const rowsMenu = [
        { label: String(rows ?? rowCount), checked: rows !== undefined, run: () => set({ rows: rows ?? rowCount }) },
        { label: 'Auto', checked: rows === undefined, run: () => set({ rows: undefined, rowSizes: undefined }) },
    ];
    return (jsx("div", { class: "pe-gridbox-wrap", ref: ref },
        jsx("button", { type: "button", class: cls('pe-gridbox', open && 'is-open'), disabled: disabled, "aria-haspopup": "dialog", "aria-expanded": open, title: "Columns and rows", onClick: () => setOpen(!open) },
            jsx("span", { class: "pe-gridbox-sketch", style: { gridTemplateColumns: `repeat(${Math.min(cols, 12)}, 1fr)`, gridTemplateRows: `repeat(${shownRows}, 1fr)` } }, Array.from({ length: Math.min(cols, 12) * shownRows }, (_, i) => jsx("span", { key: i }))),
            jsx("span", { class: "pe-gridbox-label" },
                cols,
                " \u00D7 ",
                rows ?? 'Auto')),
        open && (jsx("div", { class: "pe-popover pe-gridpick", role: "dialog", "aria-label": "Columns and rows" },
            jsx("div", { class: "pe-gridpick-counts" },
                jsx(NumberField, { icon: "gapH", title: "Number of columns", value: cols, min: 1, max: 24, decimals: 0, disabled: disabled, onChange: n => n && set({ columns: n, colSizes: trimmed(l.colSizes ?? [], n, '1fr') }) }),
                jsx("span", { class: "pe-gridpick-x" }, "\u00D7"),
                jsx(NumberField, { icon: "gapV", title: "Number of rows", value: rows, text: rows === undefined ? 'Auto' : undefined, min: 1, max: 100, decimals: 0, disabled: disabled, menu: rowsMenu, words: { auto: () => set({ rows: undefined, rowSizes: undefined }) }, onChange: n => set(n === undefined ? { rows: undefined, rowSizes: undefined } : { rows: n, rowSizes: trimmed(l.rowSizes ?? [], n, 'auto') }) })),
            jsx("div", { class: "pe-gridpick-cells", role: "radiogroup", "aria-label": "Columns \u00D7 rows", onPointerLeave: () => setHover(null) },
                Array.from({ length: PICK_ROWS }, (_, r) => Array.from({ length: PICK_COLS }, (_, c) => {
                    const on = c < cols && r < (rows ?? 1);
                    const lit = hover && c < hover[0] && r < hover[1];
                    return (jsx("button", { key: r + ':' + c, type: "button", role: "radio", "aria-checked": c + 1 === cols && r + 1 === rows, "aria-label": `${c + 1} × ${r + 1}`, class: cls('pe-gridpick-cell', on && 'is-on', lit && 'is-lit'), disabled: disabled, onPointerEnter: () => setHover([c + 1, r + 1]), onClick: () => pick(c + 1, r + 1) }));
                })),
                hover && jsx("span", { class: "pe-gridpick-tip", style: { left: hover[0] * 15 - 7 + 'px', top: hover[1] * 15 + 6 + 'px' } },
                    hover[0],
                    " \u00D7 ",
                    hover[1])),
            jsx("button", { type: "button", class: "pe-btn pe-gridpick-more", onClick: () => {
                    setOpen(false);
                    session.gridSettings.set(node.id);
                } }, "Open grid settings")))));
}
/** Fixed / Hug / Fill for one track, with Figma's icons and labels. */
function SizingButton({ size, axis, fixedPx, onChange, disabled }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    const kind = size === 'auto' ? 'hug' : size.endsWith('fr') ? 'fill' : 'fixed';
    const w = axis === 'cols';
    const items = [
        { label: `Fixed ${w ? 'width' : 'height'} (${kind === 'fixed' ? parseFloat(size) : fixedPx})`, icon: w ? 'fixedW' : 'fixedH', checked: kind === 'fixed', run: () => onChange(kind === 'fixed' ? size : Math.max(0, fixedPx) + 'px') },
        { label: 'Hug contents', icon: w ? 'hugW' : 'hugH', checked: kind === 'hug', run: () => onChange('auto') },
        { label: `Fill container (${kind === 'fill' ? size : '1fr'})`, icon: w ? 'fillW' : 'fillH', checked: kind === 'fill', run: () => onChange(kind === 'fill' ? size : '1fr') },
    ];
    return (jsx("div", { class: "pe-select-wrap", ref: ref },
        jsx("button", { type: "button", class: "pe-select", disabled: disabled, "aria-haspopup": "menu", "aria-expanded": open, title: (w ? 'Column' : 'Row') + ' sizing', onClick: () => setOpen(!open) },
            jsx("span", { class: "pe-select-text" }, kind === 'hug' ? 'Hug' : kind === 'fill' ? 'Fill' : 'Fixed')),
        open && jsx(DropMenu, { items: items, onClose: () => setOpen(false) })));
}
function TrackList({ node, axis, l, set, disabled }) {
    const cols = axis === 'cols';
    const fallback = cols ? '1fr' : 'auto';
    const sizesKey = cols ? 'colSizes' : 'rowSizes';
    const px = measured(node.id, axis);
    const auto = !cols && l.rows === undefined;
    const count = cols ? l.columns ?? 12 : l.rows ?? Math.max(1, px.length);
    const sizes = Array.from({ length: count }, (_, i) => (cols ? l.colSizes : l.rowSizes)?.[i] ?? fallback);
    const write = (next, extra = {}) => set({ [sizesKey]: trimmed(next, next.length, fallback), ...extra });
    const setSize = (i, size, m) => {
        const next = [...sizes];
        next[i] = size;
        set({ [sizesKey]: trimmed(next, count, fallback), ...(auto ? { rows: count } : {}) }, m);
    };
    const add = () => write([...sizes, fallback], cols ? { columns: count + 1 } : { rows: count + 1 });
    const remove = (i) => {
        const next = sizes.filter((_, k) => k !== i);
        write(next, cols ? { columns: next.length } : { rows: next.length });
    };
    return (jsx("section", { class: "pe-section" },
        jsx("header", { class: "pe-section-head" },
            jsx("h2", null, cols ? 'Columns' : 'Rows'),
            jsx("div", { class: "pe-section-actions" },
                jsx(IconButton, { icon: "plus", title: cols ? 'Add column' : 'Add row', disabled: disabled || (cols ? count >= 24 : count >= 100), onClick: add }))),
        jsx("div", { class: "pe-section-body pe-tracks" },
            auto && jsx("div", { class: "pe-hint pe-row-hint" }, "Rows are added as needed (Auto). Change one to fix the number of rows."),
            sizes.map((size, i) => {
                const kind = size === 'auto' ? 'hug' : size.endsWith('fr') ? 'fill' : 'fixed';
                const fill = FR_STEPS.map(f => ({ label: f + 'fr', checked: size === f + 'fr', run: () => setSize(i, f + 'fr') }));
                return (jsx("div", { key: i, class: "pe-track" },
                    jsx("span", { class: "pe-track-index" }, i + 1),
                    jsx(SizingButton, { size: size, axis: axis, fixedPx: px[i] ?? 100, disabled: disabled, onChange: s => setSize(i, s) }),
                    kind === 'fill' && (jsx(NumberField, { title: `${cols ? 'Column' : 'Row'} ${i + 1} share of the free space`, value: parseFloat(size), unit: "fr", min: 0.01, max: 100, decimals: 2, step: 0.25, menu: fill, disabled: disabled, onChange: (n, m) => n && setSize(i, n + 'fr', m) })),
                    kind === 'fixed' && (jsx(NumberField, { title: `${cols ? 'Column' : 'Row'} ${i + 1} ${cols ? 'width' : 'height'}`, value: parseFloat(size), min: 0, max: 4000, decimals: 0, disabled: disabled, onChange: (n, m) => n !== undefined && setSize(i, Math.round(n) + 'px', m) })),
                    kind === 'hug' && jsx(NumberField, { title: "Size of the content", value: px[i], disabled: true, onChange: () => undefined }),
                    jsx(IconButton, { icon: "minus", title: cols ? 'Remove column' : 'Remove row', disabled: disabled || count <= 1, onClick: () => remove(i) })));
            }))));
}
/** Figma's grid settings: they take the place of the design panel until closed. */
export function GridSettings({ node, bp }) {
    const { session } = useEditor();
    useSignal(session.docRevision);
    const l = layoutAt(node, bp);
    const set = (patch, merge) => updateLayout(session.store, node.id, bp, patch, merge);
    const disabled = !!node.locked;
    return (jsx("div", { class: "pe-design pe-grid-settings" },
        jsx("header", { class: "pe-section-head pe-grid-settings-head" },
            jsx("h2", null, "Grid"),
            jsx("div", { class: "pe-section-actions" },
                jsx(IconButton, { icon: "close", title: "Close grid settings", onClick: () => session.gridSettings.set(null) }))),
        jsx(TrackList, { node: node, axis: "cols", l: l, set: set, disabled: disabled }),
        jsx(TrackList, { node: node, axis: "rows", l: l, set: set, disabled: disabled })));
}
