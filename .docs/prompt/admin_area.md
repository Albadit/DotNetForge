# Admin Area & Navigation

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Admin Area is the visual back-office of **DotNetForge CMS** where authorized users manage content, media, extensions, and configuration. It exposes a fixed left-hand sidebar that is the canonical navigation tree for every administrative module, and it is never restyled by public frontend themes.

## Purpose

- Provide a single, consistent navigation surface for all administrative functions of DotNetForge CMS across its three operating modes (Traditional, Headless, and Hybrid).
- Render a stable, predictable sidebar tree so that every admin module has exactly one navigation entry point and one owning spec file.
- Guarantee that the admin chrome (sidebar, top bar, layout, login, and setup) is isolated from public frontend [Themes](themes.md) so that a broken or hostile theme can never compromise or restyle the back office.
- Surface the active tenant context and allow authorized users to switch tenants without leaving the admin area. See [Multi-Tenancy](multi_tenancy.md).
- Enforce that only users holding an admin-capable role may reach the admin area at all; everyone else is denied entry before any module loads.

## Main Features

- **Fixed sidebar navigation** organized into a Main group and a Settings group, each with nested sub-groups.
- **Theme isolation**: the admin area uses a dedicated, built-in admin layout. Public frontend themes affect only public-facing CMS pages and never the admin area, the setup page, or (unless explicitly configured) the login page. See [Themes](themes.md).
- **Tenant context switcher** in the admin chrome that displays the active tenant and lets authorized users change scope. All admin modules are filtered by the active tenant. See [Multi-Tenancy](multi_tenancy.md).
- **Cross-linked navigation**: every sidebar item routes to the module owned by a specific sibling spec file (see the tree below).
- **Role-gated entry**: access to the admin area is restricted to admin-capable roles; the sidebar may further hide items the current role lacks permission to use.
- **Active-state indication**: the sidebar highlights the current section and expands the group containing the active item.

## Sidebar Navigation Tree

The admin sidebar must render exactly the following nested structure. Each leaf links to the spec file that owns that module's full behavior, fields, and validation. The system must not add, remove, or reorder these entries without a spec change.

- **Main**
  - [Dashboard](dashboard.md)
  - [Content Manager](content_manager.md)
  - [File Manager](file_manager.md)
  - [Marketplace](extensions.md)
- **Settings**
  - **Global Settings**
    - [Overview](settings.md)
    - [API Tokens](api_tokens.md)
    - [Content History](content_manager.md)
    - [Internationalization](internationalization.md)
    - [File Manager](file_manager.md)
    - [Plugins](extensions.md)
    - [Transfer](transfer_updates.md)
    - [Webhooks](webhooks.md)
  - **Administration Panel**
    - [Roles](user_roles_permissions.md)
    - [Users](user_roles_permissions.md)
    - [Audit Logs](audit_logs.md)
  - **Email**
    - [Configuration](email.md)
    - [Templates](email.md)
  - **Users & Permissions Plugin**
    - [Roles](user_roles_permissions.md)
    - [Providers](authentication.md)
    - [Advanced Settings](authentication.md)

### Navigation map (item → owning spec)

| Group | Sub-group | Item | Owning spec file |
|---|---|---|---|
| Main | - | Dashboard | [dashboard.md](dashboard.md) |
| Main | - | Content Manager | [content_manager.md](content_manager.md) |
| Main | - | File Manager | [file_manager.md](file_manager.md) |
| Main | - | Marketplace | [marketplace.md](extensions.md) |
| Settings | Global Settings | Overview | [overview_settings.md](settings.md) |
| Settings | Global Settings | API Tokens | [api_tokens.md](api_tokens.md) |
| Settings | Global Settings | Content History | [content_history.md](content_manager.md) |
| Settings | Global Settings | Internationalization | [internationalization.md](internationalization.md) |
| Settings | Global Settings | File Manager | [file_manager.md](file_manager.md) |
| Settings | Global Settings | Plugins | [plugins.md](extensions.md) |
| Settings | Global Settings | Transfer | [transfer.md](transfer_updates.md) |
| Settings | Global Settings | Webhooks | [webhooks.md](webhooks.md) |
| Settings | Administration Panel | Roles | [user_roles_permissions.md](user_roles_permissions.md) |
| Settings | Administration Panel | Users | [users.md](user_roles_permissions.md) |
| Settings | Administration Panel | Audit Logs | [audit_logs.md](audit_logs.md) |
| Settings | Email | Configuration | [email_configuration.md](email.md) |
| Settings | Email | Templates | [email_templates.md](email.md) |
| Settings | Users & Permissions Plugin | Roles | [user_roles_permissions.md](user_roles_permissions.md) |
| Settings | Users & Permissions Plugin | Providers | [authentication_providers.md](authentication.md) |
| Settings | Users & Permissions Plugin | Advanced Settings | [advanced_user_settings.md](authentication.md) |

