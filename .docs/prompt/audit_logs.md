# Audit Logs

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Audit Logs module records important admin and system actions across DotNetForge CMS, providing an immutable, append-only trail of who did what, when, from where, and to which entity. Logs are tenant-aware and are used for security review, troubleshooting, compliance, and accountability.

## Purpose

The system must track important admin and system actions in a durable audit trail. The audit log answers, for any significant action: **who** performed it, **what** action occurred, **which entity** was affected, **from where** (IP and user agent), **when** it happened, and **what** additional context (details) applies.

Audit logging is a core security requirement of the CMS (see [Security](security.md)) and must capture both successful and failed actions. Audit records are append-only and immutable: they can be created and read, but never edited or deleted through normal application flows.

Audit logs are scoped per tenant. In a multi-tenant deployment, each record is associated with the tenant in whose context the action occurred, and access to logs is filtered by the active tenant. See [Multi-Tenancy](multi_tenancy.md) for tenant resolution and tenant-aware admin context.

## Data Model / Fields

Each audit log entry must record the following fields:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| User | Reference (user id) / nullable | Conditional | The user who performed the action. Null/anonymous for unauthenticated events such as a failed login where no user is resolved. Store a stable user reference plus a display snapshot (e.g. email) so the log remains meaningful even if the user is later deleted. |
| Action | String (enum) | Yes | The action that occurred (e.g. `user.login`, `content.created`). See **Logged Actions** below. |
| Entity type | String | Conditional | The type of entity affected (e.g. `Page`, `Media`, `Plugin`, `Role`, `Permission`, `Setting`, `ApiToken`, `Webhook`, `User`). May be empty for actions not tied to an entity (e.g. login). |
| Entity ID | String / integer | Conditional | The identifier of the affected entity. May be empty when no specific entity applies. |
| IP address | String | Yes | The client IP address from which the action originated. Capture the real client IP, honoring trusted proxy/forwarded headers where configured. |
| User agent | String | Yes | The client user agent string associated with the request. |
| Timestamp | DateTime (UTC) | Yes | The instant the action occurred, stored in UTC. |
| Details | Text / JSON | No | Additional structured context about the action (e.g. changed fields, previous/new values, reason, plugin name, target tenant). Must be redacted of sensitive data (see **Validation Rules** and **Edge Cases**). |
| Tenant | Reference (tenant id) | Conditional | The tenant in whose context the action occurred. Required in multi-tenant deployments; see [Multi-Tenancy](multi_tenancy.md). |

Timestamps must be stored in UTC and displayed in the viewer's configured timezone in the admin UI.

## Main Features

- Record an audit entry for every significant admin and system action, including both successful and failed actions.
- Track the canonical fields: User, Action, Entity type, Entity ID, IP address, User agent, Timestamp, and Details.
- Capture failed-action events (for example, a failed login) even when no authenticated user is resolved.
- Associate each entry with the tenant in whose context it occurred (tenant-aware logs); see [Multi-Tenancy](multi_tenancy.md).
- View audit logs in a paginated, sortable list in the admin **Administration Panel → Audit Logs** area.
- Filter logs by user, action, entity type/entity ID, and date range.
- Search across log fields (e.g. user, action, entity, IP, details).
- Export filtered audit logs (for example, to CSV or JSON) for offline analysis or compliance.
- Enforce append-only, immutable storage: entries cannot be modified or deleted through the application UI or API.
- Redact sensitive data before persistence (never log passwords or token plaintext).
- Support high-volume retention with configurable rotation/archival.

### Logged Actions

The audit log must, at minimum, support the following action types. This list is extensible; extensions may register additional action types.

| Action | Entity type (typical) | Description |
| --- | --- | --- |
| User login | User | A user successfully authenticated. |
| Failed login | User (or none) | An authentication attempt failed. Logged even when no user is resolved; record the attempted identifier in Details where safe (never the password). |
| Content created | Page / content entity | New content was created. See [Content Manager](content_manager.md). |
| Content updated | Page / content entity | Existing content was modified. |
| Content deleted | Page / content entity | Content was deleted. |
| Media uploaded | Media | A file was uploaded to the File Manager. See [File Manager](file_manager.md). |
| Plugin installed | Plugin | A plugin was installed. See [Plugins](extensions.md). |
| Plugin disabled | Plugin | A plugin was disabled. |
| Role changed | Role | A role was created, edited, duplicated, deleted, or assigned/unassigned. See [User Roles & Permissions](user_roles_permissions.md). |
| Permission changed | Permission / Role | Permissions on a role were modified. |
| Settings changed | Setting | A settings value was changed. See [Settings](settings.md). |
| API token created | ApiToken | An API token was created. Only metadata is logged; the token plaintext must never be logged. See [API Tokens](api_tokens.md). |
| Webhook created | Webhook | A webhook was created. See [Webhooks](webhooks.md). |

