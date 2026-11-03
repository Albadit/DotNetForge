# Using DotNetForge UI in an extension

Extensions use the same library as the CMS, so an admin tab or a dashboard widget looks like the admin around it in
light and dark. Extension basics (manifest, slots, views, resources): [extension development](../guides/extension-development.md).
Old Forge class names in an extension: [migration](migration.md).

## The assembly

- `DotNetForge.Sdk` references `DotNetForge.UI` (`src/DotNetForge.Sdk/DotNetForge.Sdk.csproj`), so an extension
  project that compiles against the SDK can use the Dnf types (`DnfSize`, `DnfTone`, `DnfAssets`,
  `DnfTimestampTagHelper.Relative`, ...).
- The web host references it too and ships `DotNetForge.UI.dll`. `ExtensionLoadContext` resolves every assembly the
  host ships from the host (`IsHostAssembly`), so there is one copy of the tag helper types at runtime.
- Extension views are compiled at runtime against the host's references, so they can register the tag helpers with
  `@addTagHelper *, DotNetForge.UI` - a views-only extension needs no project at all.

## Admin tab (`type: admin`)

An admin tab is a complete HTML document in an iframe ([extension host](../pages/extension-host.md)): it inherits
nothing from the admin, so it loads the library itself. The sample `extensions/admin/audit-dashboard` follows this
pattern (without `dnf.js`, which it does not need; its root is marked `data-dnf-extension="dotnetforge.admin.audit-dashboard"`).

`Views/_ViewImports.cshtml`:

```cshtml
@using DotNetForge.UI
@using DotNetForge.UI.Components
@addTagHelper *, DotNetForge.UI
```

`Views/Shared/_Layout.cshtml` (set by `Views/_ViewStart.cshtml`):

```cshtml
@{ var resourceBase = ViewData["ResourceBase"] as string ?? string.Empty; }<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="color-scheme" content="light dark" />
    <title>@ViewData["Title"]</title>
    <script src="/_content/DotNetForge.UI/dnf-theme.js"></script>          @* first: theme before the first paint *@
    <link rel="stylesheet" href="/_content/DotNetForge.UI/dnf.css" />
    <link rel="stylesheet" href="@resourceBase/css/index.css" />           @* yours, after dnf.css *@
    <script src="/_content/DotNetForge.UI/dnf.js" defer></script>           @* only if you use its hooks *@
</head>
<body class="dnf-scope" data-dnf-extension="yourcompany.admin.reports">
    @RenderBody()
    <script src="@resourceBase/js/index.js"></script>
</body>
</html>
```

