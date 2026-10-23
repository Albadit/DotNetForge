# Extension System (for AI agents)

Extensions live under `extensions/<type>/<name>/` with a `dotnetforge.extension.json` manifest, optional C# code
(built into the extension's own `bin/`) and optional Razor views/static files - separate from the core. A CMS user
works only in `extensions/`; `src/` is never edited to add one. Full description:
[extensions.md](../../.docs/features/extensions.md); how-to:
[extension-development.md](../../.docs/guides/extension-development.md); samples: [extensions/README.md](../../extensions/README.md).

## Manifest

Required (validated by `ManifestValidator`): `id`, `name`, `description`, `version` (semver), `type`, `author`,
`entryPoint`, `permissions` (non-empty, lower-case dotted keys). Optional: `assembly` (relative `.dll` path inside the
folder, e.g. `bin/Name.dll`; `entryPoint` is then the `IExtension` class), `website`, `license`, `dependencies`,
`routes` (informational), `settings`. `type` ∈ `theme`, `authentication`, `connector`, `library`, `admin`, `widget`,
`provider`, `plugin`, `module`.

## What the host does

- **Discovery:** `IExtensionLoader.Discover()` finds every manifest under `AppEnvironment.ExtensionsPath` (cached
  until a manifest changes).
- **Code:** at startup, before the container is built, `ExtensionRuntime` loads every valid manifest with an
  `assembly` into its own `ExtensionLoadContext` (host assemblies shared), creates the `IExtension` and runs
  `ConfigureServices`; `MapEndpoints` runs in `Program.cs`. A failure marks the extension "not running"; code changes
  need a restart.
- **Enable/disable:** every extension starts disabled; Super Admin enables it on Plugins
  (`ExtensionLifecycleService`: dependency checks, `InstalledExtensions` row, audit `plugin.enabled`/`plugin.disabled`).
- **The gate:** `ExtensionCatalog` (`GetActiveAsync`, `FindActiveAsync`, `IsActiveAsync`) - valid, first with its id,
  enabled, code loaded. Every slot asks it; never combine discovery, runtime and state yourself.
- **Slots:** admin tab (`/admin/ext/{id}`), dashboard widget, public theme (`ThemeResolver`), page module, endpoints
  (404 while inactive), media storage (`AddMediaStorage`), sign-in provider (`AddLoginProvider`), audit handlers
  (`AddAuditEventHandler`) - SDK in `src/DotNetForge.Sdk`.
- **Security:** extension code runs in-process with full privileges; manifest `permissions` are not enforced; an
  extension's endpoints are public unless it protects them (`AuthorizationPolicies.AdminArea`).
- `extensions/` ships in the publish output (Docker builds every extension project first) and is read-only at
  runtime: extensions must not write files (use `IFileStorage`) and must not use inline script/style (CSP). Secrets
  come from `ExtensionContext.GetEnvironmentValue` (environment, then `.env`).
- Not built: install, update, remove, configure, marketplace, permission enforcement, unloading.
