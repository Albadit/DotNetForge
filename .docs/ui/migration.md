# Migrating from the Forge classes

DotNetForge UI replaced the earlier "Forge" design system (`wwwroot/css/forge/*.css`, `wwwroot/js/forge/ui.js`, the
`<icon>`/`<badge>`/... tag helpers in `DotNetForge.Web.Ui`). The old class names, files and tag helpers are **gone**:
there are no aliases. This page maps every old name to its replacement, for extension authors updating their views
and for anyone reading an old screenshot or commit.

Rules of the new system (details in [README.md](README.md) and [styling.md](styling.md)):

- Component classes are BEM with the `dnf-` prefix: block `.dnf-card`, element `.dnf-card__title`, modifier
  `.dnf-card--danger`. A modifier is always written **with** its block: `class="dnf-button dnf-button--primary"`.
- Plain layout and typography are **Tailwind utilities** (`flex`, `gap-2`, `text-sm`, `text-muted`, `truncate`, ...).
  The theme maps them to the DotNetForge tokens: `text-muted`, `text-subtle`, `text-danger`, `bg-surface`,
  `border-border`, `text-sm` (13px), `rounded-md` ...
- State is an attribute, never a class: `data-selected="true"`, `data-dragging="true"`, `data-open`,
  `data-invalid="true"`, `data-pending`, `data-loaded="true"`, `aria-pressed`, `aria-current`, `aria-expanded`,
  `[hidden]`. The old `is-*` / `has-*` classes are gone.
- Every form control carries its class. Tailwind's reset (preflight) no longer gives bare `<input>`, `<select>` or
  `<textarea>` elements a look - and it removes list bullets and heading sizes (add `list-disc ps-5` where a list
  needs bullets).
- Sizes in Razor tag helpers are enum names: `size="Sm"`, `variant="Primary"`, `tone="Success"`.

## Files

| Old | New |
|---|---|
| `/css/forge/tokens.css`, `base.css`, `components.css` | `/_content/DotNetForge.UI/dnf.css` (one file: tokens, base, components, utilities) |
| `/js/forge/theme.js` | `/_content/DotNetForge.UI/dnf-theme.js` (sets `html.dnf-js`, was `html.js`) |
| `/js/forge/ui.js` (`window.Forge`) | `/_content/DotNetForge.UI/dnf.js` (`window.dnfUI`: `toast`, `confirm`, `init`, `icon`) |
| `/img/icons.svg` | `/_content/DotNetForge.UI/icons.svg` |
| `/css/forge/admin.css`, `auth.css`, `public.css` | CMS-internal: `/css/admin/*.css`, `/css/auth.css`, `/css/site.css` (not for extensions) |
| `@addTagHelper *, DotNetForge.Web` + `@using DotNetForge.Web.Ui` | `@addTagHelper *, DotNetForge.UI` + `@using DotNetForge.UI` + `@using DotNetForge.UI.Components` |

## Tag helpers

| Old | New | Notes |
|---|---|---|
| `<icon name size="sm" label>` | `<dnf-icon name size="Sm" label>` | sizes `Sm`, `Lg`, `Xl` |
| `<badge tone="Success" dot>` | `<dnf-badge tone="Success" dot outline>` | `Tone.X` → `DnfTone.X`; `Tone.Accent` → `DnfTone.Primary` |
| `<alert tone title>` | `<dnf-alert tone title>` | |
| `<empty-state icon title text compact heading-level>` | `<dnf-empty-state icon title description compact heading-level>` | `text` → `description` |
| `<stat-card icon label value href>` | `<dnf-stat-card icon label value href bare>` | |
| `<timestamp value empty absolute>` | `<dnf-timestamp ...>` | `TimestampTagHelper.Relative/Format` → `DnfTimestampTagHelper.Relative/Format` |
| `<form-errors />` | `<dnf-validation-summary />` | |
| `<action-menu label icon variant="secondary" placement>` | `<dnf-dropdown label icon variant="Secondary" placement text size menu-class>` | |
| `<menu-link>`, `<menu-button>`, `<menu-post>`, `<menu-separator />`, `<menu-note>` | `<dnf-dropdown-link>`, `<dnf-dropdown-button>`, `<dnf-dropdown-post>`, `<dnf-dropdown-separator />`, `<dnf-dropdown-note>` | `danger="true"` also on link/button; `confirm-tone="accent"` → `"primary"`; new `<dnf-dropdown-label>` |
| — | `<dnf-button variant size icon icon-end icon-only label pending full-width disabled href>` | optional; `class="dnf-button ..."` on `<button>`/`<a asp-...>` is equally fine |
| — | `<dnf-spinner>`, `<dnf-avatar name src size>`, `<dnf-status-dot tone label>` | |

