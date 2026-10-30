---
name: admin-page
description: >-
  Adds or changes a server-rendered admin screen in DotNetForge CMS (src/DotNetForge.Web/Areas/Admin): controller deriving
  AdminControllerBase, attribute route under /admin, role restriction, view model, Razor view built from the DotNetForge
  UI library (Dnf components), POST + antiforgery + audit for mutations, sidebar link, and the matching .docs/pages document. Use when
  asked to build an admin page/screen, replace one of the module placeholders (Marketplace, Content History,
  Internationalization), or add an
  action/form to an existing admin screen.
---

# Admin screen

Read first: `.docs/architecture/pages.md` (pattern, layouts, conventions), `.docs/architecture/design-system.md`
(how screens use the UI: layouts, feedback, building a screen), `.docs/ui/README.md` (components, tag helpers, states;
the component reference in the in-app UI docs, `/admin/docs/ui`, source in `src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/`) and the screen's doc in `.docs/pages/` if it exists. Model screens: `PagesController` (forms + service + audit), `ContentController` (forms generated from data, JSON for pickers), `SettingsController` (simple upsert),
`UsersController` / `RolesController` (list + edit form + service with rules, `[RequirePermission]` per action).

## Steps

1. **Route and controller** - `src/DotNetForge.Web/Areas/Admin/Controllers/<Name>Controller.cs`:
   ```csharp
   [Route("admin/<slug>")]
   [RequirePermission(PermissionAreas.X, PermissionActions.Read)]   // omit for every admin user; add another per mutating action
   public sealed class <Name>Controller : AdminControllerBase
   ```
   - Never inherit from `Controller` directly - `AdminControllerBase` carries `[Area("Admin")]`, the `AdminArea`
     policy, `CurrentUserId` and `Can`/`CanModify`.
   - Replacing a placeholder: delete its action from `ModulesController` and reuse the **same route** so the sidebar
     link keeps working.
2. **Reads** - query `DotNetForgeDbContext` with `AsNoTracking()`, project with `Select` into a
   view model. Put view models in `src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs`.
3. **Rules** - anything beyond a trivial check goes into a `src/DotNetForge.Web/Services/<Thing>Service.cs` (scoped, registered in
   `src/DotNetForge.Web/Startup/DependencyRegistration.cs`) returning an error message or `Result`, like `PageService.ApplyAsync`. The
   controller only maps errors to `ModelState`.
4. **Mutations** - `[HttpPost("<verb>")]` + `[ValidateAntiForgeryToken]`; load the entity **by id**
   (else `NotFound()`); save; `await _audit.LogAsync(AuditActions.<X>, "<EntityType>", id, display)` (add a constant
   to `src/DotNetForge.Shared/Constants/AuditActions.cs` if needed); `TempData["Success"] = "..."`;
   `RedirectToAction(...)`. Inject `IAuditService`, not the concrete class. For actions governed by the permission
   matrix use `Can(area, action)` / `CanModify(area, any, own, createdById)` from `AdminControllerBase` and return
   `Forbid()` when they fail. Validate string lengths before saving. On validation failure: `ModelState.AddModelError(string.Empty, msg)` and re-render the
   same view with a model built by a shared private `Build...Async` helper.
