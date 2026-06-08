using DotNetForge.Api.Authorization;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Api.Controllers;

/// <summary>GET /api/media - lists media in the token's tenant (media.read).</summary>
[Route("api/media")]
public sealed class MediaApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public MediaApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.MediaRead)]
    public async Task<IActionResult> Get()
    {
        var media = await _db.MediaFiles
            .AsNoTracking()
            .Where(m => m.TenantId == TenantId)
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { m.Id, m.FileName, m.ContentType, m.SizeBytes, m.IsPublic, m.UploadedDate })
            .ToListAsync();

        return Ok(media);
    }
}

/// <summary>GET /api/users - lists users in the token's tenant (users.read).</summary>
[Route("api/users")]
public sealed class UsersApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public UsersApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.UsersRead)]
    public async Task<IActionResult> Get()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                Status = u.Status.ToString(),
                u.LastLoginDate,
            })
            .ToListAsync();

        return Ok(users);
    }
}

/// <summary>GET /api/roles - lists roles in the token's tenant (roles.read).</summary>
[Route("api/roles")]
public sealed class RolesApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public RolesApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.RolesRead)]
    public async Task<IActionResult> Get()
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == TenantId)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsBuiltIn,
                Users = r.UserRoles.Count,
            })
            .ToListAsync();

        return Ok(roles);
    }
}

/// <summary>GET /api/settings - lists global settings (settings.read).</summary>
[Route("api/settings")]
public sealed class SettingsApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public SettingsApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.SettingsRead)]
    public async Task<IActionResult> Get()
    {
        var settings = await _db.Settings
            .AsNoTracking()
            .Where(s => s.TenantId == null || s.TenantId == TenantId)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        return Ok(settings);
    }
}

/// <summary>GET /api/extensions - lists installed extensions (extensions.read).</summary>
[Route("api/extensions")]
public sealed class ExtensionsApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public ExtensionsApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.ExtensionsRead)]
    public async Task<IActionResult> Get()
    {
        var extensions = await _db.InstalledExtensions
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Version,
                Type = x.Type.ToString(),
                Status = x.Status.ToString(),
                x.Author,
                x.InstalledDate,
                x.UpdateAvailable,
            })
            .ToListAsync();

        return Ok(extensions);
    }
}
