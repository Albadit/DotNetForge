/**
 * Figma's color picker: a panel beside the design panel, level with the field
 * that opened it. Saturation/brightness square, hue and opacity sliders,
 * eyedropper (where the browser has one), hex + opacity, then the colors used
 * on this page and the site's theme colors.
 */
import { jsx, useEffect, useLayoutEffect, useRef, useState } from "./vdom.js";
import { Icon } from "./icons.js";
import { cls, useEditor } from "./hooks.js";
import { THEME_COLORS, cssOf, fromHsl, fromHsv, hex6, hexOf, parseColor, parseInput, toHsl, toHsv } from "./color.js";
import { DropMenu, scrubValue, useDismiss } from "./fields.js";
const WIDTH = 240;
/** Up to 18 distinct colors the page already uses (fills, text, strokes). */
function pageColors(nodes) {
    const out = new Set();
    for (const n of Object.values(nodes)) {
        for (const s of Object.values(n.style ?? {})) {
            for (const k of ['background', 'color', 'borderColor']) {
                const v = s?.[k];
                if (typeof v === 'string' && !/^var\(/.test(v) && parseColor(v))
                    out.add(v.toLowerCase());
            }
        }
        if (out.size >= 18)
            break;
    }
    return [...out].slice(0, 18);
}
/** Drag inside `el`: reports the pointer as 0..1 fractions of its box. */
function drag(e, fn) {
    const el = e.currentTarget;
    const r = el.getBoundingClientRect();
    const at = (ev) => fn(Math.min(1, Math.max(0, (ev.clientX - r.left) / r.width)), Math.min(1, Math.max(0, (ev.clientY - r.top) / r.height)));
    e.preventDefault();
    el.setPointerCapture(e.pointerId);
    at(e);
    const move = (ev) => at(ev);
    const up = () => {
        el.removeEventListener('pointermove', move);
        el.removeEventListener('pointerup', up);
    };
    el.addEventListener('pointermove', move);
    el.addEventListener('pointerup', up);
}
export function ColorPicker({ value, anchor, onChange, onClose }) {
    const { session } = useEditor();
    const ref = useRef(null);
    const [pos, setPos] = useState(null);
    const [hsv, setHsv] = useState(() => toHsv(parseColor(value) ?? { r: 0, g: 0, b: 0, a: 1 }));
    const sent = useRef(value);
    // A change from outside (typing in the field, undo) moves the handles; the hue of greys is kept.
    useEffect(() => {
        if (value === sent.current)
            return;
        sent.current = value;
        const c = parseColor(value);
        if (c)
            setHsv(h => {
                const next = toHsv(c);
                return next.s === 0 || next.v === 0 ? { ...next, h: h.h } : next;
            });
    }, [value]);
    const emit = (next) => {
        setHsv(next);
        const v = hexOf(fromHsv(next));
        sent.current = v;
        onChange(v, 'picker');
    };
    // Beside the design panel (Figma), level with the field; kept inside the window.
    useLayoutEffect(() => {
        const panel = anchor?.closest('.pe-right') ?? anchor;
        if (!anchor || !panel || !ref.current)
            return;
        const a = anchor.getBoundingClientRect();
        const p = panel.getBoundingClientRect();
        const h = ref.current.offsetHeight;
        setPos({ left: Math.max(8, p.left - WIDTH - 8), top: Math.max(8, Math.min(a.top - 8, window.innerHeight - h - 8)) });
    }, []);
    useEffect(() => {
        const down = (e) => {
            const t = e.target;
            if (!ref.current?.contains(t) && !anchor?.contains(t))
                onClose();
        };
        // Escape closes only the picker (captured first, so the editor's Escape shortcut does not also select the
        // parent - a fix to the reference).
        const key = (e) => {
            if (e.key !== 'Escape')
                return;
            e.stopPropagation();
            onClose();
        };
        window.addEventListener('pointerdown', down, true);
        window.addEventListener('keydown', key, true);
        return () => {
            window.removeEventListener('pointerdown', down, true);
            window.removeEventListener('keydown', key, true);
        };
    }, [anchor]);
    const rgb = fromHsv({ ...hsv, a: 1 });
    const pure = hexOf(fromHsv({ h: hsv.h, s: 1, v: 1, a: 1 }));
    const solid = hexOf(rgb);
    const eyedropper = typeof window !== 'undefined' && 'EyeDropper' in window;
    const pick = async () => {
        try {
            const Picker = window.EyeDropper;
            const r = await new Picker().open();
            const c = parseColor(r.sRGBHex);
            if (c)
                emit(toHsv({ ...c, a: hsv.a }));
        }
        catch {
            /* cancelled */
        }
    };
    const pct = useRef(null);
    const [format, setFormatState] = useState(() => {
        try {
            const f = localStorage.getItem('pe.colorFormat');
            return f && FORMATS.includes(f) ? f : 'Hex';
        }
        catch {
            return 'Hex';
        }
    });
    const setFormat = (f) => {
        setFormatState(f);
        try {
            localStorage.setItem('pe.colorFormat', f);
        }
        catch {
            /* storage unavailable */
        }
    };
    const used = pageColors(session.store.doc.nodes);
    const swatch = (color, title) => (jsx("button", { key: color, type: "button", title: title, "aria-label": title, class: cls('pe-cp-swatch', value === color && 'is-on'), onClick: () => onChange(color) },
        jsx("span", { style: { background: color } })));
    return (jsx("div", { ref: ref, class: "pe-cp", role: "dialog", "aria-label": "Color", style: pos ? { left: pos.left + 'px', top: pos.top + 'px' } : { visibility: 'hidden' } },
        jsx("header", { class: "pe-cp-head" },
            jsx("span", { class: "pe-cp-tab" }, "Custom"),
            jsx("button", { type: "button", class: "pe-icon-btn is-sm", title: "Close", "aria-label": "Close", onClick: onClose },
                jsx(Icon, { name: "close", size: 16 }))),
        jsx("div", { class: "pe-cp-sv", style: { background: pure }, onPointerDown: e => drag(e, (x, y) => emit({ ...hsv, s: x, v: 1 - y })) },
            jsx("span", { class: "pe-cp-knob", style: { left: hsv.s * 100 + '%', top: (1 - hsv.v) * 100 + '%', background: solid } })),
        jsx("div", { class: "pe-cp-sliders" },
            jsx("button", { type: "button", class: "pe-icon-btn is-sm", title: "Pick a color from the screen", "aria-label": "Eyedropper", disabled: !eyedropper, onClick: () => void pick() },
                jsx(Icon, { name: "eyedropper", size: 16 })),
            jsx("div", { class: "pe-cp-bars" },
                jsx("div", { class: "pe-cp-hue", role: "slider", "aria-label": "Hue", "aria-valuemin": 0, "aria-valuemax": 360, "aria-valuenow": Math.round(hsv.h), onPointerDown: e => drag(e, x => emit({ ...hsv, h: x * 359.9 })) },
                    jsx("span", { class: "pe-cp-knob", style: { left: (hsv.h / 360) * 100 + '%', background: pure } })),
                jsx("div", { class: "pe-cp-alpha", role: "slider", "aria-label": "Opacity", "aria-valuemin": 0, "aria-valuemax": 100, "aria-valuenow": Math.round(hsv.a * 100), onPointerDown: e => drag(e, x => emit({ ...hsv, a: Math.round(x * 100) / 100 })) },
                    jsx("span", { class: "pe-cp-alpha-fill", style: { background: `linear-gradient(90deg, transparent, ${solid})` } }),
                    jsx("span", { class: "pe-cp-knob", style: { left: hsv.a * 100 + '%', background: solid } })))),
        jsx("div", { class: "pe-cp-values" },
            jsx(FormatButton, { format: format, onChange: setFormat }),
            jsx(ValueFields, { format: format, hsv: hsv, onChange: emit }),
            jsx("label", { class: "pe-cp-pct is-scrub", title: "Opacity: drag left or right, or click to type", onPointerDown: e => {
                    if (document.activeElement === pct.current)
                        return;
                    scrubValue(e, Math.round(hsv.a * 100), n => emit({ ...hsv, a: Math.min(100, Math.max(0, Math.round(n))) / 100 }), () => {
                        pct.current?.focus();
                        pct.current?.select();
                    });
                } },
                jsx("input", { ref: pct, class: "pe-cp-input", "aria-label": "Opacity", value: Math.round(hsv.a * 100), key: 'a' + Math.round(hsv.a * 100), onKeyDown: e => e.key === 'Enter' && e.target.blur(), onBlur: e => {
                        const n = parseFloat(e.target.value);
                        if (Number.isFinite(n))
                            emit({ ...hsv, a: Math.min(100, Math.max(0, n)) / 100 });
                    } }),
                jsx("span", { class: "pe-cp-unit" }, "%"))),
        used.length > 0 && (jsx("section", { class: "pe-cp-group" },
            jsx("h3", null, "On this page"),
            jsx("div", { class: "pe-cp-grid" }, used.map(c => swatch(c, c))))),
        jsx("section", { class: "pe-cp-group" },
            jsx("h3", null, "Site colors"),
            jsx("div", { class: "pe-cp-grid" }, THEME_COLORS.map(c => swatch(`var(--dnn-color-${c})`, c))))));
}
const FORMATS = ['Hex', 'RGB', 'CSS', 'HSL', 'HSB'];
/** Figma's color format menu (Hex, RGB, CSS, HSL, HSB); the choice is remembered. */
function FormatButton({ format, onChange }) {
    const [open, setOpen] = useState(false);
    const ref = useRef(null);
    useDismiss(ref, open, () => setOpen(false));
    return (jsx("div", { class: "pe-cp-format", ref: ref },
        jsx("button", { type: "button", class: "pe-cp-format-btn", "aria-haspopup": "menu", "aria-expanded": open, title: "Color format", onClick: () => setOpen(!open) },
            format,
            jsx(Icon, { name: "chevronDown", size: 12 })),
        open && jsx(DropMenu, { items: FORMATS.map(f => ({ label: f, checked: f === format, run: () => onChange(f) })), onClose: () => setOpen(false) })));
}
/** One number box of a format (R, G, B, H, S, L…); typing commits on Enter or leaving the box. */
function Channel({ label, value, max, onChange }) {
    const shown = Math.round(value);
    return (jsx("input", { class: "pe-cp-input pe-cp-channel", "aria-label": label, title: label, value: shown, key: label + shown, onKeyDown: e => e.key === 'Enter' && e.target.blur(), onBlur: e => {
            const n = parseFloat(e.target.value);
            if (Number.isFinite(n) && Math.round(n) !== shown)
                onChange(Math.min(max, Math.max(0, n)));
        } }));
}
/** The color in the chosen format; every box writes the whole color back. */
function ValueFields({ format, hsv, onChange }) {
    const rgb = fromHsv(hsv);
    const fromRgb = (r, g, b) => onChange({ ...toHsv({ r, g, b, a: hsv.a }), h: hsv.h });
    if (format === 'Hex' || format === 'CSS') {
        const text = format === 'Hex' ? hex6(rgb) : cssOf(rgb);
        return (jsx("input", { class: cls('pe-cp-input', format === 'Hex' && 'is-mono'), "aria-label": format, value: text, key: format + text, onKeyDown: e => e.key === 'Enter' && e.target.blur(), onBlur: e => {
                const t = e.target.value;
                const c = format === 'Hex' ? parseColor(parseInput(t, hsv.a) ?? undefined) : parseColor(t);
                if (c)
                    onChange(toHsv(format === 'Hex' ? { ...c, a: hsv.a } : c));
            } }));
    }
    if (format === 'RGB') {
        return (jsx("div", { class: "pe-cp-channels" },
            jsx(Channel, { label: "Red", value: rgb.r, max: 255, onChange: n => fromRgb(n, rgb.g, rgb.b) }),
            jsx(Channel, { label: "Green", value: rgb.g, max: 255, onChange: n => fromRgb(rgb.r, n, rgb.b) }),
            jsx(Channel, { label: "Blue", value: rgb.b, max: 255, onChange: n => fromRgb(rgb.r, rgb.g, n) })));
    }
    if (format === 'HSL') {
        const hsl = toHsl(rgb);
        const set = (h, sat, l) => onChange({ ...toHsv(fromHsl(h, sat, l, hsv.a)), h });
        return (jsx("div", { class: "pe-cp-channels" },
            jsx(Channel, { label: "Hue", value: hsv.h, max: 360, onChange: n => set(n, hsl.s, hsl.l) }),
            jsx(Channel, { label: "Saturation", value: hsl.s, max: 100, onChange: n => set(hsv.h, n, hsl.l) }),
            jsx(Channel, { label: "Lightness", value: hsl.l, max: 100, onChange: n => set(hsv.h, hsl.s, n) })));
    }
    return (jsx("div", { class: "pe-cp-channels" },
        jsx(Channel, { label: "Hue", value: hsv.h, max: 360, onChange: n => onChange({ ...hsv, h: n }) }),
        jsx(Channel, { label: "Saturation", value: hsv.s * 100, max: 100, onChange: n => onChange({ ...hsv, s: n / 100 }) }),
        jsx(Channel, { label: "Brightness", value: hsv.v * 100, max: 100, onChange: n => onChange({ ...hsv, v: n / 100 }) })));
}
