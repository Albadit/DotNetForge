# Content Manager

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The Content Manager is the central authoring surface of DotNetForge CMS. It owns both (a) **structured content** modeled as Collection Types and Single Types, and (b) the **page tree** of public-facing pages, together with per-page metadata, SEO, permissions, theming, the page builder, content history, and the editorial review workflow.

---

## Purpose

The Content Manager lets authorized users create, organize, edit, version, review, and publish all content in a tenant. It must handle two complementary content shapes:

1. **Structured content** - reusable, schema-driven entries grouped into **Collection Types** (many entries of the same shape, e.g. News) and **Single Types** (exactly one entry, e.g. a Homepage hero or global footer). Admins can create their own Collection Types and Single Types in addition to the built-in starter types.
2. **The page tree** - a hierarchical structure of public pages, each with a slug, SEO metadata, permissions, theme/layout settings, and a visual page builder.

All content is **tenant-scoped**: the tenant is resolved first (by domain, subdomain, or path prefix), and the Content Manager only ever shows and edits content belonging to the active tenant. See [Multi-Tenant Routing](multi_tenancy.md) for tenant resolution and isolation. The Content Manager never affects the admin area's own appearance - public themes apply only to rendered frontend pages.

---

## Main Features

### Structured content (Collection Types & Single Types)

- Browse, search, and filter entries within each Collection Type.
- Create, edit, duplicate, delete, and reorder entries.
- Edit the single entry of each Single Type.
- Draft / published states, scheduling, content history, and review for entries.
- Create custom Collection Types and Single Types with arbitrary field sets (admins).

#### Built-in / starter Collection Types

The CMS ships with the following starter Collection Types. Each is fully editable, and admins may add, remove, or extend fields. Admins can also create entirely new Collection Types and Single Types.

| Collection Type | Purpose | Example fields |
|---|---|---|
| **News** | News / blog articles | `title`, `slug`, `body`, `tags`, `cover image`, `publish date` |
| **Events** | Scheduled events | `title`, `location`, `start datetime`, `end datetime`, `capacity` |
| **Surveys** | Polls and questionnaires | `title`, `questions[]`, `open date`, `close date`, `responses` |
| **Warnings** | Site notices / alerts | `title`, `severity`, `message`, `start`, `end`, `dismissible` |

