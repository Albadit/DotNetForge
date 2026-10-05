---
name: database-change
description: >-
  Changes the DotNetForge CMS database schema or seed data safely: entity in src/DotNetForge.Shared/Entities,
  fluent configuration in DotNetForgeDbContext, an EF Core migration in src/DotNetForge.Data/Migrations via the pinned
  dotnet-ef tool, idempotent DataSeeder changes, migrations for both SQLite (DotNetForgeDbContext) and PostgreSQL (PostgreSqlDbContext), and .docs/architecture/database.md.
  Use when asked to add or change a table, column, index, relationship, enum stored in the DB, or seed data, or to
  create/fix a migration.
---

# Database change

Read first: `.docs/architecture/database.md`.

## Steps

1. **Entity** - add/change the class in `src/DotNetForge.Shared/Entities/` (plain POCO, `Guid Id = Guid.NewGuid()`,
   `TenantId` for tenant-scoped data, UTC `DateTime`s). Enums go in `src/DotNetForge.Shared/Enums/Enums.cs` (stored as
   int - append values, never renumber).
2. **Mapping** - in `DotNetForgeDbContext.OnModelCreating`: key, `IsRequired()`, `HasMaxLength(...)` for every string,
   indexes (unique ones include `TenantId` for tenant-scoped data), relationships + delete behaviour, `Ignore(...)` for
   computed properties. New table → add a `DbSet<T>` property.
3. **Migrations - one per provider** (from the repository root; generating never connects to a database):
   ```bash
   dotnet tool restore
   dotnet ef migrations add <PascalCaseName> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
     --context DotNetForgeDbContext --output-dir Migrations
   dotnet ef migrations add <PascalCaseName> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
     --context PostgreSqlDbContext --output-dir Migrations/PostgreSql --namespace DotNetForge.Data.Migrations.PostgreSql
   ```
   Then: strip the UTF-8 BOM EF writes (`.editorconfig` charset is `utf-8`; `dotnet format` flags `CHARSET`), and if
   EF put `PostgreSqlDbContextModelSnapshot.cs` under `src/DotNetForge.Data/DotNetForge/Data/Migrations/PostgreSql/`,
   move it into `Migrations/PostgreSql/` and delete the stray folder. Read the generated `Up`/`Down` of both. Never
   edit an already-committed migration - add a new one.
4. **Apply** - both providers migrate at startup (`DatabaseInitializer` → `MigrateAsync`). Verify PostgreSQL by
   running the integration tests with `DNF_TEST_POSTGRES` (e.g. against `docker compose -f docker/compose.dev.yml up -d`).
   Writing `DateTime` values: always UTC (`timestamp with time zone` rejects other kinds on PostgreSQL).
5. **Seed data** - only in `src/DotNetForge.Data/DataSeeder.cs`, guarded so it is idempotent (it runs on every start).
   Use constants from `src/DotNetForge.Shared/Constants`.
6. **Callers** - update view models, `PageService`-style mapping, API projections; unique-index violations surface as
   `DbUpdateException` - validate in the owning service first.
7. **Tests** - build and run both test projects; integration tests migrate a fresh SQLite DB, and with
   `DNF_TEST_POSTGRES` a fresh PostgreSQL DB per test, which catches broken migrations. New string columns: add a
   length check in the owning service (PostgreSQL enforces `HasMaxLength`, SQLite does not).
8. **Docs** - `.docs/architecture/database.md` (table row, ER diagram if relationships changed, migrations table,
   seeding table); feature docs that describe the entity's fields.
9. Run the **verify** skill.

## Don't

- Use raw SQL, `EnsureCreated`, or `Database.Migrate` from anywhere but `DatabaseInitializer`.
- Add a migration for only one provider.
- Reference `DotNetForge.Data` from `DotNetForge.Core` (add a store interface in `Shared/Stores` instead).
- Delete `storage/dotnetforge.db` on someone else's machine; locally it resets the CMS to the setup wizard.
