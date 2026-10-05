---
name: admin-page
description: >-
  Adds or changes a server-rendered admin screen in DotNetForge CMS (src/DotNetForge.Web/Areas/Admin): controller deriving
  AdminControllerBase, attribute route under /admin, role restriction, view model, Razor view with the shared CSS
  classes, POST + antiforgery + audit for mutations, sidebar link, and the matching .docs/pages document. Use when
  asked to build an admin page/screen, replace one of the module placeholders (Marketplace, Content History,
  Internationalization, Transfer, Webhooks, Email Configuration/Templates, Providers, Advanced Settings), or add an
  action/form to an existing admin screen.
---

# Admin screen

Read first: `.docs/architecture/pages.md` (pattern, layouts, conventions) and the screen's doc in `.docs/pages/` if it
exists. Model screens: `ContentController` (forms + service + audit), `SettingsController` (simple upsert),
`AdminListControllers.cs` (read-only lists).

## Steps

1. **Route and controller** - `src/DotNetForge.Web/Areas/Admin/Controllers/<Name>Controller.cs`:
   ```csharp
   [Route("admin/<slug>")]
   [Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]   // omit for every admin-capable role
   public sealed class <Name>Controller : AdminControllerBase
   ```
   - Never inherit from `Controller` directly - `AdminControllerBase` carries `[Area("Admin")]`, the `AdminArea`
     policy, `TenantId` and `CurrentUserId`.
   - Replacing a placeholder: delete its action from `ModulesController` and reuse the **same route** so the sidebar
     link keeps working.
2. **Reads** - query `DotNetForgeDbContext` with `AsNoTracking()`, `Where(x => x.TenantId == TenantId)`, project with
   `Select` into a view model. Put view models in `src/DotNetForge.Web/Areas/Admin/Models/AdminViewModels.cs`.
3. **Rules** - anything beyond a trivial check goes into a `src/DotNetForge.Web/Services/<Thing>Service.cs` (scoped, registered in
   `src/DotNetForge.Web/Startup/DependencyRegistration.cs`) returning an error message or `Result`, like `PageService.ApplyAsync`. The
   controller only maps errors to `ModelState`.
4. **Mutations** - `[HttpPost("<verb>")]` + `[ValidateAntiForgeryToken]`; load the entity **by id and `TenantId`**
   (else `NotFound()`); save; `await _audit.LogAsync(AuditActions.<X>, "<EntityType>", id, display)` (add a constant
   to `src/DotNetForge.Shared/Constants/AuditActions.cs` if needed); `TempData["Success"] = "..."`;
   `RedirectToAction(...)`. Inject `IAuditService`, not the concrete class. For actions governed by the permission
   matrix use `Can(area, action)` / `CanModify(area, any, own, createdById)` from `AdminControllerBase` and return
   `Forbid()` when they fail. Validate string lengths before saving. On validation failure: `ModelState.AddModelError(string.Empty, msg)` and re-render the
   same view with a model built by a shared private `Build...Async` helper.
5. **View** - `src/DotNetForge.Web/Areas/Admin/Views/<Name>/Index.cshtml`, set `ViewData["Title"]` (becomes the `<h1>` and tab title).
   Use existing classes only: `.panel`, `table.data`, `.badge ok|warn`, `.form-narrow`, `.form-row` (`.two`), `.btn`
   `.primary` `.small` `.danger`, `.muted`, `.flash ok`, `<div asp-validation-summary="All" class="validation-summary">`.
   Every form: `method="post"` + `@Html.AntiForgeryToken()`. Destructive actions: `data-confirm="..."` on the form
   (handled by `src/DotNetForge.Web/wwwroot/js/site.js`, loaded by `_AdminLayout`). **No inline `<script>`, `on*=` attributes or
   `<style>`** - the Content-Security-Policy blocks them in production. Show `TempData["Success"]` if you set it.
6. **Sidebar** - `src/DotNetForge.Web/Areas/Admin/Views/Shared/_Sidebar.cshtml`: add `<a class="nav-item @Active("/admin/<slug>")" href="/admin/<slug>">Label</a>`
   in the right group (links are not role-filtered - the controller attribute is the gate).
7. **Tests** - integration test in `tests/DotNetForge.IntegrationTests`: anonymous → 302 `/account/login`; after
   `factory.InstallAsync()` + sign-in → 200 (see `.docs/guides/testing.md` for the antiforgery pattern).
8. **Docs** - create/update `.docs/pages/<screen>.md` using
   `.claude/skills/documentation/reference/screen-template.md`; update the route map in `.docs/architecture/pages.md`,
   the Screens table in `.docs/README.md`, `.docs/implementation-status.md` (and `.docs/pages/module-placeholders.md`
   when replacing a placeholder).
9. Run the **verify** skill.

## Don't

- Query without `TenantId`, take a tenant id from input, or return a view for a POST success (use PRG).
- Put validation in the view or controller when a service owns the concept.
- Add JS frameworks or a client-side fetch layer; progressive enhancement only, like `src/DotNetForge.Web/wwwroot/js/admin-content.js`.
- Write files to disk: runtime files go through `IFileStorage` (the deployment directory is read-only).
