using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetForge.Core.Endpoints;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Content.Endpoints;

/// <summary>What the API Endpoints editor posts for one endpoint.</summary>
public sealed class CustomEndpointInput
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The base path after <c>/api/custom/</c>.</summary>
    public string Path { get; set; } = string.Empty;

    public string? Description { get; set; }

    public EndpointAccess Access { get; set; } = EndpointAccess.Token;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The operations as JSON (the editor serializes its cards into this):
    /// <c>[{ id, name, method, path, sql, parameters: [...], result, timeoutSeconds, maxRows }]</c>.
    /// </summary>
    public string OperationsJson { get; set; } = "[]";

    public static CustomEndpointInput From(CustomEndpoint e, IEnumerable<CustomEndpointOperation> operations) => new()
    {
        Name = e.Name,
        Path = e.Path,
        Description = e.Description,
        Access = e.Access,
        Enabled = e.Enabled,
        OperationsJson = CustomEndpointService.SerializeOperations(operations),
    };
}

/// <summary>A validated operation: the entity (not yet saved), the full path template it answers and its parameters.</summary>
public sealed record CheckedOperation(CustomEndpointOperation Operation, string Template, IReadOnlyList<EndpointParameter> Parameters);

/// <summary>What a request is for: the endpoint, the operation, its full template and the route values.</summary>
public sealed record EndpointMatch(CustomEndpoint Endpoint, CustomEndpointOperation Operation, string Template, IReadOnlyDictionary<string, string> Route);

/// <summary>
/// The rules of custom API endpoints (.docs/features/custom-endpoints.md): an endpoint (name, base path, access, token)
/// with operations (method + sub-path + SQL), validation, one operation per method and path shape across all
/// endpoints, auditing, and finding the operation a request is for. Shared by the API Endpoints screen, the
/// <c>/api/custom/</c> route and the API docs.
/// </summary>
public sealed class CustomEndpointService
{
    public const string AuditEntity = "CustomEndpoint";

    /// <summary>Every endpoint token starts with this, so a leaked one is recognisable.</summary>
    public const string TokenPrefix = "dnfe_";

    public const int MaxOperations = 20;

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDataProtector _tokens;

    public CustomEndpointService(DotNetForgeDbContext db, IAuditService audit, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _audit = audit;
        _tokens = dataProtection.CreateProtector("DotNetForge.CustomEndpoints.Token.v1");
    }

    /// <summary>The endpoint's token in plain text, or null when it has none (or the key that encrypted it is gone).</summary>
    public string? TokenOf(CustomEndpoint endpoint)
    {
        if (endpoint.TokenProtected is null)
        {
            return null;
        }

        try
        {
            return _tokens.Unprotect(endpoint.TokenProtected);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="presented"/> is the endpoint's token (constant-time comparison).</summary>
    public bool VerifyToken(CustomEndpoint endpoint, string? presented) =>
        !string.IsNullOrEmpty(presented) && TokenOf(endpoint) is { } token &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(presented));

    /// <summary>Gives the endpoint a new token; the old one stops working at once. Saves and audits.</summary>
    public async Task<CustomEndpoint?> RegenerateTokenAsync(Guid id, CancellationToken ct = default)
    {
        var endpoint = await FindAsync(id, ct);
        if (endpoint is null)
        {
            return null;
        }

        endpoint.TokenProtected = _tokens.Protect(NewToken());
        endpoint.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EndpointTokenRegenerated, AuditEntity, endpoint.Id.ToString(), Display(endpoint),
            details: endpoint.Name, cancellationToken: ct);
        return endpoint;
    }

    private static string NewToken() => TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>"/api/custom/products" - an endpoint as the audit log names it.</summary>
    public static string Display(CustomEndpoint e) => "/api/custom/" + e.Path;

    /// <summary>The full path template an operation answers, e.g. <c>products/{id}</c>.</summary>
    public static string Template(CustomEndpoint endpoint, CustomEndpointOperation operation) =>
        EndpointRules.Combine(endpoint.Path, operation.Path) ?? endpoint.Path;

