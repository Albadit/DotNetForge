using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>The admin dashboard, reached at <c>/admin</c> (.docs/pages/dashboard.md).</summary>
[Route("admin")]
public sealed class DashboardController : AdminControllerBase
{
    /// <summary>Audit entries shown under "Recent activity".</summary>
    public const int RecentActivityCount = 8;

    private readonly DotNetForgeDbContext _db;
    private readonly AppEnvironment _env;

    public DashboardController(DotNetForgeDbContext db, AppEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var state = await _db.SystemState.AsNoTracking().FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        var model = new DashboardViewModel
        {
            AppName = _env.AppName,
            Users = await _db.Users.CountAsync(u => u.TenantId == TenantId),
            Roles = await _db.Roles.CountAsync(r => r.TenantId == TenantId),
            Pages = await _db.Pages.CountAsync(p => p.TenantId == TenantId),
            Media = await _db.MediaFiles.CountAsync(m => m.TenantId == TenantId),
            ApiTokens = await _db.ApiTokens.CountAsync(t => t.TenantId == TenantId && !t.Revoked &&
                (t.ExpirationDate == null || t.ExpirationDate > now)),
            Webhooks = await _db.Webhooks.CountAsync(w => w.TenantId == TenantId),
            Extensions = await _db.InstalledExtensions.CountAsync(x => x.Status == ExtensionStatus.Enabled),
            AuditEntries = await _db.AuditLogs.CountAsync(a => a.TenantId == TenantId || a.TenantId == null),
            InstalledAtUtc = state?.InstalledAtUtc,
            CmsVersion = state?.CmsVersion ?? "1.0.0",
            Greeting = await GreetingAsync(),
            CanCreatePages = Can(PermissionAreas.CollectionTypes, PermissionActions.Create),
            CanUpload = Can(PermissionAreas.Media, PermissionActions.Create),
            RecentActivity = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.TenantId == TenantId || a.TenantId == null)
                .OrderByDescending(a => a.Id)
                .Take(RecentActivityCount)
                .ToListAsync(),
        };

        return View(model);
    }

    private async Task<string> GreetingAsync()
    {
        var userId = CurrentUserId;
        var firstName = userId is null
            ? null
            : await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FirstName).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(firstName) ? User.Identity?.Name ?? "there" : firstName;
    }
}
