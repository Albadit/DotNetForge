# Custom API endpoints

A Super Admin or Developer builds their own API on the [API Endpoints](../pages/api-endpoints.md) screen: an
**endpoint** is a name and a base path, and its **operations** - one per HTTP method and sub-path, each with its own
SQL - are what the [headless API](headless-api.md) answers under `/api/custom/{path}`. A "Products" endpoint at
`products` can answer `GET /api/custom/products`, `GET /api/custom/products/{id}`, `POST /api/custom/products` and
`DELETE /api/custom/products/{id}`. Reads (SELECT) and writes (INSERT, UPDATE, DELETE, anything the database accepts)
are both allowed: the screen is for Super Admins and Developers only, and the SQL runs with the application's database
rights.

Each endpoint has **its own token** (`dnfe_…`), one for all its operations; API tokens don't open custom endpoints.
Every enabled endpoint is also listed in the [API docs](api-docs.md) with its token filled in.

Request values are always bound as ADO.NET parameters (`@name`), never pasted into the SQL text, so a value can't
change the statement. The SQL itself is trusted: whoever may edit an endpoint can read and change the whole database.

## Where it lives

```text
src/DotNetForge.Shared/Entities/CustomEndpoint.cs                  CustomEndpoint (table CustomEndpoints), CustomEndpointOperation (table CustomEndpointOperations)
src/DotNetForge.Shared/Enums/Enums.cs                              EndpointResultShape, EndpointAccess
src/DotNetForge.Core/Endpoints/EndpointRules.cs                    pure rules: methods, paths and sub-paths (NormalizePath, NormalizeSubPath, Combine), matching, parameter definitions, value conversion, binding
src/DotNetForge.Content/Endpoints/CustomEndpointService.cs         validation, one operation per method + path shape, save/delete + audit, matching a request, response shapes, endpoint tokens
src/DotNetForge.Content/Endpoints/SqlEndpointRunner.cs             runs an operation's SQL: transaction, typed parameters, timeout, row limit, JSON-safe values
src/DotNetForge.Api/Controllers/CustomEndpointsApiController.cs    /api/custom/{**path}
src/DotNetForge.Web/Areas/Admin/Controllers/ApiEndpointsController.cs   the screen, its test run and "Regenerate token"
src/DotNetForge.Web/ApiDocs/ApiDocsTransformers.cs               the endpoints in the API docs' OpenAPI document
```

## An endpoint

