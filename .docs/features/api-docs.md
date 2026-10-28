# API docs

An interactive reference of the [headless API](headless-api.md) and the [custom endpoints](custom-endpoints.md),
rendered by [Scalar](https://scalar.com) from an OpenAPI document the app builds itself. It lives inside the admin, at
**Settings › Global Settings › API Docs** (`/admin/api-docs`), and is meant for trying calls: paste an API token and
send a request, or call a custom endpoint whose token is already filled in.

Only **Super Admins** and **Developers** open it ([authorization](authorization.md#the-developer-role)).

## Where it lives

```text
Directory.Packages.props                                         Microsoft.AspNetCore.OpenApi 10.0.12, Scalar.AspNetCore 2.18.3
src/DotNetForge.Web/ApiDocs/ApiDocsTransformers.cs               ApiDocsDocument (name, which actions), document + operation transformers
src/DotNetForge.Web/ApiDocs/ApiDocsEndpoints.cs                  MapApiDocs: the OpenAPI document route and the Scalar page, Scalar options, pre-filled endpoint tokens
src/DotNetForge.Web/Startup/DependencyRegistration.cs            services.AddOpenApi("v1", ...), the ApiDocs authorization policy
src/DotNetForge.Web/Program.cs                                   app.MapApiDocs(env)
src/DotNetForge.Web/Areas/Admin/Controllers/ApiDocsController.cs the admin screen /admin/api-docs (Views/ApiDocs/Index.cshtml)
src/DotNetForge.Web/Middleware/SecurityHeadersMiddleware.cs      the Scalar page's own Content-Security-Policy
src/DotNetForge.Shared/Constants/AuthorizationPolicies.cs        AuthorizationPolicies.ApiDocs
```

## Routes

| Route | What | Access |
| --- | --- | --- |
| `/admin/api-docs` | the admin screen: Scalar in a frame, with **API tokens**, **API endpoints** and **Open full screen** actions | `[Authorize(Policy = AuthorizationPolicies.ApiDocs)]` |
| `/admin/api-docs/reference/` | Scalar's own full page (and its scripts under that prefix) - what the frame and "Open full screen" show | `.RequireAuthorization(AuthorizationPolicies.ApiDocs)` |
| `/admin/api-docs/openapi/{documentName}.json` | the OpenAPI document - `v1` is the only one (`MapOpenApi`) | `.RequireAuthorization(AuthorizationPolicies.ApiDocs)` |

The `ApiDocs` policy is *authenticated + role `Super Admin` or `Developer`*. Signed-out visitors are sent to
`/login`, other roles to `/account/denied`. The sidebar link (icon `book`, after API Endpoints) is hidden for
roles that can't open it.

Scalar renders a complete HTML page of its own, so the admin screen isolates it in an iframe - the same pattern as an
admin extension tab ([extension host](../pages/extension-host.md)): a skeleton shows until the frame has loaded.

## The OpenAPI document

One document, `v1` (`ApiDocsDocument.Name`), registered with `services.AddOpenApi`:

- **Which actions** - `ApiDocsDocument.Includes`: every controller action whose route starts with `api/`, except the
  `api/custom/` catch-all route (`/api/custom/{**path}` is one action; the endpoints behind it are added from the
  database instead, see below). Admin and public-site routes are not in it.
- **Groups** - the API controllers carry `[Tags(...)]`: *Pages*, *Structured content*, *Media*, *Users*, *Roles*,
  *Settings*, *Extensions*. Each enabled custom endpoint is a group of its own, named like the endpoint (the
  catch-all controller's *Custom endpoints* tag is not used, because its action is left out).
- **Title and description** - `ApiDocsDocumentTransformer` sets the title to `<APP_NAME> API` and a short description
  of the two kinds of token.
- **API tokens** - the security scheme `ApiToken` (HTTP bearer). `ApiDocsOperationTransformer` marks every operation
  whose action has `[RequireApiPermission]` as needing it and appends *"Needs an API token with `<key>`."* to its
  description (the attribute's public `Permission` property).
- **Custom endpoints** - added per request (`CustomEndpointService.ListAsync` and `OperationsAsync`), so a saved
  endpoint shows on the next reload. Every **enabled** endpoint becomes a tag, and each of its operations an operation
  at `/api/custom/<endpoint path>[/<sub-path>]` with the operation's method (operations on the same path share one path
  item):

| Part | From |
| --- | --- |
| Tag | the endpoint's name, or `<name> (/<path>)` when an earlier endpoint already has that name; the tag's description is the endpoint's description, else `Custom endpoint /api/custom/<path>.` |
| Summary | the operation's name, else "METHOD /sub-path" (just the method on the endpoint's own path) |
| Parameters | `route` parameters as path parameters, `query` parameters as query parameters (with `required`) |
| Request body | `body` parameters as one `application/json` object (required when any body parameter is) |
| Schemas | `int` → integer `int64`, `number` → number, `bool` → boolean, `date` → string `date-time`, `guid` → string `uuid`, `json` → any JSON value, `string` → string; a `default` becomes the schema default |
| Responses | `200` described per result shape (`{ data: [rows], count, truncated }`, `{ data: {row} }`, `{ data: value }`, `{ affected }`), `400` (missing or wrong value), `500` (the SQL failed); plus `401` for token endpoints and `404` for `FirstRow` |
| Security | a token endpoint gets one HTTP-bearer scheme named `Endpoint <first 8 hex digits of its id>`, required by all its operations; a public endpoint has none |

Disabled endpoints are left out.

## Tokens in Scalar

- **Headless API** - Scalar asks for the `ApiToken` bearer value. **Super Admins can leave it empty:** Scalar's
  **Send** is a same-origin request from the signed-in browser, so the API uses the admin session with every
  permission ([authentication](authentication.md#api-token-authentication)); the scheme's description says so.
  Developers (and Super Admins trying a token's own permissions) paste an API token created on
  [API Tokens](../pages/api-tokens.md) (its secret is shown once, at creation) - a token, when filled in, is always
  what counts. The app stores nothing that is typed into Scalar.
- **Custom endpoints** - `MapScalarApiReference` runs per request and calls
  `options.AddHttpAuthentication("Endpoint <id8>", token)` for each enabled token-protected endpoint whose token can be
  decrypted (`CustomEndpointService.TokenOf`), so its token is already filled in. The tokens are therefore in the
  Scalar page's HTML - which is why only Super Admins and Developers, who can see them in the endpoint editor anyway,
  may open it.

Scalar options (`ApiDocsEndpoints.MapApiDocs`): title `<APP_NAME> API`, the OpenAPI route above,
`DisableDefaultFonts` (no font CDN - the CSP allows fonts from this origin only), `DisableTelemetry`, `DisableAgent`
("Ask AI") and `DisableMcp` (both would send the document to Scalar's servers), `HideDeveloperTools`, and `WithNonce`.

## Content-Security-Policy

Every response gets the strict policy (`script-src 'self'; style-src 'self'; …` - [security](security.md#implemented-protections)).
The Scalar page is the one exception: it has one inline script and injects its styles at runtime. With `WithNonce`,
Scalar stores a per-request nonce in `HttpContext.Items[ScalarOptions.NonceHttpContextItemKey]` and puts it on that
script; `SecurityHeadersMiddleware` checks for it in `Response.OnStarting` and then sends
`SecurityHeadersMiddleware.ApiReferencePolicy(nonce)` - the same policy with `script-src 'self' 'nonce-<nonce>'` and
`style-src 'self' 'unsafe-inline'`. Every other response, including the `/admin/api-docs` screen around the frame,
keeps the strict policy. "Test Request" calls go to this origin, so `connect-src 'self'` is enough.

## Using it

1. Open **Settings › API Docs** (or **Open full screen** for more room).
2. **As a Super Admin** - pick an operation and press **Test Request** → **Send**; no token needed. Calls are real
   (writes are committed and audited under your name).
3. **An API token** - create one on API Tokens with the keys you want to try, pick an operation (e.g. *Structured
   content › GET /api/entries/{type}*), paste the token into the `ApiToken` authentication field, fill in the
   parameters and press **Test Request**. The operation's description names the key it needs; a token without it
   answers `403`.
4. **A custom endpoint** - open the endpoint's group (its name), pick an operation, fill in its values and press
   **Test Request**; the token is already set. The editor's **Try it in the API docs** link ([API Endpoints](../pages/api-endpoints.md))
   leads here. Unlike the editor's test run, this is a real call: writes are committed and audited.

## Tests

`tests/DotNetForge.IntegrationTests/ApiDocsTests.cs`: only Super Admins and Developers open the screen, the reference
and the document (anonymous → login, Editor → denied), what a Developer sees in the sidebar; the document lists the
API (with the `ApiToken` scheme and the permission in the description) and the custom endpoints (a token endpoint's
operations - a list and a `DELETE /{id}` - under its tag "Items" with its own scheme, none for a public one), Scalar's page contains the endpoint's token, and its CSP carries the nonce while
other pages keep the strict policy.

## Planned (not implemented)

- Persisting the API token entered in Scalar between visits.
- Documenting the endpoints that extensions map.
- A public (anonymous) API reference.
