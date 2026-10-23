# Default Theme (sample extension)

A views-only **theme** extension: no code, just Razor views and a stylesheet. While enabled it renders the public
site - content pages (`Views/Page.cshtml`), the site root when no root page is published (`Views/Index.cshtml`) and
the shared layout with the menu (`Views/Shared/_Layout.cshtml`). Its CSS is served from `Views/Resources/` at
`/ext/dotnetforge.theme.default/resources/...`.

Settings (manifest): `supportsDarkMode` (follow the visitor's light/dark preference), `accent` (`indigo`, `teal`,
`rose`), `tagline` (footer and home page).

See [extensions/README.md](../../README.md) to try it and the [theme section](../../../.docs/guides/extension-development.md#theme-type-theme)
of the extension development guide to build your own.
