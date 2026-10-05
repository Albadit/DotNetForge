# Extension System (for AI agents)

Extensions live under `extensions/<type>/<name>/` with a `dotnetforge.extension.json` manifest, separate from the
core. Full description: [extensions.md](../../.docs/features/extensions.md); how-to:
[extension-development.md](../../.docs/guides/extension-development.md).

## Manifest

Required (validated by `ManifestValidator`): `id`, `name`, `description`, `version` (semver), `type`, `author`,
`entryPoint`, `permissions` (non-empty, lower-case dotted keys). Optional: `website`, `license`, `dependencies`,
`routes`, `settings`. `type` ∈ `theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`,
`plugin`, `module`.

## What the host does today

- `ExtensionLoader.Discover()` finds every manifest under `AppEnvironment.ExtensionsPath` recursively, parses and
  validates it (cached until a manifest changes). Invalid manifests are listed as invalid and never rendered.
- The **Plugins** screen (Super Admin) lists discovered manifests with validity.
- `admin` extensions with `Views/Index.cshtml` get a sidebar tab at `/admin/ext/{id}`, rendered by runtime Razor
  compilation inside an iframe; manifest `settings` arrive as `ViewData["Settings"]`.
- No assembly loading, DI registration, enable/disable, install or marketplace. The `IExtension` interfaces in
  `DotNetForge.Abstractions.Extensions` are not implemented or called anywhere.
- Admin extension views run with full host privileges; manifest `permissions` are not enforced.
- `extensions/` ships in the publish output and is read-only at runtime: extensions must not write files (use
  `IFileStorage` via `@inject` if they need storage) and must not use inline script/style (CSP).
