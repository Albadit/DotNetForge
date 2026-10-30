/**
 * The insert panel (port of InsertPanel.tsx): components and ready-made blocks (the reference's DNN modules
 * section has no counterpart here). Drag an item onto the canvas to place it exactly, or click it to add it next
 * to the selection.
 */
import { jsx, useState } from "./vdom.js";
import { allComponents, onRegistryChange } from "../components/registry.js";
import { useEffect, useReducer } from "./vdom.js";
import { Icon } from "./icons.js";
import { PRESETS } from "../editor/presets.js";
import { useDocVersion, useEditor } from "./hooks.js";
const CATEGORIES = [
    { id: 'structure', label: 'Layout' },
    { id: 'content', label: 'Content' },
    { id: 'media', label: 'Media' },
];
export function InsertPanel() {
    const { canvas } = useEditor();
    const [query, setQuery] = useState('');
    const [, force] = useReducer((n) => n + 1, 0);
    useEffect(() => onRegistryChange(() => force(0)), []);
    useDocVersion();
    const q = query.trim().toLowerCase();
    const comps = allComponents().filter(c => c.schema.insertable !== false && c.type !== 'module');
    const match = (s) => !q || s.toLowerCase().includes(q);
    const start = (spec) => (e) => {
        if (e.button !== 0)
            return;
        const startX = e.clientX, startY = e.clientY;
        const move = (ev) => {
            if (Math.hypot(ev.clientX - startX, ev.clientY - startY) < 5)
                return;
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            canvas.beginInsert(spec, ev);
        };
        const up = () => {
            window.removeEventListener('pointermove', move);
            window.removeEventListener('pointerup', up);
            canvas.insertAtSelection(spec);
        };
        window.addEventListener('pointermove', move);
        window.addEventListener('pointerup', up);
    };
    const item = (key, spec, icon, label, description, extra) => (jsx("button", { key: key, type: "button", class: "pe-insert-item", title: description, onPointerDown: start(spec), onKeyDown: e => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                canvas.insertAtSelection(spec);
            }
        } },
        jsx("span", { class: "pe-insert-icon" },
            jsx(Icon, { name: icon, size: 16 })),
        jsx("span", { class: "pe-insert-label" }, label),
        extra && jsx("span", { class: "pe-insert-extra" }, extra)));
    const presets = PRESETS.filter(p => match(p.label) || match(p.description));
    const presetGroups = new Map();
    for (const p of presets) {
        const g = p.group ?? 'Blocks';
        presetGroups.set(g, [...(presetGroups.get(g) ?? []), p]);
    }
    return (jsx("div", { class: "pe-insert" },
        jsx("div", { class: "pe-search" },
            jsx(Icon, { name: "search", size: 16 }),
            jsx("input", { type: "search", placeholder: "Search components", value: query, "aria-label": "Search components", onInput: e => setQuery(e.target.value) })),
        jsx("div", { class: "pe-insert-scroll" },
            jsx("p", { class: "pe-hint pe-insert-tip" }, "Drag onto the page, or click to add next to the selection."),
            CATEGORIES.map(cat => {
                const list = comps.filter(c => (c.schema.category === cat.id || (cat.id === 'content' && !CATEGORIES.some(x => x.id === c.schema.category))) && (match(c.schema.label) || match(c.type)));
                if (!list.length)
                    return null;
                return (jsx("div", { key: cat.id, class: "pe-insert-group" },
                    jsx("h3", null, cat.label),
                    jsx("div", { class: "pe-insert-grid" }, list.map(c => item(c.type, { type: c.type, label: c.schema.label }, c.icon ?? 'frame', c.schema.label, c.description)))));
            }),
            [...presetGroups].map(([group, list]) => (jsx("div", { key: group, class: "pe-insert-group" },
                jsx("h3", null, group),
                jsx("div", { class: "pe-insert-list" }, list.map(p => {
                    const nodes = p.build();
                    return item(p.id, { type: nodes[0].type, label: p.label, nodes }, p.icon, p.label, p.description);
                }))))))));
}
