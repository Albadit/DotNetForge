using DotNetForge.Data;
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

    public DashboardController(DotNetForgeDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var state = await _db.SystemState.AsNoTracking().FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        var model = new DashboardViewModel
        {
            Users = await _db.Users.CountAsync(),
            Roles = await _db.Roles.CountAsync(),
            Pages = await _db.Pages.CountAsync(),
            Media = await _db.MediaFiles.CountAsync(),
            ApiTokens = await _db.ApiTokens.CountAsync(t => !t.Revoked &&
                (t.ExpirationDate == null || t.ExpirationDate > now)),
            Webhooks = await _db.Webhooks.CountAsync(),
            Extensions = await _db.InstalledExtensions.CountAsync(x => x.Status == ExtensionStatus.Enabled),
            AuditEntries = await _db.AuditLogs.CountAsync(),
            InstalledAtUtc = state?.InstalledAtUtc,
            CmsVersion = state?.CmsVersion ?? "1.0.0",
            Greeting = await GreetingAsync(),
            CanCreatePages = Can(PermissionAreas.CollectionTypes, PermissionActions.Create),
            CanUpload = Can(PermissionAreas.Media, PermissionActions.Create),
            RecentActivity = await _db.AuditLogs.AsNoTracking()
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
