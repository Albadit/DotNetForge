# Authentication & User Access

> Part of the **DotNetForge CMS** specification. See the [index](README.md) for all modules.

This module defines how users authenticate with **DotNetForge CMS** through email and social/OAuth providers, how registration and password recovery work, and the advanced account settings that govern sign-ups, email confirmation, and default roles. Authentication providers and their advanced settings live under the **Users & Permissions Plugin** area of the admin sidebar.

## Purpose

The authentication module controls every way a user can gain an identity in DotNetForge CMS and how that identity is granted access. It must:

- Provide a built-in **Email** (username/password) authentication provider.
- Support a curated list of social/OAuth authentication providers out of the box.
- Allow additional providers to be added through the extension system (see [Extensions](extensions.md)).
- Govern registration, sign-up enablement, default role assignment, email confirmation, and password reset through **Advanced User Settings**.
- Integrate with the platform's security controls - account lockout, rate limiting, and secure password hashing (see [Security](security.md)).
- Assign newly authenticated users a role from the canonical role hierarchy (see [User Roles & Permissions](user_roles_permissions.md)).

Authentication is available in all three CMS modes: the visual **Traditional CMS** login page, the **Headless CMS** API (token-based, see [API Tokens](api_tokens.md)), and **Hybrid** usage of both simultaneously.

## Main Features

- **Authentication Providers list** - a managed catalog of default providers, each with a Name, Status (Enabled/Disabled), and a Settings/Edit action.
- **Per-provider configuration** - credentials and callback settings (e.g., client ID, client secret, authority/issuer URLs) edited per provider.
- **Extension-provided providers** - new authentication providers can be installed as `Authentication provider` extensions (see [Extensions](extensions.md)).
- **Advanced User Settings** - default role, one-account-per-email enforcement, sign-up enablement, reset-password URL, email confirmation toggle, and email confirmation redirection URL.
- **Email login** - username/password authentication using securely hashed credentials.
- **Social/OAuth login** - delegated authentication via configured external providers.
- **Registration** - self sign-up (when enabled) and provider-driven account creation.
- **Password reset** - email-driven recovery flow pointing at a configurable frontend URL.
- **Email confirmation** - optional confirmation email flow with a configurable post-confirmation redirect.
- **Security integration** - account lockout after repeated failures and rate limiting on login attempts (see [Security](security.md)).

## Default Authentication Providers

The CMS ships with the following default providers. **Email** is the built-in local provider; all others are social/OAuth providers. Each provider exposes a **Name**, a **Status**, and a **Settings/Edit** action.

| Provider | Type | Description |
|----------|------|-------------|
| Email | Local | Built-in username/password authentication. |
| Auth0 | OAuth/OIDC | Auth0 identity platform. |
| CAS | CAS | Central Authentication Service (single sign-on). |
| Cognito | OAuth/OIDC | Amazon Cognito user pools. |
| Discord | OAuth | Discord login. |
| Facebook | OAuth | Facebook login. |
| GitHub | OAuth | GitHub login. |
| Google | OAuth/OIDC | Google login. |
| Instagram | OAuth | Instagram login. |
| Keycloak | OAuth/OIDC | Keycloak identity and access management. |
| LinkedIn | OAuth/OIDC | LinkedIn login. |
| Microsoft | OAuth/OIDC | Microsoft / Entra ID login. |
| Patreon | OAuth | Patreon login. |
| Reddit | OAuth | Reddit login. |
| Twitch | OAuth | Twitch login. |
| Twitter/X | OAuth | Twitter / X login. |
| VK | OAuth | VK (VKontakte) login. |

### Providers list example

Each row in the Providers list shows the provider name, its current status, and an action to edit its settings:

```text
auth0      Disabled      Edit
github     Disabled      Edit
email      Enabled       Edit
```

### Adding providers via extensions

The default list is not exhaustive. Additional authentication providers can be installed as extensions of type **Authentication provider**. Installed provider extensions appear in the Providers list alongside the defaults and expose the same Name / Status / Settings actions. See [Extensions](extensions.md) for installation, the `dotnetforge.extension.json` manifest format, and provider registration.

## Data Model / Fields

### Provider entry

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | string | Yes | Provider identifier/display name (e.g., `auth0`, `github`, `email`). |
| Status | enum (Enabled / Disabled) | Yes | Whether the provider is active and offered at login. |
| Settings | object | Yes | Provider-specific configuration (credentials, URLs); edited via the Settings/Edit action. |

Provider **Settings** vary by provider but typically include OAuth client ID, client secret, scopes, and callback/redirect URLs; OIDC providers additionally require an authority/issuer URL. Secrets configured here are sensitive and must be stored securely and never committed to Git (see [Security](security.md)).

### Advanced User Settings

| Setting | Type | Required | Description |
|---------|------|----------|-------------|
| Default role for authenticated users | role reference | Yes | The role assigned to new registered users. Must reference an existing role. |
| One account per email address | boolean | Yes | When `true`, prevents users from creating multiple accounts using the same email with different authentication providers. |
| Enable sign-ups | boolean | Yes | When `false`, registration is forbidden for **all** providers. |
| Reset password page URL | URL | Conditional | The frontend URL where users reset their password. |
| Enable email confirmation | boolean | Yes | When `true`, new users receive a confirmation email before/after their account is usable. |
| Email confirmation redirection URL | URL | Conditional | Where users are redirected after confirming their email. |

