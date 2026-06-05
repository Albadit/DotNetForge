import { Sparkles } from "lucide-react";
import { PageHeader, Panel } from "../../shared/ui";

export default function HelloExtensionPage() {
  return (
    <>
      <PageHeader
        title="Hello Extension"
        description="An example admin extension that registered its own sidebar tab and page."
      />

      <Panel title="How extension pages work">
        <div className="flex items-start gap-3">
          <span className="flex size-9 items-center justify-center rounded-lg bg-amber-50 text-amber-600">
            <Sparkles size={18} />
          </span>
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              This page is provided by an extension, not by the core admin. It registered itself into the
              module registry, so the shell rendered a sidebar tab and this route at
              <code className="mx-1 rounded bg-slate-100 px-1.5 py-0.5 text-xs">/admin/extensions/hello</code>
              automatically.
            </p>
            <p>To add your own admin extension:</p>
            <pre className="overflow-x-auto rounded-lg bg-slate-900 p-4 text-xs leading-relaxed text-slate-100">{`// src/extensions/my-ext/index.ts
import { Boxes } from "lucide-react";
import { registerExtension } from "../../shared/registry";
import MyExtPage from "./MyExtPage";

registerExtension({
  id: "ext.my-ext",
  title: "My Extension",
  path: "extensions/my-ext",
  icon: Boxes,
  Component: MyExtPage,
});

// then add  import "./my-ext";  to src/extensions/index.ts`}</pre>
          </div>
        </div>
      </Panel>
    </>
  );
}