5. **View** - `src/DotNetForge.Web/Areas/Admin/Views/<Name>/Index.cshtml`, built from the DotNetForge UI library
   (`.docs/architecture/design-system.md#building-a-screen`; model: `Settings/Index.cshtml`, `Users/Index.cshtml`):
   - `ViewData["Title"]` (the only `<h1>`, tab title, last breadcrumb), optional `ViewData["Description"]`; header
     buttons in `@section Actions { }` (primary last).
   - Content in `dnf-card`s (`dnf-card__header` / `dnf-card__body`). Lists: `<div class="dnf-card" data-collection>` >
     `dnf-toolbar` + `div.dnf-table.dnf-table--stack` > `table.dnf-table__table` (`data-label` on each cell),
     `<dnf-empty-state>` for "nothing yet", `[data-collection-empty]` for "no match". Status:
     `<dnf-badge tone="Success">`; dates: `<dnf-timestamp value="@utc" />`. Layout: `dnf-stack`, `dnf-grid`,
     `dnf-split` or Tailwind utilities (`flex gap-2`, `text-sm text-muted`).
   - Forms: `<form method="post" class="dnf-form">` + `@Html.AntiForgeryToken()`, `<dnf-validation-summary />` at the
     top (never `asp-validation-summary` - its empty state is an inline style), `dnf-field` > `label.dnf-field__label` +
     control with its class (`dnf-input`, `dnf-select`, `dnf-textarea`, `dnf-checkbox`, `dnf-switch`) +
     `span.dnf-field__description`, buttons `<dnf-button variant="Primary">` (or `class="dnf-button dnf-button--primary"`),
     `dnf-form-actions`.
   - Feedback: set `TempData["Success"]` / `["Error"]` and redirect - the layout shows a toast; never render flash
     markup. Destructive actions: `data-confirm="..."` (+ `data-confirm-title`, `data-confirm-action`) on the form -
     `dnf.js` opens the confirm dialog.
   - Row/card actions: `<dnf-dropdown>` with `<dnf-dropdown-link>`, `<dnf-dropdown-post>` (antiforgery + `confirm`
     built in), `<dnf-dropdown-separator />` (UI docs: `/admin/docs/ui/components/dropdown`) - never a hand-written dropdown.
   - **No inline `<script>`, `on*=` attributes, `style=` or `<style>`** - the Content-Security-Policy blocks them in
     production. Prefer Dnf components and utilities; a screen-specific block goes into the matching
     `src/DotNetForge.Web/wwwroot/css/admin/*.css` (in `@layer components`, `--dnf-*` tokens only); a reusable
     component into `src/DotNetForge.UI` (`.docs/ui/README.md#where-to-change-things`). Behaviour as a `dnf.js`
     `data-*` hook or a page script in `src/DotNetForge.Web/wwwroot/js/admin/` via `@section Scripts`. New icons go
     into `src/DotNetForge.UI/wwwroot/icons.svg`. A new utility or component class needs `dotnet build` (Tailwind).
6. **Sidebar** - add a `NavLink("Label", "/admin/<slug>", "<icon>", typeof(<Name>Controller))` in the right
   section/group of `AdminNavigation.Tree` (`src/DotNetForge.Web/Areas/Admin/Navigation/AdminNavigation.cs`); when
   replacing a placeholder, change its controller type and drop `Planned: true`. Visibility per role (from the
   controller's `[Authorize]`), active state and breadcrumbs follow - hiding is never the gate.
7. **Tests** - integration test in `tests/DotNetForge.IntegrationTests`: anonymous → 302 `/login`; after
   `factory.InstallAsync()` + sign-in → 200 (see `.docs/guides/testing.md` for the antiforgery pattern). Add the route
   to `AdminUiTests.AdminScreens` (CSP, one `<h1>`, skip link).
8. **Docs** - create/update `.docs/pages/<screen>.md` using
   `.claude/skills/documentation/reference/screen-template.md`; update the route map in `.docs/architecture/pages.md`,
   the Screens table in `.docs/README.md`, `.docs/implementation-status.md` (and `.docs/pages/module-placeholders.md`
   when replacing a placeholder).
9. Run the **verify** skill.

## Don't

- Return a view for a POST success (use PRG).
- Put validation in the view or controller when a service owns the concept.
- Add CSS/JS frameworks, web fonts or a client-side fetch layer; progressive enhancement only, like
  `src/DotNetForge.Web/wwwroot/js/admin/pages.js`.
- Hard-code sidebar links or role checks in a view; use `AdminNavigation.Tree` and `AdminNavModel.Allows(href)`.
- Write files to disk: runtime files go through `IFileStorage` (the deployment directory is read-only).