## User Flows

### Flow: Email login

1. The user opens the login page (Traditional/Hybrid mode) or calls the login API (Headless mode).
2. The user submits their email and password.
3. The system locates the account by email and verifies the password against the stored secure hash.
4. If **Enable email confirmation** is on and the account is unconfirmed, login is rejected with a "confirm your email" message (see Edge Cases).
5. If the password is invalid, the system records a failed attempt and increments the lockout counter (see [Security](security.md)).
6. On success, the system establishes a session (HttpOnly, SameSite, Secure-in-production cookie) or issues the appropriate token, and redirects the user to their destination (admin dashboard or requested page).

### Flow: Social / OAuth login

1. The user selects an **Enabled** provider (e.g., Google, GitHub) on the login page.
2. The system redirects the user to the external provider's authorization endpoint using the configured client credentials and callback URL.
3. The user authenticates and authorizes the application with the external provider.
4. The provider redirects back to the CMS callback with an authorization code/token.
5. The system exchanges the code, retrieves the external profile (including email), and validates the response.
6. If a matching account exists, the user is signed in. If not, the registration sub-flow runs (see below), subject to **Enable sign-ups** and **One account per email address**.
7. On success, a session/token is established and the user is redirected to their destination.

### Flow: Registration (sign-up)

1. The user initiates registration via the email form or via a social/OAuth provider.
2. The system checks **Enable sign-ups**. If `false`, registration is rejected for all providers and the flow stops.
3. The system validates the submitted data (email format, password rules for email registration).
4. If **One account per email address** is `true`, the system rejects the registration when an account with the same email already exists under any provider (see Edge Cases).
5. The system creates the account and assigns the **Default role for authenticated users**.
6. If **Enable email confirmation** is `true`, the system sends the **Email confirmation** template (see [Email Templates](email.md)) and marks the account unconfirmed.
7. The user is informed of next steps (confirm email, or proceed to login/dashboard).

> First-time CMS installation is a separate flow: the very first registered user becomes the **Super Admin**. See [Installation & Setup](installation_setup.md).

### Flow: Password reset

1. The user requests a password reset by submitting their email.
2. The system always responds neutrally (without revealing whether the email exists) to avoid account enumeration.
3. If the account exists, the system generates a single-use, time-limited reset token and sends the **Password reset** email (see [Email Templates](email.md)) linking to the configured **Reset password page URL**.
4. The user opens the reset page, submits a new password and confirmation.
5. The system validates the token (unexpired, unused) and the new password, updates the stored hash, and invalidates the token.
6. The user is redirected to the login page.

### Flow: Email confirmation

1. After registration with **Enable email confirmation** on, the system sends a confirmation email containing a single-use, time-limited confirmation link.
2. The user clicks the confirmation link.
3. The system validates the token, marks the account as confirmed, and invalidates the token.
4. The user is redirected to the **Email confirmation redirection URL**.

## Role & Permission Rules

