/** The editor's layout: top bar, layers/insert on the left, canvas, inspector on the right. */
import { jsx, useEffect, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { Actions } from "../editor/actions.js";
import { installShortcuts } from "../editor/shortcuts.js";
import { CanvasView } from "./canvas.js";
import { ContextMenu } from "./context-menu.js";
import { Dialogs, Toasts } from "./dialogs.js";
import { Inspector } from "./inspector.js";
import { InsertPanel } from "./insert-panel.js";
import { LayersPanel } from "./layers.js";
import { TopBar } from "./top-bar.js";
import { cls, EditorContext, useSignal } from "./hooks.js";
/** Panel widths: default, smallest and largest (px). Remembered per browser. */
const PANELS = { left: [240, 200, 480], right: [276, 240, 480] };
function storedWidth(side) {
    try {
        const n = Number(localStorage.getItem('pe.' + side + 'Width'));
        const [def, min, max] = PANELS[side];
        return Number.isFinite(n) && n >= min && n <= max ? n : def;
    }
    catch {
        return PANELS[side][0];
    }
}
/** The drag strip on a panel's inner edge; double-click restores the default width. */
function Resizer({ side, width, onWidth }) {
    const [def, min, max] = PANELS[side];
    const save = (w) => {
        try {
            localStorage.setItem('pe.' + side + 'Width', String(w));
        }
        catch {
            /* storage unavailable */
        }
    };
    const down = (e) => {
        if (e.button !== 0)
            return;
        e.preventDefault();
        const el = e.currentTarget;
        el.setPointerCapture(e.pointerId);
        const startX = e.clientX;
        let w = width;
        const move = (ev) => {
            const dx = ev.clientX - startX;
            w = Math.round(Math.min(max, Math.max(min, width + (side === 'left' ? dx : -dx))));
            onWidth(w);
        };
        const up = () => {
            el.removeEventListener('pointermove', move);
            el.removeEventListener('pointerup', up);
            save(w);
        };
        el.addEventListener('pointermove', move);
        el.addEventListener('pointerup', up);
    };
    return (jsx("div", { class: cls('pe-resizer', 'is-' + side), role: "separator", "aria-orientation": "vertical", "aria-label": (side === 'left' ? 'Layers' : 'Properties') + ' panel width', "aria-valuenow": width, "aria-valuemin": min, "aria-valuemax": max, title: "Drag to resize, double-click to reset", onPointerDown: e => down(e), onDblClick: () => {
            onWidth(def);
            save(def);
        } }));
}
export function App({ ctx, onClose }) {
    const { session } = ctx;
    const [actions] = useState(() => new Actions(session, ctx.canvas, ctx.inline));
    const status = useSignal(session.status);
    const loadError = useSignal(session.loadError);
    const leftTab = useSignal(session.leftTab);
    const caps = useSignal(session.caps);
    const [leftOpen, setLeftOpen] = useState(true);
    const [leftWidth, setLeftWidth] = useState(() => storedWidth('left'));
    const [rightWidth, setRightWidth] = useState(() => storedWidth('right'));
    useEffect(() => installShortcuts(session, actions), []);
    const theme = useSignal(session.theme);
    useEffect(() => {
        // The theme is an attribute of the editor's host element (dark tokens in editor.css).
        const host = document.getElementById('pe-editor-root');
        if (!host)
            return;
        const media = typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null;
        const apply = () => {
            host.dataset.theme = theme === 'system' ? (media?.matches ? 'dark' : 'light') : theme;
        };
        apply();
        if (theme !== 'system' || !media)
            return;
        media.addEventListener('change', apply);
        return () => media.removeEventListener('change', apply);
    }, [theme]);
    if (status === 'loading') {
        return jsx("div", { class: "pe-app pe-app-loading", role: "status" },
            jsx(Icon, { name: "loader", size: 22, class: "pe-spin" }),
            " Opening the editor\u2026");
    }
    if (status === 'error') {
        return (jsx("div", { class: "pe-app pe-app-loading", role: "alert" },
            jsx(Icon, { name: "alert", size: 22 }),
            jsx("div", null,
                jsx("strong", null, "The editor could not be opened."),
                jsx("p", null, loadError),
                jsx("button", { type: "button", class: "pe-btn", onClick: onClose }, "Close"),
                jsx("button", { type: "button", class: "pe-btn pe-btn-primary", onClick: () => void session.load() }, "Try again"))));
    }
    return (jsx(EditorContext.Provider, { value: ctx },
        jsx("div", { class: cls('pe-app', !leftOpen && 'no-left'), style: { gridTemplateColumns: `${leftOpen ? leftWidth : 0}px minmax(0, 1fr) ${rightWidth}px` } },
            jsx(TopBar, { actions: actions, onClose: onClose }),
            jsx("aside", { class: "pe-left", "aria-label": "Layers and components" },
                jsx("div", { class: "pe-tabs pe-left-tabs", role: "tablist" },
                    jsx("button", { type: "button", role: "tab", "aria-selected": leftTab === 'layers', class: cls('pe-tab', leftTab === 'layers' && 'is-on'), onClick: () => session.leftTab.set('layers') },
                        jsx(Icon, { name: "layers", size: 16 }),
                        " Layers"),
                    caps.editLayout && (jsx("button", { type: "button", role: "tab", "aria-selected": leftTab === 'insert', class: cls('pe-tab', leftTab === 'insert' && 'is-on'), onClick: () => session.leftTab.set('insert') },
                        jsx(Icon, { name: "plus", size: 16 }),
                        " Insert")),
                    jsx("button", { type: "button", class: "pe-icon-btn pe-collapse", title: "Hide panel", "aria-label": "Hide left panel", onClick: () => setLeftOpen(false) },
                        jsx(Icon, { name: "chevronLeft", size: 16 }))),
                leftTab === 'insert' && caps.editLayout ? jsx(InsertPanel, null) : jsx(LayersPanel, null),
                jsx(Resizer, { side: "left", width: leftWidth, onWidth: setLeftWidth })),
            !leftOpen && jsx("button", { type: "button", class: "pe-reopen is-left", "aria-label": "Show left panel", onClick: () => setLeftOpen(true) },
                jsx(Icon, { name: "sidebar", size: 16 })),
            jsx("main", { class: "pe-center" },
                jsx(CanvasView, null)),
            jsx("aside", { class: "pe-right", "aria-label": "Properties" },
                jsx(Resizer, { side: "right", width: rightWidth, onWidth: setRightWidth }),
                jsx(Inspector, null)),
            jsx(ContextMenu, { actions: actions }),
            jsx(Dialogs, null),
            jsx(Toasts, null))));
}
