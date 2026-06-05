# Architecture Summary (for AI agents)

Layered ASP.NET Core MVC solution. Dependencies flow inward. Full detail in
[ARCHITECTURE.md](../../ARCHITECTURE.md).

| Project | Role |
| --- | --- |
| `DotNetForge.Abstractions` | Primitive-only contracts + extension-point interfaces. No dependencies. |
| `DotNetForge.Shared` | Entities, enums, constants, results, manifest models, `AppEnvironment`, `PermissionMatrix`, store interfaces. |
| `DotNetForge.Core` | `PermissionService`, `InstallationService`, validators, manifest/extension contracts. Pure logic. |
| `DotNetForge.Data` | EF Core `DotNetForgeDbContext`, migrations, provider selection, seeder, `InstallationStore`. |
| `DotNetForge.Infrastructure` | PBKDF2 hasher, clock, HMAC webhook signer, API token factory, `.env` loader, local storage, email. |
| `DotNetForge.Extensions` | `ManifestValidator`, `ExtensionLoader`. |
| `DotNetForge.Api` | Headless API controllers + API-token auth handler + permission filter. |
| `DotNetForge.Web` | MVC host (repo root): admin area, setup, auth, install middleware, DI composition. |

## Composition flow (`Program.cs`)

1. `EnvConfigurationLoader.Load(...)` → `AppEnvironment` (aborts on invalid `.env`).
2. `AddDotNetForge(env, contentRoot)` registers everything (`Startup/DependencyRegistration.cs`).
3. `DatabaseInitializer.InitializeAsync` → migrate (SQLite) / create (PostgreSQL) + seed.
4. `InstallationMiddleware` gates the app until setup completes.

## Conventions

- File-scoped namespaces, 4-space indent, nullable enabled, central package versions
  (`Directory.Packages.props`), shared build props (`Directory.Build.props`).
- New cross-cutting behavior → interface in `Abstractions`, implementation in `Infrastructure`,
  registered in `DependencyRegistration`.
