using DotNetForge.Core.Content;
using DotNetForge.PageBuilder.Rendering;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.Tests.Content;

/// <summary>
/// The pure rules of structured content (.docs/features/structured-content.md): field definitions, entry validation,
/// value migration after a schema change, the value index and the item data content lists bind to.
/// </summary>
public sealed class ContentSchemaTests
{
    private static readonly Guid Categories = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static IReadOnlyList<FieldDefinition> Fields(string json)
    {
        var result = ContentSchema.Validate(JToken.Parse(json), id => id == Categories);
        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
        return result.Fields;
    }

    private static readonly string Products = $$"""
        [
          { "key": "name", "label": "Name", "type": "text", "required": true, "maxLength": 20 },
          { "key": "slug", "type": "slug", "slugFrom": "name", "unique": true },
          { "key": "price", "label": "Price", "type": "number", "min": 0, "decimals": 2 },
          { "key": "stock", "type": "number", "integer": true },
          { "key": "inStock", "type": "boolean" },
          { "key": "released", "type": "date" },
          { "key": "size", "type": "select", "choices": [{ "value": "s", "label": "Small" }, { "value": "l", "label": "Large" }] },
          { "key": "tags", "type": "array", "maxRows": 3 },
          { "key": "category", "type": "relationship", "relationTo": "{{Categories}}" },
          { "key": "body", "type": "richText" },
          { "key": "dims", "type": "group", "fields": [{ "key": "width", "type": "number" }] },
          { "key": "variants", "type": "repeater", "minRows": 2, "fields": [{ "key": "name", "type": "text", "required": true }] },
          { "key": "site", "type": "url" },
          { "key": "contact", "type": "email" },
          { "key": "code", "type": "text", "pattern": "^[A-Z]{2}-\\d+$" }
        ]
        """;

    [Fact]
    public void Field_definitions_are_checked_and_normalized()
    {
        var fields = Fields(Products);
        Assert.Equal(15, fields.Count);
        Assert.Equal("Slug", fields[1].Label); // a missing label is made from the key
        Assert.All(fields, f => Assert.Matches("^[a-z0-9]{12}$", f.Id));
        Assert.Equal("width", fields.Single(f => f.Key == "dims").Fields[0].Key);

        // The stored form reads back to the same definitions (ids included).
        var again = ContentSchema.Parse(ContentSchema.Serialize(fields));
        Assert.Equal(ContentSchema.Serialize(fields), ContentSchema.Serialize(again));
    }

