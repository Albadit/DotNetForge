# File Manager

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

The File Manager is the centralized File Manager for **DotNetForge CMS**. It lets users upload, organize, secure, search, and serve files (images and documents) using local storage by default and external storage connectors through extensions.

## Purpose

The File Manager provides a single place to store and manage all binary assets used across DotNetForge CMS - images, documents, and other files referenced by pages, content entries, themes, and extensions. It must:

- Allow users to **upload**, **organize**, **rename**, **move**, and **delete** files and folders.
- Provide a hierarchical **folder/document library** with nesting and breadcrumb navigation.
- Mark files and folders as **public** or **private**, and enforce **role/user access control** on private files (see [User Roles & Permissions](user_roles_permissions.md)).
- Generate stable **file URLs** and provide direct **downloads** that respect public/private access.
- **Search**, **filter**, and **sort** files by name, type, tag, folder, and date.
- Use **local storage** by default and support **external storage connectors** through extensions.
- Validate every upload (file type, size, filename) and **scan or block unsafe uploads** where possible.
- Generate responsive image variants, optimize image size, and auto-correct orientation via EXIF metadata.

This module corresponds to the admin sidebar item **File Manager** (under *Main*) and its configuration counterpart **File Manager** (under *Settings → Global Settings*). The File Manager is tenant-scoped: each tenant has its own File Manager, and files must never leak between tenants (see [Multi-Tenancy](multi_tenancy.md)).

## Main Features

- **Upload files** - via drag/drop or a file picker. Single and multiple uploads are supported.
- **Organize files** - into folders with arbitrary nesting; move files and folders between locations.
- **Folders (document library)** - create, rename, move, and delete folders; nested hierarchy with breadcrumb navigation.
- **Rename files** - change the display name without breaking already-generated URLs where possible.
- **Delete files** - remove a file or folder, with protection for assets currently in use.
- **Public / private access** - mark any file or folder as public or private; private assets require explicit role/user grants.
- **Generate file URLs** - produce stable, shareable URLs for each file and for each responsive variant.
- **Downloads** - direct download of any file the requester is authorized to access, honoring public/private rules.
- **Search, filter & sort** - by name, type/MIME, tag, folder, and date; sort ascending/descending on those fields.
- **Tagging** - attach one or more tags to files to aid search and organization.
- **Local storage by default** - files stored under `storage/media/` (per tenant) out of the box.
- **External storage connectors** - pluggable connectors (e.g. S3-compatible, Azure Blob) supplied through extensions of type **Connector** (see [Extensions](extensions.md)).
- **Secure upload validation** - configurable allowed file types and configurable maximum upload size; unsafe uploads are blocked or scanned.
- **Responsive friendly upload** - when enabled, generate **small**, **medium**, and **large** variants of uploaded images.
- **Size optimization** - when enabled, reduce image file size with minor quality loss.
- **Auto orientation** - when enabled, automatically rotate images using EXIF orientation metadata.
- **Webhook events** - emit `Media create`, `Media update`, and `Media delete` events (see [Webhooks](webhooks.md)).

## Data Model / Fields

### Media File

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | GUID | Yes | Unique identifier of the file. |
| `tenantId` | GUID | Yes | Owning tenant scope; enforced on every operation. |
| `name` | string | Yes | Display name (sanitized; defaults to the original filename). |
| `fileName` | string | Yes | Stored, sanitized filename on disk/connector. |
| `extension` | string | Yes | File extension (e.g. `.png`, `.pdf`). |
| `mimeType` | string | Yes | Detected MIME type (validated against allowed list). |
| `sizeBytes` | long | Yes | File size in bytes. |
| `folderId` | GUID | No | Parent folder; null means library root. |
| `storageProvider` | string | Yes | `local` (default) or a connector id from an extension. |
| `storageKey` | string | Yes | Path/key used by the storage provider to locate the file. |
| `isPublic` | bool | Yes | `true` = publicly accessible; `false` = private (access-controlled). |
| `tags` | string[] | No | Tags used for search/filtering. |
| `width` | int | No | Image width in pixels (images only). |
| `height` | int | No | Image height in pixels (images only). |
| `variants` | object[] | No | Responsive variants (small/medium/large) with their own URLs and dimensions. |
| `checksum` | string | Yes | Content hash used for duplicate detection. |
| `createdBy` | GUID | Yes | User who uploaded the file. |
| `createdDate` | datetime | Yes | Upload timestamp. |
| `updatedDate` | datetime | No | Last modification timestamp. |

