# Multi-Tenancy & Tenant Routing

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

DotNetForge CMS must support running multiple tenants (sites) from a single application and, where possible, a single database. The routing system resolves the **tenant first** (by domain, subdomain, or path prefix), then resolves the page or dynamic route within that tenant's scope, with strict data isolation between tenants.

## Purpose

Multi-tenancy lets one DotNetForge CMS deployment host many independent sites without standing up a separate application instance per site. Each tenant owns its own scoped data (pages, content, media, users, roles, settings, themes, layouts, menus, API tokens, webhooks, extensions, dynamic routes, audit logs, content history, and import/export), while a single codebase, runtime, and (where possible) database are shared.

The module is responsible for:

- Resolving the active tenant for every incoming frontend request, admin request, and API request.
- Scoping all tenant-owned data to the resolved tenant and preventing any cross-tenant data leakage.
- Distinguishing global **Super Admin** access (all tenants) from **Tenant Admin** access (assigned tenants only).
- Providing tenant context switching and an active-tenant indicator in the admin area.
- Providing a deterministic routing order so that tenant resolution always precedes page and route resolution.

This file owns tenant resolution, the tenant data model, the routing order, tenant-aware admin/API behavior, and tenant-related edge cases. Dynamic route patterns themselves (the `[parameterName]` bracket syntax, route fields, and conflict prevention) are owned by [Dynamic Routes](dynamic_routes.md); permission semantics for the canonical roles are owned by [User Roles & Permissions](user_roles_permissions.md).

## Main Features

- **Tenant resolution** by:
  - **Domain** - e.g. `example.com`.
  - **Subdomain** - e.g. `tenant1.example.com`.
  - **Path prefix** - e.g. `example.com/tenant1/...`.
  - **Dynamic tenant route parameter** - `[tenantSlug]`, e.g. `example.com/[tenantSlug]/[pageName]`.
- **Per-tenant scoping** of all tenant-owned data where applicable: pages, content, media, users, roles, settings, themes, layouts, menus, API tokens, webhooks, extensions, dynamic routes, audit logs, content history, and import/export.
- **Strict data isolation** - no data may leak between tenants under any resolution path or query.
- **Tenant-specific** themes, layouts, menus, settings, media libraries, API tokens, permissions and roles, and dynamic routes.
- **Shared extensions** that can be enabled or disabled per tenant (tenant-aware extension enablement).
- **Global Super Admin** access across all tenants vs. **Tenant Admins** limited to their assigned tenants.
- **Tenant-aware admin** with tenant context switching and a visible active-tenant indicator.
- **Tenant-aware API** that resolves tenant context from domain, path, header, or token; tokens cannot cross tenants unless explicitly granted.
- **Default tenant fallback** when no tenant matches.
- **Tenant lifecycle status**: active, disabled, archived.
- **Deterministic routing order** that resolves the tenant before any page or route.

## Data Model / Fields

### Tenant

Reproduce every tenant field exactly. Subdomains is a list (zero or more); all other fields are single-valued.

| Field | Type | Required | Description |
|---|---|---|---|
| Tenant ID | GUID / string | Yes | Immutable unique identifier for the tenant. System-assigned. |
| Tenant name | string | Yes | Human-readable display name shown in the admin tenant switcher. |
| Tenant slug | string | Yes | URL-safe identifier used for path-prefix and `[tenantSlug]` resolution. Globally unique. |
| Primary domain | string (hostname) | No | The primary domain that resolves to this tenant (e.g. `example.com`). Globally unique when set. |
| Subdomains | list of strings (hostnames) | No | Additional subdomains mapped to this tenant (e.g. `tenant1.example.com`). Each must be globally unique. |
| Path prefix | string | No | URL path segment that resolves this tenant (e.g. `tenant1`). Globally unique when set. |
| Default locale | string (locale code) | Yes | The tenant's default locale. See [Internationalization](internationalization.md). |
| Default theme | string (theme id) | Yes | The tenant's default public theme. See [Themes](themes.md). |
| Status | enum: `active` \| `disabled` \| `archived` | Yes | Tenant lifecycle state. Defaults to `active`. |
| Created date | datetime | Yes | Timestamp when the tenant was created. System-assigned. |
| Updated date | datetime | Yes | Timestamp of the last tenant configuration change. System-maintained. |

