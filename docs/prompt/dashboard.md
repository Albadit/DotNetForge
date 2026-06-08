# Dashboard

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Dashboard is the landing page of the DotNetForge CMS admin area after sign-in. It surfaces at-a-glance widgets - project, content, user, and media statistics, recent activity, installed extensions, system health, and update status - and is extensible with custom widgets supplied through the [extension system](extensions.md).

## Purpose

The Dashboard gives signed-in admin users an immediate, role-appropriate overview of the state of their CMS instance (and, in multi-tenant deployments, the active tenant). It must:

- Aggregate key metrics and recent activity into a single screen.
- Let users add, remove, and rearrange widgets to suit their workflow.
- Allow users to drill from a summary stat into the module that owns the detail (Content Manager, [File Manager](file_manager.md), [Users](user_roles_permissions.md), [Extensions](extensions.md), etc.).
- Support custom widgets registered by extensions of type `Widget` and `Admin extension`, so users and developers can extend the Dashboard without modifying the core.

The Dashboard is part of the admin UI and is therefore **never affected by public frontend themes** (see [Themes](themes.md)). It is unavailable to the `Public` role; the public, unauthenticated experience is served entirely by the frontend and never exposes the Dashboard.

## Main Features

### Built-in Widgets

The Dashboard ships with the following built-in widgets. Each widget reads from the module that owns its data and links to that module on drill-down.

| Widget | Summarizes | Drill-down target |
| --- | --- | --- |
| Project statistics | High-level counts for the instance/active tenant (total pages, total content entries, published vs. draft, total media items, total users, total extensions). | [Content Manager](content_manager.md) / [Overview Settings](settings.md) |
| Content statistics | Pages and content entries by status (published, draft, scheduled, disabled), recently edited content, content awaiting review. | [Content Manager](content_manager.md) / [Review Content](content_manager.md) |
| User statistics | Total users, active vs. disabled, new registrations over a period, distribution of users across roles. | [Users](user_roles_permissions.md) / [User Roles & Permissions](user_roles_permissions.md) |
| Media statistics | Total media items, total storage used, public vs. private counts, breakdown by file type. | [File Manager](file_manager.md) |
| Recent activity | The most recent entries from the audit log (e.g. logins, content created/updated/deleted, media uploaded, plugin installed, role changed). | [Audit Logs](audit_logs.md) |
| Installed extensions | Count and list of installed extensions with their status (enabled/disabled) and available updates. | [Extensions](extensions.md) / [Plugins](extensions.md) |
| System health | Database connectivity and provider, environment, storage availability, background job/queue status, error indicators. | [Overview Settings](settings.md) / [Security](security.md) |
| Update status | Current CMS version, available CMS updates, and extensions with pending updates. | [Overview Settings](settings.md) |

### Custom Widgets via the Extension System

- Extensions of type `Widget` (and `Admin extension`) may register one or more Dashboard widgets. See [Extensions](extensions.md) for the extension lifecycle and [Developer Docs](developer_docs.md) for the step-by-step "How to create a dashboard widget" guide.
- A widget is described in the extension's manifest (`dotnetforge.extension.json`) and loaded through its `entryPoint`. The manifest's `permissions` field declares what the widget may read; the Dashboard must enforce these permissions before rendering the widget (see [Extension Manifest](extensions.md)).
- Custom widgets behave like built-in widgets for the purposes of add/remove/rearrange, permission filtering, and the loading/error/empty states described below.
- A custom widget is only available while its parent extension is **installed and enabled**. Disabling or removing the extension removes its widgets from the available list and from every user's layout (see [Edge Cases](#edge-cases)).

### Layout & Personalization

- The Dashboard is a configurable grid of widget cards.
- Each user has a per-user (and, in multi-tenant deployments, per-tenant) layout: the set of widgets they have chosen, their order, and their position/size in the grid.
- Users can add widgets from a picker of available widgets, remove widgets they do not want, and drag to rearrange or resize them.
- Layout changes persist immediately and are restored on the next visit.
- A "Reset to default" action restores the default layout for the user's role.

## User Flows

### Flow: View the Dashboard

1. A signed-in user navigates to the admin area (or is redirected there after sign-in / first-time setup, per [First-Time Installation](installation_setup.md)).
2. The system resolves the user's roles and, in multi-tenant deployments, the active tenant context.
3. The system loads the user's saved Dashboard layout. If none exists, it applies the default layout for the user's most-privileged role.
4. For each widget in the layout, the system checks the user's permissions; widgets the user is not permitted to see are silently omitted.
5. Each permitted widget requests its data. Widgets render independently and asynchronously, each showing a loading state until its data arrives.
6. As data arrives, each widget renders its summary. Widgets that return no data show an empty state; widgets that fail show an error state with a retry action.

