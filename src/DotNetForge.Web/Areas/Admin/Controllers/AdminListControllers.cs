using DotNetForge.Core.Extensions;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>Roles list (admin). Restricted to Super Admin / Admin.</summary>
[Route("admin/roles")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class RolesController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public RolesController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Separate single-table queries, combined in memory: they run on every provider, including MongoDB, which
        // can't count across collections in one query (.docs/database/mongodb.md#query-rules).
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == TenantId)
            .OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description, r.IsBuiltIn })
            .ToListAsync();
        var roleIds = roles.Select(r => r.Id).ToList();
        var users = (await _db.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.RoleId).ToListAsync())
            .CountBy(id => id).ToDictionary();
        var permissions = (await _db.RolePermissions.AsNoTracking().Where(p => roleIds.Contains(p.RoleId)).Select(p => p.RoleId).ToListAsync())
            .CountBy(id => id).ToDictionary();

        return View(roles.Select(r => new RoleListItem(
            r.Name, r.Description, r.IsBuiltIn, users.GetValueOrDefault(r.Id), permissions.GetValueOrDefault(r.Id))).ToList());
    }

    public sealed record RoleListItem(string Name, string? Description, bool IsBuiltIn, int Users, int Permissions);
}

/// <summary>Users list (admin). Restricted to Super Admin / Admin.</summary>
[Route("admin/users")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class UsersController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public UsersController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Single-table queries combined in memory, so the list works on every provider (including MongoDB).
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == TenantId)
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Status, u.LastLoginDate })
            .ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        var links = await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Select(ur => new { ur.UserId, ur.RoleId })
            .ToListAsync();
        var roleNames = await _db.Roles.AsNoTracking()
            .Where(r => r.TenantId == TenantId)
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name);

        return View(users.Select(u => new UserListItem(
            u.Email,
            $"{u.FirstName} {u.LastName}".Trim(),
            u.Status.ToString(),
            u.LastLoginDate,
            links.Where(l => l.UserId == u.Id && roleNames.ContainsKey(l.RoleId)).Select(l => roleNames[l.RoleId]).ToList())).ToList());
    }

    public sealed record UserListItem(string Email, string Name, string Status, DateTime? LastLogin, List<string> Roles);
}

/// <summary>Audit Logs viewer (admin). Restricted to Super Admin / Admin.</summary>
[Route("admin/audit-logs")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class AuditLogsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public AuditLogsController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Entries without a tenant (failed sign-ins: no account was resolved) belong to the sign-in tenant and stay
        // visible; entries of other tenants never are.
        var entries = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == TenantId || a.TenantId == null)
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync();

        return View(entries);
    }
}

/// <summary>Plugins page (admin). Restricted to Super Admin.</summary>
[Route("admin/plugins")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class PluginsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly IExtensionLoader _loader;

    public PluginsController(DotNetForgeDbContext db, IExtensionLoader loader)
    {
        _db = db;
        _loader = loader;
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

        foreach (var discovered in _loader.Discover())
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
