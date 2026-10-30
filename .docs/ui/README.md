# DotNetForge UI

The component library of DotNetForge CMS (`src/DotNetForge.UI`, a Razor class library). Every built-in screen and
every extension that wants to look like the admin uses it. It knows nothing about CMS data: it is markup, CSS,
tag helpers, one script and an icon sprite.

| Part | Name | Where |
| --- | --- | --- |
| Components | **Dnf** components: BEM classes with the `dnf-` prefix (`.dnf-card`, `.dnf-card__title`, `.dnf-card--danger`) | `src/DotNetForge.UI/Styles/components/*.css` |
| Tokens | CSS custom properties with the `--dnf-` prefix (`--dnf-primary`, `--dnf-space-4`) | `src/DotNetForge.UI/Styles/tokens.css` ([theming](theming.md)) |
| Utilities | Tailwind CSS v4, compiled at build time; its scale points at the tokens (`text-muted`, `bg-surface`, `gap-2`) | `Styles/theme.css`, `Styles/dnf.css`, `build/Tailwind.targets` ([styling](styling.md#tailwind)) |
| Tag helpers | `<dnf-button>`, `<dnf-icon>`, `<dnf-dropdown>`, ... | `src/DotNetForge.UI/DnfTagHelper.cs`, `Components/*.cs` |
| Behaviours | `window.dnfUI` + `data-*` hooks, no dependencies | `src/DotNetForge.UI/wwwroot/dnf.js` ([behaviours](behaviours.md)) |
| Theme bootstrap | applies the saved light/dark choice before the first paint | `src/DotNetForge.UI/wwwroot/dnf-theme.js` ([theming](theming.md#switching)) |
| Icons | SVG sprite, one `<symbol id>` per icon (Lucide paths, ISC licence) | `src/DotNetForge.UI/wwwroot/icons.svg` ([icon](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml)) |

The API vocabulary - variants (`primary`, `secondary`, `tertiary`, `outline`, `ghost`, `danger`), sizes, tones,
`pending`, state as `data-*` attributes (`data-hovered`, `data-pressed`, `data-focus-visible`, `data-selected`) - is
inspired by HeroUI; the implementation is the library's own (server-rendered Razor + CSS, no React, no HeroUI code).

Other documents: [styling](styling.md) (layers, the three customization levels, BEM, the Tailwind build),
[theming](theming.md) (tokens, light/dark), [behaviours](behaviours.md) (`dnf.js`),
[extensions](extensions.md) (using the library from an extension), [migration](migration.md) (old Forge names →
Dnf names). **The component reference is the in-app [UI docs](../pages/ui-docs.md)** (`/admin/docs/ui`, account
menu, Super Admins and Developers): a page per component with live examples (preview, Razor, HTML), the styling
reference and the API table generated from the tag helper - their source is readable in
`src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/`. How the CMS uses the library (admin shell, sign-in layout,
feedback): [design system](../architecture/design-system.md).

## Using it in a view

**1. Tag helpers** - in the view folder's `_ViewImports.cshtml`:

```cshtml
@using DotNetForge.UI
@using DotNetForge.UI.Components
@addTagHelper *, DotNetForge.UI
```

`DotNetForge.UI` holds the enums (`DnfSize`, `DnfTone`, `DnfButtonVariant`) and `DnfAssets`;
`DotNetForge.UI.Components` holds the tag helper classes (e.g. `DnfTimestampTagHelper.Relative`). Both
`_ViewImports.cshtml` files of the web host have these lines.

**2. Assets** - served as static web assets under `/_content/DotNetForge.UI/` (`DnfAssets` has the paths). Load them
in this order:

```cshtml
<head>
    <meta name="color-scheme" content="light dark" />
    @* 1. synchronously, before the stylesheet: the saved theme applies before the first paint *@
    <script src="~/_content/DotNetForge.UI/dnf-theme.js" asp-append-version="true"></script>
    @* 2. tokens, base, components and utilities in one file *@
    <link rel="stylesheet" href="~/_content/DotNetForge.UI/dnf.css" asp-append-version="true" />
    @* 3. behaviours, deferred *@
    <script src="~/_content/DotNetForge.UI/dnf.js" asp-append-version="true" defer></script>
</head>
```

| `DnfAssets` constant | Path |
| --- | --- |
| `Stylesheet` | `/_content/DotNetForge.UI/dnf.css` |
| `ThemeScript` | `/_content/DotNetForge.UI/dnf-theme.js` |
| `Script` | `/_content/DotNetForge.UI/dnf.js` |
| `Sprite` | `/_content/DotNetForge.UI/icons.svg` |

`asp-append-version` needs `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`; without it, write the plain path.

**3. The sprite** - icons are `<svg><use href=".../icons.svg#name"></use></svg>`. The tag helpers write the
cache-busted URL below the app's path base (`DnfTagHelper.SpriteUrl`); `dnf.js` reuses the first sprite URL it finds
on the page for icons it creates. Nothing has to be inlined into the page.

## Component API

Applies to every Dnf tag helper (`DnfTagHelper` in `src/DotNetForge.UI/DnfTagHelper.cs`).

- **Enums are written as member names:** `variant="Primary"`, `size="Sm"`, `tone="Success"`. They render as
  kebab-case BEM modifiers (`DangerSoft` → `dnf-button--danger-soft`).

  | Enum | Members (default first) |
  | --- | --- |
  | `DnfSize` | `Md`, `Sm`, `Lg`, `Xl` |
  | `DnfTone` | `Neutral`, `Success`, `Warning`, `Danger`, `Info`, `Primary` |
  | `DnfButtonVariant` | `Secondary`, `Primary`, `Tertiary`, `Outline`, `Ghost`, `Danger`, `DangerSoft` |

  A component that has fewer sizes or tones maps the rest; each component page lists what it supports.
- **Properties are kebab-case attributes**; booleans take `"true"`: `icon-only="true"`, `full-width="true"`,
  `pending="@isSaving"`.
- **`class` is appended last.** The component's own classes come first, then whatever the author wrote (a literal
  or a Razor expression), so Tailwind utilities and custom classes extend the component
  ([Level 2](styling.md#level-2-tailwind-utilities)): `<dnf-button variant="Primary" class="w-full sm:w-auto">`
  renders `class="dnf-button dnf-button--primary w-full sm:w-auto"` - every tag helper does this.
- **Other attributes pass through** (`type`, `name`, `value`, `form`, `id`, `data-*`, `aria-*`).
- **Classes are equally valid.** A tag helper is a shortcut, never the only way: `<a class="dnf-button
  dnf-button--primary" asp-action="Create">` is fine. A modifier is always written with its block.

### Tag helpers

| Tag | Renders | Page |
| --- | --- | --- |
| `<dnf-icon name size label />` | `<svg class="dnf-icon">` from the sprite | [icon](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml) |
| `<dnf-button variant size icon icon-end icon-only label pending full-width disabled href>` | `<button>` or `<a>` | [button](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Button.cshtml) |
| `<dnf-spinner size label />` | `<span class="dnf-spinner" role="status">` | [spinner](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Spinner.cshtml) |
| `<dnf-badge tone dot outline>` | `<span class="dnf-badge">` | [badge](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Badge.cshtml) |
| `<dnf-status-dot tone label />` | `<span class="dnf-status-dot" role="img">` | [badge](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Badge.cshtml) |
| `<dnf-alert tone title>` | `<div class="dnf-alert">` with an icon | [alert](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Alert.cshtml) |
| `<dnf-validation-summary />` | all model-state errors, or nothing | [alert](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Alert.cshtml) |
| `<dnf-empty-state icon title description compact heading-level>` | `<div class="dnf-empty-state">` | [empty state](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/EmptyState.cshtml) |
| `<dnf-stat-card icon label value href bare />` | a KPI tile (`<div>` or `<a>`) | [stat card](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/StatCard.cshtml) |
| `<dnf-avatar name src size />` | initials or an image | [avatar](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Avatar.cshtml) |
| `<dnf-timestamp value empty absolute />` | `<time>` with a relative date | [timestamp](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Timestamp.cshtml) |
| `<dnf-dropdown label text icon variant size placement menu-class>` + `<dnf-dropdown-link>`, `<dnf-dropdown-button>`, `<dnf-dropdown-post>`, `<dnf-dropdown-label>`, `<dnf-dropdown-separator />`, `<dnf-dropdown-note>` | a menu button + popover menu | [dropdown](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dropdown.cshtml) |

Every other component is markup with classes.

## States

State is an attribute, never a class, so the CSS, the markup and `dnf.js` share one vocabulary. Each attribute below
is honoured by the CSS listed or set by `dnf.js` (or both).

| Attribute | Meaning | Styled by | Set by |
| --- | --- | --- | --- |
| `data-hovered="true"` | hovered (alongside `:hover`) | `.dnf-button`, `.dnf-input`, `.dnf-select`, `.dnf-textarea` | your code (tests, visual states) |
| `data-pressed="true"` | pressed (alongside `:active`) | `.dnf-button` | your code |
| `data-focus-visible="true"` | keyboard focus (alongside `:focus-visible`) | `.dnf-button` | your code |
| `data-pending` (any value) | busy: spinner shown, clicks ignored | `.dnf-button` | `pending="true"`, `<form data-pending-on-submit>` |
| `data-selected="true"` | selected item | `tr` in `.dnf-table__table` | collection selection (`dnf.js`) |
| `data-dragging="true"` | dragged row / file over the drop area | `tr` in `.dnf-table__table`; `.dnf-file-upload` | file upload (`dnf.js`); rows: your script |
| `data-invalid="true"` | invalid value | `.dnf-input`/`-select`/`-textarea`, `.dnf-field` (its controls), `.dnf-code-block` | your markup |
| `data-open` (present) | open dropdown or popover panel | - (hook only) | `dnf.js` |
| `data-loaded="true"` | the embedded frame has loaded | - (hook for the host's CSS) | `dnf.js` on `[data-frame-host]` |
| `data-tabbed="true"` | tabs show one panel at a time | `.dnf-tab-panel`, `.dnf-tab-panel__title` | `dnf-theme.js` and `dnf.js` on `[data-tabs]` |
| `data-nav-open="true"/"false"` | drawer navigation open | - (hook for the shell's CSS) | `dnf.js` on `[data-shell]` |
| `data-leaving="true"` | toast fading out | `.dnf-toast` | `dnf.js` |
| `data-placement="top"/"bottom"` | side of the tooltip bubble | `.dnf-tooltip` | `dnf.js` |
| `aria-pressed="true"` | toggled on | `.dnf-button` (drawn as primary), `.dnf-segmented__item` | your markup; collection filters, view switch, password reveal (`dnf.js`) |
| `aria-expanded` | menu or drawer open | - | `dnf.js` (dropdown openers, `[data-drawer-open]`) |
| `aria-current="page"` (`"true"` on segmented items) | the current page/item | `.dnf-button`, `.dnf-tabs__tab`, `.dnf-segmented__item`, breadcrumbs | your markup; pager buttons (`dnf.js`) |
| `aria-selected="true"` | selected tab | `.dnf-tabs__tab` | `dnf.js` |
| `aria-checked="true"` | checked option | `.dnf-segmented__item`, `.dnf-dropdown__item` (check mark) | your markup; `[data-theme-set]` (`dnf.js`) |
| `aria-invalid="true"` | invalid value | `.dnf-input`/`-select`/`-textarea`, `.dnf-checkbox`, `.dnf-radio` | your markup |
| `aria-disabled="true"` | disabled (links, custom controls) | `.dnf-button`, `.dnf-input`/`-select`/`-textarea`, `.dnf-dropdown__item` | `<dnf-button href disabled>` |
| `aria-sort` | sorted column | `th` with `.dnf-table__sort` | `dnf.js` |
| `[hidden]` | hidden, whatever display the component sets | Tailwind's preflight | your markup; `dnf.js` |

Native states (`:disabled`, `:checked`, `:indeterminate`, `:read-only`, `[open]` on `<details>`/`<dialog>`) are used
wherever an element has them. ASP.NET's `input-validation-error` class (added by `asp-for` when the model state has an
error) counts as invalid.

## Components

| Component | Classes / tag | Page |
| --- | --- | --- |
| Button, button group, icon button | `.dnf-button`, `.dnf-button-group`, `<dnf-button>` | [button.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Button.cshtml) |
| Icon | `.dnf-icon`, `<dnf-icon>` | [icon.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml) |
| Spinner | `.dnf-spinner`, `<dnf-spinner>` | [spinner.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Spinner.cshtml) |
| Layout primitives | `.dnf-stack`, `.dnf-cluster`, `.dnf-spread`, `.dnf-grid`, `.dnf-split`, `.dnf-container`, `.dnf-divider`, `.dnf-note`, `.dnf-skip-link` | [layout.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Layout.cshtml) |
| Form, form grid, form actions | `.dnf-form`, `.dnf-form-grid`, `.dnf-form-actions` | [form.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Form.cshtml) |
| Field | `.dnf-field` | [field.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Field.cshtml) |
| Input | `.dnf-input` | [input.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Input.cshtml) |
| Select | `.dnf-select` | [select.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Select.cshtml) |
| Textarea | `.dnf-textarea` | [textarea.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Textarea.cshtml) |
| Input group | `.dnf-input-group` | [input-group.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/InputGroup.cshtml) |
| Fieldset | `.dnf-fieldset` | [fieldset.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Fieldset.cshtml) |
| Checkbox, check, check list | `.dnf-checkbox`, `.dnf-check`, `.dnf-check-list` | [checkbox.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Checkbox.cshtml) |
| Radio | `.dnf-radio` | [radio.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Radio.cshtml) |
| Switch, switch row | `.dnf-switch`, `.dnf-switch-row` | [switch.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Switch.cshtml) |
| Choice card | `.dnf-choice-cards`, `.dnf-choice-card` | [choice-card.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/ChoiceCard.cshtml) |
| File upload | `.dnf-file-upload` | [file-upload.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/FileUpload.cshtml) |
| Card | `.dnf-card` | [card.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Card.cshtml) |
| Stat card, stat group | `.dnf-stat-card`, `.dnf-stat-group`, `<dnf-stat-card>` | [stat-card.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/StatCard.cshtml) |
| Badge, status dot | `.dnf-badge`, `.dnf-status-dot`, `<dnf-badge>`, `<dnf-status-dot>` | [badge.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Badge.cshtml) |
| Chip | `.dnf-chip`, `.dnf-chip-list` | [chip.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Chip.cshtml) |
| Alert, validation summary | `.dnf-alert`, `.dnf-validation-summary`, `<dnf-alert>`, `<dnf-validation-summary>` | [alert.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Alert.cshtml) |
| Toast | `.dnf-toast-region`, `.dnf-toast` | [toast.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Toast.cshtml) |
| Avatar | `.dnf-avatar`, `<dnf-avatar>` | [avatar.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Avatar.cshtml) |
| Progress | `.dnf-progress` | [progress.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Progress.cshtml) |
| Timestamp | `<dnf-timestamp>` | [timestamp.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Timestamp.cshtml) |
| Table | `.dnf-table` | [table.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Table.cshtml) |
| Toolbar, bulk bar, collection bar | `.dnf-toolbar`, `.dnf-bulk-bar`, `.dnf-collection-bar` | [toolbar.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Toolbar.cshtml) |
| Pagination | `.dnf-pagination` (rendered by `dnf.js`) | [pagination.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Pagination.cshtml) |
| Page header | `.dnf-page-header` | [page-header.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/PageHeader.cshtml) |
| Breadcrumbs | `.dnf-breadcrumbs` | [breadcrumbs.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Breadcrumbs.cshtml) |
| Tabs, tab panel | `.dnf-tabs`, `.dnf-tab-panel` | [tabs.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Tabs.cshtml) |
| Segmented control | `.dnf-segmented` | [segmented.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Segmented.cshtml) |
| Accordion | `.dnf-accordion` | [accordion.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Accordion.cshtml) |
| Dropdown | `.dnf-dropdown`, `<dnf-dropdown>` | [dropdown.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dropdown.cshtml) |
| Dialog (and drawer) | `.dnf-dialog` | [dialog.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dialog.cshtml) |
| Tooltip | `[data-tooltip]`, `.dnf-tooltip` | [tooltip.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Tooltip.cshtml) |
| Popover | `.dnf-popover` | [popover.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Popover.cshtml) |
| Empty state | `.dnf-empty-state`, `<dnf-empty-state>` | [empty-state.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/EmptyState.cshtml) |
| Skeleton | `.dnf-skeleton` | [skeleton.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Skeleton.cshtml) |
| Description list | `.dnf-description-list` | [description-list.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/DescriptionList.cshtml) |
| Code block | `.dnf-code-block` | [code-block.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/CodeBlock.cshtml) |
| List | `.dnf-list` | [list.md](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/List.cshtml) |

## Rules

- Server-rendered and Content-Security-Policy safe: no inline script or style anywhere, behaviour through `data-*`
  hooks in `dnf.js` ([behaviours](behaviours.md#csp)).
- Components use tokens only, never literal colours or sizes ([theming](theming.md)).
- No `!important` in the library's styles ([styling](styling.md#layers)).
- Every form control carries its class: Tailwind's preflight gives bare `<input>`, `<select>` and `<textarea>` no look,
  and removes list bullets and heading margins (`list-disc ps-5` brings bullets back).
- `DotNetForge.UI` references only the ASP.NET Core shared framework - never a CMS project
  ([dependencies](../architecture/dependencies.md#project-references)).

## Where to change things

| Change | Where | Also |
| --- | --- | --- |
| A component's look | its file in `src/DotNetForge.UI/Styles/components/` | its page in `components/` |
| A new component | a block in the matching `Styles/components/*.css` (imported by `Styles/dnf.css`), a tag helper in `Components/` only when it has structure (derive from `DnfTagHelper`) | a page in `components/`, the tables above |
| A token | `Styles/tokens.css` (and `Styles/theme.css` when Tailwind should expose it) | [theming](theming.md) |
| A behaviour | `wwwroot/dnf.js` | [behaviours](behaviours.md) |
| An icon | a `<symbol id="name" viewBox="0 0 24 24">` with Lucide paths in `wwwroot/icons.svg` | [icon](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml) |

CSS and tag helper changes need a rebuild (`dotnet build` regenerates `wwwroot/dnf.css`, [styling](styling.md#build));
`dnf.js` and the sprite are served as files.

## Planned (not implemented)

Nothing is specified beyond what is built. The components without a tag helper (card, table, dialog, tabs, ...) are
written as markup with their classes.
