# Module Development

Modules are `module`-type extensions placed on pages by the page builder. They live under
`extensions/modules/<your-module>/`.

## Manifest

```json
{
  "id": "yourcompany.module.callout",
  "name": "Callout",
  "description": "A configurable callout block.",
  "version": "1.0.0",
  "type": "module",
  "author": "Your Company",
  "entryPoint": "CalloutModule",
  "permissions": ["content.read"]
}
```

## Entry point

Implement `IModuleExtension` and render from the per-module configuration the page builder supplies:

```csharp
using DotNetForge.Abstractions.Extensions;

public sealed class CalloutModule : IModuleExtension
{
    public string Id => "yourcompany.module.callout";
    public string Name => "Callout";

    public string Render(IReadOnlyDictionary<string, string?> config)
    {
        var text = config.TryGetValue("text", out var t) ? t : "Hello";
        return $"<div class=\"callout\">{System.Net.WebUtility.HtmlEncode(text)}</div>";
    }
}
```

## Notes

- Always HTML-encode untrusted configuration values to avoid XSS.
- Modules accumulate edits in a page's draft; publishing promotes the draft to live (see
  `content_manager.md`).
- Dashboard/admin widgets use `IWidgetExtension` instead.
