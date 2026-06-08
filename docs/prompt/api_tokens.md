# API Tokens & Headless API

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

API tokens authorize programmatic access to DotNetForge CMS in **Headless** and **Hybrid** modes. Each token carries a granular set of permissions, is bound to a tenant scope, and is stored only as a hash - the plaintext value is shown exactly once at creation and is never retrievable afterward.

## Purpose

DotNetForge CMS is a modular hybrid CMS that operates in three modes: Traditional (visual admin UI), **Headless** (everything managed via API with tokens and permissions), and **Hybrid** (visual CMS and API at the same time). This module owns the API token system that secures the Headless API surface.

The system must let authorized users:

- Mint API tokens that grant a precise, granular subset of CMS capabilities to external clients, scripts, and integrations.
- Expose CMS resources - content, media, users, roles, settings, and extensions - over a tokenized HTTP API.
- Resolve the correct tenant context for every API request and prevent a token from reaching tenants it was not granted.
- Generate and store tokens securely as hashes so a leaked database never reveals usable credentials.
- Inspect token usage (last-used), and revoke or rotate tokens at any time.

API tokens are managed under **Settings → Global Settings → API Tokens** in the admin area. The token form and its actions are never affected by public frontend themes (the admin area is always theme-independent).

## Main Features

- **Token lifecycle**: create (show once), list, inspect, revoke, and rotate tokens.
- **Granular permissions**: select exactly which API capabilities a token may use across every permission area, including custom extension permissions.
- **Hash-only storage**: tokens are securely generated and persisted only as cryptographic hashes; plaintext is never stored or shown again after creation.
- **Expiration**: choose a token duration (or a fixed expiration date); expired tokens are rejected automatically.
- **Usage tracking**: record and display the **Last used date** for each token.
- **Tenant scoping**: every token is bound to a tenant context; a token must not access other tenants unless explicitly granted. See [Multi-Tenancy](multi_tenancy.md).
- **Headless API surface**: REST endpoints surface content, media, users, roles, settings, and extensions, gated by the token's permissions.
- **Auditing**: token creation, revocation, and rotation are recorded. See [Audit Logs](audit_logs.md).

## Data Model / Fields

