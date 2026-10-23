import { linkUrl } from "./values.js";
const MARKS = ["b", "i", "u", "s", "code"];
function sameMarks(a, b) {
  for (const m of MARKS) if (!!a[m] !== !!b[m]) return false;
  return (a.href ?? "") === (b.href ?? "") && !!a.newTab === !!b.newTab;
}
function normalizeRuns(input, maxRuns = 2e3) {
  if (!Array.isArray(input)) return [];
  const out = [];
  for (const raw of input.slice(0, maxRuns)) {
    if (!raw || typeof raw !== "object") continue;
    const r = raw;
    if (typeof r.text !== "string" || r.text.length === 0) continue;
    const run = { text: r.text.replace(/\r\n?/g, "\n") };
    for (const m of MARKS) if (r[m] === true) run[m] = true;
    if (typeof r.href === "string") {
      const href = linkUrl(r.href);
      if (href) {
        run.href = href;
        if (r.newTab === true) run.newTab = true;
      }
    }
    const last = out[out.length - 1];
    if (last && sameMarks(last, run)) last.text += run.text;
    else out.push(run);
  }
  return out;
}
function normalizeBlocks(input, maxBlocks = 500) {
  if (!Array.isArray(input)) return [];
  const out = [];
  for (const raw of input.slice(0, maxBlocks)) {
    if (!raw || typeof raw !== "object") continue;
    const b = raw;
    if (b.type === "ul" || b.type === "ol") {
      const items = Array.isArray(b.items) ? b.items.slice(0, 200).map((i) => normalizeRuns(i)) : [];
      if (items.length) out.push({ type: b.type, items });
    } else if (b.type === "p" || b.type === "h3" || b.type === "h4" || b.type === "quote") {
      out.push({ type: b.type, runs: normalizeRuns(b.runs) });
    }
  }
  return out;
}
function runsText(runs) {
  return (runs ?? []).map((r) => r.text).join("");
}
function blocksText(blocks) {
  return (blocks ?? []).map((b) => "items" in b ? b.items.map(runsText).join("\n") : runsText(b.runs)).join("\n\n");
}
function textLength(blocks) {
  let n = 0;
  for (const b of blocks) {
    if ("items" in b) for (const i of b.items) n += runsText(i).length;
    else n += runsText(b.runs).length;
  }
  return n;
}
function plainRuns(text) {
  return text ? [{ text }] : [];
}
export {
  blocksText,
  normalizeBlocks,
  normalizeRuns,
  plainRuns,
  runsText,
  textLength
};
