import { createBrowserRouter, Navigate } from "react-router-dom";
import AdminLayout from "../layout/AdminLayout";
import SetupPage from "../setup/SetupPage";
import ExtensionHost from "../areas/extensions/ExtensionHost";
import { getModules } from "../shared/registry";
import { NotFoundPage } from "./NotFoundPage";

/**
 * Builds the router from the module registry. Every registered area/extension becomes a child route
 * under the admin layout. Routes use the full "/admin/..." prefix (no basename) so links resolve to
 * the correct URL whether navigation is client-side or a full reload - matching how the ASP.NET Core
 * host serves the SPA at /admin.
 */
export function buildRouter() {
  const modules = getModules();

  return createBrowserRouter([
    // First-run setup wizard, rendered standalone (no admin layout).
    { path: "/setup", element: <SetupPage /> },
    {
      path: "/admin",
      element: <AdminLayout />,
      children: [
        { index: true, element: <Navigate to="/admin/dashboard" replace /> },
        ...modules.map((m) => ({ path: m.path, element: <m.Component /> })),
        // One dynamic route serves every backend admin extension (extensions/admin/*).
        { path: "ext/:id", element: <ExtensionHost /> },
        { path: "*", element: <NotFoundPage /> },
      ],
    },
  ]);
}
