# Architecture overview

DotNetForge CMS is a server-rendered **ASP.NET Core MVC** CMS (C#, EF Core, Razor) on **.NET 10**, with **SQLite**
(default) or **PostgreSQL**, configured through `.env`. It serves a role-gated admin area, a public site resolved from
a page tree, and a token-secured headless API over the same data, and keeps the core (`src/`) separate from
user extensions (`extensions/`).

This is the one-page overview. The detailed, code-verified architecture:

| Topic | Document |
| --- | --- |
| Projects, folders, layers, DI, rules for changes | [codebase.md](codebase.md) |
| Startup, request pipeline, routing, auth/API/background flows | [data-flow.md](data-flow.md) |
| Screens, route map, layouts, navigation | [pages.md](pages.md) |
| Tables, migrations, seeding | [database.md](database.md) |
| Project references, packages, DI registrations | [dependencies.md](dependencies.md) |
| Built vs. specified | [implementation-status.md](../implementation-status.md) |

## Layout

```text
DotNetForge.slnx               solution (every project)
src/
  DotNetForge.Web/             MVC web app: Program.cs, Startup/, Middleware/, Services/, Controllers/, Areas/Admin/,
                               Views/, wwwroot/ - composition root, admin area, public site, setup
  DotNetForge.Abstractions/    contracts only, no dependencies
  DotNetForge.Shared/          entities, enums, constants, DTOs, results, manifest model, store interfaces
  DotNetForge.Core/            installation, permission matrix evaluation, validators (no EF Core)
  DotNetForge.Data/            EF Core contexts, migrations (SQLite + PostgreSQL), seeding, provider selection
  DotNetForge.Infrastructure/  BCL implementations: hashing, tokens, signing, .env, paths, file storage
  DotNetForge.Extensions/      manifest validation + discovery
  DotNetForge.Api/             headless API controllers, token auth, permission filter
tests/        DotNetForge.Tests (unit), DotNetForge.IntegrationTests (real host)
extensions/   extensions by type (one manifest per folder); copied next to the app on publish
docker/       Dockerfile (read-only friendly production image) and compose.dev.yml (local PostgreSQL + S3)
.docs/        technical documentation, incl. planned work per topic
```

This matches the spec's canonical tree (`src/DotNetForge.Web/` and the libraries under `src/`). Run the app with
`dotnet run --project src/DotNetForge.Web`; `dotnet build` and `dotnet test` at the root use the solution. AI-agent
quick references live in `.github/agents/` instead of a root `agents/` folder.

## Layering

```mermaid
flowchart BT
    Shared --> Abstractions
    Core --> Shared
    Data --> Shared
    Infrastructure --> Core
    Extensions --> Core
    Api --> Core
    Api --> Data
    Web["Web"] --> Api & Extensions & Infrastructure
```

- `Core` and `Data` are siblings; `Core` needs persistence only through interfaces in `Shared/Stores`
  (`IInstallationStore`).
- `Api` queries `Data` directly - a documented shortcut.
- `PermissionMatrix` lives in `Shared` so the seeder and `PermissionService` share it.
- All DI is in `src/DotNetForge.Web/Startup/DependencyRegistration.cs`.

## Key decisions

- **Server-rendered admin**, no SPA or Node build; progressive enhancement only.
- **Two auth schemes:** cookie (`dnf.auth`) for the admin, `ApiToken` bearer for `/api`.
- **Role-based admin gating** (`AdminArea` policy + `[Authorize(Roles = ...)]`), plus `(area, action)` checks from the
  permission matrix for content and media actions; granular permission keys for API tokens.
- **Install gate** middleware until the first Super Admin exists.
- **Liveness of content pages is computed per request** from `Published`, `Disabled` and the schedule; a background
  job only tidies passed schedules.
- **Extensions are discovered from disk and validated**; only `admin` extensions are rendered (runtime Razor
  compilation, iframe). No extension assemblies are loaded.
- **BCL-only security primitives** (PBKDF2, HMAC, RNG) and an in-house `.env` parser - no extra dependencies.
- **Read-only deployment directory.** Runtime data goes to the database (incl. the Data Protection key ring) and to
  object storage behind `IFileStorage` (local directory in development, S3-compatible - Cloudflare R2 recommended - in
  production). Production configuration may not default into the app's folder ([deployment](../guides/deployment.md)).
- **Five databases through providers:** SQLite, PostgreSQL, SQL Server, MySQL and MongoDB, each an `IDatabaseProvider`
  registered by name. SQL providers apply their own migration set at startup (one context type each); MongoDB creates
  its collections and indexes. `IDatabaseService` runs structured commands on any configured database
  ([database layer](../database/architecture.md)).
- **Defence in depth on the web layer:** CSP and security headers, rate limiting, session re-validation, content and
  media permission checks from the role permission matrix.
