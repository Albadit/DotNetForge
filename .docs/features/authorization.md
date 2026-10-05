# Authorization

What a signed-in user or an API token may do. There are three separate mechanisms, applied in this order for an
admin request: role checks gate the screen, then the permission matrix gates individual actions where it is used.

| Mechanism | Vocabulary | Enforced where | Status |
| --- | --- | --- | --- |
| **Role checks** (admin) | role names (`Roles.*`) | `[Authorize(Policy = "AdminArea")]` and `[Authorize(Roles = ...)]` on admin controllers | **enforced** |
| **Permission matrix** | `(area, action)` (`PermissionAreas`, `PermissionActions`) | `IPermissionService` / `PermissionMatrix` via `AdminControllerBase.Can` / `CanModify`; seeded as `RolePermission` rows (not read) | **partly enforced** - Content Manager (`Collection types`) and Media (`Media`) only |
| **API permission keys** | dotted keys (`PermissionKeys`, e.g. `content.read`) | `[RequireApiPermission(...)]` on every API action | **enforced** |

## Roles

`src/DotNetForge.Shared/Constants/Roles.cs` - most to least privileged:

| Role | `Role.IsBuiltIn` (seeded) | Admin-capable (`Roles.AdminCapable`) | Can sign in to the admin |
| --- | --- | --- | --- |
| `Super Admin` | yes | yes | yes |
| `Admin` | yes | yes | yes |
| `Editor` | yes | yes | yes |
| `Author` | yes | yes | yes |
| `Authenticated` | yes | no | signs in, but every admin screen redirects to denied |
| `Public` | yes | no | - (anonymous visitors have no role claim) |

Only the setup wizard assigns a role (Super Admin). There is no UI or API to assign roles, create users or create
custom roles.

## Admin screen matrix (enforced)

The `AdminArea` policy (`DependencyRegistration.AdminAreaPolicy`) requires one of `Roles.AdminCapable` (`Super Admin`,
`Admin`, `Editor`, `Author`). Controller-level `[Authorize(Roles = ...)]` narrows further; both must pass. Inside
`ContentController` and `MediaController`, each mutating action then checks the permission matrix (column **Check**).

| Screen / endpoint | Check | Super Admin | Admin | Editor | Author |
| --- | --- | :-: | :-: | :-: | :-: |
| Dashboard, Content Manager (view), Media (view), admin extension tabs, module placeholders | role | ✔ | ✔ | ✔ | ✔ |
| Content: create page `POST /admin/content/create` | `Collection types` / `create` | ✔ | ✔ | ✔ | ✔ |
| Content: update page `POST /admin/content/update/{id}` | `update`, or `update.own` on own pages | ✔ | ✔ | ✔ | own |
| Content: change **Published** or the schedule dates (in update) | + `publish` | ✔ | ✔ | ✔ | ✘ |
| Content: delete page `POST /admin/content/delete/{id}` | `delete`, or `delete.own` on own pages | ✔ | ✔ | ✔ | own |
| Content: reorder `POST /admin/content/reorder` | `update` | ✔ | ✔ | ✔ | ✘ |
| Media: upload `POST /admin/media/upload` | `Media` / `create` | ✔ | ✔ | ✔ | ✔ |
| Media: delete `POST /admin/media/delete/{id}` | `delete`, or `delete.own` on own files | ✔ | ✔ | ✔ | own |
| Settings (incl. save), API Tokens, Roles, Users, Audit Logs | role | ✔ | ✔ | ✘ | ✘ |
| Plugins | role | ✔ | ✘ | ✘ | ✘ |

- **Own** = the record's creator is the signed-in user: `Page.CreatedById` (set by `ContentController.Create`) or
  `MediaFile.UploadedById`. Seeded pages have no `CreatedById`, so an `Author` cannot edit or delete them.
- An `Author` therefore creates pages (unpublished), edits and deletes only its own, and cannot publish, unpublish,
  schedule or reorder. A blocked publish change is a form error: "You don't have permission to publish, unpublish or
  schedule pages."
- Media's upload panel and **Delete** buttons are hidden when not allowed (`MediaIndexViewModel.CanUpload`,
  `MediaRowViewModel.CanDelete`); the Content Manager shows every button and relies on the server check.
- Settings is Super Admin / Admin only (`[Authorize(Roles = ...)]` on `SettingsController`); Editors and Authors were
  allowed before.
