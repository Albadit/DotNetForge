#nullable disable
using System.Linq;
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    public class SegmentsTests
    {
        [Fact]
        public void Splits_markup_and_modules_in_order()
        {
            var segments = Segments.Split("<div><!--pe:module:12--></div><p>x</p><!--pe:module:7-->");
            Assert.Collection(
                segments,
                s => Assert.Equal("<div>", ((HtmlSegment)s).Html),
                s => Assert.Equal(12, ((ModuleSegment)s).ModuleId),
                s => Assert.Equal("</div><p>x</p>", ((HtmlSegment)s).Html),
                s => Assert.Equal(7, ((ModuleSegment)s).ModuleId));
        }

        [Fact]
        public void Adjacent_markers_produce_no_empty_markup()
        {
            var segments = Segments.Split("<!--pe:module:1--><!--pe:module:2-->");
            Assert.Equal(new[] { 1, 2 }, segments.Cast<ModuleSegment>().Select(s => s.ModuleId));
        }

        [Fact]
        public void Markup_without_markers_is_one_segment()
        {
            var segments = Segments.Split("<p>a</p>");
            Assert.Equal("<p>a</p>", ((HtmlSegment)Assert.Single(segments)).Html);
            Assert.Empty(Segments.Split(string.Empty));
            Assert.Empty(Segments.Split(null));
        }

        [Fact]
        public void Out_of_range_ids_and_other_comments_stay_markup()
        {
            var segments = Segments.Split("a<!--pe:module:99999999999-->b<!--pe:module:x-->c<!--other-->");
            Assert.Equal("a<!--pe:module:99999999999-->b<!--pe:module:x-->c<!--other-->", ((HtmlSegment)Assert.Single(segments)).Html);
        }

        [Fact]
        public void Rendered_module_nodes_become_module_segments()
        {
            var fixture = GoldenFixtureTests.Load("dashboard");
            var doc = DocumentValidator.Normalize(fixture["input"]);
            var html = HtmlRenderer.Render(doc, GoldenFixtureTests.ContextOf(fixture["context"]));
            var segments = Segments.Split(html);
            Assert.Equal(html, string.Concat(segments.Select(s => s is HtmlSegment h ? h.Html : "<!--pe:module:" + ((ModuleSegment)s).ModuleId + "-->")));
            Assert.Contains(segments, s => s is ModuleSegment m && m.ModuleId == 2472);
        }
    }
}
