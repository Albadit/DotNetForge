import { PAGE_ICONS } from "../core/values.js";
import { escapeHtml } from "./format.js";
const OPEN = '<svg class="pe-ico" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
function iconSvg(name, label = "") {
  const body = Object.prototype.hasOwnProperty.call(PAGE_ICONS, name) ? PAGE_ICONS[name] : "";
  if (!body) return "";
  const a11y = label ? ' role="img" aria-label="' + escapeHtml(label) + '"' : ' aria-hidden="true" focusable="false"';
  return OPEN + a11y + ">" + body + "</svg>";
}
export {
  iconSvg
};
