using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// An API a Super Admin or Developer defined on the API Endpoints screen (.docs/features/custom-endpoints.md): a name, a
/// base path under <c>/api/custom/</c>, who may call it and its own token. What it answers is its
/// <see cref="CustomEndpointOperation"/>s - one per method and sub-path, each running its own SQL with the request's
/// values bound as SQL parameters (<c>@name</c>), never pasted into the SQL text.
/// </summary>
public class CustomEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name ("Products").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The base path after <c>/api/custom/</c>: lower-case segments, <c>{param}</c> for a route value.</summary>
    public string Path { get; set; } = string.Empty;

    public string? Description { get; set; }

    public EndpointAccess Access { get; set; } = EndpointAccess.Token;

    /// <summary>
    /// The endpoint's own secret for <see cref="EndpointAccess.Token"/> calls (<c>Authorization: Bearer dnfe_...</c>),
    /// encrypted with ASP.NET Data Protection so the editor and the API docs can show it again. Null until generated.
    /// One token opens every operation of the endpoint.
    /// </summary>
    public string? TokenProtected { get; set; }

    /// <summary>Disabled endpoints answer 404 for every operation.</summary>
    public bool Enabled { get; set; } = true;

    public Guid? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One request a <see cref="CustomEndpoint"/> answers: an HTTP method on the endpoint's path plus an optional sub-path
/// (<c>{id}</c>), and the SQL that runs (any statement: the screen is Super Admin and Developer only).
/// </summary>
public class CustomEndpointOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EndpointId { get; set; }

    /// <summary>Optional label ("Get one product"); lists fall back to the method and path.</summary>
    public string? Name { get; set; }

    /// <summary><c>GET</c>, <c>POST</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Appended to the endpoint's path (empty = the endpoint's path itself), e.g. <c>{id}</c>.</summary>
    public string Path { get; set; } = string.Empty;

    public string Sql { get; set; } = string.Empty;

    /// <summary>The parameters as JSON: <c>[{ name, type, source, required, default }]</c>.</summary>
    public string ParametersJson { get; set; } = "[]";

    public EndpointResultShape Result { get; set; } = EndpointResultShape.Rows;

    /// <summary>Seconds the SQL may run (1-120).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Most rows a <see cref="EndpointResultShape.Rows"/> result returns (1-10000).</summary>
    public int MaxRows { get; set; } = 1000;

    /// <summary>Order on the screen and in the API docs.</summary>
    public int SortOrder { get; set; }
}
