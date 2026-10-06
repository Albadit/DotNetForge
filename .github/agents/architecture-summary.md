# Architecture Summary (for AI agents)

Layered solution (`DotNetForge.slnx`); the web host is `src/DotNetForge.Web`, Docker files are in `docker/`. Full detail:
[codebase.md](../../.docs/architecture/codebase.md), [dependencies.md](../../.docs/architecture/dependencies.md).

| Project | Role |
| --- | --- |
| `DotNetForge.Abstractions` | Contracts + extension-point interfaces. No dependencies, no entities. |
| `DotNetForge.Shared` | Entities, enums, constants, DTOs, `Result`, manifest model, `AppEnvironment`, `PermissionMatrix`, store interfaces. |
| `DotNetForge.Core` | `InstallationService`, `PermissionService` (content/media permission checks), validators. No EF Core. |
| `DotNetForge.Data` | `DotNetForgeDbContext` (SQLite migrations, Data Protection keys) and one context per other provider (PostgreSQL, SQL Server, MySQL migrations; MongoDB), the database layer in `Database/` (provider registry, `IDatabaseService`), `DataSeeder`, `InstallationStore`. |
| `DotNetForge.Infrastructure` | PBKDF2 hasher, clock, API token factory, HMAC signer, `.env` loader, `LocalFileStorage` and `S3FileStorage`. |
| `DotNetForge.Extensions` | `ManifestValidator`, `ExtensionLoader` (discovery only). |
| `DotNetForge.Api` | Headless API controllers, `ApiTokenAuthenticationHandler`, `RequireApiPermissionAttribute`. |
| `DotNetForge.Web` (root) | Composition root, install middleware, admin area, public site, setup, account, web-host services. |

## Composition (`Program.cs`)

1. `EnvConfigurationLoader.Load(...)` → `AppEnvironment` (exit code 1 on invalid config).
2. `AddDotNetForge(env, contentRoot)` (`src/DotNetForge.Web/Startup/DependencyRegistration.cs`) registers everything.
3. `DatabaseInitializer.InitializeAsync` → the main provider's schema step (migrations, or MongoDB `EnsureCreated`) + seed.
4. Pipeline: exception handler/HSTS (non-Development) → `SecurityHeadersMiddleware` → static files → routing →
   `InstallationMiddleware` → authentication → rate limiter → authorization → controllers, `/health`, fallback to
   `HomeController.RenderPage`.

## Conventions

- File-scoped namespaces, nullable, 4-space indent, LF; versions in `Directory.Packages.props`.
- New cross-cutting contract → `Abstractions` (or `Shared` if it needs entities); implementation in
  `Infrastructure`/`Data`; registration in `DependencyRegistration`.
- Rules for changes: [codebase.md → Architectural rules](../../.docs/architecture/codebase.md#architectural-rules-for-changes).
