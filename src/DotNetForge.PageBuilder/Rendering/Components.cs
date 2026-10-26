using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DotNetForge.PageBuilder.Rendering
{
    /// <summary>Where a container's layout (flex/grid) applies: the element itself or its inner wrapper (.pe-in).</summary>
    public enum LayoutTarget
    {
        Self,
        Inner,
    }

    /// <summary>Extra declarations a component adds to its own rule and to its inner wrapper's rule.</summary>
    public sealed class ComponentCss
    {
        private static readonly IReadOnlyList<KeyValuePair<string, string>> None = new KeyValuePair<string, string>[0];

        public ComponentCss(IEnumerable<KeyValuePair<string, string>> self = null, IEnumerable<KeyValuePair<string, string>> inner = null)
        {
            Self = self?.ToList() ?? None;
            Inner = inner?.ToList() ?? None;
        }

        public IReadOnlyList<KeyValuePair<string, string>> Self { get; }

        public IReadOnlyList<KeyValuePair<string, string>> Inner { get; }
    }

    /// <summary>
    /// The server half of a component (the editor's ComponentRenderer in
    /// components/registry.ts). Both halves must produce the same markup; the
    /// schema they validate against is registered with
    /// <see cref="ComponentRegistry.Register(IComponentRenderer, JObject)"/>.
    /// </summary>
    public interface IComponentRenderer
    {
        string Type { get; }

        LayoutTarget LayoutTarget { get; }

        /// <summary>An element without content or end tag (hr): children and content are not written.</summary>
        bool VoidElement { get; }

        string Tag(PageNode node);

        /// <summary>Classes after pe-node pe-t-{type} pe-n-{id}.</summary>
        IEnumerable<string> Classes(PageNode node);

        /// <summary>Attributes after class; values are escaped by the renderer.</summary>
        IEnumerable<KeyValuePair<string, string>> Attributes(PageNode node, RenderContext context);

        /// <summary>The element's inner markup; <paramref name="children"/> is the children's markup.</summary>
        string Content(PageNode node, RenderContext context, string children);

        /// <summary>Declarations added after the node's layout, placement and style; null for none.</summary>
        ComponentCss Css(PageNode node);
    }

    /// <summary>Defaults for the optional parts of a component, as in the editor (no classes, no attributes, content = children).</summary>
    public abstract class ComponentRenderer : IComponentRenderer
    {
        private static readonly string[] NoClasses = new string[0];
        private static readonly KeyValuePair<string, string>[] NoAttributes = new KeyValuePair<string, string>[0];

        public abstract string Type { get; }

        public virtual LayoutTarget LayoutTarget => LayoutTarget.Self;

        public virtual bool VoidElement => false;

        public abstract string Tag(PageNode node);

        public virtual IEnumerable<string> Classes(PageNode node)
        {
            return NoClasses;
        }

        public virtual IEnumerable<KeyValuePair<string, string>> Attributes(PageNode node, RenderContext context)
        {
            return NoAttributes;
        }

        public virtual string Content(PageNode node, RenderContext context, string children)
        {
            return children;
        }

        public virtual ComponentCss Css(PageNode node)
        {
            return null;
        }
    }

    /// <summary>
    /// The component renderers by type. The built-ins are registered statically;
    /// extensions register theirs (with the schema the validator needs) at
    /// application start. Built-in types cannot be replaced, so an extension
    /// cannot change how core content renders or is validated.
    /// </summary>
    public static class ComponentRegistry
    {
        private static readonly Regex TypePattern = new Regex(@"^[a-z][a-z0-9-]{1,40}\z", RegexOptions.CultureInvariant);
        private static readonly ConcurrentDictionary<string, IComponentRenderer> Renderers = new ConcurrentDictionary<string, IComponentRenderer>(StringComparer.Ordinal);

        static ComponentRegistry()
        {
            foreach (var renderer in BuiltInComponents.Create()) Renderers[renderer.Type] = renderer;
        }

        /// <summary>The renderer of a type, or null.</summary>
        public static IComponentRenderer Get(string type)
        {
            return type != null && Renderers.TryGetValue(type, out var renderer) ? renderer : null;
        }

        public static IReadOnlyCollection<IComponentRenderer> All => Renderers.Values.ToList();

        /// <summary>
        /// Registers an extension component together with its schema (shaped like an
        /// entry of components.json "types": label, container, parents, props...).
        /// Registering the same extension type again replaces it.
        /// </summary>
        public static void Register(IComponentRenderer renderer, JObject schema)
        {
            CheckType(renderer);
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            Schema.DefineComponent(renderer.Type, schema);
            Renderers[renderer.Type] = renderer;
        }

        /// <summary>Registers a renderer for an extension type whose schema was registered before.</summary>
        public static void Register(IComponentRenderer renderer)
        {
            CheckType(renderer);
            if (Schema.Component(renderer.Type) == null) throw new ArgumentException("The component type \"" + renderer.Type + "\" has no schema; register it with one.");
            Renderers[renderer.Type] = renderer;
        }

        private static void CheckType(IComponentRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (renderer.Type == null || !TypePattern.IsMatch(renderer.Type)) throw new ArgumentException("Invalid component type: " + renderer.Type);
            if (Schema.IsBuiltIn(renderer.Type)) throw new InvalidOperationException("The built-in component \"" + renderer.Type + "\" cannot be replaced.");
        }
    }

    /// <summary>The built-in components; each one's editor twin lives in components/builtins.ts.</summary>
    internal static class BuiltInComponents
    {
        private const string VideoAllow = "accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen";

        internal static IEnumerable<IComponentRenderer> Create()
        {
            yield return new BuiltIn("page")
            {
                TagOf = n => "div",
            };
            yield return new BuiltIn("section")
            {
                Target = LayoutTarget.Inner,
                TagOf = n => Choice(n, "tag"),
                AttributesOf = (n, c) =>
                {
                    var anchor = Values.Slug(n.Props["anchor"]);
                    return string.IsNullOrEmpty(anchor) ? None : new[] { Attr("id", anchor) };
                },
                ContentOf = (n, c, children) => "<div class=\"pe-in\">" + children + "</div>",
                CssOf = n =>
                {
                    var w = Values.Int(n.Props["contentWidth"], 0, 3840);
                    return w.HasValue && w.Value != 0 ? new ComponentCss(inner: new[] { Attr("max-width", Format.Px(w.Value)), Attr("margin-inline", "auto") }) : null;
                },
            };
            yield return new BuiltIn("container")
            {
                TagOf = n => string.IsNullOrEmpty(Values.LinkUrl(n.Props["href"])) ? Choice(n, "tag") : "a",
                AttributesOf = (n, c) =>
                {
                    var href = Values.LinkUrl(n.Props["href"]);
                    return string.IsNullOrEmpty(href) ? None : new[] { Attr("href", href) };
                },
            };
            yield return new BuiltIn("heading")
            {
                TagOf = n => "h" + Choice(n, "level"),
                ContentOf = (n, c, children) => RichTextHtml.RunsHtml(n.Props["text"]),
            };
            yield return new BuiltIn("text")
            {
                TagOf = n => "div",
                ContentOf = (n, c, children) => RichTextHtml.BlocksHtml(n.Props["content"]),
            };
            yield return new BuiltIn("image")
            {
                TagOf = n => "figure",
                ClassesOf = n => new[] { "pe-fit-" + Choice(n, "fit") },
                ContentOf = (n, c, children) =>
                {
                    var src = Values.ImageUrl(n.Props["src"]);
                    var caption = Str(n, "caption");
                    var html = string.Empty;
                    if (!string.IsNullOrEmpty(src))
                    {
                        var img = "<img src=\"" + Format.EscapeHtml(src) + "\" alt=\"" + Format.EscapeHtml(Str(n, "alt")) + "\" loading=\"lazy\" decoding=\"async\">";
                        var href = Values.LinkUrl(n.Props["href"]);
                        html = string.IsNullOrEmpty(href)
                            ? img
                            : "<a href=\"" + Format.EscapeHtml(href) + "\"" + (Flag(n, "newTab") ? RichTextHtml.NewTab : string.Empty) + ">" + img + "</a>";
                    }
                    else if (c.Mode == RenderMode.Canvas)
                    {
                        html = "<div class=\"pe-empty pe-empty-image\"></div>";
                    }
                    if (caption.Length > 0) html += "<figcaption>" + Format.EscapeHtml(caption) + "</figcaption>";
                    return html;
                },
            };
            yield return new BuiltIn("button")
            {
                TagOf = n => "a",
                ClassesOf = n => new[] { "pe-btn", "pe-btn-" + Choice(n, "variant") },
                AttributesOf = (n, c) =>
                {
                    var href = Values.LinkUrl(n.Props["href"]);
                    var attrs = new List<KeyValuePair<string, string>> { Attr("href", string.IsNullOrEmpty(href) ? "#" : href) };
                    attrs.AddRange(NewTabAttributes(Flag(n, "newTab")));
                    return attrs;
                },
                ContentOf = (n, c, children) =>
                {
                    var label = Format.EscapeHtml(Str(n, "label"));
                    var icon = Icons.Svg(Str(n, "icon"));
                    if (icon.Length == 0) return label;
                    return Choice(n, "iconPosition") == "end" ? label + icon : icon + label;
                },
            };
            yield return new BuiltIn("links")
            {
                TagOf = n => "nav",
                ClassesOf = n =>
                {
                    var variant = Choice(n, "variant");
                    return variant == "list"
                        ? new[] { "pe-links-" + Choice(n, "direction") }
                        : new[] { "pe-links-" + Choice(n, "direction"), "pe-links-" + variant };
                },
                ContentOf = (n, c, children) =>
                {
                    var sb = new StringBuilder("<ul>");
                    if (n.Props["items"] is JArray items)
                    {
                        foreach (var item in items)
                        {
                            if (!(item is JObject i) || !Js.TryString(i["label"], out var label)) continue;
                            var href = Values.LinkUrl(i["href"]);
                            sb.Append("<li><a href=\"").Append(Format.EscapeHtml(string.IsNullOrEmpty(href) ? "#" : href)).Append('"')
                                .Append(Js.IsTrue(i["newTab"]) ? RichTextHtml.NewTab : string.Empty).Append('>')
                                .Append(Js.TryString(i["icon"], out var icon) ? Icons.Svg(icon) : string.Empty)
                                .Append(Format.EscapeHtml(label)).Append("</a></li>");
                        }
                    }
                    return sb.Append("</ul>").ToString();
                },
            };
            yield return new BuiltIn("icon")
            {
                TagOf = n => "span",
                ContentOf = (n, c, children) => Icons.Svg(Str(n, "name"), Str(n, "label")),
                CssOf = n =>
                {
                    var size = Format.Px(Values.Int(n.Props["size"], 8, 256) ?? 24);
                    return new ComponentCss(self: new[] { Attr("width", size), Attr("height", size) });
                },
            };
            yield return new BuiltIn("post")
            {
                TagOf = n => "article",
                ContentOf = (n, c, children) => PostHtml(n, c),
            };
            yield return new BuiltIn("video")
            {
                TagOf = n => "div",
                ClassesOf = n => new[] { "pe-ar-" + ReplaceFirst(Choice(n, "aspect"), ":", "-") },
                ContentOf = (n, c, children) =>
                {
                    var embed = Values.VideoEmbed(n.Props["url"]);
                    if (embed == null) return c.Mode == RenderMode.Canvas ? "<div class=\"pe-empty pe-empty-video\"></div>" : string.Empty;
                    return "<iframe src=\"" + Format.EscapeHtml(embed) + "\" title=\"" + Format.EscapeHtml(Str(n, "title")) +
                        "\" loading=\"lazy\" allow=\"" + VideoAllow + "\"" +
                        " allowfullscreen referrerpolicy=\"strict-origin-when-cross-origin\"></iframe>";
                },
            };
            yield return new BuiltIn("spacer")
            {
                TagOf = n => "div",
                AttributesOf = (n, c) => new[] { Attr("aria-hidden", "true") },
                ContentOf = (n, c, children) => string.Empty,
            };
            yield return new BuiltIn("divider")
            {
                TagOf = n => "hr",
                Void = true,
                CssOf = n =>
                {
                    var t = Values.Int(n.Props["thickness"], 1, 20) ?? 1;
                    return new ComponentCss(self: new[] { Attr("border-top", Format.Px(t) + " " + Choice(n, "lineStyle") + " currentColor") });
                },
            };
            yield return new BuiltIn("collection")
            {
                // The layout (grid, stack) arranges the item copies inside .pe-in (Collections.Inner writes it).
                Target = LayoutTarget.Inner,
                TagOf = n => "div",
                ClassesOf = n => Choice(n, "view") == "table" ? new[] { "pe-view-table" } : Flag(n, "carousel") ? new[] { "pe-carousel" } : new string[0],
                ContentOf = (n, c, children) => children,
            };
            yield return new BuiltIn("item")
            {
                TagOf = n => Choice(n, "tag"),
            };
            yield return new BuiltIn("module")
            {
                TagOf = n => "div",
                AttributesOf = (n, c) => new[] { Attr("data-pe-module", ModuleId(n)) },
                ContentOf = (n, c, children) => "<!--pe:module:" + ModuleId(n) + "-->",
            };
        }

        private static readonly KeyValuePair<string, string>[] None = new KeyValuePair<string, string>[0];

        private static KeyValuePair<string, string> Attr(string name, string value)
        {
            return new KeyValuePair<string, string>(name, value);
        }

        private static IEnumerable<KeyValuePair<string, string>> NewTabAttributes(bool on)
        {
            return on ? new[] { Attr("target", "_blank"), Attr("rel", "noopener noreferrer") } : None;
        }

        /// <summary>A post card, and the dialog with the whole post and its comments that a click on the card opens (CSS :target, no script).</summary>
        private static string PostHtml(PageNode n, RenderContext c)
        {
            var target = "pe-post-" + n.Id;
            var title = Str(n, "title");
            var author = Str(n, "author");
            var head = "<div class=\"pe-post-head\">" + AvatarHtml(Values.ImageUrl(n.Props["avatar"]) ?? string.Empty) + "<div class=\"pe-post-by\"><strong>" + Format.EscapeHtml(author) + "</strong>" +
                (Str(n, "meta").Length > 0 ? "<span>" + Format.EscapeHtml(Str(n, "meta")) + "</span>" : string.Empty) + "</div></div>";
            var src = Values.ImageUrl(n.Props["image"]);
            var media = !string.IsNullOrEmpty(src)
                ? "<img class=\"pe-post-img\" src=\"" + Format.EscapeHtml(src) + "\" alt=\"" + Format.EscapeHtml(Str(n, "alt")) + "\" loading=\"lazy\" decoding=\"async\">"
                : c.Mode == RenderMode.Canvas ? "<div class=\"pe-post-img pe-empty pe-empty-image\"></div>" : string.Empty;
            var heading = title.Length > 0 ? "<h3 class=\"pe-post-title\">" + Format.EscapeHtml(title) + "</h3>" : string.Empty;
            var body = "<div class=\"pe-post-text\">" + RichTextHtml.BlocksHtml(n.Props["text"]) + "</div>";
            var comments = n.Props["comments"] is JArray list
                ? list.OfType<JObject>().Where(i => Js.TryString(i["text"], out _)).ToList()
                : new List<JObject>();
            var count = comments.Count;
            var foot = "<a class=\"pe-post-open\" href=\"#" + target + "\">" + Icons.Svg("comment") + "<span>" + Format.EscapeHtml(count > 0 ? Str(n, "commentsLabel") : Str(n, "emptyLabel")) + "</span>" +
                (count > 0 ? "<span class=\"pe-post-count\">" + Format.Int(count) + "</span>" : string.Empty) + "</a>";
            var listHtml = new StringBuilder();
            if (count > 0)
            {
                listHtml.Append("<ul class=\"pe-post-comments\">");
                foreach (var comment in comments)
                {
                    listHtml.Append("<li>").Append(AvatarHtml(Values.ImageUrl(comment["avatar"]) ?? string.Empty)).Append("<div class=\"pe-post-bubble\">");
                    if (Js.TryString(comment["author"], out var by) && by.Length > 0) listHtml.Append("<strong>").Append(Format.EscapeHtml(by)).Append("</strong>");
                    if (Js.TryString(comment["meta"], out var meta) && meta.Length > 0) listHtml.Append("<span>").Append(Format.EscapeHtml(meta)).Append("</span>");
                    listHtml.Append("<p>").Append(Format.EscapeHtml((string)comment["text"])).Append("</p></div></li>");
                }
                listHtml.Append("</ul>");
            }
            var closeLabel = Str(n, "closeLabel");
            var close = Format.EscapeHtml(closeLabel.Length > 0 ? closeLabel : "Close");
            var dialog = "<div class=\"pe-post-dialog\" id=\"" + target + "\" role=\"dialog\" aria-modal=\"true\" aria-label=\"" + Format.EscapeHtml(title.Length > 0 ? title : author) + "\">" +
                "<a class=\"pe-post-backdrop\" href=\"#_\" aria-label=\"" + close + "\"></a><div class=\"pe-post-sheet\">" +
                "<a class=\"pe-post-close\" href=\"#_\" aria-label=\"" + close + "\">" + Icons.Svg("close") + "</a>" + head + media + heading + body + listHtml + "</div></div>";
            return head + media + heading + body + foot + dialog;
        }

        /// <summary>A round author picture, or an empty circle without one.</summary>
        private static string AvatarHtml(string src)
        {
            return src.Length > 0
                ? "<img class=\"pe-post-avatar\" src=\"" + Format.EscapeHtml(src) + "\" alt=\"\" loading=\"lazy\" decoding=\"async\">"
                : "<span class=\"pe-post-avatar\"></span>";
        }

        private static string ModuleId(PageNode n)
        {
            return Format.Int(Values.Int(n.Props["moduleId"], 0, 2147483647) ?? 0);
        }

        /// <summary>An enum prop's value, or the schema default when it is not one of the options.</summary>
        private static string Choice(PageNode node, string prop)
        {
            var def = Schema.Component(node.Type).Prop(prop);
            return Js.TryString(node.Props[prop], out var value) && def.Options != null && def.Options.Contains(value) ? value : def.DefaultString;
        }

        private static string Str(PageNode node, string prop)
        {
            return Js.TryString(node.Props[prop], out var value) ? value : string.Empty;
        }

        private static bool Flag(PageNode node, string prop)
        {
            return Js.IsTrue(node.Props[prop]);
        }

        /// <summary>String.prototype.replace with a string pattern: the first occurrence only.</summary>
        private static string ReplaceFirst(string value, string search, string replacement)
        {
            var i = value.IndexOf(search, StringComparison.Ordinal);
            return i < 0 ? value : value.Substring(0, i) + replacement + value.Substring(i + search.Length);
        }

        /// <summary>A component written as a table of functions, like its TypeScript twin.</summary>
        private sealed class BuiltIn : ComponentRenderer
        {
            private readonly string type;

            public BuiltIn(string type)
            {
                this.type = type;
            }

            public LayoutTarget Target { get; set; } = LayoutTarget.Self;

            public bool Void { get; set; }

            public Func<PageNode, string> TagOf { get; set; }

            public Func<PageNode, IEnumerable<string>> ClassesOf { get; set; }

            public Func<PageNode, RenderContext, IEnumerable<KeyValuePair<string, string>>> AttributesOf { get; set; }

            public Func<PageNode, RenderContext, string, string> ContentOf { get; set; }

            public Func<PageNode, ComponentCss> CssOf { get; set; }

            public override string Type => type;

            public override LayoutTarget LayoutTarget => Target;

            public override bool VoidElement => Void;

            public override string Tag(PageNode node)
            {
                return TagOf(node);
            }

            public override IEnumerable<string> Classes(PageNode node)
            {
                return ClassesOf != null ? ClassesOf(node) : base.Classes(node);
            }

            public override IEnumerable<KeyValuePair<string, string>> Attributes(PageNode node, RenderContext context)
            {
                return AttributesOf != null ? AttributesOf(node, context) : base.Attributes(node, context);
            }

            public override string Content(PageNode node, RenderContext context, string children)
            {
                return ContentOf != null ? ContentOf(node, context, children) : base.Content(node, context, children);
            }

            public override ComponentCss Css(PageNode node)
            {
                return CssOf != null ? CssOf(node) : base.Css(node);
            }
        }
    }
}
