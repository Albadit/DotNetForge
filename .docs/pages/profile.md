# Profile

## Purpose

Every admin user's own account: their name and email, a password change (or a first password for an account made
through a [sign-in provider](../features/authentication.md#sign-up-through-a-provider)), and the way to two-step
verification ([Sign-in security](sign-in-security.md)). Roles and status are managed by an administrator on
[Users](users.md).

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
src/DotNetForge.Web/Services/Mfa/MfaService.cs                     MethodsAsync (the Two-step verification card)
src/DotNetForge.Web/Areas/Admin/Views/Profile/Index.cshtml         the screen
```

## Page layout

```text
Profile (h1)
├── card Details: First name · Last name · Email*            [Save details]
├── card Password: Current password* · New password* · Confirm*   [Change password]
│   (no password yet: card "Set a password" - "You signed up with a sign-in provider. With a password you can also
│    sign in with your email." · New password* · Confirm*   [Set password])
├── card Two-step verification + badge On/Off · [Sign-in security] (→ /admin/profile/security)
└── aside: avatar, name, email · Roles · Member since · Last sign-in
```

## Functionality

| Action | What happens |
| --- | --- |
| Save details | `ProfileService.UpdateDetailsAsync`; the sign-in cookie is re-issued with the new name and email (same expiry) → toast "Your profile is saved." |
| Change password | `ProfileService.ChangePasswordAsync` → toast; the next sign-in uses the new password, the current session continues. |
| Set password | the same action for an account without a password (empty `PasswordHash`, `ProfileViewModel.HasPassword` false): no current password is asked → toast "Your password is set. You can also sign in with your email and password now." |
| Sign-in security | link to [Sign-in security](sign-in-security.md); the card says **On** when the user has any two-step method (`ProfileViewModel.MfaMethods`) |

Both are audited as `user.updated` (details `Own profile` / `Own password changed`).

## Permissions

Any signed-in admin user (`AdminArea`); each user can only change their own account. While
[two-factor authentication is required](../features/mfa.md#requirement) and the user has no method, opening Profile
redirects to Sign-in security.

## Validation

| Rule | Message |
| --- | --- |
| Email valid, at most 256 characters, unique (case-insensitive) | "Enter a valid email address (at most 256 characters)." / "Another user already uses …" |
| Names at most 100 characters | "First and last name are limited to 100 characters each." |
| Current password correct (only when the account has a password) | "Your current password is not correct." |
| New password per `PasswordPolicy`, confirmed, different (from the current one, when there is one) | policy message / "The new password and its confirmation do not match." / "Choose a password different from your current one." |

Errors show in the form that was posted; the other form keeps its values.

## Related pages

[Users](users.md), [sign in](login.md), [Sign-in security](sign-in-security.md).
