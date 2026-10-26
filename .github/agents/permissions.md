# Permissions & RBAC (for AI agents)

Full description: [authorization.md](../../.docs/features/authorization.md).

## Roles (most → least privileged)

`Super Admin`, `Admin`, `Developer`, `Editor`, `Author`, `Authenticated`, `Public` (`DotNetForge.Shared.Constants.Roles`,
seven built-in), plus custom roles. `Developer` builds against the API: API Tokens (the `API` area), API Endpoints and
API Docs - beyond those only what every admin user opens (Dashboard, Profile, placeholders, admin extension tabs). Users and roles are managed on `/admin/users` and `/admin/roles` (`UserManagementService`,
`RoleManagementService`).

## What is enforced

| Mechanism | Where | Enforced |
| --- | --- | --- |
| `AdminArea` policy (`AuthorizationPolicies.AdminArea`: any role except Authenticated/Public, `Roles.CanEnterAdmin`) | every admin controller via `AdminControllerBase`, `ExtensionViewController` | yes |
| `[RequirePermission(area, action)]` (stored grants) | Users, Roles, Audit Logs, API Tokens, Settings, Content types, Pages (`Collection types`), Content Manager (`Entries`), Media - screen (`read`) and mutating actions | yes |
| `[Authorize(Roles = "Super Admin")]` | Plugins (list, enable, disable) | yes |
| `[Authorize(Roles = "Super Admin,Developer")]` | API Endpoints (custom SQL endpoints, their tokens) | yes |
| `AuthorizationPolicies.ApiDocs` (signed in as Super Admin or Developer) | API Docs screen, `/admin/api-docs/reference/` (Scalar), `GET /admin/api-docs/openapi/v1.json` | yes |
| `[RequireApiPermission("<key>")]` | every API action (`401` unauthenticated, `403` missing key) except `/api/custom/*`, which checks the endpoint's own token (`401`) or none | yes |
| `IPermissionService.IsGranted(roles, area, action)` via `AdminControllerBase.Can`/`CanModify` | Pages (`Collection types`), Content Manager (`Entries`), Content types (`Content types`, also as `[RequirePermission]`) and Media (`Media`) actions | yes (stored `RolePermission` grants via `IRoleGrantStore`; Super Admin always) |

Consequences: Authors create pages and edit/delete only their own (no publishing, scheduling or reordering) and delete
only their own uploads; Editors and Admins can do everything in content and media; Settings is Super Admin/Admin only
by default; a Developer opens only the Dashboard, the API screens (and the placeholders). Custom roles get exactly their
grants - without `read` on an area they neither see nor open its screen; API Endpoints and API Docs go by role name.
The admin sidebar hides links whose controller the role cannot open (`AdminNavigationService`) - convenience only,
the attributes above are the gate.

## Default grants (`PermissionMatrix`, seeded at install)

| Capability | SA | Admin | Developer | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Read content/media (pages, entries) | Yes | Yes | No | Yes | Yes | Yes | Yes |
| Create content/media | Yes | Yes | No | Yes | Yes | No | No |
| Update/delete any, publish | Yes | Yes | No | Yes | No (own only) | No | No |
| API (API Tokens) | Yes | Yes | Yes | No | No | No | No |
| Users, Roles, Settings, Webhooks, Plugins, Content types | Yes | Yes | No | No | No | No | No |
| Audit logs, Extensions | Yes | read | No | No | No | No | No |
| Updates | Yes | No | No | No | No | No | No |

API permission keys: see [api-reference.md](api-reference.md#permission-keys).
