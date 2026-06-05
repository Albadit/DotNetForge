import { createBrowserRouter, Navigate } from "react-router-dom";
import AdminLayout from "../layout/AdminLayout";
import { getModules } from "../shared/registry";
import { NotFoundPage } from "./NotFoundPage";

/**
 * Builds the router from the module registry. Every registered area/extension becomes a child route
 * under the admin layout, so routing requires no edits when modules are added. The router is mounted
 * at basename "/admin" to match how the ASP.NET Core host serves the SPA.
 */
export function buildRouter() {
  const modules = getModules();

  return createBrowserRouter(
    [
      {
        path: "/",
        element: <AdminLayout />,
        children: [
          { index: true, element: <Navigate to="/dashboard" replace /> },
          ...modules.map((m) => ({ path: m.path, element: <m.Component /> })),
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
    { basename: "/admin" },
  );
}
