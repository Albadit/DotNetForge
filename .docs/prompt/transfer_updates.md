# Transfer, Import/Export & Updates

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This module governs two related operational concerns in DotNetForge CMS: provider-aware database **import/export** (the admin **Transfer** section) and safe **core updates with rollback**. Both are restricted to the Super Admin role and produce auditable, validated results.

## Purpose

DotNetForge CMS must let operators move data between database providers and keep the CMS core current without breaking custom extensions. This file owns:

- **(A) Data Transfer / DB import-export** - export the CMS dataset, import it into another instance, and migrate between **SQLite** (default) and **PostgreSQL** with full provider awareness, validation, progress reporting, and result logging.
- **(B) Updates & rollbacks** - check for, back up before, apply, and roll back core CMS updates while preserving custom extensions, with automatic restore on failed updates.

Both concerns are tenant-aware (see [Multi-Tenancy](multi_tenancy.md)) and depend on shared infrastructure documented in [Security](security.md), [Audit Logs](audit_logs.md), and [User Roles & Permissions](user_roles_permissions.md).

## Main Features

### A. Data Transfer (Import / Export)

The admin **Transfer** section (Global Settings sidebar) must allow data transfer between databases.

- **Export data** - produce a portable, provider-agnostic export of CMS data (content, media metadata, users, roles, settings, extensions registry, webhooks, etc.).
- **Import data** - load a previously produced export into the current instance.
- **Select source database type** - the provider the export originated from.
- **Select target database type** - the provider the data is imported into.
- **Support SQLite** - the default provider.
- **Support PostgreSQL** - the production-grade alternative provider.
- **Provider-aware migration** - import/export must be **database-provider aware**: data exported from SQLite can be imported into PostgreSQL and vice versa, with the system translating provider-specific types, identity/sequence handling, and constraints during transfer.
- **Validate import files** - verify schema, version compatibility, and integrity before applying any change.
- **Show transfer progress** - report progress (e.g. percentage, records processed, current stage) during long-running export/import operations.
- **Log transfer results** - record the outcome (success/failure, counts, errors, duration) of every transfer for review and audit.
- **Tenant-aware import and export** - exports and imports can be scoped to a single tenant or run globally for all tenants. See [Multi-Tenancy](multi_tenancy.md) for tenant scoping rules.

The export format must be self-describing: it must embed the source provider, the CMS schema version, the CMS application version, the tenant scope, and a checksum so the importer can validate it.

### B. Updates & Rollbacks

DotNetForge CMS must separate the **core CMS** from **custom extensions** so the core can be updated without breaking user-created extensions.

