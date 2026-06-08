# Global Settings & Overview

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Global Settings module is the central hub for system-wide configuration in DotNetForge CMS. The **Overview** page surfaces version, environment, and health information, lets administrators upload the admin and login logos, and links out to every settings subpage owned by sibling specification files.

## Purpose

The Global Settings module provides administrators with a single, organized entry point into all system-wide configuration. It must:

- Display read-only diagnostics about the running instance (versions, database provider, environment, health, installed extension count, update availability).
- Allow upload of two branding assets: the admin menu logo and the authentication/login logo.
- Act as the navigation **hub** for every settings subpage, each of which is specified in detail in its own sibling file.
- Provide a one-click path to check for CMS updates and review system health.

This file owns **Section 10 (Overview Settings)** and the **Settings group of the admin sidebar (Section 6)**. Detailed behavior for each linked subpage lives in the file that owns it (see [Settings Hub Navigation](#settings-hub-navigation)).

In a multi-tenant deployment, settings are displayed within the active tenant context where applicable; global instance facts (CMS version, C#/ASP.NET Core versions, database provider, environment) are instance-wide and identical across tenants. Tenant resolution happens first, then the settings view is rendered within that tenant's scope. See [Multi-Tenant Routing](multi_tenancy.md).

## Main Features

- **Overview page** with live system diagnostics.
- **Admin menu logo upload** - branding shown in the admin sidebar/header. The admin area is never affected by public frontend themes; this logo is the admin-area branding override. See [Themes](themes.md).
- **Authentication/login logo upload** - branding shown on the login and setup screens.
- **Check for updates** action that links to the update/transfer workflow. See [Transfer & Updates](transfer_updates.md).
- **System health indicator** with status detail.
- **Settings hub** - a navigable table that routes administrators to every settings subpage and the file that owns each one.
- **Marketplace entry** is reachable from the admin **Main** menu group (a sibling of Settings) for browsing and installing extensions. See [Extensions & Plugins](extensions.md).

### Admin Sidebar - Settings Group (Section 6)

The admin sidebar organizes settings into the following groups. The Overview page is the landing page of **Global Settings**.

```text
## Main
- Dashboard
- Content Manager
- File Manager
- Marketplace

## Settings
  ### Global Settings
    - Overview            (this file)
    - API Tokens
    - Content History
    - Internationalization
    - File Manager
    - Plugins
    - Transfer
    - Webhooks
  ### Administration Panel
    - Roles
    - Users
    - Audit Logs
  ### Email
    - Configuration
    - Templates
  ### Users & Permissions Plugin
    - Roles
    - Providers
    - Advanced Settings
```

The admin area (including this sidebar and its logo) must never be affected by public frontend themes. Themes only affect public-facing CMS pages. See [Themes](themes.md).

## Data Model / Fields

### Overview Page Fields (read-only diagnostics)

| Field | Type | Source | Description |
|-------|------|--------|-------------|
| CMS version | string (semver) | Application assembly | The installed DotNetForge CMS core version. |
| Available CMS updates | string / boolean | Update check service | Latest available core version, or "Up to date" when none. Links to [Transfer & Updates](transfer_updates.md). |
| Current C# version | string | Runtime | The C# language version the build targets. |
| Current ASP.NET Core version | string | Runtime | The ASP.NET Core runtime version hosting the app. |
| Database provider | enum (`sqlite` \| `postgresql`) | `.env` (`DATABASE_PROVIDER`) | The active database provider. Configured in `.env`; secrets must never be committed to Git. |
| Environment | string (e.g. `Development`, `Production`) | Hosting environment | The ASP.NET Core environment name. |
| Installed extensions | integer / list | Extension registry | Count (and optional list) of installed extensions. Detail in [Extensions & Plugins](extensions.md). |
| System health | enum (`Healthy` \| `Degraded` \| `Unhealthy`) | Health check service | Aggregate system status with drill-down detail. |
| Admin menu logo | image (upload) | Settings store | Branding image displayed in the admin menu/header. |
| Authentication/login logo | image (upload) | Settings store | Branding image displayed on login and setup pages. |

All version, provider, environment, and health fields are **read-only** on this page. The two logo fields are the only editable inputs.

### Branding Logo Fields (editable)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Admin menu logo | Image file | No | Replaces the default admin sidebar/header logo. Cleared reverts to default. |
| Authentication/login logo | Image file | No | Replaces the default logo on the login and first-time setup screens. Cleared reverts to default. |

Logo storage and URL generation are handled by the media subsystem; allowed types and size limits are enforced per [File Manager](file_manager.md).

### Settings Hub Navigation

The Overview page presents a table linking to every settings subpage and the file that owns its specification.

| Settings subpage | Group | Owned by |
|------------------|-------|----------|
| Overview | Global Settings | this file (`settings.md`) |
| API Tokens | Global Settings | [api_tokens.md](api_tokens.md) |
| Content History | Global Settings | [content_manager.md](content_manager.md) |
| Internationalization | Global Settings | [internationalization.md](internationalization.md) |
| File Manager | Global Settings | [file_manager.md](file_manager.md) |
| Plugins | Global Settings | [extensions.md](extensions.md) |
| Transfer | Global Settings | [transfer_updates.md](transfer_updates.md) |
| Webhooks | Global Settings | [webhooks.md](webhooks.md) |
| Roles | Administration Panel | [user_roles_permissions.md](user_roles_permissions.md) |
| Users | Administration Panel | [user_roles_permissions.md](user_roles_permissions.md) |
| Audit Logs | Administration Panel | [audit_logs.md](audit_logs.md) |
| Email - Configuration | Email | [email.md](email.md) |
| Email - Templates | Email | [email.md](email.md) |
| Users & Permissions - Roles | Users & Permissions Plugin | [user_roles_permissions.md](user_roles_permissions.md) |
| Users & Permissions - Providers | Users & Permissions Plugin | [authentication_providers.md](authentication.md) |
| Users & Permissions - Advanced Settings | Users & Permissions Plugin | [user_roles_permissions.md](user_roles_permissions.md) |

The **Marketplace** entry is not part of the Settings group; it lives in the admin **Main** menu and is specified in [Extensions & Plugins](extensions.md).

## User Flows

### Flow: View overview

1. The user opens **Settings → Global Settings → Overview** from the admin sidebar.
2. The system resolves the active tenant context (if multi-tenant) and verifies the user has `Settings` read permission.
3. The system loads diagnostics: CMS version, available updates, C# version, ASP.NET Core version, database provider (from `.env`), environment, installed extension count, and system health.
4. The system renders the read-only fields, the two current logos (or defaults), the settings hub navigation table, and the **Check for updates** action.
5. The user reviews the values and may navigate to any linked settings subpage.

### Flow: Upload admin menu logo

1. From the Overview page, the user selects a new file for **Admin menu logo**.
2. The client validates the file type and size against the configured allowed image types and max upload size. See [File Manager](file_manager.md).
3. The system re-validates server-side (type, size, and safe-upload checks), then stores the file via the media subsystem and generates its URL.
4. The system saves the logo reference to the settings store and records the change in the audit log. See [Audit Logs](audit_logs.md).
5. The admin sidebar/header immediately reflects the new logo. The public frontend and its themes are unaffected.

### Flow: Upload authentication/login logo

1. From the Overview page, the user selects a new file for **Authentication/login logo**.
2. The system validates type, size, and safe-upload checks (client and server side) per [File Manager](file_manager.md).
3. The system stores the file, saves the reference, and records the change in the audit log.
4. The login and first-time setup screens display the new logo on next render.

### Flow: Remove / reset a logo

1. The user clears the **Admin menu logo** or **Authentication/login logo** field and saves.
2. The system removes the logo reference and reverts the affected surface to the built-in default logo.
3. The change is recorded in the audit log.

### Flow: Check for updates

1. From the Overview page, the user selects **Check for updates** (or follows the **Available CMS updates** link).
2. The system queries the update check service and compares the installed CMS version with the latest available version.
3. If a newer version exists, the system shows the available version and a link to the update workflow.
4. The user is routed to [Transfer & Updates](transfer_updates.md) to perform the update, which creates a backup before applying and supports rollback on failure.

### Flow: Review degraded system health

1. The Overview page shows **System health** as `Degraded` or `Unhealthy`.
2. The user selects the health status to expand the detail of failing checks.
3. The system displays which subsystem(s) reported the failure (for example database connectivity, storage, or a failing extension) so the user can act.

## Role & Permission Rules

Access is governed by the `Settings` permission area within the CMS RBAC model. Default roles, most-privileged to least, are: Super Admin, Admin, Editor, Author, Authenticated, Public. See [User Roles & Permissions](user_roles_permissions.md).

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|--------|:-----------:|:-----:|:------:|:------:|:-------------:|:------:|
| View Overview / Global Settings | Yes | Yes (if granted `Settings` read) | No | No | No | No |
| Upload / change admin menu logo | Yes | Yes (if granted `Settings` write) | No | No | No | No |
| Upload / change login logo | Yes | Yes (if granted `Settings` write) | No | No | No | No |
| Check for updates | Yes | Yes (if granted `Settings` read) | No | No | No | No |
| Apply updates / rollback | Yes | Only if explicitly granted | No | No | No | No |
| Navigate to a settings subpage | Per that subpage's own permission area | Per subpage permission | Per subpage permission | Per subpage permission | No | No |

Rules:

- **Super Admin** always has full access to Global Settings and all subpages, across all tenants in a multi-tenant deployment.
- **Admin** access requires the `Settings` permission; read for viewing, write for changing logos. Admins are scoped to their assigned tenant(s).
- **Editor, Author, Authenticated, and Public** roles must not see or modify Global Settings unless explicitly granted the `Settings` permission area.
- Navigation links in the hub are shown only when the user holds the permission area owned by the target subpage (e.g. the `Webhooks`, `API`, `Media`, `Extensions`, `Users`, or `Roles` area). Permission checks are enforced on every admin action, not merely in the UI.

## Validation Rules

- **Overview diagnostic fields** are read-only and must never be editable through this page.
- **Logo uploads** must be validated against the configurable allowed file types and configurable max upload size enforced by the media subsystem. See [File Manager](file_manager.md).
  - Only image types permitted by the media configuration are accepted (e.g. PNG, JPEG, SVG, WebP per the allowed-types setting). Non-image or disallowed types are rejected.
  - Files exceeding the configured max upload size are rejected before storage.
  - Uploads are subject to safe-upload validation (type/size validation and scanning/blocking of unsafe files where possible).
- Both logo fields are **optional**; clearing a field reverts to the built-in default logo.
- Logo validation is enforced on both client and server; the server-side check is authoritative.
- Secrets referenced by diagnostics (such as the database connection string) must never be displayed; only the non-secret database **provider** value is shown. Secrets live in `.env` and must never be committed to Git. See [Security](security.md).
- A successful change to any global setting (including logos and update actions) must be written to the audit log. See [Audit Logs](audit_logs.md).

## Edge Cases

- **Update available vs. up-to-date display:** When the update check finds a newer version, the Overview must show the available version and a link to [Transfer & Updates](transfer_updates.md). When none is available, it must show an explicit "Up to date" state rather than an empty value.
- **Update check service unreachable:** If the update check fails (network/timeout), the Overview must show an "unable to check for updates" state and not block the rest of the page.
- **System health degraded:** When health is `Degraded` or `Unhealthy`, the indicator must surface which checks failed (database, storage, extensions, etc.) and never silently report `Healthy`.
- **Oversized logo upload:** An upload larger than the configured max size must be rejected with a clear error stating the limit; no partial or corrupt asset is stored.
- **Invalid / unsafe logo type:** A non-image or disallowed file type (or a file failing safe-upload scanning) must be rejected with a clear error and not stored.
- **Concurrent logo edits:** If two administrators upload a logo at the same time, the last successful save wins; both actions are recorded in the audit log so the change history is unambiguous.
- **Missing / deleted logo asset:** If a stored logo reference points to a missing media file, the affected surface must fall back to the default logo instead of rendering a broken image.
- **Theme interference:** The admin menu logo must remain unaffected by any active public frontend theme; the admin area is never themed by frontend themes.
- **Permission downgrade mid-session:** If a user's `Settings` permission is revoked while viewing the page, subsequent save/update actions must be denied by the server-side permission check.
- **Multi-tenant context:** Instance-wide diagnostics (versions, provider, environment) are identical across tenants; tenant-scoped settings and branding must reflect the active tenant context only, with no leakage between tenants. See [Multi-Tenant Routing](multi_tenancy.md).

## Acceptance Criteria

- [ ] The Overview page displays CMS version, available CMS updates, current C# version, current ASP.NET Core version, database provider, environment, installed extensions, and system health.
- [ ] All diagnostic fields are read-only and cannot be edited from the Overview page.
- [ ] The database provider shown matches `DATABASE_PROVIDER` in `.env`, and no connection string or secret is ever displayed.
- [ ] An administrator with `Settings` write permission can upload an admin menu logo, and it appears in the admin sidebar/header.
- [ ] An administrator with `Settings` write permission can upload an authentication/login logo, and it appears on the login and setup screens.
- [ ] Clearing either logo field reverts the affected surface to the built-in default logo.
- [ ] Logo uploads are rejected when the file type is not an allowed image type or exceeds the configured max upload size, with a clear error message.
- [ ] Server-side validation rejects unsafe uploads even if client-side validation is bypassed.
- [ ] The settings hub table links to API Tokens, Content History, Internationalization, File Manager, Plugins, Transfer, Webhooks, the Email group, and the Users & Permissions / Administration Panel groups, each routing to the correct subpage.
- [ ] The Marketplace entry is reachable from the admin Main menu.
- [ ] "Check for updates" reports either an available version (linking to the update workflow) or an explicit "Up to date" state, and shows an error state when the check fails.
- [ ] System health displays `Healthy`, `Degraded`, or `Unhealthy`, and degraded/unhealthy states expose the failing subsystem detail.
- [ ] Users without the `Settings` permission cannot view or modify Global Settings.
- [ ] Super Admin always has full access to Global Settings and every subpage.
- [ ] Every successful global settings change (logo upload, logo reset, update action) is recorded in the audit log.
- [ ] The admin menu logo and admin area are unaffected by any active public frontend theme.
- [ ] In a multi-tenant deployment, tenant context is resolved before rendering and tenant-scoped data does not leak across tenants.
