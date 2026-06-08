# Extension System & Plugins

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Extension System lets users and developers extend almost every part of **DotNetForge CMS** through versioned, manifest-described packages. This file defines the extension lifecycle, the `dotnetforge.extension.json` manifest, manifest validation, marketplace installation, and the admin Plugins page.

## Purpose

Extensions are a core feature of DotNetForge CMS. They allow the CMS core to remain small and stable while users customize and grow the platform safely. The system must:

- Let authorized users **create, install, update, enable, disable, and remove** extensions.
- Support all supported extension types (Theme, Authentication provider, Connector, Library, Admin extension, Widget, Provider, Plugin, Module).
- Keep the **core CMS separate from custom extensions** so the core can be updated without breaking user-created extensions. Extensions live in a dedicated `extensions/` folder (see [Architecture](architecture.md) and [Updates & Rollback](transfer_updates.md)).
- Support **marketplace-style installation** through an external API (see the Marketplace area in [Settings](settings.md) and the admin entry point in [main.md](main.md)).
- Require every extension to ship a **manifest file** (`dotnetforge.extension.json`) so the CMS understands what the extension is, what it does, and how to load it.
- **Validate every manifest before installation** and refuse to install invalid, unsafe, or incompatible extensions.

## Main Features

- **Full lifecycle management**: create, install, update, enable, disable, remove.
- **Nine extension types**, each with its own integration point in the CMS.
- **Manifest-driven loading**: the CMS reads `dotnetforge.extension.json` to identify, validate, and load an extension.
- **Marketplace installation** via an external API, with source-trust checks before any download or install.
- **Pre-install validation**: schema check, required-field check, dependency resolution, version/compatibility check, and permission declaration check.
- **Plugins page** in the admin area listing every installed extension with status and update availability, plus per-extension actions.
- **Permission model**: extensions declare the permissions they require; the CMS enforces them through [User Roles & Permissions](user_roles_permissions.md).
- **Safe enable/disable/remove** with dependency-aware and in-use guards.
- **Custom extension points**: extensions can register dashboard widgets, modules, dynamic route handlers, authentication providers, storage connectors, and admin extensions (see [Dashboard](dashboard.md), [Content Manager](content_manager.md), [Dynamic Routes & Multi-Tenancy](dynamic_routes.md), [Authentication Providers](authentication.md), and [File Manager](file_manager.md)).

### Extension Types

| Type | Purpose |
|------|---------|
| Theme | Affects only public-facing CMS pages; provides layouts, templates, and static assets. Never affects the admin area. See [Themes](themes.md). |
| Authentication provider | Adds a login/identity provider (e.g. OAuth/OIDC). See [Authentication Providers](authentication.md). |
| Connector | Integrates external services, such as external/storage backends for the [File Manager](file_manager.md). |
| Library | Shared code/dependency reused by other extensions or the core. |
| Admin extension | Adds or modifies admin-area functionality. |
| Widget | Adds dashboard or page widgets (e.g. custom [Dashboard](dashboard.md) widgets). |
| Provider | Supplies pluggable services to the core or other extensions. |
| Plugin | General-purpose extension feature, surfaced and managed on the Plugins page. |
| Module | Placeable content unit usable by the page builder (see [Content Manager](content_manager.md)). |

## Data Model / Fields

### Manifest file: `dotnetforge.extension.json`

Each extension must include a manifest file named exactly `dotnetforge.extension.json`. Corrected example (a single `version` field; the duplicated `version` key and the misspelled `vrsion` field from the original source have been removed):

```json
{
  "id": "dotnetforge.theme.default",
  "name": "Default Theme",
  "description": "The default frontend theme for DotNetForge CMS.",
  "version": "1.0.0",
  "type": "theme",
  "author": "DotNetForge",
  "website": "https://example.com",
  "license": "MIT",
  "entryPoint": "DefaultTheme",
  "dependencies": [],
  "permissions": [
    "content.read",
    "media.read"
  ],
  "routes": [],
  "settings": {
    "supportsLayouts": true,
    "supportsDarkMode": true
  }
}
```

