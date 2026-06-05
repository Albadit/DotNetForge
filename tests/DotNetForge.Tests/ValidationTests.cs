using DotNetForge.Core.Validation;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>Password policy rejects weak passwords (security.md, installation_setup.md).</summary>
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1")]      // too short
    [InlineData("allletters")]  // no digit
    [InlineData("12345678")]    // no letter + common
    [InlineData("password")]    // common, no digit
    [InlineData("")]            // empty
    public void Weak_passwords_are_rejected(string password) =>
        Assert.False(PasswordPolicy.IsValid(password));

    [Theory]
    [InlineData("Sup3rSecret")]
    [InlineData("correct-horse-1")]
    public void Strong_passwords_are_accepted(string password) =>
        Assert.True(PasswordPolicy.IsValid(password));
}

/// <summary>Email format validation (server-side).</summary>
public sealed class EmailValidatorTests
{
    [Theory]
    [InlineData("admin@example.com", true)]
    [InlineData("a.b-c@sub.example.co", true)]
    [InlineData("not-an-email", false)]
    [InlineData("missing@domain", false)]
    [InlineData("@example.com", false)]
    [InlineData("", false)]
    public void Validates_email_format(string email, bool expected) =>
        Assert.Equal(expected, EmailValidator.IsValid(email));
}

/// <summary>Slug generation and validation (content_manager.md).</summary>
public sealed class SlugHelperTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Trim  Me  ", "trim-me")]
    [InlineData("Already-Slug", "already-slug")]
    [InlineData("Special!@#Chars", "special-chars")]
    public void Slugifies_text(string input, string expected) =>
        Assert.Equal(expected, SlugHelper.Slugify(input));

    [Theory]
    [InlineData("valid-slug", true)]
    [InlineData("Invalid Slug", false)]
    [InlineData("UPPER", false)]
    [InlineData("-leading", false)]
    public void Validates_slug(string slug, bool expected) =>
        Assert.Equal(expected, SlugHelper.IsValid(slug));
}
