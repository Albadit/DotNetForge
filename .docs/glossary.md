# Glossary

One term per concept. Every document under `.docs/` uses these words with exactly these meanings. Code identifiers
are written in `code` and match the source exactly.

| Term | Meaning | Where it lives in code |
| --- | --- | --- |
| **CMS** | The whole DotNetForge application (web host + `src/` libraries). | `src/DotNetForge.Web/DotNetForge.Web.csproj`, `src/*` |
| **Web host** | The ASP.NET Core MVC project `src/DotNetForge.Web`. Composition root, admin area, public site, setup. | `src/DotNetForge.Web/` (`Program.cs`, `Startup/`, `Controllers/`, `Areas/Admin/`, `Views/`) |
| **Screen** | A UI view a person opens in the browser (e.g. the Content Manager screen). The files in [`pages/`](pages/) each document one screen. | Razor views under `Views/` and `src/DotNetForge.Web/Areas/Admin/Views/` |
| **Content page** / `Page` | The content entity: one node in a tenant's page tree with a slug, SEO fields, publishing flags and a page type. Never used to mean "screen". | `src/DotNetForge.Shared/Entities/Content.cs` |
| **Page tree** | The hierarchy of content pages formed by `Page.ParentPageId`. | `ContentController.BuildTree` |
| **Slug** | The URL segment of one content page. `/` is reserved for the site root page; `[name]` marks a dynamic segment. | `PageService.Slugify` |
| **Dynamic segment** | A content page whose slug is `[name]`; it matches any single URL segment under its parent and exposes the value as a route value. | `HomeController.RenderPage` |
| **Published** | The author's intent flag on a content page (`Page.Published`). Not the same as live. | `Page.Published` |
| **Live** | Derived, never stored: a content page is publicly visible when `Published && !Disabled` and the current UTC time is inside its schedule window. | `HomeController.Live` |
| **Schedule** | `ScheduledPublishDate` / `ScheduledUnpublishDate` on a content page (UTC). | `Page`, `ScheduledPublishingService` |
| **Installed** | The one-way state set by the setup wizard (`SystemState.Installed = true`). Before it, every request is redirected to `/setup`. | `SystemState`, `InstallationMiddleware` |
| **Setup wizard** | The `/setup` screen that creates the first Super Admin and marks the CMS installed. | `SetupController` |
| **Tenant** | A site record. Every scoped entity has a `TenantId`. Today exactly one tenant (slug `default`) is seeded and used; there is no tenant resolution. | `Tenant`, `DataSeeder` |
| **Active tenant** | The tenant id carried in the signed-in user's `dnf:tenant` claim (admin) or in the API token's `dnf:tenant` claim (API). | `AdminControllerBase.TenantId`, `ApiControllerBase.TenantId` |
| **Role** | A named RBAC role. Six built-in roles: `Super Admin`, `Admin`, `Editor`, `Author`, `Authenticated`, `Public`. | `Roles`, `Role` |
| **Admin-capable role** | `Super Admin`, `Admin`, `Editor` or `Author` - the roles the `AdminArea` policy admits. | `DependencyRegistration.AdminAreaPolicy` |
| **Permission area / action** | The `(area, action)` vocabulary of the default permission matrix, e.g. `("Users", "delete")`. Seeded into `RolePermission` rows; **not** consulted at request time. | `PermissionAreas`, `PermissionActions`, `PermissionMatrix` |
| **Permission key** | A dotted API/extension scope such as `content.read`. Carried by API tokens and requested by extension manifests. | `PermissionKeys` |
| **API token** | A bearer credential for the headless API: `dnf_<8 hex>_<secret>`. Stored as a salted PBKDF2 hash plus a clear-text prefix. | `ApiToken`, `ApiTokenFactory` |
| **Headless API** | The JSON endpoints under `/api/*`, authenticated only by API tokens. | `src/DotNetForge.Api/` |
| **Admin area** | Everything under `/admin`, rendered with `_AdminLayout` and protected by the `AdminArea` policy. | `src/DotNetForge.Web/Areas/Admin/` |
| **Public site** | The anonymous frontend: `/` and any URL resolved to a live content page. | `HomeController`, `src/DotNetForge.Web/Views/Home/` |
| **Extension** | Any folder under `extensions/` containing a `dotnetforge.extension.json` manifest. | `ExtensionLoader` |
| **Manifest** | The `dotnetforge.extension.json` file describing an extension; validated by `ManifestValidator`. | `ExtensionManifest` |
| **Extension type** | The manifest `type`: `theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`, `plugin`, `module`. | `ManifestValidator.ValidTypes`, `ExtensionType` |
| **Admin extension** | An extension of type `admin` with a `Views/Index.cshtml`; the only type the CMS renders today. | `ExtensionsController`, `ExtensionViewController` |
| **Plugins screen** | The admin screen at `/admin/plugins` that lists extensions. "Plugin" in the UI means *extension*; `plugin` is also one extension type. | `PluginsController` |
| **Module placeholder** | An admin sidebar target that is specified but not built; renders `Modules/Placeholder.cshtml`. | `ModulesController` |
| **Object storage** | Where runtime files (uploaded media) live: an S3-compatible bucket or, in development, a local directory - always behind `IFileStorage`. | `src/DotNetForge.Abstractions/Storage/IFileStorage.cs` |
| **Storage key** | The provider-neutral path of a stored object, generated by the application (`{tenant}/{yyyy}/{MM}/{random}{ext}`) and validated by `StorageKey`; stored in `MediaFile.RelativePath`. Never contains user input. | `StorageKey`, `MediaService` |
| **Deployment directory** / **content root** | The folder the app is deployed to (`/app` in the image); `src/DotNetForge.Web` when run from a checkout. Read-only at runtime outside Development. | `IWebHostEnvironment.ContentRootPath` |
| **Setting** | A key/value row, either global (`TenantId == null`) or tenant-scoped. | `Setting`, `SettingsController` |
| **Audit entry** | One append-only `AuditLogEntry` row written by `AuditService.LogAsync`. | `AuditService` |
| **Planned section** | The `## Planned (not implemented)` section at the end of a document: target behaviour that is not built. Everything outside it describes the code as it is. | `.docs/**/*.md` |

## Words to avoid

| Don't write | Write instead | Why |
| --- | --- | --- |
| "page" for a UI view | **screen** | `Page` is the content entity. |
| "site" for a tenant | **tenant** | The code calls it `Tenant`; "site" is ambiguous with the public site. |
| "plugin" for any extension | **extension** (and `plugin` only for the manifest type) | Only one of nine types is `plugin`. |
| "published" for publicly visible | **live** | `Published` is only intent; see [Content pages and public routing](features/content-pages-and-routing.md). |
| "permission check" for the admin role gate | **role check** / **`AdminArea` policy** | The admin area checks role names, not `(area, action)` permissions. |
