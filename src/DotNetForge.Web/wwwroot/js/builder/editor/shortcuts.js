import { t } from "../ui/i18n.js";
const SHORTCUTS = [
  { group: t("General"), keys: "Ctrl+S", label: t("Save now") },
  { group: t("General"), keys: "Ctrl+Z / Ctrl+Shift+Z", label: t("Undo / redo") },
  { group: t("General"), keys: "Ctrl+P", label: t("Preview the draft") },
  { group: t("General"), keys: "?", label: t("This list") },
  { group: t("General"), keys: "Esc", label: t("Cancel a drag, stop editing text, select the parent") },
  { group: t("Selection"), keys: "Tab / Shift+Tab", label: t("Next / previous sibling") },
  { group: t("Selection"), keys: "Enter / Shift+Enter", label: t("Select the first child (or edit text) / the parent") },
  { group: t("Selection"), keys: "Ctrl+A", label: t("Select all siblings") },
  { group: t("Edit"), keys: "Ctrl+C / Ctrl+X / Ctrl+V", label: t("Copy / cut / paste") },
  { group: t("Edit"), keys: "Ctrl+D", label: t("Duplicate") },
  { group: t("Edit"), keys: "Delete", label: t("Delete") },
  { group: t("Edit"), keys: "Ctrl+G / Ctrl+Alt+G", label: t("Group / frame selection (wrap in a container)") },
  { group: t("Edit"), keys: "Ctrl+Shift+G", label: t("Ungroup (remove the container)") },
  { group: t("Edit"), keys: "Ctrl+R", label: t("Rename the layer") },
  { group: t("Layout"), keys: "Shift+A", label: t("Add auto layout (or wrap the selection in one)") },
  { group: t("Layout"), keys: "Alt+Shift+A", label: t("Remove auto layout (free positioning)") },
  { group: t("Layout"), keys: "Alt+A / Alt+D", label: t("Align left / right") },
  { group: t("Layout"), keys: "Alt+W / Alt+S", label: t("Align top / bottom") },
  { group: t("Layout"), keys: "Alt+H / Alt+V", label: t("Align horizontal / vertical centers") },
  { group: t("Layout"), keys: "Shift+H / Shift+V", label: t("Flip horizontal / vertical") },
  { group: t("Edit"), keys: "Ctrl+Shift+H", label: t("Hide or show") },
  { group: t("Edit"), keys: "Ctrl+Shift+L", label: t("Lock or unlock") },
  { group: t("Arrange"), keys: "Arrows", label: t("Move one grid cell, 1 px when free, reorder in a stack") },
  { group: t("Arrange"), keys: "Shift+Arrows", label: t("Move further (2 cells / 10 px)") },
  { group: t("Arrange"), keys: "Ctrl+Arrows", label: t("Make a grid item wider/narrower, taller/shorter") },
  { group: t("Arrange"), keys: "Ctrl+[ / Ctrl+]", label: t("Send backward / bring forward (earlier / later in a stack)") },
  { group: t("Arrange"), keys: "Ctrl+Shift+[ / Ctrl+Shift+]", label: t("Send to back / bring to front") },
  { group: t("View"), keys: "1 / 2 / 3", label: t("Desktop / tablet / mobile") },
  { group: t("View"), keys: "Ctrl+= / Ctrl+- / Ctrl+0 / Shift+1", label: t("Zoom in / out / 100% / fit") },
  { group: t("View"), keys: "Ctrl+'", label: t("Show or hide the grid") }
];
function typing(e) {
  const target = e.target;
  if (!target) return false;
  return target.isContentEditable || target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT";
}
function installShortcuts(session, actions) {
  const handler = (e) => {
    const mod = e.ctrlKey || e.metaKey;
    const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    const letter = /^Key[A-Z]$/.test(e.code) ? e.code.slice(3).toLowerCase() : key;
    const bracket = e.code === "BracketRight" || key === "]" || key === "}" ? "]" : e.code === "BracketLeft" || key === "[" || key === "{" ? "[" : "";
    if (mod && key === "s") {
      e.preventDefault();
      void session.save(true);
      return;
    }
    if (key === "Escape") {
      if (!typing(e) || session.editingText.value) {
        actions.escape();
        e.preventDefault();
      }
      return;
    }
    if (typing(e) || session.editingText.value) return;
    if (session.dialog.value && key !== "?") return;
    const run = (fn) => {
      e.preventDefault();
      e.stopPropagation();
      fn();
    };
    if (mod) {
      if (e.altKey && letter === "g") return run(() => actions.wrap());
      if (e.shiftKey && bracket === "]") return run(() => actions.moveToEnd("front"));
      if (e.shiftKey && bracket === "[") return run(() => actions.moveToEnd("back"));
      if (key === "r" && !e.shiftKey && !e.altKey) return run(() => actions.rename());
      if (key === "z" && !e.shiftKey) return run(() => actions.undo());
      if (key === "z" && e.shiftKey || key === "y") return run(() => actions.redo());
      if (key === "c") return run(() => actions.copy());
      if (key === "x") return run(() => actions.cut());
      if (key === "v") return run(() => actions.paste());
      if (key === "d") return run(() => actions.duplicate());
      if (key === "a") return run(() => actions.selectAllSiblings());
      if (key === "g" && e.shiftKey) return run(() => actions.unwrap());
      if (key === "g") return run(() => actions.wrap());
      if (key === "h" && e.shiftKey) return run(() => actions.toggleHidden());
      if (key === "l" && e.shiftKey) return run(() => actions.toggleLocked());
      if (key === "p") return run(() => actions.preview());
      if (key === "[") return run(() => actions.reorder(-1));
      if (key === "]") return run(() => actions.reorder(1));
      if (key === "=" || key === "+") return run(() => actions.zoom(1.25));
      if (key === "-") return run(() => actions.zoom(-1));
      if (key === "0") return run(() => actions.zoom(1));
      if (key === "'") return run(() => session.showGrid.update((v) => !v));
      if (key === "ArrowLeft") return run(() => actions.resizeBy(-1, 0));
      if (key === "ArrowRight") return run(() => actions.resizeBy(1, 0));
      if (key === "ArrowUp") return run(() => actions.resizeBy(0, -1));
      if (key === "ArrowDown") return run(() => actions.resizeBy(0, 1));
      return;
    }
    if (e.altKey && e.shiftKey && letter === "a") return run(() => actions.removeAutoLayout());
    if (e.altKey && !e.shiftKey) {
      const edge = { a: "left", d: "right", w: "top", s: "bottom", h: "hcenter", v: "vcenter" }[letter];
      if (edge) return run(() => actions.align(edge));
    }
    if (e.shiftKey && !e.altKey && letter === "a") return run(() => actions.addAutoLayout());
    if (e.shiftKey && !e.altKey && (letter === "h" || letter === "v")) return run(() => actions.flip(letter === "h"));
    if (key === "Delete" || key === "Backspace") return run(() => actions.remove());
    if (key === "ArrowLeft") return run(() => actions.nudge(-1, 0, e.shiftKey));
    if (key === "ArrowRight") return run(() => actions.nudge(1, 0, e.shiftKey));
    if (key === "ArrowUp") return run(() => actions.nudge(0, -1, e.shiftKey));
    if (key === "ArrowDown") return run(() => actions.nudge(0, 1, e.shiftKey));
    if (key === "Enter" && e.shiftKey) return run(() => actions.selectParent());
    if (key === "Enter") return run(() => actions.selectChild());
    if (key === "Tab" && session.store.selection.length) return run(() => actions.selectSibling(e.shiftKey ? -1 : 1));
    if (key === "?") return run(() => session.dialog.set({ kind: "shortcuts" }));
    if (key === "!" || e.shiftKey && key === "1") return run(() => actions.zoom("fit"));
    if (key === "1") return run(() => session.breakpoint.set("base"));
    if (key === "2") return run(() => session.breakpoint.set("tablet"));
    if (key === "3") return run(() => session.breakpoint.set("mobile"));
  };
  window.addEventListener("keydown", handler);
  return () => window.removeEventListener("keydown", handler);
}
export {
  SHORTCUTS,
  installShortcuts
};
