# Webhooks

Webhooks notify external systems (search indexers, caches, CI/CD, downstream sites) when content or media changes,
by sending a signed HTTP `POST` to configured endpoints. Today only the **foundation** exists - tables, event
constants, a signer and a placeholder screen. Nothing creates, fires or delivers a webhook.

## Current state

| Piece | Code | State |
| --- | --- | --- |
| Entities | `Webhook`, `WebhookDelivery` in `src/DotNetForge.Shared/Entities/Webhook.cs` | tables exist (`InitialCreate` migration); nothing writes them |
| Event keys | `WebhookEvents` (`src/DotNetForge.Shared/Constants/WebhookEvents.cs`) | constants only |
| Signing | `IWebhookSigner` (`DotNetForge.Abstractions.Security`) → `HmacWebhookSigner` (`Infrastructure/Security`) | registered as singleton in `DependencyRegistration`, **never resolved**; covered by `WebhookSignerTests` |
| Permission keys | `PermissionKeys.WebhooksRead` = `webhooks.read`, `WebhooksManage` = `webhooks.manage` | in `PermissionKeys.All`, so grantable to API tokens; no endpoint uses them |
| Permission area | `PermissionAreas.Webhooks`; `PermissionMatrix` grants Admin every action | reference data, not consulted at request time ([authorization](authorization.md)) |
| Audit | `AuditActions.WebhookCreated` = `webhook.created` | never written |
| Dashboard | `DashboardController` counts `Webhooks` of the active tenant | always `0` ([Dashboard](../pages/dashboard.md)) |
| Screen | `GET /admin/webhooks` → `ModulesController.Webhooks`; sidebar **Settings · Global Settings → Webhooks** | [placeholder](../pages/module-placeholders.md), `AdminArea` policy (any admin-capable role) |

### `Webhook`

| Property | Type | Configuration (`DotNetForgeDbContext`) | Notes |
| --- | --- | --- | --- |
| `Id` | `Guid` | key | |
| `TenantId` | `Guid` | no FK | tenant scope |
| `Name` | `string` | required, max 200 | no unique index |
| `Url` | `string` | required, max 2000 | |
| `HeadersJson` | `string` | max 4000 | JSON object, default `"{}"` |
| `EventsCsv` | `string` | max 1000 | comma-separated; split by the computed `Events` (ignored by EF) |
| `Enabled` | `bool` | | default `true` |
| `Secret` | `string` | required | stored in clear (needed to sign) |
| `CreatedDate` | `DateTime` | | default `DateTime.UtcNow` |
| `LastDelivery` | `DateTime?` | | |
| `Deliveries` | `ICollection<WebhookDelivery>` | one-to-many, **cascade delete** | |

### `WebhookDelivery`

| Property | Type | Configuration | Notes |
| --- | --- | --- | --- |
| `Id` | `Guid` | key | |
| `WebhookId` | `Guid` | FK, indexed | |
| `TargetUrl` | `string` | max 2000 | |
| `RequestTimestamp` | `DateTime` | | default `DateTime.UtcNow` |
| `Event` | `string` | max 100 | |
| `ResponseStatusCode` | `int?` | | |
| `ResponseTimeMs` | `long?` | | |
| `AttemptNumber` | `int` | | default `1` |
| `Outcome` | `WebhookOutcome` | | `Pending` (default), `Success`, `Failure` |
| `Detail` | `string?` | max 4000 | |

### Event keys and signing

- `WebhookEvents.All`: `entry.create`, `entry.update`, `entry.delete`, `entry.publish`, `entry.unpublish`,
  `media.create`, `media.update`, `media.delete`; `WebhookEvents.IsValid(key)` checks membership. No code raises
  any of them (content changes, scheduled publishing and the API emit nothing; media has no upload).
- `WebhookEvents.SignatureHeader` = `X-DotNetForge-Signature`.
- `HmacWebhookSigner.Sign(secret, payload)` = lowercase hex HMAC-SHA256 of the UTF-8 bytes of `payload` under the
  UTF-8 bytes of `secret`. `GenerateSecret()` = 32 random bytes (`RandomNumberGenerator`) as 64 lowercase hex chars.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- Create, edit, enable, disable and delete webhooks, **per tenant**; a tenant's webhooks fire only for that tenant's
  events ([multi-tenancy](multi-tenancy.md)).
- Fields:

