using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Authentication;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Sign-in providers (.docs/pages/providers.md) and two-factor authentication (.docs/features/mfa.md): settings,
/// a full OAuth round trip against a fake provider back end, sign-up through a provider, and every second factor at
/// sign-in - authenticator app, recovery code, email code, SMS code - plus lockout, the requirement and an admin reset.
/// </summary>
public sealed partial class SignInSecurityTests
{
    private const string Security = "/admin/profile/security";

    // ---- Pure rules --------------------------------------------------------------------------------------------------

    [Fact]
    public void Totp_matches_the_rfc_6238_vectors_and_accepts_a_code_once()
    {
        // RFC 6238 appendix B, SHA-1 secret "12345678901234567890"; the 6-digit code is the last six of the 8-digit one.
        var secret = Totp.ToBase32(Encoding.ASCII.GetBytes("12345678901234567890"));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", secret);
        Assert.Equal("287082", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59))));
        Assert.Equal("005924", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1234567890))));

        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.Verify(secret, "005 924", now, lastStep: 0);
        Assert.Equal(Totp.StepAt(now), step);
        Assert.Null(Totp.Verify(secret, "005924", now, lastStep: step!.Value)); // used: never again
        Assert.Null(Totp.Verify(secret, "000000", now, lastStep: 0));
        Assert.Equal(Encoding.ASCII.GetBytes("12345678901234567890"), Totp.FromBase32(secret));
    }

    [Theory]
    [InlineData("+41 79 123 45 67", "+41791234567")]
    [InlineData("0041791234567", "+41791234567")]
    [InlineData("079 123 45 67", null)]
    [InlineData("+1 2", null)]
    public void Phone_numbers_need_a_country_code(string typed, string? expected) =>
        Assert.Equal(expected, MfaService.NormalizePhone(typed));

    [Fact]
    public void Apples_client_secret_is_an_es256_jwt_signed_with_the_team_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var secret = OAuthProviderHandler.AppleClientSecret(new OAuthProviderOptions
        {
            ClientId = "com.example.web",
            TeamId = "ABCDE12345",
            KeyId = "FGHIJ67890",
            PrivateKey = key.ExportPkcs8PrivateKeyPem(),
        });

        var parts = secret.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64Url(parts[0]));
        using var payload = JsonDocument.Parse(Base64Url(parts[1]));
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("FGHIJ67890", header.RootElement.GetProperty("kid").GetString());
        Assert.Equal("ABCDE12345", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("com.example.web", payload.RootElement.GetProperty("sub").GetString());
        Assert.Equal("https://appleid.apple.com", payload.RootElement.GetProperty("aud").GetString());
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64Url(parts[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Provider_secrets_are_stored_protected_and_an_unreadable_one_reads_as_missing()
    {
        var protection = new EphemeralDataProtectionProvider();
        var json = new ProviderSettings("client", "s3cret", null, null, null, null, null).Write(protection);
        Assert.DoesNotContain("s3cret", json);
        Assert.Equal("s3cret", ProviderSettings.Read(json, protection, out var unreadable).ClientSecret);
        Assert.False(unreadable);

        var elsewhere = ProviderSettings.Read(json, new EphemeralDataProtectionProvider(), out unreadable);
        Assert.True(unreadable);
        Assert.Null(elsewhere.ClientSecret);
        Assert.Equal("client", elsewhere.ClientId);
        Assert.False(elsewhere.IsComplete(OAuthProviderCatalog.Find("google")!));
    }

    // ---- Providers ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Google_sign_in_matches_verified_emails_signs_up_new_people_and_refuses_unverified_ones()
    {
        using var baseFactory = new DotNetForgeWebFactory();
        await baseFactory.InstallAsync();
        var google = new FakeGoogle();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigureAll<OAuthProviderOptions>(o => o.Backchannel = new HttpClient(google))));
        var admin = await SignInAsync(factory, DotNetForgeWebFactory.AdminEmail, DotNetForgeWebFactory.AdminPassword);

        // Listed, not set up; turning it on without credentials is refused.
        var list = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/providers"));
        foreach (var (_, name) in AuthProviderKeys.OAuth)
        {
            Assert.Contains(name, list);
        }

        Assert.Contains("Not set up", list);
        Assert.Contains("http://localhost/signin/google", await admin.GetStringAsync("/admin/providers/google")); // APP_URL + callback
        var refused = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/providers/google", "/admin/providers/google",
            new Dictionary<string, string> { ["Enabled"] = "true", ["ClientId"] = "client-1" });
        Assert.Contains("Fill in every field", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));

        var saved = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/providers/google", "/admin/providers/google",
            new Dictionary<string, string> { ["Enabled"] = "true", ["ClientId"] = "client-1", ["ClientSecret"] = "very-secret" });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await baseFactory.WithDbAsync(async db =>
        {
            var row = await db.AuthProviders.SingleAsync(p => p.Key == "google");
            Assert.True(row.Enabled);
            Assert.DoesNotContain("very-secret", row.SettingsJson);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ProviderUpdated && a.Details == "Enabled; Changed: ClientId, ClientSecret"));
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Details != null && a.Details.Contains("very-secret")));
        });

        // An existing account: signed in, no account created.
        var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Contains("Sign in with Google", await visitor.GetStringAsync("/login"));
        google.Email = DotNetForgeWebFactory.AdminEmail.ToUpperInvariant(); // providers don't keep the case typed here
        var done = await GoogleRoundTripAsync(visitor, google);
        Assert.Equal("/admin", done.Headers.Location?.OriginalString);
        Assert.Contains("code_verifier=", google.TokenRequest); // PKCE
        Assert.Contains("client_secret=very-secret", google.TokenRequest);
        await baseFactory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserLogin && a.Details == "via Google")));

        // A new email with sign-ups off: refused, nothing created.
        var stranger = NewVisitor(factory);
        google.Email = "new.person@example.com";
        var off = await GoogleRoundTripAsync(stranger, google);
        Assert.Equal("/login", off.Headers.Location?.OriginalString);
        Assert.Contains("new accounts can", WebUtility.HtmlDecode(await stranger.GetStringAsync("/login")));

        // With sign-ups on: an account with the default role, confirmed, without a password.
        await baseFactory.WithDbAsync(async db =>
        {
            db.Settings.Add(new Setting { Key = "users.signUps", Value = "true" });
            await db.SaveChangesAsync();
        });
        var signedUp = await GoogleRoundTripAsync(stranger, google);
        Assert.Equal("/", signedUp.Headers.Location?.OriginalString); // Authenticated opens the site, not the admin
        await baseFactory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == "new.person@example.com");
            Assert.True(user.EmailConfirmed);
            Assert.Equal(string.Empty, user.PasswordHash);
            Assert.Equal("New", user.FirstName);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserRegistered && a.Details == "via Google; Role: Authenticated"));
        });

        // An unverified email is never trusted.
        var unverified = NewVisitor(factory);
        google.Email = "someone.else@example.com";
        google.Verified = false;
        var noEmail = await GoogleRoundTripAsync(unverified, google);
        Assert.Equal("/login", noEmail.Headers.Location?.OriginalString);
        Assert.Contains("no verified email", WebUtility.HtmlDecode(await unverified.GetStringAsync("/login")));
        await baseFactory.WithDbAsync(async db => Assert.False(await db.Users.AnyAsync(u => u.Email == "someone.else@example.com")));

        // Turned off: no button, no callback, the saved secret is kept.
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/providers/google", "/admin/providers/google",
            new Dictionary<string, string> { ["Enabled"] = "false", ["ClientId"] = "client-1" });
        Assert.DoesNotContain("Sign in with Google", await NewVisitor(factory).GetStringAsync("/login"));
        Assert.Equal(HttpStatusCode.NotFound, (await NewVisitor(factory).GetAsync("/signin/google?code=x&state=y")).StatusCode);
        Assert.Contains("A secret is saved.", await admin.GetStringAsync("/admin/providers/google"));
    }

    [Fact]
    public async Task Apple_needs_its_key_and_ids_and_the_page_never_shows_secrets()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var bad = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/providers/apple", "/admin/providers/apple", new Dictionary<string, string>
        {
            ["Enabled"] = "true",
            ["ClientId"] = "com.example.web",
            ["TeamId"] = "nope",
            ["KeyId"] = "FGHIJ67890",
            ["PrivateKey"] = "not a key",
        });
        var errors = WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync());
        Assert.Contains("Team ID is the 10-character code", errors);
        Assert.Contains("contents of the .p8 file", errors);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var ok = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/providers/apple", "/admin/providers/apple", new Dictionary<string, string>
        {
            ["Enabled"] = "true",
            ["ClientId"] = "com.example.web",
            ["TeamId"] = "ABCDE12345",
            ["KeyId"] = "FGHIJ67890",
            ["PrivateKey"] = pem,
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var page = await admin.GetStringAsync("/admin/providers/apple");
        Assert.DoesNotContain(pem.Split('\n')[1], page); // the key's body is never shown
        Assert.Contains("Saved - leave empty to keep it", page);
        Assert.Contains("Sign in with Apple", await factory.CreateNoRedirectClient().GetStringAsync("/login"));
    }

    // ---- Two-factor authentication -----------------------------------------------------------------------------------

    [Fact]
    public async Task Authenticator_app_and_recovery_codes_guard_the_sign_in()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

        // Set up: scan (read the secret), enter the current code, get recovery codes once.
        var setup = await editor.GetStringAsync(Security + "/authenticator");
        var secret = Hidden(setup, "totp-secret");
        var wrong = await DotNetForgeWebFactory.PostFormAsync(editor, Security + "/authenticator", Security + "/authenticator",
            new Dictionary<string, string> { ["token"] = Input(setup, "token"), ["code"] = "000000" });
        Assert.Contains("That code is not right", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()));
        var added = await DotNetForgeWebFactory.PostFormAsync(editor, Security + "/authenticator", Security + "/authenticator",
            new Dictionary<string, string> { ["token"] = Input(setup, "token"), ["code"] = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        Assert.Equal(Security + "/recovery-codes", added.Headers.Location?.OriginalString);
        var codes = RecoveryCodeRegex().Matches(await editor.GetStringAsync(Security + "/recovery-codes")).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(MfaService.RecoveryCodeCount, codes.Count);
        Assert.Equal(HttpStatusCode.Redirect, (await editor.GetAsync(Security + "/recovery-codes")).StatusCode); // shown once
        await factory.WithDbAsync(async db =>
        {
            var method = await db.UserMfaMethods.SingleAsync();
            Assert.Equal(MfaMethodType.AuthenticatorApp, method.Type);
            Assert.DoesNotContain(secret, method.Secret);
            Assert.False(await db.UserRecoveryCodes.AnyAsync(c => codes.Contains(c.CodeHash)));
        });

        // The password alone no longer signs in: the code page instead.
        var visitor = factory.CreateNoRedirectClient();
        var first = await PasswordAsync(visitor, "editor@example.com", "Edit0rPass1");
        Assert.Equal("/login/mfa", first.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync("/admin")).StatusCode);
        var codePage = await visitor.GetStringAsync("/login/mfa");
        Assert.Contains("Code from your authenticator app", codePage);
        var signedIn = await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa", new Dictionary<string, string>
        {
            ["method"] = Input(codePage, "method"),
            ["code"] = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1),
        });
        Assert.Equal("/admin", signedIn.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/admin")).StatusCode);

        // A recovery code works once.
        var lost = factory.CreateNoRedirectClient();
        await PasswordAsync(lost, "editor@example.com", "Edit0rPass1");
        var recovered = await DotNetForgeWebFactory.PostFormAsync(lost, "/login/mfa?recovery=true", "/login/mfa",
            new Dictionary<string, string> { ["recovery"] = "true", ["code"] = codes[0].ToLowerInvariant().Replace("-", " ") });
        Assert.Equal("/admin", recovered.Headers.Location?.OriginalString);
        var again = factory.CreateNoRedirectClient();
        await PasswordAsync(again, "editor@example.com", "Edit0rPass1");
        var reused = await DotNetForgeWebFactory.PostFormAsync(again, "/login/mfa?recovery=true", "/login/mfa",
            new Dictionary<string, string> { ["recovery"] = "true", ["code"] = codes[0] });
        Assert.Contains("used already", WebUtility.HtmlDecode(await reused.Content.ReadAsStringAsync()));

        await factory.WithDbAsync(async db =>
        {
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MfaMethodAdded && a.Details == "Authenticator app: Authenticator app"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserLogin && a.Details == "with an authenticator app"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserLogin && a.Details == "with a recovery code"));
            Assert.Equal(MfaService.RecoveryCodeCount - 1, await db.UserRecoveryCodes.CountAsync(c => c.UsedDate == null));
        });
    }

    [Fact]
    public async Task Wrong_codes_lock_the_account_like_wrong_passwords()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        await AddAuthenticatorAsync(factory, await factory.SignInAsync("editor@example.com", "Edit0rPass1"));

        var visitor = factory.CreateNoRedirectClient();
        await PasswordAsync(visitor, "editor@example.com", "Edit0rPass1");
        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
        {
            var page = await visitor.GetStringAsync("/login/mfa");
            last = await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa",
                new Dictionary<string, string> { ["method"] = Input(page, "method"), ["code"] = "000000" });
        }

        Assert.Equal("/login", last.Headers.Location?.OriginalString);
        await factory.WithDbAsync(async db => Assert.NotNull((await db.Users.SingleAsync(u => u.Email == "editor@example.com")).LockoutEndUtc));
        Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync("/login/mfa")).StatusCode); // the pending sign-in is gone
    }

    [Fact]
    public async Task Email_codes_are_set_up_and_used_with_the_sign_in_code_template()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await SaveMfaAsync(admin, email: true);

        var send = await DotNetForgeWebFactory.PostFormAsync(admin, Security, Security + "/code/send",
            new Dictionary<string, string> { ["type"] = nameof(MfaMethodType.Email) });
        var form = await send.Content.ReadAsStringAsync();
        var mail = factory.SentEmails.Last();
        Assert.Equal(DotNetForgeWebFactory.AdminEmail, mail.To);
        var code = SixDigits().Match(mail.Subject).Value;
        Assert.Contains(code, mail.Body);
        var on = await DotNetForgeWebFactory.PostFormAsync(admin, Security, Security + "/code", new Dictionary<string, string>
        {
            ["type"] = nameof(MfaMethodType.Email),
            ["target"] = Input(form, "target"),
            ["token"] = Input(form, "token"),
            ["code"] = code,
        });
        Assert.Equal(Security + "/recovery-codes", on.Headers.Location?.OriginalString);

        var visitor = factory.CreateNoRedirectClient();
        await PasswordAsync(visitor, DotNetForgeWebFactory.AdminEmail, DotNetForgeWebFactory.AdminPassword);
        var page = await visitor.GetStringAsync("/login/mfa");
        Assert.Contains("Send code", page);
        var method = Input(page, "method");
        await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa/send", new Dictionary<string, string> { ["method"] = method });
        var signInCode = SixDigits().Match(factory.SentEmails.Last().Subject).Value;
        var done = await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa",
            new Dictionary<string, string> { ["method"] = method, ["code"] = signInCode });
        Assert.Equal("/admin", done.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Sms_codes_go_through_the_sms_sender()
    {
        Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", "AC123");
        Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", "token");
        Environment.SetEnvironmentVariable("TWILIO_FROM", "+15550000000");
        try
        {
            using var baseFactory = new DotNetForgeWebFactory();
            await baseFactory.InstallAsync();
            var sms = new RecordingSms();
            using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISmsSender>(sms)));
            var admin = await SignInAsync(factory, DotNetForgeWebFactory.AdminEmail, DotNetForgeWebFactory.AdminPassword);
            await SaveMfaAsync(admin, sms: true);

            var bad = await DotNetForgeWebFactory.PostFormAsync(admin, Security + "/sms", Security + "/code/send",
                new Dictionary<string, string> { ["type"] = nameof(MfaMethodType.Sms), ["phone"] = "079 123 45 67" });
            Assert.Contains("country code", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
            var send = await DotNetForgeWebFactory.PostFormAsync(admin, Security + "/sms", Security + "/code/send",
                new Dictionary<string, string> { ["type"] = nameof(MfaMethodType.Sms), ["phone"] = "+41 79 123 45 67" });
            var form = await send.Content.ReadAsStringAsync();
            Assert.Equal("+41791234567", sms.Sent.Last().To);
            await DotNetForgeWebFactory.PostFormAsync(admin, Security, Security + "/code", new Dictionary<string, string>
            {
                ["type"] = nameof(MfaMethodType.Sms),
                ["target"] = Input(form, "target"),
                ["token"] = Input(form, "token"),
                ["code"] = SixDigits().Match(sms.Sent.Last().Text).Value,
            });
            await baseFactory.WithDbAsync(async db => Assert.Equal("+41 *** *** 67", (await db.UserMfaMethods.SingleAsync()).Name));

            var visitor = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            await PasswordAsync(visitor, DotNetForgeWebFactory.AdminEmail, DotNetForgeWebFactory.AdminPassword);
            var method = Input(await visitor.GetStringAsync("/login/mfa"), "method");
            await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa/send", new Dictionary<string, string> { ["method"] = method });
            var done = await DotNetForgeWebFactory.PostFormAsync(visitor, "/login/mfa", "/login/mfa",
                new Dictionary<string, string> { ["method"] = method, ["code"] = SixDigits().Match(sms.Sent.Last().Text).Value });
            Assert.Equal("/admin", done.Headers.Location?.OriginalString);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", null);
            Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", null);
            Environment.SetEnvironmentVariable("TWILIO_FROM", null);
        }
    }

    [Fact]
    public async Task Required_mfa_keeps_admin_users_on_sign_in_security_until_they_have_a_method_and_admins_can_reset_it()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var refused = await SaveMfaAsync(admin, requirement: MfaRequirement.AdminUsers, authenticator: false, passkeys: false);
        Assert.Contains("Turn on at least one method", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        await AddAuthenticatorAsync(factory, admin); // else the admin is sent to Sign-in security too
        await SaveMfaAsync(admin, requirement: MfaRequirement.AdminUsers);

        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");
        var blocked = await editor.GetAsync("/admin/pages");
        Assert.Equal(Security, blocked.Headers.Location?.OriginalString);
        Assert.Contains("Two-step verification is required", await editor.GetStringAsync(Security));

        await AddAuthenticatorAsync(factory, editor);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/admin/pages")).StatusCode);

        // Lost phone: an administrator removes every method; the password alone signs in again.
        Guid editorId = default;
        await factory.WithDbAsync(async db => editorId = (await db.Users.SingleAsync(u => u.Email == "editor@example.com")).Id);
        Assert.Contains("Reset two-step verification", await admin.GetStringAsync($"/admin/users/{editorId}"));
        await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/users/{editorId}", $"/admin/users/{editorId}/mfa/reset", new Dictionary<string, string>());
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.UserMfaMethods.AnyAsync(m => m.UserId == editorId));
            Assert.False(await db.UserRecoveryCodes.AnyAsync(c => c.UserId == editorId));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MfaReset && a.Details == "1 method(s) removed"));
        });
        var visitor = factory.CreateNoRedirectClient();
        Assert.Equal("/admin", (await PasswordAsync(visitor, "editor@example.com", "Edit0rPass1")).Headers.Location?.OriginalString);
        Assert.Equal(Security, (await visitor.GetAsync("/admin")).Headers.Location?.OriginalString); // still required: set up again
    }

    [Fact]
    public async Task Passkey_registration_starts_for_app_url_and_refuses_a_forged_credential()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, Security);

        using var optionsRequest = new HttpRequestMessage(HttpMethod.Post, Security + "/passkey/options") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        optionsRequest.Headers.Add("X-CSRF-TOKEN", token);
        using var started = JsonDocument.Parse(await (await admin.SendAsync(optionsRequest)).Content.ReadAsStringAsync());
        using var options = JsonDocument.Parse(started.RootElement.GetProperty("options").GetString()!);
        Assert.Equal("localhost", options.RootElement.GetProperty("rp").GetProperty("id").GetString()); // APP_URL's host
        Assert.False(string.IsNullOrEmpty(options.RootElement.GetProperty("challenge").GetString()));

        using var forged = new HttpRequestMessage(HttpMethod.Post, Security + "/passkey")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                token = started.RootElement.GetProperty("token").GetString(),
                name = "Fake",
                credential = new { id = "AAAA", rawId = "AAAA", type = "public-key", clientExtensionResults = new { }, response = new { clientDataJSON = "e30", attestationObject = "oA" } },
            }), Encoding.UTF8, "application/json"),
        };
        forged.Headers.Add("X-CSRF-TOKEN", token);
        using var result = JsonDocument.Parse(await (await admin.SendAsync(forged)).Content.ReadAsStringAsync());
        Assert.Contains("couldn't be verified", result.RootElement.GetProperty("error").GetString());
        await factory.WithDbAsync(async db => Assert.False(await db.UserMfaMethods.AnyAsync()));
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Redirect, (await PasswordAsync(client, email, password)).StatusCode);
        return client;
    }

    private static HttpClient NewVisitor(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static Task<HttpResponseMessage> PasswordAsync(HttpClient client, string email, string password) =>
        DotNetForgeWebFactory.PostFormAsync(client, "/login", "/login", new Dictionary<string, string> { ["email"] = email, ["password"] = password });

    /// <summary>"Sign in with Google" → the continue page → the provider (skipped: its answer is the callback) → the host's callback.</summary>
    private static async Task<HttpResponseMessage> GoogleRoundTripAsync(HttpClient client, FakeGoogle google)
    {
        var challenge = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/account/external/oauth-google", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
        var page = await challenge.Content.ReadAsStringAsync();
        var target = new Uri(WebUtility.HtmlDecode(RefreshRegex().Match(page).Groups[1].Value));
        Assert.Equal("accounts.google.com", target.Host);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(target.Query);
        Assert.Equal("client-1", query["client_id"]);
        Assert.Equal("https://localhost/signin/google", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        var callback = await client.GetAsync($"/signin/google?code=the-code&state={Uri.EscapeDataString(query["state"]!)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.StartsWith("/account/external/callback", callback.Headers.Location?.OriginalString);
        return await client.GetAsync(callback.Headers.Location);
    }

    private static async Task AddAuthenticatorAsync(DotNetForgeWebFactory factory, HttpClient client)
    {
        var setup = await client.GetStringAsync(Security + "/authenticator");
        var added = await DotNetForgeWebFactory.PostFormAsync(client, Security + "/authenticator", Security + "/authenticator", new Dictionary<string, string>
        {
            ["token"] = Input(setup, "token"),
            ["code"] = Totp.Code(Hidden(setup, "totp-secret"), Totp.StepAt(DateTimeOffset.UtcNow)),
        });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        await factory.WithDbAsync(async db => Assert.True(await db.UserMfaMethods.AnyAsync()));
    }

    private static Task<HttpResponseMessage> SaveMfaAsync(HttpClient admin, bool email = false, bool sms = false, MfaRequirement requirement = MfaRequirement.Optional,
        bool authenticator = true, bool passkeys = true) =>
        DotNetForgeWebFactory.PostFormAsync(admin, "/admin/mfa", "/admin/mfa", new Dictionary<string, string>
        {
            ["AuthenticatorApp"] = authenticator ? "true" : "false",
            ["Passkeys"] = passkeys ? "true" : "false",
            ["Email"] = email ? "true" : "false",
            ["Sms"] = sms ? "true" : "false",
            ["Requirement"] = requirement.ToString(),
        });

    private static string Input(string html, string name) =>
        WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{name}\" value=\"([^\"]*)\"").Groups[1].Value);

    private static string Hidden(string html, string id) =>
        WebUtility.HtmlDecode(Regex.Match(html, $"<span id=\"{id}\" hidden>([^<]*)</span>").Groups[1].Value);

    private static byte[] Base64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    [GeneratedRegex("<code>([A-Z0-9]{5}-[A-Z0-9]{5})</code>")]
    private static partial Regex RecoveryCodeRegex();

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex SixDigits();

    [GeneratedRegex("http-equiv=\"refresh\" content=\"0;url=([^\"]+)\"")]
    private static partial Regex RefreshRegex();

    /// <summary>Google's token and user-info endpoints, answering for whoever <see cref="Email"/> is.</summary>
    private sealed class FakeGoogle : HttpMessageHandler
    {
        public string Email { get; set; } = DotNetForgeWebFactory.AdminEmail;
        public bool Verified { get; set; } = true;
        public string TokenRequest { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == "https://oauth2.googleapis.com/token")
            {
                TokenRequest = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(new { access_token = "access", token_type = "Bearer", expires_in = 3600 });
            }

            if (url.StartsWith("https://openidconnect.googleapis.com/v1/userinfo", StringComparison.Ordinal)
                && request.Headers.Authorization?.Parameter == "access")
            {
                return Json(new { sub = "google-1", email = Email, email_verified = Verified, given_name = "New", family_name = "Person" });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    private sealed class RecordingSms : ISmsSender
    {
        public List<(string To, string Text)> Sent { get; } = new();

        public bool IsConfigured => true;

        public Task SendAsync(string phoneNumber, string text, CancellationToken ct = default)
        {
            Sent.Add((phoneNumber, text));
            return Task.CompletedTask;
        }
    }
}
