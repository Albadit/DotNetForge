# DotNetForge CMS - Architecture

DotNetForge CMS is a modular hybrid CMS built with **ASP.NET Core MVC** (C#, Entity Framework Core,
Razor Views) on **.NET 10**. It supports **SQLite** (default) and **PostgreSQL**, is configured
through `.env`, and runs cross-platform on Windows, macOS, and Linux. It operates in three modes at
once: **Traditional** (visual admin), **Headless** (token API), and **Hybrid** (both).

The overriding architectural goal is a **strict separation between the CMS core (`src/`) and
user-created extensions (`extensions/`)** so the core can be updated without breaking extensions.

This document is the canonical `ARCHITECTURE.md` deliverable. It covers the project structure, the
core architecture, and every major subsystem, and it records where this foundation makes a pragmatic,
documented choice.

## Architectural principles

- Keep the CMS core (`src/`) separate from user extensions (`extensions/`).
- Do not let frontend themes affect the admin area.
- Prioritize security from the beginning.
- Keep the project lightweight; avoid unnecessary dependencies (the `.env` parser, password hasher,
  token factory, and webhook signer are all BCL-only).
- Use clear folder boundaries, interfaces for extension points, and dependency injection throughout.
- Use EF Core migrations for schema changes; make import/export provider-aware.
- Make API permissions granular and validate extension manifests before installation.

## Canonical project structure

The specification's canonical tree (`architecture.md`). The repository reproduces it, with one
deliberate adaptation noted below.

```text
DotNetForgeCMS/
  src/
    DotNetForge.Web/
    DotNetForge.Core/
    DotNetForge.Data/
    DotNetForge.Api/
    DotNetForge.Abstractions/
    DotNetForge.Infrastructure/
    DotNetForge.Extensions/
    DotNetForge.Shared/
  tests/
    DotNetForge.Tests/
    DotNetForge.IntegrationTests/
  extensions/
    themes/  plugins/  modules/  widgets/  providers/  connectors/  authentication/  libraries/  admin/
  storage/
    media/  backups/  logs/  updates/
  agents/
    cms-overview.md  architecture-summary.md  extension-system.md  permissions.md  api-reference.md  development-guidelines.md
  docs/
    developer-guide.md  extension-development.md  theme-development.md  module-development.md  api-guide.md
  .env.example
  .gitignore
  README.md
  ARCHITECTURE.md
  LICENSE
```

### As implemented

```text
DotNetForge.Web.csproj   +  Program.cs, Controllers/, Areas/Admin/, Views/, wwwroot/   (repository ROOT)
src/
  DotNetForge.Abstractions/   DotNetForge.Shared/   DotNetForge.Core/   DotNetForge.Data/
  DotNetForge.Infrastructure/ DotNetForge.Extensions/ DotNetForge.Api/
tests/  DotNetForge.Tests/   DotNetForge.IntegrationTests/
extensions/  (themes/plugins/modules/widgets/providers/connectors/authentication/libraries/admin)
storage/  (media/backups/logs/updates - git-ignored)
docs/                     (developer documentation)
.github/  (workflows/ci.yml, dependabot.yml, agents/ - AI-agent documentation)
.env.example  .gitignore  .editorconfig  README.md  ARCHITECTURE.md  LICENSE  global.json
Directory.Build.props  Directory.Packages.props  .config/dotnet-tools.json
```

> The spec's canonical tree places `agents/` at the repository root; in this repository the AI-agent
> docs live under `.github/agents/` alongside the CI workflow and Dependabot config.

#### Adaptation: the web host lives at the repository root

The only structural deviation from the canonical tree is that **`DotNetForge.Web` is the repository
root project** rather than `src/DotNetForge.Web/`. This is a direct consequence of the requirement to
run everything from the root with bare CLI commands. The project's `DefaultItemExcludes` scopes its
file globs to its own files so it never compiles the sibling `src/`, `tests/`, or `extensions/`
folders. All other libraries remain under `src/` exactly as specified.

### Running from the repository root

The .NET CLI resolves a single project/solution from the current directory:

- `dotnet run` / `dotnet watch` require a **project file** in the directory (they ignore solutions).
- `dotnet build` / `dotnet test` accept a project **or** a solution, but error if a directory holds
  **both** a runnable project and a solution.

Putting the web project at the root therefore makes `dotnet run`, `dotnet watch`, and `dotnet build`
work bare from the root. The unit and integration test projects are run by path
(`dotnet test tests/DotNetForge.Tests`), which is also one of the commands listed in
`testing_quality.md`.

## Core architecture & layering

Dependencies flow inward toward the abstractions/shared layers. Cross-cutting wiring is done through
DI in the web composition root (`Startup/DependencyRegistration.cs`).

| Project | Layer | Responsibility | Depends on |
| --- | --- | --- | --- |
| `DotNetForge.Abstractions` | Contracts | Behavioral interfaces (password hashing, permissions, token factory, webhook signer, email/storage, **extension-point interfaces**). Primitive-only - the stable surface extensions compile against. | (none) |
| `DotNetForge.Shared` | Shared kernel | Entities, enums, constants, result types, manifest models, the `AppEnvironment` config model, the **PermissionMatrix** reference data, and entity-facing store interfaces. | Abstractions |
| `DotNetForge.Core` | Domain & services | `PermissionService`, `InstallationService`, validators (password policy, email, slug), and the manifest/extension contracts. Pure logic; no EF Core. | Abstractions, Shared |
| `DotNetForge.Data` | Persistence | EF Core `DotNetForgeDbContext`, entity configuration, migrations, provider selection, the seeder, and the installation store. | Abstractions, Shared |
| `DotNetForge.Infrastructure` | Infrastructure | BCL-only implementations: PBKDF2 hasher, system clock, HMAC webhook signer, API token factory, `.env` loader, local file storage, file-system email sender. | Abstractions, Shared, Core |
| `DotNetForge.Extensions` | Extension host | `ManifestValidator` and `ExtensionLoader` - manifest discovery and validation. | Abstractions, Shared, Core |
| `DotNetForge.Api` | API surface | Headless/Hybrid controllers, the API-token authentication handler, and the per-permission filter. | Abstractions, Shared, Core, **Data** † |
| `DotNetForge.Web` | Presentation host | MVC host, admin area, setup wizard, install middleware, auth, and the DI composition root. References everything. | All of the above |

