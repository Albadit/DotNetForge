# DotNetForge CMS - Specification

> A modular, secure, lightweight **hybrid CMS** built on ASP.NET Core MVC (C#, EF Core, Razor) with SQLite and PostgreSQL support.

This folder is the complete application specification for **DotNetForge CMS**. The original single-file prompt has been restructured into focused, cross-linked module specs so a developer - or an AI coding agent - can implement the system one module at a time without losing the big picture.

Start with **[main.md](main.md)** for the product overview, then follow the [reading order](#reading-order) below.

---

## How to Use This Spec

- **Building a module?** Open its file. Every feature module is self-contained and documents its own *Purpose, Main Features, User Flows, Role & Permission Rules, Validation Rules, Edge Cases, and Acceptance Criteria*.
- **Need shared rules?** Cross-cutting concerns live in dedicated files and are linked wherever they apply: roles and the permission matrix in [user_roles_permissions.md](user_roles_permissions.md), the security baseline in [security.md](security.md), and the system layout in [architecture.md](architecture.md).
- **AI coding agents:** treat the **Acceptance Criteria** checklist at the bottom of each file as the definition of done for that module, and honor the **Validation Rules** and **Edge Cases** as hard requirements.

## Document Conventions

- **Canonical names** (roles, permission areas, extension types, the manifest format) are defined once in their owning file and referenced elsewhere - do not redefine them locally.
- **Cross-links** use relative paths (e.g. `[Security](security.md)`); a concern is documented in full in exactly one file and linked from the rest.
- **Acceptance Criteria** are written as testable `- [ ]` checklists.

---

## Module Map

### Overview & Foundations
| File | What it covers |
| --- | --- |
| [main.md](main.md) | Product goal, the three modes (Traditional / Headless / Hybrid), target users, tech stack, deliverables, and MVP scope. |
| [installation_setup.md](installation_setup.md) | `.env` configuration, database providers, install detection, and the first-run registration/setup wizard. |
| [admin_area.md](admin_area.md) | The admin shell, the full sidebar menu, and admin-vs-frontend separation. |
| [architecture.md](architecture.md) | Project structure, core/extension separation, layering, and the `ARCHITECTURE.md` blueprint. |

### Content, Routing & Presentation
| File | What it covers |
| --- | --- |
| [content_manager.md](content_manager.md) | Collection/Single types, the page tree, page builder, drafts, publishing, content history, review workflow, tags, and SEO. |
| [dynamic_routes.md](dynamic_routes.md) | Bracket-style dynamic routing, route fields, conflict prevention, and extension route handlers. |
| [multi_tenancy.md](multi_tenancy.md) | Tenant resolution, per-tenant scoping and isolation, tenant admins, and tenant-aware admin/API context. |
| [file_manager.md](file_manager.md) | The File Manager: folders, uploads, downloads, public/private access, search, and image processing. |
| [themes.md](themes.md) | The public theme system: layouts, templates, manifest, preview, and per-page theme selection. |

### Platform & Integration
| File | What it covers |
| --- | --- |
| [extensions.md](extensions.md) | Extension system, types, manifest format, marketplace, plugins page, and validated loading. |
| [api_tokens.md](api_tokens.md) | API token creation, scoped permissions, hashed storage, and the headless API surface. |
| [webhooks.md](webhooks.md) | Webhook events, headers, request signing, retries, and delivery logging. |
| [internationalization.md](internationalization.md) | Locales, default-locale rules, and content localization. |
| [email.md](email.md) | SMTP configuration, test email, and the default email templates. |
| [settings.md](settings.md) | The Overview/Global Settings hub and logo uploads. |
| [transfer_updates.md](transfer_updates.md) | Database import/export, plus core updates, rollback, and backups. |

### Access, Security & Operations
| File | What it covers |
| --- | --- |
| [user_roles_permissions.md](user_roles_permissions.md) | RBAC, default roles, the **permission matrix**, and user management. |
| [authentication.md](authentication.md) | Authentication providers, sign-up rules, and email-confirmation settings. |
| [security.md](security.md) | The cross-cutting security baseline (hashing, CSRF/XSS, RBAC checks, rate limiting, secure headers, and more). |
| [audit_logs.md](audit_logs.md) | Audit log fields and the tracked admin/system actions. |

### Engineering
| File | What it covers |
| --- | --- |
| [developer_docs.md](developer_docs.md) | Setup guides, extension/theme/module authoring, and the `/agents` AI-agent docs folder. |
| [testing_quality.md](testing_quality.md) | Unit/integration testing requirements, linting, and code-quality standards. |

---

## Reading Order

1. [main.md](main.md) - understand the product and its three modes.
2. [architecture.md](architecture.md) - understand the project layout and how the pieces fit.
3. [installation_setup.md](installation_setup.md) → [admin_area.md](admin_area.md) → [user_roles_permissions.md](user_roles_permissions.md) - get a running, authenticated admin.
4. [content_manager.md](content_manager.md), [file_manager.md](file_manager.md), [dynamic_routes.md](dynamic_routes.md), [themes.md](themes.md) - the content experience.
5. [extensions.md](extensions.md), [api_tokens.md](api_tokens.md), [webhooks.md](webhooks.md), [multi_tenancy.md](multi_tenancy.md) - the platform capabilities.
6. [security.md](security.md), [audit_logs.md](audit_logs.md), [testing_quality.md](testing_quality.md) - harden and verify.

---

## Module Spec Template

Every feature module in this folder follows the same structure so it is predictable to read and implement:

1. **Purpose** - why the module exists.
2. **Main Features** - what it does.
3. **User Flows** - step-by-step journeys for the key actions.
4. **Role & Permission Rules** - who may do what (against the canonical roles).
5. **Validation Rules** - input constraints and business rules.
6. **Edge Cases** - failure modes, conflicts, and how the system behaves.
7. **Acceptance Criteria** - a testable checklist defining "done".

Reference documents ([architecture.md](architecture.md), [security.md](security.md), [developer_docs.md](developer_docs.md), [testing_quality.md](testing_quality.md)) adapt this structure to their content but still close with acceptance criteria.
