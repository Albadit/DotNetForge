# Extension development

How to create an extension **for the current build**. What the host does with extensions is described in
[features/extensions.md](../features/extensions.md); read its security notes first. The target authoring workflow
(DI-loaded entry points, every extension type, packaging, install, update) is under
[Planned](#planned-not-implemented) below and is not implemented.

## What works today

| You want | Status |
| --- | --- |
| An admin tab with its own Razor UI | ✔ `admin` extension (below) |
| Your extension listed on **Plugins** with manifest validation | ✔ any type |
| A theme that styles the public site | ✘ manifest is validated only; no theme loading |
| A page-builder module | ✘ no page builder |
| A dashboard widget | ✘ dashboard has no widget host |
| An OAuth provider, storage connector, provider, library, plugin | ✘ no assembly loading or DI registration |

The interfaces in `src/DotNetForge.Abstractions/Extensions/IExtensionPoints.cs` (`IThemeExtension`,
`IModuleExtension`, `IWidgetExtension`, ...) are the intended contracts for those types, but nothing loads
implementations yet.

## 1. Write the manifest

Create `extensions/<type-folder>/<your-folder>/dotnetforge.extension.json`:

```json
{
  "id": "yourcompany.admin.hello",
  "name": "Hello",
  "description": "A hello tab in the admin area.",
  "version": "1.0.0",
  "type": "admin",
  "author": "Your Company",
  "entryPoint": "HelloAdminExtension",
  "permissions": ["core.read"],
  "settings": { "greeting": "Hello from an extension" }
}
```

Rules (`ManifestValidator`): the eight fields `id`, `name`, `description`, `version`, `type`, `author`,
`entryPoint`, `permissions` are required; `version` is semver (`1.0.0`, `1.0.0-beta.1`); `type` is one of `theme`,
`authentication`, `connector`, `library`, `admin`, `widget`, `provider`, `plugin`, `module`; `permissions` is a
non-empty list of lower-case dotted keys. `website`, `license`, `dependencies`, `routes` and `settings` are optional.
Comments and trailing commas are allowed. Use a globally unique `id` - duplicates are not detected.

Check it: open **Plugins** (Super Admin) - your row should show **valid**. Discovery is cached; a file watcher on
`extensions/` refreshes it when a manifest is added, changed or removed, so no restart is needed in a development
checkout.

## 2. Add views (admin extensions only)

```text
extensions/admin/hello/
├── dotnetforge.extension.json
└── Views/
    ├── _ViewImports.cshtml        optional: @using / @inject
    ├── _ViewStart.cshtml          @{ Layout = "Shared/_Layout.cshtml"; }
    ├── Index.cshtml               required
    ├── Shared/_Layout.cshtml      a complete HTML document
    └── Resources/
        ├── css/index.css
        └── js/index.js
```

`Views/Shared/_Layout.cshtml`:

```cshtml
@{ var resourceBase = ViewData["ResourceBase"] as string ?? string.Empty; }<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>@ViewData["Title"]</title>
    <link rel="stylesheet" href="@resourceBase/css/index.css" />
</head>
<body>
    @RenderBody()
    <script src="@resourceBase/js/index.js"></script>
</body>
</html>
```

`Views/Index.cshtml`:

```cshtml
@{
    ViewData["Title"] = "Hello";
    var settings = ViewData["Settings"] as IDictionary<string, object?> ?? new Dictionary<string, object?>();
    var greeting = settings.TryGetValue("greeting", out var g) && g is string s ? s : "Hello";
}
<main><h1>@greeting</h1></main>
```

What the host gives your views:

| `ViewData` key | Value |
| --- | --- |
| `Settings` | manifest `settings` as `Dictionary<string, object?>`; JSON numbers → `long`/`double`, booleans → `bool`, strings → `string`, arrays/objects → their JSON text |
| `ResourceBase` | `/admin/ext/{id}/resources` - prefix for files in `Views/Resources/` |

Views can `@inject` any host service (e.g. `DotNetForge.Data.DotNetForgeDbContext`, `IExtensionLoader`). Doing so
couples your extension to host internals and bypasses tenant scoping and permissions - filter by tenant yourself if
you query tenant data.

### Rules for extension views

- **No inline script or style.** Every response - including your iframe document - carries the CSP
  `script-src 'self'; style-src 'self'` ([security](../features/security.md)). Inline `<script>` blocks, `<style>`
  blocks, `style="..."` attributes and `on...=` handlers are blocked by the browser outside Development (in
  Development the header is report-only, so a page that works locally can break in production - check the browser
  console for CSP reports). Put CSS and JS in `Views/Resources/` and link them through `ResourceBase`; pass data to
  scripts with `data-*` attributes. Images may also come from `data:` and any `https:` origin.
- **Never write files at runtime.** The deployment directory - `extensions/` included - is read-only in production
  ([deployment](deployment.md#read-only-deployment-requirements)). Don't write to your extension folder, the content
  root or `storage/`; keep state in the database or in object storage.
- **Storing files:** inject the host's `IFileStorage` (the S3-compatible bucket; a local folder in Development
  without S3 settings - see [media storage](../features/media-storage.md#storage-architecture)):

  ```cshtml
  @inject DotNetForge.Abstractions.Storage.IFileStorage Storage
  ```

  `SaveAsync(key, stream, contentType)`, `OpenReadAsync(key)` (`StoredFile?` - `null` when missing),
  `DeleteAsync(key)` (idempotent), `GetDownloadUrlAsync(key, contentDisposition, lifetime)` (`null` for local
  storage - stream the file yourself). Keys must pass `StorageKey.IsValid` (1-512 characters, `/`-separated segments
  of letters, digits, `.`, `_`, `-`); use your own prefix such as `ext/<your-id>/...` so you never touch media keys
  (`<tenantId>/<yyyy>/<MM>/...`). Never build a key from user input unchecked. Writes from a view should be rare -
  views render on GET.

## 3. Open it

Reload any admin screen: an **Extensions** sidebar group appears with **Hello** linking to
`/admin/ext/yourcompany.admin.hello`. Edits to `.cshtml` files are recompiled on the next request (in memory -
nothing is written to disk); CSS/JS are served as-is (watch browser caching).

Resource files are served only from `Views/Resources/`, with content types for css, js, json, svg, png, jpg/jpeg,
gif and woff2.

## 4. Ship it

`extensions/` (at the repository root) is part of the app: `src/DotNetForge.Web/DotNetForge.Web.csproj` copies it
to the publish output (`<None Include="../../extensions/**" LinkBase="extensions" .../>`), so `dotnet publish` and
`docker/Dockerfile` include your folder. Extension views are not precompiled; they compile in memory on first use. In a read-only
deployment an extension is added, changed or removed only by redeploying ([deployment](deployment.md#docker)).

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No sidebar entry | manifest valid? `type` exactly `admin`? JSON parses? (Plugins screen shows invalid rows) |
| 404 in the iframe | `Views/Index.cshtml` exists? URL uses the manifest **id**, not the folder name or `routes` |
| Compilation error inside the iframe | Razor error in your views; `_ViewImports.cshtml` `@using`s |
| Unstyled page | `_ViewStart.cshtml` sets the layout; link paths use `@ViewData["ResourceBase"]` |
| Styles or scripts work locally but not in production | inline `<style>`, `style="..."`, `<script>` or `on...=` - blocked by the CSP; move them to `Views/Resources/` |
| Extension missing after publish | the folder is under `extensions/` (copied by the csproj), not excluded by `docker/Dockerfile.dockerignore` |
| `IOException` / `UnauthorizedAccessException` in production | the view writes to disk; use `IFileStorage` or the database |

## Reference sample

`extensions/admin/audit-dashboard/` - injects the DbContext, reads `settings.showFailedLogins`, uses a layout and
CSS/JS resources (no inline script or style). Its audit query is not tenant-scoped - filter by `TenantId` in your own
extensions. The other folders under `extensions/` are manifest-only samples of each type.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
The host side (lifecycle, marketplace, loading) is described in [extensions](../features/extensions.md).

### Requirements

**General workflow:** create a folder under the type's `extensions/` subfolder → add a valid manifest (✔ today) →
implement `entryPoint` against the type's interface in `DotNetForge.Abstractions.Extensions`, resolved through DI →
declare the permissions it needs → test, package, install.

**Per type:**

| Manifest `type` | Folder | Interface | Target behaviour |
| --- | --- | --- | --- |
| `theme` | `extensions/themes/` | `IThemeExtension` (`Layouts`) | layouts, page templates and static assets; theme settings in the manifest `settings` (e.g. `supportsLayouts`, `supportsDarkMode`); preview, per-page selection, default site theme. Affects only public content pages ([themes](../features/themes.md)). |
| `module` | `extensions/modules/` | `IModuleExtension` (`Render(config)`) | Razor output placed on content pages by the page builder next to the core's built-in Razor module; may be a dynamic-route target; declares the content permissions it reads or writes. |
| `widget` | `extensions/widgets/` | `IWidgetExtension` (`Render()`) | once installed and enabled, a placeable dashboard tile; declares the data permissions it reads ([dashboard](../pages/dashboard.md)). |
| `authentication` | `extensions/authentication/` | `IAuthenticationProviderExtension` (`ProviderKey`) | provider settings (client ID/secret, callback URLs); listed among providers with Name, Status and a Settings/Edit action ([authentication](../features/authentication.md)). |
| `connector` | `extensions/connectors/` | `IConnectorExtension` (storage connectors: `IFileStorage`) | external services, mainly media storage backends; selectable wherever its capability is used; declares e.g. `media.read`, `media.upload` ([media storage](../features/media-storage.md)). |

- Separate how-tos for themes and modules (the spec's `theme-development.md` and `module-development.md`) once those
  types are loadable.

**Packaging:** one archive with `dotnetforge.extension.json` at its root, the compiled entry point and all assets;
build first so `entryPoint` is compiled; verify the archive installs cleanly into an empty `extensions/` subfolder;
the layout matches what the marketplace and installer expect.

**Installing:**

| Step | Today |
| --- | --- |
| Provide the extension: Marketplace, package/manifest upload, or folder placement | folder placement only, shipped with the app (discovered when the file watcher refreshes the cache; in a read-only deployment that means redeploying) |
| Validate the manifest before installing; reject invalid, unsafe or incompatible extensions and never load them | ✔ validated and listed; invalid ones are still listed, nothing is installed or loaded |
| Register the declared permissions | ✘ |
| Enable to activate; disable, configure, remove from the Plugins/Marketplace screens | ✘ ([plugins](../pages/plugins.md) is read-only) |

**Updating:** the CMS checks for extension updates and shows them in the admin; an update replaces only that
extension's files under `extensions/`; the new manifest is validated first; a backup is taken before, and the previous
version is restored if the update fails. Core updates never overwrite `extensions/`
([transfer and updates](../features/transfer-and-updates.md)).

### Rules and validation

- Use the manifest `type` values exactly as `ManifestValidator` accepts them: the spec's "authentication provider"
  is `authentication`.
- Permission names come from `PermissionKeys` (the spec's `media.write` is `media.upload` here).
- A manifest has exactly one, correctly spelled `version`. A misspelled `vrsion` is ✔ rejected (`version` missing);
  a duplicated `version` key is not detected - `System.Text.Json` keeps the last value.
- Authentication-provider secrets live in `.env`, never in code, manifests or Git.
- Themes never affect the admin area, the setup screen or (unless configured) the login screen.

### Acceptance criteria

- [x] Creating an extension is documented, including the `extensions/` location and a valid manifest (this guide).
- [x] The documented manifest has exactly the required fields `id`, `name`, `description`, `version`, `type`,
  `author`, `entryPoint`, `permissions` and a single `version` (this guide, `ManifestValidator`).
- [ ] Creating a theme is documented, and themes never affect the admin area.
- [ ] Creating a module is documented, including page-builder availability and the built-in Razor module.
- [ ] Creating a dashboard widget is documented.
- [ ] Creating an authentication provider is documented, with secrets in `.env`.
- [ ] Creating a connector is documented, including external storage connectors for media.
- [ ] Packaging, installing and updating an extension are each documented, including manifest validation before
  install/update and isolation from core updates.
