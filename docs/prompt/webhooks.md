# Webhooks

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

Webhooks let DotNetForge CMS notify external systems when content or media changes by sending signed HTTP `POST` requests to configured endpoints. This module covers webhook configuration, supported events, secure delivery (HMAC signing, retries with backoff, delivery logging), and the flows and rules for managing webhooks.

## Purpose

Webhooks provide an outbound integration point so external services (search indexers, caches, CI/CD pipelines, notification systems, downstream sites) can react to changes inside the CMS in near real time. When a subscribed event occurs, the system sends a signed `POST` request containing the event payload to each enabled webhook's URL, retries failed deliveries with backoff, and records every delivery attempt for auditing and troubleshooting.

In a multi-tenant deployment, webhooks are scoped per tenant: a tenant's webhooks only fire for events that occur within that tenant's scope, and they must never receive events from other tenants. See [Multi-Tenancy](multi_tenancy.md) for tenant resolution and isolation rules.

## Main Features

- Create, edit, enable, disable, and delete webhooks.
- Configure a target endpoint URL, custom HTTP headers, and a set of subscribed events per webhook.
- Subscribe to content entry and media lifecycle events.
- Send `POST` notifications with a JSON payload describing the event.
- Sign every request with an HMAC signature header so receivers can verify authenticity and integrity.
- Retry failed deliveries automatically using exponential backoff, then give up after a configured maximum.
- Send an on-demand **test delivery** to verify the endpoint before relying on it.
- Log every delivery attempt (request, response status, timing, retries, outcome) and expose a per-webhook delivery log.
- Rotate the signing secret without losing the webhook's configuration.
- Scope webhooks per tenant in multi-tenant deployments (see [Multi-Tenancy](multi_tenancy.md)).

## Data Model / Fields

### Webhook

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | string | Yes | Human-readable label for the webhook. Should be unique (see Validation Rules). |
| `URL` | string (absolute HTTPS URL) | Yes | The endpoint that receives the `POST` notification. Must be a valid absolute `https://` URL. |
| `Headers` | list of header pairs | No | Custom HTTP headers sent with every request (see Header below). |
| `Events` | list of event keys | Yes | The events this webhook subscribes to. At least one must be selected. |
| `Enabled` | boolean | Yes | Whether the webhook is active. Disabled webhooks do not fire and are not retried. |
| `Secret` | string (signing secret) | System-managed | Secret used to compute the HMAC signature header. Generated on creation; rotatable. Stored securely and never displayed after creation. See [Security](security.md). |
| `Created date` | datetime | System-managed | When the webhook was created. |
| `Last delivery` | datetime | System-managed | Timestamp of the most recent delivery attempt. |

