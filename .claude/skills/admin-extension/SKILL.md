---
name: admin-extension
description: >-
  Creates or changes a DotNetForge CMS extension under extensions/ - especially an admin-type extension that appears
  as an admin sidebar tab and renders its own Razor views (Views/Index.cshtml, Shared/_Layout.cshtml,
  Views/Resources) via runtime compilation - including a valid dotnetforge.extension.json manifest. Use when asked to
  build an extension, plugin, admin tab or dashboard-like add-on without changing the core, or to fix a manifest that
  shows as invalid on the Plugins screen.
---

# Admin extension

Read first: `.docs/guides/extension-development.md` (step by step) and `.docs/features/extensions.md` (what the host
does, security model). Reference implementation: `extensions/admin/audit-dashboard/`.

## Steps

1. **Decide if an extension is right.** Extensions plug into fixed slots ([extensions → Slots](../../../.docs/features/extensions.md#slots)):
   `admin` = sidebar tab + iframe (this skill), `widget` = dashboard panel, `theme` = public views, `module` = content
   on chosen pages, and any type with code can add endpoints, services, media storage, a sign-in provider or audit
   handlers. For a non-admin type or for code (`assembly` + `IExtension`), follow the guide. If the feature needs a
   slot that does not exist, that is a core change, not an extension.
2. **Folder** - `extensions/admin/<short-name>/`.
3. **Manifest** - `dotnetforge.extension.json` with the eight required fields (`id`, `name`, `description`,
   `version` semver, `type: "admin"`, `author`, `entryPoint`, `permissions` - non-empty lower-case dotted keys,
   preferably from `PermissionKeys`). Use a unique, namespaced `id` (`company.admin.name`); the tab URL is
   `/admin/ext/<id>`. `routes` is informational only. Put configuration in `settings`; secrets never go in the
   manifest (`ExtensionContext.GetEnvironmentValue` reads them from the environment / `.env`).
4. **Views** - `Views/_ViewStart.cshtml` (`Layout = "Shared/_Layout.cshtml"`), `Views/Shared/_Layout.cshtml` (full
   HTML document linking `@ViewData["ResourceBase"]/...`), `Views/Index.cshtml` (required),
   `Views/_ViewImports.cshtml` for `@using`/`@inject`. Read settings from `ViewData["Settings"]`
   (`IDictionary<string, object?>`; booleans arrive as `bool`, numbers as `long`/`double`) or the typed getters of
   `ViewData["Extension"]` (`DotNetForge.Sdk.ExtensionContext`).
   To look like the admin (recommended), reuse the host's design system
   ([design-system.md](../../../.docs/architecture/design-system.md)): in `_Layout.cshtml` load `<script src="/js/forge/theme.js">`
   in `<head>` and link the host's `/css/forge/` files `tokens.css`, `base.css`, `components.css` before your own CSS; in
   `_ViewImports.cshtml` add `@using DotNetForge.Web.Ui` + `@addTagHelper *, DotNetForge.Web` for `<icon>`,
   `<badge>`, `<stat-card>`, `<timestamp>`, `<empty-state>`, `<alert>`. Use the component classes (`.card`,
   `table.table`, `.btn`, ...) instead of restyling them. Model: `extensions/admin/audit-dashboard`.
5. **Assets** - only under `Views/Resources/`; supported types css, js, json, svg, png, jpg/jpeg, gif, webp, ico,
   woff2 (others are served as `application/octet-stream`). No inline script or style (CSP).
6. **Data access** - `@inject` gives full host access. If you query tenant data, filter by the user's `dnf:tenant`
   claim (`User.FindFirst("dnf:tenant")`); read-only unless the task explicitly needs writes. Never expose hashes or
   secrets.
7. **Check** - run the app, sign in as Super Admin, confirm **Plugins** shows the extension's card without a problem alert,
   click **Enable** (every extension starts disabled; nothing shows before), then confirm the **Extensions** sidebar
   group shows the tab, the iframe renders, and `/admin/ext/<id>/resources/...` serves the assets. With code: build
   it and restart the app first (code loads only at startup).
8. **Docs** - if the extension ships with the repo, add it to the samples table in `.docs/features/extensions.md` and
   to `extensions/README.md`. If you changed host behaviour (`ExtensionViewController`, `ExtensionCatalog`,
   `ExtensionLoader`, `ManifestValidator`, a slot), update `.docs/features/extensions.md`,
   `.docs/pages/extension-host.md` and the guide.

## Don't

- Add code to the core to special-case one extension.
- Resolve files from request input in host code without the `Path.GetFullPath` + prefix check used in
  `ExtensionResources.Serve`.
- Decide "is this extension active?" anywhere but `ExtensionCatalog`.
- Assume manifest `permissions` restrict anything - they are not enforced; enabled admin tabs are open to every
  admin-capable role.
