# DotNetForge CMS

Server-rendered ASP.NET Core MVC CMS on .NET 10 (EF Core, SQLite/PostgreSQL). The web host is the repository root
project; libraries are in `src/`.

- **Documentation:** start at `.docs/README.md`. It is verified against the code. Planned-but-unbuilt behaviour is
  only in each doc's `## Planned (not implemented)` section - check `.docs/implementation-status.md` before assuming a
  feature exists.
- **Rules for changes:** `.docs/architecture/codebase.md#architectural-rules-for-changes` and
  `.docs/architecture/dependencies.md#dependencies-that-must-not-be-bypassed`.
- **Terminology:** `.docs/glossary.md` ("screen" = UI view, "content page"/`Page` = entity, "live" ≠ `Published`).
- **Skills:** `admin-page`, `api-endpoint`, `database-change`, `admin-extension`, `verify`, `documentation`,
  `software-engineering`, `release-notes` (in `.claude/skills/`).

## Commands (repository root)

```bash
cp .env.example .env                              # once
dotnet run                                        # http://localhost:5000 → /setup on a fresh DB
dotnet build
dotnet test tests/DotNetForge.Tests
dotnet test tests/DotNetForge.IntegrationTests
python .claude/skills/verify/check_links.py       # docs link check
```

## Must know

- All DI in `Startup/DependencyRegistration.cs`. Admin controllers derive from `AdminControllerBase`; API
  controllers from `ApiControllerBase` with `[RequireApiPermission]` on every action.
- Filter every tenant-scoped query by `TenantId` from the base controller - there are no global query filters.
- Content-page rules live only in `Services/PageService.cs`; public visibility only in `HomeController.Live`.
- Every admin POST: `[ValidateAntiForgeryToken]` + `AuditService.LogAsync(AuditActions.X, ...)`.
- The deployment directory is read-only: never write runtime files under the content root - use `IFileStorage`
  (media) or the database; `ReadOnlyDeploymentTests` fails otherwise. No inline script/style in views (CSP).
- Content/media actions check permissions with `Can`/`CanModify` (`AdminControllerBase`); inject `IAuditService` and
  `IPageService` (interfaces in `src/DotNetForge.Shared`), not the concrete classes.
- Schema changes need a migration for **both** `DotNetForgeDbContext` (SQLite) and `PostgreSqlDbContext`.
- `IEmailSender` has no implementation and webhooks are not delivered (see `.docs/implementation-status.md`).
- Optional: `DNF_TEST_POSTGRES` runs the integration tests on PostgreSQL; `DNF_TEST_S3_*` runs live S3 tests
  (`compose.dev.yml` provides both servers).
- Documentation is part of every change - update the owning `.docs/` file (mapping in the `documentation` skill).
