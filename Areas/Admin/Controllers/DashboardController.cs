using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>The admin dashboard landing page, reached at <c>/admin</c> (dashboard.md).</summary>
[Route("admin")]
public sealed class DashboardController : AdminControllerBase
{
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

        var model = new DashboardViewModel
        {
            AppName = _env.AppName,
            Users = await _db.Users.CountAsync(u => u.TenantId == TenantId),
            Roles = await _db.Roles.CountAsync(r => r.TenantId == TenantId),
            Pages = await _db.Pages.CountAsync(p => p.TenantId == TenantId),
            Media = await _db.MediaFiles.CountAsync(m => m.TenantId == TenantId),
            ApiTokens = await _db.ApiTokens.CountAsync(t => t.TenantId == TenantId && !t.Revoked),
            Webhooks = await _db.Webhooks.CountAsync(w => w.TenantId == TenantId),
            Extensions = await _db.InstalledExtensions.CountAsync(),
            AuditEntries = await _db.AuditLogs.CountAsync(),
            InstalledAtUtc = state?.InstalledAtUtc,
            CmsVersion = state?.CmsVersion ?? "1.0.0",
        };

        return View(model);
    }
}
