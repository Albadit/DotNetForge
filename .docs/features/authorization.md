# Authorization

What a signed-in user or an API token may do. Two mechanisms:

| Mechanism | Vocabulary | Enforced where |
| --- | --- | --- |
| **Role permissions** (admin) | `(area, action)` pairs (`PermissionAreas`, `PermissionActions`) granted to roles, stored as `RolePermission` rows | `AdminArea` policy on every admin screen, `[RequirePermission(area, action)]` on screens and actions, `AdminControllerBase.Can` / `CanModify` inside actions |
| **API permission keys** | dotted keys (`PermissionKeys`, e.g. `content.read`) | `[RequireApiPermission(...)]` on every API action |

## Roles

`src/DotNetForge.Shared/Constants/Roles.cs` holds the six built-in roles, most to least privileged. Each tenant gets
them at install (`DataSeeder`, `Role.IsBuiltIn = true`) with the default grants of the
[permission matrix](#permission-matrix-default-grants). Custom roles are created on the [Roles screen](../pages/roles.md)
and given to users on the [Users screen](../pages/users.md).

| Role | Enters the admin | Grants |
| --- | --- | --- |
| `Super Admin` | yes | always everything - stored grants are ignored and can't be edited |
| `Admin` | yes | stored (editable) |
| `Editor` | yes | stored (editable) |
| `Author` | yes | stored (editable) |
| `Authenticated` | no - every admin screen redirects to denied | stored (editable; used by the site, not the admin) |
| `Public` | - (anonymous visitors have no role claim) | fixed: read-only public content |
| any custom role | yes | stored (editable) |

`Roles.CanEnterAdmin(roles)` is true for any role except `Authenticated` and `Public` (`Roles.SiteOnly`).
`Roles.AdminCapable` still lists the four admin built-ins for extensions that check names.

A user's roles are read at sign-in into role claims, so a changed **role assignment** reaches a signed-in user at
their next sign-in. Changed **grants** apply on the next request (see below); disabling or deleting a user ends their
session within 30 seconds ([session re-validation](authentication.md#session-re-validation)).

## How permissions are evaluated

`IPermissionService.HasAny(tenantId, roles, area, action)` (`src/DotNetForge.Core/Authorization/PermissionService.cs`):

1. a role named `Super Admin` → allowed;
2. otherwise the tenant's stored grants (`IRoleGrantStore.GetGrants(tenantId)`: role name → set of `area|action`
   keys) - allowed when **any** of the user's roles holds the pair (the union of all roles);
3. a role name the tenant doesn't have falls back to the reference `PermissionMatrix` (which grants unknown names
   nothing).

`RoleGrantStore` (`src/DotNetForge.Data/RoleGrantStore.cs`, singleton) reads the `Roles` and `RolePermissions`
tables with two single-table queries and caches each tenant's grants. `RoleManagementService` calls
`Invalidate(tenantId)` after every change, so grant edits apply at once on this instance; another instance re-reads
within 30 seconds (`RoleGrantStore.Lifetime`).

The matrix-only overloads `Has(role, area, action)` and `HasAny(roles, area, action)` evaluate `PermissionMatrix`
alone (used by tests and tools).

## Screen access

Every admin controller derives from `AdminControllerBase`, which carries the `AdminArea` policy
(`AuthorizationPolicies.AdminArea`; `DependencyRegistration.AdminAreaPolicy` is an alias): signed in and
`Roles.CanEnterAdmin`. Extensions use the same constant to protect their own endpoints
(`.RequireAuthorization(AuthorizationPolicies.AdminArea)`).

On top of that, `[RequirePermission(area, action)]` (`src/DotNetForge.Web/Startup/PermissionAuthorization.cs`)
requires a grant. It is a policy named `permission:{area}:{action}`, built by `PermissionPolicyProvider` and checked by
`PermissionAuthorizationHandler` through `HasAny` with the user's tenant and role claims. On a controller it gates the
screen; on an action it adds to the controller's requirement (both must pass).

| Screen / endpoint | Requires | Defaults: Super Admin · Admin · Editor · Author |
| --- | --- | :-: |
| Dashboard, Content Manager (view), Media (view), enabled admin extension tabs and `/admin/ext/{id}/resources`, module placeholders | `AdminArea` only | ✔ ✔ ✔ ✔ |
| Content: create page `POST /admin/content/create` | `Collection types` / `create` (`Can`) | ✔ ✔ ✔ ✔ |
| Content: update page `POST /admin/content/update/{id}` | `update`, or `update.own` on own pages (`CanModify`) | ✔ ✔ ✔ own |
| Content: change **Published** or the schedule dates (in update) | + `publish` | ✔ ✔ ✔ ✘ |
| Content: delete page `POST /admin/content/delete/{id}` | `delete`, or `delete.own` on own pages | ✔ ✔ ✔ own |
| Content: reorder `POST /admin/content/reorder` | `update` | ✔ ✔ ✔ ✘ |
| Media: upload `POST /admin/media/upload` | `Media` / `create` | ✔ ✔ ✔ ✔ |
| Media: make private/public `POST /admin/media/visibility/{id}` | `update`, or `update.own` on own files | ✔ ✔ ✔ own |
| Media: delete `POST /admin/media/delete/{id}` and `delete-selected` | `delete`, or `delete.own` on own files | ✔ ✔ ✔ own |
| Settings `GET /admin/settings` / save `POST /admin/settings/save` | `Settings` / `read` / + `update` | ✔ ✔ ✘ ✘ |
| API Tokens list / create / revoke | `API` / `read` / + `create` / + `delete` | ✔ ✔ ✘ ✘ |
| Users list and edit screen / create / save, enable, disable / delete | `Users` / `read` / + `create` / + `update` / + `delete` | ✔ ✔ ✘ ✘ |
| Roles list and edit screen / create, duplicate / save / delete | `Roles` / `read` / + `create` / + `update` / + `delete` | ✔ ✔ ✘ ✘ |
| Audit Logs | `Audit logs` / `read` | ✔ ✔ ✘ ✘ |
| Plugins: list, enable, disable | `[Authorize(Roles = "Super Admin")]` (not a grant) | ✔ ✘ ✘ ✘ |

The defaults are the seeded grants; a custom role or an edited built-in role gets exactly what it is granted. A
custom role with `Collection types` / `read` only, for example, opens the Content Manager but not Settings.

- **Own** = the record's creator is the signed-in user: `Page.CreatedById` (set by `ContentController.Create`) or
  `MediaFile.UploadedById`. Seeded pages have no `CreatedById`, so an `Author` cannot edit or delete them.
- An `Author` therefore creates pages (unpublished), edits and deletes only its own, and cannot publish, unpublish,
  schedule or reorder. A blocked publish change is a form error: "You don't have permission to publish, unpublish or
  schedule pages."
- Screens hide what the user can't do: Media's upload card, a file's **Delete** and its bulk-selection checkbox
  (`MediaIndexViewModel.CanUpload`, `MediaRowViewModel.CanDelete`); the dashboard's quick actions
  (`DashboardViewModel.CanCreatePages`/`CanUpload`); the **Create** buttons and edit controls on Users and Roles.
  The Content Manager hides only drag-to-reorder (`ViewData["CanReorder"]`) and relies on the server check otherwise.
- Private media downloads (`GET /media/{id}`) are allowed to any user of the file's tenant who may enter the admin
  ([media storage](media-storage.md#download-flow)).

A failed policy → cookie forbid → `302 /account/denied` ([screen](../pages/access-denied.md)); a failed
`Can`/`CanModify` returns `Forbid()`, which lands on the same screen.

The sidebar hides links the user cannot open: `AdminNavigationService` evaluates each linked screen's controller
`[Authorize]` data - including `[RequirePermission]` - for the current user ([pages → navigation](../architecture/pages.md#navigation)).
This is convenience, not the gate: the policies still refuse a direct URL.

Endpoints an extension maps itself (minimal APIs, MVC controllers) are **public** unless the extension adds an
authorization requirement; they answer only while the extension is active ([extensions → Security
model](extensions.md#security-model)). Public extension resources (`/ext/{id}/resources`, themes and modules) are
anonymous.

### Safety rules for users and roles

Enforced by `UserManagementService` and `RoleManagementService` (`src/DotNetForge.Web/Services/`), whatever the
caller's grants:

- Only a Super Admin assigns the `Super Admin` role, or changes, disables or deletes a user who holds it.
- The tenant always keeps at least one **enabled** Super Admin: removing the role from, disabling or deleting the
  last one is rejected.
- Nobody disables or deletes their own account.
- Built-in roles keep their names and can't be deleted; `Super Admin` and `Public` grants can't be edited; only a
  Super Admin changes the `Super Admin` role.
- Only a Super Admin changes the grants of a role they hold themselves (so an Admin can't lock themselves out by
  editing `Admin`).
- A role still held by users can't be deleted.

### `AdminControllerBase` helpers

| Member | Meaning |
| --- | --- |
| `TenantId` | `dnf:tenant` claim (or `Guid.Empty`) |
| `CurrentUserId` | `NameIdentifier` claim as `Guid?` |
| `Can(area, action)` | `IPermissionService.HasAny(TenantId, role claims, area, action)` - stored grants, union over the user's roles |
| `CanModify(area, anyAction, ownAction, createdById)` | `Can(area, anyAction)` or (`createdById == CurrentUserId` and `Can(area, ownAction)`) |

## Permission catalog

`src/DotNetForge.Shared/Authorization/PermissionCatalog.cs` lists the grantable pairs - what the role editor offers,
what a saved grant set may contain and what a user's effective permissions show. `Key(area, action)` is the stored
form `area|action`.

| Area (label) | Actions |
| --- | --- |
| `Collection types` (Content pages) | `read`, `create`, `update`, `delete`, `publish`, `update.own`, `delete.own` |
| `Single types` | same as Collection types |
| `Media` | `read`, `create`, `update`, `delete`, `update.own`, `delete.own` |
| `Users` | `read`, `create`, `update`, `delete` |
| `Roles` | `read`, `create`, `update`, `delete` |
| `Settings` | `read`, `update` |
| `API` (API tokens) | `read`, `create`, `delete` |
| `Webhooks` | `read`, `create`, `update`, `delete` |
| `Plugins` | `read`, `manage` |
| `Extensions` | `read`, `install`, `configure` |
| `Audit logs` | `read` |
| `Updates` | `read`, `install` |

`Single types`, `Webhooks`, `Plugins`, `Extensions` and `Updates` grants are stored and shown but not checked by any
screen yet (no such screens, or gated by role - Plugins). Seeded rows outside the catalog (for example `Media` /
`publish`, which the matrix enumerates) are kept, copied by **Duplicate** and never shown.

## Permission matrix (default grants)

`src/DotNetForge.Shared/Authorization/PermissionMatrix.cs` encodes the spec's default grants. `IsGranted(role, area,
action)`:

| Role | Grants |
| --- | --- |
| `Super Admin` | everything |
| `Admin` | Collection types / Single types / Media: `read, create, update, delete, publish, update.own, delete.own`; Users, Roles, Settings, API, Webhooks, Plugins: every action; Audit logs and Extensions: `read` only; Updates: nothing |
| `Editor` | Collection types / Single types / Media: `read, create, update, delete, publish, update.own, delete.own` |
| `Author` | Collection types / Single types / Media: `read, create, update.own, delete.own` |
| `Authenticated`, `Public` | Collection types / Single types / Media: `read` |
| any other name | nothing |

`GrantsFor(role)` enumerates these over `PermissionAreas.All` × ten actions; `DataSeeder` writes them as the
`RolePermission` rows that [evaluation](#how-permissions-are-evaluated) reads. Unit tests:
`tests/DotNetForge.Tests/PermissionTests.cs`; enforcement tests: `SecurityTests` (Author content rules, Editors
denied Settings), `MediaTests` (Author cannot delete others' media) and `UserAndRoleManagementTests` (custom roles,
grant edits, safety rules) in `tests/DotNetForge.IntegrationTests`.

## API permission keys (enforced)

`PermissionKeys.All` (25 keys): `core.read`, `core.manage`, `content.read|create|update|delete|publish`,
`media.read|upload|update|delete`, `users.read|create|update|delete`, `roles.read|create|update|delete`,
`settings.read|update`, `extensions.read|manage`, `webhooks.read|manage`.

`RequireApiPermissionAttribute` (an `IAuthorizationFilter`) checks for a `dnf:permission` claim equal
(case-insensitive) to the required key: unauthenticated → 401, missing → 403 with
`{ "error": "Missing required permission '<key>'." }`. Token keys are chosen freely by a Super Admin/Admin at
creation; they are **not** limited by the creator's own role. Endpoint → key map:
[headless API](headless-api.md#endpoints).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- **Enforce `(area, action)` on every action.** Every protected admin and API action is checked against the
  principal's effective permissions - the same model for admin screens and the headless API (API token scoping:
  [headless API](headless-api.md)). Today: ✔ API permission keys, ✔ `(area, action)` for Content Manager, Media,
  Users, Roles, Settings, API tokens and Audit logs; Plugins is gated by role; Single types, Webhooks, Extensions and
  Updates have no screens that check them.
- **Effective permissions** = union of all assigned roles (most permissive wins). No per-user deny override; restrict
  by changing role assignments. ✔ `PermissionService.HasAny`.
- ✔ **Custom roles** next to the six built-in ones, evaluated from their stored `RolePermission` rows
  ([Roles](../pages/roles.md)).
- ✔ **User management** of admin users ([Users](../pages/users.md)).
- ✔ **Built-in roles** cannot be deleted; their descriptions are editable; their grants are editable except for
  `Super Admin` (always full access) and `Public` (always read-only public content).
- **Tenant scoping:** roles and users belong to one tenant (✔ `Role.TenantId`, `User.TenantId`); the matrix defaults
  apply per tenant; only a global Super Admin spans all tenants - see [multi-tenancy](multi-tenancy.md).
- The role given to newly registered users is configurable:
  [authentication → Advanced settings screen](authentication.md#advanced-settings-screen).
- Extension-requested permission keys are reconciled against this model ([extensions](extensions.md#planned-not-implemented)).

### Built-in roles (target descriptions)

| Role | Target capability |
| --- | --- |
| `Super Admin` | Everything in every tenant, including installing updates and managing other Super Admins. Grants can never be reduced. |
| `Admin` | Content, media, users, roles, settings, extensions, API tokens and webhooks within its tenant. No core updates, no Super Admin management. |
| `Editor` | Creates, edits and publishes all content and media in its tenant. No users, roles, settings, extensions or system configuration. |
| `Author` | Creates and edits its own entries and media; cannot publish others' content or manage users, roles or settings. |
| `Authenticated` | Default role of a signed-in end user: content permitted to authenticated users and account-level features; no admin capabilities. |
| `Public` | Implicit role of anonymous visitors: read-only public content; can never gain write or admin grants. |

`DataSeeder.RoleDefinitions` ✔ seeds condensed versions of these descriptions. The spec's `Admin` description lists
"extensions", but its own matrix (below) reserves **Manage extensions** for Super Admin; the seeded description
omits extensions.

### Role and user fields

| Entity | Field | Rule | Today |
| --- | --- | --- | --- |
| Role | Name | required, unique within the tenant; built-in names cannot change | ✔ column (max 100), unique index `(TenantId, Name)` |
| Role | Description | optional | ✔ column (max 500) |
| Role | Permissions | required set of `(area, action)` grants; may be empty | ✔ `RolePermission`, unique `(RoleId, Area, Action)` |
| Role | Number of users | computed, read-only | ✔ shown on the [Roles screen](../pages/roles.md) |
| User | First name, Last name | optional | ✔ columns (max 100) |
| User | Email | required, valid format, unique within the tenant; login identifier | ✔ unique index `(TenantId, Email)`; format and case-insensitive uniqueness checked by setup and `UserManagementService` |
| User | Password | required on create; must pass `PasswordPolicy`; stored only as a hash; never returned or logged | ✔ `PasswordHash` via `IPasswordHasher`; policy applied by setup and `UserManagementService` |
| User | Roles | at least one | ✔ `UserRoles`; minimum enforced by `UserManagementService` |
| User | Status | `Enabled` / `Disabled`; disabled users cannot sign in | ✔ `UserStatus`, checked by `AuthService` |
| User | Created date, Last login date | computed, read-only; last login null until first sign-in | ✔ `CreatedDate`, `LastLoginDate` |

### Canonical permission matrix (spec)

Default capabilities of the built-in roles. **Own** = limited to records the user created; **Read** = read-only.
Defaults are editable for `Admin`, `Editor`, `Author` and `Authenticated`.

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Create content | Yes | Yes | Yes | Yes (Own) | No | No |
| Read content | Yes | Yes | Yes | Yes | Read (permitted) | Read (public only) |
| Update content | Yes | Yes | Yes | Yes (Own) | No | No |
| Delete content | Yes | Yes | Yes | Yes (Own) | No | No |
| Publish content | Yes | Yes | Yes | No | No | No |
| Manage media | Yes | Yes | Yes | Yes (Own) | No | Read (public only) |
| Manage users | Yes | Yes | No | No | No | No |
| Manage roles | Yes | Yes | No | No | No | No |
| Manage settings | Yes | Yes | No | No | No | No |
| Manage extensions | Yes | No | No | No | No | No |
| Manage API tokens | Yes | Yes | No | No | No | No |
| Manage webhooks | Yes | Yes | No | No | No | No |
| View audit logs | Yes | Yes | No | No | No | No |
| Install updates | Yes | No | No | No | No | No |

Module-level actions owned by this model:

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Create / edit / delete roles | Yes | Yes | No | No | No | No |
| Assign permissions to roles | Yes | Yes | No | No | No | No |
| Manage the Super Admin role and Super Admin users | Yes | No | No | No | No | No |
| List / create / edit users | Yes | Yes | No | No | No | No |
| Assign roles to users | Yes | Yes | No | No | No | No |
| Disable / delete users | Yes | Yes | No | No | No | No |
| Edit own profile | Yes | Yes | Yes | Yes | Yes | No |

Differences between this matrix and `PermissionMatrix` (the reference data above):

| Topic | Spec | `PermissionMatrix` |
| --- | --- | --- |
| Plugins | Manage extensions: Super Admin only (and the enforced Plugins screen is Super Admin only) | `Admin` gets **every** action on `Plugins`; `Extensions`: `read` only |
| Read scope | `Authenticated` reads *permitted* content, `Public` *public-only* content | plain `read` on Collection types / Single types / Media; no visibility distinction |
| Media for `Authenticated` | No media capability | `read` on `Media` |
| "Own" records | Author limited to records it created | ✔ `update.own` / `delete.own` checked against `Page.CreatedById` and `MediaFile.UploadedById` (`CanModify`) |
| Edit own profile | every role except `Public` | no area/action for it |
| Areas | ten canonical areas | twelve (`PermissionAreas.All` adds `Audit logs`, `Updates`) and ten actions incl. `manage`, `configure`, `install` |

Everything else (Super Admin all; Admin users/roles/settings/API/webhooks; Admin audit read; updates Super Admin
only; Editor publish; Author no publish) matches.

### Rules and validation

The user and role rules are built ([safety rules](#safety-rules-for-users-and-roles), [Users](../pages/users.md),
[Roles](../pages/roles.md)). Still planned:

- The "one account per email address" setting extending email uniqueness across providers
  ([authentication](authentication.md#advanced-settings-screen)).

### Edge cases

- **Concurrent edits** of the same role or the same user's roles: optimistic concurrency - the second save is rejected
  with a conflict notice and a reload, so invariants (last Super Admin) are re-evaluated against current state. No
  concurrency token exists on `Role` or `User` today.
- **Role or status change of a signed-in user** must take effect on the next request. Status ✔ within 30 s
  (disabled or deleted users are signed out - [session re-validation](authentication.md#session-re-validation));
  role claims stay fixed until the next sign-in (grant changes ✔ apply on the next request).
- Screen-specific cases (deleting a role with users, self-disable, conflicting roles, lowering own permissions):
  [Users](../pages/users.md#edge-cases), [Roles](../pages/roles.md#edge-cases).

### Acceptance criteria

Screen-specific criteria are listed on the [Users](../pages/users.md#acceptance-criteria) and
[Roles](../pages/roles.md#acceptance-criteria) screens.

- [x] The six default roles exist out of the box with descriptions (`DataSeeder.RoleDefinitions`; condensed wording).
- [x] A role exposes Name, Description, Permissions and a read-only number of users (`Role`, `RolesController.RoleListItem`).
- [x] Permissions can be assigned across all ten permission areas (`PermissionCatalog`, [Roles](../pages/roles.md); Single types, Webhooks, Plugins, Extensions and Updates grants are stored but not yet checked).
- [x] A user record exposes First name, Last name, Email, Password (hashed), Roles, Status, Created date and Last login date (`User`).
- [x] Email is validated for format and uniqueness; duplicates are rejected (`UserManagementService`, case-insensitive; unique index `(TenantId, Email)`).
- [x] Passwords are validated against the password policy, stored only as hashes, never returned or logged (`PasswordPolicy`, `Pbkdf2PasswordHasher`).
- [x] A user's effective permissions equal the union of all assigned roles (`PermissionService.HasAny` over all role claims and their stored grants).
- [ ] The matrix defaults are enforced: Super Admin everything; Public read-only public content; Author only own content; Editor publishes but cannot manage users/roles/settings (✔ Author own content and no publish in `ContentController` / `MediaController`; ✔ Editor publishes and is denied Users/Roles/Settings; Public/visibility rules and the other areas not enforced).
- [x] Disabling, deleting or removing the Super Admin role from the last enabled Super Admin is blocked (`UserManagementService`).
- [ ] Concurrent edits to the same role/user use optimistic concurrency with a conflict notice.
- [ ] Roles and users are tenant-scoped and only the global Super Admin spans tenants.

## Where to change things

- Gate a new admin screen: derive from `AdminControllerBase` (gives `AdminArea`) and add
  `[RequirePermission(PermissionAreas.X, PermissionActions.Read)]`; gate its mutating actions with another
  `[RequirePermission(...)]` (or `Can` when the rule depends on the record). Use the constants, never literals.
- Gate an admin action by permission: `if (!Can(PermissionAreas.X, PermissionActions.Y)) return Forbid();`, or
  `CanModify(area, PermissionActions.Update, PermissionActions.UpdateOwn, record.CreatedById)` for records with an
  owner. Load the record tenant-scoped first (404 before 403). Follow `ContentController` / `MediaController`.
- Hide what the user cannot do: compute `Can...` flags into the view model (see `MediaIndexViewModel`); a new
  screen's sidebar link in `AdminNavigation.Tree` is hidden automatically from users its controller refuses.
- New grantable pair: add it to `PermissionCatalog` (and to `PermissionMatrix` if a built-in role should get it by
  default - existing tenants keep their stored grants, so a new default needs a data migration or a manual grant).
  Update the catalog and matrix tables above.
- Gate a new API action: `[RequireApiPermission(PermissionKeys.X)]`; add new keys to `PermissionKeys.All`.
