# Styling

How the DotNetForge UI stylesheet is put together, how to customize a component without fighting it, and how the
stylesheet is built. Tokens and themes: [theming.md](theming.md).

## Layers

`src/DotNetForge.UI/Styles/dnf.css` imports Tailwind and the library into four
[cascade layers](https://developer.mozilla.org/docs/Web/CSS/@layer), declared lowest to highest:

| Layer | Holds | Source |
| --- | --- | --- |
| `theme` | Tailwind's design scale as custom properties (`--text-sm`, `--color-primary`, ...), pointing at the `--dnf-*` tokens | `@import "tailwindcss"`, `Styles/theme.css` |
| `base` | Tailwind's preflight reset, the `--dnf-*` tokens, element styles (body, headings, links, `code`, `kbd`, focus) | preflight, `Styles/tokens.css`, `Styles/base.css` |
| `components` | every `.dnf-*` class | `Styles/components/*.css` |
| `utilities` | the Tailwind utilities used in the scanned markup | generated ([Tailwind](#tailwind)) |

A later layer wins over an earlier one whatever the selector specificity, and CSS outside any layer wins over all of
them. So:

- a **Tailwind utility** on an element beats the component's own style (`class="dnf-card shadow-none"` removes the shadow
  the card sets);
- an **unlayered stylesheet** (an extension's) beats components and utilities, with plain selectors;
- the CMS's own screen stylesheets (`src/DotNetForge.Web/wwwroot/css/`) put their rules in `@layer components`, after
  the library's, so utilities in the CMS views still win over them;
- nobody needs `!important`. The library's own CSS has none; the only `!important` in the compiled file is Tailwind
  preflight's `[hidden]` rule, which keeps `hidden` working whatever `display` a component sets.

`Styles/base.css` holds element selectors only; every class lives in a component file.

## Levels

Customize at the lowest level that does the job.

### Level 1: component props and modifiers

Pick what the component offers: a tag helper property or a BEM modifier.

```cshtml
<dnf-button variant="Primary" size="Lg" icon="plus">New page</dnf-button>
<div class="dnf-card dnf-card--danger">…</div>
<input class="dnf-input dnf-input--sm" />
```

### Level 2: Tailwind utilities

Add utilities in `class`. Tag helpers append the author's `class` after their own ([component API](README.md#component-api)),
and the `utilities` layer beats the `components` layer:

```cshtml
<dnf-button variant="Primary" class="w-full sm:w-auto">Save</dnf-button>
<div class="dnf-card shadow-none">…</div>
<p class="text-sm text-muted">…</p>
@* a component token through an arbitrary property *@
<dnf-button class="[--dnf-button-radius:var(--dnf-radius-full)]">Rounded</dnf-button>
```

Utilities exist only if Tailwind saw them in a scanned file at build time ([what is scanned](#what-is-scanned)). The
theme maps the scale to the tokens, so `text-sm` is 13px (the admin scale), `rounded-md` is `--dnf-radius-md`,
`bg-primary`/`text-danger`/`border-border` follow the theme and any token override ([theme mapping](#theme-mapping)).

### Level 3: your own CSS

Write a stylesheet **without** `@layer` (an extension's `Views/Resources`; in the CMS, the UI Components showcase's
demos). It beats components and utilities without `!important`:

```css
/* a BEM element of one component, inside your own block */
.audit-summary .dnf-card__title { font-size: var(--dnf-text-lg); }

/* a component token: every modifier only sets tokens, so this re-sizes all variants */
.toolbar-large .dnf-button { --dnf-button-height: var(--dnf-control-lg); }

/* global tokens: re-theme everything inside an element */
.my-extension { --dnf-primary: #2563eb; --dnf-radius-md: 2px; }
```

Component tokens are declared on the component element itself, so set them on that element (a selector that matches
it, or a Level 2 arbitrary property) - setting them on an ancestor has no effect. Global `--dnf-*` tokens inherit, so
set them on any ancestor; in a `.dnf-scope` the derived tokens follow ([theming](theming.md#derived-tokens)).

| Component tokens | Component |
| --- | --- |
| `--dnf-button-height`, `-padding-x`, `-gap`, `-font-size`, `-radius`, `-background`, `-foreground`, `-border`, `-shadow`, `-hover-background`, `-hover-foreground`, `-hover-border` | [button](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Button.cshtml) |
| `--dnf-icon-size` | [icon](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Icon.cshtml) |
| `--dnf-spinner-size` | [spinner](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Spinner.cshtml) |
| `--dnf-control-height`, `--dnf-control-padding-x`, `--dnf-control-font-size` | [input](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Input.cshtml), [select](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Select.cshtml), [textarea](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Textarea.cshtml) |
| `--dnf-card-padding`, `-radius`, `-border` | [card](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Card.cshtml) |
| `--dnf-badge-background`, `-foreground` | [badge](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Badge.cshtml) |
| `--dnf-alert-background`, `-border`, `-icon` | [alert](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Alert.cshtml) |
| `--dnf-toast-icon` | [toast](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Toast.cshtml) |
| `--dnf-avatar-size` | [avatar](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Avatar.cshtml) |
| `--dnf-progress-value` | [progress](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Progress.cshtml) |
| `--dnf-table-cell-y`, `-x` | [table](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Table.cshtml) |
| `--dnf-dialog-width` | [dialog](../../src/DotNetForge.Web/Areas/Admin/Views/UiDocs/Components/Dialog.cshtml) |

An unlayered rule also beats Tailwind utilities written in the markup, so don't style the same property in both.

## BEM naming

| Part | Pattern | Example |
| --- | --- | --- |
| Block | `dnf-<block>` | `.dnf-card`, `.dnf-input-group` |
| Element | `dnf-<block>__<element>` | `.dnf-card__title`, `.dnf-input-group__icon` |
| Modifier | `dnf-<block>--<modifier>` or `dnf-<block>__<element>--<modifier>` | `.dnf-card--danger`, `.dnf-table__cell--actions` |

- A modifier is always written with its block: `class="dnf-button dnf-button--primary"`, never `dnf-button--primary`
  alone.
- State is an attribute, never a class ([states](README.md#states)).
- Extensions don't use the `dnf-` prefix for their own classes. (The CMS's own screen CSS in
  `src/DotNetForge.Web/wwwroot/css/` does - `dnf-shell`, `dnf-sidebar`, `dnf-auth`, `dnf-site`, ... - in
  `@layer components`; those classes are not part of the library.)
- Plain layout and typography are Tailwind utilities, not new classes.

## Tailwind

Tailwind CSS v4 runs at **build time only**: the browser gets one static `dnf.css`. The build uses Tailwind's CLI from
npm (`@tailwindcss/cli`, pinned in `tools/package.json`), so building needs Node.js; nothing from npm
reaches the app.

### Theme mapping

`Styles/theme.css` is a `@theme inline` block that maps Tailwind's scale to the tokens. `inline` keeps the `var()`
references in the utilities, so a token override on an element changes the utilities inside it too.

| Tailwind | Token | Utilities |
| --- | --- | --- |
| `--font-sans`, `--font-mono` | `--dnf-font-sans`, `--dnf-font-mono` | `font-sans`, `font-mono` |
| `--text-xs` … `--text-4xl` (incl. `--text-md`) | `--dnf-text-*` | `text-xs` (12px), `text-sm` (13px), `text-base` (14px), `text-md` (15px), `text-lg` … `text-4xl` |
| `--radius-sm` … `--radius-xl` | `--dnf-radius-*` | `rounded-sm`, `rounded-md`, `rounded-lg`, `rounded-xl` |
| `--shadow-xs` … `--shadow-lg` | `--dnf-shadow-*` | `shadow-xs` … `shadow-lg` |
| `--color-background`, `foreground`, `muted`, `subtle`, `surface`, `surface-raised`, `surface-muted`, `surface-hover`, `border`, `border-strong`, `primary`, `primary-foreground`, `primary-text`, `primary-soft`, `success`, `success-soft`, `warning`, `warning-soft`, `danger`, `danger-soft`, `info`, `info-soft` | the semantic `--dnf-*` colours | `bg-surface`, `text-muted`, `text-subtle`, `text-danger`, `border-border`, `bg-primary-soft`, ... |

Spacing (`gap-2`, `p-4`), breakpoints and Tailwind's own palette are Tailwind's defaults. `theme.css` also defines the
`dark:` variant, which agrees with the tokens: it applies under `data-theme="dark"` (on `<html>` or a subtree) and,
when the system prefers dark, everywhere except under `data-theme="light"` (on `<html>` or a subtree).

### Build

`src/DotNetForge.UI/build/Tailwind.targets` (imported by `DotNetForge.UI.csproj`) compiles `Styles/dnf.css` into
`wwwroot/dnf.css` before every build of the project that has changed inputs - so `dotnet build` of the solution, the
web host or any project referencing `DotNetForge.UI` (the SDK, so every extension project) keeps it current.
`wwwroot/dnf.css` is git-ignored output.

| Item | Value |
| --- | --- |
| Tailwind version | `4.3.3`: `@tailwindcss/cli` in `tools/package.json` (exact version, next to ESLint) and `tools/package-lock.json` (the whole tree, every platform's native package) |
| Requirement | Node.js 20 or newer (with npm) on the `PATH`; without it the build fails with "Node.js 20 or newer is needed to build the UI stylesheet" |
| Install | `npm ci --ignore-scripts` in `tools/` into `tools/node_modules/` (git-ignored) - once, on the first build, and again only when `package.json` or `package-lock.json` is newer than the installed tree. No install scripts run: the CLI needs none |
| Command | `node tools/node_modules/@tailwindcss/cli/dist/index.mjs --input Styles/dnf.css --output wwwroot/dnf.css`, with `--minify` in `Release` and `--map` (inline source map) otherwise, run in `src/DotNetForge.UI` with `NODE_PATH=tools/node_modules` - Tailwind resolves `@import "tailwindcss"` from the stylesheet's folder upwards and then from `NODE_PATH` |
| Incremental | skipped when no input changed: `Styles/**/*.css`, the project's `*.cs`, `src/DotNetForge.Web/**/*.cshtml`, `**/*.cs` and `wwwroot/js/**/*.js`, `extensions/**/*.cshtml`, the installed tree and the targets file |
| When it runs | before `BeforeBuild` and `ResolveProjectStaticWebAssets`, so even a clean build serves the new file; never in IDE design-time builds (`DesignTimeBuild=true`), which need no stylesheet and would run npm beside the real build |

| Setting | Effect |
| --- | --- |
| `-p:DnfSkipTailwind=true` | compile without regenerating `wwwroot/dnf.css` (for parallel builds of one checkout, which must not all write the file); if the file does not exist yet it is still generated |

**Upgrading Tailwind.** In `tools/`: `npm install -E @tailwindcss/cli@<version>` (or `@latest`), which
updates `package.json` and `package-lock.json`; the next `dotnet build` installs it and recompiles. Dependabot opens a
pull request for new versions weekly (`.github/dependabot.yml`, directory `/tools`). Check the
[UI tests](../guides/testing.md#ui-tests) and the in-app UI docs after a major version, and keep the version named in
[Tailwind in an extension](extensions.md#tailwind-in-an-extension) in step.

CI installs Node.js with `actions/setup-node` in every job that builds. `docker/Dockerfile` copies Node.js from the
official `node:22-bookworm-slim` image into its .NET build stage and runs `npm ci` in `tools/` as its own cached layer;
the runtime image has no Node.js. Both need network access to the npm registry (or a mirror set with
`npm config set registry`).

### What is scanned

Tailwind generates only the utilities it finds in scanned files. `Styles/dnf.css` registers:

```css
@source "../";                                  /* src/DotNetForge.UI: tag helpers, dnf.js */
@source "../../DotNetForge.Web";                 /* views, C# that renders markup, screen scripts */
@source "../../../extensions";                   /* the bundled extensions */
@source not "../../DotNetForge.Web/wwwroot/lib";
@source not "../../DotNetForge.Web/wwwroot/builder";
@source not "../../DotNetForge.Web/wwwroot/js/builder";
```

Git-ignored files (`bin/`, `obj/`) and CSS files are skipped. So `dnf.css` contains only the utilities used by the
library, the CMS and the extensions in this repository. A class built at runtime from strings Tailwind never saw
(`"gap-" + n`) does not exist - write class names in full. An extension installed later compiles its own utilities
([extensions](extensions.md#tailwind-in-an-extension)).

## Planned (not implemented)

Nothing is specified beyond what is built.