> Field sets above are sensible defaults; the authoritative schema for a tenant is whatever its admins have configured. Media-typed fields (e.g. News `cover image`) reference assets in the [File Manager](file_manager.md). Tag-typed fields use the shared tag vocabulary described under [Tags](#tags-taxonomy). Permissions over Collection Types and Single Types are governed by the canonical permission areas in [User Roles & Permissions](user_roles_permissions.md).

### Page tree

- Display all pages of the active tenant as a navigable **tree structure**.
- Select a page to edit its details, metadata, permissions, theme, and content.
- Create child pages, move pages (re-parent), and reorder siblings via Sort order.
- Manage page types, SEO, scheduling, and publish state.
- Build page content visually with the page builder.
- View content history and restore previous versions.
- Submit pages and entries through the editorial review workflow before publishing.

### Tags (taxonomy)

- A tenant-scoped tag vocabulary may be applied to pages and to tag-typed fields on entries (e.g. News `tags`).
- Tags support create-on-type, autocomplete, rename, merge, and delete.
- Tags drive filtering in the Content Manager and can power dynamic listing routes - see [Dynamic Routes](dynamic_routes.md).
- Renaming a tag updates all references; deleting a tag removes it from all content but does not delete the content.

### SEO

Every page (and every dynamically resolved page) carries SEO metadata: Meta title, Meta description, SEO keywords, and Canonical URL. SEO templates for dynamically generated pages are owned by [Dynamic Routes](dynamic_routes.md); this module owns SEO for concrete pages in the tree.

### Content history & review

- **Content history**: every change to a page or entry is versioned. Users can view history, compare versions, restore a previous version, and see who changed what and when.
- **Review workflow**: drafts can be submitted for approval, assigned to reviewers, approved/published, rejected with comments, or returned with revision requests; a full review history is kept.

---

## Data Model / Fields

### Page types

| Type | Description |
|---|---|
| **Standard** | A normal CMS page built with the page builder. |
| **Existing page** | A pointer/alias to another existing page in the tree. |
| **URL redirect** | Redirects the visitor to a `Target URL`. |
| **File** | Serves or links a file from the [File Manager](file_manager.md) via `File reference`. |

### Page fields

| Field | Type | Required | Description |
|---|---|---|---|
| `slug` (URL name) | string | Yes | URL segment for the page. Must be unique within the tenant under its parent. |
| `Title` | string | Yes | Human-readable page title. |
| `Meta title` | string | No* | SEO `<title>`; falls back to `Title` if empty. Required when SEO is enforced (see Validation). |
| `Meta description` | string | No* | SEO meta description. Required when SEO is enforced. |
| `SEO keywords` | string[] / csv | No | SEO keywords. |
| `Canonical URL` | string (URL) | No | Canonical link for duplicate-content control. |
| `Display in menu` | bool | Yes (default `false`) | Whether the page appears in generated menus. |
| `Published` | bool | Yes (default `false`) | Whether the page is live. |
| `Disabled` | bool | Yes (default `false`) | When `true`, the page is inactive regardless of `Published`. |
| `Scheduled publish date` | datetime | No | Auto-publish time (tenant timezone / UTC stored). |
| `Scheduled unpublish date` | datetime | No | Auto-unpublish time. Must be after `Scheduled publish date`. |
| `Parent page` | reference | No | Parent in the tree; empty = root-level page. Must not create a cycle. |
| `Sort order` | int | Yes (default `0`) | Order among siblings. |
| `Page type` | enum | Yes | One of Standard, Existing page, URL redirect, File. |
| `Target URL` | string (URL) | Conditional | Required when `Page type` = URL redirect. |
| `File reference` | reference | Conditional | Required when `Page type` = File; references a File Manager asset. |

\* "No\*" fields are optional by default but become required when the tenant enables enforced SEO (see [Validation Rules](#validation-rules)).

### Per-page permissions

- **View roles**: assign one or more of the canonical roles (Super Admin, Admin, Editor, Author, Authenticated, Public) that may view the page.
- **View users**: assign specific users that may view the page.
- **Public vs private**: a page is **public** when the `Public` role is granted view access; otherwise it is **private** and visible only to the assigned roles/users.
- Authoring permissions (who may create/edit/publish) are governed centrally by the permission areas (Collection types, Single types, etc.) in [User Roles & Permissions](user_roles_permissions.md); enforcement details live in [Security](security.md).

### Theme settings (per page)

- **Theme**: select the public theme used to render this page.
- **Layout**: select a layout provided by the theme.
- **Page-specific layout settings**: override layout options for this page only.
- The admin area is never affected by these settings - see [Themes](themes.md).

### Page builder

- Place **built-in modules** or **custom (extension) modules** onto a page.
- Includes a **built-in Razor module** for raw Razor/markup content.
- Supports **extension modules** registered through the [Extension System](extensions.md) (extension types include Module and Widget).
- **Preview mode**: render the page exactly as a visitor would see it before publishing.
- **Draft vs published versions**: edits accumulate in a draft; publishing promotes the draft to the live version.
- **Content history & rollback**: every saved version is tracked and restorable (see below).

### Content history record

| Field | Description |
|---|---|
| `Version` | Incrementing version number for the content item. |
| `Content type` | Page, Collection Type entry, or Single Type. |
| `Content id` | Identifier of the versioned item (tenant-scoped). |
| `Author` | User who made the change (who). |
| `Timestamp` | When the change was made (when). |
| `Change summary` | Optional note / diff metadata. |
| `Snapshot` | Serialized content state used for compare and restore. |

### Review record

| Field | Description |
|---|---|
| `Review id` | Identifier of the review. |
| `Content ref` | The page/entry version under review. |
| `Status` | Draft → In review → Approved/Published, or Rejected, or Revision requested. |
| `Reviewer(s)` | Assigned reviewer user(s). |
| `Submitted by` / `Submitted at` | Author and submission time. |
| `Decision by` / `Decision at` | Reviewer who decided and when. |
| `Comments` | Rejection reasons / revision request notes. |
| `History[]` | Ordered log of all review actions. |

---

## User Flows

### Flow: Create a structured content entry (Collection Type)

1. Open **Content Manager** and select a Collection Type (e.g. **News**).
2. Click **Create entry**; the editor renders the type's configured fields.
3. Fill required fields (e.g. `title`, `slug`, `body`), attach media (e.g. `cover image`), and add `tags`.
4. Save as **draft**. The system validates fields and records a content-history version.
5. Optionally **Submit for review** (see review flow) or, if permitted, **Publish** directly.

### Flow: Edit the Single Type entry

1. Open **Content Manager** and select a Single Type.
2. Edit its single entry's fields.
3. Save as draft; on publish, the live single entry is updated and a history version is recorded.

### Flow: Create a page in the page tree

1. Open **Content Manager**; the page tree for the active tenant is shown.
2. Choose **Add page** (optionally selecting a parent node to nest under).
3. Select the **Page type** (Standard, Existing page, URL redirect, or File).
4. Enter `slug`, `Title`, and SEO fields; the system validates slug format and uniqueness within the tenant.
5. Configure `Display in menu`, `Sort order`, scheduling, theme/layout, and per-page permissions.
6. For URL redirect, provide `Target URL`; for File, provide `File reference`.
7. Save as **draft**.

### Flow: Build and preview page content

1. With a Standard page open, switch to the **Page builder**.
2. Place built-in modules, the Razor module, or extension modules onto the page; configure each module.
3. Click **Preview** to render the draft as a visitor would see it.
4. Continue editing; each save creates a draft version (content history).

### Flow: Schedule publish / unpublish

1. Open the page (or entry) and set `Scheduled publish date` and/or `Scheduled unpublish date`.
2. The system validates that unpublish is after publish and that there are no conflicting schedules.
3. At the scheduled publish time the system sets `Published = true`; at the unpublish time it sets `Published = false`.

### Flow: View history, compare, and restore

1. Open a page or entry and choose **History**.
2. The list shows every version with **who** changed it and **when**.
3. Select two versions to **Compare** (side-by-side diff).
4. Choose **Restore** on a prior version; the system creates a new version equal to the restored snapshot (the timeline is preserved, never overwritten).

### Flow: Submit content for review

1. With a draft open, click **Submit for review**.
2. Assign one or more **reviewers** (or leave to the default reviewer pool, per configuration).
3. Status becomes **In review**; reviewers are notified. The submission is logged in review history.

### Flow: Review and decide (reviewer)

1. Open **Review Content**; see items assigned to you with their drafts and diffs.
2. Choose one of:
   - **Approve & publish** - promotes the draft to live and records the decision.
   - **Reject with comments** - returns the item to the author with required comment(s); status becomes **Rejected**.
   - **Request revision** - returns the item with notes; status becomes **Revision requested**.
3. Every decision is appended to the **review history** with reviewer, timestamp, and comments.

### Flow: Delete a page that has children

1. Choose **Delete** on a parent page.
2. The system detects child pages and prompts the user to either:
   - **Re-parent** children to the deleted page's parent (or root), or
   - **Cascade delete** the entire subtree (explicit confirmation required).
3. The chosen action is applied atomically and logged to [Audit Logs](audit_logs.md).

---

## Role & Permission Rules

Authoring access is enforced through the canonical permission areas (**Collection types**, **Single types**, etc.) defined in [User Roles & Permissions](user_roles_permissions.md). The matrix below describes typical default behavior; the authoritative grant for any tenant is its role configuration.

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|---|---|---|---|---|---|---|
| Create Collection/Single Types (schemas) | Yes | Yes | No | No | No | No |
| Create/edit any entry or page | Yes | Yes | Yes | Own only | No | No |
| Create/edit own content | Yes | Yes | Yes | Yes | No | No |
| Submit content for review | Yes | Yes | Yes | Yes | No | No |
| Assign reviewers | Yes | Yes | Yes | No | No | No |
| Approve / publish via review | Yes | Yes | Yes | No | No | No |
| Reject / request revision | Yes | Yes | Yes | No | No | No |
| Publish directly (bypassing review) | Yes | Yes | Configurable | No | No | No |
| Delete pages / entries | Yes | Yes | Configurable | Own only | No | No |
| View content history | Yes | Yes | Yes | Own only | No | No |
| Restore previous version | Yes | Yes | Yes | No | No | No |
| Manage tags vocabulary | Yes | Yes | Yes | No | No | No |
| View a private page | Granted roles/users only | - | - | - | - | No |
| View a public page | - | - | - | - | - | Yes |

> "Author - Own only": Authors can manage only content they created. Whether Editors may publish directly or delete is a configurable per-tenant permission.

---

## Validation Rules

### Slug

- `slug` is **required** for pages and for slug-bearing entries.
- Format: lowercase, URL-safe (letters, digits, hyphens; no spaces or reserved URL characters). The system slugifies and validates on save.
- **Uniqueness within the tenant**: a slug must be unique within its parent scope for the active tenant. Cross-tenant duplicates are allowed because tenants are isolated.
- A page slug must **not collide with a dynamic route** that would resolve to the same path within the tenant - see [Dynamic Routes](dynamic_routes.md) for resolution order and conflict prevention.

### Scheduling

- `Scheduled unpublish date` **must be strictly after** `Scheduled publish date` when both are set.
- Schedules must be in the future at save time (a publish date in the past is rejected or applied immediately, per configuration).
- The system rejects schedules that conflict with an existing schedule on the same item (e.g. overlapping/contradictory publish windows).

### SEO

- When the tenant enables **enforced SEO**, `Meta title` and `Meta description` are **required** before a page may be published.
- `Canonical URL`, when provided, must be a valid absolute URL.

### Page tree integrity

- `Parent page` must not create a **circular reference** (a page cannot be its own ancestor or descendant).
- The selected `Parent page` must belong to the **same tenant**.

### Page-type conditional fields

- `Page type = URL redirect` requires a valid `Target URL`.
- `Page type = File` requires a valid `File reference` to an existing File Manager asset.
- `Page type = Existing page` requires a valid reference to another page in the tree.

### Field-level

- Required fields per Collection/Single Type must be present before publish.
- Datetime range fields on entries (e.g. Events `start`/`end`, Surveys `open`/`close`, Warnings `start`/`end`) must have end ≥ start.
- Media-typed fields must reference assets the user is permitted to use; tag-typed fields must reference valid tenant tags (or create them if the user has tag-management permission).

### Publishing

- Content **cannot be published without approval** when the tenant requires review and the user lacks direct-publish permission. Such a publish attempt is blocked and the item is routed to review instead.

### General

- All forms and APIs enforce server-side input validation per [Security](security.md); client-side validation is advisory only.

---

## Edge Cases

- **Slug collides with a dynamic route**: if a saved page slug would resolve to the same path as an existing dynamic route within the tenant, the save is rejected with a conflict error. Static pages take precedence per the resolution order in [Dynamic Routes](dynamic_routes.md); the user must change the slug or adjust the route.
- **Deleting a parent page with children**: the system never silently orphans pages. The user must choose to re-parent the children or cascade-delete the subtree (explicit confirmation). Any referenced "Existing page" pointers to deleted pages are flagged.
- **Scheduling conflicts**: overlapping or contradictory publish/unpublish windows on the same item are rejected. If a scheduled publish and a manual publish race, the manual action wins and the redundant schedule is cleared.
- **Publishing without approval**: when review is mandatory and the user lacks direct-publish rights, the publish action is converted into a review submission; the live version is unchanged until a reviewer approves.
- **Simultaneous edits / version conflicts**: optimistic concurrency is enforced. If two users edit the same item, the second save is rejected with a version-conflict error, offering the user a diff against the latest version and the option to merge, overwrite, or discard. No save silently overwrites a newer version; every accepted save creates a distinct history version.
- **Restore during an open review**: restoring a prior version while an item is in review creates a new draft version and resets the review status to draft (the in-flight review is closed in history, not lost).
- **Circular parent assignment**: rejected at validation time; the offending parent selection is cleared.
- **Deleting a Collection/Single Type with existing entries**: blocked unless the entries are first removed or the deletion explicitly cascades (confirmation required), to prevent dangling content references.
- **Renaming/merging a tag in use**: references are updated transactionally; if the operation fails midway it rolls back, leaving the vocabulary consistent.
- **File or media reference removed**: if a File-type page's `File reference` (or an entry's media field) points to a deleted asset, the page surfaces a broken-reference warning and cannot be published until fixed.
- **Reject with no comment**: a rejection or revision request without the required comment is blocked; reviewers must explain the decision.
- **Cross-tenant leakage attempt**: any attempt to reference a parent, media asset, user, or tag from another tenant is rejected; see [Multi-Tenant Routing](multi_tenancy.md) and [Security](security.md).

---

## Acceptance Criteria

- [ ] The Content Manager displays the active tenant's pages as a navigable tree and never shows another tenant's content.
- [ ] Built-in Collection Types **News**, **Events**, **Surveys**, and **Warnings** exist with their documented example fields and are fully editable.
- [ ] Admins can create custom Collection Types and Single Types with arbitrary field sets.
- [ ] All documented page fields are present and editable: `slug`, `Title`, `Meta title`, `Meta description`, `SEO keywords`, `Canonical URL`, `Display in menu`, `Published`, `Disabled`, `Scheduled publish date`, `Scheduled unpublish date`, `Parent page`, `Sort order`, `Page type`, `Target URL`, `File reference`.
- [ ] All four page types (Standard, Existing page, URL redirect, File) are selectable and enforce their conditional required fields.
- [ ] Per-page permissions support assigning view roles and view users, and distinguish public vs private pages.
- [ ] Theme, layout, and page-specific layout settings can be set per page and never affect the admin area.
- [ ] The page builder lets users place built-in modules, the built-in Razor module, and extension modules; supports preview mode and draft vs published versions.
- [ ] Slugs are validated for format and uniqueness within the tenant, and a slug that collides with a dynamic route is rejected.
- [ ] Saving with `Scheduled unpublish date` not strictly after `Scheduled publish date` is rejected.
- [ ] Setting a `Parent page` that would create a cycle is rejected.
- [ ] When enforced SEO is enabled, a page cannot be published without `Meta title` and `Meta description`.
- [ ] Tags can be created, applied, renamed, merged, and deleted; references update consistently.
- [ ] Content history records every version with who and when; users can view, compare, and restore versions, and restore creates a new version rather than overwriting history.
- [ ] The review workflow supports draft review, reviewer assignment, approval/publish, reject-with-comments, revision requests, and a complete review history.
- [ ] A user lacking direct-publish permission cannot bypass a mandatory review; the publish attempt is routed to review instead.
- [ ] Reject and revision-request actions require a comment.
- [ ] Deleting a parent page prompts to re-parent or cascade-delete and never silently orphans children.
- [ ] Simultaneous edits produce a version-conflict error with a diff and resolution options; no save silently overwrites a newer version.
- [ ] All inputs are validated server-side, and every action is recorded in [Audit Logs](audit_logs.md).
