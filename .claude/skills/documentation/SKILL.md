---
name: documentation
description: >-
  Writes and maintains DotNetForge CMS documentation in .docs/ (architecture, data flow, screen docs in pages/,
  feature docs in features/, guides, glossary, implementation status), the root README/ARCHITECTURE/CLAUDE.md and
  .github/agents quick references, code comments and Mermaid diagrams - all verified against the code. Use when asked
  to document, write or update docs or a README, reorganize .docs, explain the architecture or data flow, draw
  diagrams, prepare a handover, or when a code change affects documented behaviour, screens, routes, permissions,
  configuration, the API, the database, setup or deployment.
---

# Documentation

Documentation is part of the implementation, not a final chore. It has two readers, kept in separate documents:
**users**, who must install, use and troubleshoot the application from the user guide without asking anyone; and
**developers** new to the project, who must run, understand, debug, extend and deploy it without the original
developer.

## This project's documentation

Entry point: `.docs/README.md`. The layout is fixed - put new material where it belongs instead of inventing files:

| Folder / file | Holds | One document per |
| --- | --- | --- |
| `.docs/architecture/` | `codebase.md`, `data-flow.md`, `pages.md` (route map), `database.md`, `dependencies.md` | architectural view |
| `.docs/features/` | cross-screen concepts (installation, authentication, authorization, content pages and routing, headless API, extensions, audit logging, ...) - the **authoritative** place for rules | concept |
| `.docs/pages/` | one file per screen (UI view), in the fixed section template of [screen-template.md](reference/screen-template.md) | screen |
| `.docs/guides/` | how-tos: development, testing, extension development | task |
| `.docs/glossary.md` | fixed terminology - "screen" = UI view, "content page"/`Page` = entity, "live" vs `Published`, "extension" vs `plugin` | - |
| `.docs/implementation-status.md` | built vs. planned per module (links to each Planned section) + registered-but-unused code | - |
| `.docs/product.md` | product goals, the three modes, target users, MVP scope | - |
| `/README.md`, `/ARCHITECTURE.md`, `/CLAUDE.md`, `/.github/agents/*.md` | short overviews that link into `.docs/` - keep them short, don't duplicate | - |

Planned work has no separate folder: every document that owns a topic ends with a `## Planned (not implemented)`
section (Requirements, User flows, Rules and validation, Edge cases, Acceptance criteria with `- [x]` only for
criteria verified in code). Never describe planned behaviour outside that section, and never leave built behaviour
inside it. Code comments cite the owning doc by path (e.g. `(.docs/features/headless-api.md)`).

When code changes, update in the same change:

| Code change | Documents to update |
| --- | --- |
| New/changed admin screen or route | `pages/<screen>.md`, route map in `architecture/pages.md`, `.docs/README.md` screen table, sidebar section of `architecture/pages.md` |
| Rule in `PageService` / `HomeController.Live` / `RenderPage` | `features/content-pages-and-routing.md` (and the screen docs only if what the user sees changes) |
| New API endpoint or permission key | `features/headless-api.md` endpoint table, `features/authorization.md` key list |
| `[Authorize]` / policy change | `features/authorization.md` matrix, `architecture/pages.md` route map auth column |
| Entity, index, migration, seed | `architecture/database.md` |
| DI registration, project reference, package | `architecture/dependencies.md` |
| `.env` key | `features/configuration.md`, `.env.example`, `guides/deployment.md#environment-variables` |
| Storage provider, upload rules, `IFileStorage` | `features/media-storage.md`, `pages/media.md` |
| Anything that writes files, Dockerfile, proxy/TLS, runtime requirements | `guides/deployment.md` |
| New audit action written | `features/audit-logging.md` |
| Something from a **Planned (not implemented)** section built | move it into the document body (verified), tick/remove its acceptance criteria, update `implementation-status.md` (+ "Registered but unused") |

## Workflow

1. **Inspect the project** - structure, entry points, configuration, data stores, APIs, build, tests, deployment.
   Read the code the documentation will describe.
2. **Read the existing documentation** - README, docs/, changelog, comments - and list where it disagrees with the
   code. When they disagree, find out which is outdated; don't "fix" a doc to match a bug.
3. **Plan the structure** - what this project needs, where each topic lives, and what moves, merges or goes. See
   [doc-types.md](reference/doc-types.md) for the layout and what each document holds. Follow the project's
   existing layout and terminology where it works.
4. **Write or update** - one authoritative place per topic, linked from elsewhere; concrete commands, names and
   examples; tables for settings and endpoints.
5. **Add diagrams where a picture explains better** - [mermaid.md](reference/mermaid.md). Render-check them.
6. **Verify every fact against the code**: folder names, classes, routes, tables, configuration keys and defaults,
   commands, versions, behaviour. Never invent missing information - mark it unknown or ask.
7. **Remove what is outdated** - docs, sections, diagrams and links for things that no longer exist; fix every link
   to anything that moved.
8. **Final review** (below), then report what changed and what is still unknown.

## Rules

- **No filler** - no empty files, marketing language, vague statements, long introductions or duplicated text.
  Create only documents the project actually needs.
- **Never delete documentation the project has** to fit a layout - move its content, or keep it. Removing a document
  that is still true (a user guide, a runbook) needs the owner's say-so.
- **Single source of truth** - explain each thing once, link to it elsewhere.
- **One term per concept** across code, UI, API, database and docs.
- **README stays short** - what it is, why, quick start, structure in a sentence or diagram, links to the docs.
- **Explain why**, not only what, for important decisions (ADRs for decisions that are costly to reverse).
- **Never put real secrets in documentation** - placeholders only.
- **A wrong doc or diagram is worse than none** - update or delete it in the same change as the code.
- **Code comments** explain non-obvious decisions, workarounds, limitations and assumptions - never what the code
  plainly says. Match the surrounding comment style.
- **Keep the repository's file conventions** (encoding, line endings).

## Style

Clear headings, short paragraphs, concrete explanations, examples, tables and diagrams - written for developers new
to the project. Commands copy-pasteable. Paths and names exactly as in the code.

## Final review

- Can a user install, use and troubleshoot the application from the user guide alone?
- Can a new developer set up, run, test, build and deploy from the docs alone?
- Can they find where code for a feature lives, and where to add new code?
- Can someone debugging find the logs, settings and known problems?
- Does every name, command and diagram match the code today? Do all links work?
