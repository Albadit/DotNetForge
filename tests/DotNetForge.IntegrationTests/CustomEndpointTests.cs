using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Abstractions.Security;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Custom API endpoints end to end (.docs/features/custom-endpoints.md): a Super Admin writes SQL on the API Endpoints
/// screen, and <c>/api/custom/{path}</c> runs it with the request's values as parameters - reads, writes, access by
/// token or public, validation, errors and the editor's test run. The SQL uses a table the test creates (lower-case,
/// unquoted) so it runs unchanged on SQLite and PostgreSQL.
/// </summary>
public sealed class CustomEndpointTests
{
    private const string Table = "dnf_test_items";

    private static Task CreateTableAsync(DotNetForgeWebFactory factory) => factory.WithDbAsync(async db =>
    {
        await db.Database.ExecuteSqlRawAsync($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name VARCHAR(100) NOT NULL, price INTEGER NOT NULL)");
        await db.Database.ExecuteSqlRawAsync($"INSERT INTO {Table} (id, name, price) VALUES (1, 'Oak chair', 120), (2, 'Pine table', 300), (3, 'Lamp', 40)");
    });

    /// <summary>One operation as the editor posts it (inside <c>OperationsJson</c>).</summary>
    private static object Op(string method, string sql, string path = "", string parameters = "[]", string result = "Rows") => new
    {
        method,
        path,
        sql,
        parameters = JsonDocument.Parse(parameters).RootElement,
        result,
        timeoutSeconds = 30,
        maxRows = 1000,
    };

    private static Dictionary<string, string> Endpoint(string name, string path, IEnumerable<object> operations, string access = "Token") => new()
    {
        ["Name"] = name,
        ["Path"] = path,
        ["Access"] = access,
        ["Enabled"] = "true",
        ["OperationsJson"] = JsonSerializer.Serialize(operations),
    };

