# Theming

Every colour, size and effect of the Dnf components is a `--dnf-*` custom property in
`src/DotNetForge.UI/Styles/tokens.css` (layer `base`). Components use the semantic tokens only, never literal values;
re-theming is overriding tokens. How tokens reach Tailwind utilities: [styling → theme mapping](styling.md#theme-mapping).

## Tokens

### Palette

The brand comes from the logo: charcoal `#14171c` (`--dnf-gray-950`) and forge orange `#f76707` (`--dnf-forge-500`).

| Token | Values |
| --- | --- |
| `--dnf-forge-50` … `--dnf-forge-800` | `#fff4ec`, `#ffe3cf`, `#ffc49c`, `#ff9a4d`, `#ff8124`, `#f76707` (500), `#e8590c`, `#c2410c`, `#9a3412` |
| `--dnf-gray-0` … `--dnf-gray-1000` | `#ffffff` (0), `#f7f8f9` (50), `#eef0f2` (100), `#e1e4e8` (200), `#c9ced5` (300), `#9aa1ab` (400), `#6c727d` (500), `#4d535c` (600), `#363b42` (700), `#23272d` (800), `#1d2127` (850), `#16191e` (900), `#14171c` (950), `#0d0f12` (1000) |

The palette is raw material for the semantic tokens; components don't use it directly.

### Typography

| Token | Value |
| --- | --- |
| `--dnf-font-sans` | `"Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, "Inter", "Helvetica Neue", Arial, sans-serif` (system fonts, no web font) |
| `--dnf-font-mono` | `ui-monospace, "Cascadia Code", "SF Mono", Menlo, Consolas, "Liberation Mono", monospace` |
| `--dnf-text-xs` / `-sm` / `-base` / `-md` / `-lg` / `-xl` / `-2xl` / `-3xl` / `-4xl` | 12 / 13 / 14 (admin body) / 15 / 16 / 18 / 22 (page titles) / 28 / 36 px (in rem) |
| `--dnf-leading-tight`, `--dnf-leading` | `1.25`, `1.5` |
| `--dnf-weight-regular` / `-medium` / `-semibold` / `-bold` | 400 / 500 / 600 / 700 |

### Space, controls, radius

| Token | Value |
| --- | --- |
| `--dnf-space-1` … `-6`, `-8`, `-10`, `-12`, `-16` | a 4px grid: 0.25rem … 1.5rem, 2rem, 2.5rem, 3rem, 4rem |
| `--dnf-control-sm` / `-md` / `-lg` | 30 / 36 / 44 px - control heights; 44px is also the minimum touch target |
| `--dnf-radius-sm` / `-md` / `-lg` / `-xl` / `-full` | 4 / 6 / 10 / 14 / 999 px |
| `--dnf-forge-cut` | `polygon(0 0, 100% 0, calc(100% - 0.5rem) 100%, 0 100%)` - the logo's diagonal cut, for `clip-path` brand accents |

### Layout

| Token | Value | Used by |
| --- | --- | --- |
| `--dnf-sidebar-width` | 16rem | the CMS admin shell |
| `--dnf-topbar-height` | 3.5rem | the CMS admin shell; the sticky offset of `.dnf-split__aside` |
| `--dnf-content-max` | 96rem | `.dnf-container` |
| `--dnf-form-max` | 44rem | `.dnf-form--narrow` |

### Motion

| Token | Value |
| --- | --- |
| `--dnf-duration-fast` | 100ms |
| `--dnf-duration` | 160ms |
| `--dnf-ease` | `cubic-bezier(0.2, 0, 0, 1)` |

Under `prefers-reduced-motion: reduce` both durations are `0ms`.

### Stacking

| Token | Value | Used by |
| --- | --- | --- |
| `--dnf-z-sticky` | 10 | the CMS admin shell |
| `--dnf-z-drawer` | 40 | the CMS admin shell |
| `--dnf-z-dialog` | 50 | nothing in the library (dialogs use the top layer) |
| `--dnf-z-toast` | 60 | `.dnf-toast-region`, `.dnf-skip-link` |
| `--dnf-z-tooltip` | 70 | `.dnf-tooltip` |

Dropdowns, popovers, dialogs and the tooltip open in the browser's top layer, where `z-index` does not apply
([extensions → overlays](extensions.md#overlays)).

### Semantic colours

Every colour is `light-dark(light, dark)`.

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--dnf-background` | gray-50 | gray-1000 | page |
| `--dnf-foreground` | gray-950 | gray-100 | text |
| `--dnf-muted-foreground` | gray-600 | `#a6adb8` | secondary text |
| `--dnf-subtle-foreground` | gray-500 | `#8a919c` | hints, placeholders |
| `--dnf-inverse-foreground` | gray-0 | gray-950 | text on an inverted surface |
| `--dnf-surface` | gray-0 | gray-900 | cards, controls |
| `--dnf-surface-foreground` | = `--dnf-foreground` | | text on a surface |
| `--dnf-surface-raised` | gray-0 | gray-850 | menus, dialogs, toasts |
| `--dnf-surface-muted` | gray-100 | gray-850 | table heads, card footers, inline code |
| `--dnf-surface-hover` | gray-100 | gray-800 | hovered rows and items |
| `--dnf-border` / `--dnf-border-strong` | gray-200 / gray-300 | `#2a2f37` / `#3a4049` | dividers / control borders |
| `--dnf-primary` | forge-500 | forge-500 | fills: primary buttons, indicators, focus |
| `--dnf-primary-foreground` | gray-0 (white) | gray-0 (white) | text on `--dnf-primary` |
| `--dnf-primary-text` | forge-700 | forge-300 | orange text and links |
| `--dnf-secondary` / `-foreground` | = surface / foreground | | the default button |
| `--dnf-success` | `#15703f` | `#4cc38a` | |
| `--dnf-warning` | `#9a5200` | `#f5b041` | |
| `--dnf-danger` | `#c92a2a` | `#ff6b6b` | |
| `--dnf-danger-foreground` | gray-0 | gray-950 | text on `--dnf-danger` |
| `--dnf-info` | `#1c64c8` | `#74a7ff` | |
| `--dnf-focus` | = `--dnf-primary` | | focus outline |
| `--dnf-nav-background`, `-surface`, `-hover`, `-border`, `-foreground`, `-muted-foreground`, `-strong-foreground` | white / grays | `#0a0c0f` … | navigation (the CMS sidebar) |
| `--dnf-brand-background`, `-surface`, `-hover`, `-border`, `-foreground`, `-strong-foreground` | white / grays | `#0a0c0f` … | brand chrome (the CMS dashboard band, sign-in panel) |
| `--dnf-code-background` / `-foreground` | gray-100 / gray-950 | `#0a0c0f` / forge-100 | `.dnf-code-block` |
| `--dnf-tooltip-background` / `-foreground` | gray-950 / gray-0 | gray-100 / gray-950 | `.dnf-tooltip` (contrasts with the page) |
| `--dnf-shadow-xs` / `-sm` / `-md` / `-lg` | charcoal at 4-28 % | black at 20-70 % | elevation |
| `--dnf-backdrop` | `rgb(13 15 18 / 0.55)` | `rgb(0 0 0 / 0.65)` | `::backdrop` of dialogs |
| `--dnf-selection` | forge-200 | forge-200 | `::selection` (text gray-950) |

### Derived tokens

Computed from the leaves with `color-mix()`, on `:root` **and on every `.dnf-scope` element**:

| Token | From |
| --- | --- |
| `--dnf-primary-hover` | `--dnf-primary` mixed with 8 % black |
| `--dnf-primary-soft`, `--dnf-primary-soft-border` | `--dnf-primary` tinted (light) or translucent (dark) |
| `--dnf-success-soft`, `--dnf-warning-soft`, `--dnf-danger-soft`, `--dnf-info-soft` | their colour, tinted / translucent |
| `--dnf-danger-hover` | `--dnf-danger` 12 % darker (light) / lighter (dark) |
| `--dnf-focus-ring` | `0 0 0 2px var(--dnf-surface), 0 0 0 4px var(--dnf-focus)` (a `box-shadow`) |

A custom property is computed where it is declared, so a derived token declared on `:root` keeps the root's
`--dnf-primary` even inside an element that overrides it. Declaring them again on `.dnf-scope` fixes that: a scope that
overrides `--dnf-primary` also gets a matching `--dnf-primary-hover` and `-soft`.

## Contrast

From `tokens.css`: every text token meets WCAG 2.2 AA (≥ 4.5:1) on `--dnf-surface` and `--dnf-background` in both
themes, with one deliberate exception: orange is a **fill** colour with **white** on top (`--dnf-primary-foreground`,
3.0:1 - the brand's choice; it passes WCAG only for large text, and `AccessibilityTests` allows exactly this pair).
Orange **text** uses `--dnf-primary-text` (5.2:1 light, 8.4:1 dark); never use `--dnf-forge-500` for text. Check
contrast again when you add or override a colour token - for a darker fill with white text, override `--dnf-primary`
(e.g. `#cc4a0a`, 4.6:1).

## Light and dark

- `:root` declares `color-scheme: light dark`, and every colour is `light-dark(light, dark)`: the browser picks the
  value from the element's **effective** colour scheme. Components never test the theme.
- Without a choice, the system preference (`prefers-color-scheme`) applies.
- `data-theme="light"` or `data-theme="dark"` sets `color-scheme` on that element - on `<html>` for the whole page,
  or **on any subtree** to force one theme there:

```html
<section data-theme="dark" class="dnf-card">…always dark…</section>
```

Tailwind's `dark:` variant follows `data-theme="dark"` / `"light"` (page or subtree) and otherwise the system preference
([styling](styling.md#theme-mapping)). Logo pairs use it: `<img class="block dark:hidden" src="logo.svg">` +
`<img class="hidden dark:block" src="logo-dark.svg">`.

## Switching

The user's choice is stored in `localStorage` under `dnf-theme` (`light` or `dark`; no entry = system).

- **`dnf-theme.js`** - load it synchronously in `<head>`, before the stylesheet. It sets `<html data-theme>` from
  `dnf-theme` before the first paint (no flash) and adds `class="dnf-js"` to `<html>`, so `.dnf-js-only` elements (a
  theme menu) show from the first paint. Blocked storage falls back to the system preference.
- **`dnf.js`** - any element with `data-theme-set="light|dark|system"` switches on click: it sets or removes
  `data-theme` on `<html>`, stores or clears `dnf-theme`, and sets `aria-checked="true"` on the matching
  `[data-theme-set]` elements (`"false"` on the others).

The CMS theme menu (`src/DotNetForge.Web/Views/Shared/_ThemeMenu.cshtml`):

```cshtml
<div class="dnf-dropdown dnf-js-only">
    <button type="button" class="dnf-button dnf-button--ghost dnf-button--icon-only" popovertarget="theme-menu" aria-haspopup="menu"
            aria-expanded="false" aria-label="Colour theme">
        <dnf-icon name="sun" class="inline dark:hidden" />
        <dnf-icon name="moon" class="hidden dark:inline" />
    </button>
    <div class="dnf-dropdown__menu" id="theme-menu" popover role="menu" aria-label="Colour theme" data-placement="bottom-end">
        <dnf-dropdown-label>Theme</dnf-dropdown-label>
        <dnf-dropdown-button icon="sun" role="menuitemradio" aria-checked="false" data-theme-set="light">Light</dnf-dropdown-button>
        <dnf-dropdown-button icon="moon" role="menuitemradio" aria-checked="false" data-theme-set="dark">Dark</dnf-dropdown-button>
        <dnf-dropdown-button icon="monitor" role="menuitemradio" aria-checked="false" data-theme-set="system">System</dnf-dropdown-button>
    </div>
</div>
```

The checked item shows a check mark (`.dnf-dropdown__item[aria-checked="true"]`). Every page that loads `dnf-theme.js`
shares the choice - the admin, the sign-in screens and admin extension documents.

## Re-theming a subtree

Override **leaf** tokens on an element; mark it `.dnf-scope` when you override a colour that has derived tokens:

```css
.reports.dnf-scope {
    --dnf-primary: #2563eb;            /* hover, soft and soft-border follow (derived) */
    --dnf-primary-foreground: #ffffff;
    --dnf-primary-text: light-dark(#1d4ed8, #93c5fd);
    --dnf-radius-md: 2px;
}
```

Everything inside - components and Tailwind utilities (`bg-primary`, `rounded-md`) - follows, because both read the
tokens through `var()`. Keep light and dark values with `light-dark()`. Extension roots are scopes
([extensions](extensions.md#isolation)).

## Planned (not implemented)

Nothing is specified beyond what is built.
