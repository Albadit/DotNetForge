# Themes

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

Themes control the visual presentation of public-facing CMS pages in **DotNetForge CMS**. A theme is a type of extension that bundles layouts, page templates, static assets, and configurable settings, and is applied at the site level or overridden per page.

## Purpose

The Themes module lets administrators control how public-facing CMS pages look without touching application code. A theme packages Razor layouts, page templates, and static assets (CSS, JavaScript, images, fonts) together with configurable settings, and is distributed and installed as an extension (see [Extensions](extensions.md)).

Themes apply **only** to public-facing CMS pages. They must **not** affect:

* The admin area.
* The login page (unless explicitly configured to use a theme).
* The setup page.

The system must always have exactly one **default site theme** that is used to render public pages when no per-page theme override is set, and that acts as the safe fallback when a selected theme becomes unavailable.

## Main Features

* **Layout support** - Themes provide one or more Razor layouts (e.g. master page, header, footer, sidebar regions) that wrap rendered page content.
* **Page templates** - Themes ship reusable page templates that authors can apply to individual pages.
* **Static assets** - Themes bundle CSS, JavaScript, images, fonts, and other static files served for public pages.
* **Theme settings** - Each theme exposes configurable settings (for example `supportsLayouts`, `supportsDarkMode`, color options, typography) defined in its manifest and editable in the admin UI.
* **Theme manifest** - Every theme is described by an extension manifest named `dotnetforge.extension.json` with `type: "theme"`. See [Extensions](extensions.md) for the full manifest format and required fields, since a theme is an extension type.
* **Theme preview** - Administrators can preview a theme against live public content before applying it as the default or per page.
* **Theme selection per page** - A page can override the site theme with its own theme. See [Content Manager](content_manager.md) for the page-level Theme settings (Select theme, Select layout, page-specific layout settings).
* **Default site theme setting** - A single site-wide default theme is configured in settings and used wherever no override applies.
* **Tenant-specific themes** - In multi-tenant deployments, each tenant has its own default theme and its own set of enabled themes. See [Multi-Tenancy](multi_tenancy.md).

### Theme manifest example

A theme manifest is an extension manifest with `type: "theme"`. The canonical required fields are `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, and `permissions`. (The original source listed a duplicated `version` key and a misspelled `vrsion` field - the single correct field is `version`.) See [Extensions](extensions.md) for the authoritative manifest schema.

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

Themes are installed under the extensions tree alongside other extension types:

```text
extensions/
  themes/
  plugins/
  modules/
  widgets/
  providers/
  connectors/
  authentication/
  libraries/
  admin/