> Note: "Roles" appears under both **Administration Panel** and **Users & Permissions Plugin**, mirroring the source structure. Both entries resolve to the same Roles & Permissions module ([user_roles_permissions.md](user_roles_permissions.md)). The system must not present these as two independent role stores.

## Tenant Context Switcher

The admin chrome must include a tenant context switcher, available to users authorized for more than one tenant.

- The switcher must clearly display the **active tenant context** on every admin page.
- Selecting a tenant changes the active scope; Content, Media, Users, Roles, Settings, Themes, Extensions, API Tokens, Webhooks, and Audit Logs must all be filtered to the selected tenant.
- **Global Super Admins** can view and manage all tenants and may switch to any tenant.
- **Tenant Admins** may switch only between tenants assigned to them.
- Full tenant resolution order, scoping rules, data-isolation guarantees, and tenant fields are owned by [Multi-Tenancy](multi_tenancy.md). This file only specifies the switcher's presence and gating in the admin chrome.

## User Flows

### Flow: Open the admin area

1. The user navigates to the admin URL (for example, `/admin`).
2. The system verifies the CMS is installed; if not, it redirects to the setup flow (see [First-Time Installation](installation_setup.md)).
3. The system requires authentication. If the user is not signed in, it redirects to the login page. See [Authentication Providers](authentication.md) and [Security](security.md).
4. After authentication, the system checks that the user holds an admin-capable role (Super Admin, Admin, Editor, or Author). If not, access is denied with a 403 response.
5. The system resolves the active tenant context (see [Multi-Tenancy](multi_tenancy.md)) and renders the admin layout using the built-in admin theme, never a public frontend theme.
6. The sidebar renders the navigation tree, hiding any item the current role lacks permission to access.

### Flow: Navigate to a module

1. The user expands a Settings sub-group (Global Settings, Administration Panel, Email, or Users & Permissions Plugin) or selects a Main item.
2. The user selects a leaf item (for example, **Webhooks**).
3. The system routes to the owning module (for example, [Webhooks](webhooks.md)) scoped to the active tenant.
4. The sidebar marks the selected item active and keeps its parent group expanded.

### Flow: Switch tenant context

1. The user opens the tenant context switcher in the admin chrome.
2. The system lists tenants the user is authorized to manage (all tenants for Global Super Admins; assigned tenants only for Tenant Admins).
3. The user selects a tenant.
4. The system sets the active tenant context and reloads the current module filtered to that tenant.
5. The admin pages display the newly active tenant context. Detailed scoping behavior is owned by [Multi-Tenancy](multi_tenancy.md).

## Role & Permission Rules

Access to the admin area requires an admin-capable role. The canonical roles, most-privileged to least, are: **Super Admin, Admin, Editor, Author, Authenticated, Public**. Per-module permission checks are enforced by each owning module and by [Security](security.md); detailed permission areas are owned by [User Roles & Permissions](user_roles_permissions.md).

| Role | May enter admin area | Sidebar visibility | Notes |
|---|---|---|---|
| Super Admin | Yes | Full tree, all tenants | Global access; can switch to any tenant via the context switcher. |
| Admin | Yes | Full tree within assigned tenant(s) | Tenant-scoped; may switch only between assigned tenants. |
| Editor | Yes | Items permitted by role (typically Content Manager, File Manager, Content History) | Settings sub-groups are hidden unless explicitly granted. |
| Author | Yes | Limited (typically Content Manager and File Manager for own content) | Cannot access Administration Panel, Email, or Users & Permissions settings unless granted. |
| Authenticated | No | None | Has site/login access but no admin entry. |
| Public | No | None | Anonymous visitors; no admin entry. |

