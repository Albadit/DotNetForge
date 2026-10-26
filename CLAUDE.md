# DotNetForge CMS

Server-rendered ASP.NET Core MVC CMS on .NET 10 (EF Core; SQLite, PostgreSQL, SQL Server, MySQL or MongoDB). Solution `DotNetForge.slnx`; the web
app is `src/DotNetForge.Web`, libraries are the other `src/` projects, Docker files are in `docker/`.

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
dotnet build                                      # whole solution, incl. extensions
dotnet run --project src/DotNetForge.Web          # start; http://localhost:5000 → /setup on a fresh DB
dotnet test                                       # unit + integration tests
python .claude/skills/verify/check_links.py       # docs link check
```

## Must know

- All DI in `src/DotNetForge.Web/Startup/DependencyRegistration.cs`. Admin controllers derive from `AdminControllerBase`; API
  controllers from `ApiControllerBase` with `[RequireApiPermission]` on every action (only exception: the `/api/custom/` route
  of custom SQL endpoints, `.docs/features/custom-endpoints.md`).
- Content-page rules live only in `src/DotNetForge.Web/Services/PageService.cs`; public visibility only in
  `PageVisibility.Live` (`src/DotNetForge.Shared/Content/PublishedPages.cs`).
- Three separate systems (`.docs/features/structured-content.md`): **Content Manager** `/admin/content` (entries of
  content types), **Content types** `/admin/content-types` (fields), **Pages** `/admin/pages` (page tree, URLs, SEO;
  layouts in the page builder, which shows entries with *content lists*). Field/entry rules are pure code in
  `src/DotNetForge.Core/Content/` (`ContentSchema`, `EntryData`, `ItemData`); database rules in
  `src/DotNetForge.Content/` (`ContentTypeService`, `ContentEntryService`, `ContentQueryService`), shared by admin and
  API - never re-implement them in a controller. Filter/sort entries only through `ContentQueryService` (the
  `ContentEntryValues` index keeps it single-table).
- Every admin POST: `[ValidateAntiForgeryToken]` + `AuditService.LogAsync(AuditActions.X, ...)`.
- The deployment directory is read-only: never write runtime files under the content root - use `IFileStorage`
  (media) or the database; `ReadOnlyDeploymentTests` fails otherwise. No inline script/style in views (CSP).
- UI = the Forge design system (`.docs/architecture/design-system.md`): tokens/components in
  `src/DotNetForge.Web/wwwroot/css/forge/`, behaviours in `wwwroot/js/forge/ui.js`, tag helpers in
  `src/DotNetForge.Web/Ui/`, sidebar only in `AdminNavigation.Tree` (hidden per role, never the gate); feedback via
  `TempData` toasts + `data-confirm`; `<form-errors />`, never `asp-validation-summary`. `AdminUiTests` checks it.
- Content/media actions check permissions with `Can`/`CanModify` (`AdminControllerBase`); inject `IAuditService` and
  `IPageService` (interfaces in `src/DotNetForge.Shared`), not the concrete classes.
- Schema changes need a migration for **each SQL context**: `DotNetForgeDbContext` (SQLite), `PostgreSqlDbContext`,
  `SqlServerDbContext`, `MySqlDbContext` (MongoDB needs none). Keep LINQ single-table (no joins / cross-table
  navigations) so MongoDB can run it. Database code lives in `src/DotNetForge.Data/Database/` (.docs/database/):
  one self-contained folder per database in `Providers/<Database>/` (provider, SQL builder, executor, context,
  `Migrations/`). SQL code is copied, not shared, so a fix to shared SQL behaviour goes into all four SQL folders.
- Extensions live only in `extensions/` (manifest, code against `src/DotNetForge.Sdk`, views); they contribute only
  while enabled on Plugins, and "is it active?" is answered only by `ExtensionCatalog` (`.docs/features/extensions.md`).
  `dotnet build` builds the samples; integration tests build fixtures in `tests/fixtures/extensions/`. Code changes
  need a restart.
- Page builder (`.docs/features/page-builder.md`): C# in `src/DotNetForge.PageBuilder`, editor in
  `wwwroot/js/builder` (plain ES modules, no build). One schema copy in `wwwroot/builder/schema/` (embedded by the C#
  project) - change it, or the JS core/renderer, only with `node tests/js/page-builder-fixtures.mjs` and the
  `PageBuilder` unit tests green (C# and JS must render byte-identically; content-list cases are written by
  `node tests/js/content-list-fixtures.mjs`).
- `IEmailSender` has no implementation and webhooks are not delivered (see `.docs/implementation-status.md`).
- Optional: `DNF_TEST_POSTGRES` runs the integration tests on PostgreSQL; `DNF_TEST_S3_*` runs live S3 tests
  (`docker/compose.dev.yml` provides both servers).
- In a checkout the app reads `.env`, `extensions/` and development `storage/` from the repository root
  (`AppPaths`); a published app keeps them next to itself.
- Documentation is part of every change - update the owning `.docs/` file (mapping in the `documentation` skill).