### Tenant-scoped entities

The following entities carry a tenant reference and must be filtered by the resolved tenant in every read and write path:

- Pages - see [Content Manager](content_manager.md)
- Content / content history - see [Content Manager](content_manager.md) and [Content History](content_manager.md)
- Media - see [File Manager](file_manager.md)
- Users and Roles - see [User Roles & Permissions](user_roles_permissions.md)
- Settings - see [Settings Overview](settings.md)
- Themes, Layouts, Menus - see [Themes](themes.md)
- API tokens - see [API Tokens](api_tokens.md)
- Webhooks - see [Webhooks](webhooks.md)
- Extensions (shared, enabled/disabled per tenant) - see [Extension System](extensions.md)
- Dynamic routes - see [Dynamic Routes](dynamic_routes.md)
- Audit logs - see [Audit Logs](audit_logs.md)
- Import / export - see [Transfer](transfer_updates.md)

## Routing

The routing system must resolve the tenant **first**, then resolve the page or dynamic route inside that tenant's scope.

### Routing examples

```text
example.com/about
tenant1.example.com/about
example.com/tenant1/about
example.com/[tenantSlug]/[pageName]
```

### Routing order

The system must evaluate routes in this exact order. Each step runs only after the previous step has completed; tenant resolution always precedes any page or route resolution.

1. **Resolve tenant.** Determine the active tenant from domain, subdomain, path prefix, or `[tenantSlug]` parameter. If none matches, apply the default tenant fallback.
2. **Resolve static CMS page.** Attempt to match a static page within the resolved tenant's scope.
3. **Resolve dynamic CMS route.** If no static page matches, attempt to match a tenant-scoped dynamic route (see [Dynamic Routes](dynamic_routes.md)).
4. **Resolve extension route.** If no dynamic route matches, attempt to match an extension-registered route enabled for the tenant (see [Extension System](extensions.md)).
5. **Resolve fallback / 404 page.** If nothing matches, serve the tenant's 404 / fallback page.

### Resolution precedence (within step 1)

