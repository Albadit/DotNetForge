# Design system ("Forge")

The look, components and front-end behaviour of every built-in screen: the admin area, setup and sign-in, and the
public fallback views shown while no theme is enabled. One set of tokens, one component library, one script - so every
screen is the same product. It is server-rendered and dependency-free: CSS files, typed Razor tag helpers, partials, an
SVG icon sprite and small vanilla scripts. Every action works without JavaScript; scripts add convenience - search,
filters, sorting, the theme switch and multi-select bulk actions need them (each bulk action also exists per item).

| Part | Location |
| --- | --- |
| Tokens (colour, type, space, radius, shadow, motion; light + dark) | `src/DotNetForge.Web/wwwroot/css/forge/tokens.css` |
| Base (reset, typography, focus, utilities) | `wwwroot/css/forge/base.css` |
| Components (class-based) | `wwwroot/css/forge/components.css` |
| Layouts | `wwwroot/css/forge/admin.css` (admin shell + screen layouts), `auth.css`, `public.css` |
| Icons | `wwwroot/img/icons.svg` (sprite) + `<icon>` tag helper |
| Tag helpers | `src/DotNetForge.Web/Ui/UiTagHelpers.cs` (`<icon>`, `<badge>`, `<alert>`, `<empty-state>`, `<stat-card>`, `<timestamp>`, `<form-errors>`) |
| Presentation helpers | `Ui/AuditPresentation.cs` (audit action labels/icons), `Ui/MediaPresentation.cs` (file kind, size) |
| Behaviours | `wwwroot/js/forge/theme.js` (every layout, in `<head>`), `ui.js` (admin and auth layouts), `nav-state.js` (admin layout, synchronously after the sidebar: open groups and scroll before the first paint), `pages.js` (Pages), `entries.js` (Content Manager), `content-types.js` (Content types builder) |
| Shared partials | `Views/Shared/_Feedback.cshtml` (toasts + confirm dialog) and `_ThemeMenu.cshtml` - admin and auth layouts |
| Navigation | `Areas/Admin/Navigation/AdminNavigation.cs` (tree + `AdminNavigationService`) |

