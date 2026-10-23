/** The editor's top bar: page, devices, zoom, save state and publishing (port of TopBar.tsx). */
import { jsx, useEffect, useRef, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { cls, useDocVersion, useEditor, useSignal } from "./hooks.js";
import { scrubValue } from "./fields.js";
const DEVICES = [
    { bp: 'base', icon: 'desktop', label: 'Desktop', key: '1' },
    { bp: 'tablet', icon: 'tablet', label: 'Tablet', key: '2' },
    { bp: 'mobile', icon: 'mobile', label: 'Mobile', key: '3' },
];
export function TopBar({ actions, onClose }) {
    const { session } = useEditor();
    const page = useSignal(session.page);
    const bp = useSignal(session.breakpoint);
    const zoom = useSignal(session.zoom);
    const zoomMode = useSignal(session.zoomMode);
    const caps = useSignal(session.caps);
    const others = useSignal(session.others);
    const pending = useSignal(session.pendingChanges);
    const widths = useSignal(session.deviceWidths);
    useDocVersion();
    const store = session.store;
    const status = !page ? '' : page.published && page.enabled ? (pending ? 'Live · changes not published' : 'Live') : page.published ? 'Published · switched off' : 'Layout not published';
    return (jsx("header", { class: "pe-topbar" },
        jsx("div", { class: "pe-tb-left" },
            jsx("button", { type: "button", class: "pe-icon-btn pe-close", title: "Close the editor", "aria-label": "Close the editor", onClick: onClose },
                jsx(Icon, { name: "close", size: 16 })),
            jsx("div", { class: "pe-brand" },
                jsx(Icon, { name: "editor", size: 16 })),
            jsx("div", { class: "pe-page-title" },
                jsx("span", { class: "pe-page-name", title: page?.url }, page?.name ?? '…'),
                jsx("span", { class: cls('pe-status-chip', page?.published && page.enabled ? 'is-live' : 'is-draft') }, status))),
        jsx("div", { class: "pe-tb-center" },
            jsx("div", { class: "pe-seg pe-devices", role: "radiogroup", "aria-label": "Device" }, DEVICES.map(d => (jsx("button", { key: d.bp, type: "button", role: "radio", "aria-checked": bp === d.bp, class: cls('pe-seg-btn', bp === d.bp && 'is-on'), title: `${d.label} (${d.key})${d.bp !== 'base' ? ' - changes made here only apply to ' + d.label.toLowerCase() : ''}`, onClick: () => session.breakpoint.set(d.bp) },
                jsx(Icon, { name: d.icon, size: 16 }),
                jsx("span", { class: "pe-seg-text" }, d.label))))),
            jsx(WidthInput, { value: widths[bp], onChange: w => session.deviceWidths.update(x => ({ ...x, [bp]: w })), bp: bp }),
            jsx(ZoomMenu, { zoom: zoom, fit: zoomMode === 'fit', actions: actions })),
        jsx("div", { class: "pe-tb-right" },
            others.length > 0 && (jsx("div", { class: "pe-presence", title: 'Also editing this page: ' + others.map(o => o.displayName).join(', ') }, others.slice(0, 3).map(o => jsx("span", { key: o.userId, class: "pe-avatar" }, initials(o.displayName))))),
            jsx("button", { type: "button", class: "pe-icon-btn", title: 'Undo' + (store.undoLabel ? ': ' + store.undoLabel : '') + ' (Ctrl+Z)', "aria-label": "Undo", disabled: !store.canUndo, onClick: () => actions.undo() },
                jsx(Icon, { name: "undo", size: 16 })),
            jsx("button", { type: "button", class: "pe-icon-btn", title: 'Redo' + (store.redoLabel ? ': ' + store.redoLabel : '') + ' (Ctrl+Shift+Z)', "aria-label": "Redo", disabled: !store.canRedo, onClick: () => actions.redo() },
                jsx(Icon, { name: "redo", size: 16 })),
            jsx(SaveIndicator, null),
            jsx("span", { class: "pe-tb-sep" }),
            jsx("button", { type: "button", class: "pe-btn", title: "Open the draft in a new tab (Ctrl+P)", onClick: () => actions.preview() },
                jsx(Icon, { name: "play", size: 16 }),
                " Preview"),
            jsx(MoreMenu, null),
            caps.publish && (jsx("button", { type: "button", class: "pe-btn pe-btn-primary", onClick: () => session.dialog.set({ kind: 'publish' }), disabled: !pending && !!page?.published && !!page.enabled },
                jsx(Icon, { name: "publish", size: 16 }),
                " Publish")))));
}
function initials(name) {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0].toUpperCase()).join('');
}
function WidthInput({ value, onChange, bp }) {
    const { session } = useEditor();
    const [draft, setDraft] = useState(String(value));
    useEffect(() => setDraft(String(value)), [value]);
    const bps = session.store.doc.settings.breakpoints;
    const range = bp === 'base' ? [bps.tablet, 3840] : bp === 'tablet' ? [bps.mobile, bps.tablet - 1] : [240, bps.mobile - 1];
    const commit = () => {
        const n = parseInt(draft, 10);
        if (Number.isFinite(n))
            onChange(Math.min(range[1], Math.max(range[0], n)));
        else
            setDraft(String(value));
    };
    return (jsx("label", { class: "pe-width", title: `Canvas width for this device (${range[0]}-${range[1]} px)` },
        jsx("span", { title: "Drag left or right to change the width", onPointerDown: e => scrubValue(e, value, n => onChange(Math.round(Math.min(range[1], Math.max(range[0], n))))) }, "W"),
        jsx("input", { value: draft, inputMode: "numeric", "aria-label": "Canvas width", onInput: e => setDraft(e.target.value), onBlur: commit, onKeyDown: e => e.key === 'Enter' && commit() })));
}
function ZoomMenu({ zoom, fit, actions }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useOutside(ref, open, () => setOpen(false));
    return (jsx("div", { class: "pe-dropdown", ref: ref },
        jsx("button", { type: "button", class: "pe-btn pe-btn-ghost", "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen(!open) },
            Math.round(zoom * 100),
            "%",
            fit ? ' · Fit' : '',
            " ",
            jsx(Icon, { name: "chevronDown", size: 14 })),
        open && (jsx("div", { class: "pe-menu pe-menu-anchored", role: "menu" }, [
            ['Zoom in', 'Ctrl+=', () => actions.zoom(1.25)],
            ['Zoom out', 'Ctrl+-', () => actions.zoom(-1)],
            ['Zoom to 100%', 'Ctrl+0', () => actions.zoom(1)],
            ['Fit to screen', 'Shift+1', () => actions.zoom('fit')],
        ].map(([label, keys, run]) => (jsx("button", { key: label, type: "button", role: "menuitem", onClick: () => {
                setOpen(false);
                run();
            } },
            jsx("span", { class: "pe-menu-icon" }),
            jsx("span", { class: "pe-menu-label" }, label),
            jsx("kbd", null, keys))))))));
}
function MoreMenu() {
    const { session, canvasTheme: canvasThemeSignal } = useEditor();
    const canvasTheme = useSignal(canvasThemeSignal);
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useOutside(ref, open, () => setOpen(false));
    const page = useSignal(session.page);
    const caps = useSignal(session.caps);
    const showGrid = useSignal(session.showGrid);
    const pending = useSignal(session.pendingChanges);
    const theme = useSignal(session.theme);
    const themes = [
        // The admin's theme (one setting for the admin screens and the editor - session.js).
        { value: 'light', label: 'Light theme', icon: 'sun' },
        { value: 'dark', label: 'Dark theme', icon: 'moon' },
        { value: 'system', label: 'Match system', icon: 'monitorTheme' },
    ];
    // The page in the canvas, light or dark (canvas.js), independent of the editor's theme.
    const canvasThemes = [
        { value: 'light', label: 'Page: light', icon: 'sun' },
        { value: 'dark', label: 'Page: dark', icon: 'moon' },
    ];
    const items = [
        { label: showGrid ? 'Hide grid lines' : 'Show grid lines', icon: 'grid', run: () => session.showGrid.set(!showGrid) },
        { label: 'Keyboard shortcuts', icon: 'keyboard', run: () => session.dialog.set({ kind: 'shortcuts' }) },
    ];
    if (caps.publish && page?.published) {
        items.push({ label: 'Unpublish', icon: 'cloudOff', run: () => session.dialog.set({
                kind: 'confirm', title: 'Unpublish this layout?', confirm: 'Unpublish',
                message: 'Visitors will see the page\'s default view again. Your draft is kept and can be published again.',
                onConfirm: () => void session.unpublish(),
            }) });
    }
    if (caps.editLayout && page?.published && pending) {
        items.push({ label: 'Discard unpublished changes', icon: 'restore', danger: true, run: () => session.dialog.set({
                kind: 'confirm', title: 'Discard all unpublished changes?', confirm: 'Discard changes', danger: true,
                message: 'The draft goes back to the published layout. This cannot be undone.',
                onConfirm: () => void session.discardDraft(),
            }) });
    }
    return (jsx("div", { class: "pe-dropdown", ref: ref },
        jsx("button", { type: "button", class: "pe-icon-btn", title: "More", "aria-label": "More actions", "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen(!open) },
            jsx(Icon, { name: "more", size: 16 })),
        open && (jsx("div", { class: "pe-menu pe-menu-anchored is-right", role: "menu" },
            jsx("div", { class: "pe-menu-group" }, items.map(it => (jsx("button", { key: it.label, type: "button", role: "menuitem", class: it.danger ? 'is-danger' : '', disabled: it.disabled, onClick: () => {
                    setOpen(false);
                    it.run();
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: it.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, it.label))))),
            jsx("div", { class: "pe-menu-group", role: "group", "aria-label": "Page in the canvas" }, canvasThemes.map(t => (jsx("button", { key: t.value, type: "button", role: "menuitemradio", "aria-checked": canvasTheme === t.value, onClick: () => {
                    setOpen(false);
                    canvasThemeSignal.set(t.value);
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: t.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, t.label),
                canvasTheme === t.value && jsx("span", { class: "pe-menu-check" },
                    jsx(Icon, { name: "check", size: 16 })))))),
            jsx("div", { class: "pe-menu-group", role: "group", "aria-label": "Editor theme" }, themes.map(t => (jsx("button", { key: t.value, type: "button", role: "menuitemradio", "aria-checked": theme === t.value, onClick: () => {
                    setOpen(false);
                    session.setTheme(t.value);
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: t.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, t.label),
                theme === t.value && jsx("span", { class: "pe-menu-check" },
                    jsx(Icon, { name: "check", size: 16 }))))))))));
}
export function SaveIndicator() {
    const { session } = useEditor();
    const state = useSignal(session.saveState);
    const error = useSignal(session.saveError);
    const savedOn = useSignal(session.savedOn);
    const readOnly = session.readOnly;
    if (readOnly)
        return jsx("span", { class: "pe-save is-readonly", title: "You can look but not change this page." },
            jsx(Icon, { name: "eye", size: 16 }),
            " View only");
    const map = {
        saved: { icon: 'check', text: 'Saved', title: savedOn ? 'Draft saved ' + new Date(savedOn).toLocaleTimeString() : 'All changes are saved' },
        dirty: { icon: 'cloud', text: 'Unsaved', title: 'Changes are saved automatically in a moment' },
        saving: { icon: 'loader', text: 'Saving…', title: 'Saving your changes' },
        error: { icon: 'alert', text: 'Not saved', title: error || 'Saving failed. Click to retry.' },
        offline: { icon: 'cloudOff', text: 'Offline', title: error || 'The server cannot be reached. Retrying.' },
        conflict: { icon: 'alert', text: 'Conflict', title: 'Someone else saved this page. Choose which version to keep.' },
    }[state];
    return (jsx("button", { type: "button", class: cls('pe-save', 'is-' + state), title: map.title, "aria-live": "polite", onClick: () => (state === 'conflict' ? void session.reloadFromServer() : void session.save(true)) },
        jsx(Icon, { name: map.icon, size: 16, class: state === 'saving' ? 'pe-spin' : undefined }),
        jsx("span", null, map.text)));
}
function useOutside(ref, active, close) {
    useEffect(() => {
        if (!active)
            return;
        const h = (e) => {
            if (!ref.current?.contains(e.target))
                close();
        };
        // Escape closes the menu too (an addition to the reference).
        const key = (e) => {
            if (e.key === 'Escape') {
                e.stopPropagation();
                close();
            }
        };
        window.addEventListener('pointerdown', h, true);
        window.addEventListener('keydown', key, true);
        return () => {
            window.removeEventListener('pointerdown', h, true);
            window.removeEventListener('keydown', key, true);
        };
    }, [active]);
}
