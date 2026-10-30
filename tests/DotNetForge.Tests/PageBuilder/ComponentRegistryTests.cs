#nullable disable
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    public class ComponentRegistryTests
    {
        private sealed class Badge : ComponentRenderer
        {
            private readonly string type;

            public Badge(string type)
            {
                this.type = type;
            }

            public override string Type => type;

            public override string Tag(PageNode node)
            {
                return "span";
            }

            public override string Content(PageNode node, RenderContext context, string children)
            {
                return Format.EscapeHtml((string)node.Props["text"]);
            }

            public override ComponentCss Css(PageNode node)
            {
                return new ComponentCss(self: new[] { new System.Collections.Generic.KeyValuePair<string, string>("display", "inline-block") });
            }
        }

        private static readonly JObject BadgeSchema = JObject.Parse(
            "{\"label\":\"Badge\",\"category\":\"content\",\"parents\":[\"section\",\"container\"]," +
            "\"props\":{\"text\":{\"type\":\"text\",\"label\":\"Text\",\"max\":20,\"default\":\"New\"}}}");

        [Fact]
        public void Built_ins_are_registered()
        {
            foreach (var type in Schema.BuiltInTypes) Assert.Equal(type, ComponentRegistry.Get(type).Type);
            Assert.Null(ComponentRegistry.Get("iframe"));
            Assert.Null(ComponentRegistry.Get(null));
        }

        [Fact]
        public void Built_ins_cannot_be_replaced()
        {
            Assert.Throws<InvalidOperationException>(() => ComponentRegistry.Register(new Badge("heading"), BadgeSchema));
            Assert.Throws<InvalidOperationException>(() => ComponentRegistry.Register(new Badge("heading")));
        }

        [Fact]
        public void Type_names_are_checked()
        {
            Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(new Badge("Bad Type"), BadgeSchema));
            Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(new Badge("x"), BadgeSchema));
            Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(new Badge("test-unknown")));
        }

        [Fact]
        public void Extensions_are_validated_and_rendered()
        {
            ComponentRegistry.Register(new Badge("test-badge"), BadgeSchema);
            var doc = DocumentValidator.Parse(
                "{\"schema\":1,\"root\":\"root\",\"nodes\":{" +
                "\"root\":{\"id\":\"root\",\"type\":\"page\",\"parent\":null,\"props\":{},\"children\":[\"s\"]}," +
                "\"s\":{\"id\":\"s\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[\"b\",\"c\"]}," +
                "\"b\":{\"id\":\"b\",\"type\":\"test-badge\",\"parent\":\"s\",\"props\":{\"text\":\"<Hot>\",\"x\":1}}," +
                "\"c\":{\"id\":\"c\",\"type\":\"test-badge\",\"parent\":\"s\",\"props\":{\"text\":\"far too long for a badge\"}}}}");
            Assert.Equal("{\"text\":\"<Hot>\"}", doc.Nodes["b"].Props.ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal("New", (string)doc.Nodes["c"].Props["text"]);

            var html = HtmlRenderer.RenderNode(doc, "b", new RenderContext { Mode = RenderMode.Canvas });
            Assert.Equal("<span class=\"pe-node pe-t-test-badge pe-n-b\" data-pe-id=\"b\">&lt;Hot&gt;</span>", html);
            Assert.Contains("display:inline-block", CssBuilder.NodeCss(doc, "b"));

            // A badge cannot be placed directly on the page: its parents are section and container.
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(
                "{\"schema\":1,\"root\":\"root\",\"nodes\":{" +
                "\"root\":{\"id\":\"root\",\"type\":\"page\",\"parent\":null,\"props\":{},\"children\":[\"b\"]}," +
                "\"b\":{\"id\":\"b\",\"type\":\"test-badge\",\"parent\":\"root\",\"props\":{}}}}"));
        }

        [Fact]
        public void Extension_schemas_need_parents_and_prop_defaults()
        {
            Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(new Badge("test-noparents"), JObject.Parse("{\"props\":{}}")));
            Assert.Throws<ArgumentException>(() => ComponentRegistry.Register(
                new Badge("test-nodefault"),
                JObject.Parse("{\"parents\":[\"section\"],\"props\":{\"text\":{\"type\":\"text\"}}}")));
        }
    }
}
