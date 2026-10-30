# Design system

Every built-in screen - the admin area, setup and sign-in, and the public fallback views shown while no theme is
enabled - is built from **DotNetForge UI**, the component library in `src/DotNetForge.UI`. The library (components,
tokens, Tailwind utilities, tag helpers, `dnf.js`, icons) is documented in [`.docs/ui/`](../ui/README.md). This page
keeps only the decisions specific to the CMS: brand, layouts, feedback and how a screen is put together.

| Part | Where |
| --- | --- |
| Library: tokens, components, utilities (`dnf.css`), tag helpers, behaviours (`dnf.js`), theme bootstrap, icon sprite | `src/DotNetForge.UI/` - [ui/README.md](../ui/README.md) |
| CMS screen CSS (shell, screen-specific blocks), in `@layer components` | `src/DotNetForge.Web/wwwroot/css/admin/` (`shell.css`, `content.css`, `media.css`, `tools.css`, `people.css`; `docs.css` for the UI docs only), `css/auth.css`, `css/site.css` |
| Page scripts | `src/DotNetForge.Web/wwwroot/js/admin/` (`nav-state.js` in the admin layout; `pages.js`, `entries.js`, `content-types.js`, `media.js`, `endpoints.js`, `email-templates.js`, `webhooks.js`, `passkeys.js`, `showcase.js` per screen) |
| Presentation helpers | `src/DotNetForge.Web/Ui/` (`AuditPresentation` - audit action labels and icons, `MediaPresentation` - file kind and size) |
| Shared partials | `Views/Shared/_Feedback.cshtml` (toasts + confirm dialog), `_ThemeMenu.cshtml` - admin and auth layouts |
| Navigation | `Areas/Admin/Navigation/AdminNavigation.cs` (tree + `AdminNavigationService`) |
| Component documentation | the **UI docs** (`/admin/docs/ui`, [ui-docs](../pages/ui-docs.md)): guides and a page per component with live examples, code and API |

