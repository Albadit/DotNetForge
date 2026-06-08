using DotNetForge.Api.Authorization;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Api.Controllers;

/// <summary>Headless content endpoints (api_tokens.md). All access is gated by token permissions.</summary>
[Route("api/content")]
public sealed class ContentApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public ContentApiController(DotNetForgeDbContext db)
    {
        _db = db;
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
            .ToListAsync();

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

    /// <summary>POST /api/content/pages - creates a page in the token's tenant.</summary>
    [HttpPost("pages")]
    [RequireApiPermission(PermissionKeys.ContentCreate)]
    public async Task<IActionResult> CreatePage([FromBody] CreatePageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Slug))
        {
            return BadRequest(new { error = "Title and slug are required." });
        }

        var page = new Page
        {
            TenantId = TenantId,
            Title = request.Title.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            MetaDescription = request.MetaDescription,
        };

        _db.Pages.Add(page);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPages), new { id = page.Id }, new { page.Id, page.Slug, page.Title });
    }

    public sealed class CreatePageRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? MetaDescription { get; set; }
    }
}
