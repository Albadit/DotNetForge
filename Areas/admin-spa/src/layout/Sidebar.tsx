import { NavLink } from "react-router-dom";
import { getModulesByGroup } from "../shared/registry";
import type { ModuleGroup } from "../shared/types";

const GROUPS: { id: ModuleGroup; label: string }[] = [
  { id: "main", label: "Main" },
  { id: "settings", label: "Settings" },
  { id: "extensions", label: "Extensions" },
];

export function Sidebar() {
  return (
    <aside className="flex h-screen w-64 shrink-0 flex-col overflow-y-auto bg-slate-900 px-3 py-4 text-slate-300">
      <div className="px-2 pb-1 text-lg font-bold text-white">DotNetForge</div>
      <div className="px-2 pb-4 text-xs text-slate-500">Admin</div>

      <nav className="flex-1 space-y-6">
        {GROUPS.map((group) => {
          const modules = getModulesByGroup(group.id);
          if (modules.length === 0) return null;
          return (
            <div key={group.id}>
              <div className="px-2 pb-1 text-[0.7rem] font-semibold uppercase tracking-wide text-slate-500">
                {group.label}
              </div>
              <div className="space-y-0.5">
                {modules.map((m) => (
                  <NavLink
                    key={m.id}
                    to={`/${m.path}`}
                    end
                    className={({ isActive }) =>
                      `flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition-colors ${
                        isActive ? "bg-indigo-600 text-white" : "text-slate-300 hover:bg-slate-800 hover:text-white"
                      }`
                    }
                  >
                    <m.icon size={18} />
                    <span>{m.title}</span>
                  </NavLink>
                ))}
              </div>
            </div>
          );
        })}
      </nav>

      <div className="px-2 pt-4 text-xs text-slate-500">.NET 10 · Hybrid CMS</div>
    </aside>
  );
}
