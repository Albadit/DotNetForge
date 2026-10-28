# Providers

## Purpose

How people sign in: the built-in **Email** provider (email and password), the ten built-in OAuth providers (Google,
Microsoft, Apple, Facebook, GitHub, LinkedIn, X (Twitter), Discord, GitLab, Twitch) with their credentials and on/off
switch, and - read-only - the providers that `authentication` extensions add. Used by roles with `Settings` / `read`
(by default Super Admin and Admin); saving needs `Settings` / `update`. Rules: [authentication →
providers](../features/authentication.md#providers), [built-in OAuth
providers](../features/authentication.md#built-in-oauth-providers) and [external
sign-in](../features/authentication.md#external-sign-in).

## Route / Navigation

| Item | Value |
| --- | --- |
| Routes | `GET /admin/providers` (list) · `GET /admin/providers/{key}` (edit) · `POST /admin/providers/{key}` (save); `{key}` is a catalog key (`google`, `microsoft`, `apple`, `facebook`, `github`, `linkedin`, `x`, `discord`, `gitlab`, `twitch`, case-insensitive), anything else → `404` |
| Title (h1, last breadcrumb) | "Providers" (description "How people sign in: with their email and password, or with an account they already have elsewhere."); the edit screen: the provider's name |
| Navigation entry | sidebar **Settings → Users & Permissions Plugin → Providers** (icon `lock`) |
| Breadcrumbs | Settings › Users & Permissions Plugin › Providers (› *provider*) |
| Linked from | the sidebar; the Email row links to [Advanced Settings](advanced-settings.md), extension rows to [Plugins](plugins.md) |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/AuthProvidersController.cs  Index, Edit (GET/POST); ProviderState, ProviderRow, ExtensionRow, view models
src/DotNetForge.Web/Areas/Admin/Views/AuthProviders/Index.cshtml        Sign-in providers table + From extensions list
src/DotNetForge.Web/Areas/Admin/Views/AuthProviders/Edit.cshtml         three cards: register, credentials, turn on
src/DotNetForge.Web/Authentication/AuthProviderService.cs               AllAsync, SaveAsync (validation, protected secrets, audit); ProviderInput
src/DotNetForge.Web/Authentication/OAuthProviderCatalog.cs              the ten providers: fields, help text, console link, scopes, callback path
src/DotNetForge.Web/Authentication/OAuthProviderRegistry.cs             ProviderSettings (stored JSON), the sign-in schemes a save refreshes
src/DotNetForge.Web/Services/LoginProviders.cs                          ExtensionProviders (the From extensions list)
src/DotNetForge.Shared/Constants/AuthProviderKeys.cs                    keys and names; DataSeeder keeps one AuthProvider row per key
tests/DotNetForge.IntegrationTests/SignInSecurityTests.cs               Google round trip, Apple validation, secrets never shown
```

## Page layout

```text
Providers (admin layout)
├── card **Sign-in providers** ("An enabled provider adds "Sign in with ..." to the sign-in page. Only verified email addresses are accepted; with Enable sign-ups on (Advanced Settings), a new email gets a new account.")
│   └── table: **Provider** (name link + key) · **Status** · **Callback URL** (APP_URL + /signin/<key>) · [Edit]
│       ├── first row: **Email** "Email address and password - built in" · Enabled · - · [Settings] (→ /admin/advanced-settings)
│       └── one row per built-in OAuth provider, in catalog order
└── card **From extensions** ("Providers added by authentication extensions. They are configured in .env and turned on or off on Plugins.")
    └── list: name · "Extension <id> · scheme <scheme>" · Enabled/Disabled · [Plugins] - or the empty state

Edit - "<Provider>" (medium width), one form (POST /admin/providers/{key}, antiforgery, autocomplete off)
├── <form-errors />
├── alert (warning, only when a saved secret can't be decrypted) **The saved secret can't be read here**
└── fieldset (disabled without Settings update)
    ├── card **1. Register this site at <Provider>** · help text · [Open] (the provider's console, new tab)
    │   ├── **Callback / redirect URL** (Apple: **Return URL**) - APP_URL + /signin/<key> · [Copy] (JS only)
    │   │   "From APP_URL: people must open the site at this address for the provider to send them back."
    │   └── **Scopes**
    ├── card **2. Enter its credentials** ("Secrets are stored encrypted and never shown again; leave a secret empty to keep the saved one.")
    │   ├── **Client ID** (Apple: **Services ID**)
    │   ├── **Client secret** (password field; hint "A secret is saved." / "No secret saved yet.") - not for Apple
    │   ├── Apple: **Team ID** · **Key ID** · **Private key (.p8)**
    │   ├── Microsoft: **Tenant** (optional, placeholder common)
    │   └── GitLab: **Server** (optional, placeholder https://gitlab.com)
    └── card **3. Turn it on** ("Changes apply within seconds; no restart needed.")
        ├── switch **Offer "Sign in with <Provider>"**
        └── footer (only with Settings update): Cancel · [Save]
```

## Components

| Component | Inputs | Behaviour |
| --- | --- | --- |
| Status badge | `ProviderRow.State` + `Unreadable` | **Enabled** (success) - turned on and every field filled; **Disabled** - complete but off; **Not set up** - a required field is empty; **Secret unreadable** (warning) - a saved secret was encrypted on another site |
| Callback URL | `AppEnvironment.AppUrl` (trailing `/` removed) + `OAuthProviderDefinition.CallbackPath` | read-only; what to register at the provider |
| Extension row | `LoginProviders.ExtensionProviders` + `ExtensionCatalog.IsActiveAsync` | every provider an extension registers, enabled or not; no edit |
| Credential fields | `OAuthProviderDefinition.Fields` | only the fields the provider needs; secrets (Client secret, Private key) are never rendered back - the placeholder says "Saved - leave empty to keep it" |
| Copy button | `data-copy-target` ([behaviours](../architecture/design-system.md#behaviours)) | copies the callback URL; hidden without JS |
| On/off switch | `ProviderInput.Enabled` | the checkbox posts `true` before a hidden `false` (a bound bool takes the first value) |

## Functionality

### Edit a provider

1. **Trigger:** **Edit** (or the provider's name) → `GET /admin/providers/{key}`; change fields or the switch →
   **Save**.
2. **Validation:** `AuthProviderService.SaveAsync` ([Validation](#validation)); with any error nothing is saved and
   the form re-renders with the posted values (secret fields empty) and the messages.
3. **Effect:** an empty secret field keeps the saved secret; the new settings are written to
   `AuthProvider.SettingsJson` with the client secret and private key protected (Data Protection), `Enabled` and
   `UpdatedDate` are set, and `OAuthProviderRegistry.Invalidate()` makes the next request re-read the providers - the
   sign-in button appears or disappears at once on this instance, within 30 s on others. Audit `provider.updated`
   (entity `AuthProvider`, the provider's name, details `Enabled`/`Disabled` and `Changed: ClientId, ClientSecret, …`
   - field names only). Nothing changed → nothing is saved or audited.
4. **Result:** toast "<Provider> is on: the sign-in page shows "Sign in with <Provider>"." or "<Provider> is saved and
   off." and the list.

```mermaid
sequenceDiagram
    actor A as Administrator
    participant C as AuthProvidersController
    participant S as AuthProviderService
    participant R as OAuthProviderRegistry
    A->>C: POST /admin/providers/google (antiforgery)
    C->>S: SaveAsync(definition, input)
    S->>S: validate, keep empty secrets, protect secrets
    S->>S: AuthProviders row: SettingsJson, Enabled, UpdatedDate
    S->>R: Invalidate()
    S->>S: audit provider.updated (field names only)
    C-->>A: toast + 302 /admin/providers
    Note over R: next request: scheme oauth-google added or removed
```

Turning a provider **off** removes its button and its scheme (its callback answers `404`); accounts made through it
are kept and can sign in with a password once they set one, or with another provider using the same email.

## Data used by the page

`AuthProviders` rows (all of them, `AuthProviderService.AllAsync`, secrets decrypted in memory only to know whether
they exist), `OAuthProviderCatalog.All`, `AppEnvironment.AppUrl`, the registered `LoginProviderContribution`s and
`ExtensionCatalog`.

## State

Server: the `AuthProviders` rows; the registry's in-memory scheme list (re-read at most every 30 s). No client
state.

## Permissions

`[RequirePermission(Settings, Read)]` on the controller (by default Super Admin and Admin; other roles get
[Access denied](access-denied.md) and no sidebar link); `POST` adds `[RequirePermission(Settings, Update)]` and
`[ValidateAntiForgeryToken]`. Without `update` the edit form is disabled and has no **Save** button. Extension
providers are turned on and off on [Plugins](plugins.md) (Super Admin only).

## Validation

| Rule | Message |
| --- | --- |
| Client ID / Services ID at most 500 characters | "Client ID can have at most 500 characters." ("Services ID …" for Apple) |
| Client secret at most 2000 characters | "Client secret can have at most 2000 characters." |
| Team ID, Key ID: 10 letters or digits | "Team ID is the 10-character code from your Apple developer account, e.g. ABCDE12345." (Key ID alike) |
| Private key: a P-256 EC key in PEM (the `.p8` file), at most 4000 characters | "The private key must be the contents of the .p8 file Apple gave you (-----BEGIN PRIVATE KEY----- ...)." |
| Server: absolute `https` URL without query, at most 200 characters (a trailing `/` is removed) | "Server must be an https address, e.g. https://gitlab.example.com." |
| Tenant: `common`, `consumers`, `organizations`, a tenant ID or a domain (letters, digits, `.`, `-`; max 100) | "Tenant must be common, consumers, organizations, a tenant ID or a domain." |
| Turning on needs every field except Tenant/Server | "Fill in every field before turning <Provider> on." |
| The provider's row is missing (database not seeded yet) | "<Provider> is not set up in this database yet; restart the app." |

The browser enforces the `maxlength`s. Credentials are not tested against the provider when saved: a wrong client
secret shows only when someone signs in ("…was cancelled or did not complete").

## Error handling

| Failure | User sees | Recovery |
| --- | --- | --- |
| Invalid input | messages in `<form-errors />`, nothing saved | fix and save |
| Secret saved on another site (imported export) | **Secret unreadable** on the list, the warning alert on the edit screen; the provider has no button | enter the secret (and Apple key) again |
| Wrong credentials or callback not registered at the provider | sign-in fails; the provider shows its own error, or the sign-in screen "Signing in with <Provider> was cancelled or did not complete." (warning in the application log) | check the credentials and the callback URL at the provider; open the site at `APP_URL` ([deployment](../guides/deployment.md#public-address-app_url)) |
| Unknown `{key}` | `404` | - |

## Loading behaviour

Server-rendered; one synchronous POST. No indicator.

## Empty states

**From extensions** without any provider extension: "No provider extensions" - "Authentication extensions in
extensions/ can add more providers."

## User interactions

Links, form fields, the switch, **Copy** (JS) and **Open** (new tab). Works without JavaScript except Copy.

## Dependencies

```text
AuthProvidersController
├── AuthProviderService → DotNetForgeDbContext, IDataProtectionProvider, OAuthProviderRegistry, IAuditService
├── LoginProviders (ExtensionProviders)
├── ExtensionCatalog (IsActiveAsync)
├── AppEnvironment (APP_URL)
└── AdminControllerBase (Can → IPermissionService)
```

## Page flow

```mermaid
flowchart LR
    L["GET /admin/providers"] --> E["GET /admin/providers/{key}"]
    E -- Save --> V{"SaveAsync valid?"}
    V -- yes --> T["toast + 302"] --> L
    V -- no --> F["form + errors"] --> E
    L -- Email: Settings --> AS["/admin/advanced-settings"]
    L -- extension: Plugins --> P["/admin/plugins"]
```

## Related pages

[Sign in](login.md) (shows the buttons), [Advanced Settings](advanced-settings.md) (**Enable sign-ups** decides
whether a provider can create accounts), [Plugins](plugins.md) (extension providers),
[Two-factor authentication](mfa.md) (the second step applies to provider sign-ins too), [Audit Logs](audit-logs.md).

## Important implementation details

- A provider's scheme exists only while it is enabled **and** complete, so a half-configured provider can never be
  challenged.
- The callback URL shown comes from `APP_URL`, but ASP.NET Core sends the `redirect_uri` from the request's own
  address - the two match only when people open the site at `APP_URL`.
- Apple's client secret is not entered: it is signed from the Team ID, Key ID and private key for each sign-in, so it
  never expires.
- The rows are seeded on every start (`DataSeeder`); old rows of providers that are no longer built in are removed
  only while they were never configured.

## Known limitations

- The **Email** provider can't be turned off.
- Extension providers are read-only here.
- Credentials are not verified when saved.
- The switch's hint says accounts "can still sign in with a password" - an account created through the provider has
  none until it sets one on [Profile](profile.md) or through **Forgot password**.

## Planned (not implemented)

Disabling the Email provider, extension providers with the same controls, OIDC/CAS providers:
[authentication → Planned](../features/authentication.md#providers-screen).

## Extension points

A new built-in provider: an `OAuthProviderDefinition` in `OAuthProviderCatalog.All` (endpoints, scopes, fields,
profile reader that returns a **verified** email only) and its key in `AuthProviderKeys.OAuth`; a new field type:
`ProviderField`, `ProviderSettings`, `ProviderInput`, `AuthProviderService.SaveAsync` and `Edit.cshtml`. A provider
that should not be built in: an `authentication` extension ([extension development](../guides/extension-development.md)).
