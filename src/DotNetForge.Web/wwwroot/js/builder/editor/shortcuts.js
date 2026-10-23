const SHORTCUTS = [
  { group: "General", keys: "Ctrl+S", label: "Save now" },
  { group: "General", keys: "Ctrl+Z / Ctrl+Shift+Z", label: "Undo / redo" },
  { group: "General", keys: "Ctrl+P", label: "Preview the draft" },
  { group: "General", keys: "?", label: "This list" },
  { group: "General", keys: "Esc", label: "Cancel a drag, stop editing text, select the parent" },
  { group: "Selection", keys: "Tab / Shift+Tab", label: "Next / previous sibling" },
  { group: "Selection", keys: "Enter / Shift+Enter", label: "Select the first child (or edit text) / the parent" },
  { group: "Selection", keys: "Ctrl+A", label: "Select all siblings" },
  { group: "Edit", keys: "Ctrl+C / Ctrl+X / Ctrl+V", label: "Copy / cut / paste" },
  { group: "Edit", keys: "Ctrl+D", label: "Duplicate" },
  { group: "Edit", keys: "Delete", label: "Delete" },
  { group: "Edit", keys: "Ctrl+G / Ctrl+Alt+G", label: "Group / frame selection (wrap in a container)" },
  { group: "Edit", keys: "Ctrl+Shift+G", label: "Ungroup (remove the container)" },
  { group: "Edit", keys: "Ctrl+R", label: "Rename the layer" },
  { group: "Layout", keys: "Shift+A", label: "Add auto layout (or wrap the selection in one)" },
  { group: "Layout", keys: "Alt+Shift+A", label: "Remove auto layout (free positioning)" },
  { group: "Layout", keys: "Alt+A / Alt+D", label: "Align left / right" },
  { group: "Layout", keys: "Alt+W / Alt+S", label: "Align top / bottom" },
  { group: "Layout", keys: "Alt+H / Alt+V", label: "Align horizontal / vertical centers" },
  { group: "Layout", keys: "Shift+H / Shift+V", label: "Flip horizontal / vertical" },
  { group: "Edit", keys: "Ctrl+Shift+H", label: "Hide or show" },
  { group: "Edit", keys: "Ctrl+Shift+L", label: "Lock or unlock" },
  { group: "Arrange", keys: "Arrows", label: "Move one grid cell, 1 px when free, reorder in a stack" },
  { group: "Arrange", keys: "Shift+Arrows", label: "Move further (2 cells / 10 px)" },
  { group: "Arrange", keys: "Ctrl+Arrows", label: "Make a grid item wider/narrower, taller/shorter" },
  { group: "Arrange", keys: "Ctrl+[ / Ctrl+]", label: "Send backward / bring forward (earlier / later in a stack)" },
  { group: "Arrange", keys: "Ctrl+Shift+[ / Ctrl+Shift+]", label: "Send to back / bring to front" },
  { group: "View", keys: "1 / 2 / 3", label: "Desktop / tablet / mobile" },
  { group: "View", keys: "Ctrl+= / Ctrl+- / Ctrl+0 / Shift+1", label: "Zoom in / out / 100% / fit" },
  { group: "View", keys: "Ctrl+'", label: "Show or hide the grid" }
];
function typing(e) {
  const t = e.target;
  if (!t) return false;
  return t.isContentEditable || t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT";
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
