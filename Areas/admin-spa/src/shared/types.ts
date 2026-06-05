import type { ComponentType } from "react";
import type { LucideIcon } from "lucide-react";

/** Sidebar grouping. "extensions" is reserved for dynamically-registered extension tabs. */
export type ModuleGroup = "main" | "settings" | "extensions";

/**
 * A single admin area or extension page. Both the sidebar and the router are built from the set of
 * registered modules, so adding an area or an extension is a pure registration concern - the shell
 * never has to change.
 */
export interface AdminModule {
  /** Unique id, e.g. "dashboard" or "ext.hello". */
  id: string;
  /** Sidebar label. */
  title: string;
  /** Route path relative to /admin, e.g. "dashboard" or "extensions/hello". */
  path: string;
  /** Sidebar icon. */
  icon: LucideIcon;
  /** The page rendered inside the admin layout for this module's route. */
  Component: ComponentType;
  /** Sidebar group. Defaults to "main". */
  group?: ModuleGroup;
  /** Sort order within the group (ascending). Defaults to 100. */
  order?: number;
  /** "core" for built-in areas, "extension" for registered extensions. Defaults to "core". */
  kind?: "core" | "extension";
}
