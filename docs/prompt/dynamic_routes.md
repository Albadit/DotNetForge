# Dynamic Routes

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

Dynamic Routes let users and developers define pattern-based page routes using bracket-style parameters (e.g. `/blog/[slug]`), resolve those parameters at runtime, and map each route to a CMS page, module, controller, or extension-provided handler with full SEO, preview, draft/published, permission, and content-history support. Routes are always resolved **inside a tenant's scope** - see [Multi-Tenancy](multi_tenancy.md) for tenant resolution.

---

## Purpose

DotNetForge CMS must support dynamic page routing similar to modern frontend frameworks such as React, Next.js, Nuxt, and Vue. The system must let users and developers define dynamic routes using bracket-style route parameters, extract parameter values from incoming URLs at runtime, and resolve them to the correct content, module, controller, or extension handler.

Dynamic routing complements - but does not replace - the static, tree-based pages owned by the [Content Manager](content_manager.md). Static pages handle fixed URLs; dynamic routes handle parameterized, pattern-matched URLs. The routing engine must reconcile both so that they never conflict.

This module owns the **dynamic route engine, the dynamic-route data model, and the route-resolution / priority / fallback rules**. Tenant resolution, tenant-scoped route storage, and tenant context switching are owned by [Multi-Tenancy](multi_tenancy.md); this file references them but does not redefine them.

---

## Main Features

The dynamic routing system must:

- Support route parameters using `[parameterName]` (bracket) syntax.
- Resolve dynamic route values at runtime by matching an incoming URL against stored route patterns.
- Allow dynamic routes to map to CMS pages, modules, controllers, or extension-provided handlers.
- Support page templates for dynamic routes (page title, meta title, meta description, SEO keywords, canonical URL).
- Support SEO metadata for dynamically resolved pages, generated from templates with extracted parameter values.
- Support **preview mode** for dynamic pages.
- Support **draft and published** versions for dynamic routes.
- Support **permissions** for dynamic routes (role-based and user-based; public/private).
- Support public and private dynamic pages.
- Support **content history and rollback** for dynamic route pages (see [Content History](content_manager.md)).
- Expose an **API** to create, update, delete, and resolve dynamic routes (see [API & Tokens](api_tokens.md)).
- Prevent route conflicts between static pages and dynamic pages.
- Validate route patterns before saving.
- Prevent unsafe or ambiguous route patterns.
- Support **route priority and fallback** rules.
- Allow extensions to register custom dynamic route handlers (see [Extension System](extensions.md)).
- Resolve every route within the active tenant's scope (see [Multi-Tenancy](multi_tenancy.md)).

### Route pattern examples

```text
/blog/[slug]
/products/[category]/[productSlug]
/docs/[section]/[pageName]
/users/[username]
/[tenantSlug]/[pageName]
```

### Parameter extraction example

A request to the resolved URL:

```text
/blog/my-first-post
```

matched against the pattern `/blog/[slug]` must produce the extracted parameter set:

```json
{
  "slug": "my-first-post"
}
```

The CMS must use the extracted values to resolve the correct content, module, or extension handler, and to render the templated title, meta, and canonical fields.

> **Note on `/[tenantSlug]/[pageName]`:** the `[tenantSlug]` segment is consumed during **tenant resolution**, which runs *before* dynamic route resolution. By the time a dynamic route matches `[pageName]`, the tenant is already established. See [Multi-Tenancy](multi_tenancy.md).

---

## Data Model / Fields

