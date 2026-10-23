# Profile

## Purpose

Every admin user's own account: their name and email, and a password change. Roles and status are managed by an
administrator on [Users](users.md).

## Route / Navigation

| Item | Value |
| --- | --- |
| Routes | `GET /admin/profile` · `POST /admin/profile` (details) · `POST /admin/profile/password` |
| Opened from | the account menu at the bottom of the sidebar → **Profile** |
| Sidebar entry | none (not in `AdminNavigation.Tree`) |

## Relevant source files

```text
src/DotNetForge.Web/Areas/Admin/Controllers/ProfileController.cs   screen and both POSTs; ProfileViewModel
src/DotNetForge.Web/Services/ProfileService.cs                     rules, audit (ProfileInput, PasswordChangeInput)
src/DotNetForge.Web/Services/AuthService.cs                        RefreshPrincipalAsync (new name/email in the cookie)
src/DotNetForge.Web/Areas/Admin/Views/Profile/Index.cshtml         the screen
```

## Page layout

```text
Profile (h1)
├── card Details: First name · Last name · Email*            [Save details]
├── card Password: Current password* · New password* · Confirm*   [Change password]
└── aside: avatar, name, email · Roles · Member since · Last sign-in
```

## Functionality

| Action | What happens |
| --- | --- |
| Save details | `ProfileService.UpdateDetailsAsync`; the sign-in cookie is re-issued with the new name and email (same expiry) → toast "Your profile is saved." |
| Change password | `ProfileService.ChangePasswordAsync` → toast; the next sign-in uses the new password, the current session continues. |

Both are audited as `user.updated` (details `Own profile` / `Own password changed`).

## Permissions

Any signed-in admin user (`AdminArea`); each user can only change their own account.

## Validation

| Rule | Message |
| --- | --- |
| Email valid, at most 256 characters, unique in the tenant (case-insensitive) | "Enter a valid email address (at most 256 characters)." / "Another user already uses …" |
| Names at most 100 characters | "First and last name are limited to 100 characters each." |
| Current password correct | "Your current password is not correct." |
| New password per `PasswordPolicy`, confirmed, different | policy message / "The new password and its confirmation do not match." / "Choose a password different from your current one." |

Errors show in the form that was posted; the other form keeps its values.

## Related pages

[Users](users.md), [sign in](login.md).
