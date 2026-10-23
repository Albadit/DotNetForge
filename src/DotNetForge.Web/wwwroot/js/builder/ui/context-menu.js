/** Right-click menu for the canvas and the layers panel. */
import { jsx, useEffect, useRef } from "./vdom.js";
import { signal } from "../core/signal.js";
import { resetBreakpoint } from "../core/commands.js";
import { Icon } from "./icons.js";
import { useEditor, useSignal } from "./hooks.js";
const menu = signal(null);
export function openContextMenu(x, y) {
    menu.set({ x, y });
}
export function ContextMenu({ actions }) {
    const { session, inline } = useEditor();
    const pos = useSignal(menu);
    const ref = useRef(null);
    useEffect(() => {
        if (!pos)
            return;
        const close = (e) => {
            if (!ref.current?.contains(e.target))
                menu.set(null);
        };
        const key = (e) => e.key === 'Escape' && menu.set(null);
        window.addEventListener('pointerdown', close, true);
        window.addEventListener('keydown', key, true);
        requestAnimationFrame(() => ref.current?.querySelector('button:not([disabled])')?.focus());
        return () => {
            window.removeEventListener('pointerdown', close, true);
            window.removeEventListener('keydown', key, true);
        };
    }, [pos]);
    if (!pos)
        return null;
    const store = session.store;
    const sel = store.selection;
    const doc = store.doc;
    const first = sel[0] ? doc.nodes[sel[0]] : undefined;
    const layout = session.caps.value.editLayout;
    const isRoot = sel[0] === doc.root;
    const bp = session.breakpoint.value;
    const groups = [
        [
            { label: 'Edit text', icon: 'text', keys: 'Enter', run: () => inline.start(sel[0]), disabled: !first || !inline.canEdit(first.id) },
            { label: 'Select parent', icon: 'up', keys: 'Esc', run: () => actions.selectParent(), disabled: !first?.parent },
        ],
        [
            { label: 'Copy', icon: 'copy', keys: 'Ctrl+C', run: () => actions.copy(), disabled: !sel.length || isRoot },
            { label: 'Cut', keys: 'Ctrl+X', run: () => actions.cut(), disabled: !layout || !sel.length || isRoot },
            { label: 'Paste', keys: 'Ctrl+V', run: () => actions.paste(), disabled: !layout },
            { label: 'Duplicate', icon: 'duplicate', keys: 'Ctrl+D', run: () => actions.duplicate(), disabled: !layout || !sel.length || isRoot },
        ],
        [
            { label: 'Bring to front', keys: 'Ctrl+Shift+]', run: () => actions.moveToEnd('front'), disabled: !layout || !first?.parent },
            { label: 'Bring forward', keys: 'Ctrl+]', run: () => actions.reorder(1), disabled: !layout || !first?.parent },
            { label: 'Send backward', keys: 'Ctrl+[', run: () => actions.reorder(-1), disabled: !layout || !first?.parent },
            { label: 'Send to back', keys: 'Ctrl+Shift+[', run: () => actions.moveToEnd('back'), disabled: !layout || !first?.parent },
        ],
        [
            { label: 'Group selection', icon: 'wrap', keys: 'Ctrl+G', run: () => actions.wrap(), disabled: !layout || !sel.length || isRoot },
            { label: 'Frame selection', icon: 'frame', keys: 'Ctrl+Alt+G', run: () => actions.wrap(), disabled: !layout || !sel.length || isRoot },
            { label: 'Ungroup', icon: 'unwrap', keys: 'Ctrl+Shift+G', run: () => actions.unwrap(), disabled: !layout || first?.type !== 'container' },
            { label: 'Add auto layout', icon: 'stackV', keys: 'Shift+A', run: () => actions.addAutoLayout(), disabled: !layout || !sel.length || isRoot },
            { label: 'Remove auto layout', icon: 'free', keys: 'Alt+Shift+A', run: () => actions.removeAutoLayout(), disabled: !layout || !first?.children || first?.locked },
        ],
        [
            { label: first?.hidden ? 'Show' : 'Hide', icon: first?.hidden ? 'eye' : 'eyeOff', keys: 'Ctrl+Shift+H', run: () => actions.toggleHidden(), disabled: !layout || !sel.length || isRoot },
            { label: first?.locked ? 'Unlock' : 'Lock', icon: first?.locked ? 'unlock' : 'lock', keys: 'Ctrl+Shift+L', run: () => actions.toggleLocked(), disabled: !layout || !sel.length || isRoot },
            { label: 'Rename', keys: 'Ctrl+R', run: () => actions.rename(), disabled: !layout || !sel.length || isRoot },
            ...(bp !== 'base' ? [{ label: 'Reset ' + (bp === 'tablet' ? 'tablet' : 'mobile') + ' changes', icon: 'restore', run: () => resetBreakpoint(store, sel, bp, ['style', 'layout', 'place']), disabled: !layout || !sel.length }] : []),
        ],
        [
            { label: 'Delete', icon: 'trash', keys: 'Del', run: () => actions.remove(), disabled: !layout || !sel.length || isRoot, danger: true },
        ],
    ];
    const w = 260, h = 560;
    const left = Math.min(pos.x, window.innerWidth - w - 8);
    const top = Math.min(pos.y, window.innerHeight - h - 8);
    return (jsx("div", { ref: ref, class: "pe-menu", role: "menu", style: { left: left + 'px', top: Math.max(8, top) + 'px' }, onKeyDown: e => {
            const items = Array.from(ref.current.querySelectorAll('button:not([disabled])'));
            const i = items.indexOf(document.activeElement);
            if (e.key === 'ArrowDown')
                items[(i + 1) % items.length]?.focus();
            if (e.key === 'ArrowUp')
                items[(i - 1 + items.length) % items.length]?.focus();
        } }, groups.map((g, gi) => (jsx("div", { key: gi, class: "pe-menu-group" }, g.map(it => (jsx("button", { key: it.label, type: "button", role: "menuitem", class: it.danger ? 'is-danger' : '', disabled: it.disabled, onClick: () => {
            menu.set(null);
            it.run();
        } },
        jsx("span", { class: "pe-menu-icon" }, it.icon && jsx(Icon, { name: it.icon, size: 16 })),
        jsx("span", { class: "pe-menu-label" }, it.label),
        it.keys && jsx("kbd", null, it.keys)))))))));
}
