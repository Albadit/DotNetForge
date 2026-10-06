---
name: database-change
description: >-
  Changes the DotNetForge CMS database schema or seed data safely: entity in src/DotNetForge.Shared/Entities,
  fluent configuration in DotNetForgeDbContext, EF Core migrations in
  src/DotNetForge.Data/Database/Providers/<Database>/Migrations via the pinned dotnet-ef tool, idempotent DataSeeder
  changes, migrations for every SQL provider (SQLite DotNetForgeDbContext,
  PostgreSqlDbContext, SqlServerDbContext, MySqlDbContext; MongoDB needs none), and .docs/architecture/database.md.
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
3. **Migrations - one per SQL provider** (from the repository root; generating never connects to a database):
   ```bash
   dotnet tool restore
   for ctx in DotNetForgeDbContext:Sqlite PostgreSqlDbContext:PostgreSql \
              SqlServerDbContext:SqlServer MySqlDbContext:MySql; do
     dotnet ef migrations add <PascalCaseName> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data \
       --context "${ctx%%:*}" --output-dir "Database/Providers/${ctx#*:}/Migrations"
   done
   ```
   Then:
   - Strip the UTF-8 BOM EF writes (`.editorconfig` charset is `utf-8`; `dotnet format` flags `CHARSET`).
   - Check there is exactly one `<Context>ModelSnapshot.cs` per folder; don't pass `--namespace`, EF then writes the
     snapshot elsewhere.
   - `dotnet ef migrations has-pending-model-changes --context <Context>` must report no changes.
   - Read the generated `Up`/`Down` of all four. Indexed strings need `HasMaxLength` (SQL Server/MySQL can't index
     unbounded text). Integer keys that get explicit values must not become identity columns (SQL Server; see
     `SqlServerDbContext`).
   - Never edit an already-committed migration: add a new one.
4. **Apply** - every provider prepares its schema at startup (`DatabaseInitializer` → the provider's
   `InitializeSchemaAsync`: migrations, or `EnsureCreated` for MongoDB, which adds new collections/indexes but never
   changes existing indexes).
   - Verify on real servers with the integration tests: `DNF_TEST_POSTGRES`, `DNF_TEST_SQLSERVER`, `DNF_TEST_MYSQL`,
     `DNF_TEST_MONGODB` (`docker compose -f docker/compose.dev.yml --profile <db> up -d`;
     `.docs/guides/testing.md#databases`).
   - Writing `DateTime` values: always UTC.
5. **Seed data** - only in `src/DotNetForge.Data/DataSeeder.cs`, guarded so it is idempotent (it runs on every start).
   Use constants from `src/DotNetForge.Shared/Constants`.
6. **Callers** - update view models, `PageService`-style mapping, API projections; unique-index violations surface as
   `DbUpdateException` - validate in the owning service first. Queries stay single-table (no joins, no navigation
   properties across tables) so MongoDB can run them (`.docs/database/mongodb.md#query-rules`).
7. **Tests** - build and run both test projects; integration tests migrate a fresh SQLite DB, and with a `DNF_TEST_*`
   server a fresh database of that kind per test, which catches broken migrations. New string columns: add a length
   check in the owning service (PostgreSQL, SQL Server and MySQL enforce `HasMaxLength`, SQLite does not).
8. **Docs** - `.docs/architecture/database.md` (table row, ER diagram if relationships changed, migrations table,
   seeding table); feature docs that describe the entity's fields.
9. Run the **verify** skill.

## Don't

- Use raw SQL, `EnsureCreated`, or `Database.Migrate` from anywhere but `DatabaseInitializer`.
- Add a migration for only some of the SQL providers.
- Reference `DotNetForge.Data` from `DotNetForge.Core` (add a store interface in `Shared/Stores` instead).
- Delete `storage/dotnetforge.db` on someone else's machine; locally it resets the CMS to the setup wizard.
