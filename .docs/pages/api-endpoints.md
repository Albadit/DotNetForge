# API Endpoints

## Purpose

Lets a Super Admin or Developer build their own API: an endpoint is a name and a base path under `/api/custom/`
with one or more **operations** - each an HTTP method on that path (or a sub-path such as `{id}`) with its own SQL and
`@parameters`. Every endpoint has its own token, one for all its operations; API tokens don't open custom endpoints.
The rules, the call flow and the responses are in [custom endpoints](../features/custom-endpoints.md).

## Route / Navigation

| Screen | Route | View | Title (h1, last breadcrumb) |
| --- | --- | --- | --- |
| List | `GET /admin/api-endpoints` | `ApiEndpoints/Index.cshtml` | "API Endpoints" |
| New | `GET /admin/api-endpoints/new` | `ApiEndpoints/Edit.cshtml` | "New API endpoint" |
| Create | `POST /admin/api-endpoints/new` | redirects to the editor, or `Edit.cshtml` with errors | - |
| Edit | `GET /admin/api-endpoints/{id}` | `ApiEndpoints/Edit.cshtml` | the endpoint's name (description `/api/custom/<path> · N operations`) |
| Save | `POST /admin/api-endpoints/{id}` | redirects to the editor, or `Edit.cshtml` with errors | - |
| Regenerate token | `POST /admin/api-endpoints/{id}/token` | redirects to the editor (404 for an unknown id) | - |
| Delete | `POST /admin/api-endpoints/{id}/delete` | redirects to the list | - |
| Test run | `POST /admin/api-endpoints/test` (the form + `operation`, `testValues`, `rollback`) | JSON for one operation (see [the test run](../features/custom-endpoints.md#the-test-run)) | - |

| Item | Value |
| --- | --- |
| Sidebar entry | **Settings → Global Settings → API Endpoints**, after API Tokens and before API Docs (`AdminNavigation.Tree`), shown only to Super Admins and Developers |
| Breadcrumbs | Settings › Global Settings › **API Endpoints**; editor: … › API Endpoints › **name** |
| Child flow | list → **New endpoint** → editor → **Save** → editor (toast) |
| Linked from | the sidebar; **API endpoints** button on the [API Docs](api-docs.md) screen |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/ApiEndpointsController.cs   list, new/edit, save, regenerate token, delete, test run
src/DotNetForge.Web/Areas/Admin/Views/ApiEndpoints/Index.cshtml         list card: name, path, operation badges, access, status, updated
src/DotNetForge.Web/Areas/Admin/Views/ApiEndpoints/Edit.cshtml          Path card, Operations (cards + "Operations as JSON") + Settings, Call it, Delete
src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs               ApiEndpointsIndexViewModel (+ Operations), ApiEndpointEditViewModel (+ Calls, Token)
src/DotNetForge.Web/wwwroot/js/forge/endpoints.js                       operation cards ⇄ OperationsJson: SQL hints, parameter table, result settings, test run each
src/DotNetForge.Shared/Entities/CustomEndpoint.cs                       CustomEndpoint (+ TokenProtected), CustomEndpointOperation
src/DotNetForge.Web/wwwroot/css/forge/admin.css                         "API Endpoints editor" section
src/DotNetForge.Content/Endpoints/CustomEndpointService.cs              rules (shared with the API), endpoint tokens (TokenOf, VerifyToken, RegenerateTokenAsync)
```

## Page layout

- **List** - one card: name (link) with description, `/api/custom/path`, one coloured badge per operation (method
  and sub-path; GET info, POST success, PUT/PATCH warning, DELETE danger), access (`Token` / `Public`), status,
  updated; sortable, paged by 25. Empty state with **New endpoint**. A warning on MongoDB.
- **Editor** (`.editor` two columns; a new endpoint starts with one GET operation on its own path):
  - **Path** - the base path with a fixed `/api/custom/` prefix.
  - **Operations** - "What the API answers: one per method and sub-path, each with its own SQL, parameters and test
    run." One card per operation built by `endpoints.js` (method, sub-path, SQL with hints, parameter table, result
    settings, test run), each of which opens and closes; **Open all** / **Close all** (shown from two operations on)
    and **Add operation** next to the heading. The posted field is `OperationsJson`, also editable under "Operations
    as JSON" (the only editor without JavaScript).
  - Aside: **Settings** (name, description, who may call it, enabled - off: every operation answers 404, Save),
    **Call it** (saved endpoints only, see below) and **Delete endpoint** (only the button, with confirmation).

**Who may call it** - two radio cards, **Token** (default: callers send the endpoint's own token, one for all its
operations, shown under Call it once saved) and **Anyone** (no token - only for data that may be public); no hint text.

**Call it** card (only once the endpoint is saved):

```text
Call it - no description                                                 (Token)
        - "Anyone can call it, no token needed."                          (Anyone)
├── Token: one line, the endpoint's token (dnfe_…, cut with an ellipsis,  (Token only; "This endpoint has no token
│   the full value in its title) + copy icon button                         yet. Generate one below." when none)
├── Operations "under /api/custom - copy a ready curl command" - one line per saved operation:
│   [METHOD badge] /<template> (endpoint path + sub-path without the shared /api/custom prefix, e.g. /pages/{slug};
│   ellipsis; title: its name and the full /api/custom/... route) + copy icon button that copies
│   curl -X <METHOD> "<origin>/api/custom/<template>"
│   (+ -H "Authorization: Bearer <the real token>" for Token, + a JSON body line for POST/PUT/PATCH)
├── [Try it in the API docs] → /admin/api-docs
└── [Regenerate token] / [Generate token] (Token only; submits the hidden form #tokenForm)
```

## Functionality

- **Operation cards** (`endpoints.js`, kept in sync with `OperationsJson`): each card has the method, the sub-path
  (with the live prefix `/api/custom/<path>/`, placeholder "e.g. {id}"), an optional name, the SQL, the parameter table (**Add parameter**),
  **Answers with** / **Timeout (s)** / **Row limit** and a collapsible **Test this operation** panel (a field per
  parameter, "Undo changes after the run", **Run test**). **Add operation** appends a GET card on the sub-path `{id}`
  and scrolls to it. Removing an operation asks first ("Remove GET /{id}? It is gone once you save."); the last one
  can't be removed. Editing "Operations as JSON" by hand redraws the cards when the JSON is valid; invalid JSON (e.g.
  after a failed save) opens that field and hides the cards.
- **Opening and closing operations** - a chevron button in each card's header (`aria-expanded`, "Open/Close this
  operation") hides the card's body (name, SQL, parameters, settings, test); a closed card shows only its method and
  sub-path, plus its name when it has one (never the SQL). With one operation the card starts open, with
  several they all start closed; a newly added operation opens and the others close. **Open all** (while any card is
  closed) / **Close all** toggles every card; it is shown from two operations on.
- **Parameters fill themselves in** (per operation): a `route` parameter is added for every `{name}` in the full path
  (base path + sub-path) and a route parameter whose `{name}` left the path is dropped; a parameter is added for every
  new `@name` in the SQL (from the query string for GET and DELETE, from the JSON body otherwise)
  ([parameters](../features/custom-endpoints.md#parameters)).
- **SQL hints** are computed live in the browser under each SQL box: an `@name` without a parameter ("… the database
  will reject the statement.") and a parameter the SQL doesn't use.
- **Save** validates on the server ([rules](../features/custom-endpoints.md#an-endpoint)); errors appear in
  `<form-errors />` with the form as typed. Success redirects to the editor with a toast ("Created '…'." /
  "Saved '…'."). The first save gives the endpoint its token (`CustomEndpointService.SaveAsync`:
  `TokenProtected ??= …`), whatever its access, so switching it to **Token** later never leaves it open.
- **Endpoint token** - `dnfe_` + 32 random bytes (Base64Url), stored encrypted with Data Protection (protector
  `DotNetForge.CustomEndpoints.Token.v1`, `CustomEndpoint.TokenProtected`), so the editor can show it again and the
  [API Docs](api-docs.md) can fill it in. Calls compare it in constant time (`VerifyToken`). If the key that encrypted
  it is gone, `TokenOf` returns nothing: the card says "This endpoint has no token yet." and calls get 401 until a new
  one is generated.
- **Regenerate token** - confirm dialog (title "Regenerate token", button **Regenerate**): "Create a new token for
  '<name>'? Apps using the current token get 401 until they switch." → `RegenerateTokenAsync` saves a new token and
  audits `endpoint.token.regenerated` → redirect to the editor with the toast "New token for '<name>'. The old one no
  longer works." The old token is refused at once.
- **Test run** (per operation) posts the whole form plus the operation, its test values and the rollback choice with
  the antiforgery header and shows that operation's result without saving anything (changes are undone unless the
  admin turns that off). It needs no token.
- **Try it in the API docs** opens [API Docs](api-docs.md), where every enabled token endpoint is listed with its token
  already filled in.
- **Delete** asks first ("Delete '<name>' and its operations? Apps calling them get 404. This can't be undone.");
  the endpoint and all its operations go.

## Permissions

Super Admins and Developers only (`[Authorize(Roles = "Super Admin,Developer")]` on `ApiEndpointsController`, plus
`AdminArea`): other users are sent to [Access denied](access-denied.md) and don't see the link. There is no
finer-grained check - whoever opens the screen can create, edit, test, regenerate tokens and delete. The SQL runs with
the application's database rights.

Calling an endpoint is not decided by roles or API tokens: a **Token** endpoint needs its own token
(`Authorization: Bearer dnfe_…`, else 401), an **Anyone** endpoint needs none and ignores a token that is sent
([custom endpoints → how a call runs](../features/custom-endpoints.md#how-a-call-runs),
[the endpoint's token](../features/custom-endpoints.md#the-endpoints-token)).

## Audit

Entity `CustomEndpoint` ([audit logging](../features/audit-logging.md)). From this screen: `endpoint.created` and
`endpoint.updated` (shown as `/api/custom/<path>`; details: the name and its operations, e.g.
"Products: GET, GET /{id}, POST"), `endpoint.deleted` and `endpoint.token.regenerated` (shown as `/api/custom/<path>`;
details: the name), `endpoint.tested` (shown as the tested operation's route, e.g. "GET /api/custom/products/{id}").
From the API: `endpoint.executed` (non-GET calls, shown as the operation's route).

## Known limitations

- The SQL is not checked until it runs (test run or call); the database's dialect applies.
- No version history of an endpoint's SQL; the audit log records who changed it, not what.
- An endpoint has exactly one token: no expiry, no second token for a smooth switch - regenerating cuts off every
  caller of the old one at once.
- Anyone who can open the screen can read every endpoint's token.

## Related pages

[API Docs](api-docs.md) · [API Tokens](api-tokens.md) · [Audit Logs](audit-logs.md) ·
[Custom endpoints](../features/custom-endpoints.md) · [Headless API](../features/headless-api.md)
