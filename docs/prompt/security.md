# Security Requirements

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This document defines the cross-cutting security baseline for **DotNetForge CMS**. Security is a first-class, system-wide concern: the controls below apply across every mode (Traditional, Headless, and Hybrid) and are enforced in the modules that own the relevant detail, which are cross-linked throughout.

## Purpose

DotNetForge CMS must be designed with strong security from the start. This file consolidates the complete security baseline, groups each requirement by concern, and records **where** each control is enforced so a developer or AI coding agent can implement and verify it. Individual modules (authentication, roles, media, tokens, extensions, webhooks, audit logs) own the detailed behavior; this document is the authoritative checklist that binds them together.

Security must hold across all three operating modes:

- **Traditional CMS** - the visual admin UI.
- **Headless CMS** - everything managed via API with tokens and permissions.
- **Hybrid CMS** - the visual CMS and the API used at the same time.

## Authentication & Sessions

Controls that protect user credentials, login flows, and session state.

| Control | Requirement | Enforced where |
| --- | --- | --- |
| Secure password hashing | Store user passwords using a strong, salted, adaptive hashing algorithm. Never store or log plaintext passwords. | Authentication and user storage. See [Users](user_roles_permissions.md) and [Authentication Providers](authentication.md). |
| Account lockout | Lock accounts after repeated failed login attempts to defeat brute-force and credential-stuffing attacks. The `Account locked` email template notifies the affected user. | Login flow. See [Advanced User Settings](authentication.md), [Email Templates](email.md). |
| Rate limiting | Apply rate limiting to login and to all API endpoints to throttle abuse and automated attacks. | Login flow and API pipeline. See [API Tokens](api_tokens.md). |
| Secure cookies | Session/auth cookies must be `HttpOnly` and `SameSite`, and marked `Secure` in production. | Session/cookie configuration. |
| HTTPS | Support and enforce HTTPS for all authenticated traffic. | Hosting and middleware configuration (see [Network & Headers](#network--headers)). |

Notes:

- Failed and successful logins must be recorded as security events (see [Audit Logging](#audit-logging-as-a-security-control)).
- Cookie hardening applies to all session-bearing cookies issued by the CMS, including those set by configured authentication providers.

## Access Control

Controls that ensure every action is authorized for the acting identity (user session or API token).

- **Role-Based Access Control (RBAC).** The CMS must implement RBAC. The default roles, from most-privileged to least, are **Super Admin, Admin, Editor, Author, Authenticated, Public**.
- **Permission checks on every action.** A permission check must be enforced for **every** admin action and **every** API action. No admin or API operation may execute without verifying the caller's permissions.
- **Permission areas.** Authorization must cover all permission areas: Collection types, Single types, Plugins, Settings, Extensions, Media, Users, Roles, API, and Webhooks.

Enforcement and detail live in [User Roles & Permissions](user_roles_permissions.md) (role and permission model) and [API Tokens](api_tokens.md) (token-scoped permissions for API actions). For private content access, see also [Content Manager](content_manager.md) and [File Manager](file_manager.md).

## Data Protection

Controls that protect data integrity and prevent injection and cross-site attacks.

| Control | Requirement | Enforced where |
| --- | --- | --- |
| SQL injection protection | Prevent SQL injection by using parameterized queries and Entity Framework Core for all data access. Never build SQL from concatenated, unvalidated input. | Data layer (EF Core). See [Architecture](architecture.md). |
| Input validation | Validate input on **all** forms and **all** APIs. Reject malformed, out-of-range, or unexpected input before it reaches business logic or storage. | Every form (admin UI) and every API endpoint. See [Content Manager](content_manager.md), [API Tokens](api_tokens.md). |
| XSS protection | Protect against cross-site scripting by encoding output and sanitizing user-supplied content rendered in Razor views and API responses. | Razor view rendering and API serialization. |
| CSRF protection | Protect against cross-site request forgery on all state-changing admin form submissions. | Admin UI form handling. |

Notes:

- Input validation is the shared foundation for several other controls (file upload validation, route pattern validation, manifest validation); each adds domain-specific rules on top of the baseline.
- XSS protection applies to all content surfaces, including page content, content history diffs, and any extension- or widget-rendered output.

## File Uploads

Controls that prevent malicious or oversized uploads and protect non-public media.

- **Secure file upload validation.** All uploads must pass secure validation before being stored.
- **Configurable allowed file types.** The set of permitted file types must be configurable. Uploading every file type is risky; restrict it.
- **Configurable max upload size.** The maximum upload size must be configurable.
- **Private media access control.** Files marked private must be access-controlled and must not be served to unauthorized callers via generated URLs.

These controls are owned and detailed in the [File Manager](file_manager.md), including unsafe-upload scanning/blocking where possible and public/private file marking. Token-scoped media permissions for API uploads are defined in [API Tokens](api_tokens.md).

## Tokens & Secrets

Controls that protect API credentials and application secrets.

- **Secure API token generation.** API tokens must be generated using a cryptographically secure method.
- **Store tokens as hashes.** Tokens must be stored as hashes, never as recoverable plaintext. The full token value is shown to the user only once at creation time.
- **Environment variables for secrets.** All secrets (database connection strings, SMTP credentials, provider client secrets, signing keys) must be supplied via environment variables in the `.env` file.
- **Never commit secrets.** Secrets must never be committed to Git. The `.env` file and other secret-bearing artifacts must be excluded via `.gitignore`; only `.env.example` (with placeholder values) is committed.

Token lifecycle, fields (including revocation), and granular permission scoping are owned by [API Tokens](api_tokens.md). Environment configuration and the `.env` / `.env.example` convention are described in the [Architecture](architecture.md) and project setup documentation.

## Extensions

Controls that prevent untrusted or malformed extensions from compromising the CMS.

- **Manifest validation.** Every extension ships a manifest file named `dotnetforge.extension.json`. The CMS must validate the manifest before installing the extension. The required fields are:

  | Field | Type | Required | Description |
  | --- | --- | --- | --- |
  | `id` | string | Yes | Unique extension identifier. |
  | `name` | string | Yes | Human-readable extension name. |
  | `description` | string | Yes | What the extension does. |
  | `version` | string | Yes | Semantic version of the extension. |
  | `type` | string | Yes | Extension type (Theme, Authentication provider, Connector, Library, Admin extension, Widget, Provider, Plugin, Module). |
  | `author` | string | Yes | Extension author. |
  | `entryPoint` | string | Yes | The class/component the CMS loads to activate the extension. |
  | `permissions` | array | Yes | Permissions the extension requests. |

- **Prevent unsafe extension loading.** Invalid, unsafe, or incompatible extensions must not be installed or loaded. Manifest validation gates installation; the loader must refuse anything that fails validation.

Full manifest schema, the marketplace install flow, and the extension lifecycle are owned by the [Extension System](extensions.md) and the [Extension Manifest](extensions.md) specification. Extension-requested permissions are reconciled against the model in [User Roles & Permissions](user_roles_permissions.md).

> The source manifest list contained two errors - a duplicated `version` key and a misspelled `vrsion` field. The correct, single field is **`version`**, reflected in the table above.

## Network & Headers

Controls applied at the HTTP and transport layer.

- **Secure headers.** The application must send secure HTTP response headers.
- **HTTPS support.** The application must support HTTPS and enforce it in production.

These complement the cookie hardening in [Authentication & Sessions](#authentication--sessions) (`HttpOnly`, `SameSite`, `Secure` in production). Header and HTTPS configuration is part of the hosting/middleware setup described in the [Architecture](architecture.md).

## Audit Logging as a Security Control

Audit logging is a security control: it provides the tamper-evident record needed to detect and investigate abuse.

- The CMS must track important admin and system actions.
- Security-relevant events that must be captured include **user login**, **failed login**, role changes, permission changes, settings changes, API token creation, and webhook creation, among others.

The full audit log schema (User, Action, Entity type, Entity ID, IP address, User agent, Timestamp, Details) and the complete event list are owned by [Audit Logs](audit_logs.md).

## Webhook Signing as a Security Control

Webhook signing is a security control: it lets receivers verify that a delivery genuinely originated from DotNetForge CMS and was not forged or tampered with in transit.

- Outbound webhook requests must be **signed** so receivers can verify authenticity and integrity.
- Webhook deliveries are sent as POST requests, retried on failure, and logged.

Webhook configuration, supported events, headers, and delivery behavior are owned by [Webhooks](webhooks.md). The signing key is a secret and must be managed per [Tokens & Secrets](#tokens--secrets).

## Acceptance Criteria

- [ ] User passwords are stored using a strong, salted hashing algorithm; plaintext passwords are never stored or logged.
- [ ] Accounts are locked after a configurable number of repeated failed login attempts, and the user is notified via the `Account locked` email template.
- [ ] Rate limiting is enforced on the login endpoint and on all API endpoints.
- [ ] Session/auth cookies are set with `HttpOnly` and `SameSite`, and are marked `Secure` in production.
- [ ] HTTPS is supported and enforced for authenticated traffic in production.
- [ ] RBAC is implemented with the default roles Super Admin, Admin, Editor, Author, Authenticated, and Public.
- [ ] Every admin action and every API action performs a permission check before executing.
- [ ] All database access uses parameterized queries and EF Core; no SQL is built from concatenated input.
- [ ] Input is validated on all admin forms and all API endpoints; malformed input is rejected before reaching business logic.
- [ ] Output is encoded/sanitized to prevent XSS across page content, history diffs, and extension/widget output.
- [ ] CSRF protection is enforced on all state-changing admin form submissions.
- [ ] File uploads pass secure validation; allowed file types and max upload size are both configurable.
- [ ] Private media is access-controlled and is never served to unauthorized callers.
- [ ] API tokens are generated using a cryptographically secure method and stored only as hashes (plaintext shown once at creation).
- [ ] All secrets are loaded from environment variables (`.env`); `.env` is git-ignored and never committed, while `.env.example` ships with placeholders only.
- [ ] Extension manifests (`dotnetforge.extension.json`) are validated against the required fields (`id`, `name`, `description`, `version`, `type`, `author`, `entryPoint`, `permissions`) before installation.
- [ ] Invalid, unsafe, or incompatible extensions are refused at install and load time.
- [ ] Secure HTTP response headers are sent on all responses.
- [ ] Security-relevant events (including login and failed login) are written to the audit log with full context.
- [ ] Outbound webhook requests are signed so receivers can verify authenticity and integrity.
