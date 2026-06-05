import React from "react";
import ReactDOM from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { QueryClientProvider } from "@tanstack/react-query";

// HeroUI v3 needs no provider; its styles are imported via Tailwind in index.css.
import "./index.css";

// Side-effect imports: these register the core areas and the extensions into the module registry
// BEFORE the router is built from that registry.
import "./areas";
import "./extensions";

import { buildRouter } from "./routes/router";
import { queryClient } from "./app/queryClient";

const router = buildRouter();

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>,
);
