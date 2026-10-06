# Transfer, updates and backups

Two operational concerns: provider-aware database **import/export** (the admin **Transfer** screen, including
SQLite ↔ PostgreSQL moves) and safe **core updates** with pre-update backups and rollback. None of it is built;
only reserved folders, a version column and a few flags exist.

## Current state

| Piece | Location | Status |
| --- | --- | --- |
| Transfer screen | `GET /admin/transfer` → `ModulesController.Transfer` | [placeholder](../pages/module-placeholders.md), sidebar **Settings · Global Settings**; open to every admin-capable role |
| `storage/backups/`, `storage/updates/` | repository `storage/` | empty (`.gitkeep` only), contents git-ignored; nothing reads or writes them |
| `SystemState.CmsVersion` (string, max 50, default `"1.0.0"`) | `src/DotNetForge.Shared/Entities/SystemState.cs` | shown on the [Dashboard](../pages/dashboard.md); never changed; not taken from the assembly version |
| `PermissionAreas.Updates` | `src/DotNetForge.Shared/Constants/Permissions.cs` | in `PermissionMatrix` only Super Admin is granted it (Admin gets nothing - asserted in `PermissionTests`); seeded as `RolePermission` rows, **not enforced** ([authorization](authorization.md)) |
| `InstalledExtension.UpdateAvailable` | `src/DotNetForge.Shared/Entities/InstalledExtension.cs` | returned by `GET /api/extensions`; `InstalledExtensions` is never written, so always empty |
| Schema creation | `src/DotNetForge.Data/DatabaseInitializer.cs` | the main provider's `InitializeSchemaAsync`: migrations per SQL provider (`Migrations/`, `Migrations/PostgreSql/`, `Migrations/SqlServer/`, `Migrations/MySql/`), `EnsureCreated` for MongoDB; for PostgreSQL), applied at startup ([database](../architecture/database.md)) |
| Provider selection | `DATABASE_PROVIDER` (or detected from the connection string), resolved by `DatabaseProviderRegistry` ([database configuration](../database/configuration.md)) | ✔ one provider per process; no cross-provider tooling |
| Core / extensions / runtime split | `src/` (web host in `src/DotNetForge.Web`); `extensions/` at the repository root (outside every project, discovered by `ExtensionLoader` under `AppEnvironment.ExtensionsPath`); `storage/` | ✔ folders separated |
| `.gitignore` | repository root | ignores `.env`/`.env.*` (keeps `.env.example`), `bin/`, `obj/`, `storage/{media,backups,logs,updates}/*`, `*.db*`, `node_modules/`, test output; **not** `out/`, `publish/`, `*.log`, `.cache/` |

No audit actions exist for transfer, update or rollback. `InstalledExtension`, `SystemState` and `AuthProvider`
have no `TenantId`, which matters for tenant-scoped exports.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- **Transfer** screen (Settings → Global Settings): export and import the CMS dataset (content, media metadata,
  users, roles, settings, extensions registry, webhooks, ...), choose source and target database type (`sqlite`,
  `postgresql`), provider-aware migration (types, identity/sequence handling, constraints), import-file validation,
  progress (percentage, records, stage), result log, tenant scope (one tenant or all tenants).
- Export files are self-describing: source provider, CMS schema version, CMS application version, tenant scope and
  a checksum. Exports never contain `.env` secrets.
- **Updates**: check for core updates (manual and/or scheduled) with status on the Settings overview and the
  [Dashboard](../pages/dashboard.md); back up before every update; apply to core files only; roll back to the
  previous working version; restore automatically when an update fails.
- Updates never touch `extensions/` or `storage/` (custom extensions and user data survive).
- **Extension updates** (`InstalledExtension.UpdateAvailable`, update action) are part of the extension lifecycle:
  [extensions](extensions.md).
- Repository hygiene: `.gitignore` excludes generated files, logs, uploads, cache, build output and secrets.

### Folder layout

```text
src/                    CORE - replaced by the updater (web host in src/DotNetForge.Web)
extensions/             CUSTOM EXTENSIONS - never written by updates
storage/                RUNTIME DATA - git-ignored, preserved across updates
  media/                uploads
  backups/              pre-update backups + database exports
  logs/                 transfer and update logs
  updates/              downloaded / staged update packages
```

