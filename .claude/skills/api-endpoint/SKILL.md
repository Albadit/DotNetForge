---
name: api-endpoint
description: >-
  Adds or changes a headless API endpoint in DotNetForge CMS (src/DotNetForge.Api): controller deriving
  ApiControllerBase, /api route, RequireApiPermission with a PermissionKeys constant, tenant-scoped projected
  queries, request DTO validation, integration tests and the endpoint table in .docs/features/headless-api.md. Use
  when asked to add, extend or secure an /api endpoint, add a permission key or scope, or expose data to API clients.
---

# Headless API endpoint

Read first: `.docs/features/headless-api.md` (tokens, endpoint table, conventions) and
`.docs/features/authorization.md#api-permission-keys-enforced`.

## Steps

1. **Controller** - in `src/DotNetForge.Api/Controllers/` (a new file, or `ResourceApiControllers.cs` for a small
   read-only resource):
   ```csharp
   [Route("api/<resource>")]
   public sealed class <Resource>ApiController : ApiControllerBase
   ```
   `ApiControllerBase` supplies `[ApiController]`, `[Authorize(AuthenticationSchemes = ApiTokenDefaults.Scheme)]`,
   JSON output and `TenantId` from the token. Controllers are discovered automatically (the Api assembly is an
   application part) - no registration needed.
2. **Permission** - every action gets `[RequireApiPermission(PermissionKeys.<Key>)]`. For a new key add a `const`
   **and** add it to `PermissionKeys.All` in `src/DotNetForge.Shared/Constants/Permissions.cs` (that list drives the
   token-create checkboxes and `IsKnown`). Existing tokens do not gain new keys.
3. **Query** - `AsNoTracking()`, `Where(x => x.TenantId == TenantId)` (global data must be a deliberate choice, like
   settings with `TenantId == null`), project with `Select` into an anonymous object or DTO. **Never return entities**
   (`User.PasswordHash`, `ApiToken.TokenHash`, `Webhook.Secret`).
4. **Writes** - request model as a nested class (see `ContentApiController.CreatePageRequest`) or in
   `src/DotNetForge.Shared/Dtos`; validate and return `BadRequest(new { error = "..." })`; reuse domain rules instead of
   duplicating them - `Api` cannot reference the web host, so shared rules are reached through interfaces in
   `Shared` (`IPageService`, `IAuditService`).
   Audit writes: inject `IAuditService` (`src/DotNetForge.Shared/Auditing`) - the actor is recorded as the API token.
   Content pages: inject `IPageService` and pass a `PageInput` (see `ContentApiController.CreatePage`).
5. **Status codes** - 200/201 (`CreatedAtAction`), 400, 401/403 come from the auth handler/filter, 404 for ids not in
   the tenant, 429 from the `api` rate limit (inherited from `ApiControllerBase`). Validate string lengths before
   saving (PostgreSQL enforces column lengths). Catch expected `DbUpdateException`s (unique indexes) and return 409/400 rather than a 500.
6. **Tests** - integration tests in `tests/DotNetForge.IntegrationTests`: 401 without token; 403 with a token lacking
   the key; 200 with it; data from another tenant not returned. Create tokens in the test via
   `IApiTokenFactory.Generate()` + an `ApiToken` row (scope from `factory.Services.CreateScope()`).
7. **Docs** - add the row to the endpoint table in `.docs/features/headless-api.md`; new keys in the key list in
   `.docs/features/authorization.md`; status in `.docs/implementation-status.md`.
8. Run the **verify** skill.

## Don't

- Use the cookie scheme or `[Authorize]` without the `ApiToken` scheme.
- Read the tenant from a header, query or body.
- Add a repository layer just for the API - `Api` → `Data` is an accepted, documented shortcut.
