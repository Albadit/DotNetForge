import { BUILTIN_TYPES, defineComponentSchema } from "../core/schema.js";
const registry = /* @__PURE__ */ new Map();
const listeners = /* @__PURE__ */ new Set();
function registerComponent(renderer) {
  if (!/^[a-z][a-z0-9-]{1,40}$/.test(renderer.type)) throw new Error("Invalid component type: " + renderer.type);
  if (BUILTIN_TYPES.has(renderer.type) && registry.has(renderer.type)) throw new Error("Built-in component types cannot be replaced: " + renderer.type);
  for (const [key, prop] of Object.entries(renderer.schema.props)) {
    if (prop.default === void 0) throw new Error(`Component ${renderer.type}: prop ${key} needs a default.`);
  }
  defineComponentSchema(renderer.type, renderer.schema);
  registry.set(renderer.type, renderer);
  for (const l of listeners) l();
}
function getComponent(type) {
  return registry.get(type);
}
function allComponents() {
  return [...registry.values()];
}
function onRegistryChange(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
export {
  allComponents,
  getComponent,
  onRegistryChange,
  registerComponent
};