## Classes

### Buttons

| Old | New |
|---|---|
| `btn` | `dnf-button` (+ a variant; plain `btn` = `dnf-button dnf-button--secondary`) |
| `btn-primary` / `btn-secondary` / `btn-ghost` / `btn-danger` / `btn-danger-soft` | `dnf-button--primary` / `--secondary` / `--ghost` / `--danger` / `--danger-soft` |
| — | `dnf-button--tertiary`, `dnf-button--outline` |
| `btn-sm` / `btn-lg` | `dnf-button--sm` / `dnf-button--lg` |
| `btn-icon` | `dnf-button--icon-only` (give it `aria-label`) |
| `btn-block` | `dnf-button--full-width` |
| `btn-group` | `dnf-button-group` (`--attached` joins them) |
| an `<icon>` inside a button | `<dnf-icon name="x" class="dnf-button__icon" />` (optional element class) |

### Layout and text (now Tailwind or layout primitives)

| Old | New |
|---|---|
| `stack` / `stack-sm` / `stack-lg` | `dnf-stack` / `dnf-stack dnf-stack--sm` / `dnf-stack dnf-stack--lg` |
| `cluster` / `cluster-lg` | `dnf-cluster` / `dnf-cluster dnf-cluster--lg` |
| `spread` | `dnf-spread` |
| `grow` | `grow min-w-0` |
| `inline` | `inline` (Tailwind) |
| `grid grid-2/3/4/auto` | `dnf-grid dnf-grid--cols-2/3/4/auto` |
| `editor` / `editor-main` / `editor-aside` | `dnf-split` / `dnf-split__main` / `dnf-split__aside` |
| `settings-layout` | `dnf-split dnf-split--wide-aside` |
| `editor-title` | `flex flex-wrap items-center gap-2` |
| `content-narrow` / `content-medium` (page widths) | `dnf-container--narrow` / `dnf-container--medium` |
| `sr-only` | `sr-only` (Tailwind) |
| `text-muted` / `text-subtle` / `text-danger` / `text-success` | same names - now Tailwind colour utilities |
| `text-sm` / `text-xs` | same names (Tailwind, on the DotNetForge scale) |
| `text-mono` | `font-mono` |
| `text-nowrap` | `text-nowrap` (Tailwind) |
| `text-truncate` | `truncate` |
| `weight-medium` / `weight-semibold` | `font-medium` / `font-semibold` |
| `note` | `dnf-note` |
| `skip-link` | `dnf-skip-link` |
| `logo-on-light` / `logo-on-dark` | `block dark:hidden` / `hidden dark:block` |
| `theme-icon-light` / `theme-icon-dark` | `inline dark:hidden` / `hidden dark:inline` |
| `theme-menu` (hidden without JS) | `dnf-js-only` |
| `ext-error` (rendered by the host for a failed widget/module) | `dnf-ext-error` - themes style it too |

### Cards and stats

| Old | New |
|---|---|
| `card` / `card-header` / `card-title` / `card-description` / `card-body` / `card-footer` | `dnf-card` / `dnf-card__header` / `dnf-card__title` / `dnf-card__description` / `dnf-card__body` / `dnf-card__footer` |
| `card-danger` / `card-accent` | `dnf-card--danger` / `dnf-card--accent` |
| `stat` / `stat-icon` / `stat-text` / `stat-value` / `stat-label` | `dnf-stat-card` / `__icon` / `__text` / `__value` / `__label` |
| `stat-grid` | `dnf-stat-group` |

### Forms

