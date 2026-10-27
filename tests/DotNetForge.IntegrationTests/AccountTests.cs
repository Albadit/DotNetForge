using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Sign-in at <c>/login</c>, self-service accounts (registration, password reset, email confirmation) and the
/// Advanced User Settings that govern them (.docs/features/authentication.md, .docs/pages/advanced-settings.md).
/// </summary>
public sealed class AccountTests
{
    private const string NewPassword = "N3wPassword!";

    [Fact]
    public async Task The_old_sign_in_address_redirects_to_login()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var old = await client.GetAsync("/account/login?returnUrl=%2Fadmin%2Fpages");
        Assert.Equal(HttpStatusCode.MovedPermanently, old.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fadmin%2Fpages", old.Headers.Location?.OriginalString);

        var admin = await client.GetAsync("/admin");
        Assert.Contains("/login?ReturnUrl=", admin.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Sign_ups_are_off_by_default()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/register")).StatusCode);
        Assert.DoesNotContain("href=\"/register\"", await client.GetStringAsync("/login"));
        var posted = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/register", RegisterForm("visitor@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        await factory.WithDbAsync(async db => Assert.False(await db.Users.AnyAsync(u => u.Email == "visitor@example.com")));
    }

    [Fact]
    public async Task With_sign_ups_on_a_visitor_registers_with_the_default_role_once_per_email()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await SaveSettingsAsync(factory, await factory.SignInAsync(), signUps: true);
        using var visitor = factory.CreateNoRedirectClient();

        Assert.Contains("href=\"/register\"", await visitor.GetStringAsync("/login"));
        var registered = await DotNetForgeWebFactory.PostFormAsync(visitor, "/register", "/register", RegisterForm("visitor@example.com"));

        // Signed in right away (no email confirmation), on the site: Authenticated doesn't open the admin.
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal("/", registered.Headers.Location?.OriginalString);
        var home = await visitor.GetStringAsync("/");
        Assert.Contains("action=\"/account/logout\"", home);
        Assert.DoesNotContain("href=\"/admin\"", home);
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == "visitor@example.com");
            Assert.True(user.EmailConfirmed);
            var roleId = await db.UserRoles.Where(r => r.UserId == user.Id).Select(r => r.RoleId).SingleAsync();
            Assert.Equal(Roles.Authenticated, await db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync());
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserRegistered));
        });

        // One account per email, whatever the case.
        using var second = factory.CreateNoRedirectClient();
        var duplicate = await DotNetForgeWebFactory.PostFormAsync(second, "/register", "/register", RegisterForm("Visitor@Example.com"));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("An account with this email already exists", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));
        await factory.WithDbAsync(async db => Assert.Equal(1, (await db.Users.Select(u => u.Email).ToListAsync())
            .Count(e => string.Equals(e, "visitor@example.com", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public async Task Forgot_password_answers_neutrally_and_the_reset_link_works_once()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();
        Assert.Contains("href=\"/forgot-password\"", await client.GetStringAsync("/login"));

        var unknown = await DotNetForgeWebFactory.PostFormAsync(client, "/forgot-password", "/forgot-password",
            new Dictionary<string, string> { ["email"] = "nobody@example.com" });
        var known = await DotNetForgeWebFactory.PostFormAsync(client, "/forgot-password", "/forgot-password",
            new Dictionary<string, string> { ["email"] = DotNetForgeWebFactory.AdminEmail.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Contains("Check your email", await unknown.Content.ReadAsStringAsync());
        Assert.Contains("Check your email", await known.Content.ReadAsStringAsync());

        var email = Assert.Single(factory.SentEmails);
        Assert.Equal(DotNetForgeWebFactory.AdminEmail, email.To);
        var token = TokenOf(email.Body, "http://localhost/reset-password");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/reset-password?token={token}")).StatusCode);
        var reset = await PostResetAsync(client, token, NewPassword);
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/login", reset.Headers.Location?.OriginalString);

        // The new password works; the link doesn't any more (the password it was made for has changed).
        await factory.SignInAsync(DotNetForgeWebFactory.AdminEmail, NewPassword);
        var again = await PostResetAsync(client, token, "An0therPassword!");
        Assert.Contains("This link doesn&#x27;t work", await again.Content.ReadAsStringAsync());
        await factory.SignInAsync(DotNetForgeWebFactory.AdminEmail, NewPassword);
    }

    [Fact]
    public async Task With_email_confirmation_on_a_new_account_signs_in_only_after_confirming()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await SaveSettingsAsync(factory, await factory.SignInAsync(), signUps: true, emailConfirmation: true);
        using var visitor = factory.CreateNoRedirectClient();

        var registered = await DotNetForgeWebFactory.PostFormAsync(visitor, "/register", "/register", RegisterForm("new@example.com"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Contains("Check your email", await registered.Content.ReadAsStringAsync());
        var token = TokenOf(Assert.Single(factory.SentEmails).Body, "http://localhost/confirm-email");

        var refused = await DotNetForgeWebFactory.PostFormAsync(visitor, "/login", "/login",
            new Dictionary<string, string> { ["email"] = "new@example.com", ["password"] = NewPassword });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Confirm your email first", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));

        var confirmed = await visitor.GetAsync($"/confirm-email?token={token}");
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal("/login", confirmed.Headers.Location?.OriginalString);
        await factory.SignInAsync("new@example.com", NewPassword);

        // Accounts that existed before (the installer's Super Admin) are not affected.
        await factory.SignInAsync();
    }

    [Fact]
    public async Task Advanced_settings_validate_their_input_and_need_the_settings_permission()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var superAdmin = await factory.SignInAsync();
        Assert.Contains("Enable sign-ups", await superAdmin.GetStringAsync("/admin/advanced-settings"));

        var badUrl = await SaveSettingsAsync(factory, superAdmin, signUps: true, resetPasswordUrl: "not a url");
        Assert.Equal(HttpStatusCode.OK, badUrl.StatusCode);
        Assert.Contains("must be an absolute http(s) address", await badUrl.Content.ReadAsStringAsync());

        // An Admin may change the settings, but can't make sign-ups land in a role that opens the admin.
        await factory.CreateUserAsync("admin2@example.com", "Adm1nPassword!", Roles.Admin);
        var admin = await factory.SignInAsync("admin2@example.com", "Adm1nPassword!");
        var escalation = await SaveSettingsAsync(factory, admin, signUps: true, role: Roles.Admin);
        Assert.Contains("Only a Super Admin can give new accounts a role that opens the admin", await escalation.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await SaveSettingsAsync(factory, admin, signUps: true)).StatusCode);

        await factory.CreateUserAsync("editor@example.com", "Ed1torPassword!", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Ed1torPassword!");
        Assert.Contains("/account/denied", (await editor.GetAsync("/admin/advanced-settings")).Headers.Location?.OriginalString);
    }

    private static Dictionary<string, string> RegisterForm(string email) => new()
    {
        ["FirstName"] = "New",
        ["LastName"] = "Visitor",
        ["Email"] = email,
        ["Password"] = NewPassword,
        ["ConfirmPassword"] = NewPassword,
    };

    private static async Task<HttpResponseMessage> SaveSettingsAsync(DotNetForgeWebFactory factory, HttpClient admin,
        bool signUps, bool emailConfirmation = false, string role = Roles.Authenticated, string? resetPasswordUrl = null)
    {
        Guid roleId = default;
        await factory.WithDbAsync(async db => roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync());
        return await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/advanced-settings", "/admin/advanced-settings", new Dictionary<string, string>
        {
            ["SignUps"] = signUps ? "true" : "false",
            ["DefaultRoleId"] = roleId.ToString(),
            ["EmailConfirmation"] = emailConfirmation ? "true" : "false",
            ["ResetPasswordUrl"] = resetPasswordUrl ?? string.Empty,
            ["ConfirmationRedirectUrl"] = string.Empty,
        });
    }

    private static Task<HttpResponseMessage> PostResetAsync(HttpClient client, string token, string password) =>
        client.PostAsync("/reset-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["password"] = password,
            ["confirmPassword"] = password,
        }));

    /// <summary>The (still URL-encoded) token of the link to <paramref name="target"/> in an email body.</summary>
    private static string TokenOf(string body, string target)
    {
        var match = Regex.Match(WebUtility.HtmlDecode(body), Regex.Escape(target) + @"\?token=([^""]+)");
        Assert.True(match.Success, $"No {target} link in the email.");
        return match.Groups[1].Value;
    }
}