### Token fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | string | Yes | Human-readable identifier for the token. Must be unique within the tenant. |
| Description | string (multiline) | No | Optional notes about the token's purpose and owner. |
| Token duration | enum | Yes | Validity period. One of: `7 days`, `30 days`, `90 days`, `Unlimited`, or `Custom`. When `Custom`, an explicit **Expiration date** is required. |
| Expiration date | datetime (UTC) | Conditional | The point at which the token stops working. Required when **Token duration** is `Custom`; otherwise derived from the chosen duration. Empty/`null` only when duration is `Unlimited`. |
| Permissions | string[] | Yes | The granular set of permission keys this token grants (see [Permission Model](#permission-model)). Must be a non-empty subset of the creator's own permissions. |
| Created by | reference (User) | Yes (system-set) | The user who minted the token. Set automatically; not editable. |
| Created date | datetime (UTC) | Yes (system-set) | Timestamp of creation. Set automatically; not editable. |
| Last used date | datetime (UTC) | No (system-set) | Timestamp of the most recent successful authenticated request using this token. `null` until first use. |
| Revoked status | boolean | Yes (system-set) | `true` once the token has been revoked. A revoked token is permanently unusable. |

Additional system-managed fields:

| Field | Type | Description |
|-------|------|-------------|
| Token hash | string | The salted cryptographic hash of the token secret. Never displayed; never returned by the API. |
| Token prefix | string | A short non-secret prefix (e.g. first characters) stored in clear to help users identify a token in lists without revealing the secret. |
| Tenant | reference (Tenant) | The tenant scope the token is bound to. See [Multi-Tenancy](multi_tenancy.md). |

### Permission Model

Token permissions must cover everything the API can access. Permissions are granular keys grouped by permission area. The canonical permission areas are: **Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, Webhooks** (see [User Roles & Permissions](user_roles_permissions.md)). For API tokens, the selectable capability groups are:

| Area | Example permission keys | Notes |
|------|-------------------------|-------|
| Core CMS features | `core.read`, `core.manage` | Foundational CMS operations exposed to the API. |
| Content | `content.read`, `content.create`, `content.update`, `content.delete`, `content.publish` | Pages, entries, collection types, and single types. See [Content Manager](content_manager.md). |
| Media | `media.read`, `media.upload`, `media.update`, `media.delete` | File Manager files and metadata. See [File Manager](file_manager.md). |
| Users | `users.read`, `users.create`, `users.update`, `users.delete` | Admin/account users. See [Users](user_roles_permissions.md). |
| Roles | `roles.read`, `roles.create`, `roles.update`, `roles.delete` | RBAC roles. See [User Roles & Permissions](user_roles_permissions.md). |
| Settings | `settings.read`, `settings.update` | CMS and tenant settings. See [Settings](settings.md). |
| Extensions | `extensions.read`, `extensions.manage` | Installed extensions and their lifecycle. See [Extensions](extensions.md). |
| Custom extension permissions | `<extension-defined>` (e.g. `myplugin.invoices.read`) | Permissions registered by installed extensions via their manifest. Appear dynamically in the picker. See [Extensions](extensions.md). |

The permission picker must enumerate all built-in keys plus any custom permissions declared by installed extensions. Custom extension permissions originate from the `permissions` field of each extension's `dotnetforge.extension.json` manifest.

## Headless API Overview

In Headless and Hybrid modes, the CMS exposes a REST API whose endpoints surface **content, media, users, roles, settings, and extensions**. Every endpoint enforces a permission check, and access is granted only when the presented token carries the matching permission key.

### Authentication

Clients authenticate by presenting the token in the HTTP `Authorization` header as a bearer token:

```http
GET /api/content/pages HTTP/1.1
Host: example.com
Authorization: Bearer <token>
```

### Tenant context resolution

The routing system resolves the tenant **first**, then resolves the requested resource within that tenant's scope. For API requests, the tenant context is resolved from **domain, path, header, or token** (in that order of specificity). Detailed tenant resolution rules live in [Multi-Tenancy](multi_tenancy.md).

A token is bound to a tenant scope. **A token must not access other tenants unless explicitly granted.** If a request's resolved tenant differs from the token's granted scope, the request is rejected with `403 Forbidden`.

### Endpoint surface (representative)

```text
GET    /api/content/{type}            # requires content.read
POST   /api/content/{type}            # requires content.create
PUT    /api/content/{type}/{id}       # requires content.update
DELETE /api/content/{type}/{id}       # requires content.delete
GET    /api/media                     # requires media.read
POST   /api/media                     # requires media.upload
GET    /api/users                     # requires users.read
GET    /api/roles                     # requires roles.read
GET    /api/settings                  # requires settings.read
GET    /api/extensions                # requires extensions.read
```

Every request must pass: (1) valid, non-expired, non-revoked token; (2) tenant scope match; (3) the specific permission key for the operation. Failing any check returns an error response without performing the action.

## User Flows

### Flow: Create a token (shown once)

1. The user opens **Settings → Global Settings → API Tokens** and selects **Create token**.
2. The user enters **Name** (required), optional **Description**, selects a **Token duration** (and an **Expiration date** if `Custom`), and selects one or more **Permissions**.
3. The system validates all inputs (see [Validation Rules](#validation-rules)), including that the chosen permissions are a subset of the creator's own permissions and that any expiration is in the future.
4. On save, the system **securely generates** a high-entropy token secret, computes its salted hash, and stores only the hash (plus prefix, metadata, and tenant scope). The plaintext secret is never persisted.
5. The system displays the plaintext token **exactly once** with a copy action and a clear warning that it cannot be retrieved again.
6. The creation event is recorded in the audit log (`API token created`). See [Audit Logs](audit_logs.md).

### Flow: Use a token (Authorization header)

1. The client sends an HTTP request to an API endpoint with `Authorization: Bearer <token>`.
2. The system hashes the presented secret and looks up the matching token record by hash.
3. The system verifies the token is **not revoked** and **not expired**, resolves the tenant context, and confirms the token is scoped to that tenant.
4. The system checks the endpoint's required permission key against the token's **Permissions**.
5. On success, the request proceeds and the system updates the token's **Last used date**. On any failure, the request is rejected with the appropriate error and no resource is touched.

### Flow: Inspect last-used and usage

1. The user opens the **API Tokens** list, which shows Name, prefix, permissions summary, Created date, **Last used date**, expiration, and revoked status.
2. The user selects a token to view full details (Created by, Description, full permission set, tenant scope).
3. The user uses the **Last used date** (and audit logs) to determine whether a token is active or stale and may decide to revoke or rotate it.

### Flow: Revoke a token

1. The user selects a token and chooses **Revoke**.
2. The system asks for confirmation (revocation is immediate and irreversible).
3. The system sets **Revoked status** to `true`. All subsequent requests with that token fail with `401 Unauthorized`.
4. The revocation is recorded in the audit log.

### Flow: Rotate a token (e.g. after a leak)

1. The user selects the compromised token and chooses **Rotate**.
2. The system generates a new secret for an equivalent token (same Name semantics, Description, permissions, duration, and tenant scope), shows the new plaintext **once**, and stores only the new hash.
3. The system **revokes the old token** so the leaked secret stops working immediately.
4. Both the new-token creation and old-token revocation are recorded in the audit log.

## Role & Permission Rules

Tokens are minted by authenticated admin users; the canonical roles, most-privileged to least, are: Super Admin, Admin, Editor, Author, Authenticated, Public. The right to mint tokens is itself a permission, and a token can never grant more than its creator holds.

| Role | Create token | Permissions a token may receive | Revoke / rotate | Scope |
|------|--------------|---------------------------------|-----------------|-------|
| Super Admin | Yes | Any permission, in any tenant | Any token | Global, across all tenants |
| Admin | Yes (if granted `API` permission) | Only a subset of the Admin's own permissions, within their tenant | Tokens within their tenant | Their assigned tenant(s) |
| Editor | Only if explicitly granted `API` permission | Only a subset of the Editor's own permissions | Their own tokens | Their tenant |
| Author | Only if explicitly granted `API` permission | Only a subset of the Author's own permissions | Their own tokens | Their tenant |
| Authenticated | No (unless explicitly granted `API` permission) | n/a | n/a | n/a |
| Public | No | n/a | n/a | n/a |

Rules:

- Minting a token requires the `API` permission area (see [User Roles & Permissions](user_roles_permissions.md)).
- The token's **Permissions** must be a subset of the creating user's effective permissions. A user can never escalate privilege by creating a more-powerful token than they themselves hold.
- A non–Super Admin can only create, view, revoke, or rotate tokens within tenants they are assigned to. Super Admins operate across all tenants. See [Multi-Tenancy](multi_tenancy.md).

## Validation Rules

- **Name** is required, non-empty, trimmed, and unique within the token's tenant scope.
- **Token duration** is required and must be one of the allowed values (`7 days`, `30 days`, `90 days`, `Unlimited`, `Custom`).
- **Expiration date** is required when duration is `Custom`, must be a valid datetime, and **must be in the future** at creation time. It must be empty/`null` only when duration is `Unlimited`.
- **Permissions** must be provided and must contain at least one valid permission key (a token with no permissions cannot be saved).
- **Permissions** must be a **subset of the creator's own effective permissions**; any key outside the creator's grant is rejected.
- Each selected permission key must reference a known built-in or currently-installed custom extension permission.
- **Created by**, **Created date**, **Last used date**, **Revoked status**, **Token hash**, **Token prefix**, and **Tenant** are system-managed and cannot be set or edited via the form.
- The token secret must be generated with a cryptographically secure random source and stored only as a salted hash; the system must never persist or log the plaintext secret.
- The tenant scope must be valid; a token cannot be created with a scope the creator cannot access.

## Edge Cases

- **Expired token**: a request presenting a token past its **Expiration date** is rejected with `401 Unauthorized`. The token record is retained for auditing and may be shown in the list as "Expired"; no resource access occurs.
- **Revoked token still presented**: a request with a token whose **Revoked status** is `true` is rejected with `401 Unauthorized`, even if it has not yet expired. Revocation always takes precedence over validity.
- **Token with no permissions**: cannot be created (validation rejects an empty permission set). If a token's referenced permissions are all later removed/disabled (e.g. an extension is uninstalled), every gated request fails with `403 Forbidden` until the token is updated or rotated.
- **Leaked token rotation**: when a secret is exposed, the user rotates it - the system issues a new secret and immediately revokes the old one, so the leaked value stops working at once. Audit logs capture both events.
- **Token outliving its creator's account**: a token remains valid based on its own expiration and revoked status, independent of the creator's account state. However, when the **Created by** user is disabled or deleted, the system must flag such "orphaned" tokens for review and SHOULD support an automatic-revoke policy; permissions never exceed what was granted at creation. Super Admins can revoke or rotate any orphaned token.
- **Cross-tenant access attempt**: a token scoped to tenant A presented against tenant B is rejected with `403 Forbidden` unless cross-tenant access was explicitly granted. See [Multi-Tenancy](multi_tenancy.md).
- **Ambiguous tenant context**: if domain, path, header, and token imply different tenants, resolution follows the documented order and the request is rejected if the token scope does not match the resolved tenant.
- **Hash collision / unknown token**: a presented secret whose hash matches no stored record is rejected with `401 Unauthorized`; the system must not reveal whether a token exists.
- **Concurrent last-used updates**: simultaneous requests with the same token must not corrupt the record; the **Last used date** update must be safe under concurrency and must never block or fail the underlying request.
- **Permission removed from creator after issuance**: existing tokens keep the permissions granted at creation, but administrators can revoke or rotate them; the system must surface tokens whose permissions exceed their (now-reduced) creator's grant for review.

## Acceptance Criteria

- [ ] A user with the `API` permission can create a token with Name, Description, Token duration, Expiration date, and Permissions.
- [ ] The plaintext token is displayed exactly once at creation and is never retrievable or displayed again.
- [ ] The token secret is generated from a cryptographically secure source and stored only as a salted hash; no plaintext is persisted or logged.
- [ ] Creating a token with an empty permission set is rejected.
- [ ] Creating a token whose permissions are not a subset of the creator's permissions is rejected.
- [ ] Creating a token with a missing Name, or with a non-unique Name within the tenant, is rejected.
- [ ] Selecting `Custom` duration without a future Expiration date is rejected; a past expiration date is rejected.
- [ ] The permission picker lists all built-in permission keys plus custom permissions declared by installed extension manifests.
- [ ] Authenticating with `Authorization: Bearer <token>` succeeds for valid, non-expired, non-revoked tokens with the required permission.
- [ ] A request to an endpoint whose required permission the token lacks returns `403 Forbidden`.
- [ ] An expired token returns `401 Unauthorized` and performs no action.
- [ ] A revoked token returns `401 Unauthorized` even if not yet expired.
- [ ] The **Last used date** updates on each successful authenticated request and starts as `null`.
- [ ] Revoking a token is immediate and irreversible; subsequent requests fail.
- [ ] Rotating a token issues a new secret (shown once) and immediately revokes the old secret.
- [ ] A token scoped to one tenant cannot access another tenant unless explicitly granted, returning `403 Forbidden` otherwise.
- [ ] API requests resolve tenant context from domain, path, header, or token, and reject scope mismatches.
- [ ] Token creation, revocation, and rotation are written to the audit log.
- [ ] Tokens whose **Created by** account is disabled or deleted are flagged for review and can be revoked/rotated by a Super Admin.
- [ ] No API response or log ever exposes a stored token hash or plaintext secret.
