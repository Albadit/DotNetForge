using System.Net;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>The sign-in screen's feedback and "Remember me" (.docs/pages/login.md, .docs/features/authentication.md).</summary>
public sealed class SignInTests
{
    [Fact]
    public async Task A_failed_sign_in_shows_an_error_toast_and_keeps_the_email()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var response = await DotNetForgeWebFactory.PostFormAsync(client, "/account/login", "/account/login",
            new Dictionary<string, string> { ["email"] = DotNetForgeWebFactory.AdminEmail, ["password"] = "wrong-password" });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("toast toast-danger", html);
        Assert.Contains("Invalid email or password.", html);
        Assert.DoesNotContain("validation-summary-errors", html);
        Assert.Contains($"value=\"{DotNetForgeWebFactory.AdminEmail}\"", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remember_me_makes_the_cookie_persistent(bool rememberMe)
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();
        var fields = new Dictionary<string, string>
        {
            ["email"] = DotNetForgeWebFactory.AdminEmail,
            ["password"] = DotNetForgeWebFactory.AdminPassword,
        };
        if (rememberMe)
        {
            fields["rememberMe"] = "true";
        }

        var response = await DotNetForgeWebFactory.PostFormAsync(client, "/account/login", "/account/login", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dnf.auth=", StringComparison.Ordinal));
        Assert.Equal(rememberMe, cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }
}
