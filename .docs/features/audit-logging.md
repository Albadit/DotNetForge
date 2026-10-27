# Audit logging

Append-only records of security-relevant actions. Written only through `IAuditService`
(`src/DotNetForge.Shared/Auditing/IAuditService.cs`, implemented by `src/DotNetForge.Web/Services/AuditService.cs`); read on the
[Audit Logs screen](../pages/audit-logs.md), counted on the [Dashboard](../pages/dashboard.md), and shown by the
sample [Audit Dashboard admin extension](../pages/extension-host.md).

## `IAuditService.LogAsync`

```csharp
Task LogAsync(string action, string? entityType = null, string? entityId = null,
              string? entityDisplay = null, string? details = null, bool success = true,
              CancellationToken cancellationToken = default)
```

`AuditService` builds an `AuditLogEntry` from the current `HttpContext`, saves it immediately (its own
`SaveChangesAsync`) and then queues it for [extension handlers](#extension-handlers):

| Field | Source |
| --- | --- |
| `UserId` | `NameIdentifier` claim; `null` when anonymous **or when the caller is an API token** |
| `UserDisplaySnapshot` | API token: `API token {token id}` (from the `dnf:token` claim); otherwise `Identity.Name` or the email claim. Max 256 |
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
| `UserLogin` | `user.login` | `AccountController.Login` (success; `HttpContext.User` is set to the new principal first, so the entry carries the user); `ExternalLoginController.Callback` (details `via <provider>`) | `User`, id, email |
| `UserLoginFailed` | `user.login.failed` | `AccountController.Login` (any failure; `success: false`); `ExternalLoginController` (details `via <provider>` or `external sign-in`) | `User`, no id, the typed / provider email |
| `UserLogout` | `user.logout` | `AccountController.Logout` | - |
| `UserRegistered` | `user.registered` | `AccountService.RegisterAsync` (`/register`; details `Role: <name>`; anonymous actor) | `User`, id, email |
| `PasswordResetRequested` | `user.password.reset.requested` | `AccountService.RequestPasswordResetAsync` (`success` = an enabled account matched; anonymous) | `User`, id when matched, the typed email |
| `PasswordReset` | `user.password.reset` | `AccountService.ResetPasswordAsync` (a reset link was used) | `User`, id, email |
| `EmailConfirmed` | `user.email.confirmed` | `AccountService.ConfirmEmailAsync` (first use of a confirmation link) | `User`, id, email |
| `UserCreated` | `user.created` | `UserManagementService.CreateAsync` (from `UsersController.Create`) | `User`, id, email |
| `UserUpdated` | `user.updated` | `UserManagementService.UpdateAsync` / `.SetStatusAsync` (details `Enabled` / `Disabled`); `ProfileService` (details `Own profile` / `Own password changed`) | `User`, id, email |
| `UserDeleted` | `user.deleted` | `UserManagementService.DeleteAsync` | `User`, id, email |
| `RoleChanged` | `role.changed` | `RoleManagementService` create / update of name or description / duplicate / delete (details `Created`, `Updated`, `Duplicated from <name>`, `Deleted`); `UserManagementService.UpdateAsync` when a user's roles change (entity `User`, details `Role assignment changed`) | `Role`, id, name |
| `PermissionChanged` | `permission.changed` | `RoleManagementService.UpdateAsync` when the grant set changes; `PagePermissionService.SaveAsync` when a page's grants change (entity `Page`, details list the view and edit roles) | `Role` / `Page`, id, name |
| `CmsInstalled` | `cms.installed` | `SetupController.Index` (POST, after a successful install; written after the automatic sign-in, so it carries the new user) | `User`, id, email |
| `ContentCreated` | `content.created` | `PagesController.Create` and `Duplicate` (details "Duplicate of '<title>'"); `ContentApiController.CreatePage` (`POST /api/content/pages`, actor = API token) | `Page`, id, title |
| `ContentUpdated` | `content.updated` | `PagesController.Update`; `PageBuilderService` publish / unpublish (details `Layout published` / `Layout unpublished`; draft saves are not audited) | `Page`, id, title |
| `ContentDeleted` | `content.deleted` | `PagesController.Delete` | `Page`, id, title |
| `ContentReordered` | `content.reordered` | `PagesController.Reorder` | `Page`, no id, `"{n} page(s)"` |
| `ContentTypeCreated` | `contenttype.created` | `ContentTypeService.CreateAsync` (one per type, so a template that adds linked types writes several) | `ContentType`, id, name |
| `ContentTypeUpdated` | `contenttype.updated` | `ContentTypeService.UpdateAsync` (details `"{n} field(s)"` when the fields changed) | `ContentType`, id, name |
| `ContentTypeDeleted` | `contenttype.deleted` | `ContentTypeService.DeleteAsync` (details `"With {n} entries"` when its entries went too; no entry is audited separately) | `ContentType`, id, name |
| `EntryCreated` | `entry.created` | `ContentEntryService.SaveAsync` (new entry) and `DuplicateAsync` (details "Duplicate of '<title>'") | `Entry`, entry id, "<type name>: <entry title>" (`Untitled` when the title is empty) |
| `EntryUpdated` | `entry.updated` | `ContentEntryService.SaveAsync` (existing entry) | as above |
| `EntryPublished` | `entry.published` | `ContentEntryService.PublishAsync`; also after a save that publishes (written after `entry.created` / `entry.updated`) | as above |
| `EntryUnpublished` | `entry.unpublished` | `ContentEntryService.UnpublishAsync` | as above |
| `EntryDeleted` | `entry.deleted` | `ContentEntryService.DeleteAsync`, and once per entry by `DeleteManyAsync` (bulk delete) | as above |
| `EndpointCreated` | `endpoint.created` | `CustomEndpointService.SaveAsync` (new endpoint; details: its name and operations, e.g. `Products: GET, GET /{id}, POST`) | `CustomEndpoint`, id, its base path (`CustomEndpointService.Display`: "/api/custom/products") |
| `EndpointUpdated` | `endpoint.updated` | `CustomEndpointService.SaveAsync` (existing endpoint; details as above) | as above |
| `EndpointDeleted` | `endpoint.deleted` | `CustomEndpointService.DeleteAsync` (the endpoint and its operations; details: its name) | as above |
| `EndpointTested` | `endpoint.tested` | `ApiEndpointsController.Test` (one operation; details `Changes rolled back` / `Changes kept`) | `CustomEndpoint`, no id (the run may be unsaved), the tested operation's route (`CustomEndpointService.Route`: "GET /api/custom/products/{id}") |
| `EndpointTokenRegenerated` | `endpoint.token.regenerated` | `CustomEndpointService.RegenerateTokenAsync` (from `ApiEndpointsController.RegenerateToken`; details: its name; never the token) | `CustomEndpoint`, id, its base path |
| `EndpointExecuted` | `endpoint.executed` | `CustomEndpointsApiController.Call` for every method but GET; details `<name> (called with its token)` or `<name> (public call)`; `Success = false` when the SQL failed. No API token is involved, so the actor is anonymous - or the signed-in user when the call carries the admin session cookie (e.g. "Test Request" in the [API docs](api-docs.md)) | `CustomEndpoint`, id, the called operation's route ("DELETE /api/custom/products/{id}") |
| `MediaUploaded` | `media.uploaded` | `MediaService.UploadAsync` (per stored file, from `MediaController.Upload`) | `MediaFile`, id, file name |
| `MediaDeleted` | `media.deleted` | `MediaService.DeleteAsync` (from `MediaController.Delete`) | `MediaFile`, id, file name |
| `MediaUpdated` | `media.updated` | `MediaService.SetVisibilityAsync` (from `MediaController.Visibility`; details `Made public` / `Made private`) | `MediaFile`, id, file name |
| `MediaMoved` | `media.moved` | `MediaFolderService.MoveFileAsync` (from `MediaController.MoveSelected`, per file; details `To <path>`) | `MediaFile`, id, file name |
| `MediaFolderCreated` | `media.folder.created` | `MediaFolderService.CreateAsync` (from `MediaController.CreateFolder`) | `MediaFolder`, id, path |
| `MediaFolderUpdated` | `media.folder.updated` | `MediaFolderService.RenameAsync` / `MoveAsync` (details `Renamed from …` / `Moved from …`) | `MediaFolder`, id, new path |
| `MediaFolderDeleted` | `media.folder.deleted` | `MediaFolderService.DeleteAsync` (empty folders only) | `MediaFolder`, id, path |
| `SettingsChanged` | `settings.changed` | `SettingsController.Save`; `UserAccountSettingsService.SaveAsync` (display "Advanced User Settings"); `EmailConfigurationController` connection test and test sends (display "Email configuration"; details "Connection test passed/failed", "Test email sent", "Test of '<template>' failed" ...; a failure `success: false`); `EmailTemplateService` save / restore (entity `EmailTemplate`, key, "Email template: <name>"); `DatabaseTransferService.SaveScheduleAsync` (display "Backup schedule", details the schedule) | `Setting`, key (or none), key or screen name (value not recorded) |
| `TransferExported` | `transfer.exported` | `DatabaseTransferService.ExportAsync` (from `DataManagementController.Export`; a failure `success: false`) | `TransferRecord`, id, file name |
| `BackupCreated` | `transfer.backup.created` | `DatabaseTransferService` - Back up now, automatic backups (no user; details "Automatic") and the safety backup before an import (details its note) | `TransferRecord`, id, file name |
| `BackupDownloaded` | `transfer.backup.downloaded` | `DatabaseTransferService.OpenBackupAsync` (from `DataManagementController.Download`) | `TransferRecord`, id, file name |
| `BackupDeleted` | `transfer.backup.deleted` | `DatabaseTransferService.DeleteBackupAsync`; pruning of automatic backups (details "Older than the newest n automatic backups") | `TransferRecord`, id, file name |
| `TransferImported` | `transfer.imported` | `DatabaseTransferService.ImportAsync` (Development; details `<source> → <target>`; a refused or failed import `success: false` with the reason) | `TransferRecord`, id, file name |
| `BackupRestored` | `transfer.backup.restored` | `DatabaseTransferService.RestoreAsync` (Development; like `TransferImported`) | `TransferRecord`, id, file name |
| `ApiTokenCreated` | `apitoken.created` | `ApiTokensController.Create` | `ApiToken`, id, name |
| `ApiTokenRevoked` | `apitoken.revoked` | `ApiTokensController.Revoke` | `ApiToken`, id, name |
| `PluginEnabled` | `plugin.enabled` | `ExtensionLifecycleService.EnableAsync` (from `PluginsController.Enable`) | `Extension`, manifest id, name |
| `PluginDisabled` | `plugin.disabled` | `ExtensionLifecycleService.DisableAsync` (from `PluginsController.Disable`) | `Extension`, manifest id, name |

Defined but never written: `plugin.installed`, `webhook.created`, `audit.exported`.

**Extension-defined actions.** Extensions write through the same `IAuditService` with their own action strings
(`LogAsync` accepts any string). The samples write `contactform.submitted` (Contact Form: entity `ContactMessage`, the
storage key, `"<name> <email>"`, details = the first 200 characters of the message; anonymous, so no user).

The content-type and entry actions come from the [Content Manager](../pages/content-manager.md),
[Content types](../pages/content-types.md) and `/api/entries` alike (the services audit, not the controllers), so the
actor is the signed-in user or the API token ([structured content](structured-content.md)).

Not logged: API reads, reordering of entries or content types, scheduler changes (`ScheduledPublishingService`), and failed or denied operations other than
sign-in (403s, validation errors, rejected uploads, refused enable/disable). `Details` is filled only by external
sign-in and extensions.

## Extension handlers

Extensions subscribe to every saved entry with `services.AddAuditEventHandler<THandler>(context)` (`DotNetForge.Sdk`;
`THandler : IAuditEventHandler`, registered scoped). `AuditEventDispatcher`
(`src/DotNetForge.Web/Services/AuditEventDispatcher.cs`, a `BackgroundService`) delivers them:

| Rule | Behaviour |
| --- | --- |
| Hand-off | `AuditService` calls `Publish(AuditEvent)` after `SaveChangesAsync`; it only writes to a bounded in-memory `Channel` and never blocks or throws into the request |
| Queue | `QueueCapacity` = 1000 events; when full, new events are dropped with a warning log (`Audit event queue is full; ...`). Nothing is queued when no handler is registered |
| Order | one event at a time, every handler in registration order |
| Gate | a handler runs only while its extension is active (`ExtensionCatalog.IsActiveAsync`, checked per event) |
| Isolation | each call gets its own DI scope and a `HandlerTimeout` of 30 s (via the cancellation token); an exception is logged (`Audit handler ... failed on ...`) and the next handler runs |
| Durability | none: events queued in memory are lost on shutdown or crash; the `AuditLogs` row is the record. Per instance only |

`AuditEvent` carries `Action`, `EntityType`, `EntityId`, `EntityDisplay`, `Details`, `Success`, `UserDisplay`,
`IpAddress`, `TimestampUtc` - not the user agent or user id. Sample: `extensions/providers/sms-notifications`
sends an SMS for the actions in its `notifyOn` setting (default `user.login.failed`, `contactform.submitted`,
`plugin.enabled`, `plugin.disabled`).

## Rules

- Write audit entries only via `IAuditService.LogAsync`, with a constant from `AuditActions` (add new keys there).
- Log after the change is saved; the two saves are not transactional, so a failure between them loses the entry.
- Never put secrets (passwords, token plaintext, hashes) in `Details` or the display snapshots.
- No code may update or delete `AuditLogs` rows.
- Extensions that react to audit events use `AddAuditEventHandler`, never polling `AuditLogs`; a handler must be
  fast (30 s timeout) and must not write an audit entry for every event it handles (that would loop).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
Viewer (list, filters, search, export): [Audit Logs screen](../pages/audit-logs.md#planned-not-implemented).

### Requirements

- Every significant admin **and system** action writes an audit entry, both successful and failed.
- Fields: User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC), Details - all ✔
  columns on `AuditLogEntry`. Target deltas:
  - **IP address:** the real client IP, honouring trusted forwarded/proxy headers where configured (✔ when
    `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`; off by default).
  - **Details:** structured JSON context (changed field names, previous/new values, reason, plugin name),
    redacted before persistence. No caller fills it today.
- **Acting principal** for non-interactive actions (system, background job, API token): record system, the token
  identifier or a null user, keeping IP/user agent where available. ✔ API token: `UserId = null`,
  `UserDisplaySnapshot = "API token {id}"` (`AuditService`); the scheduler writes nothing.
- **Action vocabulary** is extensible: extensions may register additional action types (✔ they may write any action
  string, e.g. `contactform.submitted`; ✘ no registration).
- **Retention:** configurable retention policy with rotation/archival (by age, or roll over to archive storage at a
  size threshold). It is a controlled system process, never an interactive per-entry delete; archival/purge events
  are themselves audited.

Tracked actions (minimum set):

| Action | Typical entity | Constant | Written today |
| --- | --- | --- | --- |
| User login | `User` | `UserLogin` | ✔ |
| Failed login | `User` or none; safe identifier only, never the password | `UserLoginFailed` | ✔ |
| Content created / updated / deleted | `Page` | `ContentCreated` / `ContentUpdated` / `ContentDeleted` | ✔ |
| Media uploaded | `Media` | `MediaUploaded` | ✔ (`MediaService`; also `MediaDeleted`, `MediaUpdated`) |
| Plugin installed / disabled | `Plugin` | `PluginInstalled` / `PluginDisabled` | installed ✘ (no install); disabled ✔ (`Extension`, also `PluginEnabled`) |
| Role changed (created, edited, duplicated, deleted, assigned/unassigned) | `Role` | `RoleChanged` | ✔ |
| Permission changed (grants of a role) | `Permission` / `Role` | `PermissionChanged` | ✔ (entity `Role`) |
| Settings changed | `Setting` | `SettingsChanged` | ✔ |
| API token created (metadata only) | `ApiToken` | `ApiTokenCreated` | ✔ |
| Webhook created | `Webhook` | `WebhookCreated` | ✘ |
| Audit log exported (optional) | - | `AuditExported` | ✘ |

### User flows

**Automatic logging (system side):** action completes or fails → build the entry (User, Action, Entity type/ID, IP,
user agent, server UTC timestamp, Details) → redact secrets from Details → append (never reversible or
editable) → a failed action is still recorded with a failure indication (`Success = false` ✔); an audit-write failure
must not silently swallow the underlying error.

### Rules and validation

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| View audit logs | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Filter / search / export | ✔ | ✔ | ✘ | ✘ | ✘ | ✘ |
| Edit or delete entries | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ |

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
- **Audit store unavailable:** records are never dropped silently - surface the failure and/or buffer for a durable
  write. Today `LogAsync` runs after the change is saved, so an audit failure produces an error after the change.

### Acceptance criteria

Viewer criteria (pagination, filters, search, export) are on the [Audit Logs screen](../pages/audit-logs.md#acceptance-criteria).

- [x] Each entry records User, Action, Entity type, Entity ID, IP address, User agent, Timestamp (UTC) and Details (`AuditLogEntry`, `AuditService.LogAsync`; no caller fills Details yet).
- [ ] Logged actions include user login, failed login, content created/updated/deleted, media uploaded, plugin installed, plugin disabled, role changed, permission changed, settings changed, API token created and webhook created (constants ✔; written ✔ except plugin installed and webhook created).
- [ ] Failed actions, including failed logins with no resolved user, are recorded with a null user and without the attempted password (failed logins ✔ `AccountController`; other failures are not logged).
- [ ] Only Super Admin and Admin can view, filter, search and export audit logs (viewing ✔ `AuditLogsController` role check; the rest does not exist).
- [ ] Entries are append-only: no UI or API path edits or deletes an entry, and mutation attempts are rejected (no path ✔; no rejection guard).
- [x] Passwords and token/secret plaintext are never persisted in any field, for any action (current writers: `AccountController` / `SetupController` log only the email, `ApiTokensController` only id and name, API-token actors only the token id; no generic redaction).
- [ ] Timestamps are stored in UTC and displayed in the viewer's configured timezone (UTC storage ✔; shown as UTC).
- [ ] A retention/rotation policy is configurable; archival/purge is a controlled process without interactive per-entry deletion.
- [ ] Every audit-log read, filter, search and export request passes an explicit permission check.
