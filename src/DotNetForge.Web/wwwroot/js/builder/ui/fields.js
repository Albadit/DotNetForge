import { jsx, useEffect, useRef, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { cls, useDraft, useEditor } from "./hooks.js";
import * as V from "../core/values.js";
import { ColorPicker } from "./color-picker.js";
import { hex6, hexOf, parseColor, parseInput } from "./color.js";
/**
 * A panel section as in Figma's design panel: a 40px header with the title
 * and icon actions on the right; the title collapses it. `empty` sections
 * (Fill, Stroke, Effects without entries) show only their header.
 */
/** A panel section, always open as in Figma: a title, optional header buttons, the rows. */
export function Section({ title, children, actions, empty }) {
    return (jsx("section", { class: cls('pe-section', empty && 'is-empty') },
        jsx("header", { class: "pe-section-head" },
            jsx("h2", null, title),
            actions && jsx("div", { class: "pe-section-actions" }, actions)),
        !empty && jsx("div", { class: "pe-section-body" }, children)));
}
/**
 * One line of controls with its labels above, on Figma's grid: two equal
 * field columns and a 24px column for an icon button. `label2` labels the
 * second column. A single field spans both field columns; `wide` spans all.
 */
export function Row({ children, label, label2, override, onReset, wide, className }) {
    return (jsx("div", { class: cls('pe-row', wide && 'is-wide', className) },
        (label || label2 || override) && (jsx("div", { class: "pe-row-labels" },
            jsx("span", { class: "pe-row-label" },
                label,
                override && jsx(OverrideDot, { onReset: onReset })),
            label2 && jsx("span", { class: "pe-row-label" }, label2))),
        jsx("div", { class: "pe-row-controls" }, children)));
}
/**
 * Figma's scrubbing: dragging left or right changes a number (Shift ×10, Alt ×0.1), as one
 * undo step. A press without moving calls onClick instead (to type the value).
 */
export function scrubValue(e, start, onValue, onClick, step = 1) {
    if (e.button !== 0)
        return;
    e.preventDefault();
    const target = e.currentTarget;
    target.setPointerCapture?.(e.pointerId);
    const startX = e.clientX;
    const key = 'scrub' + Math.random().toString(36).slice(2);
    let moved = false;
    const move = (ev) => {
        const dx = ev.clientX - startX;
        if (Math.abs(dx) < 2 && !moved)
            return;
        moved = true;
        onValue(start + Math.round(dx / 2) * step * (ev.shiftKey ? 10 : ev.altKey ? 0.1 : 1), key);
    };
    const up = () => {
        target.removeEventListener('pointermove', move);
        target.removeEventListener('pointerup', up);
        if (!moved)
            onClick?.();
    };
    target.addEventListener('pointermove', move);
    target.addEventListener('pointerup', up);
}
/** Closes something when the pointer goes down outside `ref`, or on Escape. */
export function useDismiss(ref, open, close) {
    useEffect(() => {
        if (!open)
            return;
        const down = (e) => {
            if (!ref.current?.contains(e.target))
                close();
        };
        const key = (e) => {
            if (e.key === 'Escape') {
                e.stopPropagation();
                close();
            }
        };
        window.addEventListener('pointerdown', down, true);
        window.addEventListener('keydown', key, true);
        return () => {
            window.removeEventListener('pointerdown', down, true);
            window.removeEventListener('keydown', key, true);
        };
    }, [open]);
}
/** Figma's dark dropdown menu: check marks for the current choice, separators between groups. */
export function DropMenu({ items, onClose, align = 'left' }) {
    const ref = useRef(null);
    // The focused item is highlighted only once the keyboard is used (like hover otherwise).
    const [kbd, setKbd] = useState(false);
    // Opens on the checked item, so the keyboard continues from the current value.
    useEffect(() => {
        const el = ref.current?.querySelector('[aria-checked="true"]') ?? ref.current?.querySelector('.pe-dmenu-item:not(:disabled)');
        el?.focus({ preventScroll: true });
        // Scroll only the menu's own list (scrollIntoView would also scroll the panel sideways).
        const menu = ref.current;
        if (el && menu && menu.scrollHeight > menu.clientHeight)
            menu.scrollTop = Math.max(0, el.offsetTop - menu.clientHeight / 2);
    }, []);
    const keys = (e) => {
        if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Home' && e.key !== 'End')
            return;
        e.preventDefault();
        setKbd(true);
        const all = [...(ref.current?.querySelectorAll('.pe-dmenu-item:not(:disabled)') ?? [])];
        const at = all.indexOf(document.activeElement);
        const next = e.key === 'Home' ? 0 : e.key === 'End' ? all.length - 1 : (at + (e.key === 'ArrowDown' ? 1 : -1) + all.length) % all.length;
        all[next]?.focus();
    };
    return (jsx("div", { ref: ref, class: cls('pe-dmenu', align === 'right' && 'is-right', kbd && 'is-kbd'), role: "menu", onKeyDown: keys }, items.map((it, i) => it === 'sep'
        ? jsx("div", { key: i, class: "pe-dmenu-sep", role: "separator" })
        : (jsx("button", { key: i, type: "button", role: "menuitemradio", "aria-checked": !!it.checked, class: "pe-dmenu-item", disabled: it.disabled, onClick: () => {
                onClose();
                it.run();
            } },
            jsx("span", { class: "pe-dmenu-check" }, it.checked && jsx(Icon, { name: "check", size: 12 })),
            it.icon && jsx(Icon, { name: it.icon, size: 16 }),
            jsx("span", null, it.label))))));
}
/** A 24px icon button; `on` shows it pressed (a toggle). */
export function IconButton({ icon, title, on, disabled, onClick }) {
    return (jsx("button", { type: "button", class: cls('pe-icon-btn', 'is-sm', on && 'is-on'), title: title, "aria-label": title, "aria-pressed": on === undefined ? undefined : on, disabled: disabled, onClick: onClick },
        jsx(Icon, { name: icon, size: 16 })));
}
/** An icon button that opens a small panel (settings popovers of the design panel). */
export function PopoverButton({ icon, title, children, wide }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    return (jsx("div", { class: "pe-pop-wrap", ref: ref },
        jsx(IconButton, { icon: icon, title: title, on: open, onClick: () => setOpen(!open) }),
        open && (jsx("div", { class: cls('pe-popover', 'pe-pop', wide && 'is-wide'), role: "dialog", "aria-label": title },
            jsx("div", { class: "pe-pop-head" },
                jsx("strong", null, title),
                jsx(IconButton, { icon: "close", title: "Close", onClick: () => setOpen(false) })),
            jsx("div", { class: "pe-pop-body" }, children)))));
}
function OverrideDot({ onReset }) {
    return (jsx("button", { type: "button", class: "pe-override", title: "Set for this breakpoint. Click to use the larger breakpoint's value again.", onClick: onReset, "aria-label": "Reset to inherited value" }));
}
export function NumberField({ label, icon, title, value, placeholder, unit, min = -100000, max = 100000, step = 1, decimals = 2, disabled, text, menu, words, suffix, onChange }) {
    // Symbols are part of the text, as Figma's "0°" and "100%"; words (px, col) sit after it.
    const inline = unit === '°' || unit === '%' || unit === 'fr' ? unit : '';
    const display = value === undefined ? (text ?? '') : String(value) + inline;
    const [menuOpen, setMenuOpen] = useState(false);
    const box = useRef(null);
    useDismiss(box, menuOpen, () => setMenuOpen(false));
    const [draft, setDraft, reset] = useDraft(display);
    const input = useRef(null);
    const clamp = (n) => {
        const f = Math.pow(10, decimals);
        return Math.min(max, Math.max(min, Math.round(n * f) / f));
    };
    const commit = () => {
        let t = draft.trim();
        if (t === display)
            return;
        if (inline && t.endsWith(inline))
            t = t.slice(0, -inline.length).trim();
        if (t === '') {
            onChange(undefined);
            return;
        }
        const word = words?.[t.toLowerCase()];
        if (word) {
            word();
            return;
        }
        // Simple arithmetic like "16*2" or "100-8" is allowed, as in design tools.
        const expr = /^[-+*/().\d\s]+$/.test(t) ? safeEval(t) : NaN;
        const n = Number.isFinite(expr) ? expr : parseFloat(t);
        if (Number.isFinite(n))
            onChange(clamp(n));
        else
            reset();
    };
    const scrub = (e) => {
        if (disabled || e.button !== 0)
            return;
        e.preventDefault();
        const startX = e.clientX;
        const start = value ?? (typeof placeholder === 'number' ? placeholder : 0);
        const target = e.currentTarget;
        target.setPointerCapture(e.pointerId);
        const key = 'scrub' + Math.random().toString(36).slice(2);
        let moved = false;
        const move = (ev) => {
            const dx = ev.clientX - startX;
            if (Math.abs(dx) < 2 && !moved)
                return;
            moved = true;
            const factor = ev.shiftKey ? 10 : ev.altKey ? 0.1 : 1;
            onChange(clamp(start + Math.round(dx / 2) * step * factor), key);
        };
        const up = () => {
            target.removeEventListener('pointermove', move);
            target.removeEventListener('pointerup', up);
            if (!moved)
                input.current?.focus();
        };
        target.addEventListener('pointermove', move);
        target.addEventListener('pointerup', up);
    };
    const key = (e) => {
        if (e.key === 'Enter') {
            commit();
            e.target.select();
        }
        else if (e.key === 'Escape') {
            reset();
            e.target.blur();
        }
        else if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
            e.preventDefault();
            const base = value ?? (typeof placeholder === 'number' ? placeholder : 0);
            const n = clamp(base + (e.key === 'ArrowUp' ? 1 : -1) * step * (e.shiftKey ? 10 : 1));
            onChange(n, 'arrows');
        }
    };
    return (jsx("label", { ref: box, class: cls('pe-num', disabled && 'is-disabled', !!menu && 'has-menu', !!suffix && 'has-suffix', text !== undefined && value === undefined && 'is-text'), title: title },
        (icon || label) && (jsx("span", { class: "pe-num-label", onPointerDown: e => scrub(e) }, icon ? jsx(Icon, { name: icon, size: 16 }) : label)),
        jsx("input", { ref: input, type: "text", inputMode: "decimal", value: draft, disabled: disabled, "aria-label": title ?? label, placeholder: placeholder === undefined ? '' : String(placeholder) + (typeof placeholder === 'number' ? inline : ''), onInput: e => setDraft(e.target.value), onBlur: commit, onKeyDown: key, onFocus: e => e.target.select() }),
        unit && !inline && jsx("span", { class: "pe-num-unit" }, unit),
        suffix && jsx("span", { class: "pe-num-suffix" }, suffix),
        menu && (jsx("button", { type: "button", class: "pe-num-menu", disabled: disabled, "aria-haspopup": "menu", "aria-expanded": menuOpen, "aria-label": (title ?? label ?? 'Value') + ' options', onClick: e => {
                e.preventDefault();
                setMenuOpen(!menuOpen);
            } },
            jsx(Icon, { name: "chevronDown", size: 12 }))),
        menuOpen && menu && jsx(DropMenu, { items: menu, onClose: () => setMenuOpen(false) })));
}
/** Evaluates + - * / and parentheses (a tiny recursive-descent parser; no eval). NaN when invalid. */
function safeEval(expr) {
    const s = expr.replace(/\s+/g, '');
    let i = 0;
    const num = () => {
        if (s[i] === '(') {
            i++;
            const v = sum();
            if (s[i] !== ')')
                return NaN;
            i++;
            return v;
        }
        if (s[i] === '-') {
            i++;
            return -num();
        }
        const m = /^\d+(\.\d*)?|^\.\d+/.exec(s.slice(i));
        if (!m)
            return NaN;
        i += m[0].length;
        return parseFloat(m[0]);
    };
    const product = () => {
        let v = num();
        while (s[i] === '*' || s[i] === '/') {
            const op = s[i++];
            const r = num();
            v = op === '*' ? v * r : v / r;
        }
        return v;
    };
    const sum = () => {
        let v = product();
        while (s[i] === '+' || s[i] === '-') {
            const op = s[i++];
            const r = product();
            v = op === '+' ? v + r : v - r;
        }
        return v;
    };
    const result = sum();
    return i === s.length && Number.isFinite(result) ? result : NaN;
}
/** A dropdown in the editor's own menu (themed, like the size menus), not the browser's list. */
export function SelectField({ value, options, onChange, placeholder, disabled, title }) {
    const [open, setOpen] = useState(false);
    const wrap = useRef(null);
    const button = useRef(null);
    useDismiss(wrap, open, () => setOpen(false));
    const current = options.find(o => o.value === value);
    // The placeholder (the inherited value) is its own item only when no option already says the same.
    const inherited = value === undefined ? options.find(o => o.label === placeholder) : undefined;
    const items = [
        ...(placeholder !== undefined && !options.some(o => o.label === placeholder) ? [{ label: placeholder, checked: value === undefined, run: () => onChange(undefined) }] : []),
        ...options.map(o => ({ label: o.label, checked: o.value === value || o === inherited, run: () => onChange(o.value) })),
    ];
    // Arrow keys on the closed field step through the values, as a native select does.
    const step = (e) => {
        if (open || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp'))
            return;
        e.preventDefault();
        const at = options.findIndex(o => o.value === value);
        const next = options[Math.min(options.length - 1, Math.max(0, at + (e.key === 'ArrowDown' ? 1 : -1)))];
        if (next && next.value !== value)
            onChange(next.value);
    };
    return (jsx("div", { class: "pe-select-wrap", ref: wrap },
        jsx("button", { ref: button, type: "button", class: cls('pe-select', !current && 'is-placeholder'), disabled: disabled, title: title, "aria-label": title, "aria-haspopup": "menu", "aria-expanded": open, onClick: () => setOpen(!open), onKeyDown: step },
            jsx("span", { class: "pe-select-text" }, current?.label ?? placeholder ?? '')),
        open && jsx(DropMenu, { items: items, onClose: () => {
                setOpen(false);
                button.current?.focus();
            } })));
}
export function Segmented({ value, options, onChange, disabled, inherited }) {
    const current = value ?? inherited;
    return (jsx("div", { class: cls('pe-seg', disabled && 'is-disabled'), role: "radiogroup" }, options.map(o => (jsx("button", { key: o.value, type: "button", role: "radio", "aria-checked": current === o.value, title: o.title, disabled: disabled, class: cls('pe-seg-btn', current === o.value && 'is-on', value === undefined && current === o.value && 'is-inherited'), onClick: () => onChange(o.value) }, o.icon ? jsx(Icon, { name: o.icon, size: 16 }) : o.label)))));
}
/** A checkbox with its label, as Figma's "Clip content". */
export function Toggle({ checked, onChange, label, disabled }) {
    return (jsx("label", { class: cls('pe-toggle', disabled && 'is-disabled') },
        jsx("input", { type: "checkbox", checked: checked, disabled: disabled, onChange: e => onChange(e.target.checked) }),
        jsx("span", { class: "pe-toggle-box", "aria-hidden": "true" }, checked && jsx(Icon, { name: "check", size: 12 })),
        jsx("span", null, label)));
}
export function TextField({ value, onChange, placeholder, validate, multiline, rows = 3, disabled, maxLength, ariaLabel, mono }) {
    const [draft, setDraft, reset] = useDraft(value);
    const error = validate && draft !== '' ? validate(draft) : null;
    const commit = () => {
        if (draft === value || error)
            return;
        onChange(draft);
    };
    const props = {
        class: cls('pe-input', error && 'is-invalid', mono && 'is-mono'),
        value: draft,
        placeholder,
        disabled,
        maxLength,
        'aria-label': ariaLabel,
        'aria-invalid': error ? true : undefined,
        onInput: (e) => setDraft(e.target.value),
        onBlur: commit,
        onKeyDown: (e) => {
            if (e.key === 'Enter' && (!multiline || e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                commit();
            }
            else if (e.key === 'Escape')
                reset();
        },
    };
    return (jsx("div", { class: "pe-field" },
        multiline ? jsx("textarea", { rows: rows, ...props }) : jsx("input", { type: "text", ...props }),
        error && jsx("div", { class: "pe-field-error", role: "alert" }, error)));
}
/**
 * Figma's color field: swatch, hex (without #) and opacity in one box. The
 * swatch opens the color picker beside the design panel. Theme colors
 * (var(--…)) show as text and have no opacity of their own.
 */
export function ColorField({ value, placeholder, onChange, disabled }) {
    const [open, setOpen] = useState(false);
    const root = useRef(null);
    const shown = value ?? placeholder;
    const rgba = parseColor(shown);
    const text = (c) => {
        const p = parseColor(c);
        return p ? hex6(p) : c ?? '';
    };
    const alpha = rgba ? Math.round(rgba.a * 100) : 100;
    const alphaInput = useRef(null);
    const setAlpha = (n, merge) => rgba && onChange(hexOf({ ...rgba, a: Math.min(100, Math.max(0, Math.round(n))) / 100 }), merge);
    return (jsx("div", { class: cls('pe-color', open && 'is-open', disabled && 'is-disabled'), ref: root },
        jsx("button", { type: "button", class: "pe-swatch", disabled: disabled, "aria-label": "Choose color", "aria-expanded": open, onClick: () => setOpen(!open) },
            jsx("span", { class: cls(!shown && 'is-none') },
                jsx("span", { style: { background: shown ?? 'transparent' } }))),
        jsx(TextField, { value: value === undefined ? '' : text(value), placeholder: placeholder ? text(placeholder) : 'None', disabled: disabled, ariaLabel: "Color value", validate: v => (parseInput(v) !== null || V.color(v) ? null : 'Use a hex color (FF0000), rgb(), hsl() or var(--name).'), onChange: v => {
                const hex = parseInput(v, rgba?.a ?? 1);
                onChange(hex !== null ? hex : V.color(v) ?? undefined);
            } }),
        jsx("label", { class: cls('pe-color-alpha', !disabled && rgba && 'is-scrub'), title: "Opacity: drag left or right, or click to type", onPointerDown: e => {
                if (disabled || !rgba || document.activeElement === alphaInput.current)
                    return;
                scrubValue(e, alpha, setAlpha, () => {
                    alphaInput.current?.focus();
                    alphaInput.current?.select();
                });
            } },
            jsx("input", { ref: alphaInput, type: "text", inputMode: "numeric", "aria-label": "Opacity", value: alpha, key: alpha, disabled: disabled || !rgba, onKeyDown: e => e.key === 'Enter' && e.target.blur(), onBlur: e => {
                    const n = parseFloat(e.target.value);
                    if (rgba && Number.isFinite(n) && Math.round(n) !== alpha)
                        onChange(hexOf({ ...rgba, a: Math.min(100, Math.max(0, n)) / 100 }));
                } }),
            jsx("span", null, "%")),
        open && jsx(ColorPicker, { value: shown, anchor: root.current, onChange: onChange, onClose: () => setOpen(false) })));
}
/** A page icon as the visitor sees it (same markup as the renderers). */
function PageIcon({ name, size = 20 }) {
    const body = Object.prototype.hasOwnProperty.call(V.PAGE_ICONS, name) ? V.PAGE_ICONS[name] : '';
    return (jsx("svg", { class: "pe-icon", width: size, height: size, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", "stroke-width": "2", "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true", dangerouslySetInnerHTML: { __html: body } }));
}
/** Picks one of the page icons (schema/icons.json); `allowNone` adds "No icon". */
export function IconField({ value, onChange, disabled, allowNone = true }) {
    const [open, setOpen] = useState(false);
    const [query, setQuery] = useState('');
    const root = useRef(null);
    useEffect(() => {
        if (!open)
            return;
        const close = (e) => {
            if (!root.current?.contains(e.target))
                setOpen(false);
        };
        window.addEventListener('pointerdown', close, true);
        return () => window.removeEventListener('pointerdown', close, true);
    }, [open]);
    const q = query.trim().toLowerCase();
    const names = Object.keys(V.PAGE_ICONS).filter(n => !q || n.includes(q));
    const pick = (name) => {
        onChange(name);
        setOpen(false);
    };
    return (jsx("div", { class: "pe-iconfield", ref: root },
        jsx("button", { type: "button", class: "pe-btn pe-iconfield-btn", disabled: disabled, "aria-haspopup": "dialog", "aria-expanded": open, onClick: () => setOpen(!open) },
            value ? jsx(PageIcon, { name: value }) : jsx("span", { class: "pe-iconfield-none" }),
            jsx("span", { class: "pe-iconfield-name" }, value ? value.replace(/-/g, ' ') : 'No icon'),
            jsx(Icon, { name: "chevronDown", size: 16 })),
        open && (jsx("div", { class: "pe-popover pe-iconfield-pop", role: "dialog", "aria-label": "Choose an icon" },
            jsx("div", { class: "pe-search" },
                jsx(Icon, { name: "search", size: 16 }),
                jsx("input", { type: "search", placeholder: "Search icons", value: query, "aria-label": "Search icons", onInput: e => setQuery(e.target.value) })),
            jsx("div", { class: "pe-iconfield-grid" }, names.map(n => (jsx("button", { key: n, type: "button", title: n.replace(/-/g, ' '), "aria-label": n.replace(/-/g, ' '), class: cls('pe-iconfield-cell', n === value && 'is-on'), onClick: () => pick(n) },
                jsx(PageIcon, { name: n, size: 22 }))))),
            allowNone && jsx("button", { type: "button", class: "pe-btn-text", onClick: () => pick('') }, "No icon")))));
}
/** An image address with a Browse button that opens the site's files. */
export function ImageInput({ value, onChange, disabled, placeholder, preview }) {
    const { session } = useEditor();
    return (jsx("div", { class: "pe-image-input" },
        preview && value && jsx("img", { class: "pe-image-preview", src: value, alt: "" }),
        jsx("div", { class: "pe-image-row" },
            jsx(TextField, { value: value, disabled: disabled, placeholder: placeholder ?? 'https://… or /Portals/…', validate: v => (V.imageUrl(v) === null ? 'Use an https:// address or a path on this site.' : null), onChange: v => onChange(V.imageUrl(v) ?? '') }),
            jsx("button", { type: "button", class: "pe-btn", disabled: disabled, onClick: () => session.dialog.set({ kind: 'files', onPick: url => onChange(url) }) },
                jsx(Icon, { name: "folder", size: 16 }),
                " Browse"))));
}
