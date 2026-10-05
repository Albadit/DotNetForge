# Internationalization (i18n)

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

DotNetForge CMS must support multiple locales for both the admin area and the public website. This module defines how locales are configured, which locale is the default, how content is localized, and the rules that keep exactly one default locale valid at all times.

## Purpose

The Internationalization (i18n) module lets administrators define the set of languages/locales available to a DotNetForge CMS installation, choose the default locale, enable or disable individual locales, and localize content where possible. It governs:

- The **Admin language** used in the admin UI.
- The **Website/page language** presented to public visitors.
- The **Default locale** that acts as the fallback for any missing translation.
- The set of **Additional locales** that can be enabled for translation.

Internationalization settings live under **Settings → Global Settings → Internationalization** in the [Admin Area](admin_area.md). Locale scoping is tenant-aware: each tenant has its own Default locale, as defined in [Multi-Tenancy](multi_tenancy.md). See [Settings](settings.md) for the placement of this page within Global Settings.

## Main Features

The system must allow administrators to configure:

- **Admin language** - the language used to render the admin UI. Defaults to the installation's default locale and may be overridden per user where supported.
- **Website/page language** - the language presented to public-facing visitors, resolved from the active locale (default locale unless a more specific locale is selected/requested).
- **Default locale** - the single fallback locale. Exactly one is required at all times.
- **Additional locales** - any number of further locales that can be enabled for translation of content.

Each locale is described by the fields below.

### Data Model / Fields

Locale fields:

| Field | Type | Required | Description |
|---|---|---|---|
| Locale code | string (BCP-47) | Yes | The canonical locale identifier, e.g. `en`, `en-US`, `de-DE`, `pt-BR`. Must be a valid BCP-47 tag and unique within the installation/tenant. |
| Locale display name | string | Yes | Human-readable name shown in the admin UI, e.g. "English (United States)". |
| Is default locale | boolean (`true` / `false`) | Yes | Marks this locale as the single default/fallback locale. Exactly one locale must have `true`. |
| Enabled | boolean (`true` / `false`) | Yes | Whether the locale is active and available for selection and translation. The default locale must always be enabled. |

Content supports localization where possible. Localizable content (for example, [Content Manager](content_manager.md) page fields such as Title, Meta title, Meta description, and body modules) may carry a translated value per enabled locale, falling back to the default locale when a translation is missing.

## User Flows

### Flow: Add a locale

1. The user opens **Settings → Global Settings → Internationalization**.
2. The user selects **Add locale**.
3. The user enters a **Locale code** (BCP-47, e.g. `fr-FR`) and a **Locale display name**.
4. The user chooses whether the locale is **Enabled**.
5. The system validates the locale code (valid BCP-47 format and unique within scope).
6. On success, the system creates the locale with `Is default locale = false` and persists it. The new locale is now available for content translation if enabled.

### Flow: Set the default locale

1. The user opens the Internationalization page and selects an existing locale.
2. The user chooses **Set as default**.
3. Because changing the default locale affects fallback behavior site-wide, the system prompts a **confirmation dialog** explaining the impact (existing untranslated content will fall back to the new default).
4. The user confirms.
5. The system, in a single atomic transaction, sets `Is default locale = true` on the selected locale and `false` on the previously-default locale, ensuring exactly one default remains.
6. If the newly-default locale was disabled, the system enables it automatically (the default locale must always be enabled).
7. The change is recorded in the [Audit Logs](audit_logs.md).

### Flow: Enable or disable a locale

1. The user opens the Internationalization page and selects a locale.
2. The user toggles **Enabled**.
3. If disabling, the system checks whether the locale is the default (blocked - see Edge Cases) and whether it is in active use; if in active use, the system warns before proceeding.
4. On confirmation, the system updates the `Enabled` flag.
5. Disabled locales are removed from public/admin locale selection but their existing translated content is retained.

### Flow: Translate content

1. The user opens a localizable entity in the [Content Manager](content_manager.md) (or via the [API](api_tokens.md) in headless/hybrid mode).
2. The user selects the target locale from the list of enabled locales.
3. The user enters localized values for the entity's localizable fields.
4. The system saves the translation scoped to the chosen locale and records the change in [Content History](content_manager.md).
5. Fields left untranslated fall back to the default locale value at render time.

## Role & Permission Rules

Locale management is part of the **Settings** permission area (see [User Roles & Permissions](user_roles_permissions.md)). Content translation is governed by the content/collection permissions of the entity being translated.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|---|---|---|---|---|---|---|
| Add / edit / delete locales | Yes | Yes | No | No | No | No |
| Set the default locale | Yes | Yes | No | No | No | No |
| Enable / disable a locale | Yes | Yes | No | No | No | No |
| Configure Admin / Website language | Yes | Yes | No | No | No | No |
| Translate content (localize entries) | Yes | Yes | Yes | Yes (own content only) | No | No |
| View localized public content | Yes | Yes | Yes | Yes | Yes | Yes |

