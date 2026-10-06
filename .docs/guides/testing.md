# Testing

## Test projects

| Project | References | Runs against | Count (Oct 2026) |
| --- | --- | --- | --- |
| `tests/DotNetForge.Tests` | `src/` libraries (not the host, not `Api`) | pure code, fakes, temp folders, closed ports; optionally a live S3 bucket | 198 tests: 193 run, 5 live-S3 tests skipped unless `DNF_TEST_S3_*` is set |
| `tests/DotNetForge.IntegrationTests` | `src/DotNetForge.Web/DotNetForge.Web.csproj` | the real host via `WebApplicationFactory<Program>`, a temp SQLite file (or a [database server](#databases)) and a temp media folder | 53 tests: 50 run on any database, 3 credential tests skipped unless their server is configured |

```bash
dotnet test tests/DotNetForge.Tests            # Passed: 193, Skipped: 5
dotnet test tests/DotNetForge.IntegrationTests # Passed: 50, Skipped: 3
```

Both are safe to run locally: no external services, no network, no `.env` required (the integration factory sets
environment variables), nothing written inside the repository. The integration suite passes on SQLite (default)
and on PostgreSQL 17, SQL Server 2022, MySQL 8.4 and MongoDB 8.0 ([databases](#databases)).

## Unit tests (`DotNetForge.Tests`)

| File | Classes (tests) | Covers |
| --- | --- | --- |
| `DatabaseProviderTests.cs` | `DatabaseProviderTests` | provider names and aliases, detection from the connection string (and refusing ambiguous ones), per-provider validation (SQLite paths, PostgreSQL URLs, MongoDB database name), descriptions without credentials, several databases, registering and replacing providers |
| `DatabaseTranslationTests.cs` | `DatabaseTranslationTests` | exact parameterized SQL from each SQL database's own builder, values never in SQL text (checked on all four builders), LIKE escaping, aggregates, model-checked names, identifier validation, value coercion; MongoDB filters with typed values (UUID, enum, date), values never operators, escaped regexes, composite keys under `_id` |
| `DatabaseServiceTests.cs` | `DatabaseServiceTests` (mock provider) | routing to named databases, result shape, FindOne limit and default timeout, timeout vs caller cancellation, error translation with context and inner exception, bugs not translated, `Try*` results, invalid commands rejected before the provider, transactions bound to one database, streaming, connection tests, logs without values or secrets |
| `DatabaseConnectionTests.cs` | `DatabaseConnectionTests` | real PostgreSQL, SQL Server, MySQL and MongoDB drivers against a closed port → `DatabaseConnectionException` without credentials |
| `EnvConfigurationTests.cs` | `EnvConfigurationTests`, `DotEnvParserTests` (1) | `EnvConfigurationLoader`: database settings read as written (providers validate them), named `DATABASES_*` databases and invalid keys, Development storage defaults under the content root (`storage/media`), S3 settings with R2-style defaults (`STORAGE_S3_REGION=auto`), S3 required outside Development, any S3 key selecting S3, invalid S3 configuration (missing bucket, missing region, malformed service URL, `STORAGE_S3_FORCE_PATH_STYLE`); `DotEnvParser` |
| `FileStorageTests.cs` | `LocalFileStorageTests` (8), `StorageKeyTests` (13), `S3FileStorageTests` (8, 5 of them live) | the shared `IFileStorage` contract (`FileStorageContract`: save/read, overwrite, missing → `null`, idempotent delete, invalid keys) for the local provider; local-only: files stay inside the root, no temp files left, a failed upload leaves no partial object, no download URLs; `StorageKey.IsValid`; S3 offline: presigned URLs signed, short-lived and carrying `response-content-disposition`, presigning follows a plain `http://` endpoint, invalid keys rejected before any request; S3 live (`[S3Fact]`): the contract plus a download through a presigned URL |
| `ExtensionLoaderTests.cs` | `ExtensionLoaderTests` (3) | `ExtensionLoader`: finds valid admin extensions by id case-insensitively, cache invalidated when a manifest is added (file watcher), missing root → no extensions |
| `InstallationServiceTests.cs` | `InstallationServiceTests` (5) | `InstallationService` with a `FakeInstallationStore`: success, mismatch, weak password, invalid email, already installed |
| `ManifestValidationTests.cs` | `ManifestValidationTests` (20) | `ManifestValidator`: required fields, permissions, type, semver, all nine types |
| `PermissionTests.cs` | `PermissionTests` (12) | `PermissionService` / `PermissionMatrix` defaults and union semantics |
| `SecurityPrimitiveTests.cs` | `PasswordHasherTests` (3), `ApiTokenFactoryTests` (2), `WebhookSignerTests` (3) | `Pbkdf2PasswordHasher`, `ApiTokenFactory`, `HmacWebhookSigner` |
| `ValidationTests.cs` | `PasswordPolicyTests` (7), `EmailValidatorTests` (6) | `PasswordPolicy`, `EmailValidator` (the `SlugHelper` tests were removed with the class; slug rules are tested through `PageService`) |

`EnvConfigurationTests` saves, clears and restores the configuration environment variables around each test and
loads from a temp directory.

Not covered by unit tests: `AuthService` (lockout), controllers. `PageService` and `MediaService` are covered by
integration tests.

### Live S3 tests

The five `[S3Fact]` tests in `S3FileStorageTests` are skipped unless these environment variables are set:

| Variable | Required | Example (local SeaweedFS) |
| --- | :-: | --- |
| `DNF_TEST_S3_SERVICE_URL` | ✔ | `http://localhost:8333` |
| `DNF_TEST_S3_BUCKET` | ✔ | `dnf-test` |
| `DNF_TEST_S3_ACCESS_KEY_ID` | ✔ | `dev` |
| `DNF_TEST_S3_SECRET_ACCESS_KEY` | ✔ | `dev` |
| `DNF_TEST_S3_REGION` | | default `auto` |
| `DNF_TEST_S3_FORCE_PATH_STYLE` | | `true` for SeaweedFS/MinIO (default `false`) |

A local S3-compatible server - either `docker compose -f docker/compose.dev.yml up -d` (SeaweedFS on port 8333, bucket
`dotnetforge` created by the `s3-bucket` service; use `DNF_TEST_S3_BUCKET=dotnetforge`) or a standalone container:

```bash
docker run -d --name s3 -p 8333:8333 chrislusf/seaweedfs server -s3 -dir=/data
until curl -sf -X PUT http://localhost:8333/dnf-test; do sleep 1; done   # creates the bucket
DNF_TEST_S3_SERVICE_URL=http://localhost:8333 DNF_TEST_S3_BUCKET=dnf-test \
DNF_TEST_S3_ACCESS_KEY_ID=dev DNF_TEST_S3_SECRET_ACCESS_KEY=dev DNF_TEST_S3_FORCE_PATH_STYLE=true \
  dotnet test tests/DotNetForge.Tests                                   # the 5 live tests now run too
```

The SeaweedFS S3 gateway runs without authentication (any key works) - development only. Any real S3-compatible
bucket (R2, AWS S3) works the same way; the tests write and delete objects under `tests/`.

## Integration tests (`DotNetForge.IntegrationTests`)

### `DotNetForgeWebFactory`

Each factory instance creates a temp work directory `<temp>/dnf-it-<guid>/` and sets, **as process environment
variables** before the host builds:

| Variable | Value |
| --- | --- |
| `DATABASE_CONNECTION_STRING` | `Data Source=<workdir>/cms.db` (SQLite), or a fresh PostgreSQL database when `DNF_TEST_POSTGRES` is set |
| `STORAGE_S3_*` | placeholder values (startup requires S3 outside Development); `IFileStorage` is replaced through `ConfigureTestServices` with a `LocalFileStorage` in `<workdir>/media` (exposed as `factory.StoragePath`), so no S3 server is needed |
| `APP_URL` | `http://localhost` |

So each factory gets a fresh, migrated, seeded database and its own media folder; the work directory is deleted on
dispose (best effort). Because of these process-wide variables, parallelization is disabled (`AssemblyInfo.cs`).
The constructor takes the environment name (default `Development`); `new DotNetForgeWebFactory("Production")` runs
the production code paths (CSP enforced, `Secure` cookie, absolute-path configuration rules).

| Helper | Does |
| --- | --- |
| `InstallAsync()` | installs programmatically (`admin@example.com` / `Sup3rSecret`) through `IInstallationStore` and marks `InstallationStatusCache` |
| `CreateUserAsync(email, password, role)` | adds an enabled user with one built-in role to the default tenant, returns its id |
| `WithDbAsync(db => ...)` | runs code against a fresh `DotNetForgeDbContext` scope (arrange or assert on rows) |
| `CreateNoRedirectClient()` | cookie-keeping client that does not follow redirects, base address `https://localhost` (the auth cookie is `Secure` outside Development) |
| `SignInAsync(email?, password?)` | a `CreateNoRedirectClient()` signed in through the real login form (throws unless the POST returns `302`) |
| `PostFormAsync(client, formPage, action, fields)` | GETs `formPage` for the antiforgery token, then POSTs the form fields plus the token to `action` |
| `GetAntiforgeryTokenAsync(client, formPage)` | extracts `__RequestVerificationToken` (e.g. for a JSON `fetch`-style request with `X-CSRF-TOKEN`) |

### Databases

The integration suite runs on SQLite unless one of these variables holds a server connection string **without** a
database (`TestDatabaseServer`; the first one set wins). Every factory then creates its own database `dnf_it_<guid>`
through that provider's migrations (MongoDB: `EnsureCreated`) and drops it on dispose:

| Variable | Example (the `docker/compose.dev.yml` servers) |
| --- | --- |
| `DNF_TEST_POSTGRES` | `Host=localhost;Port=5432;Username=postgres;Password=postgres;GSS Encryption Mode=Disable` |
| `DNF_TEST_SQLSERVER` | `Server=localhost,1433;User Id=sa;Password=DotNetForge!2026;TrustServerCertificate=true` |
| `DNF_TEST_MYSQL` | `Server=localhost;Port=3306;Uid=root;Pwd=dotnetforge` |
| `DNF_TEST_MONGODB` | `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true` (a replica set) |

```bash
docker compose -f docker/compose.dev.yml --profile mongodb up -d
DNF_TEST_MONGODB="mongodb://localhost:27017/?replicaSet=rs0&directConnection=true" dotnet test tests/DotNetForge.IntegrationTests
```

All 50 database-independent tests must pass on every database. `DatabaseCredentialTests` additionally run for
PostgreSQL, SQL Server and MySQL when their variable is set (`[DatabaseServerFact]`). `PageServiceTests` always use
in-memory SQLite (they don't use the factory).

### Tests

| File | Test | Asserts |
| --- | --- | --- |
| `CmsIntegrationTests.cs` | `Health_endpoint_is_ok_even_before_install` | `/health` → 200 |
| | `Before_install_all_requests_redirect_to_setup` | `/` → 302 `/setup` |
| | `Setup_page_is_served_before_install` | `/setup` → 200 containing "Email" |
| | `After_install_setup_is_blocked` | `/setup` → 302 `/admin` |
| | `After_install_admin_requires_authentication` | `/admin` → 302 containing `/account/login` |
| | `Api_requires_a_token` | `/api/content/pages` → 401 |
| `MediaTests.cs` | `Public_upload_is_stored_outside_the_app_and_downloadable_anonymously` | upload lands under `StoragePath`, anonymous download works |
| | `Private_file_is_hidden_from_anonymous_users_but_served_to_admins` | anonymous → 404, signed-in admin → file |
| | `Unknown_media_id_is_not_found` | 404 |
| | `Active_or_unknown_file_types_are_rejected` (4 cases: `.html`, `.svg`, `.js`, no extension) | form error "file type that is not allowed", no row, no stored object |
| | `Delete_removes_the_row_and_the_stored_object` | object removed, link 404, `media.deleted` audited |
| | `Storage_outage_gives_a_clear_error_and_no_orphaned_row` | a failing `IFileStorage` → clear message, no `MediaFile` row |
| | `Author_cannot_delete_media_uploaded_by_someone_else` | Author's delete of another user's file is refused |
| `SecurityTests.cs` | `Responses_carry_security_headers` | `nosniff`, `SAMEORIGIN`, CSP `script-src 'self'` (Production) |
| | `An_exception_during_a_post_renders_the_error_page` | regression: a failing form POST renders the error page (500), not an empty 500 (Production, throwing `IPageService`) |
| | `Login_ignores_an_external_return_url` | `returnUrl=https://evil.example/` → 302 `/admin` |
| | `Successful_login_is_audited_with_the_user` | `user.login` entry has `UserId` and `TenantId` |
| | `Disabling_a_user_ends_their_session` | after disabling (session cache cleared), the next admin request → 302 `/account/login` |
| | `Repeated_sign_in_attempts_are_rate_limited` | 12 login POSTs: first 200, last 429 |
| | `Editors_can_no_longer_change_settings` | Editor `GET /admin/settings` → 302 `/account/denied` |
| | `Authors_cannot_delete_or_publish_pages_they_did_not_create` | Author: delete of a seeded page → denied; own page: publishing → form error, saving with Disabled → 302 |
| | `Reorder_rejects_a_cycle` | `POST /admin/content/reorder` making a page its child's child → 400 containing "cycle" |
| | `Duplicate_api_token_name_is_a_validation_error_not_a_crash` | second token with the same name → 200 "already exists" |
| | `Api_page_creation_applies_the_content_rules_and_is_audited` | API create slugifies (`our-pricing`), duplicate root slug → 400, `content.created` with `UserId = null` and `API token ...` |
| `ReadOnlyDeploymentTests.cs` | `A_full_session_writes_nothing_into_the_content_root` | snapshot (path, length, last write) of the content root - minus `bin`, `obj`, `.git`, `.vs`, `TestResults`, `node_modules` - is identical before and after a Production session: setup POST, sign-in, admin screens, public pages, the sample admin extension, upload, download, delete |
| | `Production_refuses_database_paths_inside_the_deployment_directory` (2 cases) | the host's database registration with a production host: relative SQLite `Data Source`, missing `DATABASE_CONNECTION_STRING` → `DatabaseConfigurationException` (missing S3 storage is covered by `EnvConfigurationTests`) |
| `DatabaseServiceContractTests.cs` | `Crud_round_trip`, `Ef_core_and_the_service_read_each_others_data`, `Database_generated_keys_are_created_on_insert`, `Text_filters_match_literally`, `Aggregates_group_and_count`, `Unique_violations_are_conflicts`, `Transactions_commit_or_roll_back`, `Large_results_can_be_streamed`, `Seeded_cms_data_is_queryable_by_entity_name`, `Composite_keys_are_read_and_filtered_like_other_fields`, `Unknown_names_and_connection_status_are_reported` | `IDatabaseService` on the integration database: the provider contract ([providers](../database/providers.md#contract-and-testing)) |
| | `AdditionalDatabaseTests.Commands_run_against_a_named_database_without_a_model` | a `DATABASES_REPORTS_*` SQLite database: unmapped names, `SELECT *`, aggregates, key-less `DeleteOne` refused, identifier validation |
| `DatabaseCredentialTests.cs` | `PostgreSql_…`, `SqlServer_…`, `MySql_reports_wrong_credentials` | wrong password → `DatabaseAuthenticationException` (skipped unless the server variable is set) |
| `PageServiceTests.cs` | `Slugifies_and_applies_valid_input`, `Rejects_duplicate_slug_under_the_same_parent_including_root`, `Rejects_a_parent_that_creates_a_cycle`, `Rejects_a_second_dynamic_segment_under_one_parent`, `Rejects_overlong_fields_before_saving`, `Reorder_rejects_moves_that_duplicate_a_slug`, `Reorder_rejects_pages_of_another_tenant`, `Delete_reparents_children_or_refuses_when_slugs_would_clash` | `PageService` rules against an in-memory SQLite `DotNetForgeDbContext` (`EnsureCreated`, no host) |

Not covered: the Users, Roles, Audit Logs and API Tokens list screens (Dashboard, Content Manager, Media and Plugins
are only rendered in the read-only session), Settings saves, API endpoints other than page creation, public dynamic
routes, account lockout, the upload size limit.

### Writing a signed-in integration test

```csharp
using var factory = new DotNetForgeWebFactory();
await factory.InstallAsync();
await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

var response = await DotNetForgeWebFactory.PostFormAsync(editor, "/admin/content", "/admin/content/create",
    new Dictionary<string, string>());

Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
await factory.WithDbAsync(async db => Assert.True(await db.Pages.AnyAsync(p => p.Title == "Untitled page")));
```

Never write into the repository from a test: use `factory.StoragePath`, the factory's temp directory or
`Path.GetTempPath()` - `ReadOnlyDeploymentTests` would catch host code that does.

## What to test when you change something

| Change | Add |
| --- | --- |
| Pure rule (validator, policy, matrix, configuration) | unit test in `DotNetForge.Tests` |
| `PageService` rule | test in `PageServiceTests` (in-memory SQLite) |
| Storage provider | run `FileStorageContract` against it (unit test class like `LocalFileStorageTests`; live tests behind an `[S3Fact]`-style attribute) |
| New screen / endpoint / auth rule | integration test with `CreateNoRedirectClient` / `SignInAsync` asserting status and redirect |
| New API endpoint | 401 without token, 403 without the key, 200 with it, tenant isolation |
| Anything that touches files at runtime | keep `ReadOnlyDeploymentTests` green; extend its session if the new path isn't exercised |
| Schema change | run the integration suite on every database (`DNF_TEST_POSTGRES`, `_SQLSERVER`, `_MYSQL`, `_MONGODB`): each SQL provider has its own migration set, MongoDB builds indexes |
| New database provider | [adding a provider → Tests](../database/adding-a-provider.md#5-tests) |
| Bug fix | a regression test that fails without the fix |

## CI

`.github/workflows/ci.yml` on push/PR to `main`/`master` (and manual dispatch):

- **build-and-test** on `ubuntu-latest`, `windows-latest`, `macos-latest`: `dotnet tool restore`, `cp .env.example .env`,
  `dotnet build --configuration Release`, both test projects in Release (SQLite; live S3 tests skipped).
- **format** on Ubuntu: `dotnet format <project> --verify-no-changes --severity error` for the host, every `src/*`
  and every `tests/*` project.
- **postgres-and-s3** (`PostgreSQL + S3 storage`) on Ubuntu: a `postgres:17-alpine` service and a SeaweedFS container
  (`chrislusf/seaweedfs server -s3`, bucket `dnf-test`); runs the unit tests **including** the live S3 tests
  (`DNF_TEST_S3_*` set) and the integration tests on PostgreSQL (`DNF_TEST_POSTGRES` set).
- **database-providers** (`Integration tests (<provider>)`) on Ubuntu: a matrix that starts SQL Server 2022, MySQL 8.4
  or MongoDB 8.0 (single-node replica set) with `docker run` and runs the integration suite with `DNF_TEST_SQLSERVER`,
  `DNF_TEST_MYSQL` or `DNF_TEST_MONGODB`.
- **read-only-container** (`Read-only container`) on Ubuntu: `docker build -f docker/Dockerfile -t dotnetforge:ci .`, then
  `docker run --read-only --tmpfs /tmp` with SQLite at `/tmp/cms.db` and media at `/tmp/media`; probes `/health` and
  `/setup` and asserts `docker diff app` is empty ([deployment](deployment.md#docker)).

The two new jobs have not run on GitHub yet; their commands were verified locally.

`.github/dependabot.yml` opens weekly NuGet (grouped `Microsoft.*`/`Npgsql.*`) and GitHub Actions updates.

There is no frontend linting (no ESLint config exists).

### `dotnet format` on Windows

`.editorconfig` sets `end_of_line = lf`. With Git's default `core.autocrlf=true` on Windows, the working copy has
CRLF line endings, so a local `dotnet format <project> --verify-no-changes` reports `error ENDOFLINE: Fix end of line
marker` on every line. That is line-ending noise from the checkout, not a style problem - the repository content is
LF and the CI format job (Ubuntu) passes. To check style locally on Windows, skip the whitespace pass:
`dotnet format style <project> --verify-no-changes --severity error` (and `dotnet format analyzers ...`).

## Planned (not implemented)

Target behaviour from the original product specification. Nothing in this section exists in the code unless marked ✔.

### Requirements

**Unit test coverage** (`DotNetForge.Tests`):

| Area | Must verify | Today |
| --- | --- | --- |
| Core services | business logic of each core service in isolation | `InstallationService`, `EnvConfigurationLoader`, `ExtensionLoader`, `LocalFileStorage` / `S3FileStorage` |
| Permissions | RBAC checks for every permission area; least privilege | a few role/area pairs in `PermissionTests` |
| Content manager | page CRUD, tree resolution, page types, scheduling, draft/published state | `PageService` rules (slug, cycle, dynamic segment, lengths, reorder, delete) in `PageServiceTests` - integration project, in-memory SQLite; no scheduling or liveness tests |
| Media validation | file-type allowlist, max upload size, blocking unsafe uploads | integration only: allowlist and active types rejected (`MediaTests`); size limit untested ([media storage](../features/media-storage.md#upload-flow)) |
| API token generation | secure creation, hashing before storage, expiration, permission scoping | ✔ creation + hash verification (`ApiTokenFactoryTests`); expiration and scoping untested |
| Manifest validation | required fields; rejection of invalid, unsafe or incompatible manifests | ✔ required fields, type, semver, permissions; "unsafe" and "incompatible" do not exist as checks |
| Import/export | provider-aware export and import; import-file validation | none ([transfer and updates](../features/transfer-and-updates.md)) |

**Integration test coverage** (`DotNetForge.IntegrationTests`):

| Flow | Must verify | Today |
| --- | --- | --- |
| Setup | not-installed detection, setup screen, first user created with `Super Admin`, CMS marked installed | gate and screen ✔; `ReadOnlyDeploymentTests` POSTs `/setup` (302) and signs in, but the `Super Admin` role and installed flag are not asserted |
| Authentication | login, failed login, lockout, provider-based sign-in | login ✔ (`SignInAsync`, audit, external `returnUrl`), rate limit ✔, disabled-user session end ✔; failed login only via the rate-limit test; lockout ✘ |
| API endpoints | token-authenticated requests, permission enforcement, tenant scoping | `401` without a token; one token-authenticated `POST /api/content/pages` with `content.create`; no 403 or tenant-scoping tests |

- Multi-tenant assertions: tenant context is resolved **first** (domain, subdomain or path prefix), and an API token
  cannot reach another tenant's data unless explicitly granted ([multi-tenancy](../features/multi-tenancy.md)).
- Unit test files mirror the namespaces of the code under test (a folder per service or validator); today all files
  sit flat in the project root.
- Integration tests may also run against a disposable PostgreSQL instance (✔ `DNF_TEST_POSTGRES`, CI job
  `postgres-and-s3`).
- Code coverage collectable with `dotnet test --collect:"XPlat Code Coverage"` (needs `coverlet.collector`, not
  referenced today).
- **Frontend linting:** an ESLint configuration wherever JavaScript/TypeScript exists (`src/DotNetForge.Web/wwwroot/js`, admin extension
  and theme assets), runnable as `npm run lint` locally and in CI, failing the build on errors; JS/TS formatting
  enforced alongside it.

### Rules and validation

- Unit tests are fast and isolated: database and network replaced by fakes, stubs or mocks.
- Integration tests boot the real host and use a disposable database; never a pre-seeded or shared environment.
- One test framework and assertion style across both projects (✔ xUnit only).
- Tests run on Windows, macOS and Linux, in CI on every push and pull request (✔ `ci.yml`).
- ✔ The whole suite runs with a bare `dotnet test` from the root (`DotNetForge.slnx`); one project runs by path
  (`dotnet test tests/DotNetForge.Tests`).
- Naming: `PascalCase` for types, methods and public members, `camelCase` for locals and parameters, interfaces
  prefixed `I`; descriptive names, no cryptic abbreviations. `.editorconfig` has no `dotnet_naming_rule` entries to
  enforce this.
- Dependencies: central, pinned versions (✔ `Directory.Packages.props`); prefer the framework before adding a
  package.
- Extensions validate their manifests and permissions exactly like the core.

### Acceptance criteria

Unit test coverage:

- [ ] Unit tests exist for core services (only `InstallationService`, configuration loading, `ExtensionLoader` and the
  storage providers).
- [ ] Unit tests exist for permissions across all permission areas.
- [ ] Unit tests exist for the content manager (`PageService` rules ✔ in `PageServiceTests`, but in the integration
  project against in-memory SQLite; no CRUD or scheduling tests).
- [ ] Unit tests exist for media validation (file type and size, unsafe-upload blocking) - file types are covered by
  the integration test `MediaTests.Active_or_unknown_file_types_are_rejected`; no unit tests, no size test.
- [x] Unit tests exist for API token generation (secure generation and hashed storage) (`ApiTokenFactoryTests`).
- [ ] Unit tests exist for manifest validation, asserting the eight required fields **and** rejecting
  invalid/unsafe/incompatible manifests (required fields ✔ in `ManifestValidationTests`; no unsafe/incompatible checks).
- [x] Unit tests exist for webhook signing (`WebhookSignerTests` in `SecurityPrimitiveTests.cs`).
- [ ] Unit tests exist for import/export.

Integration test coverage:

- [ ] Integration tests cover the first-time setup flow (first admin created with `Super Admin`, CMS marked installed);
  `ReadOnlyDeploymentTests` completes setup through `POST /setup` but asserts neither the role nor the flag.
- [ ] Integration tests cover authentication (login, failed login, lockout) - login ✔ (`SecurityTests`,
  `SignInAsync`); no explicit failed-login or lockout test.
- [ ] Integration tests cover API endpoints (token auth, permission enforcement, tenant scoping) - token auth ✔
  (`SecurityTests.Api_page_creation_applies_the_content_rules_and_is_audited`,
  `CmsIntegrationTests.Api_requires_a_token`); no permission-denied or tenant-scoping test.

Structure, execution and quality:

- [x] `tests/` contains the `DotNetForge.Tests` and `DotNetForge.IntegrationTests` projects.
- [x] At least one example unit test for manifest validation and one for a permission check (`ManifestValidationTests`,
  `PermissionTests`).
- [ ] `dotnet test` runs the full suite successfully on Windows, macOS and Linux (CI runs both projects on all three
  ✔; there is no single root `dotnet test`).
- [x] Integration tests use a disposable database and never touch development or production data
  (`DotNetForgeWebFactory`: temp SQLite file or a per-factory PostgreSQL database dropped on dispose, temp media
  folder).
- [ ] An ESLint configuration is present and runnable wherever JavaScript or TypeScript is used.
- [x] Automated formatting rules exist and are verified in CI (`.editorconfig`, `ci.yml` format job with
  `dotnet format --verify-no-changes`).
- [ ] Naming conventions are documented and consistently applied (documented in the
  [development guide](development.md#conventions) only partly; not enforced).
- [x] Dependencies are managed deliberately with no unnecessary packages (`Directory.Packages.props`, Dependabot).
- [x] The structure is clean and generated files and secrets are excluded via `.gitignore`.
