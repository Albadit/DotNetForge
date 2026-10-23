# Page (screen) architecture

How screens are built, routed, laid out, secured and wired to the backend. In this document "screen" means a UI
view; the content entity is always "content page" (see the [glossary](../glossary.md)). Each screen has its own
document in [`pages/`](../pages/).

## The pattern in one picture

There are no view models with behaviour, no client-side router and no SPA. A screen is a controller action that
queries the database (directly or through a web-host service) and renders a Razor view with a plain model.

```mermaid
flowchart LR
    Route["Attribute route<br/>[Route('admin/...')]"] --> Action["Controller action<br/>(AdminControllerBase)"]
    Action -- "Can / CanModify" --> Perm["IPermissionService"]
    Action -- "simple reads" --> Ctx["DotNetForgeDbContext"]
    Action -- "rules / writes" --> Svc["Web-host service<br/>(IPageService, MediaService, IAuditService, AuthService)"]
    Svc --> Ctx
    Svc --> Files["IFileStorage"]
    Ctx --> DB[("SQLite / PostgreSQL")]
    Action --> VM["View model<br/>(AdminViewModels.cs)"]
    VM --> View["Razor view (.cshtml)"]
    View --> Layout["_AdminLayout<br/>(sidebar from AdminNavigationService)"]
    View -. "form POST + antiforgery" .-> Action
```

## Route map

Every screen in the application. Auth column: **Anon** = anonymous; **AdminArea** = any admin-capable role
(`Super Admin`, `Admin`, `Editor`, `Author`); **SA/Admin** = `Super Admin` or `Admin`; **SA** = `Super Admin` only;
**+ perm** = individual actions also check `(area, action)` permissions through `Can`/`CanModify`. All routes except
`/setup`, `/health` and paths with a file extension additionally require the CMS to be installed.