Notes:

- Managing locales requires the **Settings** permission; only **Super Admin** and **Admin** hold it by default.
- **Author** may translate only content they created, consistent with the canonical Author rule ("Authors can manage the content they have created").
- In multi-tenant deployments, a **Super Admin** manages locales across all tenants, while a tenant-scoped **Admin** manages locales only within assigned tenants. See [Multi-Tenancy](multi_tenancy.md).

## Validation Rules

- **Locale code format** - must be a valid BCP-47 language tag (e.g. `en`, `en-US`, `pt-BR`, `zh-Hans`). Malformed codes are rejected.
- **Unique locale code** - locale codes must be unique within the installation (and within a tenant in multi-tenant mode). Duplicate codes are rejected.
- **Locale display name** - required and non-empty.
- **One-and-only-one default** - exactly one locale must have `Is default locale = true` at all times. The system must never persist a state with zero or with more than one default locale.
- **Default locale must be enabled** - a locale marked as default must have `Enabled = true`; setting a disabled locale as default automatically enables it.
- **At least one locale required** - the installation must always have at least one locale (the default). Removing the last locale is forbidden.
- **Confirmation required for default change** - changing the default locale must require explicit user confirmation before it is applied.
- **Atomic default switch** - promoting a new default and demoting the old default must occur together so the one-and-only-one invariant is never violated mid-operation.

## Edge Cases

- **Deleting the default locale** - blocked. The system must prevent deletion of the locale currently marked as default. The user must first reassign the default to another enabled locale (force-reassign), then delete the former default.
- **Deleting a locale that has content** - if a locale has localized content, the system must warn the user before deletion and require explicit confirmation. On confirmation, the locale's translations are removed; affected content falls back to the default locale. Deletion must not orphan or corrupt the default-locale values.
- **Missing translation fallback** - when a localizable field has no value for the requested locale, the system must fall back to the default locale's value rather than rendering empty content. If the default itself is empty, the field renders empty (no error).
- **Disabling a locale in active use** - if a locale is referenced by published content, the active admin/website language selection, or a tenant's Default locale, the system must warn before disabling. Disabling hides the locale from selection but retains its stored translations so re-enabling restores them. The default locale cannot be disabled.
- **Setting a disabled locale as default** - the system auto-enables the locale as part of the default switch (the default must always be enabled).
- **Duplicate or malformed code on add/edit** - rejected at validation with a clear message; no partial record is created.
- **Concurrent default changes (race condition)** - two simultaneous "set default" operations must not produce two defaults. The default switch must be transactional/serialized so the last committed write wins and exactly one default remains.
- **Tenant default locale** - each tenant carries its own `Default locale` field (see [Multi-Tenancy](multi_tenancy.md)). Disabling or deleting a locale that is a tenant's default must be blocked until that tenant's default is reassigned. The one-and-only-one rule applies per tenant scope.

## Acceptance Criteria

- [ ] An administrator can configure Admin language, Website/page language, Default locale, and Additional locales from **Settings → Global Settings → Internationalization**.
- [ ] Each locale exposes Locale code, Locale display name, Is default locale, and Enabled.
- [ ] Adding a locale validates the Locale code as BCP-47 and rejects malformed codes.
- [ ] Locale codes are enforced as unique within the installation (and per tenant in multi-tenant mode).
- [ ] The system always maintains exactly one default locale; zero or multiple defaults can never be persisted.
- [ ] Changing the default locale displays a confirmation dialog and applies only after confirmation.
- [ ] Promoting a new default and demoting the old default happen atomically.
- [ ] Setting a disabled locale as default automatically enables it.
- [ ] Deleting the current default locale is blocked until the default is reassigned to another enabled locale.
- [ ] Deleting a locale that has localized content warns the user and requires confirmation; remaining content falls back to the default locale.
- [ ] A missing translation for a localizable field falls back to the default locale value at render time.
- [ ] Disabling a locale that is in active use warns the user; the default locale cannot be disabled.
- [ ] Only Super Admin and Admin (Settings permission) can manage locales; Editors/Authors can translate content per their content permissions.
- [ ] Each tenant has an independent Default locale, and the one-and-only-one default rule is enforced per tenant.
- [ ] Concurrent attempts to change the default locale never result in two default locales.
- [ ] Locale and default-locale changes are recorded in the Audit Logs.