    /// <summary>An endpoint with one operation on its own path.</summary>
    private static Dictionary<string, string> Endpoint(string name, string method, string path, string sql, string parameters = "[]",
        string result = "Rows", string access = "Token") =>
        Endpoint(name, path, new[] { Op(method, sql, parameters: parameters, result: result) }, access);

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient admin, Dictionary<string, string> fields) =>
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/new", fields);

    private static async Task<HttpClient> ApiClientAsync(DotNetForgeWebFactory factory, params string[] permissions)
    {
        var plaintext = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            var generated = factory.Services.GetRequiredService<IApiTokenFactory>().Generate();
            plaintext = generated.Plaintext;
            db.ApiTokens.Add(new ApiToken
            {
                Name = "endpoints " + Guid.NewGuid().ToString("N")[..6],
                PermissionsCsv = string.Join(',', permissions),
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Creates an endpoint on the screen and returns the token its editor shows (Call it).</summary>
    private static async Task<string> CreateWithTokenAsync(HttpClient admin, Dictionary<string, string> fields)
    {
        var created = await CreateAsync(admin, fields);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        return await TokenAsync(admin, created.Headers.Location!.OriginalString);
    }

    private static async Task<string> TokenAsync(HttpClient admin, string editUrl)
    {
        var match = Regex.Match(await admin.GetStringAsync(editUrl), "Bearer (dnfe_[A-Za-z0-9_-]+)");
        Assert.True(match.Success, "The editor shows the endpoint's token.");
        return match.Groups[1].Value;
    }

    private static HttpClient Caller(DotNetForgeWebFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [SqlDatabaseFact]
    public async Task One_endpoint_answers_several_methods_with_typed_parameters_and_its_own_token()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await CreateTableAsync(factory);
        var admin = await factory.SignInAsync();

        // One endpoint, four operations - list, one item, add, delete - and one token for all of them.
        const string id = """[{ "name": "id", "type": "int", "source": "route" }]""";
        var itemsToken = await CreateWithTokenAsync(admin, Endpoint("Items", "items", new[]
        {
            Op("GET", $"SELECT id, name, price FROM {Table} WHERE price >= @min ORDER BY id",
                parameters: """[{ "name": "min", "type": "int", "source": "query", "default": "0" }]"""),
            Op("GET", $"SELECT id, name, price FROM {Table} WHERE id = @id", "{id}", id, "FirstRow"),
            Op("POST", $"INSERT INTO {Table} (id, name, price) VALUES (@id, @name, @price)",
                parameters: """[{ "name": "id", "type": "int", "source": "body", "required": true }, { "name": "name", "source": "body", "required": true }, { "name": "price", "type": "int", "source": "body", "required": true }]""",
                result: "AffectedRows"),
            Op("DELETE", $"DELETE FROM {Table} WHERE id = @id", "{id}", id, "AffectedRows"),
        }));
        var countToken = await CreateWithTokenAsync(admin, Endpoint("Count", "GET", "items-count",
            $"SELECT COUNT(*) FROM {Table}", result: "Value"));
        Assert.NotEqual(itemsToken, countToken);

        // The screen lists the endpoint with its operations; the sidebar links to it.
        var index = await admin.GetStringAsync("/admin/api-endpoints");
        Assert.Contains("<code>/api/custom/items</code>", index);
        Assert.Contains("DELETE /{id}", index);
        Assert.Contains("href=\"/admin/api-endpoints\"", index);

        // Access: no token, an API token (even one with every permission) or another endpoint's token: 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/custom/items")).StatusCode);
        var apiToken = await ApiClientAsync(factory, PermissionKeys.All.ToArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await apiToken.GetAsync("/api/custom/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Caller(factory, countToken).GetAsync("/api/custom/items")).StatusCode);

        var api = Caller(factory, itemsToken);
        var all = await JsonAsync(await api.GetAsync("/api/custom/items"));
        Assert.Equal(3, all.GetProperty("count").GetInt32());
        Assert.Equal("Oak chair", all.GetProperty("data")[0].GetProperty("name").GetString());
        var expensive = await JsonAsync(await api.GetAsync("/api/custom/items?min=100"));
        Assert.Equal(new[] { 1, 2 }, expensive.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
        var item = api;
        var one = await JsonAsync(await item.GetAsync("/api/custom/ITEMS/2"));
        Assert.Equal("Pine table", one.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await item.GetAsync("/api/custom/items/99")).StatusCode);

        // Values are parameters, never SQL: a quote in a value is just text, a wrong type is a 400.
        var bad = await api.GetAsync("/api/custom/items?min=1%20OR%201=1");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("min", (await JsonAsync(bad)).GetProperty("errors")[0].GetProperty("parameter").GetString());

        // Writes: POST with a JSON body, DELETE by route value; both audited.
        var add = api;
        var added = await add.PostAsJsonAsync("/api/custom/items", new { id = 4, name = "Bob's \"stool\"); DROP TABLE x;--", price = 55 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(1, (await JsonAsync(added)).GetProperty("affected").GetInt32());
        Assert.Equal("Bob's \"stool\"); DROP TABLE x;--", (await JsonAsync(await item.GetAsync("/api/custom/items/4"))).GetProperty("data").GetProperty("name").GetString());
        var missing = await add.PostAsJsonAsync("/api/custom/items", new { id = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(1, (await JsonAsync(await api.DeleteAsync("/api/custom/items/1"))).GetProperty("affected").GetInt32());
        Assert.Equal(3, (await JsonAsync(await Caller(factory, countToken).GetAsync("/api/custom/items-count"))).GetProperty("data").GetInt32());

        // Wrong method or unknown path: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await item.PutAsJsonAsync("/api/custom/items/2", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/custom/nothing")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.EndpointCreated));
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.EndpointExecuted && a.Success));
        });
    }

    [SqlDatabaseFact]
    public async Task Public_endpoints_need_no_token_and_disabled_ones_answer_404()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await CreateTableAsync(factory);
        var admin = await factory.SignInAsync();

        var created = await CreateAsync(admin, Endpoint("Cheap items", "GET", "cheap", $"SELECT name FROM {Table} WHERE price < 100", access: "Public"));
        var anonymous = factory.CreateClient();
        var cheap = await JsonAsync(await anonymous.GetAsync("/api/custom/cheap"));
        Assert.Equal("Lamp", Assert.Single(cheap.GetProperty("data").EnumerateArray()).GetProperty("name").GetString());

        // A public endpoint ignores whatever token is sent.
        Assert.Equal(HttpStatusCode.OK, (await Caller(factory, "dnfe_not-a-token").GetAsync("/api/custom/cheap")).StatusCode);

        // A failing public endpoint does not show the database's message; a caller with the endpoint's token sees it.
        await CreateAsync(admin, Endpoint("Broken", "GET", "broken", "SELECT * FROM no_such_table", access: "Public"));
        var failed = await anonymous.GetAsync("/api/custom/broken");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.DoesNotContain("no_such_table", await failed.Content.ReadAsStringAsync());
        var brokenToken = await CreateWithTokenAsync(admin, Endpoint("Broken with token", "GET", "broken-token", "SELECT * FROM no_such_table"));
        Assert.Contains("no_such_table", await (await Caller(factory, brokenToken).GetAsync("/api/custom/broken-token")).Content.ReadAsStringAsync());

        var edit = created.Headers.Location!.OriginalString;
        var fields = Endpoint("Cheap items", "GET", "cheap", $"SELECT name FROM {Table} WHERE price < 100", access: "Public");
        fields["Enabled"] = "false";
        Assert.Equal(HttpStatusCode.Redirect, (await DotNetForgeWebFactory.PostFormAsync(admin, edit, edit, fields)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/custom/cheap")).StatusCode);
    }

    [SqlDatabaseFact]
    public async Task The_editor_validates_runs_tests_rotates_tokens_and_is_for_super_admins_and_developers()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await CreateTableAsync(factory);
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var admin = await factory.SignInAsync();

        // Invalid input is explained on the form.
        var invalid = await CreateAsync(admin, Endpoint("", "GET", "items/{id}", ""));
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Give the endpoint a name", html);
        Assert.Contains("GET: write the SQL it runs", html);
        Assert.Contains("add a parameter", html);
        var empty = await CreateAsync(admin, Endpoint("Empty", "empty", Array.Empty<object>()));
        Assert.Contains("Add at least one operation", await empty.Content.ReadAsStringAsync());

        // One operation per method and path shape - within an endpoint and across endpoints - and one endpoint per path.
        var twins = await CreateAsync(admin, Endpoint("Twins", "twins", new[]
        {
            Op("GET", $"SELECT * FROM {Table} WHERE id = @id", "{id}", """[{ "name": "id", "source": "route" }]"""),
            Op("GET", $"SELECT * FROM {Table} WHERE name = @name", "{name}", """[{ "name": "name", "source": "route" }]"""),
        }));
        Assert.Contains("the endpoint already has GET /{id}", WebUtility.HtmlDecode(await twins.Content.ReadAsStringAsync()));
        await CreateAsync(admin, Endpoint("By id", "GET", "items/{id}", $"SELECT * FROM {Table} WHERE id = @id", """[{ "name": "id", "source": "route" }]"""));
        var clash = await CreateAsync(admin, Endpoint("By name", "GET", "items/{name}", $"SELECT * FROM {Table} WHERE name = @name", """[{ "name": "name", "source": "route" }]"""));
        Assert.Contains("already answers GET /api/custom/items/{id}", WebUtility.HtmlDecode(await clash.Content.ReadAsStringAsync()));
        var samePath = await CreateAsync(admin, Endpoint("Same path", "POST", "items/{id}", "SELECT @id", """[{ "name": "id", "source": "route" }]"""));
        Assert.Contains("already uses /api/custom/items/{id}", WebUtility.HtmlDecode(await samePath.Content.ReadAsStringAsync()));

        // The test run answers like the API, and undoes its changes unless told otherwise.
        var insertOperation = Op("POST", $"INSERT INTO {Table} (id, name, price) VALUES (@id, 'Test', 1)",
            parameters: """[{ "name": "id", "type": "int", "source": "body", "required": true }]""", result: "AffectedRows");
        var insert = Endpoint("Add", "add", new[] { insertOperation });
        insert["operation"] = JsonSerializer.Serialize(insertOperation);
        insert["testValues"] = """{ "id": "10" }""";
        var test = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/test", insert);
        var result = await JsonAsync(test);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(1, result.GetProperty("body").GetProperty("affected").GetInt32());
        Assert.True(result.GetProperty("rolledBack").GetBoolean());
        await factory.WithDbAsync(async db =>
            Assert.Equal(3, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM {Table}").SingleAsync()));

        insert["rollback"] = "false";
        Assert.True((await JsonAsync(await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/test", insert))).GetProperty("ok").GetBoolean());
        await factory.WithDbAsync(async db =>
            Assert.Equal(4, await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS \"Value\" FROM {Table}").SingleAsync()));

        var broken = Endpoint("Broken", "GET", "broken", "SELECT * FROM no_such_table");
        broken["operation"] = JsonSerializer.Serialize(Op("GET", "SELECT * FROM no_such_table"));
        var error = await JsonAsync(await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/test", broken));
        Assert.False(error.GetProperty("ok").GetBoolean());
        Assert.Contains("no_such_table", error.GetProperty("errors")[0].GetString());

        // Regenerating a token replaces it: the old one stops working at once.
        var created = await CreateAsync(admin, Endpoint("Rotating", "GET", "rotating", $"SELECT COUNT(*) FROM {Table}", result: "Value"));
        var edit = created.Headers.Location!.OriginalString;
        var oldToken = await TokenAsync(admin, edit);
        Assert.Equal(HttpStatusCode.Redirect, (await DotNetForgeWebFactory.PostFormAsync(admin, edit, edit + "/token", new Dictionary<string, string>())).StatusCode);
        var newToken = await TokenAsync(admin, edit);
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Caller(factory, oldToken).GetAsync("/api/custom/rotating")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Caller(factory, newToken).GetAsync("/api/custom/rotating")).StatusCode);

        // Super Admins and Developers only: the screen refuses others and their sidebar has no link.
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");
        Assert.Contains("/account/denied", (await editor.GetAsync("/admin/api-endpoints")).Headers.Location?.OriginalString);
        Assert.DoesNotContain("href=\"/admin/api-endpoints\"", await editor.GetStringAsync("/admin"));
        await factory.CreateUserAsync("dev@example.com", "Dev3loperPass1", Roles.Developer);
        var developer = await factory.SignInAsync("dev@example.com", "Dev3loperPass1");
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync(edit)).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.EndpointTested));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.EndpointTokenRegenerated));
        });
    }

    [MongoDbFact]
    public async Task On_mongodb_endpoints_can_be_kept_but_answer_501()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        Assert.Contains("Needs a SQL database", await admin.GetStringAsync("/admin/api-endpoints"));
        Assert.Equal(HttpStatusCode.Redirect, (await CreateAsync(admin, Endpoint("Items", "GET", "items", "SELECT 1", access: "Public"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await factory.CreateClient().GetAsync("/api/custom/items")).StatusCode);
    }
}

/// <summary>A fact that needs SQL: skipped when the suite runs on MongoDB (<c>DNF_TEST_MONGODB</c>).</summary>
public sealed class SqlDatabaseFactAttribute : FactAttribute
{
    public SqlDatabaseFactAttribute()
    {
        if (TestDatabaseServer.IsMongoDb)
        {
            Skip = "Custom endpoints run SQL; MongoDB has none.";
        }
    }
}

/// <summary>A fact that runs only when the suite runs on MongoDB.</summary>
public sealed class MongoDbFactAttribute : FactAttribute
{
    public MongoDbFactAttribute()
    {
        if (!TestDatabaseServer.IsMongoDb)
        {
            Skip = "Runs only on MongoDB (set DNF_TEST_MONGODB).";
        }
    }
}