| Screen | Route(s) | Controller → action | View | Auth | Doc |
| --- | --- | --- | --- | --- | --- |
| Setup wizard | `GET/POST /setup` (POST rate-limited) | `SetupController.Index` | `src/DotNetForge.Web/Views/Setup/Index.cshtml` | Anon, only before install | [setup](../pages/setup.md) |
| Sign in | `GET/POST /account/login` (POST rate-limited), `POST /account/logout` | `AccountController.Login`, `.Logout` | `src/DotNetForge.Web/Views/Account/Login.cshtml` | Anon | [login](../pages/login.md) |
| External sign-in | `POST /account/external/{scheme}` (antiforgery, rate-limited), `GET /account/external/callback` (rate-limited) | `ExternalLoginController.Challenge`, `.Callback` | none - redirects (provider, then `returnUrl`/`/admin` or back to `/account/login`) | Anon; `{scheme}` must belong to an active authentication extension | [authentication](../features/authentication.md#external-sign-in-extensions) |
| Access denied | `GET /account/denied` | `AccountController.Denied` | `src/DotNetForge.Web/Views/Account/Denied.cshtml` | Anon | [access-denied](../pages/access-denied.md) |
| Public home | `GET /` | `HomeController.Index` | the active theme's `Views/Page.cshtml` / `Views/Index.cshtml`, else `src/DotNetForge.Web/Views/Home/Page.cshtml` or `Index.cshtml` | Anon | [public-home](../pages/public-home.md) |
| Public content page | any other extension-less path (fallback) | `HomeController.RenderPage` | the page's or the active theme's `Views/Page.cshtml`, else `src/DotNetForge.Web/Views/Home/Page.cshtml` | Anon, unless the page has [view grants](../features/content-pages-and-routing.md#page-permissions) | [public-page](../pages/public-page.md) |
| Page layout stylesheet | `GET /page-css/{id:guid}.css?v={hash}` | `PageCssController.Get` | none - `text/css` of a live page's published layout (cached a year when `v` matches) | Anon | [page builder → Rendering](../features/page-builder.md#rendering) |
| Public extension resource | `GET /ext/{id}/resources/{**path}` | `ExtensionResourcesController.Resource` | none - a file from the extension's `Views/Resources/` | Anon; only active `theme` / `module` extensions, else 404 | [extensions → Slots](../features/extensions.md#slots) |
| Extension endpoints | whatever an extension maps (`IExtension.MapEndpoints`) or its MVC controllers route, e.g. `/sitemap.xml`, `/robots.txt` (SEO Toolkit), `POST /ext/contact-form/submit` (Contact Form) | the extension | the extension's | as the extension sets (public unless it adds authorization); `404` while the extension is not active | [extensions](../features/extensions.md#slots) |
| Media file | `GET /media/{id:guid}/{fileName?}` | `MediaFilesController.Download` | none - 302 to a presigned URL or a file stream | public file: Anon; private file: AdminArea user of the file's tenant, else 404 | [media storage](../features/media-storage.md#download-flow) |
| Error | `/error` (any HTTP method) | `HomeController.Error` | `src/DotNetForge.Web/Views/Home/Error.cshtml` | Anon | [error](../pages/error.md) |
| Dashboard | `GET /admin` | `DashboardController.Index` | `src/DotNetForge.Web/Areas/Admin/Views/Dashboard/Index.cshtml` | AdminArea | [dashboard](../pages/dashboard.md) |
| Content Manager | `GET /admin/content`, `POST /admin/content/{create,update/{id},duplicate/{id},delete/{id},reorder}` | `ContentController` | `src/DotNetForge.Web/Areas/Admin/Views/Content/Index.cshtml` (+ `_TreeNodes`, `_PageForm` with the Page settings / SEO / Theme / Permissions / Layout tabs) | AdminArea + perm (`Collection types`: `create` (also duplicate); `update`/`update.own` or a page edit grant; `publish` to change Published or schedule; `delete`/`delete.own`; reorder and page permissions need `update`) | [content-manager](../pages/content-manager.md) |
| Page builder | `GET /admin/content/{id}/builder` (+ `canvas`, `preview`, `draft.css`, `state`, `media`; `POST save`, `publish`, `unpublish`, `discard`, `heartbeat`, `upload`) | `PageBuilderController` | `src/DotNetForge.Web/Areas/Admin/Views/PageBuilder/{Index,Canvas}.cshtml` (no admin shell) | AdminArea + may edit the page; publish/unpublish need `publish`, upload `Media`/`create` | [page-builder](../pages/page-builder.md) |
| Profile | `GET /admin/profile`, `POST /admin/profile`, `POST /admin/profile/password` | `ProfileController` | `src/DotNetForge.Web/Areas/Admin/Views/Profile/Index.cshtml` | AdminArea (own account only) | [profile](../pages/profile.md) |
| Media | `GET /admin/media`, `POST /admin/media/upload`, `POST /admin/media/visibility/{id}`, `POST /admin/media/delete/{id}`, `POST /admin/media/delete-selected` (bulk, at most `MediaController.MaxBulkDelete` = 100 ids) | `MediaController` (`.Index`, `.Upload`, `.Visibility`, `.Delete`, `.DeleteSelected`) | `src/DotNetForge.Web/Areas/Admin/Views/Media/Index.cshtml` | AdminArea + perm (`Media`: upload needs `create`; public/private needs `update` or `update.own`; delete needs `delete`, or `delete.own` for files the user uploaded - checked per file in a bulk delete) | [media](../pages/media.md) |
| Settings | `GET /admin/settings`, `POST /admin/settings/save` | `SettingsController` | `src/DotNetForge.Web/Areas/Admin/Views/Settings/Index.cshtml` | AdminArea + perm (`Settings`: `read`; save needs `update`) | [settings](../pages/settings.md) |
| API Tokens | `GET /admin/api-tokens`, `GET/POST /admin/api-tokens/create`, `POST /admin/api-tokens/revoke/{id}` | `ApiTokensController` | `src/DotNetForge.Web/Areas/Admin/Views/ApiTokens/{Index,Create,Created}.cshtml` | AdminArea + perm (`API`: `read`; create needs `create`, revoke `delete`) | [api-tokens](../pages/api-tokens.md) |
| Roles | `GET /admin/roles`, `GET/POST /admin/roles/create`, `GET/POST /admin/roles/{id}`, `POST /admin/roles/{id}/duplicate`, `POST /admin/roles/{id}/delete` | `RolesController` | `src/DotNetForge.Web/Areas/Admin/Views/Roles/{Index,Edit}.cshtml` | AdminArea + perm (`Roles`: `read`; create/duplicate `create`, save `update`, delete `delete`) | [roles](../pages/roles.md) |
| Users | `GET /admin/users`, `GET/POST /admin/users/create`, `GET/POST /admin/users/{id}`, `POST /admin/users/{id}/{enable,disable,delete}` | `UsersController` | `src/DotNetForge.Web/Areas/Admin/Views/Users/{Index,Edit}.cshtml` | AdminArea + perm (`Users`: `read`; create `create`, save/enable/disable `update`, delete `delete`) | [users](../pages/users.md) |
| Audit Logs | `GET /admin/audit-logs` | `AuditLogsController.Index` | `src/DotNetForge.Web/Areas/Admin/Views/AuditLogs/Index.cshtml` | AdminArea + perm (`Audit logs`: `read`) | [audit-logs](../pages/audit-logs.md) |
| Plugins | `GET /admin/plugins`, `POST /admin/plugins/{id}/enable`, `POST /admin/plugins/{id}/disable` | `PluginsController.Index`, `.Enable`, `.Disable` | `src/DotNetForge.Web/Areas/Admin/Views/Plugins/Index.cshtml` | SA | [plugins](../pages/plugins.md) |
| Admin extension tab | `GET /admin/ext/{id}` (shell), `GET /admin/ext/{id}/raw` (active `admin` extensions), `GET /admin/ext/{id}/resources/{**path}` (any active extension) | `ExtensionsController.Host`, `ExtensionViewController.Render` / `.Resource` | `src/DotNetForge.Web/Areas/Admin/Views/Extensions/Host.cshtml` + the extension's own views | AdminArea | [extension-host](../pages/extension-host.md) |
| Module placeholders | `/admin/marketplace`, `/admin/content-history`, `/admin/internationalization`, `/admin/transfer`, `/admin/webhooks`, `/admin/email/configuration`, `/admin/email/templates`, `/admin/providers`, `/admin/advanced-settings` | `ModulesController` | `src/DotNetForge.Web/Areas/Admin/Views/Modules/Placeholder.cshtml` | AdminArea | [module-placeholders](../pages/module-placeholders.md) |

Non-screen endpoints: `GET /health` (JSON, bypasses install gate) and `/api/*` ([headless API](../features/headless-api.md)).

The Settings screen was AdminArea before; Editors and Authors now get the denied screen there.

## How screens are registered

There is no registry. A screen exists when:

1. a controller action has an **attribute route** (every admin controller has `[Route("admin/...")]`;
   `ModulesController` uses absolute `[HttpGet("/admin/...")]` per action); and
2. a view is found by MVC's normal lookup: `src/DotNetForge.Web/Areas/Admin/Views/<Controller>/<Action>.cshtml`, then
   `src/DotNetForge.Web/Areas/Admin/Views/Shared/`, then `Views/Shared/` - or the action names the view explicitly (`View("Created", ...)`,
   `View(nameof(Index), ...)`, `View("Placeholder", ...)`, `View("Page", ...)`).

Extensions are the exception: admin tabs come from enabled extensions (see
[Admin extension tabs](#admin-extension-tabs-dynamic-navigation)), and extension endpoints are mapped at startup by
`await app.MapDotNetForgeExtensionsAsync()` (before the fallback, so they win over content-page URLs).

## Layouts

| Layout | Set by | Used by | What it renders |
| --- | --- | --- | --- |
Look, components and behaviours of the built-in layouts: [design system](design-system.md#layouts).

| Layout | Set by | Used by | What it renders |
| --- | --- | --- | --- |
| `src/DotNetForge.Web/Areas/Admin/Views/Shared/_AdminLayout.cshtml` | `src/DotNetForge.Web/Areas/Admin/Views/_ViewStart.cshtml` | every admin screen | `<title>{Title} · DotNetForge Admin</title>`; `theme.js` (in `<head>`), `forge/{tokens,base,components,admin}.css`, `ui.js` (`defer`); sidebar (logo, the `_Sidebar` partial built by `AdminNavigationService`, account menu with **View site** and **Sign out**); top bar (breadcrumbs except on the dashboard, **View site**, theme menu); page header (`ViewData["Title"]` as the only `<h1>`, optional `ViewData["Description"]`, `@section Actions`); `_Feedback`; `@section Scripts` |
| `src/DotNetForge.Web/Views/Shared/_AuthLayout.cshtml` | `Layout = "_AuthLayout"` in the view | setup, login, denied | `theme.js`, `forge/{tokens,base,components,auth}.css`, `ui.js`; brand panel (follows the theme) + form column with theme menu; `_Feedback` |
| `src/DotNetForge.Web/Views/Shared/_Layout.cshtml` | `src/DotNetForge.Web/Views/_ViewStart.cshtml` | public home (list or root page), public content pages and the error screen while no theme is enabled | `theme.js`, `forge/{tokens,base,components,public}.css` (no `ui.js`, no `_Feedback`, no theme menu); `<title>`, `meta description` and `canonical` from `ViewData["Title"]`/`["MetaDescription"]`/`["CanonicalUrl"]`; header with app name and an **Admin** link, `<main>`, footer |
| extension's own `Views/Shared/_Layout.cshtml` | the extension's `_ViewStart.cshtml` | admin extension `raw` documents; theme views (public home and content pages while a theme is active) | whatever the extension ships (same CSP applies; it may link the host's `/css/forge/*.css` and `<script src="/js/forge/theme.js">` to look like the admin - [design system](design-system.md#tag-helpers)) |

Layouts read `ViewData["Title"]`; the public and auth layouts also read `ViewData["AppName"]` (falling back to
`"DotNetForge CMS"`). Admin views set `ViewData["Title"]` (and optionally `ViewData["Description"]`) at the top of the
view; the admin layout does not use `AppName`.

**Rule:** the admin and auth layouts must never be affected by a public theme. Only `HomeController.ThemedViewAsync`
renders theme views ([themes](../features/themes.md)); admin styling lives in `wwwroot/css/forge/` and is never
replaced by a theme.

## Content Security Policy and screens

`SecurityHeadersMiddleware` sends `script-src 'self'; style-src 'self'` (plus `img-src 'self' data: https:`,
`media-src 'self' https:`, `frame-src 'self'`, `frame-ancestors 'self'`, `form-action 'self'`, `object-src 'none'`)
on every response. For screens this means:

- **No inline script or style**: no `<script>` blocks, `<style>` blocks, `style="..."` attributes or `on*="..."`
  handlers. Scripts go in `src/DotNetForge.Web/wwwroot/js/forge/` and are referenced with
  `<script src="~/js/forge/..." defer>` (page scripts via `@section Scripts`); styles go in
  `src/DotNetForge.Web/wwwroot/css/forge/`. This applies to admin extension documents too.
- Tag helpers that emit inline styles are not used: validation errors render with `<form-errors />`, never
  `asp-validation-summary` (its empty state is `<li style="display:none">`).
- Behaviour hooks are data attributes handled by `ui.js` (`data-confirm`, `data-collection`, `data-copy-target`, ...;
  list: [design system → behaviours](design-system.md#behaviours)).
- `AdminUiTests.Every_screen_renders_without_inline_script_or_style` renders every admin screen, sign-in, `/` and a
  content page and fails on any inline style, `<style>`, inline `<script>` or `on…=` handler.
- Forms may only post to this origin; images may also come from `data:` and HTTPS origins (media redirects to
  presigned object-storage URLs).
- The admin extension host embeds `/admin/ext/{id}/raw` in a same-origin `<iframe>`, allowed by `frame-src 'self'`
  and `X-Frame-Options: SAMEORIGIN`.
- In Development the policy is **report-only**: violations appear only as browser console warnings, so check the
  console when adding UI.

## Navigation

Navigation is defined once, in `AdminNavigation.Tree`
(`src/DotNetForge.Web/Areas/Admin/Navigation/AdminNavigation.cs`): sections, collapsible groups and links, each link
with label, href, icon, the screen's controller type and a `Planned` flag. The scoped `AdminNavigationService` builds
an `AdminNavModel` from it once per request; `_AdminLayout` renders the sidebar (`Shared/_Sidebar.cshtml`, one
`Shared/_NavLink.cshtml` per link) and the top-bar breadcrumbs from that model.

```mermaid
flowchart TB
    Tree["AdminNavigation.Tree"] --> Svc["AdminNavigationService.BuildAsync<br/>(per request)"]
    Cat["ExtensionCatalog.GetActiveAsync('admin')"] --> Svc
    Auth["IAuthorizationService +<br/>controller [Authorize] data"] --> Svc
    Svc --> Model["AdminNavModel<br/>(Sections, Trail, Allows(href))"]
    Model --> Sidebar["_Sidebar + _NavLink"]
    Model --> Crumbs["Top-bar breadcrumbs"]
    Model --> Screens["Screens: actions shown only when reachable<br/>(dashboard quick actions)"]
    Sidebar --> Main["Main: Dashboard, Content Manager, Media, Marketplace"]
    Sidebar --> Settings["Settings (four collapsible groups):<br/>Global Settings · Administration Panel · Email · Users & Permissions Plugin"]
    Sidebar --> Ext["Extensions: one link per enabled admin extension"]
```

| Behaviour | How |
| --- | --- |
| Role-aware | A link is shown only when the user passes its controller's `[Authorize]` data - `AdminArea` and `[RequirePermission]` (collected from the class and its base classes, combined with `AuthorizationPolicy.CombineAsync`, evaluated with `IAuthorizationService`), so custom roles see what their grants open. With the default grants an Editor or Author sees all of Main but in Settings only the placeholder links (`ModulesController` is AdminArea): no Settings, API Tokens, Plugins, Roles, Users or Audit Logs, and no Administration Panel group. Empty groups and sections are dropped. Hiding is never the gate - the controller still refuses a direct URL. |
| Active link | The longest href that equals the request path or is a parent of it (`/admin` only matches itself), so `/admin/api-tokens/create` marks **API Tokens**; it gets `aria-current="page"`. If a link were listed twice, only its first occurrence would be marked. |
| Groups | Settings sub-groups are `<details data-nav-group>`; the group holding the active link renders open, the others closed; `ui.js` remembers what the user opened or closed (`localStorage` `dnf-nav`), the active group always opens. |
| Planned modules | `Planned: true` links (the [module placeholders](../pages/module-placeholders.md)) show a **Soon** badge. |
| Breadcrumbs | Section → group → link label (the link is clickable), then the screen's `ViewData["Title"]` when it differs (e.g. **Create API token**). Not shown on the dashboard or when the trail has one entry. |
| `Allows(href)` | `AdminNavModel.Allows("/admin/users")` is true when the user's sidebar contains that href; the dashboard uses it to show quick actions and cards only for reachable screens. |

- Items with no built screen route to `ModulesController` placeholders, so the navigation has no dead links.
- Navigation is full page loads; there is no client router. Deep links are ordinary URLs, e.g.
  `/admin/content?selected={pageId}`. Below 64rem the sidebar is a drawer (menu button in the top bar).
- Tested by `AdminUiTests.The_sidebar_shows_only_screens_the_role_may_open`.

### Admin extension tabs (dynamic navigation)

`AdminNavigationService` calls `ExtensionCatalog.GetActiveAsync("admin")` on every admin request (discovery from the
loader's cache, enabled state from `ExtensionState`'s 30-second cache; ordered by name) and appends an **Extensions**
section with one link per active admin extension (label = extension name, href `/admin/ext/{manifest id}`, icon
`panel`, controller `ExtensionsController`). Enabling an admin extension on [Plugins](../pages/plugins.md) adds its
tab on the next request. See [extension host](../pages/extension-host.md).

## How screens load data

- **Synchronously on the server, before the response is sent.** There is no client-side data fetching on any
  screen; every screen arrives fully rendered. The only loading indicator is the extension host's skeleton, shown
  until the extension's `<iframe>` has loaded.
- Reads use `AsNoTracking()` and project into a view model (`Select(...)`), scoped by `AdminControllerBase.TenantId`.
  Audit Logs and the Dashboard (audit count and **Recent activity**) also include entries with no tenant
  (`TenantId == null`, e.g. failed sign-ins); extension data and global settings are not tenant-specific - documented
  per screen.
- Lists are unpaged on the server except Audit Logs (`Take(100)`) and the dashboard's activity
  (`DashboardController.RecentActivityCount` = 8). Search, filters, sorting and pagination of a list are client-side
  enhancements over the rendered rows (`data-collection` in `ui.js`); without JavaScript the whole list shows.
- A screen that also handles validation errors builds its model in a private helper (`ContentController.BuildIndexAsync`,
  `MediaController.BuildIndexAsync`, `SettingsController.BuildAsync`) so GET and the failed POST render the same
  model.
- Permission-aware screens compute their flags in the same helper (`MediaIndexViewModel.CanUpload`,
  `MediaRowViewModel.CanDelete`).

## How actions reach the backend

```mermaid
sequenceDiagram
    actor User
    participant Form as Razor form
    participant Ctl as Admin controller [HttpPost][ValidateAntiForgeryToken]
    participant Svc as Service (optional)
    participant DB as DotNetForgeDbContext
    participant Aud as IAuditService
    User->>Form: submit (ui.js asks data-confirm first)
    Form->>Ctl: POST + __RequestVerificationToken
    Ctl->>Ctl: Can / CanModify (content, media) else Forbid()
    Ctl->>Svc: validate/apply (e.g. IPageService.ApplyAsync, MediaService.UploadAsync)
    alt invalid
        Ctl-->>User: 200, same view, ModelState error
    else valid
        Ctl->>DB: SaveChangesAsync
        Ctl->>Aud: LogAsync(AuditActions.X, ...)
        Ctl-->>User: 302 RedirectToAction + TempData["Success"]
    end
```

Conventions every mutating screen follows:

- `<form method="post">` with `@Html.AntiForgeryToken()`; the action has `[HttpPost]` and
  `[ValidateAntiForgeryToken]`. Antiforgery is configured with `HeaderName = "X-CSRF-TOKEN"` so the only `fetch`
  call (Content Manager reorder) can send the token in a header. File uploads use `enctype="multipart/form-data"`
  and declare `[RequestSizeLimit]`/`[RequestFormLimits]` on the action (Media: 25 MB × 10 + 1 MB).
- Success → Post/Redirect/Get with an optional `TempData["Success"]` (or `TempData["Error"]`/`["Info"]`) message,
  shown as a toast by the layout (`Views/Shared/_Feedback.cshtml`); screens never render their own flash markup
  ([design system → feedback](design-system.md#feedback)).
- Failure → `ModelState.AddModelError(string.Empty, message)` and re-render with the posted values; views show the
  errors with `<form-errors />` at the top of the form. Input lengths are checked before saving.
- Permission denied → `Forbid()` → `/account/denied` (status 403).
- Exceptions (e.g. a unique-index violation) are not caught and surface through the global error handling.
- Successful writes are audited via `IAuditService` (Content create/update/delete/reorder, Media upload/delete,
  Settings save, API token create/revoke, login/logout, setup). The service does the audit for media
  (`MediaService`); controllers do it for the rest.

## State management

| State | Mechanism | Lifetime | Example |
| --- | --- | --- | --- |
| Selected item | query string | URL | `/admin/content?selected={id}` |
| View data | view model / `ViewData` / `ViewBag` | one request | `ViewData["ParentOptions"]`, `ViewBag.Permissions` |
| Flash message | `TempData["Success"]` / `["Error"]` / `["Info"]` (cookie TempData provider, Data Protection) | until read on the next request; the toast auto-hides after 6 s (Success only) | "Saved 'About'." |
| Form input after a failed POST | the bound model is passed back to the view | one request | `PageInput` keeps unsaved edits |
| Signed-in user | auth cookie `dnf.auth` (keys in `DataProtectionKeys`) | 8 h sliding; account re-checked every request (30 s cache) | name in the sidebar account menu |
| Client UI state | DOM (`ui.js`, `content.js`) | until reload | list search/filter/page, selection, conditional fields, drag state |
| UI preferences | browser `localStorage` (`ui.js`, `theme.js`) | until cleared | `dnf-theme` (light/dark/system), `dnf-nav` (open sidebar groups), `dnf-view:<screen>` (grid/list) |
| Persistent | database (+ `IFileStorage` for media bytes) | permanent | everything else |

`localStorage` holds only display preferences (every access is wrapped in `try`; blocked storage falls back to the
defaults); there is no client-side data cache and no global client state.

## Loading states

Screens are server-rendered in one response; the browser's own page-load indicator is the main feedback. The
extension host shows a skeleton until its `<iframe>` loads. The one asynchronous action (tree reorder) gives no
in-progress indicator and reloads the page when it finishes (after a toast on failure).

## Error handling on screens

- Validation and business-rule errors: `<form-errors />` on the same screen, or a `TempData["Error"]` toast after a
  redirect (see above).
- Not found: `NotFound()` produces an empty 404 (no custom 404 page, no status-code pages middleware).
- Authorization: redirects to login or the denied screen (role checks and `Forbid()` alike).
- Unhandled: [error screen](../pages/error.md) outside Development, for GET and POST requests.

## Permissions on screens

Two layers, both enforced on the server:

1. **Screen access** - the `AdminArea` policy plus `[RequirePermission(area, action)]` on the controller and its
   mutating actions (Plugins: `[Authorize(Roles = "Super Admin")]`); see the route map and
   [authorization → screen access](../features/authorization.md#screen-access).
2. **Record permissions** - `AdminControllerBase.Can(area, action)` and
   `CanModify(area, anyAction, ownAction, createdById)` evaluate the user's roles' stored grants
   ([authorization](../features/authorization.md#how-permissions-are-evaluated)). Used by the Content Manager (area
   `Collection types`) and Media (area `Media`); a denied action returns `Forbid()`.

What the view hides (never the gate - the server checks again):

| Where | Hidden by |
| --- | --- |
| Sidebar links and groups | `AdminNavigationService` ([navigation](#navigation)) |
| Dashboard quick actions and cards | `DashboardViewModel.CanCreatePages` / `CanUpload` and `AdminNavModel.Allows(href)` |
| Media upload card, a file's **Delete**, its selection checkbox | `MediaIndexViewModel.CanUpload`, `MediaRowViewModel.CanDelete` |
| Content Manager drag-to-reorder | `ViewData["CanReorder"]` (`Collection types`/`update`) |

The Content Manager otherwise shows every button to every admin-capable user: an Author who saves or deletes another
user's page gets the denied screen, and changing Published or the schedule without `publish` re-renders the form with
"You don't have permission to publish, unpublish or schedule pages.". Other screens have no per-button logic - if a
user can open them, every action on them is available.

## Dialogs and modals

One shared modal: the confirmation `<dialog id="confirm-dialog">` in `Views/Shared/_Feedback.cshtml`, opened by
`ui.js` for any form (or submit button) with `data-confirm="<question>"` (optional `data-confirm-title`,
`data-confirm-action`, `data-confirm-tone`). Without the dialog API it falls back to `window.confirm`; without
JavaScript the form submits directly. Mechanics: [design system → feedback](design-system.md#feedback).

| Screen | Confirmation |
| --- | --- |
| Content Manager | **Delete page** (`_PageForm`): "Delete this page? Its child pages move up a level." |
| Media | a file's **Delete** and the bulk **Delete** of the selection |
| API Tokens | **Revoke** |
| Plugins | **Disable** (enable needs no confirmation) |

A failed reorder shows a danger toast (`window.Forge.toast`) with the server's `{ error }` or "Could not save the new
order." and reloads. There are no inline `onsubmit`/`onclick` handlers (the CSP blocks them). The denied screen's
**Sign out** is a plain POST button.

## Component organization

| Kind | Location | Examples |
| --- | --- | --- |
| Layouts | `Views/Shared/`, `src/DotNetForge.Web/Areas/Admin/Views/Shared/` | `_Layout`, `_AuthLayout`, `_AdminLayout` |
| Shared partials | `Views/Shared/` (all layouts), `Areas/Admin/Views/Shared/` (admin shell) | `_Feedback` (toasts + confirm dialog), `_ThemeMenu`, `_Sidebar`, `_NavLink` |
| Screen partials | next to the screen that owns them, prefixed `_` | `Content/_TreeNodes.cshtml` (recursive), `Content/_PageForm.cshtml` |
| Navigation | `src/DotNetForge.Web/Areas/Admin/Navigation/AdminNavigation.cs` | `AdminNavigation.Tree`, `AdminNavigationService` |
| View components | admin: class in `src/DotNetForge.Web/Areas/Admin/Components/`, view in `src/DotNetForge.Web/Areas/Admin/Views/Shared/Components/<Name>/Default.cshtml`; public: `src/DotNetForge.Web/Components/` | `ExtensionWidgetsViewComponent` (dashboard widgets), `ExtensionModulesViewComponent` (page modules, returns HTML directly) |
| Tag helpers | ASP.NET Core's (`@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`) and the UI ones in `src/DotNetForge.Web/Ui/UiTagHelpers.cs` (`@addTagHelper *, DotNetForge.Web`), both in each `_ViewImports` | `asp-for`, `asp-action`; `<icon>`, `<badge>`, `<alert>`, `<empty-state>`, `<stat-card>`, `<timestamp>`, `<form-errors />` |
| Presentation helpers | `src/DotNetForge.Web/Ui/` | `AuditPresentation` (action labels/icons), `MediaPresentation` (file kind, size) |
| Styles | `src/DotNetForge.Web/wwwroot/css/forge/` | `tokens.css`, `base.css`, `components.css`, `admin.css`, `auth.css`, `public.css` |
| Scripts | `src/DotNetForge.Web/wwwroot/js/forge/` | `theme.js` (every layout, in `<head>`), `ui.js` (admin and auth layouts), `content.js` (Content Manager only) |
| Icons | `src/DotNetForge.Web/wwwroot/img/icons.svg` | sprite used by `<icon name="…">` |

Reusable UI is the **Forge design system** - class-based components, tag helpers, partials and data-attribute
behaviours. Build every screen from it: [design system → building a screen](design-system.md#building-a-screen) and
the [component table](design-system.md#components).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

| Area | Target |
| --- | --- |
| Admin shell | One built-in admin layout for every admin screen ✔ (`_AdminLayout`). Public [themes](../features/themes.md) never affect the admin area, the setup wizard or (unless login theming is explicitly configured) the sign-in screen; applying a public theme to an admin route is rejected. |
| Tenant context | The shell shows the active tenant on every admin screen and offers a tenant switcher to users authorized for more than one tenant. Behaviour, gating and scoping: [multi-tenancy → admin tenant switcher](../features/multi-tenancy.md#admin-tenant-switcher). Today the shell shows no tenant (the static "Tenant Default" block was removed: one site per installation). |
| Role-aware sidebar | Items the current role cannot open are hidden ✔ ([navigation](#navigation)). Still planned: hiding items whose module is disabled for the active tenant or role, and hiding by granted permissions rather than by the controller's role attributes (an Editor sees the placeholder links because `ModulesController` is AdminArea). |
| Active state | ✔ The current section is highlighted including its child routes and the group containing it is expanded ([navigation](#navigation)). |
| Owning module | Every sidebar leaf routes to exactly one owning module; built screens are in the [route map](#route-map), unbuilt ones in [module placeholders](../pages/module-placeholders.md). |

### Canonical sidebar tree vs the sidebar

The spec fixes the tree: **Main** (Dashboard, Content Manager, File Manager, Marketplace) and one **Settings** group
with four nested sub-groups - Global Settings (Overview, API Tokens, Content History, Internationalization, File
Manager, Plugins, Transfer, Webhooks), Administration Panel (Roles, Users, Audit Logs), Email (Configuration,
Templates), Users & Permissions Plugin (Roles, Providers, Advanced Settings) - in that order. Entries must not be
added, removed or reordered without a spec change. `AdminNavigation.Tree` follows it except:

| Spec | `AdminNavigation.Tree` today |
| --- | --- |
| Main → **File Manager** | labelled **Media** (`/admin/media`) |
| Global Settings → **Overview** | labelled **Settings** (`/admin/settings`, the key/value editor); the Overview screen is [planned](../pages/settings.md#planned-not-implemented) |
| Global Settings → **File Manager** (between Internationalization and Plugins) | missing; file manager settings: [media storage](../features/media-storage.md) |
| one Settings group with four collapsible sub-groups | ✔ one **Settings** section with four collapsible `<details>` sub-groups |
| Users & Permissions Plugin → **Roles** | not repeated: **Roles** is listed once, under Administration Panel - two links to the same screen confused users |
| no Extensions group | `AdminNavigationService` appends an **Extensions** section (admin extension tabs) when at least one admin extension is enabled - an addition the spec tree does not list |

All other entries and their order already match.

### Role rules for navigation

Default visibility; actual visibility follows the role's granted permissions ([authorization](../features/authorization.md)).

| Role | Enters admin area | Sidebar shows |
| --- | --- | --- |
| `Super Admin` | yes | full tree, all tenants |
| `Admin` | yes | full tree within assigned tenant(s) |
| `Editor` | yes | items permitted by role (typically Content Manager, File Manager, Content History); Settings sub-groups hidden unless granted |
| `Author` | yes | limited (typically Content Manager and File Manager, own content); no Administration Panel, Email or Users & Permissions Plugin unless granted |
| `Authenticated` | no | none |
| `Public` | no | none |

- The entry column is enforced ✔: non-admin-capable roles are denied before any module loads (`AdminArea` policy).
- Hiding an item is never the gate: each module enforces its own check on every request ✔ (controller
  `[Authorize]` attributes; `Can`/`CanModify` for content and media actions).
- Authors are limited to their own content ✔ in the Content Manager and Media (`update.own`/`delete.own` via
  `CanModify`).
- Hiding by role ✔: the sidebar drops links whose controller the role cannot open. Not yet by granted permissions:
  an Editor still sees the placeholder links (Content History, Email, Providers, ...), and a role without any
  `Media` permission still sees **Media**.
- Tenant scoping further restricts what a non-global admin sees (only assigned tenants and their data).

### User flows

**Open the admin area**

1. ✔ Not installed → `/setup`; not signed in → `/account/login`; not admin-capable → `/account/denied`.
2. Resolve the active tenant ([multi-tenancy](../features/multi-tenancy.md#planned-not-implemented)); if it cannot be
   resolved, fall back to the default tenant or deny.
3. Render the built-in admin layout ✔ and a sidebar filtered by role ✔, permissions and module availability.

**Navigate to a module:** select a Main item or a leaf inside a Settings sub-group → the owning module opens scoped
to the active tenant → the leaf is marked active and its parent group stays expanded ✔.

**Switch tenant:** see [multi-tenancy → admin tenant switcher](../features/multi-tenancy.md#admin-tenant-switcher).

### Rules and validation

- The sidebar matches the canonical tree exactly (groups, items, order).
- Every leaf resolves to exactly one owning module route; no dead links ✔ (placeholders via `ModulesController`).
- Admin routes always render with the built-in admin layout ✔; a public theme can never be applied to them.
- The active tenant is present and valid on every admin request; otherwise default-tenant fallback or deny. Today
  `AdminControllerBase.TenantId` silently returns `Guid.Empty` when the claim is missing.
- Unauthenticated → 401 (API-style requests) or redirect to sign-in ✔; authenticated without an admin-capable role →
  403 ✔ (via `302 /account/denied`, which responds 403).

### Edge cases

| Case | Target behaviour |
| --- | --- |
| Role or permissions change mid-session | Re-evaluated on the next request; hidden items stay unreachable by URL. Today roles are cookie claims fixed at sign-in (8 h sliding), so a change takes effect only after signing in again; a disabled or deleted account is signed out within 30 s ✔ (`ValidateSessionAsync`). |
| Authenticated/Public user hits an admin route | 403, no sidebar ✔ (denied screen uses `_AuthLayout`). |
| Public theme tries to style the admin | No effect on admin, setup or sign-in (unless login theming is configured). |
| Non-global admin with no assigned tenant | Default-tenant fallback or deny entry. |
| Tenant switched during an edit | Current screen reloads in the new scope; edits made in one tenant's context are never applied to another. |
| Module disabled for the active tenant or role | Item hidden, not shown as a broken link. |

### Acceptance criteria

- [ ] The sidebar renders the exact canonical tree (see differences above).
- [ ] Every sidebar leaf routes to its owning module and no leaf is a dead link - no dead links today, but nine
  leaves are placeholders and Global Settings → File Manager is missing.
- [x] The admin area is never restyled by a public theme and always uses the built-in admin layout -
  `src/DotNetForge.Web/Areas/Admin/Views/_ViewStart.cshtml` sets `_AdminLayout`; theme views are only used by
  `HomeController.ThemedViewAsync`.
- [x] The setup and sign-in screens are unaffected by public themes - both set `Layout = "_AuthLayout"`.
- [x] Only `Super Admin`, `Admin`, `Editor`, `Author` enter the admin area; others get 403 - `AdminArea` policy on
  `AdminControllerBase`, forbid → `AccountController.Denied` (status 403).
- [x] Unauthenticated requests to admin routes are redirected to sign-in - cookie `LoginPath` in
  `DependencyRegistration`.
- [x] Sidebar items the role cannot open are hidden, and direct-URL access stays blocked - hiding by
  `AdminNavigationService` (controller `[Authorize]` data), blocking by controller attributes and `Can`/`CanModify`;
  `AdminUiTests.The_sidebar_shows_only_screens_the_role_may_open`. (Hiding by permissions and module availability:
  not built.)
- [ ] The tenant switcher is in the admin chrome and shows the active tenant on every screen - nothing is shown today.
- [ ] The switcher lists all tenants for global Super Admins and only assigned tenants for tenant admins.
- [ ] Switching tenant reloads the current module filtered to the selected tenant.
- [x] Roles resolves to one module without divergent role data - a single `/admin/roles` link (`RolesController`;
  the spec's second entry is not repeated, `UserAndRoleManagementTests.The_sidebar_lists_roles_once`).
- [x] The selected sidebar item is highlighted and its parent group expanded - longest-href match (child routes
  count) and `ContainsActive` opens the group (`AdminNavigationService`).

## Adding a new admin screen

Use the [admin-page skill](../../.claude/skills/admin-page/SKILL.md). In short:

1. Controller in `src/DotNetForge.Web/Areas/Admin/Controllers/` deriving `AdminControllerBase`, with `[Route("admin/<slug>")]` and a
   role restriction if needed.
2. View model in `src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs`; view in `src/DotNetForge.Web/Areas/Admin/Views/<Controller>/Index.cshtml`
   setting `ViewData["Title"]`.
3. Business rules in a `src/DotNetForge.Web/Services/` class registered in `DependencyRegistration`, not in the controller. Runtime
   files go through `IFileStorage`, never the content root.
4. Mutations: POST + antiforgery + `IAuditService.LogAsync` with a constant from `AuditActions`; validate input
   lengths before saving.
5. Per-action permissions: check `Can(area, action)` / `CanModify(area, any, own, createdById)` and return
   `Forbid()`; expose the result on the view model (`CanX`) and hide what the user cannot do.
6. Build the view from the [design system](design-system.md#building-a-screen): cards, `.field`s,
   `<form-errors />`, `TempData` toasts, `data-confirm` for destructive forms, `table.table.table-stack`,
   `<empty-state>`. No inline script or style; page scripts go in `src/DotNetForge.Web/wwwroot/js/forge/` via
   `@section Scripts`.
7. Add the link to `AdminNavigation.Tree` with the screen's controller type (replace the matching placeholder link
   and `ModulesController` action if one exists) - visibility, active state and breadcrumbs follow.
8. Add `.docs/pages/<screen>.md` and update the route map above.