### Manifest fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique, stable extension identifier (e.g. reverse-dotted, `dotnetforge.theme.default`). |
| `name` | string | Yes | Human-readable display name shown on the Plugins page. |
| `description` | string | Yes | Short description of what the extension does. |
| `version` | string | Yes | Single semantic version of the extension (e.g. `1.0.0`). There must be exactly one `version` field. |
| `type` | string | Yes | One of: `theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`, `plugin`, `module`. |
| `author` | string | Yes | Extension author or vendor. |
| `entryPoint` | string | Yes | The class/component/identifier the CMS loads to activate the extension. |
| `permissions` | string[] | Yes | Permissions the extension requires (e.g. `content.read`, `media.read`). Declared up front and enforced via [User Roles & Permissions](user_roles_permissions.md). |
| `website` | string | No | Project or author website URL. |
| `license` | string | No | License identifier (e.g. `MIT`). |
| `dependencies` | array | No | Other extensions (and optional version constraints) this extension depends on. Used for dependency resolution. |
| `routes` | array | No | Routes the extension registers (including dynamic route handlers; see [Dynamic Routes & Multi-Tenancy](dynamic_routes.md)). |
| `settings` | object | No | Extension-specific configuration surfaced via the Configure action. |

## User Flows

### Flow: Create an extension

1. A developer scaffolds an extension of a chosen type inside the dedicated `extensions/` area (see [Architecture](architecture.md) for the folder layout).
2. The developer authors the required `dotnetforge.extension.json` manifest with all required fields.
3. The developer implements the `entryPoint` and any declared `routes`, and declares all required `permissions`.
4. The extension is packaged for installation or for submission to the marketplace (see [Developer Documentation](developer_docs.md)).

### Flow: Install an extension (local package)

