/**
 * Hooks that connect Preact components to the session. Each one subscribes to
 * exactly what the component shows, so an edit re-renders only the rows and
 * fields of the nodes it changed.
 */
import { createContext } from "./vdom.js";
import { useContext, useEffect, useReducer, useRef, useState } from "./vdom.js";
export const EditorContext = createContext(null);
export function useEditor() {
    return useContext(EditorContext);
}
export function useSignal(sig) {
    const [, force] = useReducer((n) => n + 1, 0);
    const rendered = useRef(sig.value);
    rendered.current = sig.value;
    useEffect(() => {
        const off = sig.subscribe(() => force(0));
        // The value may have changed between render and subscription (another component's effect).
        if (!Object.is(rendered.current, sig.value))
            force(0);
        return off;
    }, [sig]);
    return sig.value;
}
/**
 * A value derived from a source, re-rendering only when the derived value
 * changes. Lists use it so a selection or hover change re-renders the two
 * rows involved instead of all of them.
 */
export function useDerived(subscribe, get, deps) {
    const [, force] = useReducer((n) => n + 1, 0);
    const value = get();
    const last = useRef(value);
    last.current = value;
    useEffect(() => {
        const check = () => {
            if (!Object.is(get(), last.current))
                force(0);
        };
        const off = subscribe(check);
        check();
        return off;
    }, deps);
    return value;
}
/** Re-renders when the store is replaced (load/restore). */
function useStoreRevision() {
    const { session } = useEditor();
    return useSignal(session.docRevision);
}
/** One node, re-rendering only when that node changes. */
export function useNode(id) {
    const { session } = useEditor();
    const rev = useStoreRevision();
    const [, force] = useReducer((n) => n + 1, 0);
    useEffect(() => (id ? session.store.subscribeNode(id, () => force(0)) : undefined), [id, rev, session]);
    return id ? session.store.doc.nodes[id] : undefined;
}
export function useSelection() {
    const { session } = useEditor();
    const rev = useStoreRevision();
    const [, force] = useReducer((n) => n + 1, 0);
    useEffect(() => session.store.subscribeSelection(() => force(0)), [rev, session]);
    return session.store.selection;
}
/** Re-renders on every committed change (use sparingly: toolbar undo state, page-wide views). */
export function useDocVersion() {
    const { session } = useEditor();
    const rev = useStoreRevision();
    const [, force] = useReducer((n) => n + 1, 0);
    useEffect(() => session.store.subscribe(() => force(0)), [rev, session]);
    return session.store.version;
}
/** Re-renders when the tree shape changes (layers panel). */
export function useStructure() {
    const { session } = useEditor();
    const rev = useStoreRevision();
    const [n, bump] = useReducer((x) => x + 1, 0);
    useEffect(() => session.store.subscribe(c => {
        if (c.structure)
            bump(0);
    }), [rev, session]);
    return n + rev * 100000;
}
export function useLatest(value) {
    const ref = useRef(value);
    ref.current = value;
    return ref;
}
/** Local draft state for an input that commits on blur/Enter but follows external changes. */
export function useDraft(value) {
    const [draft, setDraft] = useState(value);
    const last = useRef(value);
    if (last.current !== value) {
        last.current = value;
        if (draft !== value)
            setDraft(value);
    }
    return [draft, setDraft, () => setDraft(value)];
}
export function cls(...parts) {
    return parts.filter(Boolean).join(' ');
}
