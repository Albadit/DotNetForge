# Glossary

One term per concept. Every document under `.docs/` uses these words with exactly these meanings. Code identifiers
are written in `code` and match the source exactly.

| Term | Meaning | Where it lives in code |
| --- | --- | --- |
| **CMS** | The whole DotNetForge application (web host + `src/` libraries). | `src/DotNetForge.Web/DotNetForge.Web.csproj`, `src/*` |
| **Web host** | The ASP.NET Core MVC project `src/DotNetForge.Web`. Composition root, admin area, public site, setup. | `src/DotNetForge.Web/` (`Program.cs`, `Startup/`, `Controllers/`, `Areas/Admin/`, `Views/`) |
| **Screen** | A UI view a person opens in the browser (e.g. the Pages screen). The files in [`pages/`](pages/) each document one screen. | Razor views under `Views/` and `src/DotNetForge.Web/Areas/Admin/Views/` |
| **Content page** / `Page` | The content entity: one node in the site's page tree with a slug, SEO fields, publishing flags and a page type. Never used to mean "screen". | `src/DotNetForge.Shared/Entities/Content.cs` |
| **Page tree** | The hierarchy of content pages formed by `Page.ParentPageId`. | `PagesController.BuildTree` |
| **Pages screen** | The admin screen that manages the page tree (`/admin/pages`). Earlier versions called it "Content Manager". | `PagesController`, [Pages](pages/pages.md) |
| **Content Manager** | The admin screen for structured content (`/admin/content`): entries of every content type. Never the page tree. | `ContentController`, [Content Manager](pages/content-manager.md) |
| **Structured content** | Content defined by content types and stored as entries, independent of any page; shown on pages through content lists and read through `/api/entries`. | [structured content](features/structured-content.md), `src/DotNetForge.Content/` |
| **Content type** | The definition of one kind of structured content: a name, an API id (`Slug`), a kind and its fields. | `ContentType`, `ContentTypeService`, `ContentSchema` |
| **Collection type** / **single type** | The two kinds of content type (`ContentTypeKind`): a collection has any number of entries; a single type has exactly one (site settings, a footer). Not the permission area `Collection types`, which governs content pages. | `ContentTypeKind.Collection` / `.Single` |
| **Field** / **field key** | One input of a content type (`FieldDefinition`: field type, label, rules). Entry values are stored under its `Key` (the API name); its stable `Id` lets a renamed key keep its values. | `FieldDefinition`, `FieldTypes` |
| **Entry** | One record of a content type (`ContentEntry`). | `ContentEntry`, `ContentEntryService` |
| **Working copy** | An entry's latest saved values (`DataJson`); what the admin edits. With drafts on, it can differ from the published values (`HasDraft`). | `ContentEntry.DataJson` |
| **Published values** | The values the public site and the API show by default (`PublishedDataJson`); `null` = the entry is not published. | `ContentEntry.PublishedDataJson` |
| **Content list** | The page builder component (`collection`) that shows published entries of one content type: a list of items (item template in the layout's columns, or a carousel), a table, or one entry (fixed, or named by the URL on a detail page). | `Rendering/Collections.cs`, `ContentRenderService`, [content lists](features/structured-content.md#content-lists) |
| **Item template** | The `item` node inside a content list: the layout repeated once per entry. | `Rendering/Collections.cs` |
| **Binding** | A node's `bind` map from one of its props to a field path; the renderer fills the prop from the current entry. | `Rendering/Binding.cs`, `binding.js` |
| **Field path** | How a binding names a value: a field key, `group.member`, `relationship.field`, or `$id` / `$title` / `$url`. | `ItemData` |

| **Slug** | The URL segment of one content page. `/` is reserved for the site root page; `[name]` marks a dynamic segment. | `PageService.Slugify` |
| **Dynamic segment** | A content page whose slug is `[name]`; it matches any single URL segment under its parent and exposes the value as a route value. | `HomeController.RenderPage` |
| **Published** | The content page's one on/off switch (`Page.Published`); off is shown as **Disabled**. Not the same as live (the schedule still applies). | `Page.Published` |
| **Live** | Derived, never stored: a content page is publicly visible when `Published && !Disabled` and the current UTC time is inside its schedule window. | `PageVisibility.Live` (`src/DotNetForge.Shared/Content/PublishedPages.cs`) |
| **Schedule** | `ScheduledPublishDate` / `ScheduledUnpublishDate` on a content page (UTC). | `Page`, `ScheduledPublishingService` |
| **Installed** | The one-way state set by the setup wizard (`SystemState.Installed = true`). Before it, every request is redirected to `/setup`. | `SystemState`, `InstallationMiddleware` |
| **Setup wizard** | The `/setup` screen that creates the first Super Admin and marks the CMS installed. | `SetupController` |
| **Role** | A named RBAC role. Seven built-in roles: `Super Admin`, `Admin`, `Developer`, `Editor`, `Author`, `Authenticated`, `Public`. | `Roles`, `Role` |
| **Developer** | The built-in role for building against the API: API Tokens (its `API` grants), API Endpoints and the API docs (by role); no content, media or settings screens. | `Roles.Developer`, [authorization](features/authorization.md#the-developer-role) |
| **Admin-capable role** | `Super Admin`, `Admin`, `Developer`, `Editor` or `Author` - the built-in roles the `AdminArea` policy admits (custom roles enter too). | `AuthorizationPolicies.AdminArea`, `Roles.AdminCapable` |
| **Permission area / action** | The `(area, action)` vocabulary of the default permission matrix, e.g. `("Users", "delete")`. Seeded into `RolePermission` rows; **not** consulted at request time. | `PermissionAreas`, `PermissionActions`, `PermissionMatrix` |
| **Permission key** | A dotted API/extension scope such as `content.read`. Carried by API tokens and requested by extension manifests. | `PermissionKeys` |
| **API token** | A bearer credential for the headless API: `dnf_<8 hex>_<secret>`. Stored as a salted PBKDF2 hash plus a clear-text prefix. | `ApiToken`, `ApiTokenFactory` |
| **Headless API** | The JSON endpoints under `/api/*`, authenticated by API tokens - except custom endpoints, which use their own endpoint token or none. | `src/DotNetForge.Api/` |
| **Custom endpoint** / **endpoint token** | An API defined on the API Endpoints screen: a name and a base path under `/api/custom/`, with one or more operations. A token-protected one is called with its own endpoint token `dnfe_<secret>` (stored encrypted, one for all its operations), never with an API token; access and enabled apply to all its operations. | `CustomEndpoint`, `CustomEndpointService`, [custom endpoints](features/custom-endpoints.md) |
| **Operation** (of a custom endpoint) | One request a custom endpoint answers: an HTTP method on the endpoint's path plus an optional sub-path (`{id}`), with its own SQL, parameters, result shape, timeout and row limit. One operation per method and path shape across all endpoints. Each one is an operation in the API docs, grouped under its endpoint. | `CustomEndpointOperation`, [custom endpoints](features/custom-endpoints.md#operations) |
| **API docs** | The Scalar reference of the headless API and the custom endpoints at `/admin/api-docs`, rendered from the OpenAPI document `v1`. | `src/DotNetForge.Web/ApiDocs/`, [API docs](features/api-docs.md) |
| **Admin area** | Everything under `/admin`, rendered with `_AdminLayout` and protected by the `AdminArea` policy. | `src/DotNetForge.Web/Areas/Admin/` |
| **Public site** | The anonymous frontend: `/` and any URL resolved to a live content page. | `HomeController`, `src/DotNetForge.Web/Views/Home/` |
| **Extension** | Any folder under `extensions/` containing a `dotnetforge.extension.json` manifest, optionally with compiled code (manifest `assembly`) and views. | `ExtensionLoader`, `ExtensionRuntime` |
| **Enabled extension** | An extension switched on on the Plugins screen: an `InstalledExtensions` row with `Status = Enabled`. Every extension starts disabled. | `ExtensionState`, `ExtensionLifecycleService` |
| **Active extension** | An extension the host uses right now: valid manifest (the first with its id), enabled, and - if it has code - loaded without error. Every slot asks for active extensions only. | `ExtensionCatalog`, `ActiveExtension` |
| **Slot** | A fixed place where an active extension plugs into the host: admin tab, dashboard widget, theme, page module, endpoints, media storage, sign-in provider, audit events. | [extensions → Slots](features/extensions.md#slots) |
| **Extension SDK** | The assembly extensions compile against: `IExtension`, `ExtensionContext`, the slot helpers (`AddMediaStorage`, `AddLoginProvider`, `AddAuditEventHandler`). | `src/DotNetForge.Sdk/` |
| **Active theme** | The theme extension the public site renders with: a content page's own theme (`Page.ThemeId`) if active, else the theme a parent page hands down (`Page.ThemeForChildren`) if active, else the default theme (`ThemeResolver.DefaultThemeId`, `dotnetforge.theme.default`) if active, else the first active theme by name, else none (built-in views). | `ThemeResolver` |
| **Manifest** | The `dotnetforge.extension.json` file describing an extension; validated by `ManifestValidator`. | `ExtensionManifest` |
| **Extension type** | The manifest `type`: `theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`, `plugin`, `module`. | `ManifestValidator.ValidTypes`, `ExtensionType` |
| **Admin extension** | An extension of type `admin` with a `Views/Index.cshtml`, shown as a tab under `/admin/ext/{id}` while active. | `ExtensionsController`, `ExtensionViewController` |
| **Plugins screen** | The admin screen at `/admin/plugins` that lists extensions and enables/disables them. "Plugin" in the UI means *extension*; `plugin` is also one extension type. | `PluginsController` |
| **Design system** / **Forge** | The shared look and front-end behaviour of every built-in screen (admin, setup, sign-in, public fallback): tokens, class-based components, UI tag helpers, icon sprite, `ui.js` behaviours. | [design-system.md](architecture/design-system.md), `wwwroot/css/forge/`, `wwwroot/js/forge/`, `src/DotNetForge.Web/Ui/` |
| **Colour theme** | Light or dark appearance of the built-in screens (system preference or the user's choice, `localStorage` `dnf-theme`). Not a theme extension. | `theme.js`, `_ThemeMenu.cshtml`, `tokens.css` |
| **Toast** | A transient message in the corner of the screen, rendered by the layout from `TempData["Success"/"Error"/"Info"]` after a redirect, or by scripts via `window.Forge.toast`. | `Views/Shared/_Feedback.cshtml`, `ui.js` |
| **Navigation tree** | The one definition of the admin sidebar (sections, groups, links); the sidebar, active state and breadcrumbs are built from it per request. | `AdminNavigation.Tree`, `AdminNavigationService` |
| **Module placeholder** | An admin sidebar target that is specified but not built; renders `Modules/Placeholder.cshtml`. | `ModulesController` |
| **Database provider** | One database technology (SQLite, PostgreSQL, SQL Server, MySQL, MongoDB): an `IDatabaseProvider` registered by name. The CMS database is always PostgreSQL; the others serve **additional databases** (`DATABASES_<NAME>_PROVIDER`). Not to be confused with **storage provider**. | `src/DotNetForge.Data/Database/`, [database layer](database/architecture.md) |
| **CMS database** / **main database** | The database the CMS runs on (`DATABASE_*`) - always PostgreSQL. Further **additional databases** (`DATABASES_<NAME>_*`, any provider) hold no CMS tables; they are reached through `IDatabaseService`, custom API endpoints (SQL ones) and the Data Management export. | [database configuration](database/configuration.md) |
| **Database command** | A structured, provider-neutral operation (`DatabaseCommand`: Find, Insert, Update, Delete, Count, Aggregate) executed by `IDatabaseService`. | [query routing](database/query-routing.md) |
| **Object storage** | Where runtime files (uploaded media) live: an S3-compatible bucket or, in development, a local directory - or a storage-connector extension's backend - always behind `IFileStorage`. | `src/DotNetForge.Abstractions/Storage/IFileStorage.cs`, `ExtensionMediaStorage` |
| **Storage key** | The provider-neutral path of a stored object, generated by the application (`{yyyy}/{MM}/{random}{ext}`) and validated by `StorageKey`; stored in `MediaFile.RelativePath`. Never contains user input. | `StorageKey`, `MediaService` |
| **Deployment directory** / **content root** | The folder the app is deployed to (`/app` in the image); `src/DotNetForge.Web` when run from a checkout. Read-only at runtime outside Development. | `IWebHostEnvironment.ContentRootPath` |
| **Setting** | A key/value row; one value per key (`Key` is unique). | `Setting`, `SettingsController` |
| **Audit entry** | One append-only `AuditLogEntry` row written by `AuditService.LogAsync`. | `AuditService` |
| **Planned section** | The `## Planned (not implemented)` section at the end of a document: target behaviour that is not built. Everything outside it describes the code as it is. | `.docs/**/*.md` |

## Words to avoid

| Don't write | Write instead | Why |
| --- | --- | --- |
| "page" for a UI view | **screen** | `Page` is the content entity. |
| "plugin" for any extension | **extension** (and `plugin` only for the manifest type) | Only one of nine types is `plugin`. |
| "published" for publicly visible | **live** | `Published` is only intent; see [Content pages and public routing](features/content-pages-and-routing.md). |
| "installed" for an extension that is switched on | **enabled** (switched on) / **active** (enabled and running) | There is no install step; every extension under `extensions/` is discovered. |
| "theme" for light/dark mode | **colour theme** | A **theme** is a public-site extension; light/dark is the design system's colour theme. |
| "permission check" for the admin role gate | **role check** / **`AdminArea` policy** | The admin area checks role names, not `(area, action)` permissions. |
