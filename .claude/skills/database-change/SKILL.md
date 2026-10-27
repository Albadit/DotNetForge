---
name: database-change
description: >-
  Changes the DotNetForge CMS database schema or seed data safely: entity in src/DotNetForge.Shared/Entities,
  fluent configuration in DotNetForgeDbContext, one EF Core migration for PostgreSqlDbContext in
  src/DotNetForge.Data/Database/Providers/PostgreSql/Migrations via the pinned dotnet-ef tool (the CMS database is
  PostgreSQL only), idempotent DataSeeder changes, and .docs/architecture/database.md. Use when asked to add or change
  a table, column, index, relationship, enum stored in the DB, or seed data, or to create/fix a migration.
---

# Database change

Read first: `.docs/architecture/database.md`. The CMS database is always PostgreSQL; SQLite, SQL Server, MySQL and
MongoDB are only additional databases (`DATABASES_<NAME>_*`) and never hold CMS tables, so they need no migrations.

## Steps

1. **Entity** - add/change the class in `src/DotNetForge.Shared/Entities/` (plain POCO, `Guid Id = Guid.NewGuid()`,
   UTC `DateTime`s). Enums go in `src/DotNetForge.Shared/Enums/Enums.cs` (stored as int - append values, never
   renumber).
2. **Mapping** - in `DotNetForgeDbContext.OnModelCreating`: key, `IsRequired()`, `HasMaxLength(...)` for every string,
   indexes, relationships + delete behaviour, `Ignore(...)` for computed properties. New table → add a `DbSet<T>` property.
3. **Migration - one, for `PostgreSqlDbContext`** (from the repository root; generating never connects to a database):
   ```bash
   dotnet tool restore
   dotnet ef migrations add <PascalCaseName> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data --context PostgreSqlDbContext --output-dir Database/Providers/PostgreSql/Migrations
   ```
   Then:
   - Strip the UTF-8 BOM EF writes (`.editorconfig` charset is `utf-8`; `dotnet format` flags `CHARSET`).
   - Check there is exactly one `PostgreSqlDbContextModelSnapshot.cs` in the folder; don't pass `--namespace`, EF
     then writes the snapshot elsewhere.
   - `dotnet ef migrations has-pending-model-changes --project src/DotNetForge.Data --startup-project src/DotNetForge.Data --context PostgreSqlDbContext`
     must report no changes.
   - Read the generated `Up`/`Down`: column types, defaults for existing rows, data moves.
   - Never edit an already-committed migration: add a new one.
4. **Apply** - `DatabaseInitializer` applies the migrations at startup (`PostgreSqlDatabaseProvider.InitializeSchemaAsync`
   → `MigrateAsync`).
   - Verify with the integration tests on PostgreSQL: `docker compose -f docker/databases/postgres.yml up -d`, then
     `dotnet test tests/DotNetForge.IntegrationTests` (`DNF_TEST_POSTGRES` overrides the server;
     `.docs/guides/testing.md#databases`). Every factory migrates a fresh database, which catches broken migrations.
   - Writing `DateTime` values: always UTC.
5. **Seed data** - only in `src/DotNetForge.Data/DataSeeder.cs`, guarded so it is idempotent (it runs on every start);
   a one-time update of existing data goes into `UpgradeAsync` with the next `CurrentSeedVersion`. Use constants from
   `src/DotNetForge.Shared/Constants`.
6. **Callers** - update view models, `PageService`-style mapping, API projections; unique-index violations surface as
   `DbUpdateException` - validate in the owning service first. Joins and navigations are fine now (no MongoDB CMS);
   follow the surrounding code's style.
7. **Data Management** - every entity is exported and imported automatically (`TransferFile`, model-driven); a table
   holding secrets or site-own state goes into `TransferFile.NotTransferred`.
8. **Tests** - build and run both test projects. New string columns: add a length check in the owning service
   (PostgreSQL enforces `HasMaxLength`).
9. **Docs** - `.docs/architecture/database.md` (table row, ER diagram if relationships changed, migrations table,
   seeding table); feature docs that describe the entity's fields.
10. Run the **verify** skill.

## Don't

- Use raw SQL, `EnsureCreated`, or `Database.Migrate` from anywhere but `DatabaseInitializer`.
- Add a context or migrations for another provider: SQLite, SQL Server, MySQL and MongoDB never host the CMS.
- Reference `DotNetForge.Data` from `DotNetForge.Core` (add a store interface in `Shared/Stores` instead).
- Drop someone else's database; locally, re-creating the `dotnetforge` database resets the CMS to the setup wizard.
