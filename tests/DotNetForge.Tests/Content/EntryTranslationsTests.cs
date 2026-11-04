using DotNetForge.Core.Content;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.Content;

/// <summary>Translated entries: which fields, keeping, merging and the default swap (.docs/features/internationalization.md#entries).</summary>
public sealed class EntryTranslationsTests
{
    private static readonly IReadOnlyList<FieldDefinition> Fields = new[]
    {
        new FieldDefinition { Key = "title", Label = "Title", Type = FieldTypes.Text },
        new FieldDefinition { Key = "body", Label = "Body", Type = FieldTypes.RichText },
        new FieldDefinition { Key = "price", Label = "Price", Type = FieldTypes.Number },
        new FieldDefinition { Key = "slug", Label = "Slug", Type = FieldTypes.Slug },
        new FieldDefinition { Key = "sku", Label = "SKU", Type = FieldTypes.Text, Unique = true },
    };

    [Fact]
    public void Only_text_like_fields_that_are_not_unique_are_translated()
    {
        Assert.Equal(new[] { "title", "body" }, EntryTranslations.Fields(Fields).Select(f => f.Key));
    }

    [Fact]
    public void Keep_drops_shared_fields_and_empty_values()
    {
        var kept = EntryTranslations.Keep(Fields, JObject.Parse("""{ "title": "Hallo", "body": [], "price": 3, "slug": "hallo" }"""));
        Assert.Equal("""{"title":"Hallo"}""", kept.ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void Merge_shows_translated_values_over_the_main_ones()
    {
        var main = JObject.Parse("""{ "title": "Hello", "body": [{ "type": "p" }], "price": 3 }""");
        var merged = EntryTranslations.Merge(Fields, main, JObject.Parse("""{ "title": "Hallo", "body": "  ", "price": 99 }"""));

        Assert.Equal("Hallo", (string?)merged["title"]);
        Assert.Equal(JTokenType.Array, merged["body"]!.Type); // an empty translation falls back
        Assert.Equal(3, (int)merged["price"]!); // shared fields never come from a translation
        Assert.Equal("Hello", (string?)main["title"]); // the main values are not changed
    }

    [Fact]
    public void Swap_promotes_the_translation_and_keeps_the_old_main_values()
    {
        var (main, old) = EntryTranslations.Swap(Fields, JObject.Parse("""{ "title": "Hello", "body": [{ "type": "p" }], "price": 3 }"""),
            JObject.Parse("""{ "title": "Hallo" }"""));

        Assert.Equal("Hallo", (string?)main["title"]);
        Assert.Equal(3, (int)main["price"]!);
        Assert.Equal("""{"title":"Hello"}""", old.ToString(Newtonsoft.Json.Formatting.None)); // the untranslated body stays main only
    }
}
