using DotNetForge.Core.Html;
using Xunit;

namespace DotNetForge.Tests.Html;

/// <summary>The Release build's HTML minification: smaller, displayed the same (.docs/guides/development.md#development-and-production-builds).</summary>
public sealed class HtmlMinifierTests
{
    private const char NoBreakSpace = ' ';

    [Fact]
    public void Whitespace_next_to_block_tags_goes()
    {
        const string html = "<!DOCTYPE html>\n<div>\n    <p>One</p>\n\n    <span>a</span>\n    <div>b</div>\n</div>\n";
        Assert.Equal("<!DOCTYPE html><div><p>One</p><span>a</span><div>b</div></div>", HtmlMinifier.Minify(html));
    }

    [Fact]
    public void Whitespace_between_inline_content_stays_one_space()
    {
        Assert.Equal("<a>Edit</a> <a>Delete</a> and more", HtmlMinifier.Minify("<a>Edit</a>   <a>Delete</a>\tand  more"));
        Assert.Equal("<div>Signed in <span class=\"text-muted\">admin</span></div>",
            HtmlMinifier.Minify("<div>\n    Signed in\n    <span class=\"text-muted\">admin</span>\n</div>"));
    }

    [Fact]
    public void Custom_elements_count_as_inline()
    {
        Assert.Equal("<dnf-a>x</dnf-a> <dnf-b>y</dnf-b>", HtmlMinifier.Minify("<dnf-a>x</dnf-a>\n  <dnf-b>y</dnf-b>"));
    }

    [Fact]
    public void Text_keeps_one_line_break_for_pre_line()
    {
        Assert.Equal("<p>First line\nsecond line</p>", HtmlMinifier.Minify("<p>First line\n\n      second line</p>"));
    }

    [Fact]
    public void Pre_textarea_script_and_style_keep_every_character()
    {
        const string html = "<pre class=\"x\">a\n    b  c</pre>\n  <textarea name=\"t\">  line 1\n\n  line 2</textarea>"
                            + "<script type=\"application/json\">{ \"a\":   1 }</script><STYLE>p  { color: red }</STYLE>";
        Assert.Equal(html.Replace(">\n  <textarea", "><textarea", StringComparison.Ordinal), HtmlMinifier.Minify(html));
    }

    [Fact]
    public void Attribute_values_are_kept_whitespace_between_attributes_collapses()
    {
        const string html = "<input\n    type=\"text\"\n    value=\"a  b\n c\"   title='x  > y'\n/>";
        Assert.Equal("<input type=\"text\" value=\"a  b\n c\" title='x  > y' />", HtmlMinifier.Minify(html));
    }

    [Fact]
    public void Empty_class_and_style_go_other_empty_attributes_stay()
    {
        Assert.Equal("<img src=\"a.png\" alt=\"\"><option value=\"\" data-x='class=\"\"'>",
            HtmlMinifier.Minify("<img class=\"\" src=\"a.png\" style='' alt=\"\"><option value=\"\" class=\"\" data-x='class=\"\"'>"));
    }

    [Fact]
    public void Comments_go_but_page_builder_markers_and_no_break_spaces_stay()
    {
        var text = $"a{NoBreakSpace}{NoBreakSpace}b";
        Assert.Equal(text, HtmlMinifier.Minify("<!--  a\n  b -->\n   " + text));
        Assert.Equal("<div><!--pe:module:3--></div>", HtmlMinifier.Minify("<div><!-- slot --><!--pe:module:3--></div>"));
        Assert.Equal("ab", HtmlMinifier.Minify("a<!-- x -->b"));
    }

    [Fact]
    public void Optional_closing_tags_go_where_the_spec_allows()
    {
        Assert.Equal("<ul><li>One<li>Two</ul>", HtmlMinifier.Minify("<ul>\n  <li>One</li>\n  <li>Two</li>\n</ul>"));
        Assert.Equal("<table><tr><td>a<td>b<tr><td>c</table>",
            HtmlMinifier.Minify("<table><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></table>"));
        Assert.Equal("<select><option>a<option>b</select>", HtmlMinifier.Minify("<select><option>a</option><option>b</option></select>"));
        Assert.Equal("<html><head><title>t</title><body><p>x</p>",
            HtmlMinifier.Minify("<html>\n<head><title>t</title></head>\n<body><p>x</p></body>\n</html>\n"));
    }

    [Fact]
    public void A_closing_tag_stays_when_anything_else_follows()
    {
        Assert.Equal("<ul><li>a</li><!--pe:module:1--></ul>", HtmlMinifier.Minify("<ul><li>a</li><!--pe:module:1--></ul>"));
        Assert.Equal("<li>a</li>", HtmlMinifier.Minify("<li>a</li>"));
        Assert.Equal("<p>a</p><div></div>", HtmlMinifier.Minify("<p>a</p><div></div>"));
    }

    [Fact]
    public void A_lone_angle_bracket_or_unclosed_pre_does_not_break_the_page()
    {
        Assert.Equal("1 < 2<pre>a b", HtmlMinifier.Minify("1  <  2 <pre>\n  a   b"));
    }
}
