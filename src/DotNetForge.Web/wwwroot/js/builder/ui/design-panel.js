/**
 * The Design tab, laid out like Figma's design panel: Position, Auto layout
 * (or Layout), Appearance, Typography, Fill, Stroke and Effects, each with
 * labels above two field columns and an icon column on the right.
 *
 * As in Figma, what a section shows depends on the selection: a container's
 * section is "Auto layout" for stacks and grids and "Layout" for free
 * positioning; size fields show Fixed numbers or Hug/Fill/Auto; the
 * alignment box and gap become column/row settings in a grid; position
 * fields are cells in a grid parent and pixels in a free one.
 */
import { jsx, Fragment, useEffect, useRef, useState } from "./vdom.js";
import { componentDef, BREAKPOINT_LABELS } from "../core/schema.js";
import { freePlacementAt, gridPlacementAt, layoutAt, merged } from "../core/responsive.js";
import { gridItemsAt, moveGridItem, resetBreakpoint, setFlag, setPlacement, updateLayout, updateStyle } from "../core/commands.js";
import { Icon } from "./icons.js";
import { ColorField, DropMenu, IconButton, ImageInput, NumberField, PopoverButton, Row, Section, SelectField, Toggle, useDismiss } from "./fields.js";
import { cls, useEditor, useSignal } from "./hooks.js";
import { GridBox, GridSettings } from "./grid-panel.js";
import { align as alignLayers, flip as flipLayers, guessFlow, resizeToFit, setFlow } from "../editor/layout-ops.js";
/** Layers whose Fill is their text color, as for Figma's text layers. */
const TEXT_FILL = new Set(['heading', 'text', 'links', 'icon']);
/** Layers with a Typography section. */
const TEXT_TYPES = new Set(['heading', 'text', 'button', 'links', 'icon', 'post']);
const CONTAINERS_WITH_TEXT = new Set(['section', 'container', 'page']);
/** Explicit value at the breakpoint, and the value it would have without it. */
function styleValue(node, bp, key) {
    const value = node.style?.[bp]?.[key];
    const prev = bp === 'mobile' ? 'tablet' : bp === 'tablet' ? 'base' : null;
    const inherited = prev ? merged(node.style, prev)[key] : undefined;
    return { value, inherited };
}
/** The value that applies at the breakpoint (explicit or inherited). */
function effective(node, bp, key) {
    const v = styleValue(node, bp, key);
    return v.value ?? v.inherited;
}
function useStyleSetter(ids, bp) {
    const { session } = useEditor();
    return (patch, merge) => updateStyle(session.store, ids, bp, patch, merge);
}
export function DesignTab({ ids, node }) {
    const { session } = useEditor();
    const bp = useSignal(session.breakpoint);
    useSignal(session.docRevision);
    const doc = session.store.doc;
    const parent = node.parent ? doc.nodes[node.parent] : null;
    const hasOverrides = bp !== 'base' && ids.some(id => {
        const n = doc.nodes[id];
        return !!(n?.style?.[bp] || n?.layout?.[bp] || n?.place?.[bp]);
    });
    const isText = TEXT_TYPES.has(node.type);
    const gridSettings = useSignal(session.gridSettings);
    // Grid settings belong to the grid they were opened for; selecting something else closes them.
    useEffect(() => {
        if (gridSettings && gridSettings !== node.id)
            session.gridSettings.set(null);
    }, [node.id]);
    if (gridSettings === node.id && node.children && layoutAt(node, bp).mode === 'grid')
        return jsx(GridSettings, { node: node, bp: bp });
    return (jsx("div", { class: "pe-design" },
        jsx(BreakpointBanner, { bp: bp }),
        hasOverrides && (jsx("div", { class: "pe-reset-bp" },
            jsx("button", { type: "button", class: "pe-btn-text", onClick: () => resetBreakpoint(session.store, ids, bp, ['style', 'layout', 'place']) },
                jsx(Icon, { name: "restore", size: 14 }),
                " Reset all ",
                BREAKPOINT_LABELS[bp].toLowerCase(),
                " changes"))),
        node.locked && jsx("div", { class: "pe-note" },
            jsx(Icon, { name: "lock", size: 14 }),
            " Locked. Unlock it in the layers panel to change it."),
        parent && jsx(PositionSection, { node: node, parent: parent, bp: bp, ids: ids }),
        jsx(LayoutSection, { node: node, parent: parent, bp: bp, ids: ids }),
        jsx(AppearanceSection, { node: node, ids: ids, bp: bp }),
        (isText || CONTAINERS_WITH_TEXT.has(node.type)) && jsx(TypographySection, { node: node, ids: ids, bp: bp }),
        jsx(FillSection, { node: node, ids: ids, bp: bp }),
        jsx(StrokeSection, { node: node, ids: ids, bp: bp }),
        jsx(EffectsSection, { node: node, ids: ids, bp: bp })));
}
function BreakpointBanner({ bp }) {
    const { session } = useEditor();
    if (bp === 'base')
        return null;
    const bps = session.store.doc.settings.breakpoints;
    return (jsx("div", { class: cls('pe-bp-banner', 'is-' + bp) },
        jsx(Icon, { name: bp === 'tablet' ? 'tablet' : 'mobile', size: 16 }),
        jsx("span", null,
            "Editing ",
            jsx("strong", null, BREAKPOINT_LABELS[bp]),
            ": changes apply below ",
            bp === 'tablet' ? bps.tablet : bps.mobile,
            "px.")));
}
/** Three icon buttons in one segmented box; clicking the active one clears it. */
function IconGroup({ value, options, onChange, disabled, title }) {
    return (jsx("div", { class: cls('pe-seg', disabled && 'is-disabled'), role: "group", title: title }, options.map(o => (jsx("button", { key: o.value, type: "button", class: cls('pe-seg-btn', value === o.value && 'is-on'), title: o.title, "aria-label": o.title, "aria-pressed": value === o.value, disabled: disabled, onClick: () => onChange(value === o.value ? undefined : o.value) },
        jsx(Icon, { name: o.icon, size: 16 }))))));
}
/** Spans both field columns of a row (Figma's full-width fields). */
const Span2 = ({ children }) => jsx("div", { class: "pe-span2" }, children);
/* ------------------------------------------------------------------ position */
const H_ALIGN = [
    { value: 'start', icon: 'alignLeft', title: 'Align left' },
    { value: 'center', icon: 'alignCenterH', title: 'Align horizontal centers' },
    { value: 'end', icon: 'alignRight', title: 'Align right' },
];
const V_ALIGN = [
    { value: 'start', icon: 'alignTop', title: 'Align top' },
    { value: 'center', icon: 'alignCenterV', title: 'Align vertical centers' },
    { value: 'end', icon: 'alignBottom', title: 'Align bottom' },
];
function PositionSection({ node, parent, bp, ids }) {
    const { session, canvas } = useEditor();
    const store = session.store;
    const pl = layoutAt(parent, bp);
    const setStyle = useStyleSetter(ids, bp);
    const disabled = !!node.locked;
    const rotation = styleValue(node, bp, 'rotation');
    const flip = styleValue(node, bp, 'flip');
    const angle = rotation.value ?? rotation.inherited ?? 0;
    const rotate90 = () => setStyle({ rotation: ((Math.round(angle / 90) * 90 + 90 + 180) % 360) - 180 });
    const mirror = (horizontal) => flipLayers(session, ids, horizontal);
    const alignSelf = effective(node, bp, 'alignSelf');
    const justifySelf = effective(node, bp, 'justifySelf');
    // Alignment is for first-level layers only (directly on the page); deeper layers are placed
    // by their frame's layout, so the buttons are off for them.
    const topLevel = parent.id === store.doc.root;
    const none = { value: undefined, set: () => undefined, disabled: true };
    let h;
    let v;
    if (!topLevel) {
        h = none;
        v = none;
    }
    else if (pl.mode === 'stack') {
        // The page stacks its layers: only the cross axis can be chosen per layer. A layer that fills the cross
        // axis stretches across it (the renderer ignores alignSelf then), so there is nothing to align - not in
        // the reference, where the alignment silently turned "Fill" into a content-sized layer.
        const fills = effective(node, bp, pl.direction === 'row' ? 'height' : 'width')?.mode === 'fill';
        const cross = { value: fills ? undefined : alignSelf, set: (x) => setStyle({ alignSelf: x }), disabled: disabled || fills, title: fills ? `It fills the page ${pl.direction === 'row' ? 'height' : 'width'}. Choose Fixed or Hug ${pl.direction === 'row' ? 'height' : 'width'} to align it.` : undefined };
        h = pl.direction === 'row' ? none : cross;
        v = pl.direction === 'row' ? cross : none;
    }
    else if (pl.mode === 'grid') {
        h = { value: justifySelf, set: x => setStyle({ justifySelf: x }), disabled };
        v = { value: alignSelf, set: x => setStyle({ alignSelf: x }), disabled };
    }
    else {
        // Free positioning: move the element to the parent's edge or center.
        const f = freePlacementAt(node, bp);
        const box = canvas.frame?.layoutTargetOf(parent.id)?.getBoundingClientRect();
        const pw = box ? Math.round(box.width) : 0;
        const ph = pl.height ?? (box ? Math.round(box.height) : 0);
        const at = (pos, size, total) => (pos <= 0 ? 'start' : Math.abs(pos - (total - size) / 2) <= 1 ? 'center' : pos >= total - size ? 'end' : undefined);
        h = { value: pw ? at(f.x, f.w, pw) : undefined, set: x => x && pw && setPlacement(store, node.id, bp, { x: x === 'start' ? 0 : x === 'end' ? pw - f.w : Math.round((pw - f.w) / 2) }), disabled: disabled || !pw };
        v = { value: ph ? at(f.y, f.h, ph) : undefined, set: x => x && ph && setPlacement(store, node.id, bp, { y: x === 'start' ? 0 : x === 'end' ? ph - f.h : Math.round((ph - f.h) / 2) }), disabled: disabled || !ph };
    }
    const alignOverride = bp !== 'base' && (styleValue(node, bp, 'alignSelf').value !== undefined || styleValue(node, bp, 'justifySelf').value !== undefined);
    if (ids.length > 1 && ids.every(id => store.doc.nodes[id]?.parent === store.doc.root)) {
        // Several first-level layers: the buttons align them, like Figma.
        const edge = (x, horizontal) => (x === 'start' ? (horizontal ? 'left' : 'top') : x === 'end' ? (horizontal ? 'right' : 'bottom') : horizontal ? 'hcenter' : 'vcenter');
        h = { value: undefined, set: x => alignLayers(session, canvas, ids, edge(x ?? 'start', true)), disabled };
        v = { value: undefined, set: x => alignLayers(session, canvas, ids, edge(x ?? 'start', false)), disabled };
    }
    return (jsx(Section, { id: "position", title: "Position" },
        jsx(Row, { label: "Alignment", override: alignOverride, onReset: () => setStyle({ alignSelf: undefined, justifySelf: undefined }) },
            jsx(IconGroup, { value: h.value, options: H_ALIGN, disabled: h.disabled, title: h.title, onChange: h.set }),
            jsx(IconGroup, { value: v.value, options: V_ALIGN, disabled: v.disabled, title: v.title, onChange: v.set })),
        pl.mode === 'free' && jsx(FreePosition, { node: node, bp: bp, disabled: disabled }),
        (pl.mode === 'stack' || pl.mode === 'grid') && jsx(MeasuredPosition, { node: node, parent: parent }),
        pl.mode === 'grid' && jsx(GridPosition, { node: node, parent: parent, bp: bp, disabled: disabled }),
        jsx(Row, { label: "Rotation", override: bp !== 'base' && (rotation.value !== undefined || flip.value !== undefined), onReset: () => setStyle({ rotation: undefined, flip: undefined }) },
            jsx(NumberField, { icon: "rotate", title: "Rotation", value: rotation.value, placeholder: rotation.inherited ?? 0, unit: "\u00B0", min: -360, max: 360, disabled: disabled, onChange: (n, m) => setStyle({ rotation: n }, m) }),
            jsx("div", { class: cls('pe-seg', disabled && 'is-disabled'), role: "group", "aria-label": "Rotate and flip" }, [['rotate90', 'Rotate 90°', rotate90], ['flipH', 'Flip horizontal (Shift+H)', () => mirror(true)], ['flipV', 'Flip vertical (Shift+V)', () => mirror(false)]].map(([icon, label, run]) => (jsx("button", { key: icon, type: "button", class: "pe-seg-btn", title: label, "aria-label": label, disabled: disabled, onClick: run },
                jsx(Icon, { name: icon, size: 16 }))))))));
}
/** X/Y of a stack or grid child: shown greyed out, as Figma does for children of auto layout. */
function MeasuredPosition({ node, parent }) {
    const { canvas } = useEditor();
    const r = canvas.rectOfNode(node.id);
    const box = canvas.frame?.layoutTargetOf(parent.id)?.getBoundingClientRect();
    const x = r && box ? Math.round(r.x - box.left) : undefined;
    const y = r && box ? Math.round(r.y - box.top) : undefined;
    return (jsx(Row, { label: "Position" },
        jsx(NumberField, { label: "X", title: "X position (set by the stack)", value: x, disabled: true, onChange: () => undefined }),
        jsx(NumberField, { label: "Y", title: "Y position (set by the stack)", value: y, disabled: true, onChange: () => undefined })));
}
function FreePosition({ node, bp, disabled }) {
    const { session } = useEditor();
    const f = freePlacementAt(node, bp);
    const set = (patch, merge) => setPlacement(session.store, node.id, bp, patch, merge);
    return (jsx(Row, { label: "Position", override: bp !== 'base' && !!node.place?.[bp], onReset: () => resetBreakpoint(session.store, [node.id], bp, ['place']) },
        jsx(NumberField, { label: "X", title: "X position", value: f.x, min: -2000, max: 10000, decimals: 0, disabled: disabled, onChange: (n, m) => n !== undefined && set({ x: n }, m) }),
        jsx(NumberField, { label: "Y", title: "Y position", value: f.y, min: -2000, max: 20000, decimals: 0, disabled: disabled, onChange: (n, m) => n !== undefined && set({ y: n }, m) })));
}
function GridPosition({ node, parent, bp, disabled }) {
    const { session } = useEditor();
    const store = session.store;
    const g = gridPlacementAt(node, parent, bp);
    const cols = layoutAt(parent, bp).columns ?? 12;
    // Automatic placement shows where the browser put it.
    const actual = g.auto ? gridItemsAt(store.doc, parent.id, bp).find(i => i.id === node.id) : null;
    const cur = actual ?? { col: g.col, row: g.row, colSpan: g.colSpan, rowSpan: g.rowSpan };
    const set = (patch) => moveGridItem(store, node.id, bp, { col: cur.col, row: cur.row, colSpan: cur.colSpan, rowSpan: cur.rowSpan, ...patch });
    const override = bp !== 'base' && !!node.place?.[bp];
    return (jsx(Fragment, null,
        jsx(Row, { label: "Column span", label2: "Row span", override: override, onReset: () => resetBreakpoint(store, [node.id], bp, ['place']) },
            jsx(NumberField, { icon: "gapH", title: "Column span", value: cur.colSpan, min: 1, max: cols, decimals: 0, disabled: disabled, onChange: n => n && set({ colSpan: n }) }),
            jsx(NumberField, { icon: "gapV", title: "Row span", value: cur.rowSpan, min: 1, max: 200, decimals: 0, disabled: disabled, onChange: n => n && set({ rowSpan: n }) })),
        jsx(Row, { label: "Column", label2: "Row" },
            jsx(NumberField, { label: "C", title: "Column", value: cur.col, min: 1, max: cols, decimals: 0, disabled: disabled, onChange: n => n && set({ col: n }) }),
            jsx(NumberField, { label: "R", title: "Row", value: cur.row, min: 1, max: 1000, decimals: 0, disabled: disabled, onChange: n => n && set({ row: n }) })),
        g.auto && jsx("div", { class: "pe-hint pe-row-hint" }, "Placed automatically. Change a value or drag it to place it exactly.")));
}
/* ------------------------------------------------------------------ layout */
/** The auto layout section's header button: the frame's flow, shown on (Figma). */
const AUTO_ICON = { 'stack-col': 'alOnV', 'stack-row': 'alOnH', 'stack-row-wrap': 'alOnW', grid: 'grid' };
function LayoutSection({ node, parent, bp, ids }) {
    const { session, canvas } = useEditor();
    const store = session.store;
    const isContainer = !!componentDef(node.type)?.container;
    const l = layoutAt(node, bp);
    const explicit = node.layout?.[bp] ?? {};
    const set = (patch, merge) => updateLayout(store, node.id, bp, patch, merge);
    const ov = (...k) => bp !== 'base' && k.some(x => explicit[x] !== undefined);
    const disabled = !!node.locked;
    const setStyle = useStyleSetter(ids, bp);
    const switchFlow = (flow) => setFlow(session, canvas, node.id, flow);
    const flow = l.mode === 'stack' ? (l.direction === 'row' ? 'stack-row' : 'stack-col') : (l.mode ?? 'free');
    const auto = isContainer && l.mode !== 'free';
    const title = auto ? 'Auto layout' : 'Layout';
    const guessed = isContainer && !auto ? guessFlow(session, node.id) : 'stack-col';
    const showsClip = isContainer || node.type === 'image' || node.type === 'video' || node.type === 'module';
    const overflow = effective(node, bp, 'overflow');
    return (jsx(Section, { id: "layout", title: title, actions: isContainer && (jsx(Fragment, null,
            !auto && jsx(IconButton, { icon: "resizeToFit", title: "Resize to fit", disabled: disabled, onClick: () => resizeToFit(session, node.id) }),
            auto
                ? jsx(IconButton, { icon: AUTO_ICON[flow + (l.wrap ? '-wrap' : '')] ?? AUTO_ICON[flow], title: "Remove auto layout (Alt+Shift+A)", on: true, disabled: disabled, onClick: () => switchFlow('free') })
                : jsx(IconButton, { icon: guessed === 'stack-row' ? 'alAddH' : 'alAddV', title: "Add auto layout (Shift+A)", disabled: disabled, onClick: () => switchFlow(guessed) }))) },
        isContainer && (jsx(Row, { label: "Flow", override: ov('mode', 'direction', 'wrap'), onReset: () => set({ mode: undefined, direction: undefined, wrap: undefined }) },
            jsx("div", { class: "pe-span2 pe-seg pe-flow", role: "radiogroup", "aria-label": "Flow" }, [['free', 'free', 'Freeform'], ['stack-col', 'flowDown', 'Vertical'], ['stack-row', 'flowRight', 'Horizontal'], ['grid', 'flowGrid', 'Grid']].map(([value, icon, label]) => (jsx("button", { key: value, type: "button", role: "radio", "aria-checked": flow === value, class: cls('pe-seg-btn', flow === value && 'is-on'), title: label, "aria-label": label, disabled: disabled, onClick: () => switchFlow(value) },
                jsx(Icon, { name: icon, size: 16 }))))),
            jsx(IconButton, { icon: "wrapText", title: "Wrap", on: l.mode === 'stack' && !!l.wrap, disabled: disabled || l.mode !== 'stack', onClick: () => set({ wrap: !l.wrap }) }))),
        parent && TEXT_RESIZE.has(node.type) && layoutAt(parent, bp).mode !== 'free' && jsx(TextResizing, { node: node, ids: ids, bp: bp, disabled: disabled }),
        parent && jsx(SizeRows, { node: node, parent: parent, bp: bp, ids: ids, disabled: disabled }),
        isContainer && l.mode === 'stack' && jsx(StackRows, { l: l, explicit: explicit, set: set, ov: ov, disabled: disabled }),
        isContainer && l.mode === 'grid' && jsx(GridRows, { node: node, l: l, explicit: explicit, set: set, ov: ov, disabled: disabled }),
        isContainer && l.mode === 'free' && (jsx(Row, { label: "Height", override: ov('height'), onReset: () => set({ height: undefined }) },
            jsx(NumberField, { icon: "padV", title: "Height of the free area", value: explicit.height, placeholder: l.height, min: 0, max: 10000, decimals: 0, disabled: disabled, onChange: (n, m) => set({ height: n }, m) }))),
        jsx(SpacingRows, { node: node, bp: bp, setStyle: setStyle, disabled: disabled, kind: "padding" }),
        showsClip && (jsx("div", { class: "pe-row pe-check-row" },
            jsx(Toggle, { checked: overflow === 'hidden' || overflow === 'clip', label: "Clip content", disabled: disabled, onChange: on => setStyle({ overflow: on ? 'hidden' : undefined }) }),
            overflow === 'auto' && jsx("span", { class: "pe-hint" }, "Scrolls")))));
}
const TEXT_RESIZE = new Set(['heading', 'text', 'button', 'links']);
/** Figma's text resizing: Auto width, Auto height (wraps, fills the width) or Fixed size. */
function TextResizing({ node, ids, bp, disabled }) {
    const { canvas } = useEditor();
    const setStyle = useStyleSetter(ids, bp);
    const w = effective(node, bp, 'width');
    const hgt = effective(node, bp, 'height');
    const mode = w?.mode === 'hug' ? 'width' : w?.mode === 'fixed' && hgt?.mode === 'fixed' ? 'fixed' : 'height';
    const choose = (m) => {
        if (m === 'width')
            setStyle({ width: { mode: 'hug' }, height: undefined });
        else if (m === 'height')
            setStyle({ width: { mode: 'fill' }, height: undefined });
        else {
            const r = canvas.rectOfNode(node.id);
            setStyle({ width: { mode: 'fixed', value: Math.round(r?.w ?? 240) }, height: { mode: 'fixed', value: Math.round(r?.h ?? 40) } });
        }
    };
    const options = [['width', 'textResizeW', 'Auto width'], ['height', 'textResizeH', 'Auto height'], ['fixed', 'textResizeFixed', 'Fixed size']];
    return (jsx(Row, { label: "Resizing" },
        jsx("div", { class: "pe-span2 pe-seg", role: "radiogroup", "aria-label": "Text resizing" }, options.map(([value, icon, label]) => (jsx("button", { key: value, type: "button", role: "radio", "aria-checked": mode === value, class: cls('pe-seg-btn', mode === value && 'is-on'), title: label, "aria-label": label, disabled: disabled, onClick: () => choose(value) },
            jsx(Icon, { name: icon, size: 16 })))))));
}
function SizeRows({ node, parent, bp, ids, disabled }) {
    const { session, canvas } = useEditor();
    const pl = layoutAt(parent, bp);
    const setStyle = useStyleSetter(ids, bp);
    const [limits, setLimits] = useState({ width: false, height: false });
    if (pl.mode === 'free') {
        // In a free container the size is part of the placement.
        const f = freePlacementAt(node, bp);
        const set = (patch, merge) => setPlacement(session.store, node.id, bp, patch, merge);
        return (jsx(Row, { label: "Dimensions" },
            jsx(NumberField, { label: "W", title: "Width", value: f.w, min: 1, max: 10000, decimals: 0, disabled: disabled, onChange: (n, m) => n && set({ w: n }, m) }),
            jsx(NumberField, { label: "H", title: "Height", value: f.h, min: 1, max: 20000, decimals: 0, disabled: disabled, onChange: (n, m) => n && set({ h: n }, m) })));
    }
    const rect = canvas.rectOfNode(node.id);
    const sizes = ['width', 'height'].map(axis => ({ axis, ...styleValue(node, bp, axis) }));
    const modeOf = (s) => (s.value ?? s.inherited ?? { mode: 'auto' });
    const allFixed = sizes.every(s => modeOf(s).mode === 'fixed');
    const ratio = styleValue(node, bp, 'aspectRatio');
    const ratioOn = (ratio.value ?? ratio.inherited) !== undefined;
    // Figma's size menu: Fixed always; Hug only for auto layout frames; Fill only inside auto layout.
    const nodeMode = node.children ? layoutAt(node, bp).mode : null;
    const canHug = nodeMode === 'stack' || nodeMode === 'grid';
    const canFill = pl.mode === 'stack' || pl.mode === 'grid';
    const isText = TEXT_RESIZE.has(node.type);
    /** An unset size behaves as Figma's Fill (stretched on the cross axis, a grid cell) or Hug (content-sized). */
    const effectiveMode = (axis, mode) => {
        if (mode !== 'auto')
            return mode;
        if (pl.mode === 'grid')
            return 'fill';
        if (pl.mode !== 'stack')
            return mode;
        const mainAxis = pl.direction === 'row' ? 'width' : 'height';
        if (axis === mainAxis)
            return 'hug';
        return (pl.align ?? 'stretch') === 'stretch' && effective(node, bp, 'alignSelf') === undefined ? 'fill' : 'hug';
    };
    const field = (s) => {
        const raw = modeOf(s);
        const cur = { ...raw, mode: effectiveMode(s.axis, raw.mode) };
        const actual = rect ? Math.round(s.axis === 'width' ? rect.w : rect.h) : undefined;
        const word = s.axis === 'width' ? 'width' : 'height';
        const w = s.axis === 'width';
        const menu = [
            { label: `Fixed ${word}` + (actual !== undefined ? ` (${cur.mode === 'fixed' ? cur.value : actual})` : ''), icon: w ? 'fixedW' : 'fixedH', checked: cur.mode === 'fixed', run: () => setStyle({ [s.axis]: { mode: 'fixed', value: cur.mode === 'fixed' ? cur.value : actual ?? (w ? 240 : 120) } }) },
        ];
        if (canHug)
            menu.push({ label: 'Hug contents', icon: w ? 'hugW' : 'hugH', checked: cur.mode === 'hug', run: () => setStyle({ [s.axis]: { mode: 'hug' } }) });
        if (canFill)
            // Fill on the cross axis clears an alignment, which would otherwise be kept but unused.
            menu.push({ label: 'Fill container', icon: w ? 'fillW' : 'fillH', checked: cur.mode === 'fill', run: () => setStyle(pl.mode === 'stack' && s.axis === (pl.direction === 'row' ? 'height' : 'width') ? { [s.axis]: { mode: 'fill' }, alignSelf: undefined } : { [s.axis]: { mode: 'fill' } }) });
        menu.push('sep', { label: `Add min ${word}…`, icon: w ? 'minW' : 'minH', run: () => setLimits(x => ({ ...x, [s.axis]: true })) }, { label: `Add max ${word}…`, icon: w ? 'maxW' : 'maxH', run: () => setLimits(x => ({ ...x, [s.axis]: true })) });
        if (s.value !== undefined && bp !== 'base')
            menu.push('sep', { label: 'Use the larger screen\'s size', icon: 'restore', run: () => setStyle({ [s.axis]: undefined }) });
        // Always the pixel size; the mode after it, as Figma's "6360 Hug".
        const suffix = cur.mode === 'hug' ? 'Hug' : cur.mode === 'fill' ? 'Fill' : cur.mode === 'percent' ? (cur.value ?? 100) + '%' : cur.mode === 'auto' ? 'Auto' : undefined;
        return (jsx(NumberField, { key: s.axis, label: w ? 'W' : 'H', title: w ? 'Width' : 'Height', menu: isText ? undefined : menu, disabled: disabled, value: cur.mode === 'fixed' ? cur.value : actual, suffix: suffix, min: 0, max: 10000, decimals: 0, onChange: (n, m) => n !== undefined && setStyle({ [s.axis]: { mode: 'fixed', value: n } }, m) }));
    };
    const limitRow = (axis) => {
        const minKey = axis === 'width' ? 'minWidth' : 'minHeight';
        const maxKey = axis === 'width' ? 'maxWidth' : 'maxHeight';
        const min = styleValue(node, bp, minKey);
        const max = styleValue(node, bp, maxKey);
        if (!limits[axis] && min.value === undefined && max.value === undefined && min.inherited === undefined && max.inherited === undefined)
            return null;
        return (jsx(Row, { key: axis, label: axis === 'width' ? 'Min width' : 'Min height', label2: axis === 'width' ? 'Max width' : 'Max height' },
            jsx(NumberField, { label: "Min", value: min.value, placeholder: min.inherited ?? '-', min: 0, max: 4000, decimals: 0, disabled: disabled, onChange: (n, m) => setStyle({ [minKey]: n }, m) }),
            jsx(NumberField, { label: "Max", value: max.value, placeholder: max.inherited ?? '-', min: 0, max: 4000, decimals: 0, disabled: disabled, onChange: (n, m) => setStyle({ [maxKey]: n }, m) }),
            jsx(IconButton, { icon: "minus", title: `Remove ${axis} limits`, disabled: disabled, onClick: () => {
                    setLimits(x => ({ ...x, [axis]: false }));
                    setStyle({ [minKey]: undefined, [maxKey]: undefined });
                } })));
    };
    return (jsx(Fragment, null,
        jsx(Row, { label: allFixed ? 'Dimensions' : 'Resizing', override: bp !== 'base' && sizes.some(s => s.value !== undefined), onReset: () => setStyle({ width: undefined, height: undefined }) },
            sizes.map(field),
            jsx(IconButton, { icon: "lockRatio", title: ratioOn ? 'Unlock aspect ratio' : 'Lock aspect ratio', on: ratioOn, disabled: disabled || (!ratioOn && !rect), onClick: () => {
                    if (ratioOn)
                        setStyle({ aspectRatio: undefined });
                    else if (rect && rect.h > 0)
                        setStyle({ aspectRatio: Math.min(10, Math.max(0.1, Math.round((rect.w / rect.h) * 100) / 100)) });
                } })),
        limitRow('width'),
        limitRow('height')));
}
const DISTRIBUTED = new Set(['between', 'around', 'evenly']);
function StackRows({ l, explicit, set, ov, disabled }) {
    const justify = l.justify ?? 'start';
    const align = l.align ?? 'stretch';
    const autoGap = DISTRIBUTED.has(justify);
    const gapMenu = [
        { label: String(l.gap ?? 0), checked: !autoGap, run: () => set({ justify: 'start' }) },
        { label: 'Auto', checked: autoGap, run: () => set({ justify: 'between' }) },
    ];
    return (jsx(Row, { label: "Alignment", label2: "Gap", override: ov('align', 'justify', 'gap'), onReset: () => set({ align: undefined, justify: undefined, gap: undefined }) },
        jsx(AlignBox, { direction: l.direction ?? 'column', wrap: !!l.wrap, align: align, justify: justify, disabled: disabled, onChange: (a, j) => set({ align: a, justify: j }), onToggleAuto: () => set({ justify: autoGap ? 'start' : 'between' }) }),
        jsx(NumberField, { icon: l.direction === 'row' ? 'gapH' : 'gapV', title: l.direction === 'row' ? 'Horizontal gap between objects' : 'Vertical gap between objects', value: autoGap ? undefined : explicit.gap, text: autoGap ? 'Auto' : undefined, placeholder: l.gap ?? 0, min: 0, max: 400, decimals: 0, menu: gapMenu, disabled: disabled, words: { auto: () => set({ justify: 'between' }) }, onChange: (n, m) => set(autoGap ? { gap: n, justify: 'start' } : { gap: n }, m) }),
        jsx(PopoverButton, { icon: "sliders", title: "Auto layout settings" },
            jsx(Row, { label: "Spacing" },
                jsx(Span2, null,
                    jsx(SelectField, { value: justify, disabled: disabled, options: [{ value: 'start', label: 'Packed' }, { value: 'center', label: 'Packed, centered' }, { value: 'end', label: 'Packed at the end' }, { value: 'between', label: 'Space between' }, { value: 'around', label: 'Space around' }, { value: 'evenly', label: 'Space evenly' }], onChange: x => x && set({ justify: x }) }))),
            jsx("div", { class: "pe-row pe-check-row" },
                jsx(Toggle, { checked: align === 'stretch', label: "Stretch children to fill", disabled: disabled, onChange: on => set({ align: on ? 'stretch' : 'start' }) })),
            l.direction === 'row' && (jsx("div", { class: "pe-row pe-check-row" },
                jsx(Toggle, { checked: align === 'baseline', label: "Align text baseline", disabled: disabled, onChange: on => set({ align: on ? 'baseline' : 'start' }) }))))));
}
const POS = ['start', 'center', 'end'];
const ROW_NAMES = ['top', '', 'bottom'];
const COL_NAMES = ['left', 'center', 'right'];
/** The 16px mark of a packed stack (measured from Figma). */
function markBars(kind, p) {
    const i = POS.indexOf(p);
    if (kind === 'w') {
        const second = [[3, 7], [5, 9], [7, 11]][i];
        return [[3, 3, 2, 4], [7, 3, 2, 4], [11, 3, 2, 4], [second[0], 9, 2, 4], [second[1], 9, 2, 4]];
    }
    const off = [[3, 3, 3], [5, 3, 6], [6, 3, 8]][i];
    const len = [7, 10, 5];
    return kind === 'v'
        ? len.map((l, k) => [off[k], 3 + k * 4, l, 2])
        : len.map((l, k) => [3 + k * 4, off[k], 2, l]);
}
/** The bars of a space-between strip (16px wide/high, spread over the box). */
function spreadBars(vertical, p, span) {
    const off = [[3, 3, 3], [3, 5, 3], [3, 8, 3]][POS.indexOf(p)];
    const len = [10, 5, 10];
    const at = [6, Math.round(span / 2) - 1, span - 8];
    return vertical
        ? len.map((l, k) => [off[k], at[k], l, 2])
        : len.map((l, k) => [at[k], off[k], 2, l]);
}
const Bars = ({ bars }) => (jsx(Fragment, null, bars.map(([x, y, w, h], k) => jsx("span", { key: k, class: "pe-abox-bar", style: { left: x + 'px', top: y + 'px', width: w + 'px', height: h + 'px' } }))));
function AlignBox({ direction, wrap, align, justify, onChange, onToggleAuto, disabled }) {
    const [hover, setHover] = useState(null);
    const row = direction === 'row';
    const kind = row ? (wrap ? 'w' : 'h') : 'v';
    const cross = POS.includes(align) ? align : 'start';
    const main = POS.includes(justify) ? justify : 'start';
    const distributed = DISTRIBUTED.has(justify);
    const dots = [];
    for (let r = 0; r < 3; r++)
        for (let c = 0; c < 3; c++)
            dots.push(jsx("span", { key: r + ':' + c, class: "pe-abox-dot", style: { gridRow: r + 1, gridColumn: c + 1 } }));
    if (distributed) {
        // Three choices on the cross axis: columns for a vertical stack, rows otherwise.
        const vertical = kind === 'v';
        return (jsx("div", { class: cls('pe-abox', 'is-spread', disabled && 'is-disabled'), role: "radiogroup", "aria-label": "Alignment", onDblClick: () => !disabled && onToggleAuto() },
            jsx("div", { class: "pe-abox-grid" }, dots),
            POS.map((p, i) => {
                const on = cross === p;
                const shown = on || hover === p;
                const name = 'Align ' + (vertical ? COL_NAMES[i] : ROW_NAMES[i] || 'center');
                return (jsx("button", { key: p, type: "button", role: "radio", "aria-checked": on, "aria-label": name, title: name, disabled: disabled, class: cls('pe-abox-band', vertical ? 'is-col' : 'is-row', on && 'is-on'), style: vertical ? { left: [0, 35, 69][i] + 'px' } : { top: [0, 15, 31][i] + 'px' }, onPointerEnter: () => setHover(p), onPointerLeave: () => setHover(null), onClick: () => onChange(p, justify) }, shown && jsx("span", { class: "pe-abox-strip" },
                    jsx(Bars, { bars: spreadBars(vertical, p, vertical ? 46 : 104) }))));
            })));
    }
    const cells = [];
    for (let r = 0; r < 3; r++) {
        for (let c = 0; c < 3; c++) {
            // Vertical stack: columns are the cross axis, rows the main axis; horizontal: the reverse.
            const a = row ? POS[r] : POS[c];
            const j = row ? POS[c] : POS[r];
            const on = cross === a && main === j;
            const key = r + ':' + c;
            const shown = on || hover === key;
            const name = 'Align ' + [ROW_NAMES[r], r === 1 && c === 1 ? 'center' : COL_NAMES[c]].filter(Boolean).join(' ');
            cells.push(jsx("button", { key: key, type: "button", role: "radio", "aria-checked": on, "aria-label": name, title: name, disabled: disabled, class: cls('pe-abox-cell', on && 'is-on'), style: { gridRow: r + 1, gridColumn: c + 1 }, onPointerEnter: () => setHover(key), onPointerLeave: () => setHover(null), onClick: () => onChange(a, j) }, shown && jsx("span", { class: "pe-abox-mark" },
                jsx(Bars, { bars: markBars(kind, kind === 'w' ? j : a) }))));
        }
    }
    // Double-click switches between packed and space between, as in Figma.
    return (jsx("div", { class: cls('pe-abox', disabled && 'is-disabled'), role: "radiogroup", "aria-label": "Alignment", onDblClick: () => !disabled && onToggleAuto() },
        jsx("div", { class: "pe-abox-grid" }, dots),
        jsx("div", { class: "pe-abox-grid pe-abox-cells" }, cells)));
}
/** Figma's grid rows: the Grid box (counts and picker) beside the two gaps; our own row options behind the sliders. */
function GridRows({ node, l, explicit, set, ov, disabled }) {
    return (jsx(Row, { label: "Grid", label2: "Gap", className: "pe-grid-row", override: ov('columns', 'rows', 'colSizes', 'rowSizes', 'colGap', 'rowGap'), onReset: () => set({ columns: undefined, rows: undefined, colSizes: undefined, rowSizes: undefined, colGap: undefined, rowGap: undefined }) },
        jsx(GridBox, { node: node, l: l, set: set, disabled: disabled }),
        jsx("div", { class: "pe-grid-gaps" },
            jsx(NumberField, { icon: "gapH", title: "Gap between columns", value: explicit.colGap, placeholder: l.colGap, min: 0, max: 400, decimals: 0, disabled: disabled, onChange: (n, m) => set({ colGap: n }, m) }),
            jsx(NumberField, { icon: "gapV", title: "Gap between rows", value: explicit.rowGap, placeholder: l.rowGap, min: 0, max: 400, decimals: 0, disabled: disabled, onChange: (n, m) => set({ rowGap: n }, m) })),
        jsx(PopoverButton, { icon: "sliders", title: "Grid options" },
            jsx(Row, { label: "Row height", override: ov('rowHeight'), onReset: () => set({ rowHeight: undefined }) },
                jsx(NumberField, { icon: "padV", title: "Smallest row height (rows that hug)", value: explicit.rowHeight, placeholder: l.rowHeight, min: 4, max: 600, decimals: 0, disabled: disabled, onChange: (n, m) => set({ rowHeight: n }, m) }),
                jsx(SelectField, { value: l.rowMode ?? 'auto', disabled: disabled, title: "Row height", options: [{ value: 'auto', label: 'At least' }, { value: 'fixed', label: 'Exactly' }], onChange: x => x && set({ rowMode: x }) })),
            jsx("div", { class: "pe-row pe-check-row" },
                jsx(Toggle, { checked: l.compact !== false, label: "Float items up into gaps", disabled: disabled, onChange: on => set({ compact: on }) })))));
}
/** Padding as Figma shows it: horizontal and vertical, or all four sides (left, top / right, bottom). */
function SpacingRows({ node, bp, setStyle, disabled, kind }) {
    const [individual, setIndividual] = useState(false);
    const v = styleValue(node, bp, kind);
    const box = (v.value ?? v.inherited);
    const [t, r, b, l] = box ?? [0, 0, 0, 0];
    const explicit = v.value;
    const min = 0;
    const label = 'Padding';
    const asymmetric = t !== b || l !== r;
    const showAll = individual || asymmetric;
    const write = (next, m) => setStyle({ [kind]: next }, m);
    const field = (side, name, icon) => (jsx(NumberField, { icon: icon, title: label + ' ' + name.toLowerCase(), value: explicit ? explicit[side] : undefined, placeholder: (box ?? [0, 0, 0, 0])[side], min: min, max: 400, decimals: 0, disabled: disabled, onChange: (n, m) => {
            const next = [...(box ?? [0, 0, 0, 0])];
            next[side] = n ?? 0;
            write(next, m);
        } }));
    const override = bp !== 'base' && v.value !== undefined;
    const toggle = jsx(IconButton, { icon: "corners", title: 'Individual ' + kind, on: showAll, disabled: disabled || asymmetric, onClick: () => setIndividual(!individual) });
    if (showAll) {
        return (jsx(Fragment, null,
            jsx(Row, { label: label, override: override, onReset: () => setStyle({ [kind]: undefined }) },
                field(3, 'Left', 'padLeft'),
                field(0, 'Top', 'padTop'),
                toggle),
            jsx(Row, null,
                field(1, 'Right', 'padRight'),
                field(2, 'Bottom', 'padBottom'))));
    }
    return (jsx(Row, { label: label, override: override, onReset: () => setStyle({ [kind]: undefined }) },
        jsx(NumberField, { icon: "padH", title: 'Horizontal ' + kind, value: explicit ? explicit[3] : undefined, placeholder: l, min: min, max: 400, decimals: 0, disabled: disabled, onChange: (n, m) => write([t, n ?? 0, b, n ?? 0], m) }),
        jsx(NumberField, { icon: "padV", title: 'Vertical ' + kind, value: explicit ? explicit[0] : undefined, placeholder: t, min: min, max: 400, decimals: 0, disabled: disabled, onChange: (n, m) => write([n ?? 0, r, n ?? 0, l], m) }),
        toggle));
}
/* ------------------------------------------------------------------ appearance */
function AppearanceSection({ node, ids, bp }) {
    const { session } = useEditor();
    const setStyle = useStyleSetter(ids, bp);
    const disabled = !!node.locked;
    const [corners, setCorners] = useState(false);
    const opacity = styleValue(node, bp, 'opacity');
    const radius = styleValue(node, bp, 'radius');
    const rv = (radius.value ?? radius.inherited);
    const uneven = !!rv && rv.some(x => x !== rv[0]);
    const showCorners = corners || uneven;
    const cornerField = (i, title, label) => (jsx(NumberField, { label: label, title: title, value: radius.value ? radius.value[i] : undefined, placeholder: rv ? rv[i] : 0, min: 0, max: 1000, decimals: 0, disabled: disabled, onChange: (n, m) => {
            const next = [...(rv ?? [0, 0, 0, 0])];
            next[i] = n ?? 0;
            setStyle({ radius: next }, m);
        } }));
    return (jsx(Section, { id: "appearance", title: "Appearance", actions: jsx(IconButton, { icon: node.hidden ? 'eyeOff' : 'eye', title: node.hidden ? 'Show' : 'Hide', on: !!node.hidden, onClick: () => setFlag(session.store, ids, 'hidden', !node.hidden) }) },
        jsx(Row, { label: "Opacity", label2: "Corner radius", override: bp !== 'base' && (opacity.value !== undefined || radius.value !== undefined), onReset: () => setStyle({ opacity: undefined, radius: undefined }) },
            jsx(NumberField, { icon: "opacity", title: "Opacity", value: opacity.value === undefined ? undefined : Math.round(opacity.value * 100), placeholder: opacity.inherited === undefined ? 100 : Math.round(opacity.inherited * 100), unit: "%", min: 0, max: 100, decimals: 0, disabled: disabled, onChange: (n, m) => setStyle({ opacity: n === undefined ? undefined : n / 100 }, m) }),
            jsx(NumberField, { icon: "radius", title: "Corner radius", value: !uneven && radius.value ? radius.value[0] : undefined, text: uneven ? 'Mixed' : undefined, placeholder: rv ? rv[0] : 0, min: 0, max: 1000, decimals: 0, disabled: disabled, onChange: (n, m) => setStyle({ radius: n === undefined ? undefined : [n, n, n, n] }, m) }),
            jsx(IconButton, { icon: "corners", title: "Individual corners", on: showCorners, disabled: disabled || uneven, onClick: () => setCorners(!corners) })),
        showCorners && (jsx(Fragment, null,
            jsx(Row, null,
                cornerField(0, 'Top left', '↖'),
                cornerField(1, 'Top right', '↗')),
            jsx(Row, null,
                cornerField(3, 'Bottom left', '↙'),
                cornerField(2, 'Bottom right', '↘'))))));
}
/* ------------------------------------------------------------------ typography */
function TypographySection({ node, ids, bp }) {
    const setStyle = useStyleSetter(ids, bp);
    const s = (k) => styleValue(node, bp, k);
    const ov = (...ks) => bp !== 'base' && ks.some(k => s(k).value !== undefined);
    const disabled = !!node.locked;
    const isText = TEXT_TYPES.has(node.type);
    const align = (s('textAlign').value ?? s('textAlign').inherited);
    return (jsx(Section, { id: isText ? 'typography' : 'typography-container', title: "Typography" },
        jsx(Row, { override: ov('fontFamily'), onReset: () => setStyle({ fontFamily: undefined }), label: isText ? undefined : 'Text inside' },
            jsx(Span2, null,
                jsx(SelectField, { value: s('fontFamily').value, placeholder: fontLabel(s('fontFamily').inherited), disabled: disabled, title: "Font family", options: [{ value: 'inherit', label: 'Site font' }, { value: 'sans', label: 'Sans serif' }, { value: 'serif', label: 'Serif' }, { value: 'mono', label: 'Monospace' }], onChange: v => setStyle({ fontFamily: v }) }))),
        jsx(Row, { override: ov('fontWeight', 'fontSize'), onReset: () => setStyle({ fontWeight: undefined, fontSize: undefined }) },
            jsx(SelectField, { value: s('fontWeight').value, placeholder: weightLabel(s('fontWeight').inherited), disabled: disabled, title: "Font weight", options: WEIGHTS.map(([v, l]) => ({ value: v, label: l })), onChange: v => setStyle({ fontWeight: v }) }),
            jsx(NumberField, { title: "Font size", value: s('fontSize').value, placeholder: s('fontSize').inherited ?? 'Auto', min: 6, max: 400, decimals: 1, disabled: disabled, onChange: (v, m) => setStyle({ fontSize: v }, m) })),
        jsx(Row, { label: "Line height", label2: "Letter spacing", override: ov('lineHeight', 'letterSpacing'), onReset: () => setStyle({ lineHeight: undefined, letterSpacing: undefined }) },
            jsx(NumberField, { icon: "padV", title: "Line height", value: s('lineHeight').value, placeholder: s('lineHeight').inherited ?? 'Auto', min: 0.5, max: 4, step: 0.05, disabled: disabled, onChange: (v, m) => setStyle({ lineHeight: v }, m) }),
            jsx(NumberField, { icon: "padH", title: "Letter spacing", value: s('letterSpacing').value, placeholder: s('letterSpacing').inherited ?? 0, unit: "px", min: -10, max: 40, step: 0.1, decimals: 1, disabled: disabled, onChange: (v, m) => setStyle({ letterSpacing: v }, m) })),
        jsx(Row, { label: "Alignment", override: ov('textAlign', 'textTransform', 'fontStyle'), onReset: () => setStyle({ textAlign: undefined, textTransform: undefined, fontStyle: undefined }) },
            jsx("div", { class: "pe-span2 pe-seg", role: "radiogroup", "aria-label": "Text alignment" }, [['left', 'textLeft', 'Align left'], ['center', 'textCenter', 'Align center'], ['right', 'textRight', 'Align right'], ['justify', 'textJustify', 'Justify']].map(([v, icon, title]) => (jsx("button", { key: v, type: "button", role: "radio", "aria-checked": align === v, class: cls('pe-seg-btn', align === v && 'is-on'), title: title, "aria-label": title, disabled: disabled, onClick: () => setStyle({ textAlign: v }) },
                jsx(Icon, { name: icon, size: 16 }))))),
            jsx(PopoverButton, { icon: "sliders", title: "Type settings" },
                jsx(Row, { label: "Case" },
                    jsx(Span2, null,
                        jsx(SelectField, { value: s('textTransform').value, placeholder: caseLabel(s('textTransform').inherited), disabled: disabled, options: [{ value: 'none', label: 'As typed' }, { value: 'uppercase', label: 'UPPERCASE' }, { value: 'lowercase', label: 'lowercase' }, { value: 'capitalize', label: 'Capitalize Words' }], onChange: v => setStyle({ textTransform: v }) }))),
                jsx("div", { class: "pe-row pe-check-row" },
                    jsx(Toggle, { checked: (s('fontStyle').value ?? s('fontStyle').inherited) === 'italic', label: "Italic", disabled: disabled, onChange: on => setStyle({ fontStyle: on ? 'italic' : 'normal' }) })))),
        !TEXT_FILL.has(node.type) && (jsx(Row, { label: "Text color", override: ov('color'), onReset: () => setStyle({ color: undefined }) },
            jsx(Span2, null,
                jsx(ColorField, { value: s('color').value, placeholder: s('color').inherited ?? 'Inherited', disabled: disabled, onChange: (v, m) => setStyle({ color: v }, m) }))))));
}
const WEIGHTS = [['100', 'Thin'], ['200', 'Extra light'], ['300', 'Light'], ['400', 'Regular'], ['500', 'Medium'], ['600', 'Semi bold'], ['700', 'Bold'], ['800', 'Extra bold'], ['900', 'Black']];
const fontLabel = (v) => ({ inherit: 'Site font', sans: 'Sans serif', serif: 'Serif', mono: 'Monospace' }[v ?? 'inherit'] ?? 'Site font');
const weightLabel = (v) => WEIGHTS.find(w => w[0] === v)?.[1] ?? 'Regular';
const caseLabel = (v) => ({ none: 'As typed', uppercase: 'UPPERCASE', lowercase: 'lowercase', capitalize: 'Capitalize Words' }[v ?? 'none'] ?? 'As typed');
/* ------------------------------------------------------------------ fill, stroke, effects */
/** Figma's eye on a fill or stroke: kept, not drawn. Below desktop it can also show one that desktop hides. */
function paintVisibility(node, bp, key, setStyle) {
    const hidden = effective(node, bp, key) === 'hidden';
    return { hidden, toggle: () => setStyle({ [key]: hidden ? (bp === 'base' ? undefined : 'visible') : 'hidden' }) };
}
/** One paint row as in Figma: the color (or image) box, then show/hide and remove. */
function PaintRow({ children, label, override, onReset, hidden, onToggle, onRemove, kind, disabled }) {
    return (jsx(Row, { className: cls('pe-paint-row', hidden && 'is-hidden'), label: label, override: override, onReset: onReset },
        children,
        onToggle ? jsx(IconButton, { icon: hidden ? 'eyeOff' : 'eye', title: (hidden ? 'Show ' : 'Hide ') + kind, disabled: disabled, onClick: onToggle }) : jsx("span", null),
        onRemove ? jsx(IconButton, { icon: "minus", title: 'Remove ' + kind, disabled: disabled, onClick: onRemove }) : jsx("span", null)));
}
function FillSection({ node, ids, bp }) {
    const setStyle = useStyleSetter(ids, bp);
    const disabled = !!node.locked;
    const [adding, setAdding] = useState(false);
    const bg = styleValue(node, bp, 'background');
    const color = styleValue(node, bp, 'color');
    const img = styleValue(node, bp, 'bgImage');
    const textFill = TEXT_FILL.has(node.type);
    const hasBg = (bg.value ?? bg.inherited) !== undefined;
    const hasImg = !!(img.value ?? img.inherited);
    const ov = (k) => bp !== 'base' && styleValue(node, bp, k).value !== undefined;
    const eye = paintVisibility(node, bp, 'fillHidden', setStyle);
    const addMenu = [
        { label: textFill ? 'Background color' : 'Color', icon: 'fill', disabled: hasBg, run: () => setStyle({ background: '#ffffff' }) },
        { label: 'Image', icon: 'image', disabled: hasImg, run: () => setStyle({ bgImage: '', bgSize: 'cover', bgPosition: 'center', bgRepeat: 'no-repeat' }) },
    ];
    const empty = !textFill && !hasBg && !hasImg && !adding;
    return (jsx(Section, { id: "fill", title: "Fill", empty: empty, actions: jsx(AddButton, { title: "Add fill", items: addMenu, disabled: disabled, onOpen: () => setAdding(false) }) },
        textFill && (jsx(PaintRow, { kind: "text color", override: ov('color'), onReset: () => setStyle({ color: undefined }), disabled: disabled },
            jsx(ColorField, { value: color.value, placeholder: color.inherited ?? 'Inherited', disabled: disabled, onChange: (v, m) => setStyle({ color: v }, m) }))),
        hasBg && (jsx(PaintRow, { kind: "fill", label: textFill ? 'Background' : undefined, override: ov('background') || ov('fillHidden'), onReset: () => setStyle({ background: undefined, fillHidden: undefined }), hidden: eye.hidden, onToggle: eye.toggle, onRemove: () => setStyle({ background: undefined, ...(hasImg ? {} : { fillHidden: undefined }) }), disabled: disabled },
            jsx(ColorField, { value: bg.value, placeholder: bg.inherited, disabled: disabled, onChange: (v, m) => setStyle({ background: v }, m) }))),
        (hasImg || img.value === '') && (jsx(ImageFillRow, { node: node, bp: bp, setStyle: setStyle, disabled: disabled, hidden: eye.hidden, onToggle: eye.toggle, onRemove: () => setStyle({ bgImage: undefined, bgSize: undefined, bgPosition: undefined, bgRepeat: undefined, ...(hasBg ? {} : { fillHidden: undefined }) }) }))));
}
function ImageFillRow({ node, bp, setStyle, disabled, hidden, onToggle, onRemove }) {
    const img = styleValue(node, bp, 'bgImage');
    const src = (img.value ?? img.inherited ?? '');
    const size = effective(node, bp, 'bgSize');
    const position = effective(node, bp, 'bgPosition');
    return (jsx(PaintRow, { kind: "image", override: bp !== 'base' && img.value !== undefined, onReset: () => setStyle({ bgImage: undefined }), hidden: hidden, onToggle: onToggle, onRemove: onRemove, disabled: disabled },
        jsx("div", { class: "pe-fill-image" },
            jsx("span", { class: "pe-fill-thumb", style: src ? { backgroundImage: `url("${src.replace(/"/g, '%22')}")` } : undefined }),
            jsx("span", { class: "pe-fill-name" }, src ? src.split('/').pop() : 'Image'),
            jsx(PopoverButton, { icon: "sliders", title: "Image fill", wide: true },
                jsx(ImageInput, { value: src, disabled: disabled, preview: true, onChange: v => setStyle({ bgImage: v }) }),
                jsx(Row, { label: "Fit", label2: "Position" },
                    jsx(SelectField, { value: size, placeholder: "Original", disabled: disabled, options: [{ value: 'cover', label: 'Fill' }, { value: 'contain', label: 'Fit' }, { value: 'auto', label: 'Original size' }], onChange: v => setStyle({ bgSize: v }) }),
                    jsx(SelectField, { value: position, placeholder: "Top left", disabled: disabled, options: ['center', 'top', 'bottom', 'left', 'right'].map(v => ({ value: v, label: v[0].toUpperCase() + v.slice(1) })), onChange: v => setStyle({ bgPosition: v }) })),
                jsx(Row, { label: "Repeat" },
                    jsx(Span2, null,
                        jsx(SelectField, { value: effective(node, bp, 'bgRepeat'), placeholder: "Repeat", disabled: disabled, options: [{ value: 'no-repeat', label: 'Once' }, { value: 'repeat', label: 'Tile' }, { value: 'repeat-x', label: 'Tile across' }, { value: 'repeat-y', label: 'Tile down' }], onChange: v => setStyle({ bgRepeat: v }) })))))));
}
const STROKE_RESET = { borderWidth: undefined, borderColor: undefined, borderStyle: undefined, borderSides: undefined, strokeAlign: undefined, strokeHidden: undefined };
const SIDES = ['top', 'right', 'bottom', 'left'];
/** Which of Figma's "individual strokes" choices a set of side weights is. */
function sideOf(sides) {
    if (!sides)
        return 'all';
    const on = sides.map(w => w > 0);
    return on.filter(Boolean).length === 1 ? SIDES[on.indexOf(true)] : 'custom';
}
function StrokeSection({ node, ids, bp }) {
    const setStyle = useStyleSetter(ids, bp);
    const disabled = !!node.locked;
    const width = styleValue(node, bp, 'borderWidth');
    const color = styleValue(node, bp, 'borderColor');
    const style = styleValue(node, bp, 'borderStyle');
    const sides = effective(node, bp, 'borderSides');
    const align = effective(node, bp, 'strokeAlign') ?? 'inside';
    const w = (width.value ?? width.inherited);
    const has = (w !== undefined && w > 0) || (color.value ?? color.inherited) !== undefined || !!sides;
    const ov = bp !== 'base' && Object.keys(STROKE_RESET).some(k => styleValue(node, bp, k).value !== undefined);
    const eye = paintVisibility(node, bp, 'strokeHidden', setStyle);
    const side = sideOf(sides);
    const weight = sides ? Math.max(...sides) : w ?? 0;
    const setSide = (next) => {
        const n = weight || 1;
        if (next === 'all')
            setStyle({ borderSides: undefined, borderWidth: n });
        else if (next === 'custom')
            setStyle({ borderSides: sides ?? [n, n, n, n] });
        else
            setStyle({ borderSides: SIDES.map(x => (x === next ? n : 0)) });
    };
    const sideMenu = [['all', 'All', 'strokeSides'], ['top', 'Top', 'strokeTop'], ['bottom', 'Bottom', 'strokeBottom'], ['left', 'Left', 'strokeLeft'], ['right', 'Right', 'strokeRight'], ['custom', 'Custom', 'strokeSides']]
        .map(([v, label, icon]) => ({ label, icon, checked: side === v, run: () => setSide(v) }));
    const sideField = (i, name, icon) => (jsx(NumberField, { icon: icon, title: name + ' stroke', value: sides?.[i], min: 0, max: 40, decimals: 1, disabled: disabled, onChange: (v, m) => {
            const next = [...(sides ?? [0, 0, 0, 0])];
            next[i] = v ?? 0;
            setStyle({ borderSides: next }, m);
        } }));
    // One weight for all sides, for the single side shown, or (custom) per side below.
    const setWeight = (v, m) => setStyle(sides ? { borderSides: sides.map(x => (x > 0 ? v ?? 0 : 0)) } : { borderWidth: v }, m);
    return (jsx(Section, { id: "stroke", title: "Stroke", empty: !has, actions: jsx(IconButton, { icon: "plus", title: "Add stroke", disabled: disabled || has, onClick: () => setStyle({ borderWidth: 1, borderStyle: 'solid', borderColor: '#000000' }) }) },
        jsx(PaintRow, { kind: "stroke", override: ov, onReset: () => setStyle(STROKE_RESET), hidden: eye.hidden, onToggle: eye.toggle, onRemove: () => setStyle(STROKE_RESET), disabled: disabled },
            jsx(ColorField, { value: color.value, placeholder: color.inherited ?? '#000000', disabled: disabled, onChange: (v, m) => setStyle({ borderColor: v }, m) })),
        jsx(Row, { label: "Position", label2: "Weight" },
            jsx(SelectField, { value: sides ? 'inside' : align, disabled: disabled || !!sides, title: sides ? 'Strokes on single sides are drawn inside' : 'Stroke position', options: [{ value: 'inside', label: 'Inside' }, { value: 'center', label: 'Center' }, { value: 'outside', label: 'Outside' }], onChange: v => setStyle({ strokeAlign: v === 'inside' && bp === 'base' ? undefined : v }) }),
            jsx("div", { class: "pe-stroke-weight" },
                jsx(NumberField, { icon: "strokeWeight", title: "Stroke weight", value: side === 'custom' ? undefined : weight, text: side === 'custom' ? 'Mixed' : undefined, min: 0, max: 40, decimals: 1, disabled: disabled, onChange: setWeight }),
                jsx(PopoverButton, { icon: "sliders", title: "Stroke settings" },
                    jsx(Row, { label: "Style" },
                        jsx(Span2, null,
                            jsx(SelectField, { value: style.value, placeholder: (style.inherited ?? 'solid').replace(/^./, c => c.toUpperCase()), disabled: disabled, options: ['solid', 'dashed', 'dotted', 'double'].map(v => ({ value: v, label: v[0].toUpperCase() + v.slice(1) })), onChange: v => setStyle({ borderStyle: v }) }))))),
            jsx(SideButton, { items: sideMenu, on: side !== 'all', icon: side === 'all' || side === 'custom' ? 'strokeSides' : 'stroke' + side[0].toUpperCase() + side.slice(1), disabled: disabled })),
        side === 'custom' && (jsx(Fragment, null,
            jsx(Row, null,
                sideField(3, 'Left', 'strokeLeft'),
                sideField(0, 'Top', 'strokeTop')),
            jsx(Row, null,
                sideField(1, 'Right', 'strokeRight'),
                sideField(2, 'Bottom', 'strokeBottom'))))));
}
/** Figma's "Individual strokes" button: a menu of All / one side / Custom. */
function SideButton({ items, on, icon, disabled }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    return (jsx("div", { class: "pe-pop-wrap", ref: ref },
        jsx(IconButton, { icon: icon, title: "Individual strokes", on: on || open, disabled: disabled, onClick: () => setOpen(!open) }),
        open && jsx(DropMenu, { items: items, onClose: () => setOpen(false), align: "right" })));
}
const SHADOW_OPTIONS = [
    { value: 'sm', label: 'Drop shadow · small' }, { value: 'md', label: 'Drop shadow · medium' }, { value: 'lg', label: 'Drop shadow · large' }, { value: 'xl', label: 'Drop shadow · extra large' },
    { value: 'dim', label: 'Darken image' }, { value: 'fade', label: 'Dark fade at the bottom' }, { value: 'custom', label: 'Custom shadow' },
];
function EffectsSection({ node, ids, bp }) {
    const setStyle = useStyleSetter(ids, bp);
    const disabled = !!node.locked;
    const shadow = styleValue(node, bp, 'shadow');
    const cur = shadow.value ?? shadow.inherited;
    const has = cur !== undefined && cur !== 'none';
    const custom = cur && typeof cur === 'object' ? cur : null;
    const setCustom = (patch, m) => custom && setStyle({ shadow: { ...custom, ...patch } }, m);
    return (jsx(Section, { id: "effects", title: "Effects", empty: !has, actions: jsx(IconButton, { icon: "plus", title: "Add effect", disabled: disabled || has, onClick: () => setStyle({ shadow: 'md' }) }) },
        jsx(Row, { override: bp !== 'base' && shadow.value !== undefined, onReset: () => setStyle({ shadow: undefined }) },
            jsx(Span2, null,
                jsx(SelectField, { value: typeof cur === 'string' ? cur : custom ? 'custom' : undefined, disabled: disabled, options: SHADOW_OPTIONS, onChange: v => v && setStyle({ shadow: v === 'custom' ? { x: 0, y: 4, blur: 16, spread: 0, color: 'rgba(0,0,0,0.25)' } : v }) })),
            jsx(IconButton, { icon: "minus", title: "Remove effect", disabled: disabled, onClick: () => setStyle({ shadow: bp === 'base' ? undefined : 'none' }) })),
        custom && (jsx(Fragment, null,
            jsx(Row, { label: "X", label2: "Y" },
                jsx(NumberField, { label: "X", value: custom.x, min: -200, max: 200, decimals: 0, disabled: disabled, onChange: (v, m) => setCustom({ x: v ?? 0 }, m) }),
                jsx(NumberField, { label: "Y", value: custom.y, min: -200, max: 200, decimals: 0, disabled: disabled, onChange: (v, m) => setCustom({ y: v ?? 0 }, m) })),
            jsx(Row, { label: "Blur", label2: "Spread" },
                jsx(NumberField, { icon: "blend", title: "Blur", value: custom.blur, min: 0, max: 400, decimals: 0, disabled: disabled, onChange: (v, m) => setCustom({ blur: v ?? 0 }, m) }),
                jsx(NumberField, { icon: "padH", title: "Spread", value: custom.spread, min: -200, max: 200, decimals: 0, disabled: disabled, onChange: (v, m) => setCustom({ spread: v ?? 0 }, m) })),
            jsx(Row, { label: "Color" },
                jsx(Span2, null,
                    jsx(ColorField, { value: custom.color, disabled: disabled, onChange: (v, m) => v && setCustom({ color: v }, m) })),
                jsx(IconButton, { icon: "square", title: "Inner shadow", on: !!custom.inset, disabled: disabled, onClick: () => setCustom({ inset: !custom.inset }) }))))));
}
/** The "+" of a section, with a menu when there is more than one kind to add. */
function AddButton({ title, items, disabled, onOpen }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    const available = items.filter(i => i !== 'sep' && !i.disabled);
    return (jsx("div", { class: "pe-pop-wrap", ref: ref },
        jsx(IconButton, { icon: "plus", title: title, disabled: disabled || available.length === 0, on: open, onClick: () => {
                if (available.length === 1 && available[0] !== 'sep')
                    available[0].run();
                else {
                    onOpen?.();
                    setOpen(!open);
                }
            } }),
        open && jsx(DropMenu, { items: items, onClose: () => setOpen(false), align: "right" })));
}
