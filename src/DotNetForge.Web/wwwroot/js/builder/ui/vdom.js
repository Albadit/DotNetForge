// A small virtual DOM with hooks for the page builder's UI (.docs/features/page-builder.md#editor). It implements the
// part of Preact's API that the reference editor's UI uses - h, Fragment, render, createContext and the hooks
// useState, useReducer, useEffect, useLayoutEffect, useRef, useMemo, useCallback and useContext - with Preact's
// semantics, so the UI modules are a one-to-one port of the reference's components without a framework dependency.
//
// How it works: components are plain functions that return vnodes (h()). A render diffs the new vnodes against the
// previous ones and patches the DOM in place: elements of the same type are reused (so focus, scroll position and
// iframes survive), children are matched by key or by position, and DOM nodes move only when their order changed.
// State changes (setState, useReducer's dispatch) re-render the component in a microtask, parents before children.
// Styles are set through the CSSOM (element.style), never through a style attribute, so the strict CSP holds.

const TEXT = "#text";
const EMPTY = {};
const SVG_NS = "http://www.w3.org/2000/svg";
// Style properties that take plain numbers (no "px"), as in Preact.
const NON_DIMENSIONAL = /acit|ex(?:s|g|n|p|$)|rph|grid|ows|mnc|ntw|ine[ch]|zoo|^ord|itera/i;
// Properties set as attributes even though the element has a matching DOM property (as in Preact).
const ATTRIBUTES = new Set(["width", "height", "href", "list", "form", "tabIndex", "download", "rowSpan", "colSpan", "role", "popover"]);

/** A vnode: an element (type is a tag name), a component (a function), a fragment or text. */
export function h(type, props, ...children) {
  const normalized = {};
  let key, ref;
  for (const name in props) {
    if (name === "key") key = props[name];
    else if (name === "ref") ref = props[name];
    else normalized[name] = props[name];
  }
  if (children.length) normalized.children = children.length > 1 ? children : children[0];
  return { type, props: normalized, key, ref };
}

// The UI modules call the factory jsx(): the reference's components have locals named h (heights, handlers) that
// would shadow it in plain JavaScript, where JSX syntax kept them apart.
export { h as jsx };

export function Fragment(props) {
  return props.children;
}

/* ---------- context ---------- */

let contextIds = 0;

export function createContext(defaultValue) {
  const context = { id: "c" + contextIds++, defaultValue };
  context.Provider = function Provider(props) {
    return props.children;
  };
  context.Provider.context = context;
  context.Consumer = function Consumer(props, ctx) {
    return props.children(readContext(ctx, context));
  };
  return context;
}

function readContext(ctx, context) {
  return context.id in ctx ? ctx[context.id] : context.defaultValue;
}

/* ---------- hooks ---------- */

let current = null; // the component being rendered
let hookIndex = 0;

function hookState() {
  const hooks = current.hooks;
  if (hookIndex >= hooks.length) hooks.push({});
  return hooks[hookIndex++];
}

function changed(oldDeps, newDeps) {
  return !oldDeps || !newDeps || oldDeps.length !== newDeps.length || newDeps.some((d, i) => !Object.is(d, oldDeps[i]));
}

export function useState(initial) {
  return useReducer(setStateReducer, initial, initialState);
}

function setStateReducer(state, action) {
  return typeof action === "function" ? action(state) : action;
}

function initialState(initial) {
  return typeof initial === "function" ? initial() : initial;
}

export function useReducer(reducer, initialArg, init) {
  const hook = hookState();
  hook.reducer = reducer;
  if (!hook.component) {
    const component = current;
    hook.component = component;
    hook.value = [init ? init(initialArg) : initialArg, (action) => {
      const next = hook.reducer(hook.value[0], action);
      if (Object.is(next, hook.value[0])) return;
      hook.value = [next, hook.value[1]];
      enqueueRender(component);
    }];
  }
  return hook.value;
}

export function useEffect(effect, deps) {
  const hook = hookState();
  if (changed(hook.deps, deps)) {
    hook.deps = deps;
    hook.effect = effect;
    current.passive.push(hook);
  }
}

export function useLayoutEffect(effect, deps) {
  const hook = hookState();
  if (changed(hook.deps, deps)) {
    hook.deps = deps;
    hook.effect = effect;
    current.layout.push(hook);
  }
}

export function useRef(initial) {
  return useMemo(() => ({ current: initial }), []);
}

export function useMemo(factory, deps) {
  const hook = hookState();
  if (changed(hook.deps, deps)) {
    hook.deps = deps;
    hook.value = factory();
  }
  return hook.value;
}

export function useCallback(callback, deps) {
  return useMemo(() => callback, deps);
}

export function useContext(context) {
  return readContext(current.ctx, context);
}

/* ---------- scheduling and effects ---------- */

let queue = [];
let scheduled = false;
const layoutEffects = [];
const passiveEffects = [];
let passiveScheduled = false;

function enqueueRender(component) {
  if (component.dirty || component.unmounted) return;
  component.dirty = true;
  queue.push(component);
  if (!scheduled) {
    scheduled = true;
    queueMicrotask(processQueue);
  }
}

