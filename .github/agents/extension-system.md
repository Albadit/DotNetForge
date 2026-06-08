# Extension System (for AI agents)

Extensions are the central extensibility mechanism and are kept strictly separate from the core so
the core can update without breaking them. They live under `extensions/<type>/<name>/`.

## Manifest: `dotnetforge.extension.json`

Required fields (all validated by `ManifestValidator`): `id`, `name`, `description`, `version`
(single field, semver), `type`, `author`, `entryPoint`, `permissions` (non-empty). Optional:
`website`, `license`, `dependencies`, `routes`, `settings`.

`type` is one of: `theme`, `authentication`, `connector`, `library`, `admin`, `widget`, `provider`,
`plugin`, `module`.

Example: [extensions/themes/default-theme/dotnetforge.extension.json](../../extensions/themes/default-theme/dotnetforge.extension.json).

## Validation & loading

- `ManifestValidator.Validate(manifest)` returns a `ValidationResult` (`IsValid`, `Errors[].Field`).
  Error `Field` values are PascalCase property names (`Version`, `Permissions`, …).
- `ExtensionLoader.Discover(extensionsRoot)` scans subfolders for the manifest, parses, and validates
  each. Invalid/unparseable manifests are reported but never loaded.
- The admin **Plugins** page lists installed extensions plus on-disk discoveries, flagging invalid
  manifests.

## Extension points

Interfaces in `DotNetForge.Abstractions.Extensions`: `IExtension` (base) plus `IThemeExtension`,
`IAuthenticationProviderExtension`, `IWidgetExtension`, `IModuleExtension`, `IConnectorExtension`,
`IProviderExtension`, `ILibraryExtension`, `IAdminExtension`, `IPluginExtension`. Extensions compile
against these and are resolved through DI.
