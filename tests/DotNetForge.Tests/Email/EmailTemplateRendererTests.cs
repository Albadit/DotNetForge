using DotNetForge.Core.Email;
using Xunit;

namespace DotNetForge.Tests.Email;

/// <summary>Email template variables: validation and rendering (.docs/features/email.md#templates).</summary>
public sealed class EmailTemplateRendererTests
{
    private static readonly EmailTemplateDefinition Reset = EmailTemplateCatalog.Find(EmailTemplateCatalog.PasswordReset)!;

    [Fact]
    public void Every_shipped_default_is_valid_and_renders_with_its_sample_data()
    {
        Assert.Equal(4, EmailTemplateCatalog.All.Count);
        foreach (var t in EmailTemplateCatalog.All)
        {
            Assert.Empty(EmailTemplateRenderer.Validate(t, t.DefaultSubject, t.DefaultBody));
            Assert.DoesNotContain("{{", EmailTemplateRenderer.RenderBody(t.DefaultBody, t.SampleValues));
            Assert.All(EmailTemplateRenderer.Variables(t.DefaultBody + t.DefaultSubject), v => Assert.True(t.SampleValues.ContainsKey(v), v));
        }
    }

    [Fact]
    public void An_unknown_variable_is_named()
    {
        var errors = EmailTemplateRenderer.Validate(Reset, "Hi {{firstName}}", "<a href=\"{{resetUrl}}\">x</a>");
        Assert.Contains(errors, e => e.Contains("{{firstName}}") && e.Contains("subject"));
    }

    [Fact]
    public void A_required_variable_must_stay_in_the_body()
    {
        var errors = EmailTemplateRenderer.Validate(Reset, "Reset", "<p>Hello {{userName}}</p>");
        Assert.Contains(errors, e => e.Contains("{{resetUrl}}"));
    }

    [Theory]
    [InlineData("<a href=\"{{resetUrl}\">x</a> {{resetUrl}}")]
    [InlineData("{{ resetUrl }} {{")]
    [InlineData("{{resetUrl}} }}")]
    public void An_incomplete_placeholder_is_rejected(string body)
    {
        Assert.Contains(EmailTemplateRenderer.Validate(Reset, "Reset", body), e => e.Contains("incomplete placeholder"));
    }

    [Theory]
    [InlineData("", "{{resetUrl}}", "Enter a subject.")]
    [InlineData("Reset", "  ", "Enter a body.")]
    [InlineData("Line one\nline two", "{{resetUrl}}", "one line")]
    public void Subject_and_body_are_required_and_the_subject_is_one_line(string subject, string body, string error)
    {
        Assert.Contains(EmailTemplateRenderer.Validate(Reset, subject, body), e => e.Contains(error));
    }

    [Fact]
    public void Values_are_html_encoded_in_the_body_and_plain_in_the_subject()
    {
        var values = new Dictionary<string, string> { ["userName"] = "<b>Eve</b> & co" };
        Assert.Equal("<p>Hi &lt;b&gt;Eve&lt;/b&gt; &amp; co</p>", EmailTemplateRenderer.RenderBody("<p>Hi {{ userName }}</p>", values));
        Assert.Equal("Hi <b>Eve</b> & co", EmailTemplateRenderer.RenderSubject("Hi {{userName}}", values));
        Assert.Throws<InvalidOperationException>(() => EmailTemplateRenderer.RenderBody("{{appName}}", values));
    }

    [Fact]
    public void The_layout_frames_the_body_with_the_encoded_site_name()
    {
        var html = EmailLayout.Wrap("<p>Body</p>", "Eve & <Co>", "https://example.com/");
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<p>Body</p>", html);
        Assert.Contains("Eve &amp; &lt;Co&gt;", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains(EmailLayout.Orange, html);
    }
}