Rules:

- The system must deny admin-area entry to **Authenticated** and **Public** roles before any module loads.
- The sidebar must hide (not merely disable) navigation items the current role has no permission to access.
- Even if an item is hidden, the owning module must independently enforce its own permission check on every request; navigation gating is not a substitute for module-level authorization. See [Security](security.md).
- Tenant scoping further restricts visibility: a non-global admin sees only data and tenants assigned to them. See [Multi-Tenancy](multi_tenancy.md).

## Validation Rules

- The sidebar tree must match the canonical structure above exactly: the Main group (Dashboard, Content Manager, File Manager, Marketplace) and the Settings group with its four sub-groups (Global Settings, Administration Panel, Email, Users & Permissions Plugin) and their listed items, in the given order.
- Every leaf navigation item must resolve to exactly one owning module route; no item may be a dead link.
- The admin area must always render with the built-in admin layout. The system must reject any attempt to apply a public frontend theme to admin routes.
- The tenant context switcher must only list tenants the current user is authorized to manage; it must never expose an unassigned tenant.
- The active tenant context must be present and valid on every admin request; if it cannot be resolved, the system must fall back to the default tenant or deny the request per [Multi-Tenancy](multi_tenancy.md).
- Admin routes require an authenticated session and an admin-capable role; requests failing either check must be rejected (401 for unauthenticated, 403 for unauthorized).

## Edge Cases

- **Unauthenticated access**: a request to any admin route without a valid session redirects to login (or returns 401 for API-style requests). See [Security](security.md).
- **Authenticated but not admin-capable**: an Authenticated or Public user reaching an admin route receives a 403 and is not shown the sidebar.
- **Permission revoked mid-session**: if a user's role or permissions change while signed in, the next request must re-evaluate access; hidden items must not become reachable via direct URL.
- **Theme attempts to style admin**: a malicious or misconfigured public theme must have no effect on the admin area, setup page, or login page (unless login theming is explicitly configured). The admin layout is fixed and isolated. See [Themes](themes.md).
- **No tenant assigned**: a non-global admin with no assigned tenant has nothing to switch to; the system applies the default tenant fallback or denies entry per [Multi-Tenancy](multi_tenancy.md).
- **Tenant switch during edit**: switching tenant mid-task must reload the current module in the new scope; the system must not silently apply edits made in one tenant's context to another.
- **Duplicate "Roles" entries**: both Roles links (Administration Panel and Users & Permissions Plugin) must point to the same role store; the system must not create divergent role data for the two entries.
- **Missing/disabled module**: if a module behind a sidebar item is disabled for the active tenant or role, the item must be hidden rather than shown as a broken link.

## Acceptance Criteria

- [ ] The admin sidebar renders the exact tree: Main (Dashboard, Content Manager, File Manager, Marketplace) and Settings with Global Settings, Administration Panel, Email, and Users & Permissions Plugin sub-groups containing all listed items in order.
- [ ] Every sidebar leaf routes to its owning module and no leaf is a dead link.
- [ ] The admin area is never restyled by a public frontend theme; it always uses the built-in admin layout.
- [ ] The setup page and (by default) the login page are also unaffected by public frontend themes.
- [ ] Only Super Admin, Admin, Editor, and Author roles can enter the admin area; Authenticated and Public users receive a 403.
- [ ] Unauthenticated requests to admin routes are redirected to login (or return 401).
- [ ] Sidebar items the current role cannot access are hidden, and direct-URL access to those modules is still blocked by module-level authorization.
- [ ] The tenant context switcher is present in the admin chrome and displays the active tenant on every page.
- [ ] The tenant switcher lists all tenants for Global Super Admins and only assigned tenants for Tenant Admins.
- [ ] Switching tenant reloads the current module filtered to the selected tenant.
- [ ] Both "Roles" entries resolve to the same Roles & Permissions module without creating divergent role data.
- [ ] The currently selected sidebar item is highlighted and its parent group is expanded.
