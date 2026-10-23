const HTML_ESCAPES = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
function escapeHtml(value) {
  return value.replace(/[&<>"']/g, (c) => HTML_ESCAPES[c]);
}
function fmt(n) {
  const r = Math.round(n * 100) / 100;
  return String(r === 0 ? 0 : r);
}
function px(n) {
  return fmt(n) + "px";
}
function cssUrl(url) {
  return 'url("' + url.replace(/[()"'\\\s]/g, (c) => "%" + c.charCodeAt(0).toString(16).toUpperCase().padStart(2, "0")) + '")';
}
export {
  cssUrl,
  escapeHtml,
  fmt,
  px
};
