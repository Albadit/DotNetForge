/** The site's theme colors: the --dnn-color-* variables (wwwroot/builder/site-colors.css, also linked by the editor page). Port of color.ts. */
export const THEME_COLORS = ['primary', 'secondary', 'tertiary', 'surface', 'background', 'foreground', 'neutral', 'success', 'warning', 'danger', 'info'];
const HEX = /^#?([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
let ctx;
/** RGBA of a hex, rgb() or hsl() color or a color keyword; null for var() and invalid text. */
export function parseColor(value) {
    if (!value)
        return null;
    const v = value.trim();
    const hex = HEX.exec(v);
    if (hex)
        return fromHex(hex[1]);
    if (/^var\(/i.test(v))
        return null;
    if (v.toLowerCase() === 'transparent')
        return { r: 0, g: 0, b: 0, a: 0 };
    // The browser normalizes rgb(), hsl() and keywords to #rrggbb or rgba(...).
    if (ctx === undefined)
        ctx = typeof document !== 'undefined' ? document.createElement('canvas').getContext('2d') : null;
    if (!ctx)
        return null;
    ctx.fillStyle = '#010203';
    ctx.fillStyle = v;
    const n = String(ctx.fillStyle);
    if (n === '#010203' && v.replace(/\s/g, '') !== 'rgb(1,2,3)')
        return null;
    const h = HEX.exec(n);
    if (h)
        return fromHex(h[1]);
    const m = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/.exec(n);
    return m ? { r: +m[1], g: +m[2], b: +m[3], a: m[4] === undefined ? 1 : +m[4] } : null;
}
function fromHex(h) {
    const full = h.length <= 4 ? h.split('').map(c => c + c).join('') : h;
    const n = (i) => parseInt(full.slice(i, i + 2), 16);
    return { r: n(0), g: n(2), b: n(4), a: full.length === 8 ? Math.round((n(6) / 255) * 100) / 100 : 1 };
}
const two = (n) => Math.round(Math.min(255, Math.max(0, n))).toString(16).padStart(2, '0');
/** "#rrggbb", with the alpha byte only when it is not opaque. */
export function hexOf(c) {
    return '#' + two(c.r) + two(c.g) + two(c.b) + (c.a < 1 ? two(c.a * 255) : '');
}
/** "RRGGBB" as Figma shows it next to the swatch. */
export function hex6(c) {
    return (two(c.r) + two(c.g) + two(c.b)).toUpperCase();
}
export function toHsv(c) {
    const r = c.r / 255, g = c.g / 255, b = c.b / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    let h = 0;
    if (d)
        h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return { h: ((h * 60) + 360) % 360, s: max ? d / max : 0, v: max, a: c.a };
}
export function fromHsv(c) {
    const f = (n) => {
        const k = (n + c.h / 60) % 6;
        return (c.v - c.v * c.s * Math.max(0, Math.min(k, 4 - k, 1))) * 255;
    };
    return { r: f(5), g: f(3), b: f(1), a: c.a };
}
/**
 * What the user typed into the hex box: "fff", "1E1E1E", "#00000080" or any CSS color.
 * Typing six digits keeps the current opacity. Undefined clears; null is invalid.
 */
export function parseInput(text, alpha = 1) {
    const t = text.trim();
    if (t === '')
        return undefined;
    const hex = HEX.exec(t);
    if (hex) {
        const c = fromHex(hex[1]);
        return hexOf(hex[1].length === 3 || hex[1].length === 6 ? { ...c, a: alpha } : c);
    }
    return null;
}
/** HSL (h 0-360, s and l 0-100) of an RGB color. */
export function toHsl(c) {
    const r = c.r / 255, g = c.g / 255, b = c.b / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    const l = (max + min) / 2;
    const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
    return { h: toHsv(c).h, s: s * 100, l: l * 100 };
}
export function fromHsl(h, s, l, a) {
    const S = s / 100, L = l / 100;
    const k = (n) => (n + h / 30) % 12;
    const f = (n) => (L - S * Math.min(L, 1 - L) * Math.max(-1, Math.min(k(n) - 3, 9 - k(n), 1))) * 255;
    return { r: f(0), g: f(8), b: f(4), a };
}
/** The color as CSS text, the way the picker's CSS format shows it. */
export function cssOf(c) {
    const n = (x) => Math.round(x);
    return c.a < 1 ? `rgba(${n(c.r)}, ${n(c.g)}, ${n(c.b)}, ${Math.round(c.a * 100) / 100})` : `rgb(${n(c.r)}, ${n(c.g)}, ${n(c.b)})`;
}
