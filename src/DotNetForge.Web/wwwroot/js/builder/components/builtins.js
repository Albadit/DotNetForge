import { COMPONENTS } from "../core/schema.js";
import * as V from "../core/values.js";
import { escapeHtml, px } from "../renderer/format.js";
import { iconSvg } from "../renderer/icons.js";
import { blocksHtml, NEW_TAB, runsHtml } from "../renderer/richtext.js";
import { registerComponent } from "./registry.js";
function schema(type) {
  return COMPONENTS[type];
}
function choice(node, prop) {
  const def = schema(node.type).props[prop];
  const value = node.props[prop];
  return typeof value === "string" && def.options?.includes(value) ? value : String(def.default);
}
function str(node, prop) {
  const v = node.props[prop];
  return typeof v === "string" ? v : "";
}
function flag(node, prop) {
  return node.props[prop] === true;
}
const newTabAttrs = (on) => on ? [["target", "_blank"], ["rel", "noopener noreferrer"]] : [];
function avatarHtml(src) {
  return src ? '<img class="pe-post-avatar" src="' + escapeHtml(src) + '" alt="" loading="lazy" decoding="async">' : '<span class="pe-post-avatar"></span>';
}
const builtins = [
  {
    type: "page",
    schema: schema("page"),
    tag: () => "div",
    content: (_n, _c, children) => children,
    icon: "page"
  },
  {
    type: "section",
    schema: schema("section"),
    layoutTarget: "inner",
    tag: (n) => choice(n, "tag"),
    attrs: (n) => {
      const anchor = V.slug(n.props.anchor);
      return anchor ? [["id", anchor]] : [];
    },
    content: (_n, _c, children) => '<div class="pe-in">' + children + "</div>",
    css: (n) => {
      const w = V.int(n.props.contentWidth, 0, 3840);
      return w ? { inner: [["max-width", px(w)], ["margin-inline", "auto"]] } : {};
    },
    icon: "section",
    description: "A full-width band with its own background and a centered content area."
  },
  {
    type: "container",
    schema: schema("container"),
    tag: (n) => V.linkUrl(n.props.href) ? "a" : choice(n, "tag"),
    attrs: (n) => {
      const href = V.linkUrl(n.props.href);
      return href ? [["href", href]] : [];
    },
    content: (_n, _c, children) => children,
    icon: "frame",
    description: "A box that arranges its content as a stack, a grid or freely."
  },
  {
    type: "heading",
    schema: schema("heading"),
    tag: (n) => "h" + choice(n, "level"),
    content: (n) => runsHtml(n.props.text),
    icon: "heading",
    inlineEdit: { prop: "text", kind: "inline" },
    description: "A title, h1 to h6."
  },
  {
    type: "text",
    schema: schema("text"),
    tag: () => "div",
    content: (n) => blocksHtml(n.props.content),
    icon: "text",
    inlineEdit: { prop: "content", kind: "rich" },
    description: "Paragraphs and lists with bold, italic and links."
  },
  {
    type: "image",
    schema: schema("image"),
    tag: () => "figure",
    classes: (n) => ["pe-fit-" + choice(n, "fit")],
    content: (n, ctx) => {
      const src = V.imageUrl(n.props.src);
      const caption = str(n, "caption");
      let html = "";
      if (src) {
        const img = '<img src="' + escapeHtml(src) + '" alt="' + escapeHtml(str(n, "alt")) + '" loading="lazy" decoding="async">';
        const href = V.linkUrl(n.props.href);
        html = href ? '<a href="' + escapeHtml(href) + '"' + (flag(n, "newTab") ? NEW_TAB : "") + ">" + img + "</a>" : img;
      } else if (ctx.mode === "canvas") {
        html = '<div class="pe-empty pe-empty-image"></div>';
      }
      if (caption) html += "<figcaption>" + escapeHtml(caption) + "</figcaption>";
      return html;
    },
    icon: "image",
    description: "A picture from the site's files or a web address."
  },
  {
    type: "button",
    schema: schema("button"),
    tag: () => "a",
    classes: (n) => ["pe-btn", "pe-btn-" + choice(n, "variant")],
    attrs: (n) => [["href", V.linkUrl(n.props.href) || "#"], ...newTabAttrs(flag(n, "newTab"))],
    content: (n) => {
      const label = escapeHtml(str(n, "label"));
      const icon = iconSvg(str(n, "icon"));
      if (!icon) return label;
      return choice(n, "iconPosition") === "end" ? label + icon : icon + label;
    },
    icon: "button",
    inlineEdit: { prop: "label", kind: "text" },
    description: "A call to action that links somewhere."
  },
  {
    type: "links",
    schema: schema("links"),
    tag: () => "nav",
    classes: (n) => {
      const variant = choice(n, "variant");
      return variant === "list" ? ["pe-links-" + choice(n, "direction")] : ["pe-links-" + choice(n, "direction"), "pe-links-" + variant];
    },
    content: (n) => {
      const items = Array.isArray(n.props.items) ? n.props.items : [];
      const lis = items.filter((i) => i && typeof i.label === "string").map((i) => {
        const href = V.linkUrl(i.href) || "#";
        return '<li><a href="' + escapeHtml(href) + '"' + (i.newTab === true ? NEW_TAB : "") + ">" + (typeof i.icon === "string" ? iconSvg(i.icon) : "") + escapeHtml(i.label) + "</a></li>";
      }).join("");
      return "<ul>" + lis + "</ul>";
    },
    icon: "links",
    description: "A list of links, vertical or in a row."
  },
  {
    type: "icon",
    schema: schema("icon"),
    tag: () => "span",
    content: (n) => iconSvg(str(n, "name"), str(n, "label")),
    css: (n) => {
      const size = px(V.int(n.props.size, 8, 256) ?? 24);
      return { self: [["width", size], ["height", size]] };
    },
    icon: "icon",
    description: "A single icon, for menus, headers and buttons."
  },
  {
    type: "post",
    schema: schema("post"),
    tag: () => "article",
    content: (n, ctx) => {
      const target = "pe-post-" + n.id;
      const title = str(n, "title");
      const author = str(n, "author");
      const head = '<div class="pe-post-head">' + avatarHtml(V.imageUrl(n.props.avatar) ?? "") + '<div class="pe-post-by"><strong>' + escapeHtml(author) + "</strong>" + (str(n, "meta") ? "<span>" + escapeHtml(str(n, "meta")) + "</span>" : "") + "</div></div>";
      const src = V.imageUrl(n.props.image);
      const media = src ? '<img class="pe-post-img" src="' + escapeHtml(src) + '" alt="' + escapeHtml(str(n, "alt")) + '" loading="lazy" decoding="async">' : ctx.mode === "canvas" ? '<div class="pe-post-img pe-empty pe-empty-image"></div>' : "";
      const heading = title ? '<h3 class="pe-post-title">' + escapeHtml(title) + "</h3>" : "";
      const body = '<div class="pe-post-text">' + blocksHtml(n.props.text) + "</div>";
      const comments = Array.isArray(n.props.comments) ? n.props.comments.filter((c) => c && typeof c.text === "string") : [];
      const count = comments.length;
      const foot = '<a class="pe-post-open" href="#' + target + '">' + iconSvg("comment") + "<span>" + escapeHtml(count ? str(n, "commentsLabel") : str(n, "emptyLabel")) + "</span>" + (count ? '<span class="pe-post-count">' + count + "</span>" : "") + "</a>";
      const list = count ? '<ul class="pe-post-comments">' + comments.map((c) => "<li>" + avatarHtml(V.imageUrl(c.avatar) ?? "") + '<div class="pe-post-bubble">' + (c.author ? "<strong>" + escapeHtml(c.author) + "</strong>" : "") + (c.meta ? "<span>" + escapeHtml(c.meta) + "</span>" : "") + "<p>" + escapeHtml(c.text) + "</p></div></li>").join("") + "</ul>" : "";
      const close = escapeHtml(str(n, "closeLabel") || "Close");
      const dialog = '<div class="pe-post-dialog" id="' + target + '" role="dialog" aria-modal="true" aria-label="' + escapeHtml(title || author) + '"><a class="pe-post-backdrop" href="#_" aria-label="' + close + '"></a><div class="pe-post-sheet"><a class="pe-post-close" href="#_" aria-label="' + close + '">' + iconSvg("close") + "</a>" + head + media + heading + body + list + "</div></div>";
      return head + media + heading + body + foot + dialog;
    },
    icon: "post",
    description: "A news or timeline post with a picture; a click opens it with its comments."
  },
  {
    type: "video",
    schema: schema("video"),
    tag: () => "div",
    classes: (n) => ["pe-ar-" + choice(n, "aspect").replace(":", "-")],
    content: (n, ctx) => {
      const embed = V.videoEmbed(n.props.url);
      if (!embed) return ctx.mode === "canvas" ? '<div class="pe-empty pe-empty-video"></div>' : "";
      return '<iframe src="' + escapeHtml(embed) + '" title="' + escapeHtml(str(n, "title")) + '" loading="lazy" allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen" allowfullscreen referrerpolicy="strict-origin-when-cross-origin"></iframe>';
    },
    icon: "video",
    description: "A YouTube or Vimeo video, loaded only when visible."
  },
  {
    type: "spacer",
    schema: schema("spacer"),
    tag: () => "div",
    attrs: () => [["aria-hidden", "true"]],
    content: () => "",
    icon: "spacer",
    description: "Empty space of a fixed height."
  },
  {
    type: "divider",
    schema: schema("divider"),
    tag: () => "hr",
    voidElement: true,
    css: (n) => {
      const t = V.int(n.props.thickness, 1, 20) ?? 1;
      return { self: [["border-top", px(t) + " " + choice(n, "lineStyle") + " currentColor"]] };
    },
    icon: "divider",
    description: "A horizontal line."
  },
  {
    type: "module",
    schema: schema("module"),
    tag: () => "div",
    attrs: (n) => [["data-pe-module", String(V.int(n.props.moduleId, 0, 2147483647) ?? 0)]],
    content: (n) => "<!--pe:module:" + String(V.int(n.props.moduleId, 0, 2147483647) ?? 0) + "-->",
    icon: "module",
    description: "An existing DNN module on this page."
  }
];
let registered = false;
function registerBuiltins() {
  if (registered) return;
  registered = true;
  for (const c of builtins) registerComponent(c);
}
registerBuiltins();
export {
  registerBuiltins
};
