using System.Net.Mail;
using System.Net.Sockets;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Web.Services;

/// <summary>
/// The SMTP settings of the CMS (.docs/features/email.md): only the <c>SMTP_*</c> values of <c>.env</c>
/// (<see cref="AppEnvironment.Email"/>), like the database and the storage - Email → Configuration shows them read-only
/// (.docs/pages/email-configuration.md). Also turns send failures into messages for administrators.
/// </summary>
public sealed class EmailConfigurationService : IEmailSettingsSource
{
    private readonly AppEnvironment _env;
    private readonly IHostEnvironment _host;

    public EmailConfigurationService(AppEnvironment env, IHostEnvironment host)
    {
        _env = env;
        _host = host;
    }

    /// <summary>The settings in effect: <c>SMTP_*</c> from <c>.env</c>.</summary>
    public EmailSettings Settings => _env.Email;

    public Task<EmailSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_env.Email);

    /// <summary>
    /// The web inbox of a local Mailpit (Development only, SMTP on localhost port 1025 or 1026 - the ports of
    /// docker/mail/mailpit.yml and Mailpit's default): its UI is on the SMTP port + 7000. Null otherwise.
    /// </summary>
    public string? MailpitInboxUrl =>
        _host.IsDevelopment() && Settings.IsConfigured &&
        Settings.Host is "localhost" or "127.0.0.1" or "::1" && Settings.Port is 1025 or 1026
            ? $"http://localhost:{Settings.Port + 7000}"
            : null;

    /// <summary>What went wrong, for the administrator - without the server's own text (it can echo addresses).</summary>
    public static string DescribeFailure(Exception ex, EmailSettings settings) => ex switch
    {
        TimeoutException => $"The SMTP server {settings.Host}:{settings.Port} did not answer in time. Check the host, the port and the firewall.",
        SmtpFailedRecipientException => "The server refused the recipient address.",
        SmtpException { InnerException: SocketException or IOException } or SocketException =>
            $"Could not connect to {settings.Host}:{settings.Port}. Check the host, the port and TLS.",
        SmtpException smtp when (int)smtp.StatusCode is 530 or 534 or 535 =>
            $"The server refused the sign-in ({(int)smtp.StatusCode}). Check the username and password.",
        SmtpException smtp when (int)smtp.StatusCode > 0 =>
            $"The server refused the message ({(int)smtp.StatusCode} {smtp.StatusCode}).",
        SmtpException => "Sending failed - check the host, the port, TLS and the credentials.",
        _ => $"Sending failed ({ex.GetType().Name}).",
    };
}
