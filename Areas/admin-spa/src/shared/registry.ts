import type { AdminModule, ModuleGroup } from "./types";

/**
 * The single source of truth for admin areas and extension pages. Core areas register here at
 * startup; extensions register themselves the same way (see src/extensions). The sidebar and router
 * both read from this registry, so a new area or extension appears automatically with no changes to
 * the layout or routing code.
 */
const modules: AdminModule[] = [];

export function registerModule(module: AdminModule): void {
  if (modules.some((m) => m.id === module.id)) {
    console.warn(`[admin] module "${module.id}" is already registered; ignoring duplicate.`);
    return;
  }
  modules.push(module);
}

export function registerModules(list: AdminModule[]): void {
  list.forEach(registerModule);
}

/** Convenience for extensions: register a tab in the "extensions" group. */
export function registerExtension(
  module: Omit<AdminModule, "group" | "kind"> & { group?: ModuleGroup },
): void {
  registerModule({ group: "extensions", ...module, kind: "extension" });
}

export function getModules(): AdminModule[] {
  return [...modules].sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
}

export function getModulesByGroup(group: ModuleGroup): AdminModule[] {
  return getModules().filter((m) => (m.group ?? "main") === group);
}
