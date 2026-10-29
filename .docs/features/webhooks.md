# Webhooks

Webhooks notify other systems - a search index, a cache, a static-site build, a downstream site - when structured
content entries or media files change. A webhook is a receiver URL plus the events it subscribes to; every matching
change becomes a signed HTTP `POST` with a JSON body, sent in the background and retried with exponential backoff when
it fails. Every attempt is kept in the webhook's delivery log for 30 days.

Webhooks are managed on the [Webhooks screen](../pages/webhooks.md) (**Settings → API & Webhooks → Webhooks**). There
is no API for them yet ([Planned](#planned-not-implemented)).

## Where it lives

```text
src/DotNetForge.Shared/Entities/Webhook.cs                         Webhook (table Webhooks), WebhookDelivery (table WebhookDeliveries)
src/DotNetForge.Shared/Constants/WebhookEvents.cs                  the eight event keys, SignatureHeader
src/DotNetForge.Shared/Constants/AuditActions.cs                   webhook.* audit actions
src/DotNetForge.Infrastructure/Security/HmacWebhookSigner.cs       IWebhookSigner: GenerateSecret, Sign (HMAC-SHA256 hex)
src/DotNetForge.Web/Services/Webhooks/WebhookService.cs            validation, create/update/enable/disable/delete, secrets, test deliveries, audit
src/DotNetForge.Web/Services/Webhooks/WebhookPayload.cs            audit action → event mapping, the JSON body, entry/media data
src/DotNetForge.Web/Services/Webhooks/WebhookDispatcher.cs         BackgroundService: event queue, pending attempts, retries, loop guard, log pruning
src/DotNetForge.Web/Services/Webhooks/WebhookSender.cs             one attempt: headers, signature, POST, result
src/DotNetForge.Web/Services/AuditService.cs                       raises the events (WebhookDispatcher.Raise after each saved audit entry)
src/DotNetForge.Web/Startup/DependencyRegistration.cs              registrations, the "webhooks" HttpClient (no redirects)
src/DotNetForge.Web/Areas/Admin/Controllers/WebhooksController.cs  the screen (pages/webhooks.md)
tests/DotNetForge.IntegrationTests/WebhookTests.cs                 end-to-end tests against a fake receiver
```

## A webhook

| Field | Rules (`WebhookService`) | Column |
| --- | --- | --- |
| Name | required, ≤ 200 characters, trimmed; unique case-insensitively ("Another webhook is already called '<name>'.") | `Name` |
| URL | absolute, ≤ 2000 characters, no user name/password in it ("Enter the full address of the receiver, e.g. https://example.com/hooks/cms."); `https://` only - plain `http://` only for a loopback host (`localhost`, `127.0.0.1`) while the app runs in Development ("The URL must start with https:// (plain http only for localhost, in Development).") | `Url` |
| Events | at least one, each one of the eight [event keys](#events) ("Choose at least one event."); stored in `WebhookEvents.All` order | `EventsCsv` |
| Headers | rows of name / value sent with every request; see below | `HeadersJson` |
| Enabled | default on; off = it neither fires nor retries | `Enabled` |
| Secret | generated, never typed in; see [Secret](#secret) | `Secret` |
| Created / Last delivery | set by the system; `LastDelivery` = time of the latest attempt sent (test deliveries included, failed ones too) | `CreatedDate`, `LastDelivery` |

Custom headers - a row left completely empty is ignored, the others are checked:

| Rule | Message |
| --- | --- |
| at most 20 rows (`WebhookService.MaxHeaders`) | "At most 20 headers." |
| every row has a name; the value may be empty | "Every header needs a name (its value may be empty)." |
| name = 1-100 letters, digits and `-` | "'<name>' is not a valid header name (letters, digits and - only)." |
| not a header the CMS or HTTP sets: `Host`, `Transfer-Encoding`, `Connection`, `Expect`, `User-Agent`, any `Content-*` (they describe the body, which the CMS sets) or any `X-DotNetForge-*` | "'<name>' is set by the CMS and can't be a custom header." |
| no line breaks in a value | "Header values can't contain line breaks." |
| each name once (case-insensitive) | "Each header name can be used once." |
| all headers together fit `HeadersJson` (4000 characters as stored, `MaxHeadersLength`) | "The headers are too long together (at most about 4000 characters)." |

`HeadersJson` holds an array `[{"key":"Authorization","value":"Bearer …"}]`, so the order is kept;
`WebhookService.ReadHeaders` also reads the original object format (`{"Authorization":"Bearer …"}`). Header values are
stored in clear and shown on the editor ([security](#security)).

All errors are collected and shown together; the same rules apply on create and edit. There is no API that creates
webhooks, so the screen is the only writer.

## Events

`WebhookEvents.All` - exactly eight keys. They are raised from the audit trail: `AuditService.LogAsync` saves the
audit entry, then calls `WebhookDispatcher.Raise`, which maps the action (`WebhookPayload.EventFor`) and queues the
event. Only successful audit entries raise events.

| Event | Raised by the audit action | Written by |
| --- | --- | --- |
| `entry.create` | `entry.created` | `ContentEntryService.SaveAsync` (new entry), `DuplicateAsync` |
| `entry.update` | `entry.updated` | `ContentEntryService.SaveAsync` (existing entry) |
| `entry.publish` | `entry.published` | `ContentEntryService.PublishAsync`; a save that publishes raises `entry.create`/`entry.update` **and** `entry.publish` |
| `entry.unpublish` | `entry.unpublished` | `ContentEntryService.UnpublishAsync` |
| `entry.delete` | `entry.deleted` | `ContentEntryService.DeleteAsync`, once per entry by `DeleteManyAsync` |
| `media.create` | `media.uploaded` | `MediaService.UploadAsync` (Media screen, entry forms, page builder) |
| `media.update` | `media.updated`, `media.moved` | `MediaService.SetVisibilityAsync` (public/private), `RenameAsync`; `MediaFolderService.MoveFileAsync` |
| `media.delete` | `media.deleted` | `MediaService.DeleteAsync` (also per file in a bulk delete) |

Entries are the structured-content entries of the [Content Manager](../pages/content-manager.md) - the services are
shared with `/api/entries`, so API changes fire the same events ([structured content](structured-content.md)).
Nothing else fires: no content-page (`Page`) changes, no entry or page reordering, no content-type changes (deleting
a type with its entries writes no `entry.deleted`), no folder changes (a renamed or moved folder doesn't change file
URLs, which are `/media/{id}/{fileName}`), no video compression results, no import or restore on Data Management, no
custom-endpoint SQL.

## Payload

Each delivery is a `POST` with `Content-Type: application/json; charset=utf-8` and this body
(`WebhookPayload.Build`, camelCase, nulls included):

```json
{
  "id": "7f0c2a47-3d0e-4a39-9a1e-0c6a3c1f9b52",
  "event": "entry.publish",
  "createdAt": "2026-10-29T09:41:12.345Z",
  "entity": { "type": "entry", "id": "c5b0…", "name": "Categories: Design" },
  "data": { … }
}
```

| Field | Value |
| --- | --- |
| `id` | the event id (`WebhookDelivery.EventId`) - the same on every retry and in the `X-DotNetForge-Delivery` header, so receivers can deduplicate. One change sent to several webhooks has the same id everywhere |
| `event` | the event key; `webhook.test` for a [test delivery](#test-delivery) |
| `createdAt` | when the change was raised (UTC) |
| `entity` | `type` = `entry` or `media` (`webhook` for a test), `id` = the entry or media file id, `name` = the audit display text (an entry: "<type name>: <title>", a file: its file name) |
| `data` | the entry or file as it is when the event is turned into deliveries (moments after the change, in the background); `null` for `entry.delete` / `media.delete` and when the record no longer exists |

`data` of an entry: `contentType` (the type's slug), `title`, `published`, `hasDraft`, `publishedAt`, `createdAt`,
`updatedAt`, `data` (the working copy, `DataJson` - drafts included) and `publishedData` (the published values, or
`null`). `data` of a media file: `fileName`, `originalName`, `contentType`, `sizeBytes`, `isPublic`, `folderId`,
`url` (`APP_URL` + `/media/{id}/{fileName}`; a private file needs an admin sign-in), `uploadedAt`.

The body is built once per event and stored with every attempt (`WebhookDelivery.RequestBody`, ≤ 65 536 characters),
so retries send the same bytes.

## Delivery

```mermaid
sequenceDiagram
    participant R as Request (admin or API)
    participant A as AuditService
    participant D as WebhookDispatcher
    participant DB as WebhookDeliveries
    participant S as WebhookSender
    participant X as Receiver
    R->>A: LogAsync("media.uploaded", ...)
    A->>D: Raise (in-memory queue, never blocks)
    A-->>R: request continues
    D->>DB: one Pending row per enabled, subscribed webhook
    D->>DB: claim due row (2-minute lease)
    D->>S: SendAsync
    S->>X: POST + headers + X-DotNetForge-Signature (10 s)
    X-->>S: 2xx / other / no answer
    S-->>D: Success or Failure
    D->>DB: save; on failure add the next attempt (backoff)
```

| Step | Behaviour |
| --- | --- |
| Hand-off | `Raise` writes to a bounded in-memory channel (`QueueCapacity` = 1000) and returns; when it is full the event is dropped with the warning "The webhook queue is full; …". The request that made the change never waits |
| Fan-out | the queue loop loads the enabled webhooks subscribed to the event, builds the body once and adds one `Pending` `WebhookDelivery` per webhook (`AttemptNumber` 1, `NextAttemptDate` = now) |
| Sending | the delivery loop takes due attempts (`Pending`, `NextAttemptDate` ≤ now), oldest first, 20 at a time, and sends them **one after another**. It runs on start, after each fan-out on this instance, and every 15 s (`PollInterval`) |
| Claim | before sending, an attempt's `NextAttemptDate` is moved 2 minutes ahead (`Lease`) with a conditional update; an instance that loses the race skips it. If the instance dies while sending, the attempt is due again after the lease - so delivery is **at least once** |
| Request | `POST` to the webhook's current URL with its current headers and secret: the custom headers first, then `X-DotNetForge-Event` (the event key), `X-DotNetForge-Delivery` (the event id) and `X-DotNetForge-Signature` (below), which replace a custom header of the same name; `User-Agent: DotNetForge-Webhooks/1.0` |
| Client | named `HttpClient` `"webhooks"` (`WebhookSender.ClientName`): `SocketsHttpHandler` with `AllowAutoRedirect = false` and a 10 s connect timeout |
| Success | any `2xx` within 10 s (`WebhookSender.Timeout`, covering connect, answer and reading the first 2000 characters of the response) |
| Failure | a non-`2xx` answer - redirects included ("The receiver answered 302 Found.") -, no answer in time ("No answer within 10 seconds."), a connection error ("Could not connect: …"), or a secret that can't be read ("Not sent: the signing secret can't be read on this site (it came with an import). Rotate the secret.") |
| Retries | a failed attempt up to number 4 gets a new `Pending` row for the next attempt - same `EventId`, same body - after 30 s, 2 min, 8 min, 32 min (`Backoff(n)` = 30 s × 4ⁿ⁻¹); the failed row's detail ends "Retrying at HH:mm:ss UTC.". Attempt 5 (`MaxAttempts`) failing ends with "Gave up after 5 attempts." The webhook stays enabled |
| Disabled or deleted | disabling sets every `Pending` attempt of the webhook to `Failure` "Cancelled: the webhook was disabled." (also when an attempt comes due while the webhook is off); re-enabling doesn't bring them back. Deleting removes the log with the webhook |
| Log | every attempt is a row: target URL, time, status code, response time, attempt number, outcome, detail, request body and the first 2000 characters of the response; `Webhook.LastDelivery` is updated |
| Retention | once an hour, finished attempts (not `Pending`) older than 30 days (`Retention`) are deleted |

**Durability.** Pending attempts are database rows: they survive a restart and are shared by every instance. Events
still in the in-memory queue (not yet turned into rows) are lost on shutdown or crash. The queue is per instance; any
instance can send any due attempt.

**Ordering.** Attempts go out oldest first, but retries and several instances mean a receiver can get events out of
order and more than once. Use `id` to deduplicate and `createdAt` (or the entity's `updatedAt`) to order.

### Signature

`X-DotNetForge-Signature` (`WebhookEvents.SignatureHeader`) = lowercase hex HMAC-SHA256 of the exact body bytes
(UTF-8, no BOM), keyed with the UTF-8 bytes of the webhook's secret (`HmacWebhookSigner.Sign`). A receiver checks it
over the raw body - before parsing the JSON - and compares in constant time:

```js
// Node.js (Express with express.raw({ type: "application/json" }))
const expected = Buffer.from(crypto.createHmac("sha256", secret).update(req.body).digest("hex"));
const received = Buffer.from(req.get("x-dotnetforge-signature") ?? "");
const ok = received.length === expected.length && crypto.timingSafeEqual(received, expected);
```

A proxy or framework that re-encodes the body breaks the check.

### Loop guard

A receiver that changes content in the CMS (through the API or the admin) could cause another event, and so on.

- **Changes made by a delivery fire nothing:** when the request that makes a change carries the
  `X-DotNetForge-Delivery` header, `Raise` drops the event (logged: "Webhook event {Event} not raised: the change came
  from a webhook delivery."). A receiver that calls back should copy that header from the delivery into its calls.
- **At most 120 events a minute per webhook** (`MaxEventsPerMinute`): counted from the first attempts of the last
  minute (test deliveries included); beyond that an event gets a `Failure` row "Skipped: more than 120 events in a
  minute (loop guard)." and is not sent or retried.

### Test delivery

**Send test delivery** (`WebhookService.SendTestAsync`) sends at once, in the request, a signed `webhook.test` event
(`entity.type` `webhook`, `data` = `{ message, events }`) with the custom headers - also for a disabled webhook. It is
logged as attempt 1 with event `webhook.test`, is never retried, doesn't touch real subscriptions and writes the
audit action `webhook.tested` (`Success` = the outcome).

## Secret

- Generated by `IWebhookSigner.GenerateSecret()` - 32 random bytes as 64 lowercase hex characters - when the webhook is
  created and on **Rotate secret**; never accepted from input.
- Stored protected with Data Protection (purpose `DotNetForge.Webhooks.Secret.v1`, key ring in `DataProtectionKeys`);
  unprotected only to sign (`WebhookService.Secret`, `UnprotectSecret`).
- Shown once, on the screen after creating or rotating (through `TempData`); never again, not in the audit log.
- Rotating replaces it at once: every later attempt - retries of earlier events too - is signed with the new secret,
  so the receiver must switch at the same time (or accept both for a while).
- A secret protected on another site can't be read (a [Data Management](transfer-and-updates.md) import brings the
  webhooks but not the key ring): its attempts fail with "Not sent: …" without contacting the receiver until the
  secret is rotated.

## Permissions

The **Webhooks** permission area (`PermissionAreas.Webhooks`: `read`, `create`, `update`, `delete`), checked on every
action of `WebhooksController` with `[RequirePermission]`:

| Action | Needs |
| --- | --- |
| List, editor (read-only without `update`), delivery log, the secret screen | `read` |
| Create | `create` |
| Save, enable/disable, rotate the secret, send a test delivery | `update` |
| Delete | `delete` |

By default only Super Admin and Admin hold the area (`PermissionMatrix`); Developers, Editors and Authors get it only
when granted on [Roles](../pages/roles.md). The API keys `webhooks.read` / `webhooks.manage` exist but no endpoint
uses them ([authorization](authorization.md)).

## Audit

`WebhookService` writes `webhook.created`, `webhook.updated`, `webhook.enabled`, `webhook.disabled`,
`webhook.deleted`, `webhook.secret.rotated` and `webhook.tested` (entity `Webhook`, id, name; details in
[audit logging](audit-logging.md#what-is-logged-today)). Deliveries themselves are not audited - they are in the
delivery log. Audit entries about webhooks raise no events.

## Security

- Signed bodies (above); the secret is protected at rest and shown once.
- Redirects are not followed, so a signed body never goes to an address other than the configured one.
- `https://` only outside Development.
- The loop guard bounds feedback loops.
- **Not built:** any restriction on the target address - a URL may point at a private or internal host (no SSRF
  filter), and DNS is resolved at send time. Only users with the Webhooks `create`/`update` permission can set it.
- Custom header values (e.g. an `Authorization` token for the receiver) are stored in clear in `HeadersJson`, shown on
  the editor to every user with `read`, and included in exports. See [security](security.md).

## Tests

`tests/DotNetForge.IntegrationTests/WebhookTests.cs`, against a fake receiver plugged in as the `"webhooks"` client's
handler: validation messages and the one-time secret (stored protected), a signed test delivery with custom headers
and the user agent, a media upload firing only the subscribed webhook with a valid signature, a failure retried 30 s
later with the same event id and body, disabling cancelling pending attempts, an entry publish firing `entry.create`
and `entry.publish` with the entry data, the loop guard header, and Editors denied the screen.
`WebhookSignerTests` (`tests/DotNetForge.Tests/SecurityPrimitiveTests.cs`) cover the signer.

## Where to change things

- Which changes fire which event, and the body: `WebhookPayload` (`EventFor`, `Build`, `DataAsync`). A new kind of
  event needs a key in `WebhookEvents` (and a checkbox in `Views/Webhooks/Edit.cshtml`) and an audit action that
  raises it. Changing the body or the signature breaks receivers.
- Retries, timing, the loop guard, retention: constants on `WebhookDispatcher`; timeout and headers: `WebhookSender`.
- Validation and the secret: `WebhookService` (`ValidateAsync`, `ReservedHeaders`).
- Schema: `Webhook.cs` + `DotNetForgeDbContext` + one migration ([database-change skill](../../.claude/skills/database-change/SKILL.md),
  [database](../architecture/database.md)).
- API endpoints: [api-endpoint skill](../../.claude/skills/api-endpoint/SKILL.md), gated by `webhooks.read` /
  `webhooks.manage`, calling `WebhookService` (never re-implementing its rules).

## Planned (not implemented)

Target behaviour from the original product specification that the code does not have yet. Everything above this
section is built.

### Requirements

- **API:** list, create, edit, enable/disable, delete and rotate webhooks and read the delivery log through the
  headless API, gated by `webhooks.read` / `webhooks.manage`, with the same validation as the screen.
- **Content pages:** `entry.*` events for content pages (`Page`) as well - today only structured-content entries fire.
- **Sensitive header values** protected like the secret (stored protected, masked on the editor and in exports).
- **Maximum attempts** as a setting (fixed at 5 today, `WebhookDispatcher.MaxAttempts`).
- **Target address restrictions** (SSRF): refuse private, loopback and link-local addresses outside Development,
  checked when sending as well as when saving.

### Acceptance criteria

- [x] A user with the Webhooks permission can create a webhook with Name, URL, Headers, Events and Enabled status
  (`WebhooksController.Create`, `WebhookService.CreateAsync`).
- [x] Saving with a non-HTTPS, relative or malformed URL is rejected with a clear validation error (admin form; no API).
- [x] Saving with zero selected events is rejected.
- [x] A header row with an empty Key is rejected; an empty Value is accepted.
- [x] A name duplicating an existing webhook is rejected (case-insensitive).
- [ ] All eight events can be subscribed to and each fires a delivery when it occurs (✔ for entries and media files;
  ✘ content pages fire nothing).
- [x] Each notification is an HTTP `POST` with a JSON body and all configured custom headers.
- [x] Each request carries a valid HMAC signature header over the request body using the webhook's secret,
  verifiable by the receiver (`WebhookSender`, `WebhookTests`).
- [ ] A failed delivery (connection error, non-`2xx`, timeout) is retried with exponential backoff up to the
  configured maximum, then gives up and records a final failure (✔ retries and final failure; ✘ the maximum is a
  constant, not a setting).
- [x] A slow endpoint exceeding the per-attempt timeout is recorded as a failed attempt and retried; the triggering
  CMS request is not blocked.
- [x] Every attempt is logged with target URL, timestamp, response status, response time, attempt number and
  outcome, and is viewable in the per-webhook delivery log.
- [x] **Send test delivery** sends a signed sample `POST` and reports the result without affecting real
  subscriptions.
- [x] Disabling stops firing and cancels pending retries; re-enabling restores firing.
- [x] Rotating the secret generates a new one, displays it once and signs subsequent deliveries with it.
- [ ] Only Super Admin and Admin (or roles granted the Webhooks permission) can manage webhooks; others are denied
  in UI and API (✔ UI; ✘ no API).
- [x] A webhook-triggered change cannot cause an unbounded delivery loop (`X-DotNetForge-Delivery` guard, 120 events
  a minute per webhook).
- [x] Webhook creation, enable/disable and deletion are recorded in the audit log.
- [ ] The same validation applies to the admin form and the API (no API).
