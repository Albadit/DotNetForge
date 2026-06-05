namespace DotNetForge.Abstractions.Messaging;

/// <summary>A simple outbound email message.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public bool IsHtml { get; init; } = true;
}

/// <summary>Sends transactional email via the configured SMTP provider (email.md).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Abstracts media storage; local disk is the default provider (file_manager.md).</summary>
public interface IFileStorage
{
    /// <summary>Persists a file and returns the storage-relative path.</summary>
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default);

    Stream? OpenRead(string relativePath);

    bool Delete(string relativePath);
}