| Setting | Rules |
| --- | --- |
| Name | required, ≤ 100 characters (lists, the audit log and the endpoint's group in the API docs) |
| Path | the base path after `/api/custom/`: 1-10 segments, each lower-case letters, digits, `-`, `_`, `.` (starting with a letter or digit) or `{name}` for a route value; stored trimmed of slashes with literal segments lower-cased; ≤ 200 characters. No other endpoint may have the same base path ("… already uses `/api/custom/<path>` - add your operations to it instead.") |
| Description | optional, ≤ 500 characters (the list and the API docs) |
| Who may call it (`Access`) | `Token` ("Token" on the form, the default): callers send the endpoint's own token; `Public` ("Anyone"): no token. Applies to every operation |
| Enabled | off → every operation answers 404 |
| Operations | 1-20 (`CustomEndpointService.MaxOperations`); none → "Add at least one operation: the method, the SQL it runs and its parameters." |

### Operations

| Setting | Rules |
| --- | --- |
| Method | `GET`, `POST`, `PUT`, `PATCH` or `DELETE` |
| Sub-path | appended to the endpoint's path; empty = the endpoint's path itself, else like a path (`{id}`, `{id}/reviews`). Together with the base path (`EndpointRules.Combine`) at most 10 segments and 200 characters, and no `{name}` twice |
| Name | optional, ≤ 100 characters ("Get one product"): the operation's summary in the API docs (else "METHOD /sub-path"), and shown on a closed operation card and in the **Call it** line's tooltip |
| SQL | required, ≤ 50,000 characters, `@name` where a value goes |
| Parameters | `[{ name, type, source, required, default }]` (≤ 50) - see below |
| Answers with (`Result`) | `Rows`, `FirstRow`, `Value` or `AffectedRows` - see [Responses](#responses) |
| Timeout | 1-120 seconds (default 30) |
| Row limit | 1-10,000 rows for `Rows` (default 1000) |

The editor posts the operations as one JSON field, `OperationsJson`:
`[{ id, name, method, path, sql, parameters: [...], result, timeoutSeconds, maxRows }]`. `CustomEndpointService.Check`
validates the endpoint and every operation; an operation's errors start with its method and sub-path
("GET /{id}: write the SQL it runs."). On save (`SaveAsync`) operations are updated in place by `id`, new ones added
and missing ones removed, so their ids stay stable.

One operation per method and path **shape**: `items/{id}` and `items/{slug}` answer the same URLs. Inside one endpoint
the second is refused ("GET /{slug}: the endpoint already has GET /{id} - they would answer the same URLs."), and so
is an operation another endpoint already answers ("GET /{slug}: "By id" already answers GET /api/custom/items/{id}.").
A literal path and a parameter path may overlap (`items/featured` and `items/{id}`): the one with fewer `{…}` segments
wins.

### Parameters

Each operation has its own parameters.

| Field | Values |
| --- | --- |
| `name` | letters, digits and `_`, starting with a letter or `_`, ≤ 64; unique (ignoring case) |
| `type` | `string`, `int` (64-bit), `number` (decimal), `bool` (`true/false/1/0/yes/no/on/off`), `date` (ISO 8601, sent as UTC), `guid`, `json` (sent as its JSON text) |
| `source` | `route` (a `{name}` path segment), `query` (`?name=`), `body` (a property of the JSON body, matched ignoring case) |
| `required` | a missing value is a 400; route parameters are always required |
| `default` | used when the request has no value; must convert to the type |

Every `{name}` in the operation's **full** path (the endpoint's path plus its sub-path) needs a `route` parameter, and a
`route` parameter needs its `{name}` there. A parameter the SQL doesn't use and an `@name` without a parameter are
shown as hints in the editor, computed live in the browser (the database rejects a statement with an unbound
placeholder). Only parameters the SQL references are sent to the database.

## The endpoint's token

| Aspect | Implementation |
| --- | --- |
| Format | `dnfe_` + 43 base64url characters (32 random bytes; the prefix `CustomEndpointService.TokenPrefix` makes a leaked one recognisable) |
| Scope | one token per endpoint; it opens every operation of that endpoint and nothing else |
| Created | on the endpoint's first save (`TokenProtected ??= …`), for public endpoints too, so switching one to `Token` never leaves it open |
| Stored | `CustomEndpoint.TokenProtected`: the token encrypted with ASP.NET Core Data Protection (purpose `DotNetForge.CustomEndpoints.Token.v1`; the key ring is in the database table `DataProtectionKeys`), so the editor and the API docs can show it again |
| Read | `TokenOf(endpoint)` → the plain token, or `null` when there is none or the key that encrypted it is gone |
| Checked | `VerifyToken(endpoint, presented)` - constant-time comparison (`CryptographicOperations.FixedTimeEquals`) |
| Shown | in the editor's **Call it** card (the token with a copy button, one line per saved operation whose copy button copies a ready `curl` with the token, and a **Try it in the API docs** link) and pre-filled in the [API docs](api-docs.md) |
| Rotated | **Regenerate token** (`POST /admin/api-endpoints/{id}/token`, after a confirmation) → `RegenerateTokenAsync`: a new token, the old one answers `401` at once for every operation; audited `endpoint.token.regenerated`. An endpoint without a readable token shows **Generate token** instead |

An API token (`dnf_…`) never opens a custom endpoint, and an endpoint's token opens only that endpoint's operations.

## How a call runs

`CustomEndpointsApiController.Call` handles `GET|POST|PUT|PATCH|DELETE /api/custom/{**path}`:

1. **MongoDB** - no SQL: `501 { error }`.
2. **Match** - `CustomEndpointService.MatchAsync`: the operations with that method of the enabled endpoints, each
   matched against its full template (endpoint path + sub-path); a literal segment beats a `{param}`. No match
   (unknown path, other method, disabled endpoint) → `404 { error }`.
3. **Token** - a `Token` endpoint needs `Authorization: Bearer <its token>`; a missing or different token (an API
   token or another endpoint's token included) → `401 { error }` with `WWW-Authenticate: Bearer`. A `Public` endpoint
   ignores any `Authorization` header.
4. **Values** - the operation's parameters from route values, query string and JSON body (invalid JSON → `400`),
   converted to each parameter's type with defaults applied; a missing required value or a wrong type →
   `400 { error, errors: [{ parameter, message }] }`.
5. **Run** - `SqlEndpointRunner.RunAsync(operation, values, rollback: false, …)`: one transaction (committed),
   `CommandTimeout` and a cancellation timer set to the operation's timeout, typed `DbParameter`s. On SQLite a `guid`
   is bound as upper-case text, as EF Core stores Guids there.
6. **Audit** - every method but `GET` writes `endpoint.executed`, shown as the operation's route
   (`CustomEndpointService.Route`, e.g. "DELETE /api/custom/products/{id}"), with the details
   `<name> (called with its token)` or `<name> (public call)` (`Success = false` when the SQL failed).

This is the one API controller that is not an `ApiControllerBase` and has no `[RequireApiPermission]`: a plain
`ControllerBase` with `[ApiController]` that does no API-token authentication and checks the endpoint's own token
itself. It carries `[EnableRateLimiting(RateLimitPolicies.Api)]`, so the `api` rate limit (300 requests / minute / IP)
applies.

### Responses

Each operation answers with its own shape:

| Answers with | Body |
| --- | --- |
| `Rows` | `200 { data: [ { column: value } ], count, truncated }` - at most the row limit; `truncated` when more existed |
| `FirstRow` | `200 { data: { column: value } }`; `404 { error: "Not found." }` when there is no row |
| `Value` | `200 { data: value }` - the first column of the first row (`null` when none) |
| `AffectedRows` | `200 { affected: n }` - for INSERT, UPDATE, DELETE |

Column names are the SQL's (aliases with `AS`); duplicate names get `_2`, `_3`. Values as JSON: numbers, strings and
booleans as they are, Guids as text, dates as ISO 8601 text, bytes as base64. A database without a boolean type
(SQLite) returns `0`/`1`.

### Errors

A failing statement (syntax, missing table, constraint, timeout) answers `500`. Callers of `Token` endpoints (who
sent its token) get the database's message in `{ error }`; callers of public endpoints get a generic message, because the database's message can name tables and
columns. The log records the endpoint, the operation's method and full path and the error type - never the message,
which can echo data ([database security](../database/security.md)).

## The test run

`POST /admin/api-endpoints/test` runs **one operation** of what is in the editor (saved or not): it takes the
endpoint's fields plus `operation` (that operation's JSON), `testValues` (`{ "name": "text" }`) and `rollback`.
`CustomEndpointService.CheckOne` validates the endpoint's settings and that operation; the values are bound as the API
would (body values are sent as the JSON body a client would send) and the answer is
`{ ok, status, body, ms, rolledBack }` - the same status and body as the API - or `{ ok: false, errors }` with the
validation or database messages. By default the transaction is **rolled back**, so INSERT, UPDATE and DELETE can be
tried safely; the admin can turn that off. Every test run is audited as `endpoint.tested`, shown as the tested
operation's route.

## Database support

| Database | Custom endpoints |
| --- | --- |
| SQLite, PostgreSQL, SQL Server, MySQL | ✔ - write the SQL in that database's dialect (quoting, `LIMIT` vs `TOP`, …); PostgreSQL needs `"Pages"` for the CMS's mixed-case tables |
| MongoDB | endpoints can be saved, but answer `501` and can't be test-run |

The SQL runs on the **main** database, not on additional `DATABASES_*` connections.

The tables are `CustomEndpoints` and `CustomEndpointOperations` ([database](../architecture/database.md#tables)). The
`AddEndpointOperations` migrations turned every endpoint saved before operations existed into an endpoint with one
operation (its method, SQL and settings, on the endpoint's own path). Two such endpoints that share a base path stay
separate; saving one of them then asks to add its operations to the other ("… already uses
`/api/custom/<path>` - add your operations to it instead."). On MongoDB (no migrations) an endpoint document from before has no operations - it
answered `501` there anyway.

## Security

- Only Super Admins and Developers open the screen (`[Authorize(Roles = Roles.SuperAdmin + "," + Roles.Developer)]`);
  the sidebar link is hidden for others.
- The SQL can read every table, including password hashes and token hashes, and change or drop anything. Treat the
  Super Admin and Developer roles accordingly; a `Public` endpoint exposes whatever its operations' SQL returns to
  anyone.
- Endpoint tokens are encrypted at rest (Data Protection), compared in constant time, and shown only to Super Admins
  and Developers (editor, API docs). One token opens all of an endpoint's operations: put operations that need
  different callers into different endpoints. Regenerate a token that leaked; disabling or deleting the endpoint also
  stops it.
- Values never become SQL text; the parameter types reject malformed input with a 400 before the database is called.
- A row limit and a timeout bound every call; the `api` rate limit applies to public calls too.

## Tests

`tests/DotNetForge.Tests/Endpoints/EndpointRulesTests.cs` (paths and sub-paths, joining them, matching, parameter
definitions, conversion, binding) and `tests/DotNetForge.IntegrationTests/CustomEndpointTests.cs` (one endpoint with
several operations - list, one item, add, delete - and one token; reads and writes with typed parameters; each
endpoint's own token - no token, an API token or another endpoint's token get `401` -, 404 / 400, values as
parameters, public endpoints, generic public errors, disabling, validation including "Add at least one operation",
clashing operations within an endpoint and across endpoints and a second endpoint on the same base path, the
rolled-back test run of one operation, regenerating a token, Super Admins and Developers only, audit). The endpoints in
the API docs: `ApiDocsTests.cs` ([API docs](api-docs.md#tests)). The integration tests also run on PostgreSQL with
`DNF_TEST_POSTGRES`.

## Planned (not implemented)

- Running on an additional database (`DATABASES_*`) instead of the main one.
- Several tokens per endpoint (today one, so regenerating it breaks every caller at once) and token expiry.
- Response caching; response schemas (column types) in the API docs.
