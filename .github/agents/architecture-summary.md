# Architecture Summary (for AI agents)

Layered solution (`DotNetForge.slnx`); the web host is `src/DotNetForge.Web`, Docker files are in `docker/`. Full detail:
[codebase.md](../../.docs/architecture/codebase.md), [dependencies.md](../../.docs/architecture/dependencies.md).

| Project | Role |
| --- | --- |
| `DotNetForge.Abstractions` | Contracts (`IFileStorage`, `IDatabaseService`, security, messaging). No dependencies, no entities. |
| `DotNetForge.Shared` | Entities, enums, constants (incl. `AuthorizationPolicies`), DTOs, `Result`, manifest model, `AppEnvironment`, `PermissionMatrix`, store interfaces, `PageVisibility.Live` / `IPublishedPages`. |
| `DotNetForge.Core` | `InstallationService`, `PermissionService` (content/media permission checks), validators, structured content rules (`Content/`). No EF Core. |
| `DotNetForge.Data` | `DotNetForgeDbContext` (SQLite migrations, Data Protection keys) and one context per other provider (PostgreSQL, SQL Server, MySQL migrations; MongoDB), the database layer in `Database/` (provider registry, `IDatabaseService`), `DataSeeder`, `InstallationStore`. |
| `DotNetForge.Content` | Structured content over the database: `ContentTypeService`, `ContentEntryService`, `ContentQueryService`, `ContentApiService` - shared by admin and API. Pure rules (`ContentSchema`, `EntryData`, `ItemData`) are in `DotNetForge.Core/Content`. |
| `DotNetForge.Infrastructure` | PBKDF2 hasher, clock, API token factory, HMAC signer, `.env` loader, `LocalFileStorage` and `S3FileStorage`. |
| `DotNetForge.Extensions` | `ManifestValidator`, `ExtensionLoader` (discovery), `ExtensionRuntime` + `ExtensionLoadContext` (code loading), `ExtensionCatalog` (is it active?). |
| `DotNetForge.Sdk` | What extensions compile against: `IExtension`, `ExtensionContext`, `AddMediaStorage` / `AddLoginProvider` / `AddAuditEventHandler`, `IExtensionState`. |
| `DotNetForge.Api` | Headless API controllers, `ApiTokenAuthenticationHandler`, `RequireApiPermissionAttribute`. |
| `DotNetForge.Web` (root) | Composition root, install middleware, admin area, public site, setup, account, web-host services, extension host services (`ExtensionState`, `ExtensionLifecycleService`, slots). |

## Composition (`Program.cs`)

1. `EnvConfigurationLoader.Load(...)` → `AppEnvironment` (exit code 1 on invalid config).
2. `AddDotNetForge(env, contentRoot)` (`src/DotNetForge.Web/Startup/DependencyRegistration.cs`) registers everything,
   including the extension host (`AddExtensionHost`: loads extension code, runs each `ConfigureServices`).
3. `DatabaseInitializer.InitializeAsync` → the main provider's schema step (migrations, or MongoDB `EnsureCreated`) + seed.
4. Pipeline: exception handler/HSTS (non-Development) → `SecurityHeadersMiddleware` → static files → routing →
   `InstallationMiddleware` → `ExtensionEndpointGateMiddleware` → authentication → rate limiter → authorization →
   controllers, `/health`, extension endpoints (`MapDotNetForgeExtensionsAsync`), fallback to `HomeController.RenderPage`.

## Conventions

- File-scoped namespaces, nullable, 4-space indent, LF; versions in `Directory.Packages.props`.
- New cross-cutting contract → `Abstractions` (or `Shared` if it needs entities); implementation in
  `Infrastructure`/`Data`; registration in `DependencyRegistration`.
- Rules for changes: [codebase.md → Architectural rules](../../.docs/architecture/codebase.md#architectural-rules-for-changes).