Each dynamic route is stored as a record with the following fields. All routes are scoped to a tenant (`Tenant ID`, owned by [Multi-Tenancy](multi_tenancy.md)) so that two tenants may safely register the same pattern.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| Route pattern | string | Yes | The bracket-style pattern, e.g. `/blog/[slug]`. Must be unique within a tenant. |
| Route name | string | Yes | Stable, human/developer-friendly identifier for the route (used for reverse URL generation and references). Unique within a tenant. |
| Page title template | string | No | Template for the rendered `<title>`. May reference extracted params, e.g. `{slug} - Blog`. |
| Meta title template | string | No | Template for the SEO meta title. May reference extracted params. |
| Meta description template | string | No | Template for the SEO meta description. May reference extracted params. |
| SEO keywords template | string | No | Template for SEO keywords. May reference extracted params. |
| Canonical URL template | string | No | Template for the canonical URL of the resolved page, e.g. `https://{host}/blog/{slug}`. |
| Target content type | string | Conditional | The content type to resolve against (e.g. `blogPost`). Required when the route maps to CMS content. |
| Target module | string | Conditional | The module that renders the route. Required when the route maps to a module. |
| Target handler | string | Conditional | The controller action or extension-registered handler that resolves the route. Required when the route maps to a handler. |
| Theme | string | No | Theme used to render the resolved page. Falls back to the tenant/site default theme. Never affects the admin area. |
| Layout | string | No | Layout used within the selected theme. Falls back to the theme/page default. |
| Enabled | boolean | Yes | Whether the route participates in resolution at all. Disabled routes are skipped. Default `false` until configured. |
| Published | boolean | Yes | Whether the published version is publicly resolvable. Unpublished routes resolve only in preview/draft for authorized users. |
| Sort order | integer | No | Ordering hint for admin listing and as a deterministic tiebreaker (see Priority). Default `0`. |
| Priority | integer | Yes | Resolution priority. Higher priority routes are evaluated before lower priority routes when multiple patterns could match. Default `0`. |
| Access permissions | object | Yes | Roles and users permitted to view the resolved page, plus a public/private flag. Defaults to private until configured. |

### Target binding rule

Exactly **one** primary target must be set per route: `Target content type`, `Target module`, **or** `Target handler`. `Theme` and `Layout` are optional refinements. A route with zero targets, or with more than one conflicting target, must fail validation.

### Access permissions sub-fields

| Sub-field | Type | Required | Description |
| --- | --- | --- | --- |
| isPublic | boolean | Yes | If `true`, anonymous (Public role) visitors may view the resolved page. If `false`, the page is private. |
| allowedRoles | string[] | No | Roles permitted to view the resolved page when `isPublic` is `false`. |
| allowedUsers | string[] | No | Specific users permitted to view the resolved page. |

---

## Route Templates

Template fields (`Page title template`, `Meta title template`, `Meta description template`, `SEO keywords template`, `Canonical URL template`) are evaluated after parameter extraction. They may interpolate:

- Any extracted route parameter, e.g. `{slug}`, `{category}`, `{productSlug}`.
- The resolved content's own SEO fields (when a `Target content type` resolves to an entry).
- The request host (for canonical URL generation), e.g. `{host}`.

**Precedence:** if a route resolves to a content entry that already defines its own SEO fields (see [Content Manager](content_manager.md)), the route templates provide the **default** that the entry's own values override. If no entry-level value exists, the rendered template value is used.

---

## User Flows

### Flow: Create a dynamic route