**Documented foundation choices**

- † `DotNetForge.Api` references `DotNetForge.Data` so controllers can query persistence directly. A
  full repository abstraction (which would keep Api on `Core` only) is future work.
- `PermissionMatrix` lives in `Shared` (as declarative reference data) so both `Core.PermissionService`
  and `Data.DataSeeder` consume the same source of truth without `Data` depending on `Core`.
- `IInstallationStore` lives in `Shared` so `Core` can orchestrate installation while `Data` provides
  the EF-backed implementation - preserving the Core↔Data sibling relationship.

## Composition & bootstrapping

`DotNetForge.Web` is the composition root (`Program.cs`):

1. Loads and validates `.env` via `EnvConfigurationLoader`. Missing/invalid config aborts startup
   with a clear message (a non-zero exit) instead of starting in an undefined state.
2. Registers all services, the DbContext (provider chosen from `.env`), authentication (cookie for
   the admin UI, bearer token for the API), and authorization policies.
3. Runs `DatabaseInitializer`: applies EF Core migrations (SQLite) or creates the schema
   (PostgreSQL), then seeds the default tenant, the six built-in roles with their permission grants,
   and the default authentication providers.
4. Installs `InstallationMiddleware`, which redirects every request to `/setup` until the CMS is
   installed and blocks `/setup` afterward.

## Database architecture

- Persistence is isolated in `DotNetForge.Data` using EF Core. SQL injection is prevented by EF Core
  parameterization.
- The provider is selected from `.env` (`DbProviderConfigurator`): `UseSqlite` or `UseNpgsql`.
- Schema changes flow through EF Core migrations (`src/DotNetForge.Data/Migrations`). On startup
  SQLite applies the migration set; PostgreSQL creates the schema from the model
  (`EnsureCreated`) - a PostgreSQL migration set is the documented next step.
- A `DesignTimeDbContextFactory` lets `dotnet ef migrations add` run without booting the host.
- Tenant-scoped entities carry a `TenantId` and are filtered by the active tenant.

## Authentication architecture

- The built-in **Email** provider authenticates with email/password against a salted PBKDF2 hash.
- Account lockout triggers after repeated failed attempts; sessions use HttpOnly, SameSite cookies
  (Secure in production).
- Social/OAuth providers are seeded as catalog entries (Auth0, GitHub, Google, …) and are extensible
  via `Authentication provider` extensions registered through DI.

## Authorization & RBAC

- Every admin and API action resolves a permission check. The six built-in roles are **Super Admin,
  Admin, Editor, Author, Authenticated, Public**.
- Permissions are organized by area (Collection types, Single types, Plugins, Settings, Extensions,
  Media, Users, Roles, API, Webhooks). `PermissionService.Has(role, area, action)` evaluates the
  default matrix; seeded `RolePermission` rows mirror it.
- The admin area requires an admin-capable role (`AdminArea` policy); the headless API enforces the
  token's granular permission via `[RequireApiPermission]`.

## Content, media & theme systems

- **Content**: pages form a tenant-scoped tree with slugs, SEO metadata, publish/menu flags, and page
  types. Collection/Single types and the page builder are foundation entities ready for expansion.
- **Media**: storage is abstracted behind `IFileStorage`; `LocalFileStorage` (under `storage/media`)
  is the default, with external connectors supplied by extensions.
- **Themes**: themes affect only public pages. The admin area, setup, and login use a dedicated
  built-in layout and are never restyled by a public theme.

## API system

- `DotNetForge.Api` exposes content, media, users, roles, settings, and extensions endpoints under
  `/api/...`. Access is controlled by API tokens with granular permissions.
- Tokens are generated with a non-secret prefix and a salted hash; the plaintext is shown once. A
  presented token is matched by prefix, then verified against the stored hash; expired/revoked tokens
  return `401`, missing permissions return `403`.

## Update & rollback system

- Core (`src/`) and extensions (`extensions/`) are separated so updates never overwrite extensions.
- `storage/backups/` and `storage/updates/` stage backups and downloaded packages. The full update
  workflow is a foundation surface here; the safety model (pre-update backup, rollback on failure) is
  specified in `transfer_updates.md`.

## Security model

PBKDF2 password hashing; RBAC checks on every action; hashed API tokens; HMAC-signed webhooks;
manifest validation before install; CSRF protection; HttpOnly/SameSite cookies; HSTS in production;
EF Core parameterized queries; secrets confined to `.env` and excluded from Git. See
`dotnetforge_prompt/security.md`.

## Testing strategy

- `tests/DotNetForge.Tests` (unit) covers manifest validation, the permission matrix, password
  hashing, API-token generation, webhook signing, `.env` configuration, validators, and the
  installation flow.
- `tests/DotNetForge.IntegrationTests` drives the real host with `WebApplicationFactory<Program>`
  against an isolated SQLite database, covering install detection, the setup gate, admin auth gating,
  and API token enforcement.

## Deployment strategy

- Cross-platform; configured entirely via `.env` (no secrets in the build).
- Startup applies migrations / creates the schema for the configured provider before serving traffic.
- HTTPS with Secure cookies in production; `storage/` must be writable and is excluded from Git.
