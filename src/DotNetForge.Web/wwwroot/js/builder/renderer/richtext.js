import { normalizeBlocks, normalizeRuns } from "../core/richtext.js";
import { escapeHtml } from "./format.js";
const NEW_TAB = ' target="_blank" rel="noopener noreferrer"';
function runHtml(run) {
  let html = escapeHtml(run.text).replace(/\n/g, "<br>");
  if (run.code) html = "<code>" + html + "</code>";
  if (run.s) html = "<s>" + html + "</s>";
  if (run.u) html = "<u>" + html + "</u>";
  if (run.i) html = "<em>" + html + "</em>";
  if (run.b) html = "<strong>" + html + "</strong>";
  if (run.href) html = '<a href="' + escapeHtml(run.href) + '"' + (run.newTab ? NEW_TAB : "") + ">" + html + "</a>";
  return html;
}
function runsHtml(runs) {
  return normalizeRuns(runs).map(runHtml).join("");
}
function blockHtml(block) {
  switch (block.type) {
    case "ul":
    case "ol":
      return "<" + block.type + ">" + block.items.map((i) => "<li>" + i.map(runHtml).join("") + "</li>").join("") + "</" + block.type + ">";
    case "quote":
      return "<blockquote><p>" + block.runs.map(runHtml).join("") + "</p></blockquote>";
    default:
      return "<" + block.type + ">" + block.runs.map(runHtml).join("") + "</" + block.type + ">";
  }
}
function blocksHtml(blocks) {
  return normalizeBlocks(blocks).map(blockHtml).join("");
}
export {
  NEW_TAB,
  blocksHtml,
  runHtml,
  runsHtml
};
