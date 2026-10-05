# Security

The protections that exist in the code, where they are, and the known gaps. Target requirements:
[Planned](#planned-not-implemented).

## Implemented protections

| Concern | Protection | Where |
| --- | --- | --- |
| Password storage | PBKDF2-SHA256, 120 000 iterations, random salt, constant-time verify | `Pbkdf2PasswordHasher` |
| Weak passwords | `PasswordPolicy` (≥ 8, letter + digit, common-password list) - **setup only** | `src/DotNetForge.Core/Validation/PasswordPolicy.cs` |
| Brute force | per-account lockout: 5 failures → 15 min; plus per-IP rate limiting (below) | `AuthService` |
| Rate limiting | built-in ASP.NET Core rate limiter, fixed window per client IP (`RemoteIpAddress`), `429` on rejection: policy `credentials` = 10/min on `POST /account/login` and `POST /setup`; policy `api` = 300/min on every API controller | `DependencyRegistration.AddRateLimits`, `RateLimitPolicies`, `[EnableRateLimiting]` on `AccountController`, `SetupController`, `ApiControllerBase` |
| Account enumeration | unknown email and wrong password return the same message | `AuthService`, `AccountController` |
| Session cookie | `HttpOnly`, `SameSite=Lax`, 8 h sliding; `Secure` **always** outside Development (`SameAsRequest` in Development) | `DependencyRegistration` |
| Session re-validation | every cookie request checks that the user still exists and is `Enabled` (`AuthService.IsActiveAsync`, cached 30 s per user); otherwise the session is rejected and signed out. Roles stay as issued at sign-in | `DependencyRegistration.ValidateSessionAsync` ([authentication](authentication.md#session-re-validation)) |
| CSRF | antiforgery token on every admin/account/setup POST (`[ValidateAntiForgeryToken]`); `X-CSRF-TOKEN` header for the reorder `fetch` | controllers, `admin-content.js` |
| Open redirect | after login: `LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/admin")` - an external `returnUrl` falls back to the dashboard | `AccountController.Login` |
| Security headers | on every response: `Content-Security-Policy` (`Content-Security-Policy-Report-Only` in Development), `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=()` | `Middleware/SecurityHeadersMiddleware.cs` |
| Content Security Policy | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: https:; media-src 'self' https:; font-src 'self'; connect-src 'self'; frame-src 'self'; frame-ancestors 'self'; form-action 'self'; base-uri 'self'; object-src 'none'`. Views carry no inline script or style (delete confirms use `data-confirm` handled by `site.js`; the denied screen's sign-out is a real POST form; public page styles are in `wwwroot/css/page.css`) | `SecurityHeadersMiddleware.ContentSecurityPolicy` |
| Admin access | `AdminArea` policy + role restrictions; `(area, action)` checks in the Content Manager and Media | [authorization](authorization.md) |
| API tokens | 256-bit secret, only a salted hash stored, prefix lookup, expiry, revocation, per-endpoint permission keys; verification cached 10 min under a SHA-256 of the token, revocation/expiry re-read every request | [headless API](headless-api.md), [authentication](authentication.md#api-token-authentication) |
| Tenant isolation | manual `TenantId` filters in admin and API queries; Audit Logs and the Dashboard audit count are scoped; private media checks the file's tenant | [multi-tenancy](multi-tenancy.md) (gaps listed there) |
| Input length | lengths checked before saving (PostgreSQL enforces column lengths): pages (`PageService`), settings key 200 / value 4000, API token name 200 / description 1000 + duplicate-name message, setup email 256 / names 100; `AuditService` truncates snapshots | `PageService.FindTooLong`, `SettingsController`, `ApiTokensController`, `InstallationService`, `AuditService` |
| Content structure | reorder validates tenant ownership, cycles, duplicate slugs and a second dynamic segment per parent → `400 { error }`; delete refuses when re-parented children would clash | `PageService.ReorderAsync` / `DeleteAsync` |
| File uploads | allow-list of extensions (no SVG/HTML/JS), content type derived from the extension (never the client), 25 MB per file, 10 files per request, generated storage keys (no user input), sanitized display names; non-media served as `attachment`; private files only to admin-capable users of the file's tenant, else 404 | `MediaService`, `MediaFilesController` - details in [media storage](media-storage.md#upload-flow) |
| SQL injection | EF Core LINQ only, no raw SQL | everywhere |
| XSS | Razor HTML-encodes all `@` output; no `Html.Raw` in the host's views; CSP blocks inline script | views |
| Path traversal | extension resources confined to `Views/Resources/`; extensions found by manifest id, not by path; `StorageKey` validation + `LocalFileStorage` containment check | `ExtensionViewController.Resource`, `StorageKey`, `LocalFileStorage` |
| Secrets | `.env` git-ignored and excluded from the build | `.gitignore`, csproj |
| Data Protection keys | the key ring (cookies, antiforgery) is stored in the database table `DataProtectionKeys`, shared by all instances and kept across restarts on a read-only filesystem | `DependencyRegistration` (`PersistKeysToDbContext<DotNetForgeDbContext>`) |
| Transport | `UseHsts()` outside Development; HTTPS redirection is left to the TLS-terminating proxy ([deployment](../guides/deployment.md#behind-a-reverse-proxy)) | `Program.cs` |
| Proxy awareness | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (built into ASP.NET Core) makes scheme and client IP - used by rate limits and audit - come from `X-Forwarded-*` | [deployment](../guides/deployment.md#behind-a-reverse-proxy) |
| Error handling | `HomeController.Error` is `[Route("/error")]` for every method, so exceptions in POSTs render the error page | `HomeController` |
| Audit trail | append-only `AuditLogs` | [audit logging](audit-logging.md) |
| Dependencies | NU1903 (`SQLitePCLRaw` 2.1.11) fixed: EF Core / ASP.NET packages 10.0.12 (`SQLitePCLRaw` 2.1.12), Npgsql EF 10.0.3 | `Directory.Packages.props` ([dependencies](../architecture/dependencies.md#nuget-packages)) |
| Webhook signing | HMAC-SHA256 hex (`HmacWebhookSigner`) - exists but no deliveries are made | `Infrastructure/Security` |

Pipeline order (`Program.cs`): exception handler + HSTS (outside Development) → `SecurityHeadersMiddleware` → static
files → routing → `InstallationMiddleware` → authentication → rate limiter → authorization.

## Known gaps

Verified in the current code; each is a candidate for a hardening change.

- **`(area, action)` permissions are enforced only in the Content Manager and Media.** Every other admin screen uses
  role checks; the matrix is the static `PermissionMatrix`, not the stored `RolePermission` rows. See
  [authorization](authorization.md).
- **Roles are fixed at sign-in:** session re-validation only checks that the user exists and is enabled; role changes
  apply at the next sign-in.
- **Admin extensions run with full host privileges** (DbContext via `@inject`), manifest permissions are not
  enforced, and any admin-capable role can open them (the sample Audit Dashboard shows every tenant's audit entries to
  Authors). See [extensions](extensions.md#admin-extensions).
- **Cross-tenant reads:** the public site (`HomeController.Index` and `RenderPage`) ignores `TenantId` (single tenant
  only today).
- **CSP:** no nonce support for future inline scripts; `img-src` and `media-src` allow any `https:` origin (needed for
  presigned storage URLs).
- **Data Protection keys are unencrypted at rest** in the database (ASP.NET Core logs "No XML encryptor configured"):
  database access = key access.
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
- No inline `<script>`/`<style>` or event-handler attributes in views - the CSP blocks them; put code in `wwwroot/js`.
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
| Secrets | All secrets - connection string ✔, storage credentials ✔, SMTP credentials, provider client secrets, signing keys - from `.env` only | [configuration](configuration.md#secrets) |
| Extensions | Validate the manifest before **installing**; refuse invalid, unsafe or incompatible extensions at install and load; reconcile requested permissions with the role model | [extensions](extensions.md#planned-not-implemented) |
| Audit trail | Record login ✔, failed login ✔, role changes, permission changes, settings changes ✔, API token creation ✔, webhook creation, among others | [audit logging](audit-logging.md#planned-not-implemented) |
| Webhook signing | Every delivery is a signed POST (✔ `HmacWebhookSigner` exists), retried on failure and logged; the signing key is a secret | [webhooks](webhooks.md#planned-not-implemented) |

### Acceptance criteria

- [x] User passwords are stored with a strong, salted hashing algorithm; plaintext is never stored or logged (`Pbkdf2PasswordHasher`).
- [ ] Accounts lock after a configurable number of failed logins and the user is notified via the *Account locked* email template (lockout ✔ `AuthService`, fixed constants, no email).
- [x] Rate limiting is enforced on the login endpoint and on all API endpoints (`[EnableRateLimiting(RateLimitPolicies.Credentials)]` on `AccountController.Login`, `[EnableRateLimiting(RateLimitPolicies.Api)]` on `ApiControllerBase`; per instance).
- [x] Session/auth cookies are `HttpOnly` and `SameSite` and marked `Secure` in production (`DependencyRegistration`: `CookieSecurePolicy.Always` outside Development).
- [ ] HTTPS is supported and enforced for authenticated traffic in production (`Secure` cookie ✔ and HSTS ✔; no HTTP → HTTPS redirect in the app - the proxy's job).
- [x] RBAC is implemented with the default roles Super Admin, Admin, Editor, Author, Authenticated and Public (`Roles`, `DataSeeder`, `AdminArea` policy, `IPermissionService`).
- [ ] Every admin action and every API action performs a permission check before executing (API ✔ `RequireApiPermission`; admin: `(area, action)` only in `ContentController` and `MediaController`, role checks elsewhere).
- [x] All database access uses EF Core / parameterized queries; no SQL is built from concatenated input (no `FromSql`/`ExecuteSql` in the code).
- [ ] Input is validated on all admin forms and all API endpoints (lengths ✔, reorder ✔ `PageService.ReorderAsync`, uploads ✔ `MediaService`; login has no server-side format check).
- [ ] Output is encoded/sanitized against XSS across page content, history diffs and extension/widget output (Razor encoding ✔ and CSP ✔; no sanitizing, no history, extensions unrestricted).
- [x] CSRF protection is enforced on all state-changing admin form submissions (every `[HttpPost]` in `Controllers/` and `Areas/Admin/` has `[ValidateAntiForgeryToken]`).
- [ ] File uploads pass secure validation; allowed file types and max upload size are configurable (validation ✔ `MediaService.AllowedTypes` / `MaxUploadBytes`; constants, not configurable).
- [x] Private media is access-controlled and never served to unauthorized callers (`MediaFilesController.CanReadPrivate`: admin-capable user of the file's tenant, else 404; presigned URLs for private files expire after 5 min).
- [x] API tokens are generated with a cryptographically secure method and stored only as hashes; plaintext is shown once at creation (`ApiTokenFactory` uses `RandomNumberGenerator`; `ApiTokensController`).
- [x] All secrets are loaded from `.env`; `.env` is git-ignored and never committed; `.env.example` ships placeholders only (`EnvConfigurationLoader`, `.gitignore`; connection string and S3 credentials today).
- [ ] Extension manifests are validated against `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions` before installation (`ManifestValidator` checks all eight ✔; there is no install flow).
- [ ] Invalid, unsafe or incompatible extensions are refused at install and load time (invalid admin extensions are not rendered ✔ `ExtensionsController`; no install flow, no compatibility check).
- [x] Secure HTTP response headers are sent on all responses (`SecurityHeadersMiddleware`; CSP is report-only in Development).
- [x] Security-relevant events, including login and failed login, are written to the audit log with full context (`AccountController` → `AuditService`: user or typed email, IP, user agent, timestamp, success flag).
- [ ] Outbound webhook requests are signed (signer ✔ `HmacWebhookSigner`; no deliveries exist).
