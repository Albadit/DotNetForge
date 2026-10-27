using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using DotNetForge.Abstractions.Messaging;
using DotNetForge.Core.Email;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Email → Configuration (.docs/pages/email-configuration.md): the <c>SMTP_*</c> settings of <c>.env</c> shown
/// read-only, the connection test, test sends (plain or a template with sample data), the email log and the Mailpit link.
/// </summary>
public sealed class EmailConfigurationTests
{
    private const string Screen = "/admin/email/configuration";

    [Fact]
    public async Task The_settings_from_env_are_shown_read_only()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(Screen));
        Assert.Contains("smtp.test.invalid:587", html);
        Assert.Contains("read-only here", html);
        Assert.DoesNotContain("name=\"Host\"", html);
        Assert.DoesNotContain("Save settings", html);
        Assert.DoesNotContain("Open Mailpit inbox", html);

        // Nothing on the screen saves settings any more.
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, Screen);
        var post = await admin.PostAsync(Screen, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Host"] = "evil.example.com",
        }));
        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, post.StatusCode.ToString());
    }

    [Fact]
    public async Task Test_sends_go_out_and_every_send_is_logged()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var plain = await SendTestAsync(admin, "check@example.com");
        Assert.Equal(HttpStatusCode.Redirect, plain.StatusCode);
        var template = await SendTestAsync(admin, "check@example.com", EmailTemplateCatalog.PasswordReset);
        Assert.Equal(HttpStatusCode.Redirect, template.StatusCode);

        Assert.Equal(2, factory.SentEmails.Count);
        Assert.StartsWith("Test email from", factory.SentEmails[0].Subject);
        Assert.StartsWith("[Test] ", factory.SentEmails[1].Subject);
        Assert.Contains("https://cms.example.com/reset-password?token=sample", factory.SentEmails[1].Body);

        await factory.WithDbAsync(async db =>
        {
            var log = await db.EmailLog.OrderBy(e => e.SentAt).ToListAsync();
            Assert.Equal(2, log.Count);
            Assert.All(log, e => Assert.True(e.Success));
            Assert.Null(log[0].Template);
            Assert.Equal(EmailTemplateCatalog.PasswordReset, log[1].Template);
            Assert.Equal("check@example.com", log[1].Recipient);
        });

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(Screen));
        Assert.Contains("Email log", html);
        Assert.Contains("check@example.com", html);
        Assert.Contains("Password reset", html);

        var invalid = await SendTestAsync(admin, "not-an-address");
        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        Assert.Equal(2, factory.SentEmails.Count);
    }

    [Fact]
    public async Task A_failed_send_is_logged_with_the_reason_but_not_the_servers_text()
    {
        using var baseFactory = new DotNetForgeWebFactory();
        await baseFactory.InstallAsync();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddKeyedSingleton<IEmailSender>(LoggingEmailSender.TransportKey, new FailingSender())));
        var admin = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        await DotNetForgeWebFactory.PostFormAsync(admin, "/login", "/login", new Dictionary<string, string>
        {
            ["email"] = DotNetForgeWebFactory.AdminEmail,
            ["password"] = DotNetForgeWebFactory.AdminPassword,
        });

        await SendTestAsync(admin, "check@example.com");

        await baseFactory.WithDbAsync(async db =>
        {
            var entry = await db.EmailLog.SingleAsync();
            Assert.False(entry.Success);
            Assert.Contains("refused the sign-in (535)", entry.Error);
            Assert.DoesNotContain("secret detail", entry.Error);
        });
    }

    [Fact]
    public async Task The_connection_test_shows_the_failing_step()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // smtp.test.invalid never resolves: the first step fails and the steps are shown.
        var response = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/connection", new Dictionary<string, string>());
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The connection test failed", html);
        Assert.Contains("Could not connect to smtp.test.invalid:587", html);
        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a => a.Details == "Connection test failed")));
    }

    [Fact]
    public async Task In_development_a_local_mailpit_gets_an_inbox_link()
    {
        using var factory = new DotNetForgeWebFactory();
        Environment.SetEnvironmentVariable("SMTP_HOST", "localhost");
        Environment.SetEnvironmentVariable("SMTP_PORT", "1026");
        try
        {
            await factory.InstallAsync(); // the host builds now, with these SMTP_*
            var admin = await factory.SignInAsync();
            var html = await admin.GetStringAsync(Screen);
            Assert.Contains("Open Mailpit inbox", html);
            Assert.Contains("http://localhost:8026", html);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_PORT", null);
            Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.test.invalid");
        }
    }

    [Fact]
    public async Task Without_smtp_in_env_email_is_off()
    {
        using var factory = new DotNetForgeWebFactory();
        Environment.SetEnvironmentVariable("SMTP_HOST", null);
        Environment.SetEnvironmentVariable("SMTP_FROM", null);
        try
        {
            await factory.InstallAsync();
            var admin = await factory.SignInAsync();
            Assert.Contains("Email is off", await admin.GetStringAsync(Screen));
            using var visitor = factory.CreateNoRedirectClient();
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/forgot-password")).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", "smtp.test.invalid");
            Environment.SetEnvironmentVariable("SMTP_FROM", "cms@example.com");
        }
    }

    [Fact]
    public async Task Only_the_settings_permission_opens_the_screen()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Ed1torPassword!", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Ed1torPassword!");
        Assert.Contains("/account/denied", (await editor.GetAsync(Screen)).Headers.Location?.OriginalString);
    }

    [Fact]
    public void Send_failures_are_described_without_the_servers_text()
    {
        var settings = new EmailSettings { Host = "smtp.example.com", Port = 587 };
        Assert.Contains("did not answer in time", EmailConfigurationService.DescribeFailure(new TimeoutException(), settings));
        Assert.Contains("Could not connect to smtp.example.com:587",
            EmailConfigurationService.DescribeFailure(new SmtpException("x", new SocketException()), settings));
        var auth = EmailConfigurationService.DescribeFailure(new SmtpException((SmtpStatusCode)535, "5.7.8 secret detail"), settings);
        Assert.Contains("refused the sign-in (535)", auth);
        Assert.DoesNotContain("secret detail", auth);
    }

    private static Task<HttpResponseMessage> SendTestAsync(HttpClient admin, string recipient, string? template = null) =>
        DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/test", new Dictionary<string, string>
        {
            ["recipient"] = recipient,
            ["template"] = template ?? string.Empty,
        });

    private sealed class FailingSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            throw new SmtpException((SmtpStatusCode)535, "5.7.8 secret detail");
    }
}