| Field | Rules | Column |
| --- | --- | --- |
| Name | required; unique within the tenant (warn or reject) | `Name` ✔ |
| URL | required, absolute `https://` URL | `Url` ✔ |
| Headers | list of `Key` (required) / `Value` (may be empty) pairs, sent with every request | `HeadersJson` ✔ |
| Events | at least one of the eight event keys | `EventsCsv` ✔ |
| Enabled | default on; disabled webhooks neither fire nor retry | `Enabled` ✔ |
| Secret | system-generated, rotatable, shown once at creation/rotation, never accepted from input | `Secret` ✔, `IWebhookSigner.GenerateSecret` ✔ |
| Created date / Last delivery | system-managed | `CreatedDate` ✔ / `LastDelivery` ✔ |

- Events: `entry.*` fire when a content entry (today: a content page, see
  [content pages and routing](content-pages-and-routing.md)) is created, updated, deleted, published or
  unpublished; `media.*` when a media file is created/uploaded, updated or deleted.
- Delivery:
  - HTTP `POST`, JSON body with event key, entity type, entity id, tenant (where applicable), timestamp and the
    relevant payload data.
  - All custom headers on every request; system headers (signature, identity) win over a user header of the same
    name.
  - `X-DotNetForge-Signature` ✔ = HMAC ✔ over the **exact raw body bytes** sent, using the webhook's secret.
  - Failure = connection error, timeout or non-`2xx`. Retry with **exponential backoff** up to a configured maximum
    number of attempts, then record a final failure (no setting for the maximum exists).
  - Bounded per-attempt timeout.
  - Performed **out-of-band**; never blocks the request that raised the event.
  - Every attempt (initial and each retry) logged with target URL, timestamp, status code, response time, attempt
    number and outcome (`WebhookDelivery` columns ✔), plus request/response details for inspection.
  - Loop guard: a webhook calling back into the CMS must not cascade indefinitely (suppress events caused by
    webhook-driven changes, deduplicate and/or bound delivery volume).
- Secrets: `Secret` and sensitive header values are secrets - never committed to Git; see [security](security.md).
- Audit: creation, enable/disable and deletion ([audit logging](audit-logging.md)); only `webhook.created` exists ✔.

### Planned admin screen

Replaces the placeholder at `/admin/webhooks` (sidebar entry ✔). Build with the
[admin-page skill](../../.claude/skills/admin-page/SKILL.md).

```text
Webhooks (list)
├── [Create webhook]
└── table: Name · URL · Events · Status (enabled/disabled) · Last delivery
          · [Edit] [Enable/Disable] [Send test delivery] [Delivery log] [Delete]

Webhook editor
├── Name* · URL* · Enabled (default on)
├── Headers: rows Key* / Value · [Add header]
├── Events: 8 checkboxes (≥ 1)
├── [Rotate secret] · [Send test delivery] · [Delivery log]
└── [Save] [Cancel]

Secret panel (after create / rotate): new secret shown once, to copy

Delivery log (per webhook)
└── table: Timestamp · Event · Status code · Response time · Attempt · Outcome → attempt details (request/response)
```

### User flows

| Flow | Steps |
| --- | --- |
| Create | **Create webhook** → Name, HTTPS URL, Enabled → save → validation → secret generated and shown once → listed with events and status → audit. |
| Add headers | Editor → **Headers** → add rows (Key required, Value optional) → save → every row must have a key. |
| Select events | Editor → **Events** → tick one or more of the eight → save → at least one required. |
| Send test delivery | List or editor → **Send test delivery** → signed sample `POST` with all configured headers → show status, response time, success/failure → attempt written to the delivery log; real subscriptions unaffected. |
| View delivery log | Open webhook → **Delivery log** → recent attempts → open one for request/response details. |
| Disable | Toggle **Enabled** off / **Disable** → stops firing, cancels pending retries → audit; configuration kept for re-enabling. |
| Rotate secret | Editor → **Rotate secret** → new secret shown once → later deliveries signed with it; the receiver must be updated. |

### Rules and validation

| Rule | Today |
| --- | --- |
| URL required, absolute, `https://` only; relative, `http://` and malformed URLs rejected | ✘ |
| At least one event; each in `WebhookEvents.All` | `IsValid` helper ✔, unused |
| Name required; duplicate within the tenant warns or is rejected | required at DB level ✔; no unique index |
| Every header row has a non-empty Key; empty Value allowed | ✘ (`HeadersJson` is an object, so duplicate keys collapse) |
| Enabled defaults to on | entity default ✔ |
| Secret system-generated, never from input, must exist before the webhook can fire | `Secret` required ✔ |
| Same validation on the admin form and the API | ✘ |

| Role | View webhooks & logs | Create / edit | Test delivery | Enable / disable | Delete | Rotate secret |
| --- | :-: | :-: | :-: | :-: | :-: | :-: |
| Super Admin | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Admin | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Editor | if granted Webhooks | if granted | if granted | if granted | ✘ | ✘ |
| Author, Authenticated, Public | ✘ | ✘ | ✘ | ✘ | ✘ | ✘ |

