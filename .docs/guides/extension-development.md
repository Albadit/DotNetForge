# Extension development

How to build an extension: everything happens inside one folder under `extensions/`; you never edit `src/`. What
the host does with extensions (loading, slots, enabled state, security model) is in
[features/extensions.md](../features/extensions.md) - read its [security model](../features/extensions.md#security-model)
first: extension code runs inside the CMS process with full access.

## Quick start

A plugin that adds an endpoint, in four files' worth of work:

```text
extensions/plugins/hello/
├── dotnetforge.extension.json
├── Hello.csproj
└── HelloExtension.cs
```

`dotnetforge.extension.json`:

```json
{
  "id": "yourcompany.plugin.hello",
  "name": "Hello",
  "description": "Says hello at /hello.",
  "version": "1.0.0",
  "type": "plugin",
  "author": "Your Company",
  "assembly": "bin/Hello.dll",
  "entryPoint": "Hello.HelloExtension",
  "permissions": ["core.read"],
  "settings": { "greeting": "Hello from an extension" }
}
```

`Hello.csproj` - [extensions/Directory.Build.props](../../extensions/Directory.Build.props) supplies the target
framework, the output folder and the SDK reference, so the project is empty:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

`HelloExtension.cs`:

```csharp
using DotNetForge.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Hello;

public sealed class HelloExtension : IExtension
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context) =>
        endpoints.MapGet("/hello", () => context.GetString("greeting"));
}
```

Then, from the repository root (or from the published app's folder):

```bash
dotnet build extensions/plugins/hello          # → extensions/plugins/hello/bin/Hello.dll
dotnet run --project src/DotNetForge.Web       # (re)start the CMS - code loads at startup
```

Open **Plugins** (Super Admin), click **Enable** on *Hello*, and `GET /hello` answers. Disable it and `/hello` is a
404 again - no restart needed for either.

## The extension folder

```text
extensions/<type-folder>/<name>/
├── dotnetforge.extension.json   the manifest (required)
├── <Name>.csproj + *.cs          code (optional) → bin/<Name>.dll
└── Views/                        Razor views (optional), compiled in memory at runtime
    ├── _ViewImports.cshtml       @using / @inject / @addTagHelper for these views
    ├── Index.cshtml | Widget.cshtml | Page.cshtml | Module.cshtml    depending on the type (below)
    └── Resources/                static files: css, js, images, fonts
```

An extension can be **views only** (no `assembly` in the manifest, no project) - the sample theme and admin tab are -
or **code only**, or both. `bin/` and `obj/` are git-ignored; ship the source, build in CI/Docker.

### Manifest

Required: `id`, `name`, `description`, `version` (semver), `type`, `author`, `entryPoint`, `permissions` (non-empty,
lower-case dotted keys). Optional: `assembly`, `dependencies`, `settings`, `website`, `license`, `routes`. Comments
and trailing commas are allowed. Full rules: [manifest](../features/extensions.md#manifest).

- `id` - globally unique, e.g. `yourcompany.widget.sales`. It is the extension's identity in URLs and the database;
  don't change it after release.
- `type` - `admin`, `widget`, `theme`, `module`, `plugin`, `library`, `connector`, `authentication` or `provider`. It
  decides which view slot is used; code slots are open to every type.
- `assembly` + `entryPoint` - the built DLL (relative path, normally `bin/<Name>.dll`) and the class implementing
  `IExtension` (full name, e.g. `Hello.HelloExtension`).
- `dependencies` - ids of extensions that must be enabled before this one (e.g. a library you use).
- `settings` - any JSON values; read them with `ExtensionContext`. **Never put secrets here** - see
  [Secrets](#secrets).

Check it on **Plugins**: an invalid manifest is listed with its errors; a valid one with a build or load problem shows
**not running** and the reason.

## Code: the entry point

```csharp
public sealed class MyExtension : IExtension
{
    // Register services and plug into host slots. Runs once at startup.
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) { }

    // Map minimal-API endpoints. Runs once at startup; answered only while the extension is enabled.
    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context) { }
}
```

Both members are optional. The class needs a public parameterless constructor. Both run for every valid extension at
startup, **enabled or not** - the host switches contributions on and off at request time - so don't start work in
them; a background service should check `IExtensionState.IsEnabledAsync(context.Id)`.

`ExtensionContext`: `Id`, `Name`, `Version`, `Type`, `Directory` (read-only!), `Settings`, `GetString`, `GetBool`,
`GetInt64`, `GetDouble`, `GetStringList`, `GetEnvironmentValue`, `PublicResourceBase`, `AdminResourceBase`.

MVC controllers in your assembly work too (attribute routes) and are switched off with the extension like endpoints.

### Dependencies (NuGet packages and other extensions)

- **A package the host does not ship**: add a normal `PackageReference`; it is copied to your `bin/` and loaded in
  your extension's own load context - other extensions can use a different version.
- **A package the host already ships** (ASP.NET Core, EF Core, `AWSSDK.S3`, ...): the host's copy is always used. Compile
  against the host's version and add `ExcludeAssets="runtime"` so it isn't copied (see the S3 sample's project file).
- **Another extension (a library)**: reference its project with `Private="false"` and list its id in the manifest
  `dependencies`. At runtime you share the library's single copy:

  ```xml
  <ProjectReference Include="../../libraries/markdown-kit/MarkdownKit.csproj" Private="false" />
  ```

### What you can use from the host

| Need | Use |
| --- | --- |
| Store files | inject `DotNetForge.Abstractions.Storage.IFileStorage`; keys must pass `StorageKey.IsValid`; use your own prefix `ext/<name>/...` |
| Write an audit entry | inject `DotNetForge.Shared.Auditing.IAuditService`, call `LogAsync("<your>.action", ...)` |
| Live pages with URLs | inject `DotNetForge.Shared.Content.IPublishedPages` |
| Site name / URL | inject `DotNetForge.Shared.Configuration.AppEnvironment` (`AppName`, `AppUrl`) |
| Protect an endpoint | `.RequireAuthorization(AuthorizationPolicies.AdminArea)` (`DotNetForge.Shared.Constants`) |
| Rate-limit a public form | `.RequireRateLimiting(RateLimitPolicies.Forms)` |
| Validate a form post | inject `IAntiforgery`, `await antiforgery.ValidateRequestAsync(http)`; render `@Html.AntiForgeryToken()` |
| Anything else the CMS registers | constructor injection, e.g. `DotNetForgeDbContext` - this couples you to host internals and bypasses tenant scoping; filter by `TenantId` yourself |

## Slots by type

### Admin tab (`type: admin`)

`Views/Index.cshtml` renders as a complete HTML document inside an iframe at `/admin/ext/{id}`; the sidebar gets an
**Extensions** entry. Use a layout (`Views/_ViewStart.cshtml` → `Layout = "Shared/_Layout.cshtml";`) and link your
files through `ViewData["ResourceBase"]`. Sample: `extensions/admin/audit-dashboard` ([extension host](../pages/extension-host.md)).

**Look like the admin.** The iframe document does not inherit the admin's styles, but it may reuse them: link
`<link rel="stylesheet" href="/css/forge/tokens.css">`, `base.css` and `components.css` and load
`<script src="/js/forge/theme.js">` in `<head>` (the document then
follows the admin's light/dark choice), and add `@using DotNetForge.Web.Ui` + `@addTagHelper *, DotNetForge.Web` to
your `_ViewImports.cshtml` for `<icon>`, `<badge>`, `<stat-card>`, `<timestamp>`, `<empty-state>` and `<alert>`. The
audit-dashboard sample does all of this. Classes and tag helpers: [design system](../architecture/design-system.md).

### Dashboard widget (`type: widget`)

`Views/Widget.cshtml` renders inside a card on `/admin` (the card title is the manifest `name`). It is a partial:
no layout; it sits inside the admin page, so the [design system](../architecture/design-system.md#components) classes
(`stat-value`, `text-muted`, `text-sm`, `badge`, ...) apply. The host's tag helpers are only available if your
widget's `_ViewImports.cshtml` adds `@addTagHelper *, DotNetForge.Web`. Get data by injecting a service
your `ConfigureServices` registered. Sample: `extensions/widgets/weather-widget`.

### Theme (`type: theme`)

Replaces the public site's views: `Views/Page.cshtml` (model `DotNetForge.Shared.Entities.Page`) for content pages
and `Views/Index.cshtml` (model `List<(string Title, string Slug)>`) for the root when no root page is published. A
missing one falls back to the built-in view. Extra ViewData: `AppName`, `Menu` (`IReadOnlyList<PublishedPage>`),
`RouteValues` (dynamic routes). Call `@await Component.InvokeAsync("ExtensionModules", new { page = Model })` where
modules should appear. The active theme is the tenant's default theme (`dotnetforge.theme.default` unless changed)
when enabled, otherwise the first enabled theme by name ([themes](../features/themes.md)). Sample:
`extensions/themes/default-theme`.

### Page module (`type: module`)

`Views/Module.cshtml` (model `Page`) renders on every content page whose URL path is listed in the `pages` setting
(`"contact"`, `"about/team"`, `"/"`, or `"*"` for all). Posts go to your own endpoint. Sample:
`extensions/modules/contact-form` (form + endpoint + antiforgery + rate limit + storage + audit).

### Plugin (`type: plugin`)

General features through code: endpoints, controllers, services. Sample: `extensions/plugins/seo-toolkit`
(`/sitemap.xml`, `/robots.txt`).

### Library (`type: library`)

Code other extensions use; see [Dependencies](#dependencies-nuget-packages-and-other-extensions). It must be enabled
before its dependents and can't be disabled while they are. Sample: `extensions/libraries/markdown-kit`.

### Connector (`type: connector`)

External services. A media storage backend implements `IFileStorage` and registers it:

```csharp
services.AddMediaStorage(context, sp => new MyStorage(...));
```

While enabled, all media is stored and served through it; existing media is **not** moved
([storage connectors](../features/media-storage.md#storage-connectors)). Sample: `extensions/connectors/s3-storage`.

### Authentication (`type: authentication`)

Register a remote authentication scheme that signs into `ExternalLogin.SignInScheme` and emits a `ClaimTypes.Email`
claim, then add the button:

```csharp
services.AddAuthentication().AddOAuth("myprovider", options =>
{
    options.SignInScheme = ExternalLogin.SignInScheme;
    // client id/secret from context.GetEnvironmentValue(...), endpoints, scopes, claims
});
services.AddLoginProvider(context, "myprovider", "My Provider");
```

The host signs in the existing CMS user with exactly that email; it never creates accounts. Only emit an email the
provider has verified. A remote scheme is consulted on every request, so register it only when configured (throw
from `ConfigureServices` otherwise - the reason appears on Plugins). Sample: `extensions/authentication/discord-login`
([external sign-in](../features/authentication.md#external-sign-in-extensions)).

### Provider (`type: provider`)

Pluggable services - typically reacting to CMS events:

```csharp
services.AddAuditEventHandler<MyHandler>(context);   // MyHandler : IAuditEventHandler
```

Handlers get every audit entry (`AuditEvent`) in the background, never slowing the request
([extension handlers](../features/audit-logging.md#extension-handlers)). Sample: `extensions/providers/sms-notifications`.

## Settings and secrets

`settings` in the manifest are defaults you ship; read them through `ExtensionContext` (code) or
`ViewData["Extension"]` (views). Changing them means editing the manifest (a restart is needed for values your code
read at startup).

### Secrets

API keys and passwords go in `.env` (or real environment variables), never in the manifest or Git. Read them with
`context.GetEnvironmentValue("MY_EXTENSION_API_KEY")` - process environment first, then `.env`. Prefix the names with
your extension's name. Document them in your extension's readme.

## Rules for extension code and views

- **Never write files at runtime.** The deployment directory - `extensions/` included - is read-only in production
  ([deployment](deployment.md#read-only-deployment-requirements)). Use `IFileStorage` or the database.
- **No inline script or style** in views: the CSP (`script-src 'self'; style-src 'self'`) blocks `<script>` blocks,
  `<style>` blocks, `style="..."` and `on...=` (in Development it only reports, so check the browser console). Put
  CSS/JS in `Views/Resources/` and link them through `ViewData["ResourceBase"]`; pass data with `data-*` attributes.
- **`@page` is a Razor directive**: don't name a variable `page` in a view (`@foreach (var page in ...)` fails to
  compile) - use `item`.
- **Public endpoints are public.** Add authorization, antiforgery and rate limiting yourself.
- **Tenant data**: filter by tenant when you query host tables directly.
- **Don't throw from views**: a failing widget or module shows "could not be displayed" and logs the error, but an
  admin tab or theme view error is a server error page.

## Build, run, ship

| Situation | Command / step |
| --- | --- |
| Build one extension | `dotnet build extensions/<type>/<name>` |
| Build the CMS and all sample extensions | `dotnet build` (the samples are in `DotNetForge.slnx`) |
| After changing code | rebuild, then restart the app (the DLL is not locked, so you can rebuild while it runs) |
| After changing views or CSS | nothing - views recompile on the next request, files are read per request |
| After changing the manifest | nothing for views/listing (a file watcher picks it up); restart if your code reads the setting at startup |
| Against a published CMS (no `src/`) | put the folder under the app's `extensions/` and `dotnet build` it there: the props file references the SDK DLLs next to the app |
| Docker | `docker/Dockerfile` builds every `extensions/**/*.csproj` before publishing - nothing to do |

Logs: at startup every extension with code logs `Extension {id} {version} loaded from ...` or
`Extension {id} is enabled but not running: <reason>` (a warning; while the extension is disabled the same reason is
logged as information).

## Testing

- Unit-test your own classes with any test project referencing your extension project.
- End to end: the host's integration tests show the pattern - `tests/fixtures/extensions/` holds fixture extensions,
  `DotNetForgeWebFactory(extensionsPath: ...)` points the host at them, and the tests enable extensions through the
  Plugins screen and call their URLs (`tests/DotNetForge.IntegrationTests/ExtensionTests.cs`).

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Not on Plugins | the file is named exactly `dotnetforge.extension.json` and lies under the extensions folder |
| "invalid manifest" | the listed field errors; JSON syntax |
| "not running: '…dll' was not found" | build the extension; `assembly` matches the output (`bin/<AssemblyName>.dll`) |
| "not running: Entry point … was not found / must implement" | `entryPoint` is the full type name of a public, non-abstract class implementing `DotNetForge.Sdk.IExtension` with a parameterless constructor |
| "not running: … added or changed after the app started" | restart the app |
| "not running: ConfigureServices failed: …" | the message is your exception's; often missing secrets |
| Enable refused: "needs X" | enable the dependency first |
| Endpoint 404 | the extension is enabled and running? (A route that duplicates a host route fails with an ambiguous-match 500 instead.) |
| `FileNotFoundException` for a dependency | the package is not in `bin/` - don't use `ExcludeAssets="runtime"` for packages the host lacks |
| `InvalidCastException` between "identical" types | you shipped your own copy of a host contract; reference SDK projects with `Private="false"` (the props file does) |
| View compile error | `_ViewImports.cshtml` `@using`s; types from your assembly need it built and loaded (restart) |
| Works locally, unstyled in production | inline style/script blocked by the CSP |
| `IOException` / `UnauthorizedAccessException` in production | your code writes to disk |

## Reference samples

[extensions/README.md](../../extensions/README.md) lists every sample with how to try it.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
The host side (install, update, marketplace) is described in [extensions](../features/extensions.md#planned-not-implemented).

### Requirements

- **Packaging:** one archive with `dotnetforge.extension.json` at its root, the compiled output and all assets,
  installable into an empty `extensions/` subfolder; the layout matches what the marketplace and installer expect.
- **Installing:** through the Marketplace or a package upload, with validation before install and registration of the
  declared permissions. Today: folder placement and redeploy ✔, validation ✔, enable ✔.
- **Configuring:** editing `settings` from the admin, validated against a settings definition (today: manifest only).
- **Updating:** the CMS checks for extension updates, backs up, replaces only that extension's files, restores on
  failure. Core updates never overwrite `extensions/`.
- **Permissions:** permission names come from `PermissionKeys` (the spec's `media.write` is `media.upload` here) and
  are enforced at runtime (today: recorded only).

### Acceptance criteria

- [x] Creating an extension is documented, including the `extensions/` location and a valid manifest (this guide).
- [x] The documented manifest has exactly the required fields `id`, `name`, `description`, `version`, `type`,
  `author`, `entryPoint`, `permissions` and a single `version` (this guide, `ManifestValidator`).
- [x] Creating a theme is documented, and themes never affect the admin area ([Theme](#theme-type-theme)).
- [ ] Creating a module is documented, including page-builder availability and the built-in Razor module (modules ✔;
  there is no page builder).
- [x] Creating a dashboard widget is documented ([Dashboard widget](#dashboard-widget-type-widget)).
- [x] Creating an authentication provider is documented, with secrets in `.env`.
- [x] Creating a connector is documented, including external storage connectors for media.
- [ ] Packaging, installing and updating an extension are each documented, including manifest validation before
  install/update and isolation from core updates.
