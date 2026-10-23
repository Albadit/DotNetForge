#nullable disable
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    /// <summary>Content components: icons, posts with comments, link tiles and menus.</summary>
    public class ContentComponentTests
    {
        private static JObject Doc(string type, JObject props)
        {
            return new JObject
            {
                ["schema"] = 1,
                ["root"] = "r",
                ["nodes"] = new JObject
                {
                    ["r"] = new JObject { ["id"] = "r", ["type"] = "page", ["parent"] = null, ["props"] = new JObject(), ["children"] = new JArray("s") },
                    ["s"] = new JObject { ["id"] = "s", ["type"] = "section", ["parent"] = "r", ["props"] = new JObject(), ["children"] = new JArray("x") },
                    ["x"] = new JObject { ["id"] = "x", ["type"] = type, ["parent"] = "s", ["props"] = props },
                },
            };
        }

        private static string Html(PageDocument doc)
        {
            return HtmlRenderer.Render(doc, new RenderContext { Mode = RenderMode.View });
        }

        [Theory]
        [InlineData("<svg onload=alert(1)>")]
        [InlineData("constructor")]
        [InlineData("HOME")]
        public void Unknown_icon_names_fall_back_to_the_default(string name)
        {
            var doc = DocumentValidator.Normalize(Doc("icon", new JObject { ["name"] = name }));
            Assert.Equal("star", (string)doc.Nodes["x"].Props["name"]);
        }

        [Fact]
        public void Link_icons_are_kept_only_when_known()
        {
            var items = new JArray(
                new JObject { ["label"] = "Home", ["href"] = "/", ["icon"] = "home" },
                new JObject { ["label"] = "Bad", ["href"] = "/", ["icon"] = "\"><script>" });
            var doc = DocumentValidator.Normalize(Doc("links", new JObject { ["items"] = items, ["variant"] = "menu" }));
            var normalized = (JArray)doc.Nodes["x"].Props["items"];
            Assert.Equal("home", (string)normalized[0]["icon"]);
            Assert.Null(normalized[1]["icon"]);
            var html = Html(doc);
            Assert.Contains("pe-links-column pe-links-menu", html);
            Assert.DoesNotContain("<script>", html);
        }

        [Fact]
        public void Post_comments_are_cleaned_and_escaped()
        {
            var comments = new JArray(
                new JObject { ["author"] = "<b>Eve</b>", ["avatar"] = "javascript:alert(1)", ["meta"] = "today", ["text"] = "<img src=x onerror=alert(1)>" },
                "not a comment");
            var doc = DocumentValidator.Normalize(Doc("post", new JObject { ["comments"] = comments, ["title"] = "Nieuws" }));
            var normalized = (JArray)doc.Nodes["x"].Props["comments"];
            Assert.Single(normalized);
            Assert.Equal(string.Empty, (string)normalized[0]["avatar"]);

            var html = Html(doc);
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
            Assert.Contains("&lt;b&gt;Eve&lt;/b&gt;", html);
            Assert.DoesNotContain("javascript:", html);
            // The card links to the dialog that shows the whole post with its comments.
            Assert.Contains("href=\"#pe-post-x\"", html);
            Assert.Contains("id=\"pe-post-x\" role=\"dialog\" aria-modal=\"true\" aria-label=\"Nieuws\"", html);
            Assert.Contains("<span class=\"pe-post-count\">1</span>", html);
        }

        [Fact]
        public void A_node_rendered_as_a_link_gets_an_element_selector()
        {
            var doc = DocumentValidator.Normalize(Doc("container", new JObject { ["href"] = "/groups" }));
            Assert.Equal(".pe-root a.pe-n-x", CssBuilder.SelectorOf(doc, "x"));
            var plain = DocumentValidator.Normalize(Doc("container", new JObject()));
            Assert.Equal(".pe-root .pe-n-x", CssBuilder.SelectorOf(plain, "x"));
        }
    }
}
