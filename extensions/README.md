# Extensions

Everything you add to DotNetForge CMS lives here - one folder per extension. You never need to change `src/`.

- How to build one: [extension development guide](../.docs/guides/extension-development.md)
- How the CMS hosts them: [extensions](../.docs/features/extensions.md)

## Try the samples

```bash
dotnet build                                   # builds the CMS and every sample with code
dotnet run --project src/DotNetForge.Web       # http://localhost:5000 → complete /setup the first time
```

Sign in as the Super Admin, open **Plugins** and click **Enable** on a sample. Everything starts disabled;
enabling or disabling takes effect at once.

| Sample | Type | Try it |
| --- | --- | --- |
| [admin/audit-dashboard](admin/audit-dashboard) | admin | Enable → sidebar **Extensions → Audit Dashboard**. |
| [widgets/weather-widget](widgets/weather-widget) | widget | Enable → the dashboard (`/admin`) shows the weather for the configured place. Needs internet; shows "unavailable" without it. |
| [themes/default-theme](themes/default-theme) | theme | Enable → open `/` or `/about`: new layout with a menu. Change `accent` (`indigo`, `teal`, `rose`) in its manifest and reload. |
| [libraries/markdown-kit](libraries/markdown-kit) | library | Enable it before Contact Form (which needs it). Nothing visible on its own. |
| [modules/contact-form](modules/contact-form) | module | Enable Markdown Kit, then Contact Form. In **Content**, publish the page `contact` → `/contact` shows the form. Messages land in media storage under `ext/contact-form/` and in the audit log. |
| [plugins/seo-toolkit](plugins/seo-toolkit) | plugin | Enable → `/sitemap.xml` and `/robots.txt`. Disable → both 404. |
| [providers/sms-notifications](providers/sms-notifications) | provider | Enable → fail a sign-in or send a contact message → the console shows `SMS to ...`. Set `"gateway": "twilio"` plus `TWILIO_ACCOUNT_SID`/`TWILIO_AUTH_TOKEN` in `.env` for real SMS. |
| [connectors/s3-storage](connectors/s3-storage) | connector | `docker compose -f docker/s3/seaweedfs.yml up -d`, put `S3_CONNECTOR_ACCESS_KEY_ID=dev` and `S3_CONNECTOR_SECRET_ACCESS_KEY=dev` in `.env`, restart, enable → uploads go to the bucket `dotnetforge` on `http://localhost:8333`. Media uploaded before stays in the old storage. |
| [authentication/discord-login](authentication/discord-login) | authentication | Create an app at https://discord.com/developers/applications with the redirect `http://localhost:5000/auth/discord/callback`, put `DISCORD_CLIENT_ID`/`DISCORD_CLIENT_SECRET` in `.env`, restart, enable → **Sign in with Discord** on the sign-in screen for users whose verified Discord email matches their account (or, with **Enable sign-ups** on, for anyone with a verified Discord email - the host creates the account). Discord is also built in on **Providers**, without an extension. Until configured, Plugins shows it as not running and why. |

## Start your own

Copy the sample closest to what you need, change the `id` (globally unique, e.g. `yourcompany.widget.sales`), the
names and the code, then:

```bash
dotnet build extensions/<type>/<your-folder>   # only if it has code
```

Restart the CMS (code loads at startup), enable it on **Plugins**. The [guide](../.docs/guides/extension-development.md)
covers every type, settings, secrets and troubleshooting.

[Directory.Build.props](Directory.Build.props) gives every extension project the right settings (target framework,
output to its own `bin/`, the SDK reference) - your `.csproj` stays empty unless you add packages.
