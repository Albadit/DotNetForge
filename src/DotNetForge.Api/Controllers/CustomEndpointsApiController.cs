using System.Text.Json;
using DotNetForge.Content.Endpoints;
using DotNetForge.Core.Endpoints;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace DotNetForge.Api.Controllers;

/// <summary>
/// The endpoints Super Admins and Developers define on the API Endpoints screen (.docs/features/custom-endpoints.md):
/// a request to <c>/api/custom/{path}</c> runs the SQL of the operation that answers its method and path, with the
/// request's values as parameters.
/// Not an <see cref="ApiControllerBase"/> and without <c>[RequireApiPermission]</c>: API tokens don't open custom
/// endpoints. A <see cref="EndpointAccess.Token"/> endpoint is called with its own token
/// (<c>Authorization: Bearer dnfe_...</c>, shown in the editor), a <see cref="EndpointAccess.Public"/> one with none.
/// The <c>api</c> rate limit applies. Calls that may change data (every method but GET) are audited.
/// </summary>
[ApiController]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Api)]
[Tags("Custom endpoints")]
public sealed partial class CustomEndpointsApiController : ControllerBase
{
    private readonly CustomEndpointService _endpoints;
    private readonly SqlEndpointRunner _runner;
    private readonly IAuditService _audit;
    private readonly ILogger<CustomEndpointsApiController> _logger;

    public CustomEndpointsApiController(
        CustomEndpointService endpoints, SqlEndpointRunner runner, IAuditService audit, ILogger<CustomEndpointsApiController> logger)
    {
        _endpoints = endpoints;
        _runner = runner;
        _audit = audit;
        _logger = logger;
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", Route = "/api/custom/{**path}")]
    public async Task<IActionResult> Call(string? path)
    {
        var ct = HttpContext.RequestAborted;
        var match = await _endpoints.MatchAsync(Request.Method, path ?? string.Empty, ct);
        if (match is not { } found)
        {
            return NotFound(new { error = $"No endpoint answers {Request.Method} /api/custom/{path}." });
        }

        var (endpoint, operation) = (found.Endpoint, found.Operation);
        var withToken = endpoint.Access == EndpointAccess.Token;
        if (withToken && !_endpoints.VerifyToken(endpoint, BearerToken()))
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized(new { error = "Send this endpoint's token: Authorization: Bearer <token> (shown on the API Endpoints screen)." });
        }

        JsonElement? body = null;
        if (Request.ContentLength is > 0 || Request.Headers.TransferEncoding.Count > 0)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
                body = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return BadRequest(new { error = "The request body is not valid JSON." });
            }
        }

        var (parameters, _) = EndpointRules.ParseParameters(operation.ParametersJson, found.Template);
        var (values, errors) = EndpointRules.Bind(parameters, found.Route, name => Request.Query.TryGetValue(name, out var v) ? v.ToString() : null, body);
        if (errors.Count > 0)
        {
            return BadRequest(new { error = "Some values are missing or not valid.", errors = errors.Select(e => new { parameter = e.Parameter, message = e.Message }) });
        }

        var writes = !HttpMethods.IsGet(Request.Method);
        try
        {
            var result = await _runner.RunAsync(operation, endpoint.Database, values, rollback: false, ct);
            if (writes)
            {
                await _audit.LogAsync(AuditActions.EndpointExecuted, CustomEndpointService.AuditEntity, endpoint.Id.ToString(),
                    CustomEndpointService.Route(endpoint, operation), details: Caller(endpoint), cancellationToken: ct);
            }

            var (status, response) = CustomEndpointService.Response(operation, result);
            return StatusCode(status, response);
        }
        catch (EndpointSqlException ex)
        {
            LogFailed(endpoint.Id, operation.Method, found.Template, ex.InnerException?.GetType().Name ?? nameof(EndpointSqlException));
            if (writes)
            {
                await _audit.LogAsync(AuditActions.EndpointExecuted, CustomEndpointService.AuditEntity, endpoint.Id.ToString(),
                    CustomEndpointService.Route(endpoint, operation), details: Caller(endpoint), success: false, cancellationToken: ct);
            }

            // The database's message can name tables and columns: only token callers (trusted apps) see it.
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = withToken ? ex.Message : "The endpoint failed. Its owner can see why with a test run on the API Endpoints screen." });
        }
    }

    private string? BearerToken() =>
        Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;

    private static string Caller(CustomEndpoint endpoint) =>
        endpoint.Name + (endpoint.Access == EndpointAccess.Token ? " (called with its token)" : " (public call)");

    // The error type only: driver messages can echo data (.docs/database/security.md).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom endpoint {EndpointId} ({Method} /api/custom/{Path}) failed with {ErrorType}")]
    private partial void LogFailed(Guid endpointId, string method, string path, string errorType);
}
