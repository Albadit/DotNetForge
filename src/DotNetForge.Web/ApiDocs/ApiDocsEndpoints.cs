using DotNetForge.Content.Endpoints;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using Scalar.AspNetCore;

namespace DotNetForge.Web.ApiDocs;

/// <summary>
/// The routes of the API docs (.docs/features/api-docs.md), both for Super Admins and Developers
/// (<see cref="AuthorizationPolicies.ApiDocs"/>): the OpenAPI document and the Scalar reference that the API Docs
/// screen (<c>/admin/api-docs</c>) shows in a frame.
/// </summary>
public static class ApiDocsEndpoints
{
    public const string OpenApiPattern = "/admin/api-docs/openapi/{documentName}.json";

    /// <summary>Scalar's page (<c>/admin/api-docs/reference/</c>) and its scripts.</summary>
    public const string ReferencePrefix = "/admin/api-docs/reference";

    public static void MapApiDocs(this WebApplication app, AppEnvironment env)
    {
        app.MapOpenApi(OpenApiPattern).RequireAuthorization(AuthorizationPolicies.ApiDocs);

        app.MapScalarApiReference(ReferencePrefix, async (options, http) =>
        {
            options.WithTitle($"{env.AppName} API")
                .WithOpenApiRoutePattern(OpenApiPattern)
                .DisableDefaultFonts() // no font CDN: the CSP allows fonts from this origin only
                .DisableTelemetry()
                .DisableAgent() // "Ask AI" and the MCP/SDK generator send the document to Scalar's servers
                .DisableMcp()
                .HideDeveloperTools()
                .WithNonce(); // its one inline script carries a fresh nonce (SecurityHeadersMiddleware)

            // Each token-protected custom endpoint's own token, filled in so it can be tried at once.
            var endpoints = http.RequestServices.GetRequiredService<CustomEndpointService>();
            foreach (var endpoint in await endpoints.ListAsync(http.RequestAborted))
            {
                if (endpoint.Enabled && endpoint.Access == EndpointAccess.Token && endpoints.TokenOf(endpoint) is { } token)
                {
                    options.AddHttpAuthentication(ApiDocsDocument.EndpointScheme(endpoint), scheme => scheme.Token = token);
                }
            }
        }).RequireAuthorization(AuthorizationPolicies.ApiDocs);
    }
}
