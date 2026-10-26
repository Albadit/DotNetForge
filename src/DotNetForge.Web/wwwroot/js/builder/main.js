// Page builder entry point (.docs/features/page-builder.md#editor). Loaded as a module by the full-screen editor
// screen (Areas/Admin/Views/PageBuilder/Index.cshtml), which passes its configuration as data-* attributes on
// #pe-editor-root. Everything below is plain JavaScript ported from the Dnn.Libraries.PageEditor sources: the core
// (store, commands, renderer, canvas) and the UI (ui/, the reference's components one to one, rendered by ui/vdom.js).
import "./components/builtins.js";
import { CanvasController } from "./editor/canvas/controller.js";
import { InlineEditor } from "./editor/canvas/inline-edit.js";
import { Session } from "./editor/session.js";
import { signal } from "./core/signal.js";
import { h, render } from "./ui/vdom.js";
import { App } from "./ui/app.js";

const host = document.getElementById("pe-editor-root");
const data = host.dataset;
const config = {
  api: data.api,
  tabId: data.pageId,
  canvasUrl: data.canvasUrl,
  previewUrl: data.previewUrl,
  backUrl: data.backUrl,
  canUpload: data.canUpload === "true",
};

const session = new Session(config);
const canvas = new CanvasController(session);
const inline = new InlineEditor(session, canvas);
// The canvas shows the page light or dark (the editor's ⋯ menu), independent of the editor's own theme.
const canvasTheme = signal(storedCanvasTheme());
canvasTheme.subscribe((value) => {
  try {
    localStorage.setItem("pe.canvasTheme", value);
  } catch {
    /* storage unavailable */
  }
});
const ctx = { session, canvas, inline, canvasTheme };
// Light or dark like the admin screens; follows a change made there in another tab.
session.followAdminTheme();

function storedCanvasTheme() {
  try {
    return localStorage.getItem("pe.canvasTheme") === "dark" ? "dark" : "light";
  } catch {
    return "light";
  }
}

window.addEventListener("beforeunload", (e) => {
  if (session.isDirty) {
    void session.save();
    e.preventDefault();
    e.returnValue = "";
  }
});

/** Saves pending changes, then goes back to the page on the Pages screen. */
async function close() {
  if (session.isDirty) await session.save();
  const state = session.saveState.value;
  if ((state === "error" || state === "offline" || state === "conflict") && !window.confirm("Some changes could not be saved. Leave anyway and lose them?")) return;
  session.dispose();
  location.assign(config.backUrl);
}

render(h(App, { ctx, onClose: () => void close() }), host);
void session.load();
