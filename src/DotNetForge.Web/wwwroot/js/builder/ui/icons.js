import { jsx } from "./vdom.js";
import { ICONS } from "./icon-data.js";
export function Icon({ name, size = 16, class: className, title }) {
    const data = ICONS[name] ?? ICONS.frame;
    const kind = data[0];
    const body = data.slice(1);
    const a11y = title ? '<title>' + title.replace(/[<&]/g, c => (c === '<' ? '&lt;' : '&amp;')) + '</title>' : '';
    if (kind === 'S') {
        // A 16px kit icon: drawn at its own size.
        return (jsx("svg", { class: className ? 'pe-icon ' + className : 'pe-icon', width: size, height: size, viewBox: "0 0 16 16", fill: "currentColor", "aria-hidden": title ? undefined : 'true', role: title ? 'img' : undefined, dangerouslySetInnerHTML: { __html: a11y + body } }));
    }
    if (kind === 'F') {
        const box = size * 1.5;
        return (jsx("svg", { class: className ? 'pe-icon ' + className : 'pe-icon', width: box, height: box, viewBox: "0 0 24 24", fill: "currentColor", style: { margin: -(size / 4) + 'px' }, "aria-hidden": title ? undefined : 'true', role: title ? 'img' : undefined, dangerouslySetInnerHTML: { __html: a11y + body } }));
    }
    return (jsx("svg", { class: className ? 'pe-icon ' + className : 'pe-icon', width: size, height: size, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", "stroke-width": "1.5", "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": title ? undefined : 'true', role: title ? 'img' : undefined, dangerouslySetInnerHTML: { __html: a11y + body } }));
}
