# CMS Overview (for AI agents)

DotNetForge CMS is a server-rendered **ASP.NET Core MVC** CMS on .NET 10 (EF Core, Razor). It serves a role-gated
admin at `/admin`, a public site resolved from a page tree, and a token-secured headless API under `/api` over the
same data. Full documentation: [`.docs/README.md`](../../.docs/README.md).

## Orientation

- Configuration: `.env` (copy `.env.example`). `DATABASE_CONNECTION_STRING` decides the database: empty or
  `Data Source=…` is SQLite, `Host=…` is PostgreSQL.
  See [configuration](../../.docs/features/configuration.md).
- First run: every request redirects to `/setup`; it creates the first **Super Admin** and sets
  `SystemState.Installed`. Afterwards `/setup` redirects to `/admin`. See [installation](../../.docs/features/installation.md).
- From the repository root: `dotnet run --project src/DotNetForge.Web` (or `dotnet watch --project ...`),
  `dotnet build` and `dotnet test` (solution `DotNetForge.slnx`).

## Where things live

See [codebase.md](../../.docs/architecture/codebase.md) for every folder. Screens and routes:
[pages.md](../../.docs/architecture/pages.md#route-map).

## Key invariants (as implemented)

- Admin screens are gated by **roles** (`AdminArea` policy + `[Authorize(Roles = ...)]`); API actions by
  **permission keys** (`[RequireApiPermission]`). The `(area, action)` matrix is seeded but not enforced.
  See [authorization](../../.docs/features/authorization.md).
- One tenant is seeded and used; queries filter by `TenantId` manually; there is no tenant resolution.
  See [multi-tenancy](../../.docs/features/multi-tenancy.md).
- Extensions are discovered and validated from `extensions/`; only `admin` extensions are rendered.
- The deployment directory is read-only: runtime files go to object storage via `IFileStorage` (local in development,
  S3-compatible in production), everything else to the database. See [deployment](../../.docs/guides/deployment.md).
  See [extensions](../../.docs/features/extensions.md).
- Planned-but-unbuilt behaviour lives only in each doc's **Planned (not implemented)** section - check
  [implementation-status](../../.docs/implementation-status.md) before assuming a feature exists.
