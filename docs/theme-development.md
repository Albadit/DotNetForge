# Theme Development

Themes are `theme`-type extensions under `extensions/themes/<your-theme>/`. They style **only public
frontend pages** - the admin area, setup page, and login page are never affected by a public theme.

## Manifest

```json
{
  "id": "yourcompany.theme.aurora",
  "name": "Aurora",
  "description": "A clean public theme.",
  "version": "1.0.0",
  "type": "theme",
  "author": "Your Company",
  "entryPoint": "AuroraTheme",
  "permissions": ["content.read", "media.read"],
  "settings": { "supportsLayouts": true, "supportsDarkMode": true }
}
```

A working sample lives at
[`extensions/themes/default-theme/`](../extensions/themes/default-theme/dotnetforge.extension.json).

## Entry point

Implement `IThemeExtension` (in `DotNetForge.Abstractions.Extensions`) and expose the layouts your
theme provides:

```csharp
public sealed class AuroraTheme : IThemeExtension
{
    public string Id => "yourcompany.theme.aurora";
    public string Name => "Aurora";
    public IReadOnlyList<string> Layouts => new[] { "Default", "Landing" };
}
```

## Layouts, templates & assets

A complete theme also ships Razor layouts/templates and static assets (CSS/JS/images). Themes can be
selected per page, set as the default site theme, and made tenant-specific. Theme settings are
surfaced via the manifest `settings` object.

## Isolation guarantee

The admin shell uses a dedicated built-in layout (`Areas/Admin/Views/Shared/_AdminLayout.cshtml`); a
broken or hostile public theme can never restyle or compromise the back office.
