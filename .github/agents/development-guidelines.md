# Development Guidelines (for AI agents)

## Build, run, test

- Run/develop from the repo root: `dotnet run`, `dotnet watch`, `dotnet build`.
- Tests: `dotnet test tests/DotNetForge.Tests`, `dotnet test tests/DotNetForge.IntegrationTests`.
- Migrations: `dotnet ef migrations add <Name> --project src/DotNetForge.Data --startup-project src/DotNetForge.Data`
  (the `dotnet-ef` tool is pinned in `.config/dotnet-tools.json`; run `dotnet tool restore` first).

## Layering rules

- `Abstractions` has no dependencies and references no entities (primitive contracts only).
- `Core` and `Data` are siblings; neither references the other. Logic needing both is composed in
  `Web`, or split via a `Shared` interface implemented in `Data`.
- New behavior: interface in `Abstractions` (or `Shared` if it touches entities), implementation in
  `Infrastructure`/`Data`, registered in `Startup/DependencyRegistration.cs`.

## Conventions

- File-scoped namespaces, nullable enabled, 4-space indent (`.editorconfig`).
- Central package versions in `Directory.Packages.props`; shared props in `Directory.Build.props`.
- Validate all input server-side; never log secrets; store passwords/tokens only as hashes.
- Add a unit test for new pure logic and an integration test for new HTTP behavior.

## Definition of done

Match the **Acceptance Criteria** checklist in the relevant `dotnetforge_prompt/*.md` module and keep
both test projects green.
