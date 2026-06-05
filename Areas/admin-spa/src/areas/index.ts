import { LayoutDashboard, FileText, Image, Users, Settings, Puzzle } from "lucide-react";
import { registerModules } from "../shared/registry";
import DashboardPage from "./dashboard/DashboardPage";
import ContentPage from "./content/ContentPage";
import MediaPage from "./media/MediaPage";
import UsersPage from "./users/UsersPage";
import SettingsPage from "./settings/SettingsPage";
import ExtensionsPage from "./extensions/ExtensionsPage";

// Built-in admin areas. Each is a module in the registry; the sidebar and router pick them up.
registerModules([
  { id: "dashboard", title: "Dashboard", path: "dashboard", icon: LayoutDashboard, Component: DashboardPage, group: "main", order: 10 },
  { id: "content", title: "Content", path: "content", icon: FileText, Component: ContentPage, group: "main", order: 20 },
  { id: "media", title: "Media", path: "media", icon: Image, Component: MediaPage, group: "main", order: 30 },
  { id: "users", title: "Users", path: "users", icon: Users, Component: UsersPage, group: "main", order: 40 },
  { id: "settings", title: "Settings", path: "settings", icon: Settings, Component: SettingsPage, group: "settings", order: 10 },
  { id: "extensions", title: "Extensions", path: "extensions", icon: Puzzle, Component: ExtensionsPage, group: "settings", order: 20 },
]);
