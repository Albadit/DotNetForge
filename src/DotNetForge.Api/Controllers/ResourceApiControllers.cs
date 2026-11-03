using DotNetForge.Api.Authorization;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Api.Controllers;

/// <summary>GET /api/media - lists media (media.read).</summary>
[Route("api/media")]
[Tags("Media")]
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
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { m.Id, m.FileName, m.ContentType, m.SizeBytes, m.IsPublic, m.UploadedDate })
            .ToListAsync();

        return Ok(media);
    }
}

/// <summary>GET /api/users - lists users (users.read).</summary>
[Route("api/users")]
[Tags("Users")]
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

/// <summary>GET /api/roles - lists roles (roles.read).</summary>
[Route("api/roles")]
[Tags("Roles")]
public sealed class RolesApiController : ApiControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public RolesApiController(DotNetForgeDbContext db) => _db = db;

    [HttpGet]
    [RequireApiPermission(PermissionKeys.RolesRead)]
    public async Task<IActionResult> Get()
    {
        // Two single-table queries, combined in memory.
        var roles = await _db.Roles
            .AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.Description, r.IsBuiltIn })
            .ToListAsync();
        var roleIds = roles.Select(r => r.Id).ToList();
        var users = (await _db.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.RoleId).ToListAsync())
            .CountBy(id => id).ToDictionary();

        return Ok(roles.Select(r => new { r.Id, r.Name, r.Description, r.IsBuiltIn, Users = users.GetValueOrDefault(r.Id) }));
    }
}

/// <summary>GET /api/settings - lists global settings (settings.read).</summary>
[Route("api/settings")]
[Tags("Settings")]
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
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        return Ok(settings);
    }
}

/// <summary>GET /api/extensions - lists installed extensions (extensions.read).</summary>
[Route("api/extensions")]
[Tags("Extensions")]
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
