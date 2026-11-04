/** The editor's top bar: page, devices, zoom, save state and publishing (port of TopBar.tsx). */
import { t } from "./i18n.js";
import { jsx, useEffect, useRef, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { cls, useDocVersion, useEditor, useSignal } from "./hooks.js";
import { scrubValue } from "./fields.js";
const DEVICES = [
    { bp: 'base', icon: 'desktop', label: t("Desktop"), key: '1' },
    { bp: 'tablet', icon: 'tablet', label: t("Tablet"), key: '2', only: t("changes made here only apply to tablet") },
    { bp: 'mobile', icon: 'mobile', label: t("Mobile"), key: '3', only: t("changes made here only apply to mobile") },
];
/** An undo step's name in the admin language: core/commands.js names its steps in English (core shows no text). */
function stepLabel(label) {
    const insert = /^Insert (.+)$/.exec(label);
    if (insert)
        return t("Insert {0}", insert[1]);
    const steps = {
        'Move': t("Move"),
        'Move elements': t("Move elements"),
        'Delete': t("Delete"),
        'Delete elements': t("Delete elements"),
        'Duplicate': t("Duplicate"),
        'Paste': t("Paste"),
        'Edit content': t("Edit content"),
        'Change style': t("Change style"),
        'Change layout': t("Change layout"),
        'Reset tablet': t("Reset tablet"),
        'Reset mobile': t("Reset mobile"),
        'Rename': t("Rename"),
        'Hide': t("Hide"),
        'Show': t("Show"),
        'Lock': t("Lock"),
        'Unlock': t("Unlock"),
        'Change visibility': t("Change visibility"),
        'Change advanced settings': t("Change advanced settings"),
        'Bind to content': t("Bind to content"),
        'Replace the layout': t("Replace the layout"),
        'Wrap in container': t("Wrap in container"),
        'Remove container': t("Remove container"),
        'Change breakpoints': t("Change breakpoints"),
    };
    return Object.prototype.hasOwnProperty.call(steps, label) ? steps[label] : label;
}
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
    const status = !page ? '' : page.published && page.enabled ? (pending ? t("Live · changes not published") : t("Live")) : page.published ? t("Published · switched off") : t("Layout not published");
    return (jsx("header", { class: "pe-topbar" },
        jsx("div", { class: "pe-tb-left" },
            jsx("button", { type: "button", class: "pe-icon-btn pe-close", title: t("Close the editor"), "aria-label": t("Close the editor"), onClick: onClose },
                jsx(Icon, { name: "close", size: 16 })),
            jsx("div", { class: "pe-brand" },
                jsx(Icon, { name: "editor", size: 16 })),
            jsx("div", { class: "pe-page-title" },
                jsx("span", { class: "pe-page-name", title: page?.url }, page?.name ?? '…'),
                jsx("span", { class: cls('pe-status-chip', page?.published && page.enabled ? 'is-live' : 'is-draft') }, status))),
        jsx("div", { class: "pe-tb-center" },
            jsx("div", { class: "pe-seg pe-devices", role: "radiogroup", "aria-label": t("Device") }, DEVICES.map(d => (jsx("button", { key: d.bp, type: "button", role: "radio", "aria-checked": bp === d.bp, class: cls('pe-seg-btn', bp === d.bp && 'is-on'), title: `${d.label} (${d.key})${d.only ? ' - ' + d.only : ''}`, onClick: () => session.breakpoint.set(d.bp) },
                jsx(Icon, { name: d.icon, size: 16 }),
                jsx("span", { class: "pe-seg-text" }, d.label))))),
            jsx(WidthInput, { value: widths[bp], onChange: w => session.deviceWidths.update(x => ({ ...x, [bp]: w })), bp: bp }),
            jsx(ZoomMenu, { zoom: zoom, fit: zoomMode === 'fit', actions: actions })),
        jsx("div", { class: "pe-tb-right" },
            others.length > 0 && (jsx("div", { class: "pe-presence", title: t("Also editing this page: {0}", others.map(o => o.displayName).join(', ')) }, others.slice(0, 3).map(o => jsx("span", { key: o.userId, class: "pe-avatar" }, initials(o.displayName))))),
            jsx("button", { type: "button", class: "pe-icon-btn", title: store.undoLabel ? t("Undo: {0} (Ctrl+Z)", stepLabel(store.undoLabel)) : t("Undo (Ctrl+Z)"), "aria-label": t("Undo"), disabled: !store.canUndo, onClick: () => actions.undo() },
                jsx(Icon, { name: "undo", size: 16 })),
            jsx("button", { type: "button", class: "pe-icon-btn", title: store.redoLabel ? t("Redo: {0} (Ctrl+Shift+Z)", stepLabel(store.redoLabel)) : t("Redo (Ctrl+Shift+Z)"), "aria-label": t("Redo"), disabled: !store.canRedo, onClick: () => actions.redo() },
                jsx(Icon, { name: "redo", size: 16 })),
            jsx(SaveIndicator, null),
            jsx("span", { class: "pe-tb-sep" }),
            jsx("button", { type: "button", class: "pe-btn", title: t("Open the draft in a new tab (Ctrl+P)"), onClick: () => actions.preview() },
                jsx(Icon, { name: "play", size: 16 }),
                " " + t("Preview")),
            jsx(MoreMenu, null),
            caps.publish && (jsx("button", { type: "button", class: "pe-btn pe-btn-primary", onClick: () => session.dialog.set({ kind: 'publish' }), disabled: !pending && !!page?.published && !!page.enabled },
                jsx(Icon, { name: "publish", size: 16 }),
                " " + t("Publish"))))));
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
    return (jsx("label", { class: "pe-width", title: t("Canvas width for this device ({0}-{1} px)", range[0], range[1]) },
        jsx("span", { title: t("Drag left or right to change the width"), onPointerDown: e => scrubValue(e, value, n => onChange(Math.round(Math.min(range[1], Math.max(range[0], n))))) }, t("W")),
        jsx("input", { value: draft, inputMode: "numeric", "aria-label": t("Canvas width"), onInput: e => setDraft(e.target.value), onBlur: commit, onKeyDown: e => e.key === 'Enter' && commit() })));
}
function ZoomMenu({ zoom, fit, actions }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useOutside(ref, open, () => setOpen(false));
    return (jsx("div", { class: "pe-dropdown", ref: ref },
        jsx("button", { type: "button", class: "pe-btn pe-btn-ghost", "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen(!open) },
            Math.round(zoom * 100),
            "%",
            fit ? ' · ' + t("Fit") : '',
            " ",
            jsx(Icon, { name: "chevronDown", size: 14 })),
        open && (jsx("div", { class: "pe-menu pe-menu-anchored", role: "menu" }, [
            [t("Zoom in"), 'Ctrl+=', () => actions.zoom(1.25)],
            [t("Zoom out"), 'Ctrl+-', () => actions.zoom(-1)],
            [t("Zoom to 100%"), 'Ctrl+0', () => actions.zoom(1)],
            [t("Fit to screen"), 'Shift+1', () => actions.zoom('fit')],
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
        { value: 'light', label: t("Light theme"), icon: 'sun' },
        { value: 'dark', label: t("Dark theme"), icon: 'moon' },
        { value: 'system', label: t("Match system"), icon: 'monitorTheme' },
    ];
    // The page in the canvas, light or dark (canvas.js), independent of the editor's theme.
    const canvasThemes = [
        { value: 'light', label: t("Page: light"), icon: 'sun' },
        { value: 'dark', label: t("Page: dark"), icon: 'moon' },
    ];
    const items = [
        { label: showGrid ? t("Hide grid lines") : t("Show grid lines"), icon: 'grid', run: () => session.showGrid.set(!showGrid) },
        { label: t("Keyboard shortcuts"), icon: 'keyboard', run: () => session.dialog.set({ kind: 'shortcuts' }) },
    ];
    if (caps.publish && page?.published) {
        items.push({ label: t("Unpublish"), icon: 'cloudOff', run: () => session.dialog.set({
                kind: 'confirm', title: t("Unpublish this layout?"), confirm: t("Unpublish"),
                message: t("Visitors will see the page's default view again. Your draft is kept and can be published again."),
                onConfirm: () => void session.unpublish(),
            }) });
    }
    if (caps.editLayout && page?.published && pending) {
        items.push({ label: t("Discard unpublished changes"), icon: 'restore', danger: true, run: () => session.dialog.set({
                kind: 'confirm', title: t("Discard all unpublished changes?"), confirm: t("Discard changes"), danger: true,
                message: t("The draft goes back to the published layout. This cannot be undone."),
                onConfirm: () => void session.discardDraft(),
            }) });
    }
    return (jsx("div", { class: "pe-dropdown", ref: ref },
        jsx("button", { type: "button", class: "pe-icon-btn", title: t("More"), "aria-label": t("More actions"), "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen(!open) },
            jsx(Icon, { name: "more", size: 16 })),
        open && (jsx("div", { class: "pe-menu pe-menu-anchored is-right", role: "menu" },
            jsx("div", { class: "pe-menu-group" }, items.map(it => (jsx("button", { key: it.label, type: "button", role: "menuitem", class: it.danger ? 'is-danger' : '', disabled: it.disabled, onClick: () => {
                    setOpen(false);
                    it.run();
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: it.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, it.label))))),
            jsx("div", { class: "pe-menu-group", role: "group", "aria-label": t("Page in the canvas") }, canvasThemes.map(ct => (jsx("button", { key: ct.value, type: "button", role: "menuitemradio", "aria-checked": canvasTheme === ct.value, onClick: () => {
                    setOpen(false);
                    canvasThemeSignal.set(ct.value);
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: ct.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, ct.label),
                canvasTheme === ct.value && jsx("span", { class: "pe-menu-check" },
                    jsx(Icon, { name: "check", size: 16 })))))),
            jsx("div", { class: "pe-menu-group", role: "group", "aria-label": t("Editor theme") }, themes.map(th => (jsx("button", { key: th.value, type: "button", role: "menuitemradio", "aria-checked": theme === th.value, onClick: () => {
                    setOpen(false);
                    session.setTheme(th.value);
                } },
                jsx("span", { class: "pe-menu-icon" },
                    jsx(Icon, { name: th.icon, size: 16 })),
                jsx("span", { class: "pe-menu-label" }, th.label),
                theme === th.value && jsx("span", { class: "pe-menu-check" },
                    jsx(Icon, { name: "check", size: 16 }))))))))));
}
function SaveIndicator() {
    const { session } = useEditor();
    const state = useSignal(session.saveState);
    const error = useSignal(session.saveError);
    const savedOn = useSignal(session.savedOn);
    const readOnly = session.readOnly;
    if (readOnly)
        return jsx("span", { class: "pe-save is-readonly", title: t("You can look but not change this page.") },
            jsx(Icon, { name: "eye", size: 16 }),
            " " + t("View only"));
    const map = {
        saved: { icon: 'check', text: t("Saved"), title: savedOn ? t("Draft saved {0}", new Date(savedOn).toLocaleTimeString()) : t("All changes are saved") },
        dirty: { icon: 'cloud', text: t("Unsaved"), title: t("Changes are saved automatically in a moment") },
        saving: { icon: 'loader', text: t("Saving…"), title: t("Saving your changes") },
        error: { icon: 'alert', text: t("Not saved"), title: error || t("Saving failed. Click to retry.") },
        offline: { icon: 'cloudOff', text: t("Offline"), title: error || t("The server cannot be reached. Retrying.") },
        conflict: { icon: 'alert', text: t("Conflict"), title: t("Someone else saved this page. Choose which version to keep.") },
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
