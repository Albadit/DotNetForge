# CMS Overview (for AI agents)

DotNetForge CMS is a modular, secure **hybrid CMS** on ASP.NET Core MVC (.NET 10), EF Core, and
Razor. It runs three modes over the same content: **Traditional** (visual admin at `/admin`),
**Headless** (token API under `/api`), and **Hybrid** (both).

## Orientation

- Configuration: `.env` (copied from `.env.example`). `DATABASE_PROVIDER` is `sqlite` (default) or
  `postgresql`. Secrets never leave `.env`.
- First run: the app redirects to `/setup`; completing it creates the first **Super Admin** and marks
  the CMS installed (`SystemState.Installed`). Afterwards `/setup` is blocked.
- Run from the repository root: `dotnet run`, `dotnet watch`, `dotnet build`. Tests:
  `dotnet test tests/DotNetForge.Tests` and `dotnet test tests/DotNetForge.IntegrationTests`.

## Where things live

- The web host (`DotNetForge.Web`) is at the repository **root**.
- Core libraries are under `src/` (see [architecture-summary.md](architecture-summary.md)).
- Extensions live under `extensions/` and are never overwritten by core updates.
- Runtime data lives under `storage/` (git-ignored).

## Key invariants

- Every admin and API action is gated by an RBAC permission check ([permissions.md](permissions.md)).
- The admin area is never restyled by public themes.
- Tenants are resolved first; scoped data is filtered by `TenantId`.
- Extension manifests are validated before install ([extension-system.md](extension-system.md)).