In a published deployment the core is the publish output (`/app`), with `extensions/` copied inside it; an updater
must replace everything there except `extensions/` and `.env`.

Proposed `.gitignore` additions: `out/`, `publish/`, `*.log`, `.cache/`.

### Transfer result log

| Field | Type | Req. | Notes |
| --- | --- | --- | --- |
| `id` | guid | yes | |
| `operation` | enum | yes | `export` \| `import` |
| `sourceProvider`, `targetProvider` | enum | yes | `sqlite` \| `postgresql` |
| `tenantScope` | string | yes | tenant id or `all` |
| `status` | enum | yes | `success` \| `failed` \| `incomplete` |
| `recordCount` | int | yes | |
| `startedAt` | datetime | yes | |
| `completedAt` | datetime | no | null if interrupted |
| `error` | string | no | when `failed` / `incomplete` |
| `initiatedBy` | string | yes | user id |

### Update record

| Field | Type | Req. | Notes |
| --- | --- | --- | --- |
| `id` | guid | yes | |
| `fromVersion`, `toVersion` | string | yes | `fromVersion` = working version before the update |
| `backupRef` | string | yes | path/reference of the pre-update backup |
| `backupVerified` | bool | yes | |
| `status` | enum | yes | `checking` \| `backing-up` \| `applying` \| `success` \| `failed` \| `rolled-back` |
| `autoRolledBack` | bool | yes | |
| `startedAt` / `completedAt` | datetime | yes / no | |
| `initiatedBy` | string | yes | user id |

### Roles

Every transfer, update and rollback action (export one tenant or all tenants, import, choose providers, check,
apply, roll back, view transfer/update logs) is **Super Admin only** - matching the `Updates` area of
`PermissionMatrix`. A tenant Admin may export/import only their own tenant and only if explicitly granted; all-tenant
transfers and core updates stay with the global Super Admin. Every action is audited
([audit logging](audit-logging.md)).

### User flows

1. **Export**: Transfer → Export → source type (defaults to the current provider) → tenant scope (a tenant or
   *All tenants*) → confirm → stream the data with progress → write the self-describing file to
   `storage/backups/` → log the result (counts, duration, status) → offer the download.
2. **Import**: Transfer → Import → upload or pick an export → source type read from the file, operator picks the
   target type (default: current provider) → validate (schema, checksum, version, provider compatibility) → if
   source ≠ target run provider-aware migration → import with progress for the chosen scope → log the result. Any
   validation or integrity failure aborts before data changes and is logged.
3. **Update check**: Settings overview or Dashboard → *Check for updates* → query the update source → show the
   newer version and notes → enable *Apply update*.
4. **Apply update**: back up core and database to `storage/backups/` and record the current version → verify the
   backup (failure aborts with no change) → apply to core files only → run migrations → record the new version
   (`SystemState.CmsVersion`) and log; on failure roll back automatically and log.
5. **Roll back** (manual or automatic): find the latest valid pre-update backup → none: refuse with an error
   (manual recovery) → restore core files and database without touching extensions → record the restored version →
   log.

### Rules and validation

| Rule | Constraint |
| --- | --- |
| Source / target provider | required; `sqlite` or `postgresql`; target defaults to the current provider |
| Import file | required; must match the DotNetForge export schema |
| Integrity | embedded checksum must match; corrupt or partial files rejected |
| Version compatibility | export schema version must be compatible with this instance |
| Provider compatibility | when source ≠ target, every entity must be translatable, otherwise reject |
| Tenant scope | required; the actor must be authorized for it |
| Atomicity | validate fully before writing; a failed import leaves the target database unchanged |
| Backup before update | created and verified first; no backup, no update |
| Current version | recorded before the update so rollback has a target |
| Extension preservation | updates never write to or delete under `extensions/` or `storage/` |
| Rollback target | a valid backup must exist, otherwise refuse explicitly |
| Update source | must be reachable; failures reported, never ignored |
| Migration safety | migrations applied by an update are reversible or covered by the backup (today impossible on PostgreSQL, which has no migrations) |

### Edge cases

- Provider mismatch with an untranslatable type/constraint/sequence: abort before writing, log the reason.
- Corrupt or partial import file: rejected before any change, failure logged.
- Update fails mid-way: automatic rollback to the backup, previous version restored, extensions and storage
  untouched, failure logged.