### Header

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Key` | string | Yes | The HTTP header name (e.g. `Authorization`, `X-Custom-Token`). Required for each header row. |
| `Value` | string | No | The HTTP header value. May be empty, but a header with no key is invalid. |

> Sensitive header values (tokens, secrets) are treated as secrets: they live with the webhook configuration, must never be committed to Git, and follow the secret-handling rules in [Security](security.md).

### Supported Events

| Event key | Triggered when |
|-----------|----------------|
| `entry.create` | A content entry is created. |
| `entry.update` | A content entry is updated. |
| `entry.delete` | A content entry is deleted. |
| `entry.publish` | A content entry is published. |
| `entry.unpublish` | A content entry is unpublished. |
| `media.create` | A media file is created/uploaded. |
| `media.update` | A media file is updated. |
| `media.delete` | A media file is deleted. |

Entry events relate to content managed in the [Content Manager](content_manager.md); media events relate to assets in the [File Manager](file_manager.md).

## Delivery Mechanics

- **Method:** Every notification is sent as an HTTP `POST` with a JSON body describing the event (event key, entity type, entity ID, tenant context where applicable, timestamp, and relevant payload data).
- **Custom headers:** All configured headers are attached to every request. System signing/identity headers are always added and take precedence over any user-supplied header of the same name.
- **Signing:** Each request includes an HMAC signature header (e.g. `X-DotNetForge-Signature`) computed over the raw request body using the webhook's secret. Receivers recompute the HMAC with the shared secret and compare it to verify authenticity and integrity. See [Security](security.md) for the signing scheme.
- **Retries & backoff:** A delivery is considered failed if the connection fails, times out, or the endpoint returns a non-`2xx` status. Failed deliveries are retried automatically with **exponential backoff** (increasing delays between attempts) up to a configured maximum number of attempts, after which the system gives up and records the final failure.
- **Timeouts:** Each delivery attempt uses a bounded request timeout. A slow endpoint that exceeds the timeout is treated as a failed attempt and enters the retry/backoff cycle.
- **Logging:** Every attempt (initial and each retry) is logged with the target URL, request timestamp, response status code, response time, attempt number, and final outcome.

## User Flows

### Flow: Create a webhook

1. The user opens **Settings → Global Settings → Webhooks** in the admin area.
2. The user selects **Create webhook**.
3. The user enters a **Name** and the target **URL** (an absolute HTTPS URL).
4. The user toggles **Enabled** on or off (defaults to enabled).
5. The user saves. The system validates the input, generates a signing **Secret**, and displays it once for the user to copy.
6. The webhook appears in the list with its configured events and status. The action is recorded in the [Audit Logs](audit_logs.md).

### Flow: Add headers

1. From the webhook editor, the user opens the **Headers** section.
2. The user adds a header row and enters a **Key** (required) and an optional **Value**.
3. The user repeats for each additional header.
4. The user saves. The system validates that every header row has a non-empty key.

### Flow: Select events

1. From the webhook editor, the user opens the **Events** section.
2. The user checks one or more of the supported events (entry create/update/delete/publish/unpublish, media create/update/delete).
3. The user saves. The system validates that at least one event is selected.

### Flow: Send a test delivery

1. From the webhook editor or list, the user selects **Send test delivery**.
2. The system sends a sample `POST` request to the configured URL, including all configured headers and a valid HMAC signature header.
3. The system shows the result (response status, response time, success or failure) and records the attempt in the delivery log.

### Flow: View the delivery log

1. The user opens a webhook and selects **Delivery log**.
2. The system lists recent delivery attempts with timestamp, event, response status, response time, attempt number, and outcome.
3. The user can inspect an individual attempt to see request and response details for troubleshooting.

### Flow: Disable a webhook

1. From the webhook list or editor, the user toggles **Enabled** off (or selects **Disable**).
2. The system stops firing the webhook and cancels any pending retries for it.
3. The change is recorded in the [Audit Logs](audit_logs.md). The webhook configuration is retained and can be re-enabled later.

### Flow: Rotate the signing secret

1. From the webhook editor, the user selects **Rotate secret**.
2. The system generates a new secret and displays it once for the user to copy into the receiving system.
3. New deliveries are signed with the new secret. The user must update the receiver's stored secret to keep signature verification working (see Edge Cases for the cutover guidance).

## Role & Permission Rules

Webhook management is gated by the **Webhooks** permission area (see [User Roles & Permissions](user_roles_permissions.md)). In multi-tenant deployments, these rules apply within the active tenant's scope; global Super Admins may act across all tenants (see [Multi-Tenancy](multi_tenancy.md)).

| Role | View webhooks & logs | Create / Edit / Configure | Send test delivery | Enable / Disable | Delete | Rotate secret |
|------|:---:|:---:|:---:|:---:|:---:|:---:|
| Super Admin | Yes | Yes | Yes | Yes | Yes | Yes |
| Admin | Yes | Yes | Yes | Yes | Yes | Yes |
| Editor | Optional (if granted Webhooks permission) | No (unless granted) | No (unless granted) | No (unless granted) | No | No |
| Author | No | No | No | No | No | No |
| Authenticated | No | No | No | No | No | No |
| Public | No | No | No | No | No | No |

- By default, only **Super Admin** and **Admin** manage webhooks.
- Access can be granted to other roles by assigning the **Webhooks** permission area; permission checks must be enforced for every webhook action in both the admin UI and the API. See [Security](security.md).
- The signing secret is shown only at creation and rotation, and only to users with management permission.

## Validation Rules

- **URL:** Required. Must be a valid, absolute URL using the `https://` scheme. Non-absolute URLs, `http://` URLs, and malformed URLs are rejected.
- **Events:** At least one event must be selected. A webhook with zero events cannot be saved.
- **Name:** Required. Must be unique-ish - the system warns on or rejects a duplicate name within the same scope (tenant) to avoid confusion.
- **Headers:** Each header row must have a non-empty **Key**. A row with an empty key is invalid even if it has a value. **Value** may be empty.
- **Enabled:** Boolean; defaults to enabled on creation.
- **Secret:** System-generated; never accepted directly from user input. It is required for signing and must exist before the webhook can fire.
- All input is validated on both the admin form and the API per the input-validation requirements in [Security](security.md).