| Old | New |
|---|---|
| `form` / `form-narrow` | `dnf-form` / `dnf-form dnf-form--narrow` |
| `form-grid` / `form-grid-3` | `dnf-form-grid` / `dnf-form-grid dnf-form-grid--3` |
| `span-2` (in a form grid) | `dnf-form-grid__full` |
| `form-actions` / `form-actions end` | `dnf-form-actions` / `dnf-form-actions dnf-form-actions--end` |
| `field` | `dnf-field` (invalid: `data-invalid="true"`, was `has-error`) |
| `field-label` / `field-required` / `field-optional` / `field-hint` / `field-error` | `dnf-field__label` / `__required` / `__optional` / `__description` / `__error` |
| bare `input` (text, email, password, number, url, search, tel, date, ...) | `<input class="dnf-input">` |
| `input-sm` | `dnf-input dnf-input--sm` / `dnf-select dnf-select--sm` |
| `input-code` | `dnf-input dnf-input--code` |
| bare `select` / `textarea` | `dnf-select` / `dnf-textarea` (`dnf-textarea--code` for code, `dnf-input--mono`) |
| `input-icon` (leading icon) | `dnf-input-group` with `<dnf-icon class="dnf-input-group__icon">` |
| `input-affix` (trailing button) | `dnf-input-group` with the button as `dnf-input-group__action` |
| `endpoint-path` + `endpoint-path-prefix` | `dnf-input-group` + `dnf-input-group__prefix` |
| bare checkbox / radio | `dnf-checkbox` / `dnf-radio` |
| `check` / `check-text` / `check-hint` | `dnf-check` / `dnf-check__text` / `dnf-check__description` |
| `choice-list` + `choice-row` | `dnf-check-list` + `dnf-check` |
| `switch` (on the input) / `switch-row` | `dnf-switch` / `dnf-switch-row` |
| `choice-cards` / `choice-card` | `dnf-choice-cards` / `dnf-choice-card` |
| `template-grid` / `template-card` / `template-text` | `dnf-choice-cards dnf-choice-cards--wide` / `dnf-choice-card dnf-choice-card--detailed` / `dnf-choice-card__text` (+ `__title`, `__description`) |
| `fieldset` / its `legend` | `dnf-fieldset` / `dnf-fieldset__legend` |
| `field-group` | `dnf-fieldset dnf-fieldset--framed` |
| `dropzone` / `dropzone-title` / `dropzone-files` / `is-dragover` | `dnf-file-upload` / `__title` / `__files` (`data-file-upload-files`) / `data-dragging="true"`; the input gets `dnf-file-upload__input` |
| `validation-summary-errors` | `dnf-validation-summary` (rendered by `<dnf-validation-summary />`) |

ASP.NET's own validation classes (`input-validation-error`, `field-validation-error`, `field-validation-valid`) are
added by the framework, not written in markup; the input styles honour `input-validation-error`, and an
`asp-validation-for` span gets `class="dnf-field__error"` (hidden while empty).

### Status

| Old | New |
|---|---|
| `badge` + `badge-success/warning/danger/info/accent` | `dnf-badge` + `dnf-badge--success/warning/danger/info/primary` |
| `badge-outline` / `badge-dot` | `dnf-badge--outline` / `dnf-badge--dot` |
| `method-badge` | `dnf-badge--mono` |
| `dot` / `dot-success/warning/danger` | `dnf-status-dot` / `dnf-status-dot--success/warning/danger` |
| `field-badge`, `relation-chip` | `dnf-badge`, `dnf-chip` (+ `dnf-chip__remove`), `relation-chips` → `dnf-chip-list` |
| `alert` / `alert-success...` / `alert-title` / `alert-body` | `dnf-alert` / `dnf-alert--success...` / `dnf-alert__title` / `dnf-alert__body` (icon: `dnf-alert__icon`) |
| `toast-region` / `toast` / `toast-success...` / `toast-message` / `is-leaving` | `dnf-toast-region` / `dnf-toast` / `dnf-toast--success...` / `dnf-toast__message` / `data-leaving="true"` (icon `dnf-toast__icon`; template id `dnf-toast-template`) |
| `avatar` / `avatar-sm` / `avatar-lg` | `dnf-avatar` / `--sm` / `--lg` (or `<dnf-avatar>`) |
| `skeleton` / `skeleton-line` (`medium`, `short`) / `skeleton-title` | `dnf-skeleton` / `dnf-skeleton dnf-skeleton--line` (+ `w-2/3`, `w-2/5`) / `dnf-skeleton dnf-skeleton--title` |
| — | `dnf-spinner`, `dnf-progress`, `dnf-divider` (`--label` for "or"; was `auth-divider`) |

### Tables and collections

