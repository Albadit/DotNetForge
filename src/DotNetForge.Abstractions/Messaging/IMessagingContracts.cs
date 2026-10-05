namespace DotNetForge.Abstractions.Messaging;

/// <summary>A simple outbound email message.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public bool IsHtml { get; init; } = true;
}

/// <summary>Sends transactional email via the configured SMTP provider (.docs/features/email.md).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
