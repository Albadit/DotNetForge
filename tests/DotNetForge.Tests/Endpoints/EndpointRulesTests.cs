using System.Text.Json;
using DotNetForge.Core.Endpoints;
using Xunit;

namespace DotNetForge.Tests.Endpoints;

/// <summary>
/// The pure rules of custom API endpoints (.docs/features/custom-endpoints.md): path templates, matching, parameter
/// definitions and turning request values into typed SQL parameter values.
/// </summary>
public sealed class EndpointRulesTests
{
    [Theory]
    [InlineData("/Products/{category}/", "products/{category}")]
    [InlineData("orders/{id}/items", "orders/{id}/items")]
    [InlineData("v1.0/stats_daily", "v1.0/stats_daily")]
    public void Paths_are_trimmed_and_lower_cased_but_keep_parameter_names(string path, string expected) =>
        Assert.Equal(expected, EndpointRules.NormalizePath(path));

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("products/{id}/{ID}")]
    [InlineData("products/{1id}")]
    [InlineData("products/ id")]
    [InlineData("products?x=1")]
    [InlineData("-products")]
    [InlineData("a/b/c/d/e/f/g/h/i/j/k")]
    public void Invalid_paths_are_rejected(string path) => Assert.Null(EndpointRules.NormalizePath(path));

    [Fact]
    public void An_operation_sub_path_may_be_empty_and_joins_the_endpoint_path()
    {
        Assert.Equal(string.Empty, EndpointRules.NormalizeSubPath(" / "));
        Assert.Equal("{id}/reviews", EndpointRules.NormalizeSubPath("/{id}/Reviews/"));
        Assert.Null(EndpointRules.NormalizeSubPath("{1id}"));

        Assert.Equal("products", EndpointRules.Combine("products", ""));
        Assert.Equal("shops/{shop}/products/{id}", EndpointRules.Combine("shops/{shop}/products", "{id}"));
        Assert.Null(EndpointRules.Combine("items/{id}", "{id}")); // the same {name} twice
        Assert.Null(EndpointRules.Combine("a/b/c/d/e/f", "g/h/i/j/k")); // more than 10 segments
    }

    [Fact]
    public void A_request_path_matches_a_template_and_yields_its_route_values()
    {
        var values = EndpointRules.Match("products/{category}/top", "Products/chairs%20%26%20tables/top");
        Assert.NotNull(values);
        Assert.Equal("chairs & tables", values!["category"]);

        Assert.Null(EndpointRules.Match("products/{category}", "products"));
        Assert.Null(EndpointRules.Match("products/{category}", "products/a/b"));
        Assert.Null(EndpointRules.Match("products/{category}", "orders/a"));
    }

    [Fact]
    public void Route_parameters_must_match_the_path_and_are_always_required()
    {
        const string json = """[{ "name": "id", "type": "int", "source": "route", "required": false }, { "name": "", "type": "int" }]""";
        var (parameters, errors) = EndpointRules.ParseParameters(json, "items/{id}");
        Assert.Empty(errors);
        Assert.True(Assert.Single(parameters).Required);

        Assert.Contains(EndpointRules.ParseParameters("[]", "items/{id}").Errors, e => e.Contains("{id}"));
        Assert.Contains(EndpointRules.ParseParameters(json, "items").Errors, e => e.Contains("no {id}"));
        Assert.Contains(EndpointRules.ParseParameters("""[{ "name": "id", "source": "query" }]""", "items/{id}").Errors,
            e => e.Contains("source must be route"));
    }

    [Theory]
    [InlineData("""[{ "name": "a b" }]""", "letters, digits")]
    [InlineData("""[{ "name": "a" }, { "name": "A" }]""", "listed twice")]
    [InlineData("""[{ "name": "n", "type": "int", "default": "x" }]""", "whole number")]
    [InlineData("""{ "name": "n" }""", "must be a list")]
    [InlineData("""[{""", "not valid JSON")]
    public void Bad_parameter_definitions_are_explained(string json, string message) =>
        Assert.Contains(EndpointRules.ParseParameters(json, "items").Errors, e => e.Contains(message));

    [Fact]
    public void Sql_placeholders_are_found_without_server_globals_or_email_addresses()
    {
        var names = EndpointRules.SqlParameterNames("SELECT @@ROWCOUNT, 'a@b.com', * FROM t WHERE id = @id AND x = @Id OR y = @name_2");
        Assert.Equal(new[] { "id", "name_2" }, names);
    }

    [Theory]
    [InlineData("int", "42", 42L)]
    [InlineData("number", "4.5", 4.5)]
    [InlineData("bool", "yes", true)]
    [InlineData("bool", "0", false)]
    [InlineData("string", " keep spaces ", " keep spaces ")]
    public void Text_values_are_converted_to_the_parameter_type(string type, string text, object expected)
    {
        var (value, error) = EndpointRules.Convert(new EndpointParameter("p", type, "query", false, null), text);
        Assert.Null(error);
        Assert.Equal(expected is double d ? (decimal)d : expected, value);
    }

    [Theory]
    [InlineData("int", "4.5")]
    [InlineData("number", "four")]
    [InlineData("bool", "maybe")]
    [InlineData("date", "yesterday")]
    [InlineData("guid", "123")]
    [InlineData("json", "{")]
    public void Values_of_the_wrong_type_are_errors(string type, string text) =>
        Assert.NotNull(EndpointRules.Convert(new EndpointParameter("p", type, "query", false, null), text).Error);

    [Fact]
    public void Dates_become_utc_and_guids_parse()
    {
        var date = EndpointRules.Convert(new EndpointParameter("d", "date", "query", false, null), "2026-10-26T12:00:00+02:00").Value;
        Assert.Equal(new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc), date);
        var id = Guid.NewGuid();
        Assert.Equal(id, EndpointRules.Convert(new EndpointParameter("g", "guid", "query", false, null), id.ToString()).Value);
    }

    [Fact]
    public void Binding_reads_route_query_and_body_applies_defaults_and_reports_problems()
    {
        var parameters = new[]
        {
            new EndpointParameter("id", "int", "route", true, null),
            new EndpointParameter("limit", "int", "query", false, "10"),
            new EndpointParameter("name", "string", "body", true, null),
            new EndpointParameter("meta", "json", "body", false, null),
        };
        var body = JsonDocument.Parse("""{ "Name": "Oak chair", "meta": { "tags": ["a"] } }""").RootElement;

        var (values, errors) = EndpointRules.Bind(parameters, new Dictionary<string, string> { ["id"] = "7" }, _ => null, body);
        Assert.Empty(errors);
        Assert.Equal(7L, values["id"]);
        Assert.Equal(10L, values["limit"]);
        Assert.Equal("Oak chair", values["name"]);
        Assert.Equal("""{ "tags": ["a"] }""", values["meta"]);

        var (_, missing) = EndpointRules.Bind(parameters, new Dictionary<string, string> { ["id"] = "x" }, q => q == "limit" ? "many" : null, null);
        Assert.Equal(new[] { "id", "limit", "name" }, missing.Select(e => e.Parameter));
    }
}