| Old | New |
|---|---|
| `table-wrap` > `table.table` | `div.dnf-table` > `table.dnf-table__table` |
| `table-stack` | `dnf-table--stack` (on the wrapper) |
| `col-check` / `col-actions` / `col-check-center` | `dnf-table__cell--check` / `--actions` / `--center` (on `th` and `td`) |
| `cell-lead` / `cell-primary` / `cell-sub` | `dnf-table__cell--lead` / `dnf-table__primary` / `dnf-table__secondary` |
| `table-empty` | `dnf-table__empty` (on the `tr`) |
| `th-sort` | `dnf-table__sort` (dnf.js renders it) |
| `row-grip` | `dnf-table__grip` |
| `tr.is-selected` / `tr.is-dragging` | `data-selected="true"` / `data-dragging="true"` |
| `permission-table` | `dnf-table` (row headers `th[scope=row]` are bold by default) |
| `toolbar` / `toolbar-search` / `toolbar-spacer` | `dnf-toolbar` / `dnf-toolbar__search` / `dnf-toolbar__spacer` |
| `collection-bar` / `bulk-bar` / `bulk-select` | `dnf-collection-bar` / `dnf-bulk-bar` / `dnf-select` in it (`dnf-bulk-bar__count` for the count) |
| `pagination` (+ `pagination-*` parts) | `dnf-pagination` (dnf.js renders `__info`, `__size`, `__summary`, `__pages`, `__gap`) |

### Navigation and overlays

| Old | New |
|---|---|
| `page-header` / `page-header-row` / `page-title` / `page-description` / `page-actions` | `dnf-page-header` / `__row` / `__title` / `__description` / `__actions` |
| `breadcrumbs` > `ol` > `li` | `nav.dnf-breadcrumbs` > `ol.dnf-breadcrumbs__list` > `li.dnf-breadcrumbs__item` (`dnf-breadcrumbs--collapse` in top bars) |
| `tabs` / `tab` | `dnf-tabs` / `dnf-tabs__tab` |
| `tab-panel` / `tab-panel-title` / root `is-tabbed` | `dnf-tab-panel` / `dnf-tab-panel__title` / `data-tabbed="true"` |
| `segmented` + its buttons | `dnf-segmented` + `dnf-segmented__item` |
| `menu` / `menu-list` / `menu-item` (`danger`) / `menu-label` / `menu-separator` / `menu-note` | `dnf-dropdown` / `dnf-dropdown__menu` / `dnf-dropdown__item` (`--danger`) / `__label` / `__separator` / `__note` |
| `dialog` / `dialog-body` / `dialog-icon` (`danger`) / `dialog-title` / `dialog-text` / `dialog-footer` | `dnf-dialog` / `dnf-dialog__header` + `dnf-dialog__body` / `dnf-dialog__icon` (`--danger`) / `__title` / `__description` / `__footer` |
| `media-dialog`, `folder-dialog`, `file-dialog`, `upload-dialog`, `media-preview` widths | `dnf-dialog--xl` / `--md` / `--md` / `dnf-dialog--lg dnf-dialog--bare` / `--xl` |
| `tooltip` | `dnf-tooltip` (dnf.js) |
| `extension-problem` popover | `dnf-popover dnf-popover--bare` |
| `empty-state` / `-icon` / `-title` / `-text` / `-actions` / `compact` | `dnf-empty-state` / `__icon` / `__title` / `__description` / `__actions` / `dnf-empty-state--compact` |
| `dl` / `dl-compact` | `dnf-description-list` / `dnf-description-list dnf-description-list--compact` |
| `code-block` (+ its copy `.btn`) / `is-error` | `dnf-code-block` (+ `dnf-code-block__copy`) / `data-invalid="true"` |
| `entry-api`, `delivery-code` | `dnf-code-block dnf-code-block--inline`, `dnf-code-block dnf-code-block--scroll` |
| `list` > `li` | `ul.dnf-list` > `li.dnf-list__item` |
| `list-icon` / `activity-icon` (`fail`) | `dnf-list__icon` / `dnf-list__icon dnf-list__icon--round` (`dnf-list__icon--danger`) |
| `mfa-text` | `dnf-list__content` |

### Behaviour hooks (dnf.js)

| Old | New |
|---|---|
| `[data-shell]` + `.sidebar` + `.sidebar-backdrop` + `is-nav-open` | `[data-shell]` + `[data-drawer-panel]` + `[data-drawer-backdrop]`; the shell gets `data-nav-open="true"` |
| `.dropzone` | `.dnf-file-upload`, file list `[data-file-upload-files]` |
| `.ext-frame-card` + `is-loaded` | `[data-frame-host]` gets `data-loaded="true"` |
| `#toast-template`, `#confirm-dialog`, `.dialog-title`, `.dialog-text` | `#dnf-toast-template`, `#dnf-confirm-dialog`, `[data-confirm-title]`, `[data-confirm-text]` |
| `data-confirm-tone="accent"` | `data-confirm-tone="primary"` |
| `Forge.toast(...)`, `Forge.confirm(...)` | `dnfUI.toast(...)`, `dnfUI.confirm(...)` |
| — | `dnfUI.init(root)` after inserting markup; `<form data-pending-on-submit>` |