Only administrative roles may configure providers and advanced settings; ordinary authenticated users may only authenticate and manage their own account. See [User Roles & Permissions](user_roles_permissions.md) for the full role and permission model (this module's settings live under the **Users** and **Settings** permission areas).

| Action | Super Admin | Admin | Editor | Author | Authenticated | Public |
|--------|:-----------:|:-----:|:------:|:------:|:-------------:|:------:|
| View Authentication Providers list | Yes | Yes | No | No | No | No |
| Enable / disable a provider | Yes | Yes | No | No | No | No |
| Edit provider settings (credentials/URLs) | Yes | Yes | No | No | No | No |
| Edit Advanced User Settings | Yes | Yes | No | No | No | No |
| Install provider extensions | Yes | No | No | No | No | No |
| Log in (any enabled provider) | Yes | Yes | Yes | Yes | Yes | n/a |
| Register (when sign-ups enabled) | n/a | n/a | n/a | n/a | n/a | Yes |
| Reset own password / confirm own email | Yes | Yes | Yes | Yes | Yes | n/a |

Installing or removing authentication provider extensions is restricted to **Super Admin**, consistent with the Extensions permission model (see [Extensions](extensions.md)).

## Validation Rules

- **Provider credentials must be valid.** When enabling or saving a provider, required credential fields for that provider type (e.g., OAuth client ID and client secret; OIDC authority/issuer URL) must be present. A provider with incomplete or invalid credentials must not be set to **Enabled**.
- **Callback/redirect URLs must be valid, absolute URLs** matching the application's configured `APP_URL` scheme where applicable.
- **Reset password page URL** and **Email confirmation redirection URL** must be valid, well-formed URLs. They are required when their associated feature is in use (password reset must have a reset page URL; **Email confirmation redirection URL** is required when **Enable email confirmation** is `true`).
- **Default role for authenticated users must reference an existing role.** Saving the setting with a missing or deleted role must be rejected.
- **Email format** must be valid on registration, login, and password-reset requests.
- **Passwords** for email registration must meet the platform password policy and be confirmed (password and confirm-password must match); stored only as a secure hash (see [Security](security.md)).
- **One account per email address**: when `true`, the email must be unique across all providers; a registration that would create a duplicate email is rejected.
- **Sign-up gate**: registration requests are rejected entirely when **Enable sign-ups** is `false`, regardless of provider.
- **Tokens** for password reset and email confirmation must be single-use and time-limited; expired or reused tokens are rejected.
- **Boolean settings** (`One account per email address`, `Enable sign-ups`, `Enable email confirmation`) accept only `true`/`false`.
- All authentication forms and API endpoints must enforce input validation, CSRF protection, and rate limiting (see [Security](security.md)).

## Edge Cases

- **Sign-ups disabled but OAuth attempted.** When **Enable sign-ups** is `false` and a user authenticates via a social/OAuth provider with no existing matching account, the system must not create an account; the login is rejected with a clear "registration is disabled" message. Existing accounts may still log in via OAuth.
- **Same email across two providers with "one account per email" on.** When **One account per email address** is `true` and a user authenticates via a provider whose returned email already belongs to an existing account, the system must not create a second account. It rejects the duplicate (or, per platform policy, treats it as the same identity) rather than silently creating divergent accounts.
- **Login before email is confirmed.** When **Enable email confirmation** is `true` and the user has not confirmed their email, login is blocked with a message instructing them to confirm; the system should allow re-sending the confirmation email.
- **Provider disabled while users rely on it.** Disabling a provider hides it from the login page and blocks new logins through it. Existing accounts originally created via that provider are not deleted; affected users should be able to recover access through another enabled provider or via email/password reset (where an email exists). Administrators must be aware that disabling the **Email** provider can lock out password-based recovery.
- **Repeated failed login attempts.** After the configured threshold of failures, the account is locked out (and the **Account locked** email template may be sent, see [Email Templates](email.md)); further attempts are refused until the lockout window elapses (see [Security](security.md)).
- **Login flooding / brute force.** Rate limiting on the login endpoint throttles excessive attempts independent of account lockout (see [Security](security.md)).
- **Default role deleted.** If the configured **Default role for authenticated users** is later deleted, new registrations must fail validation until a valid existing role is configured.
- **Password reset for non-existent email.** The system responds neutrally and does not reveal whether the email is registered (no account enumeration).
- **Expired or reused reset/confirmation token.** The system rejects the token and prompts the user to restart the relevant flow.
- **Disabled Email provider during reset.** If the Email provider is disabled, password-based login/reset is unavailable; recovery must rely on an enabled provider.

## Acceptance Criteria

- [ ] The Authentication Providers list displays all default providers: Email, Auth0, CAS, Cognito, Discord, Facebook, GitHub, Google, Instagram, Keycloak, LinkedIn, Microsoft, Patreon, Reddit, Twitch, Twitter/X, and VK.
- [ ] Each provider row shows Name, Status (Enabled/Disabled), and a working Settings/Edit action.
- [ ] Provider statuses render as in the example (e.g., `auth0 Disabled Edit`, `github Disabled Edit`, `email Enabled Edit`).
- [ ] A provider installed as an `Authentication provider` extension appears in the list with the same controls as the defaults.
- [ ] Only Super Admin and Admin can view the providers list, enable/disable providers, edit provider settings, and edit Advanced User Settings; only Super Admin can install provider extensions.
- [ ] A user can log in with email and a correct password; an incorrect password is rejected and increments the lockout counter.
- [ ] A user can log in via each enabled social/OAuth provider through the full redirect/callback exchange.
- [ ] A new user can register via email when sign-ups are enabled, and is assigned the configured Default role for authenticated users.
- [ ] When **Enable sign-ups** is `false`, registration is rejected for every provider, including OAuth-driven account creation.
- [ ] When **One account per email address** is `true`, registering or authenticating with an email already in use does not create a second account.
- [ ] When **Enable email confirmation** is `true`, registration sends a confirmation email and unconfirmed users cannot log in.
- [ ] Clicking a valid confirmation link marks the account confirmed and redirects to the **Email confirmation redirection URL**.
- [ ] The password reset flow emails a single-use, time-limited link to the **Reset password page URL** and updates the password on valid submission.
- [ ] Password reset requests respond neutrally for unknown emails (no account enumeration).
- [ ] Enabling a provider with missing/invalid required credentials is rejected.
- [ ] Saving a Reset password page URL or Email confirmation redirection URL that is not a valid URL is rejected; the redirection URL is required when email confirmation is enabled.
- [ ] Saving a Default role that does not reference an existing role is rejected.
- [ ] After the configured number of failed login attempts, the account is locked out and login is refused until the window elapses.
- [ ] Login attempts are rate limited at the endpoint level.
- [ ] Disabling a provider removes it from the login page and blocks new logins through it without deleting existing accounts.
- [ ] All authentication forms and APIs enforce input validation and CSRF protection, and passwords are stored only as secure hashes.
