using System.Net;
using System.Net.Mail;
using DotNetForge.Abstractions.Messaging;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Infrastructure.Messaging;

/// <summary>
/// <see cref="IEmailSender"/> over SMTP (.docs/features/email.md) with the settings in effect
/// (<see cref="IEmailSettingsSource"/>: <c>SMTP_*</c> from <c>.env</c>). Uses the BCL
/// <see cref="SmtpClient"/> (STARTTLS when <see cref="EmailSettings.UseTls"/>; implicit TLS on port 465 is not
/// supported - use 587). Throws when email is not configured: callers check <see cref="EmailSettings.IsConfigured"/>
/// first and hide what needs email.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>How long one send may take before it fails (the BCL default is 100 s).</summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    private readonly IEmailSettingsSource _source;
    private readonly string _appName;

    public SmtpEmailSender(IEmailSettingsSource source, AppEnvironment env)
    {
        _source = source;
        _appName = env.AppName;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = await _source.GetAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Email is not configured: set SMTP_HOST and SMTP_FROM in .env.");
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress!, string.IsNullOrWhiteSpace(settings.FromName) ? _appName : settings.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsHtml,
        };
        mail.To.Add(message.To);

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)SendTimeout.TotalMilliseconds,
        };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        try
        {
            await client.SendMailAsync(mail, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout, not the caller's cancellation: report it as a failed send.
            throw new TimeoutException($"The SMTP server did not answer within {SendTimeout.TotalSeconds:0} seconds.");
        }
    }
}