Unchanged hooks: `data-collection*`, `data-item`, `data-sort`, `data-select-*`, `data-bulk-*`, `data-tabs`/`data-tab`/
`data-tab-panel`/`data-tab-field`, `data-view-switch`, `data-check-group`/`-member`, `data-copy-*`, `data-reveal`,
`data-fill`, `data-tooltip`, `data-confirm*`, `data-js-only`, `data-theme-set`, `data-drawer-open`/`-close`.

## Tokens

Every custom property now starts with `--dnf-` ([theming.md](theming.md)).

| Old | New |
|---|---|
| `--color-text` / `-text-muted` / `-text-subtle` / `-text-inverse` | `--dnf-foreground` / `--dnf-muted-foreground` / `--dnf-subtle-foreground` / `--dnf-inverse-foreground` |
| `--color-bg` | `--dnf-background` |
| `--color-surface` / `-surface-raised` / `-surface-muted` / `-surface-hover` | `--dnf-surface` / `--dnf-surface-raised` / `--dnf-surface-muted` / `--dnf-surface-hover` |
| `--color-border` / `-border-strong` | `--dnf-border` / `--dnf-border-strong` |
| `--color-accent` / `-accent-hover` / `-accent-soft` / `-accent-soft-border` / `-accent-text` / `--color-on-accent` | `--dnf-primary` / `--dnf-primary-hover` / `--dnf-primary-soft` / `--dnf-primary-soft-border` / `--dnf-primary-text` / `--dnf-primary-foreground` |
| `--color-success` / `-warning` / `-danger` / `-info` (+ `-soft`), `--color-danger-hover` | `--dnf-success` / `--dnf-warning` / `--dnf-danger` / `--dnf-info` (+ `-soft`), `--dnf-danger-hover` |
| `--color-focus` / `--focus-ring` | `--dnf-focus` / `--dnf-focus-ring` |
| `--color-nav-bg` / `-nav-text` / `-nav-text-muted` / `-nav-text-strong` / `-nav-surface` / `-nav-hover` / `-nav-border` | `--dnf-nav-background` / `--dnf-nav-foreground` / `--dnf-nav-muted-foreground` / `--dnf-nav-strong-foreground` / `--dnf-nav-surface` / `--dnf-nav-hover` / `--dnf-nav-border` |
| `--color-brand-bg` / `-brand-text` / `-brand-text-strong` / `-brand-surface` / `-brand-hover` / `-brand-border` | `--dnf-brand-background` / `--dnf-brand-foreground` / `--dnf-brand-strong-foreground` / `--dnf-brand-surface` / `--dnf-brand-hover` / `--dnf-brand-border` |
| `--color-code-bg` / `-code-text`, `--color-tooltip-bg` / `-tooltip-text`, `--backdrop` | `--dnf-code-background` / `--dnf-code-foreground`, `--dnf-tooltip-background` / `--dnf-tooltip-foreground`, `--dnf-backdrop` |
| `--space-N`, `--text-*`, `--weight-*`, `--leading`, `--leading-tight` | `--dnf-space-N`, `--dnf-text-*`, `--dnf-weight-*`, `--dnf-leading`, `--dnf-leading-tight` |
| `--radius` / `--radius-sm/lg/xl/full` | `--dnf-radius-md` / `--dnf-radius-sm/lg/xl/full` |
| `--shadow-xs/sm/md/lg` | `--dnf-shadow-xs/sm/md/lg` |
| `--control-h` / `-h-sm` / `-h-lg` | `--dnf-control-md` / `--dnf-control-sm` / `--dnf-control-lg` |
| `--sidebar-w`, `--topbar-h`, `--content-max`, `--form-max` | `--dnf-sidebar-width`, `--dnf-topbar-height`, `--dnf-content-max`, `--dnf-form-max` |
| `--duration`, `--duration-fast`, `--ease`, `--z-*` | `--dnf-duration`, `--dnf-duration-fast`, `--dnf-ease`, `--dnf-z-*` |
| `--font-sans` / `--font-mono`, `--forge-N`, `--gray-N`, `--forge-cut` | `--dnf-font-sans` / `--dnf-font-mono`, `--dnf-forge-N`, `--dnf-gray-N`, `--dnf-forge-cut` |
