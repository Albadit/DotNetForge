# Permissions & RBAC (for AI agents)

Every admin and API action resolves a permission check. A user's effective permissions are the
**union** over all assigned roles.

## Roles (most → least privileged)

`Super Admin`, `Admin`, `Editor`, `Author`, `Authenticated`, `Public` (constants in
`DotNetForge.Shared.Constants.Roles`). Built-in roles cannot be deleted or renamed; Super Admin always
has full access; Public is read-only public content.

## Permission areas

`Collection types`, `Single types`, `Plugins`, `Settings`, `Extensions`, `Media`, `Users`, `Roles`,
`API`, `Webhooks` (+ `Audit logs`, `Updates`). See `PermissionAreas`.

## Checking permissions

- Admin/runtime: `IPermissionService.Has(role, area, action)` /
  `HasAny(roles, area, action)` - evaluates `PermissionMatrix` (Shared).
- Seeded `RolePermission` rows mirror the matrix (`DataSeeder` uses `PermissionMatrix.GrantsFor`).
- API tokens carry granular keys (`content.read`, `media.read`, …, see `PermissionKeys`); the
  `[RequireApiPermission("content.read")]` filter enforces them (`401` unauthenticated, `403`
  missing permission).

## Default matrix highlights

| Capability | SA | Admin | Editor | Author | Auth | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Publish content | Yes | Yes | Yes | No | No | No |
| Manage users/roles/settings | Yes | Yes | No | No | No | No |
| Manage extensions / install updates | Yes | No | No | No | No | No |
| Read public content | Yes | Yes | Yes | Yes | Yes | Yes |

The admin area itself requires an admin-capable role (`Super Admin`, `Admin`, `Editor`, `Author`) via
the `AdminArea` authorization policy.
