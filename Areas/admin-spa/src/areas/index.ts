import { lazy } from "react";
import { LayoutDashboard, FileText, Image, Users, Settings, Puzzle } from "lucide-react";
import { registerModules } from "../shared/registry";

// Lazy-loaded so each area is its own chunk (code-splitting) - the heavy deps (charts, calendar,
// file tree) only load when that route is opened, keeping the initial bundle small.
const DashboardPage = lazy(() => import("./dashboard/DashboardPage"));
const ContentManagerPage = lazy(() => import("./content/ContentManagerPage"));
const MediaPage = lazy(() => import("./media/MediaPage"));
const UsersPage = lazy(() => import("./users/UsersPage"));
const SettingsPage = lazy(() => import("./settings/SettingsPage"));
const ExtensionsPage = lazy(() => import("./extensions/ExtensionsPage"));

// Built-in admin areas. Each is a module in the registry; the sidebar and router pick them up.
registerModules([
  { id: "dashboard", title: "Dashboard", path: "dashboard", icon: LayoutDashboard, Component: DashboardPage, group: "main", order: 10 },
  { id: "content", title: "Content", path: "content", icon: FileText, Component: ContentManagerPage, group: "main", order: 20 },
  { id: "media", title: "Media", path: "media", icon: Image, Component: MediaPage, group: "main", order: 30 },
  { id: "users", title: "Users", path: "users", icon: Users, Component: UsersPage, group: "main", order: 40 },
  { id: "settings", title: "Settings", path: "settings", icon: Settings, Component: SettingsPage, group: "settings", order: 10 },
  { id: "extensions", title: "Extensions", path: "extensions", icon: Puzzle, Component: ExtensionsPage, group: "settings", order: 20 },
]);