    [Theory]
    [InlineData("""[{ "key": "Name", "type": "text" }]""", "API name must start with a lower-case letter")]
    [InlineData("""[{ "key": "ab", "type": "text" }, { "key": "aB", "type": "text" }]""", "API names must be unique")]
    [InlineData("""[{ "key": "a", "type": "colour" }]""", "unknown field type")]
    [InlineData("""[{ "key": "a", "type": "select", "choices": [] }]""", "needs at least one option")]
    [InlineData("""[{ "key": "a", "type": "relationship" }]""", "choose the content type")]
    [InlineData("""[{ "key": "a", "type": "relationship", "relationTo": "99999999-2222-3333-4444-555555555555" }]""", "choose the content type")]
    [InlineData("""[{ "key": "a", "type": "group", "fields": [] }]""", "add at least one field")]
    [InlineData("""[{ "key": "a", "type": "text", "pattern": "([a-" }]""", "not a valid regular expression")]
    [InlineData("""[{ "key": "a", "type": "number", "min": 5, "max": 1 }]""", "minimum is larger than the maximum")]
    [InlineData("""[{ "key": "a", "type": "group", "fields": [{ "key": "b", "type": "text", "unique": true }] }]""", "only top-level")]
    [InlineData("""[{ "key": "a", "type": "slug", "slugFrom": "missing" }]""", "not a text field of this type")]
    [InlineData("""[{ "key": "a", "type": "number", "default": "x" }]""", "default value is not valid")]
    public void Invalid_definitions_are_refused_with_a_reason(string json, string reason)
    {
        var result = ContentSchema.Validate(JToken.Parse(json), id => id == Categories);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Groups_and_repeaters_nest_at_most_three_levels()
    {
        const string deep = """
            [{ "key": "a", "type": "group", "fields": [{ "key": "b", "type": "repeater", "fields": [{ "key": "c", "type": "group", "fields": [{ "key": "d", "type": "text" }] }] }] }]
            """;
        Assert.Contains(ContentSchema.Validate(JToken.Parse(deep), _ => true).Errors, e => e.Contains("nested at most"));
    }

    [Fact]
    public void Entry_values_are_converted_from_form_strings_and_checked()
    {
        var fields = Fields(Products);
        var input = JObject.Parse($$"""
            {
              "name": "  Oak Chair ", "price": "12.50", "stock": "3", "inStock": "on", "released": "2026-10-26",
              "size": "l", "tags": "a\n\nb\n", "category": "{{Categories}}", "body": "First\n\nSecond",
              "dims": { "width": "40" }, "variants": [{ "name": "Red" }, {}, { "name": "Blue" }],
              "site": "https://example.com", "contact": "a@b.co", "code": "AB-12", "unknown": "dropped"
            }
            """);
        var result = EntryData.Validate(fields, input, publish: true, References(entries: true));
        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        var d = result.Data;
        Assert.Equal("Oak Chair", (string)d["name"]!);
        Assert.Equal("oak-chair", (string)d["slug"]!); // made from the name
        Assert.Equal(12.5, (double)d["price"]!);
        Assert.Equal(JTokenType.Integer, d["stock"]!.Type);
        Assert.True((bool)d["inStock"]!);
        Assert.Equal("2026-10-26", (string)d["released"]!);
        Assert.Equal(new[] { "a", "b" }, d["tags"]!.Values<string>());
        Assert.Equal(2, ((JArray)d["body"]!).Count);
        Assert.Equal(40L, (long)d["dims"]!["width"]!);
        Assert.Equal(2, ((JArray)d["variants"]!).Count); // the empty row is dropped
        Assert.Null(d["unknown"]);
    }

    [Theory]
    [InlineData("name", "\"A name that is far too long for it\"", "at most 20 characters")]
    [InlineData("price", "-1", "at least 0")]
    [InlineData("price", "\"abc\"", "must be a number")]
    [InlineData("stock", "1.5", "whole number")]
    [InlineData("released", "\"yesterday\"", "must be a date")]
    [InlineData("size", "\"xl\"", "listed options")]
    [InlineData("tags", "[\"a\", \"b\", \"c\", \"d\"]", "at most 3 items")]
    [InlineData("category", "\"99999999-2222-3333-4444-555555555555\"", "linked entry does not exist")]
    [InlineData("site", "\"javascript:alert(1)\"", "use https://")]
    [InlineData("contact", "\"not-an-email\"", "not a valid email")]
    [InlineData("code", "\"ab-12\"", "expected format")]
    public void Invalid_values_are_reported_with_the_field_path(string key, string value, string message)
    {
        var fields = Fields(Products);
        var result = EntryData.Validate(fields, JObject.Parse($$"""{ "name": "Ok", "{{key}}": {{value}} }"""), publish: false, References(entries: false));
        var error = Assert.Single(result.Errors);
        Assert.Equal(key, error.Path);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Publishing_requires_required_fields_and_minimum_rows_a_draft_does_not()
    {
        var fields = Fields(Products);
        var input = JObject.Parse("""{ "variants": [{ "name": "Only one" }, { "name": "" }] }""");
        Assert.True(EntryData.Validate(fields, input, publish: false, References(false)).IsValid);
        var errors = EntryData.Validate(fields, input, publish: true, References(false)).Errors;
        Assert.Contains(errors, e => e.Path == "name" && e.Message == "Name is required.");
        Assert.Contains(errors, e => e.Path == "variants" && e.Message.Contains("at least 2 items"));
        // Nested required fields carry their row's path.
        var nested = EntryData.Validate(fields, JObject.Parse("""{ "name": "x", "variants": [{ "name": "a" }, { "name": "b" }, { "other": 1 }] }"""), true, References(false));
        Assert.True(nested.IsValid); // a row with only unknown keys is empty and dropped
    }

    [Fact]
    public void Values_follow_renamed_fields_and_removed_fields_lose_theirs()
    {
        var before = Fields("""
            [{ "id": "aaaaaaaaaaaa", "key": "title", "type": "text" }, { "id": "bbbbbbbbbbbb", "key": "old", "type": "text" },
             { "id": "cccccccccccc", "key": "rows", "type": "repeater", "fields": [{ "id": "dddddddddddd", "key": "n", "type": "text" }] }]
            """);
        var after = Fields("""
            [{ "id": "aaaaaaaaaaaa", "key": "heading", "type": "text" },
             { "id": "cccccccccccc", "key": "rows", "type": "repeater", "fields": [{ "id": "dddddddddddd", "key": "label", "type": "text" }] }]
            """);
        var migrated = EntryData.Migrate(before, after, JObject.Parse("""{ "title": "T", "old": "gone", "rows": [{ "n": "x" }] }"""));
        Assert.Equal("""{"heading":"T","rows":[{"label":"x"}]}""", migrated.ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public void Index_values_hold_top_level_scalars_for_filtering_and_sorting()
    {
        var fields = Fields(Products);
        var data = EntryData.Validate(fields, JObject.Parse("""{ "name": "Oak", "price": 3, "inStock": true, "released": "2026-01-02", "tags": ["A", "B"] }"""),
            false, References(false)).Data;
        var values = EntryData.IndexValues(fields, data);
        Assert.Contains(values, v => v.Field == "name" && v.Text == "oak");
        Assert.Contains(values, v => v.Field == "price" && v.Number == 3);
        Assert.Contains(values, v => v.Field == "inStock" && v.Number == 1 && v.Text == "true");
        Assert.Contains(values, v => v.Field == "released" && v.Date == new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, values.Count(v => v.Field == "tags"));
        Assert.DoesNotContain(values, v => v.Field == "dims" || v.Field == "body");
    }

    [Fact]
    public void Item_data_formats_values_and_flattens_groups_and_linked_entries()
    {
        var fields = Fields(Products);
        var categoryFields = Fields("""[{ "key": "name", "type": "text" }, { "key": "icon", "type": "image" }]""");
        var media = Guid.NewGuid();
        var related = new RelatedEntry(Categories, "Chairs", categoryFields, JObject.Parse($$"""{ "name": "Chairs", "icon": "{{media}}" }"""));
        var data = JObject.Parse($$"""
            { "name": "Oak", "price": 1249.5, "inStock": false, "released": "2026-10-26", "size": "l", "category": "{{Categories}}",
              "dims": { "width": 40 }, "body": [{ "type": "p", "runs": [{ "text": "Hi", "b": true }] }] }
            """);
        var item = ItemData.Build(Guid.NewGuid(), "Oak", "/p/oak", fields, data,
            id => id == media ? new MediaInfo("/media/x/icon.png", "icon.png", true) : null,
            id => id == Categories ? related : null);
        var f = (JObject)item["fields"]!;
        string Text(string path) => (string)f[path]!["text"]!;
        Assert.Equal("1,249.50", Text("price"));
        Assert.Equal("No", Text("inStock"));
        Assert.Equal("Oct 26, 2026", Text("released"));
        Assert.Equal("Large", Text("size"));
        Assert.Equal("Chairs", Text("category"));
        Assert.Equal("Chairs", Text("category.name"));
        Assert.Equal("/media/x/icon.png", (string)f["category.icon"]!["url"]!);
        Assert.Equal("40", Text("dims.width"));
        Assert.Equal("Hi", Text("body"));
        Assert.Equal("/p/oak", (string)f["$url"]!["url"]!);

        var paths = ItemData.Paths(fields, id => id == Categories ? ("Categories", categoryFields) : null).Select(p => p.Path).ToList();
        Assert.Contains("dims.width", paths);
        Assert.Contains("category.name", paths);
        Assert.DoesNotContain("dims", paths);
    }

    [Fact]
    public void Bound_props_are_filled_from_the_item_and_cleaned_like_typed_values()
    {
        var fields = JObject.Parse("""
            { "name": { "text": "Café & co" }, "slug": { "text": "café co!" }, "photo": { "text": "p.png", "url": "javascript:alert(1)" },
              "body": { "text": "x", "rich": [{ "type": "p", "runs": [{ "text": "Rich" }] }] } }
            """);
        Assert.Equal("From Café & co", (string)Binding.Value("text", "From {name}", fields, 100)!);
        Assert.Equal("/shop/caf%C3%A9%20co%21", (string)Binding.Value("url", "/shop/{slug}", fields, 100)!);
        Assert.Equal("javascript:alert(1)", (string)Binding.Value("image", "{photo}", fields, 100)!); // cleaned by the prop validator afterwards
        Assert.Equal("Rich", (string)Binding.Value("rich", "{body}", fields, 100)![0]!["runs"]![0]!["text"]!);
        Assert.Equal("Caf", (string)Binding.Value("text", "{name}", fields, 3)!);
        Assert.Equal(string.Empty, (string)Binding.Value("text", "{missing}", fields, 100)!);
        Assert.Null(Binding.Value("bool", "{name}", fields, 100));
    }

    [Fact]
    public void Templates_create_valid_definitions()
    {
        foreach (var template in ContentTypeTemplates.All)
        {
            var json = template.Fields(_ => Categories);
            var result = ContentSchema.Validate(json, _ => true);
            Assert.True(result.IsValid, template.Id + ": " + string.Join(" | ", result.Errors));
            Assert.NotNull(ContentSchema.TitleKey(result.Fields, template.TitleField));
        }
    }

    private static EntryReferences References(bool entries) =>
        new((_, _) => true, (id, type) => entries && id == Categories && type == Categories);
}
