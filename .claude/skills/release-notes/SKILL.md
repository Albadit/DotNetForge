---
name: release-notes
description: >-
  Writes the release notes for a DotNetForge CMS release as .docs/release-notes/vX.Y.Z.md (bold summary, Highlights,
  Other changes, Upgrading, Tested, link to the changelog) and the matching CHANGELOG.md entry, from the commits since
  the previous tag and real test results - every claim checked against the code - plus a one-line release commit
  message ready to paste. Use when asked to write, draft, update or review release notes or a changelog entry, or to
  prepare "the next release".
---

# Release notes

DotNetForge has **no release process yet**: no git tags, no `CHANGELOG.md`, no version property in any `.csproj`
(the only version is `SystemState.CmsVersion`, seeded as `"1.0.0"`, shown on the Dashboard). This skill establishes
the convention on first use and follows it afterwards:

- notes: **`.docs/release-notes/vX.Y.Z.md`** (create the folder on the first release and link it from
  `.docs/README.md`);
- changelog: **`CHANGELOG.md`** at the repository root, newest first, `## vX.Y.Z` headings without dates, an
  `## Unreleased` section while work is in progress;
- tag: `vX.Y.Z` - created by the owner, never by you.

Readers are **people who run a DotNetForge site or build on it** (operators, integrators, extension authors), deciding
whether to update and what changes for them.

## Workflow

1. **Find the version and the range.** Use the version you were given; if none, ask. Previous release:
   `git describe --tags --abbrev=0` - if there is no tag yet, the range is the whole history (say so in the report).
2. **Collect what changed:**
   - `CHANGELOG.md` `## Unreleased` entry if it exists;
   - `git log --no-merges --format="%h %s%n%b" <previous tag>..HEAD` (commit prefixes here are `feat:`, `fix:`,
     `add:`, `update:`);
   - `git diff --stat <previous tag>..HEAD`, then the files, wherever an entry is unclear.
3. **Verify every claim against the code**: screen labels exactly as in the Razor views (`ViewData["Title"]`, button
   text), routes, `.env` keys and defaults, permission keys, API endpoints, migration names. Use `.docs/` as the map
   (it is verified against the code) but re-check anything you quote. Drop what you can't confirm.
4. **Find upgrade impact** - these need an *Upgrading* bullet:
   - new migrations in `src/DotNetForge.Data/Database/Providers/{Sqlite,PostgreSql,SqlServer,MySql}/Migrations/` (each
     provider applies its set on start; mention long-running or data-changing migrations and back-up advice) and
     index changes for MongoDB (`EnsureCreated` never changes existing indexes);
   - new runtime requirements (environment variables, writable volumes, object storage) - the deployment directory is
     read-only ([deployment](../../../.docs/guides/deployment.md));
   - new/changed `.env` keys (`EnvConfigurationLoader`, `.env.example`);
   - changed routes, removed screens, changed permissions or roles, new API permission keys (existing tokens don't
     gain them);
   - changed extension manifest rules (`ManifestValidator`) - existing extensions may become invalid.
5. **Get real test results:**
   `dotnet test tests/DotNetForge.Tests` and `dotnet test tests/DotNetForge.IntegrationTests` - read the counts and
   compare with the previous notes' *Tested* section. Add manual checks only if they were really done (ask).
6. **Write `.docs/release-notes/vX.Y.Z.md`** (structure below) and the `## vX.Y.Z` entry in `CHANGELOG.md`
   (rename `## Unreleased` if present).
7. **Check**: every relative link resolves; the changelog anchor exists; no class names or commit hashes in the
   notes.
8. **Report**: what the notes say, the commit message, anything left unverified. Don't commit, tag or publish.

## Structure

```markdown
# DotNetForge CMS X.Y.Z

**The release in a few words.** One or two sentences on what it does for the reader.

## Highlights

### A theme, named by what the reader gets
- What they can do now, and where (**Screen label** in bold, `route` or `setting` in code).

## Other changes
- Smaller changes, one line each.
- Fixed: a bug, described by what the user saw.

## Upgrading
Stop the site, update the files, keep your `.env`, start it again - SQLite databases are migrated on start.
- Anything the operator must do once, or that now works differently.

## Tested
- All N unit tests and M integration tests pass (up from ...).
- What was checked by hand, against what.

Full list of changes: [CHANGELOG.md](../../CHANGELOG.md#vxyz)
```

| Part | Rules |
| --- | --- |
| **Title** | `# DotNetForge CMS X.Y.Z`, then the summary - no date. |
| **Summary** | Bold phrase of 3-7 words, then 1-2 plain sentences. No "This release…". |
| **Highlights** | 2-6 `###` themes by what the reader gets, most important first. |
| **Other changes** | One line each; fixes last as `Fixed: …`. |
| **Upgrading** | Always present. Migration and new-configuration notes whenever there are any. |
| **Tested** | Only real results; omit the section rather than guess. |
| **Changelog link** | Always the last line. Anchor: heading lower-cased without dots (`## v1.1.0` → `#v110`). |

## Style

- "You", present tense; screen labels exactly as on screen in **bold**; files, routes, keys and commands in `code`.
- Concrete: "**API Tokens** can now be renamed", not "improved token management".
- No class names, commit hashes, internal refactors or dependency bumps unless they change what readers see
  (security fixes in dependencies do count - say what was fixed).
- No emoji, no marketing words. UTF-8, LF line endings (`.editorconfig`).

## Commit message

One line, no body:

```text
release: vX.Y.Z - <the Highlights' themes, lower case, comma-separated>
```

Give it in a `text` block in the report.
