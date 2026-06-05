import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

/**
 * The admin shell: a fixed, scrollable sidebar built from the module registry, a top bar, and the
 * active area rendered into the outlet. The shell is area-agnostic - it renders whatever modules are
 * registered, including extensions.
 */
export default function AdminLayout() {
  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar />
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