- **Core/extension separation** - core CMS files and custom extensions must be clearly separated, with explicit folder boundaries (see [Data Model / Layout](#data-model--folder-layout)).
- **Dedicated extensions folder** - all extensions live in a dedicated `extensions/` folder. See [Extension System](extensions.md) for the extension model and [Extension Manifest](extensions.md) for manifest validation.
- **Non-destructive updates** - updates must **not** overwrite custom extensions or user content/storage.
- **Update checks** - the CMS must support checking for available updates (manual and/or scheduled), surfaced on the Overview/Dashboard update status (see [Overview Settings](settings.md) and [Dashboard](dashboard.md)).
- **Rollback** - the CMS must support rollback to the previous working version.
- **Pre-update backups** - a backup must be created before every update.
- **Auto-restore on failure** - failed updates must automatically restore the previous version where possible.

### Repository Hygiene

The project must be uploadable to GitHub and the repository must stay as small as possible.

- The `.gitignore` must exclude **generated files, logs, uploads, cache, build output, and secrets**.
- Secrets live in `.env` and must **never** be committed to Git. See [Security](security.md) and [Installation & Configuration](installation_setup.md).

A representative `.gitignore` for the transfer/update-related artifacts:

```gitignore
# Build output
bin/
obj/
out/
publish/

# Secrets & environment
.env
*.env.local

# Generated / runtime storage (never commit)
storage/media/
storage/backups/
storage/logs/
storage/updates/
*.log

# Caches
.cache/
node_modules/
```

The runtime storage folders below are produced by this module and must be excluded from Git:

```text
storage/
  media/      # user uploads (excluded)
  backups/    # pre-update backups + DB exports (excluded)
  logs/       # transfer & update logs (excluded)
  updates/    # downloaded update packages / staging (excluded)
```

## User Flows

### Flow: Export the Database

1. Super Admin opens **Settings → Transfer**.
2. Selects **Export**.
3. Selects the **source database type** (defaults to the current provider, e.g. SQLite).
4. Selects the **tenant scope**: a specific tenant or **All tenants** (Super Admin only; see [Multi-Tenancy](multi_tenancy.md)).
5. Confirms the export.
6. The system streams the dataset, **shows transfer progress**, and writes a self-describing export file (embedding source provider, schema version, app version, tenant scope, and checksum) to `storage/backups/`.
7. On completion, the system **logs the transfer result** (record counts, duration, status) and offers the file for download.

### Flow: Import into a Different Provider

1. Super Admin opens **Settings → Transfer → Import**.
2. Uploads or selects an export file.
3. The system reads the embedded **source database type** and the operator selects the **target database type** (the current instance's provider, e.g. PostgreSQL).
4. The system **validates the import file** (schema, integrity/checksum, version compatibility, provider compatibility).
5. If the source and target providers differ, the system applies **provider-aware migration** (type translation, identity/sequence remapping, constraint handling).
6. The system **shows transfer progress** while importing, scoped to the selected tenant(s).
7. On completion, the system **logs the transfer result**. On any validation or integrity failure, the import is aborted before mutating data and the failure is logged.

### Flow: Run an Update Check

1. Super Admin opens **Settings → Overview** (or the Dashboard **Update status** widget).
2. Selects **Check for updates**.
3. The system queries the update source and reports whether a newer CMS core version is available, including the target version and notes.
4. If an update is available, the **Apply update** action becomes available.

### Flow: Apply an Update (Auto-Backup)

1. Super Admin selects **Apply update** for the available version.
2. The system **creates a backup** of the current core and database **before** making changes (stored under `storage/backups/`) and records the current working version.
3. The system verifies the backup completed successfully. If the backup fails, the update is **aborted** and no changes are made.
4. The system applies the update **only to core CMS files**, leaving the `extensions/` folder and `storage/` untouched.
5. The system runs database migrations as needed.
6. On success, the system records the new working version and logs the result.
7. On failure mid-way, the system **automatically rolls back** to the pre-update backup (see next flow) and logs the failure.

### Flow: Roll Back to the Previous Version

1. Super Admin selects **Roll back** (or the system triggers it automatically after a failed update).
2. The system locates the most recent valid pre-update backup.
3. If no backup exists, the rollback is **refused** and an error is reported (manual recovery required).
4. The system restores the core files and database from the backup, **without touching custom extensions** unless they were part of the backup.
5. The system records the restored version as the current working version and logs the rollback result.

## Role & Permission Rules

Transfer and update operations are **Super Admin only**. These are global, instance-wide operations (and, for "All tenants" scope, cross-tenant) and must not be delegated to lower roles. See [User Roles & Permissions](user_roles_permissions.md) for the full role model.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Export database (any tenant) | Yes | No | No | No | No | No |
| Export database (All tenants) | Yes | No | No | No | No | No |
| Import database | Yes | No | No | No | No | No |
| Select source/target provider | Yes | No | No | No | No | No |
| Run update check | Yes | No | No | No | No | No |
| Apply core update | Yes | No | No | No | No | No |
| Roll back to previous version | Yes | No | No | No | No | No |
| View transfer/update logs | Yes | No | No | No | No | No |

- The relevant permission areas are **Settings** and **Extensions** (see [User Roles & Permissions](user_roles_permissions.md)); transfer/update actions require the Super Admin grant within **Settings**.
- A **Tenant Admin** may export/import **only their assigned tenant's** scope **if and only if** explicitly granted; cross-tenant ("All tenants") transfer and any core update/rollback remain exclusive to the global Super Admin. See [Multi-Tenancy](multi_tenancy.md).
- Every transfer, update, and rollback action must be written to the [Audit Logs](audit_logs.md).

## Validation Rules

### Import / Export

| Rule | Constraint |
| --- | --- |
| Source provider | Required; must be one of `sqlite`, `postgresql`. Read from the export file and confirmed by the operator. |
| Target provider | Required; must be one of `sqlite`, `postgresql`. Defaults to the current instance provider. |
| Import file present | Required; an export file must be supplied/selected before import. |
| File schema | Required; the file must conform to the DotNetForge export schema. Reject if missing or malformed. |
| File integrity | Required; the embedded checksum must match the file contents. Reject corrupt or partial files. |
| Schema/version compatibility | Required; the export's CMS schema version must be compatible with the current instance. Reject incompatible versions with a clear message. |
| Provider compatibility | Required; when source ≠ target, provider-aware migration must be possible for every entity. Reject if any entity cannot be translated. |
| Tenant scope | Required; export/import scope (specific tenant or All tenants) must be selected and the actor must be authorized for that scope. |
| Atomicity | The import must validate fully before mutating data; a failed validation must leave the target database unchanged. |

### Updates / Rollbacks

| Rule | Constraint |
| --- | --- |
| Backup before update | Required; a pre-update backup must be created and verified **before** applying any update. The update must not proceed if the backup did not complete. |
| Current version recorded | Required; the current working version must be captured before the update so rollback has a known target. |
| Extension preservation | The update must not write to or delete anything under the `extensions/` folder or `storage/`. |
| Rollback target exists | Required for rollback; a valid backup must exist. If none exists, rollback must be refused with an explicit error. |
| Update source reachable | Update checks require a reachable update source; failures must be reported, not silently ignored. |
| Migration safety | Database migrations applied during an update must be reversible or covered by the pre-update backup. |

## Edge Cases

- **Provider mismatch on import** - when the export's source provider differs from the selected target provider, provider-aware migration runs. If a type/constraint/sequence cannot be safely translated, the import is aborted before any data is written and the reason is logged.
- **Corrupt or partial import file** - checksum/schema validation fails; the import is rejected before mutating the target database, and the failure is logged with the validation error.
- **Update fails mid-way** - the system automatically rolls back to the pre-update backup, restores the previous working version, and logs the failure. Custom extensions and storage remain untouched.
- **Backup fails before update** - the update is aborted and no core files or database changes are made; the operator is notified.
- **Rollback when no backup exists** - rollback is refused with an explicit error; the system instructs the operator toward manual recovery (no destructive action is taken).
- **Huge dataset / timeout during transfer** - long-running exports/imports stream data and report progress; the operation must support resumable or chunked processing and must not lock the admin UI. A timeout or interruption must leave the target in a consistent state (import aborts cleanly; export marks the result as failed/incomplete in the log).
- **Concurrent transfer/update** - only one update or transfer of overlapping scope may run at a time. A second concurrent attempt must be blocked or queued to prevent racing on the database and backup folder.
- **Update overwriting extensions** - must never happen; the updater only touches core files. Any attempt to modify `extensions/` during an update is a defect and must be prevented by design.
- **Cross-tenant scope by a Tenant Admin** - a Tenant Admin attempting "All tenants" export/import or any core update is denied; only the global Super Admin may perform cross-tenant or core operations (see [Multi-Tenancy](multi_tenancy.md)).
- **Secrets in export** - exports must not embed `.env` secrets; secrets are environment configuration, not data. See [Security](security.md).

## Data Model / Folder Layout

Core, extensions, and runtime storage are separated so updates are non-destructive:

```text
DotNetForgeCMS/
  src/                  # CORE CMS - updated by the updater
    DotNetForge.Web/
    DotNetForge.Core/
    DotNetForge.Data/
    ...
  extensions/           # CUSTOM EXTENSIONS - never overwritten by updates
    themes/ plugins/ modules/ widgets/ providers/
    connectors/ authentication/ libraries/ admin/
  storage/              # RUNTIME DATA - git-ignored, preserved across updates
    media/              # uploads
    backups/            # pre-update backups + DB exports
    logs/               # transfer & update logs
    updates/            # downloaded/staged update packages
```

Provider configuration is read from `.env` (see [Installation & Configuration](installation_setup.md)):

```env
DATABASE_PROVIDER=sqlite        # or: postgresql
DATABASE_CONNECTION_STRING=
```

### Transfer Result Log Fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string/guid | Yes | Unique transfer identifier. |
| `operation` | enum | Yes | `export` or `import`. |
| `sourceProvider` | enum | Yes | `sqlite` or `postgresql`. |
| `targetProvider` | enum | Yes | `sqlite` or `postgresql`. |
| `tenantScope` | string | Yes | Tenant id or `all`. |
| `status` | enum | Yes | `success`, `failed`, `incomplete`. |
| `recordCount` | int | Yes | Number of records processed. |
| `startedAt` | datetime | Yes | Start timestamp. |
| `completedAt` | datetime | No | End timestamp (null if interrupted). |
| `error` | string | No | Error detail when status is `failed`/`incomplete`. |
| `initiatedBy` | string | Yes | User id of the Super Admin who ran it. |

### Update Record Fields

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | string/guid | Yes | Unique update operation identifier. |
| `fromVersion` | string | Yes | Working version before the update. |
| `toVersion` | string | Yes | Target version. |
| `backupRef` | string | Yes | Reference/path to the pre-update backup. |
| `backupVerified` | boolean | Yes | Whether the backup completed successfully. |
| `status` | enum | Yes | `checking`, `backing-up`, `applying`, `success`, `failed`, `rolled-back`. |
| `autoRolledBack` | boolean | Yes | Whether automatic rollback was triggered. |
| `startedAt` | datetime | Yes | Start timestamp. |
| `completedAt` | datetime | No | End timestamp. |
| `initiatedBy` | string | Yes | User id of the Super Admin who ran it. |

## Acceptance Criteria

- [ ] The Transfer section is accessible only to the Super Admin and supports both **Export** and **Import**.
- [ ] Export produces a self-describing file embedding source provider, CMS schema version, app version, tenant scope, and a checksum, written to `storage/backups/`.
- [ ] Import requires selecting the **target database type** and reads/confirms the **source database type** from the file.
- [ ] Both **SQLite** and **PostgreSQL** are supported as source and target providers.
- [ ] Exporting from one provider and importing into a different provider succeeds via provider-aware migration.
- [ ] Import validates schema, integrity (checksum), version compatibility, and provider compatibility **before** mutating the target database.
- [ ] A corrupt or partial import file is rejected without changing the target database, and the failure is logged.
- [ ] Transfer progress is shown for long-running export/import operations.
- [ ] Every export and import writes a transfer result log entry with status, counts, duration, and errors.
- [ ] Import/export can be scoped per tenant, and only authorized actors may run a given scope (All tenants is Super Admin only).
- [ ] Core CMS files and custom extensions are stored in clearly separated folders, with extensions under a dedicated `extensions/` folder.
- [ ] The CMS supports checking for available core updates and surfaces update status on Overview/Dashboard.
- [ ] Applying an update creates and verifies a backup **before** making any change; if the backup fails, the update is aborted.
- [ ] An update never overwrites or deletes anything under `extensions/` or `storage/`.
- [ ] A failed update automatically rolls back to the pre-update backup and restores the previous working version.
- [ ] Rollback to the previous working version succeeds when a valid backup exists.
- [ ] Rollback is refused with an explicit error when no backup exists, and no destructive action is taken.
- [ ] Only one update or overlapping-scope transfer can run at a time; concurrent attempts are blocked or queued.
- [ ] A huge-dataset transfer that times out or is interrupted leaves the target in a consistent state and is recorded as failed/incomplete.
- [ ] All transfer, update, and rollback actions are written to the Audit Logs.
- [ ] The project is uploadable to GitHub with a `.gitignore` that excludes generated files, logs, uploads, cache, build output, and secrets; `.env` secrets are never committed.
