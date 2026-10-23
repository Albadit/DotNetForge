import { jsx, Fragment, useEffect, useRef, useState } from "./vdom.js";
import { createDocument } from "../core/doc.js";
import { normalizeDocument } from "../core/validate.js";
import { Icon } from "./icons.js";
import { PRESETS } from "../editor/presets.js";
import { SHORTCUTS } from "../editor/shortcuts.js";
import { cls, useEditor, useSignal } from "./hooks.js";
import { cloneSubtree } from "../core/doc.js";
function Modal({ title, children, onClose, wide, footer }) {
    const ref = useRef(null);
    useEffect(() => {
        const prev = document.activeElement;
        const el = ref.current;
        const first = el.querySelector('[autofocus], button, input, select, textarea, a[href]');
        first?.focus();
        const key = (e) => {
            if (e.key === 'Escape') {
                e.stopPropagation();
                onClose();
            }
            else if (e.key === 'Tab') {
                const items = Array.from(el.querySelectorAll('button:not([disabled]), input:not([disabled]), select, textarea, a[href]'));
                if (!items.length)
                    return;
                const i = items.indexOf(document.activeElement);
                if (e.shiftKey && i <= 0) {
                    e.preventDefault();
                    items[items.length - 1].focus();
                }
                else if (!e.shiftKey && i === items.length - 1) {
                    e.preventDefault();
                    items[0].focus();
                }
            }
        };
        el.addEventListener('keydown', key);
        return () => {
            el.removeEventListener('keydown', key);
            prev?.focus?.();
        };
    }, []);
    return (jsx("div", { class: "pe-modal-backdrop", onPointerDown: e => e.target === e.currentTarget && onClose() },
        jsx("div", { ref: ref, class: cls('pe-modal', wide && 'is-wide'), role: "dialog", "aria-modal": "true", "aria-label": title },
            jsx("header", { class: "pe-modal-head" },
                jsx("h2", null, title),
                jsx("button", { type: "button", class: "pe-icon-btn", "aria-label": "Close", onClick: onClose },
                    jsx(Icon, { name: "close", size: 16 }))),
            jsx("div", { class: "pe-modal-body" }, children),
            footer && jsx("footer", { class: "pe-modal-foot" }, footer))));
}
export function Dialogs() {
    const { session } = useEditor();
    const dialog = useSignal(session.dialog);
    if (!dialog)
        return null;
    const close = () => session.dialog.set(null);
    switch (dialog.kind) {
        case 'onboarding': return jsx(Onboarding, { onClose: close });
        case 'conflict': return jsx(ConflictDialog, { dialog: dialog });
        case 'publish': return jsx(PublishDialog, { onClose: close });
        case 'shortcuts': return jsx(ShortcutsDialog, { onClose: close });
        case 'files': return jsx(FilesDialog, { onClose: close, onPick: dialog.onPick });
        case 'confirm': return (jsx(Modal, { title: dialog.title, onClose: close, footer: jsx(Fragment, null,
                jsx("button", { type: "button", class: "pe-btn", onClick: close }, "Cancel"),
                jsx("button", { type: "button", class: cls('pe-btn', dialog.danger ? 'pe-btn-danger' : 'pe-btn-primary'), autoFocus: true, onClick: () => {
                        close();
                        dialog.onConfirm();
                    } }, dialog.confirm)) },
            jsx("p", null, dialog.message)));
        default: return null;
    }
}
// The reference's "start from the current page" (DNN's modules as blocks) has no counterpart here: a page without a
// layout starts empty or from a ready-made block.
function Onboarding({ onClose }) {
    const { session } = useEditor();
    const page = useSignal(session.page);
    if (!page)
        return null;
    const start = (doc) => {
        session.replaceDocument(doc, true);
        onClose();
        void session.save(true);
    };
    return (jsx(Modal, { title: "Design this page", onClose: onClose, wide: true },
        jsx("p", { class: "pe-lead" }, "Build the page from sections, grids and components. Nothing changes for visitors until you publish, and you can unpublish at any time to show the page's default view again."),
        jsx("div", { class: "pe-choices" },
            jsx("button", { type: "button", class: "pe-choice", autoFocus: true, onClick: () => start(createDocument()) },
                jsx(Icon, { name: "page", size: 24 }),
                jsx("strong", null, "Start with an empty page"),
                jsx("span", null, "One empty section. Add content from the Insert panel.")),
            PRESETS.slice(0, 3).map(p => (jsx("button", { key: p.id, type: "button", class: "pe-choice", onClick: () => {
                    const doc = createDocument();
                    const root = doc.nodes[doc.root];
                    delete doc.nodes[root.children[0]];
                    const nodes = p.build();
                    const copy = cloneSubtree({ nodes: Object.fromEntries(nodes.map(n => [n.id, n])) }, nodes[0].id, root.id, new Set(Object.keys(doc.nodes)));
                    for (const n of copy.nodes)
                        doc.nodes[n.id] = n;
                    root.children = [copy.root];
                    start(normalizeDocument(doc));
                } },
                jsx(Icon, { name: p.icon, size: 24 }),
                jsx("strong", null,
                    "Start with: ",
                    p.label),
                jsx("span", null, p.description)))))));
}
function ConflictDialog({ dialog }) {
    const { session } = useEditor();
    const c = dialog.conflict;
    return (jsx(Modal, { title: "Someone else changed this page", onClose: () => session.dialog.set(null), footer: jsx(Fragment, null,
            jsx("button", { type: "button", class: "pe-btn", onClick: () => void session.reloadFromServer() }, "Load their version"),
            jsx("button", { type: "button", class: "pe-btn pe-btn-danger", onClick: () => void session.overwrite() }, "Keep mine and overwrite")) },
        jsx("p", null,
            jsx("strong", null, c.savedBy),
            " saved this page at ",
            new Date(c.savedOn).toLocaleString(),
            ", after you started editing."),
        jsx("p", null, "Loading their version discards your unsaved changes. Keeping yours replaces their changes.")));
}
// This server keeps no version history: the reference's History dialog and publish note are left out.
function PublishDialog({ onClose }) {
    const { session } = useEditor();
    const page = useSignal(session.page);
    const [busy, setBusy] = useState(false);
    return (jsx(Modal, { title: "Publish this page", onClose: onClose, footer: jsx(Fragment, null,
            jsx("button", { type: "button", class: "pe-btn", onClick: onClose }, "Cancel"),
            jsx("button", { type: "button", class: "pe-btn pe-btn-primary", disabled: busy, autoFocus: true, onClick: async () => {
                    setBusy(true);
                    await session.publish();
                    setBusy(false);
                    onClose();
                } }, busy ? 'Publishing…' : 'Publish now')) },
        jsx("p", null,
            "Visitors will see the current draft as soon as you publish.",
            page?.published ? '' : ' Until then they keep seeing the page as it is now.')));
}
function ShortcutsDialog({ onClose }) {
    const groups = [...new Set(SHORTCUTS.map(s => s.group))];
    return (jsx(Modal, { title: "Keyboard shortcuts", onClose: onClose, wide: true },
        jsx("div", { class: "pe-shortcuts" }, groups.map(g => (jsx("div", { key: g },
            jsx("h3", null, g),
            jsx("dl", null, SHORTCUTS.filter(s => s.group === g).map(s => (jsx("div", { key: s.keys, class: "pe-shortcut" },
                jsx("dt", null,
                    jsx("kbd", null, s.keys)),
                jsx("dd", null, s.label))))))))),
        jsx("p", { class: "pe-hint" }, "On a Mac, use \u2318 instead of Ctrl.")));
}
function FilesDialog({ onClose, onPick }) {
    const { session } = useEditor();
    const [folder, setFolder] = useState('');
    const [listing, setListing] = useState(null);
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const load = (f) => {
        setError('');
        setListing(null);
        session.api.files(f).then(l => {
            setListing(l);
            setFolder(l.folder);
        }, e => setError(e.message));
    };
    useEffect(() => load(''), []);
    const upload = async (file) => {
        setBusy(true);
        try {
            const item = await session.api.upload(folder, file);
            onPick(item.url);
            onClose();
        }
        catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        }
        finally {
            setBusy(false);
        }
    };
    return (jsx(Modal, { title: "Choose an image", onClose: onClose, wide: true },
        jsx("div", { class: "pe-files-bar" },
            jsx("span", { class: "pe-files-path" },
                jsx(Icon, { name: "folder", size: 16 }),
                " ",
                folder || 'Media library'),
            listing?.parent !== null && listing && jsx("button", { type: "button", class: "pe-btn", onClick: () => load(listing.parent) },
                jsx(Icon, { name: "up", size: 16 }),
                " Up"),
            listing?.canUpload && (jsx("label", { class: "pe-btn" },
                jsx(Icon, { name: "upload", size: 16 }),
                " ",
                busy ? 'Uploading…' : 'Upload',
                jsx("input", { type: "file", accept: "image/*", hidden: true, disabled: busy, onChange: e => {
                        const f = e.target.files?.[0];
                        if (f)
                            void upload(f);
                    } })))),
        error && jsx("p", { class: "pe-error" }, error),
        !listing && !error && jsx("p", null,
            jsx(Icon, { name: "loader", size: 16, class: "pe-spin" }),
            " Loading\u2026"),
        listing && (jsx("div", { class: "pe-files" },
            listing.folders.map(f => (jsx("button", { key: f, type: "button", class: "pe-file is-folder", onClick: () => load(f) },
                jsx(Icon, { name: "folder", size: 28 }),
                jsx("span", null, f.replace(/\/$/, '').split('/').pop())))),
            listing.files.map(f => (jsx("button", { key: f.url, type: "button", class: "pe-file", title: f.name, onClick: () => {
                    onPick(f.url);
                    onClose();
                } },
                jsx("img", { src: f.url, alt: "", loading: "lazy" }),
                jsx("span", null, f.name)))),
            listing.folders.length + listing.files.length === 0 && jsx("p", { class: "pe-hint" }, "This folder has no images.")))));
}
export function Toasts() {
    const { session } = useEditor();
    const toasts = useSignal(session.toasts);
    return (jsx("div", { class: "pe-toasts", role: "status", "aria-live": "polite" }, toasts.map(t => (jsx("div", { key: t.id, class: cls('pe-toast', 'is-' + t.kind) },
        jsx(Icon, { name: t.kind === 'error' ? 'alert' : t.kind === 'success' ? 'check' : 'info', size: 16 }),
        jsx("span", null, t.text),
        t.action && jsx("button", { type: "button", class: "pe-btn-text", onClick: t.action.run }, t.action.label))))));
}