Constraints it is built around: the Content-Security-Policy (`script-src 'self'; style-src 'self'; font-src 'self'`
- no inline script, style attribute, event handler, web font or CDN; see [security](../features/security.md)), the
no-new-dependencies rule ([codebase rule 15](codebase.md#architectural-rules-for-changes)) and server rendering.

## Brand and tokens

The palette comes from the logo (`wwwroot/img/logo*.svg`): charcoal `#14171c` and forge orange `#f76707`.

| Role | Light | Dark | Use |
| --- | --- | --- | --- |
| `--color-bg` / `--color-surface` | `#f7f8f9` / `#fff` | `#0d0f12` / `#16191e` | page / cards |
| `--color-text` / `-muted` / `-subtle` | `#14171c` / `#4d535c` / `#6c727d` | `#eef0f2` / `#a6adb8` / `#8a919c` | text levels (all >= 4.5:1) |
| `--color-accent` + `--color-on-accent` | `#f76707` + charcoal (5.9:1) | same | primary buttons, active states, focus |
| `--color-accent-text` | `#c2410c` (5.2:1) | `#ff9a4d` (8.4:1) | orange **text** and links |
| `--color-success/warning/danger/info` (+ `-soft`) | AA on white | AA on dark | statuses, alerts, badges |
| `--color-nav-*` | white | charcoal | the admin sidebar - follows the theme |
| `--color-brand-*` | white (+ border) | charcoal | brand chrome (dashboard welcome band, sign-in panel) - follows the theme |
| `--color-code-*`, `--color-tooltip-*` | light grey / charcoal | charcoal / light grey | code blocks, tooltips |

Orange is a fill colour; never put white text on it (3.0:1) or use `--forge-500` for text. Type: the system UI stack
(`--font-sans`, Segoe UI Variable / system-ui / Inter) and `--font-mono`; scale `--text-xs` (12px) to `--text-4xl`
(36px), admin body 14px. Spacing is a 4px grid (`--space-1` … `--space-16`); radii `--radius-sm/…/-xl`; shadows
`--shadow-xs/sm/md/lg`; `--duration*` drop to 0 under `prefers-reduced-motion`. The logo's diagonal cut is the one
brand flourish, available as `--forge-cut` (a `clip-path` shape) for accents.

**Rule:** components use semantic roles only - no literal colours, sizes or shadows outside `tokens.css`. Every
surface follows the theme; nothing is dark (or light) in both. Logos come in pairs: `<img class="logo-on-light">` with
`logo.svg` and `<img class="logo-on-dark">` with `logo-dark.svg`, and CSS shows the one for the effective theme.

## Themes

Light and dark, following the system preference (`prefers-color-scheme`) until the user picks one in the theme menu
(sun/moon button: Light, Dark, System). The choice is stored in `localStorage` (`dnf-theme`) and applied as
`<html data-theme="light|dark">` by `theme.js`, which loads synchronously in `<head>` so there is no flash. Both
themes are defined in `tokens.css`; components never test the theme themselves. Admin extension documents that link
`tokens.css` and `theme.js` follow the same choice, and so does the [page builder](../pages/page-builder.md) (its own
light/dark tokens in `editor.css`, the same `dnf-theme` setting; its ⋯ menu changes it for the admin too).

## Layouts

### Admin shell

`Areas/Admin/Views/Shared/_AdminLayout.cshtml`: a **sidebar** (logo, navigation, account menu with "View site"
and "Sign out") that follows the theme - white with the charcoal-lettered `logo.svg` in light, charcoal with
`logo-dark.svg` in dark (both images are in the markup; CSS shows the matching one) - a sticky **top bar** (breadcrumbs, "View site", theme menu) and the **content** column
(max 96rem) starting with the page header. Below 64rem the sidebar becomes a drawer (menu button, backdrop, Escape,
focus moves in and back).

A screen sets:

| What | How |
| --- | --- |
| Title (the only `<h1>`, the tab title, the last breadcrumb) | `ViewData["Title"]` |
| Subtitle | `ViewData["Description"]` (optional) |
| Page width | `ViewData["PageWidth"]` (optional): `"narrow"` (56rem) for create forms, `"medium"` (80rem) for edit screens with a side column - the page, header included, stays centred; default 96rem (`--content-max`) |
| Header buttons | `@section Actions { … }` - the primary action goes last |
| Page scripts | `@section Scripts { <script src="~/js/forge/….js" asp-append-version="true" defer></script> }` |

Navigation is defined once in `AdminNavigation.Tree` (the spec tree: **Main**, **Settings** with four collapsible
sub-groups, plus **Extensions** for enabled admin extensions) and built per request by `AdminNavigationService`: links
the user may not open are hidden (it evaluates each screen controller's `[Authorize]` data with
`IAuthorizationService` - hiding is never the gate), the active link is the longest matching href (so
`/admin/api-tokens/create` marks **API Tokens**), planned modules carry a "Soon" badge, and the breadcrumb trail is
derived from the same tree. `AdminNavModel.Allows(href)` lets a screen show an action only when its target is
reachable (the dashboard's quick actions do). Details: [pages → navigation](pages.md#navigation).

### Auth layout

`Views/Shared/_AuthLayout.cshtml` (setup, sign-in, the two-factor step, "Continuing to" a provider, access denied): a
brand panel (white with `logo.svg` in light, charcoal with `logo-dark.svg` in dark) and the form column; one
column below 56rem. Never affected by public themes. Optional sections: `Head` (extra `<head>` elements, e.g. the
provider page's meta refresh) and `Scripts` (page scripts, e.g. `passkeys.js` on the two-factor step). `auth.css`
also holds the two-factor step's `.input-code` (large monospaced code field) and `.mfa-ways` (the "Try another way"
list).

### Public fallback

`Views/Shared/_Layout.cshtml` + `public.css`: the site root, content pages and the error page while no theme
extension is enabled ([themes](../features/themes.md)). It hosts page modules like a theme does. The header
(`.site-header`) holds the brand, a page menu `nav.site-nav` (`aria-label="Site"`; the top-level live pages marked
"display in menu" from `ViewData["Menu"]`, the current one `aria-current="page"` and bold) and **Admin →**, rendered
only for signed-in users who may enter the admin (`Roles.CanEnterAdmin`). The error page gets no `Menu`, so it shows
no menu.

## Components

Class names are the API. Combine them; don't restyle them per screen.

| Component | Markup |
| --- | --- |
| Button | `.btn` + `.btn-primary` (one per area) / `.btn-secondary` (default look) / `.btn-ghost` / `.btn-danger` / `.btn-danger-soft`; sizes `.btn-sm`, `.btn-lg`; `.btn-icon` (square, needs `aria-label`), `.btn-block`; groups `.btn-group` |
| Form | `<form class="form">` (vertical rhythm), `.form-narrow`, `.form-grid` (two columns, `.span-2`; `.form-grid-3` three columns, one below 40rem), `.form-actions` (`.end`) |
| Field | `.field` > `label.field-label` (+ `span.field-required` / `span.field-optional`) + control + `span.field-hint` (link with `aria-describedby`) |
| Controls | inputs, `select` and `textarea` are styled without classes; `.input-sm`; `.input-icon` (leading icon), `.input-affix` (trailing button) |
| Choices | `label.check` (+ `.check-text`, `.check-hint`); switch `input.switch[role=switch]` in a `label.switch-row`; radio cards `.choice-cards` > `label.choice-card`; `fieldset.fieldset` + `legend` |
| File drop | `.dropzone` wrapping a real `<input type="file">` (+ `[data-dropzone-files]`) |
| Card | `.card` > `.card-header` (`.card-title`, `.card-description`), `.card-body`, `.card-footer`; `.card-danger`, `.card-accent` |
| Grids | `.grid` + `.grid-2/3/4/auto`; `.stack`, `.stack-sm/-lg`, `.cluster`, `.spread` |
| Status | `<badge tone="Success" dot="true">`, `.dot` + `.dot-success/warning/danger` (with an `sr-only` label) |
| Messages | `<alert tone="Warning" title="…">`, `<form-errors />` (all model-state errors - never `asp-validation-summary`, whose empty state has an inline style) |
| Empty state | `<empty-state icon="…" title="…" text="…">actions</empty-state>` (`heading-level="1"` when it is the page's heading, e.g. the error page) |
| KPI | `<stat-card icon="…" label="…" value="…" href="…" />`; inside a card, `.stat-grid` lays out three `.stat` figures in a row (one column below 40rem; [Media Library settings](../pages/media-settings.md)) |
| Time | `<timestamp value="@utc" />` ("5 min ago", exact UTC in the tooltip; `absolute="true"`, `empty="…"`) |
| Table | `.table-wrap` > `table.table` (+ `.table-stack` and `data-label` on each cell for phone layout; first cell `.cell-lead`); `.cell-primary`, `.cell-sub`, `.col-actions`, `.col-check`; `tr.table-empty` |
| Toolbar | `.toolbar` (`.toolbar-search`, `.toolbar-spacer`), `.collection-bar` + `.bulk-bar`, `.pagination` |
| Navigation | `.breadcrumbs`, `.tabs` > `.tab[aria-current]`, `.segmented` (filter buttons with `aria-pressed`) |
| Menu | one component for every menu - [menus](#menus): `<action-menu>` for "⋯" row/card actions; the account and theme menus use the same markup with their own button |
| Dialog | the shared confirm dialog (below); `dialog.dialog` with `.dialog-body/-icon/-title/-text/-footer` for others. Fades (and its backdrop) in on `showModal()` and out on `close()` - CSS transitions with `@starting-style` and `overlay`/`display` `allow-discrete`; browsers without that close at once, `prefers-reduced-motion` turns it off |
| Other | `.list-icon` (a square icon tile at the start of a `.list` row - Sign-in security), `.avatar`, `.dl` (description list), `.code-block` (+ copy button), `.list`, `.skeleton` (`-line`, `-title`), `[data-tooltip]`, `.sr-only`, `.text-muted/-subtle/-sm/-xs/-mono`, `.note` (an icon + `<span>` text: the icon centred on the first line, e.g. `<p class="note text-sm text-muted"><icon name="info" size="sm" /><span>…</span></p>`) |

### Icons

`<icon name="pages" />` renders `<svg class="icon"><use href="/img/icons.svg?v=…#pages"/></svg>`: decorative
(`aria-hidden`) unless `label="…"` is given; `size="sm|lg|xl"`. The sprite holds Lucide paths (ISC licence, noted in the
file). To add one: copy the Lucide `<path>`s into a new `<symbol id="name" viewBox="0 0 24 24">` - the test
`AdminUiTests.Every_icon_the_ui_uses_exists_in_the_sprite` fails when a view, the navigation or a presentation helper
names an icon the sprite lacks. `smartphone` (authenticator app) was added for two-factor authentication.

### Tag helpers

Registered with `@addTagHelper *, DotNetForge.Web` (both `_ViewImports.cshtml`); enum attributes take the member name
(`tone="Danger"`). They share `UiTagHelper` (cache-busted sprite URL, icon markup). An extension view can use them too
by adding the same `@addTagHelper` and `@using DotNetForge.Web.Ui` (the `audit-dashboard` sample does).

### Menus

Every menu (row and card actions on Users, Roles and Media; the account menu; the theme menu) is the same component:
a button with `popovertarget="id"` and a `<div class="menu-list" id="id" popover role="menu" data-placement="…">`.
The list opens in the browser's **top layer**, so no table scroll area, card or stacking order can clip or cover it;
the browser closes it on Escape and outside clicks and keeps one open at a time. `ui.js` places it next to its button
(`data-placement`: `bottom-end` default, `bottom-start`, `top-end`, `top-start`; flipped when there is no room),
keeps it there while the page scrolls, moves focus to the first item for keyboard users, supports arrow keys / Home /
End, and closes it after a choice.

Write row and card menus with the tag helpers (`src/DotNetForge.Web/Ui/MenuTagHelpers.cs`) - never hand-written:

```cshtml
<action-menu label="Actions for @role.Name">                        @* variant="secondary", placement="top-start" *@
    <menu-link href="/admin/roles/@role.Id" icon="pencil">Edit</menu-link>
    <menu-button icon="link" data-copy-text="@url">Copy link</menu-button>
    <menu-post action="/admin/roles/@role.Id/duplicate" icon="copy">Duplicate</menu-post>
    <menu-separator />
    <menu-post action="/admin/roles/@role.Id/delete" icon="trash" danger="true" field-returnTo="list"
               confirm="Delete the role?" confirm-title="Delete role" confirm-action="Delete role">Delete</menu-post>
    <menu-note>Why something is missing</menu-note>
</action-menu>
```

`<menu-post>` renders a form with the antiforgery token, a hidden input per `field-*` attribute and a submit button;
`confirm*` become the form's `data-confirm*` ([feedback](#feedback)).

## Feedback

`_Feedback.cshtml` is rendered by the admin and auth layouts (the public fallback layout has no forms that need it):

- **Toasts** - a screen sets `TempData["Success"]` (auto-hides after 6 s), `TempData["Error"]` or `TempData["Info"]`
  and redirects (PRG); the layout shows it in an `aria-live` region. Screens never render their own flash markup.
  Scripts call `window.Forge.toast(message, "success|danger|info")`.
- **Confirmation** - any form (or submit button) with `data-confirm="Question?"` asks first in a modal `<dialog>`;
  optional `data-confirm-title`, `data-confirm-action` (button text) and `data-confirm-tone="accent"` (non-destructive).
  Without the dialog API it falls back to `window.confirm`; without JS the form submits directly. Scripts call
  `window.Forge.confirm({ title, text, action, tone })` (a promise).
- **Scroll after a POST** - a `method="post"` form that redirects back to the same URL (within 30 s) brings the
  screen back at the scroll position it was submitted from (`ui.js`, `sessionStorage` `dnf-scroll`, restored after
  the screen's own deferred scripts); links, reloads and Back are left to the browser.
- **Validation** - `<form-errors />` at the top of the form; the screen re-renders with the posted values.

## Behaviours

`ui.js` wires these data attributes (all optional enhancements):

| Attribute | Behaviour |
| --- | --- |
| `data-js-only` (+ `hidden`) | revealed only when scripts run (search boxes, view switches, copy buttons) - by the deferred `ui.js`, so after the first paint. Controls that must not appear late (the theme menu) use `html:not(.js) … { display: none }` instead: `theme.js` sets `<html class="js">` in `<head>` |
| `data-collection` on a container, `data-item` on rows/cards | client-side search (`data-collection-search`), filters (`select[data-collection-filter="key"]` or `button[data-collection-filter][data-value][aria-pressed]`, matching a token of the item's `data-<key>` attribute, e.g. `data-collection-filter="status"` ↔ `data-status="enabled"`), sort (`th[data-sort="text|number|date"]`, cell `data-sort-value`), pagination (`data-page-size`, `[data-collection-pagination]`), count (`[data-collection-count]`), no-match state (`[data-collection-empty]`) |
| `data-select-all`, `data-select-item`, `data-bulk-bar`, `data-bulk-count`, `data-bulk-clear` | selection with a bulk bar that overlays the toolbar (no layout shift); item checkboxes post with `form="…"` |
| `data-view-switch` + `data-view-target` + `data-view-button` | grid/list switch, remembered per screen (`data-view-mode` on the target) |
| `data-check-group` / `data-check-member` | "select all" per checkbox group |
| `data-copy-target="#id"` / `data-copy-text` | copy to clipboard with a toast |
| `data-reveal="#password"` | show/hide password |
| `data-tooltip="…"` (+ the same text in `aria-label`) | tooltip on hover and keyboard focus: one shared `.tooltip` in the top layer (`popover="manual"`), so a scrolling or `overflow: hidden` parent never clips it; above the element, below when there is no room, kept inside the window; `--color-tooltip-*` tokens (dark bubble in light theme, light bubble in dark); hidden on scroll, pointer down and Esc |
| `data-fill="#form"` + `data-fill-<field>` | fill a form from a row (Settings → Edit) |
| `details[data-nav-group]` | sidebar sub-group open state remembered across screens (the active group opens and stays open after leaving it); applied by `nav-state.js`, not `ui.js`, so it is in place before the first paint |
| `iframe[data-frame]` | hides the skeleton once an admin extension has loaded |

## Accessibility (WCAG 2.2 AA)

- Contrast: every text role >= 4.5:1, UI boundaries and focus >= 3:1, in both themes (values above).
- Keyboard: a skip link first in every layout; one visible focus style (`:focus-visible`, orange ring); menus (popovers)
  close on Escape and return focus, arrow keys move between items; the drawer and dialog manage focus; native `<dialog>`, `<details>`, `<button>` everywhere.
- Semantics: one `<h1>` per page (the layout's), landmarks (`nav`, `main`, `aside`), `aria-current="page"` for the
  active link and breadcrumb, `aria-sort` on sorted columns, `aria-pressed` on toggles, `role="switch"`, labels for
  every control (visually hidden where the design has none), status dots with text.
- Motion: only short transitions; none under `prefers-reduced-motion`. Touch: controls at least 30-36px, 44px for
  large buttons; tree/menus show their actions without hover on touch devices.
- Enforced by `tests/DotNetForge.IntegrationTests/AdminUiTests.cs`: every admin screen, sign-in and public page renders
  with exactly one `<h1>`, `lang`, the skip link, and no inline style, `<style>`, inline `<script>` or `on…=` handler.

## Building a screen

1. Controller as usual ([admin-page skill](../../.claude/skills/admin-page/SKILL.md)); success → `TempData["Success"]` +
   redirect; validation failure → `ModelState` + re-render.
2. View: `ViewData["Title"]`/`["Description"]`, header buttons in `@section Actions`, content in `.card`s. Lists:
   `.card[data-collection]` > `.toolbar` + `.table-wrap` > `table.table.table-stack` (or a card grid) + `<empty-state>`
   for "nothing yet" and `[data-collection-empty]` for "no match". Forms: `.form` + `.field`s, `<form-errors />`,
   destructive submits with `data-confirm`.
3. Navigation: add the link to `AdminNavigation.Tree` (label, href, icon, controller) - visibility, active state and
   breadcrumbs follow.
4. No inline style or script: new styles go into `components.css` (reusable) or `admin.css` (screen layout) using
   tokens; behaviour as a data attribute in `ui.js` or a page script.

## Extending the system

- **New component**: add a block to `components.css` with its own class prefix, document it in the table above; a tag
  helper in `Ui/UiTagHelpers.cs` when it has structure (derive from `UiTagHelper`).
- **New token**: add it to `tokens.css` for light and both dark blocks; check contrast.
- **New icon**: see [Icons](#icons).
- Don't add CSS frameworks, icon fonts, web fonts or JS libraries - the CSP and the dependency rule forbid them.
