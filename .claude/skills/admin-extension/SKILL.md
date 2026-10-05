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

1. **Decide if an extension is right.** Only `type: "admin"` extensions do anything at runtime (a sidebar tab + iframe).
   Other types are validated and listed only. If the feature needs to change core behaviour, it is a core change, not
   an extension.
2. **Folder** - `extensions/admin/<short-name>/`.
3. **Manifest** - `dotnetforge.extension.json` with the eight required fields (`id`, `name`, `description`,
   `version` semver, `type: "admin"`, `author`, `entryPoint`, `permissions` - non-empty lower-case dotted keys,
   preferably from `PermissionKeys`). Use a unique, namespaced `id` (`company.admin.name`); the tab URL is
   `/admin/ext/<id>`. `routes` is ignored. Put configuration in `settings`.
4. **Views** - `Views/_ViewStart.cshtml` (`Layout = "Shared/_Layout.cshtml"`), `Views/Shared/_Layout.cshtml` (full
   HTML document linking `@ViewData["ResourceBase"]/...`), `Views/Index.cshtml` (required),
   `Views/_ViewImports.cshtml` for `@using`/`@inject`. Read settings from `ViewData["Settings"]`
   (`IDictionary<string, object?>`; booleans arrive as `bool`, numbers as `long`/`double`).
5. **Assets** - only under `Views/Resources/`; supported types css, js, json, svg, png, jpg/jpeg, gif, woff2.
6. **Data access** - `@inject` gives full host access. If you query tenant data, filter by the user's `dnf:tenant`
   claim (`User.FindFirst("dnf:tenant")`); read-only unless the task explicitly needs writes. Never expose hashes or
   secrets.
7. **Check** - run the app, sign in, confirm **Plugins** shows the manifest as **valid**, the **Extensions** sidebar
   group shows the tab, the iframe renders, and `/admin/ext/<id>/resources/...` serves the assets.
8. **Docs** - if the extension ships with the repo, add it to the samples table in `.docs/features/extensions.md`.
   If you changed host behaviour (`ExtensionViewController`, `ExtensionLoader`, `ManifestValidator`), update
   `.docs/features/extensions.md`, `.docs/pages/extension-host.md` and the guide.

## Don't

- Add code to the core to special-case one extension.
- Resolve files from request input in host code without the `Path.GetFullPath` + prefix check used in
  `ExtensionViewController.Resource`.
- Assume manifest `permissions` restrict anything - they are not enforced.
