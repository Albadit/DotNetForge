using DotNetForge.Abstractions.Messaging;

namespace DotNetForge.Infrastructure.Messaging;

/// <summary>
/// A development-friendly <see cref="IEmailSender"/> that writes outgoing messages to
/// <c>storage/logs/email</c> instead of contacting an SMTP server. This keeps the foundation
/// dependency-free and lets the email flows (password reset, confirmation, account locked) be
/// exercised without external infrastructure. Wire a real SMTP sender via configuration for
/// production (email.md).
/// </summary>
public sealed class FileSystemEmailSender : IEmailSender
{
    private readonly string _outboxDir;

    public FileSystemEmailSender(string outboxDir)
    {
        _outboxDir = outboxDir;
        Directory.CreateDirectory(_outboxDir);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var safeTo = string.Concat(message.To.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '@'));
        var path = Path.Combine(_outboxDir, $"{stamp}_{safeTo}.txt");

        var contents =
            $"To: {message.To}\n" +
            $"Subject: {message.Subject}\n" +
            $"Content-Type: {(message.IsHtml ? "text/html" : "text/plain")}\n" +
            $"Date: {DateTime.UtcNow:O}\n\n" +
            message.Body;

        await File.WriteAllTextAsync(path, contents, cancellationToken);
    }
}
