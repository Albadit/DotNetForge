# Themes

A theme is an extension (manifest `type: "theme"`) that controls how the **public site** looks: Razor layouts, page
templates, static assets and configurable settings, applied per tenant and overridable per content page. Themes
never touch the admin area or the setup screen. Today only the contracts and data exist; nothing loads or applies a
theme. Extension discovery and manifests: [extensions](extensions.md). Per-page theme fields:
[content pages and routing](content-pages-and-routing.md#page-model-additions).

## Current state

### What exists

| Item | Where | Behaviour |
| --- | --- | --- |
| `IThemeExtension : IExtension` with `IReadOnlyList<string> Layouts` | `src/DotNetForge.Abstractions/Extensions/IExtensionPoints.cs` | contract only; no implementation, nothing discovers or calls it |
| `Tenant.DefaultTheme` (string, max 200, default `dotnetforge.theme.default`) | `src/DotNetForge.Shared/Entities/Tenant.cs`, seeded by `DataSeeder` | stored only; nothing reads it and no screen edits it |
| Sample theme manifest `dotnetforge.theme.default` | `extensions/themes/default-theme/dotnetforge.extension.json` | manifest only (no layouts or assets); validated and listed on the [Plugins screen](../pages/plugins.md); `settings`: `supportsLayouts: true`, `supportsDarkMode: false` |
| `ExtensionType.Theme`, `InstalledExtension` (`Status` = `Disabled`/`Enabled`) | `src/DotNetForge.Shared/` | the `InstalledExtensions` table is never written |
| Manifest validation | `ManifestValidator` | `theme` is a valid `type`; no theme-specific checks (layouts, assets, compatibility) |

### Public rendering today

| Screen | View | Layout and styles |
| --- | --- | --- |
| [Public content page](../pages/public-page.md) and root page | `Views/Home/Page.cshtml` | `Layout = null`; own document with inline CSS |
| [Public home](../pages/public-home.md) fallback list, error | `Views/Home/Index.cshtml`, `Views/Home/Error.cshtml` | `Views/Shared/_Layout.cshtml` + `wwwroot/css/site.css` |

### Isolation today

- Admin screens use `Areas/Admin/Views/Shared/_AdminLayout.cshtml` (`site.css` + `admin.css`).
- Setup, sign-in and access denied use `Views/Shared/_AuthLayout.cshtml` (`site.css`).
- `site.css` is shared by `_Layout`, `_AuthLayout` and `_AdminLayout`, so a theme must ship its own stylesheet and
  never replace `site.css`.
- `UseStaticFiles` serves only `wwwroot`; nothing serves files from `extensions/themes/`.
- Runtime Razor compilation already resolves `~/extensions/...` views from the content root (used by admin
  extensions, `DependencyRegistration`).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- **Package:** a theme is an extension with `type: "theme"` (✔ validated) installed under `extensions/themes/`
  (✔ folder convention). It bundles:
  - one or more Razor **layouts** (master page with regions such as header, footer, sidebar) wrapping page content;
  - reusable **page templates** authors can apply to pages;
  - **static assets** (CSS, JavaScript, images, fonts) served for public pages;
  - **settings** declared in the manifest `settings` (e.g. `supportsLayouts`, `supportsDarkMode`, colours,
    typography), editable in the admin.
- **Scope:** only public content pages. Never the admin area or the setup screen; the sign-in screen only when
  explicitly configured.
- **Default theme:** exactly one default theme per tenant (column ✔ `Tenant.DefaultTheme`), used when a page has no
  override and as the fallback when a selected theme is unavailable.
- **Per-page override:** a content page may store a theme, a layout of that theme and page-specific layout settings;
  unset means the tenant default ([content pages and routing](content-pages-and-routing.md#page-model-additions)).
  Dynamic routes carry the same optional theme/layout.
- **Preview:** render live public content with an installed theme in an isolated context, without applying it.
- **Tenants:** each tenant has its own default theme and its own set of enabled themes
  ([multi-tenancy](multi-tenancy.md)).

Installed theme record:

| Field | Type | Required | Meaning |
| --- | --- | :-: | --- |
| `id` | string | ✔ | manifest `id`, globally unique (e.g. `dotnetforge.theme.default`) |
| `name` | string | ✔ | display name |
| `version` | semver | ✔ | installed version |
| `entryPoint` | string | ✔ | class/component that registers layouts and assets |
| `status` | enum | ✔ | `installed`, `enabled`, `disabled`, `incompatible` (`ExtensionStatus` has only `Disabled`/`Enabled`) |
| `layouts` | string[] | ✔ | layout ids, at least one (`IThemeExtension.Layouts` ✔ contract) |
| `templates` | string[] | | page template ids |
| `assets` | string[] | ✔ | bundled static asset paths |
| `settings` | object | | configured values keyed by the manifest `settings` schema |

Default theme setting: `defaultThemeId` (required, always an installed, enabled, compatible theme) scoped by
`tenantId` in multi-tenant deployments - maps to `Tenant.DefaultTheme` ✔.

The manifest uses the canonical extension fields (`id`, `name`, `description`, `version`, `type`, `author`,
`entryPoint`, `permissions`; ✔ validated by `ManifestValidator`). The spec defines neither the format of a settings
schema nor a manifest field for core compatibility; both must be designed.

### User flows

**Install a theme.** From Marketplace/Extensions ([extensions](extensions.md)): validate the manifest and
`type: "theme"` → check required assets and core compatibility → install into `extensions/themes/`, status
`installed` → available for preview, default and per-page selection; no rendering changes until applied.

**Preview a theme.** Select an installed theme → **Preview** → public content renders with its layouts and assets in
an isolated context → exit, optionally set it as default. Live default and per-page assignments are untouched.

**Set the default theme.** Theme settings → choose an installed, enabled, compatible theme → validate (manifest,
assets, compatibility) → save `defaultThemeId` for the active tenant → every page without an override renders with it
immediately; admin, setup and (unless configured) sign-in are unaffected.

**Select a theme per page.** In the [Content Manager](../pages/content-manager.md#planned-not-implemented) choose a
theme and optionally a layout, or clear the override → validate theme state and that the layout exists in it → only
that page changes.

**Configure theme settings.** Open an installed theme's settings → controls rendered from the manifest `settings`
schema → validate values against the schema → save → pages using the theme reflect them.

### Rules and validation

| Rule | Today |
| --- | --- |
| Valid manifest with all canonical fields and `type` = `theme`; invalid manifests are not installed | ✔ validation; ✘ no install step |
| Required assets and at least one layout present before install or apply; otherwise flagged and never set as default | ✘ |
| Compatible with the current core version; otherwise status `incompatible`, not installed or applied | ✘ |
| Exactly one valid default theme per tenant; removing or unsetting it without a replacement is rejected | ✘ (column always has a value, never validated) |
| Per-page theme/layout must reference an installed, enabled, compatible theme and an existing layout; else rejected on save | ✘ |
| Theme setting values validate against the manifest `settings` schema (type, allowed values) | ✘ |
| Admin area, setup and (unless configured) sign-in are never rendered with a theme | ✔ separate layouts, no theme system |

Role defaults (permission areas `Extensions`, `Settings`; see [authorization](authorization.md)):

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| Install / uninstall a theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Set the default theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Configure theme settings | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Preview a theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Select theme/layout per page | ✔ | ✔ | ✔ | own pages if granted | ✘ | ✘ |
| View themed public pages | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

In multi-tenant deployments theme management is limited to the user's tenants, except for global Super Admins.

### Edge cases

- **Uninstalling the default theme:** blocked, or the default is first reassigned to the built-in default theme.
- **Uninstalling a theme used by pages:** those pages fall back to the tenant default theme and still render.
- **Default theme becomes invalid** (assets missing, incompatible after a core update): fall back to the built-in
  default theme and warn administrators.
- **Missing or broken assets at render time:** fall back to the default (or built-in default) theme and log; never
  render unstyled or broken public pages.
- **Incompatible after a core upgrade:** marked `incompatible`, not applied; dependent pages and settings fall back to
  the default theme.
- **Page override vs site default:** the override applies to that page only; clearing it returns to the default.
- **Tenant scope:** a theme not enabled for the active tenant never renders for it; the tenant default (or built-in
  default) applies.
- **Preview leakage:** previewing never changes the live default, per-page assignments or another user's session.
- **Setup and sign-in:** unchanged when the default theme changes (sign-in only themed when explicitly configured).

### Acceptance criteria

- [ ] Frontend themes affect only public content pages and never the admin area, setup or sign-in (unless
  configured).
- [ ] A theme installs through the Extensions/Marketplace flow only after its manifest validates and `type` is
  `theme`.
- [ ] Installation is rejected when required assets are missing or the theme is incompatible with the core version.
- [ ] An administrator can preview an installed theme without changing the live default or per-page assignments.
- [ ] An administrator can set the default theme, and every non-overridden public page renders with it immediately.
- [ ] There is always exactly one valid default theme; removing or unsetting it without a replacement is rejected.
- [ ] A page can override theme/layout in the Content Manager, affecting only that page.
- [ ] Clearing a page override returns the page to the default theme.
- [ ] Theme settings render from the manifest `settings` schema, validate on save and apply to pages using the
  theme.
- [ ] Uninstalling the current default theme reassigns the default to the built-in default first.
- [ ] Pages referencing an uninstalled theme fall back to the default theme and still render.
- [ ] Missing or broken assets at render time fall back to the default (or built-in default) theme with a logged
  warning and no broken public output.
- [ ] A theme marked incompatible after a core upgrade is not applied, and dependent pages fall back to the default.
- [ ] Each tenant has its own default theme and enabled themes, with no cross-tenant leakage.
- [ ] Only Super Admin and Admin install/uninstall, set the default, configure settings and preview themes; per-page
  selection follows the Content Manager permission model.

## Where to change things

| Change | Place |
| --- | --- |
| Discover theme manifests | `ExtensionLoader.Discover` (already finds them); filter `type == "theme"` in a new theme service, not in controllers |
| Theme-specific manifest checks (layouts, assets, compatibility) | `ManifestValidator` + `tests/DotNetForge.Tests/ManifestValidationTests.cs` |
| Read/edit the default theme | `Tenant.DefaultTheme` (column exists); add the edit to a Settings screen ([admin screens](../architecture/pages.md)) |
| Choose layout/view per request | `HomeController.Index` / `RenderPage` after a page matches; replace `Layout = null` in `Views/Home/Page.cshtml` with the theme layout |
| Theme views | runtime Razor compilation already resolves `~/extensions/...` (`DependencyRegistration`) |
| Serve theme assets | a guarded endpoint like `ExtensionViewController.Resource` (`Path.GetFullPath` + prefix check); never expose `extensions/` through `UseStaticFiles` |
| Per-page theme/layout fields | `Page` entity → migration → form, following [content pages and routing → Where to change things](content-pages-and-routing.md#where-to-change-things) |
| Keep admin and auth screens unthemed | `_AdminLayout`, `_AuthLayout`, `admin.css`; move rules they need out of the shared `site.css` before a theme replaces public styles |
