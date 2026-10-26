using System.Text.Json.Nodes;
using DotNetForge.Api.Authorization;
using DotNetForge.Content.Endpoints;
using DotNetForge.Core.Endpoints;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DotNetForge.Web.ApiDocs;

/// <summary>
/// The OpenAPI document the API docs (Scalar) render (.docs/features/api-docs.md): the headless API controllers under
/// <c>/api/</c>, every enabled custom endpoint from the database (they are not controller actions), and the bearer
/// schemes to call them - API tokens for the built-in API, each custom endpoint's own token for it.
/// </summary>
public static class ApiDocsDocument
{
    public const string Name = "v1";

    /// <summary>The security scheme of API tokens (Settings › API Tokens).</summary>
    public const string ApiTokenScheme = "ApiToken";

    /// <summary>The security scheme of one custom endpoint's token; Scalar pre-fills it by this name.</summary>
    public static string EndpointScheme(CustomEndpoint endpoint) => "Endpoint " + endpoint.Id.ToString("N")[..8];

    /// <summary>Which actions the document lists: the headless API, but not the custom-endpoint catch-all route.</summary>
    public static bool Includes(string? relativePath) =>
        relativePath is not null && relativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase) &&
        !relativePath.StartsWith("api/custom/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Title, the API-token scheme and the custom endpoints (read per request, so a saved endpoint shows at once).</summary>
public sealed class ApiDocsDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly AppEnvironment _env;
    private readonly CustomEndpointService _endpoints;

    public ApiDocsDocumentTransformer(AppEnvironment env, CustomEndpointService endpoints)
    {
        _env = env;
        _endpoints = endpoints;
    }

    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = $"{_env.AppName} API",
            Version = ApiDocsDocument.Name,
            Description = "The headless API needs an API token (Settings › API Tokens) with the permission each operation names. " +
                "Custom endpoints (Settings › API Endpoints) use their own token, already filled in here.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiDocsDocument.ApiTokenScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "An API token. Its secret is shown once, when it is created on the API Tokens screen.",
        };

        var endpoints = (await _endpoints.ListAsync(cancellationToken)).Where(e => e.Enabled).ToList();
        if (endpoints.Count == 0)
        {
            return;
        }

        // Each custom endpoint is a group (tag) of its operations, named like the endpoint.
        var operations = await _endpoints.OperationsAsync(endpoints.Select(e => e.Id).ToList(), cancellationToken);
        document.Tags ??= new HashSet<OpenApiTag>();
        document.Paths ??= new OpenApiPaths();
        foreach (var endpoint in endpoints)
        {
            var tag = document.Tags.Any(t => t.Name == endpoint.Name) ? $"{endpoint.Name} (/{endpoint.Path})" : endpoint.Name;
            document.Tags.Add(new OpenApiTag { Name = tag, Description = endpoint.Description ?? $"Custom endpoint /api/custom/{endpoint.Path}." });
            if (endpoint.Access == EndpointAccess.Token)
            {
                document.Components.SecuritySchemes[ApiDocsDocument.EndpointScheme(endpoint)] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = $"The token of \"{endpoint.Name}\" (Call it on its API Endpoints screen).",
                };
            }

            foreach (var operation in operations[endpoint.Id])
            {
                var key = "/api/custom/" + CustomEndpointService.Template(endpoint, operation);
                if (!document.Paths.TryGetValue(key, out var existing) || existing is not OpenApiPathItem item)
                {
                    item = new OpenApiPathItem();
                    document.Paths[key] = item;
                }

                item.AddOperation(new HttpMethod(operation.Method), Operation(document, endpoint, operation, tag));
            }
        }
    }

    private static OpenApiOperation Operation(OpenApiDocument document, CustomEndpoint endpoint, CustomEndpointOperation custom, string tag)
    {
        var (parameters, _) = EndpointRules.ParseParameters(custom.ParametersJson, CustomEndpointService.Template(endpoint, custom));
        var operation = new OpenApiOperation
        {
            Summary = string.IsNullOrEmpty(custom.Name) ? $"{custom.Method} /{custom.Path}".TrimEnd('/', ' ') : custom.Name,
            Tags = new HashSet<OpenApiTagReference> { new(tag, document) },
            Parameters = parameters.Where(p => p.Source != "body").Select(p => (IOpenApiParameter)new OpenApiParameter
            {
                Name = p.Name,
                In = p.Source == "route" ? ParameterLocation.Path : ParameterLocation.Query,
                Required = p.Required,
                Schema = Schema(p),
            }).ToList(),
            Responses = Responses(endpoint, custom),
        };

        var body = parameters.Where(p => p.Source == "body").ToList();
        if (body.Count > 0)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = body.Any(p => p.Required),
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = body.ToDictionary(p => p.Name, p => (IOpenApiSchema)Schema(p)),
                            Required = body.Where(p => p.Required).Select(p => p.Name).ToHashSet(),
                        },
                    },
                },
            };
        }

        if (endpoint.Access == EndpointAccess.Token)
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecuritySchemeReference(ApiDocsDocument.EndpointScheme(endpoint), document)] = new List<string>() },
            };
        }

        return operation;
    }

    private static OpenApiSchema Schema(EndpointParameter p)
    {
        var schema = p.Type switch
        {
            "int" => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
            "number" => new OpenApiSchema { Type = JsonSchemaType.Number },
            "bool" => new OpenApiSchema { Type = JsonSchemaType.Boolean },
            "date" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
            "guid" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "json" => new OpenApiSchema { Description = "Any JSON value." },
            _ => new OpenApiSchema { Type = JsonSchemaType.String },
        };
        if (p.Default is not null)
        {
            schema.Default = JsonValue.Create(p.Default);
        }

        return schema;
    }

    private static OpenApiResponses Responses(CustomEndpoint endpoint, CustomEndpointOperation operation)
    {
        var responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = operation.Result switch
                {
                    EndpointResultShape.Rows => "{ data: [rows], count, truncated }",
                    EndpointResultShape.FirstRow => "{ data: {row} }",
                    EndpointResultShape.Value => "{ data: value }",
                    _ => "{ affected: number of rows changed }",
                },
            },
            ["400"] = new OpenApiResponse { Description = "A value is missing or of the wrong type: { error, errors: [{ parameter, message }] }" },
            ["500"] = new OpenApiResponse { Description = "The SQL failed: { error }" },
        };
        if (endpoint.Access == EndpointAccess.Token)
        {
            responses["401"] = new OpenApiResponse { Description = "Missing or wrong endpoint token." };
        }

        if (operation.Result == EndpointResultShape.FirstRow)
        {
            responses["404"] = new OpenApiResponse { Description = "No row." };
        }

        return responses;
    }
}

/// <summary>Marks the headless API's operations as needing an API token and names the permission key each needs.</summary>
public sealed class ApiDocsOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var permission = context.Description.ActionDescriptor.EndpointMetadata.OfType<RequireApiPermissionAttribute>().LastOrDefault();
        if (permission is null)
        {
            return Task.CompletedTask;
        }

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new() { [new OpenApiSecuritySchemeReference(ApiDocsDocument.ApiTokenScheme, context.Document)] = new List<string>() },
        };
        var needs = $"Needs an API token with `{permission.Permission}`.";
        operation.Description = string.IsNullOrEmpty(operation.Description) ? needs : $"{operation.Description}\n\n{needs}";
        return Task.CompletedTask;
    }
}
