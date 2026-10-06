# Development Guidelines (for AI agents)

Full guide: [development.md](../../.docs/guides/development.md). Testing: [testing.md](../../.docs/guides/testing.md).

## Build, run, test

- From the repository root: `dotnet run --project src/DotNetForge.Web`, `dotnet watch --project src/DotNetForge.Web`,
  `dotnet build`, `dotnet test` (solution `DotNetForge.slnx`).
- Tests: `dotnet test tests/DotNetForge.Tests`, `dotnet test tests/DotNetForge.IntegrationTests`.
- Migrations: `dotnet tool restore`, then add the migration for **each SQL context** (`DotNetForgeDbContext`,
  `PostgreSqlDbContext`, `SqlServerDbContext`, `MySqlDbContext`, output `Database/Providers/<Database>/Migrations/`,
  with `--project/--startup-project src/DotNetForge.Data`); see the `database-change` skill.
- Optional backends: `docker compose -f docker/compose.dev.yml up -d` (PostgreSQL + S3); `DNF_TEST_POSTGRES` and
  `DNF_TEST_S3_*` run the tests against them.

## Layering rules

- `Abstractions` has no dependencies and no entities.
- `Core` and `Data` are siblings; bridge them with an interface in `Shared/Stores` implemented in `Data`.
- All DI in `src/DotNetForge.Web/Startup/DependencyRegistration.cs`.
- Admin controllers derive from `AdminControllerBase`; API controllers from `ApiControllerBase` and carry
  `[RequireApiPermission]` on every action.
- Filter tenant-scoped queries by `TenantId`; content-page rules only in `PageService` (via `IPageService`).
- Never write runtime files under the content root (read-only in production): use `IFileStorage` or the database.
- Check `(area, action)` permissions with `Can`/`CanModify` for content and media actions; audit via `IAuditService`.
- No inline script/style in views (Content-Security-Policy).
- Complete list: [codebase.md → Architectural rules](../../.docs/architecture/codebase.md#architectural-rules-for-changes).

## Conventions

- File-scoped namespaces, nullable enabled, 4-space indent, LF (`.editorconfig`).
- Validate server-side; never log secrets; store passwords/tokens only as hashes.
- Unit test new pure logic; integration test new HTTP behaviour.
- Update the owning `.docs/` document in the same change.

## Definition of done

Both test projects green, `dotnet format --verify-no-changes` clean per project, docs updated. When implementing part
of the planned scope, use the **Acceptance criteria** in the owning document's *Planned (not implemented)* section and update
[implementation-status.md](../../.docs/implementation-status.md).
