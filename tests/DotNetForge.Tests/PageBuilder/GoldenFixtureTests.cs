#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    /// <summary>
    /// Runs the golden fixtures the TypeScript editor writes
    /// (copied from the Dnn.Libraries.PageEditor repository, where its test/fixtures.ts writes them; the JS port in src/DotNetForge.Web/wwwroot/js/builder runs the same cases):
    /// for every case the C# validator must produce the same normalized document
    /// (or reject it), and the C# renderers the same HTML and CSS, byte for byte.
    /// </summary>
    public class GoldenFixtureTests
    {
        private const int ExpectedFixtureCount = 84;

        private static string FixtureDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures");

        public static IEnumerable<object[]> Fixtures()
        {
            return Directory.GetFiles(Path.Combine(FixtureDir, "render"), "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => new object[] { n });
        }

        [Fact]
        public void All_fixtures_are_present()
        {
            Assert.Equal(ExpectedFixtureCount, Fixtures().Count());
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Normalization_matches_the_editor(string name)
        {
            var fixture = Load(name);
            if ((bool)fixture["error"])
            {
                Assert.Throws<DocumentException>(() => DocumentValidator.Normalize(fixture["input"]));
                Assert.Equal(JTokenType.Null, fixture["normalized"].Type);
                return;
            }

            var doc = DocumentValidator.Normalize(fixture["input"]);
            var expected = (JObject)fixture["normalized"];
            var actual = doc.ToJson();
            AssertSameText(expected.ToString(Formatting.None), doc.ToJsonString(), name + " (normalized)");
            Assert.True(JToken.DeepEquals(expected, actual), name + ": normalized documents differ.");
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Html_matches_the_editor(string name)
        {
            var fixture = Load(name);
            var expected = (string)fixture["html"];
            if ((bool)fixture["error"])
            {
                Assert.Equal(string.Empty, expected);
                return;
            }
            var doc = DocumentValidator.Normalize(fixture["input"]);
            AssertSameText(expected, HtmlRenderer.Render(doc, ContextOf(fixture["context"])), name + " (html)");
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Css_matches_the_editor(string name)
        {
            var fixture = Load(name);
            var expected = (string)fixture["css"];
            if ((bool)fixture["error"])
            {
                Assert.Equal(string.Empty, expected);
                return;
            }
            var doc = DocumentValidator.Normalize(fixture["input"]);
            AssertSameText(expected, CssBuilder.DocumentCss(doc), name + " (css)");
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Normalizing_twice_changes_nothing(string name)
        {
            var fixture = Load(name);
            if ((bool)fixture["error"]) return;
            var once = DocumentValidator.Normalize(fixture["input"]);
            var twice = DocumentValidator.Parse(once.ToJsonString());
            Assert.Equal(once.ToJsonString(), twice.ToJsonString());
        }

        [Fact]
        public void BaseCss_matches_the_shared_schema_file()
        {
            var source = FindRepoFile(Path.Combine("src", "DotNetForge.Web", "wwwroot", "builder", "base.css"));
            Assert.Equal(File.ReadAllText(source), BaseCss.Text);
            Assert.Contains(".pe-root", BaseCss.Text);
        }

        /// <summary>
        /// tests/fixtures/page-builder/base.css is written by the fixture generator.
        /// The committed copy is empty (the generator's ?raw import yielded an empty
        /// string), so it is compared only once it has content.
        /// </summary>
        [Fact]
        public void BaseCss_matches_the_fixture_copy_when_present()
        {
            var fixture = File.ReadAllText(Path.Combine(FixtureDir, "base.css"));
            if (fixture.Length == 0) return;
            Assert.Equal(fixture, BaseCss.Text);
        }

        internal static JObject Load(string name)
        {
            var path = Path.Combine(FixtureDir, "render", name + ".json");
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
            {
                return (JObject)JToken.ReadFrom(reader);
            }
        }

        internal static RenderContext ContextOf(JToken context)
        {
            var hidden = new HashSet<string>((context["hidden"] as JArray)?.Select(v => (string)v) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var modules = context["modules"] is JArray list ? new HashSet<int>(list.Select(v => (int)v)) : null;
            return new RenderContext
            {
                Mode = (string)context["mode"] == "canvas" ? RenderMode.Canvas : RenderMode.View,
                IsVisible = n => !hidden.Contains(n.Id),
                HasModule = id => modules == null || modules.Contains(id),
                // Content list entries (tests/js/content-list-fixtures.mjs writes these cases).
                Data = context["data"] as JObject,
            };
        }

        /// <summary>Equality with a message that shows where long strings first differ.</summary>
        internal static void AssertSameText(string expected, string actual, string what)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
            var i = 0;
            while (i < expected.Length && i < actual.Length && expected[i] == actual[i]) i++;
            var from = Math.Max(0, i - 80);
            Assert.Fail(what + " differs at " + i + ":\n expected: ..." + Slice(expected, from, 200) + "\n actual:   ..." + Slice(actual, from, 200));
        }

        private static string Slice(string s, int from, int length)
        {
            return from >= s.Length ? "<end>" : s.Substring(from, Math.Min(length, s.Length - from));
        }

        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException(relative);
        }
    }
}