1. An authorized user opens the Dynamic Routes admin screen within the active tenant context.
2. The user enters the **Route pattern** (e.g. `/products/[category]/[productSlug]`) and a unique **Route name**.
3. The system validates the pattern (see [Validation Rules](#validation-rules)) and checks for conflicts against static pages and existing dynamic routes in the tenant.
4. The user selects exactly one target: **Target content type**, **Target module**, or **Target handler**.
5. The user optionally sets templates (title/meta/canonical/keywords), **Theme**, **Layout**, **Sort order**, and **Priority**.
6. The user configures **Access permissions** (public/private, allowed roles, allowed users).
7. The user saves. The route is created with **Enabled** and **Published** defaulting to `false` (draft).
8. The system records the creation in the [Audit Logs](audit_logs.md) and creates an initial entry in [Content History](content_manager.md).

### Flow: Resolve a dynamic route at runtime

1. A request arrives. The routing engine resolves the **tenant first** (by domain, subdomain, or path prefix - see [Multi-Tenancy](multi_tenancy.md)).
2. Within the tenant scope, the engine attempts to resolve a **static CMS page** for the path.
3. If no static page matches, the engine evaluates **dynamic routes** in descending **Priority** order (then ascending **Sort order**, then most-specific pattern).
4. On match, the engine extracts parameters (e.g. `{"category":"...","productSlug":"..."}`).
5. The engine checks **Enabled** and **Published** status, then evaluates **Access permissions** for the current user.
6. The engine binds the route to its target (content type / module / handler) and renders templated title, meta, canonical, and keywords.
7. If no dynamic route matches, the engine attempts an **extension route**, then falls back to the **404 page**.

### Flow: Preview a draft dynamic route

1. An authorized user opens a route and requests **Preview**.
2. The system renders the route using the **draft** version, ignoring the **Published** flag, but still applying **Access permissions** for the previewing user.
3. The preview is not resolvable by anonymous/unauthorized visitors.

### Flow: Publish / unpublish a dynamic route

1. An authorized user toggles **Published**.
2. On publish, the published version becomes publicly resolvable (subject to permissions); the action is audit-logged and a content-history version is recorded.
3. On unpublish, the route resolves only in preview/draft for authorized users.

### Flow: Roll back a dynamic route

1. An authorized user opens the route's history (see [Content History](content_manager.md)).
2. The user compares versions and selects a prior version to restore.
3. The system restores the selected version as the current draft (and may republish per the user's choice), and records the rollback in [Audit Logs](audit_logs.md).

### Flow: Manage routes via API

1. A client authenticates with an [API token](api_tokens.md) scoped to the **API** permission area and the target tenant.
2. The client calls create / update / delete / resolve endpoints.
3. The API enforces the same validation, conflict, and permission rules as the admin UI, and rejects cross-tenant access unless explicitly granted. See [API & Tokens](api_tokens.md).

### Flow: Register a dynamic route handler via extension

1. An extension declares a route handler (Module/Plugin/Provider extension type) in its `dotnetforge.extension.json` manifest and `entryPoint` (see [Extension System](extensions.md)).
2. On enable, the CMS registers the handler so it can be selected as a **Target handler** for dynamic routes.
3. Extension-registered routes participate in resolution at the **extension route** stage of the routing order, after static and dynamic CMS routes.

---

## Role & Permission Rules

Managing dynamic routes is governed by the **Settings** / **Collection types** / **API** permission areas and the canonical roles. Tenant scoping additionally applies (see [Multi-Tenancy](multi_tenancy.md)): Tenant Admins may manage routes only within their assigned tenants; global Super Admins may manage routes across all tenants.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Create / edit / delete dynamic routes | Yes | Yes | No | No | No | No |
| Register/select extension route handlers | Yes | Yes (if granted) | No | No | No | No |
| Edit route SEO templates & target binding | Yes | Yes | No | No | No | No |
| Publish / unpublish a dynamic route | Yes | Yes | Yes (own/assigned) | No | No | No |
| Edit draft content for a route | Yes | Yes | Yes | Author (own) | No | No |
| Preview a draft dynamic route | Yes | Yes | Yes | Yes | If granted | No |
| Roll back a dynamic route version | Yes | Yes | Yes | No | No | No |
| Manage routes via API | Yes | Yes (token-scoped) | No | No | No | No |
| View a published public dynamic page | Yes | Yes | Yes | Yes | Yes | Yes (if `isPublic`) |
| View a published private dynamic page | Yes | Yes | Yes | Yes | If in `allowedRoles`/`allowedUsers` | No |

Permission checks must be enforced for **every** admin and API action. See [User Roles & Permissions](user_roles_permissions.md) and [Security](security.md).

---

## Validation Rules

The system must validate **before saving** a route and reject invalid input:

- **Route pattern required.** Must be a non-empty path starting with `/`.
- **Bracket parameters well-formed.** Each parameter must use exactly `[parameterName]` syntax. Brackets must be balanced; no nested brackets; no empty `[]`.
- **Parameter names valid.** Parameter names must match a safe identifier pattern (letters, digits, underscores; must start with a letter). No spaces or reserved/unsafe characters.
- **No duplicate parameter names within a single pattern.** A pattern such as `/[slug]/[slug]` must be rejected (param name collision).
- **Whole segment only.** A bracket parameter must occupy an entire path segment (between slashes). Mixed literal+param within one segment is not supported and must be rejected.
- **Route name required and unique per tenant.** Used for reverse URL generation; must be unique within the tenant.
- **Route pattern unique per tenant.** Two routes in the same tenant must not share an identical pattern.
- **No conflict with a static page slug.** A dynamic pattern that would shadow an existing static page path within the tenant must be rejected (or flagged for explicit priority resolution - see Edge Cases). Static CMS pages always resolve before dynamic routes.
- **Exactly one target.** Exactly one of `Target content type`, `Target module`, or `Target handler` must be set.
- **Target must exist.** The referenced content type, module, or handler must exist and be enabled. Extension handlers must be registered by an enabled extension.
- **No unsafe or ambiguous patterns.** Reject patterns that are ambiguous (cannot be deterministically matched) or unsafe (path traversal, wildcards that could match the admin area, or patterns that could capture reserved admin paths). The admin area must never be reachable through a public dynamic route.
- **Priority and Sort order numeric.** Must be integers.
- **Access permissions valid.** `isPublic` is required; if `false`, the route is private and `allowedRoles`/`allowedUsers` define who may view it. Referenced roles/users must exist.
- **Templates safe.** Template fields must reference only known parameters or known content/host fields; unknown placeholders must be flagged. Rendered template output must be XSS-safe (see [Security](security.md)).
- **Tenant scope required.** Every route must belong to a valid tenant (see [Multi-Tenancy](multi_tenancy.md)).

---

## Edge Cases

- **Overlapping patterns.** When two patterns could match the same URL (e.g. `/blog/[slug]` and `/blog/[category]/[post]`), resolution is deterministic: higher **Priority** wins; on a tie, lower **Sort order**; on a further tie, the more **specific** pattern (more literal segments / fewer parameters) wins. If still ambiguous, validation must have rejected the conflicting pair at save time.
- **Param name collisions.** A single pattern with repeated parameter names (e.g. `/[id]/[id]`) is rejected at validation. Across different routes, identical parameter names are allowed because they are scoped to each route's own extraction.
- **Greedy vs. specific match.** The engine must prefer the **most specific** match over a greedy parameterized one. A request to `/blog/featured` must prefer a static page `/blog/featured` (or a more literal pattern) over the parameterized `/blog/[slug]`.
- **Conflict with a static page slug.** Static CMS pages always take precedence over dynamic routes within a tenant. A dynamic route that would shadow an existing static page is rejected at save time; if a static page is later created that collides with a dynamic pattern, the static page wins at runtime and the collision must be surfaced as a warning in the admin UI.
- **Disabled or unpublished route.** A request matching a disabled (`Enabled = false`) route is skipped entirely; a request matching an unpublished (`Published = false`) route resolves only for authorized preview, otherwise continues to the next resolution stage (extension route, then 404).
- **Missing/deleted target.** If a route's target content type, module, or handler no longer exists or its extension is disabled, resolution must fail gracefully (skip to next stage / 404) and the route should be flagged as broken in the admin UI rather than throwing.
- **Empty parameter value.** A URL that would produce an empty parameter value for a segment must not match; the engine continues to the next route or fallback.
- **No match.** When no static page, dynamic route, or extension route matches, the engine resolves the tenant's fallback / 404 page.
- **Cross-tenant resolution.** A route must never resolve outside its tenant's scope. Tenant is resolved first; an API token must not access another tenant's routes unless explicitly granted (see [Multi-Tenancy](multi_tenancy.md) and [API & Tokens](api_tokens.md)).
- **Race condition on save.** Concurrent creation of two conflicting patterns within a tenant must be serialized so that uniqueness and conflict checks remain authoritative; the second writer must fail validation.
- **Template referencing a missing parameter.** If a template references a parameter not present in the matched pattern, validation must flag it; at runtime a missing value must render safely (empty or default) without leaking errors.

---

## Routing Order

The routing engine must resolve in this exact order (tenant resolution is owned by [Multi-Tenancy](multi_tenancy.md)):

1. **Resolve tenant** (by domain, subdomain, or path prefix).
2. **Resolve static CMS page** within the tenant.
3. **Resolve dynamic CMS route** within the tenant (by Priority, then Sort order, then specificity).
4. **Resolve extension route** (extension-registered handlers).
5. **Resolve fallback / 404 page**.

---

## API

Dynamic routes must be manageable through the API in [Headless and Hybrid modes](README.md), governed by API tokens with the **API** permission area and tenant scope. See [API & Tokens](api_tokens.md) for token, permission, and tenant-resolution detail.

The API must support:

- **Create** a dynamic route (validated identically to the admin UI).
- **Update** a dynamic route.
- **Delete** a dynamic route.
- **Resolve** a route - given a URL, return the matched route, extracted parameters, target binding, and rendered SEO templates, e.g. resolving `/blog/my-first-post` returns `{"slug":"my-first-post"}` plus the resolved target and metadata.

API requests must resolve tenant context from domain, path, header, or token, and must not access another tenant's routes unless explicitly granted.

---

## Acceptance Criteria

- [ ] Routes accept `[parameterName]` bracket syntax and store the pattern, name, templates, target, theme, layout, enabled, published, sort order, priority, and access permissions.
- [ ] A request to `/blog/my-first-post` matched against `/blog/[slug]` extracts `{"slug":"my-first-post"}`.
- [ ] Multi-segment patterns such as `/products/[category]/[productSlug]` and `/docs/[section]/[pageName]` resolve and extract all parameters correctly.
- [ ] Exactly one of Target content type, Target module, or Target handler is enforced per route.
- [ ] Title, meta title, meta description, SEO keywords, and canonical URL templates render with extracted parameter values, and entry-level SEO values override templates.
- [ ] Tenant is resolved first; routes resolve only within the active tenant's scope, and no cross-tenant leakage occurs.
- [ ] Resolution order is: tenant → static page → dynamic route → extension route → 404 fallback.
- [ ] Static pages always take precedence over dynamic routes that would shadow them; a `/blog/featured` static page wins over `/blog/[slug]`.
- [ ] Overlapping patterns resolve deterministically by Priority, then Sort order, then specificity.
- [ ] Patterns with duplicate parameter names (e.g. `/[slug]/[slug]`) are rejected at validation.
- [ ] Invalid, unsafe, or ambiguous patterns (empty `[]`, mixed literal+param segments, path traversal, admin-area capture) are rejected.
- [ ] Route name and route pattern are unique within a tenant; conflicts are rejected at save time.
- [ ] Disabled routes are skipped; unpublished routes resolve only in authorized preview.
- [ ] Preview mode renders draft routes for authorized users without exposing them publicly.
- [ ] Draft and published versions are supported, and publishing/unpublishing is audit-logged.
- [ ] Content history and rollback work for dynamic route pages, including version compare and restore.
- [ ] Access permissions (public/private, allowed roles, allowed users) are enforced on every resolved page.
- [ ] Role/permission rules match the table: Editors+ publish; Admins+ create/edit/delete/manage via API; Public sees only public published pages.
- [ ] The API can create, update, delete, and resolve routes, enforcing the same validation, conflict, permission, and tenant rules as the admin UI.
- [ ] Extensions can register custom route handlers selectable as a Target handler, participating at the extension-route stage.
- [ ] A route with a missing or disabled target fails gracefully (next stage / 404) and is flagged as broken in the admin UI.
- [ ] Concurrent conflicting saves within a tenant are serialized so uniqueness/conflict checks remain authoritative.
- [ ] The admin area is never reachable through a public dynamic route and is never affected by frontend themes.
