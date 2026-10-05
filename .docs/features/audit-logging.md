# Audit logging

Append-only records of security-relevant actions. Written only through `IAuditService`
(`src/DotNetForge.Shared/Auditing/IAuditService.cs`, implemented by `Services/AuditService.cs`); read on the
[Audit Logs screen](../pages/audit-logs.md), counted on the [Dashboard](../pages/dashboard.md), and shown by the
sample [Audit Dashboard admin extension](../pages/extension-host.md).

## `IAuditService.LogAsync`

```csharp
Task LogAsync(string action, string? entityType = null, string? entityId = null,
              string? entityDisplay = null, string? details = null, bool success = true,
              CancellationToken cancellationToken = default)
```

`AuditService` builds an `AuditLogEntry` from the current `HttpContext` and saves it immediately (its own
`SaveChangesAsync`):

| Field | Source |
| --- | --- |
| `UserId` | `NameIdentifier` claim; `null` when anonymous **or when the caller is an API token** |
| `UserDisplaySnapshot` | API token: `API token {token id}` (from the `dnf:token` claim); otherwise `Identity.Name` or the email claim. Max 256 |
| `TenantId` | `dnf:tenant` claim (cookie or API token); `null` when anonymous |
| `IpAddress` | `Connection.RemoteIpAddress` or `"unknown"` - the real client IP only when forwarded headers are enabled ([deployment](../guides/deployment.md#behind-a-reverse-proxy)) |
| `UserAgent` | `User-Agent` header (max 512) or `"unknown"` |
| `Timestamp` | `DateTime.UtcNow` |
| `EntityId`, `EntityDisplaySnapshot` | arguments, max 100 / 400 |
| `Action`, `EntityType`, `Details`, `Success` | arguments |

Snapshot values can come from request input (the email typed into a failed login, the user agent), so `AuditService`
truncates `UserDisplaySnapshot`, `EntityId`, `EntityDisplaySnapshot` and `UserAgent` to their column lengths - an
oversized value never fails the request on PostgreSQL. Snapshots keep entries readable after the user or entity is
renamed or deleted. `Id` is an auto-increment `long`, so ordering by `Id` is stable.

## What is logged today

| Action constant | Key | Written by | Entity |
| --- | --- | --- | --- |
| `UserLogin` | `user.login` | `AccountController.Login` (success; `HttpContext.User` is set to the new principal first, so the entry carries the user and tenant) | `User`, id, email |
| `UserLoginFailed` | `user.login.failed` | `AccountController.Login` (any failure; `success: false`) | `User`, no id, the typed email |
| `UserLogout` | `user.logout` | `AccountController.Logout` | - |
| `CmsInstalled` | `cms.installed` | `SetupController.Index` (POST, after a successful install; written after the automatic sign-in, so it carries the new user and tenant) | `User`, id, email |
| `ContentCreated` | `content.created` | `ContentController.Create`; `ContentApiController.CreatePage` (`POST /api/content/pages`, actor = API token) | `Page`, id, title |
| `ContentUpdated` | `content.updated` | `ContentController.Update` | `Page`, id, title |
| `ContentDeleted` | `content.deleted` | `ContentController.Delete` | `Page`, id, title |
| `ContentReordered` | `content.reordered` | `ContentController.Reorder` | `Page`, no id, `"{n} page(s)"` |
| `MediaUploaded` | `media.uploaded` | `MediaService.UploadAsync` (per stored file, from `MediaController.Upload`) | `MediaFile`, id, file name |
| `MediaDeleted` | `media.deleted` | `MediaService.DeleteAsync` (from `MediaController.Delete`) | `MediaFile`, id, file name |
| `SettingsChanged` | `settings.changed` | `SettingsController.Save` | `Setting`, key, key (value not recorded) |
| `ApiTokenCreated` | `apitoken.created` | `ApiTokensController.Create` | `ApiToken`, id, name |
| `ApiTokenRevoked` | `apitoken.revoked` | `ApiTokensController.Revoke` | `ApiToken`, id, name |

Defined but never written: `plugin.installed`, `plugin.disabled`, `role.changed`, `permission.changed`,
`webhook.created`, `audit.exported`.

Not logged: API reads, scheduler changes (`ScheduledPublishingService`), and failed or denied operations other than
sign-in (403s, validation errors, rejected uploads). `Details` is never populated.

## Tenant scoping of reads

| Reader | Filter |
| --- | --- |
| Audit Logs screen (`AuditLogsController`, latest 100) | `TenantId == active tenant || TenantId == null` |
| Dashboard **Audit entries** count (`DashboardController`) | same |
| Audit Dashboard extension (`extensions/admin/audit-dashboard`) | **none** - all tenants |

Entries without a tenant (failed sign-ins, where no account was resolved) belong to the sign-in tenant and stay
visible; with a second tenant they would be visible in every tenant ([multi-tenancy](multi-tenancy.md)).

## Rules

- Write audit entries only via `IAuditService.LogAsync`, with a constant from `AuditActions` (add new keys there).
- Log after the change is saved; the two saves are not transactional, so a failure between them loses the entry.
- Never put secrets (passwords, token plaintext, hashes) in `Details` or the display snapshots.
- No code may update or delete `AuditLogs` rows.
- Any new reader of `AuditLogs` filters by the active tenant like `AuditLogsController`.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
Viewer (list, filters, search, export): [Audit Logs screen](../pages/audit-logs.md#planned-not-implemented).

### Requirements

- Every significant admin **and system** action writes an audit entry, both successful and failed.
- Fields: User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC), Details, Tenant - all ✔
  columns on `AuditLogEntry`. Target deltas:
  - **IP address:** the real client IP, honouring trusted forwarded/proxy headers where configured (✔ when
    `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`; off by default).
  - **Details:** structured JSON context (changed field names, previous/new values, reason, plugin name, target
    tenant), redacted before persistence. No caller fills it today.
  - **Tenant:** required in multi-tenant deployments; access to entries is filtered by the active tenant
    ([multi-tenancy](multi-tenancy.md)). ✔ Audit Logs screen and Dashboard count
    ([Tenant scoping of reads](#tenant-scoping-of-reads)); failed-login entries have no tenant.
- **Acting principal** for non-interactive actions (system, background job, API token): record system, the token
  identifier or a null user, keeping IP/user agent where available. ✔ API token: `UserId = null`,
  `UserDisplaySnapshot = "API token {id}"`, tenant from the token (`AuditService`); the scheduler writes nothing.
- **Action vocabulary** is extensible: extensions may register additional action types.
- **Retention:** configurable retention policy with rotation/archival (by age, or roll over to archive storage at a
  size threshold). It is a controlled system process, never an interactive per-entry delete; archival/purge events
  are themselves audited.

Tracked actions (minimum set):

| Action | Typical entity | Constant | Written today |
| --- | --- | --- | --- |
| User login | `User` | `UserLogin` | ✔ |
| Failed login | `User` or none; safe identifier only, never the password | `UserLoginFailed` | ✔ |
| Content created / updated / deleted | `Page` | `ContentCreated` / `ContentUpdated` / `ContentDeleted` | ✔ |
| Media uploaded | `Media` | `MediaUploaded` | ✔ (`MediaService`; also `MediaDeleted`) |
| Plugin installed / disabled | `Plugin` | `PluginInstalled` / `PluginDisabled` | ✘ |
| Role changed (created, edited, duplicated, deleted, assigned/unassigned) | `Role` | `RoleChanged` | ✘ |
| Permission changed (grants of a role) | `Permission` / `Role` | `PermissionChanged` | ✘ |
| Settings changed | `Setting` | `SettingsChanged` | ✔ |
| API token created (metadata only) | `ApiToken` | `ApiTokenCreated` | ✔ |
| Webhook created | `Webhook` | `WebhookCreated` | ✘ |
| Audit log exported (optional) | - | `AuditExported` | ✘ |

### User flows

**Automatic logging (system side):** action completes or fails → build the entry (User, Action, Entity type/ID, IP,
user agent, server UTC timestamp, Details, Tenant) → redact secrets from Details → append (never reversible or
editable) → a failed action is still recorded with a failure indication (`Success = false` ✔); an audit-write failure
must not silently swallow the underlying error.

### Rules and validation

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| View audit logs | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Filter / search / export | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Edit or delete entries | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ |
| View across all tenants | ✔ (global) | ✘ (assigned tenant only) | ✘ | ✘ | ✘ | ✘ |

- **Append-only:** no UI or API path updates or deletes an entry (✔ none exists) and mutation attempts are rejected
  (no store-level guard exists today).
- **Required:** Action, IP address, User agent, Timestamp always (✔ `AuditService` falls back to `"unknown"`). User
  for authenticated actions (null allowed for e.g. failed logins); Entity type and ID for entity-scoped actions.
- **Timestamp:** generated server-side in UTC (✔ `DateTime.UtcNow`), never taken from client input; displayed in the
  viewer's configured timezone.
- **Redaction:** passwords and token/secret plaintext are never persisted in any field, including Details; values that
  may embed secrets (webhook headers, SMTP credentials, connection strings) are masked; change records name the changed
  fields instead of echoing secret values.
- **Vocabulary:** Action must be a registered type (an `AuditActions` constant or an extension-registered type);
  free-text actions are rejected or normalized. `LogAsync` accepts any string today.
- **Failed actions:** failed logins ✔ and other denied/aborted operations are recorded; a failed audit write surfaces an
  error.
- **Authorization:** every read, filter, search and export request passes a permission check (Super Admin, Admin).

### Edge cases

- **Failed login without a resolved user:** entry with null user, IP, user agent, timestamp and a safe identifier ✔
  (`AccountController` stores the typed email in `EntityDisplaySnapshot`); repeated failures feed lockout and rate
  limiting ([security](security.md)).
- **Clock skew** across instances: use one consistent server time source.
- **Tenant isolation:** entries never leak across tenants; tenant admins see only their tenant (today the Audit Logs
  screen ✔ and Dashboard count ✔ are scoped, but the Audit Dashboard extension reads all tenants, and failed-login
  entries carry no `TenantId`, so they show in every tenant).
- **Audit store unavailable:** records are never dropped silently - surface the failure and/or buffer for a durable
  write. Today `LogAsync` runs after the change is saved, so an audit failure produces an error after the change.

### Acceptance criteria

Viewer criteria (pagination, filters, search, export) are on the [Audit Logs screen](../pages/audit-logs.md#acceptance-criteria).

- [x] Each entry records User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC) and Details (`AuditLogEntry`, `AuditService.LogAsync`; no caller fills Details yet).
- [ ] Logged actions include user login, failed login, content created/updated/deleted, media uploaded, plugin installed, plugin disabled, role changed, permission changed, settings changed, API token created and webhook created (constants ✔; written ✔ except plugin installed/disabled, role changed, permission changed and webhook created).
- [ ] Failed actions, including failed logins with no resolved user, are recorded with a null user and without the attempted password (failed logins ✔ `AccountController`; other failures are not logged).
- [ ] Each entry carries the tenant it occurred in and log access is filtered by the active tenant (filter ✔ `AuditLogsController`, `DashboardController`; failed-login entries have no tenant; the Audit Dashboard extension is unfiltered).
- [ ] Only Super Admin and Admin can view, filter, search and export audit logs (viewing ✔ `AuditLogsController` role check; the rest does not exist).
- [ ] Super Admin can view logs across all tenants; tenant Admins see only their tenant's logs (everyone sees the active tenant plus tenant-less entries; no cross-tenant view).
- [ ] Entries are append-only: no UI or API path edits or deletes an entry, and mutation attempts are rejected (no path ✔; no rejection guard).
- [x] Passwords and token/secret plaintext are never persisted in any field, for any action (current writers: `AccountController` / `SetupController` log only the email, `ApiTokensController` only id and name, API-token actors only the token id; no generic redaction).
- [ ] Timestamps are stored in UTC and displayed in the viewer's configured timezone (UTC storage ✔; shown as UTC).
- [ ] A retention/rotation policy is configurable; archival/purge is a controlled process without interactive per-entry deletion.
- [ ] No audit entry leaks across tenant boundaries (screen and Dashboard ✔; the Audit Dashboard extension reads all tenants).
- [ ] Every audit-log read, filter, search and export request passes an explicit permission check.
