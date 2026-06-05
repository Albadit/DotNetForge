import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The SPA is served by the ASP.NET Core host under /admin and builds into wwwroot/admin.
export default defineConfig({
  base: "/admin/",
  plugins: [react(), tailwindcss()],
  build: {
    // Source lives in Areas/admin-spa; build output is served from wwwroot/admin (two levels up).
    outDir: "../../wwwroot/admin",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    // During `npm run dev`, proxy backend calls to the .NET host so cookies stay same-site.
    proxy: {
      "/admin-api": "http://localhost:5000",
      "/api": "http://localhost:5000",
      "/account": "http://localhost:5000",
    },
  },
});
