import { getComponent } from "../../components/registry.js";
import { updateProps } from "../../core/commands.js";
import { normalizeBlocks, normalizeRuns } from "../../core/richtext.js";
function collectRuns(node, marks, out) {
  for (const child of Array.from(node.childNodes)) {
    if (child.nodeType === 3) {
      const text = child.data.replace(/ /g, " ");
      if (text) out.push({ text, ...marks });
      continue;
    }
    if (child.nodeType !== 1) continue;
    const el = child;
    const tag = el.tagName;
    if (tag === "BR") {
      out.push({ text: "\n", ...marks });
      continue;
    }
    const m = { ...marks };
    if (tag === "B" || tag === "STRONG" || /font-weight:\s*(bold|[6-9]00)/.test(el.getAttribute("style") ?? "")) m.b = true;
    if (tag === "I" || tag === "EM" || /font-style:\s*italic/.test(el.getAttribute("style") ?? "")) m.i = true;
    if (tag === "U") m.u = true;
    if (tag === "S" || tag === "STRIKE" || tag === "DEL") m.s = true;
    if (tag === "CODE") m.code = true;
    if (tag === "A" && el.getAttribute("href")) {
      m.href = el.getAttribute("href");
      if (el.getAttribute("target") === "_blank") m.newTab = true;
    }
    if (tag === "DIV" || tag === "P") {
      if (out.length && !out[out.length - 1].text.endsWith("\n")) out.push({ text: "\n", ...marks });
    }
    collectRuns(el, m, out);
  }
}
function readInline(el) {
  const out = [];
  collectRuns(el, {}, out);
  while (out.length && /^\n+$/.test(out[out.length - 1].text)) out.pop();
  return normalizeRuns(out);
}
function readBlocks(el) {
  const blocks = [];
  let loose = [];
  const flush = () => {
    const runs = normalizeRuns(loose);
    if (runs.length) blocks.push({ type: "p", runs });
    loose = [];
  };
  for (const child of Array.from(el.childNodes)) {
    if (child.nodeType === 1) {
      const c = child;
      const tag = c.tagName;
      if (tag === "P" || tag === "DIV" || tag === "H1" || tag === "H2" || tag === "H3" || tag === "H4" || tag === "H5" || tag === "H6" || tag === "BLOCKQUOTE") {
        flush();
        const runs2 = [];
        collectRuns(c, {}, runs2);
        const type = tag === "H1" || tag === "H2" || tag === "H3" ? "h3" : tag === "H4" || tag === "H5" || tag === "H6" ? "h4" : tag === "BLOCKQUOTE" ? "quote" : "p";
        const clean = normalizeRuns(runs2).filter((r, i, a) => !(i === a.length - 1 && r.text === "\n"));
        blocks.push({ type, runs: clean });
        continue;
      }
      if (tag === "UL" || tag === "OL") {
        flush();
        const items = Array.from(c.children).filter((li) => li.tagName === "LI").map((li) => {
          const runs2 = [];
          collectRuns(li, {}, runs2);
          return normalizeRuns(runs2);
        });
        if (items.length) blocks.push({ type: tag === "UL" ? "ul" : "ol", items });
        continue;
      }
    }
    const runs = [];
    const holder = el.ownerDocument.createElement("span");
    holder.appendChild(child.cloneNode(true));
    collectRuns(holder, {}, runs);
    loose.push(...runs);
  }
  flush();
  return normalizeBlocks(blocks.filter((b) => !("runs" in b) || b.runs.length > 0 || blocks.length === 1));
}
class InlineEditor {
  constructor(session, canvas) {
    this.session = session;
    this.canvas = canvas;
  }
  session;
  canvas;
  id = null;
  el = null;
  original = "";
  cleanup = [];
  get active() {
    return this.id;
  }
  canEdit(id) {
    const node = this.session.store.doc.nodes[id];
    if (!node || node.locked || !this.session.caps.value.editContent) return false;
    const edit = getComponent(node.type)?.inlineEdit;
    if (!edit) return false;
    // Text bound to a content field shows the entry's value: it is changed in the Content Manager, not here.
    if (node.bind?.[edit.prop]) {
      this.session.toast("This text comes from content. Edit the entry in the Content Manager, or unbind it in the Content tab.", "info");
      return false;
    }
    return true;
  }
  start(id, at) {
    if (!this.canEdit(id)) return false;
    this.commit();
    const frame = this.canvas.frame;
    const el = frame?.elements.get(id);
    if (!frame || !el) return false;
    this.id = id;
    this.el = el;
    this.original = el.innerHTML;
    const doc = frame.document;
    el.contentEditable = "true";
    el.spellcheck = true;
    try {
      doc.execCommand("defaultParagraphSeparator", false, "p");
    } catch {
    }
    el.focus({ preventScroll: true });
    this.placeCaret(doc, el, at);
    const kind = getComponent(this.session.store.doc.nodes[id].type).inlineEdit.kind;
    const onKey = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        this.commit();
      } else if (e.key === "Enter" && kind !== "rich" && !(kind === "inline" && e.shiftKey)) {
        e.preventDefault();
        this.commit();
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        this.link();
      } else if ((e.ctrlKey || e.metaKey) && (e.key === "z" || e.key === "y")) {
        e.stopPropagation();
      }
    };
    const onPaste = (e) => {
      e.preventDefault();
      const text = e.clipboardData?.getData("text/plain") ?? "";
      doc.execCommand("insertText", false, kind === "text" ? text.replace(/\s*\n\s*/g, " ") : text);
    };
    const onDown = (e) => {
      if (!el.contains(e.target)) this.commit();
    };
    const onInput = () => this.canvas.invalidate();
    el.addEventListener("keydown", onKey);
    el.addEventListener("paste", onPaste);
    el.addEventListener("input", onInput);
    doc.addEventListener("pointerdown", onDown, true);
    this.cleanup = [
      () => el.removeEventListener("keydown", onKey),
      () => el.removeEventListener("paste", onPaste),
      () => el.removeEventListener("input", onInput),
      () => doc.removeEventListener("pointerdown", onDown, true)
    ];
    this.session.editingText.set(id);
    return true;
  }
  placeCaret(doc, el, at) {
    const sel = doc.getSelection();
    if (!sel) return;
    let range = null;
    if (at) {
      const d = doc;
      if (d.caretRangeFromPoint) range = d.caretRangeFromPoint(at.x, at.y);
      else if (d.caretPositionFromPoint) {
        const p = d.caretPositionFromPoint(at.x, at.y);
        if (p) {
          range = doc.createRange();
          range.setStart(p.offsetNode, p.offset);
        }
      }
      if (range && !el.contains(range.startContainer)) range = null;
    }
    if (!range) {
      range = doc.createRange();
      range.selectNodeContents(el);
      if (at) range.collapse(false);
    }
    sel.removeAllRanges();
    sel.addRange(range);
  }
  /** Applies a format to the current text selection (toolbar buttons). */
  format(command, value) {
    const doc = this.canvas.frame?.document;
    if (!doc || !this.el) return;
    this.el.focus({ preventScroll: true });
    doc.execCommand(command, false, value);
    this.canvas.invalidate();
  }
  link() {
    const doc = this.canvas.frame?.document;
    if (!doc || !this.el) return;
    const sel = doc.getSelection();
    const range = sel && sel.rangeCount ? sel.getRangeAt(0).cloneRange() : null;
    const current = sel?.anchorNode?.parentElement?.closest("a")?.getAttribute("href") ?? "";
    const url = window.prompt("Link address (https://…, /page or #anchor). Leave empty to remove the link.", current);
    if (url === null) return;
    this.el.focus({ preventScroll: true });
    if (range) {
      sel.removeAllRanges();
      sel.addRange(range);
    }
    if (url.trim() === "") doc.execCommand("unlink");
    else doc.execCommand("createLink", false, url.trim());
    this.canvas.invalidate();
  }
  commit() {
    const id = this.id;
    const el = this.el;
    if (!id || !el) return;
    this.finish();
    const node = this.session.store.doc.nodes[id];
    const spec = node ? getComponent(node.type)?.inlineEdit : void 0;
    if (!node || !spec) return;
    if (el.innerHTML === this.original) return;
    let value;
    if (spec.kind === "rich") value = readBlocks(el);
    else if (spec.kind === "inline") value = readInline(el);
    else value = (el.textContent ?? "").replace(/\s+/g, " ").trim();
    updateProps(this.session.store, id, { [spec.prop]: value });
    if (JSON.stringify(this.session.store.doc.nodes[id]?.props[spec.prop]) === JSON.stringify(node.props[spec.prop])) {
      el.innerHTML = this.original;
    }
  }
  cancel() {
    if (!this.el) return;
    this.el.innerHTML = this.original;
    this.finish();
  }
  finish() {
    for (const c of this.cleanup) c();
    this.cleanup = [];
    if (this.el) {
      this.el.removeAttribute("contenteditable");
      this.el.blur();
    }
    this.id = null;
    this.el = null;
    this.session.editingText.set(null);
    this.canvas.invalidate();
  }
}
export {
  InlineEditor,
  readBlocks,
  readInline
};