- `dnf-theme.js` reads the same `localStorage` key (`dnf-theme`) as the admin, so the tab follows the user's
  light/dark choice ([theming](theming.md#switching)).
- `dnf.js` is optional: load it when the views use `data-*` hooks (tooltips, dropdowns, tabs, collections, confirm,
  [behaviours](behaviours.md)). Toasts and the confirm dialog also need their markup in your layout
  ([behaviours → toasts](behaviours.md#toasts), [confirm dialog](behaviours.md#confirm-dialog)); without the dialog,
  `data-confirm` falls back to `window.confirm`.
- The paths are the `DnfAssets` constants (`@DnfAssets.Stylesheet`, ...) if you prefer. They do not include an app
  path base.

## Dashboard widget

A widget (`Views/Widget.cshtml`) is a partial rendered inside a card on the admin dashboard, which already loads the
library: use the classes and utilities directly and add only the `DotNetForge.UI` lines to its `_ViewImports.cshtml`
when it uses tag helpers. Sample: `extensions/widgets/weather-widget` (Tailwind utilities, no stylesheet). Page
modules and themes render on the public site, which has its own styles - see
[design system → public fallback](../architecture/design-system.md#public-fallback) and [themes](../features/themes.md).

## Isolation

Mark the extension's root element:

```html
<body class="dnf-scope" data-dnf-extension="yourcompany.admin.reports">
```

- **`dnf-scope`** recomputes the derived tokens on that element ([theming → derived tokens](theming.md#derived-tokens)),
  so overriding a leaf token there re-themes everything inside, hover and soft shades included:

  ```css
  [data-dnf-extension="yourcompany.admin.reports"] {
      --dnf-primary: #2563eb;
      --dnf-primary-text: light-dark(#1d4ed8, #93c5fd);
  }
  ```

- **`data-dnf-extension="<id>"`** names the extension (its manifest `id`). The library attaches no behaviour to it; it
  is the hook for your own selectors and makes the extension's markup easy to find in the DOM.
- Prefix your own classes with something of your own (`reports-…`), never `dnf-`.

## Overriding components (Level 3)

Write your stylesheet **without** `@layer`. Unlayered CSS beats every layer of `dnf.css` (`theme`, `base`,
`components`, `utilities`) with ordinary selectors - no `!important` ([styling → levels](styling.md#levels)):

```css
/* Views/Resources/css/index.css - unlayered */
.reports-summary .dnf-card__body { padding: var(--dnf-space-4); }
.reports-toolbar .dnf-button { --dnf-button-height: var(--dnf-control-sm); }
```

Use tokens (`var(--dnf-…)`) rather than literal values, so light and dark keep working. Don't restyle the components'
internals beyond what you need: component markup and token names are the API; anything else may change.

## Overlays

Dropdown menus, popovers, dialogs and the tooltip open in the browser's **top layer** (the Popover API and
`<dialog>.showModal()`): they are drawn above everything in the document regardless of `z-index`, `overflow: hidden`
or stacking contexts, so an extension never needs a `z-index` to show them. Everything in an admin tab stays inside
its iframe.

## Markup added later

`dnf.js` wires per-element behaviours once at load. After inserting markup (a fetched fragment, a template), call:

```js
window.dnfUI.init(container);
```

Delegated behaviours (dropdowns, tooltips, confirm, copy, ...) need no call ([behaviours → public API](behaviours.md#public-api)).

## Tailwind in an extension

The host's `dnf.css` contains **only the utilities used by the CMS and the extensions in this repository** - Tailwind
generated them from the files it scanned at build time ([styling → what is scanned](styling.md#what-is-scanned)). A
bundled extension (under `extensions/` in this repository) gets its utilities into `dnf.css` on the next build. An
extension built elsewhere may use a utility the host never compiled; then it has no effect. Such an extension either
sticks to the component classes, tokens and its own CSS, or compiles its own utilities with the Tailwind v4 CLI.

Its own build imports Tailwind's **theme and utilities only** - not preflight, which the host's `dnf.css` already
applies - plus the DotNetForge theme mapping, so `text-muted`, `bg-surface`, `text-sm` and `dark:` mean the same as in
the host. For example a `reports.css` in the extension's `Styles` folder:

```css
@layer theme, base, components, utilities;
@import "tailwindcss/theme.css" layer(theme);
@import "tailwindcss/utilities.css" layer(utilities) source(none);
@import "../../../../src/DotNetForge.UI/Styles/theme.css";   /* or a copy of it */
@source "../Views";
```

```bash
tailwindcss --input Styles/reports.css --output Views/Resources/css/reports.css --minify
```

- `source(none)` turns off automatic detection; `@source` names the folders to scan (relative to the stylesheet).
- Link the output **after** `dnf.css`. Its rules are in the same named layers as the host's, so they merge into them:
  your utilities sit in `utilities`, above the components, like the host's.
- Use the same Tailwind version as the host (`4.3.3`, [styling → build](styling.md#build)); in a checkout the binary is
  already in `tools/tailwind/4.3.3/`.
- The host's build does not run this step: run it yourself (or in your CI) and ship the generated file in
  `Views/Resources/`, which is read at request time and must not be written at runtime.
- `Styles/theme.css` only maps names to `--dnf-*` tokens; the values come from the host's `dnf.css` at runtime.

No extension in this repository does this today: the bundled ones are scanned by the host build.

## Checklist

- [ ] `_ViewImports.cshtml` has the three `DotNetForge.UI` lines.
- [ ] Admin tab: `dnf-theme.js` in `<head>`, then `dnf.css`, then your CSS; `dnf.js` if you use hooks.
- [ ] Root element `class="dnf-scope" data-dnf-extension="<id>"`.
- [ ] Your CSS is unlayered, uses tokens, no `!important`, no inline `style` or `<script>` (CSP).
- [ ] `dnfUI.init(root)` after inserting markup that needs per-element behaviours.
- [ ] Every utility you use is in the host's `dnf.css` or in your own compiled CSS.

## Planned (not implemented)

Nothing is specified beyond what is built.
