using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DotNetForge.Data;
using DotNetForge.PageBuilder;
using DotNetForge.PageBuilder.Rendering;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Web.Services;

/// <summary>Result of a draft save: the new version, or a conflict with whoever saved in between.</summary>
public sealed record DraftSaveResult(Guid? Version, DateTime? SavedOn, string? Error, DraftConflict? Conflict);

public sealed record DraftConflict(string? SavedBy, DateTime? SavedOn, Guid? Version);

/// <summary>A page's published layout, rendered for one kind of viewer.</summary>
public sealed record RenderedLayout(string Html, string CssHash);

/// <summary>
/// The page builder's storage and rendering (.docs/features/page-builder.md). A page has a <b>draft</b>
/// (<see cref="Page.DraftLayoutJson"/>, saved by the editor every few seconds) and a <b>published</b> layout
/// (<see cref="Page.LayoutJson"/>, what visitors see). Every document is normalized by
/// <see cref="DocumentValidator"/> before it is stored, and again before it is rendered, so nothing the browser sends
/// reaches the site unchecked. Rendering uses the same schema as the editor (<c>DotNetForge.PageBuilder</c>).
/// </summary>
public sealed class PageBuilderService
{
    // Published documents are parsed and their CSS built once per layout version, not per request.
    private static readonly ConcurrentDictionary<string, (PageDocument Doc, string Css)> Cache = new(StringComparer.Ordinal);
    private const int MaxCacheEntries = 500;

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;

    public PageBuilderService(DotNetForgeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>The document the editor opens: the draft, else the published layout, else nothing (a new layout).</summary>
    public static string? EditableJson(Page page) => page.DraftLayoutJson ?? page.LayoutJson;

    /// <summary>Whether the draft differs from what visitors see.</summary>
    public static bool HasPendingChanges(Page page) =>
        page.DraftLayoutJson is not null && !string.Equals(page.DraftLayoutJson, page.LayoutJson, StringComparison.Ordinal);

    /// <summary>Validates and stores the editor's draft. Refuses a save based on an older version unless forced.</summary>
    public async Task<DraftSaveResult> SaveDraftAsync(Page page, JToken? document, Guid? baseVersion, bool force, Guid? userId, CancellationToken ct = default)
    {
        if (document is null || document.Type != JTokenType.Object)
        {
            return new DraftSaveResult(null, null, "The layout is missing.", null);
        }

        string json;
        try
        {
            json = DocumentValidator.Normalize(document).ToJsonString();
        }
        catch (DocumentException e)
        {
            return new DraftSaveResult(null, null, "The layout is not valid: " + e.Message, null);
        }

        if (Encoding.UTF8.GetByteCount(json) > Schema.MaxDocumentBytes)
        {
            return new DraftSaveResult(null, null, "The layout is too large.", null);
        }

        if (!force && page.DraftVersion is { } current && current != baseVersion)
        {
            var by = page.DraftUpdatedById is { } id
                ? await _db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync(ct)
                : null;
            return new DraftSaveResult(null, null, null, new DraftConflict(by, page.DraftUpdatedDate, page.DraftVersion));
        }

        page.DraftLayoutJson = json;
        page.DraftVersion = Guid.NewGuid();
        page.DraftUpdatedDate = DateTime.UtcNow;
        page.DraftUpdatedById = userId;
        await _db.SaveChangesAsync(ct);
        return new DraftSaveResult(page.DraftVersion, page.DraftUpdatedDate, null, null);
    }

    /// <summary>Makes the draft what visitors see. Null when published, else the error.</summary>
    public async Task<string?> PublishAsync(Page page, CancellationToken ct = default)
    {
        var json = EditableJson(page);
        if (json is null)
        {
            return "There is no layout to publish yet.";
        }

        try
        {
            json = DocumentValidator.Parse(json).ToJsonString();
        }
        catch (DocumentException e)
        {
            return "The layout is not valid: " + e.Message;
        }

        page.LayoutJson = json;
        page.DraftLayoutJson = json;
        page.LayoutPublishedDate = DateTime.UtcNow;
        page.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.ContentUpdated, "Page", page.Id.ToString(), page.Title, details: "Layout published", cancellationToken: ct);
        return null;
    }

    /// <summary>Visitors get the page's default view again; the draft is kept.</summary>
    public async Task UnpublishAsync(Page page, CancellationToken ct = default)
    {
        page.DraftLayoutJson ??= page.LayoutJson;
        page.LayoutJson = null;
        page.LayoutPublishedDate = null;
        page.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.ContentUpdated, "Page", page.Id.ToString(), page.Title, details: "Layout unpublished", cancellationToken: ct);
    }

    /// <summary>Throws the draft away: the editor continues from the published layout (or an empty page).</summary>
    public async Task DiscardDraftAsync(Page page, Guid? userId, CancellationToken ct = default)
    {
        page.DraftLayoutJson = page.LayoutJson;
        page.DraftVersion = Guid.NewGuid();
        page.DraftUpdatedDate = DateTime.UtcNow;
        page.DraftUpdatedById = userId;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A layout (normally <see cref="Page.LayoutJson"/>; the draft for the editor's preview) as HTML for a viewer, or
    /// null when there is none (or it can't be read - the page then falls back to its default view instead of failing).
    /// </summary>
    public static RenderedLayout? Render(string? json, bool signedIn, JObject? content = null)
    {
        if (Load(json) is not { } loaded)
        {
            return null;
        }

        var html = HtmlRenderer.Render(loaded.Doc, new RenderContext
        {
            // The entries of the layout's content lists (ContentRenderService).
            Data = content,
            Mode = RenderMode.View,
            IsVisible = node => node.Visibility.Audience switch
            {
                "anonymous" => !signedIn,
                "authenticated" => signedIn,
                _ => true,
            },
            HasModule = _ => false,
        });
        return new RenderedLayout(html, Hash(json!));
    }

    /// <summary>A layout's parsed document (cached like its CSS), or null when there is none or it can't be read.</summary>
    public static PageDocument? Document(string? json) => Load(json)?.Doc;

    /// <summary>A layout's stylesheet (the published one is served from <c>/page-css/{id}.css</c>), or null.</summary>
    public static string? Css(string? json) => Load(json)?.Css;

    /// <summary>A short content hash: changes whenever the published layout does, so its CSS URL can be cached forever.</summary>
    public static string Hash(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16].ToLowerInvariant();

    private static (PageDocument Doc, string Css)? Load(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var key = Hash(json);
        if (Cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        try
        {
            var doc = DocumentValidator.Parse(json);
            var entry = (doc, CssBuilder.DocumentCss(doc));
            if (Cache.Count >= MaxCacheEntries)
            {
                Cache.Clear();
            }

            Cache[key] = entry;
            return entry;
        }
        catch (DocumentException)
        {
            return null;
        }
    }
}
