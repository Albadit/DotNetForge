# Codebase architecture

The authoritative technical map of DotNetForge CMS: what each project and folder is for, how the layers talk, and
where a change belongs. Every name below matches the code. For request-level flows see
[data-flow.md](data-flow.md); for the UI layer see [pages.md](pages.md); for references and packages see
[dependencies.md](dependencies.md); for tables see [database.md](database.md); for running it in production see the
[deployment guide](../guides/deployment.md).

## What the application is responsible for

DotNetForge CMS is a server-rendered ASP.NET Core MVC content-management system on .NET 10. In its current state it:

- gates the whole site behind a one-time **setup wizard** that creates the first Super Admin
  ([installation](../features/installation.md));
- serves an **admin area** at `/admin` (cookie sign-in, role-gated) to manage a **page tree**, upload and delete
  **media**, view users, roles, audit entries and extensions, edit key/value settings and issue **API tokens**;
- serves a **public site** that resolves any URL to a *live* content page by walking the page tree
  ([content pages and routing](../features/content-pages-and-routing.md)), and serves media files at
  `/media/{id}/{fileName}` ([media storage](../features/media-storage.md));
- exposes a token-secured, rate-limited **headless API** under `/api` ([headless API](../features/headless-api.md));
- discovers and validates **extensions** on disk, loads their code at startup, lets a Super Admin enable them, and
  plugs enabled ones into fixed slots - admin tabs, dashboard widgets, the public theme, page modules, endpoints,
  media storage, sign-in providers, audit handlers ([extensions](../features/extensions.md));