## Edge Cases

- **Endpoint down:** If the endpoint is unreachable or returns a non-`2xx` status, the delivery is retried with exponential backoff up to the configured maximum attempts, then the system gives up and records a final failure in the delivery log. The webhook remains enabled.
- **Endpoint slow / timeouts:** A request that exceeds the per-attempt timeout is treated as a failed attempt and enters the retry/backoff cycle. Slow endpoints must not block the CMS request that triggered the event - delivery is performed out-of-band (asynchronously).
- **Signature verification by receiver:** Receivers verify the HMAC signature header using the shared secret and the raw request body. If a receiver computes a mismatched signature, it should reject the request. Body transformations (re-encoding, proxies altering the payload) will break verification; the signature must be computed over the exact bytes the receiver reads.
- **Webhook firing causing a loop:** If a webhook calls back into the CMS and triggers further events that fire the same or another webhook, an infinite loop can result. The system must guard against loops - for example, suppress events originating from webhook-driven changes, deduplicate, and/or bound delivery volume so a feedback loop cannot cascade indefinitely.
- **Secret rotation:** Rotating the secret invalidates signatures verified with the old secret. To avoid dropped deliveries during cutover, rotate during a maintenance window or have the receiver temporarily accept both old and new secrets until the new secret is in place. The system displays the new secret only once at rotation.
- **Tenant-scoped webhooks:** In multi-tenant deployments, a tenant's webhooks fire only for events within that tenant's scope and must never receive another tenant's events; tenant admins manage only their assigned tenants' webhooks. See [Multi-Tenancy](multi_tenancy.md).
- **Disabled webhook with pending retries:** Disabling a webhook cancels its pending retries; in-flight events for that webhook are dropped, not queued for later re-enable.
- **Duplicate / rapid events:** When many events fire in quick succession, deliveries are still attempted per event; receivers should treat deliveries as at-least-once and deduplicate using the payload's entity ID and timestamp.

## Acceptance Criteria

- [ ] A user with the Webhooks permission can create a webhook with a Name, URL, Headers, Events, and Enabled status.
- [ ] Saving a webhook with a non-HTTPS, relative, or malformed URL is rejected with a clear validation error.
- [ ] Saving a webhook with zero selected events is rejected.
- [ ] A header row with an empty Key is rejected; an empty Value is accepted.
- [ ] Creating a webhook with a name that duplicates an existing webhook (within the same tenant) warns or is rejected.
- [ ] All eight events (`entry.create`, `entry.update`, `entry.delete`, `entry.publish`, `entry.unpublish`, `media.create`, `media.update`, `media.delete`) can be subscribed to and each fires a delivery when it occurs.
- [ ] Each notification is sent as an HTTP `POST` with a JSON body and includes all configured custom headers.
- [ ] Each request includes a valid HMAC signature header computed over the request body using the webhook's secret, and a receiver can verify it with the shared secret.
- [ ] A failed delivery (connection error, non-`2xx`, or timeout) is retried with exponentially increasing backoff up to the configured maximum, then gives up and records a final failure.
- [ ] A slow endpoint exceeding the per-attempt timeout is recorded as a failed attempt and retried; the triggering CMS request is not blocked.
- [ ] Every delivery attempt is logged with target URL, timestamp, response status, response time, attempt number, and outcome, and is viewable in the per-webhook delivery log.
- [ ] **Send test delivery** sends a signed sample `POST` and reports the result without affecting real event subscriptions.
- [ ] Disabling a webhook stops it from firing and cancels pending retries; re-enabling restores firing.
- [ ] Rotating the signing secret generates a new secret, displays it once, and signs subsequent deliveries with it.
- [ ] Only Super Admin and Admin (or roles explicitly granted the Webhooks permission) can manage webhooks; unauthorized users are denied in both UI and API.
- [ ] In multi-tenant deployments, a webhook only receives events from its own tenant and never from other tenants.
- [ ] The system prevents a webhook-triggered change from causing an unbounded delivery loop.
- [ ] Webhook creation, enable/disable, and deletion are recorded in the [Audit Logs](audit_logs.md).