### Flow: Add a Widget

1. From the Dashboard, the user opens the "Add widget" picker.
2. The picker lists all widgets available to the user: built-in widgets plus widgets registered by installed, enabled extensions for which the user holds the required permissions.
3. The user selects a widget.
4. The system adds the widget to the user's layout (default position and size), persists the layout, and renders the widget (entering its loading state immediately).

### Flow: Remove a Widget

1. The user opens the widget's controls (e.g. its card menu) and chooses "Remove".
2. The system removes the widget from the user's layout and persists the change.
3. The grid reflows to fill the gap. Removal affects only the current user's layout; it does not uninstall or disable the underlying extension.

### Flow: Rearrange / Resize Widgets

1. The user drags a widget card to a new position, or drags its handle to resize it.
2. The system updates the layout's order and grid coordinates and persists the change.
3. The new arrangement is restored on subsequent visits.

### Flow: Drill Into a Stat

1. The user clicks a stat, count, or list item within a widget (e.g. "12 drafts" in Content statistics).
2. The system navigates to the owning module's view, pre-filtered to match the stat where applicable (e.g. Content Manager filtered to drafts).
3. The target view enforces its own permissions; if the user lacks access to the detail, the system shows an access-denied message instead of the filtered view.

### Flow: Retry a Failed Widget

1. A widget that failed to load shows an error state with a "Retry" action.
2. The user clicks "Retry".
3. The widget re-enters its loading state and re-requests its data, rendering the result (data, empty, or error) when it returns.

## Role & Permission Rules

Access to the Dashboard and to individual widgets follows the canonical roles (most-privileged to least): **Super Admin, Admin, Editor, Author, Authenticated, Public**. A widget is shown to a user only when the user can access the admin area *and* holds the permission(s) the widget's data requires (built-in widgets map to the standard [permission areas](user_roles_permissions.md); custom widgets use the `permissions` declared in their manifest).

The table below describes the **default** visibility per role. Actual visibility is always re-evaluated against the role's assigned permissions, which are configurable in [User Roles & Permissions](user_roles_permissions.md).

| Role | Sees Dashboard | Default widgets | Notes |
| --- | --- | --- | --- |
| Super Admin | Yes | All widgets | Full visibility, including System health, Update status, and all tenants in multi-tenant deployments. |
| Admin | Yes | All widgets | Same as Super Admin within their permitted tenant(s); no cross-tenant view unless granted. |
| Editor | Yes | Content statistics, Media statistics, Recent activity, Project statistics | No User statistics, System health, or Update status by default unless granted the matching permissions. |
| Author | Yes (limited) | Content statistics (scoped to their own content), Recent activity (scoped to their own actions) | Authors manage only the content they created; widgets must respect that scope. No User, Media-management, System health, or Update widgets by default. |
| Authenticated | No (by default) | None | A plain authenticated front-end account has no admin access and no Dashboard unless explicitly granted admin permissions. |
| Public | No | None | The `Public` role is unauthenticated and **never** sees the Dashboard or the admin area. |

Additional rules:

- Add/remove/rearrange affects only the acting user's own layout; a user can never change another user's Dashboard layout.
- Only Super Admin and Admin see **System health** and **Update status** by default, because these reveal environment and version details (see [Security](security.md)).
- A widget appears in the "Add widget" picker only if the user is permitted to view it; users cannot add widgets whose required permissions they lack.
- Drill-down never bypasses module permissions: the target module re-checks access.

## Validation Rules

