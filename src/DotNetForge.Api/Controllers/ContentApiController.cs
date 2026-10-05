using DotNetForge.Api.Authorization;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Api.Controllers;

/// <summary>Headless content endpoints (.docs/features/headless-api.md). All access is gated by token permissions.</summary>
[Route("api/content")]
public sealed class ContentApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly IPageService _pages;
    private readonly IAuditService _audit;

    public ContentApiController(DotNetForgeDbContext db, IPageService pages, IAuditService audit)
    {
        _db = db;
        _pages = pages;
        _audit = audit;
    }

    /// <summary>GET /api/content/pages - lists pages in the token's tenant.</summary>
    [HttpGet("pages")]
    [RequireApiPermission(PermissionKeys.ContentRead)]
    public async Task<IActionResult> GetPages()
    {
        var pages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.TenantId == TenantId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Title,
                p.Published,
                p.DisplayInMenu,
                Type = p.PageType.ToString(),
                p.UpdatedDate,
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(pages);
    }

    /// <summary>GET /api/content/{type} - lists entries of a content type (foundation: pages).</summary>
    [HttpGet("{type}")]
    [RequireApiPermission(PermissionKeys.ContentRead)]
    public async Task<IActionResult> GetByType(string type)
    {
        if (string.Equals(type, "pages", StringComparison.OrdinalIgnoreCase))
        {
            return await GetPages();
        }

        // Collection/Single types are part of the content-manager foundation; none seeded yet.
        return Ok(Array.Empty<object>());
    }

    /// <summary>
    /// POST /api/content/pages - creates an unpublished root-level page in the token's tenant. The same rules as the
    /// Content Manager apply (<see cref="IPageService"/>): slug normalization, uniqueness, length limits.
    /// </summary>
    [HttpPost("pages")]
    [RequireApiPermission(PermissionKeys.ContentCreate)]
    public async Task<IActionResult> CreatePage([FromBody] CreatePageRequest request)
    {
        var page = new Page { TenantId = TenantId };
        var error = await _pages.ApplyAsync(page, new PageInput
        {
            Title = request.Title,
            Slug = request.Slug,
            MetaDescription = request.MetaDescription,
        }, TenantId, HttpContext.RequestAborted);

        if (error is not null)
        {
            return BadRequest(new { error });
        }

        _db.Pages.Add(page);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        await _audit.LogAsync(AuditActions.ContentCreated, "Page", page.Id.ToString(), page.Title,
            cancellationToken: HttpContext.RequestAborted);

        return CreatedAtAction(nameof(GetPages), new { id = page.Id }, new { page.Id, page.Slug, page.Title });
    }

    public sealed class CreatePageRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? MetaDescription { get; set; }
    }
}