function processQueue() {
  scheduled = false;
  while (queue.length) {
    const batch = queue.sort((a, b) => a.vnode.depth - b.vnode.depth);
    queue = [];
    for (const component of batch) {
      if (!component.dirty || component.unmounted) continue;
      // One failing component must not stop the others (or the effects below) from updating.
      try {
        rerender(component);
      } catch (e) {
        reportError(e);
      }
    }
  }
  commit();
}

/** Runs the effects collected while rendering: layout effects now, passive ones in a microtask. */
function commit() {
  while (layoutEffects.length) runEffect(layoutEffects.shift());
  if (passiveEffects.length && !passiveScheduled) {
    passiveScheduled = true;
    queueMicrotask(() => {
      passiveScheduled = false;
      while (passiveEffects.length) runEffect(passiveEffects.shift());
    });
  }
}

function runEffect({ hook, component }) {
  if (component.unmounted) return;
  if (typeof hook.cleanup === "function") hook.cleanup();
  hook.cleanup = hook.effect();
}

/* ---------- rendering ---------- */

/** Renders a vnode tree into a container element, patching what an earlier render() put there. */
export function render(vnode, container) {
  const root = container.__vdomRoot ?? (container.__vdomRoot = { type: "#root", dom: container, children: [], depth: 0 });
  diffChildren(root, normalizeChildren(vnode == null ? [] : [vnode]), root.children, EMPTY, false, root);
  placeChildren(root);
  commit();
}

function normalizeChildren(children) {
  const list = Array.isArray(children) ? children : [children];
  return list.map(normalize);
}

/** A fresh vnode for one child: null for nothing, text, an array as a fragment, a copy of an element or component. */
function normalize(child) {
  if (child == null || typeof child === "boolean") return null;
  if (typeof child === "string" || typeof child === "number" || typeof child === "bigint") return { type: TEXT, props: String(child) };
  if (Array.isArray(child)) return { type: Fragment, props: { children: child } };
  return { type: child.type, props: child.props, key: child.key, ref: child.ref };
}

function diffChildren(parent, newChildren, oldChildren, ctx, svg, owner) {
  const used = new Set();
  newChildren.forEach((child, i) => {
    if (!child) return;
    const match = findMatch(oldChildren, i, child, used);
    if (match) used.add(match);
    child.depth = parent.depth + 1;
    diff(child, match, ctx, svg, owner);
  });
  for (const old of oldChildren) {
    if (old && !used.has(old)) unmount(old, true);
  }
  parent.children = newChildren;
}

function findMatch(oldChildren, index, child, used) {
  const same = (o) => o && !used.has(o) && o.type === child.type && o.key === child.key;
  if (same(oldChildren[index])) return oldChildren[index];
  return oldChildren.find(same) ?? null;
}

function diff(vnode, old, ctx, svg, owner) {
  vnode.owner = owner;
  const type = vnode.type;
  if (typeof type === "function" && type !== Fragment) {
    diffComponent(vnode, old, ctx, svg, owner);
  } else if (type === Fragment) {
    diffChildren(vnode, normalizeChildren(vnode.props.children), old?.children ?? [], ctx, svg, owner);
  } else if (type === TEXT) {
    if (old) {
      vnode.dom = old.dom;
      if (old.props !== vnode.props) vnode.dom.data = vnode.props;
    } else {
      vnode.dom = document.createTextNode(vnode.props);
    }
  } else {
    diffElement(vnode, old, ctx, svg);
  }
}

function diffComponent(vnode, old, ctx, svg, owner) {
  const component = old?.component ?? { hooks: [], layout: [], passive: [], dirty: false, unmounted: false };
  vnode.component = component;
  component.vnode = vnode;
  component.ctx = ctx;
  component.svg = svg;
  vnode.children = old?.children ?? [];
  renderComponent(component, owner);
}

function renderComponent(component, owner) {
  const vnode = component.vnode;
  const provider = vnode.type.context;
  const childCtx = provider ? Object.assign(Object.create(component.ctx), { [provider.id]: vnode.props.value }) : component.ctx;
  const previous = current;
  current = component;
  hookIndex = 0;
  component.dirty = false;
  let output;
  try {
    output = vnode.type(vnode.props, component.ctx);
  } finally {
    current = previous;
  }
  diffChildren(vnode, normalizeChildren(output), vnode.children, childCtx, component.svg, owner);
  // Child effects first, then this component's (as in Preact).
  for (const hook of component.layout) layoutEffects.push({ hook, component });
  for (const hook of component.passive) passiveEffects.push({ hook, component });
  component.layout = [];
  component.passive = [];
}

/** A state change: render the component again where it is and put its DOM nodes in place. */
function rerender(component) {
  const owner = component.vnode.owner;
  renderComponent(component, owner);
  placeChildren(owner);
}

