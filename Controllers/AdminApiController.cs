using System.Security.Claims;
using DotNetForge.Core.Extensions;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
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
            .Select(p => new
            {
                id = p.Id,
                p.Slug,
                p.Title,
                p.Published,
                p.DisplayInMenu,
                type = p.PageType.ToString(),
                p.UpdatedDate,
            })
            .ToListAsync();
        return Ok(pages);
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

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.UserLogout);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}
