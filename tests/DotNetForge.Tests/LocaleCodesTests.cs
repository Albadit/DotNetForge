using DotNetForge.Core.Validation;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>Locale codes: well-formed BCP-47, known to the culture data, stored canonically (.docs/features/internationalization.md#rules-and-validation).</summary>
public sealed class LocaleCodesTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("de-de", "de-DE")]
    [InlineData(" pt_br ", "pt-BR")]
    [InlineData("ZH-hans", "zh-Hans")]
    [InlineData("sr-latn-rs", "sr-Latn-RS")]
    [InlineData("es-419", "es-419")]
    public void Valid_codes_come_back_canonical(string input, string expected)
    {
        Assert.Equal(expected, LocaleCodes.Normalize(input, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("english")]
    [InlineData("en-")]
    [InlineData("en-US-x-private")]
    [InlineData("e")]
    [InlineData("en US")]
    [InlineData("xx")]
    [InlineData("qq-ZZ")]
    public void Malformed_or_unknown_codes_are_refused(string input)
    {
        Assert.Null(LocaleCodes.Normalize(input, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_suggested_name_is_the_language_in_its_own_words()
    {
        Assert.StartsWith("Deutsch", LocaleCodes.SuggestedName("de-DE"));
        Assert.Equal("English", LocaleCodes.SuggestedName("en"));
        Assert.Equal("de", LocaleCodes.Language("de-DE"));
    }
}
