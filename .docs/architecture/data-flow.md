# Data flow

How a process starts and how a request travels through the code. Each diagram uses real class and route names.
Structure and folders are in [codebase.md](codebase.md); the UI side of a request is in [pages.md](pages.md).

## Application startup

`Program.cs` runs these steps in order. Anything that throws before `app.Run()` stops the process.

```mermaid
sequenceDiagram
    participant P as Program.cs
    participant L as EnvConfigurationLoader
    participant DI as DependencyRegistration
    participant DBI as DatabaseInitializer
    participant S as DataSeeder
    participant H as Host
    P->>L: Load(ContentRootPath, IsDevelopment())
    alt ConfigurationException
        L-->>P: throw
        P->>P: write "[DotNetForge] Configuration error: ..." to stderr, return 1
    end
    L-->>P: AppEnvironment
    P->>DI: services.AddDotNetForge(env, builder.Environment)
    Note over DI: AddExtensionHost: discover extensions/, ExtensionRuntime.Load<br/>(code of every valid manifest with an assembly),<br/>each extension's ConfigureServices
    P->>P: builder.Build()
    P->>DBI: InitializeAsync(db, ApplicationStopping)
    DBI->>DBI: Database.MigrateAsync()
    Note over DBI: PostgreSQL: the migrations of PostgreSqlDbContext
    DBI->>S: SeedAsync(db)
    P->>H: build pipeline, map routes + MapDotNetForgeExtensionsAsync (each extension's MapEndpoints), app.Run()
    H->>H: start ScheduledPublishingService (waits 5 s), AuditEventDispatcher
```

Notes:

- Configuration is loaded **before** the host's own configuration system is used; `.env` values never enter
  `IConfiguration`. `DATABASE_CONNECTION_STRING` (PostgreSQL) is always required; outside Development the S3 settings
  are too (Development defaults media to `storage/media` at the repository root). A `DATABASE_PROVIDER` value stops
  startup (no longer used). See [configuration](../features/configuration.md).
