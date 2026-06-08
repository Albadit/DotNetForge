# User Roles & Permissions

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This module defines the role-based access control (RBAC) system for DotNetForge CMS: the default roles, the permission areas they govern, the management of roles and admin users, and the canonical permission matrix that every other module references when enforcing access. It is the central permissions reference for the entire CMS.

## Purpose

DotNetForge CMS uses RBAC to control who may perform which action across the admin UI, the public frontend, and the API. This module specifies how roles are defined and managed, which permission areas exist, how admin users are created and assigned roles, and the exact default permission baseline for the six built-in roles.

Every protected action in the CMS resolves a permission check against the acting principal's effective permissions (the union of all assigned roles). This applies identically to admin UI actions and API requests; API enforcement and token scoping are detailed in [API Tokens](api_tokens.md) and [Security](security.md). In multi-tenant deployments, roles and users are tenant-scoped except for the global Super Admin - see [Multi-Tenancy](multi_tenancy.md).

## Main Features

- **Role-based access control (RBAC)** over all admin, frontend, and API actions.
- **Six default roles**, ordered most-privileged to least: Super Admin, Admin, Editor, Author, Authenticated, Public.
- **Role management**: create, edit, duplicate, delete roles; assign users to roles; assign permissions to roles.
- **Granular permission areas**: Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks.
- **User management** (admin users): list, create, edit, disable, delete, assign roles, view last login, view account status.
- **A canonical [Permission Matrix](#permission-matrix)** that defines sensible default capabilities per role.
- **Safety invariants**: at least one enabled Super Admin must always exist; conflicting and self-destructive role/permission changes are blocked.
- **Tenant scoping**: roles and users are scoped to a tenant; the global Super Admin spans all tenants. See [Multi-Tenancy](multi_tenancy.md).

## Default Roles

The CMS ships with six built-in roles. They cannot be deleted, but their descriptions and (except for Super Admin and Public) their permissions may be edited. Custom roles may be created in addition to these.

| Role | Privilege | Description |
| --- | --- | --- |
| **Super Admin** | Highest | Full, unrestricted access to every permission area, every tenant, and all system functions including installing updates and managing other Super Admins. Permissions cannot be reduced below full access. |
| **Admin** | High | Manages content, media, users, roles, settings, extensions, API tokens, and webhooks within their tenant. Cannot install core updates or manage Super Admins by default. |
| **Editor** | Medium | Creates, edits, and publishes all content and media within their tenant. Cannot manage users, roles, settings, extensions, or system configuration. |
| **Author** | Low | Can manage the content they have created. Authors create and edit their own entries and media but cannot publish others' content or manage users, roles, or settings. |
| **Authenticated** | Minimal | Default role for any signed-in end user. Can read content permitted to authenticated users and access account-level features. No admin capabilities by default. |
| **Public** | None (anonymous) | The implicit role for unauthenticated visitors. Read-only access to public content only. No admin or write capabilities. |

The default role assigned to newly registered authenticated users is configurable in Advanced User Settings - see [Authentication & Providers](authentication.md).

### Example role

```text
Author
Authors can manage the content they have created.
```

## Data Model / Fields

### Role fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| Name | string | Yes | Unique role name (unique within the tenant). |
| Description | string | No | Human-readable explanation of the role's purpose. |
| Permissions | permission set | Yes | The set of granted permissions across all permission areas. May be empty for a minimally-scoped role. |
| Number of users with this role | integer (computed) | Read-only | Count of users currently assigned this role. Displayed in the roles list; not directly editable. |

### User fields

Admin users (users who can access the admin panel) are managed on the Users page. End-user/account validation rules for sign-up live in [Authentication & Providers](authentication.md).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| First name | string | No | User's given name. |
| Last name | string | No | User's family name. |
| Email | string (email) | Yes | Login identifier. Must be unique (see Validation Rules). |
| Password | string (write-only) | Yes on create | Stored only as a secure hash; never returned or logged. See [Security](security.md). |
| Roles | role set | Yes | One or more assigned roles. Effective permissions are the union of all assigned roles. |
| Status | enum: Enabled / Disabled | Yes | Account status. Disabled users cannot sign in. |
| Created date | datetime (computed) | Read-only | When the user account was created. |
| Last login date | datetime (computed) | Read-only | Timestamp of the user's most recent successful sign-in; null if never logged in. |

## Permission Areas

Permissions are organized into the following areas. Each area exposes granular actions (e.g. create, read, update, delete, publish, configure) that are assigned to roles.

| Area | Governs |
| --- | --- |
| **Collection types** | Multi-entry content models and their entries (CRUD + publish). |
| **Single types** | Single-instance content models (e.g. homepage, global settings content). |
| **Plugins** | Enabling, disabling, configuring, updating, and removing plugins. See [Plugins](extensions.md). |
| **Settings** | Global and administration settings (overview, internationalization, transfer, email, etc.). |
| **Extensions** | Installing, updating, enabling, disabling, and removing extensions. See [Extensions](extensions.md). |
| **Media** | Uploading, organizing, renaming, deleting, and access-controlling media. See [File Manager](file_manager.md). |
| **Users** | Listing, creating, editing, disabling, deleting users and assigning roles (this module). |
| **Roles** | Creating, editing, duplicating, deleting roles and assigning permissions (this module). |
| **API** | Managing API tokens and API access scopes. See [API Tokens](api_tokens.md). |
| **Webhooks** | Creating, editing, enabling, disabling, and deleting webhooks. See [Webhooks](webhooks.md). |

## Role Actions

Users with the **Roles** permission can perform the following on the Roles page:

- **Create** a new custom role with a name, description, and selected permissions.
- **Edit** a role's name, description, and permissions (built-in role names cannot be changed).
- **Duplicate** an existing role to seed a new role with the same permission set.
- **Delete** a custom role (subject to the edge-case rules below).
- **Assign users** to a role.
- **Assign permissions** to a role across the permission areas.

## Users Page

The Users page lists all users who have access to the admin panel and supports the following actions for users with the **Users** permission:

- **List** users with their roles, status, and last login.
- **Create** a user (set name, email, password, roles, status).
- **Edit** a user's details and role assignments.
- **Disable** a user (blocks sign-in without deleting the account).
- **Delete** a user.
- **Assign roles** to a user.
- **View last login** date.
- **View account status** (Enabled / Disabled).

## User Flows

### Flow: Create a role

1. Navigate to **Settings → Administration → Roles** and select **Create role**.
2. Enter a **Name** (unique within the tenant) and an optional **Description**.
3. Select the **Permissions** for each permission area (Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks).
4. Save. The system validates the name is unique and the permission set is well-formed, then persists the role scoped to the active tenant.
5. The new role appears in the roles list with a "Number of users with this role" count of 0.

### Flow: Duplicate a role

1. From the roles list, choose **Duplicate** on an existing role.
2. The system creates a new role pre-filled with the source role's permissions and a derived name (e.g. "Editor (copy)").
3. Edit the name, description, and permissions as needed, then save.

### Flow: Assign permissions to a role

1. Open a role and go to its **Permissions** tab.
2. Toggle the granular actions per permission area.
3. Save. The system rejects the change if it would reduce the current actor's own ability to manage roles below a safe threshold, or if it would leave no enabled Super Admin (see Edge Cases).

### Flow: Delete a role

1. From the roles list, select **Delete** on a custom role.
2. If the role still has users assigned, the system blocks deletion and prompts the actor to reassign those users to another role first (see Edge Cases).
3. Built-in roles cannot be deleted; the action is unavailable for them.
4. Confirm. The role is removed.

### Flow: Create a user

1. Navigate to **Settings → Administration → Users** and select **Create user**.
2. Enter **First name** (optional), **Last name** (optional), **Email** (required, unique), and a **Password** that satisfies the password policy.
3. Select one or more **Roles** and set **Status** (Enabled by default).
4. Save. The system validates email uniqueness and password policy, stores the password as a hash, and records the **Created date**.

### Flow: Assign roles to a user

1. Open a user and go to the **Roles** field.
2. Add or remove roles. The user's effective permissions become the union of all assigned roles.
3. Save. The system blocks the change if it would remove the last enabled Super Admin, or if it produces a disallowed conflicting assignment (see Edge Cases).

### Flow: Disable a user

1. Open a user and set **Status** to **Disabled**, or use the **Disable** action in the list.
2. The system blocks the action if the user is the last enabled Super Admin, or if the actor is attempting to disable their own account (see Edge Cases).
3. Save. A disabled user can no longer sign in; existing sessions are invalidated per [Security](security.md).

### Flow: Delete a user

1. From the users list, select **Delete**.
2. The system blocks deletion if the target is the last enabled Super Admin.
3. Confirm. The user account is removed. Audit log entries authored by the user are retained (see [Audit Logs](audit_logs.md)).

## Role & Permission Rules

The following table states which canonical roles may perform the core actions owned by this module. "Own" means limited to records the user created.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Create / edit / delete roles | Yes | Yes | No | No | No | No |
| Assign permissions to roles | Yes | Yes | No | No | No | No |
| Manage Super Admin role / Super Admin users | Yes | No | No | No | No | No |
| List / create / edit users | Yes | Yes | No | No | No | No |
| Assign roles to users | Yes | Yes | No | No | No | No |
| Disable / delete users | Yes | Yes | No | No | No | No |
| Edit own profile | Yes | Yes | Yes | Yes | Yes | No |

For the full default capabilities across every module, see the canonical [Permission Matrix](#permission-matrix) below.

## Permission Matrix

The matrix below defines the **default** capabilities for each built-in role. Cells: **Yes** = granted, **No** = denied, **Own** = limited to records created by the user, **Read** = read-only. These defaults are editable for Admin, Editor, Author, and Authenticated; Super Admin is always full access and Public is always read-only public content.

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

Notes:

- **Super Admin** has every capability and cannot have any capability removed.
- **Public** is read-only and limited to content explicitly marked public; it can never be granted write or admin capabilities.
- Module-specific detail and the enforcement of each capability live in the owning files: [Content Manager](content_manager.md), [File Manager](file_manager.md), [Extensions](extensions.md), [API Tokens](api_tokens.md), [Webhooks](webhooks.md), [Audit Logs](audit_logs.md), and update/rollback in [Updates & Rollback](transfer_updates.md).
- In multi-tenant mode these defaults apply **per tenant**; only the global Super Admin crosses tenant boundaries. See [Multi-Tenancy](multi_tenancy.md).

## Validation Rules

- **Email uniqueness**: Each user's email must be unique. In multi-tenant deployments, uniqueness is enforced within the tenant scope; the "one account per email address" advanced setting may additionally prevent duplicate accounts across providers (see [Authentication & Providers](authentication.md)).
- **Email format**: Email must be a valid, well-formed email address. Required on every user.
- **Password policy**: Password is required on create. It must satisfy the configured complexity policy (minimum length and character requirements) and is stored only as a secure hash - never in plaintext, never returned by the API, never written to logs. See [Security](security.md).
- **Required user fields**: Email, Password (on create), at least one Role, and Status are required. First name and Last name are optional.
- **Role name uniqueness**: A role's Name must be unique within the tenant.
- **Required role fields**: Name and a Permissions set are required; Description is optional.
- **At least one enabled Super Admin**: The system must always have at least one enabled Super Admin. Any operation that would violate this is rejected (see Edge Cases).
- **Built-in roles**: Built-in role names cannot be renamed and built-in roles cannot be deleted. Super Admin permissions cannot be reduced; Public permissions cannot be expanded beyond read-only public content.
- **Effective permissions**: A user's effective permissions are the union of all assigned roles. There is no per-user "deny" override; restrict by adjusting role assignments.
- **Status values**: Status must be one of Enabled or Disabled.

## Edge Cases

- **Deleting a role that still has users**: Blocked. The system prevents deletion until all assigned users are reassigned to another role, and shows the affected user count. Optionally the UI may offer bulk reassignment as part of the delete flow.
- **Removing or disabling the last Super Admin**: Blocked. Disabling, deleting, or removing the Super Admin role from the only remaining enabled Super Admin is rejected with a clear error. At least one enabled Super Admin must always exist.
- **A user disabling their own account**: Blocked. Users cannot disable or delete their own account through the Users page, preventing accidental self-lockout. An administrator must perform the action on another's account.
- **Lowering your own permissions**: A user editing roles must not remove their own ability to manage roles/users in a way that locks them (and potentially everyone) out. The system warns and blocks changes that would strip the actor's Roles/Users management permission when doing so risks leaving the tenant without an administrator; removing one's own Super Admin role is governed by the last-Super-Admin rule.
- **Assigning conflicting roles**: When a user is assigned multiple roles, effective permissions are the union (most permissive wins). The system flags semantically conflicting assignments (e.g. a role intended to restrict combined with one that grants the same area) and surfaces the resulting effective permissions so the assignment is unambiguous.
- **Concurrent role/permission edits (race condition)**: If two administrators edit the same role or the same user's roles simultaneously, the system uses optimistic concurrency; the second save is rejected with a conflict notice prompting a reload, so safety invariants (e.g. last Super Admin) are re-evaluated against current state.
- **Disabling a user with active sessions**: Disabling a user invalidates their active sessions and API access derived from session auth on next request. Standalone API tokens are governed separately - see [API Tokens](api_tokens.md).
- **Deleting a role assigned via tenant scope**: In multi-tenant mode, a role can only be deleted within its tenant; cross-tenant references are validated before deletion. See [Multi-Tenancy](multi_tenancy.md).
- **Duplicate email on create/edit**: Rejected with a uniqueness validation error; no partial user is created.

## Acceptance Criteria

- [ ] The six default roles (Super Admin, Admin, Editor, Author, Authenticated, Public) exist out of the box with the documented descriptions.
- [ ] A role exposes Name, Description, Permissions, and a read-only "Number of users with this role" count.
- [ ] Users with the Roles permission can create, edit, duplicate, and delete roles, assign users, and assign permissions.
- [ ] Built-in roles cannot be deleted and built-in role names cannot be renamed.
- [ ] Permissions can be assigned across all ten permission areas: Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks.
- [ ] The Users page lists users and supports create, edit, disable, delete, assign roles, view last login, and view account status.
- [ ] A user record exposes First name, Last name, Email, Password (hashed), Roles, Status, Created date, and Last login date.
- [ ] Email is validated for format and uniqueness; duplicate emails are rejected.
- [ ] Passwords are validated against the password policy, stored only as hashes, and never returned or logged.
- [ ] The effective permissions of a user equal the union of all assigned roles.
- [ ] The Permission Matrix defaults are enforced: Super Admin has all capabilities; Public is read-only public content; Author is limited to their own content; Editor can publish but not manage users/roles/settings.
- [ ] Deleting a role that still has users assigned is blocked until those users are reassigned.
- [ ] Disabling, deleting, or removing the Super Admin role from the last enabled Super Admin is blocked.
- [ ] A user cannot disable or delete their own account.
- [ ] Changes that would strip the actor's own role/user management in an unsafe way are blocked or warned.
- [ ] Conflicting multi-role assignments resolve to the union of permissions and display the resulting effective permissions.
- [ ] Concurrent edits to the same role/user are handled with optimistic concurrency and a conflict notice.
- [ ] Roles and users are tenant-scoped, and only the global Super Admin spans tenants (see Multi-Tenancy).
