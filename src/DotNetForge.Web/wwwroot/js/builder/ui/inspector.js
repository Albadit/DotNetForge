/**
 * The properties inspector. It shows only what applies to the selection:
 * position controls depend on the parent's layout (grid cells, pixels in a
 * free container, sizing in a stack), layout controls appear for containers,
 * typography for text, and the Content tab is generated from the component's
 * schema. Values are edited for the current breakpoint; inherited values are
 * shown as placeholders.
 */
import { jsx, useState } from "./vdom.js";
import { getComponent } from "../components/registry.js";
import { componentDef } from "../core/schema.js";
import { styleAt } from "../core/responsive.js";
import { setAdvanced, setFlag, setVisibility, updateProps, setBreakpoints, } from "../core/commands.js";
import { runsText } from "../core/richtext.js";
import * as V from "../core/values.js";
import { Icon } from "./icons.js";
import { IconField, ImageInput, NumberField, Row, Section, SelectField, TextField, Toggle } from "./fields.js";
import { cls, useEditor, useNode, useSelection, useSignal } from "./hooks.js";
import { DesignTab } from "./design-panel.js";
export function Inspector() {
    const { session } = useEditor();
    const selection = useSelection();
    const caps = useSignal(session.caps);
    const [tab, setTab] = useState('design');
    useSignal(session.docRevision);
    const first = useNode(selection[0]);
    if (!first)
        return jsx(PagePanel, null);
    const hasContent = Object.keys(componentDef(first.type)?.props ?? {}).length > 0;
    const effectiveTab = !caps.editLayout && tab === 'design' ? (hasContent ? 'content' : 'advanced') : tab === 'content' && !hasContent ? 'design' : tab;
    return (jsx("div", { class: "pe-inspector" },
        jsx("div", { class: "pe-tabs", role: "tablist" }, ['design', 'content', 'advanced'].map(t => (jsx("button", { key: t, type: "button", role: "tab", "aria-selected": effectiveTab === t, class: cls('pe-tab', effectiveTab === t && 'is-on'), disabled: (t === 'design' && !caps.editLayout) || (t === 'content' && !hasContent), onClick: () => setTab(t) }, t === 'design' ? 'Design' : t === 'content' ? 'Content' : 'Advanced')))),
        jsx("div", { class: "pe-insp-body" },
            effectiveTab === 'design' && jsx(DesignTab, { ids: selection, node: first }),
            effectiveTab === 'content' && jsx(ContentTab, { node: first }),
            effectiveTab === 'advanced' && jsx(AdvancedTab, { node: first }))));
}
/* ---------------------------------------------------------------- content */
function ContentTab({ node }) {
    const { session, inline, canvas } = useEditor();
    const caps = useSignal(session.caps);
    const def = componentDef(node.type);
    const comp = getComponent(node.type);
    if (!def)
        return null;
    const entries = Object.entries(def.props);
    const canEdit = (p) => !node.locked && (caps.editLayout || (caps.editContent && !!p.content));
    return (jsx("div", null,
        comp?.inlineEdit && (jsx("div", { class: "pe-inline-hint" },
            jsx("button", { type: "button", class: "pe-btn", disabled: !caps.editContent || !!node.locked, onClick: () => {
                    canvas.scrollIntoView(node.id);
                    inline.start(node.id);
                } },
                jsx(Icon, { name: "text", size: 16 }),
                " Edit text on the page"),
            jsx("span", { class: "pe-hint" }, "or double-click it on the canvas"))),
        jsx(Section, { id: 'content-' + node.type, title: def.label }, entries.map(([key, p]) => (jsx(PropEditor, { key: key, node: node, prop: key, def: p, disabled: !canEdit(p) }))))));
}
function PropEditor({ node, prop, def, disabled }) {
    const { session } = useEditor();
    const store = session.store;
    const value = node.props[prop];
    const set = (v, merge) => updateProps(store, node.id, { [prop]: v }, merge);
    switch (def.type) {
        case 'text':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(TextField, { value: typeof value === 'string' ? value : '', maxLength: def.max, disabled: disabled, multiline: (def.max ?? 0) > 200, onChange: set }),
                def.help && jsx("div", { class: "pe-hint" }, def.help)));
        case 'inline':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(TextField, { value: runsText(value), disabled: disabled, maxLength: def.max, onChange: v => set(v ? [{ text: v }] : []) }),
                Array.isArray(value) && value.length > 1 && jsx("div", { class: "pe-hint" }, "Has formatting; editing here makes it plain. Use the canvas to keep it.")));
        case 'rich':
            return jsx("div", { class: "pe-hint" }, "The text is edited on the page itself: double-click it. Select words to make them bold, italic or a link.");
        case 'url':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(TextField, { value: typeof value === 'string' ? value : '', disabled: disabled, placeholder: "https://\u2026, /page or #anchor", validate: v => (V.linkUrl(v) === null ? 'Use https://…, a site path like /about, #anchor, mailto: or tel:.' : null), onChange: v => set(V.linkUrl(v) ?? '') })));
        case 'image':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(ImageInput, { value: typeof value === 'string' ? value : '', disabled: disabled, onChange: set, preview: true })));
        case 'video':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(TextField, { value: typeof value === 'string' ? value : '', disabled: disabled, placeholder: "https://www.youtube.com/watch?v=\u2026", validate: v => (V.videoEmbed(v) ? null : 'Paste a YouTube or Vimeo link.'), onChange: set })));
        case 'slug':
            return (jsx(Row, { label: def.label, wide: true },
                jsx(TextField, { value: typeof value === 'string' ? value : '', disabled: disabled, placeholder: "e.g. contact", validate: v => (V.slug(v) === null ? 'Lower-case letters, digits, - and _, starting with a letter.' : null), onChange: set })));
        case 'enum':
            return (jsx(Row, { label: def.label },
                jsx(SelectField, { value: String(value ?? def.default), disabled: disabled, options: (def.options ?? []).map(o => ({ value: o, label: enumLabel(prop, o) })), onChange: v => v !== undefined && set(v) })));
        case 'bool':
            return (jsx(Row, { label: "" },
                jsx(Toggle, { checked: value === true, label: def.label, disabled: disabled, onChange: set })));
        case 'int': {
            // A width reads like the W fields of the design panel; 0 shows "Full width" after the number.
            const width = prop === 'contentWidth';
            return (jsx(Row, { label: def.label },
                jsx(NumberField, { label: width ? 'W' : undefined, title: def.help ? def.label + ' (' + def.help + ')' : def.label, value: typeof value === 'number' ? value : undefined, placeholder: def.default, suffix: width && value === 0 ? 'Full width' : undefined, min: def.min ?? 0, max: def.max ?? 100000, decimals: 0, disabled: disabled, onChange: (v, m) => set(v ?? def.default, m) }),
                def.help && !width && jsx("span", { class: "pe-hint" }, def.help)));
        }
        case 'links':
            return jsx(LinksEditor, { items: Array.isArray(value) ? value : [], disabled: disabled, onChange: set });
        case 'icon':
            return (jsx(Row, { label: def.label },
                jsx(IconField, { value: typeof value === 'string' ? value : '', disabled: disabled, allowNone: node.type !== 'icon', onChange: set })));
        case 'comments':
            return jsx(CommentsEditor, { items: Array.isArray(value) ? value : [], max: def.maxItems ?? 100, disabled: disabled, onChange: set });
        case 'module':
            return jsx(ModulePicker, { value: typeof value === 'number' ? value : 0, disabled: disabled, onChange: set });
        default:
            return null;
    }
}
function enumLabel(prop, value) {
    if (prop === 'level')
        return 'Heading ' + value;
    if (prop === 'tag')
        return '<' + value + '>';
    return value.length <= 5 && /\d/.test(value) ? value : value[0].toUpperCase() + value.slice(1);
}
function LinksEditor({ items, onChange, disabled }) {
    const update = (i, patch) => onChange(items.map((it, j) => (j === i ? { ...it, ...patch } : it)));
    const move = (i, d) => {
        const next = [...items];
        const [it] = next.splice(i, 1);
        next.splice(i + d, 0, it);
        onChange(next);
    };
    return (jsx("div", { class: "pe-links-editor" },
        items.map((it, i) => (jsx("div", { class: "pe-link-item", key: i },
            jsx(TextField, { value: it.label, placeholder: "Label", ariaLabel: 'Link ' + (i + 1) + ' label', disabled: disabled, onChange: v => update(i, { label: v }) }),
            jsx(TextField, { value: it.href, placeholder: "/page", ariaLabel: 'Link ' + (i + 1) + ' address', disabled: disabled, validate: v => (V.linkUrl(v) === null ? 'Not a valid link.' : null), onChange: v => update(i, { href: V.linkUrl(v) ?? '#' }) }),
            jsx(IconField, { value: it.icon ?? '', disabled: disabled, onChange: v => update(i, { icon: v || undefined }) }),
            jsx("div", { class: "pe-link-tools" },
                jsx(Toggle, { checked: !!it.newTab, label: "New tab", disabled: disabled, onChange: v => update(i, { newTab: v }) }),
                jsx("button", { type: "button", class: "pe-icon-btn", title: "Move up", disabled: disabled || i === 0, onClick: () => move(i, -1) },
                    jsx(Icon, { name: "up", size: 16 })),
                jsx("button", { type: "button", class: "pe-icon-btn", title: "Move down", disabled: disabled || i === items.length - 1, onClick: () => move(i, 1) },
                    jsx(Icon, { name: "down", size: 16 })),
                jsx("button", { type: "button", class: "pe-icon-btn", title: "Remove", disabled: disabled, onClick: () => onChange(items.filter((_, j) => j !== i)) },
                    jsx(Icon, { name: "trash", size: 16 })))))),
        jsx("button", { type: "button", class: "pe-btn", disabled: disabled || items.length >= 50, onClick: () => onChange([...items, { label: 'New link', href: '#', newTab: false }]) },
            jsx(Icon, { name: "plus", size: 16 }),
            " Add link")));
}
function CommentsEditor({ items, onChange, disabled, max }) {
    const update = (i, patch) => onChange(items.map((it, j) => (j === i ? { ...it, ...patch } : it)));
    const move = (i, d) => {
        const next = [...items];
        const [it] = next.splice(i, 1);
        next.splice(i + d, 0, it);
        onChange(next);
    };
    return (jsx(Row, { label: "Comments", wide: true },
        jsx("div", { class: "pe-links-editor" },
            items.length === 0 && jsx("div", { class: "pe-hint" }, "No comments yet. Comments are written here and shown when a visitor opens the post."),
            items.map((it, i) => (jsx("div", { class: "pe-link-item", key: i },
                jsx(TextField, { value: it.author, placeholder: "Name", ariaLabel: 'Comment ' + (i + 1) + ' name', maxLength: 120, disabled: disabled, onChange: v => update(i, { author: v }) }),
                jsx(TextField, { value: it.meta, placeholder: "Date (optional)", ariaLabel: 'Comment ' + (i + 1) + ' date', maxLength: 60, disabled: disabled, onChange: v => update(i, { meta: v }) }),
                jsx(TextField, { value: it.text, placeholder: "Comment", ariaLabel: 'Comment ' + (i + 1) + ' text', multiline: true, maxLength: 2000, disabled: disabled, onChange: v => update(i, { text: v }) }),
                jsx(ImageInput, { value: it.avatar, placeholder: "Picture (optional)", disabled: disabled, onChange: v => update(i, { avatar: v }) }),
                jsx("div", { class: "pe-link-tools" },
                    jsx("span", { class: "pe-hint", style: { marginRight: 'auto' } },
                        "Comment ",
                        i + 1),
                    jsx("button", { type: "button", class: "pe-icon-btn", title: "Move up", disabled: disabled || i === 0, onClick: () => move(i, -1) },
                        jsx(Icon, { name: "up", size: 16 })),
                    jsx("button", { type: "button", class: "pe-icon-btn", title: "Move down", disabled: disabled || i === items.length - 1, onClick: () => move(i, 1) },
                        jsx(Icon, { name: "down", size: 16 })),
                    jsx("button", { type: "button", class: "pe-icon-btn", title: "Remove", disabled: disabled, onClick: () => onChange(items.filter((_, j) => j !== i)) },
                        jsx(Icon, { name: "trash", size: 16 })))))),
            jsx("button", { type: "button", class: "pe-btn", disabled: disabled || items.length >= max, onClick: () => onChange([...items, { author: '', avatar: '', meta: '', text: '' }]) },
                jsx(Icon, { name: "plus", size: 16 }),
                " Add comment"))));
}
function ModulePicker({ value, onChange, disabled }) {
    const { session } = useEditor();
    const modules = useSignal(session.modules);
    return (jsx(Row, { label: "Module", wide: true },
        jsx(SelectField, { value: value ? String(value) : undefined, placeholder: "Choose a module on this page", disabled: disabled, options: modules.map(m => ({ value: String(m.moduleId), label: `${m.title || m.moduleName} (${m.paneName})` })), onChange: v => onChange(v ? Number(v) : 0) }),
        jsx("div", { class: "pe-hint" }, "Modules keep their own settings and permissions.")));
}
/* ---------------------------------------------------------------- advanced */
const DEVICES = [
    { value: 'desktop', label: 'Desktop', icon: 'desktop' },
    { value: 'tablet', label: 'Tablet', icon: 'tablet' },
    { value: 'mobile', label: 'Mobile', icon: 'mobile' },
];
function AdvancedTab({ node }) {
    const { session } = useEditor();
    const store = session.store;
    const caps = useSignal(session.caps);
    const vis = node.visibility ?? {};
    const devices = vis.devices ?? ['desktop', 'tablet', 'mobile'];
    const canLayout = caps.editLayout && !node.locked;
    const setVis = (patch) => {
        const next = { ...vis, ...patch };
        if (next.devices && next.devices.length === 3)
            delete next.devices;
        if (next.audience === 'all')
            delete next.audience;
        if (next.roles && next.roles.length === 0)
            delete next.roles;
        setVisibility(store, node.id, next);
    };
    const vars = Object.entries(node.vars ?? {});
    return (jsx("div", null,
        jsx(Section, { id: "visibility", title: "Visibility" },
            jsx(Row, { label: "Show on" },
                jsx("div", { class: "pe-device-toggles" }, DEVICES.map(d => (jsx("button", { key: d.value, type: "button", class: cls('pe-icon-btn', devices.includes(d.value) && 'is-on'), title: d.label, "aria-pressed": devices.includes(d.value), disabled: !canLayout, onClick: () => setVis({ devices: devices.includes(d.value) ? devices.filter(x => x !== d.value) : [...devices, d.value] }) },
                    jsx(Icon, { name: d.icon, size: 16 })))))),
            devices.length === 0 && jsx("div", { class: "pe-note" }, "Hidden on every device: visitors never receive it."),
            jsx(Row, { label: "Who" },
                jsx(SelectField, { value: vis.audience ?? 'all', disabled: !canLayout, options: [{ value: 'all', label: 'Everyone' }, { value: 'authenticated', label: 'Signed-in users' }, { value: 'anonymous', label: 'Visitors who are not signed in' }], onChange: v => setVis({ audience: (v ?? 'all') }) })),
            // Roles per element are not offered here: who may view or edit the page is set in the Content Manager.
            jsx("div", { class: "pe-hint" }, "Checked on the server for every visit: hidden elements are not sent to the visitor at all.")),
        jsx(Section, { id: "advanced", title: "Advanced" },
            jsx(Row, { label: "Classes", wide: true },
                jsx(TextField, { value: node.className ?? '', placeholder: "my-class another-class", disabled: !canLayout, mono: true, validate: v => (V.classNames(v) === null ? 'Letters, digits, - and _ only; classes starting with pe- are reserved.' : null), onChange: v => setAdvanced(store, node.id, { className: V.classNames(v) ?? '' }) })),
            jsx(Row, { label: "Variables", wide: true },
                vars.map(([k, v], i) => (jsx("div", { class: "pe-var", key: i },
                    jsx(TextField, { value: k, mono: true, disabled: !canLayout, ariaLabel: "Variable name", validate: x => (V.varName(x) ? null : 'Starts with --'), onChange: x => {
                            const next = Object.fromEntries(vars.map(([a, b], j) => (j === i ? [x, b] : [a, b])));
                            setAdvanced(store, node.id, { vars: next });
                        } }),
                    jsx(TextField, { value: v, mono: true, disabled: !canLayout, ariaLabel: "Variable value", validate: x => (V.varValue(x) ? null : 'Not allowed here.'), onChange: x => setAdvanced(store, node.id, { vars: { ...node.vars, [k]: x } }) }),
                    jsx("button", { type: "button", class: "pe-icon-btn", title: "Remove", disabled: !canLayout, onClick: () => {
                            const next = { ...node.vars };
                            delete next[k];
                            setAdvanced(store, node.id, { vars: next });
                        } },
                        jsx(Icon, { name: "trash", size: 16 }))))),
                jsx("button", { type: "button", class: "pe-btn", disabled: !canLayout || vars.length >= 20, onClick: () => setAdvanced(store, node.id, { vars: { ...node.vars, ['--var-' + (vars.length + 1)]: '0' } }) },
                    jsx(Icon, { name: "plus", size: 16 }),
                    " Add CSS variable")),
            jsx(Row, { label: "Selector", wide: true },
                jsx("code", { class: "pe-code" },
                    ".pe-n-",
                    node.id))),
        jsx(Section, { id: "state", title: "State" },
            jsx(Row, { label: "" },
                jsx(Toggle, { checked: !!node.hidden, label: "Hidden (not published)", disabled: !caps.editLayout, onChange: v => setFlag(store, [node.id], 'hidden', v) })),
            jsx(Row, { label: "" },
                jsx(Toggle, { checked: !!node.locked, label: "Locked", disabled: !caps.editLayout, onChange: v => setFlag(store, [node.id], 'locked', v) })))));
}
/* ---------------------------------------------------------------- page */
function PagePanel() {
    const { session } = useEditor();
    const page = useSignal(session.page);
    const caps = useSignal(session.caps);
    const pending = useSignal(session.pendingChanges);
    const problems = useSignal(session.problems);
    useSignal(session.docRevision);
    const doc = session.store.doc;
    const bps = doc.settings.breakpoints;
    if (!page)
        return null;
    return (jsx("div", { class: "pe-inspector" },
        jsx("div", { class: "pe-insp-body" },
            jsx(Section, { id: "page-status", title: "Status" },
                jsx(StatusLine, { ok: page.published, label: page.published ? 'Live: visitors see this layout' : 'Not published: visitors see the page\'s default view' }),
                pending && page.published && jsx(StatusLine, { ok: false, label: "There are unpublished changes" }),
                page.publishedOn && jsx("div", { class: "pe-hint" },
                    "Last published ",
                    new Date(page.publishedOn).toLocaleString()),
                jsx("div", { class: "pe-stack-btns" },
                    jsx("a", { class: "pe-btn", href: session.config.previewUrl, target: "_blank", rel: "noopener" },
                        jsx(Icon, { name: "external", size: 16 }),
                        " Preview draft"))),
            problems.length > 0 && (jsx(Section, { id: "page-problems", title: "Notes" }, problems.map((p, i) => jsx("div", { key: i, class: "pe-note" },
                jsx(Icon, { name: "alert", size: 16 }),
                " ",
                p)))),
            jsx(Section, { id: "page-breakpoints", title: "Breakpoints" },
                jsx(Row, { label: "Tablet below" },
                    jsx(NumberField, { label: "W", title: "Tablet styles apply below this width (drag W to change it)", value: bps.tablet, min: bps.mobile + 1, max: 3840, decimals: 0, disabled: !caps.editLayout, onChange: (v, m) => v && setBreakpoints(session.store, v, bps.mobile, m) })),
                jsx(Row, { label: "Mobile below" },
                    jsx(NumberField, { label: "W", title: "Mobile styles apply below this width (drag W to change it)", value: bps.mobile, min: 240, max: bps.tablet - 1, decimals: 0, disabled: !caps.editLayout, onChange: (v, m) => v && setBreakpoints(session.store, bps.tablet, v, m) })),
                jsx("div", { class: "pe-hint" }, "Desktop styles apply everywhere; tablet and mobile only change what you set there.")),
            jsx(Section, { id: "page-shortcuts", title: "Tips" },
                jsx("div", { class: "pe-hint" },
                    "Select an element on the canvas or in the layers. Drag it to move, drag its handles to resize. Press ",
                    jsx("kbd", null, "?"),
                    " for all keyboard shortcuts."),
                jsx("button", { type: "button", class: "pe-btn", onClick: () => session.dialog.set({ kind: 'shortcuts' }) },
                    jsx(Icon, { name: "keyboard", size: 16 }),
                    " Keyboard shortcuts")))));
}
function StatusLine({ ok, label }) {
    return jsx("div", { class: cls('pe-status-line', ok ? 'is-ok' : 'is-warn') },
        jsx("span", { class: "pe-dot" }),
        " ",
        label);
}
export { styleAt };
