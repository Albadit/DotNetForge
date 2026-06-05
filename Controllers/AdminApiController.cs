using System.Security.Claims;
using DotNetForge.Core.Extensions;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Cookie-authenticated JSON API for the admin SPA (distinct from the token-only <c>/api</c> surface).
/// GET endpoints are read-only; write endpoints require the antiforgery token in the
/// <c>X-CSRF-TOKEN</c> header (enforced by <see cref="AutoValidateAntiforgeryTokenAttribute"/>).
/// </summary>
[ApiController]
[Route("admin-api")]
[Authorize(Policy = DependencyRegistration.AdminAreaPolicy)]
[AutoValidateAntiforgeryToken]
[Produces("application/json")]
public sealed class AdminApiController : ControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly AppEnvironment _env;
    private readonly IExtensionLoader _loader;
    private readonly IWebHostEnvironment _hostEnv;
    private readonly IAntiforgery _antiforgery;
    private readonly AuditService _audit;

    public AdminApiController(
        DotNetForgeDbContext db,
        AppEnvironment env,
        IExtensionLoader loader,
        IWebHostEnvironment hostEnv,
        IAntiforgery antiforgery,
        AuditService audit)
    {
        _db = db;
        _env = env;
        _loader = loader;
        _hostEnv = hostEnv;
        _antiforgery = antiforgery;
        _audit = audit;
    }

    private Guid TenantId =>
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var id) ? id : Guid.Empty;

    /// <summary>Issues the antiforgery token (and sets its cookie) for the SPA to use on writes.</summary>
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == TenantId);
        return Ok(new
        {
            name = User.Identity?.Name ?? string.Empty,
            email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            tenant = tenant?.Name ?? "Default",
        });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var state = await _db.SystemState.AsNoTracking().FirstOrDefaultAsync();
        return Ok(new
        {
            appName = _env.AppName,
            users = await _db.Users.CountAsync(u => u.TenantId == TenantId),
            roles = await _db.Roles.CountAsync(r => r.TenantId == TenantId),
            pages = await _db.Pages.CountAsync(p => p.TenantId == TenantId),
            media = await _db.MediaFiles.CountAsync(m => m.TenantId == TenantId),
            apiTokens = await _db.ApiTokens.CountAsync(t => t.TenantId == TenantId && !t.Revoked),
            webhooks = await _db.Webhooks.CountAsync(w => w.TenantId == TenantId),
            extensions = await _db.InstalledExtensions.CountAsync(),
            auditEntries = await _db.AuditLogs.CountAsync(),
            cmsVersion = state?.CmsVersion ?? "1.0.0",
            installedAtUtc = state?.InstalledAtUtc,
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Email,
                name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                status = u.Status.ToString(),
                lastLogin = u.LastLoginDate,
                roles = u.UserRoles.Select(ur => ur.Role!.Name).ToList(),
            })
            .ToListAsync();
        return Ok(users);
    }

    [HttpGet("content/pages")]
    public async Task<IActionResult> Pages()
    {
        var pages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.TenantId == TenantId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToListAsync();
        return Ok(pages.Select(MapPage));
    }

    public sealed record PageInput(
        string Title, string Slug, string? MetaTitle, string? MetaDescription, string? SeoKeywords,
        string? CanonicalUrl, bool Published, bool Disabled, bool DisplayInMenu, Guid? ParentPageId,
        int SortOrder, string PageType, string? TargetUrl, string? FileReference,
        DateTime? ScheduledPublishDate, DateTime? ScheduledUnpublishDate);

    [HttpPost("content/pages")]
    public async Task<IActionResult> CreatePage([FromBody] PageInput input)
    {
        var page = new Page { TenantId = TenantId, CreatedById = CurrentUserId };
        var error = await ApplyPageInputAsync(page, input);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        _db.Pages.Add(page);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentCreated, "Page", page.Id.ToString(), page.Title);
        return Ok(MapPage(page));
    }

    [HttpPut("content/pages/{id:guid}")]
    public async Task<IActionResult> UpdatePage(Guid id, [FromBody] PageInput input)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == TenantId);
        if (page is null)
        {
            return NotFound();
        }

        var error = await ApplyPageInputAsync(page, input);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        page.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentUpdated, "Page", page.Id.ToString(), page.Title);
        return Ok(MapPage(page));
    }

    [HttpDelete("content/pages/{id:guid}")]
    public async Task<IActionResult> DeletePage(Guid id)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == TenantId);
        if (page is null)
        {
            return NotFound();
        }

        // Never orphan children: re-parent them to the deleted page's parent.
        var children = await _db.Pages.Where(p => p.TenantId == TenantId && p.ParentPageId == id).ToListAsync();
        foreach (var child in children)
        {
            child.ParentPageId = page.ParentPageId;
        }

        _db.Pages.Remove(page);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentDeleted, "Page", id.ToString(), page.Title);
        return NoContent();
    }

    /// <summary>Validates the input and applies it to the page; returns an error message, or null on success.</summary>
    private async Task<string?> ApplyPageInputAsync(Page page, PageInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return "Title is required.";
        }

        var slug = Slugify(input.Slug);
        if (slug.Length == 0)
        {
            return "A valid slug is required.";
        }

        if (!Enum.TryParse<PageType>(input.PageType, ignoreCase: true, out var pageType))
        {
            return "Invalid page type.";
        }

        if (input.ParentPageId is Guid parentId)
        {
            var rows = await _db.Pages.AsNoTracking()
                .Where(p => p.TenantId == TenantId)
                .Select(p => new { p.Id, p.ParentPageId })
                .ToListAsync();
            var parentOf = rows.ToDictionary(r => r.Id, r => r.ParentPageId);

            if (!parentOf.ContainsKey(parentId))
            {
                return "Parent page not found in this tenant.";
            }

            // Cycle check: walking the ancestors of the new parent must not reach this page.
            Guid? cursor = parentId;
            while (cursor is Guid c)
            {
                if (c == page.Id)
                {
                    return "That parent would create a cycle in the page tree.";
                }
                cursor = parentOf.TryGetValue(c, out var pp) ? pp : null;
            }
        }

        var slugTaken = await _db.Pages.AnyAsync(p =>
            p.TenantId == TenantId && p.ParentPageId == input.ParentPageId && p.Slug == slug && p.Id != page.Id);
        if (slugTaken)
        {
            return $"A page with the slug '{slug}' already exists under this parent.";
        }

        // A parent can have at most one dynamic "[param]" route segment (otherwise resolution is ambiguous).
        if (slug.StartsWith('[') && slug.EndsWith(']'))
        {
            var siblingSlugs = await _db.Pages.AsNoTracking()
                .Where(p => p.TenantId == TenantId && p.ParentPageId == input.ParentPageId && p.Id != page.Id)
                .Select(p => p.Slug)
                .ToListAsync();
            if (siblingSlugs.Any(s => s.StartsWith('[') && s.EndsWith(']')))
            {
                return "This parent already has a dynamic route ([…]); only one is allowed per parent.";
            }
        }

        if (input.ScheduledPublishDate is DateTime sp && input.ScheduledUnpublishDate is DateTime su && su <= sp)
        {
            return "Scheduled unpublish must be after scheduled publish.";
        }

        if (pageType == PageType.UrlRedirect && string.IsNullOrWhiteSpace(input.TargetUrl))
        {
            return "Target URL is required for a URL redirect page.";
        }

        if (pageType == PageType.File && string.IsNullOrWhiteSpace(input.FileReference))
        {
            return "File reference is required for a File page.";
        }

        page.Title = input.Title.Trim();
        page.Slug = slug;
        page.MetaTitle = Trimmed(input.MetaTitle);
        page.MetaDescription = Trimmed(input.MetaDescription);
        page.SeoKeywords = Trimmed(input.SeoKeywords);
        page.CanonicalUrl = Trimmed(input.CanonicalUrl);
        page.Published = input.Published;
        page.Disabled = input.Disabled;
        page.DisplayInMenu = input.DisplayInMenu;
        page.ParentPageId = input.ParentPageId;
        page.SortOrder = input.SortOrder;
        page.PageType = pageType;
        page.TargetUrl = Trimmed(input.TargetUrl);
        page.FileReference = Trimmed(input.FileReference);
        page.ScheduledPublishDate = input.ScheduledPublishDate;
        page.ScheduledUnpublishDate = input.ScheduledUnpublishDate;
        return null;
    }

    private static object MapPage(Page p) => new
    {
        id = p.Id,
        p.Slug,
        p.Title,
        p.MetaTitle,
        p.MetaDescription,
        p.SeoKeywords,
        p.CanonicalUrl,
        p.Published,
        p.Disabled,
        p.DisplayInMenu,
        parentPageId = p.ParentPageId,
        p.SortOrder,
        type = p.PageType.ToString(),
        p.TargetUrl,
        p.FileReference,
        p.ScheduledPublishDate,
        p.ScheduledUnpublishDate,
        p.CreatedDate,
        p.UpdatedDate,
    };

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // "/" is the reserved slug for the site root (home) page.
        if (input.Trim() == "/")
        {
            return "/";
        }

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (ch is '-' or ' ' or '_')
            {
                sb.Append('-');
            }
            else if (ch is '[' or ']')
            {
                // Brackets mark a dynamic route segment, e.g. "[id]" matches any value.
                sb.Append(ch);
            }
        }

        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }
        return slug.Trim('-');
    }

    [HttpGet("media")]
    public async Task<IActionResult> Media()
    {
        var media = await _db.MediaFiles
            .AsNoTracking()
            .Where(m => m.TenantId == TenantId)
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { id = m.Id, m.FileName, m.ContentType, m.SizeBytes, m.IsPublic, m.UploadedDate })
            .ToListAsync();
        return Ok(media);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _db.Settings
            .AsNoTracking()
            .Where(s => s.TenantId == null || s.TenantId == TenantId)
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();
        return Ok(new { appName = _env.AppName, settings });
    }

    public sealed record SettingInput(string Key, string? Value);

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSetting([FromBody] SettingInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Key))
        {
            return BadRequest(new { error = "Key is required." });
        }

        var key = input.Key.Trim();
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.TenantId == TenantId && s.Key == key);
        if (setting is null)
        {
            setting = new Setting { TenantId = TenantId, Key = key };
            _db.Settings.Add(setting);
        }

        setting.Value = input.Value;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", key, key);
        return Ok(new { setting.Key, setting.Value });
    }

    [HttpGet("extensions")]
    public async Task<IActionResult> Extensions()
    {
        var rows = await _db.InstalledExtensions
            .AsNoTracking()
            .Select(x => new ExtensionDto(
                x.Id, x.Name, x.Version, x.Type.ToString(), x.Status.ToString(), x.Author, true, "installed"))
            .ToListAsync();

        var installedIds = rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extensionsRoot = Path.Combine(_hostEnv.ContentRootPath, "extensions");

        foreach (var discovered in _loader.Discover(extensionsRoot))
        {
            var m = discovered.Manifest;
            var id = m?.Id ?? Path.GetFileName(Path.GetDirectoryName(discovered.Path)) ?? discovered.Path;
            if (m?.Id is not null && installedIds.Contains(m.Id))
            {
                continue;
            }

            rows.Add(new ExtensionDto(
                id, m?.Name ?? "(unknown)", m?.Version ?? "-", m?.Type ?? "-", "Disabled",
                m?.Author ?? "-", discovered.IsValid, "on-disk"));
        }

        return Ok(rows);
    }

    private sealed record ExtensionDto(
        string Id, string Name, string Version, string Type, string Status, string Author, bool ValidManifest, string Source);

    /// <summary>
    /// Returns the valid <c>admin</c>-type extensions discovered under <c>extensions/admin/</c>. The
    /// admin SPA renders one sidebar tab + page per entry, so dropping a manifest there adds a tab with
    /// no React changes (extensions.md).
    /// </summary>
    [HttpGet("admin-extensions")]
    public IActionResult AdminExtensions()
    {
        var extensionsRoot = Path.Combine(_hostEnv.ContentRootPath, "extensions");

        var list = _loader.Discover(extensionsRoot)
            .Where(d => d.IsValid && d.Manifest is not null &&
                        string.Equals(d.Manifest.Type, "admin", StringComparison.OrdinalIgnoreCase))
            .Select(d => new
            {
                id = d.Manifest!.Id,
                name = d.Manifest.Name,
                description = d.Manifest.Description,
                version = d.Manifest.Version,
                author = d.Manifest.Author,
                entryPoint = d.Manifest.EntryPoint,
                routes = d.Manifest.Routes,
                settings = d.Manifest.Settings,
                hasView = System.IO.File.Exists(
                    Path.Combine(Path.GetDirectoryName(d.Path) ?? string.Empty, "Views", "Index.cshtml")),
            })
            .OrderBy(x => x.name)
            .ToList();

        return Ok(list);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.UserLogout);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
