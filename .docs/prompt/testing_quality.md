# Testing & Code Quality

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This document defines the testing strategy and code-quality standards for **DotNetForge CMS** - a modular hybrid CMS built with ASP.NET Core MVC (C#, Entity Framework Core, Razor Views). It specifies the required unit and integration tests, the test project layout, illustrative examples, instructions for running the suite, and the linting, formatting, naming, and dependency-hygiene rules that keep the codebase clean and lightweight.

## Purpose

The project must ship with a working automated test suite and enforce consistent code quality across every module. Tests must cover the security-sensitive and behavior-critical parts of the CMS so that the core can be updated without silently breaking existing behavior (see [Version Control & Updates](transfer_updates.md)). Code-quality rules keep the repository small, the dependency graph minimal, and the structure predictable for both human developers and AI coding agents.

These standards apply to the core CMS and to extensions. Extensions must validate their manifests and permissions exactly like the core (see [Extension System](extensions.md) and [Extension Manifest](extensions.md)).

---

## Testing Requirements

The project must include unit testing and integration testing. The following coverage is required.

### Unit tests

The suite must include unit tests for:

| Area under test | What is verified | Owning module |
| --- | --- | --- |
| Core services | Business logic of core CMS services in isolation | [Architecture](architecture.md) |
| Permissions | RBAC checks for every permission area; least-privilege enforcement | [User Roles & Permissions](user_roles_permissions.md) |
| Content manager | Page CRUD, tree resolution, page types, scheduling, draft/published state | [Content Manager](content_manager.md) |
| Media validation | File-type allowlist, max upload size, blocking of unsafe uploads | [File Manager](file_manager.md) |
| API token generation | Secure token creation, hashing before storage, expiration, permission scoping | [API Tokens](api_tokens.md) |
| Extension manifest validation | Required-field validation of `dotnetforge.extension.json`; rejection of invalid/unsafe/incompatible manifests | [Extension Manifest](extensions.md) |
| Webhook signing | Deterministic, verifiable request signatures | [Webhooks](webhooks.md) |
| Import/export | Provider-aware data export and import; import-file validation | [Transfer](transfer_updates.md) |

### Integration tests

The suite must include integration tests for:

- **Setup flow** - first-time installation: detecting that the CMS is not yet installed, the registration/setup page, creation of the first admin user, assignment to the **Super Admin** role, and marking the CMS as installed. See [Installation & Setup](installation_setup.md).
- **Authentication** - login, failed-login handling, account lockout, and provider-based sign-in. See [Authentication Providers](authentication.md) and [Security](security.md).
- **API endpoints** - token-authenticated requests against headless/hybrid endpoints, including permission enforcement and tenant scoping. See [API Tokens](api_tokens.md) and [Multi-Tenancy](multi_tenancy.md).

> Multi-tenant note: integration tests for authentication and API endpoints must assert that tenant context is resolved **first** (by domain, subdomain, or path prefix) and that an API token cannot reach another tenant's data unless explicitly granted. See [Multi-Tenancy](multi_tenancy.md).

### Test project structure

Tests live under the `tests/` directory of the solution, split into two projects:

```text
DotNetForgeCMS/
  tests/
    DotNetForge.Tests/              # Unit tests (fast, isolated, no I/O)
    DotNetForge.IntegrationTests/   # Integration tests (host, DB, HTTP)
```

- **`DotNetForge.Tests`** - fast, isolated unit tests with no external dependencies. Database and network access are replaced with in-memory fakes, stubs, or mocks. Organize test files to mirror the namespaces of the code under test (for example, a folder per service or validator).
- **`DotNetForge.IntegrationTests`** - end-to-end tests that boot the application host (for example, via `WebApplicationFactory`) and exercise real wiring. Use an isolated SQLite database (or a disposable PostgreSQL instance) so tests never touch development or production data. See [Architecture](architecture.md) for database providers.

Both projects target the same .NET version as the rest of the solution and use a single, agreed test framework and assertion style across the codebase to keep dependencies minimal (see [Dependency management](#dependency-management)).

### Example unit tests

The following short, illustrative examples show the expected style. They are templates, not the full suite.

**Extension manifest validation** - required fields must be present, and invalid manifests must be rejected:

```csharp
using Xunit;

public class ManifestValidationTests
{
    private static ExtensionManifest ValidManifest() => new()
    {
        Id = "dotnetforge.theme.default",
        Name = "Default Theme",
        Description = "The default frontend theme for DotNetForge CMS.",
        Version = "1.0.0",
        Type = "theme",
        Author = "DotNetForge",
        EntryPoint = "DefaultTheme",
        Permissions = new[] { "content.read", "media.read" }
    };

    [Fact]
    public void Validate_ValidManifest_Succeeds()
    {
        var result = new ManifestValidator().Validate(ValidManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Name")]
    [InlineData("Description")]
    [InlineData("Version")]
    [InlineData("Type")]
    [InlineData("Author")]
    [InlineData("EntryPoint")]
    [InlineData("Permissions")]
    public void Validate_MissingRequiredField_Fails(string fieldName)
    {
        var manifest = ValidManifest();
        manifest.Clear(fieldName); // null/empty out the named required field

        var result = new ManifestValidator().Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == fieldName);
    }
}
```

The required manifest fields are exactly: `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions`. There is a single `version` field - the validator must not expect a duplicated `version` key or a misspelled `vrsion` field. See [Extension Manifest](extensions.md) for the full schema.

**Permission check** - a role must be granted access only to its allowed permission area:

```csharp
using Xunit;

public class PermissionCheckTests
{
    [Fact]
    public void Author_CanManageOwnContent_ButNotUsers()
    {
        var authorize = new PermissionService();

        // Author role: manage content they created, but no Users area access.
        Assert.True(authorize.Has("Author", area: "Collection types", action: "update.own"));
        Assert.False(authorize.Has("Author", area: "Users", action: "delete"));
    }

    [Fact]
    public void Public_HasNoAdminPermissions()
    {
        var authorize = new PermissionService();

        Assert.False(authorize.Has("Public", area: "Settings", action: "read"));
    }
}
```

Permission areas under test are: Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, and Webhooks. Default roles, from most-privileged to least, are **Super Admin, Admin, Editor, Author, Authenticated, Public**. See [User Roles & Permissions](user_roles_permissions.md) for the authoritative rules.

### Running the tests

Run the entire suite from the solution root:

```bash
# Run all unit and integration tests
dotnet test
```

Run a single project:

```bash
# Unit tests only
dotnet test tests/DotNetForge.Tests

# Integration tests only
dotnet test tests/DotNetForge.IntegrationTests
```

Useful options:

```bash
# Filter to a subset by fully-qualified name or trait
dotnet test --filter "FullyQualifiedName~ManifestValidationTests"

# Collect code-coverage data
dotnet test --collect:"XPlat Code Coverage"
```

Tests must run cross-platform on Windows, macOS, and Linux, and in CI on every push and pull request. Integration tests must use a disposable database and must never require a pre-seeded or shared environment.

---

## Linting and Code Quality

The project must include frontend linting support and enforce consistent quality rules across the codebase.

### ESLint configuration

If JavaScript or TypeScript is used (for example, in admin-area scripts or theme assets), the project must include an **ESLint** configuration. Lint must run locally and in CI, and the build should fail on lint errors. Provide an npm script so contributors can run it the same way everywhere:

```bash
# Lint all JS/TS sources
npm run lint
```

If no JavaScript or TypeScript is present in a given area, ESLint is not required there - but it must be in place wherever JS/TS exists. See the developer onboarding steps in [Developer Documentation](developer_docs.md).

### Code formatting rules

- Apply consistent, automated code formatting to all source. For C#, enforce formatting with `dotnet format` and an `.editorconfig` at the repository root; for JS/TS, enforce formatting alongside ESLint.
- Formatting must be checkable in CI so that unformatted code is rejected:

```bash
# Verify C# formatting without modifying files
dotnet format --verify-no-changes
```

### Naming conventions

- Use clear, descriptive names that reveal intent; avoid abbreviations and cryptic identifiers.
- Follow standard C# conventions: `PascalCase` for types, methods, and public members; `camelCase` for locals and parameters; interfaces prefixed with `I`.
- Keep names consistent with the canonical project and module names used throughout this specification (for example, `DotNetForge.Tests`, `DotNetForge.IntegrationTests`, `dotnetforge.extension.json`).

### Dependency management

- Manage dependencies centrally and explicitly. Pin versions and review additions deliberately.
- Avoid unnecessary packages - prefer the framework and existing project capabilities before adding a dependency.
- Keep a single, consistent test framework and assertion library across both test projects rather than mixing several.

### Clean structure and minimal project size

- Maintain clean folder boundaries between the core CMS, extensions, and tests. See [Architecture](architecture.md) for the full project layout.
- Keep the project lightweight and the repository as small as possible.
- Avoid committing generated files: build output, logs, uploads, cache, and secrets must be excluded via `.gitignore`.
- **Secrets must never be committed to Git.** All secrets live in `.env`; commit only `.env.example`. See [Security](security.md).

---

## Acceptance Criteria

The testing coverage checklist below is the acceptance criteria for this module. Every item must be satisfied.

### Unit test coverage

- [ ] Unit tests exist for core services.
- [ ] Unit tests exist for permissions (RBAC checks across all permission areas).
- [ ] Unit tests exist for the content manager.
- [ ] Unit tests exist for media validation (file type and size, unsafe-upload blocking).
- [ ] Unit tests exist for API token generation (secure generation and hashed storage).
- [ ] Unit tests exist for extension manifest validation, asserting the required fields `id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions` and rejecting invalid/unsafe/incompatible manifests.
- [ ] Unit tests exist for webhook signing.
- [ ] Unit tests exist for import/export.

### Integration test coverage

- [ ] Integration tests exist for the first-time setup flow (first admin created and assigned **Super Admin**; CMS marked installed).
- [ ] Integration tests exist for authentication (login, failed login, lockout).
- [ ] Integration tests exist for API endpoints (token auth, permission enforcement, tenant scoping).

### Structure, execution, and quality

- [ ] The solution contains a `DotNetForge.Tests` unit-test project and a `DotNetForge.IntegrationTests` integration-test project under `tests/`.
- [ ] The repository includes at least one example unit test demonstrating manifest validation and one demonstrating a permission check.
- [ ] `dotnet test` runs the full suite successfully and cross-platform (Windows, macOS, Linux).
- [ ] Integration tests use a disposable database and never touch development or production data.
- [ ] An ESLint configuration is present and runnable wherever JavaScript or TypeScript is used.
- [ ] Automated code-formatting rules exist and are verifiable in CI (`dotnet format --verify-no-changes`).
- [ ] Naming conventions are documented and consistently applied.
- [ ] Dependencies are managed deliberately, with no unnecessary packages.
- [ ] The project structure is clean, the repository stays minimal in size, and generated files and secrets are excluded via `.gitignore`.
