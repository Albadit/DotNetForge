# Multi-tenancy (current state)

The data model is tenant-aware, but the running application behaves as a **single-tenant** CMS. This document
records exactly what is and isn't tenant-scoped so changes don't assume isolation that doesn't exist. The target
design is in [Planned](#planned-not-implemented) below.

## What exists

- `Tenant` entity with slug, domain, subdomain and path-prefix fields - none are read at runtime.
- `TenantId` on `User`, `Role`, `Page`, `MediaFile`, `ApiToken`, `Webhook`, `Setting` (nullable = global) and
  `AuditLogEntry` (nullable).
- `DataSeeder` creates exactly one tenant: slug `default`, name `Default`.
- The **active tenant** is a claim, `dnf:tenant`:
  - admin: put in the cookie by `AuthService` from `User.TenantId`, read by `AdminControllerBase.TenantId`;
  - API: put in the token principal from `ApiToken.TenantId`, read by `ApiControllerBase.TenantId`.
  Both return `Guid.Empty` if the claim is missing or malformed.

## What does not exist

- **No tenant resolution** from host, subdomain, path or header. Sign-in and setup always use the oldest tenant
  (`AuthService.GetDefaultTenantIdAsync`: `Tenants.OrderBy(t => t.CreatedDate)`).
- **No tenant switcher.** `_AdminLayout` shows a static "Tenant: **Default**" label.
- **No global query filters.** Each query adds `Where(x => x.TenantId == TenantId)` by hand.

## Scoping per screen / endpoint

| Code | Tenant-scoped? |
| --- | --- |
| Content Manager (incl. the parent check on create and reorder validation), Media (list, upload, delete), Settings (tenant rows + global rows), Roles, Users, API Tokens | yes |
| Dashboard | Users, Roles, Pages, Media, API tokens, Webhooks: yes; Audit entries: yes (active tenant + entries without a tenant); **Extensions: global count** |
| Audit Logs screen | yes - `TenantId == active || TenantId == null` (tenant-less entries are failed sign-ins) |
| Audit Dashboard extension | **no** - shows entries of all tenants |
| Media downloads `GET /media/{id}/{fileName?}` (`MediaFilesController`) | public files: **no** - anyone with the link; private files: yes - only admin-capable users whose `dnf:tenant` is the file's tenant, everyone else gets 404 |
| Stored media objects | keys start with the tenant id (`{tenantId:N}/{yyyy}/{MM}/...`) - [media storage](media-storage.md#storage-architecture) |
| Plugins | n/a (extensions are global) |
| Public site (`HomeController.Index`, `RenderPage`) | **no** - live pages of all tenants are considered |
| `ScheduledPublishingService` | **no** (intentionally global) |
| All `/api/*` endpoints | yes (token's tenant); `/api/settings` also returns global rows; `/api/extensions` is global |
| `ApiTokenAuthenticationHandler` prefix lookup | global (prefixes are random) |

## Rules for new code

- Always filter tenant-scoped tables by the active tenant from the base controller.
- Never take a tenant id from request input (query, form, body).
- If you add a second tenant or tenant resolution, revisit every "no" row above - the public site and the Audit
  Dashboard extension would leak across tenants, and tenant-less audit entries would show in every tenant.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

- One deployment (one codebase, runtime and, where possible, database) hosts many tenants with **strict data
  isolation**: no request, query or resolution path returns another tenant's data.
- The active tenant is resolved for **every** public, admin and API request, by:

  | Strategy | Example |
  | --- | --- |
  | Primary domain | `example.com` |
  | Subdomain | `tenant1.example.com` |
  | Path prefix | `example.com/tenant1/...` |
  | `[tenantSlug]` route parameter | `example.com/[tenantSlug]/[pageName]` |

- Tenant-specific themes, layouts, menus, settings, media libraries, API tokens, roles and permissions, and dynamic
  routes. Extensions are shared but enabled/disabled per tenant.
- Global `Super Admin` (all tenants) vs. **tenant admin** (the `Admin` role limited to assigned tenants). Today a
  user belongs to exactly one tenant (`User.TenantId`); there is no user-to-tenant assignment.
- Tenant-aware admin: active-tenant indicator and [tenant switcher](#admin-tenant-switcher).
- Tenant-aware API: tenant from domain, path, header or token; tokens never cross tenants unless explicitly granted.
- Default-tenant fallback when nothing matches; lifecycle status `active` / `disabled` / `archived`.
- No admin screen or API exists to create, edit or delete tenants.

### Tenant model

| Spec field | `Tenant` property | Rules | Today |
| --- | --- | --- | --- |
| Tenant ID | `Id` (`Guid`) | immutable, system-assigned | ✔ |
| Tenant name | `Name` (required, max 200) | required; shown in the switcher | ✔ stored |
| Tenant slug | `Slug` (required, max 100, unique index) | URL-safe, globally unique; used for path prefix / `[tenantSlug]` | ✔ stored + unique; URL-safety not validated |
| Primary domain | `PrimaryDomain` | optional hostname, globally unique when set | stored, no uniqueness |
| Subdomains | `Subdomains` (comma-separated string) | 0..n hostnames, each globally unique | stored, no uniqueness |
| Path prefix | `PathPrefix` | optional, globally unique when set | stored, no uniqueness |
| Default locale | `DefaultLocale` (default `en`) | required; a valid, enabled locale ([internationalization](internationalization.md)) | stored, not validated |
| Default theme | `DefaultTheme` (default `dotnetforge.theme.default`) | required; an installed theme available to the tenant ([themes](themes.md)) | stored, not validated |
| Status | `Status` (`TenantStatus.Active`/`Disabled`/`Archived`) | defaults to `active` | ✔ stored (default `Active`); never read |
| Created date | `CreatedDate` | system-assigned | ✔ |
| Updated date | `UpdatedDate` | system-maintained on every configuration change | set on creation only |

`Id`, `CreatedDate` and `UpdatedDate` must never be set by clients.

### Tenant-scoped entities

Every read and write path filters these by the active tenant:

| Entity (spec) | Code today |
| --- | --- |
| Pages, dynamic routes | ✔ `Page.TenantId` (dynamic routes are `[name]` pages) - public resolution ignores it |
| Content entries, content history | no entities |
| Media | ✔ `MediaFile.TenantId`; storage keys prefixed with the tenant id; private downloads check it |
| Users, roles | ✔ `User.TenantId`, `Role.TenantId` |
| Settings | ✔ `Setting.TenantId` (null = global) |
| Themes, layouts, menus | no entities (only `Tenant.DefaultTheme`) |
| API tokens, webhooks | ✔ `ApiToken.TenantId`, `Webhook.TenantId` |
| Extensions (shared, per-tenant enablement) | `InstalledExtension` has no `TenantId` |
| Audit logs | ✔ `AuditLogEntry.TenantId` (nullable) - screen and Dashboard count scoped; Audit Dashboard extension not |
| Import / export | not built ([transfer and updates](transfer-and-updates.md)) |

### Routing order

Each step runs only after the previous one; the tenant is always resolved first.

1. **Resolve tenant** (domain, subdomain, path prefix or `[tenantSlug]`); no match → default-tenant fallback.
2. **Static CMS page** within that tenant.
3. **Dynamic CMS route** within that tenant.
4. **Extension route** enabled for that tenant.
5. **Tenant 404 / fallback page.**

Today there is no step 1, steps 2-3 are one walk over the live pages of **all** tenants in `HomeController.RenderPage`
(exact slug before a `[name]` sibling, per segment), step 4 does not exist and step 5 is an empty `NotFound()` - see
[public resolution](content-pages-and-routing.md#public-resolution).

**Precedence inside step 1:** path prefix / `[tenantSlug]` → subdomain → primary domain. A request never resolves to
more than one tenant.

### Admin tenant switcher

Owned here; the admin shell only places it ([page architecture](../architecture/pages.md#planned-not-implemented)).

- Shown in the admin chrome on every admin screen with the **active tenant** clearly displayed (today: the static
  "Tenant: **Default**" block, `.tenant-switcher` in `_AdminLayout`).
- Offers switching only to users authorized for more than one tenant: `Super Admin` → all tenants; tenant admin →
  assigned tenants only; never an unassigned tenant.
- Selecting a tenant sets the active admin context and reloads the current screen filtered to it: content, media,
  users, roles, settings, themes, layouts, menus, extensions, API tokens, webhooks, audit logs, content history,
  import/export. The indicator updates.
- Today the active tenant is the `dnf:tenant` claim written once at sign-in from `User.TenantId`. A switch must
  re-check the user's assignment, and the assignment must be re-checked on every request (revocation mid-session).

### Permissions

| Action | `Super Admin` (global) | `Admin` (tenant admin) | `Editor` / `Author` | `Authenticated` | `Public` |
| --- | :-: | :-: | :-: | :-: | :-: |
| View / manage all tenants | ✔ | ✘ | ✘ | ✘ | ✘ |
| Create / delete tenants | ✔ | ✘ | ✘ | ✘ | ✘ |
| Edit tenant configuration (domains, slug, prefix, theme, locale, status) | ✔ | assigned tenants | ✘ | ✘ | ✘ |
| Switch active tenant | any tenant | assigned tenants | ✘ | ✘ | ✘ |
| Manage tenant-scoped content/media/settings | ✔ | assigned tenants | within active tenant, per role | ✘ | ✘ |
| Grant a token cross-tenant access | ✔ | ✘ | ✘ | ✘ | ✘ |
| Access a tenant's public pages | ✔ | ✔ | ✔ | ✔ | public pages only |

A tenant admin never sees, queries or modifies data of an unassigned tenant. Role semantics:
[authorization](authorization.md).

### User flows

| Flow | Steps |
| --- | --- |
| Resolve tenant for a request | match path prefix / `[tenantSlug]` → else match host against primary domains and subdomains → else default tenant → check status: `active` continues to step 2 of the routing order; `disabled`/`archived` short-circuits to the unavailable response → all data access scoped to that tenant |
| API request | resolve tenant from domain, path, tenant header or API token (in that order of availability) → verify the token may act on that tenant → no scope and no cross-tenant grant → reject; otherwise proceed scoped to the tenant |
| Default-tenant fallback | nothing matched → configured default tenant → `active` → proceed; none configured or not `active` → fallback / 404 |
| Change tenant status | `Super Admin` opens tenant configuration → sets `active`/`disabled`/`archived` → change recorded, `UpdatedDate` updated → later requests honour it in step 1 |
| Switch tenant (admin) | see [admin tenant switcher](#admin-tenant-switcher) |

### Rules and validation

- Slug: required, URL-safe, globally unique. Name: required.
- Primary domain, each subdomain and path prefix map to at most one tenant; configuration mapping the same host or
  prefix to two tenants is rejected.
- Default locale required and must be a valid, enabled locale; default theme required and must be installed and
  available to the tenant.
- Status is `active`, `disabled` or `archived`; defaults to `active` ✔ (`Tenant.Status`).
- Every tenant-scoped query (read and write) filters by the active tenant; a query without that filter is a defect.
  Today this is done by hand per query (no global query filters).
- An API token is validated against the resolved tenant before any tenant-scoped operation; without an explicit
  cross-tenant grant it only works in its own tenant.
- A request resolving to a `disabled` or `archived` tenant is never served normal content.
- Resolution is deterministic: one request, at most one tenant.

### Edge cases

| Case | Target behaviour |
| --- | --- |
| Ambiguous domain / prefix mapping | Apply the precedence deterministically; duplicate mappings are rejected at save time and fail safe at request time (fallback / 404, never another tenant's data). |
| Disabled tenant | Not served; disabled response (404 or a configured unavailable page); no fall-through to another tenant. |
| Archived tenant | Data and history kept; public requests get the unavailable / 404 response. |
| Token used against the wrong tenant | Rejected; never silently re-scoped in a way that exposes the wrong data; never returns another tenant's data. |
| No tenant matches | Default-tenant fallback; none configured or not `active` → fallback / 404. |
| Cross-tenant access by id | An entity id of another tenant behaves as "not found". |
| Assignment revoked mid-session | Next admin action re-evaluates and removes access to that tenant context. |
| Static page vs. dynamic route in one tenant | Routing order decides (static → dynamic → extension); cross-tenant conflicts cannot occur. |
| Import / export | Scoped to the active tenant; never pulls from or writes into another tenant. |

### Acceptance criteria

- [ ] The active tenant is resolved by domain, subdomain, path prefix and `[tenantSlug]`.
- [ ] Tenant resolution always runs before page or route resolution.
- [ ] The routing order is enforced exactly: tenant → static page → dynamic route → extension route → fallback / 404.
- [ ] Every tenant-scoped entity (pages, content, media, users, roles, settings, themes, layouts, menus, API tokens,
  webhooks, extensions, dynamic routes, audit logs, content history, import/export) is filtered by the active tenant -
  partly: see [scoping per screen](#scoping-per-screen--endpoint).
- [ ] No request, query or API call returns another tenant's data - the public site and the Audit Dashboard extension
  consider all tenants (Audit Logs ✔ and private media ✔ are scoped).
- [ ] A global `Super Admin` can view and manage all tenants.
- [ ] A tenant admin can manage only assigned tenants.
- [ ] The admin area offers a tenant switch limited to tenants the user may manage.
- [ ] Admin screens show an active-tenant indicator that reflects the current context - static "Default" label.
- [ ] API requests resolve the tenant from domain, path, header or token - token only today.
- [x] An API token cannot access another tenant unless explicitly granted - `ApiControllerBase.TenantId` comes only
  from the token's `dnf:tenant` claim (`ApiTokenAuthenticationHandler`); no grant mechanism exists.
- [ ] A request matching no tenant falls back to the configured default tenant - sign-in and setup use the oldest
  tenant; there is no resolution to fall back from.
- [ ] With no default tenant configured or an inactive one, the fallback / 404 response is served.
- [ ] Status supports `active`, `disabled`, `archived`, and disabled/archived tenants are not served - the enum
  exists, nothing reads it.
- [ ] Ambiguous domain/prefix mappings are rejected at configuration time and fail safe at request time.
- [ ] A token used against the wrong tenant is rejected and never returns another tenant's data.
- [ ] Every tenant field is persisted and editable per the rules - persisted ✔ (`Tenant`), not editable anywhere.
- [ ] Slug, primary domain, subdomains and path prefix are each globally unique - only `Slug` has a unique index.
- [ ] Default locale references a valid enabled locale and default theme an installed theme.
