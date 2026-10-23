#nullable disable
using System.Diagnostics;
using System.Linq;
using DotNetForge.PageBuilder;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    /// <summary>
    /// Regression tests for the findings of the security review: hostile documents
    /// must stay cheap to validate, and CSS custom properties must not be able to
    /// break the published stylesheet.
    /// </summary>
    public class SecurityRegressionTests
    {
        private static JObject Doc(JObject heading)
        {
            heading["id"] = "h";
            heading["type"] = "heading";
            heading["parent"] = "s";
            return new JObject
            {
                ["schema"] = 1,
                ["root"] = "r",
                ["nodes"] = new JObject
                {
                    ["r"] = new JObject { ["id"] = "r", ["type"] = "page", ["parent"] = null, ["props"] = new JObject(), ["children"] = new JArray("s") },
                    ["s"] = new JObject { ["id"] = "s", ["type"] = "section", ["parent"] = "r", ["props"] = new JObject(), ["children"] = new JArray("h") },
                    ["h"] = heading,
                },
            };
        }

        [Fact]
        public void A_huge_role_list_is_validated_in_linear_time_and_capped()
        {
            var roles = new JArray(Enumerable.Range(1, 300000));
            var doc = Doc(new JObject { ["props"] = new JObject(), ["visibility"] = new JObject { ["roles"] = roles }, ["permissions"] = new JObject { ["editRoles"] = roles } });
            var watch = Stopwatch.StartNew();
            var result = DocumentValidator.Normalize(doc);
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 2000, "took " + watch.ElapsedMilliseconds + " ms");
            Assert.Equal(50, result.Nodes["h"].Visibility.Roles.Count);
            Assert.Equal(Enumerable.Range(1, 50), result.Nodes["h"].Visibility.Roles);
        }

        [Fact]
        public void Merging_many_long_runs_is_linear()
        {
            var text = new string('x', 1950);
            var runs = new JArray(Enumerable.Range(0, 2000).Select(_ => new JObject { ["text"] = text }));
            var doc = Doc(new JObject { ["props"] = new JObject { ["text"] = runs } });
            var watch = Stopwatch.StartNew();
            var result = DocumentValidator.Normalize(doc);
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 2000, "took " + watch.ElapsedMilliseconds + " ms");
            // Over the heading's limit: the default text is kept.
            Assert.Equal("Heading", (string)result.Nodes["h"].Props["text"][0]["text"]);
        }

        [Theory]
        [InlineData("calc(1px")]
        [InlineData("1px)")]
        [InlineData("a) (b")]
        [InlineData("1px /* x")]
        [InlineData("x */ y")]
        public void Custom_property_values_that_could_break_the_stylesheet_are_refused(string value)
        {
            Assert.Null(Values.VarValue(value));
        }

        [Theory]
        [InlineData("calc(100% - 2px)")]
        [InlineData("min(10px, calc(2px + 1em))")]
        [InlineData("12px")]
        public void Balanced_custom_property_values_are_kept(string value)
        {
            Assert.Equal(value, Values.VarValue(value));
        }
    }
}
