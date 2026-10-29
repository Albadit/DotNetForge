# Transfer, updates and backups

Two operational concerns: database **export/import and backups** (the admin [Data Management](../pages/data-management.md)
screen - built) and safe **core updates** with rollback (not built).

## Current state

| Piece | Location | Status |
| --- | --- | --- |
| Data Management screen | `GET /admin/data` → `DataManagementController`, sidebar **Settings → Global Settings** | ✔ Super Admin only ([Data Management](../pages/data-management.md)) |
| Export (download) | `DatabaseTransferService.ExportAsync(userId, database, ct)`, format `TransferFile` | ✔ every environment: the CMS database (importable) or an [additional database](#export-of-an-additional-database) (JSON copy, not importable) |
| Backups (manual, scheduled, pruned) | `DatabaseTransferService`, `AutomaticBackupService`; files in `IFileStorage` under `backups/` | ✔ every environment, CMS database only; schedule off by default |
| Import / restore a backup | `DatabaseTransferService.ImportAsync` / `RestoreAsync` | ✔ **Development only**; replaces all data in one transaction after a safety backup |
| Result log | `TransferRecord` (table `TransferRecords`) | ✔ every run, shown as History |
| Audit | `transfer.exported`, `transfer.imported`, `transfer.backup.created`/`.downloaded`/`.deleted`/`.restored` | ✔ ([audit logging](audit-logging.md)) |
| `storage/backups/`, `storage/updates/` | repository `storage/` | unused (`.gitkeep` only); backups go through `IFileStorage` because the deployment directory is read-only |
| `SystemState.CmsVersion` (string, max 50, default `"1.0.0"`) | `src/DotNetForge.Shared/Entities/SystemState.cs` | shown on the [Dashboard](../pages/dashboard.md); never changed; exports record the assembly version instead |
| `PermissionAreas.Updates` | `src/DotNetForge.Shared/Constants/Permissions.cs` | in `PermissionMatrix` only Super Admin is granted it; seeded, **not enforced** (the Data Management screen checks the Super Admin role) ([authorization](authorization.md)) |
| `InstalledExtension.UpdateAvailable` | `src/DotNetForge.Shared/Entities/InstalledExtension.cs` | returned by `GET /api/extensions`; never written (always `false`) |
| Core / extensions / runtime split | `src/`; `extensions/` at the repository root (`AppEnvironment.ExtensionsPath`); `storage/` | ✔ folders separated |
| `.gitignore` | repository root | ignores `.env`/`.env.*` (keeps `.env.example`), `bin/`, `obj/`, `storage/{media,backups,logs,updates}/*`, `*.db*`, `node_modules/`, test output; **not** `out/`, `publish/`, `*.log`, `.cache/` |

### Risk and environment

| Feature | Risk | Production |
| --- | --- | --- |
| Database export | low | ✔ Super Admin; the CMS database in one consistent read |
| Automatic backups | low | ✔ recommended (off until turned on) |
| Database import | critical | ✘ Development only - validated completely, safety backup first, one transaction |
| Database rollback (restore a backup) | critical | ✘ Development only - same flow as import |
| Core updates, application rollback | high | not built ([planned](#planned-not-implemented)) |

## Export file

The export and backup of the CMS database: a .zip (`dotnetforge-<export|backup>-postgresql-<yyyyMMdd-HHmmss>.zip`)
written by `TransferFile` from the EF model, never from a database's own types. The CMS database is always PostgreSQL;
because the format is model-driven, an export made on SQLite, SQL Server, MySQL or MongoDB by an earlier build imports
as well when it passes the [rules](#rules) (same data version and schema).

```text
manifest.json            TransferManifest: format "dotnetforge-export", formatVersion 1, appVersion, provider,
                         seedVersion, createdAt, tables[] { name, rows, sha256, columns[] { name, type, nullable } }
tables/<Entity>.json     one per entity, a JSON array of row objects (property name → value, enums as numbers)
```

- **Included:** every entity of `DotNetForgeDbContext` - pages and their permissions, content types/entries/values,
  media details, users (with their two-factor methods and recovery codes), roles, permissions, settings, auth providers,
  email templates, API tokens (hashes), custom
  endpoints, webhooks and deliveries, installed extensions, the audit log.
- **Not included:** `DataProtectionKeys` (this site's key ring - secrets), `SystemState` (this site's install state
  and data version), `TransferRecords` (this site's transfer history), media files - the separate
  [media export](../pages/data-management.md#media-export) downloads those.
- Values protected with the key ring can't be decrypted on another site: the tokens of custom API endpoints (refused
  - regenerate them), the built-in sign-in providers' client secrets and Apple key (the [Providers](../pages/providers.md)
  screen shows **Secret unreadable** and the provider is off until they are entered again), authenticator-app
  secrets (those apps stop working; the user signs in with another method or a recovery code and sets the app up
  again) and webhook signing secrets (the imported webhooks stay as they were, enabled ones included, but every
  attempt - pending ones from the export too - fails with "Not sent: …" without contacting the receiver until the
  secret is rotated on [Webhooks](../pages/webhooks.md)). Webhook header values are in the file in clear. Recovery codes (hashes), passkeys (public keys - as long as `APP_URL`'s host is the same) and email/SMS
  methods keep working ([MFA → storage](mfa.md#storage-secrets-and-transfer)). A restore into the same site keeps
  everything. (SMTP settings live in `.env`, not in the data.)
- The CMS database is read in one `RepeatableRead` transaction, so the tables agree with each other; dates without a
  kind (exports from SQLite) are imported as UTC.

## Export of an additional database

The Export card's **Database** select (shown when `DATABASES_<NAME>_*` databases exist) also offers each additional
database (`DatabaseTransferService.ExportDatabases`: the CMS database first, then the others with their provider).
`WriteDatabaseExportAsync` → `TransferFile.WriteDatabaseAsync` writes:

```text
manifest.json            { format "dotnetforge-database-export", formatVersion 1, appVersion, database, provider,
                           createdAt, tables[] { name, rows, sha256 }, skipped[] }
tables/<name>.json       one per table or collection, a JSON array of all its rows as the database returns them
```

- Tables come from `IDatabaseService.ListCollectionsAsync`, rows from `StreamAsync(Find)` with a 10-minute timeout per
  table; names that aren't plain identifiers can't be read through `IDatabaseService` and are listed in `skipped`.
- File name `dotnetforge-export-<name>-<provider>-<yyyyMMdd-HHmmss>.zip`; a `TransferRecord` with operation `Export`,
  `Provider` = that database's provider and `Note` = "Database <name>"; audited `transfer.exported`.
- **Not importable** - it is a copy for your own use. Backups, import and restore stay CMS-only.

## Rules

Checked by `TransferRules.Check` and `TransferFile.ReadAsync` before anything changes:

| Rule | Constraint |
| --- | --- |
| Format | `manifest.json` with format `dotnetforge-export`, format version 1 |
| Data version | the file's `seedVersion` equals this site's `SystemState.SeedVersion` |
| Schema | every table and column of the file exists here with the same type; a column required here must be in the file; an older file may lack a table (imported empty) or a nullable column |
| Retired columns | values of columns the CMS no longer has (`TransferRules.RetiredColumns`: `MediaFile.FolderPath`) are ignored instead of refusing the file |
| Integrity | every table's SHA-256 and row count match the manifest; every value deserializes |
| Usable result | the file has an enabled user with the Super Admin role |
| Atomicity | safety backup first (no backup, no import); all tables replaced in one transaction - a failure leaves the data unchanged |
| Concurrency | one export, backup, import or restore at a time per process |
| Environment | import and restore only when `IHostEnvironment.IsDevelopment()` |

Each run writes a `TransferRecord`:

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | guid | |
| `Operation` | enum | `Export` \| `Backup` \| `Import` \| `Restore` \| `MediaExport` (the [media export](../pages/data-management.md#media-export)) |
| `Status` | enum | `Success` \| `Failed` |
| `Provider`, `SourceProvider` | string(50) | this site's database (an additional database's provider for its export); the file's (imports) |
| `FileName`, `StorageKey` | string(260), string(500)? | `StorageKey` while a backup is stored |
| `SizeBytes`, `RecordCount`, `Checksum` | long, int, string(64)? | SHA-256 of the file |
| `Automatic`, `Note` | bool, string(200)? | scheduled backup; e.g. "Before import of …", "Database <name>" |
| `Error` | string(2000)? | no secrets or stack traces |
| `StartedAt`, `CompletedAt` | datetime | UTC |
| `InitiatedById` | guid? | null for automatic backups |

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code.

### Requirements

- **Updates**: check for core updates (manual and/or scheduled) with status on the Settings overview and the
  [Dashboard](../pages/dashboard.md); back up before every update; apply to core files only; roll back to the
  previous working version; restore automatically when an update fails. Development only until compatibility checks
  and tested deployments exist.
- Updates never touch `extensions/` or `storage/` (custom extensions and user data survive).
- **Extension updates** (`InstalledExtension.UpdateAvailable`, update action) are part of the extension lifecycle:
  [extensions](extensions.md).
- Data Management: progress display for long runs; importing the media files (the
  [media export](../pages/data-management.md#media-export) is a download only) and media files in backups; import in
  production only with a tested recovery procedure.
- Repository hygiene: `.gitignore` additions `out/`, `publish/`, `*.log`, `.cache/`.

### Folder layout

```text
src/                    CORE - replaced by the updater (web host in src/DotNetForge.Web)
extensions/             CUSTOM EXTENSIONS - never written by updates
storage/                RUNTIME DATA - git-ignored, preserved across updates
  media/                uploads (and, through IFileStorage, backups/)
  logs/                 update logs
  updates/              downloaded / staged update packages
```

In a published deployment the core is the publish output (`/app`), with `extensions/` copied inside it; an updater
must replace everything there except `extensions/` and `.env`.

### Update record

| Field | Type | Req. | Notes |
| --- | --- | --- | --- |
| `id` | guid | yes | |
| `fromVersion`, `toVersion` | string | yes | `fromVersion` = working version before the update |
| `backupRef` | string | yes | the pre-update backup (a `TransferRecord` plus the core files) |
| `backupVerified` | bool | yes | |
| `status` | enum | yes | `checking` \| `backing-up` \| `applying` \| `success` \| `failed` \| `rolled-back` |
| `autoRolledBack` | bool | yes | |
| `startedAt` / `completedAt` | datetime | yes / no | |
| `initiatedBy` | string | yes | user id |

### User flows

1. **Update check**: Settings overview or Dashboard → *Check for updates* → query the update source → show the newer
   version and notes → enable *Apply update*.
2. **Apply update**: back up core and database and record the current version → verify the backup (failure aborts
   with no change) → apply to core files only → run migrations → record the new version (`SystemState.CmsVersion`) and
   log; on failure roll back automatically and log.
3. **Roll back** (manual or automatic): find the latest valid pre-update backup → none: refuse with an error (manual
   recovery) → restore core files and database without touching extensions → record the restored version → log.

### Rules and edge cases

| Rule | Constraint |
| --- | --- |
| Backup before update | created and verified first; no backup, no update |
| Current version | recorded before the update so rollback has a target |
| Extension preservation | updates never write to or delete under `extensions/` or `storage/` |
| Rollback target | a valid backup must exist, otherwise refuse explicitly |
| Update source | must be reachable; failures reported, never ignored |
| Migration safety | migrations applied by an update are reversible or covered by the backup |
| Concurrency | one update at a time, never next to a transfer |

- Update fails mid-way: automatic rollback to the backup, previous version restored, extensions and storage
  untouched, failure logged.
- An update writing to `extensions/` is a defect and must be impossible by design.
- Core-file replacement cannot run inside the process it replaces: an external updater or deployment-level step
  stages packages, backs up and never touches `extensions/`, `storage/` or `.env`.

### Acceptance criteria

- [x] The Data Management screen (the spec's Transfer) is Super Admin only and supports Export and Import (import in
  Development only).
- [x] Export writes a self-describing file (source provider, data version, app version, per-table checksums).
- [x] The format is provider-neutral (model-driven): exports made on any earlier CMS database import into PostgreSQL.
- [x] Import validates schema, checksum, version and provider compatibility before changing the target database.
- [x] A corrupt or partial import file is rejected without changes and the failure is logged.
- [x] Every export and import writes a transfer result log entry (status, counts, errors) and is audited.
- [x] Only one transfer runs at a time (per process).
- [x] Core files and custom extensions are in separate folders (`extensions/`, `AppEnvironment.ExtensionsPath`).
- [ ] Long-running export/import shows progress.
- [ ] Core update checks exist and update status is shown on the Settings overview / Dashboard.
- [ ] Applying an update creates and verifies a backup first; a failed backup aborts the update.
- [ ] An update never overwrites or deletes anything under `extensions/` or `storage/`.
- [ ] A failed update automatically rolls back and restores the previous version.
- [ ] Rollback without a backup is refused with an explicit error and nothing destructive happens.
- [ ] `.gitignore` excludes `*.log` and a cache folder (`.env` ✔).

## Where to change things

- **File format / what is exported:** `TransferFile` (model-driven; exclusions in `NotTransferred`; additional
  databases in `WriteDatabaseAsync`). A breaking change
  of the format raises `TransferManifest.CurrentFormatVersion`; compatibility rules live only in `TransferRules`.
- **Operations, records, schedule:** `DatabaseTransferService`; the screen only maps results.
- **Updates:** a new feature - an external updater that stages packages and backs up through the Data Management backups,
  plus an `UpdateRecord` entity (`database-change` skill) and new `AuditActions`; set `SystemState.CmsVersion` in one
  place.