### Folder

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | GUID | Yes | Unique identifier of the folder. |
| `tenantId` | GUID | Yes | Owning tenant scope. |
| `name` | string | Yes | Folder display name (sanitized). |
| `parentId` | GUID | No | Parent folder; null means library root. |
| `path` | string | Yes | Materialized path used for breadcrumbs and navigation. |
| `isPublic` | bool | Yes | Default access for files created inside, unless overridden. |
| `createdBy` | GUID | Yes | User who created the folder. |
| `createdDate` | datetime | Yes | Creation timestamp. |

### Access Grant (private files/folders)

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `targetId` | GUID | Yes | File or folder the grant applies to. |
| `targetType` | enum | Yes | `file` or `folder`. |
| `roleId` | GUID | No | Role granted access (see [User Roles & Permissions](user_roles_permissions.md)). |
| `userId` | GUID | No | Specific user granted access. |

At least one of `roleId` or `userId` must be present on a grant.

## Media Settings (Settings → Global Settings → File Manager)

These toggles configure upload behavior globally (per tenant) and are reproduced from the source specification:

| Setting | Type | Default | Behavior |
| --- | --- | --- | --- |
| Responsive friendly upload | true / false | true | When enabled, generate **small**, **medium**, and **large** versions of uploaded images. |
| Size optimization | true / false | true | When enabled, reduce image size with **minor quality loss**. |
| Auto orientation | true / false | true | When enabled, automatically rotate images using **EXIF orientation** metadata. |
| Allowed file types | list of MIME types / extensions | configurable | The whitelist of file types accepted on upload. |
| Maximum upload size | size (bytes/MB) | configurable | The maximum allowed size per uploaded file. |
| Default storage provider | `local` or connector id | `local` | Where new files are stored. |

> **Security note:** Allowing every file type is risky. The CMS must enforce configurable allowed file types and a configurable maximum upload size, and must scan or block unsafe uploads where possible. See [Security](security.md) for the full file-upload security model.

## User Flows

### Flow: Upload a file (drag/drop or picker)

1. The user opens the **File Manager** and navigates to the target folder (or the root).
2. The user drags files onto the drop zone, or clicks the upload control and selects files via the file picker.
3. For each file, the system sanitizes the filename and validates the **MIME type/extension** against the allowed list and the **size** against the configured maximum.
4. If validation fails, the system rejects that file with a specific reason (disallowed type, oversized, unsafe) and continues with the remaining files.
5. The system scans the file (or applies safe-upload rules) and blocks it if it is detected as unsafe.
6. The system computes a checksum and checks for an existing file with the same checksum/name in the target folder to apply **duplicate handling**.
7. For images: if **Auto orientation** is enabled, orientation is corrected via EXIF; if **Size optimization** is enabled, the image is optimized; if **Responsive friendly upload** is enabled, **small/medium/large** variants are generated.
8. The file is written to the active storage provider (local by default, or a connector), inheriting the folder's public/private setting unless overridden.
9. The system records metadata, generates file URLs (including variant URLs), and emits a `Media create` webhook event.
10. The new file appears in the current folder view.

### Flow: Create / rename / move / delete a folder

1. The user clicks **New Folder**, enters a name, and the system sanitizes and creates the folder under the current location.
2. To **rename**, the user edits the folder name; the system re-sanitizes and updates the folder `path` and breadcrumbs.
3. To **move**, the user drags the folder (or uses a move action) to a new parent; the system updates the materialized `path` for the folder and all descendants, preventing moves that would create a cycle.
4. To **delete**, the user selects **Delete**; the system checks whether the folder or any descendant file is in use (see Edge Cases) before removing it.

### Flow: Browse with breadcrumbs

1. The user opens a folder; the breadcrumb shows `Root / Parent / Current`.
2. Clicking any breadcrumb segment navigates back to that folder.
3. The current folder's subfolders and files are listed and can be searched, filtered, and sorted within scope.

### Flow: Mark a file or folder public or private and grant access

1. The user opens a file or folder and toggles **Public / Private**.
2. If set to **Private**, the user assigns the roles and/or users allowed to access it (Access Grants).
3. The system persists the grants; private assets without a matching grant are inaccessible to a requester.
4. Permission changes are recorded for audit (see [Audit Logs](audit_logs.md)).

### Flow: Download a file