## User Flows

### Flow: View audit logs

1. The user opens **Administration Panel → Audit Logs** in the admin area.
2. The system verifies the user holds permission to view audit logs (Super Admin or Admin); otherwise it denies access (see **Role & Permission Rules**).
3. The system loads audit entries scoped to the active tenant (see [Multi-Tenancy](multi_tenancy.md)), ordered by Timestamp descending by default.
4. The system displays a paginated list showing User, Action, Entity type, Entity ID, IP address, Timestamp, and a summary of Details.
5. The user can select an entry to view its full details, including the complete Details payload (with sensitive fields redacted).

### Flow: Filter logs by user / action / entity / date

1. From the Audit Logs list, the user opens the filter controls.
2. The user selects one or more filters: by **user**, by **action**, by **entity type** and/or **entity ID**, and/or by **date range** (from/to).
3. The system validates the filter inputs (for example, a valid date range where "from" is not after "to").
4. The system applies the filters in combination (logical AND), scoped to the active tenant, and returns matching entries.
5. The system updates the list and pagination to reflect the filtered result set. Active filters remain visible and removable.

### Flow: Search logs

1. From the Audit Logs list, the user enters a search term.
2. The system searches across indexed log fields (e.g. user, action, entity type/ID, IP address, and non-sensitive Details), scoped to the active tenant.
3. The system returns and paginates matching entries, ordered by Timestamp descending.
4. Search may be combined with the filters above to narrow results further.

### Flow: Export logs

1. The user applies any desired filters and/or search to define the result set to export.
2. The user selects **Export** and chooses a format (e.g. CSV or JSON).
3. The system verifies the user holds permission to view/export audit logs.
4. The system generates the export from the current filtered result set, scoped to the active tenant, with sensitive fields redacted.
5. The system streams or provides a download of the export file. The export action itself may be recorded as an audit entry (e.g. `audit.exported`).

### Flow: Automatic logging of an action (system-side)

1. A significant action completes (or fails) anywhere in the CMS or API - for example, a content update or a failed login.
2. The system constructs an audit record capturing User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC), Details, and Tenant.
3. The system redacts sensitive data from Details (passwords, token plaintext, secrets) before persisting.
4. The system appends the record to the audit store. The append must not be reversible or editable through the application.
5. If the originating action failed, the system still records the attempt with an indication of failure; audit-logging failures must not silently swallow the underlying error.

## Role & Permission Rules

Audit Logs live in the **Administration Panel** and are restricted to the most privileged roles. By default, only **Super Admin** and **Admin** may view audit logs; less-privileged default roles have no access. See [User Roles & Permissions](user_roles_permissions.md) for the full role model and permission areas.

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| View audit logs | Yes | Yes | No | No | No | No |
| Filter / search audit logs | Yes | Yes | No | No | No | No |
| Export audit logs | Yes | Yes | No | No | No | No |
| Edit or delete audit logs | No | No | No | No | No | No |
| View logs across all tenants | Yes (global) | No (assigned tenant only) | No | No | No | No |

Notes:

- No role - including Super Admin - may edit or delete audit entries through the application. Logs are immutable (see **Validation Rules**). Retention/rotation is a controlled system process, not an interactive delete.
- **Super Admin** has global, cross-tenant access and can view logs for all tenants. **Admin** (tenant admin) sees only the logs of the tenant(s) they are assigned to. Tenant filtering follows the active tenant context described in [Multi-Tenancy](multi_tenancy.md).
- Permission checks must be enforced for every audit-log view, filter, search, and export action, consistent with the per-action permission requirement in [Security](security.md).

## Validation Rules

- **Append-only / immutable:** Audit records can be created and read only. The system must provide no UI or API path to update or delete an individual audit entry. Attempts to mutate an existing entry must be rejected.
- **Required fields:** Action, IP address, User agent, and Timestamp are always required on each entry. User is required for authenticated actions and may be null for unauthenticated events (e.g. failed login). Entity type and Entity ID are required for entity-scoped actions and may be empty for non-entity actions (e.g. login).
- **Timestamp:** Stored in UTC. The system generates the timestamp server-side; it must not be supplied or overridden by client input.
- **Tenant association:** In multi-tenant deployments, every entry must carry the tenant in whose context the action occurred. See [Multi-Tenancy](multi_tenancy.md).
- **Sensitive-data redaction:** The system must never persist passwords or token/secret plaintext in any field, including Details. Such values must be redacted (e.g. replaced with a masked placeholder) before the record is written. This applies to failed-login records (do not log the attempted password) and API token creation (log only token metadata, never the token value); see [API Tokens](api_tokens.md).
- **Action vocabulary:** Action must be one of the supported action types (or an action type registered by an extension). Free-text actions outside the registered vocabulary should be rejected or normalized.
- **Filter validation:** Date-range filters must validate that "from" is not after "to". Invalid filter input must be rejected with a clear error and must not return unfiltered data.
- **Failed-action logging:** Failed actions (including failed logins and other denied/aborted operations) must be recorded. A failure to write an audit record must surface an error rather than be silently ignored.
- **Authorization:** Only Super Admin and Admin may read, filter, search, or export logs; every such request must pass a permission check.

