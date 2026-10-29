using System.Text.Json;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services.Webhooks;

/// <summary>
/// The JSON body of a delivery (.docs/features/webhooks.md#payload) and which audit actions raise which event:
/// <c>{ "id", "event", "createdAt", "entity": { "type", "id", "name" }, "data" }</c>. <c>id</c> is the event id - the
/// same for every retry, so receivers can deduplicate; <c>data</c> is the entry or media file as it is when the event
/// is queued (null after a delete).
/// </summary>
public static class WebhookPayload
{
    /// <summary>The event of a test delivery (not one of the eight; never raised by a change).</summary>
    public const string TestEvent = "webhook.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The webhook event an audit action raises, or null. Entries come from the Content Manager and the API alike.</summary>
    public static string? EventFor(string auditAction) => auditAction switch
    {
        AuditActions.EntryCreated => WebhookEvents.EntryCreate,
        AuditActions.EntryUpdated => WebhookEvents.EntryUpdate,
        AuditActions.EntryDeleted => WebhookEvents.EntryDelete,
        AuditActions.EntryPublished => WebhookEvents.EntryPublish,
        AuditActions.EntryUnpublished => WebhookEvents.EntryUnpublish,
        AuditActions.MediaUploaded => WebhookEvents.MediaCreate,
        AuditActions.MediaUpdated or AuditActions.MediaMoved => WebhookEvents.MediaUpdate,
        AuditActions.MediaDeleted => WebhookEvents.MediaDelete,
        _ => null,
    };

    public static string Build(Guid eventId, string eventKey, DateTime occurredAt, string entityType, string? entityId, string? entityName, object? data) =>
        JsonSerializer.Serialize(new
        {
            id = eventId,
            @event = eventKey,
            createdAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
            entity = new { type = entityType, id = entityId, name = entityName },
            data,
        }, Json);

    public static string Test(Guid eventId, Webhook webhook) =>
        Build(eventId, TestEvent, DateTime.UtcNow, "webhook", webhook.Id.ToString(), webhook.Name,
            new { message = "A test delivery from the Webhooks screen. Check the signature with your secret.", events = webhook.Events });

    /// <summary>The entry or media file the event is about, as the payload's <c>data</c>; null when it no longer exists.</summary>
    public static async Task<object?> DataAsync(DotNetForgeDbContext db, string eventKey, string? entityId, string appUrl, CancellationToken ct)
    {
        if (!Guid.TryParse(entityId, out var id) || eventKey is WebhookEvents.EntryDelete or WebhookEvents.MediaDelete)
        {
            return null;
        }

        if (eventKey.StartsWith("entry.", StringComparison.Ordinal))
        {
            var entry = await db.ContentEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (entry is null)
            {
                return null;
            }

            var type = await db.ContentTypes.AsNoTracking().Where(t => t.Id == entry.ContentTypeId).Select(t => t.Slug).FirstOrDefaultAsync(ct);
            return new
            {
                contentType = type,
                entry.Title,
                entry.Published,
                entry.HasDraft,
                publishedAt = entry.PublishedDate,
                createdAt = entry.CreatedDate,
                updatedAt = entry.UpdatedDate,
                data = Parse(entry.DataJson),
                publishedData = entry.PublishedDataJson is null ? null : Parse(entry.PublishedDataJson),
            };
        }

        var file = await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        return file is null
            ? null
            : new
            {
                file.FileName,
                file.OriginalName,
                file.ContentType,
                file.SizeBytes,
                file.IsPublic,
                file.FolderId,
                url = appUrl.TrimEnd('/') + MediaService.UrlFor(file),
                uploadedAt = file.UploadedDate,
            };
    }

    private static JsonElement? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
