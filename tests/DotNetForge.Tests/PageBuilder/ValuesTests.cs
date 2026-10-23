#nullable disable
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.PageBuilder
{
    /// <summary>
    /// The value checks against what values.ts returns for the same input (expected
    /// values taken from the TypeScript implementation / Node), with the cases where
    /// .NET and JavaScript semantics differ called out.
    /// </summary>
    public class ValuesTests
    {
        private static readonly string Bom = ((char)0xFEFF).ToString();
        private static readonly string Nbsp = ((char)0x00A0).ToString();
        private static readonly string Nel = ((char)0x0085).ToString();
        private static readonly string LineSeparator = ((char)0x2028).ToString();
        private static readonly string Kelvin = ((char)0x212A).ToString();

        [Theory]
        [InlineData("#fff", "#fff")]
        [InlineData("  #11223344 ", "#11223344")]
        [InlineData("#12", null)]
        [InlineData("rgb(10,  20,\t30)", "rgb(10, 20, 30)")]
        [InlineData("rgba(0,0,0,.5)", "rgba(0,0,0,.5)")]
        [InlineData("hsl(200 50% 50%)", "hsl(200 50% 50%)")]
        [InlineData("hsl(200deg 50% 50% / .5)", "hsl(200deg 50% 50% / .5)")]
        [InlineData("RGB(1,2,3)", null)]
        [InlineData("var(--dnn-color-primary)", "var(--dnn-color-primary)")]
        [InlineData("currentColor", "currentColor")]
        [InlineData("blue", null)]
        [InlineData("red;}", null)]
        [InlineData("url(x)", null)]
        [InlineData("", null)]
        public void Color(string input, string expected)
        {
            Assert.Equal(expected, Values.Color(input));
        }

        [Fact]
        public void Color_uses_javascript_whitespace_and_end_of_string()
        {
            Assert.Equal("#fff", Values.Color(Bom + "#fff" + Nbsp));
            Assert.Null(Values.Color(Nel + "#fff"));
            Assert.Equal("rgb(1, 2, 3)", Values.Color("rgb(1," + LineSeparator + "2, 3)"));
            Assert.Null(Values.Color(12));
            Assert.Null(Values.Color(JValue.CreateNull()));
        }

        [Theory]
        [InlineData("/a", "/a")]
        [InlineData(" /b c.png ", "/b%20c.png")]
        [InlineData("https://x.org/y?z=1&w=2", "https://x.org/y?z=1&w=2")]
        [InlineData("HTTPS://X.ORG", "HTTPS://X.ORG")]
        [InlineData("#top", "#top")]
        [InlineData("?q=1", "?q=1")]
        [InlineData("/", "/")]
        [InlineData("mailto:a@b.c", "mailto:a@b.c")]
        [InlineData("MailTo:a@b.c", "MailTo:a@b.c")]
        [InlineData("tel:+31(0)12", "tel:+31(0)12")]
        [InlineData("tel:+31 12", null)] // spaces become %20 first, which tel: does not allow (same in values.ts)
        [InlineData("tel:abc", null)]
        [InlineData("javascript:alert(1)", null)]
        [InlineData("//evil", null)]
        [InlineData("/\\evil", null)]
        [InlineData("data:x", null)]
        [InlineData("relative.html", null)]
        [InlineData("https://x.org/\"x", null)]
        [InlineData("/a\tb", null)]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void LinkUrl(string input, string expected)
        {
            Assert.Equal(expected, Values.LinkUrl(input));
        }

        [Fact]
        public void LinkUrl_refuses_a_trailing_newline_that_dotnet_dollar_would_accept()
        {
            // trim() removes it, so the JS result is the plain number; a raw tel: with an inner newline is refused.
            Assert.Equal("tel:+3112", Values.LinkUrl("tel:+3112\n"));
            Assert.Null(Values.LinkUrl("tel:+31\n12"));
        }

        [Fact]
        public void Case_insensitive_matches_are_ascii_only()
        {
            // JavaScript's i flag never maps a non-ASCII letter onto an ASCII one; .NET's invariant culture would match the Kelvin sign.
            Assert.Null(Values.VideoEmbed("https://www.youtube-no" + "coo" + Kelvin + "ie.com/embed/dQw4w9WgXcQ"));
            Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", Values.VideoEmbed("HTTPS://WWW.YOUTUBE-NOCOOKIE.COM/EMBED/dQw4w9WgXcQ"));
        }

        [Theory]
        [InlineData("/img.png", "/img.png")]
        [InlineData("https://x.org/a.png", "https://x.org/a.png")]
        [InlineData("#top", null)]
        [InlineData("mailto:a@b.c", null)]
        [InlineData("/", "/")]
        [InlineData("", "")]
        public void ImageUrl(string input, string expected)
        {
            Assert.Equal(expected, Values.ImageUrl(input));
        }

        [Theory]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
        [InlineData("https://m.youtube.com/watch?feature=x&v=dQw4w9WgXcQ&t=1", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
        [InlineData("https://youtu.be/dQw4w9WgXcQ", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
        [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ?x", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
        [InlineData("https://vimeo.com/76979871", "https://player.vimeo.com/video/76979871?dnt=1")]
        [InlineData("https://player.vimeo.com/video/76979871/x", "https://player.vimeo.com/video/76979871?dnt=1")]
        [InlineData("https://vimeo.com/12345", null)]
        [InlineData("https://evil.com/v", null)]
        [InlineData("https://youtu.be/dQw4w9WgXcQX", null)]
        [InlineData("", null)]
        public void VideoEmbed(string input, string expected)
        {
            Assert.Equal(expected, Values.VideoEmbed(input));
        }

        [Theory]
        [InlineData("top", "top")]
        [InlineData(" a-b_c ", "a-b_c")]
        [InlineData("Bad Slug", null)]
        [InlineData("1abc", null)]
        [InlineData("", "")]
        public void Slug(string input, string expected)
        {
            Assert.Equal(expected, Values.Slug(input));
        }

        [Fact]
        public void Slug_respects_max()
        {
            Assert.Null(Values.Slug("abcdef", 5));
            Assert.Equal("abcde", Values.Slug("abcde", 5));
        }

        [Theory]
        [InlineData("a b", "a b")]
        [InlineData("  ok-class \t -x _y ", "ok-class -x _y")]
        [InlineData("pe-x", null)]
        [InlineData("bad\"class", null)]
        [InlineData("--x", null)]
        [InlineData("", "")]
        public void ClassNames(string input, string expected)
        {
            Assert.Equal(expected, Values.ClassNames(input));
        }

        [Theory]
        [InlineData("--a", "--a")]
        [InlineData("--a-b_C9", "--a-b_C9")]
        [InlineData("--a\n", null)]
        [InlineData(" --a", null)]
        [InlineData("-a", null)]
        [InlineData("--9", null)]
        public void VarName(string input, string expected)
        {
            Assert.Equal(expected, Values.VarName(input));
        }

        [Theory]
        [InlineData(" 1px ", "1px")]
        [InlineData("x;y", null)]
        [InlineData("a}", null)]
        [InlineData("URL (x)", null)]
        [InlineData("Expression(1)", null)]
        [InlineData("-moz-binding", null)]
        [InlineData("calc(1px + 2px)", "calc(1px + 2px)")]
        [InlineData("", null)]
        public void VarValue(string input, string expected)
        {
            Assert.Equal(expected, Values.VarValue(input));
        }

        [Fact]
        public void Num_rounds_like_math_round()
        {
            Assert.Equal(1.0, Values.Num(1.005, -10, 10));
            Assert.Equal(0.13, Values.Num(0.125, -10, 10));
            Assert.Equal(-0.12, Values.Num(-0.125, -10, 10));
            Assert.Equal(12.35, Values.Num(12.345, -100, 100));
            Assert.Equal(-12.34, Values.Num(-12.345, -100, 100));
            Assert.Equal(1.23, Values.Num(1.23456, -10, 10));
            Assert.Null(Values.Num(10.01, 0, 10));
            Assert.Equal(10.0, Values.Num(10.004, 0, 10));
        }

        [Fact]
        public void Num_never_returns_negative_zero()
        {
            var n = Values.Num(-0.001, -1, 1);
            Assert.Equal(0.0, n);
            Assert.True(System.BitConverter.DoubleToInt64Bits(n.Value) == 0L);
        }

        [Fact]
        public void Num_and_Int_accept_json_numbers_only()
        {
            Assert.Null(Values.Num("12", 0, 100));
            Assert.Null(Values.Num(true, 0, 100));
            Assert.Null(Values.Num(JValue.CreateNull(), 0, 100));
            Assert.Null(Values.Num(double.NaN, 0, 100));
            Assert.Null(Values.Int("12", 0, 100));
        }

        [Fact]
        public void Int_treats_a_float_with_no_fraction_as_an_integer()
        {
            // Newtonsoft reads "1.0" as a Float; Number.isInteger(1.0) is true in JavaScript.
            var token = JToken.Parse("1.0");
            Assert.Equal(JTokenType.Float, token.Type);
            Assert.Equal(1L, Values.Int(token, 0, 10));
            Assert.Null(Values.Int(1.5, 0, 10));
            Assert.Null(Values.Int(11, 0, 10));
            Assert.Equal(-1L, Values.Int(-1, -1, 10));
        }

        [Fact]
        public void Text_strips_control_characters_but_keeps_tab_and_line_breaks()
        {
            Assert.Equal("a\tb\nc\rd", Values.Text("a\u0000\tb\nc\rd\u007F", 100));
            Assert.Null(Values.Text("abcdef", 5));
            Assert.Null(Values.Text(5, 5));
        }

        [Theory]
        [InlineData("n0", "n0")]
        [InlineData("abcdefghijklmnop", "abcdefghijklmnop")]
        [InlineData("abcdefghijklmnopq", null)]
        [InlineData("Bad Id", null)]
        [InlineData("n0\n", null)]
        [InlineData("", null)]
        public void NodeId(string input, string expected)
        {
            Assert.Equal(expected, Values.NodeId(input));
        }

        [Theory]
        [InlineData(0.3, "0.3")]
        [InlineData(1.5, "1.5")]
        [InlineData(1023.98, "1023.98")]
        [InlineData(-1.5, "-1.5")]
        [InlineData(0.30000000000000004, "0.30000000000000004")]
        [InlineData(1e21, "1e+21")]
        [InlineData(1e-7, "1e-7")]
        [InlineData(2.5e-7, "2.5e-7")]
        [InlineData(123456789012345680000.0, "123456789012345680000")]
        [InlineData(5e-324, "5e-324")]
        [InlineData(0.000001, "0.000001")]
        [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
        [InlineData(-0.0, "0")]
        [InlineData(100.0, "100")]
        [InlineData(123.456, "123.456")]
        [InlineData(1.0 / 3, "0.3333333333333333")]
        public void NumberToString_matches_javascript_String(double value, string expected)
        {
            Assert.Equal(expected, Js.NumberToString(value));
        }

        [Theory]
        [InlineData(-0.001, "0")]
        [InlineData(1.005, "1")]
        [InlineData(0.125, "0.13")]
        [InlineData(-0.125, "-0.12")]
        [InlineData(2.675, "2.68")]
        [InlineData(1024 - 0.02, "1023.98")]
        [InlineData(640 - 0.02, "639.98")]
        [InlineData(-0.5, "-0.5")]
        [InlineData(3999.995, "4000")]
        public void Fmt_matches_the_editor(double value, string expected)
        {
            Assert.Equal(expected, Format.Fmt(value));
        }

        [Fact]
        public void EscapeHtml_and_CssUrl()
        {
            Assert.Equal("A &amp; B &lt;c&gt; &quot;d&quot; &#39;e&#39;", Format.EscapeHtml("A & B <c> \"d\" 'e'"));
            Assert.Equal("url(\"/a%28b%29%20c%3000\")", Format.CssUrl("/a(b) c" + ((char)0x3000).ToString()));
        }

        [Fact]
        public void Trim_uses_the_ecmascript_whitespace_set()
        {
            Assert.Equal("x", Js.Trim(Bom + Nbsp + " x" + LineSeparator));
            Assert.Equal(Nel + "x", Js.Trim(Nel + "x"));
        }
    }
}
