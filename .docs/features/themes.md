# Themes

A theme is an extension (manifest `type: "theme"`) that controls how the **public site** looks: Razor views, a
layout, static assets and manifest settings. Themes never touch the admin area, setup, sign-in or the error screen.
The site-wide theme slot, per-page themes and theme inheritance down the page tree are built; a selectable default
theme, preview and settings editing are [planned](#planned-not-implemented). Extension mechanics (enabling, view data, resources): [extensions](extensions.md).
Per-page theme fields: [content pages and routing](content-pages-and-routing.md#page-model-additions).

## Which theme is active

`ThemeResolver.GetActiveThemeAsync` (`src/DotNetForge.Web/Services/ThemeResolver.cs`, scoped; it reads only
`ExtensionCatalog` - the page tree for inheritance is read by `PageThemeIdAsync` through `DotNetForgeDbContext`)
decides per request:

1. The active `theme` extensions (`ExtensionCatalog.GetActiveAsync("theme")`: valid manifest, enabled on the
   [Plugins screen](../pages/plugins.md), code loaded if it has any). None → the built-in views.
2. For a content page, the theme it asks for (`ThemeResolver.PageThemeIdAsync`, see [theme
   inheritance](#theme-inheritance)) when that names one of them: the page's own `ThemeId` (chosen on the Pages
   screen's **Theme** tab), else the theme a parent page hands down. A page theme that is not enabled is ignored.
3. `ThemeResolver.DefaultThemeId` = `dotnetforge.theme.default` (the bundled sample) when it is one of them
   (case-insensitive). The default is a constant; no setting or screen changes it.
4. Otherwise the first active theme by name.

Disabling the active theme therefore falls back to the next enabled theme, then to the built-in views, without a
restart (other instances follow within 30 s, `ExtensionState.Lifetime`).

### Theme inheritance

A page with a theme of its own can hand it down: the **Use this theme for child pages too** switch on its Theme tab
sets `Page.ThemeForChildren` (stored only together with a `ThemeId`; `PageService.ApplyAsync` clears it otherwise).
`PageThemeIdAsync(page)` returns:

1. the page's own `ThemeId`, when it has one;
2. else, for a child page, `ThemeResolver.InheritedFrom(tree, page.ParentPageId)`: walks up the parents and stops at
   the first one with its own `ThemeId` - that ancestor hands its theme down when `ThemeForChildren` is on, otherwise
   the walk ends with nothing (a page that keeps its theme to itself also stops a theme from higher up);
3. else null - the site theme (steps 3-4 above).

The tree (`ThemeNode`: id, parent, title, `ThemeId`, `ThemeForChildren`) comes from one single-table query over all
pages, live or not, and is walked in memory (cycle-guarded). Only content pages use it - `HomeController` and the page
builder's canvas/preview; the fallback list of the [public home](../pages/public-home.md) has no page and uses the site
theme. On the Pages screen the same `InheritedFrom` labels a child page's first theme option **Parent's theme**
([pages → page editor](../pages/pages.md#page-editor)).

## Theme views

`HomeController.ThemedViewAsync(name, model)` renders the active theme's `Views/<name>.cshtml` when the file exists,
else the built-in view:

| View | Used for | Model | Built-in fallback |
| --- | --- | --- | --- |
| `Views/Page.cshtml` | every [public content page](../pages/public-page.md) and the root page | `Page` (+ `ViewData["RouteValues"]` for dynamic segments) | `src/DotNetForge.Web/Views/Home/Page.cshtml` (`_Layout`, `site.css`) |
| `Views/Index.cshtml` | the [public home](../pages/public-home.md) fallback list (no live root page) | `List<(string Title, string Slug)>` | `src/DotNetForge.Web/Views/Home/Index.cshtml` (`_Layout`, `site.css`) |

A theme may ship either file or both; its `_ViewStart.cshtml` / `Shared/_Layout.cshtml` are found by normal Razor
lookup from the view's folder (the views are compiled in memory from `~/extensions/...`). Theme views get, besides the
standard [extension view data](extensions.md#slots) (`Extension`, `Settings`, `ResourceBase` =
`/ext/{id}/resources`), these keys (set for the built-in views too, whose `_Layout` shows the menu):

| `ViewData` key | Value |
| --- | --- |
| `AppName` | `APP_NAME` |
| `Menu` | `IReadOnlyList<PublishedPage>`: top-level live pages with "display in menu", from `IPublishedPages` (tree order, then sort order) |

To show [modules](extensions.md#slots) placed on a page, a theme's `Page.cshtml` calls
`@await Component.InvokeAsync("ExtensionModules", new { page = Model })` (the built-in view does too).

**Page builder layouts.** A page designed with the [page builder](page-builder.md#rendering) has a published
`LayoutJson`. A theme's `Page.cshtml` renders it with `@await Component.InvokeAsync("PageLayout", new { page = Model })`
(stylesheet links and HTML) and should then skip its own page markup. Page views also set these keys for the layout
to read:

| `ViewData` key | Set by the page view | Layout use |
| --- | --- | --- |
| `FullWidth` | `true` when the page has a layout | drop the centred content column (sections set their own width) |
| `NoIndex` | `Page.NoIndex` | `<meta name="robots" content="noindex">` |
| `SocialImage` | `Page.SocialImageUrl` | `<meta property="og:image">` (absolute URL) |

Layouts use the `--dnn-color-*` colour variables ([page builder → theme colours](page-builder.md#theme-colours));
a theme can redefine them on `body` to match its palette (the sample does). The page builder's canvas sets
`data-theme="light"|"dark"` on `<html>`; a theme whose dark mode should follow the canvas choice checks it next to
`prefers-color-scheme` (see the sample's `theme.css`).

**Assets.** `Views/Resources/` of an active theme is served publicly at `/ext/{id}/resources/{**path}`
(`ExtensionResourcesController`, path-traversal guarded). `UseStaticFiles` still serves only `wwwroot`. The CSP
(`script-src 'self'; style-src 'self'`) applies: no inline script or style, so settings such as colours select CSS
classes rather than writing styles.

The built-in fallback is the public layout of the [design system](../architecture/design-system.md#public-fallback):
`Views/Shared/_Layout.cshtml` with the library's `dnf.css` and `wwwroot/css/site.css` (light/dark by system
preference or the saved `dnf-theme` choice). Its header lists the `Menu` pages (`nav.dnf-site__nav`, the current page
`aria-current="page"`) and shows **Admin →** only to signed-in users who may enter the admin (`Roles.CanEnterAdmin`).

**Isolation.** Only `HomeController.Index` and `RenderPage` use themes, plus the page builder's canvas and draft
preview (`PageBuilderController`), which show the page as the site does. Admin screens keep `_AdminLayout`
(`css/admin/*.css`); setup, sign-in and access denied keep `_AuthLayout` (`css/auth.css`); the error screen keeps
`_Layout`. A theme ships its own stylesheet and never replaces `dnf.css` or the CMS's `wwwroot/css/` files. The admin's light/dark switch
([design system → themes](../architecture/design-system.md#themes)) is unrelated to public themes: a theme never
affects it, and it never affects a theme.

**Sample.** `extensions/themes/default-theme` (`dotnetforge.theme.default`, views only): `Shared/_Layout.cshtml`
with the menu (and the `FullWidth` / `NoIndex` / `SocialImage` keys), `Page.cshtml` (renders a page builder layout
when there is one), `Index.cshtml`, `Views/Resources/css/theme.css`; settings `supportsDarkMode`,
`accent` (`indigo`/`teal`/`rose`), `tagline`. It is `ThemeResolver.DefaultThemeId`, so enabling it themes the site.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- **Package:** a theme is an extension with `type: "theme"` (✔ validated) installed under `extensions/themes/`
  (✔ folder convention). It bundles:
  - one or more Razor **layouts** (master page with regions such as header, footer, sidebar) wrapping page content
    (✔ one layout via the theme's own `_ViewStart`; ✘ several selectable layouts);
  - reusable **page templates** authors can apply to pages (✘; ✔ one `Page.cshtml` for every page);
  - **static assets** (CSS, JavaScript, images, fonts) served for public pages (✔ `/ext/{id}/resources`);
  - **settings** declared in the manifest `settings` (e.g. `supportsDarkMode`, colours, typography) ✔ read by the
    views, editable in the admin ✘.
- **Scope:** only public content pages ✔. Never the admin area or the setup screen ✔; the sign-in screen only when
  explicitly configured (✘ never themed today).
- **Default theme:** exactly one default theme, chosen by an administrator (✘; today the constant
  `ThemeResolver.DefaultThemeId` ✔), used when a page has no override and as the fallback when a selected theme is
  unavailable.
- **Per-page override:** a content page may store a theme, a layout of that theme and page-specific layout settings;
  unset means the default theme ([content pages and routing](content-pages-and-routing.md#page-model-additions)).
  Dynamic routes carry the same optional theme/layout.
- **Preview:** render live public content with an installed theme in an isolated context, without applying it.

Installed theme record:

| Field | Type | Required | Meaning |
| --- | --- | :-: | --- |
| `id` | string | ✔ | manifest `id`, globally unique (e.g. `dotnetforge.theme.default`) |
| `name` | string | ✔ | display name |
| `version` | semver | ✔ | installed version |
| `entryPoint` | string | ✔ | class/component that registers layouts and assets |
| `status` | enum | ✔ | `installed`, `enabled`, `disabled`, `incompatible` (`ExtensionStatus` has only `Disabled`/`Enabled` ✔) |
| `layouts` | string[] | ✔ | layout ids, at least one (no such field or contract exists) |
| `templates` | string[] | | page template ids |
| `assets` | string[] | ✔ | bundled static asset paths |
| `settings` | object | | configured values keyed by the manifest `settings` schema |

Default theme setting: `defaultThemeId` (required, always an installed, enabled, compatible theme) - replaces the
constant `ThemeResolver.DefaultThemeId` ✔.

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
assets, compatibility) → save `defaultThemeId` → every page without an override renders with it
immediately; admin, setup and (unless configured) sign-in are unaffected.

**Select a theme per page.** On [Pages](../pages/pages.md#planned-not-implemented) choose a
theme and optionally a layout, or clear the override → validate theme state and that the layout exists in it → only
that page changes.

**Configure theme settings.** Open an installed theme's settings → controls rendered from the manifest `settings`
schema → validate values against the schema → save → pages using the theme reflect them.

### Rules and validation

| Rule | Today |
| --- | --- |
| Valid manifest with all canonical fields and `type` = `theme`; invalid manifests are not installed | ✔ validation (invalid themes are never used); ✘ no install step |
| Required assets and at least one layout present before install or apply; otherwise flagged and never set as default | ✘ |
| Compatible with the current core version; otherwise status `incompatible`, not installed or applied | ✘ |
| Exactly one valid default theme; removing or unsetting it without a replacement is rejected | ✘ (a constant; when it is not enabled the first enabled theme is used) |
| Per-page theme/layout must reference an installed, enabled, compatible theme and an existing layout; else rejected on save | ✘ |
| Theme setting values validate against the manifest `settings` schema (type, allowed values) | ✘ |
| Admin area, setup and (unless configured) sign-in are never rendered with a theme | ✔ only `HomeController` uses themes |

Role defaults (permission areas `Extensions`, `Settings`; see [authorization](authorization.md)):

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| Install / uninstall a theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Set the default theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Configure theme settings | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Preview a theme | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Select theme/layout per page | ✔ | ✔ | ✔ | own pages if granted | ✘ | ✘ |
| View themed public pages | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |

### Edge cases

- **Uninstalling the default theme:** blocked, or the default is first reassigned to the built-in default theme.
  (✔ today: disabling it falls back to the next enabled theme or the built-in views - [Which theme is
  active](#which-theme-is-active).)
- **Uninstalling a theme used by pages:** those pages fall back to the default theme and still render.
- **Default theme becomes invalid** (assets missing, incompatible after a core update): fall back to the built-in
  default theme and warn administrators.
- **Missing or broken assets at render time:** fall back to the default (or built-in default) theme and log; never
  render unstyled or broken public pages.
- **Incompatible after a core upgrade:** marked `incompatible`, not applied; dependent pages and settings fall back to
  the default theme.
- **Page override vs site default:** the override applies to that page only; clearing it returns to the default.
  (✔ today; with **Use this theme for child pages too** it also applies to child pages without their own theme -
  [theme inheritance](#theme-inheritance).)
- **Preview leakage:** previewing never changes the live default, per-page assignments or another user's session.
- **Setup and sign-in:** unchanged when the default theme changes (sign-in only themed when explicitly configured).

### Acceptance criteria

- [x] Frontend themes affect only public content pages and never the admin area, setup or sign-in
  (`HomeController.ThemedViewAsync` only; sign-in is never themed; no test asserts the admin side).
- [ ] A theme installs through the Extensions/Marketplace flow only after its manifest validates and `type` is
  `theme`.
- [ ] Installation is rejected when required assets are missing or the theme is incompatible with the core version.
- [ ] An administrator can preview an installed theme without changing the live default or per-page assignments.
- [ ] An administrator can set the default theme, and every non-overridden public page renders with it immediately
  (✔ enabling a theme applies at once; ✘ no screen sets the default theme).
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
- [ ] Only Super Admin and Admin install/uninstall, set the default, configure settings and preview themes; per-page
  selection follows the Content Manager permission model.

## Where to change things

| Change | Place |
| --- | --- |
| Which theme is active | `ThemeResolver` (never ask `IExtensionLoader` or the state directly - go through `ExtensionCatalog`) |
| Theme inheritance | `ThemeResolver.PageThemeIdAsync` / `InheritedFrom`; storing the switch: `PageService.ApplyAsync`; tests: `PublicSiteTests` |
| Which view is rendered, theme view data | `HomeController.ThemedViewAsync`; data every extension view gets: `ExtensionViewRenderer.AddExtensionViewData` |
| Theme-specific manifest checks (layouts, assets, compatibility) | `ManifestValidator` + `tests/DotNetForge.Tests/ManifestValidationTests.cs` |
| Edit the default theme | today the constant `ThemeResolver.DefaultThemeId`; a selectable default needs a stored setting read by `ThemeResolver` and an edit on a Settings screen ([admin screens](../architecture/pages.md)) |
| Theme a new public screen | call `ThemedViewAsync("<Name>", model)` in its `HomeController` action and document the view name above |
| Serve theme assets | `ExtensionResourcesController` / `ExtensionResources.Serve` (`Path.GetFullPath` + prefix check); never expose `extensions/` through `UseStaticFiles` |
| Write a theme | [extension development guide](../guides/extension-development.md); tests: `ExtensionTests` (fixture `tests/fixtures/extensions/test-theme`) |
| Per-page theme/layout fields | `Page` entity → migration → form, following [content pages and routing → Where to change things](content-pages-and-routing.md#where-to-change-things) |
| Keep admin and auth screens unthemed | `_AdminLayout`, `_AuthLayout`, `wwwroot/css/admin/` and `css/auth.css` - themes ship their own CSS and never load or replace these |
