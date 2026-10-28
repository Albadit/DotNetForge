# Security

The protections that exist in the code, where they are, and the known gaps. Target requirements:
[Planned](#planned-not-implemented).

## Implemented protections

| Concern | Protection | Where |
| --- | --- | --- |
| Password storage | PBKDF2-SHA256, 120 000 iterations, random salt, constant-time verify | `Pbkdf2PasswordHasher` |
| Weak passwords | `PasswordPolicy` (≥ 8, letter + digit, common-password list) - **setup only** | `src/DotNetForge.Core/Validation/PasswordPolicy.cs` |
| Brute force | per-account lockout: 5 failures → 15 min (wrong two-factor codes and passkeys count too); plus per-IP rate limiting (below) | `AuthService` |
| Rate limiting | built-in ASP.NET Core rate limiter, fixed window per client IP (`RemoteIpAddress`), `429` on rejection: policy `credentials` = 10/min on `POST /login`, the self-service account POSTs (`/register`, `/forgot-password`, `/reset-password`, `/confirm-email/resend`), `POST /setup`, the external sign-in endpoints and the POSTs of the two-factor step (`/login/mfa/*`); policy `api` = 300/min on every API controller (custom endpoints included); policy `forms` = 5/min for anonymous extension form posts that opt in (Contact Form sample) | `DependencyRegistration.AddRateLimits`, `RateLimitPolicies`, `[EnableRateLimiting]` on `AccountController`, `SetupController`, `ExternalLoginController`, `MfaLoginController`, `ApiControllerBase`, `CustomEndpointsApiController`; `.RequireRateLimiting(RateLimitPolicies.Forms)` in extensions |
| Account enumeration | unknown email and wrong password return the same message; **Forgot password** and **resend confirmation** answer the same whether or not an account exists (registration does say an email is taken - by design, it is the one-account-per-email rule) | `AuthService`, `AccountController`, `AccountService` |
| Session cookie | `HttpOnly`, `SameSite=Lax`, 8 h sliding; `Secure` **always** outside Development (`SameAsRequest` in Development) | `DependencyRegistration` |
| External sign-in cookie | `dnf.external`: `HttpOnly`, `SameSite=Lax`, 10 min, same `Secure` rule; read and deleted on the callback; only a **verified** email from the provider is accepted (the built-in providers' profile readers drop unverified ones); disabled/locked accounts refused; new accounts only while **Enable sign-ups** is on | `DependencyRegistration`, `ExternalLoginController`, `OAuthProviderCatalog` ([authentication](authentication.md#external-sign-in)) |
| Built-in OAuth providers | the state parameter and correlation cookie of ASP.NET Core's `OAuthHandler` (`SameSite=Lax`, Apple's posted callback `None` + `Secure`, `Secure` outside Development); PKCE for Google, Microsoft, X and GitLab; a scheme only for enabled, complete providers; client secrets and Apple's key stored protected with Data Protection (`DotNetForge.AuthProviders.Secrets.v1`), never shown again, audited by field name only; Apple's client secret signed per sign-in (5 min); id tokens read without signature check because they come straight from the token endpoint over TLS | `src/DotNetForge.Web/Authentication/` ([authentication](authentication.md#built-in-oauth-providers)) |
| Two-factor authentication | authenticator app (TOTP, a code works once, ±1 step), passkeys (WebAuthn, bound to `APP_URL`), email and SMS codes (10 min, only a hash inside a Data Protection token); 10 recovery codes stored as SHA-256 hashes; wrong codes count toward the 5-attempt lockout; the pending sign-in is a separate 10-minute cookie `dnf.mfa` - no session before the second step; optional requirement for every admin user; TOTP secrets protected (`DotNetForge.Mfa.Secrets.v1`) | `src/DotNetForge.Web/Services/Mfa/`, `MfaLoginController` ([MFA](mfa.md)) |
| Redirects after a form post | the CSP's `form-action 'self'` also applies to redirects that follow a form submission, so the "Sign in with ..." post is answered with a `200` page whose `<meta http-equiv="refresh">` (and a link) continues to the provider - a new navigation - instead of a `302` off-site; `referrer` `no-referrer` on that page | `ExternalLoginController.Challenge`, `Views/ExternalLogin/Continue.cshtml` ([authentication](authentication.md#external-sign-in)) |
| Session re-validation | every cookie request checks that the user still exists and is `Enabled` (`AuthService.IsActiveAsync`, cached 30 s per user); otherwise the session is rejected and signed out. Roles stay as issued at sign-in | `DependencyRegistration.ValidateSessionAsync` ([authentication](authentication.md#session-re-validation)) |
| CSRF | antiforgery token on every admin/account/setup POST (`[ValidateAntiForgeryToken]`) except `POST /reset-password`, where the single-use, time-limited reset token is the proof (so a front end's own reset page can post there); `X-CSRF-TOKEN` header on the `fetch` POSTs (page reorder, entry reorder, the page builder) | controllers, `wwwroot/js/forge/pages.js`, `entries.js`, `js/builder` |
| Open redirect | after login (password or external): `LocalRedirect` to `returnUrl` only when `Url.IsLocalUrl`, else `/admin` (a password sign-in of a site-only account: `/`); `/confirm-email` redirects off-site only to the URL an administrator configured in the Advanced User Settings | `AccountController.Login`, `ExternalLoginController` |
| Security headers | on every response: `Content-Security-Policy` (`Content-Security-Policy-Report-Only` in Development), `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=()` | `src/DotNetForge.Web/Middleware/SecurityHeadersMiddleware.cs` |
| Code editor (Monaco) | the email template editor allows inline styles (`style-src 'self' 'unsafe-inline'`, `SecurityHeadersMiddleware.CodeEditorPolicy`): Monaco injects its styles at runtime and supports no nonce. Scripts stay `'self'` - Monaco is self-hosted (`/lib/monaco`), its workers load from the same origin and its loader falls back from `eval` to plain script loading | `EmailTemplatesController.Edit`, `SecurityHeadersMiddleware` |
| Email template preview | administrator-written HTML is never rendered inside the admin page: a separate document in an `<iframe sandbox>`, served with an enforced `default-src 'none'; style-src 'unsafe-inline'; img-src https: data:; form-action 'none'; sandbox` policy (also in Development) | `EmailTemplatesController`, `SecurityHeadersMiddleware.EmailPreviewPolicy` |
| Content Security Policy | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; media-src 'self' https:; font-src 'self'; connect-src 'self'; frame-src 'self' https://www.youtube-nocookie.com https://player.vimeo.com; frame-ancestors 'self'; form-action 'self'; base-uri 'self'; object-src 'none'`. Views carry no inline script or style: styles in `wwwroot/css/forge/`, behaviour in `wwwroot/js/forge/` via data attributes (`data-confirm` opens the confirm dialog in `ui.js`), validation errors via `<form-errors />` instead of `asp-validation-summary` (whose empty state emits `<li style="display:none">`), the denied screen's sign-out is a real POST form, no web fonts or CDNs ([design system](../architecture/design-system.md)). `AdminUiTests.Every_screen_renders_without_inline_script_or_style` verifies every admin screen, sign-in, `/` and a content page. The two video hosts are allowed in `frame-src` for the page builder's video component. Page builder layouts keep the policy: their CSS is the external `/page-css/{id}.css`, the editor writes canvas CSS into constructable stylesheets ([page builder](page-builder.md#validation-and-security)). **One exception:** the Scalar API reference page (`/admin/api-docs/reference/`, Super Admins and Developers) gets `script-src 'self' 'nonce-<per-request>'` and `style-src 'self' 'unsafe-inline'` - its one inline script carries the nonce, and it injects its styles at runtime; the middleware switches to that policy in `Response.OnStarting` only when Scalar has stored a nonce in `HttpContext.Items` ([API docs](api-docs.md#content-security-policy)). Every other response keeps the strict policy (`ApiDocsTests`) | `SecurityHeadersMiddleware.ContentSecurityPolicy` / `ApiReferencePolicy`, `AdminUiTests` |
| Admin access | `AdminArea` policy + role restrictions (Plugins: Super Admin; API Endpoints and the API docs: Super Admin or Developer); `(area, action)` checks in Pages, the Content Manager, Content types and Media | [authorization](authorization.md) |
| API session for Super Admins | the API also accepts a signed-in Super Admin's admin cookie, but only without a token and only for `Sec-Fetch-Site: same-origin` requests (set by the browser, so no other site can ride the cookie); a sent token always wins (`SecurityTests.A_signed_in_super_admin_calls_the_api_from_the_admin_without_a_token`) | `AdminSessionAuthenticationHandler`, `ApiTokenDefaults.RequestScheme` ([authentication](authentication.md#api-token-authentication)) |
| API tokens | 256-bit secret, only a salted hash stored, prefix lookup, expiry, revocation, per-endpoint permission keys; verification cached 10 min under a SHA-256 of the token, revocation/expiry re-read every request | [headless API](headless-api.md), [authentication](authentication.md#api-token-authentication) |
| Custom endpoint tokens | one token per endpoint, opening all its operations (`dnfe_` + 256-bit secret), stored **encrypted** with Data Protection (not hashed, so the editor and the API docs can show it again), compared in constant time; API tokens don't open custom endpoints; regenerating one is audited | `CustomEndpointService` ([custom endpoints](custom-endpoints.md#the-endpoints-token)) |
| Input length | lengths checked before saving (PostgreSQL enforces column lengths): pages (`PageService`), settings key 200 / value 4000, API token name 200 / description 1000 + duplicate-name message, setup email 256 / names 100; `AuditService` truncates snapshots | `PageService.FindTooLong`, `SettingsController`, `ApiTokensController`, `InstallationService`, `AuditService` |
| Content structure | reorder validates that moved pages and target parents exist, cycles, duplicate slugs and a second dynamic segment per parent → `400 { error }`; delete refuses when re-parented children would clash | `PageService.ReorderAsync` / `DeleteAsync` |
| File uploads | allow-list of extensions (no SVG/HTML/JS), content type derived from the extension (never the client), a size limit per kind of file ([upload limits](media-storage.md#upload-limits), request size sized to match), 10 files per request, generated storage keys (no user input), sanitized display names; non-media served as `attachment`; private files only to signed-in admin-capable users, else 404 | `MediaService`, `MediaFilesController` - details in [media storage](media-storage.md#upload-flow) |
| Media processing | uploaded images are decoded (SkiaSharp) only up to 64 MB and 100 megapixels, metadata (EXIF, GPS) is not carried over; ffmpeg (optional, `FFMPEG_PATH`) runs with an argument list - no shell, no user input in it - one video at a time, stopped after 2 hours; a failure keeps the upload as it came | `ImageOptimizer`, `MediaService`, `Ffmpeg` - [media storage → optimization](media-storage.md#optimization) |
| SQL injection | EF Core LINQ only, no raw SQL | everywhere |
| XSS | Razor HTML-encodes all `@` output; no `Html.Raw` in the host's views; the UI tag helpers HTML-encode the text they emit; CSP blocks inline script | views, `src/DotNetForge.Web/Ui/UiTagHelpers.cs` |
| Path traversal | extension resources confined to `Views/Resources/`; extensions found by manifest id, not by path; manifest `assembly` must be a relative `.dll` path inside the extension folder (no `..`, no rooted path); `StorageKey` validation + `LocalFileStorage` containment check | `ExtensionResources.Serve` (`ExtensionViewController.cs`), `ManifestValidator`, `StorageKey`, `LocalFileStorage` |
| Extensions | nothing an extension offers is used until a Super Admin enables it (audited); an extension's endpoints answer `404` while it is inactive (`ExtensionEndpointGateMiddleware`); failing extension code is isolated to "not running" and never stops startup; a throwing widget/module view is replaced by a notice. Extension code itself is **trusted** - it runs in-process with full privileges ([extensions → Security model](extensions.md#security-model)) | `ExtensionCatalog`, `ExtensionLifecycleService`, `Startup/ExtensionHosting.cs`, `ExtensionViewRenderer` |
| Secrets | `.env` git-ignored and excluded from the build | `.gitignore`, csproj |
| Data Protection keys | the key ring (cookies, antiforgery, custom endpoint tokens, sign-in provider secrets, authenticator-app secrets) is stored in the database table `DataProtectionKeys`, shared by all instances and kept across restarts on a read-only filesystem | `DependencyRegistration` (`PersistKeysToDbContext<DotNetForgeDbContext>`) |
| Transport | `UseHsts()` outside Development; HTTPS redirection is left to the TLS-terminating proxy ([deployment](../guides/deployment.md#behind-a-reverse-proxy)) | `Program.cs` |
| Proxy awareness | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (built into ASP.NET Core) makes scheme and client IP - used by rate limits and audit - come from `X-Forwarded-*` | [deployment](../guides/deployment.md#behind-a-reverse-proxy) |
| Error handling | `HomeController.Error` is `[Route("/error")]` for every method, so exceptions in POSTs render the error page | `HomeController` |
| Audit trail | append-only `AuditLogs` | [audit logging](audit-logging.md) |
| Dependencies | NU1903 (`SQLitePCLRaw` 2.1.11) fixed: EF Core / ASP.NET packages 10.0.12 (`SQLitePCLRaw` 2.1.12), Npgsql EF 10.0.3 | `Directory.Packages.props` ([dependencies](../architecture/dependencies.md#nuget-packages)) |
| Webhook signing | HMAC-SHA256 hex (`HmacWebhookSigner`) - exists but no deliveries are made | `Infrastructure/Security` |

Pipeline order (`Program.cs`): exception handler + HSTS (outside Development) → `SecurityHeadersMiddleware` → static
files → routing → `InstallationMiddleware` → `ExtensionEndpointGateMiddleware` → authentication → rate limiter →
authorization.

## Known gaps

Verified in the current code; each is a candidate for a hardening change.

- **Custom API endpoints run any SQL a Super Admin or Developer writes** with the application's database rights -
  including reading password and token hashes - and a `Public` one exposes its result to anyone. Request values are always
  bound as parameters, results are bounded by a row limit and timeout, and public callers never see database errors
  ([custom endpoints](custom-endpoints.md#security)).

- **`(area, action)` permissions are enforced only in the Content Manager and Media.** Every other admin screen uses
  role checks; the matrix is the static `PermissionMatrix`, not the stored `RolePermission` rows. See
  [authorization](authorization.md).
- **Roles are fixed at sign-in:** session re-validation only checks that the user exists and is enabled; role changes
  apply at the next sign-in.
- **Extensions run with full host privileges** (code in-process, DbContext via `@inject`), manifest permissions are
  not enforced, enabled admin tabs and widgets are open to every admin-capable role (Authors too), and an extension's
  own endpoints are public unless it protects them. Install only trusted extensions. See
  [extensions → Security model](extensions.md#security-model).
- **Sign-in providers are trusted with identity:** the host signs in (or, with sign-ups on, creates) whoever the
  provider's email claim names. The built-in providers pass on only emails the provider reports verified; an
  extension that passes on unverified emails allows account takeover. A second factor on the account still stops a
  takeover through a provider.
- **The two-factor requirement is enforced on `AdminControllerBase` only:** a raw admin extension view
  (`/admin/ext/{id}/raw`) and the API through a Super Admin's admin session are not redirected for a user who has no
  method yet; removing a method or making new recovery codes needs only the open session (no password prompt)
  ([MFA](mfa.md#known-limitations)).
- **CSP:** `img-src` and `media-src` allow any `https:` origin (needed for presigned storage URLs); the Scalar API
  reference page allows inline styles (`style-src 'unsafe-inline'`). Nonces exist only for Scalar's script, not for
  the app's own views.
- **Data Protection keys are unencrypted at rest** in the database (ASP.NET Core logs "No XML encryptor configured"):
  database access = key access, and so also access to every custom endpoint's token.
- **Concurrent setup on PostgreSQL** can create two Super Admins (no concurrency token on `SystemState`) - see
  [installation](installation.md#setup-sequence).
- **Uploads:** type by extension only - no antivirus or content sniffing (mitigated by `nosniff` and `attachment` for
  non-media).
- **Rate limits are per instance** (in-memory): N instances allow N × the limit. Behind a proxy without forwarded
  headers every client shares the proxy's IP, so one bucket.
- **No HTTPS redirection** in the app (`UseHttpsRedirection` is not called); a plain-HTTP request is not redirected
  unless the proxy does it.
- **Forwarded headers are off by default:** without `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` the audit IP and the
  rate-limit partition are the proxy's address.

## Rules for changes

- Never log or display passwords, token plaintext (except the one-time Created view), hashes or connection strings.
- Every new POST: `[ValidateAntiForgeryToken]`. Every new API action: `[RequireApiPermission]`. Every new admin
  controller: derive from `AdminControllerBase`; check `Can`/`CanModify` for content-like data.
- Never build file paths from request input without the `GetFullPath` + prefix check; storage keys go through
  `StorageKey`.
- Never return entities from the API (they contain `PasswordHash`, `TokenHash`, `Secret`).
- No inline `<script>`/`<style>`, `style=` or event-handler attributes in views - the CSP blocks them; put code in
  `src/DotNetForge.Web/wwwroot/js/forge/`, styles in `wwwroot/css/forge/`, and use `<form-errors />`, never
  `asp-validation-summary`.
- Check input lengths against the column limits before saving (PostgreSQL rejects over-long values).
- New credential-like endpoints get `[EnableRateLimiting(RateLimitPolicies.Credentials)]`.

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.
The baseline holds in all three CMS modes - Traditional, Headless and Hybrid ([product](../product.md)); the owning
documents hold the details.

### Requirements

| Concern | Target requirement (beyond [Implemented protections](#implemented-protections)) | Owner |
| --- | --- | --- |
| Account lockout | Threshold configurable (today fixed constants in `AuthService`); notify the user with the *Account locked* email template | [authentication](authentication.md#planned-not-implemented), [email](email.md#planned-not-implemented) |
| Rate limiting | On the login endpoint and on **all** API endpoints ✔ (`credentials` / `api` policies) | [authentication](authentication.md#rate-limiting), [headless API](headless-api.md) |
| Cookies | `Secure` always in production ✔ (`CookieSecurePolicy.Always` outside Development); same hardening for every session-bearing cookie, including those set by authentication providers | [authentication](authentication.md#cookie-options) |
| HTTPS | Enforced for all authenticated traffic in production (✔ `Secure` cookie and HSTS; redirection left to the proxy) | hosting / `Program.cs`, [deployment](../guides/deployment.md#behind-a-reverse-proxy) |
| Secure headers | Secure HTTP response headers on every response ✔ | `SecurityHeadersMiddleware` |
| Access control | A permission check on **every** admin and API action, covering all ten permission areas (✔ API keys; ✔ `Collection types` and `Media` in the admin) | [authorization](authorization.md#planned-not-implemented) |
| Input validation | All forms and all APIs reject malformed, out-of-range or unexpected input before business logic or storage; the shared base for upload, route-pattern and manifest validation (✔ lengths, reorder, uploads) | every controller |
| XSS | Encode output **and** sanitize user-supplied content in Razor views and API responses, including content history diffs and extension/widget output | views, API serialization |
| File uploads | Secure validation of every upload ✔; configurable allowed file types and max upload size (today constants in `MediaService`); block unsafe uploads where possible ✔ (allow-list); private media never served to unauthorized callers, including through generated URLs ✔ (presigned URLs for private files live 5 min) | [media storage](media-storage.md) |
| Secrets | All secrets - connection string ✔, storage credentials ✔, SMTP credentials ✔, Twilio credentials ✔, provider client secrets, signing keys - from `.env` only (✘ by decision for the built-in sign-in providers: their client secrets and Apple key are entered on [Providers](../pages/providers.md) and stored in the database, protected with Data Protection, so they can change without a restart) | [configuration](configuration.md#secrets) |
| Extensions | Validate the manifest before **installing**; refuse invalid, unsafe or incompatible extensions at install and load; reconcile requested permissions with the role model | [extensions](extensions.md#planned-not-implemented) |
| Audit trail | Record login ✔, failed login ✔, role changes, permission changes, settings changes ✔, API token creation ✔, webhook creation, among others | [audit logging](audit-logging.md#planned-not-implemented) |
| Webhook signing | Every delivery is a signed POST (✔ `HmacWebhookSigner` exists), retried on failure and logged; the signing key is a secret | [webhooks](webhooks.md#planned-not-implemented) |

### Acceptance criteria

- [x] User passwords are stored with a strong, salted hashing algorithm; plaintext is never stored or logged (`Pbkdf2PasswordHasher`).
- [ ] Accounts lock after a configurable number of failed logins and the user is notified via the *Account locked* email template (lockout ✔ `AuthService`, fixed constants, no email).
- [x] Rate limiting is enforced on the login endpoint and on all API endpoints (`[EnableRateLimiting(RateLimitPolicies.Credentials)]` on `AccountController.Login`, `[EnableRateLimiting(RateLimitPolicies.Api)]` on `ApiControllerBase`; per instance).
- [x] Session/auth cookies are `HttpOnly` and `SameSite` and marked `Secure` in production (`DependencyRegistration`: `CookieSecurePolicy.Always` outside Development).
- [ ] HTTPS is supported and enforced for authenticated traffic in production (`Secure` cookie ✔ and HSTS ✔; no HTTP → HTTPS redirect in the app - the proxy's job).
- [x] RBAC is implemented with the default roles Super Admin, Admin, Editor, Author, Authenticated and Public (plus Developer) (`Roles`, `DataSeeder`, `AdminArea` policy, `IPermissionService`).
- [ ] Every admin action and every API action performs a permission check before executing (API ✔ `RequireApiPermission`, custom endpoints check their own token; admin: `(area, action)` only in `ContentController` and `MediaController`, role checks elsewhere).
- [x] All database access uses EF Core / parameterized queries; no SQL is built from concatenated input (no `FromSql`/`ExecuteSql` in the code).
- [ ] Input is validated on all admin forms and all API endpoints (lengths ✔, reorder ✔ `PageService.ReorderAsync`, uploads ✔ `MediaService`; login has no server-side format check).
- [ ] Output is encoded/sanitized against XSS across page content, history diffs and extension/widget output (Razor encoding ✔ and CSP ✔; no sanitizing, no history, extensions unrestricted).
- [x] CSRF protection is enforced on all state-changing admin form submissions (every `[HttpPost]` in `src/DotNetForge.Web/Controllers/` and `src/DotNetForge.Web/Areas/Admin/` has `[ValidateAntiForgeryToken]`).
- [ ] File uploads pass secure validation; allowed file types and max upload size are configurable (validation ✔ `MediaService.AllowedTypes` and the per-kind limits; max upload size ✔ configurable on [Media Library settings](../pages/media-settings.md); allowed types are a constant).
- [x] Private media is access-controlled and never served to unauthorized callers (`MediaFilesController.CanReadPrivate`: signed-in admin-capable user, else 404; presigned URLs for private files expire after 5 min).
- [x] API tokens are generated with a cryptographically secure method and stored only as hashes; plaintext is shown once at creation (`ApiTokenFactory` uses `RandomNumberGenerator`; `ApiTokensController`).
- [x] All secrets are loaded from `.env`; `.env` is git-ignored and never committed; `.env.example` ships placeholders only (`EnvConfigurationLoader`, `.gitignore`; connection string, S3, SMTP and Twilio credentials; by decision the built-in sign-in providers' secrets are stored in the database, protected with Data Protection).
- [ ] Extension manifests are validated against `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions` before installation (`ManifestValidator` checks all eight ✔; there is no install flow).
- [ ] Invalid, unsafe or incompatible extensions are refused at install and load time (invalid or failing extensions are never used ✔ `ExtensionCatalog`; no install flow, no safety or compatibility check).
- [x] Secure HTTP response headers are sent on all responses (`SecurityHeadersMiddleware`; CSP is report-only in Development).
- [x] Security-relevant events, including login and failed login, are written to the audit log with full context (`AccountController` → `AuditService`: user or typed email, IP, user agent, timestamp, success flag).
- [ ] Outbound webhook requests are signed (signer ✔ `HmacWebhookSigner`; no deliveries exist).