    /// <summary>"GET /api/custom/products/{id}" - an operation as lists and the audit log name it.</summary>
    public static string Route(CustomEndpoint endpoint, CustomEndpointOperation operation) =>
        $"{operation.Method} /api/custom/{Template(endpoint, operation)}";

    public Task<List<CustomEndpoint>> ListAsync(CancellationToken ct = default) =>
        _db.CustomEndpoints.AsNoTracking().OrderBy(e => e.Path).ThenBy(e => e.Name).ToListAsync(ct);

    public Task<CustomEndpoint?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.CustomEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>The operations of the given endpoints, in their screen order (one single-table query).</summary>
    public async Task<ILookup<Guid, CustomEndpointOperation>> OperationsAsync(IReadOnlyCollection<Guid> endpointIds, CancellationToken ct = default)
    {
        var ids = endpointIds.ToList();
        var operations = await _db.CustomEndpointOperations.AsNoTracking()
            .Where(o => ids.Contains(o.EndpointId))
            .ToListAsync(ct);
        return operations.OrderBy(o => o.SortOrder).ToLookup(o => o.EndpointId);
    }

    public async Task<List<CustomEndpointOperation>> OperationsAsync(Guid endpointId, CancellationToken ct = default) =>
        (await OperationsAsync(new[] { endpointId }, ct))[endpointId].ToList();

    /// <summary>
    /// The enabled operation for <paramref name="method"/> and <paramref name="path"/> with its route values, or null.
    /// A literal segment wins over a <c>{param}</c> (<c>products/featured</c> before <c>products/{id}</c>).
    /// </summary>
    public async Task<EndpointMatch?> MatchAsync(string method, string path, CancellationToken ct = default)
    {
        method = method.ToUpperInvariant();
        var endpoints = await _db.CustomEndpoints.AsNoTracking().Where(e => e.Enabled).ToListAsync(ct);
        var ids = endpoints.Select(e => e.Id).ToList();
        var operations = await _db.CustomEndpointOperations.AsNoTracking()
            .Where(o => o.Method == method && ids.Contains(o.EndpointId))
            .ToListAsync(ct);
        var byId = endpoints.ToDictionary(e => e.Id);
        return operations
            .Select(o => (Endpoint: byId[o.EndpointId], Operation: o, Template: Template(byId[o.EndpointId], o)))
            .Select(m => (m.Endpoint, m.Operation, m.Template, Route: EndpointRules.Match(m.Template, path)))
            .Where(m => m.Route is not null)
            .OrderBy(m => m.Template.Count(c => c == '{'))
            .Select(m => new EndpointMatch(m.Endpoint, m.Operation, m.Template, m.Route!))
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks an input: the endpoint's settings and every operation. Returns a draft endpoint and the checked operations
    /// (not saved) with the errors, each operation's prefixed with its method and path.
    /// </summary>
    public (CustomEndpoint Draft, List<CheckedOperation> Operations, List<string> Errors) Check(CustomEndpointInput input)
    {
        var (draft, errors) = CheckEndpoint(input);
        var operations = new List<CheckedOperation>();
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(string.IsNullOrWhiteSpace(input.OperationsJson) ? "[]" : input.OperationsJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            errors.Add("The operations are not valid JSON.");
            return (draft, operations, errors);
        }

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            errors.Add("Add at least one operation: the method, the SQL it runs and its parameters.");
            return (draft, operations, errors);
        }

        if (root.GetArrayLength() > MaxOperations)
        {
            errors.Add($"An endpoint can have at most {MaxOperations} operations.");
        }

        var position = 0;
        foreach (var item in root.EnumerateArray())
        {
            position++;
            var (operation, operationErrors) = CheckOperation(draft.Path, item, position);
            errors.AddRange(operationErrors);
            if (operation is not null)
            {
                operation.Operation.SortOrder = position - 1;
                if (operations.FirstOrDefault(o => o.Operation.Method == operation.Operation.Method && Shape(o.Template) == Shape(operation.Template)) is { } twin)
                {
                    errors.Add($"{Label(operation)}: the endpoint already has {Label(twin)} - they would answer the same URLs.");
                }

                operations.Add(operation);
            }
        }

        return (draft, operations, errors);
    }

