import { Suspense, type CSSProperties } from "react";
import { Outlet, useHref, useNavigate } from "react-router-dom";
import { RouterProvider } from "react-aria-components";
import { Sidebar } from "@heroui-pro/react";
import { SidebarInner } from "./Sidebar";
import { Loading } from "../shared/ui";

/**
 * The admin shell: the HeroUI Pro <Sidebar> (compact style, driven by the module registry) plus the
 * active area in the outlet. No top bar - navigation and the account menu live in the sidebar.
 * react-aria's RouterProvider routes the sidebar links through React Router.
 */
export default function AdminLayout() {
  const navigate = useNavigate();

  return (
    <RouterProvider navigate={navigate} useHref={useHref}>
      <Sidebar.Provider>
        <Sidebar style={{ "--spacing": "0.2rem" } as CSSProperties}>
          <SidebarInner idPrefix="admin" />
          <Sidebar.Rail />
        </Sidebar>

        <Sidebar.Mobile>
          <SidebarInner idPrefix="admin-mobile" />
        </Sidebar.Mobile>

        <Sidebar.Main>
          <main className="h-screen overflow-y-auto p-6">
            <Suspense fallback={<Loading />}>
              <Outlet />
            </Suspense>
          </main>
        </Sidebar.Main>
      </Sidebar.Provider>
    </RouterProvider>
  );
}