function diffElement(vnode, old, ctx, svg) {
  const type = vnode.type;
  svg = svg || type === "svg";
  const dom = old ? old.dom : svg ? document.createElementNS(SVG_NS, type) : document.createElement(type);
  vnode.dom = dom;
  const props = vnode.props;
  const oldProps = old ? old.props : EMPTY;
  for (const name in oldProps) {
    if (name !== "children" && !(name in props)) setProperty(dom, name, null, oldProps[name], svg);
  }
  for (const name in props) {
    if (name === "children" || name === "value" || name === "checked" || name === "dangerouslySetInnerHTML") continue;
    if (props[name] !== oldProps[name]) setProperty(dom, name, props[name], oldProps[name], svg);
  }
  const html = props.dangerouslySetInnerHTML?.__html;
  if (html != null) {
    for (const child of old?.children ?? []) if (child) unmount(child, false);
    if (html !== oldProps.dangerouslySetInnerHTML?.__html || !old) dom.innerHTML = html;
    vnode.children = [];
  } else {
    if (oldProps.dangerouslySetInnerHTML) dom.innerHTML = "";
    diffChildren(vnode, normalizeChildren(props.children), old?.children ?? [], ctx, svg && type !== "foreignObject", vnode);
    placeChildren(vnode);
  }
  // After the children, so a <select> has its options.
  if ("value" in props && props.value !== undefined && (props.value !== dom.value || (type === "option" && props.value !== oldProps.value))) {
    dom.value = props.value == null ? "" : props.value;
  }
  if ("checked" in props && props.checked !== undefined && props.checked !== dom.checked) dom.checked = props.checked;
  if (old?.ref !== vnode.ref) {
    if (old?.ref) applyRef(old.ref, null);
    if (vnode.ref) applyRef(vnode.ref, dom);
  }
}

function applyRef(ref, value) {
  if (typeof ref === "function") ref(value);
  else ref.current = value;
}

/** The DOM nodes a vnode stands for, in order: its own node, or those of its children (components, fragments). */
function domNodes(vnode, out = []) {
  if (!vnode) return out;
  if (vnode.dom && vnode.type !== "#root") out.push(vnode.dom);
  else for (const child of vnode.children ?? []) domNodes(child, out);
  return out;
}

/** Puts the DOM children of an element (or the root container) in the order of its vnodes, moving only what moved. */
function placeChildren(owner) {
  const parent = owner.dom;
  let cursor = parent.firstChild;
  for (const vnode of owner.children) {
    for (const node of domNodes(vnode)) {
      if (node === cursor) cursor = cursor.nextSibling;
      else parent.insertBefore(node, cursor);
    }
  }
}

function unmount(vnode, removeDom) {
  if (vnode.ref) applyRef(vnode.ref, null);
  const component = vnode.component;
  if (component) {
    component.unmounted = true;
    for (const hook of component.hooks) {
      if (typeof hook.cleanup === "function") hook.cleanup();
    }
  }
  const isNode = vnode.type === TEXT || typeof vnode.type === "string";
  for (const child of vnode.children ?? []) if (child) unmount(child, removeDom && !isNode);
  if (isNode && removeDom) vnode.dom.remove();
}

/* ---------- properties ---------- */

function setStyle(style, key, value) {
  if (key[0] === "-") style.setProperty(key, value == null ? "" : value);
  else style[key] = value == null ? "" : typeof value !== "number" || NON_DIMENSIONAL.test(key) ? value : value + "px";
}

function setProperty(dom, name, value, oldValue, svg) {
  if (name === "key" || name === "ref") return;
  if (name === "style") {
    if (typeof value === "string") {
      dom.style.cssText = value;
      return;
    }
    if (typeof oldValue === "string") dom.style.cssText = oldValue = "";
    if (oldValue) for (const key in oldValue) if (!(value && key in value)) setStyle(dom.style, key, "");
    if (value) for (const key in value) if (!oldValue || value[key] !== oldValue[key]) setStyle(dom.style, key, value[key]);
    return;
  }
  if (name[0] === "o" && name[1] === "n") {
    const capture = name !== (name = name.replace(/(PointerCapture)$|Capture$/i, "$1"));
    const lower = name.toLowerCase();
    const event = lower in dom || name === "onFocusOut" || name === "onFocusIn" ? lower.slice(2) : name.slice(2);
    const listeners = dom.__listeners ?? (dom.__listeners = {});
    const slot = event + (capture ? "!" : "");
    if (value) {
      if (!listeners[slot]) dom.addEventListener(event, capture ? proxyCapture : proxy, capture);
      listeners[slot] = value;
    } else if (listeners[slot]) {
      dom.removeEventListener(event, capture ? proxyCapture : proxy, capture);
      delete listeners[slot];
    }
    return;
  }
  if (svg) {
    if (name === "className") name = "class";
  } else if (!ATTRIBUTES.has(name) && name in dom) {
    try {
      dom[name] = value == null ? "" : value;
      return;
    } catch {
      /* read-only property: set the attribute */
    }
  }
  if (typeof value === "function") return;
  if (value != null && (value !== false || name[4] === "-")) dom.setAttribute(name, name === "popover" && value === true ? "" : value);
  else dom.removeAttribute(name);
}

function proxy(e) {
  return this.__listeners[e.type](e);
}

function proxyCapture(e) {
  return this.__listeners[e.type + "!"](e);
}
