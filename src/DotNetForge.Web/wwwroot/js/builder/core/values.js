import { iconSchema } from "./schema-data.js";
const PAGE_ICONS = iconSchema.icons;
const HEX = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/;
const NUM = "[-+]?(?:\\d+\\.?\\d*|\\.\\d+)%?";
const FUNC_COLOR = new RegExp(`^(rgb|rgba|hsl|hsla)\\(\\s*${NUM}(?:deg)?\\s*(?:,\\s*${NUM}\\s*){2,3}\\)$|^(rgb|rgba|hsl|hsla)\\(\\s*${NUM}(?:deg)?(?:\\s+${NUM}){2}(?:\\s*\\/\\s*${NUM})?\\s*\\)$`);
const VAR_REF = /^var\(--[A-Za-z][A-Za-z0-9_-]{0,63}\)$/;
const COLOR_KEYWORDS = /* @__PURE__ */ new Set(["transparent", "currentColor", "inherit", "white", "black"]);
function color(value) {
  if (typeof value !== "string") return null;
  const v = value.trim();
  if (v.length === 0 || v.length > 100) return null;
  if (HEX.test(v) || VAR_REF.test(v) || COLOR_KEYWORDS.has(v)) return v;
  if (FUNC_COLOR.test(v)) return v.replace(/\s+/g, " ");
  return null;
}
const URL_FORBIDDEN = /[\s\u0000-\u001f\u007f"'<>\\`]/;
function prepareUrl(value) {
  return value.trim().replace(/ /g, "%20");
}
function linkUrl(value) {
  if (typeof value !== "string") return null;
  const v = prepareUrl(value);
  if (v.length === 0) return "";
  if (v.length > 2e3 || URL_FORBIDDEN.test(v)) return null;
  if (/^https?:\/\/[^/?#]+/i.test(v)) return v;
  if (/^\/(?![/\\])/.test(v) || v === "/") return v;
  if (/^[#?]/.test(v)) return v;
  if (/^mailto:[^/]/i.test(v) || /^tel:[+0-9()\-.]+$/i.test(v)) return v;
  return null;
}
function imageUrl(value) {
  if (typeof value !== "string") return null;
  const v = prepareUrl(value);
  if (v.length === 0) return "";
  if (v.length > 2e3 || URL_FORBIDDEN.test(v)) return null;
  if (/^https?:\/\/[^/?#]+/i.test(v)) return v;
  if (/^\/(?![/\\])/.test(v)) return v;
  return null;
}
function videoEmbed(value) {
  if (typeof value !== "string") return null;
  const v = value.trim();
  if (v.length === 0 || v.length > 500 || URL_FORBIDDEN.test(v)) return null;
  let m = /^https?:\/\/(?:www\.|m\.)?youtube(?:-nocookie)?\.com\/(?:watch\?(?:[^#]*&)?v=|embed\/|shorts\/|live\/)([A-Za-z0-9_-]{11})(?:[?&#].*)?$/i.exec(v);
  if (!m) m = /^https?:\/\/youtu\.be\/([A-Za-z0-9_-]{11})(?:[?#].*)?$/i.exec(v);
  if (m) return "https://www.youtube-nocookie.com/embed/" + m[1];
  m = /^https?:\/\/(?:www\.|player\.)?vimeo\.com\/(?:video\/)?(\d{6,12})(?:[/?#].*)?$/i.exec(v);
  if (m) return "https://player.vimeo.com/video/" + m[1] + "?dnt=1";
  return null;
}
function slug(value, max = 60) {
  if (typeof value !== "string") return null;
  const v = value.trim();
  if (v.length === 0) return "";
  if (v.length > max) return null;
  return /^[a-z][a-z0-9_-]*$/.test(v) ? v : null;
}
function classNames(value, max = 200) {
  if (typeof value !== "string") return null;
  const parts = value.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "";
  const joined = parts.join(" ");
  if (joined.length > max) return null;
  return parts.every((p) => /^-?[_a-zA-Z][_a-zA-Z0-9-]*$/.test(p) && !p.startsWith("pe-")) ? joined : null;
}
function varName(value) {
  if (typeof value !== "string") return null;
  return /^--[A-Za-z][A-Za-z0-9_-]{0,40}$/.test(value) ? value : null;
}
function varValue(value) {
  if (typeof value !== "string") return null;
  const v = value.trim();
  if (v.length === 0 || v.length > 200) return null;
  if (/[;{}<>\\"'`@\u0000-\u001f\u007f]/.test(v)) return null;
  if (/url\s*\(|expression\s*\(|image-set\s*\(|-moz-binding/i.test(v)) return null;
  if (v.includes("/*") || v.includes("*/")) return null;
  let depth = 0;
  for (const c of v) {
    if (c === "(") depth++;
    else if (c === ")" && --depth < 0) return null;
  }
  return depth === 0 ? v : null;
}
function track(value) {
  if (typeof value !== "string") return null;
  if (value === "auto") return "auto";
  const px = /^(\d{1,4})px$/.exec(value);
  if (px) return Number(px[1]) <= 4e3 ? Number(px[1]) + "px" : null;
  const fr = /^(\d{1,3}(?:\.\d{1,6})?)fr$/.exec(value);
  if (!fr) return null;
  const n = Math.round(Number(fr[1]) * 100) / 100;
  return n >= 0.01 && n <= 100 ? n + "fr" : null;
}
function num(value, min, max) {
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  const rounded = Math.round(value * 100) / 100;
  if (rounded < min || rounded > max) return null;
  return rounded === 0 ? 0 : rounded;
}
function int(value, min, max) {
  if (typeof value !== "number" || !Number.isInteger(value)) return null;
  return value < min || value > max ? null : value;
}
function clamp(value, min, max) {
  return value < min ? min : value > max ? max : value;
}
function text(value, max) {
  if (typeof value !== "string") return null;
  const v = value.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "");
  return v.length > max ? null : v;
}
function iconName(value) {
  if (typeof value !== "string") return null;
  if (value === "") return "";
  return Object.prototype.hasOwnProperty.call(PAGE_ICONS, value) ? value : null;
}
function nodeId(value) {
  return typeof value === "string" && /^[a-z0-9]{1,16}$/.test(value) ? value : null;
}
/** A content type or entry id (a GUID with hyphens), lower-cased; null for anything else. */
function reference(value) {
  return typeof value === "string" && /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(value) ? value.toLowerCase() : null;
}
/** A content field path (price, specs.width, $title), or "" for none; null when invalid. */
function fieldPath(value) {
  if (typeof value !== "string") return null;
  if (value === "") return "";
  return /^[$A-Za-z][$A-Za-z0-9_.]{0,119}$/.test(value) ? value : null;
}
export {
  PAGE_ICONS,
  clamp,
  classNames,
  color,
  fieldPath,
  iconName,
  imageUrl,
  int,
  linkUrl,
  nodeId,
  num,
  reference,
  slug,
  text,
  track,
  varName,
  varValue,
  videoEmbed
};