```

## Data Model / Fields

### Theme (installed)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Manifest `id`, globally unique (e.g. `dotnetforge.theme.default`). |
| `name` | string | Yes | Display name shown in the admin UI. |
| `version` | string (semver) | Yes | Installed theme version. |
| `entryPoint` | string | Yes | Theme entry point class/component used to register layouts and assets. |
| `status` | enum | Yes | `installed`, `enabled`, `disabled`, `incompatible`. |
| `layouts` | string[] | Yes | Layout identifiers provided by the theme (at least one required). |
| `templates` | string[] | No | Page template identifiers provided by the theme. |
| `assets` | string[] | Yes | Static asset paths bundled with the theme. |
| `settings` | object | No | Configured theme settings values, keyed by the manifest `settings` schema. |

### Default theme setting

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `defaultThemeId` | string | Yes | The site-wide (or tenant-wide) default theme `id`. Must always reference an installed, enabled, compatible theme. |
| `tenantId` | string | Conditional | Required in multi-tenant deployments; scopes the default theme to a tenant. See [Multi-Tenancy](multi_tenancy.md). |

### Page-level theme override

The page-level Theme settings (`Select theme`, `Select layout`, page-specific layout settings) are owned by the [Content Manager](content_manager.md). A page may store a `themeId` and `layoutId`; when unset, the page renders with the default site theme.

## User Flows

### Flow: Install a theme

1. An authorized user opens the Marketplace / Extensions area and selects a theme to install (see [Extensions](extensions.md)).
2. The system validates the theme's `dotnetforge.extension.json` manifest and confirms `type` is `theme`.
3. The system verifies all required static assets are present and the theme is compatible with the current core version.
4. On success, the theme is installed into `extensions/themes/` and marked `installed`.
5. The theme becomes available for preview, default-theme selection, and per-page selection. It does not change any page rendering until it is applied.

### Flow: Preview a theme

1. An authorized user selects an installed theme and chooses **Preview**.
2. The system renders public CMS content using the selected theme's layouts and assets in an isolated preview context.
3. The preview must not change the live default theme or any per-page theme assignments.
4. The user exits preview, optionally proceeding to set the theme as the default site theme.

### Flow: Set the default site theme

1. An authorized user opens Theme settings and selects an installed, enabled, compatible theme.
2. The system validates the selected theme (valid manifest, required assets present, compatible with core).
3. The system sets `defaultThemeId` to the selected theme and persists the change.
4. In a multi-tenant deployment, the default theme is scoped to the active tenant (see [Multi-Tenancy](multi_tenancy.md)).
5. All public pages without a per-page override immediately render with the new default theme. The admin, login (unless configured), and setup pages are unaffected.

### Flow: Select a theme per page

1. An authorized user opens a page in the [Content Manager](content_manager.md) and opens its Theme settings.
2. The user selects a theme (and optionally a layout) for that page, or clears the override to inherit the site default.
3. The system validates that the chosen theme is installed, enabled, and compatible, and that the chosen layout exists in that theme.
4. On save, that page renders with the selected theme/layout, overriding the site default; all other pages are unaffected.

### Flow: Configure theme settings

1. An authorized user opens the settings panel for an installed theme.
2. The system renders setting controls from the theme manifest's `settings` schema (for example `supportsLayouts`, `supportsDarkMode`).
3. The user changes values and saves.
4. The system validates the values against the manifest schema and persists them.
5. Public pages rendered with that theme reflect the new settings.

## Role & Permission Rules

Theme management is governed by the **Extensions** permission area (themes are an extension type) together with **Settings** (default theme) and the page-level controls in the **Content Manager**. Theme settings and assignment affect only the public frontend, never the admin area. See [User Roles & Permissions](user_roles_permissions.md) for the canonical role and permission model.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|--------|:-----------:|:-----:|:------:|:------:|:-------------:|:------:|
| Install / uninstall a theme (via Extensions) | Yes | Yes | No | No | No | No |
| Set the default site theme (Settings) | Yes | Yes | No | No | No | No |
| Configure theme settings | Yes | Yes | No | No | No | No |
| Preview a theme | Yes | Yes | No | No | No | No |
| Select a theme/layout per page (Content Manager) | Yes | Yes | Yes | No* | No | No |
| View themed public pages | Yes | Yes | Yes | Yes | Yes | Yes |

\* Authors may set per-page theme/layout only where the page-level permission model in the [Content Manager](content_manager.md) grants it for pages they own; otherwise this is restricted to Editor and above. In multi-tenant deployments, all theme management is scoped to tenants the user is assigned to, except global Super Admins (see [Multi-Tenancy](multi_tenancy.md)).

## Validation Rules

* **Valid theme manifest** - A theme must have a valid `dotnetforge.extension.json` manifest with all canonical required fields (`id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions`) and `type` equal to `theme`. Invalid manifests must be rejected by the [Extensions](extensions.md) installer; the theme must not be installed.
* **Required assets present** - All static assets and at least one layout declared by the theme must exist before the theme can be installed or applied. A theme missing required assets must be flagged and must not be set as default.
* **Core compatibility** - A theme must be compatible with the current core version. Incompatible themes must not be installed or applied and must be marked `incompatible`.
* **A default theme must always be set** - The system must always have exactly one valid default site theme (per tenant in multi-tenant mode). The default theme must reference an installed, enabled, compatible theme. Removing or unsetting the default without providing a replacement must be rejected.
* **Per-page theme validity** - A page-level theme/layout override must reference an installed, enabled, compatible theme and an existing layout within that theme. Invalid overrides must be rejected at save time.
* **Theme settings values** - Configured theme settings must validate against the manifest's `settings` schema (type and allowed values). Invalid values must be rejected.
* **Scope enforcement** - Theme assignment must affect only public-facing CMS pages. The admin area, setup page, and login page (unless explicitly configured) must never be rendered with a frontend theme.

## Edge Cases

* **Theme uninstalled while set as default** - Uninstalling a theme currently set as the default site theme must be blocked, or the system must reassign the default to a guaranteed fallback (the built-in default theme) before completing uninstall, so a valid default always remains.
* **Theme uninstalled while used by pages** - If a theme is uninstalled while one or more pages reference it via a per-page override, those pages must fall back to the current default site theme rather than fail to render.
* **Default theme becomes invalid** - If the default theme's assets become missing or the theme becomes incompatible after a core update, the system must fall back to the built-in default theme and surface a warning to administrators.
* **Missing or broken assets** - If a theme's static assets are missing or fail to load at render time, the system must fall back to the default theme (or the built-in default) and log the failure; it must not render unstyled or broken public pages without a fallback.
* **Theme incompatible with core** - A theme that becomes incompatible after a core upgrade must be marked `incompatible`, must not be applied, and any pages or site settings pointing to it must fall back to the default theme.
* **Page-level theme overriding site theme** - When a page specifies a theme/layout, that override takes precedence over the site default for that page only; all other pages continue to use the default. Clearing the override returns the page to the site default.
* **Tenant-specific theme conflicts** - A per-page or default-theme reference must resolve within the active tenant's scope. A theme not enabled for the active tenant must not render for that tenant; the tenant's default (or built-in default) applies instead. See [Multi-Tenancy](multi_tenancy.md).
* **Preview leakage** - Previewing a theme must never alter the live default theme, per-page assignments, or another user's session; the preview context must be isolated.
* **Login/setup pages** - Theme changes must never alter the setup page or, unless explicitly configured, the login page, even when the default theme changes.

## Acceptance Criteria

- [ ] Frontend themes affect only public-facing CMS pages and never the admin area, setup page, or login page (unless explicitly configured).
- [ ] A theme can be installed through the Extensions/Marketplace flow only after its `dotnetforge.extension.json` manifest validates and `type` is `theme`.
- [ ] Installation is rejected when required assets are missing or the theme is incompatible with the current core version.
- [ ] An administrator can preview an installed theme without changing the live default theme or any per-page assignments.
- [ ] An administrator can set the default site theme, and all non-overridden public pages immediately render with it.
- [ ] The system always has exactly one valid default site theme; attempting to remove or unset it without a replacement is rejected.
- [ ] A page can override the site theme/layout via the Content Manager, and the override applies only to that page.
- [ ] Clearing a page-level theme override returns that page to the site default theme.
- [ ] Theme settings render from the manifest `settings` schema, validate on save, and apply to pages rendered with that theme.
- [ ] Uninstalling a theme that is the current default reassigns the default to the built-in default before completing.
- [ ] Pages referencing an uninstalled theme fall back to the default site theme and still render.
- [ ] Missing or broken theme assets at render time trigger a fallback to the default (or built-in default) theme and a logged warning, with no broken public output.
- [ ] A theme marked incompatible after a core upgrade is not applied, and dependent pages fall back to the default theme.
- [ ] In multi-tenant deployments, each tenant has its own default theme and enabled themes, scoped per tenant with no cross-tenant leakage.
- [ ] Only Super Admin and Admin can install/uninstall themes, set the default theme, configure theme settings, and preview themes; per-page selection follows the Content Manager permission model.