- Backup fails: update aborted with no change; operator notified.
- Rollback without a backup: refused with guidance toward manual recovery; nothing destructive happens.
- Huge dataset / timeout: stream with progress, chunked or resumable, admin UI not blocked; an interrupted import
  aborts cleanly, an interrupted export is logged `failed`/`incomplete`.
- Concurrent operations: one update or one transfer of overlapping scope at a time; others blocked or queued.
- An update writing to `extensions/` is a defect and must be impossible by design.
- A tenant Admin attempting an all-tenant transfer or any core update is denied.

### Acceptance criteria

- [ ] The Transfer screen is Super Admin only and supports Export and Import (today a placeholder open to every admin-capable role).
- [ ] Export writes a self-describing file (source provider, schema version, app version, tenant scope, checksum) to `storage/backups/`.
- [ ] Import requires a target database type and reads/confirms the source type from the file.
- [ ] SQLite and PostgreSQL are supported as both source and target.
- [ ] Export from one provider and import into the other succeeds via provider-aware migration.
- [ ] Import validates schema, checksum, version and provider compatibility before changing the target database.
- [ ] A corrupt or partial import file is rejected without changes and the failure is logged.
- [ ] Long-running export/import shows progress.
- [ ] Every export and import writes a transfer result log entry (status, counts, duration, errors).
- [ ] Import/export can be scoped per tenant; *All tenants* is Super Admin only.
- [x] Core files and custom extensions are in separate folders, with extensions in a dedicated `extensions/` folder (`ExtensionLoader` root `AppEnvironment.ExtensionsPath`; copied into the publish output by `src/DotNetForge.Web/DotNetForge.Web.csproj`).
- [ ] Core update checks exist and update status is shown on the Settings overview / Dashboard (the Dashboard shows only `SystemState.CmsVersion`).
- [ ] Applying an update creates and verifies a backup first; a failed backup aborts the update.
- [ ] An update never overwrites or deletes anything under `extensions/` or `storage/`.
- [ ] A failed update automatically rolls back and restores the previous version.
- [ ] Rollback succeeds when a valid backup exists.
- [ ] Rollback without a backup is refused with an explicit error and nothing destructive happens.
- [ ] Only one update or overlapping-scope transfer runs at a time.
- [ ] An interrupted huge-dataset transfer leaves the target consistent and is logged `failed`/`incomplete`.
- [ ] Every transfer, update and rollback is audited.
- [ ] `.gitignore` excludes generated files, logs, uploads, cache, build output and secrets, and `.env` is never committed (all covered except a cache entry and `*.log`; `.env` ✔).

## Where to change things

- **PostgreSQL migrations first**: provider-aware transfer and update-time migrations need a real PostgreSQL
  migration set instead of `EnsureCreatedAsync` in `DatabaseInitializer` (`database-change` skill,
  [database](../architecture/database.md)).
- **Export/import**: a service in `src/DotNetForge.Data` (it needs `DotNetForgeDbContext` and every provider) that
  writes a provider-neutral format (e.g. JSON per table + a manifest with versions and checksum) via EF Core, not
  raw SQL. Validation of the manifest is pure logic for `src/DotNetForge.Core`. Read paths from
  `IHostEnvironment.ContentRootPath`, never from request input.
- **Long-running work**: run transfers and updates as a hosted background job (the pattern of
  `ScheduledPublishingService`, own scope) with a single-runner lock; the screen polls a status record instead of
  holding the request open.
- **Entities**: `TransferLog` and `UpdateRecord` in `src/DotNetForge.Shared/Entities` + migration; new
  `AuditActions` constants for export, import, update and rollback.
- **Versioning**: set `SystemState.CmsVersion` from the applied release (or derive it from the assembly version)
  in one place.
- **Updater**: core-file replacement cannot run inside the process it replaces; plan an external updater or
  deployment-level step that stages packages in `storage/updates/`, backs up to `storage/backups/`, and never touches
  `extensions/`, `storage/` or `.env`.
- **Screen**: replace `ModulesController.Transfer` with an admin controller restricted to `Roles.SuperAdmin`
  (`admin-page` skill); mutations `POST` + antiforgery + audit. Document it under [pages](../pages/) and update
  [module placeholders](../pages/module-placeholders.md), [implementation status](../implementation-status.md) and,
  when update status appears, the [Dashboard](../pages/dashboard.md) doc.
