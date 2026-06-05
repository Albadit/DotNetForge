import { ChevronDown, LogOut, Puzzle } from "lucide-react";
import { useLocation } from "react-router-dom";
import { Avatar, Chip, Dropdown, Label } from "@heroui/react";
import { Sidebar } from "@heroui-pro/react";
import { getModulesByGroup } from "../shared/registry";
import { useMe, useAdminExtensions, type Me } from "../shared/hooks";
import { apiPost } from "../shared/api";
import type { ModuleGroup } from "../shared/types";

// Static groups come from the registry (core areas). The "Admin Extensions" group is dynamic - it is
// built from the backend's extensions/admin/ folder, so dropping a manifest adds a tab automatically.
const GROUPS: { id: ModuleGroup; label: string }[] = [
  { id: "main", label: "Main" },
  { id: "settings", label: "Settings" },
];

function initialsOf(me: Me | undefined): string {
  const src = (me?.name?.trim() || me?.email || "").trim();
  if (!src) return "DF";
  const parts = src.split(/[\s@.]+/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return src.slice(0, 2).toUpperCase();
}

/**
 * The admin sidebar content (header → registry-driven nav groups → separator → user dropdown footer),
 * built with the HeroUI Pro <Sidebar> compound. Rendered for both the desktop and mobile sidebars by
 * AdminLayout; `idPrefix` keeps the two instances' collection item ids unique.
 */
export function SidebarInner({ idPrefix }: { idPrefix: string }) {
  const { pathname } = useLocation();
  const { data: me } = useMe();
  const { data: adminExtensions = [] } = useAdminExtensions();

  async function logout() {
    try {
      await apiPost("/logout");
    } finally {
      window.location.href = "/account/login";
    }
  }

  return (
    <>
      <Sidebar.Header>
        <div className="flex items-center gap-3 px-1 py-2">
          <div className="bg-accent flex size-6 shrink-0 items-center justify-center rounded-md">
            <span className="text-sm font-bold text-white">D</span>
          </div>
          <span className="text-foreground text-sm font-semibold" data-sidebar="label">
            DotNetForge
          </span>
        </div>
      </Sidebar.Header>

      <Sidebar.Content>
        {GROUPS.map((group) => {
          const modules = getModulesByGroup(group.id);
          if (modules.length === 0) return null;

          return (
            <Sidebar.Group key={group.id}>
              <Sidebar.GroupLabel>{group.label}</Sidebar.GroupLabel>
              <Sidebar.Menu aria-label={group.label}>
                {modules.map((m) => {
                  const to = `/admin/${m.path}`;
                  return (
                    <Sidebar.MenuItem
                      key={m.id}
                      id={`${idPrefix}-${m.id}`}
                      href={to}
                      isCurrent={pathname === to}
                      textValue={m.title}
                    >
                      <Sidebar.MenuIcon>
                        <m.icon className="size-4 shrink-0" />
                      </Sidebar.MenuIcon>
                      <Sidebar.MenuLabel>{m.title}</Sidebar.MenuLabel>
                    </Sidebar.MenuItem>
                  );
                })}
              </Sidebar.Menu>
            </Sidebar.Group>
          );
        })}

        {adminExtensions.length > 0 ? (
          <Sidebar.Group>
            <Sidebar.GroupLabel>Admin Extensions</Sidebar.GroupLabel>
            <Sidebar.Menu aria-label="Admin Extensions">
              {adminExtensions.map((ext) => {
                const to = `/admin/ext/${ext.id}`;
                return (
                  <Sidebar.MenuItem
                    key={ext.id}
                    id={`${idPrefix}-ext-${ext.id}`}
                    href={to}
                    isCurrent={pathname === to}
                    textValue={ext.name}
                  >
                    <Sidebar.MenuIcon>
                      <Puzzle className="size-4 shrink-0" />
                    </Sidebar.MenuIcon>
                    <Sidebar.MenuLabel>{ext.name}</Sidebar.MenuLabel>
                  </Sidebar.MenuItem>
                );
              })}
            </Sidebar.Menu>
          </Sidebar.Group>
        ) : null}
      </Sidebar.Content>

      <Sidebar.Separator />

      <Sidebar.Footer className="px-2 pb-2 pt-0">
        <Dropdown>
          <Dropdown.Trigger className="hover:bg-default flex w-full items-center gap-2 rounded-xl px-2 py-1.5 text-left outline-none">
            <Avatar size="sm">
              <Avatar.Fallback>{initialsOf(me)}</Avatar.Fallback>
            </Avatar>
            <span className="text-foreground truncate text-sm font-medium" data-sidebar="label">
              {me?.name || me?.email || "Account"}
            </span>
            <ChevronDown className="text-muted ml-auto size-3 shrink-0" data-sidebar="label" />
          </Dropdown.Trigger>
          <Dropdown.Popover placement="top start">
            <div className="px-3 pb-1 pt-3">
              <div className="flex items-center gap-2">
                <Avatar size="sm">
                  <Avatar.Fallback>{initialsOf(me)}</Avatar.Fallback>
                </Avatar>
                <div className="flex flex-col gap-0">
                  <p className="text-sm font-medium leading-5">{me?.name || me?.email || "-"}</p>
                  <p className="text-muted text-xs leading-none">{me?.tenant ? `Tenant: ${me.tenant}` : ""}</p>
                </div>
              </div>
            </div>
            <Dropdown.Menu
              aria-label="Account"
              onAction={(key) => {
                if (key === `${idPrefix}-dd-logout`) {
                  void logout();
                }
              }}
            >
              <Dropdown.Item id={`${idPrefix}-dd-dashboard`} href="/admin/dashboard" textValue="Dashboard">
                <Label>Dashboard</Label>
              </Dropdown.Item>
              <Dropdown.Item id={`${idPrefix}-dd-settings`} href="/admin/settings" textValue="Settings">
                <Label>Settings</Label>
              </Dropdown.Item>
              <Dropdown.Item id={`${idPrefix}-dd-logout`} textValue="Log Out" variant="danger">
                <div className="flex w-full items-center justify-between gap-2">
                  <Label>Log Out</Label>
                  <LogOut className="text-danger size-3.5" />
                </div>
              </Dropdown.Item>
            </Dropdown.Menu>
          </Dropdown.Popover>
        </Dropdown>
      </Sidebar.Footer>
    </>
  );
}
