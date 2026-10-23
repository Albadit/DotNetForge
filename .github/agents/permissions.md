# Permissions & RBAC (for AI agents)

Full description: [authorization.md](../../.docs/features/authorization.md).

## Roles (most → least privileged)

`Super Admin`, `Admin`, `Editor`, `Author`, `Authenticated`, `Public` (`DotNetForge.Shared.Constants.Roles`), plus
custom roles. Users and roles are managed on `/admin/users` and `/admin/roles` (`UserManagementService`,
`RoleManagementService`).

## What is enforced

| Mechanism | Where | Enforced |
| --- | --- | --- |
| `AdminArea` policy (`AuthorizationPolicies.AdminArea`: any role except Authenticated/Public, `Roles.CanEnterAdmin`) | every admin controller via `AdminControllerBase`, `ExtensionViewController` | yes |
| `[RequirePermission(area, action)]` (stored grants) | Users, Roles, Audit Logs, API Tokens, Settings - screen (`read`) and mutating actions | yes |
| `[Authorize(Roles = "Super Admin")]` | Plugins (list, enable, disable) | yes |
| `[RequireApiPermission("<key>")]` | every API action (`401` unauthenticated, `403` missing key) | yes |
| `IPermissionService.HasAny(tenantId, roles, area, action)` via `AdminControllerBase.Can`/`CanModify` | Content Manager (`Collection types`) and Media (`Media`) actions | yes (stored `RolePermission` grants via `IRoleGrantStore`; Super Admin always) |

Consequences: Authors create pages and edit/delete only their own (no publishing, scheduling or reordering) and delete
only their own uploads; Editors and Admins can do everything in content and media; Settings is Super Admin/Admin only
by default. Custom roles get exactly their grants.
The admin sidebar hides links whose controller the role cannot open (`AdminNavigationService`) - convenience only,
the attributes above are the gate.

## Default grants (`PermissionMatrix`, seeded per tenant)

| Capability | SA | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Read content/media | Yes | Yes | Yes | Yes | Yes | Yes |
| Create content/media | Yes | Yes | Yes | Yes | No | No |
| Update/delete any, publish | Yes | Yes | Yes | No (own only) | No | No |
| Users, Roles, Settings, API, Webhooks, Plugins | Yes | Yes | No | No | No | No |
| Audit logs, Extensions | Yes | read | No | No | No | No |
| Updates | Yes | No | No | No | No | No |

API permission keys: see [api-reference.md](api-reference.md#permission-keys).
