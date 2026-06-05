using DotNetForge.Core.Extensions;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>Roles &amp; Permissions list (user_roles_permissions.md). Restricted to Super Admin / Admin.</summary>
[Route("admin/roles")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class RolesController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public RolesController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == TenantId)
            .OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.Name)
            .Select(r => new RoleListItem(r.Name, r.Description, r.IsBuiltIn, r.UserRoles.Count, r.Permissions.Count))
            .ToListAsync();

        return View(roles);
    }

    public sealed record RoleListItem(string Name, string? Description, bool IsBuiltIn, int Users, int Permissions);
}

/// <summary>Users list (user_roles_permissions.md). Restricted to Super Admin / Admin.</summary>
[Route("admin/users")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class UsersController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public UsersController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .OrderBy(u => u.Email)
            .Select(u => new UserListItem(
                u.Email,
                ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                u.Status.ToString(),
                u.LastLoginDate,
                u.UserRoles.Select(ur => ur.Role!.Name).ToList()))
            .ToListAsync();

        return View(users);
    }

    public sealed record UserListItem(string Email, string Name, string Status, DateTime? LastLogin, List<string> Roles);
}

/// <summary>Audit Logs viewer (audit_logs.md). Read-only; never editable. Restricted to Super Admin / Admin.</summary>
[Route("admin/audit-logs")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class AuditLogsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public AuditLogsController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var entries = await _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync();

        return View(entries);
    }
}

/// <summary>
/// Plugins page (extensions.md). Lists installed extensions and on-disk extensions discovered under
/// the <c>extensions/</c> folder, flagging any with an invalid manifest. Restricted to Super Admin.
/// </summary>
[Route("admin/plugins")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class PluginsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly IExtensionLoader _loader;
    private readonly IWebHostEnvironment _hostEnv;

    public PluginsController(DotNetForgeDbContext db, IExtensionLoader loader, IWebHostEnvironment hostEnv)
    {
        _db = db;
        _loader = loader;
        _hostEnv = hostEnv;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var installed = await _db.InstalledExtensions
            .AsNoTracking()
            .Select(x => new PluginRowViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Version = x.Version,
                Type = x.Type.ToString(),
                Status = x.Status.ToString(),
                Author = x.Author,
                ValidManifest = true,
                Source = "installed",
            })
            .ToListAsync();

        var installedIds = installed.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extensionsRoot = Path.Combine(_hostEnv.ContentRootPath, "extensions");
        foreach (var discovered in _loader.Discover(extensionsRoot))
        {
            var m = discovered.Manifest;
            var id = m?.Id ?? Path.GetFileName(Path.GetDirectoryName(discovered.Path)) ?? discovered.Path;
            if (m?.Id is not null && installedIds.Contains(m.Id))
            {
                continue;
            }

            installed.Add(new PluginRowViewModel
            {
                Id = id,
                Name = m?.Name ?? "(unknown)",
                Version = m?.Version ?? "-",
                Type = m?.Type ?? "-",
                Status = "Disabled",
                Author = m?.Author ?? "-",
                ValidManifest = discovered.IsValid,
                Source = "on-disk",
            });
        }

        return View(installed);
    }
}