- Private media downloads (`GET /media/{id}`) are allowed to any admin-capable role of the file's tenant
  ([media storage](media-storage.md#download-flow)).

Missing role → cookie forbid → `302 /account/denied` ([screen](../pages/access-denied.md)); a failed `Can`/`CanModify`
returns `Forbid()`, which lands on the same screen. The sidebar does not hide links the user cannot open.

### `AdminControllerBase` helpers

| Member | Meaning |
| --- | --- |
| `TenantId` | `dnf:tenant` claim (or `Guid.Empty`) |
| `CurrentUserId` | `NameIdentifier` claim as `Guid?` |
| `Can(area, action)` | `IPermissionService.HasAny(role claims, area, action)` - union over the user's roles |
| `CanModify(area, anyAction, ownAction, createdById)` | `Can(area, anyAction)` or (`createdById == CurrentUserId` and `Can(area, ownAction)`) |

## Permission matrix (reference data)

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

`GrantsFor(role)` enumerates these over `PermissionAreas.All` × ten actions; `DataSeeder` writes them as
`RolePermission` rows (shown as the **Permissions** count on the [Roles screen](../pages/roles.md)).
`PermissionService.Has`/`HasAny` evaluate the static matrix, **not** the stored rows. Unit tests:
`tests/DotNetForge.Tests/PermissionTests.cs`; enforcement tests: `SecurityTests` (Author content rules, Editors
denied Settings) and `MediaTests` (Author cannot delete others' media) in `tests/DotNetForge.IntegrationTests`.

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
  [headless API](headless-api.md)). Today: ✔ role checks, ✔ API permission keys, ✔ `(area, action)` in the Content
  Manager (`Collection types`) and Media (`Media`); the other eight areas are not checked.
- **Effective permissions** = union of all assigned roles (most permissive wins). No per-user deny override; restrict
  by changing role assignments. ✔ `PermissionService.HasAny`, called through `AdminControllerBase.Can`.
- **Custom roles** next to the six built-in ones, evaluated from their stored `RolePermission` rows
  (`PermissionMatrix` grants nothing to unknown role names ✔). Screen: [Roles](../pages/roles.md#planned-not-implemented).
- **User management** of admin users. Screen: [Users](../pages/users.md#planned-not-implemented).
- **Built-in roles** cannot be deleted; their descriptions are editable; their grants are editable except for
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
| User | Email | required, valid format, unique within the tenant; login identifier | ✔ unique index `(TenantId, Email)`; format checked only by setup (`EmailValidator`) |
| User | Password | required on create; must pass `PasswordPolicy`; stored only as a hash; never returned or logged | ✔ `PasswordHash` via `IPasswordHasher`; policy applied only by setup |
| User | Roles | at least one | ✔ `UserRoles`; minimum not enforced |
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

- At least one **enabled** Super Admin must always exist; any operation that would break this is rejected.
- Only a Super Admin may manage the Super Admin role or Super Admin users.
- `Super Admin` grants cannot be reduced; `Public` grants cannot exceed read-only public content.
- Required user fields: Email, Password (on create), at least one role, Status. Required role fields: Name and a
  permission set.
- Email: valid format and unique within the tenant (the "one account per email address" setting can extend this
  across providers - [authentication](authentication.md#advanced-settings-screen)).
- Password: `PasswordPolicy`; stored only as a hash; never returned by the API, never logged ([security](security.md)).
- Status must be `Enabled` or `Disabled`.
- Role and permission changes are audited as `role.changed` / `permission.changed` ([audit logging](audit-logging.md#planned-not-implemented)).

### Edge cases

- **Concurrent edits** of the same role or the same user's roles: optimistic concurrency - the second save is rejected
  with a conflict notice and a reload, so invariants (last Super Admin) are re-evaluated against current state. No
  concurrency token exists on `Role` or `User` today.
- **Role or status change of a signed-in user** must take effect on the next request. Status ✔ within 30 s
  (disabled or deleted users are signed out - [session re-validation](authentication.md#session-re-validation));
  role claims stay fixed until the next sign-in.
- Screen-specific cases (deleting a role with users, self-disable, conflicting roles, lowering own permissions):
  [Users](../pages/users.md#edge-cases), [Roles](../pages/roles.md#edge-cases).

### Acceptance criteria

Screen-specific criteria are listed on the [Users](../pages/users.md#acceptance-criteria) and
[Roles](../pages/roles.md#acceptance-criteria) screens.

- [x] The six default roles exist out of the box with descriptions (`DataSeeder.RoleDefinitions`; condensed wording).
- [x] A role exposes Name, Description, Permissions and a read-only number of users (`Role`, `RolesController.RoleListItem`).
- [ ] Permissions can be assigned across all ten permission areas.
- [x] A user record exposes First name, Last name, Email, Password (hashed), Roles, Status, Created date and Last login date (`User`).
- [ ] Email is validated for format and uniqueness; duplicates are rejected (unique index ✔; no user creation outside setup).
- [ ] Passwords are validated against the password policy, stored only as hashes, never returned or logged (hashing ✔ `Pbkdf2PasswordHasher`; policy only in setup).
- [x] A user's effective permissions equal the union of all assigned roles (`PermissionService.HasAny` over all role claims, via `AdminControllerBase.Can`).
- [ ] The matrix defaults are enforced: Super Admin everything; Public read-only public content; Author only own content; Editor publishes but cannot manage users/roles/settings (✔ Author own content and no publish in `ContentController` / `MediaController`; ✔ Editor publishes and is denied Users/Roles/Settings; Public/visibility rules and the other areas not enforced).
- [ ] Disabling, deleting or removing the Super Admin role from the last enabled Super Admin is blocked.
- [ ] Concurrent edits to the same role/user use optimistic concurrency with a conflict notice.
- [ ] Roles and users are tenant-scoped and only the global Super Admin spans tenants.

## Where to change things

- Gate a new admin screen: derive from `AdminControllerBase` (gives `AdminArea`), add
  `[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]` for admin-only screens. Use the constants, never literals.
- Gate an admin action by permission: `if (!Can(PermissionAreas.X, PermissionActions.Y)) return Forbid();`, or
  `CanModify(area, PermissionActions.Update, PermissionActions.UpdateOwn, record.CreatedById)` for records with an
  owner. Load the record tenant-scoped first (404 before 403). Follow `ContentController` / `MediaController`.
- Hide what the user cannot do: compute `Can...` flags into the view model (see `MediaIndexViewModel`).
- Default grants: `PermissionMatrix` (static). Custom roles would need `PermissionService` to read the stored
  `RolePermission` rows instead. Update this document and the matrix table above.
- Gate a new API action: `[RequireApiPermission(PermissionKeys.X)]`; add new keys to `PermissionKeys.All`.