1. The requester opens or links to a file URL (or clicks **Download**).
2. The system resolves the tenant, then the file.
3. If the file is **public**, the system serves it directly.
4. If the file is **private**, the system verifies the requester's role/user against the file's (and parent folder's) Access Grants. Unauthorized requests receive **403 Forbidden**; missing files receive **404 Not Found**.
5. Authorized requests are streamed for direct download with the correct content type and filename.

### Flow: Search, filter, and sort

1. The user enters a search term and/or selects filters: **name**, **type/MIME**, **tag**, **folder**, and **date** range.
2. The system returns matching files (and optionally folders) within the current tenant scope, respecting the requester's access.
3. The user sorts results by name, type, size, or date, ascending or descending.

### Flow: Delete a file

1. The user selects a file and chooses **Delete**.
2. The system checks whether the file is referenced by any page (Page type **File** / file reference) or content entry.
3. If in use, the system warns and either blocks the deletion or requires explicit confirmation, listing the references.
4. On confirmation, the file (and its variants) are removed from storage, metadata is deleted, and a `Media delete` webhook event is emitted.

### Flow: Configure an external storage connector

1. An administrator installs a **Connector** extension (see [Extensions](extensions.md)) and configures its credentials in `.env`/settings (secrets stored in `.env`, never committed to Git).
2. The administrator selects the connector as the default storage provider or for specific operations.
3. New uploads are routed to the connector; existing local files remain addressable.
4. If the connector becomes unreachable, the system surfaces a clear error and falls back per Edge Cases.

## Role & Permission Rules

Access is governed by the **Media** permission area and by per-file/per-folder Access Grants. Roles below are the canonical DotNetForge roles, most- to least-privileged. See [User Roles & Permissions](user_roles_permissions.md) for the full RBAC model.

| Capability | Super Admin | Admin | Editor | Author | Authenticated | Public |
| --- | --- | --- | --- | --- | --- | --- |
| Configure media settings (allowed types, max size, toggles) | Yes | Yes | No | No | No | No |
| Configure external storage connectors | Yes | Yes | No | No | No | No |
| Upload files | Yes | Yes | Yes | Yes | No | No |
| Create / rename / move / delete folders | Yes | Yes | Yes | Own folders | No | No |
| Rename / move files | Yes | Yes | Yes | Own files | No | No |
| Delete files | Yes | Yes | Yes | Own files | No | No |
| Mark files/folders public or private | Yes | Yes | Yes | No | No | No |
| Manage Access Grants on private files | Yes | Yes | No | No | No | No |
| Search / browse the library | Yes | Yes | Yes | Yes | No | No |
| Download **public** files | Yes | Yes | Yes | Yes | Yes | Yes |
| Download **private** files | Yes | Yes | If granted | If granted | If granted | No |

Notes:

- **Author** can manage only the files and folders they created, consistent with the canonical Author role ("Authors can manage the content they have created").
- **Public** (anonymous) requesters can only retrieve files explicitly marked public.
- All permission checks are enforced per admin and API action, and within the active tenant scope.

## Validation Rules

- **Maximum upload size:** every uploaded file must be at most the configured maximum size. Oversized files are rejected with a clear error.
- **Allowed MIME types / extensions:** the file's detected MIME type and extension must both be on the configured allowlist. Disallowed types are rejected. Detection must not rely solely on the client-supplied extension.
- **Filename sanitization:** filenames must be sanitized - strip or replace path separators, control characters, and reserved/unsafe characters; prevent directory-traversal sequences (e.g. `../`); enforce a maximum length.
- **Unsafe upload blocking/scanning:** uploads must be scanned or blocked where possible; files flagged unsafe (e.g. detected as executable/script content disguised as an allowed type) must not be stored.
- **Duplicate handling:** when a file with the same checksum or name exists in the target folder, the system must apply a deterministic strategy (reject as duplicate, auto-rename with a numeric suffix, or replace), surfaced to the user.
- **Folder name:** required, sanitized, unique among siblings within the same parent and tenant.
- **Folder move integrity:** a folder cannot be moved into itself or any of its descendants (no cycles).
- **Public/private:** every file and folder must have an explicit `isPublic` value; new files default to their parent folder's setting.
- **Access Grant:** a grant on a private asset must reference at least one valid role or user.
- **Tenant scope:** every file, folder, URL, and Access Grant operation must be constrained to the active tenant; cross-tenant references are invalid.
- **Image processing:** EXIF auto-orientation, optimization, and responsive variant generation apply only to supported image types and only when the corresponding setting is enabled.
- **URLs:** generated file URLs (including variant URLs) must be stable for public files and must enforce access checks for private files.

## Edge Cases

- **Oversized file:** the upload is rejected before storage with a message stating the configured maximum; other files in a batch continue.
- **Disallowed file type:** rejected with a message naming the file and the allowed types; never stored.
- **Unsafe / malicious upload:** blocked or quarantined; never served. Logged for audit (see [Audit Logs](audit_logs.md)).
- **Duplicate filename / content:** resolved via the configured duplicate-handling strategy (reject, auto-rename, or replace); the user is informed which action was taken.
- **Deleting a file in use by a page/content:** the system detects references (e.g. Page type **File**, file references, content fields), warns the user, lists the dependents, and either blocks deletion or requires explicit confirmation; on deletion, dependent references must be handled to avoid broken links.
- **Deleting a non-empty folder:** the system warns and applies the same in-use checks to every descendant before removal.
- **Accessing a private file without permission:** anonymous requests receive 403/404; authenticated requests without a matching grant receive **403 Forbidden**. No private metadata is leaked.
- **Broken external connector:** if the storage connector is unreachable or misconfigured, uploads fail gracefully with a clear error, existing local files remain available, and the failure is logged; the admin is notified to restore the connector or switch the storage provider.
- **EXIF orientation missing or invalid:** the image is stored as-is without rotation; processing must not fail the upload.
- **Corrupt or unreadable image:** variant generation and optimization are skipped, the original is preserved (if it passes validation), and a warning is recorded.
- **Concurrent rename/move/delete on the same asset:** the system serializes the operations (optimistic concurrency); the losing operation receives a conflict and the user is prompted to retry.
- **Folder move during concurrent uploads:** uploads in flight are reconciled to the asset's final folder path; no orphaned storage keys remain.
- **Tenant mismatch:** any attempt to access or reference a file outside the active tenant is denied and logged.

## Acceptance Criteria

- [ ] Users can upload files via drag/drop and via a file picker, including multiple files at once.
- [ ] Uploads are rejected when they exceed the configured maximum upload size.
- [ ] Uploads are rejected when the MIME type/extension is not on the configured allowlist, with detection not relying solely on the client extension.
- [ ] Unsafe uploads are scanned or blocked and never served.
- [ ] Filenames are sanitized and directory-traversal attempts are neutralized.
- [ ] Duplicate filenames/content are handled by the configured strategy and the result is communicated to the user.
- [ ] Users can create, rename, move, and delete folders, with arbitrary nesting.
- [ ] Breadcrumb navigation reflects the folder hierarchy and each segment is clickable.
- [ ] A folder cannot be moved into itself or a descendant.
- [ ] Files and folders can be marked public or private.
- [ ] Private files enforce role/user Access Grants; unauthorized download attempts return 403 (and 404 for missing files), with no metadata leakage.
- [ ] Public files are directly downloadable, including by anonymous (Public) requesters.
- [ ] Stable file URLs are generated for each file and for each responsive variant.
- [ ] Search returns results filtered by name, type, tag, folder, and date, and results can be sorted ascending/descending.
- [ ] When **Responsive friendly upload** is enabled, small/medium/large image variants are generated.
- [ ] When **Size optimization** is enabled, image file size is reduced with minor quality loss.
- [ ] When **Auto orientation** is enabled, images are rotated using EXIF orientation metadata.
- [ ] Local storage is used by default, and an external storage connector installed as an extension can be selected and used.
- [ ] A broken/unreachable external connector fails gracefully without losing access to existing local files, and the failure is logged.
- [ ] Deleting a file referenced by a page/content is blocked or requires explicit confirmation, with dependents listed.
- [ ] Media operations are tenant-scoped, with no cross-tenant access or data leakage.
- [ ] `Media create`, `Media update`, and `Media delete` webhook events fire on the corresponding operations.
- [ ] Media settings (allowed types, max size, responsive/optimization/orientation toggles, default storage provider) are configurable by Super Admin and Admin only.
- [ ] Connector secrets are read from `.env` and are never committed to Git.

## Related Specifications

- [User Roles & Permissions](user_roles_permissions.md) - RBAC model, the **Media** permission area, and Access Grants.
- [Security](security.md) - secure file-upload validation, configurable allowed types/max size, and private media access control.
- [Extensions](extensions.md) - **Connector** extension type for external storage and the extension manifest (`dotnetforge.extension.json`).
- [Content Manager](content_manager.md) - Page type **File** and file references that consume media.
- [Webhooks](webhooks.md) - `Media create`, `Media update`, and `Media delete` events.
- [Audit Logs](audit_logs.md) - logging of media uploads and permission changes.
- [Multi-Tenancy](multi_tenancy.md) - per-tenant media libraries and tenant resolution.
