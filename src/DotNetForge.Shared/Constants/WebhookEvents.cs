namespace DotNetForge.Shared.Constants;

/// <summary>
/// The exactly-eight webhook event keys (owned by .docs/features/webhooks.md). A webhook subscribes to one
/// or more of these and fires a signed POST when a matching event occurs.
/// </summary>
public static class WebhookEvents
{
    public const string EntryCreate = "entry.create";
    public const string EntryUpdate = "entry.update";
    public const string EntryDelete = "entry.delete";
    public const string EntryPublish = "entry.publish";
    public const string EntryUnpublish = "entry.unpublish";
    public const string MediaCreate = "media.create";
    public const string MediaUpdate = "media.update";
    public const string MediaDelete = "media.delete";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EntryCreate, EntryUpdate, EntryDelete, EntryPublish, EntryUnpublish,
        MediaCreate, MediaUpdate, MediaDelete,
    };

    public static bool IsValid(string eventKey) => All.Contains(eventKey);

    /// <summary>The HMAC signature header attached to every delivery (.docs/features/webhooks.md).</summary>
    public const string SignatureHeader = "X-DotNetForge-Signature";
}