## Edge Cases

- **High-volume retention and rotation:** Audit logs can grow rapidly. The system must support a configurable retention policy with rotation/archival (for example, archive or purge entries older than a configured age, or roll over to archive storage when a size threshold is reached). Rotation is an administrative/system process and must not be exposed as an interactive per-entry delete; the immutability guarantee for live entries still holds. Archival/purge events should themselves be auditable.
- **Sensitive-data redaction:** Never log passwords or token plaintext. Details payloads that may embed secrets (e.g. webhook headers, SMTP credentials, connection strings) must be redacted/masked before persistence. Change records should capture which fields changed rather than echoing secret values.
- **Failed-action logging:** Record actions that fail or are denied, not only successful ones. A failed login with no resolved user must still produce an entry (with User null) capturing IP address, user agent, timestamp, and a safe identifier in Details - never the attempted password. Repeated failed logins also feed account lockout and rate limiting (see [Security](security.md)).
- **Clock and timezone:** Store all timestamps in UTC and render them in the viewer's local/configured timezone. Do not rely on client-supplied time. Account for clock skew across distributed instances by using a consistent server time source.
- **Deleted or renamed users/entities:** Because a referenced user or entity may later be deleted or renamed, store a stable reference plus a display snapshot (e.g. user email, entity label) so historical entries remain interpretable.
- **Anonymous / system actions:** Some actions originate from the system, a background job, or an API token rather than an interactive user. Record the acting principal appropriately (system, token identifier, or null user) without losing the IP/user-agent context where available.
- **Tenant data isolation:** Logs must never leak across tenants. Tenant admins must only see entries for their assigned tenant; only global Super Admins may view across all tenants. See [Multi-Tenancy](multi_tenancy.md).
- **Unknown or missing IP / user agent:** If the IP address or user agent cannot be determined, record a clearly marked placeholder rather than failing the action; the underlying action must still proceed and be logged.
- **Concurrency / ordering:** Under concurrent writes, entries must be appended safely without loss. Ordering for display relies on the UTC timestamp; ties should be broken by a monotonic sequence/identifier so the original order is preserved.
- **Audit-store availability:** If the audit store is temporarily unavailable, the system must not silently drop records; it should surface the failure and/or buffer for durable write, consistent with the security requirement that significant actions are auditable.

## Acceptance Criteria

- [ ] Each audit entry records User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC), and Details.
- [ ] The supported logged actions include user login, failed login, content created/updated/deleted, media uploaded, plugin installed, plugin disabled, role changed, permission changed, settings changed, API token created, and webhook created.
- [ ] Failed actions (including failed logins with no resolved user) are recorded, with User null where applicable and without logging the attempted password.
- [ ] Each entry is associated with the tenant in whose context it occurred, and log access is filtered by the active tenant (see [Multi-Tenancy](multi_tenancy.md)).
- [ ] Only Super Admin and Admin can view, filter, search, and export audit logs; all other default roles are denied.
- [ ] Super Admin can view logs across all tenants; tenant Admins see only their assigned tenant's logs.
- [ ] The Audit Logs page lists entries paginated and ordered by timestamp descending by default.
- [ ] Logs can be filtered by user, by action, by entity type/entity ID, and by date range, and filters combine correctly.
- [ ] A date-range filter rejects invalid ranges (from after to) with a clear error.
- [ ] Logs can be searched across indexed fields, and search can combine with filters.
- [ ] Filtered/searched logs can be exported (e.g. CSV or JSON) with sensitive fields redacted.
- [ ] Audit entries are append-only: there is no UI or API path to edit or delete an individual entry, and mutation attempts are rejected.
- [ ] Passwords and token/secret plaintext are never persisted in any field, including Details, for any action (including API token creation and failed logins).
- [ ] Timestamps are stored in UTC and displayed in the viewer's configured timezone.
- [ ] A retention/rotation policy is configurable for high-volume logs, and archival/purge is a controlled process that does not expose interactive per-entry deletion.
- [ ] No audit entry leaks across tenant boundaries.
- [ ] Every audit-log read, filter, search, and export request passes an explicit permission check.