- The scoped `DotNetForgeDbContext` resolves to `PostgreSqlDbContext`, so the schema step applies its migration set
  ([database → provider](database.md#provider), [database layer](../database/architecture.md#startup)).
- Migration and seeding run on **every** start, synchronously, before Kestrel accepts requests. Seeding is
  idempotent (each step checks for existing rows). Details: [database.md](database.md#seeding).
- The Data Protection key ring is read from the `DataProtectionKeys` table on first use; the first key is created
  (and stored) on the first request that needs one.
- A database failure here (bad PostgreSQL connection string, PostgreSQL not running, an old PostgreSQL database
  without migration history) throws an unhandled exception and the process exits.
- Extension code is loaded **before** the container is built, enabled or not, because extensions register services.
  A failing extension (missing DLL, bad entry type, throwing `ConfigureServices`/`MapEndpoints`) is marked "not
  running" and logged (`Extension {id} is enabled but not running: ...` (warning; information while disabled)); it never stops startup
  ([extensions → Loading code](../features/extensions.md#loading-code)).

## Request pipeline

```mermaid
flowchart TD
    Req([HTTP request]) --> Dev{"Environment = Development?"}
    Dev -- no --> EH["UseExceptionHandler('/error') + UseHsts()"]
    Dev -- yes --> SH
    EH --> SH["SecurityHeadersMiddleware (CSP, nosniff, X-Frame-Options, ...)"]
    SH --> SF["UseStaticFiles (wwwroot)"]
    SF -- "file found" --> Done([static file])
    SF --> R["UseRouting (select endpoint)"]
    R --> IM["InstallationMiddleware"]
    IM -- "not installed, path not /setup" --> S302([302 /setup])
    IM -- "installed, path starts /setup" --> A302([302 /admin])
    IM --> GATE["ExtensionEndpointGateMiddleware"]
    GATE -- "endpoint of an inactive extension" --> G404([404])
    GATE --> AuthN["UseAuthentication (cookie scheme, OnValidatePrincipal session check)"]
    AuthN --> RL["UseRateLimiter (policies from [EnableRateLimiting])"]
    RL -- "limit exceeded" --> T429([429])
    RL --> AuthZ["UseAuthorization (policies, roles, ApiToken scheme for /api)"]
    AuthZ -- "anonymous on protected admin endpoint" --> Login(["302 /login?ReturnUrl=..."])
    AuthZ -- "signed in, role missing" --> Denied([302 /account/denied])
    AuthZ --> EP["Endpoint: controller action / /health / fallback"]
```

Key properties:

- With `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (behind a TLS proxy) the host inserts the forwarded-headers
  middleware ahead of everything, so the scheme and `RemoteIpAddress` come from `X-Forwarded-*`
  ([deployment](../guides/deployment.md#behind-a-reverse-proxy)).
- **`SecurityHeadersMiddleware`** sets `Content-Security-Policy` (`Content-Security-Policy-Report-Only` in
  Development), `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`,
  `Referrer-Policy: strict-origin-when-cross-origin` and `Permissions-Policy` on every response, including static
  files and re-executed error pages. Policy: [security](../features/security.md).
- **Static files** in `src/DotNetForge.Web/wwwroot/` are served before routing, so the install gate never blocks CSS/JS.
- `InstallationMiddleware` lets through, without a DB check, any path starting with `/css`, `/js`, `/lib`, `/images`,
  `/img`, `/fonts`, `/favicon`, `/health`, **and any path with a file extension** (`Path.HasExtension`).
  Everything else is redirected to `/setup` until installed - including `/api/*` (a `302`, not a `401`).
- The installed check is cached by `InstallationStatusCache`: once true it never queries the DB again in that
  process.
- **Authentication** uses the cookie scheme by default. API controllers request the `ApiToken` scheme explicitly
  via `[Authorize(AuthenticationSchemes = "ApiToken")]`, so a cookie never authenticates an API call and a bearer
  token never authenticates an admin screen. The token handler runs during authorization, i.e. **after** the rate
  limiter, so rejected requests never reach PBKDF2.
- **`ExtensionEndpointGateMiddleware`** (`Startup/ExtensionHosting.cs`) runs after routing has picked the endpoint:
  an endpoint carrying `ExtensionEndpointMetadata` (mapped by `MapDotNetForgeExtensionsAsync` or an MVC controller in an
  extension assembly) answers `404` while its extension is not active (`ExtensionCatalog.IsActiveAsync`).
- **Rate limiting** applies only to endpoints with `[EnableRateLimiting]` / `RequireRateLimiting`: `credentials`
  (POST `/login`, the self-service account POSTs, POST `/setup`, the external sign-in endpoints; 10 per minute per IP), `api` (every
  `ApiControllerBase` action; 300 per minute per IP) and `forms` (extension form posts that opt in; 5 per minute per
  IP). Fixed windows, no queue, partitioned by `Connection.RemoteIpAddress`, per instance. Rejections get an empty
  `429`.

## Routing

Endpoints come from five sources, mapped in `Program.cs`:

| Source | Mapping | Matches |
| --- | --- | --- |
| Attribute routes | `[Route]`/`[HttpGet("...")]` on every controller (including MVC controllers in extension assemblies) | all admin, account, setup, extension and API routes, `/`, `/error` (any method), `/media/{id:guid}/{fileName?}`, `/ext/{id}/resources/{**path}` |
| Conventional route `default` | `{controller=Home}/{action=Index}/{id?}` | only actions **without** attribute routes - in practice `HomeController.RenderPage` at `/Home/RenderPage` (unused) |
| Minimal API | `app.MapGet("/health", ...)` | `/health` |
| Extension endpoints | `await app.MapDotNetForgeExtensionsAsync()`: each loaded extension's `IExtension.MapEndpoints` in a group tagged with its id | e.g. `/sitemap.xml`, `/robots.txt`, `POST /ext/contact-form/submit` (samples); gated per request |
| Fallback | `app.MapFallbackToController("RenderPage", "Home")` | any path not matched above **and without a file extension** (`{*path:nonfile}`) |

Because the fallback has the lowest priority, real routes such as `/admin/...`, `/setup`, `/account/...`,
`/media/...`, `/api/...`, `/ext/...` and extension endpoints always win over a content page with the same slug. A content page with slug `admin` is
therefore unreachable on the public site.

Full screen route list: [pages.md → Route map](pages.md#route-map).

### Public URL resolution

```mermaid
flowchart TD
    U["GET /about/team"] --> FB["Fallback → HomeController.RenderPage"]
    FB --> Load["Load (Id, ParentPageId, Slug) of ALL live pages"]
    Load --> Walk["For each URL segment: find child of current parent<br/>exact slug (case-insensitive) first, else a '[param]' slug"]
    Walk -- "no match" --> NF([404 NotFound])
    Walk -- "matched every segment" --> Full["Load the matched page in full"]
    Full --> RV["Capture dynamic values into ViewData['RouteValues']"]
    RV --> CL["Layout? resolve its content lists:<br/>PageLayoutViewComponent.ResolveContentAsync → ContentRenderService (published entries)"]
    CL -- "detail page names no published entry" --> NF
    CL --> View["ThemedViewAsync('Page', matched): active theme's Views/Page.cshtml, else built-in; modules via ExtensionModules"]
    Root["GET /"] --> Index["HomeController.Index"]
    Index -- "live page with slug '/' or 'home'" --> View
    Index -- "none" --> List["ThemedViewAsync('Index'): list of live pages"]
```

Rules and limitations are documented once in
[content pages and routing](../features/content-pages-and-routing.md#public-resolution).

Content lists are resolved per request in `HomeController` before the view renders, so a detail page
(`/products/[slug]`) whose URL names no published entry is a 404 like an unknown address. The result is handed to
`PageLayoutViewComponent` through `HttpContext.Items["dnf:layout-content"]` (not queried twice), and the entry's
title becomes the page title (`ViewData["ContentTitle"]`). Details:
[structured content → rendering flow](../features/structured-content.md#rendering-flow).

## Navigation flow (admin)

All admin navigation is plain links in the sidebar (built from `AdminNavigation.Tree` by `AdminNavigationService`)
and full-page loads. Mutations are form POSTs followed by
`RedirectToAction` (Post/Redirect/Get), except the drag-to-reorder of the Pages tree and of an entry list, which are
`fetch` POSTs (and the page builder's JSON API). See
[pages.md → Navigation](pages.md#navigation).

## Authentication flow (admin sign-in)

```mermaid
sequenceDiagram
    actor User
    participant RL as Rate limiter (credentials)
    participant AC as AccountController
    participant AS as AuthService
    participant DB as DotNetForgeDbContext
    participant H as IPasswordHasher
    participant Aud as IAuditService
    User->>RL: POST /login (email, password, returnUrl, antiforgery)
    alt more than 10 attempts this minute from this IP
        RL-->>User: 429
    end
    RL->>AC: Login
    AC->>AS: ValidateAsync(email, password)
    AS->>DB: User by Email
    alt not found
        AS-->>AC: InvalidCredentials
    else Disabled
        AS-->>AC: Disabled
    else LockoutEndUtc in future
        AS-->>AC: LockedOut
    else wrong password
        AS->>DB: FailedLoginCount++ (5th → LockoutEndUtc = now + 15 min, count reset)
        AS-->>AC: InvalidCredentials
    else ok, user has a usable second factor
        AS->>DB: reset counters
        AS-->>AC: SecondFactorRequired
    else ok
        AS->>DB: reset counters, LastLoginDate = now
        AS->>DB: role names via UserRoles ⨝ Roles
        AS-->>AC: Success + ClaimsPrincipal
    end
    alt SecondFactorRequired
        AC-->>User: dnf.mfa cookie, 302 /login/mfa (MfaLoginController finishes: cookie + user.login)
    else Success
        AC->>AC: SignInAsync(cookie scheme), HttpContext.User = principal
        AC->>Aud: user.login (entry carries the signed-in user)
        AC-->>User: LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/admin")
    else failure
        AC->>Aud: user.login.failed (success = false)
        AC-->>User: re-render Login with error
    end
```

The cookie principal carries `NameIdentifier` (user id), `Name` (display name), `Email` and one `Role` claim
per role. Roles are read **only at sign-in**; changing a user's roles takes effect after they sign in
again. A non-local or missing `returnUrl` goes to `/admin`. `POST /setup` follows the same pattern (rate-limited,
signs the new Super Admin in, audits `cms.installed`). Sign-in through a provider - built in or from an
authentication extension (`POST /account/external/{scheme}` → "Continuing to" page → provider →
`GET /account/external/callback`) - ends in the same `AuthService` principal and cookie, creating the account first
while sign-ups are on: [external sign-in](../features/authentication.md#external-sign-in). An account with a second
factor goes through `/login/mfa` either way ([MFA → sign-in](../features/mfa.md#sign-in)). Full details:
[authentication](../features/authentication.md).

### Session validation (every cookie request)

```mermaid
sequenceDiagram
    participant C as Cookie handler
    participant V as DependencyRegistration.ValidateSessionAsync
    participant M as IMemoryCache
    participant AS as AuthService
    C->>V: OnValidatePrincipal
    alt no parsable NameIdentifier
        V->>C: RejectPrincipal
    end
    V->>M: get dnf:session:{userId}
    alt not cached
        V->>AS: IsActiveAsync(userId) - exists and Status = Enabled
        V->>M: set result for 30 s
    end
    alt inactive
        V->>C: RejectPrincipal + SignOutAsync (request continues anonymous)
    end
```

A disabled or deleted user therefore loses access within 30 seconds; their role claims are not re-read.

## Authorization flow (admin screen)

```mermaid
flowchart LR
    Req["POST /admin/pages/update/{id}"] --> Policy{"AdminArea policy:<br/>role in Super Admin, Admin, Editor, Author?"}
    Policy -- "no cookie" --> L([302 /login])
    Policy -- "no" --> D([302 /account/denied → 403 page])
    Policy -- yes --> Roles{"Controller [Authorize(Roles = ...)]?"}
    Roles -- "not in role" --> D
    Roles -- ok --> Perm{"Can / CanModify<br/>(IPermissionService)"}
    Perm -- "denied: Forbid()" --> D
    Perm -- ok --> Action["PagesController.Update"]
```

All `[Authorize]` attributes on a controller must pass (they are combined with AND). The `Can`/`CanModify` step exists
only in `ContentController` (area `Collection types`) and `MediaController` (area `Media`); other screens stop at
the role checks. `CanModify` grants the "any" action, or the `.own` action when `CreatedById`/`UploadedById` is the
current user. The sidebar hides links whose controller's `[Authorize]` data the user fails (`AdminNavigationService`),
but that is not the gate: a direct URL still meets these checks and leads to the denied screen. See [authorization](../features/authorization.md).

## API request flow

```mermaid
sequenceDiagram
    actor Client
    participant IM as InstallationMiddleware
    participant RL as Rate limiter (api)
    participant TH as ApiTokenAuthenticationHandler
    participant MC as IMemoryCache
    participant DB as DotNetForgeDbContext
    participant F as RequireApiPermission filter
    participant C as Api controller
    Client->>IM: GET /api/content/pages, Authorization: Bearer dnf_ab12cd34_...
    IM->>RL: (installed)
    alt more than 300 requests this minute from this IP
        RL-->>Client: 429
    end
    RL->>TH: authorize endpoint with scheme ApiToken
    alt no header / not Bearer
        TH-->>Client: NoResult → 401
    end
    TH->>MC: get dnf:apitoken:{SHA-256 of presented token}
    alt cached
        TH->>DB: ApiToken by cached Id
    else not cached
        TH->>DB: ApiTokens where TokenPrefix = prefix
        TH->>TH: PBKDF2 verify against each candidate
        TH->>MC: set token Id for 10 min (on success)
    end
    alt unknown, revoked or expired
        TH-->>Client: Fail → 401
    end
    opt LastUsedDate missing or older than 5 min
        TH->>DB: LastUsedDate = now (failure logged, request continues)
    end
    TH->>F: principal with dnf:permission claims
    alt permission claim missing
        F-->>Client: 403 { "error": "Missing required permission '...'" }
    end
    F->>C: execute
    C->>DB: query (AsNoTracking, projected)
    C-->>Client: 200 JSON
```

The token row is re-read on every request, so revocation and expiry take effect immediately even when the PBKDF2
result is cached. `POST /api/content/pages` goes through `IPageService.ApplyAsync` (400 `{ error }` on a rule
violation) and audits `content.created` as `API token {id}`. See [headless API](../features/headless-api.md).

## Database request flow (typical admin mutation)

```mermaid
sequenceDiagram
    actor Editor
    participant CC as PagesController.Update
    participant PS as IPageService (PageService)
    participant DB as DotNetForgeDbContext
    participant Aud as IAuditService
    Editor->>CC: POST /admin/pages/update/{id} (PageInput + antiforgery)
    CC->>DB: Page where Id = id (tracked)
    CC->>CC: CanModify(Collection types, update, update.own, CreatedById) else Forbid()
    alt Published or schedule changed and not Can(Collection types, publish)
        CC-->>Editor: 200 re-render with "You don't have permission to publish, unpublish or schedule pages."
    end
    CC->>PS: ApplyAsync(page, form)
    PS->>DB: AsNoTracking reads for parent/cycle, slug uniqueness, dynamic sibling
    alt rule violated (incl. length limits)
        PS-->>CC: error message
        CC-->>Editor: 200 re-render Index with ModelState error (form keeps input)
    else valid
        PS-->>CC: null (page mutated in memory, not saved)
        CC->>DB: UpdatedDate = now, SaveChangesAsync
        CC->>Aud: content.updated → separate SaveChangesAsync
        CC-->>Editor: 302 /admin/pages?selected={id} + TempData["Success"]
    end
```

The entity save and the audit save are two separate `SaveChangesAsync` calls (no shared transaction). The other
content mutations follow the same shape:

| Action | Permission | Service call | On rule violation | Audit |
| --- | --- | --- | --- | --- |
| `POST /admin/pages/create` | `create`; parent must exist (else 404) | none (new page `new-page-<8 hex>`) | - | `content.created` |
| `POST /admin/pages/update/{id}` | `update` or `update.own`; `publish` to change Published/schedule | `ApplyAsync` | re-render with error | `content.updated` |
| `POST /admin/pages/delete/{id}` | `delete` or `delete.own` | `DeleteAsync` (re-parents children; refuses if they would clash) | re-render with error | `content.deleted` |
| `POST /admin/pages/reorder` (`fetch`) | `update` | `ReorderAsync` (known page ids, no cycles, no duplicate slugs / second dynamic segment) | `400 { error }` → `alert(error)` | `content.reordered` |
| `POST /api/content/pages` | API key `content.create` | `ApplyAsync` | `400 { error }` | `content.created` |

## Media upload flow

```mermaid
sequenceDiagram
    actor User
    participant MC as MediaController.Upload
    participant MS as MediaService
    participant FS as IFileStorage
    participant DB as DotNetForgeDbContext
    participant Aud as IAuditService
    User->>MC: POST /admin/media/upload (multipart files, isPublic, antiforgery)
    Note over MC: [MediaUploadLimit] = largest per-kind limit x 10 + 1 MB<br/>bodies over 64 KB are buffered in the OS temp dir
    MC->>MC: Can(Media, create) else Forbid(), then 1 to 10 files
    loop each file
        MC->>MS: UploadAsync(file, isPublic, CurrentUserId)
        MS->>MS: reject empty, extension not in AllowedTypes, over the limit of its kind, duplicate
        opt image optimization on
            MS->>MS: ImageOptimizer (kept only when smaller)
        end
        MS->>MS: key = {yyyy}/{MM}/{guid:N}{ext}, content type from extension
        MS->>FS: SaveAsync(key, stream, contentType)
        alt storage throws
            MS->>FS: DeleteAsync(key) (best effort)
            MS-->>MC: "'x' could not be stored right now. Try again later." (LogError)
        else stored
            MS->>DB: add MediaFile (MP4 to compress: Optimization Pending), SaveChangesAsync
            opt save throws
                MS->>FS: DeleteAsync(key), rethrow (no orphaned object)
            end
            MS->>Aud: media.uploaded
        end
    end
    alt any file failed
        MC-->>User: 200 Media screen with errors (stored files are listed)
    else all stored
        MC-->>User: 302 /admin/media + TempData["Success"]
    end
```

Delete (`POST /admin/media/delete/{id}`) needs `Can(Media, delete)` or `delete.own` on files the user uploaded;
`MediaService.DeleteAsync` removes the row first, then the object (a failed object delete is logged as an orphan
warning), then audits `media.deleted`. Rules and limits:
[media storage → upload flow](../features/media-storage.md#upload-flow).

## Media download flow

```mermaid
sequenceDiagram
    actor B as Browser
    participant MF as MediaFilesController.Download
    participant DB as DotNetForgeDbContext
    participant FS as IFileStorage
    participant OS as Object storage
    B->>MF: GET /media/{id}/{fileName?}
    MF->>DB: MediaFile by Id (FileName, ContentType, RelativePath, IsPublic)
    alt no row, or private and caller is not an admin-capable user
        MF-->>B: 404 (existence not revealed)
    end
    MF->>MF: Content-Disposition inline (image, video, audio, PDF) or attachment<br/>Cache-Control "public, max-age=300" or "private, no-store"
    MF->>FS: GetDownloadUrlAsync(key, disposition, 1 h public / 5 min private)
    alt S3 provider returns a presigned URL
        MF-->>B: 302 to url.AbsoluteUri
        B->>OS: GET presigned URL
        OS-->>B: object (response-content-disposition)
    else local provider returns null
        MF->>FS: OpenReadAsync(key)
        alt object missing
            MF-->>B: 404 (LogWarning)
        else found
            MF-->>B: 200 stream + Content-Disposition, range requests when seekable
        end
    end
```

The trailing `fileName` is cosmetic; the id selects the file. The controller has no `[Authorize]`: the cookie (if
any) is read by `UseAuthentication`, and private access is decided by role claim (admin-capable roles). Details:
[media storage → download flow](../features/media-storage.md#download-flow).

## Background operation

`ScheduledPublishingService` (hosted) waits 5 s, then every 15 s opens a DI scope and finalizes due schedules.
It runs on every instance; the work is idempotent. It does not decide public visibility -
`PageVisibility.Live` already hides/shows pages by comparing the schedule to `DateTime.UtcNow` on each request. See
[scheduled publishing](../features/scheduled-publishing.md).

```mermaid
stateDiagram-v2
    [*] --> Waiting : host start
    Waiting --> Applying : 5 s startup delay, then every 15 s
    Applying --> Waiting : saved (or nothing due)
    Applying --> Waiting : exception → LogWarning, retry next tick
    Waiting --> [*] : stoppingToken cancelled
```

## Configuration loading

```mermaid
flowchart LR
    EnvVar["Process environment variable"] -- "wins when non-empty" --> Get["EnvConfigurationLoader.Get(key)"]
    File[".env in content root (DotEnvParser)"] -- "used when env var empty/missing" --> Get
    Get --> Validate{"valid? (outside Development:<br/>database configured, S3 configured)"}
    Validate -- no --> Ex["ConfigurationException → exit 1"]
    Validate -- yes --> AE["AppEnvironment singleton"]
    AE --> DbCfg["DatabaseProviderRegistry → provider<br/>(EF Core context + IDatabaseService)"]
    AE --> Storage["IFileStorage: S3FileStorage (dev fallback: LocalFileStorage)"]
    AE --> Views["AppName in ViewData / layouts"]
    AppSettings["src/DotNetForge.Web/appsettings.json / .Development.json"] --> Logging["Logging levels, AllowedHosts"]
```

See [configuration](../features/configuration.md) and
[deployment → environment variables](../guides/deployment.md#environment-variables).

## Error handling flow

| Situation | Where detected | What the user gets |
| --- | --- | --- |
| Invalid `.env`, or a relative/missing path outside Development | `EnvConfigurationLoader` at startup | Process exits with code 1; message on stderr |
| Form validation failure (incl. length limits) | `PageService`, `MediaService`, `InstallationService`, controller checks (Settings, API Tokens) | Same screen re-rendered with the errors in `<form-errors />` |
| Permission denied by `Can`/`CanModify` | `PagesController`, `ContentController`, `MediaController` → `Forbid()` | 302 to `/account/denied` (renders with status 403) |
| Content delete would make children clash | `PageService.DeleteAsync` | Pages re-rendered with the error |
| Entry invalid, not unique, or saved by someone else meanwhile | `ContentEntryService.SaveAsync` | entry form re-rendered with the errors (a conflict offers an overwrite) |
| Detail page names an entry that doesn't exist or isn't published | `ContentRenderService.ResolveAsync` (`MissingEntry`) | 404, like an unknown URL |
| Reorder rejected (cycle, clash, foreign id) | `PageService.ReorderAsync` | `400 { error }`; `pages.js` shows it as a danger toast and reloads |
| Entity not found | controller `FirstOrDefaultAsync` → `NotFound()` | Empty-body 404 |
| Public URL not resolving | `HomeController.RenderPage` | Empty-body 404 |
| Private media requested without access, or missing object | `MediaFilesController` | Empty-body 404 (missing object also logged) |
| Media storage unavailable during upload | `MediaService.UploadAsync` | Per-file message on the Media screen; error logged |
| Anonymous on admin | cookie challenge | 302 to `/login?ReturnUrl=...` |
| Wrong role | cookie forbid | 302 to `/account/denied` (renders with status 403) |
| Disabled/deleted user with a valid cookie | `ValidateSessionAsync` | Signed out within 30 s; next admin request → login |
| Too many requests | rate limiter (`credentials`, `api`, `forms`) | Empty `429` |
| Endpoint of a disabled or failed extension | `ExtensionEndpointGateMiddleware` | Empty-body 404 |
| Extension widget/module view throws | `ExtensionViewRenderer` | "<name> could not be displayed." in place of the view; error logged |
| Extension audit handler throws or times out (30 s) | `AuditEventDispatcher` | Nothing visible; error logged, next handler runs |
| External sign-in fails (no account, disabled, locked, provider error) | `ExternalLoginController` | Redirect to `/login` with a message; `user.login.failed` audited |
| API auth failure / missing permission | `ApiTokenAuthenticationHandler` / `RequireApiPermissionAttribute` | 401 / 403 JSON |
| API rule violation | `ContentApiController` via `IPageService` | `400 { "error": "..." }` |
| Unhandled exception (Development) | developer exception page | Stack trace page |
| Unhandled exception (other environments) | `UseExceptionHandler("/error")` → `HomeController.Error` (`[Route("/error")]`, any method) | "Something went wrong" page, for GET and POST alike |
| Background job exception | `ScheduledPublishingService` catch | Log warning; retried next tick |

Details and gaps: [logging and errors](../features/logging-and-error-handling.md).

## External integrations

Out-of-process dependencies at runtime: the PostgreSQL server, any additional databases (`DATABASES_<NAME>_*`) and an S3-compatible object
store (outside Development always) - the app uploads, reads and deletes objects through
`S3FileStorage` (or an enabled storage connector), and browsers download directly from presigned URLs. Enabled
extensions may call their own services (the samples: Open-Meteo, Discord OAuth, Twilio, a second S3 bucket). Planned integrations and their current state
are listed in [implementation-status.md](../implementation-status.md).
