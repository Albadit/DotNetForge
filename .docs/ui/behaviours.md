# Behaviours (dnf.js)

`src/DotNetForge.UI/wwwroot/dnf.js` - one dependency-free script, served at `/_content/DotNetForge.UI/dnf.js`. Load it
with `defer` after the stylesheet ([README](README.md#using-it-in-a-view)). It wires `data-*` hooks and exposes
`window.dnfUI`. State it changes is written as attributes ([states](README.md#states)).

## Progressive enhancement

Every screen must work without JavaScript: forms post, links navigate, `<details>` opens, tabs show all panels
stacked, a confirmation-less form submits directly. The script only adds convenience - client-side search and
filters, sorting, paging, the theme switch and bulk selection need it (so each bulk action should also exist per
item).

Two ways to hide controls that only work with JavaScript:

| Mechanism | Shown | Use for |
| --- | --- | --- |
| `hidden data-js-only` | from the first paint (`dnf-theme.js`, see [below](#before-the-first-paint)); inserted later, when `dnfUI.init` runs | search boxes, view switches, copy buttons |
| `class="dnf-js-only"` | from the first paint (`dnf-theme.js` adds `html.dnf-js` in `<head>`) | controls that must not pop in late (the theme menu) |

### Before the first paint

`dnf.js` is deferred, so over a real network the page can paint before it runs. What it changes in the layout would then
jump, so `dnf-theme.js`, which runs in `<head>`, does that part first: a `MutationObserver` sees each element as the parser
adds it, before the browser can paint it, and stops at `DOMContentLoaded`.

| Element | Done early |
| --- | --- |
| `[data-js-only]` | shown |
| `[data-view-mode]` with an `id` its `[data-view-switch]` targets (the switch comes first in the page) | the saved view, and `aria-pressed` on the switch's buttons |
| `[data-item]` in a `[data-collection]` with `data-page-size` | hidden past the first page, at the saved page size |
| `[data-tree-item]` | `data-collapsed`, from the branches closed in the page tree |
| `[data-tab-panel]` in a `[data-tabs]` (the tabs come first in the group) | the open tab: `hidden` on the other panels, `aria-selected` on the tabs, `data-tabbed` on the root, which hides the panel titles |

`dnf.js` repeats the work afterwards, which changes nothing, and adds the behaviour. A view switch also keeps the first
item in sight on the screen, because the two layouts differ in height. What a screen's own script draws can't start
early, so the server draws skeletons of the same height in its place (the content type fields, the endpoint cards). Without JavaScript none of this
runs, and every item shows. A new behaviour that changes the layout on load belongs in this list.

## CSP

Nothing here needs `unsafe-inline`: no inline script, no `style` attribute in markup, no event-handler attributes.
Configuration travels in `data-*` attributes; text for dialogs and toasts is set with `textContent`. The script sets
`style.top`/`style.left` on floating panels and a custom property on the tooltip through the CSSOM, which
`style-src 'self'` allows.

## Public API

```js
window.dnfUI = { toast, confirm, init, icon };
```

| Member | Does |
| --- | --- |
| `dnfUI.toast(message, tone)` | shows a toast; `tone` `"success"`, `"info"` (default for anything unknown), `"warning"` or `"danger"`. Needs the toast region and template ([toasts](#toasts)). `danger` stays until dismissed (`role="alert"`); the others auto-hide after 6 s (`role="status"`) |
| `dnfUI.confirm({ title, text, action, tone })` | opens the confirmation dialog; returns `Promise<boolean>`. `tone: "primary"` for a non-destructive question (danger otherwise). Falls back to `window.confirm(text)` without the dialog or `showModal` |
| `dnfUI.init(root)` | wires the per-element behaviours (tabs, collections, view switches, file uploads, frames, `data-js-only`, check groups, theme buttons, server toasts) inside markup added after load; each behaviour runs once per element (an element may carry several, e.g. `data-js-only` and `data-view-switch`) |
| `dnfUI.icon(name, size, className)` | the markup of a decorative sprite icon; `size` `"sm"`, `"lg"`, `"xl"` or omitted; `className` extra classes (e.g. `"dnf-button__icon"`). Nothing is encoded - pass known icon ids and class names only |

Delegated behaviours (dropdowns, tooltips, confirm, pending, copy, reveal, fill, drawer, theme switch) work for added
markup without `init`.

## Hooks

### Dropdowns and popovers

Native popovers: `<button popovertarget="id">` + a panel with `popover`. The browser opens the panel in the top layer
and closes it on Escape and outside clicks (one `popover="auto"` open at a time). `dnf.js` adds, for
`.dnf-dropdown__menu[popover]` and `.dnf-popover[popover]`:

- placement next to the opener from `data-placement`: `bottom-end` (default), `bottom-start`, `top-end`,
  `top-start`; flipped above/below when there is no room, kept 8px inside the window, re-placed on scroll and resize;
- `aria-expanded` on the opener and `data-open` on the panel while open;
- for menus opened from the keyboard: focus on the first `.dnf-dropdown__item`, ArrowDown/ArrowUp (wrapping), Home,
  End; a click on an item closes the menu.

```html
<div class="dnf-dropdown">
  <button type="button" class="dnf-button dnf-button--ghost dnf-button--icon-only" popovertarget="row-7"
          aria-haspopup="menu" aria-expanded="false" aria-label="Actions for report.pdf">…</button>
  <div class="dnf-dropdown__menu" id="row-7" popover role="menu" data-placement="bottom-end">
    <a class="dnf-dropdown__item" role="menuitem" href="/edit/7">Edit</a>
  </div>
</div>
```

Prefer `<dnf-dropdown>` ([dropdown](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dropdown.cshtml)); popover panels: [popover](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Popover.cshtml).

### Tooltips

`data-tooltip="Text"` on a focusable element. One shared `.dnf-tooltip` (`role="tooltip"`, `popover="manual"`) shows on
hover and on keyboard focus (`:focus-visible`), above the element or below when there is no room, centred and kept
inside the window (`data-placement`, arrow via `--dnf-tooltip-arrow-x`). Hidden on mouse-out, blur, scroll, pointer
down and Escape. Give the element the same text as its accessible name (`aria-label`) - without JS that is all there
is. [tooltip](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Tooltip.cshtml)

### Tabs

```html
<div data-tabs data-initial-tab="general">
  <div class="dnf-tabs" role="tablist">
    <button type="button" class="dnf-tabs__tab" role="tab" data-tab="general">General</button>
    <button type="button" class="dnf-tabs__tab" role="tab" data-tab="seo">SEO</button>
  </div>
  <section class="dnf-tab-panel" data-tab-panel="general"><h2 class="dnf-tab-panel__title">General</h2>…</section>
  <section class="dnf-tab-panel" data-tab-panel="seo"><h2 class="dnf-tab-panel__title">SEO</h2>…</section>
</div>
```

The root gets `data-tabbed="true"`; one panel shows (others `hidden`), tabs get `aria-selected` and a roving
`tabindex`; ArrowLeft/ArrowRight (wrapping), Home and End move between tabs. The open tab is written to the URL hash
(`#seo`, `history.replaceState`), so a reload or a redirect to `...#seo` reopens it; `hashchange` switches too. First
tab: the hash, else `data-initial-tab`, else the first tab. Inside a form, a field with `data-tab-field` receives the
open tab's name, and a field that fails native validation on a hidden tab opens its tab. With
`data-tab-hash="false"` on the root the hash is neither read nor written (many small tab groups on one page, like the
UI docs' example frames). A root only manages its own tabs and panels - a panel may hold another tab group. The open
tab is in place [before the first paint](#before-the-first-paint), so a reload never shows another tab first. A group
added later shows only its first panel until `dnf.js` has wired it (`dnf.css`, with `html.dnf-js`); without JavaScript
every panel shows.
[tabs](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Tabs.cshtml)

### Dialogs

```html
<button type="button" class="dnf-button dnf-button--secondary" data-dialog-open="#new-folder">New folder</button>
<dialog class="dnf-dialog dnf-dialog--md" id="new-folder" aria-labelledby="new-folder-title" data-dialog-light-dismiss>
  …<button type="button" class="dnf-button dnf-button--secondary" data-dialog-close>Cancel</button>…
</dialog>
```

`data-dialog-open="<selector>"` opens that `<dialog>` with `showModal()` (focus moves in, Escape closes it, the page
behind is inert); `data-dialog-close` closes the dialog it is in (a `<form method="dialog">` button does too, and sets
`returnValue`); `data-dialog-light-dismiss` on the dialog closes it on a click on the backdrop.

### Drawer navigation

```html
<div data-shell>
  <nav data-drawer-panel>… <button type="button" data-drawer-close aria-label="Close navigation">…</button></nav>
  <div data-drawer-backdrop data-drawer-close hidden></div>
  <button type="button" data-drawer-open aria-expanded="false" aria-label="Open navigation">…</button>
</div>
```

`[data-drawer-open]` sets `data-nav-open="true"` on the first `[data-shell]` of the page, shows every
`[data-drawer-backdrop]`, sets `aria-expanded` and focuses the panel's `a[aria-current]` (else its first link).
`[data-drawer-close]`, Escape, and a window at least `64.0625rem` wide close it and return focus to the open button.
The library has no drawer CSS: the layout styles `[data-nav-open="true"]` (the CMS admin shell does).

### Scroll restore after a POST

When a `method="post"` form is submitted (and not cancelled), the URL and scroll position are saved in
`sessionStorage` (`dnf-scroll`). If the next page has the same path and query within 30 s, it scrolls back after
`DOMContentLoaded` (after every deferred screen script). Links, reloads and Back are untouched. Nothing to add to the
markup.

### Toasts

```html
<div class="dnf-toast-region" data-toasts aria-live="polite">
  <!-- toasts rendered by the server: data-toast (+ data-autohide="true") -->
</div>
<template id="dnf-toast-template">
  <div class="dnf-toast" role="status" data-toast>
    <svg class="dnf-icon dnf-toast__icon" aria-hidden="true"><use href="/_content/DotNetForge.UI/icons.svg#info"></use></svg>
    <p class="dnf-toast__message"></p>
    <button type="button" class="dnf-button dnf-button--ghost dnf-button--sm dnf-button--icon-only" data-toast-close
            aria-label="Dismiss">…</button>
  </div>
</template>
```

`dnfUI.toast` clones the template's first element, adds `dnf-toast--<tone>`, sets the role, the message and the icon
(`circle-check`, `circle-x`, `info`, `triangle-alert` - the `href` fragment of `.dnf-toast__icon use` is replaced) and
appends it to `[data-toasts]`. On load, server-rendered `[data-toasts] [data-toast]` elements are re-inserted (so
screen readers announce them) and armed. `data-autohide="true"`: removed after 6 s unless hovered or focused.
`[data-toast-close]` dismisses its `[data-toast]` (`data-leaving="true"`, removed after 200 ms). [toast](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Toast.cshtml)

### Confirm dialog

```html
<form method="post" action="/admin/pages/7/delete" data-confirm="Delete the page?"
      data-confirm-title="Delete page" data-confirm-action="Delete" data-confirm-tone="danger">…</form>
```

On submit of a form with `data-confirm` - or through a submit button that carries `data-confirm*` itself - the
submit is cancelled, the dialog asks, and Yes re-submits with the same submitter (`requestSubmit`). Attributes:
`data-confirm` (the question), `data-confirm-title` (default "Are you sure?"), `data-confirm-action` (OK button text,
default "Confirm"), `data-confirm-tone="primary"` (non-destructive; anything else is danger). Without JS the form
submits directly. The dialog markup (once per layout; the CMS renders it in `Views/Shared/_Feedback.cshtml`):

```html
<dialog class="dnf-dialog" id="dnf-confirm-dialog" aria-labelledby="confirm-title" aria-describedby="confirm-text">
  <form method="dialog">
    <div class="dnf-dialog__header">
      <span class="dnf-dialog__icon dnf-dialog__icon--danger" data-confirm-icon>…</span>
      <div>
        <h2 class="dnf-dialog__title" id="confirm-title" data-confirm-title>Are you sure?</h2>
        <p class="dnf-dialog__description" id="confirm-text" data-confirm-text></p>
      </div>
    </div>
    <div class="dnf-dialog__footer">
      <button type="submit" value="cancel" class="dnf-button dnf-button--secondary">Cancel</button>
      <button type="submit" value="confirm" class="dnf-button dnf-button--danger" data-confirm-ok>Confirm</button>
    </div>
  </form>
</dialog>
```

The result is `returnValue === "confirm"`. The script replaces the OK button's and the icon's `className` per call
(`dnf-button dnf-button--danger|--primary`, `dnf-dialog__icon` ± `--danger`), so don't put other classes on them.

### Pending on submit

`<form data-pending-on-submit>`: on a submit that was not cancelled, the submitter (else the first `[type=submit]`)
gets `data-pending="true"` and `aria-busy="true"` - a [button](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Button.cshtml) shows its spinner and ignores
further clicks until the next page loads.

### Collections

Client-side search, filters, sorting, paging and selection over the items already in the page.

```html
<div class="dnf-card" data-collection data-page-size="25">
  <div class="dnf-collection-bar">
    <div class="dnf-toolbar">
      <input class="dnf-input dnf-toolbar__search" type="search" data-collection-search aria-label="Search" hidden data-js-only />
      <select class="dnf-select" data-collection-filter="status" aria-label="Status">
        <option value="">All</option><option value="enabled">Enabled</option>
      </select>
      <span class="dnf-toolbar__spacer"></span>
      <span class="text-sm text-muted" data-collection-count></span>
    </div>
    <div class="dnf-bulk-bar" data-bulk-bar hidden>
      <span class="dnf-bulk-bar__count" data-bulk-count></span>
      <button type="submit" form="bulk" class="dnf-button dnf-button--danger dnf-button--sm">Delete</button>
      <button type="button" class="dnf-button dnf-button--ghost dnf-button--sm" data-bulk-clear>Clear</button>
    </div>
  </div>
  <div class="dnf-table">
    <table class="dnf-table__table">
      <thead><tr>
        <th class="dnf-table__cell--check"><input type="checkbox" class="dnf-checkbox" data-select-all aria-label="Select all" /></th>
        <th data-sort="text">Name</th><th data-sort="date">Updated</th>
      </tr></thead>
      <tbody>
        <tr data-item data-status="enabled" data-search="report pdf">
          <td class="dnf-table__cell--check"><input type="checkbox" class="dnf-checkbox" data-select-item form="bulk" name="ids" value="7" aria-label="Select report.pdf" /></td>
          <td>report.pdf</td><td data-sort-value="2026-10-03">3 Oct 2026</td>
        </tr>
      </tbody>
    </table>
  </div>
  <p class="dnf-empty-state dnf-empty-state--compact" data-collection-empty hidden>No match.</p>
  <div class="dnf-pagination" data-collection-pagination hidden></div>
</div>
<form id="bulk" method="post" action="/admin/media/delete-selected">…</form>
```

| Hook | Behaviour |
| --- | --- |
| `[data-collection]` | the scope; `[data-item]` elements inside are the items |
| `[data-collection-search]` | input: an item matches when its `data-search` (else its text) contains the query (case-insensitive) |
| `select[data-collection-filter="key"]` | an item matches when the select's value is one of the whitespace-separated tokens of its `data-<key>`; empty value = no filter |
| `button[data-collection-filter="key"][data-value]` | the same as exclusive toggle buttons: the clicked one gets `aria-pressed="true"`, the others with that key `"false"` (put them in a [segmented control](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Segmented.cshtml)) |
| `th[data-sort="text\|number\|date"]` | turned into a `.dnf-table__sort` button with a `sort` icon; click sorts the rows of the first `tbody` by that column's `data-sort-value` (else text), toggling `aria-sort` ascending/descending |
| `data-page-size="N"` + `[data-collection-pagination]` | paging; the pager is rendered into the element ([pagination](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Pagination.cshtml)). Without `data-page-size` (or `0`) everything shows |
| `[data-collection-count]` | `"N total"` or `"X of N"` |
| `[data-collection-empty]` | shown when nothing matches (and there are items) |
| `[data-select-all]` | checks every visible `[data-select-item]`; shows checked / indeterminate |
| `[data-select-item]` | a selected item's `[data-item]` gets `data-selected="true"` |
| `[data-bulk-bar]`, `[data-bulk-count]`, `[data-bulk-clear]` | the bar shows while something is selected (`"N selected"`); clear unchecks all |

Search, filter and page-size changes go back to page 1. Item checkboxes post with `form="…"`, so they can live in the
table while the bulk form is elsewhere.

### View switch

```html
<div class="dnf-segmented" data-view-switch="media" data-view-target="#items" data-default="grid" hidden data-js-only>
  <button type="button" class="dnf-segmented__item dnf-segmented__item--icon" data-view-button="grid" aria-label="Grid">…</button>
  <button type="button" class="dnf-segmented__item dnf-segmented__item--icon" data-view-button="list" aria-label="List">…</button>
</div>
```

Sets `data-view-mode="grid|list"` on the target and `aria-pressed` on the buttons; remembered per switch in
`localStorage` (`dnf-view:<name>`). The library has no `data-view-mode` CSS: the screen lays the same items out for
each mode.

### Check-all groups

`<input type="checkbox" data-check-group="content">` checks or clears every `[data-check-member="content"]`; it shows
checked when all members are and indeterminate when some are (also on load).

### Copy

`<button data-copy-text="value">` or `<button data-copy-target="#id">` (an `<input>`'s value, else the element's
text) copies to the clipboard (trimmed) and shows a toast ("Copied to clipboard." or an error).

### Password reveal

```html
<input class="dnf-input" type="password" id="password" />
<button type="button" class="dnf-button dnf-button--ghost dnf-button--icon-only" data-reveal="#password"
        aria-pressed="false" aria-label="Show password">
  <svg class="dnf-icon" data-reveal-icon="show" aria-hidden="true">…eye…</svg>
  <svg class="dnf-icon" data-reveal-icon="hide" aria-hidden="true" hidden>…eye-off…</svg>
</button>
```

Toggles the input between `password` and `text`, `aria-pressed`, the label ("Show password" / "Hide password") and
which `[data-reveal-icon]` is hidden. Put it in an [input group](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/InputGroup.cshtml) as
`.dnf-input-group__action`.

### File upload

On every `.dnf-file-upload` (wired by `init`): `data-dragging="true"` while files are dragged over it, and after a
choice the names are listed as `dnf-badge dnf-badge--primary` badges in `[data-file-upload-files]`.
[file upload](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/FileUpload.cshtml)

### Fill a form

`<button data-fill="#settings-form" data-fill-key="site.name" data-fill-value="DotNetForge">Edit</button>` sets each
named field of the form from the matching `data-fill-<name>` attribute (`form.elements.namedItem(name)`), then
focuses the form's first control. HTML lower-cases attribute names, so this suits lower-case field names.

### Frames

`<iframe data-frame>` inside a `[data-frame-host]`: the host gets `data-loaded="true"` once the frame has loaded (also
when it already had). The host's own CSS decides what that shows (the CMS hides an extension tab's skeleton).

### Theme switch

`[data-theme-set="light|dark|system"]` - [theming → switching](theming.md#switching).

## Storage keys

| Key | Storage | Holds |
| --- | --- | --- |
| `dnf-theme` | `localStorage` | `light` / `dark` (absent = system) |
| `dnf-page-size:<path>:<index>` | `localStorage` | rows per page of the collection at that position |
| `dnf-view:<name>` | `localStorage` | the view of a `data-view-switch` |
| `dnf-scroll` | `sessionStorage` | scroll position of the last POST |

Blocked storage (private mode) is caught everywhere; the behaviour then falls back to defaults.

## Planned (not implemented)

Nothing is specified beyond what is built.
