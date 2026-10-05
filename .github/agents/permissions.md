# Permissions & RBAC (for AI agents)

Full description: [authorization.md](../../.docs/features/authorization.md).

## Roles (most → least privileged)

`Super Admin`, `Admin`, `Editor`, `Author`, `Authenticated`, `Public` (`DotNetForge.Shared.Constants.Roles`). Only the
setup wizard assigns a role (Super Admin); there is no role or user management UI.

## What is enforced

| Mechanism | Where | Enforced |
| --- | --- | --- |
| `AdminArea` policy (Super Admin, Admin, Editor, Author) | every admin controller via `AdminControllerBase`, `ExtensionViewController` | yes |
| `[Authorize(Roles = "Super Admin,Admin")]` | Users, Roles, Audit Logs, API Tokens | yes |
| `[Authorize(Roles = "Super Admin")]` | Plugins | yes |
| `[RequireApiPermission("<key>")]` | every API action (`401` unauthenticated, `403` missing key) | yes |
| `IPermissionService.HasAny(roles, area, action)` / `PermissionMatrix` via `AdminControllerBase.Can`/`CanModify` | Content Manager (`Collection types`) and Media (`Media`) actions | yes (static matrix; stored `RolePermission` rows are not read) |
| `[Authorize(Roles = "Super Admin,Admin")]` | Settings | yes |

Consequences: Authors create pages and edit/delete only their own (no publishing, scheduling or reordering) and delete
only their own uploads; Editors and Admins can do everything in content and media; Settings is Super Admin/Admin only.

## Permission matrix (reference data, `PermissionMatrix`)

| Capability | SA | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Read content/media | Yes | Yes | Yes | Yes | Yes | Yes |
| Create content/media | Yes | Yes | Yes | Yes | No | No |
| Update/delete any, publish | Yes | Yes | Yes | No (own only) | No | No |
| Users, Roles, Settings, API, Webhooks, Plugins | Yes | Yes | No | No | No | No |
| Audit logs, Extensions | Yes | read | No | No | No | No |
| Updates | Yes | No | No | No | No | No |

API permission keys: see [api-reference.md](api-reference.md#permission-keys).
