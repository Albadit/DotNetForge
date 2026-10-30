namespace DotNetForge.Shared.Entities;

/// <summary>
/// One sign-in (.docs/features/authentication.md#sessions): the cookie holds its id, and it counts only while this row
/// exists, so signing out ends it on the server too. No foreign key to <see cref="User"/>: a Data Management import
/// replaces the users, while the people signed in stay signed in when their ids survive it.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>The latest it can last, however often the cookie is renewed.</summary>
    public DateTime ExpiresDate { get; set; }
}