- **Widget identity:** Every widget (built-in or custom) must have a stable, unique widget identifier. The Dashboard must not register two widgets with the same identifier; custom widget identifiers derive from their extension `id` (see [Extension Manifest](extensions.md)).
- **Permission enforcement:** Before a widget is offered in the picker or rendered, the system must verify the current user holds the widget's required permission(s). Unauthorized widgets are omitted, not shown disabled.
- **Layout persistence:** A saved layout must reference only widgets that exist and are currently available to the user. References to missing or unavailable widgets must be ignored at render time (and may be pruned on save).
- **Grid constraints:** Widget positions and sizes must stay within the grid bounds; overlapping or out-of-bounds coordinates must be normalized to a valid arrangement before persisting.
- **Custom widget loading:** A custom widget must only load if its parent extension is installed, enabled, and passed manifest validation. Widgets from invalid, unsafe, or incompatible extensions must not load (see [Extensions](extensions.md)).
- **Tenant scoping:** In multi-tenant deployments, every widget's data query must be scoped to the active tenant; a widget must never aggregate or display data from a tenant the user cannot access. Layouts are stored per user **and** per tenant.
- **Read-only:** The Dashboard is a read/aggregate surface. Widgets must not perform write actions directly; any action (e.g. publishing) must navigate the user to the owning module, which applies its own validation.
- **Data freshness:** Each widget must indicate when its data was last loaded where relevant, and must not block the page on a slow widget (widgets load independently - see [Edge Cases](#edge-cases)).

## Edge Cases

- **Fresh install / no data yet:** On a brand-new instance, counts are zero and lists are empty. Each widget must render a clear, non-error empty state (e.g. "No content yet") with a relevant call to action (e.g. "Create your first page") rather than a blank card or an error. Recent activity and Installed extensions may legitimately be empty immediately after [first-time setup](installation_setup.md).
- **Widget from a disabled extension:** If a user's layout references a widget whose extension is disabled, the widget must not render its data. The system either hides the widget or shows a non-blocking placeholder indicating the extension is disabled, with a link to [Extensions](extensions.md)/[Plugins](extensions.md) for those permitted to manage it. The reference must remain harmless if the extension is later re-enabled.
- **Widget from a removed/uninstalled extension:** When an extension is uninstalled, its widgets must disappear from the picker and from all layouts; dangling layout references are ignored at render and pruned on next save. No error is shown to end users.
- **Slow widget data load:** Each widget loads asynchronously and independently. A slow widget must show a loading state and must never block the rest of the Dashboard or the admin shell. The system must apply a timeout; on timeout the widget transitions to the failed state.
- **Failed widget data load:** A widget that errors or times out must show a contained error state with a "Retry" action, and must not crash or blank the page. Errors in one widget must not affect sibling widgets.
- **Misbehaving custom widget:** A custom widget that throws, exceeds its time budget, or requests data beyond its declared `permissions` must be isolated - its failure is contained to its own card and logged (see [Audit Logs](audit_logs.md)); it must not be able to read data outside its permitted scope or tenant.
- **Permission revoked while viewing:** If a user's permissions change so they no longer qualify for a widget, that widget stops rendering its data on the next load and is omitted; the layout reference is retained but inert in case access is restored.
- **System health / update status unavailable:** If health checks or the update-check service cannot be reached, those widgets show a degraded/unknown state with a retry, rather than implying everything is healthy.
- **Concurrent layout edits:** If the same user edits their layout from two sessions, the last persisted write wins; persistence must be atomic so a layout is never left partially saved.
- **Tenant switch:** When an authorized user switches tenant context, the Dashboard must reload with that tenant's per-user layout and tenant-scoped widget data; stale data from the previous tenant must not be shown.

## Acceptance Criteria

- [ ] A signed-in admin user sees the Dashboard with the default layout for their most-privileged role when no saved layout exists.
- [ ] The `Public` role can never reach the Dashboard or the admin area.
- [ ] An `Authenticated`-only user with no admin permissions does not see the Dashboard by default.
- [ ] `Author` users see only content/activity widgets scoped to content they created; they do not see User statistics, System health, or Update status by default.
- [ ] System health and Update status widgets are visible only to roles with the corresponding permissions (Super Admin/Admin by default).
- [ ] All eight built-in widgets render: Project statistics, Content statistics, User statistics, Media statistics, Recent activity, Installed extensions, System health, Update status.
- [ ] A user can add a widget from the picker, and only widgets they are permitted to view appear in the picker.
- [ ] A user can remove a widget; removal affects only that user's layout and does not disable or uninstall any extension.
- [ ] A user can drag to rearrange and resize widgets, and the arrangement persists across sessions.
- [ ] Clicking a stat drills into the owning module (e.g. Content statistics → Content Manager) with appropriate filtering, and the target enforces its own permissions.
- [ ] On a fresh install, every widget shows a clear empty state (not an error and not a blank card).
- [ ] A widget belonging to a disabled extension does not render its data and shows a placeholder or is hidden; re-enabling the extension restores it.
- [ ] Uninstalling an extension removes its widgets from the picker and from all layouts without producing errors.
- [ ] Widgets load asynchronously; a slow widget shows a loading state and never blocks the rest of the Dashboard.
- [ ] A widget that fails or times out shows a contained error state with a working "Retry" action, and sibling widgets are unaffected.
- [ ] A custom widget loads only when its parent extension is installed, enabled, and manifest-valid, and it cannot read data beyond its declared permissions.
- [ ] In multi-tenant deployments, every widget's data is scoped to the active tenant, layouts are stored per user and per tenant, and switching tenants reloads the correct layout and data.
- [ ] A widget never displays data the user lacks permission to view; unauthorized widgets are omitted rather than shown.