Constraints: the Content-Security-Policy (`script-src 'self'; style-src 'self'; font-src 'self'` - no inline script,
style attribute, event handler, web font or CDN; [security](../features/security.md)), the no-new-dependencies rule
([codebase rule 15](codebase.md#architectural-rules-for-changes); Tailwind runs only at build time, [styling](../ui/styling.md#tailwind))
and server rendering. Old "Forge" class names: [migration](../ui/migration.md).

## Brand

The palette comes from the logo (`wwwroot/img/logo*.svg`): charcoal `#14171c` and forge orange `#f76707`. Orange is a
fill colour with charcoal on top, never white text on orange; orange text uses `--dnf-primary-text`. Logos come in
pairs - `logo.svg` with `class="block dark:hidden"`, `logo-dark.svg` with `class="hidden dark:block"` - so the image
follows the effective theme. Tokens and contrast: [theming](../ui/theming.md).

## Themes

Light and dark, following the system preference until the user picks one in the theme menu (`_ThemeMenu.cshtml`: Light,
Dark, System, in the admin top bar and on the auth screens). The choice is stored in `localStorage` (`dnf-theme`) and
applied by `dnf-theme.js` before the first paint ([theming → switching](../ui/theming.md#switching)). Admin extension
documents that load `dnf-theme.js` follow the same choice ([extensions](../ui/extensions.md#admin-tab-type-admin)), and
so does the [page builder](../pages/page-builder.md) (its own light/dark tokens in `editor.css`, the same `dnf-theme`
setting; its ⋯ menu changes it for the admin too).

## Layouts

Every layout loads `dnf-theme.js`, then `dnf.css`, then its own CSS ([using the library](../ui/README.md#using-it-in-a-view)).

### Admin shell

`Areas/Admin/Views/Shared/_AdminLayout.cshtml` (+ `css/admin/*.css`, `dnf.js`): a **sidebar** (`[data-drawer-panel]`:
logo pair, navigation from `_Sidebar`, account menu with Profile, View site and Sign out) that follows the theme, a
**top bar** (breadcrumbs `dnf-breadcrumbs--collapse`, View site, theme menu) and the **content** column
(`dnf-container`) starting with the page header. Below 64rem the sidebar is a drawer (menu button, backdrop, Escape,
focus moves in and back - `dnf.js` [drawer hooks](../ui/behaviours.md#drawer-navigation), styled by `shell.css`).
`nav-state.js` runs synchronously right after the sidebar to restore its open groups (`details[data-nav-group]`) and
scroll position before the first paint.

A screen sets:

| What | How |
| --- | --- |
| Title (the only `<h1>`, the tab title, the last breadcrumb) | `ViewData["Title"]` |
| Subtitle | `ViewData["Description"]` (optional) |
| Page width | `ViewData["PageWidth"]` (optional): `"narrow"` (56rem) for create forms, `"medium"` (80rem) for edit screens with a side column (`dnf-container--narrow` / `--medium`); default 96rem (`--dnf-content-max`) |
| Header buttons | `@section Actions { … }` - the primary action goes last |
| Extra `<head>` elements | `@section Head { … }` |
| Page scripts | `@section Scripts { <script src="~/js/admin/….js" asp-append-version="true" defer></script> }` |

Navigation is defined once in `AdminNavigation.Tree` and built per request by `AdminNavigationService`: links the user
may not open are hidden (hiding is never the gate), the active link is the longest matching href, planned modules
carry a "Soon" badge, and the breadcrumb trail comes from the same tree. `AdminNavModel.Allows(href)` lets a screen
show an action only when its target is reachable. Details: [pages → navigation](pages.md#navigation).

### Auth layout

`Views/Shared/_AuthLayout.cshtml` (+ `css/auth.css`, `dnf.js`): setup, sign-in and the two-step verification step,
the self-service account screens and access denied. A brand panel (logo pair, three selling points) and the form
column (`dnf-auth__card`, theme menu at the top); one column on narrow screens. Never affected by public themes.
Optional sections: `Head` and `Scripts` (e.g. `passkeys.js` on the two-step verification step).

### Public fallback

`Views/Shared/_Layout.cshtml` (+ `css/site.css`; no `dnf.js` - nothing there needs a behaviour, but `dnf-theme.js`
applies a saved light/dark choice): the site root, content pages and the error page while no theme extension is
enabled ([themes](../features/themes.md)). It hosts page modules like a theme does. The header holds the brand, a
page menu `nav.dnf-site__nav` (`aria-label="Site"`; the top-level live pages marked "display in menu" from
`ViewData["Menu"]`, the current one `aria-current="page"`) and **Admin →** for signed-in users who may enter the admin
(`Roles.CanEnterAdmin`) - or **Sign out** for a signed-in site-only account. The error page gets no `Menu`, so it shows
no menu. `ViewData["FullWidth"]` (set for page builder layouts) drops the content width.

## Components

The components are the library's: [component catalogue](../ui/README.md#components). Screen-specific blocks that no
generic component covers (the page tree, the media grid, the Plugins cards, the endpoint operation cards, the Monaco
frame, ...) live in the CMS screen CSS and are listed in each file's header comment. Prefer a library component plus
Tailwind utilities; add a CMS block only for genuinely screen-specific structure.

### Icons

The sprite is the library's (`src/DotNetForge.UI/wwwroot/icons.svg`, [icon](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml)). The test
`AdminUiTests.Every_icon_the_ui_uses_exists_in_the_sprite` checks the icon names used in `icon="…"` attributes of
views, the navigation tree, `AuditPresentation` and `MediaPresentation` against it.

### Tag helpers

The `<dnf-*>` tag helpers ([ui/README.md → tag helpers](../ui/README.md#tag-helpers)) are registered in both
`_ViewImports.cshtml` of the web host with `@using DotNetForge.UI`, `@using DotNetForge.UI.Components` and
`@addTagHelper *, DotNetForge.UI`; extension views add the same lines ([extensions](../ui/extensions.md)).

### Menus

Row and card actions (Users, Roles, Media, ...), the account menu and the theme menu are all the
[dropdown](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dropdown.cshtml) component. Write row and card menus with `<dnf-dropdown>` and its item tag
helpers - never hand-written; `<dnf-dropdown-post>` carries the antiforgery token and the `confirm*` attributes.

## Feedback

`_Feedback.cshtml` is rendered by the admin and auth layouts (the public fallback has no forms that need it):

- **Toasts** - a screen sets `TempData["Success"]` (auto-hides after 6 s), `TempData["Error"]` or `TempData["Info"]`
  and redirects (PRG); the layout renders it in the `[data-toasts]` region. Screens never render their own flash
  markup. Scripts call `dnfUI.toast(message, "success|danger|info|warning")` ([toast](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Toast.cshtml)).
- **Confirmation** - any form (or submit button) with `data-confirm="Question?"` asks first in the shared
  `#dnf-confirm-dialog`; optional `data-confirm-title`, `data-confirm-action` (button text) and
  `data-confirm-tone="primary"` (non-destructive). Without JS the form submits directly. Scripts call
  `dnfUI.confirm({ title, text, action, tone })` ([behaviours](../ui/behaviours.md#confirm-dialog)).
- **Scroll after a POST** - a `method="post"` form that redirects back to the same URL (within 30 s) brings the screen
  back at the scroll position it was submitted from ([behaviours](../ui/behaviours.md#scroll-restore-after-a-post)).
- **Validation** - `<dnf-validation-summary />` at the top of the form ([alert](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Alert.cshtml));
  the screen re-renders with the posted values.

## Behaviours

All front-end behaviour is `dnf.js` hooks ([behaviours](../ui/behaviours.md)) or a page script in `wwwroot/js/admin/`.
Every action works without JavaScript; search, filters, sorting, the theme switch and multi-select bulk actions need
it (each bulk action also exists per item). The CMS-only hook is `details[data-nav-group]` (sidebar groups, handled
by `nav-state.js`). An admin extension tab or API Docs frame is an `iframe[data-frame]` in a `[data-frame-host]`;
`shell.css` hides its skeleton once `dnf.js` sets `data-loaded="true"`.

### Collection pagination

Lists page on the client with the library's collection pager ([pagination](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Pagination.cshtml)): rows per
page 10, 25, 50, 100 (plus the screen's own `data-page-size` when it is none of these, e.g. 48 on Media), remembered per
screen and collection in `localStorage` (`dnf-page-size:<path>:<index>`), hidden while the matching items fit 10 rows.
Every list with a pager sets `data-page-size`.

## Accessibility (WCAG 2.2 AA)

- Contrast: every text token ≥ 4.5:1 in both themes ([theming → contrast](../ui/theming.md#contrast)).
- Keyboard: a skip link first in every layout; one visible focus style; menus close on Escape and support the arrow
  keys; the drawer and dialogs manage focus; native `<dialog>`, `<details>`, `<button>` everywhere.
- Semantics: one `<h1>` per page (the layout's), landmarks (`nav`, `main`, `aside`), `aria-current="page"` for the
  active link and breadcrumb, `aria-sort` on sorted columns, `aria-pressed` on toggles, `role="switch"`, labels for
  every control (visually hidden where the design has none), status dots with a label.
- Motion: short transitions only, none under `prefers-reduced-motion`.
- Enforced by `tests/DotNetForge.IntegrationTests/AdminUiTests.cs` (every admin screen, sign-in and public page: one
  `<h1>`, `lang`, the skip link, no inline style, `<style>`, inline `<script>` or `on…=` handler) and the browser tests
  in `tests/DotNetForge.UiTests` (axe-core, [testing → UI tests](../guides/testing.md#ui-tests)).

## Building a screen

1. Controller as usual ([admin-page skill](../../.claude/skills/admin-page/SKILL.md)); success → `TempData["Success"]` +
   redirect; validation failure → `ModelState` + re-render.
2. View: `ViewData["Title"]`/`["Description"]`, header buttons in `@section Actions`, content in `dnf-card`s. Lists:
   `<div class="dnf-card" data-collection>` > `dnf-toolbar` + `div.dnf-table.dnf-table--stack` > `table.dnf-table__table`
   (or a card grid) + `<dnf-empty-state>` for "nothing yet" and `[data-collection-empty]` for "no match"
   ([table](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Table.cshtml)). Forms: `dnf-form` + `dnf-field`s, `<dnf-validation-summary />`, destructive
   submits with `data-confirm` ([form](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Form.cshtml)).
3. Navigation: add the link to `AdminNavigation.Tree` (label, href, icon, controller) - visibility, active state and
   breadcrumbs follow.
4. No inline style or script: components and utilities first; a screen-specific block goes into the matching
   `wwwroot/css/admin/*.css` (in `@layer components`, tokens only); a reusable component goes into the library
   ([ui/README.md → where to change things](../ui/README.md#where-to-change-things)); behaviour as a `dnf.js` hook or a
   page script.
