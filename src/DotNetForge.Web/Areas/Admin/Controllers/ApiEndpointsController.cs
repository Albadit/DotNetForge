using System.Text.Json;
using DotNetForge.Content.Endpoints;
using DotNetForge.Core.Endpoints;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// API Endpoints (.docs/pages/api-endpoints.md): endpoints under <c>/api/custom/</c> whose operations - a method and an
/// optional sub-path each - run SQL. Super Admins and Developers only - the SQL runs with the application's database rights. Rules in
/// <see cref="CustomEndpointService"/>; the test run uses <see cref="SqlEndpointRunner"/> like the API does.
/// </summary>
[Route("admin/api-endpoints")]
[Authorize(Roles = Roles.SuperAdmin + "," + Roles.Developer)]
public sealed class ApiEndpointsController : AdminControllerBase
{
    private readonly CustomEndpointService _endpoints;
    private readonly SqlEndpointRunner _runner;
    private readonly IAuditService _audit;

    public ApiEndpointsController(CustomEndpointService endpoints, SqlEndpointRunner runner, IAuditService audit)
    {
        _endpoints = endpoints;
        _runner = runner;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var endpoints = await _endpoints.ListAsync(HttpContext.RequestAborted);
        return View(new ApiEndpointsIndexViewModel
        {
            Endpoints = endpoints,
            Operations = await _endpoints.OperationsAsync(endpoints.Select(e => e.Id).ToList(), HttpContext.RequestAborted),
        });
    }

    /// <summary>A new endpoint starts with one GET operation on its own path.</summary>
    [HttpGet("new")]
    public IActionResult New() => View("Edit", EditModel(null, new CustomEndpointInput
    {
        OperationsJson = CustomEndpointService.SerializeOperations(new[] { new CustomEndpointOperation() }),
    }));

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Create(CustomEndpointInput input) => SaveAsync(null, input);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var endpoint = await _endpoints.FindAsync(id, HttpContext.RequestAborted);
        if (endpoint is null)
        {
            return NotFound();
        }

        var operations = await _endpoints.OperationsAsync(id, HttpContext.RequestAborted);
        return View(EditModel(id, CustomEndpointInput.From(endpoint, operations), _endpoints.TokenOf(endpoint), endpoint, operations));
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Update(Guid id, CustomEndpointInput input) => SaveAsync(id, input);

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var endpoint = await _endpoints.DeleteAsync(id, HttpContext.RequestAborted);
        if (endpoint is not null)
        {
            TempData["Success"] = $"Deleted '{endpoint.Name}'.";
        }

