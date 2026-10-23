#nullable disable
using System.Linq;
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    /// <summary>Validator and CSS details the golden fixtures do not pin down on their own.</summary>
    public class DocumentValidatorTests
    {
        private static string Doc(string nodes, string root = "root", string extra = "")
        {
            return "{\"schema\":1,\"root\":\"" + root + "\",\"nodes\":{" + nodes + "}" + extra + "}";
        }

        private const string Page = "\"root\":{\"id\":\"root\",\"type\":\"page\",\"parent\":null,\"props\":{},\"children\":[\"s\"]}";

        private static string Section(string children)
        {
            return "\"s\":{\"id\":\"s\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[" + children + "]}";
        }

        [Fact]
        public void Parse_keeps_date_like_strings_as_strings()
        {
            var doc = DocumentValidator.Parse(Doc(Page + "," + Section("\"h\"") + ",\"h\":{\"id\":\"h\",\"type\":\"heading\",\"parent\":\"s\",\"name\":\"2024-01-01T00:00:00Z\",\"props\":{}}"));
            Assert.Equal("2024-01-01T00:00:00Z", doc.Nodes["h"].Name);
            Assert.Contains("\"name\":\"2024-01-01T00:00:00Z\"", doc.ToJsonString());
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{} {}")]
        [InlineData("[1]")]
        [InlineData("null")]
        public void Parse_refuses_malformed_input(string json)
        {
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(json));
        }

        [Fact]
        public void Parse_limits_nesting()
        {
            var deep = new string('[', 70) + new string(']', 70);
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(Doc(Page + "," + Section(""), extra: ",\"x\":" + deep)));
        }

        [Fact]
        public void Removed_modules_are_kept_sorted_without_placed_ones()
        {
            var module = ",\"m\":{\"id\":\"m\",\"type\":\"module\",\"parent\":\"s\",\"props\":{\"moduleId\":446}}";
            var settings = ",\"settings\":{\"removedModules\":[3473,446,\"7\",-1,3473,12]}";
            var doc = DocumentValidator.Parse(Doc(Page + "," + Section("\"m\"") + module, extra: settings));
            Assert.Equal(new[] { 12, 3473 }, doc.RemovedModules.ToArray());
            Assert.Contains("\"removedModules\":[12,3473]", doc.ToJsonString());
            Assert.DoesNotContain("removedModules", DocumentValidator.Parse(Doc(Page + "," + Section(""))).ToJsonString());
        }

        [Fact]
        public void Schema_one_written_as_a_float_is_accepted()
        {
            var doc = DocumentValidator.Parse(Doc(Page + "," + Section("")).Replace("\"schema\":1", "\"schema\":1.0"));
            Assert.Equal("root", doc.Root);
        }

        [Fact]
        public void Number_like_strings_are_not_numbers()
        {
            var doc = DocumentValidator.Parse(Doc(Page + ",\"s\":{\"id\":\"s\",\"type\":\"section\",\"parent\":\"root\",\"props\":{\"contentWidth\":\"12\"},\"children\":[]}"));
            Assert.Equal(0, (int)doc.Nodes["s"].Props["contentWidth"]);
        }

        [Fact]
        public void Numeric_node_ids_are_ordered_like_javascript_object_keys()
        {
            var nodes = "\"root\":{\"id\":\"root\",\"type\":\"page\",\"parent\":null,\"props\":{},\"children\":[\"10\",\"2\",\"a1\"]}," +
                "\"a1\":{\"id\":\"a1\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[]}," +
                "\"10\":{\"id\":\"10\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[]}," +
                "\"2\":{\"id\":\"2\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[]}";
            var doc = DocumentValidator.Parse(Doc(nodes));
            Assert.Equal(new[] { "2", "10", "root", "a1" }, doc.Nodes.Keys.ToArray());
            Assert.Equal(new[] { "root", "10", "2", "a1" }, doc.Walk().ToArray());
        }

        [Fact]
        public void Unknown_keys_are_dropped_and_values_cleaned()
        {
            var doc = DocumentValidator.Parse(Doc(Page + "," + Section("\"v\",\"t\"") +
                ",\"v\":{\"id\":\"v\",\"type\":\"video\",\"parent\":\"s\",\"props\":{\"url\":\"  https://youtu.be/dQw4w9WgXcQ \",\"bogus\":1}," +
                "\"style\":{\"base\":{\"opacity\":0.555,\"nope\":1,\"padding\":[1,2,3]},\"desktop\":{\"opacity\":1},\"tablet\":{}}}" +
                ",\"t\":{\"id\":\"t\",\"type\":\"text\",\"parent\":\"s\",\"props\":{\"content\":[{\"type\":\"p\",\"runs\":[{\"text\":\"a\",\"b\":true},{\"text\":\"b\",\"b\":true,\"i\":false},{\"text\":\"\"},{\"text\":\"c\\r\\nd\"}]},{\"type\":\"script\",\"runs\":[]}]}}"));
            var video = doc.Nodes["v"].ToJson();
            Assert.Equal("https://youtu.be/dQw4w9WgXcQ", (string)video["props"]["url"]);
            Assert.Null(video["props"]["bogus"]);
            Assert.Equal("{\"base\":{\"opacity\":0.56}}", video["style"].ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal(
                "[{\"type\":\"p\",\"runs\":[{\"text\":\"ab\",\"b\":true},{\"text\":\"c\\nd\"}]}]",
                doc.Nodes["t"].Props["content"].ToString(Newtonsoft.Json.Formatting.None));
        }

        [Fact]
        public void Structural_problems_throw()
        {
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(Doc(Page)));
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(Doc(Page + ",\"s\":{\"id\":\"s\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":null}")));
            Assert.Throws<DocumentException>(() => DocumentValidator.Parse(Doc(Page + "," + Section("\"s\""))));
        }

        [Fact]
        public void Css_replaces_declarations_in_place_and_resets_dropped_ones()
        {
            var doc = DocumentValidator.Parse(Doc(
                "\"root\":{\"id\":\"root\",\"type\":\"page\",\"parent\":null,\"props\":{},\"children\":[\"c\"]}," +
                "\"c\":{\"id\":\"c\",\"type\":\"container\",\"parent\":\"root\",\"props\":{},\"children\":[]," +
                "\"layout\":{\"base\":{\"mode\":\"grid\"},\"tablet\":{\"mode\":\"stack\"}}," +
                "\"style\":{\"base\":{\"width\":{\"mode\":\"fill\"},\"minWidth\":10,\"alignSelf\":\"center\"}}," +
                "\"vars\":{\"--z\":\"1\",\"--a\":\"2\"}}"));
            Assert.Equal(
                ".pe-root .pe-n-c{display:grid;grid-template-columns:repeat(12,minmax(0,1fr));grid-auto-rows:minmax(40px,auto);column-gap:16px;row-gap:16px;" +
                // Fill on the page's cross axis keeps align-self:stretch; the alignment does not replace it.
                "align-self:stretch;min-width:10px;--z:1;--a:2}\n" +
                "@media (max-width:1023.98px){.pe-root .pe-n-c{display:flex;flex-direction:column;gap:16px;align-items:stretch;justify-content:flex-start;flex-wrap:nowrap;" +
                "grid-template-columns:unset;grid-auto-rows:unset;column-gap:unset;row-gap:unset}}",
                CssBuilder.NodeCss(doc, "c"));
        }

        [Fact]
        public void Visibility_is_never_null_and_round_trips()
        {
            var doc = DocumentValidator.Parse(Doc(Page + ",\"s\":{\"id\":\"s\",\"type\":\"section\",\"parent\":\"root\",\"props\":{},\"children\":[]," +
                "\"visibility\":{\"devices\":[\"tv\"],\"audience\":\"all\",\"roles\":[1,1,\"x\",2.5,-1]},\"permissions\":{\"editRoles\":[5,\"x\",6]}}"));
            Assert.Same(Visibility.Everyone, doc.Nodes["root"].Visibility);
            var s = doc.Nodes["s"];
            Assert.Empty(s.Visibility.Devices);
            Assert.Equal("all", s.Visibility.Audience);
            Assert.Equal(new[] { 1, -1 }, s.Visibility.Roles);
            Assert.Equal(new[] { 5, 6 }, s.EditRoles);
            Assert.Equal("{\"devices\":[],\"roles\":[1,-1]}", s.ToJson()["visibility"].ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal(string.Empty, HtmlRenderer.Render(doc, new RenderContext { Mode = RenderMode.View }).Replace("<div class=\"pe-root pe-node pe-t-page pe-n-root\"></div>", string.Empty));
        }
    }
}
