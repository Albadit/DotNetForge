namespace DotNetForge.Abstractions.Messaging;

/// <summary>A simple outbound email message.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public bool IsHtml { get; init; } = true;

    /// <summary>The email template it was rendered from (e.g. <c>password-reset</c>), for the email log; null otherwise.</summary>
    public string? Template { get; init; }
}

/// <summary>Sends transactional email via the configured SMTP provider (.docs/features/email.md).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