When more than one resolution strategy could apply to a single request, the system must apply a single deterministic precedence and never resolve to two tenants. Recommended precedence: explicit **path prefix / `[tenantSlug]`** match, then **subdomain** match, then **primary domain** match. If the configured mappings are ambiguous (the same host or prefix maps to more than one tenant), see [Edge Cases](#edge-cases).

## User Flows

### Flow: Resolve a tenant for an incoming request

1. The system receives a request with a host (domain/subdomain) and path.
2. The system checks for a path prefix or `[tenantSlug]` segment that matches a tenant slug or path prefix.
3. If no path match, the system checks the host against tenant primary domains and subdomains.
4. If a tenant is matched, the system sets it as the active tenant for the request.
5. If no tenant is matched, the system applies the default tenant fallback.
6. The system reads the resolved tenant's status:
   - `active` - continue to step 2 of the routing order.
   - `disabled` or `archived` - short-circuit and serve the disabled/archived response (see [Edge Cases](#edge-cases)).
7. All subsequent data access for the request is scoped to the resolved tenant.

### Flow: Switch tenant context in the admin area

1. An authenticated admin user opens the admin area.
2. The admin UI displays the active-tenant indicator showing the current tenant context.
3. The user opens the tenant switcher.
4. The system lists tenants the user is authorized to manage:
   - **Super Admin** - all tenants.
   - **Tenant Admin** - only assigned tenants.
5. The user selects a target tenant.
6. The system sets the selected tenant as the active admin context.
7. All admin pages (content, media, users, roles, settings, themes, layouts, menus, extensions, API tokens, webhooks, audit logs, content history, import/export) are filtered to the active tenant.
8. The active-tenant indicator updates to the newly selected tenant.

### Flow: Resolve tenant context for an API request

1. The API receives a request.
2. The system resolves tenant context, in order of availability, from: the request **domain**, the request **path**, an explicit tenant **header**, or the **API token**.
3. The system validates that the presented API token is permitted to act on the resolved tenant.
4. If the token's tenant scope does not include the resolved tenant and no cross-tenant grant exists, the request is rejected (see [Edge Cases](#edge-cases)).
5. If the token is valid for the tenant, the request proceeds with all data access scoped to that tenant.

### Flow: Apply default tenant fallback

1. Tenant resolution (Flow above) finds no matching domain, subdomain, path prefix, or `[tenantSlug]`.
2. The system selects the configured default tenant.
3. If the default tenant is `active`, the request proceeds within its scope.
4. If no default tenant is configured or the default tenant is not `active`, the system serves the fallback / 404 response.

### Flow: Change tenant status

1. A Super Admin opens the tenant configuration.
2. The Super Admin sets the status to `active`, `disabled`, or `archived`.
3. The system records the change and updates the **Updated date**.
4. Subsequent requests resolving to that tenant honor the new status during tenant resolution (step 1 of the routing order).

## Role & Permission Rules

Roles below are the canonical DotNetForge roles. Tenant management is a global concern: creating, configuring, and switching across tenants is reserved for the global Super Admin, while Tenant Admins operate only within their assigned tenants. See [User Roles & Permissions](user_roles_permissions.md) for full permission semantics.

| Action | Super Admin (global) | Admin (Tenant Admin, assigned tenants) | Editor | Author | Authenticated | Public |
|---|---|---|---|---|---|---|
| View / manage **all** tenants | Yes | No | No | No | No | No |
| Create / delete tenants | Yes | No | No | No | No | No |
| Edit tenant configuration (domains, slug, prefix, theme, locale, status) | Yes | Assigned tenants only | No | No | No | No |
| Switch active tenant context | Yes (any tenant) | Yes (assigned tenants only) | No | No | No | No |
| Manage tenant-scoped content/media/settings | Yes | Assigned tenants only | Within active tenant per role | Within active tenant per role | No | No |
| Grant a token cross-tenant access | Yes | No | No | No | No | No |
| Access frontend pages of a tenant | Yes | Yes | Yes | Yes | Yes | Yes (public pages only) |

Notes:

- "Admin" in the table denotes a Tenant Admin: the canonical Admin role scoped to one or more assigned tenants. A Tenant Admin must never see, query, or modify data belonging to a tenant they are not assigned to.
- Editors and Authors act only within the active tenant context and only within the permissions granted by their role for that tenant.

## Validation Rules

- **Tenant slug** is required, must be URL-safe, and must be globally unique.
- **Tenant name** is required.
- **Primary domain**, each **subdomain**, and **path prefix** must each resolve to at most one tenant. The system must reject configuration that maps the same host or path prefix to more than one tenant.
- **Default locale** is required and must reference a valid, enabled locale (see [Internationalization](internationalization.md)).
- **Default theme** is required and must reference an installed theme available to the tenant (see [Themes](themes.md)).
- **Status** must be one of `active`, `disabled`, or `archived`; it defaults to `active` on creation.
- **Tenant ID**, **Created date**, and **Updated date** are system-assigned/maintained and must not be set by clients.
- Every tenant-scoped query (read and write) must include the active tenant as a filter; queries without a tenant filter on tenant-scoped entities must be treated as a defect and rejected.
- An API token must be validated against the resolved tenant before any tenant-scoped operation; a token without an explicit cross-tenant grant must only operate within its own tenant.
- A request resolving to a `disabled` or `archived` tenant must not be served normal content.
- Tenant resolution must be deterministic: a single request must never resolve to more than one tenant.

## Edge Cases

- **Ambiguous domain / prefix mapping.** If a host, subdomain, or path prefix maps to more than one tenant, the system must not guess. It must apply the configured resolution precedence deterministically; if the configuration itself is genuinely ambiguous (duplicate mappings), it must reject the offending configuration at save time and, at request time, fail safe (serve the fallback / 404 rather than leak across tenants).
- **Request to a disabled tenant.** Tenant resolves but status is `disabled`. The system must not serve tenant content; it serves a disabled-tenant response (e.g. 404 or a configured unavailable page) and does not fall through to another tenant.
- **Request to an archived tenant.** Treated as unavailable. Archived tenants are retained for data/history but are not served on the public frontend; requests resolve to an unavailable / 404 response.
- **Token used against the wrong tenant.** An API token presented for a tenant it is not scoped to (and lacks an explicit cross-tenant grant) must be rejected; the request must not be silently re-scoped to the token's own tenant in a way that exposes the wrong data, and it must never return another tenant's data.
- **Missing tenant.** If no domain, subdomain, path prefix, or `[tenantSlug]` matches, the system applies the **default tenant fallback**. If no default tenant is configured or it is not `active`, the system serves the fallback / 404 page.
- **Cross-tenant data access attempt.** Any query, filter bypass, or identifier reuse that would return another tenant's data must be blocked. Direct access to a tenant-scoped entity by ID that belongs to a different tenant than the active one must behave as "not found".
- **Tenant context drift during admin session.** If an admin's assignment to the active tenant is revoked mid-session, the next admin action must re-evaluate authorization and remove access to that tenant context.
- **Conflict between static page and dynamic route within a tenant.** Resolved per the routing order (static page before dynamic route before extension route); cross-tenant conflicts cannot occur because resolution is always scoped to a single tenant. See [Dynamic Routes](dynamic_routes.md) for intra-tenant conflict handling.
- **Import/export across tenants.** Import and export are tenant-aware; data must be scoped to the active tenant and must not bleed into or pull from other tenants. See [Transfer](transfer_updates.md).

## Acceptance Criteria

- [ ] The system resolves the active tenant by domain, subdomain, path prefix, and `[tenantSlug]` parameter.
- [ ] Tenant resolution always runs before page or route resolution.
- [ ] The routing order is enforced exactly: (1) resolve tenant, (2) static CMS page, (3) dynamic CMS route, (4) extension route, (5) fallback / 404.
- [ ] All tenant-scoped entities - pages, content, media, users, roles, settings, themes, layouts, menus, API tokens, webhooks, extensions, dynamic routes, audit logs, content history, import/export - are filtered by the active tenant.
- [ ] No request, query, or API call returns data belonging to a tenant other than the resolved/active one (strict data isolation verified).
- [ ] A global Super Admin can view and manage all tenants.
- [ ] A Tenant Admin can manage only assigned tenants and cannot access unassigned tenants.
- [ ] The admin area exposes a tenant context switch limited to tenants the user may manage.
- [ ] Admin pages display a clear active-tenant indicator that reflects the current context.
- [ ] API requests resolve tenant context from domain, path, header, or token.
- [ ] An API token cannot access another tenant unless explicitly granted.
- [ ] A request that matches no tenant falls back to the configured default tenant.
- [ ] If no default tenant is configured or it is not active, the system serves the fallback / 404 response.
- [ ] Tenant status supports `active`, `disabled`, and `archived`, and requests to disabled or archived tenants are not served normal content.
- [ ] Ambiguous domain/prefix mappings are rejected at configuration time and fail safe at request time.
- [ ] A token used against the wrong tenant is rejected and never returns another tenant's data.
- [ ] Every tenant field (Tenant ID, name, slug, primary domain, subdomains, path prefix, default locale, default theme, status, created date, updated date) is persisted and editable per the validation rules.
- [ ] Tenant slug, primary domain, subdomains, and path prefix are each globally unique and resolve to at most one tenant.
- [ ] Default locale references a valid enabled locale and default theme references an installed theme.