- runs one background job that finalizes **publish schedules** ([scheduled publishing](../features/scheduled-publishing.md));
- runs from a **read-only deployment directory**: database rows, uploaded files and the Data Protection key ring all
  live outside it ([deployment](../guides/deployment.md#read-only-deployment-requirements)).

Much of the target product ([product overview](../product.md): webhook delivery, email, i18n,
per-page themes, page builder) has data models or contracts in place but no behaviour. Each feature document lists its target
behaviour under **Planned (not implemented)**; the gap is tracked in
[implementation-status.md](../implementation-status.md). The target architecture is in
[Planned](#planned-not-implemented) below.

## Technologies

| Concern | Technology | Where |
| --- | --- | --- |
| Runtime | .NET 10 (`net10.0`), SDK pinned to `10.0.100` with `rollForward: latestFeature` | `Directory.Build.props`, `global.json` |
| Web framework | ASP.NET Core MVC: controllers + Razor views, one MVC **Area** (`Admin`), view components, tag helpers | `src/DotNetForge.Web` |
| Runtime view compilation | `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` (always on) - compiles extension views from `extensions/` **in memory** | `DependencyRegistration` |
| Extension code | one non-collectible `AssemblyLoadContext` per extension (`ExtensionLoadContext`), host assemblies shared; extensions compile against `DotNetForge.Sdk` | `src/DotNetForge.Extensions`, `src/DotNetForge.Sdk` |
| ORM | EF Core 10 on PostgreSQL (the CMS database, one migration set); provider registry + `IDatabaseService` for additional SQLite, PostgreSQL, SQL Server, MySQL or MongoDB databases ([database layer](../database/architecture.md)) | `src/DotNetForge.Data` |
| Auth | ASP.NET Core cookie authentication + a custom `ApiToken` bearer scheme; Data Protection key ring persisted to the database | `DependencyRegistration`, `ApiTokenAuthenticationHandler` |
| File storage | `IFileStorage`: local directory or any S3-compatible service (`AWSSDK.S3`) | `src/DotNetForge.Infrastructure/Storage` |
| Request hardening | `SecurityHeadersMiddleware` (CSP and friends), built-in rate limiter (`AddRateLimiter`) | `src/DotNetForge.Web/Middleware/`, `DependencyRegistration` |
| API docs | `Microsoft.AspNetCore.OpenApi` builds the OpenAPI document of `/api/*` and the custom endpoints; `Scalar.AspNetCore` renders it at `/admin/api-docs/reference/` ([API docs](../features/api-docs.md)) | `src/DotNetForge.Web/ApiDocs/`, `DependencyRegistration` (`AddOpenApi`) |
| Crypto | BCL only: PBKDF2-SHA256 (`Rfc2898DeriveBytes.Pbkdf2`), HMAC-SHA256, SHA-256, `RandomNumberGenerator` | `src/DotNetForge.Infrastructure/Security`, `ApiTokenAuthenticationHandler` |
| Configuration | In-house `.env` parser + environment variables, typed as `AppEnvironment` | `src/DotNetForge.Infrastructure/Configuration` |
| Frontend | The Forge design system ([design-system.md](design-system.md)): hand-written CSS (`wwwroot/css/forge/`), Razor tag helpers (`src/DotNetForge.Web/Ui/`), an SVG icon sprite and vanilla JS files (`wwwroot/js/forge/theme.js`, `ui.js`, `nav-state.js`, and one script per screen that needs one: `pages.js`, `entries.js`, `content-types.js`). Light and dark themes. No inline script or style. No bundler, no Node build, no front-end packages. | `src/DotNetForge.Web/wwwroot/`, `src/DotNetForge.Web/Ui/` |
| Page builder | Layout documents, validation and HTML/CSS rendering in `DotNetForge.PageBuilder`; the editor as plain ES modules (`wwwroot/js/builder/`) with its own styles (`wwwroot/builder/`). Ported from the Dnn.Libraries.PageEditor project ([page builder](../features/page-builder.md)). | `src/DotNetForge.PageBuilder`, `src/DotNetForge.Web/wwwroot/{js/,}builder` |
| Container | `docker/Dockerfile` (`sdk:10.0` build → `aspnet:10.0`, non-root `$APP_UID`, port 8080); `docker/compose.dev.yml` (PostgreSQL + RustFS + Mailpit), `docker/compose.app.yml` (the app built and run in Docker), one compose file per service in `docker/databases/`, `docker/s3/`, `docker/mail/` | `docker/` |
| Tests | xUnit 2.9, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) | `tests/` |
| CI | GitHub Actions: build + unit tests on Ubuntu/Windows/macOS, `dotnet format` check, integration tests on PostgreSQL + S3, additional-database jobs (SQL Server, MySQL, MongoDB), read-only container job; Dependabot | `.github/` |

## Solution shape at a glance

```mermaid
flowchart TB
    subgraph Host["DotNetForge.Web (src/DotNetForge.Web)"]
        Program["src/DotNetForge.Web/Program.cs"]
        DI["src/DotNetForge.Web/Startup/DependencyRegistration.cs"]
        MW["src/DotNetForge.Web/Middleware/ (SecurityHeaders, Installation) + Startup/ExtensionHosting.cs (extension gate)"]
        PubCtl["src/DotNetForge.Web/Controllers/ (Home, Account, ExternalLogin, Setup, MediaFiles, ExtensionView)"]
        AdminCtl["src/DotNetForge.Web/Areas/Admin/Controllers"]
        Svc["src/DotNetForge.Web/Services/ (AuthService, AuditService, PageService, MediaService, ...)"]
    end
    Api["DotNetForge.Api"]
    Ext["DotNetForge.Extensions"]
    Sdk["DotNetForge.Sdk"]
    ExtCode["extensions/**/*.csproj (loaded at runtime, not referenced)"]
    Infra["DotNetForge.Infrastructure"]
    PB["DotNetForge.PageBuilder"]
    Cnt["DotNetForge.Content"]
    Core["DotNetForge.Core"]
    Data["DotNetForge.Data"]
    Shared["DotNetForge.Shared"]
    Abs["DotNetForge.Abstractions"]

    Host --> Api & Ext & Sdk & Infra & Core & Data & Shared & Abs & PB & Cnt
    Api --> Cnt & Core & Data & Shared & Abs
    Cnt --> Core & Data & Shared & Abs
    Ext --> Sdk & Core & Shared & Abs
    Sdk --> Shared & Abs
    ExtCode -. compiles against .-> Sdk
    Infra --> Core & Shared & Abs
    Core --> Shared & Abs & PB
    Data --> Shared & Abs
    Shared --> Abs
```

Arrows are `ProjectReference`s. `Core` and `Data` are deliberate siblings: neither references the other. When domain
logic needs persistence, the contract goes in `Shared` and `Data` implements it (the `IInstallationStore` pattern).
When the API needs a web-host service, the contract also goes in `Shared` and the host implements it
(`IPageService`, `IAuditService`): `Api` resolves them from DI without referencing the host. `Api` → `Data` is a
documented shortcut: API controllers query `DotNetForgeDbContext` directly. Structured content needs the same database
rules in the admin and the API, more than a contract would carry, so they are a library of their own:
`DotNetForge.Content` (above `Core` and `Data`, referenced by the host and `Api`). `Core` references `PageBuilder`
because entries store rich text in the page builder's format. Extension projects reference only
`Sdk`, `Shared` and `Abstractions` (without copying them, `extensions/Directory.Build.props`) and are loaded by the
host at runtime; the host shares its own copies of those assemblies with them.

## Application entry points

| Entry point | What starts there |
| --- | --- |
| `src/DotNetForge.Web/Program.cs` | The only process entry point. Loads `.env` with `EnvConfigurationLoader.Load(ContentRootPath, IsDevelopment())`, registers services (`AddDotNetForge(env, builder.Environment)`), migrates + seeds the database, builds the middleware pipeline, maps routes, runs Kestrel. Exits with code `1` on a `ConfigurationException`. |
| `public partial class Program` (end of `Program.cs`) | Exposed so `WebApplicationFactory<Program>` can host the app in integration tests. |
| `src/DotNetForge.Web/Services/ScheduledPublishingService` | `BackgroundService` started by the host. |
| `src/DotNetForge.Web/Services/AuditEventDispatcher` | `BackgroundService` delivering audit events to extension handlers ([audit logging](../features/audit-logging.md#extension-handlers)). |
| `IExtension.ConfigureServices` / `MapEndpoints` of each extension | called once at startup by `DependencyRegistration.AddExtensionHost` and `await app.MapDotNetForgeExtensionsAsync()` ([extensions → Loading code](../features/extensions.md#loading-code)). |
| `src/DotNetForge.Data/DesignTimeDbContextFactory.cs` | `PostgreSqlDesignTimeDbContextFactory` - the entry point for `dotnet ef` design-time tooling (does not boot the web host or connect). |
| `docker/Dockerfile` | `ENTRYPOINT ["dotnet", "DotNetForge.Web.dll"]` with `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_HTTP_PORTS=8080`. |

Startup order is detailed in [data-flow.md → Application startup](data-flow.md#application-startup).

## Project structure

```text
DotNetForge/                         repository root
├── DotNetForge.slnx                 solution: every project below (dotnet build / dotnet test)
├── src/
│   ├── DotNetForge.Web/             web host (ASP.NET Core MVC) = content root when running
│   │   ├── DotNetForge.Web.csproj   links ../../extensions/** into the publish output
│   │   ├── Program.cs               composition + pipeline + route map
│   │   ├── Startup/
│   │   │   ├── DependencyRegistration.cs  every DI registration, extension host (AddExtensionHost), auth schemes, session validation, policies, rate limits
│   │   │   └── ExtensionHosting.cs   extension endpoint gate middleware, controller convention, MapDotNetForgeExtensionsAsync
│   │   ├── ApiDocs/
│   │   │   ├── ApiDocsEndpoints.cs   MapApiDocs: the OpenAPI document and the Scalar reference under /admin/api-docs (ApiDocs policy)
│   │   │   └── ApiDocsTransformers.cs  the OpenAPI document: /api/* with the ApiToken scheme and permission keys, the custom endpoints with their own schemes
│   │   ├── Middleware/
│   │   │   ├── SecurityHeadersMiddleware.cs  CSP (a nonce'd script-src for the Scalar page only), nosniff, X-Frame-Options, Referrer-Policy, Permissions-Policy
│   │   │   └── InstallationMiddleware.cs     redirects to /setup until installed; blocks /setup after
│   │   ├── Services/                web-host application services (need HttpContext or the DbContext)
│   │   ├── Controllers/             non-area controllers: public site, media files, account, external sign-in, setup, extension views/resources
│   │   ├── Components/              public view components (ExtensionModules)
│   │   ├── Ui/                      UI tag helpers (UiTagHelpers.cs) + presentation helpers (AuditPresentation, MediaPresentation)
│   │   ├── Areas/Admin/
│   │   │   ├── Controllers/         one controller per admin screen (+ AdminControllerBase)
│   │   │   ├── Components/          view components (dashboard widgets)
│   │   │   ├── Models/AdminViewModels.cs  every admin view model
│   │   │   ├── Navigation/          AdminNavigation.cs: the sidebar tree + AdminNavigationService
│   │   │   └── Views/               admin Razor views, _AdminLayout, _Sidebar, _NavLink
│   │   ├── Views/                   public, auth and setup Razor views + _Layout/_AuthLayout, _Feedback, _ThemeMenu
│   │   ├── wwwroot/                 static files served by UseStaticFiles: css/forge/, js/forge/, img/ (logos, icons.svg)
│   │   ├── Properties/launchSettings.json  http / https profiles
│   │   └── appsettings*.json        logging levels and AllowedHosts
│   ├── DotNetForge.Abstractions/    contracts only (no implementation, no dependencies)
│   ├── DotNetForge.Shared/          entities, enums, constants, DTOs, results, manifest model, store/service contracts
│   ├── DotNetForge.Core/            pure domain logic: installation, permission evaluation, validators, structured content rules (Content/)
│   ├── DotNetForge.Content/         structured content over the database: content types, entries, queries, API shapes
│   ├── DotNetForge.Data/            EF Core model + PostgreSQL context and migrations, database layer (Database/), seeding, stores
│   ├── DotNetForge.Infrastructure/  hashing, tokens, signing, .env loading, AppPaths, file storage (local, S3)
│   ├── DotNetForge.PageBuilder/     page builder documents: schema, validator, HTML/CSS renderer (no other references)
│   ├── DotNetForge.Extensions/      manifest validation, cached on-disk discovery, code loading (load contexts), ExtensionCatalog
│   ├── DotNetForge.Sdk/             the extension SDK: IExtension, ExtensionContext, slot helpers, host contracts
│   └── DotNetForge.Api/             headless API controllers, token auth handler, permission filter
├── tests/
│   ├── DotNetForge.Tests/           unit tests (no host, no real DB; live S3 tests opt-in)
│   ├── DotNetForge.IntegrationTests/ real host over a fresh PostgreSQL database per factory (DNF_TEST_POSTGRES)
│   ├── fixtures/extensions/         fixture extensions the integration tests load
│   ├── fixtures/page-builder/       golden page builder fixtures (input, normalized document, HTML, CSS)
│   └── js/                          page-builder-fixtures.mjs: the fixtures through the browser modules (Node)
├── extensions/<type>/<name>/        user extensions, one folder per manifest (+ optional .csproj → bin/); copied to the publish output
├── extensions/Directory.Build.props build settings shared by every extension project
├── docker/
│   ├── Dockerfile                   production image (read-only root filesystem capable); build context = repository root
│   ├── Dockerfile.dockerignore      keeps the build context to what `dotnet publish` needs
│   ├── compose.dev.yml              development: PostgreSQL + RustFS + Mailpit (the services of .env.developer)
│   ├── compose.app.yml              the app built from Dockerfile, run read-only in Production, with those services
│   ├── databases/                   postgres.yml, mongodb.yml, sqlserver.yml, mysql.yml (one service each)
│   ├── s3/                          rustfs.yml, ceph.yml, seaweedfs.yml (S3 servers with a dotnetforge bucket)
│   └── mail/                        mailpit.yml
├── .docs/                           this documentation (current behaviour + planned sections)
├── .github/                         CI workflow, Dependabot, AI-agent quick references (agents/)
├── .claude/skills/                  project skills for Claude Code
├── .vscode/                         build/run/test/migration tasks and F5 launch config
├── Directory.Build.props            net10.0, nullable, implicit usings, central package management
├── Directory.Packages.props         every NuGet version
├── global.json                      SDK pin
└── .env.example                     configuration template (copy to .env)
```

`storage/` is **not in the repository** (`.gitignore` ignores `/storage/` - the root folder only, so the
`Storage/` source folders are not matched on case-insensitive file systems). It appears only in Development, as the
default location of local media and of SQLite additional databases - see [`storage/`](#storage-development-only).

### Folder responsibilities

#### `src/DotNetForge.Web/` (the web host)

The composition root and everything that needs `HttpContext`: controllers, views, middleware, web-host services,
static files. It is the **content root** when the app runs, from a checkout or published. Repository-level folders
(`.env`, `extensions/`, the Development `storage/`) are located by `AppPaths`
(`src/DotNetForge.Infrastructure/Configuration/AppPaths.cs`): next to the app first, then the repository root (the
folder containing `DotNetForge.slnx`) - see [where the app finds its files](../guides/development.md#where-the-app-finds-its-files).
The csproj adds `../../extensions/**` (except `**/obj/**`) as `None` items with `CopyToPublishDirectory="PreserveNewest"`,
so published output carries the extensions - views and built `bin/` folders - as plain files (the Razor SDK does not
precompile them).

#### `src/DotNetForge.Web/Startup/`

- **Belongs here:** service registration (`AddDotNetForge(env, hostEnv)`), persistence and storage provider
  selection, Data Protection, the extension host (`AddExtensionHost`: discovery and code loading before the container
  is built, each extension's `ConfigureServices`), authentication schemes and cookie session validation
  (`ValidateSessionAsync`), authorization policies, rate-limit policies (`AddRateLimits`), antiforgery options,
  MVC/Razor configuration (extension assemblies as application parts and view-compilation references).
  `ExtensionHosting.cs` holds the pipeline half: `ExtensionEndpointGateMiddleware`, `ExtensionControllerConvention`,
  `ExtensionPipeline.MapDotNetForgeExtensionsAsync`.
- **Does not belong:** request-time logic, configuration parsing (that is `EnvConfigurationLoader`).
- **Depends on:** every project. **Used by:** `Program.cs`. Policy names are in `AuthorizationPolicies` (Shared);
  `DependencyRegistration.AdminAreaPolicy` is an alias kept for `AdminControllerBase`.

#### `src/DotNetForge.Web/Middleware/`

- **Belongs here:** cross-cutting request gates that run for every request.
- `SecurityHeadersMiddleware` runs right after the exception handler/HSTS (outside Development) and before
  `UseStaticFiles`, so every response - static files and re-executed error pages included - carries the headers.
  The CSP is enforced outside Development and `Content-Security-Policy-Report-Only` in Development
  ([security](../features/security.md)).
- `InstallationMiddleware` ([installation](../features/installation.md)) runs after `UseRouting` and before
  `UseAuthentication`, so an uninstalled CMS redirects even API calls to `/setup`.
- `ExtensionEndpointGateMiddleware` (in `Startup/ExtensionHosting.cs`) runs right after it: an endpoint tagged with
  `ExtensionEndpointMetadata` answers `404` while its extension is not active.

#### `src/DotNetForge.Web/Services/` (web host)

Application services that need `HttpContext`, the cookie scheme or the DbContext, so they cannot live in `Core`.

| Class | Registered as | Lifetime | Responsibility | Used by |
| --- | --- | --- | --- | --- |
| `AuthService` | self | scoped | Email/password validation with lockout; builds the cookie `ClaimsPrincipal` (user id, name, email, roles); `IsActiveAsync` (user exists and is `Enabled`). Defines `SignInStatus`, `SignInResult`. | `AccountController`, `SetupController`, `DependencyRegistration.ValidateSessionAsync` |
| `AuditService` | `IAuditService` | scoped | Appends an `AuditLogEntry` with the acting user (or `API token {id}` with `UserId = null`), IP and user agent; truncates snapshots to the column lengths. | `AccountController`, `SetupController`, `PagesController`, `SettingsController`, `ApiTokensController`, `MediaService`, `ContentApiController`, the `DotNetForge.Content` services |
| `PageService` | `IPageService` | scoped | Every content-page rule: `ApplyAsync`, `ReorderAsync`, `DeleteAsync` (validate and mutate, **never save**); static `Slugify`, `ToUtc`, `IsDynamic`. | `PagesController`, `ContentApiController` |
| `MediaService` | self | scoped | Upload rules (`AllowedTypes`, `MaxUploadBytes` 25 MB, `MaxFilesPerUpload` 10), generated storage keys, `MediaFile` rows, delete, audit; static `IsInline`, `UrlFor`. | `MediaController`; statics also by `MediaFilesController` and `Media/Index.cshtml` |
| `InstallationStatusCache` | self | singleton | Caches the one-way "installed" flag so the middleware stops querying the DB after install. | `InstallationMiddleware`, `SetupController`, integration tests |
| `ScheduledPublishingService` | hosted | singleton | Every 15 s finalizes due publish/unpublish schedules. | host |
| `ContentRenderService` | self | scoped | The entries every content list of a layout shows (queries, media, linked entries → item data); flags a missing detail entry | `HomeController`, `PageLayoutViewComponent`, `PageBuilderController` (preview), `ContentController` (list columns) |
| `EntryForm` | static | - | Reads the Content Manager's entry form (`f.` + field path) into entry values | `ContentController` |

- **Belongs here:** logic shared by several admin/public controllers that touches EF, storage or HTTP.
- **Does not belong:** pure rules with no I/O (put them in `Core/Validation` or similar), contracts for extensions
  (`Abstractions`).
- When the headless API needs a service, put its contract in `Shared`
  (`src/DotNetForge.Shared/Content/IPageService.cs`, `src/DotNetForge.Shared/Auditing/IAuditService.cs`) and
  register the host class against it. Inject the interface, never the
  concrete class.

#### `src/DotNetForge.Web/Controllers/` (non-area)

| Controller | Routes | Purpose |
| --- | --- | --- |
| `HomeController` | `GET /`, fallback `{*path:nonfile}` → `RenderPage`, `/error` (any HTTP method) | Public site and the error screen |
| `MediaFilesController` | `GET /media/{id:guid}/{fileName?}` | Media downloads: redirect to a presigned URL (S3) or stream (local). Private files only for admin-capable users, else 404. |
| `AccountController` | `/login` (GET/POST, POST rate-limited; `/account/login` → 301), `/register`, `/forgot-password`, `/reset-password`, `/confirm-email`, `/confirm-email/resend`, `/account/logout` (POST), `/account/denied` | Cookie sign-in/out, self-service accounts ([authentication](../features/authentication.md#self-service-accounts)) |
| `SetupController` | `/setup` (GET/POST, POST rate-limited) | Setup wizard; writes `cms.installed` |
| `ExtensionViewController` | `/admin/ext/{id}/raw`, `/admin/ext/{id}/resources/{**path}` | Renders an admin extension's own Razor document and assets. Lives outside the area so its views resolve from `extensions/` with no admin layout. Protected by the `AdminArea` policy. |

#### `src/DotNetForge.Web/Areas/Admin/`

The admin UI. Every controller derives from `AdminControllerBase` (`[Area("Admin")]`,
`[Authorize(Policy = "AdminArea")]`), which exposes `CurrentUserId` from claims and the permission
checks `Can(area, action)` and `CanModify(area, anyAction, ownAction, createdById)` (via `IPermissionService`). Each
controller declares an attribute `[Route("admin/...")]`. Views use `_AdminLayout` via
`src/DotNetForge.Web/Areas/Admin/Views/_ViewStart.cshtml`. All view models are in one file, `src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs`,
except `RoleListItem` and `UserListItem`, which are nested records inside their controllers; the Pages form binds
`PageInput` from `Shared/Content`. See [pages.md](pages.md) for the screen architecture.

- **Belongs here:** admin screens, their view models, admin-only view components, the navigation tree
  (`Navigation/AdminNavigation.cs` - add a screen's sidebar link there).
- **Does not belong:** business rules (put them in a `src/DotNetForge.Web/Services/` class like `PageService` or `MediaService`), API
  endpoints (`DotNetForge.Api`).

#### `Views/`, `Ui/` and `src/DotNetForge.Web/wwwroot/`

`Views/` holds the public site, setup, login and denied screens with two layouts: `_Layout` (public fallback: home,
content pages, error) and `_AuthLayout` (setup/login/denied), plus the shared partials `_Feedback` (toasts + confirm
dialog, rendered by `_AdminLayout` and `_AuthLayout`) and `_ThemeMenu`. `Ui/` holds the UI tag helpers and
presentation helpers. How these fit together, and how to add a component, token or icon:
[design-system.md](design-system.md).

| File | Loaded by |
| --- | --- |
| `wwwroot/css/forge/tokens.css`, `base.css`, `components.css` | every built-in layout |
| `wwwroot/css/forge/admin.css` / `auth.css` / `public.css` | `_AdminLayout` / `_AuthLayout` / `_Layout` |
| `wwwroot/js/forge/theme.js` | every built-in layout, synchronously in `<head>` (applies the saved light/dark choice) |
| `wwwroot/js/forge/nav-state.js` | `_AdminLayout`, synchronously right after the sidebar (restores open sidebar groups and scroll position before the first paint) |
| `wwwroot/js/forge/ui.js` | `_AdminLayout`, `_AuthLayout` (`defer`) - `data-confirm`, toasts, menus, drawer, list search/filter/sort/paging, copy, theme switch |
| `wwwroot/js/forge/pages.js` | Pages only (`@section Scripts`) |
| `wwwroot/js/forge/entries.js` | Content Manager entry list and entry form |
| `wwwroot/js/forge/content-types.js` | Content types builder (`/admin/content-types/{id}`) |
| `wwwroot/img/` | `icons.svg` (icon sprite for `<icon>`), `favicon.svg`, `logo-dark.svg` (dark sidebar, auth panel), `logo-mark.svg` (public header, auth on phones), `logo.svg` (light sidebar, README) |
| `Ui/UiTagHelpers.cs` | `@addTagHelper *, DotNetForge.Web` in both `_ViewImports.cshtml` |

No view contains inline `<script>`, `<style>`, `style=` or `on*=` handlers: the CSP (`script-src 'self'; style-src
'self'`) would block them. `AdminUiTests` checks every admin screen, sign-in and the public pages for this.

#### `src/DotNetForge.Abstractions`

Interfaces with no implementation and no project references - the surface extensions are meant to compile against.

| File | Contents |
| --- | --- |
| `Authorization/IPermissionService.cs` | `Has(role, area, action)`, `HasAny(roles, area, action)` (reference matrix), `IsGranted(roles, area, action)` (stored grants) |
| `Security/ISecurityContracts.cs` | `IDateTimeProvider`, `IPasswordHasher`, `IApiTokenFactory` + `GeneratedApiToken`, `IWebhookSigner` |
| `Messaging/IMessagingContracts.cs` | `EmailMessage`, `IEmailSender` (implemented by `SmtpEmailSender` in Infrastructure) |
| `Storage/IFileStorage.cs` | `IFileStorage` (`SaveAsync`, `OpenReadAsync` → `StoredFile?`, `DeleteAsync`, `GetDownloadUrlAsync` → `Uri?`), `StoredFile`, `StorageKey` (key validation) |
| `Database/` | `IDatabaseService`, `DatabaseCommand`, `DatabaseFilter`, `DatabaseResult`, database exceptions ([database layer](../database/architecture.md)) |

The extension entry-point contract is not here but in `DotNetForge.Sdk` (below).

**Rule:** no entities here. A contract that needs an entity goes in `Shared` (see `Shared/Stores`, `Shared/Content`).

#### `src/DotNetForge.Shared`

The shared kernel referenced by everything above `Abstractions`.

| Folder | Contents |
| --- | --- |
| `Entities/` | `User`, `Role`, `RolePermission`, `UserRole`, `Page`, `MediaFile`, `ApiToken`, `Webhook`, `WebhookDelivery`, `AuditLogEntry`, `InstalledExtension`, `SystemState`, `Setting`, `AuthProvider`, `ContentType`, `ContentEntry`, `ContentEntryValue` (`StructuredContent.cs`) |
| `Enums/Enums.cs` | `StorageProvider`, `UserStatus`, `ExtensionType`, `ExtensionStatus`, `TokenDuration`, `PageType`, `WebhookOutcome`, `ContentTypeKind` |
| `Media/MediaUrls.cs` | `MediaUrls.For(id, fileName)` - the `/media/{id}/{name}` path (admin, renderer and API) |
| `Constants/` | `Roles`, `PermissionAreas`, `PermissionActions`, `PermissionKeys`, `AuditActions`, `WebhookEvents`, `RateLimitPolicies`, `AuthorizationPolicies` |
| `Authorization/PermissionMatrix.cs` | Default `(role, area, action)` grants - consumed by `PermissionService` and `DataSeeder` |
| `Configuration/AppEnvironment.cs` | Typed `.env` values (`Database`, `AdditionalDatabases`, `StorageSettings`, `EmailSettings`), `Lookup` (any key: environment, then `.env` - for extension settings) |
| `Configuration/SqliteConnectionStrings.cs` | `GetDataSource`, `IsInMemory` - used by `SqliteDatabaseProvider` (additional databases) |
| `Content/IPageService.cs` | `IPageService`, `PagePosition`, `PageInput` (+ `PageInput.From(page)`) |
| `Content/PublishedPages.cs` | `PageVisibility.Live` (the public "live" rule), `IPublishedPages`, `PublishedPage` |
| `Auditing/IAuditService.cs` | `IAuditService.LogAsync` |
| `Dtos/SetupRequest.cs` | Setup-wizard form model |
| `Manifests/` | `ExtensionManifest`, `ValidationResult`, `ValidationError` |
| `Results/Result.cs` | `Result` success-or-error type (used by `InstallationService`) |
| `Stores/IInstallationStore.cs` | Persistence contract implemented by `Data` |

**Rule:** declarative data and contracts only - no EF Core, no HTTP.

#### `src/DotNetForge.Core`

Pure domain logic with no EF Core.

| File | Contents | Called from |
| --- | --- | --- |
| `Installation/InstallationService.cs` | Validates `SetupRequest` (email ≤ 256, names ≤ 100, `PasswordPolicy`, confirm match), hashes the password, delegates to `IInstallationStore` | `SetupController` |
| `Authorization/PermissionService.cs` | `IsGranted` evaluates the roles' stored grants (`IRoleGrantStore`; `Super Admin` always, a role without stored grants falls back to `PermissionMatrix`); `Has`/`HasAny` evaluate the matrix only | `AdminControllerBase.Can` / `CanModify` |
| `Validation/InputValidation.cs` | `EmailValidator` | `InstallationService` |
| `Validation/PasswordPolicy.cs` | Min 8 chars, ≥1 letter, ≥1 digit, not in a 12-entry common list | `InstallationService` |
| `Extensions/IExtensionContracts.cs` | `IManifestValidator`, `IExtensionLoader` (`Discover()`), `DiscoveredExtension` | `DotNetForge.Extensions`, `PluginsController` |
| `Content/ContentSchema.cs` | Field types (`FieldTypes`), `FieldDefinition`, `ContentSchema.Validate`/`Parse` (field definitions of a content type) | `DotNetForge.Content` services, admin views |
| `Content/EntryData.cs` | Entry value validation (draft vs publish), migration after field changes, title, search text, index values | `ContentEntryService` |
| `Content/ItemData.cs` | Entry → item data a content list binds to; bindable paths | `ContentRenderService`, `PageBuilderController` |
| `Content/ContentTypeTemplates.cs` | Starter content types | `ContentTypeService`, the new-type screen |

#### `src/DotNetForge.Content`

Structured content over the database ([structured content](../features/structured-content.md)), shared by the admin
screens and the headless API so both apply the same rules. References `Abstractions`, `Shared`, `Core`, `Data`.

| Class | Responsibility |
| --- | --- |
| `ContentTypeService` | create (from templates, with linked types), update (validated schema, entries migrated in the same save), delete, reorder; saves and audits |
| `ContentEntryService` | save (draft / publish), publish, unpublish, duplicate, delete, bulk delete, reorder, uniqueness, conflicts (`Version`), value index rows; saves and audits |
| `ContentQueryService` | search, filters, sorting and paging over the value index (`ContentEntryValues`) |
| `ContentApiService` | the API's JSON for types and entries (media and relationships resolved) |

All four are scoped, registered in `DependencyRegistration`.

#### `src/DotNetForge.Data`

| File | Responsibility |
| --- | --- |
| `DotNetForgeDbContext.cs` | The EF Core model: 24 `DbSet`s (incl. `DataProtectionKeys`) and all fluent configuration. Implements `IDataProtectionKeyContext`. Not sealed; a public constructor for tools and tests that only need the model. |
| `Database/` | The database layer ([architecture](../database/architecture.md)): `IDatabaseProvider`, registry, catalog, `QueryRouter`, `DatabaseService`, schema mapping, value coercion. `Providers/<Database>/` holds everything specific to one database, with no code shared between databases: the provider; for SQL databases also the dialect, SQL builder and executor; for PostgreSQL the CMS context `PostgreSqlDbContext` and its migrations; for MongoDB the executor and key generators. |
| `DatabaseInitializer.cs` | Startup: the PostgreSQL provider's `InitializeSchemaAsync` (migrations), then `DataSeeder.SeedAsync`. |
| `DataSeeder.cs` | Idempotent seed: six built-in roles + grants, starter page tree, auth-provider catalog, `SystemState` row; one-time updates by `SystemState.SeedVersion` (`UpgradeAsync`). |
| `InstallationStore.cs` | Transactional create-first-admin + installed flag. |
| `DesignTimeDbContextFactory.cs` | `PostgreSqlDesignTimeDbContextFactory` for `dotnet ef --context PostgreSqlDbContext`; reads `DATABASE_CONNECTION_STRING` from the process environment only. |
| `Database/Providers/PostgreSql/Migrations/` | The CMS migrations; the list is in [database.md → Migrations](database.md#migrations) (latest: `AddEndpointDatabase`). |

Details: [database.md](database.md).

#### `src/DotNetForge.Infrastructure`

Implementations of `Abstractions` contracts plus configuration loading. BCL only, except `AWSSDK.S3`.

| File | Implements | Used at runtime? |
| --- | --- | --- |
| `Configuration/DotEnvParser.cs`, `EnvConfigurationLoader.cs` | `.env` → `AppEnvironment`, `ConfigurationException`; outside Development refuses defaults inside the deployment directory | yes (`Program.cs`) |
| `Security/Pbkdf2PasswordHasher.cs` | `IPasswordHasher` (`pbkdf2-sha256$120000$salt$hash`) | yes |
| `Security/ApiTokenFactory.cs` | `IApiTokenFactory` (`dnf_<hex>_<secret>`, hash `60000$salt$hash`) | yes |
| `Security/SystemClock.cs` | `IDateTimeProvider` | yes |
| `Security/HmacWebhookSigner.cs` | `IWebhookSigner` | no - not registered (kept, with tests, for planned webhooks) |
| `Storage/LocalFileStorage.cs` | `IFileStorage` on a directory: atomic writes (temp sibling + move), path containment, no download URLs | Development fallback when no `STORAGE_S3_*` key is set |
| `Storage/S3FileStorage.cs` | `IFileStorage` on any S3-compatible service: private objects, presigned GET URLs with `response-content-disposition` | when `STORAGE_S3_*` is set (always outside Development) |

The host's own storage is registered as the keyed `IFileStorage` `"core"`, chosen from `AppEnvironment.Storage.Provider`;
the unkeyed `IFileStorage` is `ExtensionMediaStorage` (Web), which routes to an enabled storage-connector extension or
to the core one ([storage connectors](../features/media-storage.md#storage-connectors)). `Messaging/SmtpEmailSender.cs` is the
`IEmailSender` over SMTP (`SMTP_*`, [email](../features/email.md)). Provider choice and setup:
[media storage → provider choice](../features/media-storage.md#provider-choice).

#### `src/DotNetForge.Extensions`

| File | Responsibility |
| --- | --- |
| `ManifestValidator.cs` | required fields, type, semver, permission-key shape, `assembly` path |
| `ExtensionLoader.cs` | recursive search for `dotnetforge.extension.json` under `AppEnvironment.ExtensionsPath`; cached, invalidated by a `FileSystemWatcher` (a version counter discards scans that overlapped a change); without a watcher every call rescans |
| `ExtensionRuntime.cs` | startup code loading in dependency order, entry-type creation, `LoadedExtension` (assembly, instance, context, `Error`) |
| `ExtensionLoadContext.cs` | one `AssemblyLoadContext` per extension: host assemblies first, then dependencies' contexts, then the extension's `bin/` |
| `ExtensionCatalog.cs` | the one answer to "is it active?": `Installed()`, `Problem()`, `GetActiveAsync`, `FindActiveAsync`, `IsActiveAsync`; `ActiveExtension` |

Enabled state (`ExtensionState`), enable/disable (`ExtensionLifecycleService`) and the slot services live in the web
host because they need the database or MVC. See [extensions](../features/extensions.md).

#### `src/DotNetForge.Sdk`

The one assembly extensions compile against (references `Abstractions` and `Shared`, framework reference to ASP.NET
Core): `IExtension`, `ExtensionContext`, `ExtensionServiceCollectionExtensions` (`AddMediaStorage`,
`AddLoginProvider`, `AddAuditEventHandler`) and their contribution records, `IExtensionState`, `IAuditEventHandler` /
`AuditEvent`, `ExternalLogin.SignInScheme`. Extensions compile against it, so a change is a breaking change for every
extension ([extensions → Extension SDK](../features/extensions.md#extension-sdk)).

#### `src/DotNetForge.Api`

`ApiControllerBase` (`[ApiController]`, `[Authorize(AuthenticationSchemes = "ApiToken")]`,
`[EnableRateLimiting("api")]`), `ContentApiController` (creates pages through
`IPageService`, audits through `IAuditService`), `EntriesApiController` (content types and entries through the
`DotNetForge.Content` services), five resource controllers in `ResourceApiControllers.cs`,
`CustomEndpointsApiController` (`/api/custom/{**path}`; a plain `ControllerBase` with `[ApiController]`, `[Produces]`
and the `api` rate limit - not an `ApiControllerBase`, because each custom endpoint has its own token instead of an
API token, [custom endpoints](../features/custom-endpoints.md)),
`ApiTokenAuthenticationHandler` + `ApiTokenDefaults`, and `RequireApiPermissionAttribute`. Mounted into the host with
`AddApplicationPart(typeof(ContentApiController).Assembly)`. See [headless API](../features/headless-api.md).

#### `tests/`

`DotNetForge.Tests` references the `src/` libraries (not the host or `Api`). `DotNetForge.IntegrationTests`
references the host, and - with `ReferenceOutputAssembly="false"`, only so they are built - the fixture extension
`tests/fixtures/extensions/test-kit` and the samples it loads. See [testing guide](../guides/testing.md).

#### `extensions/`

One folder per extension type (`admin`, `authentication`, `connectors`, `libraries`, `modules`, `plugins`,
`providers`, `themes`, `widgets`), each with one runnable sample ([extensions → Samples](../features/extensions.md#samples-in-the-repository));
seven have a `.csproj` (in `DotNetForge.slnx`, folder `/extensions/`) that builds into the extension's own `bin/`
using `extensions/Directory.Build.props`. The folder name is not used by the loader - only the manifest `type`
matters. The folder is read-only at runtime. A CMS user works only here: the core in `src/` is never edited to add
an extension.

#### `storage/` (Development only)

Not in the repository and never written outside Development. In Development, `EnvConfigurationLoader` defaults local
media to `storage/media` and `SqliteDatabaseProvider` defaults a SQLite additional database to `storage/<name>.db`
(under the repository root in a checkout, [AppPaths](../guides/development.md#where-the-app-finds-its-files)); the
folders are created on first use. The CMS database is PostgreSQL (in Development `localhost:5432` by default).
Outside Development `DATABASE_CONNECTION_STRING` and the `STORAGE_S3_*` settings are required - see
[deployment](../guides/deployment.md#environment-variables).

## Domain boundaries

| Domain | Entities | Owning code | Screens / endpoints |
| --- | --- | --- | --- |
| Installation | `SystemState` | `InstallationService`, `InstallationStore`, `InstallationMiddleware`, `InstallationStatusCache` | [Setup](../pages/setup.md) |
| Identity & access | `User`, `Role`, `RolePermission`, `UserRole`, `AuthProvider` | `AuthService`, `AccountController`, `PermissionMatrix`, `PermissionService`, `AdminControllerBase.Can`/`CanModify` | [Login](../pages/login.md), [Users](../pages/users.md), [Roles](../pages/roles.md) |
| Pages | `Page` | `IPageService`/`PageService`, `PagesController`, `ContentApiController`, `HomeController`, `ScheduledPublishingService` | [Pages](../pages/pages.md), [public site](../pages/public-home.md) |
| Structured content | `ContentType`, `ContentEntry`, `ContentEntryValue` | `Core/Content`, `DotNetForge.Content`, `ContentController`, `ContentTypesController`, `ContentRenderService`, `EntriesApiController` | [Content Manager](../pages/content-manager.md), [Content types](../pages/content-types.md), `/api/entries` |
| Media | `MediaFile` (+ objects in `IFileStorage`) | `MediaService`, `MediaController`, `MediaFilesController`, `MediaApiController` (read-only) | [Media](../pages/media.md), `/media/{id}/{fileName}` |
| Configuration | `Setting` | `SettingsController` | [Settings](../pages/settings.md) |
| Headless API | `ApiToken`, `CustomEndpoint`, `CustomEndpointOperation` | `ApiTokensController`, `ApiEndpointsController`, `ApiDocsController`, `src/DotNetForge.Api`, `DotNetForge.Content/Endpoints`, `src/DotNetForge.Web/ApiDocs` | [API Tokens](../pages/api-tokens.md), [API Endpoints](../pages/api-endpoints.md), [API Docs](../pages/api-docs.md) |
| Audit | `AuditLogEntry` | `IAuditService`/`AuditService`, `AuditLogsController` | [Audit Logs](../pages/audit-logs.md) |
| Extensions | `InstalledExtension` (enabled state) | `ExtensionLoader`, `ManifestValidator`, `ExtensionRuntime`, `ExtensionCatalog`, `ExtensionState`, `ExtensionLifecycleService`, `PluginsController`, slot code ([extensions → Where to change things](../features/extensions.md#where-to-change-things)) | [Plugins](../pages/plugins.md), [Extension host](../pages/extension-host.md), [Dashboard](../pages/dashboard.md#widgets) |
| Webhooks | `Webhook`, `WebhookDelivery` | `HmacWebhookSigner` (not registered) | placeholder only |
| Platform | `DataProtectionKey` | `AddDataProtection().PersistKeysToDbContext<DotNetForgeDbContext>()` | none |

## Layer-by-layer reference

### Controllers and endpoints

Full route table: [pages.md → Route map](pages.md#route-map) (screens) and
[headless API](../features/headless-api.md#endpoints) (JSON). Routing rules: [data-flow.md → Routing](data-flow.md#routing).

### Services

There is no generic service layer. Controllers either call one of the web-host services above or query
`DotNetForgeDbContext` directly (all list screens, Settings, API Tokens, most API controllers). This is the
established pattern; follow it for simple reads, and extract a `src/DotNetForge.Web/Services/` class when a rule is shared or non-trivial
(as `PageService` and `MediaService` do). Services validate and mutate; for content pages the caller saves and audits.

### Repositories

There are **no repositories**. `DotNetForgeDbContext` is the unit of work and the query API. The single
persistence abstraction is `IInstallationStore` (exists so `Core` can stay EF-free). Do not add repositories for
their own sake; add a `Shared` store interface only when `Core` logic needs persistence. File bytes are not
persistence of this kind: they go through `IFileStorage`, and the `MediaFile` row stores the opaque key.

### Database access

EF Core LINQ only - no raw SQL, except the SQL of custom API endpoints (`SqlEndpointRunner`). Read queries use `AsNoTracking()` and project to view models with
`Select`. Writes load the tracked entity, mutate, `SaveChangesAsync()`. There are no global query filters. Input is length-checked
before saving because PostgreSQL enforces column lengths. See [database.md](database.md).

### Models and DTOs

| Kind | Location |
| --- | --- |
| Entities | `src/DotNetForge.Shared/Entities` |
| Content-page input (Pages form, API create) | `src/DotNetForge.Shared/Content/IPageService.cs` (`PageInput`, `PagePosition`) |
| Admin view models | `src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs` (+ nested records in `UsersController.cs` and `RolesController.cs`) |
| Setup DTO | `src/DotNetForge.Shared/Dtos/SetupRequest.cs` |
| API request models | nested in the controller (`ContentApiController.CreatePageRequest`) |
| API responses | anonymous objects projected in each action |
| Extension manifest | `src/DotNetForge.Shared/Manifests/ExtensionManifest.cs` |

### State management

Server-rendered; there is no client state store. Request state lives in the model passed to the view, `ViewData`,
and `TempData["Success"]` (flash messages after redirect). Long-lived state is the database (rows and the Data
Protection key ring) and `IFileStorage` (media bytes). In-memory state is per process and disposable:
`InstallationStatusCache` (one `volatile bool`), `IMemoryCache` (session check 30 s, API token verification 10 min),
the `ExtensionLoader` cache, the `ExtensionState` enabled-set cache (30 s), the audit event queue and rate-limiter
counters. See [pages.md → State](pages.md#state-management).

### Configuration

`.env` + environment variables → `AppEnvironment` (singleton), including `StorageSettings`. `appsettings*.json` only
configures logging and `AllowedHosts`. Details and every key: [configuration](../features/configuration.md);
production values: [deployment](../guides/deployment.md#environment-variables).

### Dependency injection

All registrations are in `src/DotNetForge.Web/Startup/DependencyRegistration.AddDotNetForge`. Lifetimes and consumers are listed in
[dependencies.md → DI registrations](dependencies.md#di-registrations). Rules:

- stateless primitives (hasher, clock, token factory, validators, loader), the extension host singletons
  (`ExtensionRuntime`, `ExtensionCatalog`, `ExtensionState`, ...) and the `IFileStorage` router are **singletons**;
- extensions add their own registrations only through `IExtension.ConfigureServices`, called by
  `DependencyRegistration.AddExtensionHost`;
- anything touching `DotNetForgeDbContext` is **scoped**;
- background services create their own scope (`IServiceProvider.CreateScope()`).

### Authentication

Two schemes: the default **cookie** scheme (`dnf.auth`, 8 h sliding, `Secure` always outside Development) for the
admin, and **`ApiToken`** (bearer) selected only by `ApiControllerBase` (custom endpoints compare their own token in
`CustomEndpointsApiController` instead); plus the 10-minute `dnf.external` cookie that
carries a sign-in extension's result to `ExternalLoginController`. Every cookie request is re-validated
(`OnValidatePrincipal` → `AuthService.IsActiveAsync`, cached 30 s), so disabled or deleted users are signed out.
The Data Protection key ring that protects cookies, antiforgery tokens and TempData is stored in the
`DataProtectionKeys` table, so sessions survive restarts and work across instances. See
[authentication](../features/authentication.md).

### Authorization

The `AdminArea` policy (`Roles.CanEnterAdmin`: any role except Authenticated/Public) on every admin controller, plus
`[RequirePermission(area, action)]` - evaluated from the roles' stored grants (`IRoleGrantStore`) - on Users, Roles,
Audit Logs, API Tokens, Settings, Content types, Pages (`Collection types` / `read`), the Content Manager (`Entries` /
`read`) and Media (`Media` / `read`) and their mutating actions; Plugins keeps `[Authorize(Roles = "Super Admin")]`,
API Endpoints `[Authorize(Roles = "Super Admin,Developer")]` and API Docs the `ApiDocs` policy (Super Admin or
Developer). Pages, Content Manager and Media actions add record-level `(area, action)` checks through
`AdminControllerBase.Can`/`CanModify` (areas `Collection types`, `Entries` and `Media`), returning `Forbid()`; Content
types uses `[RequirePermission]` on `Content types`. User and role rules live in `UserManagementService` /
`RoleManagementService`. The API uses per-endpoint permission keys via
`[RequireApiPermission]`. See [authorization](../features/authorization.md).

### Routing

Attribute routes on every controller, one conventional `default` route, `/health` minimal API, the endpoints each
loaded extension maps (`await app.MapDotNetForgeExtensionsAsync()`, gated per request), and a catch-all fallback to
`HomeController.RenderPage`. See [data-flow.md → Routing](data-flow.md#routing).

### Logging

Default ASP.NET Core console/debug providers configured by `appsettings*.json`. Application log entries come from
`ScheduledPublishingService`, the extension host (`DotNetForge.Extensions` category: one line per extension at
startup - loaded, or `Extension {id} is enabled but not running: ...` (warning; information while disabled); `ExtensionViewRenderer` and `AuditEventDispatcher`
errors), `MediaService` (storage failure: error; orphaned object: warning),
`MediaFilesController` (row without stored object: warning) and `ApiTokenAuthenticationHandler` (`LastUsedDate`
write failure: warning). Data Protection logs a "No XML encryptor configured" warning when it creates a key (keys
are stored unencrypted). User-facing actions are recorded as audit entries instead. See
[logging and errors](../features/logging-and-error-handling.md).

### Error handling

Validation errors → `ModelState` + re-render; not-found → `NotFound()` (empty 404); permission denied → `Forbid()` →
`/account/denied`; rate limit → 429; unhandled exceptions → developer exception page (Development) or
`UseExceptionHandler("/error")` (other environments, any HTTP method). See
[logging and errors](../features/logging-and-error-handling.md).

### Background processes

`ScheduledPublishingService`, on every instance (idempotent) - see
[scheduled publishing](../features/scheduled-publishing.md); `AuditEventDispatcher` (in-memory queue to extension
handlers) - see [audit logging](../features/audit-logging.md#extension-handlers). Extensions may register their own
hosted services (they should check `IExtensionState`).

### External integrations

The PostgreSQL server, any additional database servers (`DATABASES_<NAME>_*`) and an S3-compatible object store
(`S3FileStorage`; browsers download directly from it through presigned URLs). Anything else comes from extensions
(the samples call Open-Meteo, Discord OAuth, Twilio, a second S3 bucket). An SMTP server, when `SMTP_*` is
configured, receives the password-reset and confirmation emails ([email](../features/email.md)). Webhook delivery
exists only as entities and contracts.

### Shared utilities

| Utility | Location |
| --- | --- |
| `Result` | `Shared/Results` |
| `PageService.Slugify`, `.ToUtc`, `.IsDynamic` | `src/DotNetForge.Web/Services/PageService.cs` (the slug rules actually used) |
| `MediaService.IsInline`, `.UrlFor`, `.AllowedTypes` | `src/DotNetForge.Web/Services/MediaService.cs` |
| `EmailValidator`, `PasswordPolicy` | `Core/Validation` |
| `StorageKey` | `Abstractions/Storage` |
| `SqliteConnectionStrings` | `Shared/Configuration` |
| `DotEnvParser` | `Infrastructure/Configuration` |
| Role/permission/audit/rate-limit constants | `Shared/Constants` - always use these, never string literals |

### UI architecture

Server-rendered Razor with progressive enhancement and no inline script or style. See [pages.md](pages.md).

### API architecture

Separate class library mounted as an application part, token-only, rate-limited per IP,
anonymous-object JSON. See [headless API](../features/headless-api.md).

### Persistence architecture

One model on PostgreSQL: `PostgreSqlDbContext`, registered as `DotNetForgeDbContext`; migrations + seed on every
start. Structured commands for the CMS database or any additional database go through `IDatabaseService`. See [database.md](database.md) and
[database layer](../database/architecture.md).

### Build system

MSBuild with `Directory.Build.props` (target framework, nullable, `ManagePackageVersionsCentrally`,
`InvariantGlobalization=false` - SQL Server's client, used for additional databases, needs ICU) and `Directory.Packages.props` (all versions, transitive pinning). Local tool `dotnet-ef`
10.0.12 pinned in `.config/dotnet-tools.json`. `Microsoft.EntityFrameworkCore.Design` is referenced only by `Data`
with `PrivateAssets="all"`, so it never reaches the published app. `DotNetForge.slnx` at the root lists every
project, so `dotnet build` and `dotnet test` work without arguments - see
[development guide](../guides/development.md#everyday-commands-repository-root).

### Development environment

`.vscode/tasks.json` (build, watch, run, tests, clean, restore tools, add migration) and `.vscode/launch.json`
(F5 debug of `src/DotNetForge.Web/bin/Debug/net10.0/DotNetForge.Web.dll`). `src/DotNetForge.Web/Properties/launchSettings.json` profiles `http`
(`http://localhost:5000`) and `https` (`https://localhost:5001`), both `Development`. `docker/compose.dev.yml` starts
PostgreSQL, RustFS (S3) and Mailpit for production-like local runs. See
[development guide](../guides/development.md).

### Production / runtime architecture

A single ASP.NET Core process (Kestrel); the Docker image runs it as non-root `$APP_UID` on port 8080 with
`ASPNETCORE_ENVIRONMENT=Production`. Outside Development the **deployment directory is read-only**: the app writes
nothing under the content root (verified by `ReadOnlyDeploymentTests` and the CI `read-only-container` job, which
runs `docker/compose.app.yml` - read-only root filesystem, `/tmp` tmpfs - and checks `/health`, `/setup` and that
`/app` is not writable). Requirements:
[deployment → read-only deployment requirements](../guides/deployment.md#read-only-deployment-requirements).

| Runtime state | Where it lives |
| --- | --- |
| Relational data | PostgreSQL (the CMS database); additional databases wherever they are configured |
| Data Protection key ring (auth cookie, antiforgery, TempData) | `DataProtectionKeys` table - shared by all instances, survives restarts; unencrypted at rest, so database access = key access |
| Uploaded media | `IFileStorage`: private objects in an S3-compatible bucket ([storage architecture](../features/media-storage.md#storage-architecture)) |
| Compiled extension views | memory (runtime Razor compilation reads `extensions/` from the publish output) |
| Extension code | memory (assemblies are read into memory, files never locked; built `bin/` folders ship with the publish output) |
| Enabled extensions | `InstalledExtensions` table (cached 30 s per instance) |
| Multipart bodies over 64 KB | OS temp directory (`ASPNETCORE_TEMP` or `/tmp`) - must be writable (tmpfs in containers) |
| Session/token caches, rate-limit counters, extension caches, audit event queue | process memory, per instance |

Pipeline and hosting choices outside Development: `UseExceptionHandler("/error")` + `UseHsts()`;
`SecurityHeadersMiddleware` with an enforced CSP; cookie `SecurePolicy = Always`; `UseRateLimiter()` after
`UseAuthentication()`; no `UseHttpsRedirection` - TLS terminates at a reverse proxy, and
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the scheme and client IP (rate limits, audit) come from
`X-Forwarded-*` ([behind a reverse proxy](../guides/deployment.md#behind-a-reverse-proxy)). The PostgreSQL
migrations run before Kestrel accepts traffic. `/health` returns `{ "status": "ok", "app": "<APP_NAME>" }`
and bypasses the install gate.

```mermaid
flowchart LR
    Browser([Browser]) -- "HTTPS, cookie dnf.auth" --> Proxy["TLS reverse proxy"]
    Client([API client]) -- "Bearer dnf_..." --> Proxy
    Proxy -- "HTTP + X-Forwarded-*" --> Kestrel
    subgraph Process["DotNetForge.Web process (read-only content root)"]
        Kestrel --> Pipeline["Security headers, install gate, auth, rate limiter"]
        Pipeline --> MVC["MVC controllers + Razor"]
        MVC --> Ctx["DotNetForgeDbContext"]
        MVC --> Store["IFileStorage"]
        DP["Data Protection key ring"] --> Ctx
        Job["ScheduledPublishingService"] --> Ctx
        MVC -- "runtime Razor compile, in memory" --> ExtFiles[("extensions/ (read-only)")]
    end
    Ctx --> DB[("PostgreSQL")]
    Store --> Obj[("S3-compatible bucket")]
    Browser -. "302 to presigned GET URL" .-> Obj
```

Multi-instance notes: rate limits and caches are per instance (N instances allow N times the limit);
`ScheduledPublishingService` runs on every instance (idempotent); a disabled user can keep a session for up to the
30 s session-check cache.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
Product goals and modes: [product overview](../product.md).

### Requirements

**Layout.** The specification's tree differs from the repository in two deliberate ways; keep the current layout
unless the architecture is changed on purpose (the web host in `src/DotNetForge.Web/` matches the specification):

| Spec | Repository |
| --- | --- |
| `agents/` at the root | `.github/agents/` |
| `docs/` (`developer-guide.md`, `extension-development.md`, `theme-development.md`, `module-development.md`, `api-guide.md`) | `.docs/` |

**Target responsibilities per project** (what each project should own beyond what it does today):

| Project | Target responsibility not yet present |
| --- | --- |
| `DotNetForge.Core` | content system, media, themes, settings, extension management, update/rollback orchestration (permission checks used at request time ✔ via `PermissionService`) |
| `DotNetForge.Data` | repository/data interfaces for `Core` (PostgreSQL migrations ✔; other databases as additional databases ✔) |
| `DotNetForge.Infrastructure` | SMTP email, webhook delivery, background jobs, logging, external connectors (today: file storage providers ✔ used; `HmacWebhookSigner` unregistered) |
| `DotNetForge.Extensions` | lifecycle install / update / remove (✔ dependency resolution, code loading and DI registration of each extension's services; enable/disable ✔ in `ExtensionLifecycleService` in the web host) |
| `DotNetForge.Api` | full surface: content, media, users, roles, settings, extensions, dynamic routes, custom extension permissions; no direct `Data` reference |
| `DotNetForge.Web` | page builder, per-page themes (✔ site-wide theme slot) |

**Composition and subsystems:**

- **Extension points:** ✔ extensions implement `DotNetForge.Sdk.IExtension` and register services through DI; slots are
  fixed per type ([extensions → Slots](../features/extensions.md#slots)); marketplace installation through an external
  API ✘ ([extensions](../features/extensions.md)).
- **Database:** every schema change is an EF Core migration ✔
  (`Database/Providers/PostgreSql/Migrations/`, [database](database.md)); model-driven
  import/export ([transfer and updates](../features/transfer-and-updates.md)).
- **Authentication:** pluggable providers registered through DI and supplied by `authentication` extensions in
  `extensions/authentication/` (✔ `AddLoginProvider`, [external sign-in](../features/authentication.md#external-sign-in-extensions));
  built-in Email provider ✔; external providers (Auth0, GitHub, Google, Microsoft, ...) with Name, Status and a
  Settings action; rate limiting ✔ (`credentials` policy on sign-in and setup); advanced user
  settings ([authentication](../features/authentication.md)).
- **Authorization:** a permission check on every admin and API action (✔ API keys; ✔ Pages, Content Manager and Media
  `(area, action)` checks; other admin screens use role checks); roles created, edited, duplicated, deleted and
  assigned ([authorization](../features/authorization.md)).
- **Content:** page types (Standard, Existing page, URL redirect, File), per-page and per-role/user permissions,
  theme/layout selection, page builder with built-in and extension modules, preview, draft/published versions,
  history with rollback; dynamic routes resolve content, modules, controllers or extension handlers
  ([content pages and routing](../features/content-pages-and-routing.md)).
- **Media:** storage behind an interface ✔ (`IFileStorage`, local and S3 built in); external connectors from
  `extensions/connectors/` ✔ ([storage connectors](../features/media-storage.md#storage-connectors)); type and size validation ✔ (fixed list and 25 MB in `MediaService`), configurable allowed
  types and max size; public/private with access control ✔ (`MediaFilesController`); unsafe-upload scanning;
  optional small/medium/large variants, optimisation, EXIF auto-orientation
  ([media storage](../features/media-storage.md)).
- **Themes:** `theme` extensions in `extensions/themes/` with layouts, page templates, assets, settings and preview;
  per-page theme, default site theme (✔ one site-wide theme with views, assets and read-only
  settings - [themes](../features/themes.md)).
- **API:** rate limiting ✔ (`api` policy) and input
  validation on every endpoint ([headless API](../features/headless-api.md)).
- **Update and rollback:** owned by [transfer and updates](../features/transfer-and-updates.md).
- **Security:** full control set in [security](../features/security.md).
- **Deployment:** ✔ built - see [Production / runtime architecture](#production--runtime-architecture) and the
  [deployment guide](../guides/deployment.md) (HTTPS with `Secure` cookies, migrations before traffic, no writes to
  the deployment directory, no secrets in the image).

### Rules and validation

- Public frontend themes never affect the admin area, the setup screen or (unless explicitly configured) the login
  screen ✔ (only `HomeController` uses themes).
- The core never writes user code into `extensions/`; extensions never modify `src/` ✔ (`extensions/` is read-only
  at runtime).
- Manifests are validated **before installation**; invalid, unsafe or incompatible extensions are never installed
  (validation exists in `ManifestValidator` ✔; there is no install step).
- Avoid unnecessary dependencies; document everything important.

### Acceptance criteria

Update/rollback and import/export criteria are tracked in [transfer and updates](../features/transfer-and-updates.md).

- [ ] The repository reproduces the specified tree exactly (`src/DotNetForge.Web`, root `agents/`, `docs/`) - see
  the layout table above.
- [ ] Each project has its specified responsibility and dependency direction, with `Abstractions` holding only
  contracts (`Abstractions` ✔; `Api` → `Data` shortcut and the responsibilities above are not).
- [x] `src/` and `extensions/` are separated and `extensions/` is partitioned by type (nine type folders).
- [ ] Extension points are interfaces in `DotNetForge.Abstractions` **resolved via DI** (deliberately different: the
  entry point `IExtension` and slot helpers are in `DotNetForge.Sdk`, extensions register through DI ✔).
- [ ] The extension host validates manifests before install and rejects invalid, unsafe or incompatible extensions.
- [x] The required manifest fields are exactly `id`, `name`, `description`, `version`, `type`, `author`,
  `entryPoint`, `permissions` (`ManifestValidator`).
- [x] EF Core migrations drive all schema changes - `DatabaseInitializer.InitializeAsync` → `MigrateAsync` of the
  PostgreSQL set. The spec's SQLite option for the CMS was dropped: the CMS runs on PostgreSQL only, SQLite remains an
  additional database.
- [x] The database is configured from `.env` (`DATABASE_CONNECTION_STRING`, always PostgreSQL, resolved by
  `DatabaseProviderRegistry`; additional databases `DATABASES_<NAME>_*`).
- [ ] Authentication providers are pluggable via DI and extensions, with Email built in (✔ extensions via
  `AddLoginProvider`, existing users only; no provider management).
- [ ] RBAC enforces a permission check on every admin and API action with the six default roles (roles ✔ seeded;
  API ✔; Pages, Content Manager and Media ✔ via `Can`/`CanModify`; other admin screens use role checks).
- [x] Permission areas cover Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API,
  Webhooks (`PermissionAreas.Canonical`).
- [ ] Content, media, theme and API systems are implemented in their specified layers.
- [x] The admin area is never affected by public frontend themes (only `HomeController.ThemedViewAsync` renders theme
  views; no test asserts the admin side).
- [ ] The security model is implemented per [security](../features/security.md) (partial).
- [x] Secrets live in `.env` and are never committed; `storage/`, build output, logs (`*.log`) and uploads are
  git-ignored (`.gitignore`); the Docker image takes configuration from environment variables only.
- [ ] Unit tests cover core services, permissions, content manager, media validation, API token generation, manifest
  validation, webhook signing and import/export; integration tests cover setup, authentication and API endpoints
  (see [testing → Planned](../guides/testing.md#planned-not-implemented)).
- [x] The app builds and runs on Windows, macOS and Linux and applies migrations before serving traffic - CI
  `build-and-test` matrix on all three; `Program.cs` runs `DatabaseInitializer.InitializeAsync`
  before `app.Run()`.
- [ ] The architecture overview ([overview.md](overview.md); the spec's root `ARCHITECTURE.md`) documents
  structure, core, extension, database, authentication, authorization, content, media, theme, API, update/rollback,
  security, testing and deployment architecture (it is a short summary pointing here; update/rollback and themes
  have no architecture yet).

## Architectural rules for changes

These rules keep the codebase consistent; follow them unless you are deliberately changing the architecture (then
update this document in the same change).

1. **All host DI goes in `src/DotNetForge.Web/Startup/DependencyRegistration.cs`.** Don't register services in
   `Program.cs`. Extensions register theirs only in `IExtension.ConfigureServices` (called from `AddExtensionHost`)
   and plug into slots with the SDK helpers, never by replacing host registrations.
2. **`Abstractions` stays dependency-free and entity-free.** Contracts that need entities go in `Shared`.
3. **`Core` never references EF Core or `Data`.** Put a store interface in `Shared/Stores`, implement it in `Data`.
4. **Content-page rules belong in `IPageService`** (`PageService`). Controllers and API endpoints must not
   re-implement slug, parent-cycle, uniqueness, length or schedule rules; they call `ApplyAsync`/`ReorderAsync`/
   `DeleteAsync`, then save and audit. The public "live" rule is `PageVisibility.Live`
   (`src/DotNetForge.Shared/Content/PublishedPages.cs`); call it, never copy it.
5. **Every admin controller derives from `AdminControllerBase`** and every API controller from `ApiControllerBase`.
   Never bypass them - they carry the auth attributes, the current user id and permission checks (admin) and the
   `api` rate limit (API). The only exception is `CustomEndpointsApiController`: custom endpoints are opened by their
   own token (or none), never by an API token, so it is a plain `ControllerBase` that sets `[ApiController]`,
   `[Produces("application/json")]` and `[EnableRateLimiting(RateLimitPolicies.Api)]` itself.
6. **Every mutating admin action is `[HttpPost]` + `[ValidateAntiForgeryToken]`** and writes an audit entry through
   `IAuditService` with a constant from `AuditActions`.
7. **Inject interfaces for shared services:** `IAuditService` and `IPageService`, never `AuditService` or
   `PageService`. (`MediaService`, `AuthService` and the `DotNetForge.Content` services have no interface and are injected directly.)
8. **Permission checks use `Can(area, action)` / `CanModify(area, any, own, createdById)`** with `PermissionAreas` /
   `PermissionActions` constants and return `Forbid()`; the view model exposes the same result (`CanUpload`,
   `CanDelete`) so the screen hides what the user cannot do. Hiding is never the gate.
9. **Every API action carries `[RequireApiPermission(PermissionKeys.X)]`.** Add new keys to `PermissionKeys.All`
   so they appear on the token-create screen; the [API docs](../features/api-docs.md) name the key on each operation.
   The only exception is `CustomEndpointsApiController.Call`, which takes no API token at all: it checks the
   endpoint's own token (`CustomEndpointService.VerifyToken`) or none for a public endpoint
   ([custom endpoints](../features/custom-endpoints.md)).
10. **Use the constants** in `Shared/Constants` for role names, permission keys, audit actions, webhook events and
    rate-limit policies.
11. **No runtime writes under the content root.** Files the app creates go through `IFileStorage` (never `System.IO`
    on `ContentRootPath`/`WebRootPath`), state goes in the database. Storage keys are generated, never built from
    user input (`StorageKey` validates them). New configuration must not default to a path inside the deployment
    directory outside Development.
12. **No inline script or style in views** (the CSP forbids it). Build screens from the
    [design system](design-system.md): behaviour as a data attribute handled in `wwwroot/js/forge/ui.js` (e.g.
    `data-confirm`) or a page script, styles in `wwwroot/css/forge/` using the tokens, validation errors with
    `<form-errors />` (never `asp-validation-summary`, whose empty state carries an inline style).
13. **Extension discovery goes through `IExtensionLoader`** (`Discover()`), and "may this extension contribute now?"
    through `ExtensionCatalog` (`GetActiveAsync`, `FindActiveAsync`, `IsActiveAsync`); never combine discovery, code
    and enabled state in a caller, never scan `extensions/` directly, and never build file paths from request input
    (see `ExtensionResources.Serve` for the traversal guard). A new slot follows the pattern in
    [extensions → Where to change things](../features/extensions.md#where-to-change-things).
14. **Schema changes go through one migration** for `PostgreSqlDbContext`, into
    `Database/Providers/PostgreSql/Migrations/` (the only CMS database). Strip the UTF-8 BOM EF writes into generated
    files (`.editorconfig` `charset = utf-8`) and check the snapshot landed in the folder ([database → migrations](database.md#migrations), the
    [database change skill](../../.claude/skills/database-change/SKILL.md)). Seed data goes in `DataSeeder` and must
    be idempotent.
15. **No new dependencies for things the platform does** (the hasher, token factory, signer and `.env` parser are
    deliberately in-house; rate limiting and Data Protection are ASP.NET Core's). `AWSSDK.S3` is the one exception,
    for S3 request signing.
16. **Content-type and entry rules live in the `DotNetForge.Content` services** (`ContentTypeService`,
    `ContentEntryService`, `ContentQueryService`), pure rules (field definitions, value validation, item data) in
    `src/DotNetForge.Core/Content/`. Controllers - admin and API - call them; they never validate entry values or
    write `ContentEntryValues` themselves. A content list renders identically in C# and JavaScript: change
    `Rendering/Collections.cs` / `Binding.cs` and `renderer/collection.js` / `binding.js` together, with the golden
    fixtures ([structured content](../features/structured-content.md#where-to-change-things)).