1. A Super Admin or Admin uploads or selects an extension package.
2. The CMS locates and reads `dotnetforge.extension.json`.
3. The CMS runs full manifest validation (see [Validation Rules](#validation-rules)): schema, required fields, dependency resolution, version/compatibility check, and permission declarations.
4. If validation fails, the install is rejected with a clear reason and nothing is installed or loaded.
5. If validation passes, the CMS installs the extension into the `extensions/` area, records the installed date, and (per type) leaves it **disabled** until explicitly enabled.
6. The action is recorded in the audit log (see [Audit Logs](audit_logs.md)).

### Flow: Install an extension (marketplace)

1. A Super Admin or Admin opens the Marketplace (admin entry point defined in [main.md](main.md); marketplace configuration in [Settings](settings.md)).
2. The CMS queries the external marketplace API for available extensions.
3. The user selects an extension to install.
4. The CMS verifies the marketplace **source is trusted**; an untrusted source is rejected before any download.
5. The CMS downloads the package, reads `dotnetforge.extension.json`, and runs the same full manifest validation as a local install.
6. On success, the extension is installed (disabled by default) and the installed date is recorded; on failure, the install is rejected with a clear reason.
7. The action is recorded in the audit log (see [Audit Logs](audit_logs.md)).

### Flow: Enable an extension

1. A Super Admin or Admin selects an installed extension and chooses **Enable**.
2. The CMS verifies all `dependencies` are installed and enabled and that the compatibility check still holds.
3. The CMS loads the extension via its `entryPoint`, registering any routes, widgets, providers, or handlers.
4. The extension Status becomes Enabled; the change is audit-logged.

### Flow: Disable an extension

1. A Super Admin or Admin selects an enabled extension and chooses **Disable**.
2. The CMS checks whether other enabled extensions depend on it. If so, the system warns and refuses to disable until dependents are disabled (or requires explicit confirmation per business rules).
3. The CMS checks whether the extension is currently in use (e.g. an active theme or an active provider) and warns/blocks accordingly.
4. On confirmation, the CMS unloads the extension; Status becomes Disabled; the change is audit-logged.

### Flow: Configure an extension

1. A Super Admin or Admin selects an installed extension and chooses **Configure**.
2. The CMS presents the extension's `settings` for editing.
3. The user saves valid settings; invalid settings are rejected with field-level messages.

### Flow: Update an extension

1. The Plugins page shows **Update availability** for each extension (compared against the marketplace/update source).
2. A Super Admin or Admin chooses **Update**.
3. The CMS fetches the new package, reads its manifest, and runs full validation including a version/compatibility check against the current core version.
4. The update must **not** overwrite unrelated custom extensions, and a backup should be created before applying it (see [Updates & Rollback](transfer_updates.md)).
5. On success, the version and update status are refreshed; on failure, the previous version is preserved/restored where possible. The change is audit-logged.

### Flow: Remove an extension

1. A Super Admin or Admin selects an installed extension and chooses **Remove**.
2. The CMS checks for dependents and for in-use status (active theme, active authentication provider, active connector, etc.).
3. If the extension is depended on or in use, removal is blocked with a clear explanation until the conflict is resolved.
4. On confirmation, the CMS disables, unloads, and removes the extension from the `extensions/` area; the change is audit-logged.

## Role & Permission Rules

Only **Super Admin** and **Admin** may manage extensions. The Extensions and Plugins permission areas are evaluated through [User Roles & Permissions](user_roles_permissions.md). All others have no access to extension management.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|--------|:-----------:|:-----:|:------:|:------:|:-------------:|:------:|
| View Plugins page | Yes | Yes | No | No | No | No |
| Install (local / marketplace) | Yes | Yes | No | No | No | No |
| Enable / Disable | Yes | Yes | No | No | No | No |
| Configure | Yes | Yes | No | No | No | No |
| Update | Yes | Yes | No | No | No | No |
| Remove | Yes | Yes | No | No | No | No |
| Create / develop extensions | Yes | Yes | No | No | No | No |

> Note: an installed extension's own runtime permissions (its declared `permissions`) are granted to roles separately through the role/permission model. Managing the extension lifecycle is restricted to Super Admin and Admin regardless of an extension's declared permissions.

## Validation Rules

The CMS must validate the manifest **before** installing, enabling, or updating an extension. The install must be rejected if any rule fails.

- **Manifest present**: the package must contain a file named exactly `dotnetforge.extension.json`.
- **Schema validation**: the manifest must be well-formed JSON and conform to the manifest schema.
- **Required fields present**: `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, and `permissions` must all be present and non-empty.
- **Single `version` field**: there must be exactly one `version` field (a valid semantic version string). Duplicate `version` keys or a misspelled `vrsion` field are invalid.
- **Valid `type`**: `type` must be one of the supported extension types (`theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`, `plugin`, `module`).
- **Unique `id`**: the `id` must not collide with an already-installed extension (except when updating the same `id`).
- **Permission declarations**: every entry in `permissions` must be a known/declarable permission; the CMS enforces declared permissions at runtime (see [User Roles & Permissions](user_roles_permissions.md)).
- **Dependency resolution**: every entry in `dependencies` must resolve to an installed (and, on enable, enabled) extension that satisfies any version constraint.
- **Version / compatibility check**: the extension must declare or satisfy compatibility with the current CMS core version; incompatible extensions are rejected (see [Updates & Rollback](transfer_updates.md) for core versioning).
- **Safety check**: unsafe extensions (failing manifest validation or safety checks) must not be loaded.
- **Marketplace source trust**: marketplace installs must come from a trusted source; untrusted sources are rejected before download.
- **Configure settings validation**: values saved via Configure must validate against the extension's `settings` definition.

## Edge Cases

- **Invalid or malformed manifest**: if `dotnetforge.extension.json` is missing, is not valid JSON, fails schema validation, or omits a required field, the install/update is rejected with a clear, specific error and nothing is loaded.
- **Duplicate / misspelled version key**: a manifest with two `version` keys or a `vrsion` field fails validation and is rejected.
- **Missing dependency**: if a declared dependency is not installed (or not enabled when enabling), the install/enable is blocked and the missing dependency is named.
- **Incompatible core version**: if the extension is not compatible with the running CMS core version, install/update/enable is rejected with the required vs. current version reported.
- **Disabling an extension others depend on**: the system warns and refuses to disable until dependent extensions are disabled first (or requires explicit confirmation), preventing broken dependents.
- **Removing an in-use theme**: removing the active site/page theme (or a theme selected per page) is blocked until another theme is selected (see [Themes](themes.md)). The admin area is never affected by frontend themes, so admin access is never lost.
- **Removing an in-use provider/connector**: removing an active authentication provider (see [Authentication Providers](authentication.md)) or an in-use storage connector (see [File Manager](file_manager.md)) is blocked until it is no longer in use, to avoid locking out logins or orphaning media.
- **Untrusted marketplace source**: any install attempt from an untrusted or unverified marketplace source is rejected before download; no package is fetched or executed.
- **ID collision**: installing an extension whose `id` already exists (and is not an update of that `id`) is rejected to avoid overwriting an unrelated extension.
- **Update failure / rollback**: if an update fails mid-apply, the previous working version is preserved or automatically restored where possible, and the failure is reported (see [Updates & Rollback](transfer_updates.md)).
- **Disabled-but-installed extension**: a disabled extension remains installed and listed on the Plugins page, contributes no routes/handlers, and can still be configured, updated, or removed.
- **Concurrent lifecycle actions**: simultaneous enable/disable/update/remove on the same extension must be serialized so the extension cannot end up in an inconsistent state.

## Plugins Page

The Plugins page (admin Settings area; see [main.md](main.md) and [Settings](settings.md)) lists all installed plugins/extensions.

### Listed columns

| Column | Description |
|--------|-------------|
| Name | Display name from the manifest `name`. |
| Description | Manifest `description`. |
| Version | Installed `version`. |
| Type | Extension `type`. |
| Status | Enabled or Disabled. |
| Author | Manifest `author`. |
| Installed date | When the extension was installed. |
| Update availability | Whether a newer version is available from the marketplace/update source. |

### Per-extension actions

- **Enable** - load and activate the extension (subject to dependency and compatibility checks).
- **Disable** - unload the extension (subject to dependent/in-use guards).
- **Configure** - edit the extension's `settings`.
- **Update** - apply an available newer version after validation.
- **Remove** - uninstall the extension (subject to dependent/in-use guards).

All actions are restricted to Super Admin and Admin and are recorded in the [Audit Logs](audit_logs.md).

## Acceptance Criteria

- [ ] An extension cannot be installed without a valid `dotnetforge.extension.json` manifest.
- [ ] Manifest validation enforces all required fields: `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions`.
- [ ] A manifest with duplicate `version` keys or a `vrsion` field is rejected; exactly one `version` field is required.
- [ ] `type` is validated against the supported extension types (Theme, Authentication provider, Connector, Library, Admin extension, Widget, Provider, Plugin, Module).
- [ ] Invalid, unsafe, or incompatible extensions are never installed or loaded.
- [ ] Dependency resolution blocks installing/enabling an extension whose dependencies are missing, naming the missing dependency.
- [ ] A version/compatibility check against the current core version blocks incompatible extensions.
- [ ] Marketplace installs verify source trust before download; untrusted sources are rejected.
- [ ] Marketplace install is reachable from the admin Marketplace entry point and uses the external marketplace API configured in Settings.
- [ ] The full lifecycle works: create, install, update, enable, disable, remove.
- [ ] Disabling an extension that others depend on is prevented (or requires explicit confirmation) and does not break dependents.
- [ ] Removing an in-use theme is blocked until another theme is selected; the admin area remains unaffected by frontend themes.
- [ ] Removing an in-use authentication provider or connector is blocked until it is no longer in use.
- [ ] Updating an extension does not overwrite unrelated custom extensions and creates a backup beforehand.
- [ ] A failed update preserves or restores the previous working version where possible.
- [ ] The Plugins page lists every installed plugin with Name, Description, Version, Type, Status, Author, Installed date, and Update availability.
- [ ] The Plugins page exposes Enable, Disable, Configure, Update, and Remove actions per extension.
- [ ] Only Super Admin and Admin can view the Plugins page and manage extensions; all other roles are denied.
- [ ] Every install, enable, disable, configure, update, and remove action is recorded in the audit log.
- [ ] Each installed extension declares its required permissions, which are enforced at runtime via the role/permission model.
