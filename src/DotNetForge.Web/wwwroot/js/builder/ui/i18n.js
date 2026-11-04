// The editor's text (.docs/features/internationalization.md#admin-language): t("Saved") or t("{0} selected", n) gives
// the English text, or its translation from the page's <meta name="dnf-text"> (written by PageBuilder/Index.cshtml in
// the admin language). The English text is the key; a text the catalog lacks stays English. Without a document (Node
// tests) everything stays English.
let texts = null;

function catalog() {
  if (texts) return texts;
  texts = {};
  if (typeof document === "undefined") return texts;
  const meta = document.querySelector("meta[name='dnf-text']");
  try {
    const parsed = meta ? JSON.parse(meta.getAttribute("content") || "{}") : {};
    if (parsed && typeof parsed === "object") texts = parsed;
  } catch {
    /* a broken catalog keeps English */
  }
  return texts;
}

/** The text in the admin language; {0}, {1}... are replaced with the arguments. */
export function t(text, ...args) {
  const all = catalog();
  const shown = Object.prototype.hasOwnProperty.call(all, text) && typeof all[text] === "string" ? all[text] : text;
  return args.length === 0 ? shown : shown.replace(/\{(\d+)\}/g, (m, i) => (i < args.length ? String(args[i]) : m));
}
