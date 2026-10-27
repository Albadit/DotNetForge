namespace DotNetForge.Shared.Entities;

/// <summary>
/// One email the CMS tried to send (Email → Configuration, .docs/pages/email-configuration.md#email-log): who to,
/// what, and whether it went out. No bodies are stored; entries older than 30 days are removed.
/// </summary>
public class EmailLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string Recipient { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    /// <summary>The email template's key (e.g. <c>password-reset</c>); null for a plain test email.</summary>
    public string? Template { get; set; }

    public bool Success { get; set; }

    /// <summary>Why it failed, in the CMS's own words (never the server's text).</summary>
    public string? Error { get; set; }
}