        return Redirect("/admin/api-endpoints");
    }

    /// <summary>Gives the endpoint a new token; apps using the old one get 401 from now on.</summary>
    [HttpPost("{id:guid}/token")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateToken(Guid id)
    {
        var endpoint = await _endpoints.RegenerateTokenAsync(id, HttpContext.RequestAborted);
        if (endpoint is null)
        {
            return NotFound();
        }

        TempData["Success"] = $"New token for '{endpoint.Name}'. The old one no longer works.";
        return Redirect($"/admin/api-endpoints/{endpoint.Id}");
    }

    /// <summary>
    /// Runs one operation of the editor's current (unsaved) endpoint - <paramref name="operation"/>, its JSON - with the
    /// test values and answers what the API would. Changes are rolled back unless the admin turns that off. The
    /// database's error message is shown: the caller wrote the SQL.
    /// </summary>
    [HttpPost("test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(CustomEndpointInput input, string? operation, string? testValues, bool rollback = true)
    {
        var (draft, tested, errors) = _endpoints.CheckOne(input, operation);
        if (errors.Count > 0 || tested is null)
        {
            return Json(new { ok = false, errors });
        }

        var parameters = tested.Parameters;
        var given = ReadTestValues(testValues);
        var route = parameters.Where(p => p.Source == "route" && given.ContainsKey(p.Name))
            .ToDictionary(p => p.Name, p => given[p.Name], StringComparer.OrdinalIgnoreCase);
        var body = TestBody(parameters, given);
        var (values, valueErrors) = EndpointRules.Bind(parameters, route,
            name => parameters.Any(p => p.Source == "query" && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) && given.TryGetValue(name, out var v) ? v : null,
            body);
        if (valueErrors.Count > 0)
        {
            return Json(new { ok = false, status = 400, errors = valueErrors.Select(e => e.Message) });
        }

        await _audit.LogAsync(AuditActions.EndpointTested, CustomEndpointService.AuditEntity, null,
            CustomEndpointService.Route(draft, tested.Operation), details: rollback ? "Changes rolled back" : "Changes kept", cancellationToken: HttpContext.RequestAborted);
        try
        {
            var result = await _runner.RunAsync(tested.Operation, draft.Database, values, rollback, HttpContext.RequestAborted);
            var (status, response) = CustomEndpointService.Response(tested.Operation, result);
            return Json(new { ok = true, status, body = response, ms = Math.Round(result.Duration.TotalMilliseconds, 1), rolledBack = rollback });
        }
        catch (EndpointSqlException ex)
        {
            return Json(new { ok = false, status = 500, errors = new[] { ex.Message } });
        }
    }

    private async Task<IActionResult> SaveAsync(Guid? id, CustomEndpointInput input)
    {
        var (endpoint, errors) = await _endpoints.SaveAsync(id, input, CurrentUserId, HttpContext.RequestAborted);
        if (endpoint is null)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            var existing = id is { } current ? await _endpoints.FindAsync(current, HttpContext.RequestAborted) : null;
            var saved = existing is null ? new List<CustomEndpointOperation>() : await _endpoints.OperationsAsync(existing.Id, HttpContext.RequestAborted);
            return View("Edit", EditModel(id, input, existing is null ? null : _endpoints.TokenOf(existing), existing, saved));
        }

        TempData["Success"] = id is null ? $"Created '{endpoint.Name}'." : $"Saved '{endpoint.Name}'.";
        return Redirect($"/admin/api-endpoints/{endpoint.Id}");
    }

    /// <param name="saved">The endpoint as saved (the "Call it" card), null for a new one.</param>
    private ApiEndpointEditViewModel EditModel(Guid? id, CustomEndpointInput input, string? token = null,
        CustomEndpoint? saved = null, IReadOnlyList<CustomEndpointOperation>? savedOperations = null) => new()
        {
            Id = id,
            Input = input,
            Databases = _runner.Databases,
            Token = token,
            Calls = saved is null || savedOperations is null
            ? Array.Empty<(CustomEndpointOperation, string)>()
            : savedOperations.Select(o => (o, CustomEndpointService.Template(saved, o))).ToList(),
        };

    /// <summary>The test panel's values: <c>{ "name": "text" }</c>.</summary>
    private static Dictionary<string, string> ReadTestValues(string? json)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } text)
                    {
                        values[property.Name] = text;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Treated as no values: required ones are then reported as missing.
        }

        return values;
    }

    /// <summary>The body-sourced test values as the JSON body a client would send (JSON-typed values as JSON).</summary>
    private static JsonElement TestBody(IReadOnlyList<EndpointParameter> parameters, Dictionary<string, string> given)
    {
        var body = new Dictionary<string, JsonElement>();
        foreach (var p in parameters.Where(p => p.Source == "body" && given.ContainsKey(p.Name)))
        {
            var text = given[p.Name];
            JsonElement value;
            try
            {
                value = p.Type == "json" ? JsonDocument.Parse(text).RootElement.Clone() : JsonSerializer.SerializeToElement(text);
            }
            catch (JsonException)
            {
                value = JsonSerializer.SerializeToElement(text);
            }

            body[p.Name] = value;
        }

        return JsonSerializer.SerializeToElement(body);
    }
}