    /// <summary>Checks one operation of the editor (its test run) together with the endpoint's settings.</summary>
    public (CustomEndpoint Draft, CheckedOperation? Operation, List<string> Errors) CheckOne(CustomEndpointInput input, string? operationJson)
    {
        var (draft, errors) = CheckEndpoint(input);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(operationJson) ? "{}" : operationJson);
            var (operation, operationErrors) = CheckOperation(draft.Path, document.RootElement.Clone(), 1);
            errors.AddRange(operationErrors);
            return (draft, operation, errors);
        }
        catch (JsonException)
        {
            errors.Add("The operation is not valid JSON.");
            return (draft, null, errors);
        }
    }

    private static (CustomEndpoint Draft, List<string> Errors) CheckEndpoint(CustomEndpointInput input)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 100)
        {
            errors.Add("Give the endpoint a name (up to 100 characters).");
        }

        var path = EndpointRules.NormalizePath(input.Path);
        if (path is null || path.Length > 200)
        {
            errors.Add("The path needs 1-10 segments of lower-case letters, digits, '-', '_' or '.', or {name} for a route value - e.g. products.");
        }

        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description?.Length > 500)
        {
            errors.Add("The description is limited to 500 characters.");
        }

        if (!Enum.IsDefined(input.Access))
        {
            errors.Add("Choose who may call the endpoint.");
        }

        var draft = new CustomEndpoint
        {
            Name = name,
            Path = path ?? string.Empty,
            Description = description,
            Access = input.Access,
            Enabled = input.Enabled,
        };
        return (draft, errors);
    }

    private static (CheckedOperation? Operation, List<string> Errors) CheckOperation(string endpointPath, JsonElement item, int position)
    {
        var errors = new List<string>();
        if (item.ValueKind != JsonValueKind.Object)
        {
            return (null, new List<string> { $"Operation {position} is not an object." });
        }

        var method = (Str(item, "method") ?? string.Empty).Trim().ToUpperInvariant();
        var subPath = EndpointRules.NormalizeSubPath(Str(item, "path"));
        var label = $"{(method.Length > 0 ? method : $"Operation {position}")} /{subPath ?? Str(item, "path")}".TrimEnd('/', ' ');
        if (!EndpointRules.Methods.Contains(method))
        {
            errors.Add($"{label}: choose an HTTP method.");
        }

        var template = subPath is null || endpointPath.Length == 0 ? null : EndpointRules.Combine(endpointPath, subPath);
        if (subPath is null)
        {
            errors.Add($"{label}: the sub-path needs segments of lower-case letters, digits, '-', '_' or '.', or {{name}} - e.g. {{id}} - or nothing.");
        }
        else if (endpointPath.Length > 0 && (template is null || template.Length > 200))
        {
            errors.Add($"{label}: together with the endpoint's path it has more than 10 segments or 200 characters, or repeats a {{name}}.");
        }

        var name = Str(item, "name")?.Trim();
        if (name?.Length > 100)
        {
            errors.Add($"{label}: the name is limited to 100 characters.");
        }

        var sql = Str(item, "sql")?.Trim() ?? string.Empty;
        if (sql.Length == 0)
        {
            errors.Add($"{label}: write the SQL it runs.");
        }
        else if (sql.Length > EndpointRules.MaxSql)
        {
            errors.Add($"{label}: the SQL is limited to {EndpointRules.MaxSql:N0} characters.");
        }

        var parametersJson = item.TryGetProperty("parameters", out var p) ? p.GetRawText() : "[]";
        var (parameters, parameterErrors) = EndpointRules.ParseParameters(parametersJson, template);
        errors.AddRange(parameterErrors.Select(e => $"{label}: {e}"));

        var result = EndpointResultShape.Rows;
        if (item.TryGetProperty("result", out var r))
        {
            EndpointResultShape? parsed = r.ValueKind switch
            {
                JsonValueKind.String when Enum.TryParse<EndpointResultShape>(r.GetString(), ignoreCase: true, out var shape) => shape,
                JsonValueKind.Number when r.TryGetInt32(out var number) => (EndpointResultShape)number,
                _ => null,
            };
            if (parsed is { } valid && Enum.IsDefined(valid))
            {
                result = valid;
            }
            else
            {
                errors.Add($"{label}: choose what it answers with.");
            }
        }

        var timeout = Int(item, "timeoutSeconds") ?? 30;
        if (timeout is < 1 or > 120)
        {
            errors.Add($"{label}: the timeout must be 1-120 seconds.");
        }

        var maxRows = Int(item, "maxRows") ?? 1000;
        if (maxRows is < 1 or > 10_000)
        {
            errors.Add($"{label}: the row limit must be 1-10,000.");
        }

        var operation = new CustomEndpointOperation
        {
            Id = Guid.TryParse(Str(item, "id"), out var id) ? id : Guid.NewGuid(),
            Name = string.IsNullOrEmpty(name) ? null : name,
            Method = method,
            Path = subPath ?? string.Empty,
            Sql = sql,
            ParametersJson = Serialize(parameters),
            Result = result,
            TimeoutSeconds = Math.Clamp(timeout, 1, 120),
            MaxRows = Math.Clamp(maxRows, 1, 10_000),
        };
        return (new CheckedOperation(operation, template ?? endpointPath, parameters), errors);
    }

    /// <summary>Creates (<paramref name="id"/> null) or updates an endpoint and its operations. Saves and audits when there are no errors.</summary>
    public async Task<(CustomEndpoint? Endpoint, List<string> Errors)> SaveAsync(
        Guid? id, CustomEndpointInput input, Guid? userId, CancellationToken ct = default)
    {
        var (draft, operations, errors) = Check(input);
        CustomEndpoint? endpoint = null;
        if (id is { } existingId)
        {
            endpoint = await FindAsync(existingId, ct);
            if (endpoint is null)
            {
                return (null, new List<string> { "The endpoint no longer exists." });
            }
        }

        if (errors.Count == 0)
        {
            errors.AddRange(await ConflictsAsync(id ?? Guid.Empty, draft, operations, ct));
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var created = endpoint is null;
        if (endpoint is null)
        {
            endpoint = new CustomEndpoint { CreatedById = userId };
            _db.CustomEndpoints.Add(endpoint);
        }

        endpoint.Name = draft.Name;
        endpoint.Path = draft.Path;
        endpoint.Description = draft.Description;
        endpoint.Access = draft.Access;
        endpoint.Enabled = draft.Enabled;
        endpoint.UpdatedDate = DateTime.UtcNow;
        // Every endpoint has a token from its first save, so switching it to "Token" access never leaves it open.
        endpoint.TokenProtected ??= _tokens.Protect(NewToken());

        // Operations are updated in place (by id), added or removed - their ids stay stable across saves.
        var endpointId = endpoint.Id;
        var existing = created
            ? new List<CustomEndpointOperation>()
            : await _db.CustomEndpointOperations.Where(o => o.EndpointId == endpointId).ToListAsync(ct);
        foreach (var (operation, _, _) in operations)
        {
            var target = existing.FirstOrDefault(o => o.Id == operation.Id);
            if (target is null)
            {
                target = new CustomEndpointOperation { EndpointId = endpointId };
                _db.CustomEndpointOperations.Add(target);
            }

            target.Name = operation.Name;
            target.Method = operation.Method;
            target.Path = operation.Path;
            target.Sql = operation.Sql;
            target.ParametersJson = operation.ParametersJson;
            target.Result = operation.Result;
            target.TimeoutSeconds = operation.TimeoutSeconds;
            target.MaxRows = operation.MaxRows;
            target.SortOrder = operation.SortOrder;
        }

        _db.CustomEndpointOperations.RemoveRange(existing.Where(o => operations.All(c => c.Operation.Id != o.Id)));
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(created ? AuditActions.EndpointCreated : AuditActions.EndpointUpdated, AuditEntity,
            endpoint.Id.ToString(), Display(endpoint), details: $"{endpoint.Name}: {string.Join(", ", operations.Select(Label))}",
            cancellationToken: ct);
        return (endpoint, errors);
    }

    /// <summary>Another endpoint with the same base path, or an operation another endpoint answers already.</summary>
    private async Task<List<string>> ConflictsAsync(Guid selfId, CustomEndpoint draft, List<CheckedOperation> operations, CancellationToken ct)
    {
        var errors = new List<string>();
        var others = await _db.CustomEndpoints.AsNoTracking().Where(e => e.Id != selfId).ToListAsync(ct);
        if (others.FirstOrDefault(e => e.Path == draft.Path) is { } samePath)
        {
            errors.Add($"\"{samePath.Name}\" already uses /api/custom/{samePath.Path} - add your operations to it instead.");
        }

        var otherOperations = await OperationsAsync(others.Select(e => e.Id).ToList(), ct);
        foreach (var other in others)
        {
            foreach (var otherOperation in otherOperations[other.Id])
            {
                var otherTemplate = Template(other, otherOperation);
                if (operations.FirstOrDefault(o => o.Operation.Method == otherOperation.Method && Shape(o.Template) == Shape(otherTemplate)) is { } clash)
                {
                    errors.Add($"{Label(clash)}: \"{other.Name}\" already answers {otherOperation.Method} /api/custom/{otherTemplate}.");
                }
            }
        }

        return errors;
    }

    public async Task<CustomEndpoint?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var endpoint = await FindAsync(id, ct);
        if (endpoint is null)
        {
            return null;
        }

        _db.CustomEndpointOperations.RemoveRange(await _db.CustomEndpointOperations.Where(o => o.EndpointId == id).ToListAsync(ct));
        _db.CustomEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EndpointDeleted, AuditEntity, endpoint.Id.ToString(), Display(endpoint),
            details: endpoint.Name, cancellationToken: ct);
        return endpoint;
    }

    /// <summary>
    /// The HTTP status and JSON body of a successful run: <c>{ data, count, truncated }</c> for rows, <c>{ data }</c> for
    /// one row (404 when there is none) or one value, <c>{ affected }</c> for a write. The test panel shows the same.
    /// </summary>
    public static (int Status, object Body) Response(CustomEndpointOperation operation, EndpointRunResult result) => operation.Result switch
    {
        EndpointResultShape.Rows => (200, new { data = result.Data, count = (result.Data as System.Collections.ICollection)?.Count ?? 0, truncated = result.Truncated }),
        EndpointResultShape.FirstRow when result.Data is null => (404, new { error = "Not found." }),
        EndpointResultShape.AffectedRows => (200, new { affected = result.Affected }),
        _ => (200, new { data = result.Data }),
    };

    /// <summary>The parameters as stored (normalized: required route parameters, known types and sources).</summary>
    public static string Serialize(IReadOnlyList<EndpointParameter> parameters) =>
        JsonSerializer.Serialize(parameters.Select(p => new
        {
            name = p.Name,
            type = p.Type,
            source = p.Source,
            required = p.Required,
            @default = p.Default,
        }));

    /// <summary>Operations as the editor reads and posts them (<see cref="CustomEndpointInput.OperationsJson"/>).</summary>
    public static string SerializeOperations(IEnumerable<CustomEndpointOperation> operations) =>
        JsonSerializer.Serialize(operations.OrderBy(o => o.SortOrder).Select(o => new
        {
            id = o.Id,
            name = o.Name ?? string.Empty,
            method = o.Method,
            path = o.Path,
            sql = o.Sql,
            parameters = JsonDocument.Parse(o.ParametersJson).RootElement,
            result = o.Result.ToString(),
            timeoutSeconds = o.TimeoutSeconds,
            maxRows = o.MaxRows,
        }));

    private static string Label(CheckedOperation operation) =>
        $"{operation.Operation.Method} /{operation.Operation.Path}".TrimEnd('/', ' ');

    private static string Shape(string path) =>
        string.Join('/', path.Split('/').Select(s => s.StartsWith('{') ? "{}" : s));

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
        : item.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n) ? n
        : null;
}
