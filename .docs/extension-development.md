# Extension Development

Extensions live under `extensions/<type>/<your-extension>/` and are isolated from the core so updates
never overwrite them.

## 1. Create the manifest

Every extension needs a `dotnetforge.extension.json` with the eight required fields:

```json
{
  "id": "yourcompany.plugin.hello",
  "name": "Hello Plugin",
  "description": "Adds a hello widget.",
  "version": "1.0.0",
  "type": "plugin",
  "author": "Your Company",
  "entryPoint": "HelloPlugin",
  "permissions": ["content.read"]
}
```

`type` ∈ `theme | authentication | connector | library | admin | widget | provider | plugin | module`.
`version` must be semver. `permissions` must be non-empty dotted keys. Optional: `website`,
`license`, `dependencies`, `routes`, `settings`.

The CMS validates the manifest before install (`ManifestValidator`); invalid manifests are rejected.
The admin **Plugins** page lists discovered extensions and flags invalid manifests.

## 2. Implement an extension point

Reference `DotNetForge.Abstractions` and implement the interface for your type, e.g. a plugin:

```csharp
using DotNetForge.Abstractions.Extensions;

public sealed class HelloPlugin : IPluginExtension
{
    public string Id => "yourcompany.plugin.hello";
    public string Name => "Hello Plugin";
    public void OnEnabled() { /* register services / routes */ }
}
```

The extension host resolves enabled extensions through DI.

## 3. Test locally

Drop the folder under `extensions/plugins/` and open **Admin → Plugins** - your extension appears with
its manifest validation status. See also [theme-development.md](theme-development.md) and
[module-development.md](module-development.md).
