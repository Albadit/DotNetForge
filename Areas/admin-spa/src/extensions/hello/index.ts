import { Sparkles } from "lucide-react";
import { registerExtension } from "../../shared/registry";
import HelloExtensionPage from "./HelloExtensionPage";

// Self-registration: this extension adds a sidebar tab (in the "Extensions" group) and a route at
// /admin/extensions/hello that renders its own page.
registerExtension({
  id: "ext.hello",
  title: "Hello Extension",
  path: "extensions/hello",
  icon: Sparkles,
  Component: HelloExtensionPage,
  order: 10,
});