- Default: only Super Admin and Admin manage webhooks; other roles through the **Webhooks** permission area.
  Enforced on every action in the admin UI and the API (`webhooks.read` / `webhooks.manage` for tokens).
- Rules apply within the active tenant; Super Admins may act across tenants.
- The secret is shown only at creation and rotation, and only to users with management permission.

### Edge cases

| Case | Required behaviour |
| --- | --- |
| Endpoint down / non-`2xx` | retry with exponential backoff up to the maximum, then final failure in the delivery log; webhook stays enabled |
| Endpoint slow | timeout = failed attempt → retry cycle; triggering CMS request never blocked |
| Receiver signature check | receiver recomputes the HMAC over the raw body with the shared secret and rejects mismatches; proxies or re-encoding that alter the body break verification |
| Feedback loop | webhook-triggered changes must not cause unbounded deliveries |
| Secret rotation | old-secret signatures stop verifying; cut over in a maintenance window or let the receiver accept both secrets temporarily; new secret shown once |
| Multi-tenant | a webhook never receives another tenant's events; tenant admins manage only their tenants' webhooks |
| Disabled with pending retries | retries cancelled; in-flight events dropped, not queued for re-enable |
| Many events in quick succession | one delivery per event; at-least-once - receivers deduplicate on entity id + timestamp |

### Acceptance criteria

- [ ] A user with the Webhooks permission can create a webhook with Name, URL, Headers, Events and Enabled status.
- [ ] Saving with a non-HTTPS, relative or malformed URL is rejected with a clear validation error.
- [ ] Saving with zero selected events is rejected.
- [ ] A header row with an empty Key is rejected; an empty Value is accepted.
- [ ] A name duplicating an existing webhook in the same tenant warns or is rejected.
- [ ] All eight events can be subscribed to and each fires a delivery when it occurs.
- [ ] Each notification is an HTTP `POST` with a JSON body and all configured custom headers.
- [ ] Each request carries a valid HMAC signature header over the request body using the webhook's secret,
  verifiable by the receiver (signer ✔ in `HmacWebhookSigner`, but no request is ever sent).
- [ ] A failed delivery (connection error, non-`2xx`, timeout) is retried with exponential backoff up to the
  configured maximum, then gives up and records a final failure.
- [ ] A slow endpoint exceeding the per-attempt timeout is recorded as a failed attempt and retried; the triggering
  CMS request is not blocked.
- [ ] Every attempt is logged with target URL, timestamp, response status, response time, attempt number and
  outcome, and is viewable in the per-webhook delivery log.
- [ ] **Send test delivery** sends a signed sample `POST` and reports the result without affecting real
  subscriptions.
- [ ] Disabling stops firing and cancels pending retries; re-enabling restores firing.
- [ ] Rotating the secret generates a new one, displays it once and signs subsequent deliveries with it.
- [ ] Only Super Admin and Admin (or roles granted the Webhooks permission) can manage webhooks; others are denied
  in UI and API.
- [ ] In multi-tenant deployments a webhook receives only its own tenant's events.
- [ ] A webhook-triggered change cannot cause an unbounded delivery loop.
- [ ] Webhook creation, enable/disable and deletion are recorded in the audit log.

## Where to change things

- Schema: `src/DotNetForge.Shared/Entities/Webhook.cs` + `DotNetForgeDbContext` + a migration
  ([database-change skill](../../.claude/skills/database-change/SKILL.md), [database](../architecture/database.md)).
- Event keys and the signature header: `WebhookEvents`. Signature format: `HmacWebhookSigner` +
  `tests/DotNetForge.Tests/SecurityPrimitiveTests.cs` - changing it breaks every receiver.
- Raising events: where content pages change (`PageService`, `ContentController`, `ContentApiController.CreatePage`,
  `ScheduledPublishingService`) and, once uploads exist, media. Hand off to a queue; never deliver inline.
- Delivery and retries: a background worker (the `ScheduledPublishingService` hosted-service pattern), writing
  `WebhookDelivery` rows and `Webhook.LastDelivery`.
- Admin screen: replace `ModulesController.Webhooks` with a dedicated controller, add a `pages/` document and update
  [module placeholders](../pages/module-placeholders.md).
- API endpoints for webhooks: [api-endpoint skill](../../.claude/skills/api-endpoint/SKILL.md), gated by
  `webhooks.read` / `webhooks.manage`.
- Audit keys: `AuditActions` (add enable/disable/delete next to `WebhookCreated`).
